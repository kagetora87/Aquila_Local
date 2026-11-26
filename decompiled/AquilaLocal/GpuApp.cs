namespace AquilaLocal;

public sealed record GpuApp(string Name, string AdapterKey, double Utilization, long LocalBytes, string Pids)
{
	public string UsageLabel => $"{Utilization:0.0}%";

	public string MemoryLabel
	{
		get
		{
			if (LocalBytes >= 1073741824)
			{
				return $"{(double)LocalBytes / 1073741824.0:0.00} GB";
			}
			return $"{(double)LocalBytes / 1048576.0:0} MB";
		}
	}
}
