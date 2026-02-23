using Serilog;
using SixLabors.ImageSharp.Formats.Jpeg;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types;
using Telegram.Bot;
using System.Collections;
using EasyConvert2.Validation.Interfaces;
using EasyConvert2.Validation.Classes;

class Program
{
	private static readonly TelegramBotClient botClient;
	private static readonly ILogger<Program> logger;

	public static IHostBuilder CreateHostBuilder(string[] args) =>
	Host.CreateDefaultBuilder(args)
		.ConfigureWebHostDefaults(webBuilder =>
		{
			webBuilder.UseUrls($"http://0.0.0.0:{GetPort()}");

			webBuilder.ConfigureServices((context, services) =>
			{
				var configuration = context.Configuration;
				var token = configuration["TelegramBot:Token"]
					?? throw new InvalidOperationException("TelegramBot:Token не найден в конфигурации");

				services.AddControllersWithViews();

				services.AddSingleton<ITelegramBotClient>(_ =>
					new TelegramBotClient(token));

				////////////////////////////регистрация самописных классов////////////////////////////////////
				
				services.AddSingleton<IImageValidator, ImageValidator>();//валидация для фотографий

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

					endpoints.MapControllers();
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

		var token = "";

#if DEBUG
		var builder = WebApplication.CreateBuilder();

        token = builder.Configuration["TelegramBot:Token"];
#else
		token = configuration["TelegramBot:Token"]
			?? throw new InvalidOperationException("TelegramBot:Token не найден в конфигурации");
#endif

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

		logger.LogInformation(L("Бот запущен. Нажмите Ctrl+C для остановки...", "Bot started. Press Ctrl+C to stop..."));

		var tcs = new TaskCompletionSource();
		Console.CancelKeyPress += (_, e) =>
		{
			e.Cancel = true;
			tcs.SetResult();
			logger.LogInformation(L("Бот остановлен пользователем.", "Bot stopped by user."));
		};

		await host.StartAsync(); // просто запускаем хост, но не ждём завершения

		// Устанавливаем Webhook
		var baseUrl = Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL")
					 ?? throw new Exception("RENDER_EXTERNAL_URL не задан");

		var webhookUrl = $"{baseUrl}/api/update";

		await botClient.SetWebhook(webhookUrl);

		logger.LogInformation("Webhook установлен: " + webhookUrl);


		await tcs.Task; // ждём Ctrl+C

		cts.Cancel();

		await host.StopAsync(); // корректно останавливаем хост

		logger.LogInformation(L("Бот остановлен.", "Bot stopped."));
	}


	private static string L(string ru, string en) => Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" ? ru : en;
}
