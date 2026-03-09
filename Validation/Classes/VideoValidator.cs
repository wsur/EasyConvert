using EasyConvert2.Validation.Interfaces;

namespace EasyConvert2.Validation.Classes
{
    public class VideoValidator : IFileValidator
    {
        /// <summary>
        /// максимальный размер видео в мб (для телеграм бота)
        /// </summary>
        private const int MaxFileSizeInMb = 20;

        private const int MaxFileSize = 20 * 1024 * 10424;

        /// <summary>
        /// поддерживаемые типы видео у телеграм ботов
        /// </summary>
        private static readonly HashSet<string> allowedMimeTypes = [
            "video/mp4",
            "video/quicktime",
            "video/mpeg",
            "video/x-matroska"
            ];

        public bool ValidateMimeType(string? mimeType, out string errorMessage)
        {
            if (mimeType is null)
            {
                errorMessage = $"No Image file type is provided. Allowed formats: {String.Join(", ", allowedMimeTypes)}";
                return false;
            }
            if (!allowedMimeTypes.Contains(mimeType!.ToLower()))
            {
                errorMessage = $"Image file type is not supported: {mimeType}. Allowed formats: {String.Join(", ", allowedMimeTypes)}";
                return false;
            }

            errorMessage = string.Empty;

            return true;
        }

        public bool ValidateSize(long? fileSize, out string errorMessage)
        {
            if (fileSize <= 0)
            {
                errorMessage = "Файл пустой.";
                return false;
            }

            if (fileSize > MaxFileSize)
            {
                errorMessage = $"Файл слишком большой. Максимум — {MaxFileSizeInMb} МБ.";
                return false;
            }

            errorMessage = string.Empty;

            return true;
        }
    }
}
