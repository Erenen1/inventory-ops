using System.Net;
using InventoryOps.Application.Common;
using InventoryOps.Application.Modules.Catalog;
using InventoryOps.Application.Modules.Inventory;
using InventoryOps.Application.Modules.Orders;
using InventoryOps.Domain.Orders;
using InventoryOps.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace InventoryOps.IntegrationTests;

public sealed class BusinessRuleTests(InfrastructureFixture fixture) : IAsyncLifetime
{
    private readonly ApiClient _manager = new(fixture.Api);

    public async ValueTask InitializeAsync() => await _manager.LoginAsync(InfrastructureFixture.ManagerEmail, InfrastructureFixture.Password);

    public ValueTask DisposeAsync()
    {
        _manager.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Product_lifecycle_round_trips_through_the_api()
    {
        var id = await TestData.CreateProductAsync(_manager, price: 19.99m);
        var categoryId = await TestData.AnyCategoryAsync(_manager);

        var update = await _manager.PutAsync($"/api/v1/products/{id}", new
        {
            name = "Renamed widget", description = "Updated", categoryId, unitPrice = 24.50m, currency = "USD",
        });
        await update.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await (await _manager.PostAsync($"/api/v1/products/{id}/deactivate")).ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        var product = await _manager.GetJsonAsync<ProductDetails>($"/api/v1/products/{id}");
        product.Name.ShouldBe("Renamed widget");
        product.UnitPrice.ShouldBe(24.50m);
        product.IsActive.ShouldBeFalse();

        var search = await _manager.GetJsonAsync<PagedResult<ProductListItem>>($"/api/v1/products?search={product.Sku}&isActive=false");
        search.Items.ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    [Fact]
    public async Task Duplicate_sku_is_rejected_even_under_concurrency()
    {
        var sku = TestData.UniqueSku("DUP");
        var categoryId = await TestData.AnyCategoryAsync(_manager);
        var body = new { sku, name = "Dup", description = (string?)null, categoryId, unitPrice = 1m, currency = "USD", reorderThreshold = 1 };

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _manager.PostAsync("/api/v1/products", body)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(3);
    }

    [Fact]
    public async Task Unknown_properties_are_rejected_to_prevent_mass_assignment()
    {
        var categoryId = await TestData.AnyCategoryAsync(_manager);

        var response = await _manager.PostAsync("/api/v1/products", new
        {
            sku = TestData.UniqueSku(), name = "x", categoryId, unitPrice = 1m, currency = "USD", reorderThreshold = 1,
            isActive = false, id = Guid.NewGuid(),
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ProblemCodeAsync()).ShouldBe("malformed_request");
    }

    [Theory]
    [InlineData("/api/v1/products?pageSize=101")]
    [InlineData("/api/v1/products?pageSize=100000")]
    [InlineData("/api/v1/products?page=0")]
    [InlineData("/api/v1/products?sortBy=PasswordHash")]
    [InlineData("/api/v1/orders?pageSize=500")]
    [InlineData("/api/v1/inventory?sortBy=1;DROP TABLE x")]
    public async Task Expensive_or_invalid_queries_are_rejected(string url) =>
        await (await _manager.GetAsync(url)).ShouldHaveStatusAsync(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Invalid_payload_returns_field_level_validation_errors()
    {
        var response = await _manager.PostAsync("/api/v1/products", new
        {
            sku = "!", name = "", description = (string?)null, categoryId = Guid.Empty, unitPrice = -1m, currency = "ABC", reorderThreshold = -1,
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var field in new[] { "sku", "name", "categoryId", "unitPrice", "currency", "reorderThreshold" })
        {
            body.ShouldContain($"\"{field}\"");
        }
    }

    [Fact]
    public async Task Stock_cannot_go_negative_or_below_reservations()
    {
        var id = await TestData.CreateProductAsync(_manager, stock: 5);
        await TestData.SubmitOrderOkAsync(_manager, id, 4);

        var negative = await _manager.PostAsync($"/api/v1/inventory/{id}/adjustments", new { quantityChange = -6, reason = "Damaged", note = (string?)null });
        var belowReserved = await _manager.PostAsync($"/api/v1/inventory/{id}/adjustments", new { quantityChange = -2, reason = "Damaged", note = (string?)null });

        await negative.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        await belowReserved.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        (await belowReserved.ProblemCodeAsync()).ShouldBe("inventory.below_reserved");
        var stock = await TestData.StockAsync(_manager, id);
        (stock.OnHand, stock.Reserved, stock.Available).ShouldBe((5, 4, 1));
    }

    [Fact]
    public async Task Database_check_constraints_back_up_the_domain_invariants()
    {
        var id = await TestData.CreateProductAsync(_manager, stock: 3);
        await using var db = fixture.CreateDbContext();

        var ex = await Should.ThrowAsync<SqlException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE inventory.InventoryItems SET Reserved = OnHand + 1 WHERE ProductId = {id}"));

        ex.Number.ShouldBe(547); // CHECK constraint violation
    }

    [Fact]
    public async Task Order_lifecycle_reserves_consumes_and_records_stock_movements()
    {
        var id = await TestData.CreateProductAsync(_manager, stock: 10, price: 2.50m);

        var order = await TestData.SubmitOrderOkAsync(_manager, id, 4);
        order.Status.ShouldBe(OrderStatus.Submitted);
        order.TotalAmount.ShouldBe(10.00m);
        (await TestData.StockAsync(_manager, id)).ShouldSatisfyAllConditions(
            s => s.OnHand.ShouldBe(10), s => s.Reserved.ShouldBe(4), s => s.Available.ShouldBe(6));

        await (await _manager.PostAsync($"/api/v1/orders/{order.Id}/process")).ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await (await _manager.PostAsync($"/api/v1/orders/{order.Id}/fulfill")).ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        var stock = await TestData.StockAsync(_manager, id);
        (stock.OnHand, stock.Reserved, stock.Available).ShouldBe((6, 0, 6));

        var details = await _manager.GetJsonAsync<OrderDetails>($"/api/v1/orders/{order.Id}");
        details.Status.ShouldBe(OrderStatus.Fulfilled);
        details.History.Select(h => h.ToStatus).ShouldBe([OrderStatus.Submitted, OrderStatus.Processing, OrderStatus.Fulfilled]);
        details.AllowedTransitions.ShouldBeEmpty();

        var movements = await _manager.GetJsonAsync<PagedResult<StockMovementDto>>($"/api/v1/inventory/{id}/movements");
        movements.Items.Select(m => m.Type).ShouldBe(["Fulfillment", "Reservation", "Adjustment"]);
        movements.Items[0].OrderNumber.ShouldBe(order.Number);
        movements.Items[0].PerformedBy.ShouldBe(InfrastructureFixture.ManagerEmail);
    }

    [Fact]
    public async Task Cancelling_releases_reservations_and_terminal_states_reject_transitions()
    {
        var id = await TestData.CreateProductAsync(_manager, stock: 3);
        var order = await TestData.SubmitOrderOkAsync(_manager, id, 3);

        var fulfillTooEarly = await _manager.PostAsync($"/api/v1/orders/{order.Id}/fulfill");
        await fulfillTooEarly.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        (await fulfillTooEarly.ProblemCodeAsync()).ShouldBe("orders.invalid_transition");

        await (await _manager.PostAsync($"/api/v1/orders/{order.Id}/cancel", new { reason = "Customer request" })).ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await TestData.StockAsync(_manager, id)).Available.ShouldBe(3);

        await (await _manager.PostAsync($"/api/v1/orders/{order.Id}/process")).ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        var details = await _manager.GetJsonAsync<OrderDetails>($"/api/v1/orders/{order.Id}");
        details.CancellationReason.ShouldBe("Customer request");
    }

    [Fact]
    public async Task Inactive_products_cannot_be_ordered()
    {
        var id = await TestData.CreateProductAsync(_manager, stock: 5);
        await _manager.PostAsync($"/api/v1/products/{id}/deactivate");

        var response = await TestData.SubmitOrderAsync(_manager, id, 1);

        await response.ShouldHaveStatusAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ProblemCodeAsync()).ShouldBe("orders.product_inactive");
    }

    [Fact]
    public async Task Order_lines_keep_their_price_snapshot_after_price_changes()
    {
        var id = await TestData.CreateProductAsync(_manager, stock: 5, price: 10m);
        var order = await TestData.SubmitOrderOkAsync(_manager, id, 2);
        var categoryId = await TestData.AnyCategoryAsync(_manager);

        await _manager.PutAsync($"/api/v1/products/{id}", new { name = "Repriced", description = (string?)null, categoryId, unitPrice = 99m, currency = "USD" });

        var details = await _manager.GetJsonAsync<OrderDetails>($"/api/v1/orders/{order.Id}");
        details.Lines.ShouldHaveSingleItem().UnitPrice.ShouldBe(10m);
        details.TotalAmount.ShouldBe(20m);
    }

    [Fact]
    public async Task Idempotency_key_replays_return_the_original_order()
    {
        var id = await TestData.CreateProductAsync(_manager, stock: 10);
        var key = Guid.NewGuid().ToString("N");

        var first = await TestData.SubmitOrderAsync(_manager, id, 2, key);
        var replay = await TestData.SubmitOrderAsync(_manager, id, 2, key);
        var mismatch = await TestData.SubmitOrderAsync(_manager, id, 3, key);
        var missingKey = await _manager.PostAsync("/api/v1/orders", new { customerName = "x", notes = (string?)null, lines = new[] { new { productId = id, quantity = 1 } } });

        await first.ShouldHaveStatusAsync(HttpStatusCode.Created);
        await replay.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await replay.ReadJsonAsync<OrderDetails>()).Id.ShouldBe((await first.ReadJsonAsync<OrderDetails>()).Id);
        await mismatch.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        await missingKey.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await TestData.StockAsync(_manager, id)).Reserved.ShouldBe(2);
    }

    [Fact]
    public async Task Concurrent_submissions_with_the_same_key_create_one_order()
    {
        var id = await TestData.CreateProductAsync(_manager, stock: 10);
        var key = Guid.NewGuid().ToString("N");

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => TestData.SubmitOrderAsync(_manager, id, 1, key)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.OK);
        var ids = await Task.WhenAll(responses.Select(async r => (await r.ReadJsonAsync<OrderDetails>()).Id));
        ids.Distinct().ShouldHaveSingleItem();
        (await TestData.StockAsync(_manager, id)).Reserved.ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_orders_never_oversell_stock()
    {
        const int stock = 10;
        const int buyers = 30;
        var id = await TestData.CreateProductAsync(_manager, stock: stock);

        var responses = await Task.WhenAll(Enumerable.Range(0, buyers).Select(_ => TestData.SubmitOrderAsync(_manager, id, 1)));

        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var codes = await Task.WhenAll(responses.Where(r => r.StatusCode != HttpStatusCode.Created).Select(r => r.ProblemCodeAsync()));
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict);
        codes.ShouldAllBe(c => c == "inventory.insufficient_stock" || c == "concurrency_conflict");
        created.ShouldBeInRange(1, stock);

        var after = await TestData.StockAsync(_manager, id);
        after.OnHand.ShouldBe(stock);
        after.Reserved.ShouldBe(created);

        await using var db = fixture.CreateDbContext();
        var reservedInOrders = await db.OrderLines.Where(l => l.ProductId == id).SumAsync(l => l.Quantity);
        reservedInOrders.ShouldBe(created);

        // Exactly the remaining stock can still be reserved: nothing was lost or double counted.
        for (var i = created; i < stock; i++)
        {
            await TestData.SubmitOrderOkAsync(_manager, id, 1);
        }

        var oversell = await TestData.SubmitOrderAsync(_manager, id, 1);
        await oversell.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        (await oversell.ProblemCodeAsync()).ShouldBe("inventory.insufficient_stock");
        (await TestData.StockAsync(_manager, id)).Available.ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_fulfillment_consumes_stock_once()
    {
        var id = await TestData.CreateProductAsync(_manager, stock: 5);
        var order = await TestData.SubmitOrderOkAsync(_manager, id, 2);
        await _manager.PostAsync($"/api/v1/orders/{order.Id}/process");

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _manager.PostAsync($"/api/v1/orders/{order.Id}/fulfill")));

        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(3);
        var stock = await TestData.StockAsync(_manager, id);
        (stock.OnHand, stock.Reserved).ShouldBe((3, 0));
    }

    [Fact]
    public async Task Dashboard_summary_reflects_backend_state()
    {
        var summary = await _manager.GetJsonAsync<InventoryOps.Application.Modules.Dashboard.DashboardSummary>("/api/v1/dashboard/summary");

        summary.OrdersByStatus.Select(s => s.Status).ShouldBe(Enum.GetValues<OrderStatus>());
        summary.TotalOrders.ShouldBe(summary.OrdersByStatus.Sum(s => s.Count));
        summary.TotalAvailable.ShouldBe(summary.TotalOnHand - summary.TotalReserved);
        summary.OrdersLast14Days.Count.ShouldBe(14);
    }
}
