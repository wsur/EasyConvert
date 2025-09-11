using EasyConvert2.Convertations.Interfaces;

namespace EasyConvert2.Convertations.Classes
{
	public class ConverterContext
	{
		public IImageConverter ImageConverter { private get; set; }

		public void InstallConverter(IImageConverter imageConverter)
		{
			ImageConverter = imageConverter;
		}

		public Stream Convert(Stream inputStream, string mimeType, out string? ErrorMessage)
		{
			if (ImageConverter.CanConvert(mimeType))
			{
				return ImageConverter.Convert(inputStream, out ErrorMessage);
			}
			else
			{
				ErrorMessage = "Не найден конвертер для данного файла.";
				return Stream.Null;//возвращаем пустой поток, если конвертер не нашёлся.
			}
		}
	}
}
