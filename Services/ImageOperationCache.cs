using Microsoft.Extensions.Caching.Memory;

namespace EasyConvert2.Services
{
    public class ImageOperationCache(IMemoryCache memoryCache)
    {
        private static readonly TimeSpan CachedImageLifetime = TimeSpan.FromMinutes(15);

        private readonly IMemoryCache _memoryCache = memoryCache;

        public string Store(byte[] imageBytes)
        {
            var operationId = Guid.NewGuid().ToString("N");
            _memoryCache.Set(GetImageCacheKey(operationId), imageBytes, CachedImageLifetime);

            return operationId;
        }

        public bool TryGet(string operationId, out byte[]? imageBytes)
        {
            return _memoryCache.TryGetValue(GetImageCacheKey(operationId), out imageBytes);
        }

        private static string GetImageCacheKey(string operationId)
            => $"image:{operationId}";
    }
}
