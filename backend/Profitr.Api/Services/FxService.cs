using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Profitr.Api.Data;
using Profitr.Api.Data.Entities;
using Profitr.Api.Models;

namespace Profitr.Api.Services;

/// <summary>
/// FX rate service with persistent SQLite caching.
/// Memory cache → SQLite → IFxRateProvider (API).
/// Historical rates are cached permanently; latest rates refresh hourly.
/// </summary>
public class FxService(IFxRateProvider provider, ProfitrDbContext db, IMemoryCache cache, ILogger<FxService> logger)
{
    public List<CurrencyInfo> GetSupportedCurrencies() => provider.GetSupportedCurrencies();

    public async Task<decimal> GetLatestRateAsync(string from, string to)
    {
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) return 1m;
        from = from.ToUpper(); to = to.ToUpper();

        var cacheKey = $"fx:latest:{from}:{to}";
        if (cache.TryGetValue(cacheKey, out decimal memCached))
            return memCached;

        // Try the API first (latest endpoint gives most current rate)
        var rate = await provider.GetLatestRateAsync(from, to);
        if (rate.HasValue)
        {
            cache.Set(cacheKey, rate.Value, TimeSpan.FromHours(1));
            // Also persist with today's date for fallback
            await UpsertRateAsync(from, to, DateTime.UtcNow.Date, rate.Value);
            return rate.Value;
        }

        // API failed — fall back to most recent cached rate from SQLite
        var fallback = await db.CachedFxRates
            .Where(r => r.BaseCurrency == from && r.QuoteCurrency == to)
            .OrderByDescending(r => r.RateDate)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync();

        if (fallback.HasValue)
        {
            logger.LogWarning("Using cached fallback rate for {From}→{To}: {Rate}", from, to, fallback.Value);
            cache.Set(cacheKey, fallback.Value, TimeSpan.FromMinutes(10));
            return fallback.Value;
        }

        logger.LogError("No rate available for {From}→{To}, returning 1", from, to);
        return 1m;
    }

    public async Task<decimal> GetHistoricalRateAsync(string from, string to, DateTime date)
    {
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) return 1m;
        from = from.ToUpper(); to = to.ToUpper();

        var dateStr = date.ToString("yyyy-MM-dd");
        var cacheKey = $"fx:hist:{from}:{to}:{dateStr}";
        if (cache.TryGetValue(cacheKey, out decimal memCached))
            return memCached;

        // Check SQLite
        var dbDate = date.Date;
        var dbRate = await db.CachedFxRates
            .Where(r => r.BaseCurrency == from && r.QuoteCurrency == to && r.RateDate == dbDate)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync();

        if (dbRate.HasValue)
        {
            cache.Set(cacheKey, dbRate.Value, TimeSpan.FromHours(24));
            return dbRate.Value;
        }

        // Fetch from provider
        var rate = await provider.GetHistoricalRateAsync(from, to, date);
        if (rate.HasValue)
        {
            cache.Set(cacheKey, rate.Value, TimeSpan.FromHours(24));
            await UpsertRateAsync(from, to, dbDate, rate.Value);
            return rate.Value;
        }

        // API failed — try nearest cached date as fallback
        var nearest = await db.CachedFxRates
            .Where(r => r.BaseCurrency == from && r.QuoteCurrency == to && r.RateDate <= dbDate)
            .OrderByDescending(r => r.RateDate)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync();

        if (nearest.HasValue)
        {
            logger.LogWarning("Using nearest cached rate for {From}→{To} on {Date}: {Rate}", from, to, dateStr, nearest.Value);
            return nearest.Value;
        }

        logger.LogError("No rate available for {From}→{To} on {Date}, returning 1", from, to, dateStr);
        return 1m;
    }

    public async Task<Dictionary<string, decimal>> GetRateRangeAsync(string from, string to, DateTime startDate, DateTime endDate)
    {
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase))
            return new Dictionary<string, decimal>();

        from = from.ToUpper(); to = to.ToUpper();

        // Short-lived memory cache for identical repeat calls (e.g., page refresh)
        var cacheKey = $"fx:range:{from}:{to}:{startDate:yyyy-MM-dd}:{endDate:yyyy-MM-dd}";
        if (cache.TryGetValue(cacheKey, out Dictionary<string, decimal>? memCached) && memCached != null)
            return memCached;

        // Load all cached rates from SQLite for this pair + range
        var startDb = startDate.Date;
        var endDb = endDate.Date;
        var dbRates = await db.CachedFxRates
            .Where(r => r.BaseCurrency == from && r.QuoteCurrency == to
                     && r.RateDate >= startDb && r.RateDate <= endDb)
            .ToDictionaryAsync(
                r => r.RateDate.ToString("yyyy-MM-dd"),
                r => r.Rate);

        // Determine if we need to fetch from API
        DateTime? fetchFrom = null;
        if (dbRates.Count == 0)
        {
            fetchFrom = startDate;
        }
        else
        {
            var latestCachedDate = dbRates.Keys
                .Select(k => DateTime.ParseExact(k, "yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Max();
            var gap = (endDate.Date - latestCachedDate).TotalDays;

            // If range ends near today, refresh if >1 day stale; for historical ranges, 3-day buffer for weekends
            bool endsNearToday = endDate.Date >= DateTime.UtcNow.Date.AddDays(-1);
            if (endsNearToday ? gap > 1 : gap > 3)
                fetchFrom = latestCachedDate.AddDays(1);
        }

        if (fetchFrom != null)
        {
            var fetched = await provider.GetRateRangeAsync(from, to, fetchFrom.Value, endDate);
            if (fetched != null)
            {
                var newRates = new List<CachedFxRate>();
                foreach (var (dateStr, rate) in fetched)
                {
                    if (!dbRates.ContainsKey(dateStr))
                    {
                        newRates.Add(new CachedFxRate
                        {
                            BaseCurrency = from,
                            QuoteCurrency = to,
                            RateDate = DateTime.ParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                            Rate = rate
                        });
                    }
                    dbRates[dateStr] = rate;
                }
                if (newRates.Count > 0)
                {
                    db.CachedFxRates.AddRange(newRates);
                    await db.SaveChangesAsync();
                    logger.LogInformation("Cached {Count} FX rates for {From}→{To}", newRates.Count, from, to);
                }
            }
        }

        cache.Set(cacheKey, dbRates, TimeSpan.FromMinutes(5));
        return dbRates;
    }

    private async Task UpsertRateAsync(string from, string to, DateTime date, decimal rate)
    {
        var existing = await db.CachedFxRates
            .FirstOrDefaultAsync(r => r.BaseCurrency == from && r.QuoteCurrency == to && r.RateDate == date);

        if (existing != null)
            existing.Rate = rate;
        else
            db.CachedFxRates.Add(new CachedFxRate { BaseCurrency = from, QuoteCurrency = to, RateDate = date, Rate = rate });

        await db.SaveChangesAsync();
    }
}
