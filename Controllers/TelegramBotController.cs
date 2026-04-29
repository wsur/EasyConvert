using EasyConvert2.Convertations.Classes;
using EasyConvert2.Validation.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace EasyConvert2.Controllers
{
    [ApiController]
    [Route("api/update")]
    public class TelegramController(ITelegramBotClient botClient, ILogger<TelegramController> logger, IWebHostEnvironment env, IFileValidator imageValidator, IMemoryCache memoryCache) : ControllerBase
    {
        private readonly ITelegramBotClient _botClient = botClient;
        private readonly ILogger<TelegramController> _logger = logger;
        private readonly string _environment = env.EnvironmentName;
        private readonly IFileValidator ImageValidator = imageValidator;
        private readonly IMemoryCache _memoryCache = memoryCache;

        private string? ErrorMessage = null;

        private const string Scale2Action = "scale_2";
        private const string Scale4Action = "scale_4";
        private const string ScaleDownAction = "scale_down";
        private static readonly TimeSpan CachedImageLifetime = TimeSpan.FromMinutes(15);


        [HttpPost]
        public async Task<IActionResult> Post([FromBody] Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.Message != null)
                {

                    /*if (update.Type != UpdateType.Message || update.Message is not { } message)
                        return Ok();*/
                    var message = update.Message;

                    var chatId = message.Chat.Id;
                    Console.WriteLine($"Image from @{message.From?.Username} (ID: {message.From?.Id})");

                    Stream? imageStream = null;
                    string fileName;

                    var converter = new ConverterContext();

                    switch (message.Type)
                    {
                        case MessageType.Photo:
                            var photo = message.Photo?.LastOrDefault();
                            if (photo is null)
                                return await Reply(chatId, "Ошибка: не удалось получить фото.", cancellationToken);

                            if (ImageValidator.ValidateSize(photo.FileSize, out ErrorMessage) is false)
                                return await Reply(chatId, ErrorMessage, cancellationToken);

                            var file = await _botClient.GetFile(photo.FileId, cancellationToken);
                            imageStream = new MemoryStream();
                            await _botClient.DownloadFile(file.FilePath!, imageStream, cancellationToken);
                            imageStream.Seek(0, SeekOrigin.Begin);
                            fileName = "compressed_from_photo.jpg";
                            break;
                        case MessageType.Document:
                            if (ImageValidator.ValidateMimeType(message.Document?.MimeType, out ErrorMessage) is false)
                                return await Reply(chatId, ErrorMessage, cancellationToken);

                            if (ImageValidator.ValidateSize(message.Document!.FileSize, out ErrorMessage) is false)
                                return await Reply(chatId, ErrorMessage, cancellationToken);

                            file = await _botClient.GetFile(message.Document.FileId, cancellationToken);
                            var originalStream = new MemoryStream();
                            await _botClient.DownloadFile(file.FilePath!, originalStream, cancellationToken);
                            originalStream.Seek(0, SeekOrigin.Begin);

                            // Определим MIME-типа
                            var mimeType = message.Document.MimeType;

                            if (mimeType == "image/heic" || mimeType == "image/heif")
                            {
                                converter.InstallConverter(new HeicToJpgImageConverter());
                                // Преобразуем HEIC/HEIF в JPEG
                                imageStream = converter.Convert(originalStream, mimeType, out ErrorMessage);
                                if (imageStream == Stream.Null || !string.IsNullOrWhiteSpace(ErrorMessage))
                                {
                                    _logger.LogWarning("HEIC conversion failed: {ErrorMessage}", ErrorMessage);
                                    return await Reply(chatId,
                                        ErrorMessage ?? "Не удалось конвертировать HEIC изображение. Попробуйте другой формат.",
                                        cancellationToken);
                                }

                                fileName = "converted_from_heic.jpg";
                            }
                            else
                            {
                                // Если не HEIC — просто передаём оригинал
                                imageStream = originalStream;
                                fileName = "compressed_from_document.jpg";
                            }
                            break;
                        default:
                            return await Reply(chatId, "Пожалуйста, пришлите изображение как фото или файл.", cancellationToken);
                    }

                    await Reply(chatId, "Изображение получено, обрабатываю...", cancellationToken);

                    using var image = await Image.LoadAsync(imageStream, cancellationToken);

                    var operationId = Guid.NewGuid().ToString("N");
                    var jpegEncoder = new JpegEncoder { Quality = 100 };

                    using var sourceImageStream = new MemoryStream();
                    await image.SaveAsync(sourceImageStream, jpegEncoder, cancellationToken);
                    var sourceImageBytes = sourceImageStream.ToArray();

                    _memoryCache.Set(GetImageCacheKey(operationId), sourceImageBytes, CachedImageLifetime);

                    using var photoStream = new MemoryStream(sourceImageBytes);

                    await _botClient.SendPhoto(chatId, new InputFileStream(photoStream, fileName),
                        caption: L("Вот ваше изображение со сжатием. Для теста добавил кнопочки, тыкните любую", "Here is your compressed image."),
                        replyMarkup: CreateScaleKeyboard(operationId),
                        cancellationToken: cancellationToken);

                    using var docStream = new MemoryStream(sourceImageBytes);

                    await _botClient.SendDocument(chatId, new InputFileStream(docStream, fileName),
                        caption: L("Вот ваше изображение без сжатия.", "Here is your uncompressed image."),
                        replyMarkup: CreateScaleKeyboard(operationId),
                        cancellationToken: cancellationToken);

                    _logger.LogInformation(L("Изображение успешно пересжато и отправлено.", "Image compressed and sent."));
                }
                if (update.CallbackQuery != null)
                {
                    await HandleScaleCallback(update.CallbackQuery, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{L("Ошибка при обработке изображения", "Image processing error")}");
                if (update?.Message != null)
                {
                    await _botClient.SendMessage(update.Message.Chat.Id,
                        L("Ошибка при обработке изображения. Попробуйте снова.", "Error during processing. Try again."),
                        cancellationToken: cancellationToken);
                }
            }

            return Ok();

        }

        private async Task<IActionResult> Reply(long chatId, string message, CancellationToken token)
        {
            await _botClient.SendMessage(chatId, message, cancellationToken: token);
            return Ok();
        }

        private async Task HandleScaleCallback(CallbackQuery callbackQuery, CancellationToken cancellationToken)
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

            if (!TryParseScaleCallback(callbackQuery.Data, out var action, out var operationId, out var scaleFactor, out var scaleLabel))
            {
                await _botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    text: L("Неизвестное действие.", "Unknown action."),
                    cancellationToken: cancellationToken);
                return;
            }

            if (!_memoryCache.TryGetValue<byte[]>(GetImageCacheKey(operationId), out var sourceImageBytes) || sourceImageBytes is null)
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
                text: L($"Масштабирую изображение {scaleLabel}...", $"Scaling image {scaleLabel}..."),
                cancellationToken: cancellationToken);

            using var sourceStream = new MemoryStream(sourceImageBytes);
            using var image = await Image.LoadAsync(sourceStream, cancellationToken);

            var newWidth = Math.Max(1, (int)Math.Round(image.Width * scaleFactor));
            var newHeight = Math.Max(1, (int)Math.Round(image.Height * scaleFactor));

            image.Mutate(context => context.Resize(newWidth, newHeight, KnownResamplers.Lanczos3));

            using var scaledStream = new MemoryStream();
            await image.SaveAsync(scaledStream, new JpegEncoder { Quality = 100 }, cancellationToken);
            scaledStream.Seek(0, SeekOrigin.Begin);

            await _botClient.SendDocument(
                chatId,
                new InputFileStream(scaledStream, $"scaled_{action}.jpg"),
                caption: L($"Готово: изображение {scaleLabel}.", $"Done: image scaled {scaleLabel}."),
                cancellationToken: cancellationToken);
        }

        private static InlineKeyboardMarkup CreateScaleKeyboard(string operationId)
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("2x", CreateScaleCallbackData(Scale2Action, operationId)),
                    InlineKeyboardButton.WithCallbackData("4x", CreateScaleCallbackData(Scale4Action, operationId)),
                    InlineKeyboardButton.WithCallbackData("0.5x", CreateScaleCallbackData(ScaleDownAction, operationId))
                }
            });
        }

        private static string CreateScaleCallbackData(string action, string operationId)
            => $"{action}:{operationId}";

        private static string GetImageCacheKey(string operationId)
            => $"image:{operationId}";

        private static bool TryParseScaleCallback(
            string? callbackData,
            out string action,
            out string operationId,
            out double scaleFactor,
            out string scaleLabel)
        {
            action = string.Empty;
            operationId = string.Empty;
            scaleFactor = 0;
            scaleLabel = string.Empty;

            if (string.IsNullOrWhiteSpace(callbackData))
                return false;

            var parts = callbackData.Split(':', 2);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]))
                return false;

            action = parts[0];
            operationId = parts[1];

            switch (action)
            {
                case Scale2Action:
                    scaleFactor = 2;
                    scaleLabel = "2x";
                    return true;
                case Scale4Action:
                    scaleFactor = 4;
                    scaleLabel = "4x";
                    return true;
                case ScaleDownAction:
                    scaleFactor = 0.5;
                    scaleLabel = "0.5x";
                    return true;
                default:
                    return false;
            }
        }


        private string L(string ru, string en)
            => _environment == "Development" ? ru : en;
    }
}
