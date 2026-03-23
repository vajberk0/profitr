namespace Profitr.Api.Data.Entities;

public enum CashTransactionType
{
    Deposit,
    Withdrawal
}

public class CashTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PortfolioId { get; set; }
    public CashTransactionType Type { get; set; }
    public decimal Amount { get; set; }              // always positive
    public required string Currency { get; set; }    // e.g. "EUR", "USD"
    public DateTime TransactionDate { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Portfolio Portfolio { get; set; } = null!;
}
