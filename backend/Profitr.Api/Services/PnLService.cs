using Profitr.Api.Data.Entities;
using Profitr.Api.Models;

namespace Profitr.Api.Services;

public class PnLService(YahooFinanceService yahoo, FxService fx)
{
    public async Task<PortfolioSummaryDto> CalculatePortfolioSummaryAsync(Portfolio portfolio, string displayCurrency)
    {
        var transactions = portfolio.Transactions.OrderBy(t => t.TransactionDate).ToList();
        var dividends = portfolio.Dividends.ToList();

        // Group transactions by symbol to compute positions
        var symbolGroups = transactions.GroupBy(t => t.Symbol);
        var positions = new List<PositionDto>();

        // Fetch all quotes in parallel
        var symbols = transactions.Select(t => t.Symbol).Distinct().ToList();
        var quotes = await yahoo.GetQuotesAsync(symbols);

        foreach (var group in symbolGroups)
        {
            var symbol = group.Key;
            var txns = group.OrderBy(t => t.TransactionDate).ToList();
            var first = txns.First();

            var position = await CalculatePositionAsync(symbol, first.InstrumentName, first.AssetType,
                first.NativeCurrency, txns, dividends.Where(d => d.Symbol == symbol).ToList(),
                quotes.GetValueOrDefault(symbol), displayCurrency);

            if (position != null)
                positions.Add(position);
        }

        var totalValueDisplay = positions.Sum(p => p.CurrentValueDisplay);
        var totalCostDisplay = positions.Sum(p => p.TotalInvestedDisplay);
        var totalPnLDisplay = positions.Sum(p => p.PnLDisplay);
        var totalPnLPercent = totalCostDisplay != 0 ? (totalPnLDisplay / totalCostDisplay) * 100 : 0;
        var totalDividendsDisplay = positions.Sum(p => p.TotalDividendsDisplay);

        return new PortfolioSummaryDto(
            portfolio.Id,
            portfolio.Name,
            portfolio.IsDefault,
            displayCurrency,
            totalValueDisplay,
            totalCostDisplay,
            totalPnLDisplay,
            totalPnLPercent,
            totalDividendsDisplay,
            positions
        );
    }

    private async Task<PositionDto?> CalculatePositionAsync(
        string symbol, string instrumentName, string assetType, string nativeCurrency,
        List<Transaction> transactions, List<Dividend> dividends,
        QuoteResult? currentQuote, string displayCurrency)
    {
        // Compute net quantity and weighted average cost using FIFO-like averaging
        decimal netQty = 0;
        decimal totalCost = 0; // in native currency
        decimal totalReturned = 0; // from sells, in native currency

        foreach (var txn in transactions)
        {
            if (txn.Type == TransactionType.Buy)
            {
                totalCost += txn.Quantity * txn.PricePerUnit;
                netQty += txn.Quantity;
            }
            else // Sell
            {
                totalReturned += txn.Quantity * txn.PricePerUnit;
                netQty -= txn.Quantity;
            }
        }

        // Skip fully closed positions with 0 quantity
        if (netQty <= 0 && currentQuote == null) return null;
        if (netQty < 0) netQty = 0; // shouldn't happen, but safety

        var avgCost = netQty > 0 ? (totalCost - totalReturned) / netQty : 0;
        if (avgCost < 0) avgCost = 0;
        var totalInvested = netQty * avgCost;

        var currentPrice = currentQuote?.Price ?? 0;
        var currentValue = netQty * currentPrice;
        var pnl = currentValue - totalInvested;
        var pnlPercent = totalInvested != 0 ? (pnl / totalInvested) * 100 : 0;

        // Calculate dividends in native currency
        var totalDivNative = dividends.Sum(d => d.AmountPerShare * netQty);

        // Convert to display currency
        var currentFxRate = await fx.GetLatestRateAsync(nativeCurrency, displayCurrency);

        // For cost basis, use a weighted average of historical FX rates
        // Simplified: use current FX rate for display (more accurate would be per-transaction FX)
        decimal totalInvestedDisplay = 0;
        foreach (var txn in transactions.Where(t => t.Type == TransactionType.Buy))
        {
            var txnFx = await fx.GetHistoricalRateAsync(nativeCurrency, displayCurrency, txn.TransactionDate);
            totalInvestedDisplay += txn.Quantity * txn.PricePerUnit * txnFx;
        }
        foreach (var txn in transactions.Where(t => t.Type == TransactionType.Sell))
        {
            var txnFx = await fx.GetHistoricalRateAsync(nativeCurrency, displayCurrency, txn.TransactionDate);
            totalInvestedDisplay -= txn.Quantity * txn.PricePerUnit * txnFx;
        }
        if (totalInvestedDisplay < 0) totalInvestedDisplay = 0;

        var currentValueDisplay = currentValue * currentFxRate;
        var pnlDisplay = currentValueDisplay - totalInvestedDisplay;
        var pnlPercentDisplay = totalInvestedDisplay != 0 ? (pnlDisplay / totalInvestedDisplay) * 100 : 0;
        var totalDivDisplay = totalDivNative * currentFxRate;

        return new PositionDto(
            symbol, instrumentName, assetType, nativeCurrency,
            netQty, avgCost, totalInvested,
            currentPrice, currentValue, pnl, pnlPercent, totalDivNative,
            totalInvestedDisplay, currentValueDisplay, pnlDisplay, pnlPercentDisplay, totalDivDisplay
        );
    }

    /// <summary>
    /// Computes portfolio value over time for charting.
    /// Returns a list of (date, value in display currency) points.
    /// </summary>
    public async Task<List<ChartDataPoint>> ComputePortfolioHistoryAsync(
        Portfolio portfolio, string displayCurrency, string range = "1y")
    {
        var transactions = portfolio.Transactions.OrderBy(t => t.TransactionDate).ToList();
        if (transactions.Count == 0) return [];

        // Determine date range
        var startDate = range switch
        {
            "1w" => DateTime.UtcNow.AddDays(-7),
            "1m" => DateTime.UtcNow.AddMonths(-1),
            "3m" => DateTime.UtcNow.AddMonths(-3),
            "6m" => DateTime.UtcNow.AddMonths(-6),
            "1y" => DateTime.UtcNow.AddYears(-1),
            "all" => transactions.First().TransactionDate,
            _ => DateTime.UtcNow.AddYears(-1)
        };

        // Earliest is the first transaction
        if (startDate < transactions.First().TransactionDate)
            startDate = transactions.First().TransactionDate;

        var symbols = transactions.Select(t => t.Symbol).Distinct().ToList();

        // Get historical charts for each symbol
        var chartRange = range switch
        {
            "1w" => "5d",
            "1m" => "1mo",
            "3m" => "3mo",
            "6m" => "6mo",
            "1y" => "1y",
            "all" => "max",
            _ => "1y"
        };

        var charts = new Dictionary<string, ChartResult>();
        foreach (var symbol in symbols)
        {
            var chart = await yahoo.GetChartAsync(symbol, chartRange);
            if (chart != null) charts[symbol] = chart;
        }

        // Build daily portfolio values
        var points = new List<ChartDataPoint>();
        var endDate = DateTime.UtcNow.Date;

        // Get FX rates needed
        var currencyPairs = transactions.Select(t => t.NativeCurrency).Distinct()
            .Where(c => !c.Equals(displayCurrency, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var fxRates = new Dictionary<string, Dictionary<string, decimal>>();
        foreach (var curr in currencyPairs)
        {
            fxRates[curr] = await fx.GetRateRangeAsync(curr, displayCurrency, startDate, endDate);
        }

        // For each day in range, compute portfolio value
        for (var date = startDate.Date; date <= endDate; date = date.AddDays(1))
        {
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) continue;

            var dateStr = date.ToString("yyyy-MM-dd");
            decimal totalValue = 0;

            // Compute holdings as of this date
            var holdingsAtDate = transactions
                .Where(t => t.TransactionDate.Date <= date)
                .GroupBy(t => t.Symbol)
                .Select(g => new
                {
                    Symbol = g.Key,
                    NativeCurrency = g.First().NativeCurrency,
                    Quantity = g.Sum(t => t.Type == TransactionType.Buy ? t.Quantity : -t.Quantity)
                })
                .Where(h => h.Quantity > 0);

            foreach (var holding in holdingsAtDate)
            {
                // Find price on this date from chart data
                decimal price = 0;
                if (charts.TryGetValue(holding.Symbol, out var chart))
                {
                    var point = chart.Points.LastOrDefault(p => p.Date.Date <= date);
                    if (point != null) price = point.Close;
                }

                var valueNative = holding.Quantity * price;

                // Convert to display currency
                decimal fxRate = 1m;
                if (!holding.NativeCurrency.Equals(displayCurrency, StringComparison.OrdinalIgnoreCase))
                {
                    if (fxRates.TryGetValue(holding.NativeCurrency, out var rates))
                    {
                        // Find closest date
                        var closestDate = rates.Keys.Where(d => string.Compare(d, dateStr) <= 0).OrderByDescending(d => d).FirstOrDefault();
                        if (closestDate != null) fxRate = rates[closestDate];
                    }
                }

                totalValue += valueNative * fxRate;
            }

            if (totalValue > 0)
                points.Add(new ChartDataPoint(date, totalValue));
        }

        return points;
    }
}

public record ChartDataPoint(DateTime Date, decimal Value);
