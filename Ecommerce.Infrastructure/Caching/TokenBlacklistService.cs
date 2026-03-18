using Ecommerce.Application.Interfaces;

namespace Ecommerce.Infrastructure.Caching
{
    public class TokenBlacklistService : ITokenBlacklistService
    {
        private readonly ICacheService _cacheService;
        private const string BlacklistPrefix = "BlacklistedToken:";

        public TokenBlacklistService(ICacheService cacheService)
        {
            _cacheService = cacheService;
        }

        public async Task BlacklistTokenAsync(string token, TimeSpan expiry)
        {
            var key = $"{BlacklistPrefix}{token}";
            // We only need to store an empty string or 'true' to indicate it's blacklisted.
            // Using GetStringAsync checking for null is safer.
            await _cacheService.SetAsync(key, "true", expiry);
        }

        public async Task<bool> IsTokenBlacklistedAsync(string token)
        {
            var key = $"{BlacklistPrefix}{token}";
            var value = await _cacheService.GetAsync<string>(key);
            return !string.IsNullOrEmpty(value);
        }
    }
}
