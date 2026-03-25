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

    private Mock<IFxRateProvider> CreateDeadProvider()
    {
        var p = new Mock<IFxRateProvider>();
        p.Setup(x => x.GetSupportedCurrencies()).Returns([]);
        p.Setup(x => x.GetLatestRateAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((decimal?)null);
        p.Setup(x => x.GetHistoricalRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync((decimal?)null);
        p.Setup(x => x.GetRateRangeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync((Dictionary<string, decimal>?)null);
        return p;
    }

    private FxService CreateService(IFxRateProvider? provider = null)
    {
        provider ??= new FrankfurterFxProvider(new HttpClient(), new Mock<ILogger<FrankfurterFxProvider>>().Object);
        return new FxService(provider, _db, _cache, new Mock<ILogger<FxService>>().Object);
    }

    // ──────────────────────────────────────────────
    //  GetSupportedCurrencies
    // ──────────────────────────────────────────────

    [Fact]
    public void GetSupportedCurrencies_DelegatesToProvider()
    {
        var service = CreateService();
        var currencies = service.GetSupportedCurrencies();

        Assert.Equal(30, currencies.Count);
        Assert.Contains(currencies, c => c.Code == "USD");
        Assert.Contains(currencies, c => c.Code == "EUR");
        Assert.Contains(currencies, c => c.Code == "GBP");
    }

    // ──────────────────────────────────────────────
    //  GetLatestRateAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Latest_SameCurrency_ReturnsOne()
    {
        var service = CreateService();
        Assert.Equal(1m, await service.GetLatestRateAsync("USD", "USD"));
    }

    [Fact]
    public async Task Latest_MemoryCacheHit_ReturnsWithoutApiOrDb()
    {
        _cache.Set("fx:latest:USD:EUR", 0.85m, TimeSpan.FromHours(1));
        var provider = CreateDeadProvider();

        var service = CreateService(provider.Object);
        Assert.Equal(0.85m, await service.GetLatestRateAsync("USD", "EUR"));

        provider.Verify(x => x.GetLatestRateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Latest_ApiSucceeds_PersistsToSqliteAndMemory()
    {
        var provider = CreateDeadProvider();
        provider.Setup(x => x.GetLatestRateAsync("USD", "EUR")).ReturnsAsync(0.92m);

        var service = CreateService(provider.Object);
        var rate = await service.GetLatestRateAsync("USD", "EUR");

        Assert.Equal(0.92m, rate);
        // Persisted to SQLite with today's date
        var dbRow = await _db.CachedFxRates.FirstOrDefaultAsync(r =>
            r.BaseCurrency == "USD" && r.QuoteCurrency == "EUR" && r.RateDate == DateTime.UtcNow.Date);
        Assert.NotNull(dbRow);
        Assert.Equal(0.92m, dbRow.Rate);
        // Second call hits memory, not API
        var rate2 = await service.GetLatestRateAsync("USD", "EUR");
        Assert.Equal(0.92m, rate2);
        provider.Verify(x => x.GetLatestRateAsync("USD", "EUR"), Times.Once);
    }

    [Fact]
    public async Task Latest_ApiFails_FallsBackToMostRecentSqlite()
    {
        _db.CachedFxRates.Add(new() { BaseCurrency = "USD", QuoteCurrency = "EUR", RateDate = DateTime.UtcNow.Date.AddDays(-5), Rate = 0.87m });
        _db.CachedFxRates.Add(new() { BaseCurrency = "USD", QuoteCurrency = "EUR", RateDate = DateTime.UtcNow.Date.AddDays(-1), Rate = 0.88m });
        await _db.SaveChangesAsync();

        var service = CreateService(CreateDeadProvider().Object);
        var rate = await service.GetLatestRateAsync("USD", "EUR");

        // Should pick the most recent row, not just any row
        Assert.Equal(0.88m, rate);
    }

    [Fact]
    public async Task Latest_ApiFails_NoSqliteData_ReturnsOne()
    {
        var service = CreateService(CreateDeadProvider().Object);
        Assert.Equal(1m, await service.GetLatestRateAsync("USD", "EUR"));
    }

    [Fact]
    public async Task Latest_CaseInsensitive()
    {
        var provider = CreateDeadProvider();
        provider.Setup(x => x.GetLatestRateAsync("USD", "EUR")).ReturnsAsync(0.92m);

        var service = CreateService(provider.Object);
        Assert.Equal(0.92m, await service.GetLatestRateAsync("usd", "eur"));
    }

    // ──────────────────────────────────────────────
    //  GetHistoricalRateAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Historical_SameCurrency_ReturnsOne()
    {
        var service = CreateService();
        Assert.Equal(1m, await service.GetHistoricalRateAsync("EUR", "EUR", DateTime.Today));
    }

    [Fact]
    public async Task Historical_MemoryCacheHit_ReturnsWithoutApiOrDb()
    {
        _cache.Set("fx:hist:USD:EUR:2024-06-15", 0.923m, TimeSpan.FromHours(24));
        var provider = CreateDeadProvider();

        var service = CreateService(provider.Object);
        Assert.Equal(0.923m, await service.GetHistoricalRateAsync("USD", "EUR", new DateTime(2024, 6, 15)));

        provider.Verify(x => x.GetHistoricalRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Historical_SqliteHit_ReturnsWithoutApi()
    {
        _db.CachedFxRates.Add(new() { BaseCurrency = "USD", QuoteCurrency = "EUR", RateDate = new DateTime(2024, 6, 15), Rate = 0.91m });
        await _db.SaveChangesAsync();

        var provider = CreateDeadProvider();
        var service = CreateService(provider.Object);
        var rate = await service.GetHistoricalRateAsync("USD", "EUR", new DateTime(2024, 6, 15));

        Assert.Equal(0.91m, rate);
        provider.Verify(x => x.GetHistoricalRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Historical_ApiSucceeds_PersistsToSqliteAndMemory()
    {
        var provider = CreateDeadProvider();
        provider.Setup(x => x.GetHistoricalRateAsync("USD", "EUR", new DateTime(2024, 6, 15))).ReturnsAsync(0.91m);

        var service = CreateService(provider.Object);
        var rate = await service.GetHistoricalRateAsync("USD", "EUR", new DateTime(2024, 6, 15));

        Assert.Equal(0.91m, rate);
        // Persisted to SQLite
        var dbRow = await _db.CachedFxRates.FirstOrDefaultAsync(r =>
            r.BaseCurrency == "USD" && r.QuoteCurrency == "EUR" && r.RateDate == new DateTime(2024, 6, 15));
        Assert.NotNull(dbRow);
        Assert.Equal(0.91m, dbRow.Rate);
        // Second call hits memory, not API
        await service.GetHistoricalRateAsync("USD", "EUR", new DateTime(2024, 6, 15));
        provider.Verify(x => x.GetHistoricalRateAsync("USD", "EUR", new DateTime(2024, 6, 15)), Times.Once);
    }

    [Fact]
    public async Task Historical_ApiFails_FallsBackToNearestEarlierDate()
    {
        // Cache a rate 3 days before the requested date
        _db.CachedFxRates.Add(new() { BaseCurrency = "USD", QuoteCurrency = "EUR", RateDate = new DateTime(2024, 6, 12), Rate = 0.905m });
        _db.CachedFxRates.Add(new() { BaseCurrency = "USD", QuoteCurrency = "EUR", RateDate = new DateTime(2024, 6, 10), Rate = 0.900m });
        await _db.SaveChangesAsync();

        var service = CreateService(CreateDeadProvider().Object);
        var rate = await service.GetHistoricalRateAsync("USD", "EUR", new DateTime(2024, 6, 15));

        // Should pick Jun 12 (nearest earlier), not Jun 10
        Assert.Equal(0.905m, rate);
    }

    [Fact]
    public async Task Historical_ApiFails_NoSqliteData_ReturnsOne()
    {
        var service = CreateService(CreateDeadProvider().Object);
        Assert.Equal(1m, await service.GetHistoricalRateAsync("USD", "EUR", new DateTime(2024, 6, 15)));
    }

    // ──────────────────────────────────────────────
    //  GetRateRangeAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Range_SameCurrency_ReturnsEmptyDict()
    {
        var service = CreateService();
        var rates = await service.GetRateRangeAsync("EUR", "EUR", DateTime.Today.AddDays(-30), DateTime.Today);
        Assert.Empty(rates);
    }

    [Fact]
    public async Task Range_MemoryCacheHit_ReturnsWithoutApiOrDb()
    {
        var start = new DateTime(2025, 6, 1);
        var end = new DateTime(2025, 6, 30);
        var cached = new Dictionary<string, decimal> { ["2025-06-02"] = 0.90m };
        _cache.Set($"fx:range:USD:EUR:{start:yyyy-MM-dd}:{end:yyyy-MM-dd}", cached, TimeSpan.FromMinutes(5));

        var provider = CreateDeadProvider();
        var service = CreateService(provider.Object);
        var rates = await service.GetRateRangeAsync("USD", "EUR", start, end);

        Assert.Single(rates);
        Assert.Equal(0.90m, rates["2025-06-02"]);
        provider.Verify(x => x.GetRateRangeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Range_EmptyDb_FetchesFullRange()
    {
        var provider = CreateDeadProvider();
        var start = new DateTime(2025, 6, 1);
        var end = new DateTime(2025, 6, 10);
        provider.Setup(x => x.GetRateRangeAsync("USD", "EUR", start, end))
            .ReturnsAsync(new Dictionary<string, decimal>
            {
                ["2025-06-02"] = 0.90m,
                ["2025-06-03"] = 0.91m,
            });

        var service = CreateService(provider.Object);
        var rates = await service.GetRateRangeAsync("USD", "EUR", start, end);

        Assert.Equal(2, rates.Count);
        // Persisted to SQLite
        Assert.Equal(2, await _db.CachedFxRates.CountAsync(r => r.BaseCurrency == "USD" && r.QuoteCurrency == "EUR"));
    }

    [Fact]
    public async Task Range_HeadGap_FetchesFullRange()
    {
        // Only a date in the middle is cached — large gap from startDate
        _db.CachedFxRates.Add(new() { BaseCurrency = "USD", QuoteCurrency = "EUR", RateDate = new DateTime(2025, 6, 20), Rate = 0.91m });
        await _db.SaveChangesAsync();

        var provider = CreateDeadProvider();
        var start = new DateTime(2025, 6, 1);
        var end = new DateTime(2025, 6, 22);
        provider.Setup(x => x.GetRateRangeAsync("USD", "EUR", start, end))
            .ReturnsAsync(new Dictionary<string, decimal>
            {
                ["2025-06-02"] = 0.895m,
                ["2025-06-03"] = 0.90m,
                ["2025-06-20"] = 0.91m,
            });

        var service = CreateService(provider.Object);
        var rates = await service.GetRateRangeAsync("USD", "EUR", start, end);

        Assert.Equal(3, rates.Count);
        // Full range was requested because of head gap
        provider.Verify(x => x.GetRateRangeAsync("USD", "EUR", start, end), Times.Once);
    }

    [Fact]
    public async Task Range_TailGapOnly_FetchesDelta()
    {
        // Well-populated cache from start, but missing recent days
        var start = new DateTime(2025, 6, 2); // Monday
        for (var d = start; d <= new DateTime(2025, 6, 20); d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            _db.CachedFxRates.Add(new() { BaseCurrency = "USD", QuoteCurrency = "EUR", RateDate = d, Rate = 0.90m + (d.Day * 0.001m) });
        }
        await _db.SaveChangesAsync();

        var provider = CreateDeadProvider();
        var end = new DateTime(2025, 6, 27);
        provider.Setup(x => x.GetRateRangeAsync("USD", "EUR",
                It.Is<DateTime>(d => d == new DateTime(2025, 6, 21)),
                It.Is<DateTime>(d => d == end)))
            .ReturnsAsync(new Dictionary<string, decimal>
            {
                ["2025-06-23"] = 0.925m,
                ["2025-06-24"] = 0.926m,
            });

        var service = CreateService(provider.Object);
        var rates = await service.GetRateRangeAsync("USD", "EUR", start, end);

        // Provider called only for the delta, not the full range
        provider.Verify(x => x.GetRateRangeAsync("USD", "EUR", start, It.IsAny<DateTime>()), Times.Never);
        Assert.Equal(17, rates.Count); // 15 from DB + 2 new
    }

    [Fact]
    public async Task Range_FullyCovered_NoApiCall()
    {
        // Cache covers the entire requested range — no API call needed
        var start = new DateTime(2025, 6, 2); // Monday
        var end = new DateTime(2025, 6, 6);   // Friday
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            _db.CachedFxRates.Add(new() { BaseCurrency = "USD", QuoteCurrency = "EUR", RateDate = d, Rate = 0.90m });
        }
        await _db.SaveChangesAsync();

        var provider = CreateDeadProvider();
        var service = CreateService(provider.Object);
        var rates = await service.GetRateRangeAsync("USD", "EUR", start, end);

        Assert.Equal(5, rates.Count);
        provider.Verify(x => x.GetRateRangeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Range_ApiFails_ReturnsCachedData()
    {
        // Some cached data + API failure → should return what we have, not crash
        _db.CachedFxRates.Add(new() { BaseCurrency = "USD", QuoteCurrency = "EUR", RateDate = new DateTime(2025, 6, 2), Rate = 0.90m });
        _db.CachedFxRates.Add(new() { BaseCurrency = "USD", QuoteCurrency = "EUR", RateDate = new DateTime(2025, 6, 3), Rate = 0.91m });
        await _db.SaveChangesAsync();

        var service = CreateService(CreateDeadProvider().Object);
        var start = new DateTime(2025, 6, 1);
        var end = new DateTime(2025, 6, 30);
        var rates = await service.GetRateRangeAsync("USD", "EUR", start, end);

        // Returns cached data even though range is incomplete
        Assert.Equal(2, rates.Count);
        Assert.Equal(0.90m, rates["2025-06-02"]);
    }

    [Fact]
    public async Task Range_ApiFails_EmptyDb_ReturnsEmptyDict()
    {
        var service = CreateService(CreateDeadProvider().Object);
        var rates = await service.GetRateRangeAsync("USD", "EUR", new DateTime(2025, 6, 1), new DateTime(2025, 6, 30));

        Assert.Empty(rates);
    }

    [Fact]
    public async Task Range_DifferentPairs_DontInterfere()
    {
        _db.CachedFxRates.Add(new() { BaseCurrency = "USD", QuoteCurrency = "EUR", RateDate = new DateTime(2025, 6, 2), Rate = 0.90m });
        _db.CachedFxRates.Add(new() { BaseCurrency = "GBP", QuoteCurrency = "EUR", RateDate = new DateTime(2025, 6, 2), Rate = 1.17m });
        await _db.SaveChangesAsync();

        var start = new DateTime(2025, 6, 1);
        var end = new DateTime(2025, 6, 5);
        var provider = CreateDeadProvider();
        var service = CreateService(provider.Object);

        var usdRates = await service.GetRateRangeAsync("USD", "EUR", start, end);
        var gbpRates = await service.GetRateRangeAsync("GBP", "EUR", start, end);

        Assert.Single(usdRates);
        Assert.Equal(0.90m, usdRates["2025-06-02"]);
        Assert.Single(gbpRates);
        Assert.Equal(1.17m, gbpRates["2025-06-02"]);
    }

    // ──────────────────────────────────────────────
    //  Live API tests (hit real Frankfurter API)
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Live_LatestRate_ReasonableValue()
    {
        var service = CreateService();
        var rate = await service.GetLatestRateAsync("USD", "EUR");
        Assert.True(rate > 0.5m && rate < 1.5m, $"USD→EUR rate {rate} is out of expected range");
    }

    [Fact]
    public async Task Live_HistoricalRate_FetchesAndPersists()
    {
        var service = CreateService();
        var rate = await service.GetHistoricalRateAsync("USD", "EUR", new DateTime(2024, 1, 15));

        Assert.True(rate > 0.5m && rate < 1.5m, $"Historical USD→EUR rate {rate} is out of expected range");

        var cached = await _db.CachedFxRates.FirstOrDefaultAsync(r =>
            r.BaseCurrency == "USD" && r.QuoteCurrency == "EUR" && r.RateDate == new DateTime(2024, 1, 15));
        Assert.NotNull(cached);
        Assert.Equal(rate, cached.Rate);
    }
}
