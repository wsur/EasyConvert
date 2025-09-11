namespace EasyConvert2.Convertations.Interfaces
{
	public interface IImageConverter
	{
		bool CanConvert(string mimeType);
		Stream Convert(Stream InputStream, out string? ErrorMessage);

	}
}
