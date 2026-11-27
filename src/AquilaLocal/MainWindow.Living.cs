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

	private bool LivingBusy => busy || attaching || pendingCompanion is { IsCompleted: false } || pendingLearning is { IsCompleted: false } || voiceMode == VoiceMode.Call;

	private void ShowLivingButton()
	{
		ShowOnLabel(LivingButtonText, settings.Living.Enabled ? "Living mode · " : "Living mode", settings.Living.Enabled ? "on" : null, "");
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
				asleep);

			if (asleep)
			{
				SetLivingStatus(name + " is dreaming…");
			}
			string system = asleep ? InnerLifePrompt.DreamSystem(name) : InnerLifePrompt.WriterSystem(name, canLookUp);
			InnerThought thought = InnerLifePrompt.Parse(await AskInnerLifeAsync(system, InnerLifePrompt.WriterUser(input), ct), name);
			JournalEntry entry = new JournalEntry { PersonaId = personaId, At = now };
			if (thought.LookUp.Length > 0 && canLookUp)
			{
				SetLivingStatus(name + " is looking up “" + thought.LookUp + "”…");
				(string found, string problem) = await LivingLookUpAsync(thought.LookUp, entry.Lookups, ct);
				if (found.Length > 0)
				{
					SetLivingStatus(name + " is reading about “" + thought.LookUp + "”…");
					InnerThought after = InnerLifePrompt.Parse(await AskInnerLifeAsync(InnerLifePrompt.WriterSystem(name, canLookUp: false), InnerLifePrompt.AfterLookUpUser(input, thought, found), ct), name);
					thought = after with { LookUp = thought.LookUp };
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
				string? whyNot = LivingSchedule.WhyNoMessage(now, now.LocalDateTime.Hour, thought.Urge, earlier, waiting, settings.Living.Policy());
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

			InnerLife.Remember(runtime.Memory, entry);
			Journal.Append(entry);
			lastThought[personaId] = now;
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
		AquilaLocal.Companion.Pictures.PictureRequest request = new AquilaLocal.Companion.Pictures.PictureRequest("Her dream: " + dream.Text + " Paint it dreamlike: soft, surreal light and a slightly impossible feel.", dream.SheIsInIt, "scene");
		string prompt = await WritePicturePromptAsync(request, "", dream.SheIsInIt ? name : null, appearance, references.Count > 0, "inside her dream", ct);
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
	private async Task<(ChatSession Chat, Turn Turn)> MessageFirstAsync(CompanionRuntime runtime, string personaId, string name, ChatSession? chat, InnerThought thought, TimeSpan sinceTheyWrote, CancellationToken ct)
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
		messages.Add(new JsonObject { ["role"] = "user", ["content"] = context.InnerState + "\n\n" + InnerLifePrompt.ReachOutCue(thought, now, sinceTheyWrote) });
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
		string message = InnerLifePrompt.CleanMessage(node?["choices"]?.AsArray().FirstOrDefault()?["message"]?["content"]?.GetValue<string>() ?? "");
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
			Created = now
		};
		chat.Turns.Add(turn);
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
