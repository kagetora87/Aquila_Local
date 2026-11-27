using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Living;

namespace AquilaLocal;

/// <summary>
/// Living mode: while you're away from Aquila, a persona has thoughts of her own on the chat model's idle second
/// slot (no extra model, nothing added to your chat), may look something up, and may message you first.
/// Every thought goes into her journal and her memory.
/// </summary>
public partial class MainWindow
{
	private DispatcherTimer? livingTimer;

	private DateTimeOffset lastActivity = DateTimeOffset.Now;

	private readonly DateTimeOffset livingStarted = DateTimeOffset.Now;

	private readonly Dictionary<string, DateTimeOffset?> lastThought = new Dictionary<string, DateTimeOffset?>();

	private readonly Dictionary<string, double> livingJitter = new Dictionary<string, double>();

	private CancellationTokenSource? livingCts;

	private bool livingRunning;

	private string lastLivingProblem = "";

	private InnerLifeJournal? journal;

	/// <summary>What living mode is doing now (shown in its window).</summary>
	private string livingStatus = "";

	/// <summary>Raised on the UI thread after a thought is written or the status changes.</summary>
	private event Action? LivingChanged;

	private InnerLifeJournal Journal => journal ??= new InnerLifeJournal(Path.Combine(store.Root, "living"));

	private void StartLiving()
	{
		PreviewKeyDown += delegate { lastActivity = DateTimeOffset.Now; };
		PreviewMouseDown += delegate { lastActivity = DateTimeOffset.Now; };
		PreviewMouseWheel += delegate { lastActivity = DateTimeOffset.Now; };
		livingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
		livingTimer.Tick += async delegate { await LivingTickAsync(); };
		livingTimer.Start();
		ShowLivingButton();
	}

	private void StopLiving()
	{
		livingTimer?.Stop();
		livingCts?.Cancel();
	}

	/// <summary>You're using Aquila: any thought in progress stops, so the chat model is all yours.</summary>
	private void InterruptLiving()
	{
		lastActivity = DateTimeOffset.Now;
		livingCts?.Cancel();
	}

	private bool LivingBusy => busy || attaching || pendingSpeechResume is { IsCompleted: false } || pendingCompanion is { IsCompleted: false } || pendingLearning is { IsCompleted: false } || voiceMode == VoiceMode.Call;

	private void ShowLivingButton()
	{
		// The little badge next to her name: a green dot while Living mode is on.
		bool on = settings.Living.Enabled;
		LivingDot.Fill = Ui.Brush(on ? "On" : "Text.Disabled");
		LivingGlyph.Foreground = Ui.Brush(on ? "Accent" : "Text.Tertiary");
		LivingButton.ToolTip = on
			? "Living mode is on: your companions think, dream and keep a journal while you're away. Click to open it."
			: "Living mode is off. Click to open it and switch it on.";
	}

	/// <summary>The personas who live: the chosen ones, or every persona that's set up.</summary>
	private List<PersonaEntry> LivingPersonas()
	{
		List<PersonaEntry> all = companion.Registry.Load().Personas.Where((PersonaEntry p) => companion.IsSetUp(p.Id)).ToList();
		return settings.Living.PersonaIds.Count == 0 ? all : all.Where((PersonaEntry p) => settings.Living.PersonaIds.Contains(p.Id)).ToList();
	}

	private DateTimeOffset? LastThoughtOf(string personaId)
	{
		if (!lastThought.TryGetValue(personaId, out DateTimeOffset? at))
		{
			at = Journal.Read(personaId).LastOrDefault()?.At;
			lastThought[personaId] = at;
		}
		return at;
	}

	private double JitterOf(string personaId)
	{
		if (!livingJitter.TryGetValue(personaId, out double j))
		{
			j = Random.Shared.NextDouble();
			livingJitter[personaId] = j;
		}
		return j;
	}

	private void SetLivingStatus(string text)
	{
		if (livingStatus != text)
		{
			livingStatus = text;
			LivingChanged?.Invoke();
		}
	}

	private async Task LivingTickAsync()
	{
		if (featuresClosing || livingRunning)
		{
			return;
		}
		if (!settings.Living.Enabled)
		{
			SetLivingStatus("Living mode is off.");
			return;
		}
		try
		{
			LivingPolicy policy = settings.Living.Policy();
			DateTimeOffset now = DateTimeOffset.Now;
			List<PersonaEntry> personas = LivingPersonas();
			if (personas.Count == 0)
			{
				SetLivingStatus("No persona is set up yet.");
				return;
			}
			bool night = LivingSchedule.IsNight(now.LocalDateTime.Hour, policy);
			PersonaEntry next = personas.OrderBy((PersonaEntry p) => LivingSchedule.NextThought(LastThoughtOf(p.Id), livingStarted, policy, JitterOf(p.Id), night)).First();
			DateTimeOffset due = LivingSchedule.NextThought(LastThoughtOf(next.Id), livingStarted, policy, JitterOf(next.Id), night);
			// An open Aquila window (pictures, a scene, settings…) means you're here.
			if (OwnedWindows.Count > 0)
			{
				lastActivity = now;
			}
			// A dream that stayed with her comes first: once she's awake and allowed to, she tells you about it.
			if (settings.Living.MessageFirst && !night && LivingSchedule.WhyNotNow(now, now, lastActivity, policy, LivingBusy, engine.Ready) == null)
			{
				foreach (PersonaEntry p in personas)
				{
					if (shareRetryAfter.TryGetValue(p.Id, out DateTimeOffset retry) && now < retry)
					{
						continue;
					}
					ToTell? waiting = companion.ToTell.Load(p.Id, now);
					if (waiting != null && WhyNotTell(p.Id, now, policy) == null)
					{
						await ShareToTellAsync(p.Id, p.Name, waiting);
						return;
					}
				}
			}
			string? why = LivingSchedule.WhyNotNow(now, due, lastActivity, policy, LivingBusy, engine.Ready);
			if (why != null)
			{
				SetLivingStatus(next.Name + ": " + why + ".");
				return;
			}
			await LiveOnceAsync(next.Id, next.Name);
		}
		catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException)
		{
			LivingProblem(ex.Message);
		}
	}

	/// <summary>When telling you about a dream failed, the next try waits a while.</summary>
	private readonly Dictionary<string, DateTimeOffset> shareRetryAfter = new Dictionary<string, DateTimeOffset>();

	/// <summary>Her latest chat with you (where her messages land), or null.</summary>
	private ChatSession? LatestChatWith(string personaId) =>
		chats.Where((ChatSession c) => c.PersonaId == personaId && !c.IsCall && c.Turns.Count > 0).OrderByDescending((ChatSession c) => c.Updated).FirstOrDefault();

	/// <summary>Why she can't message you now (the same rules as any message she sends first), or null.</summary>
	private string? WhyNotTell(string personaId, DateTimeOffset now, LivingPolicy policy)
	{
		bool waiting = LatestChatWith(personaId)?.Turns.LastOrDefault()?.Unprompted == true;
		List<DateTimeOffset> earlier = Journal.Read(personaId).Where((JournalEntry e) => e.MessagedYou).Select((JournalEntry e) => e.At).ToList();
		return LivingSchedule.WhyNoMessage(now, now.LocalDateTime.Hour, 1.0, earlier, waiting, policy, GenderOf(personaId));
	}

	/// <summary>She messages you about the dream (or feeling) that stayed with her, then lets it go.</summary>
	private async Task ShareToTellAsync(string personaId, string name, ToTell waiting)
	{
		if (livingRunning)
		{
			return;
		}
		livingRunning = true;
		livingCts = new CancellationTokenSource();
		CancellationToken ct = livingCts.Token;
		DateTimeOffset now = DateTimeOffset.Now;
		try
		{
			SetLivingStatus(name + " is writing to you about " + (waiting.Kind == ThoughtKinds.Dream ? "her dream" : "something on her mind") + "…");
			CompanionRuntime runtime = companion.Open(personaId);
			TimeSpan since = now - runtime.Store.LastInteraction;
			InnerThought thought = new InnerThought(waiting.Kind, waiting.Text, "", 1.0, waiting.Idea);
			(ChatSession into, Turn turn) = await MessageFirstAsync(runtime, personaId, name, LatestChatWith(personaId), thought, since, ct, ToTellStore.Cue(waiting, now, since));
			companion.ToTell.Clear(personaId);
			Journal.Append(new JournalEntry
			{
				PersonaId = personaId,
				At = now,
				Kind = ThoughtKinds.Memory,
				Text = (waiting.Kind == ThoughtKinds.Dream ? "I finally told them about my dream from " : "I finally told them what's been on my mind since ") + waiting.At.ToLocalTime().ToString("ddd h:mm tt") + ".",
				Urge = 1.0,
				Message = turn.Answer,
				ChatId = into.Id
			});
			shareRetryAfter.Remove(personaId);
			lastLivingProblem = "";
			SetLivingStatus(name + " told you about " + (waiting.Kind == ThoughtKinds.Dream ? "her dream" : "what was on her mind") + " at " + now.ToString("h:mm tt") + ".");
			LivingChanged?.Invoke();
		}
		catch (OperationCanceledException)
		{
			SetLivingStatus(name + " was about to tell you something, but you came back. She'll bring it up.");
		}
		catch (Exception ex) when (ex is IOException || ex is PersonaConfigException || ex is HttpRequestException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is JsonException || ex is DriveStateUnavailableException || ex is AppearanceCardUnavailableException || ex is ArgumentException)
		{
			shareRetryAfter[personaId] = now.AddMinutes(30);
			LivingProblem(ex.Message);
		}
		finally
		{
			livingRunning = false;
			livingCts?.Dispose();
			livingCts = null;
		}
	}

	private void LivingProblem(string message)
	{
		SetLivingStatus("Problem: " + message);
		if (message != lastLivingProblem)
		{
			lastLivingProblem = message;
			Notice("Living mode: " + message);
		}
	}

	/// <summary>One thought for one persona. Returns her journal entry, or null if it was stopped or failed (the reason is in the status).</summary>
	/// <param name="dream">True to dream now whatever the time (the "Have a dream now" button).</param>
	private async Task<JournalEntry?> LiveOnceAsync(string personaId, string name, bool dream = false)
	{
		if (livingRunning)
		{
			return null;
		}
		livingRunning = true;
		livingCts = new CancellationTokenSource();
		CancellationToken ct = livingCts.Token;
		DateTimeOffset now = DateTimeOffset.Now;
		try
		{
			SetLivingStatus(name + " is thinking…");
			CompanionRuntime runtime = companion.Open(personaId);
			PersonaConfig config = runtime.Config;
			bool asleep = dream || LivingSchedule.IsNight(now.LocalDateTime.Hour, settings.Living.Policy());
			bool canLookUp = settings.Living.WebLookups && !asleep;

			Dictionary<string, DriveValue> drives = runtime.Store.ReadDrives(config.Drives.Select((DriveSpec d) => d.Name));
			foreach (DriveChange change in DriveEngine.Tick(config, drives, runtime.Store.LastTick, now))
			{
				drives[change.Drive] = drives[change.Drive] with { Value = change.New };
			}
			string top = DriveEngine.TopDrive(config, drives);
			List<JournalEntry> history = Journal.Read(personaId);
			DateTimeOffset lastTalk = runtime.Store.LastInteraction;
			MoodState? moodNow = companion.Moods.Current(personaId);
			string moodText = moodNow == null ? "" : moodNow.Word + ", " + MoodRules.Decayed(moodNow, now).ToString("0") + "/10" + (moodNow.Why.Length > 0 ? " (" + moodNow.Why + ")" : "");
			InnerLifeInput input = new InnerLifeInput(
				name,
				config.PersonalitySheet,
				runtime.Store.ReadSummary().CappedTo(config.Memory.SummaryTokenCap).Render(),
				ContextInjector.DriveBlock(config, drives, top),
				runtime.Store.RecentExchanges(8),
				history.TakeLast(6).Select((JournalEntry e) => e.Text).ToList(),
				LastLocation(personaId),
				now,
				now - lastTalk,
				companion.LoadAboutYou().Render(),
				canLookUp,
				asleep,
				moodText,
				config.Gender);

			if (asleep)
			{
				SetLivingStatus(name + " is dreaming…");
			}
			string system = asleep ? InnerLifePrompt.DreamSystem(name, config.Gender) : InnerLifePrompt.WriterSystem(name, canLookUp, config.Gender);
			InnerThought thought = InnerLifePrompt.Parse(await AskInnerLifeAsync(system, InnerLifePrompt.WriterUser(input), ct), name);
			JournalEntry entry = new JournalEntry { PersonaId = personaId, At = now };
			if (thought.LookUp.Length > 0 && canLookUp)
			{
				SetLivingStatus(name + " is looking up “" + thought.LookUp + "”…");
				(string found, string problem) = await LivingLookUpAsync(thought.LookUp, entry.Lookups, ct);
				if (found.Length > 0)
				{
					SetLivingStatus(name + " is reading about “" + thought.LookUp + "”…");
					InnerThought after = InnerLifePrompt.Parse(await AskInnerLifeAsync(InnerLifePrompt.WriterSystem(name, canLookUp: false, config.Gender), InnerLifePrompt.AfterLookUpUser(input, thought, found), ct), name);
					thought = after with { LookUp = thought.LookUp, MoodWord = after.MoodWord.Length > 0 ? after.MoodWord : thought.MoodWord, MoodStrength = after.MoodWord.Length > 0 ? after.MoodStrength : thought.MoodStrength, MoodWhy = after.MoodWord.Length > 0 ? after.MoodWhy : thought.MoodWhy };
				}
				else
				{
					entry.Note = "The lookup didn't work: " + problem;
				}
			}
			if (asleep)
			{
				thought = thought with { Kind = ThoughtKinds.Dream, LookUp = "" };
			}
			entry.Kind = thought.Kind;
			entry.Text = thought.Text;
			entry.Urge = thought.Urge;
			entry.MoodWord = thought.MoodWord;
			entry.MoodStrength = thought.MoodStrength;
			entry.MoodWhy = thought.MoodWhy;
			if (thought.Kind == ThoughtKinds.Dream && settings.Living.DrawDreams)
			{
				try
				{
					bool animate = settings.Video.AnimateDreams && PictureDrawer.Video.Check().Ready;
					entry.Picture = await DrawDreamAsync(runtime, personaId, name, thought, animate, ct);
					if (animate)
					{
						SetLivingStatus(name + " is dreaming… bringing the dream to life (a few minutes).");
						entry.Video = await AnimateAsync(entry.Picture, AquilaLocal.Companion.Pictures.VideoRequest.ClipPrompt("Slow, dreamlike motion: the scene gently drifts, shifts and flows, soft light changing", thought.Text), ct);
					}
				}
				catch (Exception ex) when (ex is IOException || ex is PersonaConfigException || ex is HttpRequestException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
				{
					entry.Note = ("The dream picture didn't work: " + ex.Message.Truncate(300)).Trim();
				}
				finally
				{
					PictureStatus(null);
				}
			}

			if (settings.Living.MessageFirst)
			{
				ChatSession? chat = chats.Where((ChatSession c) => c.PersonaId == personaId && !c.IsCall && c.Turns.Count > 0).OrderByDescending((ChatSession c) => c.Updated).FirstOrDefault();
				bool waiting = chat?.Turns.LastOrDefault()?.Unprompted == true;
				List<DateTimeOffset> earlier = history.Where((JournalEntry e) => e.MessagedYou).Select((JournalEntry e) => e.At).ToList();
				string? whyNot = LivingSchedule.WhyNoMessage(now, now.LocalDateTime.Hour, thought.Urge, earlier, waiting, settings.Living.Policy(), config.Gender);
				if (whyNot == null)
				{
					SetLivingStatus(name + " is writing to you…");
					(ChatSession into, Turn turn) = await MessageFirstAsync(runtime, personaId, name, chat, thought, now - lastTalk, ct);
					entry.Message = turn.Answer;
					entry.ChatId = into.Id;
				}
				else if (thought.Urge >= 0.5)
				{
					entry.Note = (entry.Note + " She wanted to message you, but didn't: " + whyNot + ".").Trim();
				}
			}

			try
			{
				await AfterThoughtAsync(personaId, entry, thought, asleep);
			}
			catch (Exception ex) when (ex is IOException || ex is PersonaConfigException || ex is UnauthorizedAccessException)
			{
				entry.Note = (entry.Note + " Her mood or what she wants to tell you wasn't saved: " + ex.Message.Truncate(200)).Trim();
			}
			InnerLife.Remember(runtime.Memory, entry);
			Journal.Append(entry);
			lastThought[personaId] = now;
			ShowMood();
			livingJitter.Remove(personaId);
			lastLivingProblem = "";
			SetLivingStatus(name + " had a thought at " + now.ToString("h:mm tt") + ".");
			LivingChanged?.Invoke();
			return entry;
		}
		catch (OperationCanceledException)
		{
			SetLivingStatus(name + "'s thought was set aside because you came back.");
			return null;
		}
		catch (Exception ex) when (ex is IOException || ex is PersonaConfigException || ex is HttpRequestException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is JsonException || ex is DriveStateUnavailableException || ex is AppearanceCardUnavailableException || ex is ArgumentException)
		{
			// Wait a full interval before trying again, so a problem isn't retried every 30 seconds.
			lastThought[personaId] = now;
			LivingProblem(ex.Message);
			return null;
		}
		finally
		{
			livingRunning = false;
			livingCts?.Dispose();
			livingCts = null;
		}
	}

	/// <summary>
	/// Paints her dream with the picture model chosen in Pictures (and her appearance and reference pictures when
	/// she's in it). The best model pauses the chat model, which is fine: she's dreaming because you're away.
	/// </summary>
	private async Task<string> DrawDreamAsync(CompanionRuntime runtime, string personaId, string name, InnerThought dream, bool forVideo, CancellationToken ct)
	{
		string model = settings.Pictures.Model;
		(bool ready, string problem) = PictureDrawer.Kit.Check(model);
		if (!ready)
		{
			throw new InvalidOperationException(problem);
		}
		SetLivingStatus(name + " is dreaming… painting the dream.");
		string? appearance = null;
		List<string> references = new List<string>();
		if (dream.SheIsInIt)
		{
			try
			{
				appearance = AppearanceCaption.For(name, runtime.Store.ReadAppearance());
			}
			catch (AppearanceCardUnavailableException)
			{
				appearance = CompanionHost.Looks(runtime);
			}
			if (settings.Pictures.UseReferences)
			{
				references = AquilaLocal.Companion.Pictures.PictureFiles.References(companion.Folder(personaId)).Take(AquilaLocal.Companion.Pictures.PictureCommand.MaxReferences).ToList();
			}
		}
		AquilaLocal.Companion.Pictures.PictureRequest request = new AquilaLocal.Companion.Pictures.PictureRequest((Genders.IsMale(runtime.Config.Gender) ? "His" : "Her") + " dream: " + dream.Text + " Paint it dreamlike: soft, surreal light and a slightly impossible feel.", dream.SheIsInIt, "scene");
		string prompt = await WritePicturePromptAsync(request, "", dream.SheIsInIt ? name : null, appearance, references.Count > 0, "inside " + Genders.Possessive(runtime.Config.Gender) + " dream", ct, runtime.Config.Gender);
		return await DrawAsync(model, AquilaLocal.Companion.Pictures.PicturePrompt.WithReferences(prompt, references.Count), forVideo ? AquilaLocal.Companion.Pictures.VideoRequest.StillSize : request.Size(), references, name + " dream", ct);
	}

	/// <summary>One JSON answer from the chat model's second slot (the conversation's cache on slot 0 is untouched).</summary>
	private async Task<string> AskInnerLifeAsync(string system, string user, CancellationToken ct)
	{
		JsonObject payload = new JsonObject
		{
			["model"] = engine.Alias,
			["id_slot"] = 1,
			["messages"] = new JsonArray
			{
				new JsonObject { ["role"] = "system", ["content"] = system },
				new JsonObject { ["role"] = "user", ["content"] = user }
			},
			["stream"] = false,
			["temperature"] = 0.95,
			["top_p"] = 0.95,
			["max_tokens"] = 700,
			["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },
			["response_format"] = new JsonObject { ["type"] = "json_object" }
		};
		using HttpResponseMessage response = await engine.PostAsync("v1/chat/completions", payload, stream: false, ct);
		JsonNode? node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
		return node?["choices"]?.AsArray().FirstOrDefault()?["message"]?["content"]?.GetValue<string>() ?? throw new IOException("The chat model returned no thought.");
	}

	/// <summary>
	/// Her own lookup: with a Brave Search key, a web search and the top page; without one, Wikipedia. Every
	/// request is listed in her journal. Her switch in Living mode allows it, whatever the chat's Internet setting is.
	/// </summary>
	private async Task<(string Found, string Problem)> LivingLookUpAsync(string query, List<JournalLookup> lookups, CancellationToken ct)
	{
		Settings web = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings)) ?? throw new InvalidOperationException("Couldn't copy the settings.");
		web.WebMode = "Automatic";
		List<WebSource> sources = new List<WebSource>();
		string said = "";
		using WebAccess access = new WebAccess(web, sources, delegate(string text) { said = text; });
		if (settings.WebKeyProtected.Length > 0)
		{
			lookups.Add(new JournalLookup(JournalLookup.Search, query, ""));
			string answer = await access.ExecuteAsync("web_search", JsonSerializer.Serialize(new { query }), ct);
			if (sources.Count == 0)
			{
				return ("", answer.Truncate(200));
			}
			List<WebSource> results = sources.ToList();
			WebSource top = results[0];
			await access.ExecuteAsync("web_read", JsonSerializer.Serialize(new { url = top.Url }), ct);
			WebSource page = sources.First((WebSource s) => s.Url == top.Url);
			lookups.Add(new JournalLookup(JournalLookup.Read, top.Url, top.Title));
			string snippets = string.Join("\n", results.Select((WebSource s) => "- " + s.Title + ": " + s.Text.Truncate(300)));
			return ("Search results:\n" + snippets + "\n\nThe top page, " + page.Title + ":\n" + page.Text.Truncate(3500), "");
		}
		string url = "https://en.wikipedia.org/w/index.php?title=Special:Search&go=Go&search=" + Uri.EscapeDataString(query);
		lookups.Add(new JournalLookup(JournalLookup.Search, query + " (Wikipedia)", ""));
		string read = await access.ExecuteAsync("web_read", JsonSerializer.Serialize(new { url }), ct);
		if (sources.Count == 0)
		{
			return ("", (said.Length > 0 ? said : read).Truncate(200));
		}
		WebSource found = sources[0];
		lookups.Add(new JournalLookup(JournalLookup.Read, found.Url, found.Title));
		return (found.Title + ":\n" + found.Text.Truncate(4000), "");
	}

	/// <summary>
	/// She writes to you first, in her own chat voice (her full persona prompt and your recent chat, on the second
	/// slot), and the message lands in your latest chat with her with a sound.
	/// </summary>
	/// <param name="cue">What prompts her (by default: the thought she can't keep to herself).</param>
	private async Task<(ChatSession Chat, Turn Turn)> MessageFirstAsync(CompanionRuntime runtime, string personaId, string name, ChatSession? chat, InnerThought thought, TimeSpan sinceTheyWrote, CancellationToken ct, string? cue = null)
	{
		runtime.AboutUser = companion.LoadAboutYou().Render();
		try
		{
			runtime.Soundboard = companion.SoundboardPrompt(settings.SoundboardEnabled);
		}
		catch (PersonaConfigException)
		{
			runtime.Soundboard = "";
		}
		(InjectedContext context, _, _) = runtime.Assemble();
		DateTimeOffset now = DateTimeOffset.Now;
		JsonArray messages = new JsonArray
		{
			new JsonObject { ["role"] = "system", ["content"] = context.SystemPrompt }
		};
		foreach (Turn t in chat?.Turns.Where((Turn t) => t.Status == "complete").TakeLast(8) ?? Enumerable.Empty<Turn>())
		{
			messages.Add(new JsonObject { ["role"] = "user", ["content"] = t.Unprompted ? t.Context : t.User });
			messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = t.Answer });
		}
		string moodLine = MoodMarkup.TurnLine(companion.Moods.Current(personaId), now);
		messages.Add(new JsonObject { ["role"] = "user", ["content"] = context.InnerState + "\n" + moodLine + "\n\n" + (cue ?? InnerLifePrompt.ReachOutCue(thought, now, sinceTheyWrote)) });
		JsonObject payload = new JsonObject
		{
			["model"] = engine.Alias,
			["id_slot"] = 1,
			["messages"] = messages,
			["stream"] = false,
			["temperature"] = 0.85,
			["max_tokens"] = 500,
			["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false }
		};
		using HttpResponseMessage response = await engine.PostAsync("v1/chat/completions", payload, stream: false, ct);
		JsonNode? node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
		string written = node?["choices"]?.AsArray().FirstOrDefault()?["message"]?["content"]?.GetValue<string>() ?? "";
		MoodState? mood = MoodMarkup.Read(written, now);
		string message = InnerLifePrompt.CleanMessage(MoodMarkup.Strip(written));
		ct.ThrowIfCancellationRequested();

		if (chat == null)
		{
			chat = new ChatSession { PersonaId = personaId, PersonaName = name, Title = "Chat with " + name, Location = LastLocation(personaId) };
			chats.Insert(0, chat);
		}
		Turn turn = new Turn
		{
			User = "",
			Context = InnerLifePrompt.ReachOutContext(thought, now),
			Answer = message,
			Status = "complete",
			Unprompted = true,
			Created = now,
			Mood = mood?.Word ?? "",
			MoodStrength = mood?.Strength ?? 0,
			MoodWhy = mood?.Why ?? ""
		};
		chat.Turns.Add(turn);
		if (mood != null)
		{
			companion.Moods.Append(personaId, mood);
		}
		if (current?.PersonaId == personaId)
		{
			ClearDaydream();
			ShowMood();
		}
		store.SaveChat(chat);
		runtime.Memory.Add(chat.Id, turn.Id, now, "", message);
		if (chats.IndexOf(chat) > 0)
		{
			chats.Move(chats.IndexOf(chat), 0);
		}
		ChatsList.Items.Refresh();
		if (current == chat)
		{
			RenderChat();
		}
		else
		{
			Notice(name + " sent you a message. It's in “" + chat.Title.Truncate(60) + "”.");
		}
		UiSounds.Play(UiSound.Reply);
		_ = PlayCuedSoundsAsync(persona: true, message);
		return (chat, turn);
	}

	private void Living_Click(object sender, System.Windows.RoutedEventArgs e)
	{
		LivingWindow window = new LivingWindow(settings, companion, Journal, () => livingStatus, delegate
		{
			store.SaveSettings(settings);
			lastThought.Clear();
			ShowLivingButton();
		}, async delegate(string personaId, string name, bool dream)
		{
			if (LivingBusy || !engine.Ready)
			{
				throw new InvalidOperationException(engine.Ready ? "Wait until the current reply finishes." : "The chat model isn't loaded yet.");
			}
			return await LiveOnceAsync(personaId, name, dream);
		}, PictureDrawer.Video.Check().Ready)
		{
			Owner = this
		};
		void Changed() => window.Refresh();
		LivingChanged += Changed;
		try
		{
			window.ShowDialog();
		}
		finally
		{
			LivingChanged -= Changed;
		}
	}
}
