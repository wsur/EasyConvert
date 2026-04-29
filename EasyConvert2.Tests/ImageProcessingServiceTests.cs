using EasyConvert2.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace EasyConvert2.Tests;

public class ImageProcessingServiceTests
{
    [Fact]
    public async Task ScaleImageAsync_ReturnsResizedImage()
    {
        var service = CreateService();
        var sourceImageBytes = CreateJpegBytes(10, 5);
        var command = new ScaleCallbackCommand("scale_2", "operation-id", 2, "2x");

        var result = await service.ScaleImageAsync(sourceImageBytes, command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ImageBytes);
        Assert.Equal("scaled_scale_2.jpg", result.FileName);

        using var scaledImage = Image.Load(result.ImageBytes);
        Assert.Equal(20, scaledImage.Width);
        Assert.Equal(10, scaledImage.Height);
    }

    [Fact]
    public async Task ScaleImageAsync_ReturnsFailure_WhenOutputWouldBeTooLarge()
    {
        var service = CreateService();
        var sourceImageBytes = CreateJpegBytes(10, 10);
        var command = new ScaleCallbackCommand("scale_huge", "operation-id", 1000, "1000x");

        var result = await service.ScaleImageAsync(sourceImageBytes, command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.ImageBytes);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotEmpty(result.ErrorMessage);
    }

    private static ImageProcessingService CreateService()
    {
        return new ImageProcessingService(
            botClient: null!,
            imageValidator: null!,
            logger: NullLogger<ImageProcessingService>.Instance);
    }

    private static byte[] CreateJpegBytes(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();

        image.Save(stream, new JpegEncoder { Quality = 100 });

        return stream.ToArray();
    }
}
