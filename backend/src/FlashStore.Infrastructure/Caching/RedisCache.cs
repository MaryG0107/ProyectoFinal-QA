using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FlashStore.Infrastructure.Caching;

/// <summary>Resultado de una consulta con cache. Se envía al cliente en el header X-Cache.</summary>
public enum CacheStatus
{
    Hit,
    Miss,
    Bypass
}

/// <summary>
/// Cache-aside sobre Redis con tolerancia a fallos:
/// si Redis no está disponible, la consulta va directo a PostgreSQL (estado BYPASS)
/// y la aplicación sigue funcionando.
/// </summary>
public sealed class RedisCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCache> _logger;

    public RedisCache(IConnectionMultiplexer redis, ILogger<RedisCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public bool IsAvailable => _redis.IsConnected;

    public async Task<(T? Value, CacheStatus Status)> GetOrSetAsync<T>(string key, TimeSpan ttl, Func<Task<T?>> factory)
        where T : class
    {
        IDatabase db;
        RedisValue cached;

        try
        {
            db = _redis.GetDatabase();
            cached = await db.StringGetAsync(key);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "Redis no disponible al leer {Key}; se consulta directo a la base de datos", key);
            return (await factory(), CacheStatus.Bypass);
        }

        if (cached.HasValue)
        {
            var value = JsonSerializer.Deserialize<T>(cached.ToString(), JsonOptions);
            if (value is not null)
            {
                return (value, CacheStatus.Hit);
            }
        }

        var fresh = await factory();

        if (fresh is not null)
        {
            try
            {
                await db.StringSetAsync(key, JsonSerializer.Serialize(fresh, JsonOptions), ttl);
            }
            catch (Exception ex) when (IsRedisFailure(ex))
            {
                _logger.LogWarning(ex, "Redis no disponible al guardar {Key}", key);
                return (fresh, CacheStatus.Bypass);
            }
        }

        return (fresh, CacheStatus.Miss);
    }

    /// <summary>Invalida (borra) llaves del cache. Si Redis está caído, solo se registra la advertencia.</summary>
    public async Task InvalidateAsync(IEnumerable<string> keys)
    {
        var redisKeys = keys.Distinct().Select(k => (RedisKey)k).ToArray();

        if (redisKeys.Length == 0)
        {
            return;
        }

        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(redisKeys);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "Redis no disponible al invalidar {Count} llaves", redisKeys.Length);
        }
    }

    /// <summary>Tiempo de vida restante de una llave (para evidenciar el TTL en las pruebas).</summary>
    public async Task<TimeSpan?> GetTimeToLiveAsync(string key)
    {
        try
        {
            return await _redis.GetDatabase().KeyTimeToLiveAsync(key);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "Redis no disponible al consultar TTL de {Key}", key);
            return null;
        }
    }

    public static bool IsRedisFailure(Exception ex) =>
        ex is RedisException or RedisTimeoutException or ObjectDisposedException;
}
