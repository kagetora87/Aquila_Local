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

    public static readonly IReadOnlyList<string> All = new[] { Lust, Curiosity, Comfort, Loneliness, Horizon };
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

    /// <summary>How far one satisfied turn lowers the drive.</summary>
    public double SatisfyStep { get; set; } = 1.0;

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
        foreach (string name in Companion.Drives.All)
        {
            if (Drives.Count(d => d.Name == name) != 1)
            {
                throw new PersonaConfigException($"{Name}'s config must define the '{name}' drive exactly once.");
            }
        }
        foreach (DriveSpec d in Drives)
        {
            if (!Companion.Drives.All.Contains(d.Name))
            {
                throw new PersonaConfigException($"{Name}'s config has an unknown drive '{d.Name}'.");
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
