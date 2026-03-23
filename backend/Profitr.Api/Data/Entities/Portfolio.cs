namespace Profitr.Api.Data.Entities;

public class Portfolio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UserId { get; set; }
    public string Name { get; set; } = "My Portfolio";
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public List<Transaction> Transactions { get; set; } = [];
    public List<Dividend> Dividends { get; set; } = [];
    public List<CashTransaction> CashTransactions { get; set; } = [];
}
