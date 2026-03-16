namespace Profitr.Api.Models;

public record CreateDividendRequest(
    string Symbol,
    decimal AmountPerShare,
    string NativeCurrency,
    DateTime ExDate,
    DateTime PayDate,
    string? Notes
);

public record DividendDto(
    Guid Id,
    string Symbol,
    decimal AmountPerShare,
    string NativeCurrency,
    DateTime ExDate,
    DateTime PayDate,
    string? Notes,
    DateTime CreatedAt
);
