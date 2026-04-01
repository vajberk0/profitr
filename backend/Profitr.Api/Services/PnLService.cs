using Profitr.Api.Data.Entities;
using Profitr.Api.Models;

namespace Profitr.Api.Services;

public class PnLService(YahooFinanceService yahoo, FxService fx)
{
    public async Task<PortfolioSummaryDto> CalculatePortfolioSummaryAsync(Portfolio portfolio, string displayCurrency)
    {
        var transactions = portfolio.Transactions.OrderBy(t => t.TransactionDate).ToList();
        var dividends = portfolio.Dividends.ToList();
        var cashTransactions = portfolio.CashTransactions.OrderBy(c => c.TransactionDate).ToList();

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

        // Compute cash balance in display currency
        var cashBalance = await CalculateCashBalanceAsync(cashTransactions, transactions, dividends, positions, displayCurrency);

        var totalValueDisplay = positions.Sum(p => p.CurrentValueDisplay) + cashBalance;
        var totalCostDisplay = positions.Sum(p => p.TotalInvestedDisplay);
        var totalPnLDisplay = positions.Sum(p => p.PnLDisplay);
        var totalPnLPercent = totalCostDisplay != 0 ? (totalPnLDisplay / totalCostDisplay) * 100 : 0;
        var totalDividendsDisplay = positions.Sum(p => p.TotalDividendsDisplay);

        // Compute TWRR and annualized return
        decimal? twrrPercent = null;
        decimal? annualizedReturnPercent = null;
        try
        {
            var twrrResult = await CalculateTwrrAsync(portfolio, displayCurrency, transactions, cashTransactions);
            twrrPercent = twrrResult.Twrr;
            annualizedReturnPercent = twrrResult.Annualized;
        }
        catch
        {
            twrrPercent = null;
            annualizedReturnPercent = null;
        }

        // Fallback: if TWRR couldn't compute annualized, use simple annualization
        if (annualizedReturnPercent == null && transactions.Count > 0 && totalCostDisplay != 0)
        {
            var firstDate = transactions.Min(t => t.TransactionDate);
            if (cashTransactions.Count > 0)
            {
                var firstCash = cashTransactions.Min(c => c.TransactionDate);
                if (firstCash < firstDate) firstDate = firstCash;
            }
            var totalDaysSimple = (DateTime.UtcNow - firstDate).TotalDays;
            if (totalDaysSimple >= 30)
            {
                var totalReturnDecimal = totalPnLDisplay / totalCostDisplay;
                if (totalReturnDecimal > -1)
                    annualizedReturnPercent = ((decimal)Math.Pow((double)(1 + totalReturnDecimal), 365.0 / totalDaysSimple) - 1) * 100;
            }
        }

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
            cashBalance,
            positions,
            twrrPercent,
            annualizedReturnPercent
        );
    }

    /// <summary>
    /// Computes cash balance in display currency using Option A (implicit):
    ///   + deposits  - withdrawals  - buys  + sells  + dividends
    /// Each amount is FX-converted to displayCurrency at the historical rate on the transaction date.
    /// </summary>
    private async Task<decimal> CalculateCashBalanceAsync(
        List<CashTransaction> cashTransactions,
        List<Transaction> transactions,
        List<Dividend> dividends,
        List<PositionDto> positions,
        string displayCurrency)
    {
        decimal cash = 0;

        // Deposits and withdrawals
        foreach (var ct in cashTransactions)
        {
            var rate = await fx.GetHistoricalRateAsync(ct.Currency, displayCurrency, ct.TransactionDate);
            var amount = ct.Amount * rate;
            cash += ct.Type == CashTransactionType.Deposit ? amount : -amount;
        }

        // Buys deduct cash, sells add cash (converted from native currency)
        foreach (var txn in transactions)
        {
            var rate = await fx.GetHistoricalRateAsync(txn.NativeCurrency, displayCurrency, txn.TransactionDate);
            var amount = txn.Quantity * txn.PricePerUnit * rate;
            cash += txn.Type == TransactionType.Buy ? -amount : amount;
        }

        // Dividends add cash (amountPerShare × quantity held at ex-date, in native currency)
        foreach (var div in dividends)
        {
            // Find quantity held at ex-date for this symbol
            var qtyAtExDate = transactions
                .Where(t => t.Symbol == div.Symbol && t.TransactionDate.Date <= div.ExDate.Date)
                .Sum(t => t.Type == TransactionType.Buy ? t.Quantity : -t.Quantity);
            if (qtyAtExDate <= 0) continue;

            var rate = await fx.GetHistoricalRateAsync(div.NativeCurrency, displayCurrency, div.PayDate);
            cash += div.AmountPerShare * qtyAtExDate * rate;
        }

        return cash;
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
        var cashTransactions = portfolio.CashTransactions.OrderBy(c => c.TransactionDate).ToList();
        var dividends = portfolio.Dividends.OrderBy(d => d.PayDate).ToList();

        if (transactions.Count == 0 && cashTransactions.Count == 0) return [];

        // Determine date range
        var startDate = range switch
        {
            "1w" => DateTime.UtcNow.AddDays(-7),
            "1m" => DateTime.UtcNow.AddMonths(-1),
            "3m" => DateTime.UtcNow.AddMonths(-3),
            "6m" => DateTime.UtcNow.AddMonths(-6),
            "1y" => DateTime.UtcNow.AddYears(-1),
            "all" => GetEarliestDate(transactions, cashTransactions),
            _ => DateTime.UtcNow.AddYears(-1)
        };

        // Earliest is the first activity
        var earliest = GetEarliestDate(transactions, cashTransactions);
        if (startDate < earliest)
            startDate = earliest;

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

        // Also get FX rates for cash transaction currencies
        var cashCurrencies = cashTransactions.Select(c => c.Currency).Distinct()
            .Where(c => !c.Equals(displayCurrency, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var curr in cashCurrencies.Where(c => !fxRates.ContainsKey(c)))
        {
            fxRates[curr] = await fx.GetRateRangeAsync(curr, displayCurrency, startDate, endDate);
        }
        // And for dividend currencies
        var divCurrencies = dividends.Select(d => d.NativeCurrency).Distinct()
            .Where(c => !c.Equals(displayCurrency, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var curr in divCurrencies.Where(c => !fxRates.ContainsKey(c)))
        {
            fxRates[curr] = await fx.GetRateRangeAsync(curr, displayCurrency, startDate, endDate);
        }

        // Helper to find FX rate for a currency on a given date
        decimal GetFxRateForDate(string currency, string dateStr)
        {
            if (currency.Equals(displayCurrency, StringComparison.OrdinalIgnoreCase)) return 1m;
            if (!fxRates.TryGetValue(currency, out var rates)) return 1m;
            var closestDate = rates.Keys.Where(d => string.Compare(d, dateStr) <= 0)
                .OrderByDescending(d => d).FirstOrDefault();
            return closestDate != null ? rates[closestDate] : 1m;
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
                // Fall back to first available point when no earlier data exists
                // (handles holidays/gaps at the start of the chart range)
                decimal price = 0;
                if (charts.TryGetValue(holding.Symbol, out var chart))
                {
                    var point = chart.Points.LastOrDefault(p => p.Date.Date <= date)
                                ?? chart.Points.FirstOrDefault();
                    if (point != null) price = point.Close;
                }

                var valueNative = holding.Quantity * price;
                totalValue += valueNative * GetFxRateForDate(holding.NativeCurrency, dateStr);
            }

            // Compute cash balance as of this date (deposits - withdrawals - buys + sells + dividends)
            decimal cashAtDate = 0;

            foreach (var ct in cashTransactions.Where(c => c.TransactionDate.Date <= date))
            {
                var amount = ct.Amount * GetFxRateForDate(ct.Currency, ct.TransactionDate.ToString("yyyy-MM-dd"));
                cashAtDate += ct.Type == CashTransactionType.Deposit ? amount : -amount;
            }

            foreach (var txn in transactions.Where(t => t.TransactionDate.Date <= date))
            {
                var amount = txn.Quantity * txn.PricePerUnit * GetFxRateForDate(txn.NativeCurrency, txn.TransactionDate.ToString("yyyy-MM-dd"));
                cashAtDate += txn.Type == TransactionType.Buy ? -amount : amount;
            }

            foreach (var div in dividends.Where(d => d.PayDate.Date <= date))
            {
                var qtyAtExDate = transactions
                    .Where(t => t.Symbol == div.Symbol && t.TransactionDate.Date <= div.ExDate.Date)
                    .Sum(t => t.Type == TransactionType.Buy ? t.Quantity : -t.Quantity);
                if (qtyAtExDate > 0)
                {
                    cashAtDate += div.AmountPerShare * qtyAtExDate * GetFxRateForDate(div.NativeCurrency, div.PayDate.ToString("yyyy-MM-dd"));
                }
            }

            totalValue += cashAtDate;

            if (totalValue != 0)
                points.Add(new ChartDataPoint(date, totalValue));
        }

        return points;
    }

    /// <summary>
    /// Computes Time-Weighted Rate of Return (TWRR) using the daily valuation method.
    /// TWRR isolates investment performance from the timing of cash flows (deposits/withdrawals).
    /// </summary>
    private async Task<(decimal? Twrr, decimal? Annualized)> CalculateTwrrAsync(
        Portfolio portfolio, string displayCurrency,
        List<Transaction> transactions, List<CashTransaction> cashTransactions)
    {
        var history = await ComputePortfolioHistoryAsync(portfolio, displayCurrency, "all");
        if (history.Count < 2)
            return (null, null);

        // Build a map of external cash flows (deposits/withdrawals) by date in display currency
        var externalFlows = new Dictionary<string, decimal>();
        foreach (var ct in cashTransactions)
        {
            var rate = ct.Currency.Equals(displayCurrency, StringComparison.OrdinalIgnoreCase)
                ? 1m
                : await fx.GetHistoricalRateAsync(ct.Currency, displayCurrency, ct.TransactionDate);
            var amount = ct.Amount * rate;
            var dateStr = ct.TransactionDate.ToString("yyyy-MM-dd");
            if (!externalFlows.ContainsKey(dateStr))
                externalFlows[dateStr] = 0;
            externalFlows[dateStr] += ct.Type == CashTransactionType.Deposit ? amount : -amount;
        }

        // Compute TWRR using daily linked returns with cash flow adjustment
        decimal product = 1m;
        for (int i = 1; i < history.Count; i++)
        {
            var prevValue = history[i - 1].Value;
            var currValue = history[i].Value;
            var currDateStr = history[i].Date.ToString("yyyy-MM-dd");

            // External cash flow on the current day
            externalFlows.TryGetValue(currDateStr, out var cf);

            // Adjusted denominator: previous value + cash flow that arrived today
            var denominator = prevValue + cf;

            if (denominator > 0 && currValue >= 0)
            {
                product *= currValue / denominator;
            }
            // Skip days where denominator <= 0 (e.g. full withdrawal)
        }

        var twrr = (product - 1m) * 100m;

        // Annualize the TWRR
        var totalDays = (history[^1].Date - history[0].Date).TotalDays;
        decimal? annualized = null;
        if (totalDays >= 30 && product > 0)
        {
            annualized = ((decimal)Math.Pow((double)product, 365.0 / totalDays) - 1m) * 100m;
        }

        return (twrr, annualized);
    }

    private static DateTime GetEarliestDate(List<Transaction> transactions, List<CashTransaction> cashTransactions)
    {
        var dates = new List<DateTime>();
        if (transactions.Count > 0) dates.Add(transactions.First().TransactionDate);
        if (cashTransactions.Count > 0) dates.Add(cashTransactions.First().TransactionDate);
        return dates.Count > 0 ? dates.Min() : DateTime.UtcNow;
    }
}

public record ChartDataPoint(DateTime Date, decimal Value);
