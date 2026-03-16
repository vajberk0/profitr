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

    public List<ParsedRow> Parse(string csvContent)
    {
        var rows = new List<ParsedRow>();
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var fields = ParseCsvLine(line.TrimEnd('\r'));

            // Transaction data rows have at least 13 fields
            if (fields.Count < 13) continue;

            // Only "Transaction History" data rows
            if (fields[0] != "Transaction History" || fields[1] != "Data") continue;

            // Skip rows without a symbol (cash, interest, deposits)
            var symbol = fields[6].Trim();
            if (symbol == "-" || string.IsNullOrWhiteSpace(symbol)) continue;

            // Only Buy and Sell
            var txnType = fields[5].Trim();
            if (txnType != "Buy" && txnType != "Sell") continue;

            if (!decimal.TryParse(fields[7].Trim(), out var quantity)) continue;
            if (!decimal.TryParse(fields[8].Trim(), out var price)) continue;

            var commissionStr = fields[11].Trim();
            var commission = commissionStr != "-" && decimal.TryParse(commissionStr, out var c) ? c : 0m;

            rows.Add(new ParsedRow(
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

        return rows;
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
