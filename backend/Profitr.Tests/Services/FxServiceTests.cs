using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Profitr.Api.Services;

namespace Profitr.Tests.Services;

public class FxServiceTests
{
    [Fact]
    public void GetSupportedCurrencies_Returns30Currencies()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new Mock<ILogger<FxService>>();
        var service = new FxService(new HttpClient(), cache, logger.Object);

        var currencies = service.GetSupportedCurrencies();

        Assert.Equal(30, currencies.Count);
        Assert.Contains(currencies, c => c.Code == "USD");
        Assert.Contains(currencies, c => c.Code == "EUR");
        Assert.Contains(currencies, c => c.Code == "GBP");
    }

    [Fact]
    public async Task SameCurrency_ReturnsOne()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new Mock<ILogger<FxService>>();
        var service = new FxService(new HttpClient(), cache, logger.Object);

        var rate = await service.GetLatestRateAsync("USD", "USD");
        Assert.Equal(1m, rate);

        rate = await service.GetHistoricalRateAsync("EUR", "EUR", DateTime.Today);
        Assert.Equal(1m, rate);
    }

    [Fact]
    public async Task CachedRate_ReturnsFromCache()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("fx:latest:USD:EUR", 0.85m, TimeSpan.FromHours(1));

        var logger = new Mock<ILogger<FxService>>();
        var service = new FxService(new HttpClient(), cache, logger.Object);

        var rate = await service.GetLatestRateAsync("USD", "EUR");
        Assert.Equal(0.85m, rate);
    }

    [Fact]
    public async Task CachedHistoricalRate_ReturnsFromCache()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("fx:hist:USD:EUR:2024-06-15", 0.923m, TimeSpan.FromHours(24));

        var logger = new Mock<ILogger<FxService>>();
        var service = new FxService(new HttpClient(), cache, logger.Object);

        var rate = await service.GetHistoricalRateAsync("USD", "EUR", new DateTime(2024, 6, 15));
        Assert.Equal(0.923m, rate);
    }

    [Fact]
    public async Task LiveLatestRate_FetchesFromApi()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new Mock<ILogger<FxService>>();
        var service = new FxService(new HttpClient(), cache, logger.Object);

        var rate = await service.GetLatestRateAsync("USD", "EUR");

        // Should return a reasonable rate (roughly 0.8-1.0 for USD→EUR)
        Assert.True(rate > 0.5m && rate < 1.5m, $"USD→EUR rate {rate} is out of expected range");
    }

    [Fact]
    public async Task LiveHistoricalRate_FetchesFromApi()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new Mock<ILogger<FxService>>();
        var service = new FxService(new HttpClient(), cache, logger.Object);

        var rate = await service.GetHistoricalRateAsync("USD", "EUR", new DateTime(2024, 1, 15));

        Assert.True(rate > 0.5m && rate < 1.5m, $"Historical USD→EUR rate {rate} is out of expected range");
    }
}
