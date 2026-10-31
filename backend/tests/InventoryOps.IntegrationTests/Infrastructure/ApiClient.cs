using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Shouldly;

namespace InventoryOps.IntegrationTests.Infrastructure;

/// <summary>
/// Browser-like client: keeps cookies and echoes the XSRF-TOKEN cookie in the X-XSRF-TOKEN header,
/// exactly as the SPA does.
/// </summary>
public sealed class ApiClient : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly Uri BaseAddress = new("http://localhost");
    private readonly CookieContainer _cookies = new();

    public ApiClient(WebApplicationFactory<Program> factory) =>
        Http = factory.CreateDefaultClient(BaseAddress, new CookieContainerHandler(_cookies));

    public HttpClient Http { get; }

    public string? XsrfToken => _cookies.GetCookies(BaseAddress)["XSRF-TOKEN"]?.Value;

    public async Task<HttpResponseMessage> TryLoginAsync(string email, string password)
    {
        if (XsrfToken is null)
        {
            (await Http.GetAsync(new Uri("/api/v1/auth/csrf", UriKind.Relative))).EnsureSuccessStatusCode();
        }

        return await SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email, password });
    }

    public async Task LoginAsync(string email, string password)
    {
        var response = await TryLoginAsync(email, password);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public Task<HttpResponseMessage> GetAsync(string url) => Http.GetAsync(new Uri(url, UriKind.Relative));

    public async Task<T> GetJsonAsync<T>(string url)
    {
        var response = await GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, object? body = null, string? idempotencyKey = null, bool includeCsrf = true)
    {
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        if (includeCsrf && XsrfToken is { } token)
        {
            request.Headers.Add("X-XSRF-TOKEN", token);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await Http.SendAsync(request);
    }

    public Task<HttpResponseMessage> PostAsync(string url, object? body = null, string? idempotencyKey = null) =>
        SendAsync(HttpMethod.Post, url, body, idempotencyKey);

    public Task<HttpResponseMessage> PutAsync(string url, object body) => SendAsync(HttpMethod.Put, url, body);

    public async Task CreateUserAsync(string email, string role)
    {
        var response = await PostAsync("/api/v1/users", new { email, displayName = role, role, password = InfrastructureFixture.Password });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    public void Dispose() => Http.Dispose();
}

public static class HttpResponseExtensions
{
    public static async Task<T> ReadJsonAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(ApiClient.Json))!;

    /// <summary>Reads the stable machine-readable <c>code</c> from a ProblemDetails body.</summary>
    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static async Task ShouldHaveStatusAsync(this HttpResponseMessage response, HttpStatusCode expected) =>
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
}
