using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AquilaLocal;

internal static class InferenceVerification
{
	internal static async Task RunAsync(string folder, string modelPath, string? q4Model)
	{
		int checks = 0;
		using (CancellationTokenSource deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3.0)))
		{
			CancellationToken ct = deadline.Token;
			long gib = 1073741824L;
			InferencePreferences inferencePreferences = new InferencePreferences();
			HardwareInventory hardware = new HardwareInventory(48 * gib, 35 * gib, new InferenceGpu("Vulkan1", "High GPU", "b", 24 * gib, 22 * gib));
			HardwareInventory hardwareInventory = new HardwareInventory(16 * gib, 8 * gib, new InferenceGpu("Vulkan0", "Laptop GPU", "a", 8 * gib, 7 * gib));
			InferenceProfile inferenceProfile = InferenceProfile.Resolve(inferencePreferences, hardware);
			InferenceProfile inferenceProfile2 = InferenceProfile.Resolve(inferencePreferences, hardwareInventory);
			Check(inferenceProfile.Key == "High" && inferenceProfile.Requested(inferencePreferences) == 32768 && inferenceProfile.Maximum == 65536, "24 GiB VRAM selects the high profile with its 32K default and 64K cap");
			Check(inferenceProfile2.Key == "Laptop" && inferenceProfile2.Requested(inferencePreferences) == 16384 && inferenceProfile2.Maximum == 24576, "8 GiB VRAM and 16 GiB RAM select the laptop profile with its 16K default and 24K cap");
			Check(InferenceProfile.Resolve(inferencePreferences, hardwareInventory with
			{
				RamBytes = 48 * gib
			}).Key == "Custom" && InferenceProfile.Resolve(inferencePreferences, hardwareInventory with
			{
				Gpu = null
			}).Key == "Custom", "Unmatched hardware retains the Custom profile");
			InferenceGpu inferenceGpu = HardwareInventory.SelectGpu(new GpuAdapter[2]
			{
				new GpuAdapter("Laptop GPU", "a", 8 * gib, 0),
				new GpuAdapter("UMA", "b", 536870912L, 1)
			}, "Available devices:\n  Vulkan0: UMA (29700 MiB, 25000 MiB free)\n  Vulkan1: Laptop GPU (7956 MiB, 7018 MiB free)");
			Check(inferenceGpu?.Device == "Vulkan1" && inferenceGpu.DedicatedBytes == 8 * gib, "Integrated shared memory is excluded and actual Vulkan ID is retained");
			ModelInfo modelInfo = ModelInfo.Read(modelPath);
			Settings settings = new Settings
			{
				Inference = inferencePreferences,
				Hardware = hardware
			};
			InferenceLaunch inferenceLaunch = InferenceLaunch.Resolve(settings, modelInfo);
			List<string> list = new List<string>();
			inferenceLaunch.AddArguments(list.AddRange);
			Check(inferenceLaunch.FullOffload && inferenceLaunch.Device == "Vulkan1" && list.Contains("all") && list.Contains("--fit") && inferenceLaunch.Cache == "q8_0" && inferenceLaunch.Flash == "on", "High mode requests full offload, Flash Attention and q8 K and V");
			settings.Hardware = hardwareInventory;
			inferencePreferences.ExpertCpuLayers = 99;
			inferenceLaunch = InferenceLaunch.Resolve(settings, modelInfo);
			list.Clear();
			inferenceLaunch.AddArguments(list.AddRange);
			Check(inferenceLaunch.Layers == "auto" && inferenceLaunch.Cache == "q8_0" && list.Contains("mmap") && !list.Contains("mlock") && !list.Contains("--n-cpu-moe"), "Laptop uses mmap without mlock and ignores the MoE option for dense models");
			inferencePreferences.LaptopQ4 = true;
			ModelInfo model = modelInfo with
			{
				ExpertCount = 8,
				BlockCount = 24
			};
			inferenceLaunch = InferenceLaunch.Resolve(settings, model);
			list.Clear();
			inferenceLaunch.AddArguments(list.AddRange);
			Check(inferenceLaunch.FullOffload && inferenceLaunch.Cache == "q4_0" && inferenceLaunch.ExpertCpuLayers == 24 && list.Contains("--n-cpu-moe") && list.Contains("24"), "MoE caps expert placement by model layers while keeping other layers on GPU");
			InferencePreferences options = new InferencePreferences
			{
				CustomContext = 31744
			};
			inferenceProfile.Remember(options, 65536);
			Check(inferenceProfile2.Requested(options) == 16384 && inferenceProfile.Requested(options) == 65536 && InferenceProfile.Resolve(new InferencePreferences
			{
				Profile = "Custom"
			}, hardwareInventory).Key == "Custom", "Manual override and saved context are independent of automatic hardware selection");
			JsonObject jsonObject = new JsonObject();
			ChatSampling.Apply(jsonObject, settings, supportsDry: true);
			Check(((double?)jsonObject["temperature"]).GetValueOrDefault() == 0.95 && ((double?)jsonObject["min_p"]).GetValueOrDefault() == 0.06 && (double?)jsonObject["top_p"] == 1.0 && (int?)jsonObject["top_k"] == 0 && ((double?)jsonObject["dry_multiplier"]).GetValueOrDefault() == 0.8 && ((double?)jsonObject["dry_base"]).GetValueOrDefault() == 1.75 && (int?)jsonObject["dry_allowed_length"] == 2, "Creative requests transmit the intended values without additional top-k filtering");
			jsonObject.Clear();
			ChatSampling.Apply(jsonObject, settings, supportsDry: false);
			Check(((double?)jsonObject["repeat_penalty"]).GetValueOrDefault() == 1.05 && !jsonObject.ContainsKey("dry_multiplier"), "Fallback uses the supported repeat penalty key");
			HardwareInventory hardware2 = await HardwareInventory.DetectAsync(Engine.Executable);
			Storage store = new Storage(Path.Combine(folder, "data"));
			store.SaveSettings(new Settings
			{
				ModelPath = modelPath,
				Context = 31744,
				MaxTokens = 512,
				GpuLayers = 0
			});
			Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			MainWindow window = new MainWindow(store.Root);
			string oldAllowance = ((TextBlock)window.FindName("ContextThresholds")).Text;
			TextBlock allowance = (TextBlock)window.FindName("ContextValue");
			ComboBox profileCombo = (ComboBox)window.FindName("ProfileCombo");
			await WaitForHardware(window, ct);
			Check(!oldAllowance.Contains("Danger") && (InferenceProfile.Resolve(new InferencePreferences(), hardware2).Key != "Laptop" || allowance.Text.StartsWith("16,384")), "Startup hides the placeholder warning and applies the detected profile");
			if (InferenceProfile.Resolve(new InferencePreferences(), hardware2).Key == "Laptop")
			{
				profileCombo.SelectedValue = "Custom";
				Check(allowance.Text == "31,744 tokens", "Custom preserves the previous version's context allowance");
				profileCombo.SelectedValue = "Auto";
				Check(allowance.Text == "16,384 tokens", "Switching back restores the laptop allowance");
			}
			TaskCompletionSource closed = new TaskCompletionSource();
			window.Closed += delegate
			{
				closed.TrySetResult();
			};
			window.Close();
			await closed.Task.WaitAsync(TimeSpan.FromSeconds(10.0), ct);
			Check(store.LoadSettings().Inference?.Profile == "Auto", "The selected profile persists through normal WPF settings save");
			Engine engine = new Engine(store.Root);
			try
			{
				Settings live = new Settings
				{
					ModelPath = modelPath,
					Context = 16384,
					MaxTokens = 128,
					Hardware = hardware2,
					GpuLayers = -1,
					CpuThreads = 2,
					Inference = new InferencePreferences
					{
						Profile = "Laptop"
					},
					WorkspaceTools = false,
					LearnMemories = false,
					UseMemories = false
				};
				await engine.StartAsync(live, ct);
				Check(engine.SupportsDry && engine.SlotContext >= live.Context, "The running llama.cpp reports DRY support and the requested q8 context");
				JsonArray messages = new JsonArray(Message("system", "You write as the character Mira."));
				for (int num = 0; num < 32; num++)
				{
					messages.Add(Message("user", $"Scene {num}: " + string.Join(" ", Enumerable.Repeat("The river carries its lanterns past the fishing boats.", 8))));
					messages.Add(Message("assistant", $"Mira remembers scene {num}."));
				}
				messages.Add(Message("user", "Continue the story from the riverbank."));
				FittedPrompt fittedPrompt = await PromptWindow.FitAsync(messages, 32, 1400, (JsonArray value) => engine.CountPromptAsync(value, null, thinking: false, ct));
				int omittedPairs = fittedPrompt.OmittedPairs;
				int success;
				if (omittedPairs > 0 && omittedPairs < 32 && fittedPrompt.Messages.Count == messages.Count - fittedPrompt.OmittedPairs * 2 && JsonNode.DeepEquals(fittedPrompt.Messages[0], messages[0]))
				{
					JsonArray messages2 = fittedPrompt.Messages;
					if (JsonNode.DeepEquals(messages2[messages2.Count - 1], messages[messages.Count - 1]))
					{
						success = (fittedPrompt.Messages[1]["content"].ToString().StartsWith($"Scene {fittedPrompt.OmittedPairs}:") ? 1 : 0);
						goto IL_0e02;
					}
				}
				success = 0;
				goto IL_0e02;
				IL_0e02:
				Check((byte)success != 0, "Exact backend counting trims whole oldest pairs while preserving system and current request");
				Turn novel = new Turn
				{
					User = "Write one short sentence of fiction about a river lantern.",
					Status = "generating"
				};
				await new Agent(engine).RunAsync(live, new ChatSession
				{
					Turns = new List<Turn>(1) { novel }
				}, novel, ct);
				string status = novel.Status;
				bool flag = ((status == "complete" || status == "limit") ? true : false);
				Check(flag && novel.Answer.Length > 0 && novel.CompletionTokens > 0, "A real creative sampler request streams a reply");
				int? previousPid = engine.ProcessId;
				live.Inference.LaptopQ4 = true;
				live.ModelPath = q4Model ?? modelPath;
				live.Context = 24576;
				await engine.StartAsync(live, ct);
				Check(engine.ProcessId != previousPid && engine.SlotContext >= 24576, "Changing to q4 cache reloads the engine and starts at 24K");
				live.Inference.Creative = false;
				live.Temperature = 0.0;
				live.Instructions = "You are a helpful assistant.";
				Agent agent = new Agent(engine);
				ChatSession chatSession = new ChatSession();
				List<Turn> list2 = new List<Turn>(1);
				Turn item;
				novel = (item = new Turn
				{
					User = "Reply with one word: HELLO.",
					Status = "generating"
				});
				list2.Add(item);
				chatSession.Turns = list2;
				await agent.RunAsync(live, chatSession, novel, ct);
				File.WriteAllText(Path.Combine(folder, "q4-answer.json"), JsonSerializer.Serialize(new { novel.Answer, novel.Status, novel.CompletionTokens }));
				Check((q4Model != null) ? (novel.Status == "complete" && novel.Answer.Trim().Equals("HELLO", StringComparison.OrdinalIgnoreCase)) : (novel.Answer.Length > 0 && novel.CompletionTokens > 0), (q4Model == null) ? "Real q4-cache generation returns tokens" : "The installed Deckard 4B answers correctly with q4 cache at 24K");
				File.WriteAllText(Path.Combine(folder, "metrics.json"), JsonSerializer.Serialize(new
				{
					checks = checks,
					detected = InferenceProfile.Resolve(new InferencePreferences(), hardware2).Label,
					ramGiB = (double)hardware2.RamBytes / (double)gib,
					gpu = hardware2.Gpu?.Name,
					vramGiB = (double?)hardware2.Gpu?.DedicatedBytes / (double)gib,
					SlotContext = engine.SlotContext,
					q4Model = Path.GetFileName(live.ModelPath)
				}, new JsonSerializerOptions
				{
					WriteIndented = true
				}));
			}
			finally
			{
				if (engine != null)
				{
					((IDisposable)engine).Dispose();
				}
			}
		}
		void Check(bool flag2, string label)
		{
			if (!flag2)
			{
				throw new InvalidOperationException(label);
			}
			File.AppendAllLines(Path.Combine(folder, "checks.txt"), new global::_003C_003Ez__ReadOnlySingleElementList<string>("PASS " + label));
			checks++;
		}
	}

	private static JsonObject Message(string role, string text)
	{
		return new JsonObject
		{
			["role"] = role,
			["content"] = text
		};
	}

	private static async Task WaitForHardware(MainWindow window, CancellationToken ct)
	{
		TextBlock label = (TextBlock)window.FindName("HardwareText");
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(TimeSpan.FromSeconds(15.0));
		while (label.Text.StartsWith("Detecting"))
		{
			await Task.Delay(40, timeout.Token);
		}
	}
}
