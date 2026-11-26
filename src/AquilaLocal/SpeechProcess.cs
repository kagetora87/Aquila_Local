using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace AquilaLocal;

public sealed class SpeechProcess(string assets, string output, string dataRoot) : IDisposable
{
	private struct BasicLimits
	{
		public long PerProcessUserTimeLimit;

		public long PerJobUserTimeLimit;

		public uint LimitFlags;

		public nuint MinimumWorkingSetSize;

		public nuint MaximumWorkingSetSize;

		public uint ActiveProcessLimit;

		public nuint Affinity;

		public uint PriorityClass;

		public uint SchedulingClass;
	}

	private struct IoCounters
	{
		public ulong ReadOperationCount;

		public ulong WriteOperationCount;

		public ulong OtherOperationCount;

		public ulong ReadTransferCount;

		public ulong WriteTransferCount;

		public ulong OtherTransferCount;
	}

	private struct JobLimits
	{
		public BasicLimits Basic;

		public IoCounters Io;

		public nuint ProcessMemoryLimit;

		public nuint JobMemoryLimit;

		public nuint PeakProcessMemoryUsed;

		public nuint PeakJobMemoryUsed;
	}

	private Process? process;

	private SafeFileHandle? job;

	private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

	public int? ProcessId
	{
		get
		{
			Process process = this.process;
			if (process == null || process.HasExited)
			{
				return null;
			}
			return this.process.Id;
		}
	}

	public string Backend { get; private set; } = "";

	public event Action<string>? Progress;

	public async Task<string> GenerateAsync(string text, VoiceOptions options, string mood, CancellationToken ct, VoiceReference? reference = null)
	{
		await gate.WaitAsync(ct);
		try
		{
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromMinutes((options.Device == "cpu") ? 8 : 5));
			Process process = this.process;
			if (process == null || process.HasExited)
			{
				await StartAsync(timeout.Token);
			}
			string id = Guid.NewGuid().ToString("N");
			JsonObject jsonObject = new JsonObject
			{
				["id"] = id,
				["text"] = text,
				["speaker"] = options.Speaker,
				["device"] = options.Device,
				["engine"] = (options.UsesKitten ? "kitten" : "qwen"),
				["direction"] = options.Instruction(mood),
				["mood"] = mood,
				["intensity"] = options.Intensity,
				["pace"] = options.Pace
			};
			if ((object)reference != null)
			{
				jsonObject["reference"] = reference.AudioFile;
				jsonObject["reference_text"] = reference.Transcript;
			}
			await this.process.StandardInput.WriteLineAsync(jsonObject.ToJsonString().AsMemory(), timeout.Token);
			await this.process.StandardInput.FlushAsync(timeout.Token);
			JsonNode jsonNode;
			while (true)
			{
				jsonNode = await ReadAsync(timeout.Token);
				if (!(jsonNode["type"]?.GetValue<string>() == "progress"))
				{
					break;
				}
				this.Progress?.Invoke(jsonNode["message"]?.GetValue<string>() ?? "Preparing speech…");
			}
			if (jsonNode["id"]?.GetValue<string>() != id)
			{
				throw new IOException("Speech reply identity mismatch.");
			}
			if (jsonNode["type"]?.GetValue<string>() == "error")
			{
				throw new IOException(jsonNode["message"]?.GetValue<string>() ?? "Speech generation failed.");
			}
			if (jsonNode["type"]?.GetValue<string>() != "audio")
			{
				throw new IOException("Unexpected speech reply.");
			}
			string text2 = Path.Combine(Path.GetFullPath(output), id + ".wav");
			if (!string.Equals(jsonNode["path"]?.GetValue<string>(), text2, StringComparison.OrdinalIgnoreCase) || !File.Exists(text2))
			{
				throw new IOException("Speech output path did not match the owned temporary file.");
			}
			if (new FileInfo(text2).Length > 4324096)
			{
				throw new IOException("Speech audio exceeded its duration bound.");
			}
			Backend = jsonNode["backend"]?.GetValue<string>() ?? "";
			return text2;
		}
		catch
		{
			Stop();
			throw;
		}
		finally
		{
			gate.Release();
		}
	}

	private async Task StartAsync(CancellationToken ct)
	{
		string text = Path.Combine(assets, "python", "python.exe");
		string text2 = Path.Combine(AppContext.BaseDirectory, "SpeechWorker.py");
		if (!File.Exists(text) || !File.Exists(text2))
		{
			throw new IOException("The bundled speech runtime is missing. Keep the speech folder with Aquila.");
		}
		Directory.CreateDirectory(output);
		Directory.CreateDirectory(Path.Combine(dataRoot, "logs"));
		ProcessStartInfo processStartInfo = new ProcessStartInfo(text)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			WorkingDirectory = assets,
			StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		string[] array = new string[10]
		{
			"-I",
			"-B",
			"-u",
			text2,
			"--assets",
			assets,
			"--output",
			output,
			"--references",
			Path.Combine(dataRoot, "voice-samples")
		};
		foreach (string item in array)
		{
			processStartInfo.ArgumentList.Add(item);
		}
		array = processStartInfo.Environment.Keys.Where((string k) => k.StartsWith("PYTHON", StringComparison.OrdinalIgnoreCase) || k.StartsWith("HF_", StringComparison.OrdinalIgnoreCase)).ToArray();
		foreach (string key in array)
		{
			processStartInfo.Environment.Remove(key);
		}
		processStartInfo.Environment["PYTHONUTF8"] = "1";
		job = CreateJobObject(IntPtr.Zero, null);
		JobLimits info = new JobLimits
		{
			Basic = new BasicLimits
			{
				LimitFlags = 8192u
			}
		};
		if (job.IsInvalid || !SetInformationJobObject(job, 9, ref info, (uint)Marshal.SizeOf<JobLimits>()))
		{
			throw new IOException("Could not create speech lifetime guard.");
		}
		process = new Process
		{
			StartInfo = processStartInfo
		};
		process.Start();
		process.PriorityClass = ProcessPriorityClass.BelowNormal;
		if (!AssignProcessToJobObject(job, process.Handle))
		{
			Stop();
			throw new IOException("Could not protect the speech worker lifetime.");
		}
		string log = Path.Combine(dataRoot, "logs", "speech.log");
		if (File.Exists(log) && new FileInfo(log).Length > 1000000)
		{
			File.Move(log, log + ".previous", overwrite: true);
		}
		process.ErrorDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				try
				{
					if (!File.Exists(log) || new FileInfo(log).Length < 1000000)
					{
						File.AppendAllText(log, e.Data + Environment.NewLine);
					}
				}
				catch (IOException)
				{
				}
			}
		};
		process.BeginErrorReadLine();
		if ((await ReadAsync(ct))["type"]?.GetValue<string>() != "ready")
		{
			throw new IOException("Speech runtime failed to become ready.");
		}
	}

	private async Task<JsonNode> ReadAsync(CancellationToken ct)
	{
		string obj = (await process.StandardOutput.ReadLineAsync(ct)) ?? throw new IOException("The speech worker exited. Check Data/logs/speech.log.");
		if (obj.Length > 16000)
		{
			throw new IOException("Speech reply exceeded its protocol bound.");
		}
		return JsonNode.Parse(obj) ?? throw new IOException("Invalid speech reply.");
	}

	public void Stop()
	{
		Process process = this.process;
		this.process = null;
		job?.Dispose();
		job = null;
		if (process == null)
		{
			return;
		}
		try
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
				process.WaitForExit(2000);
			}
		}
		catch (Exception ex) when (((ex is InvalidOperationException || ex is Win32Exception) ? 1 : 0) != 0)
		{
		}
		process.Dispose();
	}

	public void Dispose()
	{
		Stop();
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateJobObject(nint attributes, string? name);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref JobLimits info, uint size);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool AssignProcessToJobObject(SafeFileHandle job, nint process);
}
