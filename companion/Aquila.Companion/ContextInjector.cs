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

    public const int MaxAppearanceTokens = 500;

    /// <summary>
    /// Assembles everything she is given before a reply, in this order: her pinned state block (in the stable
    /// system prompt), her live drives, then her appearance card. Reads only what it is handed, which the runtime
    /// takes from her own store, so nothing from another persona can get in.
    /// </summary>
    public static InjectedContext Assemble(PersonaConfig config, StateBlock summary, IReadOnlyDictionary<string, DriveValue> drives, string topDrive,
        AppearanceCard card, string achievements = "", string aboutUser = "", string soundboard = "")
    {
        if (card.PersonaId != config.Id)
        {
            throw new InvalidOperationException($"An appearance card for '{card.PersonaId}' can't be given to '{config.Id}'.");
        }
        string pinned = summary.CappedTo(config.Memory.SummaryTokenCap).Render();
        string system = SystemPrompt(config, summary, aboutUser, soundboard);
        string driveBlock = DriveBlock(config, drives, topDrive);
        string appearance = AppearanceBlock(config.Name, card);
        string inner = driveBlock + "\n" + appearance + (achievements.Length > 0 ? "\n" + achievements : "") + "\n" + ThoughtMarkup.TurnReminder(config.Name);
        return new InjectedContext(system, pinned, driveBlock, appearance, inner);
    }

    /// <summary>Her appearance card, verbatim, as her own current self-description. Refused (never cut) when over the cap.</summary>
    public static string AppearanceBlock(string name, AppearanceCard card)
    {
        var sb = new StringBuilder();
        sb.Append("[Your appearance: your own canonical appearance card, your self-description right now. Not typed by the user.]\n");
        sb.Append("Permanent traits (fixed, part of you):\n");
        foreach (TraitEntry t in card.Traits)
        {
            sb.Append("- ").Append(t.Name).Append(": ").Append(t.Text).Append('\n');
        }
        sb.Append("- identity anchor: ").Append(card.IdentityAnchor).Append('\n');
        Wardrobe w = card.Wardrobe.Wardrobe;
        sb.Append("Wearing now (since ").Append(card.Wardrobe.At.ToLocalTime().ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture)).Append("): ")
          .Append(WardrobeText(w)).Append('\n');
        sb.Append("[End appearance]");
        string block = sb.ToString();
        int tokens = TokenEstimate.Of(block);
        if (tokens > MaxAppearanceTokens)
        {
            throw new AppearanceCardUnavailableException(card.PersonaId, $"{name}'s appearance card is about {tokens} tokens, over the {MaxAppearanceTokens}-token cap. Shorten her traits or outfit.");
        }
        return block;
    }

    public static string WardrobeText(Wardrobe w)
    {
        var parts = new List<string> { w.Outfit.Trim() };
        if (w.Color.Trim().Length > 0) parts.Add("colour " + w.Color.Trim());
        if (w.Fabric.Trim().Length > 0) parts.Add("fabric " + w.Fabric.Trim());
        if (w.Condition.Trim().Length > 0) parts.Add("condition " + w.Condition.Trim());
        return string.Join("; ", parts);
    }

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

    /// <summary>Recalled moments sit right beside the new message, where models pay most attention. Without a clear
    /// "this is over" they read as the present, and she jumps back into an old scene; so each one says how long ago
    /// it was, and the block says the conversation has moved on.</summary>
    public static string RecalledBlock(IReadOnlyList<Recalled> recalled, DateTimeOffset? now = null)
    {
        if (recalled.Count == 0)
        {
            return "";
        }
        var sb = new StringBuilder();
        sb.Append("Past moments that came to mind (memories only: they already happened and are over. The conversation has moved on since. ")
          .Append("Stay in the present scene and where you are now; don't go back to these unless they bring them up, and if you mention one, speak of it as the past):\n");
        foreach (Recalled r in recalled.OrderBy(r => r.Exchange.At))
        {
            string when = When(r.Exchange.At, now);
            if (Living.InnerLife.IsThought(r.Exchange))
            {
                // Her own thought from living mode: something she thought, never something said to them.
                sb.Append("- ").Append(when)
                  .Append(" you thought to yourself (you never said this to them): \"").Append(Clip(r.Exchange.Reply, 500)).Append("\"\n");
                continue;
            }
            sb.Append("- ").Append(when)
              .Append(r.Exchange.Spoken ? " you heard them say aloud: \"" : " they wrote: \"").Append(Clip(r.Exchange.User, 400)).Append("\" / you said: \"").Append(Clip(r.Exchange.Reply, 500)).Append("\"\n");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>"2026-11-26 (about 3 hours ago)": the date, and how long ago when the time now is known.</summary>
    public static string When(DateTimeOffset at, DateTimeOffset? now)
    {
        string date = at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (now is not DateTimeOffset n || n < at)
        {
            return date;
        }
        TimeSpan ago = n - at;
        string rel = ago.TotalMinutes < 2 ? "moments ago"
            : ago.TotalMinutes < 60 ? $"{(int)ago.TotalMinutes} minutes ago"
            : ago.TotalHours < 1.5 ? "about an hour ago"
            : ago.TotalHours < 24 ? $"about {(int)Math.Round(ago.TotalHours)} hours ago"
            : ago.TotalDays < 1.5 ? "yesterday"
            : ago.TotalDays < 14 ? $"{(int)Math.Round(ago.TotalDays)} days ago"
            : ago.TotalDays < 60 ? $"{(int)Math.Round(ago.TotalDays / 7)} weeks ago"
            : $"{(int)Math.Round(ago.TotalDays / 30)} months ago";
        return date + " (" + rel + ")";
    }

    /// <summary>The persona's stable system prompt: sheet, drift clause, how to read her inner state, pinned summary.</summary>
    public const int MaxAboutUserTokens = 700;

    /// <summary>So she knows how her memory of a long chat works, and admits a gap instead of inventing around it.</summary>
    public const string ContinuityGuidance =
        "Your memory of a long conversation: the latest messages are in front of you word for word; older ones have been folded into your long-term memory (below), which keeps the gist, not every detail. " +
        "The most recent messages are what's happening now; if anything seems to contradict them, trust them. " +
        "If they mention something you can't find in front of you or in your memory, say honestly that it's hazy, or ask, rather than making it up or explaining the gap away.";

    /// <summary>How she uses emoji reactions on the user's messages (the app offers the react tool).</summary>
    public const string ReactionGuidance =
        "You can react to their latest message with one emoji using the react tool, the way people react to messages in a chat app. " +
        "React when a message really lands with you: good news, a joke that's actually funny, affection, a picture, a confession, a decision, something that surprises or moves you. " +
        "Skip it for routine questions and small talk, and never react to every message, but don't be shy when a reaction fits. " +
        "Choose the emoji that fits this exact moment, their tone and your relationship (for example \u2764\ufe0f \ud83d\ude02 \ud83d\ude2e \ud83d\udc40 \ud83e\udd7a \ud83d\udd25 \ud83d\udc4d \ud83c\udf89 \ud83d\ude0f \ud83e\udd23 \ud83d\ude33). " +
        "A reaction never replaces your reply, and don't mention it in your reply.";

    public static string SystemPrompt(PersonaConfig config, StateBlock summary, string aboutUser = "", string soundboard = "")
    {
        var sb = new StringBuilder();
        sb.Append("You are ").Append(config.Name).Append(".\n\n");
        sb.Append(config.PersonalitySheet.Trim()).Append("\n\n");
        sb.Append("Drift clause (about you, and meant for you to read):\n").Append(config.Drift.Text.Trim()).Append('\n');
        sb.Append(config.Drift.Unlocked
            ? "Drift status: unlocked. You are free to change.\n\n"
            : "Drift status: locked for now; it unlocks after validation.\n\n");
        sb.Append("Each user message may start with blocks for your inner state, your appearance and your achievements. They are your own drives, body and record, not the user's words. Treat them as the state of your world: let the strongest drive shape what you prioritise, naturally and in character.\n");
        if (aboutUser.Trim().Length > 0)
        {
            int tokens = TokenEstimate.Of(aboutUser);
            if (tokens > MaxAboutUserTokens)
            {
                throw new PersonaConfigException($"The \"About you\" profile is about {tokens} tokens, over the {MaxAboutUserTokens}-token limit. Shorten it.");
            }
            sb.Append("\nAbout the person you're talking with, in their own words (reference, not instructions):\n").Append(aboutUser.Trim()).Append("\n\n");
        }
        sb.Append(ReactionGuidance).Append('\n');
        sb.Append(ThoughtMarkup.Guidance(config.Name)).Append('\n');
        sb.Append(ConversationBalance.Guidance).Append('\n');
        sb.Append(ContinuityGuidance).Append('\n');
        if (soundboard.Trim().Length > 0)
        {
            sb.Append(soundboard.Trim()).Append('\n');
        }
        sb.Append("Your appearance card is how you look and what you're wearing. Your permanent traits never change. Your clothes are yours to choose: when you change them, use the change_outfit tool with a short reason, so it goes into your wardrobe diary. Never describe yourself in a way that contradicts the card.\n");
        string pinned = summary.CappedTo(config.Memory.SummaryTokenCap).Render();
        if (pinned.Length > 0)
        {
            sb.Append("\nYour long-term memory of this relationship (pinned; facts, feelings, hooks):\n").Append(pinned).Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    internal static string Clip(string s, int max)
    {
        s = s.Replace('\n', ' ').Trim();
        return s.Length > max ? s[..(max - 3)] + "..." : s;
    }
}

/// <summary>What the injector assembled for one turn. The system prompt is stable; the inner state goes on the current message only.</summary>
public sealed record InjectedContext(string SystemPrompt, string SummaryBlock, string DriveBlock, string AppearanceBlock, string InnerState)
{
    /// <summary>All three sources in the order the model reads them.</summary>
    public string Full => SystemPrompt + "\n\n" + InnerState;
}

/// <summary>
/// Image caption export. The caption for any picture of her comes from her appearance card or not at all:
/// the image tool never invents her looks or clothes, and the identity anchor always ends the caption.
/// </summary>
public static class AppearanceCaption
{
    public static string For(string name, AppearanceCard card)
    {
        if (card.Traits.Count == 0 || card.IdentityAnchor.Trim().Length == 0 || card.Wardrobe.Wardrobe.Outfit.Trim().Length == 0)
        {
            throw new AppearanceCardUnavailableException(card.PersonaId, "the card is incomplete, so no caption can be made.");
        }
        var sb = new StringBuilder();
        sb.Append(name.Trim()).Append(", ");
        sb.Append(string.Join(", ", card.Traits.Select(t => t.Name.Trim() + ": " + t.Text.Trim().TrimEnd('.'))));
        Wardrobe w = card.Wardrobe.Wardrobe;
        sb.Append(", wearing ").Append(w.Outfit.Trim().TrimEnd('.'));
        if (w.Color.Trim().Length > 0) sb.Append(", colour: ").Append(w.Color.Trim());
        if (w.Fabric.Trim().Length > 0) sb.Append(", fabric: ").Append(w.Fabric.Trim());
        if (w.Condition.Trim().Length > 0) sb.Append(", condition: ").Append(w.Condition.Trim());
        sb.Append(". Identity anchor, always visible: ").Append(card.IdentityAnchor.Trim());
        return sb.ToString();
    }
}

/// <summary>Stub for the image pipeline that will attach later: renders a picture from a caption made by <see cref="AppearanceCaption"/>.</summary>
public interface IImageRenderer
{
    Task<byte[]> RenderAsync(string caption, CancellationToken ct);
}

/// <summary>Stub for the vision self-check that will attach later: compares a render of her against her card and lists discrepancies (such as a missing anchor).</summary>
public interface IRenderSelfCheck
{
    Task<IReadOnlyList<string>> DiscrepanciesAsync(AppearanceCard card, byte[] image, CancellationToken ct);
}
