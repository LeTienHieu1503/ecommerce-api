using Ecommerce.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace Ecommerce.Infrastructure.Caching;

public class MemoryCacheService : ICacheService
{
    private readonly IDistributedCache _cache;

    public MemoryCacheService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var json = await _cache.GetStringAsync(key);
        if (json == null) return default;
        return JsonSerializer.Deserialize<T>(json);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        var json = JsonSerializer.Serialize(value);
        var options = new DistributedCacheEntryOptions();
        if (expiry.HasValue)
            options.SetAbsoluteExpiration(expiry.Value);
        await _cache.SetStringAsync(key, json, options);
    }

    public async Task<long> IncrementAsync(string key, TimeSpan? expiry = null)
    {
        var json = await _cache.GetStringAsync(key);
        long current = json == null ? 0 : long.Parse(json);
        long newValue = current + 1;

        var options = new DistributedCacheEntryOptions();
        if (expiry.HasValue)
            options.SetAbsoluteExpiration(expiry.Value);

        await _cache.SetStringAsync(key, newValue.ToString(), options);
        return newValue;
    }

    public async Task RemoveAsync(string key)
    {
        await _cache.RemoveAsync(key);
    }
}