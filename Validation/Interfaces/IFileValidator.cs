namespace EasyConvert2.Validation.Interfaces
{
	public interface IFileValidator
	{
		bool ValidateSize(long? fileSize, out string errorMessage);
		bool ValidateMimeType(string? mimeType, out string errorMessage);
	}
}
