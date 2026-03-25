using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Profitr.Api.Data;
using Profitr.Api.Services;

namespace Profitr.Tests.Services;

public class FxServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ProfitrDbContext _db;
    private readonly MemoryCache _cache;

    public FxServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<ProfitrDbContext>()
            .UseSqlite(_conn).Options;
        _db = new ProfitrDbContext(opts);
        _db.Database.EnsureCreated();
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
        _cache.Dispose();
    }

    private FxService CreateService(IFxRateProvider? provider = null)
    {
        provider ??= new FrankfurterFxProvider(new HttpClient(), new Mock<ILogger<FrankfurterFxProvider>>().Object);
        return new FxService(provider, _db, _cache, new Mock<ILogger<FxService>>().Object);
    }

    [Fact]
    public void GetSupportedCurrencies_Returns30Currencies()
    {
        var service = CreateService();
        var currencies = service.GetSupportedCurrencies();

        Assert.Equal(30, currencies.Count);
        Assert.Contains(currencies, c => c.Code == "USD");
        Assert.Contains(currencies, c => c.Code == "EUR");
        Assert.Contains(currencies, c => c.Code == "GBP");
    }

    [Fact]
    public async Task SameCurrency_ReturnsOne()
    {
        var service = CreateService();

        Assert.Equal(1m, await service.GetLatestRateAsync("USD", "USD"));
        Assert.Equal(1m, await service.GetHistoricalRateAsync("EUR", "EUR", DateTime.Today));
    }

    [Fact]
    public async Task CachedRate_ReturnsFromMemoryCache()
    {
        _cache.Set("fx:latest:USD:EUR", 0.85m, TimeSpan.FromHours(1));
        var service = CreateService();

        Assert.Equal(0.85m, await service.GetLatestRateAsync("USD", "EUR"));
    }

    [Fact]
    public async Task CachedHistoricalRate_ReturnsFromMemoryCache()
    {
        _cache.Set("fx:hist:USD:EUR:2024-06-15", 0.923m, TimeSpan.FromHours(24));
        var service = CreateService();

        Assert.Equal(0.923m, await service.GetHistoricalRateAsync("USD", "EUR", new DateTime(2024, 6, 15)));
    }

    [Fact]
    public async Task HistoricalRate_CachedInSqlite_ReturnsWithoutApi()
    {
        // Pre-populate SQLite
        _db.CachedFxRates.Add(new() { BaseCurrency = "USD", QuoteCurrency = "EUR", RateDate = new DateTime(2024, 6, 15), Rate = 0.91m });
        await _db.SaveChangesAsync();

        // Provider that always returns null (simulates API down)
        var deadProvider = new Mock<IFxRateProvider>();
        deadProvider.Setup(p => p.GetSupportedCurrencies()).Returns([]);
        deadProvider.Setup(p => p.GetHistoricalRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
            .ReturnsAsync((decimal?)null);

        var service = CreateService(deadProvider.Object);
        var rate = await service.GetHistoricalRateAsync("USD", "EUR", new DateTime(2024, 6, 15));

        Assert.Equal(0.91m, rate);
        // Provider should never have been called because SQLite had the rate
        deadProvider.Verify(p => p.GetHistoricalRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task LatestRate_FallsBackToSqlite_WhenApiFails()
    {
        // Pre-populate SQLite with a recent rate
        _db.CachedFxRates.Add(new() { BaseCurrency = "USD", QuoteCurrency = "EUR", RateDate = DateTime.UtcNow.Date.AddDays(-1), Rate = 0.88m });
        await _db.SaveChangesAsync();

        var deadProvider = new Mock<IFxRateProvider>();
        deadProvider.Setup(p => p.GetSupportedCurrencies()).Returns([]);
        deadProvider.Setup(p => p.GetLatestRateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((decimal?)null);

        var service = CreateService(deadProvider.Object);
        var rate = await service.GetLatestRateAsync("USD", "EUR");

        Assert.Equal(0.88m, rate);
    }

    [Fact]
    public async Task RangeRate_UsesCache_ThenFetchesDelta()
    {
        // Pre-populate SQLite with some historical rates
        _db.CachedFxRates.Add(new() { BaseCurrency = "USD", QuoteCurrency = "EUR", RateDate = new DateTime(2024, 1, 2), Rate = 0.90m });
        _db.CachedFxRates.Add(new() { BaseCurrency = "USD", QuoteCurrency = "EUR", RateDate = new DateTime(2024, 1, 3), Rate = 0.91m });
        await _db.SaveChangesAsync();

        // Provider returns rates for the missing range
        var provider = new Mock<IFxRateProvider>();
        provider.Setup(p => p.GetSupportedCurrencies()).Returns([]);
        provider.Setup(p => p.GetRateRangeAsync("USD", "EUR", It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new Dictionary<string, decimal>
            {
                ["2024-01-04"] = 0.92m,
                ["2024-01-05"] = 0.93m,
            });

        var service = CreateService(provider.Object);
        var rates = await service.GetRateRangeAsync("USD", "EUR", new DateTime(2024, 1, 2), new DateTime(2024, 1, 10));

        // Should have original 2 + fetched 2 = 4 rates
        Assert.Equal(4, rates.Count);
        Assert.Equal(0.90m, rates["2024-01-02"]);
        Assert.Equal(0.93m, rates["2024-01-05"]);

        // New rates should be persisted in SQLite
        Assert.Equal(4, await _db.CachedFxRates.CountAsync(r => r.BaseCurrency == "USD" && r.QuoteCurrency == "EUR"));
    }

    [Fact]
    public async Task LiveLatestRate_FetchesFromApi()
    {
        var service = CreateService();
        var rate = await service.GetLatestRateAsync("USD", "EUR");

        Assert.True(rate > 0.5m && rate < 1.5m, $"USD→EUR rate {rate} is out of expected range");
    }

    [Fact]
    public async Task LiveHistoricalRate_FetchesAndCaches()
    {
        var service = CreateService();
        var rate = await service.GetHistoricalRateAsync("USD", "EUR", new DateTime(2024, 1, 15));

        Assert.True(rate > 0.5m && rate < 1.5m, $"Historical USD→EUR rate {rate} is out of expected range");

        // Should be persisted in SQLite
        var cached = await _db.CachedFxRates.FirstOrDefaultAsync(r =>
            r.BaseCurrency == "USD" && r.QuoteCurrency == "EUR" && r.RateDate == new DateTime(2024, 1, 15));
        Assert.NotNull(cached);
        Assert.Equal(rate, cached.Rate);
    }
}
