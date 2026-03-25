using Profitr.Api.Models;

namespace Profitr.Api.Services;

/// <summary>
/// Abstraction for FX rate data sources. Implement this to swap providers
/// (e.g., Frankfurter, ECB, Open Exchange Rates, etc.)
/// </summary>
public interface IFxRateProvider
{
    /// <summary>Get the latest available rate. Returns null on failure.</summary>
    Task<decimal?> GetLatestRateAsync(string from, string to);

    /// <summary>Get rate for a specific date. Returns null on failure.</summary>
    Task<decimal?> GetHistoricalRateAsync(string from, string to, DateTime date);

    /// <summary>Get daily rates for a date range. Returns null on failure.</summary>
    Task<Dictionary<string, decimal>?> GetRateRangeAsync(string from, string to, DateTime startDate, DateTime endDate);

    /// <summary>Get the list of supported currency codes.</summary>
    List<CurrencyInfo> GetSupportedCurrencies();
}
