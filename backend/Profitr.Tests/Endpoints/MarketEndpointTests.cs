using System.Net;
using System.Net.Http.Json;
using Profitr.Api.Models;

namespace Profitr.Tests.Endpoints;

public class MarketEndpointTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;

    public MarketEndpointTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Search_ReturnsResults()
    {
        var response = await _client.GetAsync("/api/market/search?q=AAPL");
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadFromJsonAsync<TickerSearchResult[]>();
        Assert.NotNull(results);
        Assert.True(results.Length > 0);
        Assert.Contains(results, r => r.Symbol == "AAPL");
    }

    [Fact]
    public async Task Search_EmptyQuery_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/market/search?q=");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Quote_ReturnsPrice()
    {
        var response = await _client.GetAsync("/api/market/quote?symbols=AAPL");
        response.EnsureSuccessStatusCode();

        var quotes = await response.Content.ReadFromJsonAsync<Dictionary<string, QuoteResult>>();
        Assert.NotNull(quotes);
        Assert.True(quotes.ContainsKey("AAPL"));
        Assert.True(quotes["AAPL"].Price > 0);
        Assert.Equal("USD", quotes["AAPL"].Currency);
    }

    [Fact]
    public async Task Chart_ReturnsData()
    {
        var response = await _client.GetAsync("/api/market/chart/AAPL?range=1mo");
        response.EnsureSuccessStatusCode();

        var chart = await response.Content.ReadFromJsonAsync<ChartResult>();
        Assert.NotNull(chart);
        Assert.Equal("AAPL", chart.Symbol);
        Assert.True(chart.Points.Count > 0);
    }

    [Fact]
    public async Task FxCurrencies_Returns30()
    {
        var response = await _client.GetAsync("/api/fx/currencies");
        response.EnsureSuccessStatusCode();

        var currencies = await response.Content.ReadFromJsonAsync<CurrencyInfo[]>();
        Assert.NotNull(currencies);
        Assert.Equal(30, currencies.Length);
    }

    [Fact]
    public async Task FxLatest_ReturnsRate()
    {
        var response = await _client.GetAsync("/api/fx/latest?from=USD&to=EUR");
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.NotNull(data);
        Assert.True(data.ContainsKey("rate"));
    }
}
