using EasyConvert2.Validation.Classes;
using FFMpegCore;
using FFMpegCore.Enums;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace EasyConvert2.Services
{
    public class VideoProcessingService(
        ITelegramBotClient botClient,
        VideoValidator videoValidator,
        VideoOperationCache videoOperationCache)
    {
        private readonly ITelegramBotClient _botClient = botClient;
        private readonly VideoValidator _videoValidator = videoValidator;
        private readonly VideoOperationCache _videoOperationCache = videoOperationCache;

        public bool CanProcess(Message message)
        {
            return message.Type == MessageType.Video
                || message.Document?.MimeType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true;
        }

        public async Task<VideoProcessingResult> PrepareVideoAsync(Message message, CancellationToken cancellationToken)
        {
            var fileId = string.Empty;
            var fileName = "video.mp4";
            string? mimeType;
            long? fileSize;

            if (message.Type == MessageType.Video && message.Video is not null)
            {
                fileId = message.Video.FileId;
                mimeType = message.Video.MimeType ?? "video/mp4";
                fileSize = message.Video.FileSize;
                fileName = message.Video.FileName ?? fileName;
            }
            else if (message.Document is not null)
            {
                fileId = message.Document.FileId;
                mimeType = message.Document.MimeType;
                fileSize = message.Document.FileSize;
                fileName = message.Document.FileName ?? fileName;
            }
            else
            {
                return VideoProcessingResult.Failure("Ошибка: не удалось получить видео.");
            }

            if (!_videoValidator.ValidateMimeType(mimeType, out var mimeTypeError))
                return VideoProcessingResult.Failure(mimeTypeError);

            if (!_videoValidator.ValidateSize(fileSize, out var sizeError))
                return VideoProcessingResult.Failure(sizeError);

            var file = await _botClient.GetFile(fileId, cancellationToken);
            var inputPath = _videoOperationCache.CreateTemporaryVideoPath(Path.GetExtension(fileName));

            await using var inputStream = File.Create(inputPath);
            await _botClient.DownloadFile(file.FilePath!, inputStream, cancellationToken);

            return VideoProcessingResult.Success(inputPath, fileName);
        }

        public async Task<VideoProcessingResult> DownscaleVideoAsync(string inputPath, CancellationToken cancellationToken)
        {
            var outputPath = _videoOperationCache.CreateTemporaryVideoPath(".mp4");

            try
            {
                await FFMpegArguments
                    .FromFileInput(inputPath)
                    .OutputToFile(outputPath, overwrite: true, options => options
                        .WithVideoCodec(VideoCodec.LibX264)
                        .WithAudioCodec(AudioCodec.Aac)
                        .WithCustomArgument("-vf \"scale=trunc(iw*0.5/2)*2:trunc(ih*0.5/2)*2\"")
                        .WithFastStart())
                    .ProcessAsynchronously(false, new FFOptions
                    {
                        TemporaryFilesFolder = Path.GetTempPath()
                    });

                return VideoProcessingResult.Success(outputPath, "downscaled_video.mp4");
            }
            catch
            {
                _videoOperationCache.DeleteFile(outputPath);
                return VideoProcessingResult.Failure("Не удалось уменьшить видео. Попробуйте другой файл.");
            }
        }
    }

    public sealed record VideoProcessingResult(
        bool IsSuccess,
        string? VideoPath,
        string? FileName,
        string? ErrorMessage)
    {
        public static VideoProcessingResult Success(string videoPath, string fileName)
            => new(true, videoPath, fileName, null);

        public static VideoProcessingResult Failure(string errorMessage)
            => new(false, null, null, errorMessage);
    }
}
