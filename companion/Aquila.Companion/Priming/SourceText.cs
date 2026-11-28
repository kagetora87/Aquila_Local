using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AquilaLocal.Companion.Priming;

/// <summary>Something to learn a companion from: a saved conversation, a character write-up, notes. Name is the file name or "Pasted text".</summary>
public sealed record PrimeSource(string Name, string Text);

/// <summary>One line of a conversation: who said it and what.</summary>
public sealed record SpokenLine(string Speaker, string Text);

/// <summary>
/// Reads the plain text out of the files people save conversations in, finds who is speaking in a pasted
/// conversation ("Ru: hi" or ChatGPT's "You said:" lines), and cuts long text into parts the model can read.
/// </summary>
public static class SourceText
{
    /// <summary>The file types read directly here (chat-app exports are read by the app's chat importer).</summary>
    public static readonly IReadOnlyList<string> PlainExtensions = new[] { ".txt", ".md", ".markdown", ".log", ".html", ".htm", ".docx", ".rtf" };

    public const int MaxSourceChars = 2_000_000;

    /// <summary>Plain text of a .txt, .md, .html or .docx file. Anything else is refused with a plain explanation.</summary>
    public static string Read(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        string text = ext switch
        {
            ".docx" => FromDocx(path),
            ".html" or ".htm" => FromHtml(File.ReadAllText(path, Encoding.UTF8)),
            ".rtf" => FromRtf(File.ReadAllText(path, Encoding.UTF8)),
            ".txt" or ".md" or ".markdown" or ".log" => File.ReadAllText(path, Encoding.UTF8),
            ".pdf" => throw new InvalidDataException("PDF files can't be read yet. Open it, select all the text, copy it, and paste it into the box instead."),
            _ => throw new InvalidDataException($"“{Path.GetFileName(path)}” isn't a kind of file Aquila can read here. Use a text file (.txt or .md), a Word document (.docx), a web page (.html), or a chat export, or paste the text into the box.")
        };
        text = Normalise(text);
        if (text.Length == 0)
        {
            throw new InvalidDataException($"“{Path.GetFileName(path)}” has no text in it.");
        }
        if (text.Length > MaxSourceChars)
        {
            throw new InvalidDataException($"“{Path.GetFileName(path)}” is very large ({text.Length / 1000:N0} thousand characters). Split it into smaller files, up to {MaxSourceChars / 1000:N0} thousand characters each.");
        }
        return text;
    }

    public static string Normalise(string text) =>
        Regex.Replace((text ?? "").TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n'), @"\n{4,}", "\n\n\n").Trim();

    /// <summary>
    /// A Word document's text. When speakers are told apart by text colour, label the first message of each colour
    /// once ("Ru: ...") and every later message in that colour is labelled the same way here, so the chat reads as
    /// "Name: words" lines. Documents without colours (or without labels) come out as they are.
    /// </summary>
    public static string FromDocx(string path)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);
        ZipArchiveEntry entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException($"“{Path.GetFileName(path)}” doesn't look like a Word document.");
        using Stream s = entry.Open();
        XDocument doc = XDocument.Load(s);
        return LabelByColour(Segments(doc));
    }

    /// <summary>The text in pieces of one colour each ("" for automatic/black), paragraph breaks kept.</summary>
    internal static List<(string Colour, string Text)> Segments(XDocument doc)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var segments = new List<(string Colour, StringBuilder Text)>();
        void Add(string colour, string text)
        {
            if (text.Length == 0)
            {
                return;
            }
            // Spaces and line breaks don't change who's speaking.
            if (segments.Count > 0 && (segments[^1].Colour == colour || text.Trim().Length == 0))
            {
                segments[^1].Text.Append(text);
                return;
            }
            segments.Add((colour, new StringBuilder(text)));
        }
        foreach (XElement p in doc.Descendants(w + "p"))
        {
            string paragraphColour = Colour(p.Element(w + "pPr")?.Element(w + "rPr"), w);
            foreach (XElement r in p.Descendants(w + "r"))
            {
                string colour = Colour(r.Element(w + "rPr"), w);
                if (colour.Length == 0)
                {
                    colour = paragraphColour;
                }
                var sb = new StringBuilder();
                foreach (XElement node in r.Elements())
                {
                    if (node.Name == w + "t")
                    {
                        sb.Append(node.Value);
                    }
                    else if (node.Name == w + "tab")
                    {
                        sb.Append('\t');
                    }
                    else if (node.Name == w + "br" || node.Name == w + "cr")
                    {
                        sb.Append('\n');
                    }
                }
                Add(colour, sb.ToString());
            }
            Add(segments.Count > 0 ? segments[^1].Colour : "", "\n");
        }
        return segments.Select(x => (x.Colour, x.Text.ToString())).ToList();
    }

    private static string Colour(XElement? rPr, XNamespace w)
    {
        // Text colour first; highlighted or shaded text counts too (some chats are coloured that way).
        string v = ((string?)rPr?.Element(w + "color")?.Attribute(w + "val") ?? "").Trim().ToUpperInvariant();
        if (v.Length > 0 && v != "AUTO" && v != "000000")
        {
            return v;
        }
        string h = ((string?)rPr?.Element(w + "highlight")?.Attribute(w + "val") ?? "").Trim().ToUpperInvariant();
        if (h.Length > 0 && h != "NONE")
        {
            return "HL-" + h;
        }
        string fill = ((string?)rPr?.Element(w + "shd")?.Attribute(w + "fill") ?? "").Trim().ToUpperInvariant();
        return fill.Length > 0 && fill != "AUTO" && fill != "FFFFFF" ? "SH-" + fill : "";
    }

    private static readonly Regex LeadingLabel = new(@"^\s*([^:\n\r]{1,40}?)\s*:\s", RegexOptions.CultureInvariant);

    /// <summary>
    /// Labels each coloured piece with its colour's speaker, learnt from the first piece of that colour that starts
    /// with "Name:". Without any learnt label the text is returned unchanged.
    /// </summary>
    internal static string LabelByColour(List<(string Colour, string Text)> segments)
    {
        var names = new Dictionary<string, string>();
        foreach ((string colour, string text) in segments)
        {
            if ((names.TryGetValue(colour, out string? known) && known.Length > 0) || text.Trim().Length == 0)
            {
                continue;
            }
            // The first line in this colour that starts with "Name:" names it (a heading before it doesn't count).
            Match? m = text.Split('\n').Select(line => LeadingLabel.Match(line + " ")).FirstOrDefault(x => x.Success && !x.Groups[1].Value.Contains("http", StringComparison.OrdinalIgnoreCase));
            names[colour] = m != null ? m.Groups[1].Value.Trim() : "";
        }
        // Colours only count when at least two speakers were labelled (one colour alone is just formatting).
        bool useColours = names.Values.Count(n => n.Length > 0) >= 2 || (names.Values.Count(n => n.Length > 0) == 1 && names.Count >= 2);
        var sb = new StringBuilder();
        foreach ((string colour, string text) in segments)
        {
            if (!useColours || !names.TryGetValue(colour, out string? name) || name.Length == 0 || text.Trim().Length == 0)
            {
                sb.Append(text);
                continue;
            }
            if (sb.Length > 0 && sb[^1] != '\n')
            {
                sb.Append('\n');
            }
            // Already labelled where you wrote the name in: keep it as you wrote it.
            if (text.Split('\n').Any(line => LeadingLabel.Match(line + " ") is { Success: true } x && x.Groups[1].Value.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                sb.Append(text.TrimStart(' ', '\t'));
                continue;
            }
            sb.Append(name).Append(": ").Append(text.TrimStart(' ', '\t', '\n'));
        }
        return sb.ToString();
    }

    public static string FromHtml(string html)
    {
        string s = Regex.Replace(html, @"(?is)<(script|style|head)\b.*?</\1>", " ");
        s = Regex.Replace(s, @"(?i)<br\s*/?>|</(p|div|li|h[1-6]|tr|blockquote|section|article)>", "\n");
        s = Regex.Replace(s, "<[^>]+>", " ");
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"[ \t\u00A0]+", " ");
        s = Regex.Replace(s, @" *\n *", "\n");
        return s;
    }

    public static string FromRtf(string rtf)
    {
        string s = Regex.Replace(rtf, @"\\par[d]?\b ?", "\n");
        s = Regex.Replace(s, @"\\'([0-9a-fA-F]{2})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        s = Regex.Replace(s, @"\\[a-zA-Z]+-?\d* ?", "");
        s = s.Replace("{", "").Replace("}", "");
        return s;
    }

    // "Ru: hello", "**Ru:** hello", "[Ru] hello", and ChatGPT's copied "You said:" / "ChatGPT said:" on a line of their own.
    private static readonly Regex Labelled = new(@"^\s*(?:\*\*|__)?\[?(?<name>[\p{L}][\p{L}\p{N} .'_-]{0,29}?)\]?(?:\*\*|__)?\s*[:：](?:\*\*|__)?\s?(?<text>.*)$");
    private static readonly Regex SaidLine = new(@"^\s*(?<name>[\p{L}][\p{L}\p{N} .'_-]{0,29}?) said:\s*$");
    private static readonly Regex Clock = new(@"^\d{1,2}$");

    /// <summary>Words that often start a line with a colon but aren't people.</summary>
    private static readonly HashSet<string> NotSpeakers = new(StringComparer.OrdinalIgnoreCase)
    {
        "note", "notes", "edit", "update", "p.s", "ps", "example", "warning", "tip", "important", "summary", "title", "date", "time",
        "subject", "re", "from", "to", "cc", "http", "https", "step", "answer", "question", "q", "a", "source", "link", "tl;dr", "tldr"
    };

    private static (string Name, string Text)? Label(string line)
    {
        Match said = SaidLine.Match(line);
        if (said.Success)
        {
            return (said.Groups["name"].Value.Trim(), "");
        }
        Match m = Labelled.Match(line);
        if (!m.Success)
        {
            return null;
        }
        string name = m.Groups["name"].Value.Trim();
        if (NotSpeakers.Contains(name) || Clock.IsMatch(name) || name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 3)
        {
            return null;
        }
        return (name, m.Groups["text"].Value);
    }

    /// <summary>
    /// Who speaks in a pasted conversation, most lines first. Only names that start at least two lines count, and
    /// only when two or more people speak; a document that isn't a conversation has no speakers.
    /// </summary>
    public static List<string> Speakers(string text)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var spelling = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in Normalise(text).Split('\n'))
        {
            if (Label(line) is (string name, _))
            {
                counts[name] = counts.GetValueOrDefault(name) + 1;
                spelling.TryAdd(name, name);
            }
        }
        List<string> speakers = counts.Where(kv => kv.Value >= 2).OrderByDescending(kv => kv.Value).Select(kv => spelling[kv.Key]).ToList();
        if (speakers.Count < 2)
        {
            return new List<string>();
        }
        // A conversation is mostly its two main speakers; stray labels ("Chapter: 3") that turn up rarely are dropped.
        int top = counts[speakers[0]] + counts[speakers[1]];
        return speakers.Where((s, i) => i < 2 || counts[s] * 10 >= top).Take(6).ToList();
    }

    /// <summary>The conversation as lines, one per message. Lines without a known label belong to the message above them.</summary>
    public static List<SpokenLine> Lines(string text, IReadOnlyCollection<string> speakers)
    {
        var known = new HashSet<string>(speakers, StringComparer.OrdinalIgnoreCase);
        var lines = new List<SpokenLine>();
        string? who = null;
        var sb = new StringBuilder();
        void Flush()
        {
            if (who != null && sb.ToString().Trim().Length > 0)
            {
                lines.Add(new SpokenLine(who, sb.ToString().Trim()));
            }
            sb.Clear();
        }
        foreach (string line in Normalise(text).Split('\n'))
        {
            if (Label(line) is (string name, string rest) && known.Contains(name))
            {
                Flush();
                who = speakers.First(s => s.Equals(name, StringComparison.OrdinalIgnoreCase));
                sb.Append(rest);
                continue;
            }
            if (who != null)
            {
                sb.Append('\n').Append(line);
            }
        }
        Flush();
        return lines;
    }

    /// <summary>
    /// Pairs the user's messages with the companion's replies, as her recall memory keeps them. Messages in a row
    /// from one side are joined; anyone else's lines are left out.
    /// </summary>
    public static List<(string User, string Reply)> Pairs(IEnumerable<SpokenLine> lines, string userSpeaker, string companionSpeaker)
    {
        var pairs = new List<(string, string)>();
        string? user = null, reply = null;
        foreach (SpokenLine l in lines)
        {
            if (l.Speaker.Equals(userSpeaker, StringComparison.OrdinalIgnoreCase))
            {
                if (reply != null)
                {
                    pairs.Add((user ?? "", reply));
                    user = null;
                    reply = null;
                }
                user = user == null ? l.Text : user + "\n\n" + l.Text;
            }
            else if (l.Speaker.Equals(companionSpeaker, StringComparison.OrdinalIgnoreCase))
            {
                reply = reply == null ? l.Text : reply + "\n\n" + l.Text;
            }
        }
        if (reply != null)
        {
            pairs.Add((user ?? "", reply));
        }
        return pairs;
    }

    /// <summary>
    /// Cuts text into parts of at most <paramref name="maxChars"/>, at paragraph or line breaks where it can (and
    /// at a message start in a conversation), so no message is split unless one message alone is too long.
    /// </summary>
    public static List<string> Chunk(string text, int maxChars, IReadOnlyCollection<string>? speakers = null)
    {
        if (maxChars < 200)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChars), "Parts must be at least 200 characters.");
        }
        text = Normalise(text);
        var parts = new List<string>();
        if (text.Length == 0)
        {
            return parts;
        }
        var known = new HashSet<string>(speakers ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        // Units are messages in a conversation, paragraphs otherwise.
        var units = new List<string>();
        var sb = new StringBuilder();
        foreach (string line in text.Split('\n'))
        {
            bool starts = known.Count > 0 ? Label(line) is (string n, _) && known.Contains(n) : line.Trim().Length == 0;
            if (starts && sb.Length > 0)
            {
                units.Add(sb.ToString());
                sb.Clear();
            }
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }
            sb.Append(line);
        }
        if (sb.Length > 0)
        {
            units.Add(sb.ToString());
        }
        var part = new StringBuilder();
        foreach (string u in units.SelectMany(u => u.Length > maxChars ? Split(u, maxChars) : new[] { u }))
        {
            if (part.Length > 0 && part.Length + 1 + u.Length > maxChars)
            {
                parts.Add(part.ToString().Trim());
                part.Clear();
            }
            if (part.Length > 0)
            {
                part.Append('\n');
            }
            part.Append(u);
        }
        if (part.ToString().Trim().Length > 0)
        {
            parts.Add(part.ToString().Trim());
        }
        return parts.Where(p => p.Length > 0).ToList();
    }

    /// <summary>One very long message or paragraph, cut at sentence ends or spaces.</summary>
    private static IEnumerable<string> Split(string s, int max)
    {
        int i = 0;
        while (i < s.Length)
        {
            int len = Math.Min(max, s.Length - i);
            if (i + len < s.Length)
            {
                int cut = s.LastIndexOfAny(new[] { '.', '!', '?', '\n' }, i + len - 1, len);
                if (cut <= i + max / 2)
                {
                    cut = s.LastIndexOf(' ', i + len - 1, len);
                }
                if (cut > i + max / 2)
                {
                    len = cut - i + 1;
                }
            }
            yield return s.Substring(i, len);
            i += len;
        }
    }
}
