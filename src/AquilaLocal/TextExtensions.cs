namespace AquilaLocal;

public static class TextExtensions
{
	public static string Truncate(this string value, int max)
	{
		if (value.Length > max)
		{
			return value.Substring(0, max) + "…";
		}
		return value;
	}
}
