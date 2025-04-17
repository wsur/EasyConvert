using Microsoft.AspNetCore.Mvc;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace EasyConvert2.Controllers
{
	[ApiController]
	[Route("api/update")]
	public class TelegramController(ITelegramBotClient botClient, ILogger<TelegramController> logger, IWebHostEnvironment env) : ControllerBase
	{
		private readonly ITelegramBotClient _botClient = botClient;
		private readonly ILogger<TelegramController> _logger = logger;
		private readonly string _environment = env.EnvironmentName;

		[HttpPost]
		public async Task<IActionResult> Post([FromBody] Update update, CancellationToken cancellationToken)
		{
			try
			{
				if (update.Type != UpdateType.Message || update.Message is not { } message)
					return Ok();

				var chatId = message.Chat.Id;
				Console.WriteLine($"Image from @{message.From?.Username} (ID: {message.From?.Id})");

				Stream? imageStream = null;
				string fileName;

				if (message.Type == MessageType.Photo)
				{
					var photo = message.Photo?.LastOrDefault();
					if (photo is null)
						return await Reply(chatId, "Ошибка: не удалось получить фото.", cancellationToken);

					if (photo.FileSize > 10 * 1024 * 1024)
						return await Reply(chatId, "Файл слишком большой. Максимум — 10 МБ.", cancellationToken);

					var file = await _botClient.GetFile(photo.FileId, cancellationToken);
					imageStream = new MemoryStream();
					await _botClient.DownloadFile(file.FilePath!, imageStream, cancellationToken);
					imageStream.Seek(0, SeekOrigin.Begin);
					fileName = "compressed_from_photo.jpg";
				}
				else if (message.Type == MessageType.Document && message.Document?.MimeType?.StartsWith("image/") == true)
				{
					if (message.Document.FileSize > 10 * 1024 * 1024)
						return await Reply(chatId, "Файл слишком большой. Максимум — 10 МБ.", cancellationToken);

					var file = await _botClient.GetFile(message.Document.FileId, cancellationToken);
					imageStream = new MemoryStream();
					await _botClient.DownloadFile(file.FilePath!, imageStream, cancellationToken);
					imageStream.Seek(0, SeekOrigin.Begin);
					fileName = "compressed_from_document.jpg";
				}
				else
				{
					return await Reply(chatId, "Пожалуйста, пришлите изображение как фото или файл.", cancellationToken);
				}

				await Reply(chatId, "Изображение получено, обрабатываю...", cancellationToken);

				using var image = Image.Load(imageStream);

				using var photoStream = new MemoryStream();
				image.Save(photoStream, new JpegEncoder { Quality = 100 });
				photoStream.Seek(0, SeekOrigin.Begin);
				await _botClient.SendPhoto(chatId, new InputFileStream(photoStream, fileName),
					caption: L("Вот ваше изображение со сжатием.", "Here is your compressed image."),
					cancellationToken: cancellationToken);

				using var docStream = new MemoryStream();
				image.Save(docStream, new JpegEncoder { Quality = 100 });
				docStream.Seek(0, SeekOrigin.Begin);
				await _botClient.SendDocument(chatId, new InputFileStream(docStream, fileName),
					caption: L("Вот ваше изображение без сжатия.", "Here is your uncompressed image."),
					cancellationToken: cancellationToken);

				_logger.LogInformation(L("Изображение успешно пересжато и отправлено.", "Image compressed and sent."));
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

		private string L(string ru, string en)
			=> _environment == "Development" ? ru : en;
	}
}
