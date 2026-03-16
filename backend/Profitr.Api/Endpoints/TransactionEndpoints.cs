using Microsoft.EntityFrameworkCore;
using Profitr.Api.Data;
using Profitr.Api.Data.Entities;
using Profitr.Api.Models;

namespace Profitr.Api.Endpoints;

public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        group.MapGet("/portfolios/{portfolioId:guid}/transactions", async (Guid portfolioId, HttpContext ctx, ProfitrDbContext db) =>
        {
            var userId = await PortfolioEndpoints.GetUserId(ctx, db);
            var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);
            if (portfolio == null) return Results.NotFound();

            var transactions = await db.Transactions
                .Where(t => t.PortfolioId == portfolioId)
                .OrderByDescending(t => t.TransactionDate)
                .Select(t => new TransactionDto(
                    t.Id, t.Type.ToString(), t.Symbol, t.InstrumentName, t.AssetType,
                    t.Quantity, t.PricePerUnit, t.NativeCurrency,
                    t.TransactionDate, t.Notes, t.CreatedAt
                ))
                .ToListAsync();

            return Results.Ok(transactions);
        });

        group.MapPost("/portfolios/{portfolioId:guid}/transactions", async (Guid portfolioId, HttpContext ctx, ProfitrDbContext db, CreateTransactionRequest req) =>
        {
            var userId = await PortfolioEndpoints.GetUserId(ctx, db);
            var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);
            if (portfolio == null) return Results.NotFound();

            // Validate sell: can't sell more than you own
            if (req.Type.Equals("Sell", StringComparison.OrdinalIgnoreCase))
            {
                var currentQty = await db.Transactions
                    .Where(t => t.PortfolioId == portfolioId && t.Symbol == req.Symbol)
                    .SumAsync(t => t.Type == TransactionType.Buy ? t.Quantity : -t.Quantity);

                if (req.Quantity > currentQty)
                    return Results.BadRequest($"Cannot sell {req.Quantity} shares. Current holdings: {currentQty}");
            }

            var transaction = new Transaction
            {
                PortfolioId = portfolioId,
                Type = Enum.Parse<TransactionType>(req.Type, true),
                Symbol = req.Symbol.ToUpper(),
                InstrumentName = req.InstrumentName,
                AssetType = req.AssetType,
                Quantity = req.Quantity,
                PricePerUnit = req.PricePerUnit,
                NativeCurrency = req.NativeCurrency,
                TransactionDate = req.TransactionDate,
                Notes = req.Notes
            };

            db.Transactions.Add(transaction);
            await db.SaveChangesAsync();

            return Results.Created($"/api/transactions/{transaction.Id}",
                new TransactionDto(transaction.Id, transaction.Type.ToString(), transaction.Symbol,
                    transaction.InstrumentName, transaction.AssetType, transaction.Quantity,
                    transaction.PricePerUnit, transaction.NativeCurrency,
                    transaction.TransactionDate, transaction.Notes, transaction.CreatedAt));
        });

        group.MapPut("/transactions/{id:guid}", async (Guid id, HttpContext ctx, ProfitrDbContext db, UpdateTransactionRequest req) =>
        {
            var userId = await PortfolioEndpoints.GetUserId(ctx, db);
            var txn = await db.Transactions
                .Include(t => t.Portfolio)
                .FirstOrDefaultAsync(t => t.Id == id && t.Portfolio.UserId == userId);

            if (txn == null) return Results.NotFound();

            txn.Quantity = req.Quantity;
            txn.PricePerUnit = req.PricePerUnit;
            txn.TransactionDate = req.TransactionDate;
            txn.Notes = req.Notes;
            await db.SaveChangesAsync();

            return Results.Ok(new TransactionDto(txn.Id, txn.Type.ToString(), txn.Symbol,
                txn.InstrumentName, txn.AssetType, txn.Quantity,
                txn.PricePerUnit, txn.NativeCurrency,
                txn.TransactionDate, txn.Notes, txn.CreatedAt));
        });

        group.MapDelete("/transactions/{id:guid}", async (Guid id, HttpContext ctx, ProfitrDbContext db) =>
        {
            var userId = await PortfolioEndpoints.GetUserId(ctx, db);
            var txn = await db.Transactions
                .Include(t => t.Portfolio)
                .FirstOrDefaultAsync(t => t.Id == id && t.Portfolio.UserId == userId);

            if (txn == null) return Results.NotFound();

            db.Transactions.Remove(txn);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });
    }
}
