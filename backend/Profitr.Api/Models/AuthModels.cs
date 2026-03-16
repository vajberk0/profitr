namespace Profitr.Api.Models;

public record UserInfo(
    string Id,
    string Email,
    string? Name,
    string? AvatarUrl,
    string DisplayCurrency
);
