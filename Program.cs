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
			// ������������ ���-����������
			webBuilder.ConfigureServices(services =>
			{
				// ����������� ��������
				services.AddControllersWithViews(); // ������: ��������� ��������� MVC
			})
			.Configure((context, app) =>
			{
				// ������������ pipeline ��������� ��������
				if (context.HostingEnvironment.IsDevelopment())
				{
					app.UseDeveloperExceptionPage(); // �������� ������ � ������ ����������
				}
				else
				{
					app.UseExceptionHandler("/Home/Error"); // ��������� ������ � ��������
					app.UseHsts();
				}

				app.UseHttpsRedirection();
				app.UseStaticFiles();
				app.UseRouting();

				// ��������� ���������
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
				?? throw new InvalidOperationException("TelegramBot:Token �� ������ � ������������");

			Console.WriteLine("TOKEN: " + (token?.Substring(0, 5) ?? "null"));

			botClient = new TelegramBotClient(token);

			// ��������� Serilog
			Log.Logger = new LoggerConfiguration()
				.WriteTo.Console()  // ����������� � ������� (����� Serilog.Sinks.Console)
				.WriteTo.File("app.log", rollingInterval: RollingInterval.Day)  // ����������� � ����
				.CreateLogger();

			// ���������� Serilog ��� �����������
			logger = LoggerFactory.Create(builder =>
			{
				builder.AddSerilog(); // ��������� Serilog ��� �����
			}).CreateLogger<Program>();
		}

			public static async Task Main(string[] args)
		{
			// �������� � ������ ����� ��� ASP.NET
			var host = CreateHostBuilder(args).Build();

			// ������ Telegram ���� � ��������� ������
			var cts = new CancellationTokenSource();
			var receiverOptions = new ReceiverOptions
			{
				AllowedUpdates = [UpdateType.Message] // ��������� ������ ���������
			};

			// ������ ��������� ���������� � ��������� ������
			_ = Task.Run(() =>
			{
				botClient.StartReceiving(
					updateHandler: BotClient_OnUpdate,
					errorHandler: BotClient_OnError,
					receiverOptions: receiverOptions,
					cancellationToken: cts.Token
				);
			});

			logger.LogInformation("��� �������. ������� Ctrl+C ��� ���������...");

			// ������� ������� Ctrl+C ��� ���������� ������
			var tcs = new TaskCompletionSource();
			Console.CancelKeyPress += (_, e) =>
			{
				e.Cancel = true;
				tcs.SetResult();
				logger.LogInformation("��� ���������� �������������.");
			};

			// ��������� ���-���������� � ��� ����������
			await host.RunAsync();

			// ��������� ��������� ���������� ����� ���������
			cts.Cancel();
			logger.LogInformation("��� ����������.");
		}

		private static Task BotClient_OnError(
			ITelegramBotClient client,
			Exception exception,
			HandleErrorSource source,
			CancellationToken token)
		{
			logger.LogError($"������: {exception.GetType().Name} � {exception.Message}");
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
			Console.WriteLine($"�������� ���� �� @{update.Message.From?.Username} (ID: {update.Message.From?.Id})");

			try
			{
				var photo = update.Message.Photo?.LastOrDefault();
				if (photo is null)
				{
					await botClient.SendMessage(chatId, "������: �� ������� �������� ����.", cancellationToken: token);
					return;
				}

				// �������� �� ������ �����
				if (photo.FileSize > 10 * 1024 * 1024) // 10 MB
				{
					await botClient.SendMessage(chatId, "��������, ���� ������� �������. ������������ ������ � 10 ��.", cancellationToken: token);
					return;
				}

				var file = await botClient.GetFile(photo.FileId, cancellationToken: token);

				// �������� �� null � ���������� �����
				using var downloadStream = new MemoryStream();
				await botClient.DownloadFile(
					file.FilePath ?? throw new InvalidOperationException("file.FilePath is null"),
					downloadStream,
					token);

				// �������� ������������, ��� ��������� ��������
				await botClient.SendMessage(chatId, "���������� ��������, �����������...", cancellationToken: token);

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

				Console.WriteLine("���� ������� ���������� � ���������� ������������.");
			}
			catch (Exception ex)
			{
				// �������� ������
				Console.WriteLine($"������ ��� ��������� ����: {ex.Message}");
				await botClient.SendMessage(chatId, "������ ��� ��������� ����������. ���������� �����.", cancellationToken: token);
				logger.LogError($"������ ��� ��������� ����: {ex.Message}");
			}
		}
	}
}
