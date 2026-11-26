using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace AquilaLocal;

public sealed class GpuMonitor : IDisposable
{
	[StructLayout(LayoutKind.Explicit, Size = 16)]
	private struct CounterValue
	{
		[FieldOffset(0)]
		public uint Status;

		[FieldOffset(8)]
		public double Number;
	}

	private struct CounterItem
	{
		public nint Name;

		public CounterValue Value;
	}

	private struct Luid
	{
		public uint Low;

		public int High;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct AdapterDesc
	{
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string Description;

		public uint Vendor;

		public uint Device;

		public uint SubSystem;

		public uint Revision;

		public nuint DedicatedVideoMemory;

		public nuint DedicatedSystemMemory;

		public nuint SharedSystemMemory;

		public Luid Luid;

		public uint Flags;
	}

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int EnumAdapterDelegate(nint factory, uint index, out nint adapter);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int GetDescDelegate(nint adapter, out AdapterDesc desc);

	[StructLayout(LayoutKind.Sequential)]
	private sealed class MemoryStatus
	{
		public uint Length = 64u;

		public uint Load;

		public ulong Total;

		public ulong Available;

		public ulong PageTotal;

		public ulong PageAvailable;

		public ulong VirtualTotal;

		public ulong VirtualAvailable;

		public ulong Extended;
	}

	private nint query;

	private nint engines;

	private nint memory;

	private nint adapterMemory;

	private readonly List<GpuAdapter> adapters;

	private readonly Dictionary<int, (string Name, DateTimeOffset At)> names = new Dictionary<int, (string, DateTimeOffset)>();

	private string? error;

	private static readonly Regex Instance = new Regex("pid_(\\d+)_luid_(0x[0-9a-f]+_0x[0-9a-f]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex AdapterInstance = new Regex("luid_(0x[0-9a-f]+_0x[0-9a-f]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	public static long AvailableSystemMemory => SystemMemory.Available;

	public static (long Total, long Available) SystemMemory
	{
		get
		{
			MemoryStatus memoryStatus = new MemoryStatus();
			if (!GlobalMemoryStatusEx(memoryStatus))
			{
				return (Total: 0L, Available: 0L);
			}
			return (Total: (long)memoryStatus.Total, Available: (long)memoryStatus.Available);
		}
	}

	public GpuMonitor()
	{
		adapters = GetAdapters();
		uint num = PdhOpenQuery(null, UIntPtr.Zero, out query);
		if (num == 0)
		{
			num = PdhAddEnglishCounter(query, "\\GPU Engine(*)\\Utilization Percentage", UIntPtr.Zero, out engines);
		}
		if (num == 0)
		{
			num = PdhAddEnglishCounter(query, "\\GPU Process Memory(*)\\Local Usage", UIntPtr.Zero, out memory);
		}
		if (num == 0)
		{
			num = PdhAddEnglishCounter(query, "\\GPU Local Adapter Memory(*)\\Local Usage", UIntPtr.Zero, out adapterMemory);
		}
		if (num == 0)
		{
			num = PdhCollectQueryData(query);
		}
		if (num != 0)
		{
			error = $"Windows GPU counters unavailable (0x{num:X8}).";
		}
	}

	public GpuSnapshot Sample(int? ownEnginePid)
	{
		if (error != null)
		{
			return new GpuSnapshot(adapters, Array.Empty<GpuApp>(), new Dictionary<string, long>(), error, DateTimeOffset.Now);
		}
		uint num = PdhCollectQueryData(query);
		if (num != 0)
		{
			return new GpuSnapshot(adapters, Array.Empty<GpuApp>(), new Dictionary<string, long>(), $"GPU sampling unavailable (0x{num:X8}).", DateTimeOffset.Now);
		}
		try
		{
			List<(string Name, double Value)> list = Read(engines);
			List<(string, double)> list2 = Read(memory);
			List<(string, double)> list3 = Read(adapterMemory);
			Dictionary<(int, string), (Dictionary<string, double>, long)> dictionary = new Dictionary<(int, string), (Dictionary<string, double>, long)>();
			foreach (var item7 in list)
			{
				string item = item7.Name;
				double item2 = item7.Value;
				Match match = Instance.Match(item);
				if (match.Success && int.TryParse(match.Groups[1].Value, out var result))
				{
					(int, string) key = (result, match.Groups[2].Value.ToLowerInvariant());
					if (!dictionary.TryGetValue(key, out var value))
					{
						value = (new Dictionary<string, double>(), 0L);
					}
					string text = item;
					int num2 = match.Index + match.Length;
					string key2 = text.Substring(num2, text.Length - num2);
					value.Item1[key2] = Math.Max(0.0, item2);
					dictionary[key] = value;
				}
			}
			foreach (var item8 in list2)
			{
				string item3 = item8.Item1;
				double item4 = item8.Item2;
				Match match2 = Instance.Match(item3);
				if (match2.Success && int.TryParse(match2.Groups[1].Value, out var result2))
				{
					(int, string) key3 = (result2, match2.Groups[2].Value.ToLowerInvariant());
					if (!dictionary.TryGetValue(key3, out var value2))
					{
						value2 = (new Dictionary<string, double>(), 0L);
					}
					value2.Item2 += (long)Math.Max(0.0, item4);
					dictionary[key3] = value2;
				}
			}
			List<GpuApp> list4 = (from p in dictionary.Select((KeyValuePair<(int Pid, string Adapter), (Dictionary<string, double> Engines, long Memory)> p) => new
				{
					Name = ((p.Key.Pid == ownEnginePid) ? "Aquila model" : ProcessName(p.Key.Pid)),
					Pid = p.Key.Pid,
					Adapter = p.Key.Adapter,
					Engines = p.Value.Engines,
					Memory = p.Value.Memory
				}).ToList()
				group p by (Name: p.Name, Adapter: p.Adapter) into g
				select new GpuApp(g.Key.Name, g.Key.Adapter, Math.Clamp((from e in g.SelectMany(p => p.Engines)
					group e by e.Key into e
					select e.Sum((KeyValuePair<string, double> v) => v.Value)).DefaultIfEmpty(0.0).Max(), 0.0, 100.0), g.Sum(p => p.Memory), string.Join(", ", g.Select(p => p.Pid))) into p
				where p.Utilization >= 0.05 || p.LocalBytes >= 1048576
				orderby p.Utilization descending, p.LocalBytes descending
				select p).ToList();
			Dictionary<string, long> dictionary2 = new Dictionary<string, long>();
			foreach (var item9 in list3)
			{
				string item5 = item9.Item1;
				double item6 = item9.Item2;
				Match match3 = AdapterInstance.Match(item5);
				if (match3.Success)
				{
					string key4 = match3.Groups[1].Value.ToLowerInvariant();
					dictionary2[key4] = dictionary2.GetValueOrDefault(key4) + (long)Math.Max(0.0, item6);
				}
			}
			foreach (GpuApp item10 in list4.Where((GpuApp a) => !adapters.Any((GpuAdapter d) => d.Key == a.AdapterKey)))
			{
				adapters.Add(new GpuAdapter("GPU " + item10.AdapterKey, item10.AdapterKey, 0L, adapters.Count));
			}
			Dictionary<string, double> engineUsage = (from p in dictionary
				group p by p.Key.Adapter).ToDictionary((IGrouping<string, KeyValuePair<(int Pid, string Adapter), (Dictionary<string, double> Engines, long Memory)>> g) => g.Key, (IGrouping<string, KeyValuePair<(int Pid, string Adapter), (Dictionary<string, double> Engines, long Memory)>> g) => Math.Clamp((from e in g.SelectMany((KeyValuePair<(int Pid, string Adapter), (Dictionary<string, double> Engines, long Memory)> p) => p.Value.Engines)
				group e by e.Key into e
				select e.Sum((KeyValuePair<string, double> v) => v.Value)).DefaultIfEmpty(0.0).Max(), 0.0, 100.0));
			return new GpuSnapshot(adapters.ToArray(), list4, dictionary2, null, DateTimeOffset.Now)
			{
				EngineUsage = engineUsage
			};
		}
		catch (Exception ex) when (((ex is InvalidOperationException || ex is Win32Exception) ? 1 : 0) != 0)
		{
			return new GpuSnapshot(adapters.ToArray(), Array.Empty<GpuApp>(), new Dictionary<string, long>(), ex.Message, DateTimeOffset.Now);
		}
	}

	private string ProcessName(int pid)
	{
		if (names.TryGetValue(pid, out (string, DateTimeOffset) value) && DateTimeOffset.Now - value.Item2 < TimeSpan.FromSeconds(10.0))
		{
			return value.Item1;
		}
		string text;
		try
		{
			using Process process = Process.GetProcessById(pid);
			text = process.ProcessName;
		}
		catch
		{
			text = "PID " + pid;
		}
		names[pid] = (text, DateTimeOffset.Now);
		if (names.Count > 2000)
		{
			names.Clear();
		}
		return text;
	}

	private static List<(string Name, double Value)> Read(nint counter)
	{
		uint size = 0u;
		uint count = 0u;
		uint num = PdhGetFormattedCounterArray(counter, 33280u, ref size, ref count, IntPtr.Zero);
		if (size == 0 && num != 0)
		{
			return new List<(string, double)>();
		}
		if (size > 16777216)
		{
			throw new InvalidOperationException("GPU counter sample is too large.");
		}
		for (int i = 0; i < 3; i++)
		{
			nint num2 = Marshal.AllocHGlobal((int)size);
			try
			{
				uint size2 = size;
				num = PdhGetFormattedCounterArray(counter, 33280u, ref size2, ref count, num2);
				switch (num)
				{
				case 2147485650u:
					size = Math.Min(16777216u, Math.Max(size * 2, size2));
					break;
				default:
					throw new InvalidOperationException($"GPU counter read failed (0x{num:X8}).");
				case 0u:
				{
					List<(string, double)> list = new List<(string, double)>();
					int num3 = Marshal.SizeOf<CounterItem>();
					if (count * num3 > size)
					{
						throw new InvalidOperationException("Invalid GPU counter response.");
					}
					for (int j = 0; j < count; j++)
					{
						CounterItem counterItem = Marshal.PtrToStructure<CounterItem>(num2 + j * num3);
						uint status = counterItem.Value.Status;
						bool flag = status <= 1;
						if (flag && double.IsFinite(counterItem.Value.Number))
						{
							list.Add((Marshal.PtrToStringUni(counterItem.Name) ?? "", counterItem.Value.Number));
						}
					}
					return list;
				}
				}
			}
			finally
			{
				Marshal.FreeHGlobal(num2);
			}
		}
		throw new InvalidOperationException("GPU counter list changed during sampling; retrying next sample.");
	}

	internal static List<GpuAdapter> GetAdapters()
	{
		List<GpuAdapter> list = new List<GpuAdapter>();
		nint factory = IntPtr.Zero;
		try
		{
			Guid iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
			if (CreateDXGIFactory1(ref iid, out factory) != 0)
			{
				return list;
			}
			EnumAdapterDelegate delegateForFunctionPointer = Marshal.GetDelegateForFunctionPointer<EnumAdapterDelegate>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 12 * IntPtr.Size));
			nint adapter;
			for (uint num = 0u; num < 16 && delegateForFunctionPointer(factory, num, out adapter) == 0; num++)
			{
				try
				{
					if (Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapter), 10 * IntPtr.Size))(adapter, out var desc) == 0 && (desc.Flags & 2) == 0)
					{
						list.Add(new GpuAdapter(desc.Description.TrimEnd('\0'), $"0x{desc.Luid.High:X8}_0x{desc.Luid.Low:X8}".ToLowerInvariant(), (long)((UIntPtr)desc.DedicatedVideoMemory).ToUInt64(), (int)num));
					}
				}
				finally
				{
					Marshal.Release(adapter);
				}
			}
		}
		catch (Exception ex) when (((ex is COMException || ex is DllNotFoundException || ex is EntryPointNotFoundException) ? 1 : 0) != 0)
		{
		}
		finally
		{
			if (factory != IntPtr.Zero)
			{
				Marshal.Release(factory);
			}
		}
		return list;
	}

	public void Dispose()
	{
		if (query != IntPtr.Zero)
		{
			PdhCloseQuery(query);
			query = IntPtr.Zero;
		}
	}

	[DllImport("dxgi.dll")]
	private static extern int CreateDXGIFactory1(ref Guid iid, out nint factory);

	[DllImport("pdh.dll", CharSet = CharSet.Unicode)]
	private static extern uint PdhOpenQuery(string? source, nuint user, out nint query);

	[DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
	private static extern uint PdhAddEnglishCounter(nint query, string path, nuint user, out nint counter);

	[DllImport("pdh.dll")]
	private static extern uint PdhCollectQueryData(nint query);

	[DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")]
	private static extern uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint size, ref uint count, nint items);

	[DllImport("pdh.dll")]
	private static extern uint PdhCloseQuery(nint query);

	[DllImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx([In][Out] MemoryStatus status);
}
