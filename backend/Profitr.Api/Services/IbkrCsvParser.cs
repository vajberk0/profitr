namespace Profitr.Api.Services;

public class IbkrCsvParser
{
    public record ParsedRow(
        string Date,
        string Description,
        string TransactionType,
        string Symbol,
        decimal Quantity,
        decimal Price,
        string PriceCurrency,
        decimal Commission
    );

    public record ParsedCashRow(
        string Date,
        string Description,
        string CashType,       // "Deposit" or "Withdrawal"
        decimal Amount,         // always positive
        string Currency
    );

    public record ParseResult(
        List<ParsedRow> Transactions,
        List<ParsedCashRow> CashTransactions,
        string? BaseCurrency
    );

    /// <summary>
    /// Legacy method — returns only trade rows for backward compatibility.
    /// </summary>
    public List<ParsedRow> Parse(string csvContent) => ParseAll(csvContent).Transactions;

    /// <summary>
    /// Full parse — returns trades, cash deposits/withdrawals, and the base currency.
    /// </summary>
    public ParseResult ParseAll(string csvContent)
    {
        var trades = new List<ParsedRow>();
        var cashRows = new List<ParsedCashRow>();
        string? baseCurrency = null;

        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var fields = ParseCsvLine(line.TrimEnd('\r'));

            // Extract base currency from Summary section
            if (fields.Count >= 4 && fields[0] == "Summary" && fields[1] == "Data"
                && fields[2].Trim() == "Base Currency")
            {
                baseCurrency = fields[3].Trim();
                continue;
            }

            // Transaction data rows have at least 13 fields
            if (fields.Count < 13) continue;

            // Only "Transaction History" data rows
            if (fields[0] != "Transaction History" || fields[1] != "Data") continue;

            var txnType = fields[5].Trim();

            // Handle Deposit / Withdrawal rows (symbol is "-" or empty)
            if (txnType == "Deposit" || txnType == "Withdrawal")
            {
                // Amount is in the Net Amount field (index 12), or Gross Amount (index 10)
                var amountStr = fields[12].Trim();
                if (amountStr == "-" || !decimal.TryParse(amountStr, out var amount))
                {
                    amountStr = fields[10].Trim();
                    if (amountStr == "-" || !decimal.TryParse(amountStr, out amount))
                        continue;
                }

                cashRows.Add(new ParsedCashRow(
                    Date: fields[2].Trim(),
                    Description: fields[4].Trim(),
                    CashType: txnType,
                    Amount: Math.Abs(amount),
                    Currency: baseCurrency ?? "USD"
                ));
                continue;
            }

            // Skip rows without a symbol (interest, fees, etc.)
            var symbol = fields[6].Trim();
            if (symbol == "-" || string.IsNullOrWhiteSpace(symbol)) continue;

            // Only Buy and Sell
            if (txnType != "Buy" && txnType != "Sell") continue;

            if (!decimal.TryParse(fields[7].Trim(), out var quantity)) continue;
            if (!decimal.TryParse(fields[8].Trim(), out var price)) continue;

            var commissionStr = fields[11].Trim();
            var commission = commissionStr != "-" && decimal.TryParse(commissionStr, out var c) ? c : 0m;

            trades.Add(new ParsedRow(
                Date: fields[2].Trim(),
                Description: fields[4].Trim(),
                TransactionType: txnType,
                Symbol: symbol,
                Quantity: Math.Abs(quantity),
                Price: Math.Abs(price),
                PriceCurrency: fields[9].Trim(),
                Commission: Math.Abs(commission)
            ));
        }

        return new ParseResult(trades, cashRows, baseCurrency);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = "";
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            if (inQuotes)
            {
                if (line[i] == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current += '"';
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current += line[i];
                }
            }
            else
            {
                if (line[i] == '"')
                {
                    inQuotes = true;
                }
                else if (line[i] == ',')
                {
                    fields.Add(current);
                    current = "";
                }
                else
                {
                    current += line[i];
                }
            }
        }

        fields.Add(current);
        return fields;
    }
}
