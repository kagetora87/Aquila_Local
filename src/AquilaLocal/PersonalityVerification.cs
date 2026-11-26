using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AquilaLocal;

public static class PersonalityVerification
{
	public static async Task RunAsync(string folder, string modelPath)
	{
		List<string> results = new List<string>();
		Storage storage = new Storage(Path.Combine(folder, "data"));
		PersonalityStore profiles = new PersonalityStore(storage.Root);
		MemoryStore memories = new MemoryStore(storage.Root);
		Check(profiles.List().Count == 4 && PersonalityStore.Prompt(profiles.Find("default")) == "", "Default profile preserves existing instructions and built-in voices are available");
		PersonalityProfile concise = new PersonalityProfile
		{
			Name = "Concise test",
			Style = "Begin every response with Briefly: and use at most 40 words in a single paragraph. Use a direct, compact speaking style. Do not use bullet points.",
			Examples = "User: How do I begin?\nAssistant: Briefly: Start with one small, testable step."
		};
		profiles.Save(concise);
		PersonalityProfile checklist = new PersonalityProfile
		{
			Name = "Checklist test",
			Style = "Begin every answer with Checklist: on its own line. Then give exactly three short bullet points, each beginning with a hyphen. Use a methodical, practical voice.",
			Examples = "User: How do I begin?\nAssistant: Checklist:\n- Choose a goal.\n- Try a small step.\n- Check the result."
		};
		profiles.Save(checklist);
		Check(new PersonalityStore(storage.Root).Find(concise.Id).Examples == concise.Examples, "Custom profiles, speaking instructions and example exchanges persist");
		concise.Style += " Avoid filler.";
		profiles.Save(concise);
		Check(profiles.List().Count((PersonalityProfile p) => p.Id == concise.Id) == 1, "Editing a profile updates its existing entry");
		bool ok = false;
		try
		{
			profiles.Save(new PersonalityProfile
			{
				Name = concise.Name,
				Style = "Other"
			});
		}
		catch (ArgumentException)
		{
			ok = true;
		}
		Check(ok, "Duplicate profile names are rejected");
		ok = false;
		try
		{
			profiles.Delete("default");
		}
		catch (ArgumentException)
		{
			ok = true;
		}
		Check(ok && profiles.Find("missing").Id == "default", "Default is protected and missing selections resolve safely");
		PersonalityProfile temporary = new PersonalityProfile
		{
			Name = "Temporary",
			Style = "Temporary style"
		};
		profiles.Save(temporary);
		profiles.Delete(temporary.Id);
		Check(profiles.List().All((PersonalityProfile p) => p.Id != temporary.Id), "Removing a profile retains other profiles");
		string text = Path.Combine(folder, "damaged");
		Directory.CreateDirectory(text);
		File.WriteAllText(Path.Combine(text, "personalities.json"), "{broken");
		PersonalityStore personalityStore = new PersonalityStore(text);
		Check(personalityStore.List().Count == 4 && personalityStore.RecoveryNotice != null && Directory.GetFiles(text, "*.corrupt-*").Length == 1, "Corrupt profile data is preserved while defaults recover");
		Settings settings = new Settings
		{
			ModelPath = modelPath,
			VisionDisabled = true,
			GpuLayers = -1,
			CpuThreads = 2,
			Context = 4096,
			MaxTokens = 256,
			Thinking = false,
			WorkspaceTools = false,
			Temperature = 0.2,
			PersonalityId = concise.Id
		};
		storage.SaveSettings(settings);
		Check(storage.LoadSettings().PersonalityId == concise.Id, "Active personality selection persists with settings");
		using (Engine engine = new Engine(storage.Root))
		{
			using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4.0));
			settings.PersonalityId = "creative";
			Turn fiction = new Turn
			{
				User = "Please remember that my fictional captain is named Seventeen Seas.",
				Status = "complete"
			};
			Check((object)(await new MemoryLearner(engine, memories, profiles).LearnAsync(settings, new ChatSession(), fiction, timeout.Token)) == null && fiction.MemoryPromptTokens == 0L && !engine.Ready, "Creative profile can pause learning before any model request");
			MemoryChange memoryChange = memories.Apply(new MemoryCandidate[] { new MemoryCandidate("captain", "Seventeen Seas", "Seventeen Seas", "fact", "personal", 1.0, Array.Empty<string>()) }, "", new ChatSession(), new Turn
			{
				User = "I prefer practical answers in this conversation."
			});
			Check(memoryChange.Rejected == 1 && memories.Snapshot().Count == 0, "Profile/example facts cannot be saved without a supporting typed user quote");
			settings.PersonalityId = concise.Id;
			Turn first = new Turn
			{
				User = "How should I approach an unfamiliar problem?",
				Status = "generating"
			};
			ChatSession chatA = new ChatSession
			{
				Turns = new List<Turn>(1) { first }
			};
			await new Agent(engine, memories, null, profiles).RunAsync(settings, chatA, first, timeout.Token);
			Check(first.Answer.TrimStart().StartsWith("Briefly:", StringComparison.OrdinalIgnoreCase) && first.Answer.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= 50, "Real Deckard follows a concise custom speaking profile");
			settings.PersonalityId = checklist.Id;
			Turn second = new Turn
			{
				User = first.User,
				Status = "generating"
			};
			ChatSession chatB = new ChatSession
			{
				Turns = new List<Turn>(1) { second }
			};
			await new Agent(engine, memories, null, profiles).RunAsync(settings, chatB, second, timeout.Token);
			Check(second.Answer.TrimStart().StartsWith("Checklist:", StringComparison.OrdinalIgnoreCase) && Regex.Matches(second.Answer, "(?m)^\\s*- ").Count == 3 && !second.Answer.Contains("Briefly:"), "Switching profiles changes actual answer style without leaking the old profile");
			Check(first.PersonalityId == concise.Id && second.PersonalityId == checklist.Id && second.PersonalityName == checklist.Name, "Conversation turns record which personality produced each answer");
			memories.SaveManual(null, "", personal: true, "communication", "Keep explanations brief and to the point.", "fact", pinned: false);
			(string, string)[] array = new(string, string)[4]
			{
				("cycling", "My bicycle uses tubeless tires."),
				("dessert", "My favorite dessert is chocolate cake."),
				("gardening", "I grow tomatoes in the garden."),
				("collecting", "I collect antique postage stamps.")
			};
			for (int num = 0; num < array.Length; num++)
			{
				(string, string) tuple = array[num];
				memories.SaveManual(null, "", personal: true, tuple.Item1, tuple.Item2, "preference", pinned: false);
			}
			using SemanticMemory semantic = new SemanticMemory(memories, storage.Root);
			settings.PersonalityId = concise.Id;
			Turn semanticTurn = new Turn
			{
				User = "I want a concise response with minimal verbosity. What response style have I saved?",
				Status = "generating"
			};
			ChatSession semanticChat = new ChatSession
			{
				Turns = new List<Turn>(1) { semanticTurn }
			};
			await new Agent(engine, memories, semantic, profiles).RunAsync(settings, semanticChat, semanticTurn, timeout.Token);
			Check(semanticTurn.UsedMemoryIds.Count > 0 && Regex.IsMatch(semanticTurn.Answer, "(?i)brief|concise|short|point"), "Real chat combines semantic memory retrieval with the selected speaking profile");
			settings.SemanticMemories = false;
			Turn legacyTurn = new Turn
			{
				User = semanticTurn.User,
				Status = "generating"
			};
			ChatSession legacyChat = new ChatSession
			{
				Turns = new List<Turn>(1) { legacyTurn }
			};
			await new Agent(engine, memories, semantic, profiles).RunAsync(settings, legacyChat, legacyTurn, timeout.Token);
			Check(semanticTurn.PromptTokens < legacyTurn.PromptTokens && semanticTurn.UsedMemoryIds.Count < legacyTurn.UsedMemoryIds.Count, "Semantic selection reduces actual main-model input tokens for a mixed-memory fixture");
			storage.SaveChat(chatA);
			storage.SaveChat(chatB);
			storage.SaveChat(semanticChat);
			storage.SaveChat(legacyChat);
			Check(storage.LoadChats().Any((ChatSession c) => c.Turns.Any((Turn t) => t.PersonalityId == checklist.Id)), "Personality provenance survives conversation reload");
			File.WriteAllText(Path.Combine(folder, "transcripts.json"), JsonSerializer.Serialize(new { chatA, chatB, semanticChat, legacyChat }, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			engine.Stop();
			Check(!engine.Ready && !engine.ProcessId.HasValue, "Profile verification unloads its model");
			File.WriteAllText(Path.Combine(folder, "metrics.json"), JsonSerializer.Serialize(new
			{
				checks = results.Count,
				semanticInputTokens = semanticTurn.PromptTokens,
				keywordInputTokens = legacyTurn.PromptTokens,
				semanticMemories = semanticTurn.UsedMemoryIds.Count,
				keywordMemories = legacyTurn.UsedMemoryIds.Count
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		void Check(bool flag, string label)
		{
			if (!flag)
			{
				throw new Exception("Failed: " + label);
			}
			results.Add("PASS " + label);
			File.WriteAllLines(Path.Combine(folder, "checks.txt"), results);
		}
	}
}
