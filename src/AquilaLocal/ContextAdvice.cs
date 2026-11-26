namespace AquilaLocal;

public sealed record ContextAdvice(int Recommended, int Danger, int NativeMaximum, string Note)
{
	public bool FitsMinimum { get; init; }

	public bool FitsNativeMaximum { get; init; }
}
