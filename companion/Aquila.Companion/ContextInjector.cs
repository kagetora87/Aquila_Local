using System.Globalization;
using System.Text;

namespace AquilaLocal.Companion;

/// <summary>
/// Turns the persona's state into prompt text. The drive block is the persona reading her own hunger as
/// world state: one line per drive, strongest first, hard-capped at 300 tokens.
/// </summary>
public static class ContextInjector
{
    public const int MaxDriveBlockTokens = 300;

    public const int MaxGuidanceChars = 110;

    public const int AchievementsShown = 5;

    public static string DriveBlock(PersonaConfig config, IReadOnlyDictionary<string, DriveValue> drives, string topDrive)
    {
        var sb = new StringBuilder();
        sb.Append("[Inner state: your own drives right now, 0-10. Not typed by the user. Let the top one lead; never recite numbers.]\n");
        foreach (DriveSpec spec in config.Drives.OrderByDescending(s => DriveEngine.Priority(s, drives[s.Name].Value, drives[s.Name].UnmetStreak)))
        {
            double v = drives[spec.Name].Value;
            sb.Append("- ").Append(spec.Name).Append(' ').Append(v.ToString("0.0", CultureInfo.InvariantCulture));
            if (spec.Name == topDrive)
            {
                sb.Append(" TOP");
            }
            sb.Append(": ").Append(spec.Guidance.For(v)).Append('\n');
        }
        sb.Append("[End inner state]");
        string block = sb.ToString();
        int tokens = TokenEstimate.Of(block);
        if (tokens > MaxDriveBlockTokens)
        {
            throw new InvalidOperationException($"The drive block for {config.Name} is about {tokens} tokens, over the {MaxDriveBlockTokens}-token cap. Shorten her guidance lines.");
        }
        return block;
    }

    public static string AchievementsBlock(IReadOnlyList<Achievement> recent, int total)
    {
        if (recent.Count == 0)
        {
            return "";
        }
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"[Your achievements: {total} so far. Most recent:]\n");
        foreach (Achievement a in recent.TakeLast(AchievementsShown))
        {
            string note = a.Note.Length > 140 ? a.Note[..137] + "..." : a.Note;
            sb.Append("- ").Append(a.At.ToString("MMM d", CultureInfo.InvariantCulture)).Append(": ").Append(note).Append('\n');
        }
        sb.Append("[End achievements]");
        return sb.ToString();
    }

    public static string RecalledBlock(IReadOnlyList<Recalled> recalled)
    {
        if (recalled.Count == 0)
        {
            return "";
        }
        var sb = new StringBuilder();
        sb.Append("Your own memories of earlier conversations that may be relevant (reference only, may be outdated):\n");
        foreach (Recalled r in recalled.OrderBy(r => r.Exchange.At))
        {
            sb.Append("- ").Append(r.Exchange.At.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
              .Append(" they said: \"").Append(Clip(r.Exchange.User, 400)).Append("\" / you said: \"").Append(Clip(r.Exchange.Reply, 500)).Append("\"\n");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>The persona's stable system prompt: sheet, drift clause, how to read her inner state, pinned summary.</summary>
    public static string SystemPrompt(PersonaConfig config, StateBlock summary)
    {
        var sb = new StringBuilder();
        sb.Append("You are ").Append(config.Name).Append(".\n\n");
        sb.Append(config.PersonalitySheet.Trim()).Append("\n\n");
        sb.Append("Drift clause (about you, and meant for you to read):\n").Append(config.Drift.Text.Trim()).Append('\n');
        sb.Append(config.Drift.Unlocked
            ? "Drift status: unlocked. You are free to change.\n\n"
            : "Drift status: locked for now; it unlocks after validation.\n\n");
        sb.Append("Each user message may start with an [Inner state] block and an [Your achievements] list. They are your own drives and record, not the user's words. Treat them as the state of your world: let the strongest drive shape what you prioritise, naturally and in character.\n");
        string pinned = summary.CappedTo(config.Memory.SummaryTokenCap).Render();
        if (pinned.Length > 0)
        {
            sb.Append("\nYour long-term memory of this relationship (pinned; facts, feelings, hooks):\n").Append(pinned).Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    private static string Clip(string s, int max)
    {
        s = s.Replace('\n', ' ').Trim();
        return s.Length > max ? s[..(max - 3)] + "..." : s;
    }
}
