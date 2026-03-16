namespace Profitr.Api.Data.Entities;

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..20];
    public required string Email { get; set; }
    public string? Name { get; set; }
    public string? AvatarUrl { get; set; }
    public string DisplayCurrency { get; set; } = "EUR";
    public required string GoogleSubjectId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Portfolio> Portfolios { get; set; } = [];
}
