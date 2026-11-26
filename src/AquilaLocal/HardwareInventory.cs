using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AquilaLocal;

public sealed record HardwareInventory(long RamBytes, long FreeRamBytes, InferenceGpu? Gpu)
{
	public const long GiB = 1073741824L;

	private static readonly Regex DeviceLine = new Regex("^\\s*(Vulkan\\d+):\\s+(.+?)\\s+\\((\\d+) MiB, (\\d+) MiB free\\)\\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

	/// <summary>Where detection writes what it saw, so "no GPU found" can be diagnosed.</summary>
	public static string? LogPath { get; set; }

	public static async Task<HardwareInventory> DetectAsync(string executable)
	{
		// A laptop's discrete GPU can be asleep or switching modes at startup, which made a single
		// 6-second attempt report "no GPU". Allow more time and retry before giving up.
		StringBuilder log = new StringBuilder();
		List<GpuAdapter> adapters = new List<GpuAdapter>();
		string listing = "";
		InferenceGpu gpu = null;
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			adapters = GpuMonitor.GetAdapters();
			listing = await ListDevicesAsync(executable, log);
			gpu = SelectGpu(adapters, listing);
			log.AppendLine($"Attempt {attempt}: adapters [{string.Join("; ", adapters.Select((GpuAdapter a) => $"{a.Name} {a.DedicatedBytes / 1048576} MiB"))}]");
			log.AppendLine("Engine devices:\n" + listing.Trim());
			log.AppendLine("Selected: " + (gpu?.ToString() ?? "none"));
			if (gpu != null || !adapters.Any((GpuAdapter a) => a.DedicatedBytes >= MinimumDedicated))
			{
				break;
			}
			await Task.Delay(2000);
		}
		WriteLog(log.ToString());
		var (ramBytes, freeRamBytes) = GpuMonitor.SystemMemory;
		return new HardwareInventory(ramBytes, freeRamBytes, gpu);
	}

	private static async Task<string> ListDevicesAsync(string executable, StringBuilder log)
	{
		if (!File.Exists(executable))
		{
			log.AppendLine("Engine not found: " + executable);
			return "";
		}
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
			using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20.0));
			if (!process.Start())
			{
				return "";
			}
			Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
			Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
			await process.WaitForExitAsync(timeout.Token);
			string text = await stdout + "\n" + await stderr;
			if (process.ExitCode != 0)
			{
				log.AppendLine($"--list-devices exited with {process.ExitCode}");
			}
			// Parse the output even on a non-zero exit: the device lines are still valid.
			return text;
		}
		catch (Exception ex) when (((ex is IOException || ex is InvalidOperationException || ex is Win32Exception || ex is OperationCanceledException) ? 1 : 0) != 0)
		{
			log.AppendLine("--list-devices failed: " + ex.GetType().Name + " " + ex.Message);
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
			return "";
		}
	}

	private static void WriteLog(string text)
	{
		try
		{
			if (LogPath != null)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
				File.WriteAllText(LogPath, DateTime.Now.ToString("u") + "\n" + text);
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
		}
	}

	/// <summary>Integrated GPUs report well under this as dedicated memory.</summary>
	private const long MinimumDedicated = 2L * GiB;

	private static string Normalize(string name)
	{
		string text = name.ToLowerInvariant().Replace("(tm)", "").Replace("(r)", "");
		return new string(text.Where(char.IsLetterOrDigit).ToArray());
	}

	internal static InferenceGpu? SelectGpu(IEnumerable<GpuAdapter> adapters, string listing)
	{
		List<GpuAdapter> discrete = adapters.Where((GpuAdapter a) => a.DedicatedBytes >= MinimumDedicated).ToList();
		var devices = (from Match match in DeviceLine.Matches(listing)
			select new
			{
				Device = match.Groups[1].Value,
				Name = match.Groups[2].Value.Trim(),
				Total = long.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) * 1024 * 1024,
				Free = long.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) * 1024 * 1024
			}).ToList();
		// Match engine devices to Windows adapters by name, tolerating "(TM)", spacing and suffixes.
		var matched = (from d in devices
			from a in discrete
			let dn = Normalize(d.Name)
			let an = Normalize(a.Name)
			where dn.Length > 0 && an.Length > 0 && (dn == an || dn.Contains(an) || an.Contains(dn))
			orderby a.DedicatedBytes descending, d.Free descending
			select new InferenceGpu(d.Device, d.Name, a.Key, a.DedicatedBytes, Math.Min(d.Free, a.DedicatedBytes))).FirstOrDefault();
		if (matched != null)
		{
			return matched;
		}
		// Names differ (driver naming quirks): if there is exactly one discrete adapter, pick the
		// engine device whose memory size is closest to it, within 15%.
		if (discrete.Count == 1)
		{
			GpuAdapter only = discrete[0];
			var closest = devices.OrderBy(d => Math.Abs(d.Total - only.DedicatedBytes)).FirstOrDefault();
			if (closest != null && Math.Abs(closest.Total - only.DedicatedBytes) <= only.DedicatedBytes * 15 / 100)
			{
				return new InferenceGpu(closest.Device, closest.Name, only.Key, only.DedicatedBytes, Math.Min(closest.Free, only.DedicatedBytes));
			}
		}
		return null;
	}

	[CompilerGenerated]
	private HardwareInventory(HardwareInventory original)
	{
		RamBytes = original.RamBytes;
		FreeRamBytes = original.FreeRamBytes;
		Gpu = original.Gpu;
	}
}
