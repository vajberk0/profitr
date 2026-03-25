using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Profitr.Api.Models;

namespace Profitr.Api.Services;

public class FxService(HttpClient httpClient, IMemoryCache cache, ILogger<FxService> logger)
{
    private const string BaseUrl = "https://api.frankfurter.app";
    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    private async Task<string> GetStringWithRetryAsync(string url)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await httpClient.GetStringAsync(url);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries && IsTransient(ex))
            {
                logger.LogWarning("Frankfurter request failed (attempt {Attempt}/{Max}, status {Status}), retrying in {Delay}ms: {Url}",
                    attempt + 1, MaxRetries, ex.StatusCode, RetryDelays[attempt].TotalMilliseconds, url);
                await Task.Delay(RetryDelays[attempt]);
            }
        }
    }

    private static bool IsTransient(HttpRequestException ex)
    {
        // 5xx = server errors, 429 = rate limited, null = network-level failure
        return ex.StatusCode is null
            || (int)ex.StatusCode >= 500
            || ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests;
    }

    private static readonly Dictionary<string, string> CurrencyNames = new()
    {
        ["AUD"] = "Australian Dollar", ["BRL"] = "Brazilian Real", ["CAD"] = "Canadian Dollar",
        ["CHF"] = "Swiss Franc", ["CNY"] = "Chinese Renminbi Yuan", ["CZK"] = "Czech Koruna",
        ["DKK"] = "Danish Krone", ["EUR"] = "Euro", ["GBP"] = "British Pound",
        ["HKD"] = "Hong Kong Dollar", ["HUF"] = "Hungarian Forint", ["IDR"] = "Indonesian Rupiah",
        ["ILS"] = "Israeli New Sheqel", ["INR"] = "Indian Rupee", ["ISK"] = "Icelandic Króna",
        ["JPY"] = "Japanese Yen", ["KRW"] = "South Korean Won", ["MXN"] = "Mexican Peso",
        ["MYR"] = "Malaysian Ringgit", ["NOK"] = "Norwegian Krone", ["NZD"] = "New Zealand Dollar",
        ["PHP"] = "Philippine Peso", ["PLN"] = "Polish Złoty", ["RON"] = "Romanian Leu",
        ["SEK"] = "Swedish Krona", ["SGD"] = "Singapore Dollar", ["THB"] = "Thai Baht",
        ["TRY"] = "Turkish Lira", ["USD"] = "United States Dollar", ["ZAR"] = "South African Rand"
    };

    public List<CurrencyInfo> GetSupportedCurrencies()
    {
        return CurrencyNames.Select(kv => new CurrencyInfo(kv.Key, kv.Value)).OrderBy(c => c.Code).ToList();
    }

    public async Task<decimal> GetLatestRateAsync(string from, string to)
    {
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) return 1m;

        var cacheKey = $"fx:latest:{from.ToUpper()}:{to.ToUpper()}";
        if (cache.TryGetValue(cacheKey, out decimal cached))
            return cached;

        try
        {
            var url = $"{BaseUrl}/latest?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
            var response = await GetStringWithRetryAsync(url);
            var doc = JsonDocument.Parse(response);
            var rate = doc.RootElement.GetProperty("rates").GetProperty(to.ToUpper()).GetDecimal();

            cache.Set(cacheKey, rate, TimeSpan.FromHours(1));
            return rate;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Frankfurter latest rate failed for {From}→{To}", from, to);
            return 1m; // fallback: no conversion
        }
    }

    public async Task<decimal> GetHistoricalRateAsync(string from, string to, DateTime date)
    {
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) return 1m;

        var dateStr = date.ToString("yyyy-MM-dd");
        var cacheKey = $"fx:hist:{from.ToUpper()}:{to.ToUpper()}:{dateStr}";
        if (cache.TryGetValue(cacheKey, out decimal cached))
            return cached;

        try
        {
            var url = $"{BaseUrl}/{dateStr}?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
            var response = await GetStringWithRetryAsync(url);
            var doc = JsonDocument.Parse(response);
            var rate = doc.RootElement.GetProperty("rates").GetProperty(to.ToUpper()).GetDecimal();

            cache.Set(cacheKey, rate, TimeSpan.FromHours(24));
            return rate;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Frankfurter historical rate failed for {From}→{To} on {Date}", from, to, dateStr);
            return 1m;
        }
    }

    /// <summary>
    /// Gets a range of daily rates, useful for building P&L charts
    /// </summary>
    public async Task<Dictionary<string, decimal>> GetRateRangeAsync(string from, string to, DateTime startDate, DateTime endDate)
    {
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase))
            return new Dictionary<string, decimal>();

        var start = startDate.ToString("yyyy-MM-dd");
        var end = endDate.ToString("yyyy-MM-dd");
        var cacheKey = $"fx:range:{from.ToUpper()}:{to.ToUpper()}:{start}:{end}";
        if (cache.TryGetValue(cacheKey, out Dictionary<string, decimal>? cached) && cached != null)
            return cached;

        try
        {
            var url = $"{BaseUrl}/{start}..{end}?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
            var response = await GetStringWithRetryAsync(url);
            var doc = JsonDocument.Parse(response);

            var rates = new Dictionary<string, decimal>();
            foreach (var prop in doc.RootElement.GetProperty("rates").EnumerateObject())
            {
                rates[prop.Name] = prop.Value.GetProperty(to.ToUpper()).GetDecimal();
            }

            cache.Set(cacheKey, rates, TimeSpan.FromHours(24));
            return rates;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Frankfurter rate range failed for {From}→{To} from {Start} to {End}", from, to, start, end);
            return new Dictionary<string, decimal>();
        }
    }
}
