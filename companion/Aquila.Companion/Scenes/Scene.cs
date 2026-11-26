using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace AquilaLocal.Companion.Scenes;

/// <summary>
/// Layer one of a scene: the shared situation every persona is told about (where, when, what's happening,
/// what's there). Editable at any time; every change is logged so everyone present notices it.
/// </summary>
public sealed class WorldState
{
    public string Place { get; set; } = "";
    public string Time { get; set; } = "";
    public string Situation { get; set; } = "";
    public List<string> Objects { get; set; } = new();
    public string Notes { get; set; } = "";

    public WorldState Copy() => new() { Place = Place, Time = Time, Situation = Situation, Objects = Objects.ToList(), Notes = Notes };

    public string Render()
    {
        var sb = new StringBuilder();
        void Line(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                sb.Append(label).Append(": ").Append(value.Trim()).Append('\n');
            }
        }
        Line("Place", Place);
        Line("Time", Time);
        Line("What's happening", Situation);
        List<string> things = Objects.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).ToList();
        if (things.Count > 0)
        {
            Line("Things here", string.Join("; ", things));
        }
        Line("Notes", Notes);
        return sb.ToString().TrimEnd();
    }

    /// <summary>What changed from <paramref name="before"/>, in words, for the scene log.</summary>
    public string DescribeChangeFrom(WorldState before)
    {
        var parts = new List<string>();
        void Compare(string label, string a, string b)
        {
            if (!string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal))
            {
                parts.Add(b.Trim().Length == 0 ? label + " cleared" : label + ": " + b.Trim());
            }
        }
        Compare("Place", before.Place, Place);
        Compare("Time", before.Time, Time);
        Compare("What's happening", before.Situation, Situation);
        Compare("Things here", string.Join("; ", before.Objects), string.Join("; ", Objects));
        Compare("Notes", before.Notes, Notes);
        return parts.Count == 0 ? "" : string.Join(". ", parts);
    }
}

/// <summary>Someone in the scene's cast. Each member is a full persona with her own sheet, drives, memory and card.</summary>
public sealed record SceneMember(string PersonaId, string Name);

/// <summary>How the next speaker is chosen.</summary>
public sealed class TurnPolicy
{
    public const string Automatic = "auto";
    public const string Manual = "manual";

    /// <summary>"auto": the scheduler picks; "manual": you call on someone each time.</summary>
    public string Mode { get; set; } = Automatic;

    /// <summary>After your message, at most this many personas speak in a row before the scene waits for you.</summary>
    public int MaxChain { get; set; } = 2;

    /// <summary>After a persona speaks, another reacts only if someone is named or scores at least this.</summary>
    public double ContinueAbove { get; set; } = 1.4;
}

/// <summary>Layer two: the scene manifest. The roster (by persona id), the world, the turn policy.</summary>
public sealed class SceneManifest
{
    public const int MaxCast = 12;

    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public WorldState World { get; set; } = new();
    public List<SceneMember> Cast { get; set; } = new();
    public TurnPolicy Policy { get; set; } = new();

    public static SceneManifest Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new SceneException($"The scene file is missing: {path}");
        }
        try
        {
            return JsonSerializer.Deserialize<SceneManifest>(File.ReadAllText(path), PersonaConfig.Json) ?? throw new SceneException($"The scene file is empty: {path}");
        }
        catch (JsonException ex)
        {
            throw new SceneException($"The scene file can't be read ({path}): {ex.Message}", ex);
        }
    }

    public void Save(string path)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, PersonaConfig.Json));
        File.Move(tmp, path, overwrite: true);
    }
}

public static class SceneEventKinds
{
    public const string Said = "said";
    public const string Joined = "joined";
    public const string Left = "left";
    public const string WorldChanged = "world";
    public const string Narration = "narration";
}

public sealed record SceneEvent(long Id, DateTimeOffset At, string Kind, string SpeakerId, string SpeakerName, string Text);

/// <summary>
/// The shared event log: every statement, action, arrival, departure and world change, with who did it.
/// Append-only (enforced by SQLite triggers). One log for the world; each persona's own memory stays private.
/// </summary>
public sealed class SceneLog
{
    /// <summary>The speaker id used for you (the user).</summary>
    public const string UserId = "user";

    /// <summary>The speaker id for narration and world changes.</summary>
    public const string WorldId = "world";

    private readonly string connectionString;

    public string Path { get; }

    private SceneLog(string path, bool create)
    {
        Path = System.IO.Path.GetFullPath(path);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path, Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
    }

    public static SceneLog Create(string path)
    {
        if (File.Exists(path))
        {
            throw new SceneException($"A scene log already exists at {path}. It was not touched.");
        }
        var log = new SceneLog(path, create: true);
        using SqliteConnection c = log.Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE events(id INTEGER PRIMARY KEY AUTOINCREMENT, at TEXT NOT NULL, kind TEXT NOT NULL, speaker_id TEXT NOT NULL, speaker_name TEXT NOT NULL, text TEXT NOT NULL);
            CREATE TRIGGER events_no_update BEFORE UPDATE ON events BEGIN SELECT RAISE(ABORT, 'the scene log is append-only'); END;
            CREATE TRIGGER events_no_delete BEFORE DELETE ON events BEGIN SELECT RAISE(ABORT, 'the scene log is append-only'); END;
            """;
        cmd.ExecuteNonQuery();
        return log;
    }

    public static SceneLog Open(string path)
    {
        if (!File.Exists(path))
        {
            throw new SceneException($"The scene log is missing: {path}. Nothing was guessed or recreated.");
        }
        return new SceneLog(path, create: false);
    }

    private SqliteConnection Connect()
    {
        var c = new SqliteConnection(connectionString);
        c.Open();
        return c;
    }

    public long Append(string kind, string speakerId, string speakerName, string text, DateTimeOffset at)
    {
        using SqliteConnection c = Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO events(at,kind,speaker_id,speaker_name,text) VALUES($a,$k,$s,$n,$t); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$a", at.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$k", kind);
        cmd.Parameters.AddWithValue("$s", speakerId);
        cmd.Parameters.AddWithValue("$n", speakerName);
        cmd.Parameters.AddWithValue("$t", text);
        return (long)cmd.ExecuteScalar()!;
    }

    public List<SceneEvent> All()
    {
        using SqliteConnection c = Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,at,kind,speaker_id,speaker_name,text FROM events ORDER BY id";
        using SqliteDataReader r = cmd.ExecuteReader();
        var list = new List<SceneEvent>();
        while (r.Read())
        {
            list.Add(new SceneEvent(r.GetInt64(0), DateTimeOffset.Parse(r.GetString(1), CultureInfo.InvariantCulture), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));
        }
        return list;
    }

    /// <summary>For tests that prove the log can't be rewritten.</summary>
    public void ExecuteForTests(string sql)
    {
        using SqliteConnection c = Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

/// <summary>One scene on disk: its manifest (scene.json) and its log (log.db).</summary>
public sealed class Scene
{
    public string Folder { get; }
    public SceneManifest Manifest { get; }
    public SceneLog Log { get; }
    public string Id => Manifest.Id;
    public string ManifestPath => System.IO.Path.Combine(Folder, "scene.json");

    internal Scene(string folder, SceneManifest manifest, SceneLog log)
    {
        Folder = folder;
        Manifest = manifest;
        Log = log;
    }

    public void Save() => Manifest.Save(ManifestPath);

    public long Say(string speakerId, string speakerName, string text, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Nothing was said.");
        }
        if (speakerId != SceneLog.UserId && Manifest.Cast.All(m => m.PersonaId != speakerId))
        {
            throw new SceneException($"{speakerName} isn't in this scene, so they can't speak in it.");
        }
        return Log.Append(SceneEventKinds.Said, speakerId, speakerName, text.Trim(), now);
    }

    public long Narrate(string text, DateTimeOffset now) => Log.Append(SceneEventKinds.Narration, SceneLog.WorldId, "", text.Trim(), now);

    public long Join(SceneMember member, DateTimeOffset now)
    {
        if (Manifest.Cast.Any(m => m.PersonaId == member.PersonaId))
        {
            throw new SceneException($"{member.Name} is already in this scene.");
        }
        if (Manifest.Cast.Count >= SceneManifest.MaxCast)
        {
            throw new SceneException($"A scene holds at most {SceneManifest.MaxCast} personas. Drop someone first.");
        }
        Manifest.Cast.Add(member);
        Save();
        return Log.Append(SceneEventKinds.Joined, member.PersonaId, member.Name, $"{member.Name} joins the scene.", now);
    }

    public long Leave(string personaId, DateTimeOffset now)
    {
        SceneMember member = Manifest.Cast.FirstOrDefault(m => m.PersonaId == personaId) ?? throw new SceneException($"'{personaId}' isn't in this scene.");
        Manifest.Cast.Remove(member);
        Save();
        return Log.Append(SceneEventKinds.Left, member.PersonaId, member.Name, $"{member.Name} leaves the scene.", now);
    }

    /// <summary>Changes the shared world and logs what changed. Returns 0 when nothing changed.</summary>
    public long ChangeWorld(WorldState next, DateTimeOffset now)
    {
        string change = next.DescribeChangeFrom(Manifest.World);
        if (change.Length == 0)
        {
            return 0;
        }
        Manifest.World = next.Copy();
        Save();
        return Log.Append(SceneEventKinds.WorldChanged, SceneLog.WorldId, "", change, now);
    }

    /// <summary>
    /// What this persona witnessed: only events while she was present (from her arrival to her departure,
    /// across every visit). This is all she ever gets from the scene; nobody's private state is in it.
    /// </summary>
    public List<SceneEvent> VisibleTo(string personaId, int maxEvents = int.MaxValue)
    {
        var seen = new List<SceneEvent>();
        bool present = false;
        foreach (SceneEvent e in Log.All())
        {
            if (e.SpeakerId == personaId && e.Kind == SceneEventKinds.Joined)
            {
                present = true;
                seen.Add(e);
            }
            else if (e.SpeakerId == personaId && e.Kind == SceneEventKinds.Left)
            {
                seen.Add(e);
                present = false;
            }
            else if (present)
            {
                seen.Add(e);
            }
        }
        return seen.Count > maxEvents ? seen.Skip(seen.Count - maxEvents).ToList() : seen;
    }
}

/// <summary>All scenes, each in its own folder under Data\scenes.</summary>
public sealed class SceneLibrary
{
    public string Root { get; }

    public SceneLibrary(string root)
    {
        Root = System.IO.Path.GetFullPath(root);
    }

    public Scene Create(string title, WorldState world, IReadOnlyList<SceneMember> cast, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new SceneException("Give the scene a name.");
        }
        if (cast.Count > SceneManifest.MaxCast)
        {
            throw new SceneException($"A scene holds at most {SceneManifest.MaxCast} personas.");
        }
        if (cast.Select(c => c.PersonaId).Distinct().Count() != cast.Count)
        {
            throw new SceneException("Someone is in the cast twice.");
        }
        string id = "scene-" + now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6];
        string folder = System.IO.Path.Combine(Root, id);
        Directory.CreateDirectory(folder);
        var manifest = new SceneManifest { Id = id, Title = title.Trim(), CreatedAt = now, World = world.Copy() };
        SceneLog log = SceneLog.Create(System.IO.Path.Combine(folder, "log.db"));
        var scene = new Scene(folder, manifest, log);
        scene.Save();
        foreach (SceneMember m in cast)
        {
            scene.Join(m, now);
        }
        return scene;
    }

    public Scene Open(string id)
    {
        string folder = System.IO.Path.Combine(Root, id);
        SceneManifest manifest = SceneManifest.Load(System.IO.Path.Combine(folder, "scene.json"));
        if (manifest.Id != id)
        {
            throw new SceneException($"The scene file in {folder} belongs to '{manifest.Id}', not '{id}'.");
        }
        return new Scene(folder, manifest, SceneLog.Open(System.IO.Path.Combine(folder, "log.db")));
    }

    /// <summary>Every scene, newest first. Unreadable ones are listed with their problem instead of hidden.</summary>
    public List<(string Id, string Title, DateTimeOffset CreatedAt, string? Problem)> List()
    {
        var list = new List<(string, string, DateTimeOffset, string?)>();
        if (!Directory.Exists(Root))
        {
            return list;
        }
        foreach (string dir in Directory.EnumerateDirectories(Root))
        {
            string id = System.IO.Path.GetFileName(dir);
            try
            {
                SceneManifest m = SceneManifest.Load(System.IO.Path.Combine(dir, "scene.json"));
                list.Add((id, m.Title, m.CreatedAt, null));
            }
            catch (SceneException ex)
            {
                list.Add((id, id, DateTimeOffset.MinValue, ex.Message));
            }
        }
        return list.OrderByDescending(s => s.Item3).ToList();
    }
}

/// <summary>The log as a readable transcript for one persona.</summary>
public static class SceneTranscript
{
    public static string Line(SceneEvent e, string viewerId) => e.Kind switch
    {
        SceneEventKinds.Said => e.SpeakerName + (e.SpeakerId == viewerId ? " (you)" : "") + ": " + e.Text,
        SceneEventKinds.Joined => "[" + e.SpeakerName + " joins the scene]",
        SceneEventKinds.Left => "[" + e.SpeakerName + " leaves the scene]",
        SceneEventKinds.WorldChanged => "[The scene changes. " + e.Text + "]",
        _ => "[" + e.Text + "]",
    };

    public static string Render(IEnumerable<SceneEvent> events, string viewerId) => string.Join("\n", events.Select(e => Line(e, viewerId)));
}

/// <summary>
/// Builds a speaker's prompt so the shared parts come first and stay byte-identical between speakers (the
/// world, the cast, then the log), and only the speaker's private part comes last. llama.cpp can then reuse
/// the shared part from its cache, so switching speakers doesn't re-read the whole scene.
/// </summary>
public static class ScenePrompt
{
    public static string Header(SceneManifest manifest, IReadOnlyDictionary<string, string> looks, string userName)
    {
        var sb = new StringBuilder();
        sb.Append("[Scene: ").Append(manifest.Title).Append("]\n");
        sb.Append("This is a shared scene with several characters and one real person. Below is the world everyone here shares, who is here, and then everything said and done out loud, labelled with who did it. Nobody can read anyone else's thoughts or memories.\n\n");
        sb.Append("[World]\n").Append(manifest.World.Render()).Append("\n[End world]\n\n");
        sb.Append("[Here now]\n");
        sb.Append("- ").Append(userName).Append(": the user, a real person\n");
        foreach (SceneMember m in manifest.Cast)
        {
            sb.Append("- ").Append(m.Name);
            if (looks.TryGetValue(m.PersonaId, out string? look) && look.Trim().Length > 0)
            {
                sb.Append(": ").Append(look.Trim());
            }
            sb.Append('\n');
        }
        sb.Append("[End here now]");
        return sb.ToString();
    }

    public static string Cue(string name, IEnumerable<string> others)
    {
        string them = string.Join(", ", others.Where(o => o != name));
        return $"[Your turn] You are {name}. Write only what {name} says and does next in this scene, in character, speaking only for yourself. Never write lines, thoughts or actions for anyone else{(them.Length > 0 ? " (" + them + ")" : "")}. Keep it to a natural conversational length. Put physical actions in *asterisks*. Don't start with your name.";
    }

    private static readonly Regex OtherSpeaker = new(@"^\s*\**\s*[\p{Lu}][\p{L}' .-]{0,30}\s*\**\s*:\s", RegexOptions.CultureInvariant);

    /// <summary>Removes a leading "Name:" tag and anything written after another speaker's tag.</summary>
    public static string CleanReply(string reply, string name)
    {
        string text = reply.Trim();
        var own = new Regex(@"^\s*\**\s*" + Regex.Escape(name) + @"\s*\**\s*:\s*\**\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = own.Replace(text, "", 1);
        string[] lines = text.Split('\n');
        var kept = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0 && OtherSpeaker.IsMatch(lines[i]))
            {
                break;
            }
            kept.Add(lines[i]);
        }
        return string.Join("\n", kept).Trim();
    }
}

public sealed record SceneCandidate(string PersonaId, string Name, IReadOnlyList<string> Aliases, float[]? Interests, double Urge);

public sealed record RankedSpeaker(string PersonaId, string Name, double Score, string Why)
{
    public bool Named => Why.Contains("named", StringComparison.Ordinal);
}

/// <summary>
/// Layer three: who speaks next. Blends being named or addressed, how relevant the last line is to each
/// persona (local embeddings, no extra model call, so no dead air), and how strong each persona's drives are,
/// with a nudge against the same voices dominating. Picking is weighted, so the scene feels alive rather than
/// round-robin, but someone named always answers.
/// </summary>
public static class TurnScheduler
{
    public const double NamedBonus = 3.0;
    public const double RelevanceWeight = 1.2;
    public const double UrgeWeight = 0.8;
    public const double RecentPenalty = 0.3;
    public const double QuietBonus = 0.2;
    public const double Temperature = 0.35;

    public static List<RankedSpeaker> Rank(IReadOnlyList<SceneCandidate> present, SceneEvent last, IReadOnlyList<SceneEvent> recent, float[]? lastEmbedding, TurnPolicy policy)
    {
        List<SceneEvent> said = recent.Where(e => e.Kind == SceneEventKinds.Said).ToList();
        var ranked = new List<RankedSpeaker>();
        foreach (SceneCandidate c in present)
        {
            if (c.PersonaId == last.SpeakerId)
            {
                continue;
            }
            double score = 0;
            var why = new List<string>();
            if (Mentions(last.Text, c))
            {
                score += NamedBonus;
                why.Add("named");
            }
            if (lastEmbedding != null && c.Interests != null)
            {
                double rel = Math.Clamp((Cosine(lastEmbedding, c.Interests) - 0.05) / 0.45, 0, 1);
                score += rel * RelevanceWeight;
                why.Add(string.Create(CultureInfo.InvariantCulture, $"relevance {rel:0.00}"));
            }
            score += c.Urge * UrgeWeight;
            why.Add(string.Create(CultureInfo.InvariantCulture, $"drive {c.Urge:0.00}"));
            int spoke = said.TakeLast(4).Count(e => e.SpeakerId == c.PersonaId);
            if (spoke > 0)
            {
                score -= spoke * RecentPenalty;
                why.Add($"spoke recently x{spoke}");
            }
            else if (said.TakeLast(8).All(e => e.SpeakerId != c.PersonaId))
            {
                score += QuietBonus;
                why.Add("has been quiet");
            }
            ranked.Add(new RankedSpeaker(c.PersonaId, c.Name, score, string.Join(", ", why)));
        }
        return ranked.OrderByDescending(r => r.Score).ToList();
    }

    /// <summary>After a persona has spoken: does someone else naturally react, or does the scene wait for you?</summary>
    public static bool ShouldContinue(RankedSpeaker top, int chainSoFar, TurnPolicy policy) =>
        chainSoFar < policy.MaxChain && (top.Named || top.Score >= policy.ContinueAbove);

    public static RankedSpeaker Pick(IReadOnlyList<RankedSpeaker> ranked, Random rng)
    {
        if (ranked.Count == 0)
        {
            throw new SceneException("Nobody in the scene can speak.");
        }
        if (ranked[0].Named)
        {
            return ranked[0];
        }
        double max = ranked.Max(r => r.Score);
        double[] w = ranked.Select(r => Math.Exp((r.Score - max) / Temperature)).ToArray();
        double roll = rng.NextDouble() * w.Sum();
        for (int i = 0; i < w.Length; i++)
        {
            roll -= w[i];
            if (roll <= 0)
            {
                return ranked[i];
            }
        }
        return ranked[^1];
    }

    /// <summary>How much a persona's strongest weighted drive is pushing her to act right now (about 0 to 1.5).</summary>
    public static double Urge(PersonaConfig config, IReadOnlyDictionary<string, DriveValue> drives)
    {
        double maxWeight = config.Drives.Max(d => d.Weight ?? 0);
        if (maxWeight <= 0)
        {
            return 0;
        }
        double top = config.Drives.Max(d => drives.TryGetValue(d.Name, out DriveValue? v) ? DriveEngine.Priority(d, v.Value, v.UnmetStreak) : 0);
        return Math.Clamp(top / (Drives.Max * maxWeight), 0, 1.5);
    }

    private static bool Mentions(string text, SceneCandidate c) =>
        new[] { c.Name }.Concat(c.Aliases).Where(n => n.Trim().Length > 0)
            .Any(n => Regex.IsMatch(text, @"(?<![\p{L}])" + Regex.Escape(n.Trim()) + @"(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            return 0;
        }
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / Math.Sqrt(na * nb);
    }
}
