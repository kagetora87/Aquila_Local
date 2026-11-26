namespace AquilaLocal;

public sealed record GpuAdapter(string Name, string Key, long DedicatedBytes, int Index)
{
	public string Label => Name;

	public override string ToString()
	{
		return Name;
	}
}
