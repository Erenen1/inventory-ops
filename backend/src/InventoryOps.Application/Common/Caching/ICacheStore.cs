namespace InventoryOps.Application.Common.Caching;

/// <summary>
/// Best-effort distributed cache used for cache-aside reads. Implementations must never throw
/// for cache infrastructure failures: a failed read is a miss and a failed write is skipped.
/// SQL Server remains the source of truth.
/// </summary>
public interface ICacheStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class;

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken) where T : class;

    Task RemoveAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
}

public static class CacheStoreExtensions
{
    public static async Task<T?> GetOrCreateAsync<T>(
        this ICacheStore cache, string key, TimeSpan ttl, Func<CancellationToken, Task<T?>> factory, CancellationToken cancellationToken)
        where T : class
    {
        var cached = await cache.GetAsync<T>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var value = await factory(cancellationToken);
        if (value is not null)
        {
            await cache.SetAsync(key, value, ttl, cancellationToken);
        }

        return value;
    }

    public static Task RemoveAsync(this ICacheStore cache, CancellationToken cancellationToken, params string[] keys) =>
        cache.RemoveAsync(keys, cancellationToken);
}

/// <summary>
/// Cache key conventions: <c>{module}:{resource}[:{id}]:v{schemaVersion}</c>. The schema version
/// suffix is bumped when a cached DTO shape changes so old entries are ignored after deploys.
/// </summary>
public static class CacheKeys
{
    public static readonly TimeSpan ProductTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan CategoriesTtl = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DashboardTtl = TimeSpan.FromSeconds(30);

    public static string Product(Guid id) => $"catalog:product:{id:N}:v1";

    public const string Categories = "catalog:categories:v1";

    public const string DashboardSummary = "dashboard:summary:v1";
}
