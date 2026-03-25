namespace Profitr.Api.Data.Entities;

public class CachedFxRate
{
    public required string BaseCurrency { get; set; }
    public required string QuoteCurrency { get; set; }
    public DateTime RateDate { get; set; }
    public decimal Rate { get; set; }
}
