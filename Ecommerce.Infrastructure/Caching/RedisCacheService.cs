using Ecommerce.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using System.Text.Json;
using System.Collections.Concurrent;

namespace Ecommerce.Infrastructure.Caching;

public class RedisCacheService : ICacheService
{
    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer? _multiplexer;
    private readonly string _prefix;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> KeyLocks = new();
    private const string IncrementScript = """
        local key = KEYS[1]
        local ttlSeconds = tonumber(ARGV[1])
        local fallbackBase = tonumber(ARGV[2]) or 0

        local current = redis.call('GET', key)
        local exists = current ~= false

        if not exists then
          local v = redis.call('INCR', key)
          if ttlSeconds and ttlSeconds > 0 then
            redis.call('EXPIRE', key, ttlSeconds)
          end
          return v
        end

        local num = tonumber(current)
        if not num then
          local unquoted = string.match(current, '^"(%-?%d+)"$')
          if unquoted then
            num = tonumber(unquoted)
          end
        end

        if not num then
          num = fallbackBase
        end

        redis.call('SET', key, tostring(math.floor(num)))
        local nextVal = redis.call('INCR', key)

        local ttl = redis.call('TTL', key)
        if ttl == -1 and ttlSeconds and ttlSeconds > 0 then
          redis.call('EXPIRE', key, ttlSeconds)
        end

        return nextVal
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public RedisCacheService(
        IDistributedCache cache,
        IConnectionMultiplexer? multiplexer,
        IConfiguration configuration)
    {
        _cache = cache;
        _multiplexer = multiplexer;
        _prefix = configuration["Redis:InstanceName"] ?? string.Empty;
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        string? cached;
        try
        {
            cached = await _cache.GetStringAsync(key);
        }
        catch (RedisServerException ex) when (
            ex.Message.Contains("WRONGTYPE", StringComparison.OrdinalIgnoreCase))
        {
            // Key was previously written with an incompatible Redis type.
            // Remove and treat as cache miss so it can be recreated correctly.
            await _cache.RemoveAsync(key);
            return default;
        }

        if (string.IsNullOrEmpty(cached))
            return default;

        return JsonSerializer.Deserialize<T>(cached, JsonOptions);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry ?? TimeSpan.FromMinutes(5)
        };

        await _cache.SetStringAsync(key, json, options);
    }

    public async Task<long> IncrementAsync(string key, TimeSpan? expiry = null)
    {
        if (_multiplexer is null)
            return await IncrementWithoutRedisAsync(key, expiry);

        var db = _multiplexer.GetDatabase();
        var fullKey = _prefix + key;
        var ttl = expiry ?? TimeSpan.FromDays(1);
        var ttlSeconds = ttl > TimeSpan.Zero ? (long)Math.Ceiling(ttl.TotalSeconds) : 0;
        var fallbackBase = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var result = await db.ScriptEvaluateAsync(
            IncrementScript,
            new RedisKey[] { fullKey },
            new RedisValue[] { ttlSeconds, fallbackBase });

        return (long)result;
    }

    public async Task RemoveAsync(string key)
    {
        await _cache.RemoveAsync(key);
    }

    private async Task<long> IncrementWithoutRedisAsync(string key, TimeSpan? expiry)
    {
        var keyLock = KeyLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync();
        try
        {
            var cached = await _cache.GetStringAsync(key);
            var next = NormalizeAndIncrement(cached);

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry ?? TimeSpan.FromDays(1)
            };
            await _cache.SetStringAsync(key, next.ToString(), options);

            return next;
        }
        finally
        {
            keyLock.Release();
        }
    }

    private static long NormalizeAndIncrement(string? current)
    {
        if (string.IsNullOrWhiteSpace(current))
            return 1;

        if (long.TryParse(current, out var number))
            return number + 1;

        if (current.Length >= 2 &&
            current[0] == '"' &&
            current[^1] == '"' &&
            long.TryParse(current[1..^1], out var quoted))
        {
            return quoted + 1;
        }

        var fallbackBase = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return fallbackBase + 1;
    }
}
