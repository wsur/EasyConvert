namespace EasyConvert2
{
	using Telegram.Bot;
	using Telegram.Bot.Types;
	using Telegram.Bot.Types.Enums;
	using Telegram.Bot.Polling;
	using Telegram.Bot.Exceptions;
	using SixLabors.ImageSharp.Formats.Jpeg;
	using Microsoft.Extensions.Configuration;
	using Microsoft.Extensions.Logging;
	using Serilog;
	using System;
	using System.IO;
	using System.Threading;
	using System.Threading.Tasks;

	class Program
	{
		private static readonly TelegramBotClient botClient;
		private static readonly ILogger<Program> logger;

		static Program()
		{
			var configuration = new ConfigurationBuilder()
				.SetBasePath(Directory.GetCurrentDirectory())
				.AddJsonFile("appsettings.json")
				.Build();

			// Вытаскиваем токен из переменной окружения для безопасности
			var token = configuration["TelegramBot:Token"] ?? throw new InvalidOperationException("Токен бота не найден в переменных окружения");
			botClient = new TelegramBotClient(token);

			// Настройка Serilog
			Log.Logger = new LoggerConfiguration()
				.WriteTo.Console()  // Логирование в консоль (пакет Serilog.Sinks.Console)
				.WriteTo.File("app.log", rollingInterval: RollingInterval.Day)  // Логирование в файл
				.CreateLogger();

			// Используем Serilog для логирования
			logger = LoggerFactory.Create(builder =>
			{
				builder.AddSerilog(); // Добавляем Serilog как логер
			}).CreateLogger<Program>();
		}

		static async Task Main(string[] args)
		{
			var cts = new CancellationTokenSource();

			// Используем ReceiverOptions для конфигурации получения обновлений
			var receiverOptions = new ReceiverOptions
			{
				AllowedUpdates = [UpdateType.Message] // Принимаем только сообщения
			};

			// Запуск получения обновлений в отдельном потоке
			botClient.StartReceiving(
				updateHandler: BotClient_OnUpdate,
				errorHandler: BotClient_OnError,
				receiverOptions: receiverOptions,
				cancellationToken: cts.Token
			);

			// Логируем старт бота
			logger.LogInformation("Бот запущен. Нажмите Ctrl+C для остановки...");

			// Ожидаем нажатие Ctrl+C для завершения работы
			var tcs = new TaskCompletionSource();
			Console.CancelKeyPress += (_, e) =>
			{
				e.Cancel = true;
				tcs.SetResult();
				logger.LogInformation("Бот остановлен пользователем.");
			};

			await tcs.Task; // Ждём завершения работы

			cts.Cancel(); // Завершаем получение обновлений
			logger.LogInformation("Бот остановлен.");
		}

		private static Task BotClient_OnError(
			ITelegramBotClient client,
			Exception exception,
			HandleErrorSource source,
			CancellationToken token)
		{
			logger.LogError($"Ошибка: {exception.GetType().Name} — {exception.Message}");
			if (exception is ApiRequestException apiEx)
				logger.LogError($"Telegram API Error:\n[{apiEx.ErrorCode}]\n{apiEx.Message}");

			return Task.CompletedTask;
		}

		private static async Task BotClient_OnUpdate(ITelegramBotClient client,
												  Update update,
												  CancellationToken token)
		{
			if (update.Type != UpdateType.Message || update.Message?.Type != MessageType.Photo)
				return;

			var chatId = update.Message.Chat.Id;
			Console.WriteLine($"Получено фото от @{update.Message.From?.Username} (ID: {update.Message.From?.Id})");

			try
			{
				var photo = update.Message.Photo?.LastOrDefault();
				if (photo is null)
				{
					await botClient.SendMessage(chatId, "Ошибка: не удалось получить фото.", cancellationToken: token);
					return;
				}

				// Проверка на размер файла
				if (photo.FileSize > 10 * 1024 * 1024) // 10 MB
				{
					await botClient.SendMessage(chatId, "Извините, файл слишком большой. Максимальный размер — 10 МБ.", cancellationToken: token);
					return;
				}

				var file = await botClient.GetFile(photo.FileId, cancellationToken: token);

				// Проверка на null и скачивание файла
				using var downloadStream = new MemoryStream();
				await botClient.DownloadFile(
					file.FilePath ?? throw new InvalidOperationException("file.FilePath is null"),
					downloadStream,
					token);

				// Сообщаем пользователю, что обработка началась
				await botClient.SendMessage(chatId, "Фотография получена, обрабатываю...", cancellationToken: token);

				downloadStream.Seek(0, SeekOrigin.Begin);

				using var image = SixLabors.ImageSharp.Image.Load(downloadStream);
				using var outputStream = new MemoryStream();

				image.Save(outputStream, new JpegEncoder());
				outputStream.Seek(0, SeekOrigin.Begin);

				var fileToSend = new InputFileStream(outputStream, "converted_image.jpg");

				await botClient.SendPhoto(
					chatId,
					fileToSend,
					cancellationToken: token);

				Console.WriteLine("Фото успешно обработано и отправлено пользователю.");
			}
			catch (Exception ex)
			{
				// Логируем ошибку
				Console.WriteLine($"Ошибка при обработке фото: {ex.Message}");
				await botClient.SendMessage(chatId, "Ошибка при обработке фотографии. Попробуйте снова.", cancellationToken: token);
				logger.LogError($"Ошибка при обработке фото: {ex.Message}");
			}
		}
	}
}
