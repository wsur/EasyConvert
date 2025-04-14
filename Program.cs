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
	using Microsoft.AspNetCore.Hosting;
	using Microsoft.Extensions.Hosting;
	using Serilog;
	using System;
	using System.IO;
	using System.Threading;
	using System.Threading.Tasks;
	using System.Collections;
	using System.Linq;

	class Program
	{
		private static readonly TelegramBotClient botClient;
		private static readonly ILogger<Program> logger;

		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureWebHostDefaults(webBuilder =>
				{
					// Указываем адрес и порт для Render
					webBuilder.UseUrls($"http://0.0.0.0:{GetPort()}");

					webBuilder.ConfigureServices(services =>
					{
						services.AddControllersWithViews();
					})
					.Configure((context, app) =>
					{
						if (context.HostingEnvironment.IsDevelopment())
						{
							app.UseDeveloperExceptionPage();
						}
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

			logger.LogInformation("Бот запущен. Нажмите Ctrl+C для остановки...");

			var tcs = new TaskCompletionSource();
			Console.CancelKeyPress += (_, e) =>
			{
				e.Cancel = true;
				tcs.SetResult();
				logger.LogInformation("Бот остановлен пользователем.");
			};

			await host.RunAsync();

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

		private static async Task BotClient_OnUpdate(
			ITelegramBotClient client,
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

				if (photo.FileSize > 10 * 1024 * 1024)
				{
					await botClient.SendMessage(chatId, "Извините, файл слишком большой. Максимальный размер — 10 МБ.", cancellationToken: token);
					return;
				}

				var file = await botClient.GetFile(photo.FileId, cancellationToken: token);

				using var downloadStream = new MemoryStream();
				await botClient.DownloadFile(file.FilePath ?? throw new InvalidOperationException("file.FilePath is null"), downloadStream, token);

				await botClient.SendMessage(chatId, "Фотография получена, обрабатываю...", cancellationToken: token);

				downloadStream.Seek(0, SeekOrigin.Begin);

				using var image = SixLabors.ImageSharp.Image.Load(downloadStream);
				using var outputStream = new MemoryStream();

				image.Save(outputStream, new JpegEncoder());
				outputStream.Seek(0, SeekOrigin.Begin);

				var fileToSend = new InputFileStream(outputStream, "converted_image.jpg");

				await botClient.SendPhoto(chatId, fileToSend, cancellationToken: token);

				Console.WriteLine("Фото успешно обработано и отправлено пользователю.");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка при обработке фото: {ex.Message}");
				await botClient.SendMessage(chatId, "Ошибка при обработке фотографии. Попробуйте снова.", cancellationToken: token);
				logger.LogError($"Ошибка при обработке фото: {ex.Message}");
			}
		}
	}
}
