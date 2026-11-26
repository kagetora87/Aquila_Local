using System.Text;
using System.Text.RegularExpressions;

namespace AquilaLocal.Companion;

public enum MarkupKind { Speech, Thought, Action }

public sealed record MarkupPart(MarkupKind Kind, string Text);

/// <summary>
/// Thoughts and actions inside a message. &lt;thought&gt;...&lt;/thought&gt; is private: what someone is thinking
/// (shown blue). &lt;action&gt;...&lt;/action&gt; is what someone visibly does (shown orange). Everything else is speech.
/// </summary>
public static class ThoughtMarkup
{
    public const string ThoughtOpen = "<thought>";
    public const string ThoughtClose = "</thought>";
    public const string ActionOpen = "<action>";
    public const string ActionClose = "</action>";

    private static readonly Regex Tag = new(@"<(/?)(thought|action)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool HasMarkup(string text) => Tag.IsMatch(text ?? "");

    public static List<MarkupPart> Parse(string text)
    {
        var parts = new List<MarkupPart>();
        MarkupKind kind = MarkupKind.Speech;
        int at = 0;
        foreach (Match m in Tag.Matches(text ?? ""))
        {
            Add(parts, kind, text![at..m.Index]);
            bool closing = m.Groups[1].Value == "/";
            MarkupKind tagKind = m.Groups[2].Value.Equals("thought", StringComparison.OrdinalIgnoreCase) ? MarkupKind.Thought : MarkupKind.Action;
            kind = closing ? MarkupKind.Speech : tagKind;
            at = m.Index + m.Length;
        }
        Add(parts, kind, (text ?? "")[at..]);
        return parts;
    }

    private static void Add(List<MarkupPart> parts, MarkupKind kind, string text)
    {
        if (text.Length == 0)
        {
            return;
        }
        if (parts.Count > 0 && parts[^1].Kind == kind)
        {
            parts[^1] = parts[^1] with { Text = parts[^1].Text + text };
        }
        else
        {
            parts.Add(new MarkupPart(kind, text));
        }
    }

    public static string Write(IEnumerable<MarkupPart> parts)
    {
        var sb = new StringBuilder();
        foreach (MarkupPart p in parts)
        {
            switch (p.Kind)
            {
                case MarkupKind.Thought:
                    sb.Append(ThoughtOpen).Append(p.Text).Append(ThoughtClose);
                    break;
                case MarkupKind.Action:
                    sb.Append(ActionOpen).Append(p.Text).Append(ActionClose);
                    break;
                default:
                    sb.Append(p.Text);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Every line gets complete tags, so line-by-line display colours each line correctly.</summary>
    public static string BalancePerLine(string text)
    {
        if (!HasMarkup(text))
        {
            return text;
        }
        var lines = new List<string>();
        foreach (string line in Lines(Parse(text)))
        {
            lines.Add(line);
        }
        return string.Join("\n", lines);
    }

    private static IEnumerable<string> Lines(List<MarkupPart> parts)
    {
        var line = new List<MarkupPart>();
        foreach (MarkupPart p in parts)
        {
            string[] pieces = p.Text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < pieces.Length; i++)
            {
                if (i > 0)
                {
                    yield return Write(line);
                    line.Clear();
                }
                if (pieces[i].Length > 0)
                {
                    line.Add(new MarkupPart(p.Kind, pieces[i]));
                }
            }
        }
        yield return Write(line);
    }

    /// <summary>The words with the tags taken out (for titles and previews).</summary>
    public static string Plain(string text) => HasMarkup(text) ? Tag.Replace(text, "") : text;

    /// <summary>The message without anyone's thoughts (actions and speech stay).</summary>
    public static string StripThoughts(string text)
    {
        if (!HasMarkup(text))
        {
            return text;
        }
        string s = Write(Parse(text).Where(p => p.Kind != MarkupKind.Thought));
        return Regex.Replace(s, @"[ \t]{2,}", " ").Trim();
    }

    /// <summary>
    /// For the voice: thoughts, actions and the tags themselves become spaces of the same length, so nothing
    /// but speech is read aloud and the positions of voice cues in the text don't move.
    /// </summary>
    public static string MaskForSpeech(string text)
    {
        if (!HasMarkup(text))
        {
            return text;
        }
        var sb = new StringBuilder(text.Length);
        MarkupKind kind = MarkupKind.Speech;
        int at = 0;
        foreach (Match m in Tag.Matches(text))
        {
            string piece = text[at..m.Index];
            sb.Append(kind == MarkupKind.Speech ? piece : Blank(piece));
            sb.Append(' ', m.Length);
            kind = m.Groups[1].Value == "/" ? MarkupKind.Speech : (m.Groups[2].Value.Equals("thought", StringComparison.OrdinalIgnoreCase) ? MarkupKind.Thought : MarkupKind.Action);
            at = m.Index + m.Length;
        }
        string rest = text[at..];
        sb.Append(kind == MarkupKind.Speech ? rest : Blank(rest));
        return sb.ToString();
    }

    private static string Blank(string s) => new(s.Select(c => c == '\n' ? '\n' : ' ').ToArray());

    /// <summary>
    /// Whether personas write their thoughts and actions in first person ("I smile") or third person ("Vicki smiles").
    /// An app-wide display preference, set from the app's settings.
    /// </summary>
    public static MarkupPerson Person { get; set; } = MarkupPerson.First;

    /// <summary>How thoughts and actions work, for a persona's system prompt.</summary>
    public static string Guidance(string name) =>
        "Thoughts and actions: write what you visibly do inside <action>...</action> (everyone present sees it) and what you privately think inside <thought>...</thought> (nobody in the story hears it). Everything else is what you say aloud. Use both naturally and often, the way a person in a story would, but keep each one short. "
        + PersonRule(name) + " "
        + "The user can use them too. Their <action> is something they visibly do, and your character sees it. Their <thought> is private: your character cannot hear or know it, so never mention it, answer it, or act as if you knew it. As the storyteller you may let it quietly shape where things go, only when that fits naturally.";

    /// <summary>The first- or third-person rule for one persona's thoughts and actions.</summary>
    public static string PersonRule(string name) => Person == MarkupPerson.Third
        ? $"Write your thoughts and actions in the third person, using your name or your pronoun, for example <action>{name} smiles</action> and <thought>{name} hopes this goes well</thought>. Speech stays in the first person."
        : "Write your thoughts and actions in the first person, for example <action>I smile</action> and <thought>I hope this goes well</thought>.";
}

public enum MarkupPerson { First, Third }
