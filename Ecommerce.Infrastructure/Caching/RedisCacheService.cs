using Ecommerce.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using System.Text.Json;

namespace Ecommerce.Infrastructure.Caching;

public class RedisCacheService : ICacheService
{
    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer _redis;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public RedisCacheService(IDistributedCache cache, IConnectionMultiplexer redis)
    {
        _cache = cache;
        _redis = redis;
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var cached = await _cache.GetStringAsync(key);

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

    public async Task RemoveAsync(string key)
    {
        await _cache.RemoveAsync(key);
    }
}
