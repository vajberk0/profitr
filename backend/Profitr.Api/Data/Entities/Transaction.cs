namespace Profitr.Api.Data.Entities;

public enum TransactionType
{
    Buy,
    Sell
}

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PortfolioId { get; set; }
    public TransactionType Type { get; set; }
    public required string Symbol { get; set; }
    public required string InstrumentName { get; set; }
    public required string AssetType { get; set; }  // EQUITY, ETF, ETC
    public decimal Quantity { get; set; }
    public decimal PricePerUnit { get; set; }        // in native currency
    public required string NativeCurrency { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Portfolio Portfolio { get; set; } = null!;
}
