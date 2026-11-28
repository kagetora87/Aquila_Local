using System.Globalization;

namespace AquilaLocal.Companion;

/// <summary>Pure drive arithmetic: time passing, events, priority. No I/O, so it is easy to test.</summary>
public static class DriveEngine
{
    /// <summary>Extra priority per consecutive unsatisfied turn, so an ignored drive weighs heavier next turn.</summary>
    public const double StreakBoost = 0.15;

    public const int MaxStreakBoost = 6;

    public static double Clamp(DriveSpec spec, double v) => Math.Clamp(v, spec.Floor, spec.Ceiling);

    /// <summary>
    /// Value after <paramref name="hours"/> have passed: first the idle rise, then exponential decay of the
    /// distance to the resting level (lust at 2 % per hour loses 2 % of its value every hour).
    /// </summary>
    public static double AfterTime(DriveSpec spec, double value, double hours)
    {
        if (hours <= 0)
        {
            return Clamp(spec, value);
        }
        double v = value + spec.RisePerHour * hours;
        if (spec.DecayPerHour > 0)
        {
            v = spec.RestingLevel + (v - spec.RestingLevel) * Math.Pow(1.0 - spec.DecayPerHour, hours);
        }
        return Clamp(spec, v);
    }

    public static List<DriveChange> Tick(PersonaConfig config, IReadOnlyDictionary<string, DriveValue> drives, DateTimeOffset lastTick, DateTimeOffset now)
    {
        double hours = (now - lastTick).TotalHours;
        var changes = new List<DriveChange>();
        if (hours <= 0)
        {
            return changes;
        }
        foreach (DriveSpec spec in config.Drives)
        {
            double old = drives[spec.Name].Value;
            double v = AfterTime(spec, old, hours);
            if (Math.Abs(v - old) > 1e-9)
            {
                changes.Add(new DriveChange(spec.Name, old, v, string.Create(CultureInfo.InvariantCulture, $"time: {hours:0.##} h passed")));
            }
        }
        return changes;
    }

    public static List<DriveChange> ApplyEvents(PersonaConfig config, IReadOnlyDictionary<string, double> values, IReadOnlyCollection<string> events)
    {
        var changes = new List<DriveChange>();
        foreach (DriveSpec spec in config.Drives)
        {
            double old = values[spec.Name];
            double v = old;
            var applied = new List<string>();
            foreach (string e in events)
            {
                if (spec.Events.TryGetValue(e, out double delta) && delta != 0)
                {
                    v += delta;
                    applied.Add(e);
                }
            }
            v = Clamp(spec, v);
            if (Math.Abs(v - old) > 1e-9)
            {
                changes.Add(new DriveChange(spec.Name, old, v, "event: " + string.Join(", ", applied)));
            }
        }
        return changes;
    }

    /// <summary>How hard a drive presses on her: a need presses when high, a feeling level when low.</summary>
    public static double Pressure(DriveSpec spec, double value) => spec.IsLevel ? spec.Ceiling - value : value;

    public static double Priority(DriveSpec spec, double value, int unmetStreak) =>
        Pressure(spec, value) * (spec.Weight ?? 0) * (1.0 + StreakBoost * Math.Min(unmetStreak, MaxStreakBoost));

    /// <summary>Her value after one satisfied turn: a need goes down a step, a feeling level goes up one.</summary>
    public static double Satisfied(DriveSpec spec, double value) => Clamp(spec, spec.IsLevel ? value + spec.SatisfyStep : value - spec.SatisfyStep);

    /// <summary>The drive with the highest value x weight (boosted by its unsatisfied streak). Ties go to the earlier drive.</summary>
    public static string TopDrive(PersonaConfig config, IReadOnlyDictionary<string, DriveValue> drives)
    {
        string best = config.Drives[0].Name;
        double bestScore = double.NegativeInfinity;
        foreach (DriveSpec spec in config.Drives)
        {
            DriveValue d = drives[spec.Name];
            double p = Priority(spec, d.Value, d.UnmetStreak);
            if (p > bestScore + 1e-12)
            {
                best = spec.Name;
                bestScore = p;
            }
        }
        return best;
    }
}

public static class TokenEstimate
{
    /// <summary>
    /// Conservative token count for English prose on Qwen/Gemma tokenizers (they average about 4 characters
    /// per token; we assume 3 so the 300 and 2,000 token caps hold with margin).
    /// </summary>
    public static int Of(string text) => (int)Math.Ceiling(text.Length / 3.0);
}
