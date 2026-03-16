namespace Profitr.Api.Models;

public record CreateTransactionRequest(
    string Type,            // "Buy" or "Sell"
    string Symbol,
    string InstrumentName,
    string AssetType,       // EQUITY, ETF, ETC
    decimal Quantity,
    decimal PricePerUnit,
    string NativeCurrency,
    DateTime TransactionDate,
    string? Notes
);

public record UpdateTransactionRequest(
    decimal Quantity,
    decimal PricePerUnit,
    DateTime TransactionDate,
    string? Notes
);

public record TransactionDto(
    Guid Id,
    string Type,
    string Symbol,
    string InstrumentName,
    string AssetType,
    decimal Quantity,
    decimal PricePerUnit,
    string NativeCurrency,
    DateTime TransactionDate,
    string? Notes,
    DateTime CreatedAt
);
