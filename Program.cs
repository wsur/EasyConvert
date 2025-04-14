using Serilog;
using SixLabors.ImageSharp.Formats.Jpeg;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types;
using Telegram.Bot;
using System.Collections;

class Program
{
	private static readonly TelegramBotClient botClient;
	private static readonly ILogger<Program> logger;

	public static IHostBuilder CreateHostBuilder(string[] args) =>
		Host.CreateDefaultBuilder(args)
			.ConfigureWebHostDefaults(webBuilder =>
			{
				webBuilder.UseUrls($"http://0.0.0.0:{GetPort()}");

				webBuilder.ConfigureServices(services =>
				{
					services.AddControllersWithViews();
				})
				.Configure((context, app) =>
				{
					var isDevelopment = context.HostingEnvironment.IsDevelopment();

					if (isDevelopment)
						app.UseDeveloperExceptionPage();
					else
					{
						app.UseExceptionHandler("/Home/Error");
						app.UseHsts();
					}

					app.UseHttpsRedirection();
					app.UseStaticFiles();
					app.UseRouting();

					app.UseEndpoints(endpoints =>
					{
						endpoints.MapControllerRoute(
							name: "default",
							pattern: "{controller=Home}/{action=Index}/{id?}");
					});
				});
			});

	private static string GetPort()
	{
		var port = Environment.GetEnvironmentVariable("PORT");
		return string.IsNullOrEmpty(port) ? "8080" : port;
	}

	static Program()
	{
		Console.OutputEncoding = System.Text.Encoding.Unicode;

		foreach (var (key, value) in Environment.GetEnvironmentVariables().Cast<DictionaryEntry>())
		{
			Console.WriteLine($"{key} = {value}");
		}

		var configuration = new ConfigurationBuilder()
			.AddEnvironmentVariables()
			.Build();

		var token = configuration["TelegramBot:Token"]
			?? throw new InvalidOperationException("TelegramBot:Token не найден в конфигурации");

		Console.WriteLine("TOKEN: " + (!string.IsNullOrEmpty(token) ? token[..Math.Min(5, token.Length)] : "null"));

		botClient = new TelegramBotClient(token);

		Log.Logger = new LoggerConfiguration()
			.WriteTo.Console()
			.WriteTo.File("app.log", rollingInterval: RollingInterval.Day, encoding: System.Text.Encoding.Unicode)
			.CreateLogger();

		logger = LoggerFactory.Create(builder =>
		{
			builder.AddSerilog();
		}).CreateLogger<Program>();
	}

	public static async Task Main(string[] args)
	{
		var host = CreateHostBuilder(args).Build();

		var cts = new CancellationTokenSource();
		var receiverOptions = new ReceiverOptions
		{
			AllowedUpdates = [UpdateType.Message]
		};

		_ = Task.Run(() =>
		{
			botClient.StartReceiving(
				updateHandler: BotClient_OnUpdate,
				errorHandler: BotClient_OnError,
				receiverOptions: receiverOptions,
				cancellationToken: cts.Token
			);
		});

		logger.LogInformation(L("Бот запущен. Нажмите Ctrl+C для остановки...", "Bot started. Press Ctrl+C to stop..."));

		var tcs = new TaskCompletionSource();
		Console.CancelKeyPress += (_, e) =>
		{
			e.Cancel = true;
			tcs.SetResult();
			logger.LogInformation(L("Бот остановлен пользователем.", "Bot stopped by user."));
		};

		await host.StartAsync(); // просто запускаем хост, но не ждём завершения

		await tcs.Task; // ждём Ctrl+C

		cts.Cancel();

		await host.StopAsync(); // корректно останавливаем хост

		logger.LogInformation(L("Бот остановлен.", "Bot stopped."));
	}


	private static Task BotClient_OnError(
		ITelegramBotClient client,
		Exception exception,
		HandleErrorSource source,
		CancellationToken token)
	{
		logger.LogError($"{L("Ошибка", "Error")}: {exception.GetType().Name} — {exception.Message}");
		if (exception is ApiRequestException apiEx)
			logger.LogError($"Telegram API Error:\n[{apiEx.ErrorCode}]\n{apiEx.Message}");

		return Task.CompletedTask;
	}

	private static async Task BotClient_OnUpdate(
	ITelegramBotClient client,
	Update update,
	CancellationToken token)
	{
		if (update.Type != UpdateType.Message || update.Message is not { } message)
			return;

		var chatId = message.Chat.Id;
		Console.WriteLine($"Image from @{message.From?.Username} (ID: {message.From?.Id})");

		try
		{
			Stream? imageStream = null;
			string fileName;

			if (message.Type == MessageType.Photo)
			{
				var photo = message.Photo?.LastOrDefault();
				if (photo is null)
				{
					await botClient.SendMessage(chatId, L("Ошибка: не удалось получить фото.", "Error: failed to get the photo."), cancellationToken: token);
					return;
				}

				if (photo.FileSize > 10 * 1024 * 1024)
				{
					await botClient.SendMessage(chatId, L("Файл слишком большой. Максимум — 10 МБ.", "File too large. Max is 10 MB."), cancellationToken: token);
					return;
				}

				var file = await botClient.GetFile(photo.FileId, cancellationToken: token);
				imageStream = new MemoryStream();
				await botClient.DownloadFile(file.FilePath!, imageStream, token);
				imageStream.Seek(0, SeekOrigin.Begin);

				fileName = "compressed_from_photo.jpg";
			}
			else if (message.Type == MessageType.Document && message.Document?.MimeType?.StartsWith("image/") == true)
			{
				if (message.Document.FileSize > 10 * 1024 * 1024)
				{
					await botClient.SendMessage(chatId, L("Файл слишком большой. Максимум — 10 МБ.", "File too large. Max is 10 MB."), cancellationToken: token);
					return;
				}

				var file = await botClient.GetFile(message.Document.FileId, cancellationToken: token);
				imageStream = new MemoryStream();
				await botClient.DownloadFile(file.FilePath!, imageStream, token);
				imageStream.Seek(0, SeekOrigin.Begin);

				fileName = "compressed_from_document.jpg";
			}
			else
			{
				await botClient.SendMessage(chatId, L("Пожалуйста, пришлите изображение как фото или файл.", "Please send an image as a photo or file."), cancellationToken: token);
				return;
			}

			await botClient.SendMessage(chatId, L("Изображение получено, обрабатываю...", "Image received, processing..."), cancellationToken: token);

			using var image = SixLabors.ImageSharp.Image.Load(imageStream);

			// Первый поток — для SendPhoto
			using var photoStream = new MemoryStream();

			image.Save(photoStream, new JpegEncoder
			{
				Quality = 100
			});

			photoStream.Seek(0, SeekOrigin.Begin);
			var photoToSend = new InputFileStream(photoStream, fileName);
			await botClient.SendPhoto(
				chatId: chatId,
				photo: photoToSend,
				caption: L("Вот ваше изображение со сжатием.", "Here is your compressed image."),
				cancellationToken: token
			);

			// Второй поток — для SendDocument
			using var docStream = new MemoryStream();

			image.Save(docStream, new JpegEncoder
			{
				Quality = 100
			});
			docStream.Seek(0, SeekOrigin.Begin);
			var docToSend = new InputFileStream(docStream, fileName);
			await botClient.SendDocument(
				chatId: chatId,
				document: docToSend,
				caption: L("Вот ваше изображение без сжатия.", "Here is your uncompressed image."),
				cancellationToken: token
			);

			logger.LogInformation(L("Изображение успешно пересжато и отправлено.", "Image compressed and sent."));
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Process error: {ex.Message}");
			await botClient.SendMessage(chatId, L("Ошибка при обработке изображения. Попробуйте снова.", "Error during processing. Try again."), cancellationToken: token);
			logger.LogError($"{L("Ошибка при обработке изображения", "Image processing error")}: {ex}");
		}
	}


	private static string L(string ru, string en) => Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" ? ru : en;
}
