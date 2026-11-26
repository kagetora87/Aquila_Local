using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AquilaLocal.Companion.Speech;

public static class VoiceActions
{
    /// <summary>Full attention: listen and converse normally.</summary>
    public const string Talk = "talk";

    /// <summary>Call a persona by name: her own call chat, answered aloud in her voice.</summary>
    public const string Call = "call";

    /// <summary>End the current call.</summary>
    public const string HangUp = "hangup";

    /// <summary>Stop paying attention and go back to waiting for the wake word.</summary>
    public const string Stop = "stop";

    public static readonly IReadOnlyList<string> All = new[] { Talk, Call, HangUp, Stop };
}

public sealed class VoiceCommand
{
    public string Action { get; set; } = "";

    /// <summary>Ways of saying it. Call phrases contain {name} where the persona's name goes.</summary>
    public List<string> Phrases { get; set; } = new();
}

/// <summary>The wake word and the commands that can follow it. Saved as an editable JSON file.</summary>
public sealed class VoiceCommandSet
{
    public const string NameSlot = "{name}";

    public string WakeWord { get; set; } = "Aquila";

    /// <summary>How the recognizer sometimes writes the wake word ("Akila", "Aquilla"...), so it still counts.</summary>
    public List<string> WakeSoundsLike { get; set; } = new();

    public List<VoiceCommand> Commands { get; set; } = new();

    public static VoiceCommandSet Defaults() => new()
    {
        WakeWord = "Aquila",
        WakeSoundsLike = { "Akila", "Aquilla", "Aquela", "Aquilah", "Achila", "Aguila", "Equila", "A quila", "Ah kila", "Okila" },
        Commands =
        {
            new VoiceCommand { Action = VoiceActions.Talk, Phrases = { "let's talk", "let's chat", "talk to me", "can we talk", "can we chat", "I want to talk", "I want to chat", "have a conversation", "let's have a chat", "are you there", "I need to talk" } },
            new VoiceCommand { Action = VoiceActions.Call, Phrases = { "call {name}", "phone {name}", "ring {name}", "get me {name}", "put me through to {name}", "connect me to {name}", "I want to talk to {name}", "let me talk to {name}" } },
            new VoiceCommand { Action = VoiceActions.HangUp, Phrases = { "hang up", "end the call", "end call", "hang up the call" } },
            new VoiceCommand { Action = VoiceActions.Stop, Phrases = { "stop listening", "go to sleep", "that's all", "never mind", "stop" } },
        }
    };

    /// <summary>Reads the file, or the defaults if there isn't one yet. A broken file is refused, never half-used.</summary>
    public static VoiceCommandSet Load(string path)
    {
        if (!File.Exists(path))
        {
            return Defaults();
        }
        VoiceCommandSet? set;
        try
        {
            set = JsonSerializer.Deserialize<VoiceCommandSet>(File.ReadAllText(path), PersonaConfig.Json);
        }
        catch (JsonException ex)
        {
            throw new PersonaConfigException($"The voice commands file can't be read ({path}): {ex.Message}", ex);
        }
        if (set == null)
        {
            throw new PersonaConfigException($"The voice commands file is empty: {path}");
        }
        try
        {
            set.Validate();
        }
        catch (PersonaConfigException ex)
        {
            throw new PersonaConfigException($"{ex.Message} (in {path})", ex);
        }
        return set;
    }

    public void Save(string path)
    {
        Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, PersonaConfig.Json));
        File.Move(tmp, path, overwrite: true);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(WakeWord) || WakeWord.Trim().Length > 40)
        {
            throw new PersonaConfigException("The wake word must be a word or two (up to 40 letters).");
        }
        if (Commands.Count == 0)
        {
            throw new PersonaConfigException("There are no voice commands.");
        }
        foreach (VoiceCommand c in Commands)
        {
            if (!VoiceActions.All.Contains(c.Action))
            {
                throw new PersonaConfigException($"'{c.Action}' isn't a voice command Aquila knows. Use one of: {string.Join(", ", VoiceActions.All)}.");
            }
            List<string> phrases = c.Phrases.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (phrases.Count == 0)
            {
                throw new PersonaConfigException($"The '{c.Action}' command has no phrases.");
            }
            foreach (string p in phrases)
            {
                bool slot = p.Contains(NameSlot, StringComparison.OrdinalIgnoreCase);
                if (c.Action == VoiceActions.Call && !slot)
                {
                    throw new PersonaConfigException($"The call phrase \"{p}\" needs {NameSlot} where the persona's name goes, like \"call {NameSlot}\".");
                }
                if (c.Action != VoiceActions.Call && slot)
                {
                    throw new PersonaConfigException($"Only call phrases can contain {NameSlot} (found in \"{p}\").");
                }
            }
        }
    }
}

public sealed record WakeResult(bool Heard, string Rest);

/// <param name="PersonaId">For a call: who was matched, or null if nobody by that name exists.</param>
/// <param name="PersonaName">For a call: the matched persona's name, or the name as heard when nobody matched.</param>
public sealed record CommandMatch(string Action, string? PersonaId, string? PersonaName, double Score);

/// <summary>
/// Understanding what was said after the wake word. Loose on purpose: punctuation, case and the recognizer's
/// spelling of names don't matter ("Vicky" is Vicki, "Sofia" is Sophia), and a wording that contains a known
/// phrase counts. An optional meaning check (local embeddings) catches wordings that aren't in the list.
/// </summary>
public static class VoiceCommandMatcher
{
    public const double FuzzyWake = 0.8;
    public const double NameMatch = 0.7;
    public const double PhraseMatch = 0.75;
    public const double MeaningMatch = 0.7;

    private static readonly HashSet<string> Fillers = new() { "please", "now", "for", "me", "up", "right", "away", "thanks", "thank", "you", "the", "a", "an", "to", "on", "phone" };

    private static readonly Regex Word = new(@"[\p{L}\p{N}'’]+", RegexOptions.CultureInvariant);

    public static WakeResult FindWake(string transcript, VoiceCommandSet set)
    {
        List<Match> words = Word.Matches(transcript ?? "").Cast<Match>().ToList();
        if (words.Count == 0)
        {
            return new WakeResult(false, "");
        }
        string[] tokens = words.Select(w => Norm(w.Value)).ToArray();
        string wake = Norm(set.WakeWord);
        List<string[]> forms = new[] { set.WakeWord }.Concat(set.WakeSoundsLike).Select(f => Tokens(f)).Where(t => t.Length > 0).ToList();
        for (int i = 0; i < tokens.Length; i++)
        {
            foreach (string[] form in forms)
            {
                if (i + form.Length <= tokens.Length && form.Select((t, k) => t == tokens[i + k]).All(x => x))
                {
                    return Found(transcript!, words[i + form.Length - 1]);
                }
            }
            if (!wake.Contains(' ') && Math.Abs(tokens[i].Length - wake.Length) <= 2 && Ratio(tokens[i], wake) >= FuzzyWake)
            {
                return Found(transcript!, words[i]);
            }
        }
        return new WakeResult(false, "");
    }

    private static WakeResult Found(string transcript, Match last)
    {
        string rest = transcript[(last.Index + last.Length)..].TrimStart(' ', ',', '.', '!', '?', ';', ':', '-', '—', '…').Trim();
        return new WakeResult(true, rest);
    }

    public static CommandMatch? Match(string command, VoiceCommandSet set, IReadOnlyList<(string Id, string Name)> personas, Func<string, string, double>? meaning = null)
    {
        string[] said = Tokens(command);
        if (said.Length == 0)
        {
            return null;
        }
        CommandMatch? call = MatchCall(said, set, personas);
        if (call != null)
        {
            return call;
        }
        CommandMatch? best = null;
        foreach (VoiceCommand c in set.Commands.Where(c => c.Action != VoiceActions.Call))
        {
            foreach (string phrase in c.Phrases.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                string[] p = Tokens(phrase);
                double score = Contains(said, p, 0) >= 0 ? 1.0 : p.Count(t => said.Any(s => Same(s, t))) / (double)Math.Max(p.Length, said.Length);
                if (score >= PhraseMatch && (best == null || score > best.Score))
                {
                    best = new CommandMatch(c.Action, null, null, score);
                }
            }
        }
        if (best != null || meaning == null)
        {
            return best;
        }
        foreach (VoiceCommand c in set.Commands.Where(c => c.Action != VoiceActions.Call))
        {
            foreach (string phrase in c.Phrases.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                double score = meaning(command, phrase);
                if (score >= MeaningMatch && (best == null || score > best.Score))
                {
                    best = new CommandMatch(c.Action, null, null, score);
                }
            }
        }
        return best;
    }

    private static CommandMatch? MatchCall(string[] said, VoiceCommandSet set, IReadOnlyList<(string Id, string Name)> personas)
    {
        foreach (VoiceCommand c in set.Commands.Where(c => c.Action == VoiceActions.Call))
        {
            foreach (string phrase in c.Phrases.Where(p => p.Contains(VoiceCommandSet.NameSlot, StringComparison.OrdinalIgnoreCase)))
            {
                int slot = phrase.IndexOf(VoiceCommandSet.NameSlot, StringComparison.OrdinalIgnoreCase);
                string[] before = Tokens(phrase[..slot]);
                if (before.Length == 0)
                {
                    continue;
                }
                int at = Contains(said, before, 0);
                if (at < 0)
                {
                    continue;
                }
                string[] name = said.Skip(at + before.Length).Where(t => !Fillers.Contains(t)).ToArray();
                if (name.Length == 0)
                {
                    continue;
                }
                (string Id, string Name)? who = null;
                double bestScore = 0;
                foreach ((string id, string personaName) in personas)
                {
                    string target = Sound(string.Join(" ", Tokens(personaName)));
                    int len = Math.Max(1, Tokens(personaName).Length);
                    for (int k = 1; k <= Math.Min(len + 1, name.Length); k++)
                    {
                        double r = Ratio(Sound(string.Join(" ", name.Take(k))), target);
                        if (r > bestScore)
                        {
                            bestScore = r;
                            who = (id, personaName);
                        }
                    }
                }
                if (who != null && bestScore >= NameMatch)
                {
                    return new CommandMatch(VoiceActions.Call, who.Value.Id, who.Value.Name, bestScore);
                }
                string heard = string.Join(" ", name.Select(t => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(t)));
                return new CommandMatch(VoiceActions.Call, null, heard, 0);
            }
        }
        return null;
    }

    /// <summary>A hint for the recognizer so it spells the wake word and persona names as you do.</summary>
    public static string Hint(VoiceCommandSet set, IReadOnlyList<(string Id, string Name)> personas) =>
        string.Join(", ", new[] { set.WakeWord.Trim() }.Concat(personas.Select(p => p.Name.Trim())).Where(s => s.Length > 0)) + ".";

    public static string Norm(string s) => s.ToLowerInvariant().Replace("'", "").Replace("’", "");

    private static string[] Tokens(string s) => Word.Matches(s ?? "").Select(m => Norm(m.Value)).Where(t => t.Length > 0).ToArray();

    private static int Contains(string[] said, string[] phrase, int from)
    {
        for (int i = from; i + phrase.Length <= said.Length; i++)
        {
            bool all = true;
            for (int k = 0; k < phrase.Length && all; k++)
            {
                all = Same(said[i + k], phrase[k]);
            }
            if (all)
            {
                return i;
            }
        }
        return -1;
    }

    private static bool Same(string a, string b) => a == b || (a.Length >= 5 && b.Length >= 5 && Ratio(a, b) >= 0.85);

    /// <summary>A rough sound-alike key: "Sophia" and "Sofia", "Vicky" and "Vicki" come out the same.</summary>
    private static string Sound(string s)
    {
        string t = s.Replace("ph", "f").Replace("ck", "k").Replace("qu", "kw").Replace("c", "k").Replace("y", "i").Replace("ee", "i").Replace("ie", "i");
        var sb = new StringBuilder();
        foreach (char ch in t)
        {
            if (sb.Length == 0 || sb[^1] != ch)
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    private static double Ratio(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
        {
            return 1;
        }
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }
        }
        return 1.0 - d[a.Length, b.Length] / (double)Math.Max(a.Length, b.Length);
    }
}
