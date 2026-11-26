using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AquilaAvatarProtocol;
using Microsoft.Win32.SafeHandles;

namespace AquilaLocal;

public sealed class AvatarHost : IAsyncDisposable
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

	private readonly string root;

	private readonly string executable;

	private readonly SemaphoreSlim lifecycle = new SemaphoreSlim(1);

	private readonly SemaphoreSlim sending = new SemaphoreSlim(1);

	private readonly object stateGate = new object();

	private CancellationTokenSource? stop;

	private TcpListener? listener;

	private Process? process;

	private SafeFileHandle? job;

	private WebSocket? socket;

	private TaskCompletionSource<bool>? ready;

	private AvatarMessage latest = new AvatarMessage();

	private AvatarPresentation presentation = new AvatarPresentation();

	private long sequence;

	private string session = "";

	private string token = "";

	private bool disposed;

	private readonly object logGate = new object();

	public bool Running
	{
		get
		{
			Process process = this.process;
			if (process != null)
			{
				return !process.HasExited;
			}
			return false;
		}
	}

	public bool Connected
	{
		get
		{
			WebSocket? webSocket = socket;
			if (webSocket == null)
			{
				return false;
			}
			return webSocket.State == WebSocketState.Open;
		}
	}

	public int? ProcessId
	{
		get
		{
			if (!Running)
			{
				return null;
			}
			return process.Id;
		}
	}

	public long LastAppliedSequence { get; private set; } = -1L;


	public string LastAppliedState { get; private set; } = "";


	public string LastAppliedDetail { get; private set; } = "";


	public AvatarPresentation? LastAppliedPresentation { get; private set; }

	public int Connections { get; private set; }

	public int Port { get; private set; }

	public string ModelPath { get; private set; } = "";


	internal string Session => session;

	internal string Token => token;

	internal Uri Endpoint => new Uri($"ws://127.0.0.1:{Port}/avatar");

	public event Action<string>? Status;

	public event Action<AvatarMessage>? Applied;

	public AvatarHost(string dataRoot, string? executable = null)
	{
		root = dataRoot;
		this.executable = executable ?? Path.Combine(AppContext.BaseDirectory, "avatar", "Aquila Avatar.exe");
	}

	public async Task StartAsync(AvatarPresentation config, CancellationToken ct, bool headless = false, string modelPath = "")
	{
		if (!config.Valid())
		{
			throw new ArgumentException("Invalid avatar camera or frame-rate settings.");
		}
		string resolvedModel = AvatarCatalog.Resolve(modelPath);
		AvatarModelFile.Inspect(resolvedModel);
		await lifecycle.WaitAsync(ct);
		try
		{
			ObjectDisposedException.ThrowIf(disposed, this);
			await StopCore();
			ModelPath = resolvedModel;
			if (!File.Exists(executable))
			{
				throw new FileNotFoundException("The bundled avatar is missing. Keep the avatar folder beside Aquila Local.exe.");
			}
			presentation = config;
			session = Guid.NewGuid().ToString("N");
			token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
			sequence = 0L;
			latest = new AvatarMessage
			{
				Session = session,
				Sequence = 0L,
				Presentation = presentation
			};
			LastAppliedSequence = -1L;
			LastAppliedState = "";
			Connections = 0;
			ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			stop = new CancellationTokenSource();
			listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start(4);
			Port = ((IPEndPoint)listener.LocalEndpoint).Port;
			AcceptLoop(listener, stop.Token);
			ProcessStartInfo processStartInfo = new ProcessStartInfo(executable)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = Path.GetDirectoryName(executable),
				RedirectStandardError = true,
				RedirectStandardOutput = true
			};
			processStartInfo.Environment["AQUILA_AVATAR_ENDPOINT"] = Endpoint.ToString();
			processStartInfo.Environment["AQUILA_AVATAR_SESSION"] = session;
			processStartInfo.Environment["AQUILA_AVATAR_TOKEN"] = token;
			processStartInfo.Environment["AQUILA_AVATAR_MODEL"] = resolvedModel;
			if (headless)
			{
				processStartInfo.ArgumentList.Add("--headless");
			}
			job = CreateJobObject(IntPtr.Zero, null);
			if (job.IsInvalid)
			{
				throw new IOException("Could not create avatar process lifetime guard.");
			}
			JobLimits jobLimits = default(JobLimits);
			jobLimits.Basic = new BasicLimits
			{
				LimitFlags = 8192u
			};
			JobLimits i = jobLimits;
			if (!SetInformationJobObject(job, 9, ref i, (uint)Marshal.SizeOf<JobLimits>()))
			{
				throw new IOException("Could not configure avatar lifetime guard.");
			}
			Process owned = new Process
			{
				StartInfo = processStartInfo,
				EnableRaisingEvents = true
			};
			process = owned;
			owned.Exited += delegate
			{
				Task.Run(async delegate
				{
					await lifecycle.WaitAsync();
					try
					{
						if (process == owned)
						{
							await StopCore();
							this.Status?.Invoke("Avatar closed · Start to reopen");
						}
					}
					finally
					{
						lifecycle.Release();
					}
				});
			};
			if (!owned.Start())
			{
				throw new IOException("Could not launch the bundled avatar.");
			}
			if (!AssignProcessToJobObject(job, owned.Handle))
			{
				throw new IOException("Could not attach the avatar lifetime guard.");
			}
			owned.PriorityClass = ProcessPriorityClass.BelowNormal;
			owned.OutputDataReceived += delegate(object _, DataReceivedEventArgs e)
			{
				Log(e.Data);
			};
			owned.ErrorDataReceived += delegate(object _, DataReceivedEventArgs e)
			{
				Log(e.Data);
			};
			owned.BeginOutputReadLine();
			owned.BeginErrorReadLine();
			this.Status?.Invoke("Starting avatar…");
			try
			{
				await ready.Task.WaitAsync(TimeSpan.FromSeconds(25.0), ct);
			}
			catch
			{
				await StopCore();
				throw;
			}
			this.Status?.Invoke("Avatar connected · Idle");
		}
		catch
		{
			await StopCore();
			throw;
		}
		finally
		{
			lifecycle.Release();
		}
	}

	private async Task AcceptLoop(TcpListener server, CancellationToken ct)
	{
		SemaphoreSlim slots = new SemaphoreSlim(4);
		try
		{
			_ = 1;
			try
			{
				while (!ct.IsCancellationRequested)
				{
					TcpClient client = await server.AcceptTcpClientAsync(ct);
					if (!(await slots.WaitAsync(0, ct)))
					{
						client.Dispose();
						continue;
					}
					Task.Run(async delegate
					{
						try
						{
							await Handle(client, ct);
						}
						catch (Exception ex2) when (((ex2 is IOException || ex2 is WebSocketException || ex2 is OperationCanceledException || ex2 is JsonException || ex2 is InvalidOperationException || ex2 is ArgumentException) ? 1 : 0) != 0)
						{
						}
						finally
						{
							client.Dispose();
							try
							{
								slots.Release();
							}
							catch (ObjectDisposedException)
							{
							}
						}
					});
				}
			}
			catch (Exception ex) when (((ex is OperationCanceledException || ex is SocketException || ex is ObjectDisposedException) ? 1 : 0) != 0)
			{
			}
		}
		finally
		{
			if (slots != null)
			{
				((IDisposable)slots).Dispose();
			}
		}
	}

	private async Task Handle(TcpClient client, CancellationToken ct)
	{
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
		deadline.CancelAfter(3000);
		NetworkStream stream = client.GetStream();
		List<byte> header = new List<byte>();
		byte[] one = new byte[1];
		while (header.Count < 8192)
		{
			if (await stream.ReadAsync(one, deadline.Token) != 1)
			{
				return;
			}
			header.Add(one[0]);
			if (header.Count >= 4 && header.TakeLast(4).SequenceEqual(new byte[4] { 13, 10, 13, 10 }))
			{
				break;
			}
		}
		if (header.Count < 4 || !header.TakeLast(4).SequenceEqual(new byte[4] { 13, 10, 13, 10 }))
		{
			return;
		}
		string[] array = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in array.Skip(1))
		{
			int num = item.IndexOf(':');
			if (num > 0)
			{
				string key = item.Substring(0, num).Trim();
				string text = item;
				int num2 = num + 1;
				if (!dictionary.TryAdd(key, text.Substring(num2, text.Length - num2).Trim()))
				{
					return;
				}
			}
		}
		string valueOrDefault = dictionary.GetValueOrDefault("Authorization", "");
		byte[] bytes = Encoding.ASCII.GetBytes("Bearer " + token);
		bool flag = array[0] == "GET /avatar HTTP/1.1" && dictionary.GetValueOrDefault("Host") == $"127.0.0.1:{Port}" && !dictionary.ContainsKey("Origin") && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(valueOrDefault), bytes) && dictionary.GetValueOrDefault("Upgrade", "").Equals("websocket", StringComparison.OrdinalIgnoreCase) && dictionary.GetValueOrDefault("Connection", "").Split(',').Any((string v) => v.Trim().Equals("Upgrade", StringComparison.OrdinalIgnoreCase)) && dictionary.GetValueOrDefault("Sec-WebSocket-Version") == "13";
		string valueOrDefault2 = dictionary.GetValueOrDefault("Sec-WebSocket-Key", "");
		try
		{
			flag &= Convert.FromBase64String(valueOrDefault2).Length == 16;
		}
		catch (FormatException)
		{
			flag = false;
		}
		if (flag && !Connected)
		{
			string text2 = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(valueOrDefault2 + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
			await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + text2 + "\r\n\r\n"), deadline.Token);
			using WebSocket peer = WebSocket.CreateFromStream(stream, isServer: true, null, TimeSpan.FromSeconds(10.0));
			bool accepted = false;
			try
			{
				AvatarMessage avatarMessage = await Receive(peer, deadline.Token);
				if (avatarMessage == null || !avatarMessage.Valid(session) || avatarMessage.Type != "hello")
				{
					return;
				}
				lock (stateGate)
				{
					if (socket != null && socket.State == WebSocketState.Open)
					{
						return;
					}
					socket = peer;
					accepted = true;
					Connections++;
				}
				deadline.CancelAfter(-1);
				await SendLatest(ct);
				this.Status?.Invoke("Avatar connected");
				while (!ct.IsCancellationRequested && peer.State == WebSocketState.Open)
				{
					AvatarMessage avatarMessage2 = await Receive(peer, ct);
					if (avatarMessage2 == null || !avatarMessage2.Valid(session) || avatarMessage2.Type != "applied" || avatarMessage2.Sequence > sequence)
					{
						break;
					}
					if (avatarMessage2.Sequence >= LastAppliedSequence)
					{
						LastAppliedState = avatarMessage2.State;
						LastAppliedSequence = avatarMessage2.Sequence;
						LastAppliedDetail = avatarMessage2.Detail;
						LastAppliedPresentation = avatarMessage2.Presentation;
						this.Applied?.Invoke(avatarMessage2);
						if (avatarMessage2.Detail == "model ready")
						{
							ready?.TrySetResult(result: true);
						}
						else
						{
							ready?.TrySetException(new IOException("The avatar could not load its VRM model. See the avatar log."));
						}
					}
				}
				return;
			}
			finally
			{
				if (accepted)
				{
					lock (stateGate)
					{
						if (socket == peer)
						{
							socket = null;
						}
					}
					if (!ct.IsCancellationRequested)
					{
						this.Status?.Invoke("Avatar reconnecting…");
					}
				}
			}
		}
		await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), deadline.Token);
	}

	private static async Task<AvatarMessage?> Receive(WebSocket peer, CancellationToken ct)
	{
		byte[] buffer = new byte[4096];
		using MemoryStream body = new MemoryStream();
		WebSocketReceiveResult webSocketReceiveResult;
		do
		{
			webSocketReceiveResult = await peer.ReceiveAsync(buffer, ct);
			if (webSocketReceiveResult.MessageType != 0 || body.Length + webSocketReceiveResult.Count > 16384)
			{
				return null;
			}
			body.Write(buffer, 0, webSocketReceiveResult.Count);
		}
		while (!webSocketReceiveResult.EndOfMessage);
		return JsonSerializer.Deserialize<AvatarMessage>(body.ToArray());
	}

	public Task SetStateAsync(string state, bool preview = false, string cue = "none", string utterance = "")
	{
		if (Array.IndexOf(AvatarMessage.States, state) < 0 || Array.IndexOf(AvatarMessage.Cues, cue) < 0 || utterance.Length > 80)
		{
			throw new ArgumentException("Invalid avatar state or cue.");
		}
		if (!Running)
		{
			return Task.CompletedTask;
		}
		lock (stateGate)
		{
			latest = new AvatarMessage
			{
				Session = session,
				Sequence = Interlocked.Increment(ref sequence),
				State = state,
				Preview = preview,
				Cue = cue,
				Utterance = utterance,
				Presentation = presentation
			};
		}
		return SendLatest(stop?.Token ?? CancellationToken.None);
	}

	private async Task SendLatest(CancellationToken ct)
	{
		_ = 1;
		try
		{
			await sending.WaitAsync(ct);
			try
			{
				AvatarMessage avatarMessage;
				WebSocket webSocket;
				lock (stateGate)
				{
					avatarMessage = latest;
					webSocket = socket;
				}
				if (webSocket != null && webSocket.State == WebSocketState.Open)
				{
					await webSocket.SendAsync(avatarMessage.Encode(), WebSocketMessageType.Text, endOfMessage: true, ct);
				}
			}
			finally
			{
				sending.Release();
			}
		}
		catch (Exception ex) when (((ex is OperationCanceledException || ex is WebSocketException || ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
		}
	}

	public Task SetSpeechFrameAsync(string utterance, string mood, float mouth)
	{
		bool flag = !float.IsFinite(mouth);
		if (!flag)
		{
			bool flag2 = ((mouth < 0f || mouth > 1f) ? true : false);
			flag = flag2;
		}
		if (flag || !VoiceOptions.Moods.Contains(mood) || utterance.Length > 80)
		{
			throw new ArgumentException("Invalid speech animation frame.");
		}
		if (!Running)
		{
			return Task.CompletedTask;
		}
		lock (stateGate)
		{
			latest = new AvatarMessage
			{
				Session = session,
				Sequence = Interlocked.Increment(ref sequence),
				State = "speaking",
				Utterance = utterance,
				Mouth = mouth,
				Emotion = mood,
				Presentation = presentation
			};
		}
		return SendLatest(stop?.Token ?? CancellationToken.None);
	}

	internal void DisconnectForVerification()
	{
		socket?.Abort();
	}

	public Task UpdatePresentationAsync(AvatarPresentation config)
	{
		if (!config.Valid())
		{
			throw new ArgumentException("Invalid avatar presentation.");
		}
		presentation = config;
		return SetStateAsync(latest.State, latest.Preview, latest.Cue, latest.Utterance);
	}

	public async Task StopAsync()
	{
		await lifecycle.WaitAsync();
		try
		{
			await StopCore();
			this.Status?.Invoke("Avatar stopped");
		}
		finally
		{
			lifecycle.Release();
		}
	}

	private async Task StopCore()
	{
		Process owned = process;
		WebSocket? webSocket = socket;
		if (webSocket != null && webSocket.State == WebSocketState.Open && owned != null && !owned.HasExited)
		{
			lock (stateGate)
			{
				latest = new AvatarMessage
				{
					Session = session,
					Sequence = Interlocked.Increment(ref sequence),
					Type = "shutdown",
					Presentation = presentation
				};
			}
			using CancellationTokenSource grace = new CancellationTokenSource(500);
			await SendLatest(grace.Token);
			try
			{
				await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(1200.0));
			}
			catch (TimeoutException)
			{
			}
		}
		stop?.Cancel();
		listener?.Stop();
		listener = null;
		Port = 0;
		socket?.Abort();
		socket = null;
		process = null;
		job?.Dispose();
		job = null;
		if (owned != null)
		{
			try
			{
				if (!owned.HasExited)
				{
					owned.Kill(entireProcessTree: true);
					await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3.0));
				}
			}
			catch (Exception ex2) when (((ex2 is InvalidOperationException || ex2 is Win32Exception || ex2 is TimeoutException) ? 1 : 0) != 0)
			{
			}
			owned.Dispose();
		}
		stop?.Dispose();
		stop = null;
		ready?.TrySetCanceled();
	}

	private void Log(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		try
		{
			lock (logGate)
			{
				string text2 = Path.Combine(root, "logs", "avatar.log");
				Directory.CreateDirectory(Path.GetDirectoryName(text2));
				if (File.Exists(text2) && new FileInfo(text2).Length > 256000)
				{
					File.Move(text2, text2 + ".previous", overwrite: true);
				}
				File.AppendAllText(text2, DateTimeOffset.Now.ToString("O") + " " + text.Truncate(1200) + Environment.NewLine);
			}
		}
		catch (IOException)
		{
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (!disposed)
		{
			disposed = true;
			await StopAsync();
		}
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateJobObject(nint a, string? name);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool SetInformationJobObject(SafeFileHandle j, int c, ref JobLimits i, uint n);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool AssignProcessToJobObject(SafeFileHandle j, nint p);
}
