using Telegram.Bot;
using Telegram.Bot.Types;

namespace EasyConvert2.Services
{
    public class TelegramUpdateHandler(
        ITelegramBotClient botClient,
        ImageProcessingService imageProcessingService,
        ImageOperationCache imageOperationCache,
        ScaleKeyboardFactory scaleKeyboardFactory,
        ILogger<TelegramUpdateHandler> logger,
        IWebHostEnvironment env)
    {
        private readonly ITelegramBotClient _botClient = botClient;
        private readonly ImageProcessingService _imageProcessingService = imageProcessingService;
        private readonly ImageOperationCache _imageOperationCache = imageOperationCache;
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
                "Image from @{Username} (ID: {UserId})",
                message.From?.Username,
                message.From?.Id);

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

        private async Task Reply(long chatId, string message, CancellationToken token)
        {
            await _botClient.SendMessage(chatId, message, cancellationToken: token);
        }

        private string L(string ru, string en)
            => _environment == "Development" ? ru : en;
    }
}
