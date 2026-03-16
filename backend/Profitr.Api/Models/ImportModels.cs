namespace Profitr.Api.Models;

public record ImportPreviewRow(
    int RowIndex,
    string Date,
    string Symbol,
    string InstrumentName,
    string AssetType,
    string TransactionType,
    decimal Quantity,
    decimal PricePerUnit,
    string NativeCurrency,
    decimal Commission,
    bool IsValid,
    string? Error
);

public record SymbolMapping(
    string CsvSymbol,
    bool Resolved,
    string? YahooSymbol,
    string? InstrumentName,
    string? AssetType,
    List<TickerSearchResult> Suggestions
);

public record ImportPreviewResponse(
    List<ImportPreviewRow> Rows,
    int TotalRows,
    int ValidRows,
    int SkippedRows,
    Dictionary<string, SymbolMapping> SymbolMappings
);

public record ImportConfirmRow(
    string Date,
    string Symbol,
    string InstrumentName,
    string AssetType,
    string TransactionType,
    decimal Quantity,
    decimal PricePerUnit,
    string NativeCurrency,
    string? Notes
);

public record ImportConfirmRequest(
    List<ImportConfirmRow> Rows
);

public record ImportResultResponse(
    int ImportedCount,
    int SkippedCount,
    List<string> Errors,
    List<TransactionDto> Transactions
);
