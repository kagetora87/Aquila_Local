namespace AquilaLocal;

public sealed class SpriteAvatarOptions
{
	public bool Enabled { get; set; } = true;

	/// <summary>Folder holding one subfolder per animation. Empty = Data\\avatar.</summary>
	public string Folder { get; set; } = "";

	/// <summary>0 = only obvious moments, 1 = very expressive.</summary>
	public double Sensitivity { get; set; } = 0.5;

	public bool PoppedOut { get; set; }

	public double Left { get; set; } = double.NaN;

	public double Top { get; set; } = double.NaN;

	public int Height { get; set; } = 320;

	public bool Topmost { get; set; } = true;
}
