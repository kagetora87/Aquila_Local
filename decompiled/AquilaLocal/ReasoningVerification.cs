using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AquilaLocal;

public static class ReasoningVerification
{
	public static async Task RunAsync(string folder, string modelPath)
	{
		string folder2 = folder;
		List<string> results = new List<string>();
		Settings settings = new Settings
		{
			ModelPath = modelPath,
			VisionDisabled = true,
			GpuLayers = -1,
			CpuThreads = 2,
			Context = 4096,
			MaxTokens = 512,
			Thinking = true,
			ThinkingBudget = 256,
			WorkspaceTools = false,
			Temperature = 0.0
		};
		Check(settings.EffectiveThinkingBudget == 256, "Thinking budget reserves a final-answer allowance");
		settings.ThinkingBudget = 16384;
		settings.MaxTokens = 128;
		Check(settings.EffectiveThinkingBudget == 64, "Oversized thinking budget is capped by the total response allowance");
		settings.Thinking = false;
		Check(settings.EffectiveThinkingBudget == 0, "Thinking off requests zero reasoning budget");
		settings.Thinking = true;
		settings.ThinkingBudget = 256;
		settings.MaxTokens = 512;
		Storage storage = new Storage(Path.Combine(folder2, "data"));
		storage.SaveSettings(settings);
		Check(storage.LoadSettings().Thinking && storage.LoadSettings().ThinkingBudget == 256, "Thinking settings survive save/reload");
		using (Engine engine = new Engine(storage.Root))
		{
			using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4.0));
			ChatSession session = new ChatSession();
			Turn reasoning = new Turn
			{
				User = "What is 17 times 23? Reason before answering, then give just the final number.",
				Status = "generating"
			};
			session.Turns.Add(reasoning);
			await new Agent(engine).RunAsync(settings, session, reasoning, timeout.Token);
			File.WriteAllText(Path.Combine(folder2, "thinking.json"), JsonSerializer.Serialize(reasoning, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			Check(reasoning.Reasoning.Length > 0 && reasoning.Answer.Contains("391") && reasoning.Status == "complete", "Real Deckard produces separate reasoning and a correct completed final answer");
			Check(reasoning.ThinkingEnabled && reasoning.ThinkingTokenBudget == 256 && reasoning.CompletionTokens > 0 && !reasoning.UsageIncomplete, "Turn records effective thinking mode and complete combined output usage");
			settings.Thinking = false;
			Turn direct = new Turn
			{
				User = "Calculate 17 times 23. Reply with just the number.",
				Status = "generating"
			};
			ChatSession other = new ChatSession
			{
				Turns = new List<Turn>(1) { direct }
			};
			await new Agent(engine).RunAsync(settings, other, direct, timeout.Token);
			Check(direct.Answer.Contains("391") && direct.Reasoning.Length == 0 && !direct.ThinkingEnabled, "Switching thinking off yields a direct answer without reasoning text");
			settings.Thinking = true;
			settings.ThinkingBudget = 512;
			settings.MaxTokens = 1024;
			Turn stopped = new Turn
			{
				User = "Reason carefully through all possible arrangements of nine distinct books on three labeled shelves, and derive a general formula with multiple checks.",
				Status = "generating"
			};
			ChatSession stopping = new ChatSession
			{
				Turns = new List<Turn>(1) { stopped }
			};
			CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
			try
			{
				Agent agent = new Agent(engine);
				agent.Token += delegate(string _, bool thought)
				{
					if (thought && stopped.Reasoning.Length > 40)
					{
						cancel.Cancel();
					}
				};
				bool cancelled = false;
				try
				{
					await agent.RunAsync(settings, stopping, stopped, cancel.Token);
				}
				catch (OperationCanceledException)
				{
					cancelled = true;
					stopped.Status = "cancelled";
				}
				Check(cancelled && stopped.Reasoning.Length > 0, "Stop during thinking preserves partial reasoning and cancels promptly");
			}
			finally
			{
				if (cancel != null)
				{
					((IDisposable)cancel).Dispose();
				}
			}
			storage.SaveChat(session);
			storage.SaveChat(other);
			storage.SaveChat(stopping);
			Check(storage.LoadChats().Any((ChatSession c) => c.Turns.Any((Turn t) => t.ThinkingTokenBudget == 256 && t.Reasoning.Length > 0)), "Reasoning and effective budget persist in conversation history");
			engine.Stop();
			Check(!engine.Ready && !engine.ProcessId.HasValue, "Thinking verification unloads its model");
			File.WriteAllText(Path.Combine(folder2, "result.json"), JsonSerializer.Serialize(new
			{
				checks = results.Count,
				reasoningCharacters = reasoning.Reasoning.Length,
				answer = reasoning.Answer,
				cpuThreads = 2,
				context = 4096
			}));
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
}
