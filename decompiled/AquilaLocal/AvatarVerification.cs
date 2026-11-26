using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AquilaAvatarProtocol;

namespace AquilaLocal;

public static class AvatarVerification
{
	private static async Task Until(Func<bool> condition, string why)
	{
		Stopwatch watch = Stopwatch.StartNew();
		while (!condition())
		{
			if (watch.Elapsed > TimeSpan.FromSeconds(12.0))
			{
				throw new Exception("Timeout: " + why);
			}
			await Task.Delay(35);
		}
	}

	public static async Task RunAsync(string folder, string smallModel)
	{
		string folder2 = folder;
		List<string> results = new List<string>();
		Storage storage = new Storage(Path.Combine(folder2, "data"));
		Settings options = new Settings
		{
			ModelPath = smallModel,
			GpuLayers = 0,
			CpuThreads = 2,
			Context = 4096,
			MaxTokens = 128,
			WorkspaceTools = false,
			Temperature = 0.0
		};
		Check(!options.AvatarEnabled && options.Avatar.Valid(), "Avatar defaults off with a bounded camera and frame rate");
		storage.SaveSettings(options);
		Check(!storage.LoadSettings().AvatarEnabled, "Disabled avatar setting survives save/reload");
		AvatarMessage avatarMessage = new AvatarMessage
		{
			Session = "s",
			State = "idle",
			Version = 2
		};
		Check(!avatarMessage.Valid("s") && !new AvatarMessage
		{
			Session = "old"
		}.Valid("new"), "Protocol rejects unsupported versions and stale sessions");
		Check(!new AvatarMessage
		{
			Session = "s",
			State = "invalid"
		}.Valid("s") && !new AvatarMessage
		{
			Session = "s",
			Presentation = new AvatarPresentation
			{
				Distance = float.NaN
			}
		}.Valid("s"), "Protocol rejects unknown states and invalid presentation values");
		AvatarHost host = new AvatarHost(storage.Root);
		try
		{
			Check(host.Port == 0 && !host.Running, "Disabled subsystem creates no listener or process");
			await host.StartAsync(new AvatarPresentation(), CancellationToken.None, headless: true);
			Check(host.Running && host.Connected && host.LastAppliedDetail == "model ready", "Exported avatar launches independently and loads the real VRM model");
			Check(host.Endpoint.Host == "127.0.0.1" && host.Token.Length == 64 && host.Session.Length == 32, "Connection uses loopback with a fresh session and launch token");
			ConcurrentQueue<string> transitions = new ConcurrentQueue<string>();
			host.Applied += delegate(AvatarMessage m)
			{
				transitions.Enqueue(m.State);
			};
			string[] states = AvatarMessage.States;
			foreach (string state in states)
			{
				long previous = host.LastAppliedSequence;
				await host.SetStateAsync(state, preview: true, "none", "test-turn");
				await Until(() => host.LastAppliedSequence > previous && host.LastAppliedState == state, state);
				Check(ok: true, "Exported renderer applies state: " + state);
			}
			bool rejected2 = false;
			using (ClientWebSocket wrong = new ClientWebSocket())
			{
				wrong.Options.SetRequestHeader("Authorization", "Bearer " + new string('0', 64));
				try
				{
					using CancellationTokenSource ct = new CancellationTokenSource(3000);
					await wrong.ConnectAsync(host.Endpoint, ct.Token);
				}
				catch (WebSocketException)
				{
					rejected2 = true;
				}
			}
			Check(rejected2 && host.Connected, "Unauthenticated WebSocket connection is rejected without disturbing the avatar");
			rejected2 = false;
			using (ClientWebSocket wrong = new ClientWebSocket())
			{
				wrong.Options.SetRequestHeader("Authorization", "Bearer " + host.Token);
				wrong.Options.SetRequestHeader("Origin", "http://example.invalid");
				try
				{
					using CancellationTokenSource ct = new CancellationTokenSource(3000);
					await wrong.ConnectAsync(host.Endpoint, ct.Token);
				}
				catch (WebSocketException)
				{
					rejected2 = true;
				}
			}
			Check(rejected2, "Browser-origin connection is rejected");
			long beforeCamera = host.LastAppliedSequence;
			await host.UpdatePresentationAsync(new AvatarPresentation
			{
				Distance = 3.9f,
				Height = 1.1f,
				Fov = 42f,
				Fps = 15
			});
			await Until(() => host.LastAppliedSequence > beforeCamera, "presentation ack");
			AvatarPresentation lastAppliedPresentation = host.LastAppliedPresentation;
			Check(lastAppliedPresentation != null && lastAppliedPresentation.Fps == 15 && (double)Math.Abs(lastAppliedPresentation.Distance - 3.9f) < 0.01 && (double)Math.Abs(lastAppliedPresentation.Height - 1.1f) < 0.01 && (double)Math.Abs(lastAppliedPresentation.Fov - 42f) < 0.01, "Camera and frame-rate configuration are acknowledged from actual Godot state");
			int connections = host.Connections;
			host.DisconnectForVerification();
			await host.SetStateAsync("thinking");
			await Until(() => host.Connections > connections && host.LastAppliedState == "thinking", "reconnect snapshot");
			Check(host.Connected, "Avatar reconnects and receives the latest state after a dropped connection");
			int child = host.ProcessId.Value;
			string oldSession = host.Session;
			string oldToken = host.Token;
			Process.GetProcessById(child).Kill();
			await Until(() => !host.Running, "independent exit");
			Check(!host.Running, "Avatar failure is isolated from the main harness process");
			await host.StartAsync(new AvatarPresentation(), CancellationToken.None, headless: true);
			Check(host.Session != oldSession && host.Token != oldToken && host.ProcessId != child && host.Connected, "Restart replaces the process, session and credentials");
			using Engine engine = new Engine(storage.Root);
			Agent agent = new Agent(engine);
			Turn turn = new Turn
			{
				User = "What is 6 times 7? Reply with just the number.",
				Status = "generating"
			};
			ChatSession session = new ChatSession
			{
				Turns = new List<Turn>(1) { turn }
			};
			agent.AnswerStarted += delegate
			{
				host.SetStateAsync("thinking", preview: false, "none", turn.Id);
			};
			using CancellationTokenSource limit = new CancellationTokenSource(TimeSpan.FromMinutes(3.0));
			await agent.RunAsync(options, session, turn, limit.Token);
			await host.SetStateAsync("idle");
			await Until(() => host.LastAppliedState == "idle", "chat idle");
			Check(turn.Answer.Contains("42") && transitions.Contains("thinking"), "Existing inference works while the avatar receives answer lifecycle events");
			engine.Stop();
			await host.StopAsync();
			Check(!host.Running && !host.Connected && host.Port == 0, "Shutdown closes the avatar process, socket and listener");
			Turn another = new Turn
			{
				User = "Reply with exactly OK.",
				Status = "generating"
			};
			ChatSession session2 = new ChatSession
			{
				Turns = new List<Turn>(1) { another }
			};
			await new Agent(engine).RunAsync(options, session2, another, limit.Token);
			Check(another.Answer.Contains("OK", StringComparison.OrdinalIgnoreCase) && host.Port == 0, "Existing chat remains functional with the avatar stopped and disabled");
			engine.Stop();
			AvatarHost missing = new AvatarHost(storage.Root, Path.Combine(folder2, "missing.exe"));
			try
			{
				rejected2 = false;
				try
				{
					await missing.StartAsync(new AvatarPresentation(), CancellationToken.None);
				}
				catch (FileNotFoundException)
				{
					rejected2 = true;
				}
				Check(rejected2 && !missing.Running && missing.Port == 0, "Missing renderer fails cleanly without leaking a listener");
				options.AvatarEnabled = true;
				options.Avatar = new AvatarPresentation
				{
					Fps = 15,
					Distance = 3.9f
				};
				storage.SaveSettings(options);
				Check(storage.LoadSettings().AvatarEnabled && storage.LoadSettings().Avatar.Fps == 15, "Enabled avatar and presentation settings persist");
				string text = File.ReadAllText(Path.Combine(storage.Root, "logs", "avatar.log"));
				Check(!text.Contains(host.Token) && !text.Contains(oldToken) && !text.Contains("SCRIPT ERROR"), "Avatar logs contain neither launch secrets nor runtime script errors");
				Application.Current.ShutdownMode = (ShutdownMode)2;
				bool[] array = new bool[2] { false, true };
				for (int j = 0; j < array.Length; j++)
				{
					bool enabled = array[j];
					Storage storage2 = new Storage(Path.Combine(folder2, "window-close-" + enabled));
					storage2.SaveSettings(new Settings
					{
						ModelPath = smallModel,
						AvatarEnabled = enabled
					});
					MainWindow window = new MainWindow(storage2.Root);
					TaskCompletionSource closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
					((Window)window).Closed += delegate
					{
						closed.TrySetResult();
					};
					((Window)window).Show();
					if (!enabled)
					{
						await Task.Delay(150);
					}
					else
					{
						await Until(() => window.AvatarForVerification.Connected, "WPF startup avatar");
					}
					((Window)window).Close();
					await closed.Task.WaitAsync(TimeSpan.FromSeconds(8.0));
					Check(!window.AvatarForVerification.Running && window.AvatarForVerification.Port == 0, "Actual main window closes cleanly with avatar " + (enabled ? "enabled" : "disabled"));
				}
				File.WriteAllText(Path.Combine(folder2, "result.json"), JsonSerializer.Serialize(new
				{
					checks = results.Count,
					chatAnswer = turn.Answer,
					states = transitions.ToArray(),
					renderer = "Godot 4.7.2 .NET Windows export",
					avatarModel = Path.GetFileName(host.ModelPath),
					cpuThreads = 2
				}, new JsonSerializerOptions
				{
					WriteIndented = true
				}));
			}
			finally
			{
				if (missing != null)
				{
					await missing.DisposeAsync();
				}
			}
		}
		finally
		{
			if (host != null)
			{
				await host.DisposeAsync();
			}
		}
		void Check(bool ok, string label)
		{
			if (!ok)
			{
				throw new Exception("Failed: " + label);
			}
			results.Add("PASS " + label);
			File.WriteAllLines(Path.Combine(folder2, "checks.txt"), results);
		}
	}

	public static async Task LifetimeFixture(string directory)
	{
		Directory.CreateDirectory(directory);
		AvatarHost host = new AvatarHost(new Storage(Path.Combine(directory, "data")).Root);
		await host.StartAsync(new AvatarPresentation(), CancellationToken.None, headless: true);
		File.WriteAllText(Path.Combine(directory, "child.json"), JsonSerializer.Serialize(new
		{
			hostPid = Environment.ProcessId,
			avatarPid = host.ProcessId
		}));
		await Task.Delay(-1);
	}
}
