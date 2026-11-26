namespace AquilaLocal;

public sealed class Attachment
{
	public string Name { get; set; } = "";

	public string Kind { get; set; } = "text";

	public string Text { get; set; } = "";

	public string MimeType { get; set; } = "";

	public string Base64 { get; set; } = "";

	public long Bytes { get; set; }

	public string Label
	{
		get
		{
			if (Kind == "image")
			{
				return $"{Name} · image · {(double)Bytes / 1024.0:0} KB";
			}
			return $"{Name} · {Text.Length:N0} characters";
		}
	}

	public string ContentDigest { get; set; } = "";
}
