using EasyConvert2.Convertations.Interfaces;
using ImageMagick;

namespace EasyConvert2.Convertations.Classes
{
	public class HeicToJpgImageConverter : IImageConverter
	{
		public bool CanConvert(string mimeType)
		{
			return mimeType.Equals("image/heic", StringComparison.OrdinalIgnoreCase)
			|| mimeType.Equals("image/heif", StringComparison.OrdinalIgnoreCase);
		}

		public Stream Convert(Stream inputStream, out string? ErrorMessage)
		{
			try
			{
				// Преобразуем HEIC/HEIF в JPEG
				using var image = new MagickImage(inputStream);

				image.Format = MagickFormat.Jpeg;
				image.Quality = 100;

				var output = new MemoryStream();
				image.Write(output);
				output.Seek(0, SeekOrigin.Begin);

				ErrorMessage = null;

				return output;
			}
			catch (Exception)
			{
				//не передаём и не сохраняем ошибку, которая выпала. Выдаём только кастомную.
				ErrorMessage = "Ошибка при конвертации HEIC изображения. Попробуйте другой формат.";
				return Stream.Null;
			}
		}
	}
}
