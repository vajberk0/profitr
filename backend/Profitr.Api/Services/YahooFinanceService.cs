using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Profitr.Api.Models;

namespace Profitr.Api.Services;

public class YahooFinanceService(HttpClient httpClient, IMemoryCache cache, ILogger<YahooFinanceService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<TickerSearchResult>> SearchAsync(string query, int maxResults = 8)
    {
        var cacheKey = $"yf:search:{query.ToLower()}";
        if (cache.TryGetValue(cacheKey, out List<TickerSearchResult>? cached) && cached != null)
            return cached;

        try
        {
            var url = $"https://query2.finance.yahoo.com/v1/finance/search?q={Uri.EscapeDataString(query)}&quotesCount={maxResults}&newsCount=0&listsCount=0";
            var response = await httpClient.GetStringAsync(url);
            var doc = JsonDocument.Parse(response);

            var results = new List<TickerSearchResult>();
            if (doc.RootElement.TryGetProperty("quotes", out var quotes))
            {
                foreach (var q in quotes.EnumerateArray())
                {
                    var symbol = q.GetProperty("symbol").GetString() ?? "";
                    var name = q.TryGetProperty("shortname", out var sn) ? sn.GetString() ?? "" : "";
                    var type = q.TryGetProperty("quoteType", out var qt) ? qt.GetString() ?? "" : "";
                    var exchange = q.TryGetProperty("exchange", out var ex) ? ex.GetString() ?? "" : "";
                    var exchDisp = q.TryGetProperty("exchDisp", out var ed) ? ed.GetString() ?? exchange : exchange;

                    if (!string.IsNullOrEmpty(symbol))
                        results.Add(new TickerSearchResult(symbol, name, NormalizeAssetType(type, name), exchange, exchDisp));
                }
            }

            cache.Set(cacheKey, results, TimeSpan.FromMinutes(5));
            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Yahoo Finance search failed for query: {Query}", query);
            return [];
        }
    }

    public async Task<QuoteResult?> GetQuoteAsync(string symbol)
    {
        var cacheKey = $"yf:quote:{symbol.ToUpper()}";
        if (cache.TryGetValue(cacheKey, out QuoteResult? cached) && cached != null)
            return cached;

        try
        {
            var url = $"https://query2.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=1d&interval=1d";
            var response = await httpClient.GetStringAsync(url);
            var doc = JsonDocument.Parse(response);

            var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
            var meta = result.GetProperty("meta");

            var price = meta.GetProperty("regularMarketPrice").GetDecimal();
            var prevClose = meta.GetProperty("chartPreviousClose").GetDecimal();
            var currency = meta.GetProperty("currency").GetString() ?? "USD";
            var exchange = meta.TryGetProperty("exchangeName", out var exn) ? exn.GetString() ?? "" : "";
            var type = meta.TryGetProperty("instrumentType", out var it) ? it.GetString() ?? "" : "";
            var name = meta.TryGetProperty("shortName", out var sn) ? sn.GetString() ?? symbol : symbol;

            var change = price - prevClose;
            var changePercent = prevClose != 0 ? (change / prevClose) * 100 : 0;

            var quote = new QuoteResult(symbol.ToUpper(), name, price, change, changePercent, currency, exchange, NormalizeAssetType(type, name));
            cache.Set(cacheKey, quote, TimeSpan.FromSeconds(60));
            return quote;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Yahoo Finance quote failed for symbol: {Symbol}", symbol);
            return null;
        }
    }

    public async Task<Dictionary<string, QuoteResult>> GetQuotesAsync(IEnumerable<string> symbols)
    {
        var tasks = symbols.Distinct().Select(async s =>
        {
            var q = await GetQuoteAsync(s);
            return (s.ToUpper(), q);
        });

        var results = await Task.WhenAll(tasks);
        return results
            .Where(r => r.q != null)
            .ToDictionary(r => r.Item1, r => r.q!);
    }

    public async Task<ChartResult?> GetChartAsync(string symbol, string range = "1y", string interval = "1d")
    {
        var cacheKey = $"yf:chart:{symbol.ToUpper()}:{range}:{interval}";
        if (cache.TryGetValue(cacheKey, out ChartResult? cached) && cached != null)
            return cached;

        try
        {
            var url = $"https://query2.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range={range}&interval={interval}";
            var response = await httpClient.GetStringAsync(url);
            var doc = JsonDocument.Parse(response);

            var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
            var meta = result.GetProperty("meta");
            var currency = meta.GetProperty("currency").GetString() ?? "USD";

            var timestamps = result.GetProperty("timestamp").EnumerateArray().Select(t => DateTimeOffset.FromUnixTimeSeconds(t.GetInt64()).UtcDateTime).ToList();
            var indicators = result.GetProperty("indicators").GetProperty("quote")[0];
            var opens = indicators.GetProperty("open").EnumerateArray().ToList();
            var highs = indicators.GetProperty("high").EnumerateArray().ToList();
            var lows = indicators.GetProperty("low").EnumerateArray().ToList();
            var closes = indicators.GetProperty("close").EnumerateArray().ToList();
            var volumes = indicators.GetProperty("volume").EnumerateArray().ToList();

            var points = new List<ChartPoint>();
            for (var i = 0; i < timestamps.Count; i++)
            {
                if (closes[i].ValueKind == JsonValueKind.Null) continue;
                points.Add(new ChartPoint(
                    timestamps[i],
                    opens[i].ValueKind != JsonValueKind.Null ? opens[i].GetDecimal() : 0,
                    highs[i].ValueKind != JsonValueKind.Null ? highs[i].GetDecimal() : 0,
                    lows[i].ValueKind != JsonValueKind.Null ? lows[i].GetDecimal() : 0,
                    closes[i].GetDecimal(),
                    volumes[i].ValueKind != JsonValueKind.Null ? volumes[i].GetInt64() : 0
                ));
            }

            var chartResult = new ChartResult(symbol.ToUpper(), currency, points);
            cache.Set(cacheKey, chartResult, TimeSpan.FromHours(range == "1d" ? 0.016 : 1)); // 1min for 1d, 1h for others
            return chartResult;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Yahoo Finance chart failed for symbol: {Symbol}", symbol);
            return null;
        }
    }

    public async Task<HistoryPriceResult?> GetHistoricalPriceAsync(string symbol, DateTime date)
    {
        var cacheKey = $"yf:histprice:{symbol.ToUpper()}:{date:yyyy-MM-dd}";
        if (cache.TryGetValue(cacheKey, out HistoryPriceResult? cached) && cached != null)
            return cached;

        try
        {
            var period1 = new DateTimeOffset(date.Date).ToUnixTimeSeconds();
            var period2 = new DateTimeOffset(date.Date.AddDays(5)).ToUnixTimeSeconds(); // look forward a few days for non-trading days
            var url = $"https://query2.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={period1}&period2={period2}&interval=1d";
            var response = await httpClient.GetStringAsync(url);
            var doc = JsonDocument.Parse(response);

            var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
            var meta = result.GetProperty("meta");
            var currency = meta.GetProperty("currency").GetString() ?? "USD";

            if (!result.TryGetProperty("timestamp", out var tsArray) || tsArray.GetArrayLength() == 0)
                return null;

            var closes = result.GetProperty("indicators").GetProperty("quote")[0].GetProperty("close").EnumerateArray().ToList();
            // Find the first non-null close
            for (var i = 0; i < closes.Count; i++)
            {
                if (closes[i].ValueKind != JsonValueKind.Null)
                {
                    var hp = new HistoryPriceResult(symbol.ToUpper(), date, closes[i].GetDecimal(), currency);
                    cache.Set(cacheKey, hp, TimeSpan.FromHours(1));
                    return hp;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Yahoo Finance historical price failed for {Symbol} on {Date}", symbol, date);
            return null;
        }
    }

    private static string NormalizeAssetType(string type, string name = "")
    {
        var normalized = type?.ToUpper() switch
        {
            "ETF" => "ETF",
            "ETC" => "ETC",
            "MUTUALFUND" => "ETF",
            _ => "EQUITY"
        };
        // Yahoo Finance classifies many ETCs (e.g. IGLN.L) as EQUITY or ETF;
        // detect them by their name containing " ETC" (e.g. "iShares Physical Gold ETC")
        if (normalized != "ETC" && !string.IsNullOrEmpty(name) &&
            name.Contains(" ETC", StringComparison.OrdinalIgnoreCase))
            return "ETC";
        return normalized;
    }
}
