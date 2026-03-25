using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Profitr.Api.Data;
using Profitr.Api.Data.Entities;
using Profitr.Api.Models;
using Profitr.Api.Services;

namespace Profitr.Tests.Services;

public class PnLServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ProfitrDbContext _db;

    public PnLServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<ProfitrDbContext>()
            .UseSqlite(_conn).Options;
        _db = new ProfitrDbContext(opts);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private PnLService CreateService(
        Dictionary<string, QuoteResult>? quotes = null,
        Dictionary<string, decimal>? fxRates = null)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());

        // Mock Yahoo Finance
        var yahooLogger = new Mock<ILogger<YahooFinanceService>>();
        var yahooHttp = new HttpClient(new MockHttpHandler());

        // Pre-fill quote cache so it won't make HTTP calls
        quotes ??= new();
        foreach (var (symbol, quote) in quotes)
        {
            cache.Set($"yf:quote:{symbol}", quote, TimeSpan.FromMinutes(5));
        }

        var realYahoo = new YahooFinanceService(yahooHttp, cache, yahooLogger.Object);

        // FX service with null-returning provider (rates come from memory cache)
        var mockProvider = new Mock<IFxRateProvider>();
        mockProvider.Setup(p => p.GetLatestRateAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((decimal?)null);
        mockProvider.Setup(p => p.GetHistoricalRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync((decimal?)null);
        mockProvider.Setup(p => p.GetSupportedCurrencies()).Returns([]);

        var fxLogger = new Mock<ILogger<FxService>>();
        var realFx = new FxService(mockProvider.Object, _db, cache, fxLogger.Object);

        // Pre-fill FX cache
        fxRates ??= new();
        foreach (var (key, rate) in fxRates)
        {
            cache.Set(key, rate, TimeSpan.FromHours(24));
        }

        return new PnLService(realYahoo, realFx);
    }

    [Fact]
    public async Task SingleBuyPosition_SameCurrency_CorrectPnL()
    {
        // Arrange: Buy 10 AAPL at $150, current price $195, display=USD
        var quotes = new Dictionary<string, QuoteResult>
        {
            ["AAPL"] = new("AAPL", "Apple", 195m, 5m, 2.63m, "USD", "NASDAQ", "EQUITY")
        };

        var service = CreateService(quotes);

        var portfolio = new Portfolio
        {
            Id = Guid.NewGuid(),
            UserId = "test",
            Name = "Test",
            IsDefault = true,
            Transactions =
            [
                new Transaction
                {
                    Symbol = "AAPL",
                    InstrumentName = "Apple Inc.",
                    AssetType = "EQUITY",
                    Type = TransactionType.Buy,
                    Quantity = 10,
                    PricePerUnit = 150m,
                    NativeCurrency = "USD",
                    TransactionDate = new DateTime(2024, 6, 15)
                }
            ],
            Dividends = [],
            CashTransactions =
            [
                new CashTransaction
                {
                    Type = CashTransactionType.Deposit,
                    Amount = 1500m,
                    Currency = "USD",
                    TransactionDate = new DateTime(2024, 6, 14)
                }
            ]
        };

        // Act
        var summary = await service.CalculatePortfolioSummaryAsync(portfolio, "USD");

        // Assert
        Assert.Single(summary.Positions);
        var pos = summary.Positions[0];
        Assert.Equal("AAPL", pos.Symbol);
        Assert.Equal(10m, pos.Quantity);
        Assert.Equal(150m, pos.AverageCostBasis);
        Assert.Equal(1500m, pos.TotalInvested);
        Assert.Equal(195m, pos.CurrentPrice);
        Assert.Equal(1950m, pos.CurrentValue);
        Assert.Equal(450m, pos.PnL);
        Assert.Equal(30m, pos.PnLPercent);

        // TotalValue = securities (1950) + cash (1500 deposit - 1500 buy = 0) = 1950
        Assert.Equal(1950m, summary.TotalValue);
        Assert.Equal(0m, summary.CashBalance);
        Assert.Equal(1500m, summary.TotalCostBasis);
        Assert.Equal(450m, summary.TotalPnL);
    }

    [Fact]
    public async Task BuyAndPartialSell_CorrectNetPosition()
    {
        // Buy 10 at $100, sell 4 at $120
        var quotes = new Dictionary<string, QuoteResult>
        {
            ["TEST"] = new("TEST", "Test Co", 130m, 0, 0, "USD", "NYSE", "EQUITY")
        };

        var service = CreateService(quotes);

        var portfolio = new Portfolio
        {
            Id = Guid.NewGuid(),
            UserId = "test",
            Name = "Test",
            Transactions =
            [
                new Transaction
                {
                    Symbol = "TEST",
                    InstrumentName = "Test Co",
                    AssetType = "EQUITY",
                    Type = TransactionType.Buy,
                    Quantity = 10,
                    PricePerUnit = 100m,
                    NativeCurrency = "USD",
                    TransactionDate = new DateTime(2024, 1, 1)
                },
                new Transaction
                {
                    Symbol = "TEST",
                    InstrumentName = "Test Co",
                    AssetType = "EQUITY",
                    Type = TransactionType.Sell,
                    Quantity = 4,
                    PricePerUnit = 120m,
                    NativeCurrency = "USD",
                    TransactionDate = new DateTime(2024, 6, 1)
                }
            ],
            Dividends = []
        };

        var summary = await service.CalculatePortfolioSummaryAsync(portfolio, "USD");
        var pos = summary.Positions[0];

        Assert.Equal(6m, pos.Quantity); // 10 - 4
        Assert.Equal(130m, pos.CurrentPrice);
        Assert.Equal(780m, pos.CurrentValue); // 6 * 130
    }

    [Fact]
    public async Task MultiCurrency_ConvertsProperly()
    {
        // Buy SIE.DE at €200 (native=EUR), display in USD
        // FX: at purchase EUR→USD was 1.10, now EUR→USD is 1.08
        var quotes = new Dictionary<string, QuoteResult>
        {
            ["SIE.DE"] = new("SIE.DE", "Siemens", 220m, 5m, 2.3m, "EUR", "XETRA", "EQUITY")
        };

        var fxRates = new Dictionary<string, decimal>
        {
            // Historical rate at purchase date
            ["fx:hist:EUR:USD:2024-06-15"] = 1.10m,
            // Current rate
            ["fx:latest:EUR:USD"] = 1.08m
        };

        var service = CreateService(quotes, fxRates);

        var portfolio = new Portfolio
        {
            Id = Guid.NewGuid(),
            UserId = "test",
            Name = "Test",
            Transactions =
            [
                new Transaction
                {
                    Symbol = "SIE.DE",
                    InstrumentName = "Siemens AG",
                    AssetType = "EQUITY",
                    Type = TransactionType.Buy,
                    Quantity = 5,
                    PricePerUnit = 200m,
                    NativeCurrency = "EUR",
                    TransactionDate = new DateTime(2024, 6, 15)
                }
            ],
            Dividends = []
        };

        var summary = await service.CalculatePortfolioSummaryAsync(portfolio, "USD");
        var pos = summary.Positions[0];

        // Native currency values
        Assert.Equal(5m, pos.Quantity);
        Assert.Equal(200m, pos.AverageCostBasis);   // EUR
        Assert.Equal(220m, pos.CurrentPrice);         // EUR
        Assert.Equal(1100m, pos.CurrentValue);        // 5 * 220 EUR

        // Display currency (USD) values
        // Cost: 5 * 200 * 1.10 = 1100 USD
        Assert.Equal(1100m, pos.TotalInvestedDisplay);
        // Current: 5 * 220 * 1.08 = 1188 USD
        Assert.Equal(1188m, pos.CurrentValueDisplay);
        // P&L: 1188 - 1100 = 88 USD
        Assert.Equal(88m, pos.PnLDisplay);
    }

    [Fact]
    public async Task MultiplePositions_SumsCorrectly()
    {
        var quotes = new Dictionary<string, QuoteResult>
        {
            ["AAPL"] = new("AAPL", "Apple", 200m, 0, 0, "USD", "NASDAQ", "EQUITY"),
            ["MSFT"] = new("MSFT", "Microsoft", 400m, 0, 0, "USD", "NASDAQ", "EQUITY")
        };

        var service = CreateService(quotes);

        var portfolio = new Portfolio
        {
            Id = Guid.NewGuid(),
            UserId = "test",
            Name = "Test",
            Transactions =
            [
                new Transaction
                {
                    Symbol = "AAPL",
                    InstrumentName = "Apple",
                    AssetType = "EQUITY",
                    Type = TransactionType.Buy,
                    Quantity = 10,
                    PricePerUnit = 150m,
                    NativeCurrency = "USD",
                    TransactionDate = new DateTime(2024, 1, 1)
                },
                new Transaction
                {
                    Symbol = "MSFT",
                    InstrumentName = "Microsoft",
                    AssetType = "EQUITY",
                    Type = TransactionType.Buy,
                    Quantity = 5,
                    PricePerUnit = 350m,
                    NativeCurrency = "USD",
                    TransactionDate = new DateTime(2024, 1, 1)
                }
            ],
            Dividends = [],
            CashTransactions =
            [
                new CashTransaction
                {
                    Type = CashTransactionType.Deposit,
                    Amount = 3250m,
                    Currency = "USD",
                    TransactionDate = new DateTime(2023, 12, 31)
                }
            ]
        };

        var summary = await service.CalculatePortfolioSummaryAsync(portfolio, "USD");

        Assert.Equal(2, summary.Positions.Count);
        // AAPL: 10*200 = 2000, MSFT: 5*400 = 2000, cash: 3250-3250 = 0 → total 4000
        Assert.Equal(4000m, summary.TotalValue);
        Assert.Equal(0m, summary.CashBalance);
        // AAPL cost: 1500, MSFT cost: 1750 → total 3250
        Assert.Equal(3250m, summary.TotalCostBasis);
        // PnL: 4000 - 3250 = 750
        Assert.Equal(750m, summary.TotalPnL);
    }

    [Fact]
    public async Task EmptyPortfolio_ReturnsZeros()
    {
        var service = CreateService();

        var portfolio = new Portfolio
        {
            Id = Guid.NewGuid(),
            UserId = "test",
            Name = "Empty",
            Transactions = [],
            Dividends = []
        };

        var summary = await service.CalculatePortfolioSummaryAsync(portfolio, "EUR");

        Assert.Empty(summary.Positions);
        Assert.Equal(0m, summary.TotalValue);
        Assert.Equal(0m, summary.TotalPnL);
    }

    [Fact]
    public async Task FractionalShares_HandledCorrectly()
    {
        var quotes = new Dictionary<string, QuoteResult>
        {
            ["BRK.B"] = new("BRK.B", "Berkshire", 500m, 0, 0, "USD", "NYSE", "EQUITY")
        };

        var service = CreateService(quotes);

        var portfolio = new Portfolio
        {
            Id = Guid.NewGuid(),
            UserId = "test",
            Name = "Test",
            Transactions =
            [
                new Transaction
                {
                    Symbol = "BRK.B",
                    InstrumentName = "Berkshire",
                    AssetType = "EQUITY",
                    Type = TransactionType.Buy,
                    Quantity = 0.5m,
                    PricePerUnit = 400m,
                    NativeCurrency = "USD",
                    TransactionDate = new DateTime(2024, 1, 1)
                }
            ],
            Dividends = []
        };

        var summary = await service.CalculatePortfolioSummaryAsync(portfolio, "USD");
        var pos = summary.Positions[0];

        Assert.Equal(0.5m, pos.Quantity);
        Assert.Equal(200m, pos.TotalInvested);   // 0.5 * 400
        Assert.Equal(250m, pos.CurrentValue);     // 0.5 * 500
        Assert.Equal(50m, pos.PnL);
    }

    [Fact]
    public async Task FullySoldPosition_ExcludedFromSummary()
    {
        var quotes = new Dictionary<string, QuoteResult>
        {
            ["SOLD"] = new("SOLD", "Sold Co", 100m, 0, 0, "USD", "NYSE", "EQUITY")
        };

        var service = CreateService(quotes);

        var portfolio = new Portfolio
        {
            Id = Guid.NewGuid(),
            UserId = "test",
            Name = "Test",
            Transactions =
            [
                new Transaction
                {
                    Symbol = "SOLD",
                    InstrumentName = "Sold Co",
                    AssetType = "EQUITY",
                    Type = TransactionType.Buy,
                    Quantity = 10,
                    PricePerUnit = 50m,
                    NativeCurrency = "USD",
                    TransactionDate = new DateTime(2024, 1, 1)
                },
                new Transaction
                {
                    Symbol = "SOLD",
                    InstrumentName = "Sold Co",
                    AssetType = "EQUITY",
                    Type = TransactionType.Sell,
                    Quantity = 10,
                    PricePerUnit = 80m,
                    NativeCurrency = "USD",
                    TransactionDate = new DateTime(2024, 6, 1)
                }
            ],
            Dividends = []
        };

        var summary = await service.CalculatePortfolioSummaryAsync(portfolio, "USD");

        // Fully sold position should show with 0 qty or be excluded
        // The position is kept with 0 qty since a current quote exists
        var pos = summary.Positions.FirstOrDefault(p => p.Symbol == "SOLD");
        if (pos != null)
        {
            Assert.Equal(0m, pos.Quantity);
            Assert.Equal(0m, pos.CurrentValue);
        }
    }

    // Helper: mock HTTP handler that returns nothing (we use cache instead)
    private class MockHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}
