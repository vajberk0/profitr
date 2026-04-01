namespace Profitr.Api.Models;

public record CreatePortfolioRequest(string Name);
public record UpdatePortfolioRequest(string Name);

public record PortfolioDto(
    Guid Id,
    string Name,
    bool IsDefault,
    DateTime CreatedAt,
    int PositionCount
);

public record PortfolioSummaryDto(
    Guid Id,
    string Name,
    bool IsDefault,
    string DisplayCurrency,
    decimal TotalValue,
    decimal TotalCostBasis,
    decimal TotalPnL,
    decimal TotalPnLPercent,
    decimal TotalDividends,
    decimal CashBalance,
    List<PositionDto> Positions,
    decimal? TwrrPercent = null,
    decimal? AnnualizedReturnPercent = null
);

public record PositionDto(
    string Symbol,
    string InstrumentName,
    string AssetType,
    string NativeCurrency,
    decimal Quantity,
    decimal AverageCostBasis,
    decimal TotalInvested,
    decimal CurrentPrice,
    decimal CurrentValue,
    decimal PnL,
    decimal PnLPercent,
    decimal TotalDividends,
    // All values below are in display currency
    decimal TotalInvestedDisplay,
    decimal CurrentValueDisplay,
    decimal PnLDisplay,
    decimal PnLPercentDisplay,
    decimal TotalDividendsDisplay
);
