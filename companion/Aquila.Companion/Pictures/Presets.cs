using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AquilaLocal.Companion.Pictures;

/// <summary>The three kinds of preset on the Generate screen.</summary>
public static class PresetKinds
{
    public const string Action = "action";
    public const string Outfit = "outfit";
    public const string Scene = "scene";

    public static readonly IReadOnlyList<string> All = new[] { Action, Outfit, Scene };

    public static bool IsKnown(string kind) => All.Contains(kind);

    public static string Label(string kind) => kind switch
    {
        Action => "Action",
        Outfit => "Outfit",
        _ => "Scene"
    };
}

/// <summary>
/// One preset: what it adds to the picture (Prompt), the shot it suits (actions only), what moves in a video
/// (actions only), and who it fits ("" anyone, or "female" / "male" for some outfits).
/// </summary>
public sealed record GenPreset(string Id, string Kind, string Name, string Prompt, string Framing = "", string Motion = "", string For = "", bool Custom = false)
{
    public bool Fits(string gender) => For.Length == 0 || For == gender;
}

/// <summary>
/// The presets: the built-in ones, plus the ones you add with Custom (Data\presets\presets.json). Each has a
/// small picture (Data\presets\tiles\&lt;id&gt;.png), made once on this computer by the picture engine.
/// </summary>
public sealed class PresetLibrary
{
    public const int MaxNameChars = 40;
    public const int MaxPromptChars = 300;
    public const int MaxCustom = 200;

    /// <summary>Tile pictures are drawn at this size (3:4, multiples of 64) and shown cropped.</summary>
    public static readonly (int Width, int Height) TileSize = (512, 640);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public PresetLibrary(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public string CustomPath => Path.Combine(Root, "presets.json");

    public string TilesFolder => Path.Combine(Root, "tiles");

    public string TilePath(string id) => Path.Combine(TilesFolder, id + ".png");

    /// <summary>A preset's tile; in a folder named after the tile style, so a style change draws them all again.</summary>
    public string PresetTilePath(string id) => Path.Combine(TilesFolder, "anime", id + ".png");

    public static readonly IReadOnlyList<GenPreset> BuiltIn = new List<GenPreset>
    {
        // Actions: what they're doing, the shot that suits it, and how it moves in a video.
        new("action-smile", PresetKinds.Action, "Smiling", "smiling warmly at the camera", "portrait", "smiles softly, blinks and tilts the head a little"),
        new("action-laugh", PresetKinds.Action, "Laughing", "laughing happily, eyes crinkled", "half body", "laughs, shoulders shaking gently, then smiles"),
        new("action-wave", PresetKinds.Action, "Waving hello", "waving hello with one hand, friendly smile", "half body", "waves hello with one hand and smiles"),
        new("action-kiss", PresetKinds.Action, "Blowing a kiss", "blowing a kiss toward the camera", "portrait", "blows a kiss toward the camera and smiles"),
        new("action-selfie", PresetKinds.Action, "Taking a selfie", "taking a selfie with a phone held at arm's length", "portrait", "holds up the phone, adjusts the angle and smiles"),
        new("action-coffee", PresetKinds.Action, "Drinking coffee", "holding a warm mug of coffee with both hands, taking a sip", "half body", "lifts the mug, takes a sip and lowers it with a content sigh"),
        new("action-read", PresetKinds.Action, "Reading", "reading a book, absorbed in it", "half body", "turns a page slowly, eyes following the lines"),
        new("action-cook", PresetKinds.Action, "Cooking", "cooking at a stove, stirring a pan", "half body", "stirs the pan and tastes from a wooden spoon"),
        new("action-guitar", PresetKinds.Action, "Playing guitar", "playing an acoustic guitar", "half body", "strums the guitar, fingers moving on the strings"),
        new("action-walk", PresetKinds.Action, "Walking", "walking toward the camera, relaxed stride", "full body", "walks toward the camera with a relaxed stride"),
        new("action-dance", PresetKinds.Action, "Dancing", "dancing freely, mid-spin, joyful", "full body", "dances and spins, hair and clothes swaying"),
        new("action-sit", PresetKinds.Action, "Sitting relaxed", "sitting relaxed with legs crossed", "full body", "shifts comfortably and glances toward the camera"),
        new("action-stretch", PresetKinds.Action, "Stretching", "stretching both arms overhead, eyes closed", "full body", "stretches both arms overhead and relaxes"),
        new("action-lie", PresetKinds.Action, "Daydreaming", "lying back, daydreaming, gazing upward", "full body", "breathes slowly, smiles faintly, eyes drifting"),
        new("action-shoulder", PresetKinds.Action, "Over the shoulder", "looking back over the shoulder at the camera", "half body", "turns the head to look back over the shoulder"),
        new("action-sleep", PresetKinds.Action, "Sleeping", "sleeping peacefully, head on a pillow", "half body", "breathes slowly in deep sleep"),

        // Outfits.
        new("outfit-jeans", PresetKinds.Outfit, "Jeans & T-shirt", "a plain white T-shirt and blue jeans"),
        new("outfit-sweater", PresetKinds.Outfit, "Cozy sweater", "an oversized cream knit sweater"),
        new("outfit-hoodie", PresetKinds.Outfit, "Hoodie", "a soft grey hoodie and joggers"),
        new("outfit-leather", PresetKinds.Outfit, "Leather jacket", "a black leather jacket over a dark top, dark jeans and boots"),
        new("outfit-suit", PresetKinds.Outfit, "Business suit", "a tailored navy business suit"),
        new("outfit-sport", PresetKinds.Outfit, "Workout clothes", "athletic workout clothes and running shoes"),
        new("outfit-pajamas", PresetKinds.Outfit, "Pajamas", "soft flannel pajamas"),
        new("outfit-winter", PresetKinds.Outfit, "Winter coat", "a warm wool winter coat, knit scarf and gloves"),
        new("outfit-rain", PresetKinds.Outfit, "Raincoat", "a yellow raincoat and rubber boots"),
        new("outfit-swim", PresetKinds.Outfit, "Swimwear", "swimwear for a day at the beach"),
        new("outfit-yukata", PresetKinds.Outfit, "Yukata", "a patterned summer yukata with an obi sash"),
        new("outfit-summer", PresetKinds.Outfit, "Summer dress", "a light floral summer dress", For: Genders.Female),
        new("outfit-gown", PresetKinds.Outfit, "Evening gown", "an elegant floor-length evening gown", For: Genders.Female),
        new("outfit-shirt", PresetKinds.Outfit, "Linen shirt", "a relaxed linen shirt with rolled sleeves and chinos", For: Genders.Male),
        new("outfit-tux", PresetKinds.Outfit, "Tuxedo", "a classic black tuxedo with a bow tie", For: Genders.Male),

        // Scenes.
        new("scene-living", PresetKinds.Scene, "Cozy living room", "a cozy living room with warm lamps, a soft sofa and plants"),
        new("scene-cafe", PresetKinds.Scene, "Coffee shop", "a sunny coffee shop with wooden tables and big windows"),
        new("scene-kitchen", PresetKinds.Scene, "Kitchen", "a bright home kitchen in the morning"),
        new("scene-bedroom", PresetKinds.Scene, "Bedroom, morning", "a calm bedroom in soft morning light through the curtains"),
        new("scene-library", PresetKinds.Scene, "Library", "a quiet old library with tall wooden shelves"),
        new("scene-beach", PresetKinds.Scene, "Beach at sunset", "a sandy beach at golden sunset, gentle waves"),
        new("scene-park", PresetKinds.Scene, "Autumn park", "a park in autumn with golden trees and fallen leaves"),
        new("scene-city", PresetKinds.Scene, "City at night", "a city street at night with neon signs and wet pavement"),
        new("scene-rooftop", PresetKinds.Scene, "Rooftop at dusk", "a rooftop terrace at dusk with string lights and a skyline"),
        new("scene-lake", PresetKinds.Scene, "Mountain lake", "a clear mountain lake with pine trees and peaks"),
        new("scene-snow", PresetKinds.Scene, "Snowy forest", "a snowy forest with soft falling snow"),
        new("scene-garden", PresetKinds.Scene, "Japanese garden", "a Japanese garden with a koi pond and maple trees"),
        new("scene-dinner", PresetKinds.Scene, "Candlelit dinner", "a candlelit restaurant table for two"),
        new("scene-studio", PresetKinds.Scene, "Photo studio", "a photo studio with a plain backdrop and soft lighting"),
    };

    /// <summary>Built-in presets first, then your own; one kind, or all with kind null.</summary>
    public List<GenPreset> Load(string? kind = null)
    {
        List<GenPreset> all = BuiltIn.Concat(LoadCustom()).ToList();
        return kind == null ? all : all.Where(p => p.Kind == kind).ToList();
    }

    public GenPreset? Find(string id) => id.Length == 0 ? null : Load().FirstOrDefault(p => p.Id == id);

    public List<GenPreset> LoadCustom()
    {
        if (!File.Exists(CustomPath))
        {
            return new List<GenPreset>();
        }
        List<GenPreset>? list;
        try
        {
            list = JsonSerializer.Deserialize<List<GenPreset>>(File.ReadAllText(CustomPath), Json);
        }
        catch (JsonException ex)
        {
            throw new PersonaConfigException($"Your presets file isn't valid JSON ({CustomPath}, line {ex.LineNumber + 1}): {ex.Message}", ex);
        }
        return (list ?? new List<GenPreset>()).Select(p => p with { Custom = true }).ToList();
    }

    /// <summary>Adds one of your own presets ("Custom"). Returns it; its tile is drawn separately.</summary>
    public GenPreset AddCustom(string kind, string name, string prompt, string framing = "", string motion = "")
    {
        if (!PresetKinds.IsKnown(kind))
        {
            throw new PersonaConfigException($"\"{kind}\" isn't a kind of preset.");
        }
        name = Regex.Replace(name ?? "", @"\s+", " ").Trim();
        prompt = Regex.Replace(prompt ?? "", @"\s+", " ").Trim();
        if (name.Length == 0)
        {
            throw new PersonaConfigException("Give the preset a short name.");
        }
        if (name.Length > MaxNameChars)
        {
            throw new PersonaConfigException($"Keep the name under {MaxNameChars} characters.");
        }
        if (prompt.Length == 0)
        {
            throw new PersonaConfigException("Describe what the preset should show.");
        }
        if (prompt.Length > MaxPromptChars)
        {
            throw new PersonaConfigException($"Keep the description under {MaxPromptChars} characters.");
        }
        if (framing.Length > 0 && !PictureRequest.Framings.Contains(framing))
        {
            framing = "";
        }
        List<GenPreset> custom = LoadCustom();
        if (custom.Count >= MaxCustom)
        {
            throw new PersonaConfigException($"You already have {MaxCustom} presets of your own. Remove one first.");
        }
        if (Load(kind).Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PersonaConfigException($"There's already a {PresetKinds.Label(kind).ToLowerInvariant()} called \"{name}\".");
        }
        string slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length == 0)
        {
            slug = "preset";
        }
        if (slug.Length > 24)
        {
            slug = slug[..24].Trim('-');
        }
        string id = $"custom-{kind}-{slug}";
        for (int n = 2; Load().Any(p => p.Id == id); n++)
        {
            id = $"custom-{kind}-{slug}-{n}";
        }
        GenPreset added = new(id, kind, name, prompt, kind == PresetKinds.Action ? framing : "", kind == PresetKinds.Action ? motion.Trim() : "", "", true);
        custom.Add(added);
        Save(custom);
        return added;
    }

    /// <summary>Removes one of your own presets and its tile. Built-in presets can't be removed.</summary>
    public void Remove(string id)
    {
        if (BuiltIn.Any(p => p.Id == id))
        {
            throw new PersonaConfigException("Built-in presets can't be removed.");
        }
        List<GenPreset> custom = LoadCustom();
        if (custom.RemoveAll(p => p.Id == id) == 0)
        {
            throw new PersonaConfigException("That preset wasn't found.");
        }
        Save(custom);
        string tile = PresetTilePath(id);
        if (File.Exists(tile))
        {
            File.Delete(tile);
        }
    }

    private void Save(List<GenPreset> custom)
    {
        Directory.CreateDirectory(Root);
        string temp = CustomPath + ".saving";
        File.WriteAllText(temp, JsonSerializer.Serialize(custom.Select(p => p with { Custom = false }).ToList(), Json));
        File.Move(temp, CustomPath, overwrite: true);
    }

    /// <summary>Presets whose small picture hasn't been made yet.</summary>
    public List<GenPreset> MissingTiles() => Load().Where(p => !File.Exists(PresetTilePath(p.Id))).ToList();

    /// <summary>
    /// The prompt for a preset's small picture: an adult model in casual clothes doing the action, a model
    /// wearing the outfit on a plain backdrop, or the place itself with nobody in it.
    /// </summary>
    public static string TilePrompt(GenPreset p)
    {
        // A woman unless the preset is for men only: most companions are women, and a vague "young adult"
        // let the picture model pick a man. Anime style to match how companions usually look.
        string person = p.For == Genders.Male ? "a young adult man" : "a young adult woman";
        return p.Kind switch
        {
            PresetKinds.Action => $"Anime illustration of {person} in simple casual clothes, {p.Prompt}. Clean line art, soft cel shading, soft light, simple softly blurred background, centered.",
            PresetKinds.Outfit => $"Anime illustration of {person} wearing {p.Prompt}, full body, standing, plain light grey background, clean line art, soft cel shading, even light.",
            _ => $"{Capital(p.Prompt)}. No people. Anime background art, atmospheric wide view, clean line art, soft light, detailed."
        };
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

/// <summary>What you picked on the Generate screen: a companion, up to one preset of each kind, extra details and a shot.</summary>
public sealed record GenerateRecipe(GenPreset? Action, GenPreset? Outfit, GenPreset? Scene, string Extra, string Framing = "")
{
    public const int MaxExtraChars = 800;

    /// <summary>The shot: the one you chose, or the action's, or half body.</summary>
    public string Shot => Framing.Length > 0 && PictureRequest.Framings.Contains(Framing) ? Framing : (Action?.Framing.Length > 0 ? Action.Framing : "half body");

    public bool IsEmpty => Action == null && Outfit == null && Scene == null && Extra.Trim().Length == 0;

    /// <summary>The picture request in words, for the prompt writer: who, doing what, wearing what, where, and anything extra.</summary>
    public string Describe(string name, string gender)
    {
        string subject = Genders.Subject(gender);
        StringBuilder sb = new();
        sb.Append(name).Append(", an adult");
        sb.Append(Action != null ? ", " + Action.Prompt : ", looking natural and relaxed").Append('.');
        if (Outfit != null)
        {
            sb.Append(' ').Append(Capital(subject)).Append(" is wearing ").Append(Outfit.Prompt).Append(" (instead of ").Append(Genders.Possessive(gender)).Append(" usual outfit).");
        }
        if (Scene != null)
        {
            sb.Append(" Setting: ").Append(Scene.Prompt).Append('.');
        }
        string extra = Extra.Trim();
        if (extra.Length > 0)
        {
            sb.Append(" Also: ").Append((extra.Length > MaxExtraChars ? extra[..MaxExtraChars] : extra).TrimEnd());
            if (!".!?".Contains(sb[^1]))
            {
                sb.Append('.');
            }
        }
        return sb.ToString();
    }

    public PictureRequest Request(string name, string gender) => new(Describe(name, gender), true, Shot);

    /// <summary>
    /// The image prompt written directly, for when the chat model isn't loaded to write one: their appearance,
    /// then the request. The appearance should leave out their usual outfit when an outfit was picked.
    /// </summary>
    public string DirectPrompt(string name, string gender, string appearance)
    {
        string prompt = "Photo of " + Describe(name, gender) + " Appearance: " + appearance.Trim().TrimEnd('.') + ". Framing: " + Shot + ". Realistic photography, natural light, detailed.";
        return prompt.Length > PicturePrompt.MaxPromptChars ? prompt[..PicturePrompt.MaxPromptChars] : prompt;
    }

    /// <summary>What moves in a video: what you typed, or the action's motion, or a gentle default.</summary>
    public string Motion(string typed)
    {
        string m = typed.Trim();
        if (m.Length > 0)
        {
            return m.Length > VideoRequest.MaxMotionChars ? m[..VideoRequest.MaxMotionChars] : m;
        }
        return Action?.Motion.Length > 0 ? Action.Motion : "breathes and blinks naturally, a slight smile, gentle natural movement";
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

/// <summary>Their appearance in words, from their persona file, for companions whose card isn't set up yet.</summary>
public static class SeedCaption
{
    public static string For(string name, AppearanceSeed seed, bool withOutfit)
    {
        List<string> parts = seed.Traits.Where(t => t.Text.Trim().Length > 0).Select(t => t.Name.Trim() + ": " + t.Text.Trim().TrimEnd('.')).ToList();
        if (withOutfit && seed.Wardrobe.Outfit.Trim().Length > 0)
        {
            parts.Add("wearing " + ContextInjector.WardrobeText(seed.Wardrobe));
        }
        if (seed.IdentityAnchor.Trim().Length > 0)
        {
            parts.Add("always visible: " + seed.IdentityAnchor.Trim());
        }
        return parts.Count == 0 ? name : name + ", " + string.Join(", ", parts);
    }

    /// <summary>Their appearance card in words; without their current outfit when another outfit was picked.</summary>
    public static string ForCard(string name, AppearanceCard card, bool withOutfit)
    {
        if (withOutfit)
        {
            return AppearanceCaption.For(name, card);
        }
        if (card.Traits.Count == 0 || card.IdentityAnchor.Trim().Length == 0)
        {
            throw new AppearanceCardUnavailableException(card.PersonaId, "the card is incomplete, so no caption can be made.");
        }
        return name.Trim() + ", " + string.Join(", ", card.Traits.Select(t => t.Name.Trim() + ": " + t.Text.Trim().TrimEnd('.'))) + ". Identity anchor, always visible: " + card.IdentityAnchor.Trim();
    }
}
