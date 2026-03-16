using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Profitr.Api.Data;
using Profitr.Api.Data.Entities;
using Profitr.Api.Models;
using Profitr.Api.Services;

namespace Profitr.Api.Endpoints;

public static class PortfolioEndpoints
{
    public static void MapPortfolioEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolios").RequireAuthorization();

        group.MapGet("/", async (HttpContext ctx, ProfitrDbContext db) =>
        {
            var userId = await GetUserId(ctx, db);
            if (userId == null) return Results.Unauthorized();

            var portfolios = await db.Portfolios
                .Where(p => p.UserId == userId)
                .Include(p => p.Transactions)
                .OrderByDescending(p => p.IsDefault)
                .ThenBy(p => p.Name)
                .ToListAsync();

            var dtos = portfolios.Select(p => new PortfolioDto(
                p.Id, p.Name, p.IsDefault, p.CreatedAt,
                p.Transactions.Select(t => t.Symbol).Distinct().Count()
            )).ToList();

            return Results.Ok(dtos);
        });

        group.MapPost("/", async (HttpContext ctx, ProfitrDbContext db, CreatePortfolioRequest req) =>
        {
            var userId = await GetUserId(ctx, db);
            if (userId == null) return Results.Unauthorized();

            var portfolio = new Portfolio
            {
                UserId = userId,
                Name = req.Name,
                IsDefault = false
            };

            db.Portfolios.Add(portfolio);
            await db.SaveChangesAsync();

            return Results.Created($"/api/portfolios/{portfolio.Id}",
                new PortfolioDto(portfolio.Id, portfolio.Name, portfolio.IsDefault, portfolio.CreatedAt, 0));
        });

        group.MapPut("/{id:guid}", async (Guid id, HttpContext ctx, ProfitrDbContext db, UpdatePortfolioRequest req) =>
        {
            var userId = await GetUserId(ctx, db);
            var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
            if (portfolio == null) return Results.NotFound();

            portfolio.Name = req.Name;
            await db.SaveChangesAsync();

            return Results.Ok(new PortfolioDto(portfolio.Id, portfolio.Name, portfolio.IsDefault, portfolio.CreatedAt, 0));
        });

        group.MapDelete("/{id:guid}", async (Guid id, HttpContext ctx, ProfitrDbContext db) =>
        {
            var userId = await GetUserId(ctx, db);
            var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
            if (portfolio == null) return Results.NotFound();
            if (portfolio.IsDefault) return Results.BadRequest("Cannot delete the default portfolio.");

            db.Portfolios.Remove(portfolio);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/set-default", async (Guid id, HttpContext ctx, ProfitrDbContext db) =>
        {
            var userId = await GetUserId(ctx, db);
            var portfolios = await db.Portfolios.Where(p => p.UserId == userId).ToListAsync();
            var target = portfolios.FirstOrDefault(p => p.Id == id);
            if (target == null) return Results.NotFound();

            foreach (var p in portfolios) p.IsDefault = false;
            target.IsDefault = true;
            await db.SaveChangesAsync();

            return Results.Ok();
        });

        group.MapGet("/{id:guid}/summary", async (Guid id, string? range, HttpContext ctx, ProfitrDbContext db, PnLService pnl) =>
        {
            var userId = await GetUserId(ctx, db);
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            var portfolio = await db.Portfolios
                .Include(p => p.Transactions)
                .Include(p => p.Dividends)
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

            if (portfolio == null) return Results.NotFound();

            var summary = await pnl.CalculatePortfolioSummaryAsync(portfolio, user.DisplayCurrency);
            return Results.Ok(summary);
        });

        group.MapGet("/{id:guid}/history", async (Guid id, string? range, HttpContext ctx, ProfitrDbContext db, PnLService pnl) =>
        {
            var userId = await GetUserId(ctx, db);
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            var portfolio = await db.Portfolios
                .Include(p => p.Transactions)
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

            if (portfolio == null) return Results.NotFound();

            var history = await pnl.ComputePortfolioHistoryAsync(portfolio, user.DisplayCurrency, range ?? "1y");
            return Results.Ok(history);
        });
    }

    internal static async Task<string?> GetUserId(HttpContext ctx, ProfitrDbContext db)
    {
        var googleId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(googleId)) return null;
        var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubjectId == googleId);
        return user?.Id;
    }
}
