using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace AquilaLocal;

public static class ConversationVerification
{
	public static async Task RunAsync(string folder, string model)
	{
		string folder2 = folder;
		List<string> checks = new List<string>();
		Storage store = new Storage(Path.Combine(folder2, "store"));
		ChatSession a = new ChatSession
		{
			Title = "Synthetic first",
			Turns = new List<Turn>(1)
			{
				new Turn
				{
					User = "First",
					Answer = "Answer",
					UsageRequests = 1
				}
			}
		};
		ChatSession chatSession = new ChatSession
		{
			Title = "Synthetic neighbor"
		};
		store.SaveChat(a);
		store.SaveChat(chatSession);
		string text = Path.Combine(store.Root, "chats", a.Id + ".json");
		string text2 = File.ReadAllText(Path.Combine(store.Root, "chats", chatSession.Id + ".json"));
		File.WriteAllText(text + ".tmp", "Synthetic partial write");
		store.DeleteChat(a.Id);
		Check(!File.Exists(text) && !File.Exists(text + ".tmp") && File.ReadAllText(Path.Combine(store.Root, "chats", chatSession.Id + ".json")) == text2, "Deletion removes only the selected chat and its temporary snapshot");
		store.DeleteChat(a.Id);
		Check(store.LoadChats().Count == 1, "Deleting a missing saved file is safe and preserves neighbors");
		bool rejected3 = false;
		try
		{
			store.DeleteChat("../outside");
		}
		catch (ArgumentException)
		{
			rejected3 = true;
		}
		Check(rejected3 && File.Exists(Path.Combine(store.Root, "chats", chatSession.Id + ".json")), "Deletion rejects path traversal IDs");
		Application.Current.ShutdownMode = (ShutdownMode)2;
		Storage uiStore = new Storage(Path.Combine(folder2, "ui"));
		uiStore.SaveSettings(new Settings
		{
			ModelPath = model
		});
		uiStore.SaveChat(a);
		uiStore.SaveChat(chatSession);
		new MemoryStore(uiStore.Root).Apply(turn: new Turn
		{
			User = "I prefer centimeters."
		}, candidates: new _003C_003Ez__ReadOnlySingleElementList<MemoryCandidate>(new MemoryCandidate("units", "centimeters", "centimeters", "preference", "personal", 1.0, Array.Empty<string>())), workspace: "", session: a);
		MainWindow mainWindow = new MainWindow(uiStore.Root);
		((Window)mainWindow).Show();
		ChatSession selectedItem = ((IEnumerable<ChatSession>)((ItemsControl)mainWindow.ChatsList).ItemsSource).Single((ChatSession c) => c.Id == a.Id);
		((Selector)mainWindow.ChatsList).SelectedItem = selectedItem;
		int prompts = 0;
		mainWindow.ConfirmDeleteForVerification = delegate
		{
			prompts++;
			return false;
		};
		((RoutedCommand)MainWindow.DeleteChatCommand).Execute((object)null, (IInputElement)(object)mainWindow.ChatsList);
		Check(prompts == 1 && File.Exists(Path.Combine(uiStore.Root, "chats", a.Id + ".json")) && ((CollectionView)((ItemsControl)mainWindow.ChatsList).Items).Count == 2, "Declining confirmation keeps the selected chat in UI and storage");
		mainWindow.ConfirmDeleteForVerification = delegate
		{
			prompts++;
			return true;
		};
		((RoutedCommand)MainWindow.DeleteChatCommand).Execute((object)null, (IInputElement)(object)mainWindow.ChatsList);
		Check(prompts == 2 && ((CollectionView)((ItemsControl)mainWindow.ChatsList).Items).Count == 1 && ((ChatSession)((Selector)mainWindow.ChatsList).SelectedItem).Id == chatSession.Id && !File.Exists(Path.Combine(uiStore.Root, "chats", a.Id + ".json")), "Confirmed command deletes the selected chat and selects its neighbor");
		Check(new MemoryStore(uiStore.Root).Snapshot().Any((MemoryEntry e) => e.Value == "centimeters"), "Chat deletion preserves separately learned memories as the prompt explains");
		((RoutedCommand)MainWindow.DeleteChatCommand).Execute((object)null, (IInputElement)(object)mainWindow.ChatsList);
		Check(((CollectionView)((ItemsControl)mainWindow.ChatsList).Items).Count == 1 && ((ChatSession)((Selector)mainWindow.ChatsList).SelectedItem).Turns.Count == 0 && ((ChatSession)((Selector)mainWindow.ChatsList).SelectedItem).Id != chatSession.Id, "Deleting the last chat opens a fresh blank conversation");
		Check(((IEnumerable)((UIElement)mainWindow.ChatsList).InputBindings).OfType<KeyBinding>().Any((KeyBinding k) => (int)k.Key == 32 && ((InputBinding)k).Command == MainWindow.DeleteChatCommand) && !((IEnumerable)((UIElement)mainWindow).InputBindings).OfType<KeyBinding>().Any((KeyBinding k) => (int)k.Key == 32), "Delete shortcut is restricted to the conversation list");
		TaskCompletionSource closed = new TaskCompletionSource();
		((Window)mainWindow).Closed += delegate
		{
			closed.SetResult();
		};
		((Window)mainWindow).Close();
		await closed.Task.WaitAsync(TimeSpan.FromSeconds(8.0));
		Check(uiStore.LoadChats().Count == 0, "Closing the window does not resurrect a deleted chat");
		ChatSession chat = new ChatSession
		{
			Title = "Compaction fixture"
		};
		string text3 = string.Join(" ", Enumerable.Repeat("We discussed routine project organization and reviewed the existing checklist. No additional final decision was made in this explanatory paragraph.", 14));
		chat.Turns.AddRange(new Turn[4]
		{
			new Turn
			{
				User = "Our project codename is CEDAR-7319. It is a small warehouse planning project.",
				Answer = "Confirmed: the project is CEDAR-7319. " + text3,
				UsageRequests = 1
			},
			new Turn
			{
				User = "Correction: use centimeters for all project measurements, never inches.",
				Answer = "Use centimeters, not inches. " + text3,
				UsageRequests = 1
			},
			new Turn
			{
				User = "The delivery date is undecided. Do not turn a suggested date into a commitment.",
				Answer = "There is no committed delivery date. " + text3,
				UsageRequests = 1
			},
			new Turn
			{
				User = "Please review this synthetic reference.",
				Answer = "The reference identifies the inventory label as BLUE-427. " + text3,
				Attachments = new List<Attachment>(1)
				{
					new Attachment
					{
						Name = "reference.txt",
						Text = "The inventory label is BLUE-427."
					}
				},
				UsageRequests = 1
			}
		});
		for (int i = 0; i < 4; i++)
		{
			chat.Turns.Add(new Turn
			{
				User = $"Recent conversation item {i}: explain why a checklist is helpful.",
				Answer = "A checklist records the steps and reduces omissions. " + text3,
				UsageRequests = 1
			});
		}
		string original = JsonSerializer.Serialize(chat.Turns);
		Check(ConversationContext.CanCompact(chat) && !ConversationContext.CanCompact(new ChatSession
		{
			Turns = chat.Turns.Take(4).ToList()
		}), "Compaction preserves four recent complete turns and requires earlier history");
		Settings settings = new Settings
		{
			ModelPath = model,
			ProjectorPath = "",
			VisionDisabled = true,
			GpuLayers = -1,
			CpuThreads = 2,
			Context = 8192,
			MaxTokens = 2048,
			Thinking = true,
			ThinkingBudget = 512,
			WorkspaceTools = false,
			LearnMemories = false,
			UseMemories = false
		};
		using (Engine engine = new Engine(Path.Combine(folder2, "engine-data")))
		{
			ChatCompactor compactor = new ChatCompactor(engine);
			using CancellationTokenSource limit = new CancellationTokenSource(TimeSpan.FromMinutes(5.0));
			ContextCheckpoint checkpoint = await compactor.CompactAsync(settings, chat, limit.Token);
			Check(checkpoint.Summary.Contains("CEDAR-7319", StringComparison.OrdinalIgnoreCase) && checkpoint.Summary.Contains("centimeter", StringComparison.OrdinalIgnoreCase), "Real Deckard summary preserves the project identifier and corrected units");
			Check(checkpoint.CoveredTurns == 4 && checkpoint.ThroughTurnId == chat.Turns[3].Id && checkpoint.SummaryTokens < checkpoint.SourceTokens, "Compaction covers a contiguous prefix and produces a smaller token-counted representation");
			Check(JsonSerializer.Serialize(chat.Turns) == original && chat.CompactedContext == null, "Summary generation leaves all original messages and the active checkpoint unchanged until committed");
			chat.CompactedContext = checkpoint;
			store.SaveChat(chat);
			ChatSession chatSession2 = store.LoadChats().Single((ChatSession c) => c.Id == chat.Id);
			Check(chatSession2.CompactedContext?.Summary == checkpoint.Summary && chatSession2.CompactionPromptTokens > 0 && chatSession2.CompactionCompletionTokens > 0 && chatSession2.TotalTokens == chatSession2.CompactionPromptTokens + chatSession2.CompactionCompletionTokens, "Compaction checkpoint and cumulative overhead survive persistence exactly once");
			Check((from t in ConversationContext.ActiveTurns(chatSession2)
				select t.Id).SequenceEqual(from t in chat.Turns.TakeLast(4)
				select t.Id), "Future requests retain the most recent four turns verbatim and exclude summarized originals");
			chatSession2.CompactedContext.ThroughTurnId = "missing";
			Check(ConversationContext.Checkpoint(chatSession2) == null && ConversationContext.ActiveTurns(chatSession2).Count == 8, "An invalid summary anchor falls back to original history without dropping turns");
			Turn question = new Turn
			{
				User = "What is our project codename and preferred measurement unit? Answer both.",
				Status = "generating"
			};
			chat.Turns.Add(question);
			await new Agent(engine).RunAsync(settings, chat, question, limit.Token);
			Check(question.Answer.Contains("CEDAR-7319", StringComparison.OrdinalIgnoreCase) && question.Answer.Contains("centimeter", StringComparison.OrdinalIgnoreCase) && question.Activity.Any((string s) => s.StartsWith("Using compacted context")), "Real chat recalls summarized facts after the original turns are excluded from input");
			long used2 = chat.TotalTokens;
			ContextCheckpoint again = await compactor.CompactAsync(settings, chat, limit.Token);
			Check(again.CoveredTurns == 5 && again.Summary.Contains("CEDAR-7319", StringComparison.OrdinalIgnoreCase) && chat.TotalTokens > used2, "Repeated compaction merges the earlier summary and advances the checkpoint with additional accounted usage");
			using CancellationTokenSource canceled = new CancellationTokenSource();
			canceled.Cancel();
			rejected3 = false;
			used2 = chat.TotalTokens;
			try
			{
				await compactor.CompactAsync(settings, chat, canceled.Token);
			}
			catch (OperationCanceledException)
			{
				rejected3 = true;
			}
			Check(rejected3 && chat.CompactedContext == checkpoint && chat.TotalTokens == used2, "Cancellation before inference preserves the previous checkpoint and usage totals");
			ChatSession chatSession3 = new ChatSession();
			List<Turn> list = new List<Turn>();
			list.Add(new Turn
			{
				User = string.Join(" ", Enumerable.Repeat("oversized transcript", 15000)),
				Answer = "Recorded.",
				UsageRequests = 1
			});
			list.AddRange(chat.Turns.TakeLast(4));
			chatSession3.Turns = list;
			ChatSession oversized = chatSession3;
			rejected3 = false;
			try
			{
				await compactor.CompactAsync(settings, oversized, limit.Token);
			}
			catch (ArgumentException)
			{
				rejected3 = true;
			}
			Check(rejected3 && oversized.CompactedContext == null && oversized.CompactionPromptTokens == 0, "Oversized earliest turns are rejected without skipping history or starting inference");
			chat.CompactedContext = null;
			store.SaveChat(chat);
			chatSession2 = store.LoadChats().Single((ChatSession c) => c.Id == chat.Id);
			Check(chatSession2.CompactedContext == null && chatSession2.TotalTokens == used2 && ConversationContext.ActiveTurns(chatSession2).Count == 9, "Resetting compaction restores original history eligibility without resetting cumulative usage");
			engine.Stop();
			Check(!engine.Ready, "Verification unloads the model after the bounded GPU run");
			File.WriteAllText(Path.Combine(folder2, "result.json"), JsonSerializer.Serialize(new
			{
				checks = checks.Count,
				checkpoint = checkpoint,
				repeatedCheckpoint = again,
				answer = question.Answer,
				totalTokens = chat.TotalTokens,
				cpuThreads = 2,
				context = 8192
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		void Check(bool ok, string label)
		{
			if (!ok)
			{
				throw new Exception("Failed: " + label);
			}
			checks.Add("PASS " + label);
			File.WriteAllLines(Path.Combine(folder2, "checks.txt"), checks);
		}
	}
}
