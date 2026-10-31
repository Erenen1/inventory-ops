using System.Net;
using System.Text.Json;
using InventoryOps.Application.Common.Caching;
using InventoryOps.Application.Modules.Catalog;
using InventoryOps.IntegrationTests.Infrastructure;
using Shouldly;
using StackExchange.Redis;

namespace InventoryOps.IntegrationTests;

public sealed class CachingTests(InfrastructureFixture fixture) : IAsyncLifetime
{
    private readonly ApiClient _manager = new(fixture.Api);
    private ConnectionMultiplexer _redis = null!;

    public async ValueTask InitializeAsync()
    {
        await _manager.LoginAsync(InfrastructureFixture.ManagerEmail, InfrastructureFixture.Password);
        _redis = await ConnectionMultiplexer.ConnectAsync(fixture.Redis.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        _manager.Dispose();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task Product_details_are_cached_and_invalidated_after_updates()
    {
        var id = await TestData.CreateProductAsync(_manager);
        var key = CacheKeys.Product(id);
        var db = _redis.GetDatabase();

        await _manager.GetJsonAsync<ProductDetails>($"/api/v1/products/{id}");
        (await db.KeyExistsAsync(key)).ShouldBeTrue();
        (await db.KeyTimeToLiveAsync(key)).ShouldNotBeNull().ShouldBeLessThanOrEqualTo(CacheKeys.ProductTtl);
        JsonDocument.Parse((string)(await db.StringGetAsync(key))!).RootElement.GetProperty("id").GetGuid().ShouldBe(id);

        var categoryId = await TestData.AnyCategoryAsync(_manager);
        await _manager.PutAsync($"/api/v1/products/{id}", new { name = "Cache busted", description = (string?)null, categoryId, unitPrice = 3m, currency = "USD" });

        (await db.KeyExistsAsync(key)).ShouldBeFalse();
        (await _manager.GetJsonAsync<ProductDetails>($"/api/v1/products/{id}")).Name.ShouldBe("Cache busted");
    }

    [Fact]
    public async Task Database_stays_the_source_of_truth_for_stock()
    {
        var id = await TestData.CreateProductAsync(_manager, stock: 4);

        await _manager.GetJsonAsync<object>("/api/v1/dashboard/summary");
        (await _redis.GetDatabase().KeyExistsAsync(CacheKeys.DashboardSummary)).ShouldBeTrue();

        await _manager.PostAsync($"/api/v1/inventory/{id}/adjustments", new { quantityChange = 6, reason = "Received", note = (string?)null });

        // Stock writes invalidate the dashboard summary; inventory itself is never served from cache.
        (await _redis.GetDatabase().KeyExistsAsync(CacheKeys.DashboardSummary)).ShouldBeFalse();
        (await TestData.StockAsync(_manager, id)).OnHand.ShouldBe(10);
    }

    [Fact]
    public async Task Api_keeps_working_when_redis_is_unavailable()
    {
        await using var factory = fixture.CreateFactory(new() { ["ConnectionStrings:Redis"] = "127.0.0.1:1,connectTimeout=500" });
        using var client = new ApiClient(factory);
        await client.LoginAsync(InfrastructureFixture.ManagerEmail, InfrastructureFixture.Password);

        var id = await TestData.CreateProductAsync(client);
        var product = await client.GetJsonAsync<ProductDetails>($"/api/v1/products/{id}");
        var ready = await client.GetAsync("/api/health/ready");

        product.Id.ShouldBe(id);
        await ready.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await ready.Content.ReadAsStringAsync()).ShouldContain("\"redis\":\"Degraded\"");
    }
}
