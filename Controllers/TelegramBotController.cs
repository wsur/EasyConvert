using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types;
using Telegram.Bot;

namespace EasyConvert2.Controllers
{
	[ApiController]
	[Route("api/update")]
	public class TelegramController : ControllerBase
	{
		private readonly TelegramBotClient _botClient;

		public TelegramController(IConfiguration config)
		{
			var token = config["TelegramBot:Token"];
			_botClient = new TelegramBotClient(token);
		}

		[HttpPost]
		public async Task<IActionResult> Post([FromBody] Update update)
		{
			if (update.Type == UpdateType.Message && update.Message is { } message)
			{
				var chatId = message.Chat.Id;
				await _botClient.SendMessage(chatId, "Привет! Я получил твоё сообщение через Webhook 🎉");
			}

			return Ok();
		}
	}
}
