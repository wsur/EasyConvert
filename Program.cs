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

		public static IHostBuilder CreateHostBuilder(string[] args) =>
		Host.CreateDefaultBuilder(args)
		.ConfigureWebHostDefaults(webBuilder =>
		{
			// Конфигурация веб-приложения
			webBuilder.ConfigureServices(services =>
			{
				// Регистрация сервисов
				services.AddControllersWithViews(); // Пример: добавляем поддержку MVC
			})
			.Configure((context, app) =>
			{
				// Конфигурация pipeline обработки запросов
				if (context.HostingEnvironment.IsDevelopment())
				{
					app.UseDeveloperExceptionPage(); // Страница ошибок в режиме разработки
				}
				else
				{
					app.UseExceptionHandler("/Home/Error"); // Обработка ошибок в продакшн
					app.UseHsts();
				}

				app.UseHttpsRedirection();
				app.UseStaticFiles();
				app.UseRouting();

				// Настройка маршрутов
				app.UseEndpoints(endpoints =>
				{
					endpoints.MapControllerRoute(
						name: "default",
						pattern: "{controller=Home}/{action=Index}/{id?}");
				});
			});
		});

		static Program()
		{

			var configuration = new ConfigurationBuilder()
				.SetBasePath(Directory.GetCurrentDirectory())
				.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
				.AddEnvironmentVariables()
				.Build();

			var token = configuration["TelegramBot:Token"]
				?? throw new InvalidOperationException("TelegramBot:Token не найден в конфигурации");

			Console.WriteLine("TOKEN: " + (token?.Substring(0, 5) ?? "null"));

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

			public static async Task Main(string[] args)
		{
			// Создание и запуск хоста для ASP.NET
			var host = CreateHostBuilder(args).Build();

			// Запуск Telegram бота в отдельном потоке
			var cts = new CancellationTokenSource();
			var receiverOptions = new ReceiverOptions
			{
				AllowedUpdates = [UpdateType.Message] // Принимаем только сообщения
			};

			// Запуск получения обновлений в отдельном потоке
			_ = Task.Run(() =>
			{
				botClient.StartReceiving(
					updateHandler: BotClient_OnUpdate,
					errorHandler: BotClient_OnError,
					receiverOptions: receiverOptions,
					cancellationToken: cts.Token
				);
			});

			logger.LogInformation("Бот запущен. Нажмите Ctrl+C для остановки...");

			// Ожидаем нажатие Ctrl+C для завершения работы
			var tcs = new TaskCompletionSource();
			Console.CancelKeyPress += (_, e) =>
			{
				e.Cancel = true;
				tcs.SetResult();
				logger.LogInformation("Бот остановлен пользователем.");
			};

			// Запускаем веб-приложение и ждём завершения
			await host.RunAsync();

			// Завершаем получение обновлений после остановки
			cts.Cancel();
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
