using Microsoft.Extensions.Caching.Distributed;
using System.Threading;
using System.Threading.Tasks;

namespace UberClient.Server.Extensions.Cache
{
    //! \class DistributedCaching
    /*!
     * This class uses two methods SetAsync and GetAsync. The SetAsync method creates/updates a new object of type
     * <T>, serialization, and inserts it into the cache. The GetAsync method retrieves the object of type <T> 
     * from the cache and deserialization.
    */
    public static class DistributedCaching
    {
        public async static Task SetAsync<T>(this IDistributedCache distributedCache, string key, T value, DistributedCacheEntryOptions options, CancellationToken token = default(CancellationToken))
        {
            await distributedCache.SetAsync(key, value.ToByteArray(), options, token);
        }

        public async static Task<T> GetAsync<T>(this IDistributedCache distributedCache, string key, CancellationToken token = default(CancellationToken)) where T : class
        {
            var result = await distributedCache.GetAsync(key, token);
            return result.FromByteArray<T>();
        }
    }
}
