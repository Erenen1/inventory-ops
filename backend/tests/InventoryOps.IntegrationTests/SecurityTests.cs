using System.Net;
using InventoryOps.Application.Common;
using InventoryOps.Application.Modules.Identity;
using InventoryOps.IntegrationTests.Infrastructure;
using Shouldly;

namespace InventoryOps.IntegrationTests;

public sealed class SecurityTests(InfrastructureFixture fixture)
{
    private const string Password = InfrastructureFixture.Password;

    private async Task<ApiClient> SignedInAsync(string email)
    {
        var client = new ApiClient(fixture.Api);
        await client.LoginAsync(email, Password);
        return client;
    }

    [Theory]
    [InlineData("/api/v1/products")]
    [InlineData("/api/v1/orders")]
    [InlineData("/api/v1/inventory")]
    [InlineData("/api/v1/dashboard/summary")]
    [InlineData("/api/v1/users")]
    [InlineData("/api/v1/auth/me")]
    public async Task Unauthenticated_requests_are_rejected(string url)
    {
        using var anonymous = new ApiClient(fixture.Api);

        var response = await anonymous.GetAsync(url);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        (await response.ProblemCodeAsync()).ShouldBe("unauthenticated");
    }

    [Fact]
    public async Task Wrong_password_returns_a_generic_401()
    {
        using var client = new ApiClient(fixture.Api);

        var unknown = await client.TryLoginAsync("nobody@it.local", Password);
        var wrong = await client.TryLoginAsync(InfrastructureFixture.ViewerEmail, "Wrong-Password-123");

        await unknown.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        await wrong.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        (await unknown.Content.ReadAsStringAsync()).ShouldNotContain("nobody");
        (await unknown.ProblemCodeAsync()).ShouldBe(await wrong.ProblemCodeAsync());
    }

    [Fact]
    public async Task Session_cookie_is_http_only_and_strict()
    {
        using var client = new ApiClient(fixture.Api);
        await client.Http.GetAsync(new Uri("/api/v1/auth/csrf", UriKind.Relative));

        var response = await client.TryLoginAsync(InfrastructureFixture.ViewerEmail, Password);

        var session = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".InventoryOps.Session=", StringComparison.Ordinal));
        session.ShouldContain("httponly", Case.Insensitive);
        session.ShouldContain("samesite=strict", Case.Insensitive);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        using var client = await SignedInAsync(InfrastructureFixture.ViewerEmail);

        await (await client.PostAsync("/api/v1/auth/logout")).ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        await (await client.GetAsync("/api/v1/auth/me")).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task State_changing_requests_without_a_valid_csrf_token_are_rejected()
    {
        using var client = await SignedInAsync(InfrastructureFixture.AdminEmail);
        var body = new { name = $"Csrf {Guid.NewGuid():N}" };

        var missing = await client.SendAsync(HttpMethod.Post, "/api/v1/categories", body, includeCsrf: false);
        using var forged = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/categories", UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(body),
        };
        forged.Headers.Add("X-XSRF-TOKEN", "forged-token");
        var invalid = await client.Http.SendAsync(forged);

        await missing.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await missing.ProblemCodeAsync()).ShouldBe("antiforgery_invalid");
        await invalid.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Csrf_token_issued_to_another_session_is_rejected()
    {
        using var victim = await SignedInAsync(InfrastructureFixture.AdminEmail);
        using var attacker = await SignedInAsync(InfrastructureFixture.ManagerEmail);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/categories", UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { name = "Stolen token" }),
        };
        request.Headers.Add("X-XSRF-TOKEN", attacker.XsrfToken);

        var response = await victim.Http.SendAsync(request);

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(InfrastructureFixture.ViewerEmail, "POST", "/api/v1/products", HttpStatusCode.Forbidden)]
    [InlineData(InfrastructureFixture.ViewerEmail, "POST", "/api/v1/categories", HttpStatusCode.Forbidden)]
    [InlineData(InfrastructureFixture.ViewerEmail, "GET", "/api/v1/activity", HttpStatusCode.Forbidden)]
    [InlineData(InfrastructureFixture.ViewerEmail, "GET", "/api/v1/users", HttpStatusCode.Forbidden)]
    [InlineData(InfrastructureFixture.ManagerEmail, "GET", "/api/v1/users", HttpStatusCode.Forbidden)]
    [InlineData(InfrastructureFixture.ManagerEmail, "POST", "/api/v1/users", HttpStatusCode.Forbidden)]
    [InlineData(InfrastructureFixture.ViewerEmail, "GET", "/api/v1/products", HttpStatusCode.OK)]
    [InlineData(InfrastructureFixture.ViewerEmail, "GET", "/api/v1/dashboard/summary", HttpStatusCode.OK)]
    [InlineData(InfrastructureFixture.ManagerEmail, "GET", "/api/v1/activity", HttpStatusCode.OK)]
    public async Task Role_policies_are_enforced_server_side(string email, string method, string url, HttpStatusCode expected)
    {
        using var client = await SignedInAsync(email);

        var response = method == "GET"
            ? await client.GetAsync(url)
            : await client.PostAsync(url, new { name = "x" });

        await response.ShouldHaveStatusAsync(expected);
    }

    [Fact]
    public async Task Viewer_cannot_modify_stock_or_orders()
    {
        using var admin = await SignedInAsync(InfrastructureFixture.AdminEmail);
        var productId = await TestData.CreateProductAsync(admin, stock: 5);
        using var viewer = await SignedInAsync(InfrastructureFixture.ViewerEmail);

        var adjust = await viewer.PostAsync($"/api/v1/inventory/{productId}/adjustments", new { quantityChange = 100, reason = "Received", note = (string?)null });
        var order = await TestData.SubmitOrderAsync(viewer, productId, 1);

        await adjust.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await order.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        (await TestData.StockAsync(admin, productId)).OnHand.ShouldBe(5);
    }

    [Fact]
    public async Task Manager_cannot_escalate_privileges_and_admin_cannot_demote_self()
    {
        using var admin = await SignedInAsync(InfrastructureFixture.AdminEmail);
        var users = await admin.GetJsonAsync<PagedResult<UserDto>>("/api/v1/users?pageSize=100");
        var managerId = users.Items.Single(u => u.Email == InfrastructureFixture.ManagerEmail).Id;
        var adminId = users.Items.Single(u => u.Email == InfrastructureFixture.AdminEmail).Id;
        using var manager = await SignedInAsync(InfrastructureFixture.ManagerEmail);

        var escalate = await manager.PutAsync($"/api/v1/users/{managerId}/role", new { role = Roles.Admin });
        var selfDemote = await admin.PutAsync($"/api/v1/users/{adminId}/role", new { role = Roles.Viewer });

        await escalate.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await selfDemote.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        (await selfDemote.ProblemCodeAsync()).ShouldBe("identity.self_change");
    }

    [Fact]
    public async Task Repeated_failed_logins_lock_the_account()
    {
        using var admin = await SignedInAsync(InfrastructureFixture.AdminEmail);
        var email = $"lockout-{Guid.NewGuid():N}@it.local";
        await admin.CreateUserAsync(email, Roles.Viewer);
        using var attacker = new ApiClient(fixture.Api);

        for (var i = 0; i < 5; i++)
        {
            await (await attacker.TryLoginAsync(email, $"Wrong-Guess-{i}xY")).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        }

        // The correct password no longer works while the account is locked out.
        await (await attacker.TryLoginAsync(email, Password)).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_endpoint_is_rate_limited_per_client()
    {
        await using var factory = fixture.CreateFactory(new() { ["RateLimiting:LoginPermitsPerMinute"] = "3" });
        using var client = new ApiClient(factory);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await client.TryLoginAsync("nobody@it.local", "Wrong-Password-1")).StatusCode);
        }

        statuses.Take(3).ShouldAllBe(s => s == HttpStatusCode.Unauthorized);
        statuses.Skip(3).ShouldAllBe(s => s == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Deactivated_users_cannot_sign_in()
    {
        using var admin = await SignedInAsync(InfrastructureFixture.AdminEmail);
        var email = $"inactive-{Guid.NewGuid():N}@it.local";
        await admin.CreateUserAsync(email, Roles.Viewer);
        var users = await admin.GetJsonAsync<PagedResult<UserDto>>("/api/v1/users?pageSize=100");
        var id = users.Items.Single(u => u.Email == email).Id;

        await (await admin.PutAsync($"/api/v1/users/{id}/status", new { isActive = false })).ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        using var client = new ApiClient(fixture.Api);
        await (await client.TryLoginAsync(email, Password)).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_correlation_id()
    {
        using var client = new ApiClient(fixture.Api);

        var response = await client.GetAsync("/api/v1/products");

        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        response.Headers.GetValues("X-Frame-Options").ShouldContain("DENY");
        response.Headers.GetValues("X-Correlation-ID").Single().ShouldNotBeNullOrWhiteSpace();
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("StackTrace");
    }
}
