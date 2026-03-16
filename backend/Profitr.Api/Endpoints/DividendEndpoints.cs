using Microsoft.EntityFrameworkCore;
using Profitr.Api.Data;
using Profitr.Api.Data.Entities;
using Profitr.Api.Models;

namespace Profitr.Api.Endpoints;

public static class DividendEndpoints
{
    public static void MapDividendEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        group.MapGet("/portfolios/{portfolioId:guid}/dividends", async (Guid portfolioId, HttpContext ctx, ProfitrDbContext db) =>
        {
            var userId = await PortfolioEndpoints.GetUserId(ctx, db);
            var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);
            if (portfolio == null) return Results.NotFound();

            var dividends = await db.Dividends
                .Where(d => d.PortfolioId == portfolioId)
                .OrderByDescending(d => d.PayDate)
                .Select(d => new DividendDto(
                    d.Id, d.Symbol, d.AmountPerShare, d.NativeCurrency,
                    d.ExDate, d.PayDate, d.Notes, d.CreatedAt
                ))
                .ToListAsync();

            return Results.Ok(dividends);
        });

        group.MapPost("/portfolios/{portfolioId:guid}/dividends", async (Guid portfolioId, HttpContext ctx, ProfitrDbContext db, CreateDividendRequest req) =>
        {
            var userId = await PortfolioEndpoints.GetUserId(ctx, db);
            var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);
            if (portfolio == null) return Results.NotFound();

            var dividend = new Dividend
            {
                PortfolioId = portfolioId,
                Symbol = req.Symbol.ToUpper(),
                AmountPerShare = req.AmountPerShare,
                NativeCurrency = req.NativeCurrency,
                ExDate = req.ExDate,
                PayDate = req.PayDate,
                Notes = req.Notes
            };

            db.Dividends.Add(dividend);
            await db.SaveChangesAsync();

            return Results.Created($"/api/dividends/{dividend.Id}",
                new DividendDto(dividend.Id, dividend.Symbol, dividend.AmountPerShare,
                    dividend.NativeCurrency, dividend.ExDate, dividend.PayDate,
                    dividend.Notes, dividend.CreatedAt));
        });

        group.MapDelete("/dividends/{id:guid}", async (Guid id, HttpContext ctx, ProfitrDbContext db) =>
        {
            var userId = await PortfolioEndpoints.GetUserId(ctx, db);
            var dividend = await db.Dividends
                .Include(d => d.Portfolio)
                .FirstOrDefaultAsync(d => d.Id == id && d.Portfolio.UserId == userId);

            if (dividend == null) return Results.NotFound();

            db.Dividends.Remove(dividend);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });
    }
}
