using Microsoft.Extensions.Caching.Memory;

namespace EasyConvert2.Services
{
    public class VideoOperationCache(IMemoryCache memoryCache, ILogger<VideoOperationCache> logger)
    {
        private static readonly TimeSpan CachedVideoLifetime = TimeSpan.FromMinutes(15);

        private readonly IMemoryCache _memoryCache = memoryCache;
        private readonly ILogger<VideoOperationCache> _logger = logger;

        public string Store(string videoPath)
        {
            var operationId = Guid.NewGuid().ToString("N");
            _memoryCache.Set(GetVideoCacheKey(operationId), videoPath, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CachedVideoLifetime
            }.RegisterPostEvictionCallback((_, value, _, _) =>
            {
                if (value is string path)
                    DeleteFile(path);
            }));

            return operationId;
        }

        public bool TryGet(string operationId, out string? videoPath)
        {
            return _memoryCache.TryGetValue(GetVideoCacheKey(operationId), out videoPath)
                && !string.IsNullOrWhiteSpace(videoPath)
                && File.Exists(videoPath);
        }

        public string CreateTemporaryVideoPath(string extension = ".mp4")
        {
            var directory = Path.Combine(Path.GetTempPath(), "EasyConvertVideos");
            Directory.CreateDirectory(directory);

            return Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
        }

        public void DeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete temporary video file: {Path}", path);
            }
        }

        private static string GetVideoCacheKey(string operationId)
            => $"video:{operationId}";
    }
}
