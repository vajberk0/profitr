namespace Profitr.Api.Models;

public record CreateCashTransactionRequest(
    string Type,            // "Deposit" or "Withdrawal"
    decimal Amount,
    string Currency,
    DateTime TransactionDate,
    string? Notes
);

public record CashTransactionDto(
    Guid Id,
    string Type,
    decimal Amount,
    string Currency,
    DateTime TransactionDate,
    string? Notes,
    DateTime CreatedAt
);
