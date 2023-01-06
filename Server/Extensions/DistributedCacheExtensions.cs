using Microsoft.Extensions.Caching.Distributed;
using System.Text;
using System.Text.Json;

namespace UberClient.Extensions
{
    public static class DistributedCacheExtensions
    {
        public static void Set<T>(this IDistributedCache cache, string key, T value)
        {
            Set(cache, key, value, new DistributedCacheEntryOptions());
        }
        public static void Set<T>(this IDistributedCache cache, string key, T value, DistributedCacheEntryOptions options)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            cache.Set(key, bytes, options);
        }
        public static Task SetAsync<T>(this IDistributedCache cache, string key, T value) 
        {
            return SetAsync(cache, key, value, new DistributedCacheEntryOptions());
        }
        public static Task SetAsync<T>(this IDistributedCache cache, string key, T value, DistributedCacheEntryOptions options) 
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Serialization.GetJsonSerializerOptions()));
            return cache.SetAsync(key, bytes, options);
        }
        public async static Task<T?> GetAsync<T>(this IDistributedCache cache, string key) 
        {
            var byteResult = await cache.GetAsync(key);
            if (byteResult is null) return default;

            return JsonSerializer.Deserialize<T>(byteResult, Serialization.GetJsonSerializerOptions());
        }
    }
}
