using System.Globalization;
using System.Text;

namespace AquilaLocal.Companion;

public sealed record TuningResult(string Message, bool ConfigChanged);

/// <summary>
/// The mid-session tuning command. Typed into a companion chat as "/tune ...", it is handled by the app and
/// never sent to the model. Parameter changes are written to the persona's persona.json (so the file stays
/// the single source of truth) and take effect immediately; value changes are logged like any other change.
/// </summary>
public static class PersonaTuning
{
    public const string Prefix = "/tune";

    public static readonly IReadOnlyDictionary<string, string> Parameters = new Dictionary<string, string>
    {
        ["weight"] = "how much the drive matters when picking the top drive (0-10)",
        ["initial"] = "starting value used only when a persona is first set up",
        ["decay"] = "fraction lost per hour toward the resting level (0.02 = 2 % per hour)",
        ["rest"] = "resting level that decay pulls toward",
        ["rise"] = "points gained per idle hour",
        ["step"] = "how far one satisfied turn (or horizon milestone) lowers it",
        ["floor"] = "lowest allowed value (horizon can't go below 3)",
        ["ceiling"] = "highest allowed value"
    };

    public static bool IsCommand(string text) =>
        text.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) &&
        (text.Trim().Length == Prefix.Length || char.IsWhiteSpace(text.TrimStart()[Prefix.Length]));

    public static string Help(PersonaConfig config) =>
        $"""
        Tuning {config.Name} (changes are saved to her persona.json and apply right away):
        /tune show                         current drive values and settings
        /tune <drive> <value>              set a drive's current value now, e.g. /tune lust 6.5
        /tune <drive> <setting> <number>   change a setting, e.g. /tune horizon weight 2.0
             settings: {string.Join(", ", Parameters.Keys)}
        /tune <drive> event <event> <change>   e.g. /tune comfort event conflict -2
             events: {string.Join(", ", DriveEvents.All)}
        /tune drift unlock | lock          change the drift clause status
        /tune reload                       re-read persona.json after editing it by hand
        Drives: {string.Join(", ", Drives.All)}
        """;

    /// <param name="configPath">Path of the persona's persona.json.</param>
    public static TuningResult Run(string command, string configPath, PersonaStore store, DateTimeOffset now)
    {
        PersonaConfig config = PersonaConfig.Load(configPath);
        string[] words = command.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
        if (words.Length == 0 || words[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return new TuningResult(Help(config), false);
        }
        string first = words[0].ToLowerInvariant();
        if (first == "show")
        {
            return new TuningResult(Show(config, store), false);
        }
        if (first == "reload")
        {
            return new TuningResult($"Reloaded {config.Name}'s persona.json.", true);
        }
        if (first == "drift" && words.Length == 2 && (words[1] is "unlock" or "lock"))
        {
            config.Drift.Unlocked = words[1] == "unlock";
            config.ValidateShape();
            config.Save(configPath);
            return new TuningResult($"{config.Name}'s drift is now {(config.Drift.Unlocked ? "unlocked" : "locked")}.", true);
        }
        if (!Drives.All.Contains(first))
        {
            throw new ArgumentException($"'{words[0]}' isn't a drive or a tuning command. Type /tune for help.");
        }
        DriveSpec spec = config.Drive(first);
        if (words.Length == 2)
        {
            double value = Number(words[1]);
            double old = store.ReadDrives(config.Drives.Select(d => d.Name))[first].Value;
            if (value < spec.Floor || value > spec.Ceiling)
            {
                throw new ArgumentException($"{first} must stay between {spec.Floor} and {spec.Ceiling}.");
            }
            store.Apply(new[] { new DriveChange(first, old, value, "operator: /tune") }, now);
            return new TuningResult(string.Create(CultureInfo.InvariantCulture, $"{config.Name}'s {first} set from {old:0.0} to {value:0.0} (logged)."), false);
        }
        if (words.Length == 4 && words[1].Equals("event", StringComparison.OrdinalIgnoreCase))
        {
            string e = words[2].ToLowerInvariant();
            spec.Events[e] = Number(words[3]);
            config.ValidateShape();
            config.Save(configPath);
            return new TuningResult($"{config.Name}'s {first} now changes by {words[3]} on '{e}'. Saved.", true);
        }
        if (words.Length == 3 && Parameters.ContainsKey(words[1].ToLowerInvariant()))
        {
            double n = Number(words[2]);
            switch (words[1].ToLowerInvariant())
            {
                case "weight": spec.Weight = n; break;
                case "initial": spec.Initial = n; break;
                case "decay": spec.DecayPerHour = n; break;
                case "rest": spec.RestingLevel = n; break;
                case "rise": spec.RisePerHour = n; break;
                case "step": spec.SatisfyStep = n; break;
                case "floor": spec.Floor = n; break;
                case "ceiling": spec.Ceiling = n; break;
            }
            config.ValidateShape();
            config.Save(configPath);
            return new TuningResult($"{config.Name}'s {first} {words[1].ToLowerInvariant()} is now {words[2]}. Saved to persona.json.", true);
        }
        throw new ArgumentException("I didn't understand that. Type /tune for help.");
    }

    public static string Show(PersonaConfig config, PersonaStore store)
    {
        Dictionary<string, DriveValue> drives = store.ReadDrives(config.Drives.Select(d => d.Name));
        var sb = new StringBuilder();
        sb.Append(config.Name).Append(" (drift ").Append(config.Drift.Unlocked ? "unlocked" : "locked").Append(")\n");
        foreach (DriveSpec d in config.Drives)
        {
            DriveValue v = drives[d.Name];
            sb.Append(string.Create(CultureInfo.InvariantCulture,
                $"{d.Name}: {v.Value:0.0}  weight {d.Weight}  decay {d.DecayPerHour}/h  rest {d.RestingLevel}  rise {d.RisePerHour}/h  step {d.SatisfyStep}  range {d.Floor}-{d.Ceiling}"));
            if (d.Events.Count > 0)
            {
                sb.Append("  events ").Append(string.Join(", ", d.Events.Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.Key} {e.Value:+0.##;-0.##}"))));
            }
            if (v.UnmetStreak > 0)
            {
                sb.Append("  (unsatisfied ").Append(v.UnmetStreak).Append(" turn(s))");
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    private static double Number(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v)
            ? v
            : throw new ArgumentException($"'{s}' isn't a number.");
}
