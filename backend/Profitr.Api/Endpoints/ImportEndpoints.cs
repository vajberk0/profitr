using Microsoft.EntityFrameworkCore;
using Profitr.Api.Data;
using Profitr.Api.Data.Entities;
using Profitr.Api.Models;
using Profitr.Api.Services;

namespace Profitr.Api.Endpoints;

public static class ImportEndpoints
{
    public static void MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        // Parse CSV and return preview
        group.MapPost("/portfolios/{portfolioId:guid}/transactions/import/parse",
            async (Guid portfolioId, IFormFile file, HttpContext ctx, ProfitrDbContext db, YahooFinanceService yahoo) =>
            {
                var userId = await PortfolioEndpoints.GetUserId(ctx, db);
                var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);
                if (portfolio == null) return Results.NotFound();

                using var reader = new StreamReader(file.OpenReadStream());
                var content = await reader.ReadToEndAsync();

                var parser = new IbkrCsvParser();
                var parseResult = parser.ParseAll(content);
                var parsedRows = parseResult.Transactions;

                if (parsedRows.Count == 0 && parseResult.CashTransactions.Count == 0)
                    return Results.BadRequest("No valid transactions found in the CSV file. Make sure it's an IBKR Transaction History export.");

                // Look up unique symbols: quote (to check if direct symbol works) + search (for alternatives)
                var uniqueSymbols = parsedRows.Select(r => r.Symbol).Distinct().ToArray();
                var quotes = await yahoo.GetQuotesAsync(uniqueSymbols);

                var symbolMappings = new Dictionary<string, SymbolMapping>();
                foreach (var symbol in uniqueSymbols)
                {
                    var upperSymbol = symbol.ToUpper();
                    var quote = quotes.GetValueOrDefault(upperSymbol);
                    var suggestions = await yahoo.SearchAsync(symbol);

                    if (quote != null)
                    {
                        // Direct symbol resolved — ensure it appears first in suggestions
                        var quoteAsResult = new TickerSearchResult(
                            quote.Symbol, quote.Name, quote.AssetType, quote.Exchange, quote.Exchange);
                        if (!suggestions.Any(s => s.Symbol.Equals(quote.Symbol, StringComparison.OrdinalIgnoreCase)))
                            suggestions.Insert(0, quoteAsResult);

                        symbolMappings[upperSymbol] = new SymbolMapping(
                            CsvSymbol: upperSymbol,
                            Resolved: true,
                            YahooSymbol: quote.Symbol,
                            InstrumentName: quote.Name,
                            AssetType: quote.AssetType,
                            Suggestions: suggestions
                        );
                    }
                    else
                    {
                        var best = suggestions.FirstOrDefault();
                        symbolMappings[upperSymbol] = new SymbolMapping(
                            CsvSymbol: upperSymbol,
                            Resolved: false,
                            YahooSymbol: best?.Symbol,
                            InstrumentName: best?.Name,
                            AssetType: best?.Type,
                            Suggestions: suggestions
                        );
                    }
                }

                var previewRows = parsedRows.Select((r, i) =>
                {
                    var upperSymbol = r.Symbol.ToUpper();
                    var mapping = symbolMappings[upperSymbol];

                    return new ImportPreviewRow(
                        RowIndex: i,
                        Date: r.Date,
                        Symbol: upperSymbol,
                        InstrumentName: mapping.Resolved ? mapping.InstrumentName! : r.Description,
                        AssetType: mapping.AssetType ?? "ETF",
                        TransactionType: r.TransactionType,
                        Quantity: r.Quantity,
                        PricePerUnit: r.Price,
                        NativeCurrency: r.PriceCurrency,
                        Commission: r.Commission,
                        IsValid: true,
                        Error: null
                    );
                }).ToList();

                var cashPreviewRows = parseResult.CashTransactions.Select((c, i) =>
                    new ImportCashPreviewRow(
                        RowIndex: i,
                        Date: c.Date,
                        Description: c.Description,
                        CashType: c.CashType,
                        Amount: c.Amount,
                        Currency: c.Currency,
                        IsValid: true,
                        Error: null
                    )
                ).ToList();

                var totalRows = previewRows.Count + cashPreviewRows.Count;

                return Results.Ok(new ImportPreviewResponse(
                    Rows: previewRows,
                    CashRows: cashPreviewRows,
                    TotalRows: totalRows,
                    ValidRows: totalRows,
                    SkippedRows: 0,
                    SymbolMappings: symbolMappings
                ));
            }).DisableAntiforgery();

        // Confirm and create transactions
        group.MapPost("/portfolios/{portfolioId:guid}/transactions/import/confirm",
            async (Guid portfolioId, ImportConfirmRequest req, HttpContext ctx, ProfitrDbContext db) =>
            {
                var userId = await PortfolioEndpoints.GetUserId(ctx, db);
                var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);
                if (portfolio == null) return Results.NotFound();

                var transactions = new List<Transaction>();
                var cashTransactions = new List<CashTransaction>();
                var errors = new List<string>();

                foreach (var row in req.Rows)
                {
                    try
                    {
                        var txn = new Transaction
                        {
                            PortfolioId = portfolioId,
                            Type = Enum.Parse<TransactionType>(row.TransactionType, true),
                            Symbol = row.Symbol.ToUpper(),
                            InstrumentName = row.InstrumentName,
                            AssetType = row.AssetType,
                            Quantity = row.Quantity,
                            PricePerUnit = row.PricePerUnit,
                            NativeCurrency = row.NativeCurrency,
                            TransactionDate = DateTime.Parse(row.Date),
                            Notes = row.Notes
                        };
                        db.Transactions.Add(txn);
                        transactions.Add(txn);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{row.Symbol} on {row.Date}: {ex.Message}");
                    }
                }

                // Import cash deposits/withdrawals
                if (req.CashRows != null)
                {
                    foreach (var cashRow in req.CashRows)
                    {
                        try
                        {
                            if (!Enum.TryParse<CashTransactionType>(cashRow.CashType, true, out var cashType))
                            {
                                errors.Add($"Cash {cashRow.CashType} on {cashRow.Date}: Invalid type.");
                                continue;
                            }

                            var cashTxn = new CashTransaction
                            {
                                PortfolioId = portfolioId,
                                Type = cashType,
                                Amount = cashRow.Amount,
                                Currency = cashRow.Currency.ToUpper(),
                                TransactionDate = DateTime.Parse(cashRow.Date),
                                Notes = cashRow.Notes
                            };
                            db.CashTransactions.Add(cashTxn);
                            cashTransactions.Add(cashTxn);
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Cash {cashRow.CashType} on {cashRow.Date}: {ex.Message}");
                        }
                    }
                }

                await db.SaveChangesAsync();

                var dtos = transactions.Select(t => new TransactionDto(
                    t.Id, t.Type.ToString(), t.Symbol, t.InstrumentName, t.AssetType,
                    t.Quantity, t.PricePerUnit, t.NativeCurrency,
                    t.TransactionDate, t.Notes, t.CreatedAt
                )).ToList();

                return Results.Ok(new ImportResultResponse(
                    ImportedCount: transactions.Count,
                    CashImportedCount: cashTransactions.Count,
                    SkippedCount: errors.Count,
                    Errors: errors,
                    Transactions: dtos
                ));
            });
    }
}
