namespace AquilaLocal;

public sealed record AvatarChoice(string Key, string Label)
{
	public override string ToString()
	{
		return Label;
	}
}
