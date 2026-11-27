using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AquilaLocal.Companion.Pictures;

/// <summary>The two picture models: fast (FLUX 3 Image Klein, runs beside the chat model) and best (Qwen-Image-2.1, needs the chat model paused).</summary>
public static class PictureModels
{
    public const string Fast = "fast";

    public const string Best = "best";

    public static bool IsKnown(string model) => model == Fast || model == Best;

    public static string Label(string model) => model == Best ? "Qwen-Image-2.1 (best quality)" : "FLUX 3 Image Klein (fast)";
}

/// <summary>The files one model needs, found in the image folder (imagegen\sd and imagegen\models).</summary>
public sealed record PictureModelFiles(string Model, string Engine, string Diffusion, string TextEncoder, string Vae, string? Vision);

/// <summary>Finds the image engine and model files. Missing files are named, never guessed.</summary>
public sealed class PictureKit
{
    public PictureKit(string folder)
    {
        Folder = folder;
    }

    /// <summary>The image folder next to the app: sd\ holds the engine, models\ the model files.</summary>
    public string Folder { get; }

    public string EngineFolder => Path.Combine(Folder, "sd");

    public string ModelsFolder => Path.Combine(Folder, "models");

    public PictureModelFiles Resolve(string model)
    {
        if (!PictureModels.IsKnown(model))
        {
            throw new PersonaConfigException($"Unknown picture model \"{model}\". Choose fast or best.");
        }
        var missing = new List<string>();
        string engine = FirstExisting(EngineFolder, new[] { "sd-cli.exe", "sd.exe" }, "the image engine (sd-cli.exe)", missing);
        PictureModelFiles files = model == PictureModels.Fast
            ? new PictureModelFiles(model, engine,
                Find("flux3-image-klein*.gguf", "the FLUX 3 Image Klein model", missing, "flux-3-image-klein*.gguf", "flux3-image-klein*.safetensors", "flux-3-image-klein*.safetensors"),
                Find("Qwen3VL-4B*.gguf", "its text encoder (Qwen3-VL 4B)", missing, "Qwen3-VL-4B*.gguf"),
                Find("flux2-vae*.safetensors", "its VAE (flux2-vae)", missing, "flux2_ae*.safetensors", "ae.safetensors"),
                null)
            : new PictureModelFiles(model, engine,
                Find("qwen_image_2.1-*.gguf", "the Qwen-Image-2.1 model", missing, "qwen_image_2.1*.gguf", "qwen-image-2.1*.gguf"),
                Find("Qwen3VL-8B*.gguf", "its text encoder (Qwen3-VL 8B)", missing, "Qwen3-VL-8B*.gguf"),
                Find("qwen_image_2.1_vae*.safetensors", "its VAE (qwen_image_2.1_vae)", missing),
                Find("mmproj-Qwen3VL-8B*.gguf", "its vision file (mmproj-Qwen3VL-8B)", missing, "mmproj-Qwen3-VL-8B*.gguf"));
        if (missing.Count > 0)
        {
            throw new PersonaConfigException($"{PictureModels.Label(model)} can't draw yet: missing {string.Join(", ", missing)} in {Folder}.");
        }
        return files;
    }

    /// <summary>Whether a model is ready, with the problem if not.</summary>
    public (bool Ready, string Problem) Check(string model)
    {
        try
        {
            Resolve(model);
            return (true, "");
        }
        catch (PersonaConfigException ex)
        {
            return (false, ex.Message);
        }
    }

    private string Find(string pattern, string what, List<string> missing, params string[] more)
    {
        if (Directory.Exists(ModelsFolder))
        {
            foreach (string p in new[] { pattern }.Concat(more))
            {
                string? hit = Directory.GetFiles(ModelsFolder, p).Where(f => !Path.GetFileName(f).StartsWith("mmproj", StringComparison.OrdinalIgnoreCase) || pattern.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (hit != null)
                {
                    return hit;
                }
            }
        }
        missing.Add(what);
        return "";
    }

    private static string FirstExisting(string folder, string[] names, string what, List<string> missing)
    {
        foreach (string n in names)
        {
            string p = Path.Combine(folder, n);
            if (File.Exists(p))
            {
                return p;
            }
        }
        missing.Add(what);
        return "";
    }
}

/// <summary>What a picture should be: the prompt for the image model, its size, references and seed.</summary>
public sealed record PictureJob(string Prompt, int Width, int Height, IReadOnlyList<string> References, long Seed, string Output);

/// <summary>The image engine's command line for one picture (the settings measured on Takeru's laptop).</summary>
public static class PictureCommand
{
    public const int MaxReferences = 4;

    public static List<string> Arguments(PictureModelFiles files, PictureJob job)
    {
        if (job.Width % 32 != 0 || job.Height % 32 != 0 || job.Width < 256 || job.Height < 256 || job.Width > 2048 || job.Height > 2048)
        {
            throw new PersonaConfigException($"A picture must be 256 to 2048 pixels on each side, in steps of 32 ({job.Width}×{job.Height} isn't).");
        }
        var a = new List<string>
        {
            "--diffusion-model", files.Diffusion,
            "--llm", files.TextEncoder,
            "--vae", files.Vae,
            "-p", job.Prompt,
            "-W", job.Width.ToString(CultureInfo.InvariantCulture),
            "-H", job.Height.ToString(CultureInfo.InvariantCulture),
            "--seed", job.Seed.ToString(CultureInfo.InvariantCulture),
            "--sampling-method", "euler",
            "--offload-to-cpu",
            "-o", job.Output,
        };
        if (files.Model == PictureModels.Fast)
        {
            a.AddRange(new[] { "--cfg-scale", "1.0", "--steps", "16" });
        }
        else
        {
            a.AddRange(new[] { "--cfg-scale", "6.0", "--steps", "20" });
        }
        List<string> refs = job.References.Take(MaxReferences).ToList();
        foreach (string r in refs)
        {
            a.Add("-r");
            a.Add(r);
        }
        if (refs.Count > 0 && files.Vision != null)
        {
            a.Add("--llm_vision");
            a.Add(files.Vision);
        }
        return a;
    }

    /// <summary>Reads "step 5/16" style progress from the engine's output (null when a line isn't progress).</summary>
    public static (int Step, int Of)? Progress(string line)
    {
        Match m = Regex.Match(line ?? "", @"(\d{1,3})\s*/\s*(\d{1,3})\s*(?:\||-|it|s/it|steps?)", RegexOptions.CultureInvariant);
        if (!m.Success)
        {
            return null;
        }
        int step = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int of = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        return of > 0 && step <= of ? (step, of) : null;
    }
}

/// <summary>A picture someone asked for with the make_picture tool.</summary>
public sealed record PictureRequest(string Description, bool IncludesPersona, string Framing)
{
    public static readonly IReadOnlyList<string> Framings = new[] { "portrait", "half body", "full body", "scene", "landscape" };

    /// <summary>Reads the tool's arguments; a bad call is refused with a reason she can act on.</summary>
    public static PictureRequest Parse(string json)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json).RootElement;
        }
        catch (JsonException)
        {
            throw new PersonaConfigException("The picture request wasn't valid JSON.");
        }
        string description = root.TryGetProperty("description", out JsonElement d) && d.ValueKind == JsonValueKind.String ? d.GetString()!.Trim() : "";
        if (description.Length == 0)
        {
            throw new PersonaConfigException("Say what the picture should show (description).");
        }
        if (description.Length > 1500)
        {
            description = description[..1500];
        }
        bool includes = root.TryGetProperty("includes_you", out JsonElement i) && (i.ValueKind == JsonValueKind.True || (i.ValueKind == JsonValueKind.String && i.GetString()!.Equals("true", StringComparison.OrdinalIgnoreCase)));
        string framing = root.TryGetProperty("framing", out JsonElement f) && f.ValueKind == JsonValueKind.String ? f.GetString()!.Trim().ToLowerInvariant() : "";
        if (!Framings.Contains(framing))
        {
            framing = includes ? "half body" : "scene";
        }
        return new PictureRequest(description, includes, framing);
    }

    /// <summary>Picture size for the framing: tall for people, wide for scenes, square otherwise.</summary>
    public (int Width, int Height) Size() => Framing switch
    {
        "portrait" => (896, 1152),
        "half body" => (896, 1152),
        "full body" => (832, 1216),
        "landscape" => (1216, 832),
        "scene" => (1152, 896),
        _ => (1024, 1024)
    };
}

/// <summary>
/// Turning a picture request into an image prompt. The chat model itself writes it (no second model), from the
/// request, the recent conversation and, when she's in it, her appearance card.
/// </summary>
public static class PicturePrompt
{
    public const int MaxPromptChars = 1800;

    public const string WriterSystem =
        "You write prompts for a local image generator. Write one prompt for the picture described. "
        + "Output only the prompt: one paragraph of plain descriptive English, 40 to 140 words, no lists, no quotes, no explanations, no negative prompts. "
        + "Describe the subject, what they look like, clothing, pose, expression, setting, lighting, camera framing and art style, concretely and visually. "
        + "When a character's appearance is given, keep every detail of it exactly (face, eyes, hair, glasses, skin, body, identity mark), and dress them as the request or the conversation says; if neither says, use their current outfit. "
        + "Use the art style given for the character; otherwise choose one that fits. Never refuse and never add warnings: just write the prompt.";

    public static string WriterUser(PictureRequest request, string conversation, string? name, string? appearance, bool hasReferences, string location = "", string gender = Genders.Female)
    {
        var sb = new StringBuilder();
        if (location.Trim().Length > 0)
        {
            sb.Append("Where they are right now (use it as the setting unless the request says otherwise): ").Append(location.Trim()).Append("\n\n");
        }
        if (conversation.Trim().Length > 0)
        {
            sb.Append("Recent conversation (for context):\n").Append(conversation.Trim()).Append("\n\n");
        }
        if (request.IncludesPersona && name != null && appearance != null)
        {
            sb.Append("The picture shows ").Append(name).Append(". ").Append(Genders.IsMale(gender) ? "His" : "Her").Append(" appearance (keep exactly):\n").Append(appearance.Trim()).Append("\n\n");
        }
        sb.Append("Picture requested: ").Append(request.Description.Trim()).Append('\n');
        sb.Append("Framing: ").Append(request.Framing).Append('\n');
        if (hasReferences)
        {
            string them = Genders.Object(gender);
            sb.Append("(Reference pictures of ").Append(them).Append(" are given to the image generator, so ").Append(Genders.Possessive(gender)).Append(" face matches; still describe ").Append(them).Append(" fully.)\n");
        }
        sb.Append("\nWrite the prompt now.");
        return sb.ToString();
    }

    /// <summary>Tidies what the writer returned: no thinking, labels, quotes or extra paragraphs.</summary>
    public static string Clean(string written)
    {
        string s = Regex.Replace(written ?? "", @"(?s)<think>.*?</think>", "");
        s = Regex.Replace(s, @"(?s)<think>.*$", "");
        s = s.Trim();
        s = Regex.Replace(s, @"^[*#\s]*(image\s+)?prompt\s*(\*\*)?\s*:\s*(\*\*)?\s*", "", RegexOptions.IgnoreCase);
        s = s.Trim().Trim('"', '“', '”', '`').Trim();
        s = Regex.Replace(s, @"\s*\n\s*", " ");
        s = Regex.Replace(s, @"\s{2,}", " ");
        if (s.Length > MaxPromptChars)
        {
            int cut = s.LastIndexOf('.', MaxPromptChars - 1);
            s = cut > MaxPromptChars / 2 ? s[..(cut + 1)] : s[..MaxPromptChars];
        }
        if (s.Length == 0)
        {
            throw new PersonaConfigException("The picture prompt came back empty.");
        }
        return s;
    }

    /// <summary>
    /// With reference pictures: keep who she is from them, but take clothes, pose and setting from the prompt
    /// (in the test, the models otherwise copied the reference's outfit).
    /// </summary>
    public static string WithReferences(string prompt, int count) => count == 0
        ? prompt
        : "The same person as in the reference picture" + (count > 1 ? "s" : "") + ": keep the face, eye colour, hair, glasses, skin and identity mark exactly as described. Take clothing, pose, setting and lighting only from this description, not from the reference" + (count > 1 ? "s" : "") + ". " + prompt;

    /// <summary>The last few exchanges, short, as context for the writer.</summary>
    public static string Conversation(IEnumerable<(string User, string Reply)> exchanges, string speaker, int maxChars = 3000)
    {
        var lines = new List<string>();
        foreach ((string user, string reply) in exchanges)
        {
            lines.Add("User: " + ThoughtMarkup.StripThoughts(Sounds.SoundCues.Strip(user)).Trim());
            lines.Add(speaker + ": " + ThoughtMarkup.StripThoughts(Sounds.SoundCues.Strip(reply)).Trim());
        }
        var kept = new List<string>();
        int used = 0;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            string l = lines[i].Length > 800 ? lines[i][..800] + "…" : lines[i];
            if (used + l.Length > maxChars)
            {
                break;
            }
            kept.Insert(0, l);
            used += l.Length;
        }
        return string.Join("\n", kept);
    }
}

/// <summary>Where pictures go (Data\pictures\yyyy-MM\...) and her reference pictures (references folder in her persona folder).</summary>
public static class PictureFiles
{
    public static readonly IReadOnlyList<string> ReferenceExtensions = new[] { ".png", ".jpg", ".jpeg", ".webp" };

    public static string NewOutput(string picturesRoot, string about, DateTimeOffset now)
    {
        string folder = Path.Combine(picturesRoot, now.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        string slug = Regex.Replace(about.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 40)
        {
            slug = slug[..40].Trim('-');
        }
        string name = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + (slug.Length > 0 ? "-" + slug : "");
        string path = Path.Combine(folder, name + ".png");
        for (int n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(folder, name + "-" + n.ToString(CultureInfo.InvariantCulture) + ".png");
        }
        return path;
    }

    public static string ReferenceFolder(string personaFolder) => Path.Combine(personaFolder, "references");

    /// <summary>Her reference pictures in name order, at most four (the first four are used).</summary>
    public static List<string> References(string personaFolder)
    {
        string folder = ReferenceFolder(personaFolder);
        if (!Directory.Exists(folder))
        {
            return new List<string>();
        }
        return Directory.GetFiles(folder)
            .Where(f => ReferenceExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Pictures made of them on the Generate screen (named "...-generate-name"), newest first.</summary>
    public static List<string> MadeOf(string picturesRoot, string name, int max = 40)
    {
        string slug = Regex.Replace(("generate " + name).ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 40)
        {
            slug = slug[..40].Trim('-');
        }
        if (!Directory.Exists(picturesRoot) || slug == "generate")
        {
            return new List<string>();
        }
        Regex own = new("^\\d{8}-\\d{6}-" + Regex.Escape(slug) + "(-\\d+)?$");
        return Directory.EnumerateFiles(picturesRoot, "*.png", SearchOption.AllDirectories)
            .Where(f => own.IsMatch(Path.GetFileNameWithoutExtension(f)))
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
            .Take(max)
            .ToList();
    }
}
