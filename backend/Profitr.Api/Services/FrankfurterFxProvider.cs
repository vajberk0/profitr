using System.Text.Json;
using Profitr.Api.Models;

namespace Profitr.Api.Services;

public class FrankfurterFxProvider(HttpClient httpClient, ILogger<FrankfurterFxProvider> logger) : IFxRateProvider
{
    private const string BaseUrl = "https://api.frankfurter.app";
    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

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

    public async Task<decimal?> GetLatestRateAsync(string from, string to)
    {
        try
        {
            var url = $"{BaseUrl}/latest?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
            var response = await GetStringWithRetryAsync(url);
            var doc = JsonDocument.Parse(response);
            return doc.RootElement.GetProperty("rates").GetProperty(to.ToUpper()).GetDecimal();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Frankfurter latest rate failed for {From}→{To}", from, to);
            return null;
        }
    }

    public async Task<decimal?> GetHistoricalRateAsync(string from, string to, DateTime date)
    {
        try
        {
            var dateStr = date.ToString("yyyy-MM-dd");
            var url = $"{BaseUrl}/{dateStr}?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
            var response = await GetStringWithRetryAsync(url);
            var doc = JsonDocument.Parse(response);
            return doc.RootElement.GetProperty("rates").GetProperty(to.ToUpper()).GetDecimal();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Frankfurter historical rate failed for {From}→{To} on {Date}", from, to, date.ToString("yyyy-MM-dd"));
            return null;
        }
    }

    public async Task<Dictionary<string, decimal>?> GetRateRangeAsync(string from, string to, DateTime startDate, DateTime endDate)
    {
        try
        {
            var start = startDate.ToString("yyyy-MM-dd");
            var end = endDate.ToString("yyyy-MM-dd");
            var url = $"{BaseUrl}/{start}..{end}?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
            var response = await GetStringWithRetryAsync(url);
            var doc = JsonDocument.Parse(response);

            var rates = new Dictionary<string, decimal>();
            foreach (var prop in doc.RootElement.GetProperty("rates").EnumerateObject())
            {
                rates[prop.Name] = prop.Value.GetProperty(to.ToUpper()).GetDecimal();
            }
            return rates;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Frankfurter rate range failed for {From}→{To} from {Start} to {End}",
                from, to, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
            return null;
        }
    }

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
        return ex.StatusCode is null
            || (int)ex.StatusCode >= 500
            || ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests;
    }
}
