using EasyConvert2.Services;
using Microsoft.Extensions.Caching.Memory;

namespace EasyConvert2.Tests;

public class ImageOperationCacheTests
{
    [Fact]
    public void Store_ThenTryGet_ReturnsStoredBytes()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new ImageOperationCache(memoryCache);
        var imageBytes = new byte[] { 1, 2, 3 };

        var operationId = cache.Store(imageBytes);
        var found = cache.TryGet(operationId, out var cachedBytes);

        Assert.True(found);
        Assert.NotNull(cachedBytes);
        Assert.Equal(imageBytes, cachedBytes);
    }

    [Fact]
    public void TryGet_ReturnsFalse_ForUnknownOperation()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new ImageOperationCache(memoryCache);

        var found = cache.TryGet("missing", out var cachedBytes);

        Assert.False(found);
        Assert.Null(cachedBytes);
    }
}
