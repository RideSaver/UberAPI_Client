using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using UberClient.Interface;

namespace UberClient.Internal
{
    public class CacheProvider : ICacheProvider
    {
        private readonly IDistributedCache _cache;

        public CacheProvider(IDistributedCache cache) => _cache = cache;

        public async Task ClearCacheAsync(string key) => await _cache.RemoveAsync(key);

        public async Task<T?> GetFromCacheAsync<T>(string key) where T : class
        {
            var cachedResponse = await _cache.GetStringAsync(key);
            return cachedResponse == null ? null : JsonSerializer.Deserialize<T>(cachedResponse);
        }

        public async Task SetCacheAsync<T>(string key, T value, DistributedCacheEntryOptions options) where T : class
        {
            var response = JsonSerializer.Serialize(value);
            await _cache.SetStringAsync(key, response, options);
        }
    }
}
