using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace AquilaLocal;

public sealed class Engine : IDisposable
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

	private Process? process;

	private SafeFileHandle? job;

	private HttpClient? http;

	private string fingerprint = "";

	private readonly string logPath;

	/// <summary>Where the conversation's working memory is saved while the engine is paused for a picture.</summary>
	private readonly string slotFolder;

	private const string ParkedSlotFile = "parked-conversation.bin";

	private readonly object logLock = new object();

	private readonly Queue<string> tail = new Queue<string>();

	private string alias = "";

	public bool Ready
	{
		get
		{
			Process process = this.process;
			if (process != null && !process.HasExited && http != null)
			{
				return fingerprint.Length > 0;
			}
			return false;
		}
	}

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

	public int Port { get; private set; }

	public bool SupportsDry { get; private set; }

	public int? SlotContext { get; private set; }

	public static string VulkanExecutable => Path.Combine(AppContext.BaseDirectory, "engine", "llama-server.exe");

	/// <summary>NVIDIA's CUDA build of the same engine, optional. On an RTX 5090 it read prompts about
	/// 40% faster and wrote about 14% faster than the Vulkan build.</summary>
	public static string CudaExecutable => Path.Combine(AppContext.BaseDirectory, "engine-cuda", "llama-server.exe");

	private static bool? useCuda;

	/// <summary>Use CUDA when it is installed and an NVIDIA card is present; detection turns this off
	/// again if the CUDA engine can't see the card (for example an old driver).</summary>
	public static bool UseCuda
	{
		get
		{
			if (!useCuda.HasValue)
			{
				try
				{
					useCuda = File.Exists(CudaExecutable) && GpuMonitor.GetAdapters().Any((GpuAdapter a) => a.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
				}
				catch (Exception)
				{
					useCuda = false;
				}
			}
			return useCuda.Value;
		}
		set
		{
			useCuda = value;
		}
	}

	public static string Executable => UseCuda ? CudaExecutable : VulkanExecutable;

	public long WorkingSetBytes
	{
		get
		{
			try
			{
				Process process = this.process;
				if (process == null || process.HasExited)
				{
					return 0L;
				}
				this.process.Refresh();
				return this.process.WorkingSet64;
			}
			catch
			{
				return 0L;
			}
		}
	}

	public string Alias => alias;

	public event Action<string>? Status;

	public Engine(string dataRoot)
	{
		logPath = Path.Combine(dataRoot, "logs", "engine.log");
		slotFolder = Path.Combine(dataRoot, "engine-cache");
	}

	public async Task StartAsync(Settings settings, CancellationToken token)
	{
		settings.Validate();
		ModelInfo modelInfo = ModelInfo.Read(settings.ModelPath);
		if (settings.Hardware?.Gpu == null && File.Exists(Executable))
		{
			// Startup detection can miss a graphics card that was asleep or switching modes. Try again
			// right before loading rather than silently falling back to the processor.
			this.Status?.Invoke("Looking for your graphics card…");
			HardwareInventory again = await HardwareInventory.DetectAsync(Executable);
			if (again.Gpu != null || settings.Hardware == null)
			{
				settings.Hardware = again;
			}
		}
		InferenceLaunch inferenceLaunch = InferenceLaunch.Resolve(settings, modelInfo);
		if (inferenceLaunch.AutoContext > 0)
		{
			settings.Context = inferenceLaunch.AutoContext;
			settings.MaxTokens = Math.Min(settings.MaxTokens, settings.Context / 2);
		}
		int threads = ((settings.CpuThreads > 0) ? settings.CpuThreads : inferenceLaunch.AutoThreads);
		string next = $"{settings.ModelPath}|{settings.ProjectorPath}|{settings.Context}|{inferenceLaunch}|{threads}|{settings.EffectiveThinkingBudget}";
		if (Ready && next == fingerprint)
		{
			return;
		}
		Stop();
		if (!File.Exists(settings.ModelPath))
		{
			throw new FileNotFoundException("Choose an existing GGUF model in the Model panel.");
		}
		if (Path.GetFileName(settings.ModelPath).Contains("mmproj", StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("Choose the language model as Model, and its mmproj file as Vision projector.");
		}
		if (modelInfo.MetadataKnown && settings.Context > modelInfo.NativeContext)
		{
			throw new ArgumentException($"This model declares a {modelInfo.NativeContext:N0}-token native context. Lower the context slider before loading.");
		}
		if (settings.Context > InferenceProfile.Resolve(settings.Inference, settings.Hardware).Maximum)
		{
			throw new ArgumentException("The context exceeds the selected inference profile's limit.");
		}
		if (settings.ProjectorPath.Length > 0 && !File.Exists(settings.ProjectorPath))
		{
			throw new FileNotFoundException("The selected vision projector is missing. Choose its matching mmproj file or clear it.");
		}
		using (FileStream fileStream = File.OpenRead(settings.ModelPath))
		{
			byte[] array = new byte[4];
			if (fileStream.Read(array) != 4 || Encoding.ASCII.GetString(array) != "GGUF")
			{
				throw new ArgumentException("This file is not a GGUF model.");
			}
		}
		MemoryStatus memoryStatus = new MemoryStatus();
		if (GlobalMemoryStatusEx(memoryStatus))
		{
			// Only a CPU load needs the whole model in system RAM. With GPU offload most of it goes to
			// video memory, so just make sure Windows has some breathing room.
			ulong num = (ulong)((inferenceLaunch.CpuOnly || !inferenceLaunch.FullOffload) ? (new FileInfo(settings.ModelPath).Length + ((settings.ProjectorPath.Length > 0) ? new FileInfo(settings.ProjectorPath).Length : 0) + 805306368) : 1610612736);
			if (!inferenceLaunch.CpuOnly && !inferenceLaunch.FullOffload)
			{
				num /= 2uL;
			}
			if (memoryStatus.Available < num)
			{
				throw new IOException($"Not enough free system memory to load this model comfortably. Available: {(double)memoryStatus.Available / 1073741824.0:0.0} GB; estimated minimum: {(double)num / 1073741824.0:0.0} GB. Close other work before loading, choose a smaller model, or use GPU offload when the GPU is free. Nothing has been loaded.");
			}
		}
		string executable = Executable;
		if (!File.Exists(executable))
		{
			throw new FileNotFoundException("The bundled engine is missing. Keep the engine folder beside Aquila Local.exe.");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(logPath));
		if (File.Exists(logPath) && new FileInfo(logPath).Length > 2000000)
		{
			File.Move(logPath, logPath + ".previous", overwrite: true);
		}
		lock (logLock)
		{
			tail.Clear();
		}
		TcpListener tcpListener = new TcpListener(IPAddress.Loopback, 0);
		tcpListener.Start();
		Port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
		tcpListener.Stop();
		alias = "aquila-" + Guid.NewGuid().ToString("N");
		// llama-server refuses to start ("not a directory") if --slot-save-path doesn't exist yet.
		Directory.CreateDirectory(slotFolder);
		string text = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
		ProcessStartInfo start = new ProcessStartInfo(executable)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = Path.GetDirectoryName(executable),
			RedirectStandardError = true,
			RedirectStandardOutput = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		Arg(new string[]
		{
			"--model",
			settings.ModelPath,
			"--alias",
			alias,
			"--host",
			"127.0.0.1",
			"--port",
			Port.ToString(),
			"--ctx-size",
			(settings.Context + 4096).ToString(),
			// Two slots sharing one cache pool: slot 0 holds the conversation, slot 1 serves short
			// background jobs (memory learning, summaries) without evicting the conversation.
			"--parallel",
			"2",
			"--kv-unified",
			// Without this, a request on slot 1 parks the idle conversation in the RAM prompt cache and
			// clears it; hybrid (recurrent) models then fail to restore it and re-read the whole chat.
			"--no-cache-idle-slots",
			"--batch-size",
			inferenceLaunch.Batch.ToString(),
			"--ubatch-size",
			inferenceLaunch.MicroBatch.ToString(),
			"--no-webui",
			// Lets the conversation's working memory be saved to disk and loaded back when the engine is
			// paused for the best-quality picture model, so the chat isn't re-read afterwards.
			"--slot-save-path",
			slotFolder,
			"--jinja",
			"--reasoning-format",
			"deepseek",
			"--fit-target",
			"1024"
		});
		Arg(new string[10]
		{
			"--threads",
			threads.ToString(),
			"--threads-batch",
			threads.ToString(),
			"--threads-http",
			"2",
			"--poll",
			"0",
			"--poll-batch",
			"0"
		});
		Arg(new string[2]
		{
			"--reasoning-budget",
			settings.EffectiveThinkingBudget.ToString()
		});
		if (settings.ProjectorPath.Length > 0)
		{
			Arg(new string[4] { "--mmproj", settings.ProjectorPath, "--image-max-tokens", "1024" });
		}
		inferenceLaunch.AddArguments(Arg);
		ClearInheritedArgs(start);
		// Record exactly how the engine was started (the API key is passed via the environment, not here).
		Log("[aquila] launch: " + executable + " " + string.Join(" ", start.ArgumentList.Select((string a) => a.Contains(' ') ? ("\"" + a + "\"") : a)));
		start.Environment["LLAMA_API_KEY"] = text;
		this.Status?.Invoke("Loading model into memory…");
		try
		{
			job = CreateJobObject(IntPtr.Zero, null);
			if (job.IsInvalid)
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}
			JobLimits info = new JobLimits
			{
				Basic = new BasicLimits
				{
					LimitFlags = 8192u
				}
			};
			if (!SetInformationJobObject(job, 9, ref info, (uint)Marshal.SizeOf<JobLimits>()))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}
			process = new Process
			{
				StartInfo = start
			};
			if (!process.Start())
			{
				throw new IOException("Could not start the bundled engine.");
			}
			// Normal priority: BelowNormal let any background task starve the thread that feeds the GPU.
			process.PriorityClass = ProcessPriorityClass.Normal;
			if (!AssignProcessToJobObject(job, process.Handle))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}
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
			http = new HttpClient(new SocketsHttpHandler
			{
				UseProxy = false
			})
			{
				BaseAddress = new Uri($"http://127.0.0.1:{Port}/"),
				Timeout = Timeout.InfiniteTimeSpan
			};
			http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", text);
			Stopwatch watch = Stopwatch.StartNew();
			while (watch.Elapsed < TimeSpan.FromMinutes(5.0))
			{
				token.ThrowIfCancellationRequested();
				if (process.HasExited)
				{
					throw new IOException("The model engine stopped during loading. Try CPU mode or fewer GPU layers.\n" + RecentLog());
				}
				try
				{
					using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
					deadline.CancelAfter(2000);
					using HttpResponseMessage response = await http.GetAsync("v1/models", deadline.Token);
					if (response.IsSuccessStatusCode)
					{
						JsonNode? jsonNode = JsonNode.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
						if (jsonNode != null && jsonNode["data"]?.AsArray().Any((JsonNode m) => m?["id"]?.GetValue<string>() == alias) == true)
						{
							await ReadServerPropertiesAsync(http, token);
							fingerprint = next;
							this.Status?.Invoke("Model ready");
							return;
						}
					}
				}
				catch (HttpRequestException)
				{
				}
				catch (OperationCanceledException) when (!token.IsCancellationRequested)
				{
				}
				await Task.Delay(350, token);
			}
			throw new TimeoutException("Model loading took longer than five minutes. Try CPU mode or a smaller context window.");
		}
		catch
		{
			Stop();
			throw;
		}
		void Arg(string[] values)
		{
			foreach (string item in values)
			{
				start.ArgumentList.Add(item);
			}
		}
	}

	internal static void ClearInheritedArgs(ProcessStartInfo start)
	{
		string[] array = start.Environment.Keys.Where((string k) => k.StartsWith("LLAMA_ARG_", StringComparison.OrdinalIgnoreCase) || k.Equals("GGML_RPC_SERVERS", StringComparison.OrdinalIgnoreCase) || k.Equals("LLAMA_API_KEY", StringComparison.OrdinalIgnoreCase)).ToArray();
		foreach (string key in array)
		{
			start.Environment.Remove(key);
		}
	}

	private async Task ReadServerPropertiesAsync(HttpClient client, CancellationToken token)
	{
		_ = 1;
		try
		{
			using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
			deadline.CancelAfter(1500);
			using HttpResponseMessage response = await client.GetAsync("props", deadline.Token);
			if (!response.IsSuccessStatusCode)
			{
				return;
			}
			JsonNode jsonNode = JsonNode.Parse(await response.Content.ReadAsStringAsync(deadline.Token))?["default_generation_settings"];
			Engine engine = this;
			int? num = jsonNode?["n_ctx"]?.GetValue<int>();
			int? slotContext;
			if (num.HasValue)
			{
				int valueOrDefault = num.GetValueOrDefault();
				if (valueOrDefault > 0)
				{
					slotContext = valueOrDefault;
					goto IL_01d3;
				}
			}
			slotContext = null;
			goto IL_01d3;
			IL_01d3:
			engine.SlotContext = slotContext;
			SupportsDry = jsonNode?["params"]?["dry_multiplier"] != null;
		}
		catch (OperationCanceledException) when (!token.IsCancellationRequested)
		{
		}
		catch (Exception ex2) when (((ex2 is HttpRequestException || ex2 is JsonException || ex2 is InvalidOperationException || ex2 is FormatException) ? 1 : 0) != 0)
		{
		}
	}

	private void Log(string? line)
	{
		if (line == null)
		{
			return;
		}
		lock (logLock)
		{
			tail.Enqueue(line);
			while (tail.Count > 16)
			{
				tail.Dequeue();
			}
			try
			{
				if (File.Exists(logPath) && new FileInfo(logPath).Length > 2000000)
				{
					File.Move(logPath, logPath + ".previous", overwrite: true);
				}
				File.AppendAllText(logPath, line + Environment.NewLine);
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}

	private string RecentLog()
	{
		lock (logLock)
		{
			return string.Join('\n', tail).Truncate(2400);
		}
	}

	public async Task<HttpResponseMessage> PostAsync(string endpoint, JsonObject payload, bool stream, CancellationToken token)
	{
		if (!Ready || http == null)
		{
			throw new IOException("The model is not loaded. Load it again from the Model panel.");
		}
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint)
		{
			Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
		};
		HttpResponseMessage response = await http.SendAsync(request, stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, token);
		if (!response.IsSuccessStatusCode)
		{
			string value = await response.Content.ReadAsStringAsync(token);
			HttpStatusCode statusCode = response.StatusCode;
			response.Dispose();
			throw new IOException($"Model request failed ({statusCode}). {value.Truncate(1000)}");
		}
		return response;
	}

	public async Task<int> CountPromptAsync(JsonArray messages, JsonArray? tools, bool thinking, CancellationToken token)
	{
		JsonObject jsonObject = new JsonObject
		{
			["model"] = Alias,
			["messages"] = messages.DeepClone(),
			["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = thinking }
		};
		if (tools != null)
		{
			jsonObject["tools"] = tools.DeepClone();
		}
		using HttpResponseMessage countResponse = await PostAsync("v1/chat/completions/input_tokens", jsonObject, stream: false, token);
		return JsonNode.Parse(await countResponse.Content.ReadAsStringAsync(token))?["input_tokens"]?.GetValue<int>() ?? throw new IOException("Engine returned no token count.");
	}

	/// <summary>
	/// Pauses the engine to free graphics memory: saves the conversation's working memory to disk (if it can),
	/// then unloads the model. Returns whether the working memory was saved.
	/// </summary>
	public async Task<bool> ParkAsync(CancellationToken token)
	{
		bool saved = false;
		if (Ready)
		{
			try
			{
				Directory.CreateDirectory(slotFolder);
				string file = Path.Combine(slotFolder, ParkedSlotFile);
				if (File.Exists(file))
				{
					File.Delete(file);
				}
				using HttpResponseMessage response = await PostAsync("slots/0?action=save", new JsonObject { ["filename"] = ParkedSlotFile }, stream: false, token);
				saved = File.Exists(file);
			}
			catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is UnauthorizedAccessException)
			{
				this.Status?.Invoke("Couldn't save the chat's working memory (" + ex.Message.Truncate(120) + "); it will be re-read after the picture.");
			}
		}
		Stop();
		return saved;
	}

	/// <summary>Loads the model again after a pause and restores the conversation's working memory if it was saved.</summary>
	public async Task ResumeAsync(Settings settings, bool restore, CancellationToken token)
	{
		await StartAsync(settings, token);
		string file = Path.Combine(slotFolder, ParkedSlotFile);
		if (!restore || !File.Exists(file))
		{
			return;
		}
		try
		{
			using HttpResponseMessage response = await PostAsync("slots/0?action=restore", new JsonObject { ["filename"] = ParkedSlotFile }, stream: false, token);
		}
		catch (Exception ex) when (ex is IOException || ex is HttpRequestException)
		{
			this.Status?.Invoke("Couldn't restore the chat's working memory (" + ex.Message.Truncate(120) + "); the next reply re-reads the chat.");
		}
		finally
		{
			try
			{
				File.Delete(file);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
			}
		}
	}

	public void Stop()
	{
		fingerprint = "";
		SlotContext = null;
		SupportsDry = false;
		http?.Dispose();
		http = null;
		if (process != null)
		{
			try
			{
				if (!process.HasExited)
				{
					process.Kill(entireProcessTree: true);
					process.WaitForExit(5000);
				}
			}
			catch (InvalidOperationException)
			{
			}
			catch (Win32Exception)
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

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx([In][Out] MemoryStatus status);
}
