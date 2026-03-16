namespace Profitr.Api.Models;

public record FxRateResult(
    string From,
    string To,
    decimal Rate,
    string Date
);

public record CurrencyInfo(
    string Code,
    string Name
);
