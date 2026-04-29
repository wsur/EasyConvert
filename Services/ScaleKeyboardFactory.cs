using Telegram.Bot.Types.ReplyMarkups;

namespace EasyConvert2.Services
{
    public class ScaleKeyboardFactory
    {
        private const string Scale2Action = "scale_2";
        private const string Scale4Action = "scale_4";
        private const string ScaleDownAction = "scale_down";

        public InlineKeyboardMarkup Create(string operationId)
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

        public bool TryParse(
            string? callbackData,
            out ScaleCallbackCommand command)
        {
            command = ScaleCallbackCommand.Empty;

            if (string.IsNullOrWhiteSpace(callbackData))
                return false;

            var parts = callbackData.Split(':', 2);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]))
                return false;

            var action = parts[0];
            var operationId = parts[1];

            command = action switch
            {
                Scale2Action => new ScaleCallbackCommand(action, operationId, 2, "2x"),
                Scale4Action => new ScaleCallbackCommand(action, operationId, 4, "4x"),
                ScaleDownAction => new ScaleCallbackCommand(action, operationId, 0.5, "0.5x"),
                _ => ScaleCallbackCommand.Empty
            };

            return !command.IsEmpty;
        }

        private static string CreateScaleCallbackData(string action, string operationId)
            => $"{action}:{operationId}";
    }

    public sealed record ScaleCallbackCommand(
        string Action,
        string OperationId,
        double ScaleFactor,
        string ScaleLabel)
    {
        public static ScaleCallbackCommand Empty { get; } = new(string.Empty, string.Empty, 0, string.Empty);

        public bool IsEmpty => string.IsNullOrWhiteSpace(Action);
    }
}
