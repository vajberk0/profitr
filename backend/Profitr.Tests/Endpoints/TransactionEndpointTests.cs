using System.Net;
using System.Net.Http.Json;
using Profitr.Api.Models;

namespace Profitr.Tests.Endpoints;

public class TransactionEndpointTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;
    private const string PortfolioId = "11111111-1111-1111-1111-111111111111";

    public TransactionEndpointTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateBuyTransaction_ReturnsCreated()
    {
        var txn = new
        {
            type = "Buy",
            symbol = "AAPL",
            instrumentName = "Apple Inc.",
            assetType = "EQUITY",
            quantity = 10,
            pricePerUnit = 150.50,
            nativeCurrency = "USD",
            transactionDate = "2024-06-15T00:00:00Z",
            notes = "First purchase"
        };

        var response = await _client.PostAsJsonAsync($"/api/portfolios/{PortfolioId}/transactions", txn);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<TransactionDto>();
        Assert.NotNull(result);
        Assert.Equal("Buy", result.Type);
        Assert.Equal("AAPL", result.Symbol);
        Assert.Equal(10m, result.Quantity);
        Assert.Equal(150.50m, result.PricePerUnit);
    }

    [Fact]
    public async Task ListTransactions_ReturnsAll()
    {
        // Create a buy
        await _client.PostAsJsonAsync($"/api/portfolios/{PortfolioId}/transactions", new
        {
            type = "Buy", symbol = "MSFT", instrumentName = "Microsoft", assetType = "EQUITY",
            quantity = 5, pricePerUnit = 350, nativeCurrency = "USD", transactionDate = "2024-01-01T00:00:00Z"
        });

        var response = await _client.GetAsync($"/api/portfolios/{PortfolioId}/transactions");
        response.EnsureSuccessStatusCode();

        var transactions = await response.Content.ReadFromJsonAsync<TransactionDto[]>();
        Assert.NotNull(transactions);
        Assert.True(transactions.Length >= 1);
    }

    [Fact]
    public async Task SellMoreThanOwned_ReturnsBadRequest()
    {
        // Buy 5, then try to sell 10
        await _client.PostAsJsonAsync($"/api/portfolios/{PortfolioId}/transactions", new
        {
            type = "Buy", symbol = "TSLA", instrumentName = "Tesla", assetType = "EQUITY",
            quantity = 5, pricePerUnit = 200, nativeCurrency = "USD", transactionDate = "2024-01-01T00:00:00Z"
        });

        var sellResponse = await _client.PostAsJsonAsync($"/api/portfolios/{PortfolioId}/transactions", new
        {
            type = "Sell", symbol = "TSLA", instrumentName = "Tesla", assetType = "EQUITY",
            quantity = 10, pricePerUnit = 250, nativeCurrency = "USD", transactionDate = "2024-06-01T00:00:00Z"
        });

        Assert.Equal(HttpStatusCode.BadRequest, sellResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteTransaction_ReturnsNoContent()
    {
        var createResp = await _client.PostAsJsonAsync($"/api/portfolios/{PortfolioId}/transactions", new
        {
            type = "Buy", symbol = "GOOG", instrumentName = "Alphabet", assetType = "EQUITY",
            quantity = 2, pricePerUnit = 140, nativeCurrency = "USD", transactionDate = "2024-01-01T00:00:00Z"
        });
        var created = await createResp.Content.ReadFromJsonAsync<TransactionDto>();

        var response = await _client.DeleteAsync($"/api/transactions/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTransaction_ChangesValues()
    {
        var createResp = await _client.PostAsJsonAsync($"/api/portfolios/{PortfolioId}/transactions", new
        {
            type = "Buy", symbol = "AMZN", instrumentName = "Amazon", assetType = "EQUITY",
            quantity = 3, pricePerUnit = 170, nativeCurrency = "USD", transactionDate = "2024-01-01T00:00:00Z"
        });
        var created = await createResp.Content.ReadFromJsonAsync<TransactionDto>();

        var updateResp = await _client.PutAsJsonAsync($"/api/transactions/{created!.Id}", new
        {
            quantity = 5,
            pricePerUnit = 175,
            transactionDate = "2024-02-01T00:00:00Z",
            notes = "Updated"
        });
        updateResp.EnsureSuccessStatusCode();

        var updated = await updateResp.Content.ReadFromJsonAsync<TransactionDto>();
        Assert.Equal(5m, updated!.Quantity);
        Assert.Equal(175m, updated.PricePerUnit);
        Assert.Equal("Updated", updated.Notes);
    }

    [Fact]
    public async Task NonExistentPortfolio_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync("/api/portfolios/99999999-9999-9999-9999-999999999999/transactions", new
        {
            type = "Buy", symbol = "X", instrumentName = "X", assetType = "EQUITY",
            quantity = 1, pricePerUnit = 1, nativeCurrency = "USD", transactionDate = "2024-01-01T00:00:00Z"
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
