using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AquilaLocal.Companion.Living;

/// <summary>Her emotional state at one moment: one word, how strong (1 to 10), and why.</summary>
/// <param name="Source">Where it came from: "chat", "thought" or "dream".</param>
public sealed record MoodState(string Word, double Strength, string Why, DateTimeOffset At, string Source = MoodSources.Chat);

public static class MoodSources
{
    public const string Chat = "chat";
    public const string Thought = "thought";
    public const string Dream = "dream";
}

/// <summary>
/// Her mood, decided by her own reply each round at almost no cost: the rule lives once in the cached system
/// prompt, the current mood is one short line on the current message only, and she ends each reply with a tiny
/// hidden tag, &lt;mood strength="6" why="..."&gt;word&lt;/mood&gt;, which is taken out before anyone sees or hears the reply.
/// </summary>
public static class MoodMarkup
{
    public const int MaxWordChars = 24;

    public const int MaxWhyChars = 140;

    private static readonly Regex Tag = new(@"<mood\b([^<>]*)>([^<>]{0,60})</mood\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A tag she hasn't finished writing yet, at the end of a reply that's still streaming.</summary>
    private static readonly Regex Partial = new(@"<(m(o(o(d(\b[^<>]*(>[^<>]{0,60}(<(/(m(o(o(d\s*)?)?)?)?)?)?)?)?)?)?)?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Attribute = new(@"(\w+)\s*=\s*[""“]([^""”]*)[""”]", RegexOptions.CultureInvariant);

    /// <summary>Once, in her stable system prompt (so it's cached, not paid for on every message).</summary>
    public static string Guidance =>
        "Your mood: you have an emotional state that carries from moment to moment. At the very end of every reply, after everything else, add one hidden tag: " +
        "<mood strength=\"N\" why=\"a few words\" about=\"\" reading=\"\">word</mood>. word is one plain word for how you feel right now (like content, playful, wistful, anxious, smitten, irritated); " +
        "strength is 1 to 10; why is under 12 words, in your own voice. Moods shift gradually: keep the same one or move a little unless something really moves you. " +
        "about: one to three short words for the parts of your own inner world this reply touched (like loneliness, wanting, work, your past), or empty when it was simply about the moment. " +
        "reading: your quick guess, under 12 words, at how they're feeling and what they want from you right now. " +
        "Nobody sees the tag; never mention it or your mood rating.";

    /// <summary>The short line on the current message: how she's feeling coming in, and the reminder.</summary>
    public static string TurnLine(MoodState? current, DateTimeOffset now)
    {
        if (current == null)
        {
            return "[End this reply with your <mood> tag.]";
        }
        double strength = MoodRules.Decayed(current, now);
        return "[Your mood coming into this: " + current.Word + ", " + strength.ToString("0", CultureInfo.InvariantCulture) + "/10" +
            (current.Why.Length > 0 ? " (" + current.Why + ")" : "") + ". End this reply with your <mood> tag.]";
    }

    /// <summary>The last mood tag in a reply, or null when she didn't write one.</summary>
    public static MoodState? Read(string reply, DateTimeOffset at, string source = MoodSources.Chat)
    {
        MatchCollection all = Tag.Matches(reply ?? "");
        if (all.Count == 0)
        {
            return null;
        }
        Match m = all[^1];
        string word = Word(m.Groups[2].Value);
        if (word.Length == 0)
        {
            return null;
        }
        string why = "";
        double strength = 5;
        foreach (Match a in Attribute.Matches(m.Groups[1].Value))
        {
            string name = a.Groups[1].Value.ToLowerInvariant();
            if (name == "why")
            {
                why = Clip(a.Groups[2].Value, MaxWhyChars);
            }
            else if (name is "strength" or "intensity")
            {
                strength = Strength(a.Groups[2].Value, 5);
            }
        }
        return new MoodState(word, strength, why, at, source);
    }

    /// <summary>
    /// What else her last mood tag says: the parts of her inner world the reply touched (about="..."), and her read
    /// on how they feel (reading="...").
    /// </summary>
    public static (List<string> About, string Reading) Extra(string reply)
    {
        MatchCollection all = Tag.Matches(reply ?? "");
        var about = new List<string>();
        string reading = "";
        if (all.Count == 0)
        {
            return (about, reading);
        }
        foreach (Match a in Attribute.Matches(all[^1].Groups[1].Value))
        {
            string name = a.Groups[1].Value.ToLowerInvariant();
            if (name == "about")
            {
                about = a.Groups[2].Value.Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => Clip(t.ToLowerInvariant(), 30)).Where(t => t.Length > 0 && t != "none" && t != "nothing").Distinct().Take(3).ToList();
            }
            else if (name == "reading")
            {
                reading = Clip(a.Groups[2].Value, MaxWhyChars);
            }
        }
        return (about, reading);
    }

    /// <summary>The reply without mood tags (finished or still being written).</summary>
    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('<'))
        {
            return text ?? "";
        }
        string s = Tag.Replace(text, "");
        // A reaction written as markup (some models do that instead of calling the tool) is hidden the same way.
        s = ReactMarkup.Strip(s);
        s = Partial.Replace(s.TrimEnd(), "");
        return s.TrimEnd();
    }

    /// <summary>Tags become blanks of the same length, so the voice skips them and voice cue positions don't move.</summary>
    public static string MaskForSpeech(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('<'))
        {
            return text ?? "";
        }
        return ReactMarkup.Mask(Tag.Replace(text, m => new string(' ', m.Length)));
    }

    /// <summary>One lowercase word (or a short phrase cut to its first word), letters and hyphens only.</summary>
    public static string Word(string raw)
    {
        string s = Regex.Replace(raw ?? "", @"[^\p{L}\-\s]", " ").Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"\s+", " ");
        if (s.Length == 0)
        {
            return "";
        }
        // "a bit wistful" → "wistful": the last word carries the feeling.
        string[] words = s.Split(' ');
        string w = words.Length <= 2 ? s : words[^1];
        return w.Length > MaxWordChars ? w[..MaxWordChars] : w;
    }

    public static double Strength(string raw, double fallback)
    {
        Match m = Regex.Match(raw ?? "", @"\d+(\.\d+)?");
        if (!m.Success || !double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v))
        {
            return fallback;
        }
        return Math.Clamp(v, 1, 10);
    }

    private static string Clip(string s, int max)
    {
        s = Regex.Replace(s ?? "", @"\s+", " ").Trim();
        return s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
    }
}

/// <summary>How her mood settles over time, and when a thought or dream is strong enough to change it.</summary>
public static class MoodRules
{
    /// <summary>Where a mood settles back to (a calm baseline).</summary>
    public const double Baseline = 3;

    /// <summary>How fast a mood fades toward the baseline, in points per hour.</summary>
    public const double FadePerHour = 0.5;

    /// <summary>A dream this strong (or one she badly wants to tell you about) stays with her until she can tell you.</summary>
    public const double StrongDream = 7;

    public const double DreamUrgeToTell = 0.5;

    public static double Decayed(MoodState mood, DateTimeOffset now)
    {
        double hours = Math.Max(0, (now - mood.At).TotalHours);
        if (mood.Strength <= Baseline)
        {
            return mood.Strength;
        }
        return Math.Max(Baseline, mood.Strength - hours * FadePerHour);
    }

    /// <summary>
    /// Whether a mood from a thought or a dream takes over. Her own thoughts nudge her rather than swing her:
    /// the same feeling always refreshes it; a different one only wins when it's at least as strong as what she
    /// still feels now. (A chat reply always sets her mood: that's her reacting to you, with her last mood in view.)
    /// </summary>
    public static bool Replaces(MoodState? current, MoodState next, DateTimeOffset now)
    {
        if (current == null || next.Source == MoodSources.Chat)
        {
            return true;
        }
        if (string.Equals(current.Word, next.Word, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return next.Strength >= Decayed(current, now);
    }

    /// <summary>A dream she'll want to tell you about later.</summary>
    public static bool WorthTelling(double moodStrength, double urge) => moodStrength >= StrongDream || urge >= DreamUrgeToTell;
}

/// <summary>Her moods over time (Data\living\&lt;persona&gt;\mood.jsonl): appended, never rewritten; the last line is how she feels now.</summary>
public sealed class MoodLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly object gate = new();

    public MoodLog(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }

    public string PathOf(string personaId)
    {
        if (!PersonaRegistry.IsValidId(personaId))
        {
            throw new PersonaConfigException($"\"{personaId}\" isn't a valid persona id.");
        }
        return Path.Combine(Folder, personaId, "mood.jsonl");
    }

    public void Append(string personaId, MoodState mood)
    {
        string path = PathOf(personaId);
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, JsonSerializer.Serialize(mood, Json) + "\n", new UTF8Encoding(false));
        }
    }

    /// <summary>How she feels now, or null before her first mood. A damaged last line is reported, not skipped.</summary>
    public MoodState? Current(string personaId)
    {
        string path = PathOf(personaId);
        string? last;
        lock (gate)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            last = File.ReadLines(path).LastOrDefault(l => l.Trim().Length > 0);
        }
        if (last == null)
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<MoodState>(last, Json) ?? throw new JsonException("empty line");
        }
        catch (JsonException ex)
        {
            throw new PersonaConfigException($"The last line of {path} is damaged ({ex.Message}). Fix or remove that line.", ex);
        }
    }

    /// <summary>Sets her mood if the rules let it change. Returns the mood she has afterwards.</summary>
    public MoodState Offer(string personaId, MoodState next, DateTimeOffset now)
    {
        MoodState? current = Current(personaId);
        if (!MoodRules.Replaces(current, next, now))
        {
            return current!;
        }
        Append(personaId, next);
        return next;
    }
}

/// <summary>A dream (or feeling) she wants to tell you about when she next can.</summary>
/// <param name="Weight">How much it matters to her (its mood strength out of 10 plus her urge), so a stronger one replaces a weaker one.</param>
public sealed record ToTell(string JournalId, DateTimeOffset At, string Kind, string Text, string Idea, string Mood, double Weight = 0);

/// <summary>What she's waiting to tell you (Data\living\&lt;persona&gt;\to-tell.json). Kept for a day and a half at most.</summary>
public sealed class ToTellStore
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(36);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ToTellStore(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }

    private string PathOf(string personaId)
    {
        if (!PersonaRegistry.IsValidId(personaId))
        {
            throw new PersonaConfigException($"\"{personaId}\" isn't a valid persona id.");
        }
        return Path.Combine(Folder, personaId, "to-tell.json");
    }

    public void Save(string personaId, ToTell item)
    {
        string path = PathOf(personaId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(item, Json), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>What she still wants to tell you, or null (nothing, or it's too old now and has been let go).</summary>
    public ToTell? Load(string personaId, DateTimeOffset now)
    {
        string path = PathOf(personaId);
        if (!File.Exists(path))
        {
            return null;
        }
        ToTell? item;
        try
        {
            item = JsonSerializer.Deserialize<ToTell>(File.ReadAllText(path), Json);
        }
        catch (JsonException ex)
        {
            throw new PersonaConfigException($"{path} is damaged ({ex.Message}). Delete it to forget what was waiting to be said.", ex);
        }
        if (item == null || now - item.At > KeepFor)
        {
            File.Delete(path);
            return null;
        }
        return item;
    }

    public void Clear(string personaId)
    {
        string path = PathOf(personaId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>The line on your next message when you come back first, so she brings it up herself.</summary>
    public static string Reminder(ToTell item, DateTimeOffset now) =>
        "[Something on your mind: " + (item.Kind == ThoughtKinds.Dream ? "you dreamt this " : "you had this on your mind ") + InnerLifePrompt.Ago(now - item.At) +
        " and still want to tell them: \"" + (item.Text.Length > 500 ? item.Text[..499] + "…" : item.Text) + "\"" +
        (item.Idea.Length > 0 ? " (" + item.Idea + ")" : "") + ". Bring it up naturally in this reply, in your own words.]";

    /// <summary>The cue when she messages you about it herself.</summary>
    public static string Cue(ToTell item, DateTimeOffset localNow, TimeSpan? sinceTheyWrote) =>
        "[No new message from them. It's " + localNow.ToString("dddd h:mm tt", CultureInfo.InvariantCulture) +
        (sinceTheyWrote is TimeSpan away ? "; they last wrote " + InnerLifePrompt.Ago(away) : "") +
        ". " + (item.Kind == ThoughtKinds.Dream ? "You had a dream " + InnerLifePrompt.Ago(localNow - item.At) + " that's stayed with you" : "Something's stayed with you since " + InnerLifePrompt.Ago(localNow - item.At)) +
        (item.Mood.Length > 0 ? " (it left you " + item.Mood + ")" : "") + ": \"" + (item.Text.Length > 600 ? item.Text[..599] + "…" : item.Text) + "\"" +
        (item.Idea.Length > 0 ? " You want to tell them: " + item.Idea + "." : "") +
        " Now you can finally tell them. Write only the message you send them now: natural, in your usual style, like texting someone about a dream you can't stop thinking about. Don't mention being prompted.]";
}
