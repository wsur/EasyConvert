using EasyConvert2.Convertations.Classes;
using EasyConvert2.Validation.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace EasyConvert2.Services
{
    public class ImageProcessingService(
        ITelegramBotClient botClient,
        IFileValidator imageValidator,
        ILogger<ImageProcessingService> logger)
    {
        private const int JpegQuality = 100;
        private const long MaxOutputPixels = 4096L * 4096L;

        private readonly ITelegramBotClient _botClient = botClient;
        private readonly IFileValidator _imageValidator = imageValidator;
        private readonly ILogger<ImageProcessingService> _logger = logger;

        public async Task<ImageProcessingResult> PrepareImageAsync(Message message, CancellationToken cancellationToken)
        {
            Stream? imageStream = null;

            try
            {
                var fileName = string.Empty;

                switch (message.Type)
                {
                    case MessageType.Photo:
                        var photo = message.Photo?.LastOrDefault();
                        if (photo is null)
                            return ImageProcessingResult.Failure("Ошибка: не удалось получить фото.");

                        if (!_imageValidator.ValidateSize(photo.FileSize, out var photoSizeError))
                            return ImageProcessingResult.Failure(photoSizeError);

                        var photoFile = await _botClient.GetFile(photo.FileId, cancellationToken);
                        imageStream = await DownloadTelegramFileAsync(photoFile.FilePath, cancellationToken);
                        fileName = "compressed_from_photo.jpg";
                        break;

                    case MessageType.Document:
                        var document = message.Document;
                        if (document is null)
                            return ImageProcessingResult.Failure("Ошибка: не удалось получить файл.");

                        if (!_imageValidator.ValidateMimeType(document.MimeType, out var mimeTypeError))
                            return ImageProcessingResult.Failure(mimeTypeError);

                        if (!_imageValidator.ValidateSize(document.FileSize, out var documentSizeError))
                            return ImageProcessingResult.Failure(documentSizeError);

                        var documentFile = await _botClient.GetFile(document.FileId, cancellationToken);
                        var originalStream = await DownloadTelegramFileAsync(documentFile.FilePath, cancellationToken);
                        var mimeType = document.MimeType ?? string.Empty;

                        if (IsHeic(mimeType))
                        {
                            imageStream = ConvertHeicToJpeg(originalStream, out var conversionError);
                            originalStream.Dispose();

                            if (imageStream == Stream.Null || !string.IsNullOrWhiteSpace(conversionError))
                            {
                                _logger.LogWarning("HEIC conversion failed: {ErrorMessage}", conversionError);
                                return ImageProcessingResult.Failure(
                                    conversionError ?? "Не удалось конвертировать HEIC изображение. Попробуйте другой формат.");
                            }

                            fileName = "converted_from_heic.jpg";
                        }
                        else
                        {
                            imageStream = originalStream;
                            fileName = "compressed_from_document.jpg";
                        }

                        break;

                    default:
                        return ImageProcessingResult.Failure("Пожалуйста, пришлите изображение как фото или файл.");
                }

                using var image = await Image.LoadAsync(imageStream, cancellationToken);
                var imageBytes = await SaveAsJpegAsync(image, cancellationToken);

                return ImageProcessingResult.Success(imageBytes, fileName);
            }
            finally
            {
                imageStream?.Dispose();
            }
        }

        public async Task<ImageProcessingResult> ScaleImageAsync(
            byte[] sourceImageBytes,
            ScaleCallbackCommand command,
            CancellationToken cancellationToken)
        {
            using var sourceStream = new MemoryStream(sourceImageBytes);
            using var image = await Image.LoadAsync(sourceStream, cancellationToken);

            var newWidth = Math.Max(1, (int)Math.Round(image.Width * command.ScaleFactor));
            var newHeight = Math.Max(1, (int)Math.Round(image.Height * command.ScaleFactor));
            var outputPixels = (long)newWidth * newHeight;

            if (outputPixels > MaxOutputPixels)
            {
                return ImageProcessingResult.Failure(
                    "Результат получится слишком большим. Попробуйте меньший масштаб или отправьте изображение поменьше.");
            }

            image.Mutate(context => context.Resize(newWidth, newHeight, KnownResamplers.Lanczos3));
            var imageBytes = await SaveAsJpegAsync(image, cancellationToken);

            return ImageProcessingResult.Success(imageBytes, $"scaled_{command.Action}.jpg");
        }

        private async Task<MemoryStream> DownloadTelegramFileAsync(string? filePath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new InvalidOperationException("Telegram file path is empty.");

            var stream = new MemoryStream();
            await _botClient.DownloadFile(filePath, stream, cancellationToken);
            stream.Seek(0, SeekOrigin.Begin);

            return stream;
        }

        private static Stream ConvertHeicToJpeg(Stream originalStream, out string? errorMessage)
        {
            var converter = new ConverterContext();
            converter.InstallConverter(new HeicToJpgImageConverter());

            return converter.Convert(originalStream, "image/heic", out errorMessage);
        }

        private static async Task<byte[]> SaveAsJpegAsync(Image image, CancellationToken cancellationToken)
        {
            using var outputStream = new MemoryStream();
            await image.SaveAsync(outputStream, new JpegEncoder { Quality = JpegQuality }, cancellationToken);

            return outputStream.ToArray();
        }

        private static bool IsHeic(string mimeType)
        {
            return mimeType.Equals("image/heic", StringComparison.OrdinalIgnoreCase)
                || mimeType.Equals("image/heif", StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed record ImageProcessingResult(
        bool IsSuccess,
        byte[]? ImageBytes,
        string? FileName,
        string? ErrorMessage)
    {
        public static ImageProcessingResult Success(byte[] imageBytes, string fileName)
            => new(true, imageBytes, fileName, null);

        public static ImageProcessingResult Failure(string errorMessage)
            => new(false, null, null, errorMessage);
    }
}
