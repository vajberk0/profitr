using Profitr.Api.Services;

namespace Profitr.Api.Endpoints;

public static class MarketEndpoints
{
    public static void MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/market");

        group.MapGet("/search", async (string q, YahooFinanceService yahoo) =>
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length < 1)
                return Results.BadRequest("Query must be at least 1 character.");

            var results = await yahoo.SearchAsync(q);
            return Results.Ok(results);
        });

        group.MapGet("/quote", async (string symbols, YahooFinanceService yahoo) =>
        {
            var symbolList = symbols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (symbolList.Length == 0)
                return Results.BadRequest("At least one symbol required.");

            var quotes = await yahoo.GetQuotesAsync(symbolList);
            return Results.Ok(quotes);
        });

        group.MapGet("/chart/{symbol}", async (string symbol, string? range, string? interval, YahooFinanceService yahoo) =>
        {
            var chart = await yahoo.GetChartAsync(symbol, range ?? "1y", interval ?? "1d");
            if (chart == null) return Results.NotFound();
            return Results.Ok(chart);
        });

        group.MapGet("/history-price", async (string symbol, DateTime date, YahooFinanceService yahoo) =>
        {
            var result = await yahoo.GetHistoricalPriceAsync(symbol, date);
            if (result == null) return Results.NotFound();
            return Results.Ok(result);
        });
    }
}
