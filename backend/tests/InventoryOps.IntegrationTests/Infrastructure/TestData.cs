using System.Net;
using InventoryOps.Application.Common;
using InventoryOps.Application.Modules.Catalog;
using InventoryOps.Application.Modules.Inventory;
using InventoryOps.Application.Modules.Orders;
using Shouldly;

namespace InventoryOps.IntegrationTests.Infrastructure;

public sealed record IdResponse(Guid Id);

/// <summary>Creates isolated test data through the public API (unique SKUs per call).</summary>
public static class TestData
{
    public static string UniqueSku(string prefix = "IT") => $"{prefix}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

    public static async Task<Guid> AnyCategoryAsync(ApiClient client)
    {
        var categories = await client.GetJsonAsync<List<CategoryDto>>("/api/v1/categories");
        if (categories.Count > 0)
        {
            return categories[0].Id;
        }

        var response = await client.PostAsync("/api/v1/categories", new { name = "Integration" });
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return (await client.GetJsonAsync<List<CategoryDto>>("/api/v1/categories"))[0].Id;
        }

        return (await response.ReadJsonAsync<IdResponse>()).Id;
    }

    public static async Task<Guid> CreateProductAsync(ApiClient client, int stock = 0, decimal price = 10m, string? sku = null, int reorderThreshold = 2)
    {
        var categoryId = await AnyCategoryAsync(client);
        var response = await client.PostAsync("/api/v1/products", new
        {
            sku = sku ?? UniqueSku(),
            name = "Integration widget",
            description = (string?)null,
            categoryId,
            unitPrice = price,
            currency = "USD",
            reorderThreshold,
        });
        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var id = (await response.ReadJsonAsync<IdResponse>()).Id;

        if (stock > 0)
        {
            var adjust = await client.PostAsync($"/api/v1/inventory/{id}/adjustments", new { quantityChange = stock, reason = "Received", note = "setup" });
            await adjust.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }

        return id;
    }

    public static Task<HttpResponseMessage> SubmitOrderAsync(ApiClient client, Guid productId, int quantity, string? key = null) =>
        client.PostAsync(
            "/api/v1/orders",
            new { customerName = "Integration Customer", notes = (string?)null, lines = new[] { new { productId, quantity } } },
            key ?? Guid.NewGuid().ToString("N"));

    public static async Task<OrderDetails> SubmitOrderOkAsync(ApiClient client, Guid productId, int quantity)
    {
        var response = await SubmitOrderAsync(client, productId, quantity);
        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        return await response.ReadJsonAsync<OrderDetails>();
    }

    public static Task<InventoryItemDto> StockAsync(ApiClient client, Guid productId) =>
        client.GetJsonAsync<InventoryItemDto>($"/api/v1/inventory/{productId}");

    public static async Task<T> EventuallyAsync<T>(Func<Task<T?>> probe, TimeSpan? timeout = null)
        where T : class
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            if (await probe() is { } value)
            {
                return value;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("Condition was not met in time.");
    }
}
