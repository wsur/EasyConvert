using Telegram.Bot;
using Telegram.Bot.Types;

namespace EasyConvert2.Services
{
    public class TelegramUpdateHandler(
        ITelegramBotClient botClient,
        ImageProcessingService imageProcessingService,
        VideoProcessingService videoProcessingService,
        ImageOperationCache imageOperationCache,
        VideoOperationCache videoOperationCache,
        ScaleKeyboardFactory scaleKeyboardFactory,
        ILogger<TelegramUpdateHandler> logger,
        IWebHostEnvironment env)
    {
        private readonly ITelegramBotClient _botClient = botClient;
        private readonly ImageProcessingService _imageProcessingService = imageProcessingService;
        private readonly VideoProcessingService _videoProcessingService = videoProcessingService;
        private readonly ImageOperationCache _imageOperationCache = imageOperationCache;
        private readonly VideoOperationCache _videoOperationCache = videoOperationCache;
        private readonly ScaleKeyboardFactory _scaleKeyboardFactory = scaleKeyboardFactory;
        private readonly ILogger<TelegramUpdateHandler> _logger = logger;
        private readonly string _environment = env.EnvironmentName;

        public async Task HandleAsync(Update update, CancellationToken cancellationToken)
        {
            if (update.Message is not null)
            {
                await HandleMessageAsync(update.Message, cancellationToken);
            }

            if (update.CallbackQuery is not null)
            {
                await HandleScaleCallbackAsync(update.CallbackQuery, cancellationToken);
            }
        }

        public async Task SendProcessingErrorAsync(Update update, CancellationToken cancellationToken)
        {
            if (update.Message is null)
                return;

            await _botClient.SendMessage(
                update.Message.Chat.Id,
                L("Ошибка при обработке изображения. Попробуйте снова.", "Error during processing. Try again."),
                cancellationToken: cancellationToken);
        }

        private async Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            _logger.LogInformation(
                "Media from @{Username} (ID: {UserId})",
                message.From?.Username,
                message.From?.Id);

            if (_videoProcessingService.CanProcess(message))
            {
                await HandleVideoMessageAsync(message, cancellationToken);
                return;
            }

            var imageResult = await _imageProcessingService.PrepareImageAsync(message, cancellationToken);
            if (!imageResult.IsSuccess || imageResult.ImageBytes is null || imageResult.FileName is null)
            {
                await Reply(chatId, imageResult.ErrorMessage ?? "Не удалось обработать изображение.", cancellationToken);
                return;
            }

            await Reply(chatId, "Изображение получено, обрабатываю...", cancellationToken);

            var operationId = _imageOperationCache.Store(imageResult.ImageBytes);
            var keyboard = _scaleKeyboardFactory.Create(operationId);

            using var photoStream = new MemoryStream(imageResult.ImageBytes);
            await _botClient.SendPhoto(
                chatId,
                new InputFileStream(photoStream, imageResult.FileName),
                caption: L("Вот ваше изображение со сжатием. Для теста добавил кнопочки, тыкните любую", "Here is your compressed image."),
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);

            using var docStream = new MemoryStream(imageResult.ImageBytes);
            await _botClient.SendDocument(
                chatId,
                new InputFileStream(docStream, imageResult.FileName),
                caption: L("Вот ваше изображение без сжатия.", "Here is your uncompressed image."),
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);

            _logger.LogInformation(L("Изображение успешно пересжато и отправлено.", "Image compressed and sent."));
        }

        private async Task HandleVideoMessageAsync(Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var videoResult = await _videoProcessingService.PrepareVideoAsync(message, cancellationToken);
            if (!videoResult.IsSuccess || videoResult.VideoPath is null)
            {
                await Reply(chatId, videoResult.ErrorMessage ?? "Не удалось обработать видео.", cancellationToken);
                return;
            }

            var operationId = _videoOperationCache.Store(videoResult.VideoPath);

            await _botClient.SendMessage(
                chatId,
                L("Видео получено. Сейчас доступно уменьшение 0.5x.", "Video received. 0.5x downscale is available."),
                replyMarkup: _scaleKeyboardFactory.CreateVideo(operationId),
                cancellationToken: cancellationToken);
        }

        private async Task HandleScaleCallbackAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            var chatId = callbackQuery.Message?.Chat.Id;
            if (chatId is null)
            {
                await _botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    text: L("Не удалось определить чат.", "Could not detect the chat."),
                    cancellationToken: cancellationToken);
                return;
            }

            if (!_scaleKeyboardFactory.TryParse(callbackQuery.Data, out var command))
            {
                await _botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    text: L("Неизвестное действие.", "Unknown action."),
                    cancellationToken: cancellationToken);
                return;
            }

            if (command.IsVideo)
            {
                await HandleVideoScaleCallbackAsync(chatId.Value, callbackQuery, command, cancellationToken);
                return;
            }

            if (!_imageOperationCache.TryGet(command.OperationId, out var sourceImageBytes) || sourceImageBytes is null)
            {
                var expiredMessage = L(
                    "Срок действия изображения истек. Отправьте фото еще раз.",
                    "The image expired. Send the photo again.");

                await _botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    text: expiredMessage,
                    cancellationToken: cancellationToken);

                await _botClient.SendMessage(chatId, expiredMessage, cancellationToken: cancellationToken);
                return;
            }

            await _botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                text: L($"Масштабирую изображение {command.ScaleLabel}...", $"Scaling image {command.ScaleLabel}..."),
                cancellationToken: cancellationToken);

            var scaledImageResult = await _imageProcessingService.ScaleImageAsync(sourceImageBytes, command, cancellationToken);
            if (!scaledImageResult.IsSuccess || scaledImageResult.ImageBytes is null || scaledImageResult.FileName is null)
            {
                await _botClient.SendMessage(
                    chatId,
                    scaledImageResult.ErrorMessage ?? L("Не удалось масштабировать изображение.", "Could not scale the image."),
                    cancellationToken: cancellationToken);
                return;
            }

            using var scaledStream = new MemoryStream(scaledImageResult.ImageBytes);
            await _botClient.SendDocument(
                chatId,
                new InputFileStream(scaledStream, scaledImageResult.FileName),
                caption: L($"Готово: изображение {command.ScaleLabel}.", $"Done: image scaled {command.ScaleLabel}."),
                cancellationToken: cancellationToken);
        }

        private async Task HandleVideoScaleCallbackAsync(
            long chatId,
            CallbackQuery callbackQuery,
            ScaleCallbackCommand command,
            CancellationToken cancellationToken)
        {
            if (!_videoOperationCache.TryGet(command.OperationId, out var sourceVideoPath) || sourceVideoPath is null)
            {
                var expiredMessage = L(
                    "Срок действия видео истек. Отправьте видео еще раз.",
                    "The video expired. Send the video again.");

                await _botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    text: expiredMessage,
                    cancellationToken: cancellationToken);

                await _botClient.SendMessage(chatId, expiredMessage, cancellationToken: cancellationToken);
                return;
            }

            await _botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                text: L("Уменьшаю видео 0.5x...", "Downscaling video 0.5x..."),
                cancellationToken: cancellationToken);

            var scaledVideoResult = await _videoProcessingService.DownscaleVideoAsync(sourceVideoPath, cancellationToken);
            if (!scaledVideoResult.IsSuccess || scaledVideoResult.VideoPath is null || scaledVideoResult.FileName is null)
            {
                await _botClient.SendMessage(
                    chatId,
                    scaledVideoResult.ErrorMessage ?? L("Не удалось уменьшить видео.", "Could not downscale the video."),
                    cancellationToken: cancellationToken);
                return;
            }

            await using var scaledStream = File.OpenRead(scaledVideoResult.VideoPath);
            await _botClient.SendDocument(
                chatId,
                new InputFileStream(scaledStream, scaledVideoResult.FileName),
                caption: L("Готово: видео уменьшено 0.5x.", "Done: video downscaled 0.5x."),
                cancellationToken: cancellationToken);

            _videoOperationCache.DeleteFile(scaledVideoResult.VideoPath);
        }

        private async Task Reply(long chatId, string message, CancellationToken token)
        {
            await _botClient.SendMessage(chatId, message, cancellationToken: token);
        }

        private string L(string ru, string en)
            => _environment == "Development" ? ru : en;
    }
}
