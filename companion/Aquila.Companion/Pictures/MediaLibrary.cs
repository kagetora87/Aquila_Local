using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AquilaLocal.Companion.Pictures;

/// <summary>One picture in My Media: the still, its video if one was made from it, when, and the prompt it was drawn from.</summary>
public sealed record MediaItem(string Path, string Clip, DateTimeOffset At, string Prompt, string Model, int ReferenceCount)
{
    public bool IsVideo => Clip.Length > 0;
}

/// <summary>
/// Everything drawn on this computer (Data\pictures\yyyy-MM\*.png, with any .webm made from it), newest first,
/// and which of them you marked as favorites (Data\pictures\favorites.json).
/// </summary>
public sealed class MediaLibrary
{
    private static readonly Regex MonthFolder = new(@"^\d{4}-\d{2}$");
    private static readonly Regex Stamp = new(@"^(\d{8}-\d{6})");

    public MediaLibrary(string picturesRoot)
    {
        Root = picturesRoot;
    }

    public string Root { get; }

    public string FavoritesPath => Path.Combine(Root, "favorites.json");

    /// <summary>All pictures, newest first. Video first-frame helpers (*.start.png) aren't pictures of their own.</summary>
    public List<MediaItem> Scan()
    {
        List<MediaItem> items = new();
        if (!Directory.Exists(Root))
        {
            return items;
        }
        foreach (string folder in Directory.GetDirectories(Root).Where(d => MonthFolder.IsMatch(Path.GetFileName(d))))
        {
            foreach (string file in Directory.GetFiles(folder, "*.png"))
            {
                if (file.EndsWith(".start.png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string clip = System.IO.Path.ChangeExtension(file, ".webm");
                (string prompt, string model, int refs) = ReadSidecar(file);
                items.Add(new MediaItem(file, File.Exists(clip) ? clip : "", WhenMade(file), prompt, model, refs));
            }
        }
        return items.OrderByDescending(i => i.At).ThenByDescending(i => i.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>When it was made: from its name (yyyyMMdd-HHmmss…), else the file's time.</summary>
    public static DateTimeOffset WhenMade(string file)
    {
        Match m = Stamp.Match(System.IO.Path.GetFileName(file));
        if (m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime at))
        {
            return new DateTimeOffset(at);
        }
        return new DateTimeOffset(File.GetLastWriteTime(file));
    }

    /// <summary>The prompt, model and reference count saved beside a picture (its .txt), or blanks.</summary>
    public static (string Prompt, string Model, int References) ReadSidecar(string picture)
    {
        string sidecar = System.IO.Path.ChangeExtension(picture, ".txt");
        if (!File.Exists(sidecar))
        {
            return ("", "", 0);
        }
        string text;
        try
        {
            text = File.ReadAllText(sidecar).Replace("\r\n", "\n");
        }
        catch (IOException)
        {
            return ("", "", 0);
        }
        int split = text.IndexOf("\n\n", StringComparison.Ordinal);
        string prompt = (split >= 0 ? text[..split] : text).Trim();
        string model = "";
        int refs = 0;
        foreach (string line in (split >= 0 ? text[(split + 2)..] : "").Split('\n'))
        {
            if (line.StartsWith("Model: ", StringComparison.Ordinal))
            {
                model = line[7..].Trim();
            }
            else if (line.StartsWith("References: ", StringComparison.Ordinal))
            {
                refs = line[12..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
            }
        }
        return (prompt, model, refs);
    }

    private string Key(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        string root = System.IO.Path.GetFullPath(Root) + System.IO.Path.DirectorySeparatorChar;
        return (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full[root.Length..] : full).Replace('\\', '/');
    }

    public HashSet<string> Favorites()
    {
        HashSet<string> set = new(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(FavoritesPath))
        {
            return set;
        }
        try
        {
            foreach (string s in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FavoritesPath)) ?? new List<string>())
            {
                set.Add(s);
            }
        }
        catch (JsonException ex)
        {
            throw new PersonaConfigException("Your favorites file isn't valid (" + FavoritesPath + "): " + ex.Message, ex);
        }
        return set;
    }

    public bool IsFavorite(string path, HashSet<string>? favorites = null) => (favorites ?? Favorites()).Contains(Key(path));

    /// <summary>Marks or unmarks a favorite. Returns whether it is one now.</summary>
    public bool ToggleFavorite(string path)
    {
        HashSet<string> set = Favorites();
        string key = Key(path);
        bool now = set.Add(key) || !set.Remove(key);
        Save(set);
        return now;
    }

    private void Save(HashSet<string> set)
    {
        Directory.CreateDirectory(Root);
        string temp = FavoritesPath + ".saving";
        File.WriteAllText(temp, JsonSerializer.Serialize(set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FavoritesPath, overwrite: true);
    }

    /// <summary>Deletes a picture with its video and the notes saved beside them. Only pictures in My Media's folders.</summary>
    public void Delete(MediaItem item)
    {
        string full = System.IO.Path.GetFullPath(item.Path);
        if (!full.StartsWith(System.IO.Path.GetFullPath(Root) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new PersonaConfigException("That picture isn't in My Media.");
        }
        foreach (string f in new[] { full, System.IO.Path.ChangeExtension(full, ".webm"), System.IO.Path.ChangeExtension(full, ".txt"), System.IO.Path.ChangeExtension(full, ".video.txt"), System.IO.Path.ChangeExtension(full, ".start.png") })
        {
            if (File.Exists(f))
            {
                File.Delete(f);
            }
        }
        HashSet<string> set = Favorites();
        if (set.Remove(Key(full)))
        {
            Save(set);
        }
    }

    /// <summary>Whether a picture's file name says it's of this companion ("…-generate-vicki", "…-vicki-dream", "…-portrait-vicki").</summary>
    public static bool NamedFor(string path, string name)
    {
        string slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length == 0)
        {
            return false;
        }
        string file = "-" + System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant() + "-";
        return file.Contains("-" + slug + "-", StringComparison.Ordinal);
    }
}
