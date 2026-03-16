namespace Profitr.Api.Data.Entities;

public class Dividend
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PortfolioId { get; set; }
    public required string Symbol { get; set; }
    public decimal AmountPerShare { get; set; }
    public required string NativeCurrency { get; set; }
    public DateTime ExDate { get; set; }
    public DateTime PayDate { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Portfolio Portfolio { get; set; } = null!;
}
