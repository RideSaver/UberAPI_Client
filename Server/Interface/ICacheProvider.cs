using Microsoft.Extensions.Caching.Distributed;

namespace UberClient.Interface
{
    public interface ICacheProvider
    {
        Task<T?> GetFromCacheAsync<T>(string key) where T : class;
        Task SetCacheAsync<T>(string key, T value, DistributedCacheEntryOptions options) where T : class;
        Task ClearCacheAsync(string key);
    }
}
