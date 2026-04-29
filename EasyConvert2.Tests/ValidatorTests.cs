using EasyConvert2.Validation.Classes;

namespace EasyConvert2.Tests;

public class ValidatorTests
{
    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/jpg")]
    [InlineData("image/heic")]
    [InlineData("image/heif")]
    [InlineData("IMAGE/JPEG")]
    public void ImageValidator_AcceptsSupportedMimeTypes(string mimeType)
    {
        var validator = new ImageValidator();

        var result = validator.ValidateMimeType(mimeType, out var errorMessage);

        Assert.True(result);
        Assert.Empty(errorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("image/png")]
    [InlineData("video/mp4")]
    public void ImageValidator_RejectsUnsupportedMimeTypes(string? mimeType)
    {
        var validator = new ImageValidator();

        var result = validator.ValidateMimeType(mimeType, out var errorMessage);

        Assert.False(result);
        Assert.NotEmpty(errorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(10L * 1024 * 1024 + 1)]
    public void ImageValidator_RejectsInvalidSizes(long? fileSize)
    {
        var validator = new ImageValidator();

        var result = validator.ValidateSize(fileSize, out var errorMessage);

        Assert.False(result);
        Assert.NotEmpty(errorMessage);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(10L * 1024 * 1024)]
    public void ImageValidator_AcceptsValidSizes(long fileSize)
    {
        var validator = new ImageValidator();

        var result = validator.ValidateSize(fileSize, out var errorMessage);

        Assert.True(result);
        Assert.Empty(errorMessage);
    }

    [Theory]
    [InlineData("video/mp4")]
    [InlineData("video/quicktime")]
    [InlineData("video/mpeg")]
    [InlineData("video/x-matroska")]
    [InlineData("VIDEO/MP4")]
    public void VideoValidator_AcceptsSupportedMimeTypes(string mimeType)
    {
        var validator = new VideoValidator();

        var result = validator.ValidateMimeType(mimeType, out var errorMessage);

        Assert.True(result);
        Assert.Empty(errorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("image/jpeg")]
    [InlineData("video/avi")]
    public void VideoValidator_RejectsUnsupportedMimeTypes(string? mimeType)
    {
        var validator = new VideoValidator();

        var result = validator.ValidateMimeType(mimeType, out var errorMessage);

        Assert.False(result);
        Assert.NotEmpty(errorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(20L * 1024 * 1024 + 1)]
    public void VideoValidator_RejectsInvalidSizes(long? fileSize)
    {
        var validator = new VideoValidator();

        var result = validator.ValidateSize(fileSize, out var errorMessage);

        Assert.False(result);
        Assert.NotEmpty(errorMessage);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(20L * 1024 * 1024)]
    public void VideoValidator_AcceptsValidSizes(long fileSize)
    {
        var validator = new VideoValidator();

        var result = validator.ValidateSize(fileSize, out var errorMessage);

        Assert.True(result);
        Assert.Empty(errorMessage);
    }
}
