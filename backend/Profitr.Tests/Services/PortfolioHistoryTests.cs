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

public class PortfolioHistoryTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ProfitrDbContext _db;

    public PortfolioHistoryTests()
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

    #region Helpers

    private PnLService CreateService(
        Dictionary<string, QuoteResult>? quotes = null,
        Dictionary<string, ChartResult>? charts = null)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());

        var yahooLogger = new Mock<ILogger<YahooFinanceService>>();
        var yahooHttp = new HttpClient(new MockHttpHandler());

        quotes ??= new();
        foreach (var (symbol, quote) in quotes)
            cache.Set($"yf:quote:{symbol}", quote, TimeSpan.FromMinutes(5));

        charts ??= new();
        foreach (var (key, chart) in charts)
            cache.Set(key, chart, TimeSpan.FromHours(24));

        var realYahoo = new YahooFinanceService(yahooHttp, cache, yahooLogger.Object);

        var mockProvider = new Mock<IFxRateProvider>();
        mockProvider.Setup(p => p.GetLatestRateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((decimal?)null);
        mockProvider.Setup(p => p.GetHistoricalRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
            .ReturnsAsync((decimal?)null);
        mockProvider.Setup(p => p.GetRateRangeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync((Dictionary<string, decimal>?)null);
        mockProvider.Setup(p => p.GetSupportedCurrencies()).Returns([]);

        var fxLogger = new Mock<ILogger<FxService>>();
        var realFx = new FxService(mockProvider.Object, _db, cache, fxLogger.Object);

        return new PnLService(realYahoo, realFx);
    }

    private static string ChartKey(string symbol, string range, string interval)
        => $"yf:chart:{symbol.ToUpper()}:{range}:{interval}";

    /// <summary>Generate one chart point per weekday at daily granularity.</summary>
    private static List<ChartPoint> DailyPoints(DateTime from, DateTime to, decimal price)
    {
        var pts = new List<ChartPoint>();
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            pts.Add(new ChartPoint(d, price, price + 1, price - 1, price, 1000));
        }
        return pts;
    }

    /// <summary>Generate hourly chart points during simulated market hours (14-21 UTC) per weekday.</summary>
    private static List<ChartPoint> HourlyPoints(DateTime from, DateTime to, decimal price)
    {
        var pts = new List<ChartPoint>();
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            for (var h = 14; h <= 21; h++)
                pts.Add(new ChartPoint(d.AddHours(h).AddMinutes(30), price, price + .5m, price - .5m, price, 500));
        }
        return pts;
    }

    private static Portfolio SingleSymbolPortfolio(
        string symbol = "AAPL", string currency = "USD",
        decimal qty = 10, decimal price = 150m,
        DateTime? txDate = null, decimal? deposit = null)
    {
        var date = txDate ?? DateTime.UtcNow.AddMonths(-6);
        var p = new Portfolio
        {
            Id = Guid.NewGuid(), UserId = "test", Name = "Test", IsDefault = true,
            Transactions =
            [
                new Transaction
                {
                    Symbol = symbol, InstrumentName = $"{symbol} Inc.", AssetType = "EQUITY",
                    Type = TransactionType.Buy, Quantity = qty, PricePerUnit = price,
                    NativeCurrency = currency, TransactionDate = date
                }
            ],
            Dividends = [],
            CashTransactions = []
        };
        if (deposit.HasValue)
            p.CashTransactions.Add(new CashTransaction
            {
                Type = CashTransactionType.Deposit, Amount = deposit.Value,
                Currency = currency, TransactionDate = date.AddDays(-1)
            });
        return p;
    }

    private static Portfolio TwoSymbolPortfolio(
        DateTime? firstTxDate = null, DateTime? secondTxDate = null)
    {
        var first = firstTxDate ?? DateTime.UtcNow.AddMonths(-6);
        var second = secondTxDate ?? first;
        return new Portfolio
        {
            Id = Guid.NewGuid(), UserId = "test", Name = "Multi",
            Transactions =
            [
                new Transaction
                {
                    Symbol = "AAPL", InstrumentName = "Apple", AssetType = "EQUITY",
                    Type = TransactionType.Buy, Quantity = 10, PricePerUnit = 150m,
                    NativeCurrency = "USD", TransactionDate = first
                },
                new Transaction
                {
                    Symbol = "MSFT", InstrumentName = "Microsoft", AssetType = "EQUITY",
                    Type = TransactionType.Buy, Quantity = 5, PricePerUnit = 350m,
                    NativeCurrency = "USD", TransactionDate = second
                }
            ],
            Dividends = [],
            CashTransactions =
            [
                new CashTransaction
                {
                    Type = CashTransactionType.Deposit, Amount = 3250m,
                    Currency = "USD", TransactionDate = first.AddDays(-1)
                }
            ]
        };
    }

    /// <summary>Seed the test DB with a flat FX rate for every weekday in the range.</summary>
    private async Task SeedFxRates(string from, string to, DateTime start, DateTime end, decimal rate)
    {
        for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            _db.CachedFxRates.Add(new CachedFxRate
            {
                BaseCurrency = from, QuoteCurrency = to,
                Rate = rate, RateDate = d
            });
        }
        await _db.SaveChangesAsync();
    }

    private static bool IsWeekday(DateTime d) =>
        d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

    private class MockHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }

    #endregion

    // ======================================================================
    // Empty portfolio — every range returns nothing
    // ======================================================================

    [Theory]
    [InlineData("1w")]
    [InlineData("1m")]
    [InlineData("3m")]
    [InlineData("6m")]
    [InlineData("1y")]
    [InlineData("all")]
    public async Task Empty_AllRanges_ReturnsEmpty(string range)
    {
        var service = CreateService();
        var portfolio = new Portfolio
        {
            Id = Guid.NewGuid(), UserId = "test", Name = "Empty",
            Transactions = [], Dividends = [], CashTransactions = []
        };

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", range);

        Assert.Empty(history);
    }

    // ======================================================================
    // Single-symbol portfolio — all ranges
    // ======================================================================

    [Fact]
    public async Task Single_1w_ReturnsHourlyGranularity()
    {
        var txDate = DateTime.UtcNow.AddDays(-30);
        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", "5d", "1h")] =
                new("AAPL", "USD", HourlyPoints(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow.AddDays(-1), 160m))
        };

        var service = CreateService(charts: charts);
        var portfolio = SingleSymbolPortfolio(txDate: txDate, deposit: 1500m);

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", "1w");

        Assert.NotEmpty(history);
        // Hourly means multiple points on at least one calendar day
        var grouped = history.GroupBy(p => p.Date.Date);
        Assert.Contains(grouped, g => g.Count() > 1);
    }

    [Fact]
    public async Task Single_1w_IncludesTodayWhenMissingFromChart()
    {
        // Yahoo 5d data ends yesterday — the synthetic-point fix should add today
        var txDate = DateTime.UtcNow.AddDays(-30);
        // Build hourly data ending the most recent *past* weekday
        var lastWeekday = DateTime.UtcNow.Date.AddDays(-1);
        while (!IsWeekday(lastWeekday)) lastWeekday = lastWeekday.AddDays(-1);

        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", "5d", "1h")] =
                new("AAPL", "USD", HourlyPoints(DateTime.UtcNow.AddDays(-7), lastWeekday, 160m))
        };

        var service = CreateService(charts: charts);
        var portfolio = SingleSymbolPortfolio(txDate: txDate, deposit: 1500m);

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", "1w");

        if (IsWeekday(DateTime.UtcNow))
        {
            Assert.NotEmpty(history);
            Assert.Equal(DateTime.UtcNow.Date, history[^1].Date.Date);
        }
    }

    [Fact]
    public async Task Single_1w_NoSyntheticPointOnWeekend()
    {
        if (IsWeekday(DateTime.UtcNow))
            return; // test only meaningful on weekends

        var txDate = DateTime.UtcNow.AddDays(-30);
        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", "5d", "1h")] =
                new("AAPL", "USD", HourlyPoints(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow.AddDays(-2), 160m))
        };

        var service = CreateService(charts: charts);
        var portfolio = SingleSymbolPortfolio(txDate: txDate, deposit: 1500m);

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", "1w");

        if (history.Count > 0)
            Assert.NotEqual(DateTime.UtcNow.Date, history[^1].Date.Date);
    }

    [Theory]
    [InlineData("1m", "1mo")]
    [InlineData("3m", "3mo")]
    [InlineData("6m", "6mo")]
    [InlineData("1y", "1y")]
    public async Task Single_DailyRanges_OnlyOnePointPerDay(string range, string yahooRange)
    {
        var txDate = DateTime.UtcNow.AddYears(-2);
        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", yahooRange, "1d")] =
                new("AAPL", "USD", DailyPoints(DateTime.UtcNow.AddYears(-1), DateTime.UtcNow, 160m))
        };

        var service = CreateService(charts: charts);
        var portfolio = SingleSymbolPortfolio(txDate: txDate, deposit: 1500m);

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", range);

        Assert.NotEmpty(history);
        Assert.True(history.GroupBy(p => p.Date.Date).All(g => g.Count() == 1));
    }

    [Theory]
    [InlineData("1m", "1mo", 20, 35)]
    [InlineData("3m", "3mo", 55, 100)]
    [InlineData("6m", "6mo", 120, 190)]
    [InlineData("1y", "1y", 240, 370)]
    public async Task Single_DailyRanges_CorrectSpan(string range, string yahooRange, int minDays, int maxDays)
    {
        var txDate = DateTime.UtcNow.AddYears(-2);
        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", yahooRange, "1d")] =
                new("AAPL", "USD", DailyPoints(DateTime.UtcNow.AddYears(-2), DateTime.UtcNow, 160m))
        };

        var service = CreateService(charts: charts);
        var portfolio = SingleSymbolPortfolio(txDate: txDate, deposit: 1500m);

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", range);

        Assert.NotEmpty(history);
        var span = (history[^1].Date - history[0].Date).TotalDays;
        Assert.True(span >= minDays && span <= maxDays,
            $"Range '{range}' span {span:F0}d not in [{minDays},{maxDays}]");
    }

    [Theory]
    [InlineData("1m", "1mo")]
    [InlineData("3m", "3mo")]
    [InlineData("6m", "6mo")]
    [InlineData("1y", "1y")]
    public async Task Single_DailyRanges_IncludesToday(string range, string yahooRange)
    {
        if (!IsWeekday(DateTime.UtcNow)) return;

        var txDate = DateTime.UtcNow.AddYears(-2);
        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", yahooRange, "1d")] =
                new("AAPL", "USD", DailyPoints(DateTime.UtcNow.AddYears(-2), DateTime.UtcNow, 160m))
        };

        var service = CreateService(charts: charts);
        var portfolio = SingleSymbolPortfolio(txDate: txDate, deposit: 1500m);

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", range);

        Assert.NotEmpty(history);
        Assert.Equal(DateTime.UtcNow.Date, history[^1].Date.Date);
    }

    [Fact]
    public async Task Single_All_StartsAtFirstTransaction()
    {
        // Portfolio 60 days old → "all" chartRange = "3mo"
        var txDate = DateTime.UtcNow.AddDays(-60);
        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", "3mo", "1d")] =
                new("AAPL", "USD", DailyPoints(txDate.AddDays(-5), DateTime.UtcNow, 160m))
        };

        var service = CreateService(charts: charts);
        var portfolio = SingleSymbolPortfolio(txDate: txDate, deposit: 1500m);

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", "all");

        Assert.NotEmpty(history);
        // First point should be on or very near the transaction date
        Assert.True(Math.Abs((history[0].Date.Date - txDate.Date).TotalDays) <= 2,
            $"Expected start near {txDate:d}, got {history[0].Date:d}");
    }

    [Fact]
    public async Task Single_DailyRange_SkipsWeekends()
    {
        var txDate = DateTime.UtcNow.AddYears(-2);
        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", "1mo", "1d")] =
                new("AAPL", "USD", DailyPoints(DateTime.UtcNow.AddMonths(-1), DateTime.UtcNow, 160m))
        };

        var service = CreateService(charts: charts);
        var portfolio = SingleSymbolPortfolio(txDate: txDate, deposit: 1500m);

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", "1m");

        Assert.DoesNotContain(history, p =>
            p.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    // ======================================================================
    // Value correctness — constant price
    // ======================================================================

    [Fact]
    public async Task Single_1m_ValueEqualsHoldingsPlusCash()
    {
        // 10 shares at $150 each, $1500 deposit → cash = 0 after buy
        // Chart price = constant $160 → value = 10*160 + 0 = 1600
        var txDate = DateTime.UtcNow.AddMonths(-6);
        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", "1mo", "1d")] =
                new("AAPL", "USD", DailyPoints(DateTime.UtcNow.AddMonths(-1), DateTime.UtcNow, 160m))
        };

        var service = CreateService(charts: charts);
        var portfolio = SingleSymbolPortfolio(txDate: txDate, deposit: 1500m);

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", "1m");

        Assert.NotEmpty(history);
        Assert.All(history, pt => Assert.Equal(1600m, pt.Value));
    }

    [Fact]
    public async Task Single_1w_ValueCorrectAtHourlyGranularity()
    {
        // Same constant-price check but on the hourly branch
        var txDate = DateTime.UtcNow.AddDays(-30);
        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", "5d", "1h")] =
                new("AAPL", "USD", HourlyPoints(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow.AddDays(-1), 160m))
        };

        var service = CreateService(charts: charts);
        var portfolio = SingleSymbolPortfolio(txDate: txDate, deposit: 1500m);

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", "1w");

        Assert.NotEmpty(history);
        // All points from chart data should be 1600 (10 * 160 + 0 cash)
        // The last point may be the synthetic "today" point which also uses 160 → 1600
        Assert.All(history, pt => Assert.Equal(1600m, pt.Value));
    }

    // ======================================================================
    // Dividends flow into history
    // ======================================================================

    [Fact]
    public async Task Single_1m_DividendIncreasesValue()
    {
        var txDate = DateTime.UtcNow.AddMonths(-6);
        var divPayDate = DateTime.UtcNow.AddDays(-10);
        // Make sure divPayDate is a weekday
        while (!IsWeekday(divPayDate)) divPayDate = divPayDate.AddDays(-1);

        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", "1mo", "1d")] =
                new("AAPL", "USD", DailyPoints(DateTime.UtcNow.AddMonths(-1), DateTime.UtcNow, 160m))
        };

        var service = CreateService(charts: charts);

        var portfolio = new Portfolio
        {
            Id = Guid.NewGuid(), UserId = "test", Name = "Div",
            Transactions =
            [
                new Transaction
                {
                    Symbol = "AAPL", InstrumentName = "Apple", AssetType = "EQUITY",
                    Type = TransactionType.Buy, Quantity = 10, PricePerUnit = 150m,
                    NativeCurrency = "USD", TransactionDate = txDate
                }
            ],
            CashTransactions =
            [
                new CashTransaction
                {
                    Type = CashTransactionType.Deposit, Amount = 1500m,
                    Currency = "USD", TransactionDate = txDate.AddDays(-1)
                }
            ],
            Dividends =
            [
                new Dividend
                {
                    Symbol = "AAPL", AmountPerShare = 1m, NativeCurrency = "USD",
                    ExDate = divPayDate.AddDays(-5), PayDate = divPayDate
                }
            ]
        };

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", "1m");

        var before = history.Where(p => p.Date.Date < divPayDate.Date).ToList();
        var after = history.Where(p => p.Date.Date >= divPayDate.Date).ToList();

        Assert.NotEmpty(before);
        Assert.NotEmpty(after);
        // Before: 10*160 + 0 cash = 1600
        // After:  10*160 + (10*$1 div) = 1610
        Assert.Equal(1600m, before[0].Value);
        Assert.Equal(1610m, after[0].Value);
    }

    // ======================================================================
    // Multiple symbols — same dates
    // ======================================================================

    [Theory]
    [InlineData("1m", "1mo", "1d")]
    [InlineData("3m", "3mo", "1d")]
    [InlineData("6m", "6mo", "1d")]
    [InlineData("1y", "1y",  "1d")]
    public async Task Multi_DailyRanges_CombinesBothPositions(string range, string yahooRange, string interval)
    {
        var txDate = DateTime.UtcNow.AddYears(-2);
        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", yahooRange, interval)] =
                new("AAPL", "USD", DailyPoints(DateTime.UtcNow.AddYears(-1), DateTime.UtcNow, 160m)),
            [ChartKey("MSFT", yahooRange, interval)] =
                new("MSFT", "USD", DailyPoints(DateTime.UtcNow.AddYears(-1), DateTime.UtcNow, 400m))
        };

        var service = CreateService(charts: charts);
        var portfolio = TwoSymbolPortfolio(firstTxDate: txDate);

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", range);

        Assert.NotEmpty(history);
        // AAPL: 10*160=1600, MSFT: 5*400=2000, cash: 3250-1500-1750=0 → 3600
        Assert.All(history, pt => Assert.Equal(3600m, pt.Value));
    }

    [Fact]
    public async Task Multi_1w_CombinesBothPositions()
    {
        var txDate = DateTime.UtcNow.AddMonths(-6);
        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", "5d", "1h")] =
                new("AAPL", "USD", HourlyPoints(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow.AddDays(-1), 160m)),
            [ChartKey("MSFT", "5d", "1h")] =
                new("MSFT", "USD", HourlyPoints(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow.AddDays(-1), 400m))
        };

        var service = CreateService(charts: charts);
        var portfolio = TwoSymbolPortfolio(firstTxDate: txDate);

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", "1w");

        Assert.NotEmpty(history);
        Assert.All(history, pt => Assert.Equal(3600m, pt.Value));
    }

    [Fact]
    public async Task Multi_All_StaggeredBuys_ValueJumpsOnSecondBuy()
    {
        // First buy 60 days ago, second buy 20 days ago → all chartRange="3mo"
        var firstDate = DateTime.UtcNow.AddDays(-60);
        var secondDate = DateTime.UtcNow.AddDays(-20);

        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", "3mo", "1d")] =
                new("AAPL", "USD", DailyPoints(firstDate.AddDays(-5), DateTime.UtcNow, 160m)),
            [ChartKey("MSFT", "3mo", "1d")] =
                new("MSFT", "USD", DailyPoints(firstDate.AddDays(-5), DateTime.UtcNow, 400m))
        };

        var service = CreateService(charts: charts);
        var portfolio = TwoSymbolPortfolio(firstTxDate: firstDate, secondTxDate: secondDate);

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", "all");

        // Pick points after the first buy but before the second buy
        // (before[0] is the deposit date, before any buys happen)
        var betweenBuys = history.Where(p => p.Date.Date > firstDate.Date && p.Date.Date < secondDate.Date).ToList();
        var afterSecondBuy = history.Where(p => p.Date.Date > secondDate.Date).ToList();

        Assert.NotEmpty(betweenBuys);
        Assert.NotEmpty(afterSecondBuy);

        // Between buys: only AAPL held → 10*160=1600 + cash 3250-1500=1750 → 3350
        Assert.Equal(3350m, betweenBuys[0].Value);
        // After second buy: AAPL 1600 + MSFT 5*400=2000 + cash 3250-1500-1750=0 → 3600
        Assert.Equal(3600m, afterSecondBuy[0].Value);
    }

    // ======================================================================
    // Range clamping — portfolio newer than requested range
    // ======================================================================

    [Theory]
    [InlineData("3m", "3mo")]
    [InlineData("6m", "6mo")]
    [InlineData("1y", "1y")]
    public async Task Single_RangeLongerThanPortfolio_ClampsToFirstTx(string range, string yahooRange)
    {
        // Portfolio is only 20 days old; requesting a longer range should clamp start
        var txDate = DateTime.UtcNow.AddDays(-20);
        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", yahooRange, "1d")] =
                new("AAPL", "USD", DailyPoints(DateTime.UtcNow.AddYears(-1), DateTime.UtcNow, 160m))
        };

        var service = CreateService(charts: charts);
        var portfolio = SingleSymbolPortfolio(txDate: txDate, deposit: 1500m);

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", range);

        Assert.NotEmpty(history);
        Assert.True(Math.Abs((history[0].Date.Date - txDate.Date).TotalDays) <= 2,
            $"Start should clamp to tx date ({txDate:d}), got {history[0].Date:d}");
    }

    // ======================================================================
    // Multi-currency portfolio
    // ======================================================================

    [Theory]
    [InlineData("1m", "1mo", "1d")]
    [InlineData("3m", "3mo", "1d")]
    public async Task MultiCurrency_DailyRanges_AppliesFxConversion(string range, string yahooRange, string interval)
    {
        // EUR stock displayed in USD, flat FX rate 1.10
        var txDate = DateTime.UtcNow.AddMonths(-6);
        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("SIE.DE", yahooRange, interval)] =
                new("SIE.DE", "EUR", DailyPoints(DateTime.UtcNow.AddYears(-1), DateTime.UtcNow, 200m))
        };

        // Seed FX rates covering the entire possible query window
        await SeedFxRates("EUR", "USD", txDate.AddDays(-5), DateTime.UtcNow, 1.10m);

        var service = CreateService(charts: charts);
        var portfolio = SingleSymbolPortfolio(
            symbol: "SIE.DE", currency: "EUR",
            qty: 5, price: 200m,
            txDate: txDate, deposit: 1000m);

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", range);

        Assert.NotEmpty(history);
        // Stock: 5 * 200 EUR * 1.10 = 1100 USD
        // Cash:  (1000 deposit - 1000 buy) EUR * 1.10 = 0 USD
        // Total = 1100
        Assert.All(history, pt => Assert.Equal(1100m, pt.Value));
    }

    [Fact]
    public async Task MultiCurrency_1w_AppliesFxConversion()
    {
        var txDate = DateTime.UtcNow.AddMonths(-6);
        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("SIE.DE", "5d", "1h")] =
                new("SIE.DE", "EUR", HourlyPoints(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow.AddDays(-1), 200m))
        };

        await SeedFxRates("EUR", "USD", txDate.AddDays(-5), DateTime.UtcNow, 1.10m);

        var service = CreateService(charts: charts);
        var portfolio = SingleSymbolPortfolio(
            symbol: "SIE.DE", currency: "EUR",
            qty: 5, price: 200m,
            txDate: txDate, deposit: 1000m);

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", "1w");

        Assert.NotEmpty(history);
        Assert.All(history, pt => Assert.Equal(1100m, pt.Value));
    }

    [Fact]
    public async Task MultiCurrency_MultiSymbol_1m_CombinesFxCorrectly()
    {
        // AAPL (USD) + SIE.DE (EUR) displayed in USD
        var txDate = DateTime.UtcNow.AddMonths(-6);
        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", "1mo", "1d")] =
                new("AAPL", "USD", DailyPoints(DateTime.UtcNow.AddMonths(-1), DateTime.UtcNow, 160m)),
            [ChartKey("SIE.DE", "1mo", "1d")] =
                new("SIE.DE", "EUR", DailyPoints(DateTime.UtcNow.AddMonths(-1), DateTime.UtcNow, 200m))
        };

        await SeedFxRates("EUR", "USD", txDate.AddDays(-5), DateTime.UtcNow, 1.10m);

        var service = CreateService(charts: charts);

        var portfolio = new Portfolio
        {
            Id = Guid.NewGuid(), UserId = "test", Name = "Mixed",
            Transactions =
            [
                new Transaction
                {
                    Symbol = "AAPL", InstrumentName = "Apple", AssetType = "EQUITY",
                    Type = TransactionType.Buy, Quantity = 10, PricePerUnit = 150m,
                    NativeCurrency = "USD", TransactionDate = txDate
                },
                new Transaction
                {
                    Symbol = "SIE.DE", InstrumentName = "Siemens", AssetType = "EQUITY",
                    Type = TransactionType.Buy, Quantity = 5, PricePerUnit = 200m,
                    NativeCurrency = "EUR", TransactionDate = txDate
                }
            ],
            Dividends = [],
            CashTransactions =
            [
                new CashTransaction
                {
                    Type = CashTransactionType.Deposit, Amount = 1500m,
                    Currency = "USD", TransactionDate = txDate.AddDays(-1)
                },
                new CashTransaction
                {
                    Type = CashTransactionType.Deposit, Amount = 1000m,
                    Currency = "EUR", TransactionDate = txDate.AddDays(-1)
                }
            ]
        };

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", "1m");

        Assert.NotEmpty(history);
        // AAPL: 10*160 = 1600 USD
        // SIE: 5*200*1.10 = 1100 USD
        // Cash USD: 1500 - 1500(AAPL buy) = 0 USD
        // Cash EUR: (1000 - 1000(SIE buy))*1.10 = 0 USD
        // Total = 2700
        Assert.All(history, pt => Assert.Equal(2700m, pt.Value));
    }

    // ======================================================================
    // "all" range — Yahoo range selection
    // ======================================================================

    [Theory]
    [InlineData(5,   "5d")]     // ≤7 days
    [InlineData(20,  "1mo")]    // ≤30 days
    [InlineData(60,  "3mo")]    // ≤90 days
    [InlineData(120, "6mo")]    // ≤180 days
    [InlineData(300, "1y")]     // ≤365 days
    [InlineData(500, "2y")]     // ≤730 days
    public async Task All_SelectsCorrectYahooRange(int daysAgo, string expectedYahooRange)
    {
        var txDate = DateTime.UtcNow.AddDays(-daysAgo);
        // For very short ranges this triggers the hourly (5d) or daily path
        var isHourly = expectedYahooRange == "5d";
        var interval = isHourly ? "1h" : "1d";
        var points = isHourly
            ? HourlyPoints(txDate.AddDays(-2), DateTime.UtcNow, 160m)
            : DailyPoints(txDate.AddDays(-2), DateTime.UtcNow, 160m);

        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", expectedYahooRange, interval)] =
                new("AAPL", "USD", points)
        };

        var service = CreateService(charts: charts);
        var portfolio = SingleSymbolPortfolio(txDate: txDate, deposit: 1500m);

        // "all" should pick the expected Yahoo range and produce data
        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", "all");

        Assert.NotEmpty(history);
    }

    // ======================================================================
    // Sell reduces position in history
    // ======================================================================

    [Fact]
    public async Task Single_1m_SellReducesValue()
    {
        var buyDate = DateTime.UtcNow.AddMonths(-6);
        var sellDate = DateTime.UtcNow.AddDays(-10);
        while (!IsWeekday(sellDate)) sellDate = sellDate.AddDays(-1);

        var charts = new Dictionary<string, ChartResult>
        {
            [ChartKey("AAPL", "1mo", "1d")] =
                new("AAPL", "USD", DailyPoints(DateTime.UtcNow.AddMonths(-1), DateTime.UtcNow, 160m))
        };

        var service = CreateService(charts: charts);

        var portfolio = new Portfolio
        {
            Id = Guid.NewGuid(), UserId = "test", Name = "Sell",
            Transactions =
            [
                new Transaction
                {
                    Symbol = "AAPL", InstrumentName = "Apple", AssetType = "EQUITY",
                    Type = TransactionType.Buy, Quantity = 10, PricePerUnit = 150m,
                    NativeCurrency = "USD", TransactionDate = buyDate
                },
                new Transaction
                {
                    Symbol = "AAPL", InstrumentName = "Apple", AssetType = "EQUITY",
                    Type = TransactionType.Sell, Quantity = 4, PricePerUnit = 160m,
                    NativeCurrency = "USD", TransactionDate = sellDate
                }
            ],
            Dividends = [],
            CashTransactions =
            [
                new CashTransaction
                {
                    Type = CashTransactionType.Deposit, Amount = 1500m,
                    Currency = "USD", TransactionDate = buyDate.AddDays(-1)
                }
            ]
        };

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", "1m");

        var before = history.Where(p => p.Date.Date < sellDate.Date).ToList();
        var after = history.Where(p => p.Date.Date >= sellDate.Date).ToList();

        Assert.NotEmpty(before);
        Assert.NotEmpty(after);
        // Before sell: 10*160 + (1500-1500)=0 cash = 1600
        Assert.Equal(1600m, before[0].Value);
        // After sell: 6*160 + (1500 - 1500 buy + 640 sell proceeds) = 960 + 640 = 1600
        // Wait — cash changes: deposit 1500, buy -1500, sell +4*160=640 → cash=640
        // Holdings: 6*160=960, total=960+640=1600
        Assert.Equal(1600m, after[0].Value);
    }

    // ======================================================================
    // Cash-only portfolio (no holdings)
    // ======================================================================

    [Theory]
    [InlineData("1m")]
    [InlineData("1y")]
    public async Task CashOnly_DailyRanges_ShowsCashBalance(string range)
    {
        var depositDate = DateTime.UtcNow.AddDays(-60);
        var service = CreateService();

        var portfolio = new Portfolio
        {
            Id = Guid.NewGuid(), UserId = "test", Name = "Cash",
            Transactions = [],
            Dividends = [],
            CashTransactions =
            [
                new CashTransaction
                {
                    Type = CashTransactionType.Deposit, Amount = 5000m,
                    Currency = "USD", TransactionDate = depositDate
                }
            ]
        };

        var history = await service.ComputePortfolioHistoryAsync(portfolio, "USD", range);

        Assert.NotEmpty(history);
        Assert.All(history, pt => Assert.Equal(5000m, pt.Value));
    }
}
