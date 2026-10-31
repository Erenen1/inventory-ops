using System.Text.Json;
using InventoryOps.Application.Common.Caching;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace InventoryOps.Infrastructure.Caching;

/// <summary>
/// Owns the shared Redis multiplexer. Connection failures never block requests: the multiplexer is
/// created with <c>abortConnect=false</c> and keeps reconnecting in the background, and callers
/// simply skip the cache while it is disconnected.
/// </summary>
public sealed class RedisConnection(string configuration, ILogger<RedisConnection> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ConnectionMultiplexer? _multiplexer;

    public bool IsConnected => _multiplexer?.IsConnected ?? false;

    public async Task<IDatabase?> GetDatabaseAsync()
    {
        var multiplexer = _multiplexer ?? await ConnectAsync();
        return multiplexer is { IsConnected: true } ? multiplexer.GetDatabase() : null;
    }

    private async Task<IConnectionMultiplexer?> ConnectAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_multiplexer is null)
            {
                var options = ConfigurationOptions.Parse(configuration);
                options.AbortOnConnectFail = false;
                options.ConnectTimeout = Math.Min(options.ConnectTimeout, 2000);
                options.SyncTimeout = 1000;
                options.AsyncTimeout = 1000;
                options.ClientName = "inventoryops-api";
                _multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
                _multiplexer.ConnectionFailed += (_, e) => logger.LogWarning("Redis connection failed: {FailureType}", e.FailureType);
                _multiplexer.ConnectionRestored += (_, _) => logger.LogInformation("Redis connection restored");
            }

            return _multiplexer;
        }
        catch (Exception ex) when (ex is RedisException or ArgumentException)
        {
            logger.LogWarning(ex, "Redis is not configured correctly; caching is disabled");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_multiplexer is not null)
        {
            await _multiplexer.DisposeAsync();
        }

        _gate.Dispose();
    }
}

internal sealed class RedisCacheStore(RedisConnection redis, ILogger<RedisCacheStore> logger) : ICacheStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var db = await redis.GetDatabaseAsync();
            if (db is null)
            {
                return null;
            }

            var value = await db.StringGetAsync(key).WaitAsync(cancellationToken);
            return value.HasValue ? JsonSerializer.Deserialize<T>(value.ToString(), Json) : null;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or JsonException)
        {
            // Never include cached values in logs; the key carries no sensitive data.
            logger.LogWarning("Cache read failed for {CacheKey}: {Error}", key, ex.GetType().Name);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var db = await redis.GetDatabaseAsync();
            if (db is not null)
            {
                await db.StringSetAsync(key, JsonSerializer.Serialize(value, Json), ttl).WaitAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning("Cache write failed for {CacheKey}: {Error}", key, ex.GetType().Name);
        }
    }

    public async Task RemoveAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        try
        {
            var db = await redis.GetDatabaseAsync();
            if (db is not null)
            {
                await db.KeyDeleteAsync(keys.Select(k => (RedisKey)k).ToArray()).WaitAsync(cancellationToken);
            }
            else
            {
                logger.LogWarning("Cache invalidation skipped (Redis unavailable) for {CacheKeys}; entries expire by TTL", keys);
            }
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning("Cache invalidation failed for {CacheKeys}: {Error}; entries expire by TTL", keys, ex.GetType().Name);
        }
    }
}

/// <summary>Redis is optional for correctness, so an outage reports Degraded rather than Unhealthy.</summary>
internal sealed class RedisHealthCheck(RedisConnection redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var db = await redis.GetDatabaseAsync();
        if (db is null)
        {
            return HealthCheckResult.Degraded("Redis unavailable; serving uncached reads.");
        }

        try
        {
            await db.PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return HealthCheckResult.Degraded("Redis ping failed; serving uncached reads.");
        }
    }
}
