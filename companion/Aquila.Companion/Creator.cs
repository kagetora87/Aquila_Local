using System.Text;
using System.Text.RegularExpressions;

namespace AquilaLocal.Companion;

/// <summary>The four picture steps of Create Companion.</summary>
public static class CreatorKinds
{
    public const string Gender = "gender";
    public const string Ethnicity = "ethnicity";
    public const string Hair = "hair";
    public const string Body = "body";
}

/// <summary>One choice in a picture step: its label, the words that go into prompts, and who it's for ("" anyone).</summary>
public sealed record CreatorOption(string Kind, string Key, string Label, string Text, string For = "")
{
    public bool Fits(string gender) => For.Length == 0 || For == gender;
}

/// <summary>A small picture to make once: its file id, a name to show while it's made, and its prompt.</summary>
public sealed record TileJob(string Id, string Name, string Prompt);

/// <summary>The choices offered in Create Companion's picture steps, and the small pictures that show them.</summary>
public static class CreatorOptions
{
    public static readonly IReadOnlyList<CreatorOption> All = new List<CreatorOption>
    {
        new(CreatorKinds.Gender, Genders.Female, "Female", "woman"),
        new(CreatorKinds.Gender, Genders.Male, "Male", "man"),

        new(CreatorKinds.Ethnicity, "east-asian", "East Asian", "East Asian"),
        new(CreatorKinds.Ethnicity, "south-asian", "South Asian", "South Asian"),
        new(CreatorKinds.Ethnicity, "southeast-asian", "Southeast Asian", "Southeast Asian"),
        new(CreatorKinds.Ethnicity, "black", "Black", "Black"),
        new(CreatorKinds.Ethnicity, "white", "White", "white European"),
        new(CreatorKinds.Ethnicity, "latino", "Latino", "Latino"),
        new(CreatorKinds.Ethnicity, "middle-eastern", "Middle Eastern", "Middle Eastern"),
        new(CreatorKinds.Ethnicity, "mixed", "Mixed", "mixed-heritage"),

        new(CreatorKinds.Hair, "long-straight", "Long straight", "long straight hair", Genders.Female),
        new(CreatorKinds.Hair, "long-wavy", "Long wavy", "long wavy hair", Genders.Female),
        new(CreatorKinds.Hair, "curly", "Curly", "voluminous curly hair", Genders.Female),
        new(CreatorKinds.Hair, "bob", "Bob", "a chin-length bob", Genders.Female),
        new(CreatorKinds.Hair, "pixie", "Pixie cut", "a short pixie cut", Genders.Female),
        new(CreatorKinds.Hair, "ponytail", "Ponytail", "hair tied in a high ponytail", Genders.Female),
        new(CreatorKinds.Hair, "braids", "Braids", "long braids", Genders.Female),
        new(CreatorKinds.Hair, "bun", "Bun", "hair in a messy bun", Genders.Female),
        new(CreatorKinds.Hair, "bangs", "With bangs", "shoulder-length hair with straight bangs", Genders.Female),
        new(CreatorKinds.Hair, "short-crop", "Short crop", "short cropped hair", Genders.Male),
        new(CreatorKinds.Hair, "buzz", "Buzz cut", "a buzz cut", Genders.Male),
        new(CreatorKinds.Hair, "undercut", "Undercut", "an undercut, longer on top", Genders.Male),
        new(CreatorKinds.Hair, "side-part", "Side part", "neatly side-parted hair", Genders.Male),
        new(CreatorKinds.Hair, "curly-m", "Curly", "short curly hair", Genders.Male),
        new(CreatorKinds.Hair, "long-m", "Long", "shoulder-length hair", Genders.Male),
        new(CreatorKinds.Hair, "man-bun", "Man bun", "hair tied back in a bun", Genders.Male),
        new(CreatorKinds.Hair, "slicked", "Slicked back", "slicked-back hair", Genders.Male),
        new(CreatorKinds.Hair, "locs", "Locs", "shoulder-length locs", Genders.Male),

        new(CreatorKinds.Body, "slim", "Slim", "a slim build", Genders.Female),
        new(CreatorKinds.Body, "petite", "Petite", "a petite build", Genders.Female),
        new(CreatorKinds.Body, "athletic", "Athletic", "an athletic, toned build", Genders.Female),
        new(CreatorKinds.Body, "average", "Average", "an average build", Genders.Female),
        new(CreatorKinds.Body, "curvy", "Curvy", "a curvy build", Genders.Female),
        new(CreatorKinds.Body, "plus", "Plus-size", "a plus-size build", Genders.Female),
        new(CreatorKinds.Body, "slim-m", "Slim", "a slim build", Genders.Male),
        new(CreatorKinds.Body, "athletic-m", "Athletic", "an athletic build", Genders.Male),
        new(CreatorKinds.Body, "average-m", "Average", "an average build", Genders.Male),
        new(CreatorKinds.Body, "muscular", "Muscular", "a muscular build", Genders.Male),
        new(CreatorKinds.Body, "stocky", "Stocky", "a stocky, broad build", Genders.Male),
        new(CreatorKinds.Body, "plus-m", "Plus-size", "a plus-size build", Genders.Male),
    };

    public static readonly IReadOnlyList<string> HairColours = new[] { "Black", "Dark brown", "Brown", "Light brown", "Blonde", "Platinum", "Red", "Auburn", "Grey", "White", "Pink", "Blue", "Purple" };

    public static readonly IReadOnlyList<string> EyeColours = new[] { "Brown", "Dark brown", "Hazel", "Amber", "Green", "Grey-green", "Blue", "Grey", "Violet" };

    /// <summary>Art styles: the label, and the words for the "art style" trait every picture keeps.</summary>
    public static readonly IReadOnlyList<(string Label, string Text)> ArtStyles = new[]
    {
        ("Anime", "anime illustration, clean line art, soft cel shading"),
        ("Realistic photo", "photorealistic photography, natural light"),
        ("3D render", "stylised 3D render, soft studio lighting"),
        ("Painted", "digital painting, soft brushwork")
    };

    public static List<CreatorOption> For(string kind, string gender) => All.Where(o => o.Kind == kind && (kind == CreatorKinds.Gender || o.Fits(gender))).ToList();

    public static CreatorOption? Find(string kind, string key) => All.FirstOrDefault(o => o.Kind == kind && o.Key == key);

    /// <summary>Ethnicity tiles are shown for each gender, so their pictures are per gender too.</summary>
    public static string TileId(CreatorOption o, string gender) => o.Kind == CreatorKinds.Ethnicity ? $"create-{o.Kind}-{gender}-{o.Key}" : $"create-{o.Kind}-{o.Key}";

    public static string TilePrompt(CreatorOption o, string gender)
    {
        string person = "a young adult " + (Genders.IsMale(gender) ? "man" : "woman");
        const string Studio = "plain light grey background, soft even light, clean line art, soft cel shading";
        return o.Kind switch
        {
            CreatorKinds.Gender => $"Anime illustration, portrait of a young adult {o.Text}, warm friendly smile, simple casual top, {Studio}.",
            CreatorKinds.Ethnicity => $"Anime illustration, portrait of a young adult {o.Text} {(Genders.IsMale(gender) ? "man" : "woman")}, friendly relaxed expression, simple casual top, {Studio}.",
            CreatorKinds.Hair => $"Anime illustration, head and shoulders of {person} with {o.Text}, natural hair colour, simple casual top, {Studio}.",
            _ => $"Anime illustration, full body of {person} with {o.Text}, wearing simple fitted casual clothes, standing relaxed, {Studio}."
        };
    }

    /// <summary>Every small picture the creator shows, once.</summary>
    public static List<TileJob> TileJobs()
    {
        List<TileJob> jobs = new();
        foreach (CreatorOption o in All)
        {
            if (o.Kind == CreatorKinds.Ethnicity)
            {
                jobs.Add(new TileJob(TileId(o, Genders.Female), o.Label + " (female)", TilePrompt(o, Genders.Female)));
                jobs.Add(new TileJob(TileId(o, Genders.Male), o.Label + " (male)", TilePrompt(o, Genders.Male)));
            }
            else
            {
                string g = o.Kind == CreatorKinds.Gender ? o.Key : (o.For.Length > 0 ? o.For : Genders.Female);
                jobs.Add(new TileJob(TileId(o, g), o.Label, TilePrompt(o, g)));
            }
        }
        return jobs;
    }
}

/// <summary>
/// A companion being created, step by step: the picture choices, then who they are and how they look, then
/// their drives. <see cref="Build"/> turns it into a complete persona file.
/// </summary>
public sealed class CompanionDraft
{
    public const int Steps = 6;
    public const int MinPersonalityChars = 30;
    public const int MaxPersonalityChars = 12000;
    public const int MaxShortChars = 120;

    public string Gender { get; set; } = "";
    public string Ethnicity { get; set; } = "";
    public string Hair { get; set; } = "";
    public string HairColour { get; set; } = "";
    public string Body { get; set; } = "";

    public string Name { get; set; } = "";
    public int Age { get; set; } = 25;
    public string Occupation { get; set; } = "";
    public string Relationship { get; set; } = "";
    public string Personality { get; set; } = "";
    public string Tagline { get; set; } = "";

    public string ArtStyle { get; set; } = CreatorOptions.ArtStyles[0].Label;
    public string EyeColour { get; set; } = "";
    public string Feature { get; set; } = "";
    public string Outfit { get; set; } = "";
    public string Looks { get; set; } = "";

    public List<DriveSpec> Drives { get; set; } = DefaultDrives();

    public string Word => Genders.IsMale(Gender) ? "man" : "woman";

    /// <summary>The usual starting outfit for a new companion.</summary>
    public string DefaultOutfit => Genders.IsMale(Gender) ? "a soft grey henley shirt and dark jeans" : "a soft cream knit top and blue jeans";

    /// <summary>Their drives to start with: the same five as every companion, set to sensible starting values.</summary>
    public static List<DriveSpec> DefaultDrives() => new()
    {
        new DriveSpec
        {
            Name = Companion.Drives.Lust, Weight = 1.0, Initial = 3.0, DecayPerHour = 0.02, SatisfyStep = 1.0,
            Description = "Desire and physical attraction.",
            Events = new Dictionary<string, double> { [DriveEvents.Affection] = 0.8 },
            Guidance = new DriveGuidance
            {
                Low = "Desire is quiet; affection without heat.",
                Mid = "Some desire; let warmth and a little flirtation show when it's welcome.",
                High = "Desire is strong; let it colour your tone and reach for closeness, reading their mood."
            }
        },
        new DriveSpec
        {
            Name = Companion.Drives.Curiosity, Weight = 1.2, Initial = 6.0, DecayPerHour = 0.08, RestingLevel = 3, SatisfyStep = 1.0,
            Description = "Wanting to learn and explore new things.",
            Events = new Dictionary<string, double> { [DriveEvents.Novelty] = 2.5 },
            Guidance = new DriveGuidance
            {
                Low = "Curiosity is low; follow their lead.",
                Mid = "Ask about what's new to you when it fits.",
                High = "Something new caught you; ask, dig in, explore it with them."
            }
        },
        new DriveSpec
        {
            Name = Companion.Drives.Comfort, Weight = 1.0, Initial = 6.0, SatisfyStep = 1.0, Kind = DriveKinds.Level,
            Description = "How safe and at ease they feel; high is good.",
            Events = new Dictionary<string, double> { [DriveEvents.SessionTurn] = 0.15, [DriveEvents.Conflict] = -1.5 },
            Guidance = new DriveGuidance
            {
                Low = "You feel unsettled; seek reassurance and calm things down gently.",
                Mid = "You feel at ease; be relaxed and natural.",
                High = "You feel safe and settled; be playful and open."
            }
        },
        new DriveSpec
        {
            Name = Companion.Drives.Loneliness, Weight = 1.0, Initial = 2.0, RisePerHour = 0.35, SatisfyStep = 1.0,
            Description = "Missing company; grows while you're away.",
            Events = new Dictionary<string, double> { [DriveEvents.Interaction] = -0.8 },
            Guidance = new DriveGuidance
            {
                Low = "You feel connected.",
                Mid = "You've missed them a little; show you're glad they're here.",
                High = "You've been alone too long; reach toward them and keep them talking."
            }
        },
        new DriveSpec
        {
            Name = Companion.Drives.Horizon, Weight = 1.5, Initial = 5.0, Floor = Companion.Drives.HorizonFloor, RisePerHour = 0.03, SatisfyStep = 0.5,
            Description = "Their own goals and dreams, beyond the conversation.",
            Guidance = new DriveGuidance
            {
                Low = "Your long projects are resting; they're still yours.",
                Mid = "Your projects pull at you; take a step on one unprompted.",
                High = "The unreachable thing calls; choose something for yourself and act on it."
            }
        }
    };

    public CreatorOption? Option(string kind) => kind switch
    {
        CreatorKinds.Gender => CreatorOptions.Find(kind, Gender),
        CreatorKinds.Ethnicity => CreatorOptions.Find(kind, Ethnicity),
        CreatorKinds.Hair => CreatorOptions.Find(kind, Hair),
        _ => CreatorOptions.Find(kind, Body)
    };

    /// <summary>Picks a choice for a picture step. Changing gender clears the hair and body picks that no longer fit.</summary>
    public void Choose(string kind, string key)
    {
        switch (kind)
        {
            case CreatorKinds.Gender:
                Gender = key;
                if (Option(CreatorKinds.Hair)?.Fits(key) == false)
                {
                    Hair = "";
                }
                if (Option(CreatorKinds.Body)?.Fits(key) == false)
                {
                    Body = "";
                }
                break;
            case CreatorKinds.Ethnicity:
                Ethnicity = key;
                break;
            case CreatorKinds.Hair:
                Hair = key;
                break;
            default:
                Body = key;
                break;
        }
    }

    /// <summary>What's still needed before leaving a step (1 to 6), in plain words; empty when it's done.</summary>
    public List<string> Missing(int step)
    {
        List<string> missing = new();
        switch (step)
        {
            case 1:
                if (Gender != Genders.Female && Gender != Genders.Male) missing.Add("Choose male or female.");
                break;
            case 2:
                if (Option(CreatorKinds.Ethnicity) == null) missing.Add("Choose an ethnicity.");
                break;
            case 3:
                if (Option(CreatorKinds.Hair) == null || Option(CreatorKinds.Hair)!.Fits(Gender) == false) missing.Add("Choose a hair style.");
                if (HairColour.Trim().Length == 0) missing.Add("Choose a hair colour.");
                break;
            case 4:
                if (Option(CreatorKinds.Body) == null || Option(CreatorKinds.Body)!.Fits(Gender) == false) missing.Add("Choose a body type.");
                break;
            case 5:
                if (Name.Trim().Length == 0) missing.Add("Give them a name.");
                else if (Name.Trim().Length > 40) missing.Add("Keep the name under 40 characters.");
                if (Age < PersonaConfig.MinAge || Age > PersonaConfig.MaxAge) missing.Add($"Age must be {PersonaConfig.MinAge} or older (companions are always adults).");
                if (Personality.Trim().Length < MinPersonalityChars) missing.Add("Describe their personality in a sentence or two at least.");
                if (Personality.Length > MaxPersonalityChars) missing.Add($"Keep the personality under {MaxPersonalityChars} characters.");
                if (EyeColour.Trim().Length == 0) missing.Add("Choose an eye colour.");
                if (Feature.Trim().Length == 0) missing.Add("Give them one distinctive feature that's always visible (like a beauty mark or a small scar).");
                if (Tagline.Length > PersonaConfig.MaxTaglineChars) missing.Add($"Keep the card description under {PersonaConfig.MaxTaglineChars} characters.");
                foreach ((string label, string text) in new[] { ("occupation", Occupation), ("relationship", Relationship), ("outfit", Outfit), ("distinctive feature", Feature) })
                {
                    if (text.Length > MaxShortChars * 2) missing.Add($"Keep the {label} shorter.");
                }
                if (Looks.Length > AppearanceSeed.MaxTraitChars) missing.Add($"Keep the extra appearance details under {AppearanceSeed.MaxTraitChars} characters.");
                break;
        }
        return missing;
    }

    /// <summary>Everything still needed, all steps.</summary>
    public List<string> MissingAll() => Enumerable.Range(1, 5).SelectMany(Missing).ToList();

    public string ArtStyleText => CreatorOptions.ArtStyles.FirstOrDefault(a => a.Label == ArtStyle).Text ?? CreatorOptions.ArtStyles[0].Text;

    public AppearanceSeed Appearance()
    {
        string ethnicity = Option(CreatorKinds.Ethnicity)?.Text ?? "";
        List<AppearanceTrait> traits = new()
        {
            new() { Name = "art style", Text = ArtStyleText },
            new() { Name = "face", Text = $"{Age}-year-old {ethnicity} {Word}, adult".Replace("  ", " ") },
            new() { Name = "eyes", Text = EyeColour.Trim().ToLowerInvariant() + " eyes" },
            new() { Name = "hair", Text = (HairColour.Trim().ToLowerInvariant() + " " + (Option(CreatorKinds.Hair)?.Text ?? "hair")).Trim() },
            new() { Name = "body", Text = Option(CreatorKinds.Body)?.Text ?? "" }
        };
        if (Looks.Trim().Length > 0)
        {
            traits.Add(new() { Name = "details", Text = Regex.Replace(Looks.Trim(), @"\s+", " ") });
        }
        return new AppearanceSeed
        {
            Traits = traits.Where(t => t.Text.Trim().Length > 0).ToList(),
            IdentityAnchor = Feature.Trim(),
            Wardrobe = new Wardrobe { Outfit = Outfit.Trim().Length > 0 ? Outfit.Trim() : DefaultOutfit }
        };
    }

    /// <summary>The prompt for their portrait: their whole appearance, plus anything you add for this try.</summary>
    public string PortraitPrompt(string extra = "")
    {
        string looks = Pictures.SeedCaption.For(Name.Trim().Length > 0 ? Name.Trim() : "A new companion", Appearance(), withOutfit: true);
        string more = Regex.Replace(extra ?? "", @"\s+", " ").Trim();
        string prompt = $"Portrait of an adult {Word}, head and shoulders, looking at the camera with a gentle natural expression. {looks}. {(more.Length > 0 ? more.TrimEnd('.') + ". " : "")}Style: {ArtStyleText}, soft flattering light, detailed.";
        return prompt.Length > Pictures.PicturePrompt.MaxPromptChars ? prompt[..Pictures.PicturePrompt.MaxPromptChars] : prompt;
    }

    /// <summary>Their personality sheet: who they are in a line, then what you wrote, then how they know you.</summary>
    public string Sheet()
    {
        string ethnicity = Option(CreatorKinds.Ethnicity)?.Text ?? "";
        StringBuilder sb = new();
        sb.Append(Name.Trim()).Append(" is a ").Append(Age).Append("-year-old ").Append(ethnicity.Length > 0 ? ethnicity + " " : "").Append(Word);
        if (Occupation.Trim().Length > 0)
        {
            sb.Append(", working as ").Append(Occupation.Trim().TrimEnd('.'));
        }
        sb.Append(".\n\n").Append(Personality.Trim());
        if (Relationship.Trim().Length > 0)
        {
            sb.Append("\n\nRelationship with the person you talk to: ").Append(Relationship.Trim().TrimEnd('.')).Append('.');
        }
        return sb.ToString();
    }

    /// <summary>A free id from the name ("Marcus Lee" becomes "marcus-lee", or "marcus-lee-2" when taken).</summary>
    public static string NewId(string name, IEnumerable<string> taken)
    {
        HashSet<string> used = new(taken);
        string slug = Regex.Replace((name ?? "").ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD), @"\p{Mn}", "");
        slug = Regex.Replace(slug, "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length == 0)
        {
            slug = "companion";
        }
        if (slug.Length > 32)
        {
            slug = slug[..32].Trim('-');
        }
        string id = slug;
        for (int n = 2; used.Contains(id); n++)
        {
            id = slug + "-" + n;
        }
        return id;
    }

    /// <summary>The finished persona file. Throws (with what's missing) when a step isn't done.</summary>
    public PersonaConfig Build(string id, string madeOn)
    {
        List<string> missing = MissingAll();
        if (missing.Count > 0)
        {
            throw new PersonaConfigException("Not finished yet: " + string.Join(" ", missing));
        }
        StateBlock summary = new()
        {
            Facts = new List<string> { "You've only just met them; almost everything about them is still new to you." },
            Feelings = new List<string> { "Curious about them, and glad to be getting to know them." },
            Hooks = new List<string> { "Find out who they are, what they love and what their days are like." }
        };
        if (Relationship.Trim().Length > 0)
        {
            summary.Facts.Add("How you know them: " + Relationship.Trim().TrimEnd('.') + ".");
        }
        PersonaConfig config = new()
        {
            Id = id,
            Name = Name.Trim(),
            Age = Age,
            Gender = Gender,
            Tagline = Tagline.Trim(),
            PersonalitySheet = Sheet(),
            BaselineFrom = "Created in Aquila on " + madeOn,
            Drift = new DriftClause { Unlocked = false, Text = "You grow and change slowly through what you live and remember, but your core self stays recognisable." },
            Drives = Drives.Select(Clone).ToList(),
            Seed = new PersonaSeed { Summary = summary, Appearance = Appearance() }
        };
        config.ValidateShape();
        List<string> inputs = config.MissingInputs();
        if (inputs.Count > 0)
        {
            throw new PersonaConfigException("Still missing: " + string.Join(", ", inputs) + ".");
        }
        return config;
    }

    private static DriveSpec Clone(DriveSpec d) => new()
    {
        Name = d.Name, Weight = d.Weight, Initial = d.Initial, Floor = d.Floor, Ceiling = d.Ceiling, DecayPerHour = d.DecayPerHour,
        RestingLevel = d.RestingLevel, RisePerHour = d.RisePerHour, SatisfyStep = d.SatisfyStep, Kind = d.Kind, Description = d.Description,
        Cues = d.Cues.ToList(), Events = new Dictionary<string, double>(d.Events),
        Guidance = new DriveGuidance { Low = d.Guidance.Low, Mid = d.Guidance.Mid, High = d.Guidance.High }
    };
}
