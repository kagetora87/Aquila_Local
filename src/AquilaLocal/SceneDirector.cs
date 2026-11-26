using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Scenes;

namespace AquilaLocal;

/// <summary>
/// Runs a multi-persona scene against the local model: chooses who speaks next and has that persona speak.
/// Each speaker gets the shared scene (world, who's here, what she witnessed) followed by her own private
/// state from her own store; her reply goes into the scene log and into her own memory only.
/// </summary>
public sealed class SceneDirector
{
	private readonly Engine engine;

	private readonly CompanionHost host;

	private readonly Random random = new Random();

	/// <summary>Streams the reply as it's written.</summary>
	public event Action<string>? Token;

	public SceneDirector(Engine engine, CompanionHost host)
	{
		this.engine = engine;
		this.host = host;
	}

	/// <summary>Everyone present, ranked by how naturally they'd speak next after the last event.</summary>
	public List<RankedSpeaker> Rank(Scene scene)
	{
		List<SceneEvent> all = scene.Log.All();
		SceneEvent? last = all.LastOrDefault((SceneEvent e) => e.Kind == SceneEventKinds.Said) ?? all.LastOrDefault();
		if (last == null || scene.Manifest.Cast.Count == 0)
		{
			return new List<RankedSpeaker>();
		}
		List<SceneCandidate> candidates = new List<SceneCandidate>();
		foreach (SceneMember m in scene.Manifest.Cast)
		{
			CompanionRuntime runtime = host.Open(m.PersonaId);
			Dictionary<string, DriveValue> drives = runtime.Store.ReadDrives(runtime.Config.Drives.Select((DriveSpec d) => d.Name));
			candidates.Add(new SceneCandidate(m.PersonaId, m.Name, Array.Empty<string>(), host.Interests(runtime), TurnScheduler.Urge(runtime.Config, drives)));
		}
		float[]? lastVector = last.Text.Trim().Length > 0 ? host.Embed(last.Text) : null;
		return TurnScheduler.Rank(candidates, last, all.TakeLast(12).ToList(), lastVector, scene.Manifest.Policy);
	}

	public RankedSpeaker Pick(IReadOnlyList<RankedSpeaker> ranked) => TurnScheduler.Pick(ranked, random);

	/// <summary>Has one persona say what she says next. Throws (nothing is logged) if she can't.</summary>
	public async Task<SceneEvent> SpeakAsync(Scene scene, string personaId, Settings settings, string userName, CancellationToken ct)
	{
		SceneMember member = scene.Manifest.Cast.FirstOrDefault((SceneMember m) => m.PersonaId == personaId) ?? throw new SceneException("That persona isn't in this scene.");
		if (!host.IsSetUp(personaId))
		{
			throw new SceneException(member.Name + " hasn't been set up yet. Set her up in Companions first; nothing was sent.");
		}
		await engine.StartAsync(settings, ct);
		int contextLimit = Math.Min(settings.Context, engine.SlotContext ?? settings.Context);
		int replyLimit = Math.Clamp(contextLimit / 8, 256, 1024);
		CompanionRuntime runtime = host.Open(personaId);
		runtime.AboutUser = host.LoadAboutYou().Render();
		try
		{
			runtime.Soundboard = host.SoundboardPrompt(settings.SoundboardEnabled);
		}
		catch (PersonaConfigException)
		{
			// A broken soundboard list is reported in the main chat and the soundboard window; the scene goes on without sounds.
			runtime.Soundboard = "";
		}
		List<SceneEvent> seen = scene.VisibleTo(personaId);
		string recent = SceneTranscript.Render(seen.TakeLast(6), "");
		bool spokeBefore = seen.Any((SceneEvent e) => e.Kind == SceneEventKinds.Said && e.SpeakerId == personaId);
		DateTimeOffset now = DateTimeOffset.Now;
		PreparedTurn prepared = await Task.Run(() => runtime.Prepare(recent, new HashSet<string>(), spokeBefore, now), ct);
		Dictionary<string, string> looks = new Dictionary<string, string>();
		foreach (SceneMember m in scene.Manifest.Cast)
		{
			looks[m.PersonaId] = CompanionHost.Looks(host.Open(m.PersonaId));
		}
		string header = ScenePrompt.Header(scene.Manifest, looks, userName);
		string privatePart = "[Private to you, " + member.Name + ". Nobody else in the scene sees this.]\n" + prepared.SystemPrompt + "\n\n" + "[Now: " + DateTime.Now.ToString("dddd d MMMM yyyy, h:mm tt") + "]\n" + prepared.InnerState + (prepared.RecalledText.Length > 0 ? "\n\n" + prepared.RecalledText : "") + "\nIn a scene there are no tools: just speak and act.\n[End private]";
		string cue = ScenePrompt.Cue(member.Name, scene.Manifest.Cast.Select((SceneMember m) => m.Name).Append(userName));
		// The transcript is what she witnessed, newest last, trimmed from the oldest end to fit.
		int budgetChars = Math.Max(4000, (contextLimit - replyLimit) * 3 - (header.Length + privatePart.Length + cue.Length) - 2000);
		List<string> lines = seen.Select((SceneEvent e) => SceneTranscript.Line(e, "")).ToList();
		int from = lines.Count;
		int used = 0;
		while (from > 0 && used + lines[from - 1].Length + 1 <= budgetChars)
		{
			from--;
			used += lines[from].Length + 1;
		}
		string transcript = (from > 0 ? "[Earlier parts of the scene are left out]\n" : "") + string.Join("\n", lines.Skip(from));
		string user = "[Scene so far]\n" + transcript + "\n[End scene so far]\n\n" + privatePart + "\n\n" + cue;
		JsonArray messages = new JsonArray
		{
			new JsonObject { ["role"] = "system", ["content"] = header },
			new JsonObject { ["role"] = "user", ["content"] = user }
		};
		JsonObject request = new JsonObject
		{
			["model"] = engine.Alias,
			["messages"] = messages,
			["stream"] = true,
			["max_tokens"] = replyLimit,
			["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },
			["id_slot"] = 0,
			["cache_prompt"] = true
		};
		ChatSampling.Apply(request, settings, engine.SupportsDry);
		StringBuilder content = new StringBuilder();
		using (HttpResponseMessage response = await engine.PostAsync("v1/chat/completions", request, stream: true, ct))
		using (StreamReader reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct), Encoding.UTF8))
		{
			while (true)
			{
				string? line = await reader.ReadLineAsync(ct);
				if (line == null)
				{
					break;
				}
				if (!line.StartsWith("data:", StringComparison.Ordinal))
				{
					continue;
				}
				string data = line.Substring(5).Trim();
				if (data == "[DONE]")
				{
					break;
				}
				JsonNode? node = JsonNode.Parse(data);
				if (node?["error"] is JsonNode error)
				{
					throw new IOException("The model refused the request: " + error.ToJsonString());
				}
				string? piece = node?["choices"]?.AsArray().FirstOrDefault()?["delta"]?["content"]?.GetValue<string>();
				if (!string.IsNullOrEmpty(piece))
				{
					content.Append(piece);
					Token?.Invoke(piece);
				}
			}
		}
		string reply = ScenePrompt.CleanReply(content.ToString(), member.Name);
		if (reply.Length == 0)
		{
			throw new IOException(member.Name + " didn't say anything (the model returned an empty reply). Nothing was logged.");
		}
		DateTimeOffset at = DateTimeOffset.Now;
		long id = scene.Say(personaId, member.Name, reply, at);
		await Task.Run(() => runtime.Complete(prepared, scene.Id, scene.Id + ":" + id, "[In the scene \"" + scene.Manifest.Title + "\"]\n" + recent, reply, at), ct);
		return scene.Log.All().Last((SceneEvent e) => e.Id == id);
	}
}
