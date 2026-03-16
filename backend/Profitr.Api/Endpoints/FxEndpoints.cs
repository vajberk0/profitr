using Profitr.Api.Services;

namespace Profitr.Api.Endpoints;

public static class FxEndpoints
{
    public static void MapFxEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/fx");

        group.MapGet("/currencies", (FxService fx) =>
        {
            return Results.Ok(fx.GetSupportedCurrencies());
        });

        group.MapGet("/latest", async (string from, string to, FxService fx) =>
        {
            var rate = await fx.GetLatestRateAsync(from, to);
            return Results.Ok(new { from, to, rate, date = DateTime.UtcNow.ToString("yyyy-MM-dd") });
        });

        group.MapGet("/historical", async (string from, string to, DateTime date, FxService fx) =>
        {
            var rate = await fx.GetHistoricalRateAsync(from, to, date);
            return Results.Ok(new { from, to, rate, date = date.ToString("yyyy-MM-dd") });
        });
    }
}
