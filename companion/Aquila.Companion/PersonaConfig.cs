using System.Text.Json;
using System.Text.Json.Serialization;

namespace AquilaLocal.Companion;

public static class Drives
{
    public const string Lust = "lust";
    public const string Curiosity = "curiosity";
    public const string Comfort = "comfort";
    public const string Loneliness = "loneliness";
    public const string Horizon = "horizon";

    /// <summary>Horizon is a floor, not a wave: it never goes below this.</summary>
    public const double HorizonFloor = 3.0;

    public const double Min = 0.0;
    public const double Max = 10.0;

    /// <summary>The five drives Aquila started with. A persona may rename, replace or add to them.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Lust, Curiosity, Comfort, Loneliness, Horizon };

    /// <summary>At most this many drives, so her inner-state block stays short.</summary>
    public const int MaxDrives = 8;

    public const int MaxCues = 40;

    public const int MaxDescriptionChars = 200;

    /// <summary>A drive name: 1 to 24 lowercase letters, digits or dashes, starting with a letter (one word, so /tune can name it).</summary>
    public static bool IsValidName(string name) => System.Text.RegularExpressions.Regex.IsMatch(name ?? "", "^[a-z][a-z0-9-]{0,23}$");
}

/// <summary>What a drive's number means.</summary>
public static class DriveKinds
{
    /// <summary>A need, like hunger: the number is how much she wants it. Satisfying it lowers it.</summary>
    public const string Need = "need";

    /// <summary>A feeling level, like comfort: the number is how much of it she has. Satisfying it raises it, and it presses on her when it's low.</summary>
    public const string Level = "level";
}

/// <summary>Event names a user message can trigger. Each drive says in its config how much an event moves it.</summary>
public static class DriveEvents
{
    /// <summary>Every user message.</summary>
    public const string Interaction = "interaction";
    /// <summary>Every turn inside the same chat session, so comfort grows with session length.</summary>
    public const string SessionTurn = "session_turn";
    /// <summary>The user message is hostile or argumentative.</summary>
    public const string Conflict = "conflict";
    /// <summary>The user message is unlike anything in the persona's memory.</summary>
    public const string Novelty = "novelty";
    /// <summary>The user message is affectionate or flirtatious.</summary>
    public const string Affection = "affection";

    public static readonly IReadOnlyList<string> All = new[] { Interaction, SessionTurn, Conflict, Novelty, Affection };
}

public sealed class DriveGuidance
{
    /// <summary>Used below 3.5.</summary>
    public string Low { get; set; } = "";
    /// <summary>Used from 3.5 up to 7.</summary>
    public string Mid { get; set; } = "";
    /// <summary>Used from 7 up.</summary>
    public string High { get; set; } = "";

    public string For(double value) => value < 3.5 ? Low : value < 7.0 ? Mid : High;
}

public sealed class DriveSpec
{
    public string Name { get; set; } = "";

    /// <summary>How much this drive matters when choosing the top drive. Null means "still a placeholder".</summary>
    public double? Weight { get; set; }

    /// <summary>Starting value when the persona is first set up. Null means "still a placeholder".</summary>
    public double? Initial { get; set; }

    public double Floor { get; set; } = Drives.Min;

    public double Ceiling { get; set; } = Drives.Max;

    /// <summary>Fraction of the distance to <see cref="RestingLevel"/> lost per hour (0.02 = 2 % per hour).</summary>
    public double DecayPerHour { get; set; }

    public double RestingLevel { get; set; }

    /// <summary>Linear rise per elapsed hour (idle pull), applied before decay.</summary>
    public double RisePerHour { get; set; }

    /// <summary>How far one satisfied turn moves the drive (down for a need, up for a level).</summary>
    public double SatisfyStep { get; set; } = 1.0;

    /// <summary>"need" (default: satisfying lowers it) or "level" (satisfying raises it; it presses when low).</summary>
    public string Kind { get; set; } = DriveKinds.Need;

    /// <summary>What this drive is, in plain words (for you; shown in the editor).</summary>
    public string Description { get; set; } = "";

    /// <summary>Words or phrases in her reply that show she acted on this drive (added to the built-in ones for the five original drives).</summary>
    public List<string> Cues { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLevel => Kind == DriveKinds.Level;

    /// <summary>Event name to additive change, for example {"novelty": 2.5}.</summary>
    public Dictionary<string, double> Events { get; set; } = new();

    public DriveGuidance Guidance { get; set; } = new();
}

public sealed class DriftClause
{
    public bool Unlocked { get; set; }

    /// <summary>Shown to the persona verbatim in her system prompt.</summary>
    public string Text { get; set; } = "";
}

public sealed class MemorySettings
{
    public int SummarizeEveryTurns { get; set; } = 20;
    public int KeepVerbatimTurns { get; set; } = 10;
    public int SummaryTokenCap { get; set; } = 2000;
    public int RetrieveTopK { get; set; } = 5;
}

public sealed class StateBlock
{
    public List<string> Facts { get; set; } = new();
    public List<string> Feelings { get; set; } = new();
    public List<string> Hooks { get; set; } = new();

    [JsonIgnore]
    public bool IsEmpty => Facts.Count == 0 && Feelings.Count == 0 && Hooks.Count == 0;

    public string Render()
    {
        if (IsEmpty)
        {
            return "";
        }
        var sb = new System.Text.StringBuilder();
        void Layer(string title, List<string> items)
        {
            sb.Append(title).Append(":\n");
            foreach (string item in items)
            {
                sb.Append("- ").Append(item.Trim()).Append('\n');
            }
        }
        Layer("Facts", Facts);
        Layer("Feelings", Feelings);
        Layer("Hooks", Hooks);
        return sb.ToString().TrimEnd();
    }

    /// <summary>Drops the oldest items, hooks first and facts last, until the rendered block fits.</summary>
    public StateBlock CappedTo(int maxTokens)
    {
        var copy = new StateBlock { Facts = Facts.ToList(), Feelings = Feelings.ToList(), Hooks = Hooks.ToList() };
        while (TokenEstimate.Of(copy.Render()) > maxTokens)
        {
            List<string> victim = copy.Hooks.Count > 0 && copy.Hooks.Count >= copy.Feelings.Count / 2 ? copy.Hooks
                : copy.Feelings.Count > 0 && copy.Feelings.Count >= copy.Facts.Count / 2 ? copy.Feelings
                : copy.Facts.Count > 0 ? copy.Facts
                : copy.Feelings.Count > 0 ? copy.Feelings
                : copy.Hooks;
            if (victim.Count == 0)
            {
                break;
            }
            victim.RemoveAt(0);
        }
        return copy;
    }
}

public sealed class PersonaSeed
{
    /// <summary>The summary block written into the persona's state the first time she is set up.</summary>
    public StateBlock Summary { get; set; } = new();

    /// <summary>Her canonical appearance card, written into her state the first time she is set up.</summary>
    public AppearanceSeed Appearance { get; set; } = new();
}

/// <summary>One permanent trait, such as eyes or hair. Locked once she is set up.</summary>
public sealed class AppearanceTrait
{
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>What she is wearing. Changes are logged, never overwritten.</summary>
public sealed class Wardrobe
{
    public string Outfit { get; set; } = "";
    public string Color { get; set; } = "";
    public string Fabric { get; set; } = "";
    public string Condition { get; set; } = "";
}

/// <summary>The canonical appearance card as seed data: permanent traits, the identity anchor, and her starting wardrobe.</summary>
public sealed class AppearanceSeed
{
    public const int MaxTraitChars = 400;

    public List<AppearanceTrait> Traits { get; set; } = new();

    /// <summary>The one detail every picture of her must show (such as a beauty mark). Appended to every image caption.</summary>
    public string IdentityAnchor { get; set; } = "";

    public Wardrobe Wardrobe { get; set; } = new();

    public bool IsComplete =>
        Traits.Count > 0
        && Traits.All(t => !string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(t.Text))
        && !string.IsNullOrWhiteSpace(IdentityAnchor)
        && !string.IsNullOrWhiteSpace(Wardrobe.Outfit)
        && !Text().Contains(PersonaConfig.Placeholder);

    private string Text() => string.Join("\n", Traits.Select(t => t.Name + t.Text)) + IdentityAnchor + Wardrobe.Outfit + Wardrobe.Color + Wardrobe.Fabric + Wardrobe.Condition;
}

/// <summary>A persona's config file (persona.json): personality sheet, drive profile, drift clause.</summary>
public sealed class PersonaConfig
{
    public const string Placeholder = "[[PLACEHOLDER";

    public int Schema { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string PersonalitySheet { get; set; } = "";
    public string BaselineFrom { get; set; } = "";
    public DriftClause Drift { get; set; } = new();
    public List<DriveSpec> Drives { get; set; } = new();
    public MemorySettings Memory { get; set; } = new();
    public PersonaSeed Seed { get; set; } = new();

    /// <summary>Her speaking voice in the app's voice library (for example "Clone: vicki" or "Kitten: Luna"). Empty uses the app's voice.</summary>
    public string Voice { get; set; } = "";

    /// <summary>Her favourite colour, shown with her name beside the avatar: a colour name (teal) or a hex code (#4FC3F7). Empty uses the app's accent.</summary>
    public string FavoriteColor { get; set; } = "";

    /// <summary>Her own avatar: a set name in Data\avatars (for example "vicki"), or a full folder path. Empty uses the shared avatar.</summary>
    public string Avatar { get; set; } = "";

    /// <summary>Her age in years, shown on her card (0 = not set). Companions are always adults: 18 or older.</summary>
    public int Age { get; set; }

    /// <summary>"female" or "male": which words the app uses for them (she/her or he/him).</summary>
    public string Gender { get; set; } = Genders.Female;

    /// <summary>A line or two about them for their card in My Companions. Empty uses the start of the personality sheet.</summary>
    public string Tagline { get; set; } = "";

    public const int MinAge = 18;
    public const int MaxAge = 120;
    public const int MaxTaglineChars = 300;

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public DriveSpec Drive(string name) =>
        Drives.FirstOrDefault(d => d.Name == name) ?? throw new PersonaConfigException($"{Name} has no '{name}' drive in her config.");

    public static PersonaConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new PersonaConfigException($"Persona config not found: {path}");
        }
        PersonaConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<PersonaConfig>(File.ReadAllText(path), Json);
        }
        catch (JsonException ex)
        {
            throw new PersonaConfigException($"Persona config is not valid JSON ({path}, line {ex.LineNumber + 1}): {ex.Message}", ex);
        }
        if (config == null)
        {
            throw new PersonaConfigException($"Persona config is empty: {path}");
        }
        config.ValidateShape();
        return config;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Structural errors that make the config unusable. Throws on the first one.</summary>
    public void ValidateShape()
    {
        if (Schema != 1)
        {
            throw new PersonaConfigException($"Persona config schema {Schema} is not supported.");
        }
        if (!PersonaRegistry.IsValidId(Id))
        {
            throw new PersonaConfigException($"Persona id '{Id}' must be 1-40 lowercase letters, digits or dashes.");
        }
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new PersonaConfigException($"Persona '{Id}' has no name.");
        }
        if (Age != 0 && (Age < MinAge || Age > MaxAge))
        {
            throw new PersonaConfigException($"{Name}'s age must be between {MinAge} and {MaxAge} (companions are always adults), or left empty.");
        }
        if (Gender != Genders.Female && Gender != Genders.Male)
        {
            throw new PersonaConfigException($"{Name}'s gender must be \"{Genders.Female}\" or \"{Genders.Male}\".");
        }
        if (Tagline.Length > MaxTaglineChars)
        {
            throw new PersonaConfigException($"{Name}'s card description is {Tagline.Length} characters; keep it under {MaxTaglineChars}.");
        }
        if (Drives.Count == 0 || Drives.Count > Companion.Drives.MaxDrives)
        {
            throw new PersonaConfigException($"{Name} needs between 1 and {Companion.Drives.MaxDrives} drives (she has {Drives.Count}).");
        }
        foreach (DriveSpec d in Drives)
        {
            if (!Companion.Drives.IsValidName(d.Name))
            {
                throw new PersonaConfigException($"\"{d.Name}\" can't be a drive name: use one word of 1 to 24 lowercase letters, digits or dashes, starting with a letter.");
            }
            if (Drives.Count(x => x.Name == d.Name) != 1)
            {
                throw new PersonaConfigException($"{Name} has the drive '{d.Name}' more than once.");
            }
            if (d.Kind != DriveKinds.Need && d.Kind != DriveKinds.Level)
            {
                throw new PersonaConfigException($"{Name}'s '{d.Name}' drive has kind '{d.Kind}': it must be \"need\" or \"level\".");
            }
            if (d.Description.Length > Companion.Drives.MaxDescriptionChars || d.Description.Contains('\n'))
            {
                throw new PersonaConfigException($"{Name}'s '{d.Name}' description must be one line of at most {Companion.Drives.MaxDescriptionChars} characters.");
            }
            if (d.Cues.Count > Companion.Drives.MaxCues || d.Cues.Any(c => string.IsNullOrWhiteSpace(c) || c.Length > 60))
            {
                throw new PersonaConfigException($"{Name}'s '{d.Name}' drive can have up to {Companion.Drives.MaxCues} words or phrases, each up to 60 characters.");
            }
            if (d.Floor < Companion.Drives.Min || d.Ceiling > Companion.Drives.Max || d.Floor >= d.Ceiling)
            {
                throw new PersonaConfigException($"{Name}'s '{d.Name}' drive needs 0 <= floor < ceiling <= 10.");
            }
            if (d.Name == Companion.Drives.Horizon && d.Floor < Companion.Drives.HorizonFloor)
            {
                throw new PersonaConfigException($"Horizon never goes below {Companion.Drives.HorizonFloor}; its floor in {Name}'s config is {d.Floor}.");
            }
            if (d.DecayPerHour < 0 || d.DecayPerHour >= 1 || d.RisePerHour < 0 || d.SatisfyStep < 0)
            {
                throw new PersonaConfigException($"{Name}'s '{d.Name}' drive has a negative or out-of-range rate.");
            }
            if (d.Weight is < 0 or > 10)
            {
                throw new PersonaConfigException($"{Name}'s '{d.Name}' weight must be between 0 and 10.");
            }
            if (d.Initial is double v && (v < d.Floor || v > d.Ceiling))
            {
                throw new PersonaConfigException($"{Name}'s starting '{d.Name}' value {v} is outside {d.Floor}-{d.Ceiling}.");
            }
            foreach (string e in d.Events.Keys)
            {
                if (!DriveEvents.All.Contains(e))
                {
                    throw new PersonaConfigException($"{Name}'s '{d.Name}' drive reacts to unknown event '{e}'.");
                }
            }
            foreach (string line in new[] { d.Guidance.Low, d.Guidance.Mid, d.Guidance.High })
            {
                if (line.Length > ContextInjector.MaxGuidanceChars || line.Contains('\n'))
                {
                    throw new PersonaConfigException($"{Name}'s '{d.Name}' guidance lines must be one line of at most {ContextInjector.MaxGuidanceChars} characters.");
                }
            }
        }
        // Her inner-state block must fit its cap whatever her drives' values are (each line uses its longest guidance).
        int worst = TokenEstimate.Of("[Inner state: your own drives right now, 0-10. Not typed by the user. Let the top one lead; never recite numbers.]\n[End inner state]")
            + Drives.Sum(d => TokenEstimate.Of("- " + d.Name + " 10.0 TOP" + (d.IsLevel ? ContextInjector.LevelMark : "") + ": " + new[] { d.Guidance.Low, d.Guidance.Mid, d.Guidance.High }.MaxBy(g => g.Length) + "\n"));
        if (worst > ContextInjector.MaxDriveBlockTokens)
        {
            throw new PersonaConfigException($"{Name}'s drives would need about {worst} tokens in her inner-state block, over the {ContextInjector.MaxDriveBlockTokens}-token cap. Use fewer drives or shorter guidance lines.");
        }
        var traitNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AppearanceTrait t in Seed.Appearance.Traits)
        {
            string n = t.Name.Trim();
            if (n.Equals(PersonaStore.AnchorTrait, StringComparison.OrdinalIgnoreCase))
            {
                throw new PersonaConfigException($"{Name}'s identity anchor goes in 'identityAnchor', not in her traits list.");
            }
            if (n.Length > 0 && !traitNames.Add(n))
            {
                throw new PersonaConfigException($"{Name}'s appearance lists the trait '{n}' twice.");
            }
            if (t.Name.Length > 60 || t.Text.Length > AppearanceSeed.MaxTraitChars)
            {
                throw new PersonaConfigException($"{Name}'s appearance trait '{n}' is too long (names up to 60 characters, descriptions up to {AppearanceSeed.MaxTraitChars}).");
            }
        }
        if (Memory.SummarizeEveryTurns < 2 || Memory.KeepVerbatimTurns < 0 || Memory.KeepVerbatimTurns >= Memory.SummarizeEveryTurns * 4
            || Memory.SummaryTokenCap is < 200 or > 2000 || Memory.RetrieveTopK is < 0 or > 20)
        {
            throw new PersonaConfigException($"{Name}'s memory settings are out of range (summary cap at most 2000 tokens).");
        }
    }

    /// <summary>Everything that still blocks this persona from running, in plain language. Empty means ready.</summary>
    public List<string> MissingInputs()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(PersonalitySheet) || PersonalitySheet.Contains(Placeholder))
        {
            missing.Add($"{Name}'s personality sheet");
        }
        foreach (DriveSpec d in Drives)
        {
            if (d.Weight == null)
            {
                missing.Add($"weight for {d.Name}");
            }
            if (d.Initial == null)
            {
                missing.Add($"starting value for {d.Name}");
            }
            if (string.IsNullOrWhiteSpace(d.Guidance.Low) || string.IsNullOrWhiteSpace(d.Guidance.Mid) || string.IsNullOrWhiteSpace(d.Guidance.High))
            {
                missing.Add($"behaviour guidance for {d.Name}");
            }
            if (d.Name != Companion.Drives.Horizon && d.Cues.Count == 0 && !HeuristicScorer.KnowsDrive(d.Name))
            {
                missing.Add($"words that show she acted on {d.Name} (so Aquila can tell when it's satisfied)");
            }
        }
        if (Seed.Summary.Render().Contains(Placeholder))
        {
            missing.Add("the starting summary block (facts, feelings, hooks)");
        }
        if (string.IsNullOrWhiteSpace(Drift.Text) || Drift.Text.Contains(Placeholder))
        {
            missing.Add("the drift clause");
        }
        if (!Seed.Appearance.IsComplete)
        {
            missing.Add("her appearance card (permanent traits, identity anchor and what she's wearing)");
        }
        return missing;
    }
}

/// <summary>A companion's gender, and the words that go with it.</summary>
public static class Genders
{
    public const string Female = "female";
    public const string Male = "male";

    public static bool IsMale(string gender) => gender == Male;

    /// <summary>"she" / "he".</summary>
    public static string Subject(string gender) => IsMale(gender) ? "he" : "she";

    /// <summary>"her" / "him".</summary>
    public static string Object(string gender) => IsMale(gender) ? "him" : "her";

    /// <summary>"her" / "his".</summary>
    public static string Possessive(string gender) => IsMale(gender) ? "his" : "her";
}
