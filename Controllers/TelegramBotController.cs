using EasyConvert2.Services;
using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Types;

namespace EasyConvert2.Controllers
{
    [ApiController]
    [Route("api/update")]
    public class TelegramController(TelegramUpdateHandler updateHandler, ILogger<TelegramController> logger) : ControllerBase
    {
        private readonly TelegramUpdateHandler _updateHandler = updateHandler;
        private readonly ILogger<TelegramController> _logger = logger;

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] Update update, CancellationToken cancellationToken)
        {
            try
            {
                await _updateHandler.HandleAsync(update, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Image processing error");
                await _updateHandler.SendProcessingErrorAsync(update, cancellationToken);
            }

            return Ok();
        }
    }
}
