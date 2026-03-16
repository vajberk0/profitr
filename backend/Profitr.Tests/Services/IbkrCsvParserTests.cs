using Profitr.Api.Services;

namespace Profitr.Tests.Services;

public class IbkrCsvParserTests
{
    private readonly IbkrCsvParser _parser = new();

    private const string SampleCsv = """
        Statement,Header,Field Name,Field Value
        Statement,Data,Title,Transaction History
        Statement,Data,Period,"March 13, 2025 - March 13, 2026"
        Statement,Data,WhenGenerated,"2026-03-16, 09:14:13 EDT"
        Summary,Header,Field Name,Field Value
        Summary,Data,Base Currency,USD
        Summary,Data,Starting Cash,0.0
        Summary,Data,Change,-1189.55935
        Summary,Data,Ending Cash,-1189.55935
        Transaction History,Header,Date,Account,Description,Transaction Type,Symbol,Quantity,Price,Price Currency,Gross Amount ,Commission,Net Amount
        Transaction History,Data,2026-03-10,U***77795,VANG S&P500 USDA,Sell,VUAA,-30.0,131.3,USD,3939.0,-4.0,3935.0
        Transaction History,Data,2026-03-10,U***77795,L&G BATTERY VALUE-CHAIN,Buy,BATT,30.0,31.7268,USD,-951.8,-4.0,-955.8
        Transaction History,Data,2025-12-03,U***77795,USD Credit Interest for Nov-2025,Credit Interest,-,-,-,-,0.06,-,0.06
        Transaction History,Data,2025-10-30,U***77795,Electronic Fund Transfer,Deposit,-,-,-,-,20000.0,-,20000.0
        Transaction History,Data,2025-10-30,U***77795,ISHARES PHYSICAL GOLD ETC,Buy,IGLN,75.0,77.17,USD,-5787.75,-4.0,-5791.75
        """;

    [Fact]
    public void Parse_SkipsSummaryAndStatementRows()
    {
        var rows = _parser.Parse(SampleCsv);
        Assert.DoesNotContain(rows, r => r.Symbol == "Transaction History");
        Assert.DoesNotContain(rows, r => r.Description.Contains("Statement"));
    }

    [Fact]
    public void Parse_SkipsCashAndInterestRows()
    {
        var rows = _parser.Parse(SampleCsv);
        Assert.DoesNotContain(rows, r => r.Symbol == "-");
        Assert.DoesNotContain(rows, r => r.Description.Contains("Credit Interest"));
        Assert.DoesNotContain(rows, r => r.Description.Contains("Deposit"));
    }

    [Fact]
    public void Parse_ExtractsOnlyBuyAndSellRows()
    {
        var rows = _parser.Parse(SampleCsv);
        Assert.All(rows, r => Assert.True(r.TransactionType == "Buy" || r.TransactionType == "Sell"));
    }

    [Fact]
    public void Parse_CorrectRowCount()
    {
        var rows = _parser.Parse(SampleCsv);
        // Should get: VUAA Sell, BATT Buy, IGLN Buy = 3 rows
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void Parse_SellRow_ParsedCorrectly()
    {
        var rows = _parser.Parse(SampleCsv);
        var sell = rows.First(r => r.TransactionType == "Sell");

        Assert.Equal("2026-03-10", sell.Date);
        Assert.Equal("VUAA", sell.Symbol);
        Assert.Equal("VANG S&P500 USDA", sell.Description);
        Assert.Equal("Sell", sell.TransactionType);
        Assert.Equal(30.0m, sell.Quantity);       // absolute value of -30.0
        Assert.Equal(131.3m, sell.Price);
        Assert.Equal("USD", sell.PriceCurrency);
        Assert.Equal(4.0m, sell.Commission);      // absolute value of -4.0
    }

    [Fact]
    public void Parse_BuyRow_ParsedCorrectly()
    {
        var rows = _parser.Parse(SampleCsv);
        var buy = rows.First(r => r.Symbol == "BATT");

        Assert.Equal("2026-03-10", buy.Date);
        Assert.Equal("BATT", buy.Symbol);
        Assert.Equal("L&G BATTERY VALUE-CHAIN", buy.Description);
        Assert.Equal("Buy", buy.TransactionType);
        Assert.Equal(30.0m, buy.Quantity);
        Assert.Equal(31.7268m, buy.Price);
        Assert.Equal("USD", buy.PriceCurrency);
        Assert.Equal(4.0m, buy.Commission);
    }

    [Fact]
    public void Parse_QuantityAlwaysPositive()
    {
        var rows = _parser.Parse(SampleCsv);
        Assert.All(rows, r => Assert.True(r.Quantity > 0));
    }

    [Fact]
    public void Parse_CommissionAlwaysPositive()
    {
        var rows = _parser.Parse(SampleCsv);
        Assert.All(rows, r => Assert.True(r.Commission >= 0));
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsEmptyList()
    {
        var rows = _parser.Parse("");
        Assert.Empty(rows);
    }

    [Fact]
    public void Parse_OnlySummaryRows_ReturnsEmptyList()
    {
        var csv = """
            Statement,Header,Field Name,Field Value
            Statement,Data,Title,Transaction History
            Summary,Data,Base Currency,USD
            """;
        var rows = _parser.Parse(csv);
        Assert.Empty(rows);
    }

    [Fact]
    public void Parse_HandlesQuotedFieldsWithCommas()
    {
        // The Statement rows have quoted fields with commas — ensure parser doesn't break
        var csv = """
            Statement,Data,Period,"March 13, 2025 - March 13, 2026"
            Statement,Data,WhenGenerated,"2026-03-16, 09:14:13 EDT"
            Transaction History,Data,2026-03-10,U***77795,SOME ETF,Buy,TEST,10.0,50.0,USD,-500.0,-1.0,-501.0
            """;
        var rows = _parser.Parse(csv);
        Assert.Single(rows);
        Assert.Equal("TEST", rows[0].Symbol);
    }

    [Fact]
    public void Parse_FullSampleFile_AllTransactionsExtracted()
    {
        // The full sample has these trade rows (excluding Credit Interest and Deposit):
        // VUAA Sell, BATT Buy, BATT Buy, IGLN Buy, EIMI Buy, ISUN Buy,
        // ISUN Buy, ICLN Sell, QQQM Sell, QQQM Sell, QQQM Buy, VUAA Buy,
        // SMH Buy, EIMI Buy, VOO Sell, IEMG Sell, ICLN Buy, IGLN Buy,
        // BATT Buy, IEMG Buy, QQQM Buy, VOO Buy = 22 rows
        var fullCsv = """
            Statement,Header,Field Name,Field Value
            Statement,Data,Title,Transaction History
            Statement,Data,Period,"March 13, 2025 - March 13, 2026"
            Statement,Data,WhenGenerated,"2026-03-16, 09:14:13 EDT"
            Summary,Header,Field Name,Field Value
            Summary,Data,Base Currency,USD
            Summary,Data,Starting Cash,0.0
            Summary,Data,Change,-1189.55935
            Summary,Data,Ending Cash,-1189.55935
            Transaction History,Header,Date,Account,Description,Transaction Type,Symbol,Quantity,Price,Price Currency,Gross Amount ,Commission,Net Amount
            Transaction History,Data,2026-03-10,U***77795,VANG S&P500 USDA,Sell,VUAA,-30.0,131.3,USD,3939.0,-4.0,3935.0
            Transaction History,Data,2026-03-10,U***77795,L&G BATTERY VALUE-CHAIN,Buy,BATT,30.0,31.7268,USD,-951.8,-4.0,-955.8
            Transaction History,Data,2026-03-10,U***77795,L&G BATTERY VALUE-CHAIN,Buy,BATT,35.0,31.7102,USD,-1109.86,-4.0,-1113.86
            Transaction History,Data,2026-03-10,U***77795,ISHARES PHYSICAL GOLD ETC,Buy,IGLN,16.0,100.4945,USD,-1607.91,-4.0,-1611.91
            Transaction History,Data,2026-03-10,U***77795,ISHARES CORE EM IMI ACC,Buy,EIMI,5.0,48.199,USD,-241.0,-4.0,-245.0
            Transaction History,Data,2026-03-10,U***77795,INVESCO SOLAR ENERGY ETF,Buy,ISUN,42.0,28.37,USD,-1191.54,-4.0,-1195.54
            Transaction History,Data,2025-12-03,U***77795,USD Credit Interest for Nov-2025,Credit Interest,-,-,-,-,0.06,-,0.06
            Transaction History,Data,2025-11-05,U***77795,USD Credit Interest for Oct-2025,Credit Interest,-,-,-,-,0.19,-,0.19
            Transaction History,Data,2025-11-03,U***77795,INVESCO SOLAR ENERGY ETF,Buy,ISUN,38.0,25.9,USD,-984.2,-4.0,-988.2
            Transaction History,Data,2025-10-31,U***77795,ISHARES GLOBAL CLEAN ENERGY,Sell,ICLN,-100.0,17.195,USD,1719.5,-1.0188,1718.4812
            Transaction History,Data,2025-10-31,U***77795,INVESCO NASDAQ 100 ETF,Sell,QQQM,-80.0,260.15,USD,20812.0,-1.01504,20810.98496
            Transaction History,Data,2025-10-31,U***77795,INVESCO NASDAQ 100 ETF,Sell,QQQM,-20.0,260.15,USD,5203.0,-0.00376,5202.99624
            Transaction History,Data,2025-10-31,U***77795,INVESCO NASDAQ 100 ETF,Buy,QQQM,80.0,260.19,USD,-20815.2,-1.00176,-20816.20176
            Transaction History,Data,2025-10-31,U***77795,VANG S&P500 USDA,Buy,VUAA,30.0,131.4,USD,-3942.0,-4.0,-3946.0
            Transaction History,Data,2025-10-31,U***77795,VANECK SEMICONDUCTOR ETF,Buy,SMH,80.0,62.58,USD,-5006.4,-4.0,-5010.4
            Transaction History,Data,2025-10-31,U***77795,ISHARES CORE EM IMI ACC,Buy,EIMI,70.0,44.81,USD,-3136.7,-4.0,-3140.7
            Transaction History,Data,2025-10-31,U***77795,VANGUARD S&P 500 ETF,Sell,VOO,-5.0,627.69,USD,3138.45,-1.00094,3137.44906
            Transaction History,Data,2025-10-31,U***77795,ISHARES CORE MSCI EMERGING,Sell,IEMG,-30.0,68.13,USD,2043.9,-1.00564,2042.89436
            Transaction History,Data,2025-10-30,U***77795,ISHARES GLOBAL CLEAN ENERGY,Buy,ICLN,100.0,17.19,USD,-1719.0,-1.0022,-1720.0022
            Transaction History,Data,2025-10-30,U***77795,Electronic Fund Transfer,Deposit,-,-,-,-,20000.0,-,20000.0
            Transaction History,Data,2025-10-30,U***77795,ISHARES PHYSICAL GOLD ETC,Buy,IGLN,75.0,77.17,USD,-5787.75,-4.0,-5791.75
            Transaction History,Data,2025-10-30,U***77795,L&G BATTERY VALUE-CHAIN,Buy,BATT,40.0,27.375,USD,-1095.0,-4.0,-1099.0
            Transaction History,Data,2025-10-30,U***77795,ISHARES CORE MSCI EMERGING,Buy,IEMG,30.0,68.41,USD,-2052.3,-1.00066,-2053.3006600000003
            Transaction History,Data,2025-10-30,U***77795,INVESCO NASDAQ 100 ETF,Buy,QQQM,20.0,260.19,USD,-5203.8,-1.00044,-5204.80044
            Transaction History,Data,2025-10-30,U***77795,VANGUARD S&P 500 ETF,Buy,VOO,5.0,628.83,USD,-3144.15,-1.00011,-3145.15011
            """;

        var rows = _parser.Parse(fullCsv);
        Assert.Equal(22, rows.Count);

        // Verify no Credit Interest or Deposit rows snuck in
        Assert.DoesNotContain(rows, r => r.TransactionType == "Credit Interest");
        Assert.DoesNotContain(rows, r => r.TransactionType == "Deposit");
    }

    [Fact]
    public void Parse_FractionalCommission_ParsedCorrectly()
    {
        var rows = _parser.Parse(SampleCsv);
        // All commissions in the sample are 4.0
        var igln = rows.First(r => r.Symbol == "IGLN");
        Assert.Equal(4.0m, igln.Commission);
    }
}
