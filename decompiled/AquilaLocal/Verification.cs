using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AquilaLocal;

public static class Verification
{
	public static async Task RunAsync(string directory, string? modelPath = null)
	{
		string directory2 = directory;
		List<string> results = new List<string>();
		string text = Path.Combine(directory2, "data");
		Storage storage = new Storage(text);
		Settings settings = new Settings
		{
			ModelPath = (modelPath ?? Path.Combine(AppContext.BaseDirectory, "models", "Heretic-Q4_K_M.gguf")),
			MaxTokens = 256,
			Context = 4096,
			GpuLayers = 0,
			CpuThreads = 2
		};
		settings.Validate();
		storage.SaveSettings(settings);
		Check(storage.LoadSettings().MaxTokens == 256, "Settings round trip");
		ChatSession session4 = new ChatSession
		{
			Title = "Test persistence",
			Turns = new List<Turn>(1)
			{
				new Turn
				{
					User = "Hello",
					Answer = "Hi",
					Status = "generating"
				}
			}
		};
		storage.SaveChat(session4);
		Check(storage.LoadChats().Single().Turns.Single().Status == "interrupted", "Interrupted turn recovery");
		File.WriteAllText(Path.Combine(text, "chats", "broken.json"), "{broken");
		Check(storage.LoadChats().Count == 1 && Directory.GetFiles(Path.Combine(text, "chats"), "*.corrupt-*").Length == 1, "Corrupt history preserved without losing valid chats");
		string workspace = Path.Combine(directory2, "workspace");
		Directory.CreateDirectory(workspace);
		File.WriteAllText(Path.Combine(workspace, "manifest.txt"), "Project: River Lantern\nInventory verification number: 7319\nStatus: ready\n");
		Directory.CreateDirectory(Path.Combine(workspace, "nested"));
		File.WriteAllText(Path.Combine(workspace, "nested", "dialogue.md"), "# Dialogue\nCaptain: Welcome aboard.\n");
		WorkspaceTools workspaceTools = new WorkspaceTools(workspace);
		Check(workspaceTools.Execute("read_file", "{\"path\":\"manifest.txt\"}", default(CancellationToken)).Contains("7319"), "Workspace text reading");
		Check(workspaceTools.Execute("search_files", "{\"query\":\"captain\"}", default(CancellationToken)).Contains("dialogue.md"), "Workspace literal search");
		Check(workspaceTools.Execute("list_files", "{\"path\":\".\"}", default(CancellationToken)).Contains("manifest.txt"), "Workspace listing");
		Check(workspaceTools.Execute("read_file", "{\"path\":\"../outside.txt\"}", default(CancellationToken)).Contains("outside the selected workspace"), "Traversal rejected");
		Check(workspaceTools.Execute("read_file", "{\"path\":\"C:\\\\Windows\\\\win.ini\"}", default(CancellationToken)).Contains("relative workspace path"), "Absolute path rejected");
		Check(workspaceTools.Execute("read_file", "{\"path\":\"manifest.txt:stream\"}", default(CancellationToken)).Contains("relative workspace path"), "Alternate data stream rejected");
		Check(workspaceTools.Execute("run_command", "{}", default(CancellationToken)).Contains("Unknown tool"), "Unknown tool rejected");
		Check(workspaceTools.Execute("read_file", "not-json", default(CancellationToken)).Contains("error"), "Invalid tool JSON handled");
		using (CancellationTokenSource cancellationTokenSource = new CancellationTokenSource())
		{
			cancellationTokenSource.Cancel();
			bool condition2 = false;
			try
			{
				workspaceTools.Execute("list_files", "{}", cancellationTokenSource.Token);
			}
			catch (OperationCanceledException)
			{
				condition2 = true;
			}
			Check(condition2, "Workspace scan cancellation");
		}
		using (Engine engine = new Engine(storage.Root))
		{
			using CancellationTokenSource deadline = new CancellationTokenSource(TimeSpan.FromMinutes(12.0));
			await engine.StartAsync(settings, deadline.Token);
			Check(engine.Ready, "Real GGUF model loaded by bundled engine in CPU-only / two-thread mode");
			File.WriteAllText(Path.Combine(directory2, "engine-process.json"), JsonSerializer.Serialize(new
			{
				pid = engine.ProcessId,
				port = engine.Port
			}));
			int? initialPid = engine.ProcessId;
			await engine.StartAsync(settings, deadline.Token);
			Check(engine.ProcessId == initialPid, "Loaded model reused");
			ChatSession session3 = new ChatSession();
			Turn turn4 = new Turn
			{
				User = "What is 19 + 23? Reply with only the integer.",
				Status = "generating"
			};
			session3.Turns.Add(turn4);
			int chunks = 0;
			Agent agent = new Agent(engine);
			agent.Token += delegate
			{
				chunks++;
			};
			await agent.RunAsync(settings, session3, turn4, deadline.Token);
			File.WriteAllText(Path.Combine(directory2, "arithmetic.json"), JsonSerializer.Serialize(turn4, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			Check(turn4.Answer.Contains("42") && turn4.Status == "complete" && chunks > 0, "Real inference and streamed answer");
			settings.Memory = "The user's project codename is CEDAR-42.";
			turn4 = new Turn
			{
				User = "What is my project codename? Reply only with the codename.",
				Status = "generating"
			};
			session3.Turns.Add(turn4);
			await agent.RunAsync(settings, session3, turn4, deadline.Token);
			Check(turn4.Answer.Contains("CEDAR-42"), "Persistent memory injected into real prompt");
			settings.Workspace = workspace;
			settings.Temperature = 0.0;
			settings.Thinking = false;
			settings.RequireLookup = true;
			settings.ThinkingBudget = 128;
			settings.MaxTokens = 768;
			turn4 = new Turn
			{
				User = "Read manifest.txt using the read_file function with path manifest.txt. Then tell me the inventory verification number from the file. You must call the function to obtain it.",
				Status = "generating"
			};
			session3 = new ChatSession
			{
				Turns = new List<Turn>(1) { turn4 }
			};
			await agent.RunAsync(settings, session3, turn4, deadline.Token);
			File.WriteAllText(Path.Combine(directory2, "tools.json"), JsonSerializer.Serialize(turn4, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			Check(turn4.Answer.Contains("7319") && turn4.Activity.Any((string a) => a.StartsWith("read_file")), "Real model tool call, execution, and grounded answer");
			Check(turn4.UsageRequests >= 2 && turn4.PromptTokens > 0 && turn4.CompletionTokens > 0 && !turn4.UsageIncomplete, "Usage includes every model request in tool loop");
			settings.Workspace = "";
			settings.Memory = "";
			settings.MaxTokens = 2048;
			settings.Thinking = false;
			Turn cancelTurn = new Turn
			{
				User = "Write a very long numbered list of 1000 distinct sentences about boats. Keep going until all 1000 are written.",
				Status = "generating"
			};
			ChatSession session5 = new ChatSession
			{
				Turns = new List<Turn>(1) { cancelTurn }
			};
			CancellationTokenSource cancel = new CancellationTokenSource();
			try
			{
				Agent agent2 = new Agent(engine);
				agent2.Token += delegate
				{
					if (cancelTurn.Answer.Length > 40)
					{
						cancel.Cancel();
					}
				};
				bool stopped = false;
				try
				{
					await agent2.RunAsync(settings, session5, cancelTurn, cancel.Token);
				}
				catch (OperationCanceledException)
				{
					stopped = true;
				}
				Check(stopped && cancelTurn.Answer.Length > 0, "Streaming generation can be stopped with partial output retained");
				Check(cancelTurn.UsageIncomplete, "Cancelled stream is marked as incomplete token accounting");
			}
			finally
			{
				if (cancel != null)
				{
					((IDisposable)cancel).Dispose();
				}
			}
			turn4 = new Turn
			{
				User = "Reply with the word READY.",
				Status = "generating"
			};
			session3 = new ChatSession
			{
				Turns = new List<Turn>(1) { turn4 }
			};
			settings.MaxTokens = 128;
			await agent.RunAsync(settings, session3, turn4, deadline.Token);
			Check(turn4.Answer.Contains("READY", StringComparison.OrdinalIgnoreCase), "Engine accepts new request after cancellation");
			int value = engine.ProcessId.Value;
			engine.Stop();
			bool flag;
			try
			{
				using Process process = Process.GetProcessById(value);
				flag = process.HasExited;
			}
			catch (ArgumentException)
			{
				flag = true;
			}
			Check(flag && !engine.Ready, "Unload releases engine process");
			File.WriteAllText(Path.Combine(directory2, "success.json"), JsonSerializer.Serialize(new
			{
				passed = results.Count,
				engine = "b12110 CPU-only / two threads / below-normal priority",
				model = Path.GetFileName(settings.ModelPath),
				finished = DateTimeOffset.Now
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		void Check(bool condition, string label)
		{
			if (!condition)
			{
				throw new Exception("Failed: " + label);
			}
			results.Add("PASS " + label);
			File.WriteAllLines(Path.Combine(directory2, "checks.txt"), results);
		}
	}
}
