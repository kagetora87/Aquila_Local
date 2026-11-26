using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AquilaLocal.Companion.Sounds;

/// <summary>One sound on the soundboard: what it's called, what kind of sound it is, and a short description.</summary>
public sealed class SoundEntry
{
    public string Name { get; set; } = "";

    /// <summary>A kind or category, like "door", "laugh", "phone", "animal".</summary>
    public string Type { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>The file name inside the soundboard folder ("03 - Door knock.wav"), numbered by its place in the list.</summary>
    public string File { get; set; } = "";
}

/// <summary>
/// The soundboard: sounds in the order you put them, kept in one folder (Data\soundboard) as numbered files plus
/// soundboard.json with their names, types and descriptions. Personas can cue one with &lt;sound&gt;name&lt;/sound&gt;.
/// </summary>
public sealed class SoundboardSet
{
    public const int MaxSounds = 200;

    public const int MaxNameLength = 60;

    public const int MaxDescriptionLength = 200;

    public const long MaxFileBytes = 25L * 1024 * 1024;

    /// <summary>File types Windows can play without anything extra.</summary>
    public static readonly IReadOnlyList<string> Extensions = new[] { ".wav", ".mp3", ".wma", ".m4a", ".aac" };

    public List<SoundEntry> Sounds { get; set; } = new();

    public void Validate()
    {
        if (Sounds.Count > MaxSounds)
        {
            throw new PersonaConfigException($"The soundboard holds up to {MaxSounds} sounds; it has {Sounds.Count}.");
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Sounds.Count; i++)
        {
            SoundEntry s = Sounds[i];
            string name = (s.Name ?? "").Trim();
            if (name.Length == 0)
            {
                throw new PersonaConfigException($"Sound {i + 1} needs a name.");
            }
            if (name.Length > MaxNameLength)
            {
                throw new PersonaConfigException($"The name of sound {i + 1} is longer than {MaxNameLength} letters.");
            }
            if (name.Contains('<') || name.Contains('>'))
            {
                throw new PersonaConfigException($"The name \"{name}\" can't contain < or >.");
            }
            if (!names.Add(name))
            {
                throw new PersonaConfigException($"Two sounds are called \"{name}\". Each sound needs its own name.");
            }
            if ((s.Description ?? "").Length > MaxDescriptionLength)
            {
                throw new PersonaConfigException($"The description of \"{name}\" is longer than {MaxDescriptionLength} letters.");
            }
            if ((s.Type ?? "").Length > MaxNameLength)
            {
                throw new PersonaConfigException($"The type of \"{name}\" is longer than {MaxNameLength} letters.");
            }
            if (string.IsNullOrWhiteSpace(s.File) || s.File != Path.GetFileName(s.File))
            {
                throw new PersonaConfigException($"\"{name}\" has no sound file.");
            }
        }
    }
}

/// <summary>Reads and writes the soundboard folder. Files are renumbered to match the list order on every save.</summary>
public sealed class SoundboardLibrary
{
    public SoundboardLibrary(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }

    public string ListPath => Path.Combine(Folder, "soundboard.json");

    public string PathOf(SoundEntry entry) => Path.Combine(Folder, entry.File);

    /// <summary>The soundboard, or an empty one if there isn't one yet. A broken file is refused, never half-used.</summary>
    public SoundboardSet Load()
    {
        if (!File.Exists(ListPath))
        {
            return new SoundboardSet();
        }
        SoundboardSet? set;
        try
        {
            set = JsonSerializer.Deserialize<SoundboardSet>(File.ReadAllText(ListPath), PersonaConfig.Json);
        }
        catch (JsonException ex)
        {
            throw new PersonaConfigException($"The soundboard list can't be read ({ListPath}): {ex.Message}", ex);
        }
        if (set == null)
        {
            throw new PersonaConfigException($"The soundboard list is empty: {ListPath}");
        }
        set.Validate();
        return set;
    }

    /// <summary>Copies a sound file into the folder and adds it to the end of the list (not saved until Save).</summary>
    public SoundEntry Import(SoundboardSet set, string sourcePath, string? name = null)
    {
        string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!SoundboardSet.Extensions.Contains(ext))
        {
            throw new PersonaConfigException($"\"{Path.GetFileName(sourcePath)}\" isn't a sound type Aquila can play. Use {string.Join(", ", SoundboardSet.Extensions)}.");
        }
        var info = new FileInfo(sourcePath);
        if (!info.Exists)
        {
            throw new PersonaConfigException($"\"{sourcePath}\" doesn't exist.");
        }
        if (info.Length > SoundboardSet.MaxFileBytes)
        {
            throw new PersonaConfigException($"\"{info.Name}\" is over {SoundboardSet.MaxFileBytes / (1024 * 1024)} MB. Soundboard sounds should be short.");
        }
        if (set.Sounds.Count >= SoundboardSet.MaxSounds)
        {
            throw new PersonaConfigException($"The soundboard is full ({SoundboardSet.MaxSounds} sounds).");
        }
        string baseName = (name ?? Path.GetFileNameWithoutExtension(sourcePath)).Trim();
        baseName = Regex.Replace(baseName, @"[_]+", " ").Trim();
        baseName = baseName.Replace("<", "").Replace(">", "");
        if (baseName.Length > SoundboardSet.MaxNameLength)
        {
            baseName = baseName[..SoundboardSet.MaxNameLength].Trim();
        }
        if (baseName.Length == 0)
        {
            baseName = "Sound";
        }
        string unique = baseName;
        for (int n = 2; set.Sounds.Any(s => s.Name.Equals(unique, StringComparison.OrdinalIgnoreCase)); n++)
        {
            unique = $"{baseName} {n}";
        }
        Directory.CreateDirectory(Folder);
        string file = "new-" + Guid.NewGuid().ToString("N")[..8] + ext;
        File.Copy(sourcePath, Path.Combine(Folder, file));
        var entry = new SoundEntry { Name = unique, File = file };
        set.Sounds.Add(entry);
        return entry;
    }

    /// <summary>
    /// Saves the list and renames the files to "NN - Name.ext" in list order. Files no longer in the list are
    /// moved to a "removed" subfolder, not deleted.
    /// </summary>
    public void Save(SoundboardSet set)
    {
        foreach (SoundEntry s in set.Sounds)
        {
            s.Name = s.Name.Trim();
            s.Type = (s.Type ?? "").Trim();
            s.Description = (s.Description ?? "").Trim();
        }
        set.Validate();
        Directory.CreateDirectory(Folder);
        foreach (SoundEntry s in set.Sounds)
        {
            if (!File.Exists(PathOf(s)))
            {
                throw new PersonaConfigException($"The file for \"{s.Name}\" is missing ({s.File}). Remove it or add it again.");
            }
        }
        // Two passes so "02 - A" and "01 - B" can swap without colliding.
        var temp = new List<(SoundEntry Entry, string Temp)>();
        foreach (SoundEntry s in set.Sounds)
        {
            string t = "moving-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(s.File);
            File.Move(PathOf(s), Path.Combine(Folder, t));
            temp.Add((s, t));
        }
        int width = Math.Max(2, set.Sounds.Count.ToString(CultureInfo.InvariantCulture).Length);
        for (int i = 0; i < temp.Count; i++)
        {
            (SoundEntry s, string t) = temp[i];
            string final = (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0') + " - " + SafeFileName(s.Name) + Path.GetExtension(t).ToLowerInvariant();
            File.Move(Path.Combine(Folder, t), Path.Combine(Folder, final));
            s.File = final;
        }
        var kept = new HashSet<string>(set.Sounds.Select(s => s.File), StringComparer.OrdinalIgnoreCase);
        foreach (string f in Directory.GetFiles(Folder))
        {
            string name = Path.GetFileName(f);
            if (kept.Contains(name) || name.Equals("soundboard.json", StringComparison.OrdinalIgnoreCase) || !SoundboardSet.Extensions.Contains(Path.GetExtension(name).ToLowerInvariant()))
            {
                continue;
            }
            string removed = Path.Combine(Folder, "removed");
            Directory.CreateDirectory(removed);
            string target = Path.Combine(removed, name);
            if (File.Exists(target))
            {
                target = Path.Combine(removed, Path.GetFileNameWithoutExtension(name) + " " + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + Path.GetExtension(name));
            }
            File.Move(f, target);
        }
        string tmp = ListPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(set, PersonaConfig.Json));
        File.Move(tmp, ListPath, overwrite: true);
    }

    private static string SafeFileName(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            sb.Append(Path.GetInvalidFileNameChars().Contains(c) || c == '/' || c == '\\' || c == ':' || c == '*' || c == '?' || c == '"' || c == '|' ? '-' : c);
        }
        return sb.ToString().Trim().TrimEnd('.');
    }
}

/// <summary>&lt;sound&gt;name&lt;/sound&gt; cues in a reply, the soundboard list for her prompt, and matching a cue to a sound.</summary>
public static class SoundCues
{
    public const int MaxListed = 120;

    public const int MaxPerReply = 3;

    private static readonly Regex Cue = new(@"<sound>([^<>\n]{1,80})</sound>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>An unfinished cue at the end of a reply that's still being written.</summary>
    private static readonly Regex Partial = new(@"<(s(o(u(n(d(>[^<>\n]{0,80}(<(/(s(o(u(n(d)?)?)?)?)?)?)?)?)?)?)?)?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool HasCue(string text) => Cue.IsMatch(text ?? "");

    /// <summary>The sound names she cued, in order (at most three per reply).</summary>
    public static List<string> Names(string text) =>
        Cue.Matches(text ?? "").Select(m => m.Groups[1].Value.Trim()).Where(n => n.Length > 0).Take(MaxPerReply).ToList();

    /// <summary>The reply without its cues (for her memory and plain text).</summary>
    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('<'))
        {
            return text ?? "";
        }
        string s = Partial.Replace(Cue.Replace(text, ""), "");
        return Regex.Replace(s, @"[ \t]{2,}", " ").Trim();
    }

    /// <summary>Cues shown as an action ("🔊 Door knock"), and an unfinished cue hidden while she's still writing.</summary>
    public static string ForDisplay(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('<'))
        {
            return text ?? "";
        }
        return Partial.Replace(Cue.Replace(text, m => "<action>🔊 " + m.Groups[1].Value.Trim() + "</action>"), "");
    }

    /// <summary>Cues become blanks of the same length, so the voice skips them and voice cue positions don't move.</summary>
    public static string MaskForSpeech(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('<'))
        {
            return text ?? "";
        }
        return Cue.Replace(text, m => new string(' ', m.Length));
    }

    /// <summary>What a persona is told about the soundboard (empty when there are no sounds).</summary>
    public static string Prompt(SoundboardSet set)
    {
        if (set.Sounds.Count == 0)
        {
            return "";
        }
        var sb = new StringBuilder();
        sb.Append("Soundboard: you can play a sound effect when something you do or something happening would really make a sound (a knock, a laugh, a door, a phone, a kiss...). ")
          .Append("Check the list below for a sound that matches by name, type or description. If there's a good match, put <sound>its exact name</sound> anywhere in your reply; it plays after your message (after your voice, if you're speaking). ")
          .Append("Use it only when it fits the moment, at most one or two in a reply, and never mention the soundboard itself. If nothing matches well, play nothing.\n")
          .Append("Sounds (name - type: description):\n");
        foreach (SoundEntry s in set.Sounds.Take(MaxListed))
        {
            sb.Append("- ").Append(s.Name);
            if (s.Type.Length > 0)
            {
                sb.Append(" - ").Append(s.Type);
            }
            if (s.Description.Length > 0)
            {
                sb.Append(": ").Append(s.Description.Length > 100 ? s.Description[..100] + "…" : s.Description);
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// The sound a cue means: the exact name first, then a close spelling, then shared words in name, type and
    /// description, then (if given) similarity of meaning. Null when nothing matches well enough.
    /// </summary>
    public static SoundEntry? Match(string cue, SoundboardSet set, Func<string, string, double>? meaning = null)
    {
        string want = Normal(cue);
        if (want.Length == 0 || set.Sounds.Count == 0)
        {
            return null;
        }
        SoundEntry? exact = set.Sounds.FirstOrDefault(s => Normal(s.Name) == want);
        if (exact != null)
        {
            return exact;
        }
        SoundEntry? best = null;
        double bestScore = 0;
        HashSet<string> wantWords = Words(want);
        foreach (SoundEntry s in set.Sounds)
        {
            string name = Normal(s.Name);
            double score = Ratio(name, want);
            if (name.Length >= 3 && (want.Contains(name) || name.Contains(want)))
            {
                score = Math.Max(score, 0.85);
            }
            HashSet<string> nameWords = Words(name);
            HashSet<string> allWords = Words(Normal(s.Name + " " + s.Type + " " + s.Description));
            if (wantWords.Count > 0)
            {
                double inName = wantWords.Count(w => nameWords.Any(n => SameWord(n, w))) / (double)wantWords.Count;
                double inAll = wantWords.Count(w => allWords.Any(n => SameWord(n, w))) / (double)wantWords.Count;
                score = Math.Max(score, Math.Max(inName * 0.9, inAll * 0.75));
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = s;
            }
        }
        if (bestScore >= 0.7)
        {
            return best;
        }
        if (meaning != null)
        {
            SoundEntry? close = null;
            double closeScore = 0;
            foreach (SoundEntry s in set.Sounds)
            {
                double m = meaning(cue, (s.Name + ". " + s.Type + ". " + s.Description).Trim(' ', '.'));
                if (m > closeScore)
                {
                    closeScore = m;
                    close = s;
                }
            }
            if (closeScore >= 0.6)
            {
                return close;
            }
        }
        return null;
    }

    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal) { "a", "an", "the", "of", "on", "at", "in", "to", "and", "sound", "sounds", "noise", "effect", "sfx" };

    private static string Normal(string s) => Regex.Replace((s ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    private static HashSet<string> Words(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Filler.Contains(w)).ToHashSet();

    /// <summary>"knock", "knocks", "knocking" and "knocked" count as the same word.</summary>
    private static bool SameWord(string a, string b)
    {
        if (a == b)
        {
            return true;
        }
        string sa = Stem(a), sb = Stem(b);
        return sa.Length >= 3 && sa == sb;
    }

    private static string Stem(string w)
    {
        foreach (string end in new[] { "ing", "ed", "es", "s" })
        {
            if (w.Length > end.Length + 2 && w.EndsWith(end, StringComparison.Ordinal))
            {
                return w[..^end.Length];
            }
        }
        return w;
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
