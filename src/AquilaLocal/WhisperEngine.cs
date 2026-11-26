using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AquilaLocal.Companion.Speech;
using Microsoft.Win32.SafeHandles;

namespace AquilaLocal;

/// <summary>Thrown when listening can't work. Shown as is; Aquila never quietly drops back to typing.</summary>
public sealed class ListeningUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Local speech-to-text: runs whisper.cpp's server (from the "whisper" folder next to Aquila) on this computer
/// only, and turns a recorded utterance into timed transcript segments. Nothing is sent anywhere else.
/// </summary>
public sealed class WhisperEngine : IDisposable
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

	private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

	private readonly string folder;

	private Process? process;

	private SafeFileHandle? job;

	private HttpClient? http;

	public string Folder => folder;

	public string LastLog { get; private set; } = "";

	public WhisperEngine(string? folder = null)
	{
		this.folder = folder ?? Path.Combine(AppContext.BaseDirectory, "whisper");
	}

	public string? ServerPath
	{
		get
		{
			string direct = Path.Combine(folder, "whisper-server.exe");
			if (File.Exists(direct))
			{
				return direct;
			}
			return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "whisper-server.exe", SearchOption.AllDirectories).FirstOrDefault() : null;
		}
	}

	/// <summary>The speech model: the largest ggml-*.bin in whisper\models (or the whisper folder).</summary>
	public string? ModelPath
	{
		get
		{
			if (!Directory.Exists(folder))
			{
				return null;
			}
			return Directory.EnumerateFiles(folder, "ggml-*.bin", SearchOption.AllDirectories).OrderByDescending((string f) => new FileInfo(f).Length).FirstOrDefault();
		}
	}

	public bool Installed => ServerPath != null && ModelPath != null;

	public bool Running => process != null && !process.HasExited;

	public async Task StartAsync(CancellationToken ct)
	{
		await gate.WaitAsync(ct);
		try
		{
			if (Running && http != null)
			{
				return;
			}
			Stop();
			string server = ServerPath ?? throw new ListeningUnavailableException("The speech recognizer isn't installed (no whisper-server.exe in " + folder + "). Listening is off until it is.");
			string model = ModelPath ?? throw new ListeningUnavailableException("The speech recognizer has no model (no ggml-*.bin in " + folder + "). Listening is off until one is added.");
			TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
			probe.Start();
			int port = ((IPEndPoint)probe.LocalEndpoint).Port;
			probe.Stop();
			ProcessStartInfo start = new ProcessStartInfo(server)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				WorkingDirectory = Path.GetDirectoryName(server)!
			};
			foreach (string arg in new[] { "-m", model, "--host", "127.0.0.1", "--port", port.ToString(CultureInfo.InvariantCulture), "-t", "4" })
			{
				start.ArgumentList.Add(arg);
			}
			job = CreateJobObject(IntPtr.Zero, null);
			if (job.IsInvalid)
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}
			JobLimits info = new JobLimits { Basic = new BasicLimits { LimitFlags = 8192u } };
			if (!SetInformationJobObject(job, 9, ref info, (uint)Marshal.SizeOf<JobLimits>()))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}
			process = new Process { StartInfo = start };
			if (!process.Start())
			{
				throw new ListeningUnavailableException("The speech recognizer could not start.");
			}
			AssignProcessToJobObject(job, process.Handle);
			process.OutputDataReceived += delegate(object _, DataReceivedEventArgs e)
			{
				Log(e.Data);
			};
			process.ErrorDataReceived += delegate(object _, DataReceivedEventArgs e)
			{
				Log(e.Data);
			};
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
			{
				BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
				Timeout = TimeSpan.FromSeconds(60)
			};
			DateTime until = DateTime.UtcNow.AddSeconds(90);
			while (true)
			{
				ct.ThrowIfCancellationRequested();
				if (process.HasExited)
				{
					throw new ListeningUnavailableException("The speech recognizer stopped while loading (exit code " + process.ExitCode + "). " + LastLog);
				}
				try
				{
					using HttpResponseMessage r = await http.GetAsync("", ct);
					if (r.IsSuccessStatusCode)
					{
						break;
					}
				}
				catch (HttpRequestException)
				{
				}
				if (DateTime.UtcNow > until)
				{
					throw new ListeningUnavailableException("The speech recognizer didn't finish loading within 90 seconds. " + LastLog);
				}
				await Task.Delay(250, ct);
			}
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

	private void Log(string? line)
	{
		if (!string.IsNullOrWhiteSpace(line))
		{
			LastLog = line.Length > 300 ? line.Substring(0, 300) : line;
		}
	}

	/// <param name="language">"en", "ja", ... or "auto".</param>
	public async Task<List<TranscriptSegment>> TranscribeAsync(byte[] wav, string language, CancellationToken ct)
	{
		HttpClient client = http ?? throw new ListeningUnavailableException("The speech recognizer isn't running.");
		using MultipartFormDataContent form = new MultipartFormDataContent();
		ByteArrayContent file = new ByteArrayContent(wav);
		file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
		form.Add(file, "file", "speech.wav");
		form.Add(new StringContent("0.0"), "temperature");
		form.Add(new StringContent("verbose_json"), "response_format");
		form.Add(new StringContent(string.IsNullOrWhiteSpace(language) ? "auto" : language), "language");
		using HttpResponseMessage response = await client.PostAsync("inference", form, ct);
		string body = await response.Content.ReadAsStringAsync(ct);
		if (!response.IsSuccessStatusCode)
		{
			throw new IOException("The speech recognizer failed (" + (int)response.StatusCode + "): " + (body.Length > 300 ? body.Substring(0, 300) : body));
		}
		JsonNode? json = JsonNode.Parse(body);
		List<TranscriptSegment> list = new List<TranscriptSegment>();
		if (json?["segments"] is JsonArray segments)
		{
			foreach (JsonNode? s in segments)
			{
				string text = s?["text"]?.GetValue<string>() ?? "";
				double startAt = Seconds(s?["start"]) ?? Seconds(s?["t0"], 0.01) ?? 0;
				double endAt = Seconds(s?["end"]) ?? Seconds(s?["t1"], 0.01) ?? startAt;
				if (!IsNoise(text))
				{
					list.Add(new TranscriptSegment(startAt, endAt, text.Trim())
					{
						NoSpeechProbability = Seconds(s?["no_speech_prob"]),
						AverageLogProbability = Seconds(s?["avg_logprob"])
					});
				}
			}
		}
		if (list.Count == 0)
		{
			string text = json?["text"]?.GetValue<string>() ?? "";
			if (!IsNoise(text))
			{
				list.Add(new TranscriptSegment(0, wav.Length / 32000.0, text.Trim()));
			}
		}
		return list;
	}

	private static double? Seconds(JsonNode? node, double scale = 1.0)
	{
		if (node is JsonValue v)
		{
			if (v.TryGetValue(out double d))
			{
				return d * scale;
			}
			if (v.TryGetValue(out string? s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double p))
			{
				return p * scale;
			}
		}
		return null;
	}

	/// <summary>Whisper's stock outputs for silence or noise ("[BLANK_AUDIO]", "(music)", "Thank you.").</summary>
	public static bool IsNoise(string text)
	{
		string t = text.Trim();
		if (t.Length == 0)
		{
			return true;
		}
		if ((t.StartsWith("[") && t.EndsWith("]")) || (t.StartsWith("(") && t.EndsWith(")")) || (t.StartsWith("*") && t.EndsWith("*")))
		{
			return true;
		}
		return false;
	}

	public void Stop()
	{
		http?.Dispose();
		http = null;
		if (process != null)
		{
			try
			{
				if (!process.HasExited)
				{
					process.Kill(entireProcessTree: true);
				}
			}
			catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception)
			{
			}
			process.Dispose();
			process = null;
		}
		job?.Dispose();
		job = null;
	}

	public void Dispose()
	{
		Stop();
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateJobObject(nint attributes, string? name);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref JobLimits info, uint size);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool AssignProcessToJobObject(SafeFileHandle job, nint process);
}
