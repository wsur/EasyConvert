using EasyConvert2.Validation.Interfaces;

namespace EasyConvert2.Validation.Classes
{
	public class ImageValidator : IFileValidator
	{
		private static readonly HashSet<string> allowedMimeTypes = [
			"image/heic",
			"image/heif",
			"image/jpg",
			"image/jpeg",
			];

		private const long maxFileSize = 10 * 1024 * 1024;// 10 мб в байтах
		private const int maxFileSizeInMB = 10;

		public bool ValidateMimeType(string? mimeType, out string errorMessage)
		{
			if(mimeType is null)
			{
				errorMessage = $"No Image file type is provided. Allowed formats: {String.Join(", ", allowedMimeTypes)}";
				return false;
			}
			if (!allowedMimeTypes.Contains(mimeType.ToLowerInvariant()))
			{
				errorMessage = $"Image file type is not supported: {mimeType}. Allowed formats: {String.Join(", ", allowedMimeTypes)}";
				return false;
			}

			errorMessage = string.Empty;

			return true;
		}

		public bool ValidateSize(long? fileSize, out string errorMessage)
		{

			if (fileSize is null or <= 0)
			{
				errorMessage = "Файл пустой.";
				return false;
			}

			if (fileSize > maxFileSize)
			{
				errorMessage = $"Файл слишком большой. Максимум — {maxFileSizeInMB} МБ.";
				return false;
			}

			errorMessage = string.Empty;

			return true;
		}
	}
}
