using System.Net;
using System.Net.Http.Json;
using Profitr.Api.Models;

namespace Profitr.Tests.Endpoints;

public class PortfolioEndpointTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;

    public PortfolioEndpointTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListPortfolios_ReturnsSeededPortfolio()
    {
        var response = await _client.GetAsync("/api/portfolios");
        response.EnsureSuccessStatusCode();

        var portfolios = await response.Content.ReadFromJsonAsync<PortfolioDto[]>();
        Assert.NotNull(portfolios);
        Assert.Single(portfolios);
        Assert.Equal("Test Portfolio", portfolios[0].Name);
        Assert.True(portfolios[0].IsDefault);
    }

    [Fact]
    public async Task CreatePortfolio_ReturnsCreated()
    {
        var response = await _client.PostAsJsonAsync("/api/portfolios", new { name = "New Portfolio" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var portfolio = await response.Content.ReadFromJsonAsync<PortfolioDto>();
        Assert.NotNull(portfolio);
        Assert.Equal("New Portfolio", portfolio.Name);
        Assert.False(portfolio.IsDefault);
    }

    [Fact]
    public async Task UpdatePortfolio_ChangesName()
    {
        // Create one first
        var createResp = await _client.PostAsJsonAsync("/api/portfolios", new { name = "Original" });
        var created = await createResp.Content.ReadFromJsonAsync<PortfolioDto>();

        var response = await _client.PutAsJsonAsync($"/api/portfolios/{created!.Id}", new { name = "Renamed" });
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<PortfolioDto>();
        Assert.Equal("Renamed", updated!.Name);
    }

    [Fact]
    public async Task DeleteDefaultPortfolio_ReturnsBadRequest()
    {
        var response = await _client.DeleteAsync("/api/portfolios/11111111-1111-1111-1111-111111111111");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteNonDefaultPortfolio_ReturnsNoContent()
    {
        var createResp = await _client.PostAsJsonAsync("/api/portfolios", new { name = "To Delete" });
        var created = await createResp.Content.ReadFromJsonAsync<PortfolioDto>();

        var response = await _client.DeleteAsync($"/api/portfolios/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetSummary_EmptyPortfolio_ReturnsZeros()
    {
        var response = await _client.GetAsync("/api/portfolios/11111111-1111-1111-1111-111111111111/summary");
        response.EnsureSuccessStatusCode();

        var summary = await response.Content.ReadFromJsonAsync<PortfolioSummaryDto>();
        Assert.NotNull(summary);
        Assert.Equal(0m, summary.TotalValue);
        Assert.Empty(summary.Positions);
    }
}
