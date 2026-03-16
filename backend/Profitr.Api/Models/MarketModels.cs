namespace Profitr.Api.Models;

public record TickerSearchResult(
    string Symbol,
    string Name,
    string Type,        // EQUITY, ETF, ETC
    string Exchange,
    string ExchangeDisplay
);

public record QuoteResult(
    string Symbol,
    string Name,
    decimal Price,
    decimal Change,
    decimal ChangePercent,
    string Currency,
    string Exchange,
    string AssetType
);

public record ChartPoint(
    DateTime Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume
);

public record ChartResult(
    string Symbol,
    string Currency,
    List<ChartPoint> Points
);

public record HistoryPriceResult(
    string Symbol,
    DateTime Date,
    decimal ClosePrice,
    string Currency
);
