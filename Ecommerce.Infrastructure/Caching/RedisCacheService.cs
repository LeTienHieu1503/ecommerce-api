using Ecommerce.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using System.Text.Json;
using System.Collections.Concurrent;

namespace Ecommerce.Infrastructure.Caching;

public class RedisCacheService : ICacheService
{
    private readonly IDistributedCache _cache;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> KeyLocks = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public RedisCacheService(IDistributedCache cache)
    {
        _cache = cache;
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

        try
        {
            return JsonSerializer.Deserialize<T>(cached, JsonOptions);
        }
        catch (JsonException) when (TryParseLong(cached, out var n))
        {
            if (typeof(T) == typeof(long))
                return (T)(object)n;
            if (typeof(T) == typeof(long?))
                return (T)(object)(long?)n;
            throw;
        }
    }

    private static bool TryParseLong(string s, out long value)
        => long.TryParse(s.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry ?? TimeSpan.FromMinutes(5)
        };

        await _cache.SetStringAsync(key, json, options);
    }

    public Task<long> IncrementAsync(string key, TimeSpan? expiry = null)
        => IncrementViaDistributedCacheAsync(key, expiry);

    public async Task RemoveAsync(string key)
    {
        await _cache.RemoveAsync(key);
    }

    private async Task<long> IncrementViaDistributedCacheAsync(string key, TimeSpan? expiry)
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
