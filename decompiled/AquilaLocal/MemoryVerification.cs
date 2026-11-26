using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AquilaLocal;

public static class MemoryVerification
{
	public static async Task RunAsync(string folder, string modelPath)
	{
		string folder2 = folder;
		List<string> results = new List<string>();
		string root = Path.Combine(folder2, "deterministic");
		MemoryStore store = new MemoryStore(root);
		string projectA = Path.Combine(folder2, "project-a");
		string text = Path.Combine(folder2, "project-b");
		Directory.CreateDirectory(projectA);
		Directory.CreateDirectory(text);
		ChatSession chatSession = new ChatSession();
		Turn turn = new Turn
		{
			User = "My preferred units are metric units."
		};
		chatSession.Turns.Add(turn);
		MemoryCandidate memoryCandidate = new MemoryCandidate("preferred_units", "metric units", turn.User, "preference", "personal", 0.99, Array.Empty<string>());
		MemoryChange memoryChange = store.Apply(new _003C_003Ez__ReadOnlySingleElementList<MemoryCandidate>(memoryCandidate), "", chatSession, turn);
		Check(memoryChange.Saved == 1 && store.Snapshot().Single().Sources.Single().TurnId == turn.Id, "Automatic entries preserve exact supporting user quote and chat/turn provenance");
		store.Apply(new _003C_003Ez__ReadOnlySingleElementList<MemoryCandidate>(memoryCandidate), "", chatSession, turn);
		Check(store.Snapshot().Single().Confirmations == 1, "Repeated processing of one turn does not duplicate evidence");
		Turn turn2 = new Turn
		{
			User = "Actually my preferred units are imperial units."
		};
		chatSession.Turns.Add(turn2);
		memoryChange = store.Apply(new _003C_003Ez__ReadOnlySingleElementList<MemoryCandidate>(memoryCandidate with
		{
			Value = "imperial units",
			Quote = turn2.User
		}), "", chatSession, turn2);
		Check(memoryChange.Updated == 1 && store.Snapshot().Count((MemoryEntry e) => e.State == "active") == 1 && store.Retrieve("", "units").Single().Value == "imperial units", "Corrections supersede old facts without retrieving stale values");
		MemoryEntry active = store.Snapshot().Single((MemoryEntry e) => e.State == "active");
		store.SaveManual(active.Id, "", personal: true, active.Topic, active.Value, active.Kind, pinned: true);
		memoryChange = store.Apply(new _003C_003Ez__ReadOnlySingleElementList<MemoryCandidate>(memoryCandidate), "", chatSession, turn);
		Check(memoryChange.Review == 1 && store.Retrieve("", "units").All((MemoryEntry e) => e.Value != "metric units"), "Pinned conflict waits for review and cannot replace active memory automatically");
		store.SaveManual(null, projectA, personal: false, "project_code", "RIVER-A", "fact", pinned: false);
		store.SaveManual(null, text, personal: false, "project_code", "RIVER-B", "fact", pinned: false);
		Check(store.Retrieve(projectA, "project code").Any((MemoryEntry e) => e.Value == "RIVER-A") && store.Retrieve(projectA, "project code").All((MemoryEntry e) => e.Value != "RIVER-B") && store.Retrieve("", "project code").All((MemoryEntry e) => e.Scope == "personal"), "Project memory is isolated by canonical folder and personal scope crosses projects");
		Turn turn3 = new Turn
		{
			User = "Our project renderer uses Vulkan for every scene."
		};
		store.Apply(new _003C_003Ez__ReadOnlySingleElementList<MemoryCandidate>(new MemoryCandidate("renderer", "Vulkan", turn3.User, "decision", "personal", 0.99, Array.Empty<string>())), projectA, chatSession, turn3);
		Check(store.Snapshot().Single((MemoryEntry e) => e.Topic == "renderer").Scope == MemoryStore.ScopeFor(projectA), "Automatic project facts cannot become personal without explicit global wording");
		Turn turn4 = new Turn
		{
			User = "Across all projects my preferred editor is Aquila."
		};
		store.Apply(new _003C_003Ez__ReadOnlySingleElementList<MemoryCandidate>(new MemoryCandidate("preferred_editor", "Aquila", turn4.User, "preference", "personal", 0.99, Array.Empty<string>())), projectA, chatSession, turn4);
		Check(store.Retrieve(text, "preferred editor").Any((MemoryEntry e) => e.Value == "Aquila" && e.Scope == "personal"), "Explicit across-project preferences can be personal and recalled in another workspace");
		MemoryChange memoryChange2 = store.Apply(new _003C_003Ez__ReadOnlySingleElementList<MemoryCandidate>(memoryCandidate with
		{
			Topic = "hallucinated",
			Value = "invented",
			Quote = "Invented quote"
		}), "", chatSession, turn);
		Check(memoryChange2.Rejected == 1, "Fabricated source quotes cannot enter the memory catalog");
		MemoryChange memoryChange3 = store.Apply(new _003C_003Ez__ReadOnlySingleElementList<MemoryCandidate>(memoryCandidate with
		{
			Topic = "hallucinated",
			Value = "not stated"
		}), "", chatSession, turn);
		Check(memoryChange3.Rejected == 1, "Values must be directly supported by their quoted user text");
		store.Forget(active.Id);
		Check((from e in store.Snapshot()
			where e.Topic == active.Topic
			select e).All((MemoryEntry e) => e.State == "forgotten" && e.Value == "" && e.Sources.Count == 0) && store.Apply(new _003C_003Ez__ReadOnlySingleElementList<MemoryCandidate>(memoryCandidate), "", chatSession, turn).Rejected == 1, "Forgetting clears all topic versions and evidence, blocks automatic relearning");
		store.AllowRelearning(active.Id);
		Check(store.Apply(new _003C_003Ez__ReadOnlySingleElementList<MemoryCandidate>(memoryCandidate), "", chatSession, turn).Saved == 1, "Explicit relearning control removes the forgotten-topic block");
		Check(new MemoryStore(root).Retrieve(text, "project code").Any((MemoryEntry e) => e.Value == "RIVER-B"), "Catalog and scope survive a fresh store instance");
		await Task.WhenAll(from i in Enumerable.Range(0, 6)
			select Task.Run(() => new MemoryStore(root).SaveManual(null, "", personal: true, "parallel_" + i, "value " + i, "fact", pinned: false)));
		Check(store.Snapshot().Count((MemoryEntry e) => e.Topic.StartsWith("parallel_")) == 6, "Concurrent writers retain every edit through the catalog mutex");
		Check(store.Retrieve(projectA, "remember all", 1).Count == 0, "Retrieval obeys its memory character budget");
		string text2 = Path.Combine(folder2, "damaged");
		Directory.CreateDirectory(text2);
		File.WriteAllText(Path.Combine(text2, "memories.json"), "{broken");
		MemoryStore memoryStore = new MemoryStore(text2);
		Check(memoryStore.Snapshot().Count == 0 && memoryStore.RecoveryNotice != null && Directory.GetFiles(text2, "*.corrupt-*").Length == 1, "Corrupt catalog is preserved and recovered without losing the original file");
		Check(!MemoryLearner.Eligible("My API key is a synthetic example") && !MemoryLearner.Eligible("Pretend my preferred color is blue") && !MemoryLearner.Eligible("My favorite color is blue, but don't remember this."), "Likely secrets, fictional instructions and explicit non-remember requests skip automatic extraction");
		Storage storage = new Storage(Path.Combine(folder2, "live-data"));
		MemoryStore live = new MemoryStore(storage.Root);
		Settings settings = new Settings
		{
			ModelPath = modelPath,
			VisionDisabled = true,
			ProjectorPath = "",
			GpuLayers = -1,
			CpuThreads = 2,
			Context = 4096,
			MaxTokens = 128,
			Temperature = 0.0,
			WorkspaceTools = false
		};
		using (Engine engine = new Engine(storage.Root))
		{
			using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5.0));
			await engine.StartAsync(settings, timeout.Token);
			MemoryLearner learner = new MemoryLearner(engine, live);
			ChatSession sourceChat = new ChatSession();
			Turn sourceTurn = new Turn
			{
				User = "Please remember that my project codename is SILVER-OTTER-426.",
				Status = "complete"
			};
			sourceChat.Turns.Add(sourceTurn);
			MemoryChange memoryChange4 = await learner.LearnAsync(settings, sourceChat, sourceTurn, timeout.Token);
			storage.SaveChat(sourceChat);
			File.WriteAllText(Path.Combine(folder2, "learned.json"), JsonSerializer.Serialize(live.Snapshot(), new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			Check((object)memoryChange4 != null && live.Snapshot().Any((MemoryEntry e) => e.Value.Contains("SILVER-OTTER-426")), "Real Deckard automatically extracts a durable fact with validated source evidence");
			Check(sourceTurn.MemoryPromptTokens > 0 && sourceTurn.MemoryCompletionTokens > 0 && sourceChat.TotalTokens == sourceTurn.MemoryPromptTokens + sourceTurn.MemoryCompletionTokens && sourceTurn.UsageRequests == 1, "Memory inference overhead is counted once in cumulative session tokens");
			ChatSession followup = new ChatSession();
			Turn question = new Turn
			{
				User = "What is my project codename? Reply with just the codename.",
				Status = "generating"
			};
			followup.Turns.Add(question);
			await new Agent(engine, new MemoryStore(storage.Root)).RunAsync(settings, followup, question, timeout.Token);
			storage.SaveChat(followup);
			Check(question.Answer.Contains("SILVER-OTTER-426") && question.UsedMemoryIds.Count > 0, "Real model recalls saved information in a separate conversation with no source history");
			Turn turn5 = new Turn
			{
				User = "Correction: my project codename is now AMBER-FOX-919. Please remember the new codename.",
				Status = "complete"
			};
			sourceChat.Turns.Add(turn5);
			await learner.LearnAsync(settings, sourceChat, turn5, timeout.Token);
			storage.SaveChat(sourceChat);
			Check(live.Retrieve("", "project codename").Any((MemoryEntry e) => e.Value.Contains("AMBER-FOX-919")) && live.Retrieve("", "project codename").All((MemoryEntry e) => !e.Value.Contains("SILVER-OTTER-426")), "Real extraction recognizes a correction and retrieval excludes the superseded fact");
			settings.UseMemories = false;
			Turn disabled = new Turn
			{
				User = "What is my project codename? Say unknown if no information is supplied.",
				Status = "generating"
			};
			ChatSession isolated = new ChatSession
			{
				Turns = new List<Turn>(1) { disabled }
			};
			await new Agent(engine, live).RunAsync(settings, isolated, disabled, timeout.Token);
			Check(disabled.UsedMemoryIds.Count == 0 && !disabled.Answer.Contains("AMBER-FOX-919"), "Disabling recall prevents memory injection into a new chat");
			settings.LearnMemories = false;
			Turn skipped = new Turn
			{
				User = "Remember my favorite color is marigold.",
				Status = "complete"
			};
			Check((object)(await learner.LearnAsync(settings, sourceChat, skipped, timeout.Token)) == null && skipped.MemoryPromptTokens == 0, "Disabling learning makes no extraction request");
			File.WriteAllText(Path.Combine(folder2, "real-transcripts.json"), JsonSerializer.Serialize(new { sourceChat, followup, isolated }, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			File.Copy(Path.Combine(storage.Root, "memories.json"), Path.Combine(folder2, "catalog.json"), overwrite: true);
			engine.ProcessId.GetValueOrDefault();
			engine.Stop();
			Check(!engine.Ready && !engine.ProcessId.HasValue, "Memory verification unloads its model and releases the GPU");
			File.WriteAllText(Path.Combine(folder2, "result.txt"), $"{results.Count} checks passed. Real Deckard Q4_K_M, GPU, 2 CPU threads, 4096 context.\n");
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
