using EasyConvert2.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyConvert2.Tests;

public class VideoOperationCacheTests
{
    [Fact]
    public void Store_ThenTryGet_ReturnsStoredVideoPath()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new VideoOperationCache(memoryCache, NullLogger<VideoOperationCache>.Instance);
        var videoPath = Path.GetTempFileName();

        try
        {
            var operationId = cache.Store(videoPath);
            var found = cache.TryGet(operationId, out var cachedPath);

            Assert.True(found);
            Assert.Equal(videoPath, cachedPath);
        }
        finally
        {
            cache.DeleteFile(videoPath);
        }
    }

    [Fact]
    public void TryGet_ReturnsFalse_ForMissingVideoFile()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new VideoOperationCache(memoryCache, NullLogger<VideoOperationCache>.Instance);

        var operationId = cache.Store(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4"));
        var found = cache.TryGet(operationId, out var cachedPath);

        Assert.False(found);
        Assert.NotNull(cachedPath);
    }

    [Fact]
    public void CreateTemporaryVideoPath_ReturnsPathInEasyConvertVideoDirectory()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new VideoOperationCache(memoryCache, NullLogger<VideoOperationCache>.Instance);

        var path = cache.CreateTemporaryVideoPath();

        Assert.EndsWith(".mp4", path);
        Assert.Contains("EasyConvertVideos", path);
    }
}
