using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AquilaLocal;

public sealed record HardwareInventory(long RamBytes, long FreeRamBytes, InferenceGpu? Gpu)
{
	public const long GiB = 1073741824L;

	private static readonly Regex DeviceLine = new Regex("^\\s*(Vulkan\\d+):\\s+(.+?)\\s+\\((\\d+) MiB, (\\d+) MiB free\\)\\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

	public static async Task<HardwareInventory> DetectAsync(string executable)
	{
		List<GpuAdapter> adapters = GpuMonitor.GetAdapters();
		string listing = "";
		if (File.Exists(executable))
		{
			using Process process = new Process
			{
				StartInfo = new ProcessStartInfo(executable, "--list-devices")
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					WorkingDirectory = Path.GetDirectoryName(executable)
				}
			};
			Engine.ClearInheritedArgs(process.StartInfo);
			try
			{
				using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6.0));
				if (process.Start())
				{
					Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
					Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
					await process.WaitForExitAsync(timeout.Token);
					if (process.ExitCode == 0)
					{
						listing = await stdout + "\n" + await stderr;
					}
				}
			}
			catch (Exception ex) when (((ex is IOException || ex is InvalidOperationException || ex is Win32Exception || ex is OperationCanceledException) ? 1 : 0) != 0)
			{
				try
				{
					if (!process.HasExited)
					{
						process.Kill(entireProcessTree: true);
					}
				}
				catch (Exception ex2) when (((ex2 is InvalidOperationException || ex2 is Win32Exception) ? 1 : 0) != 0)
				{
				}
			}
		}
		var (ramBytes, freeRamBytes) = GpuMonitor.SystemMemory;
		return new HardwareInventory(ramBytes, freeRamBytes, SelectGpu(adapters, listing));
	}

	internal static InferenceGpu? SelectGpu(IEnumerable<GpuAdapter> adapters, string listing)
	{
		return (from Match match in DeviceLine.Matches(listing)
			let name = match.Groups[2].Value.Trim()
			from adapter in adapters
			where adapter.DedicatedBytes >= 1073741824 && adapter.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)
			let free = long.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) * 1024 * 1024
			orderby adapter.DedicatedBytes descending, free descending
			select new InferenceGpu(match.Groups[1].Value, name, adapter.Key, adapter.DedicatedBytes, Math.Min(free, adapter.DedicatedBytes))).FirstOrDefault();
	}

	[CompilerGenerated]
	private HardwareInventory(HardwareInventory original)
	{
		RamBytes = original.RamBytes;
		FreeRamBytes = original.FreeRamBytes;
		Gpu = original.Gpu;
	}
}
