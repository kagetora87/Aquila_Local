using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Living;

namespace AquilaLocal;

/// <summary>
/// While she sleeps: once a night she goes over the day and keeps a few reflections in her memory, and about once
/// a week she makes small changes to her own personality sheet from them. Changes apply at once; you're told, and
/// can look them over, edit or undo them (her card's … menu, "How she's changed").
/// </summary>
public partial class MainWindow
{
	/// <summary>The reason reflections are saved with, so the weekly look back can find them.</summary>
	private const string ReflectionReason = "a reflection while asleep: ";

	/// <summary>How long after one night's reflection the next can come.</summary>
	private static readonly TimeSpan ReflectEvery = TimeSpan.FromHours(16);

	private GrowthStore Growths => new GrowthStore(companion.LivingRoot);

	/// <summary>After a problem, her sleep work waits this long before it's tried again.</summary>
	private readonly Dictionary<string, DateTimeOffset> sleepRetryAfter = new Dictionary<string, DateTimeOffset>();

	/// <summary>Does one persona's sleep work (a reflection, or her weekly growth) if one is due. True if it did something.</summary>
	private async Task<bool> SleepWorkAsync(List<PersonaEntry> personas, DateTimeOffset now)
	{
		GrowthStore growths = Growths;
		foreach (PersonaEntry p in personas)
		{
			if (sleepRetryAfter.TryGetValue(p.Id, out DateTimeOffset retry) && now < retry)
			{
				continue;
			}
			DateTimeOffset? reflected = growths.LastReflected(p.Id);
			if (reflected == null || now - reflected.Value >= ReflectEvery)
			{
				await SleepAsync(p.Id, p.Name, () => ReflectAsync(p.Id, p.Name, reflected, now));
				return true;
			}
			if (GrowthDue(growths, p.Id, now))
			{
				await SleepAsync(p.Id, p.Name, () => GrowAsync(p.Id, p.Name, now));
				return true;
			}
		}
		return false;
	}

	/// <summary>A week since she last grew (or never), with at least a few new reflections to grow from.</summary>
	private bool GrowthDue(GrowthStore growths, string personaId, DateTimeOffset now)
	{
		DateTimeOffset? grew = growths.LastGrew(personaId);
		if (grew != null && now - grew.Value < Growth.Every)
		{
			return false;
		}
		int fresh = companion.Open(personaId).Store.ActiveMemory().Count((AquilaLocal.Companion.MemoryEntry m) => m.Reason.StartsWith(ReflectionReason, StringComparison.Ordinal) && (grew == null || m.At > grew.Value));
		return fresh >= 3;
	}

	private async Task SleepAsync(string personaId, string name, Func<Task> work)
	{
		if (livingRunning)
		{
			return;
		}
		livingRunning = true;
		livingCts = new CancellationTokenSource();
		try
		{
			await work();
			lastLivingProblem = "";
			LivingChanged?.Invoke();
		}
		catch (OperationCanceledException)
		{
			SetLivingStatus(name + "'s sleep was interrupted because you came back.");
		}
		catch (Exception ex) when (ex is IOException || ex is PersonaConfigException || ex is HttpRequestException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is JsonException || ex is ArgumentException)
		{
			// Don't try again every 30 seconds.
			sleepRetryAfter[personaId] = DateTimeOffset.Now + TimeSpan.FromHours(1);
			LivingProblem(ex.Message);
		}
		finally
		{
			livingRunning = false;
			livingCts?.Dispose();
			livingCts = null;
		}
	}

	/// <summary>She goes over what happened since her last reflection and keeps what it added up to.</summary>
	private async Task ReflectAsync(string personaId, string name, DateTimeOffset? since, DateTimeOffset now)
	{
		CancellationToken ct = livingCts!.Token;
		CompanionRuntime runtime = companion.Open(personaId);
		DateTimeOffset from = since ?? now - TimeSpan.FromDays(1);
		List<Exchange> day = runtime.Store.RecentExchanges(60).Where((Exchange e) => e.At > from).TakeLast(24).ToList();
		List<string> thoughts = Journal.Read(personaId).Where((JournalEntry e) => e.At > from && e.Text.Trim().Length > 0).TakeLast(12).Select((JournalEntry e) => (e.Kind == ThoughtKinds.Dream ? "(a dream) " : "") + e.Text).ToList();
		if (day.Count == 0 && thoughts.Count < 2)
		{
			Growths.MarkReflected(personaId, now);
			return;
		}
		SetLivingStatus(name + " is asleep, going over the day…");
		List<string> known = runtime.Store.ActiveMemory().Select((AquilaLocal.Companion.MemoryEntry m) => m.Text).ToList();
		PersonaConfig config = runtime.Config;
		string answer = await AskInnerLifeAsync(Reflections.Prompt(name, config.Gender), Reflections.User(name, day, thoughts, known), ct, maxTokens: 900, temperature: 0.7);
		List<Reflection> found = Reflections.Parse(answer, known);
		foreach (Reflection r in found)
		{
			runtime.Store.AddMemory(r.Layer, r.Text, MemoryAuthors.Persona, ReflectionReason + (r.Why.Length > 0 ? r.Why : "going over the day"), now);
		}
		Growths.MarkReflected(personaId, now);
		SetLivingStatus(name + (found.Count == 0 ? " slept on the day; nothing new came of it." : " slept on the day and kept " + found.Count + (found.Count == 1 ? " reflection." : " reflections.")));
	}

	/// <summary>She looks back over the week and, if something's really shifted, changes her own sheet a little.</summary>
	private async Task GrowAsync(string personaId, string name, DateTimeOffset now)
	{
		CancellationToken ct = livingCts!.Token;
		GrowthStore growths = Growths;
		DateTimeOffset? grew = growths.LastGrew(personaId);
		CompanionRuntime runtime = companion.Open(personaId);
		string path = companion.ConfigPath(personaId);
		PersonaConfig config = PersonaConfig.Load(path);
		string sheet = Normalize(config.PersonalitySheet);
		DateTimeOffset from = grew ?? now - TimeSpan.FromDays(14);
		List<string> lately = runtime.Store.ActiveMemory().Where((AquilaLocal.Companion.MemoryEntry m) => m.At > from && (m.Author == MemoryAuthors.Persona || m.Author == MemoryAuthors.Operator)).Select((AquilaLocal.Companion.MemoryEntry m) => m.Text).ToList();
		SetLivingStatus(name + " is asleep, noticing how " + Genders.Words("{s}'s", config.Gender) + " changed…");
		// The whole sheet comes back, so the answer needs room for it.
		int room = Math.Clamp(sheet.Length / 3 + 400, 900, 8000);
		string answer = await AskInnerLifeAsync(Growth.Prompt(name, config.Gender), Growth.User(sheet, lately), ct, maxTokens: room, temperature: 0.6);
		GrowthChange? change = Growth.Parse(answer, sheet, now);
		growths.MarkGrew(personaId, now);
		if (change == null)
		{
			SetLivingStatus(name + " looked back over the week; nothing about " + Genders.Words("{o}", config.Gender) + " has really shifted.");
			return;
		}
		// Read again just before writing, in case you changed her settings meanwhile.
		PersonaConfig fresh = PersonaConfig.Load(path);
		if (Normalize(fresh.PersonalitySheet) != sheet)
		{
			return;
		}
		fresh.PersonalitySheet = change.After;
		fresh.Save(path);
		companion.Forget(personaId);
		growths.Add(personaId, change);
		if (change.Summary.Length > 0)
		{
			runtime.Store.AddMemory(MemoryLayers.Feelings, change.Summary, MemoryAuthors.Persona, "how I've changed lately" + (change.Why.Length > 0 ? ": " + change.Why : ""), now);
		}
		SetLivingStatus(name + " changed a little.");
		Notice(name + " changed a little" + (change.Summary.Length > 0 ? ": “" + change.Summary + "”" : ".") + " You can look it over, edit it or undo it: click the … on " + Genders.Possessive(config.Gender) + " card, then How " + Genders.Words("{s}'s", config.Gender) + " changed.");
	}

	/// <summary>On startup: tells you about changes she made that you haven't looked at yet.</summary>
	private void NoticeUnseenGrowth()
	{
		try
		{
			GrowthStore growths = Growths;
			List<string> who = companion.Registry.Load().Personas.Where((PersonaEntry p) => companion.IsSetUp(p.Id) && growths.Unseen(p.Id) > 0).Select((PersonaEntry p) => p.Name).ToList();
			if (who.Count > 0)
			{
				Notice(string.Join(" and ", who) + (who.Count == 1 ? " has" : " have") + " changed a little since you last looked. Click the … on " + (who.Count == 1 ? "the" : "a") + " companion card, then How they've changed, to look it over.");
			}
		}
		catch (Exception)
		{
			// Only a reminder: it must never stop Aquila from opening.
		}
	}

	private void OpenGrowth(string personaId, string name)
	{
		GrowthWindow window = new GrowthWindow(companion, Growths, personaId, name, GenderOf(personaId))
		{
			Owner = this
		};
		window.ShowDialog();
	}

	internal static string Normalize(string sheet) => (sheet ?? "").Replace("\r\n", "\n").Trim();
}
