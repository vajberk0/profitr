using Microsoft.EntityFrameworkCore;
using Profitr.Api.Data;
using Profitr.Api.Data.Entities;
using Profitr.Api.Models;

namespace Profitr.Api.Endpoints;

public static class CashEndpoints
{
    public static void MapCashEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        group.MapGet("/portfolios/{portfolioId:guid}/cash", async (Guid portfolioId, HttpContext ctx, ProfitrDbContext db) =>
        {
            var userId = await PortfolioEndpoints.GetUserId(ctx, db);
            var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);
            if (portfolio == null) return Results.NotFound();

            var cashTxns = await db.CashTransactions
                .Where(c => c.PortfolioId == portfolioId)
                .OrderByDescending(c => c.TransactionDate)
                .Select(c => new CashTransactionDto(
                    c.Id, c.Type.ToString(), c.Amount, c.Currency,
                    c.TransactionDate, c.Notes, c.CreatedAt
                ))
                .ToListAsync();

            return Results.Ok(cashTxns);
        });

        group.MapPost("/portfolios/{portfolioId:guid}/cash", async (Guid portfolioId, HttpContext ctx, ProfitrDbContext db, CreateCashTransactionRequest req) =>
        {
            var userId = await PortfolioEndpoints.GetUserId(ctx, db);
            var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);
            if (portfolio == null) return Results.NotFound();

            if (!Enum.TryParse<CashTransactionType>(req.Type, true, out var type))
                return Results.BadRequest("Type must be 'Deposit' or 'Withdrawal'.");

            if (req.Amount <= 0)
                return Results.BadRequest("Amount must be positive.");

            if (string.IsNullOrWhiteSpace(req.Currency) || req.Currency.Length != 3)
                return Results.BadRequest("Currency must be a 3-letter code.");

            var cashTxn = new CashTransaction
            {
                PortfolioId = portfolioId,
                Type = type,
                Amount = req.Amount,
                Currency = req.Currency.ToUpper(),
                TransactionDate = req.TransactionDate,
                Notes = req.Notes
            };

            db.CashTransactions.Add(cashTxn);
            await db.SaveChangesAsync();

            return Results.Created($"/api/cash/{cashTxn.Id}",
                new CashTransactionDto(cashTxn.Id, cashTxn.Type.ToString(), cashTxn.Amount,
                    cashTxn.Currency, cashTxn.TransactionDate, cashTxn.Notes, cashTxn.CreatedAt));
        });

        group.MapDelete("/cash/{id:guid}", async (Guid id, HttpContext ctx, ProfitrDbContext db) =>
        {
            var userId = await PortfolioEndpoints.GetUserId(ctx, db);
            var cashTxn = await db.CashTransactions
                .Include(c => c.Portfolio)
                .FirstOrDefaultAsync(c => c.Id == id && c.Portfolio.UserId == userId);

            if (cashTxn == null) return Results.NotFound();

            db.CashTransactions.Remove(cashTxn);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });
    }
}
