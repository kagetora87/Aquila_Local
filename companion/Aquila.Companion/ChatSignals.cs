using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AquilaLocal.Companion;

/// <summary>
/// A reaction she writes into her reply as markup (<c>&lt;react emoji="❤️" /&gt;</c>) instead of calling the react
/// tool, which some models (Gemma 4) do: it's taken as her reaction and never shown or spoken as text.
/// </summary>
public static class ReactMarkup
{
    private static readonly Regex Tag = new(
        @"<\s*react\b[^>]*?\bemoji\s*=\s*[""'“”]?(?<e>[^""'“”/>\s]+)[""'“”]?[^>]*?/?\s*>(?:\s*</\s*react\s*>)?|<\s*react\s*>(?<e2>[^<]{1,16})</\s*react\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A tag still being written at the end of a streamed reply (hidden until it's complete).</summary>
    private static readonly Regex Partial = new(@"<\s*r(?:e(?:a(?:c(?:t\b[^>]*)?)?)?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The first reaction in the text (the emoji as written), or null.</summary>
    public static string? Read(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('<'))
        {
            return null;
        }
        Match m = Tag.Match(text);
        if (!m.Success)
        {
            return null;
        }
        string e = (m.Groups["e"].Success ? m.Groups["e"].Value : m.Groups["e2"].Value).Trim();
        return e.Length == 0 ? null : e;
    }

    /// <summary>Tags become blanks of the same length (for the voice, so cue positions don't move).</summary>
    public static string Mask(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('<'))
        {
            return text ?? "";
        }
        return Tag.Replace(text, m => new string(' ', m.Length));
    }

    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('<'))
        {
            return text ?? "";
        }
        string s = Tag.Replace(text, "");
        s = Partial.Replace(s, "");
        // A tag on its own line leaves an empty line behind.
        s = Regex.Replace(s, @"^\s*\n", "");
        return s;
    }
}

/// <summary>Typing :heart: or :) in the message box turns it into the emoji, like chat apps do.</summary>
public static class EmojiShortcodes
{
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["heart"] = "❤️", ["hearts"] = "💕", ["love"] = "🥰", ["heart_eyes"] = "😍", ["kiss"] = "😘",
        ["blush"] = "😊", ["smile"] = "🙂", ["grin"] = "😁", ["joy"] = "😂", ["lol"] = "😂",
        ["rofl"] = "🤣", ["laugh"] = "😆", ["wink"] = "😉", ["smirk"] = "😏", ["tongue"] = "😛",
        ["cool"] = "😎", ["sunglasses"] = "😎", ["thinking"] = "🤔", ["hmm"] = "🤔", ["shrug"] = "🤷",
        ["eyes"] = "👀", ["eyeroll"] = "🙄", ["roll_eyes"] = "🙄", ["flushed"] = "😳", ["blushing"] = "😳",
        ["pleading"] = "🥺", ["puppy"] = "🥺", ["sad"] = "🙁", ["cry"] = "😢", ["sob"] = "😭",
        ["angry"] = "😠", ["rage"] = "😡", ["pout"] = "😤", ["shock"] = "😮", ["wow"] = "😮",
        ["scream"] = "😱", ["sweat"] = "😅", ["nervous"] = "😬", ["grimace"] = "😬", ["sleepy"] = "😴",
        ["sleep"] = "😴", ["yawn"] = "🥱", ["hug"] = "🤗", ["hugs"] = "🤗", ["devil"] = "😈",
        ["angel"] = "😇", ["halo"] = "😇", ["skull"] = "💀", ["dead"] = "💀", ["melt"] = "🫠",
        ["upside_down"] = "🙃", ["zipper"] = "🤐", ["shush"] = "🤫", ["facepalm"] = "🤦", ["sweat_smile"] = "😅",
        ["thumbsup"] = "👍", ["+1"] = "👍", ["thumbsdown"] = "👎", ["-1"] = "👎", ["ok"] = "👌",
        ["clap"] = "👏", ["wave"] = "👋", ["pray"] = "🙏", ["please"] = "🙏", ["muscle"] = "💪",
        ["fingers_crossed"] = "🤞", ["point_up"] = "☝️", ["peace"] = "✌️", ["handshake"] = "🤝", ["raised_hands"] = "🙌",
        ["fire"] = "🔥", ["sparkles"] = "✨", ["star"] = "⭐", ["100"] = "💯", ["tada"] = "🎉",
        ["party"] = "🥳", ["rose"] = "🌹", ["flower"] = "🌸", ["sun"] = "☀️", ["moon"] = "🌙",
        ["coffee"] = "☕", ["tea"] = "🍵", ["wine"] = "🍷", ["beer"] = "🍺", ["cake"] = "🍰",
        ["pizza"] = "🍕", ["cat"] = "🐱", ["dog"] = "🐶", ["rain"] = "🌧️", ["snow"] = "❄️",
        ["music"] = "🎵", ["book"] = "📖", ["gift"] = "🎁", ["kiss_mark"] = "💋", ["lips"] = "👄",
        ["broken_heart"] = "💔", ["purple_heart"] = "💜", ["black_heart"] = "🖤", ["blue_heart"] = "💙", ["sparkling_heart"] = "💖",
        ["check"] = "✅", ["x"] = "❌", ["question"] = "❓", ["exclamation"] = "❗", ["zzz"] = "💤",
        ["sweat_drops"] = "💦", ["peach"] = "🍑", ["eggplant"] = "🍆", ["salute"] = "🫡", ["smiling_imp"] = "😈"
    };

    /// <summary>Text faces, turned into emoji when followed by a space or the end of the message.</summary>
    public static readonly IReadOnlyDictionary<string, string> Faces = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [":)"] = "🙂", [":-)"] = "🙂", [":D"] = "😄", [":-D"] = "😄", [";)"] = "😉",
        [";-)"] = "😉", [":("] = "🙁", [":-("] = "🙁", [":'("] = "😢", [":P"] = "😛",
        [":p"] = "😛", [":-P"] = "😛", [":O"] = "😮", [":o"] = "😮", ["<3"] = "❤️",
        ["</3"] = "💔", [":*"] = "😘", ["XD"] = "😆", ["xD"] = "😆", [":|"] = "😐",
        [":/"] = "😕", ["^^"] = "😊", ["^_^"] = "😊", ["T_T"] = "😭", [">:("] = "😠"
    };

    /// <summary>The emoji offered in the pickers (message box and reactions).</summary>
    public static readonly IReadOnlyList<string> Picker = new[]
    {
        "❤️", "😂", "😍", "🥰", "😘", "😊", "😉", "😏",
        "😳", "🥺", "😮", "😢", "😭", "🙄", "🤔", "😅",
        "😈", "😇", "🤗", "😴", "👀", "🔥", "✨", "🎉",
        "👍", "👏", "🙏", "💯", "💋", "💕", "💔", "☕"
    };

    /// <summary>The quick reactions offered on her messages.</summary>
    public static readonly IReadOnlyList<string> Reactions = new[]
    {
        "❤️", "😂", "😍", "😮", "😢", "😳", "🥺", "😏", "🔥", "👍", "🙄", "👀"
    };

    private static readonly Regex Code = new(@"(?:^|(?<=[\s(]))(:[a-z0-9_+\-]{1,30})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The closing colon of :name: was just typed: when the text before it ends with ":name" and the name is known,
    /// gives how many characters to replace and the emoji.
    /// </summary>
    public static bool TryShortcode(string before, out int length, out string emoji)
    {
        length = 0;
        emoji = "";
        Match m = Code.Match(before ?? "");
        if (!m.Success || !Names.TryGetValue(m.Groups[1].Value[1..], out string? e))
        {
            return false;
        }
        length = m.Groups[1].Value.Length;
        emoji = e;
        return true;
    }

    /// <summary>A space (or Enter) was just typed: when the word before it is a text face, gives its length and emoji.</summary>
    public static bool TryFace(string before, out int length, out string emoji)
    {
        length = 0;
        emoji = "";
        string s = before ?? "";
        int start = s.Length;
        while (start > 0 && !char.IsWhiteSpace(s[start - 1]))
        {
            start--;
        }
        string word = s[start..];
        if (!Faces.TryGetValue(word, out string? e))
        {
            return false;
        }
        length = word.Length;
        emoji = e;
        return true;
    }

    /// <summary>Converts every shortcode and text face in a whole message (for pasted text).</summary>
    public static string ReplaceAll(string text)
    {
        string s = Regex.Replace(text ?? "", @"(?<=^|[\s(]):([a-z0-9_+\-]{1,30}):", m => Names.TryGetValue(m.Groups[1].Value, out string? e) ? e : m.Value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.Replace(s, @"(?<=^|\s)(\S{2,4})(?=\s|$)", m => Faces.TryGetValue(m.Value, out string? e) ? e : m.Value);
    }
}

/// <summary>What she decided about following up on a message you've read but not answered.</summary>
public sealed record FollowUpDecision(bool Send, bool Settled, string Why);

/// <summary>
/// "Read" receipts and follow-ups: she knows when you've read her message, sees your reactions, and, if you leave
/// her on read, may send a short message of her own, if that's what she'd do. A small side request decides it
/// every couple of minutes; it's told to stay quiet when the talk has wound down.
/// </summary>
public static class ReadReceipts
{
    /// <summary>How long after you read her message (by follow-ups already sent) before she may follow up.</summary>
    public static readonly IReadOnlyList<TimeSpan> Waits = new[] { TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(10) };

    public static int MaxFollowUps => Waits.Count;

    /// <summary>Checks per message before it's left alone (one every couple of minutes, so about 20 minutes).</summary>
    public const int MaxChecks = 10;

    public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(2);

    /// <summary>Whether a check is due for her latest message.</summary>
    public static bool Due(DateTimeOffset? readAt, int followUpsSoFar, int checks, bool settled, DateTimeOffset now)
    {
        return readAt != null && !settled && checks < MaxChecks && followUpsSoFar < MaxFollowUps && now - readAt.Value >= Waits[followUpsSoFar];
    }

    /// <summary>
    /// What she's told with your next message about her last one: your reaction, and (with read receipts on) when
    /// you read it and how long you took to answer. Empty when there's nothing worth saying.
    /// </summary>
    public static string NextMessageNote(string? reaction, DateTimeOffset? sentAt, DateTimeOffset? readAt, DateTimeOffset now, bool receipts)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(reaction))
        {
            parts.Add("they reacted " + reaction.Trim() + " to it");
        }
        if (receipts && sentAt != null && readAt != null)
        {
            TimeSpan toRead = readAt.Value - sentAt.Value;
            TimeSpan toAnswer = now - readAt.Value;
            if (toRead >= TimeSpan.FromMinutes(2))
            {
                parts.Add("they read it " + Span(toRead) + " after you sent it");
            }
            if (toAnswer >= TimeSpan.FromMinutes(3))
            {
                parts.Add("they wrote this " + Span(toAnswer) + " after reading it");
            }
        }
        return parts.Count == 0 ? "" : "[About your last message: " + string.Join("; ", parts) + ".]";
    }

    public static string DecisionSystem(string name, string personality)
    {
        string sheet = (personality ?? "").Trim();
        if (sheet.Length > 3000)
        {
            sheet = sheet[..3000] + "…";
        }
        return
            "You are the instinct of " + name + ", a character in an ongoing chat. The other person has read " + name + "'s latest message and hasn't replied. "
            + "Decide whether " + name + " would send a short extra message right now.\n\n"
            + "Who " + name + " is:\n" + sheet + "\n\n"
            + "Send only if " + name + " genuinely would, given their personality, the relationship, the time of day, how long it's been and how the conversation stands. "
            + "Don't send when the conversation reached a natural close (goodbye, goodnight, see you, they said they were going to do something or be away, a thank-you or reaction that wraps things up), "
            + "when " + name + "'s last message didn't really call for an answer, or when a nudge would come across as needy or pushy for " + name + ". "
            + "After a follow-up that also went unanswered, only send again if " + name + " would truly be worried or impatient. "
            + "People differ: some wait, some tease, some check in, some worry; follow the personality.\n"
            + "Answer with JSON only: {\"send\": true or false, \"settled\": true or false, \"why\": \"a few words\"}. "
            + "settled is true when there's no point checking again later for this message (the talk is over, or a nudge would never fit).";
    }

    public static string DecisionUser(string name, IEnumerable<(string Who, string Text)> recent, DateTimeOffset sentAt, DateTimeOffset readAt, DateTimeOffset now, string? reaction, int followUpsSoFar)
    {
        var sb = new StringBuilder();
        sb.Append("Local time now: ").Append(now.ToString("dddd h:mm tt", CultureInfo.InvariantCulture)).Append(".\n\n");
        sb.Append("The conversation so far (oldest first):\n");
        foreach ((string who, string text) in recent)
        {
            string t = (text ?? "").Replace('\n', ' ').Trim();
            if (t.Length > 500)
            {
                t = t[..500] + "…";
            }
            sb.Append(who).Append(": ").Append(t).Append('\n');
        }
        sb.Append('\n').Append(name).Append("'s latest message was sent at ").Append(sentAt.ToString("h:mm tt", CultureInfo.InvariantCulture))
            .Append(". They read it at ").Append(readAt.ToString("h:mm tt", CultureInfo.InvariantCulture)).Append(", ").Append(Span(now - readAt)).Append(" ago, and haven't written back.");
        if (!string.IsNullOrWhiteSpace(reaction))
        {
            sb.Append(" They reacted ").Append(reaction.Trim()).Append(" to it.");
        }
        if (followUpsSoFar > 0)
        {
            sb.Append(' ').Append(name).Append(" has already sent ").Append(followUpsSoFar).Append(" follow-up").Append(followUpsSoFar > 1 ? "s" : "").Append(" without an answer.");
        }
        sb.Append("\n\nDecide now.");
        return sb.ToString();
    }

    /// <summary>Reads the decision; anything unclear counts as "don't send" (never a surprise message).</summary>
    public static FollowUpDecision Parse(string json)
    {
        string s = Regex.Replace(json ?? "", @"(?s)<think>.*?</think>", "").Trim();
        int open = s.IndexOf('{');
        int close = s.LastIndexOf('}');
        if (open < 0 || close <= open)
        {
            return new FollowUpDecision(false, false, "no decision");
        }
        try
        {
            JsonElement root = JsonDocument.Parse(s[open..(close + 1)]).RootElement;
            bool send = root.TryGetProperty("send", out JsonElement a) && (a.ValueKind == JsonValueKind.True || (a.ValueKind == JsonValueKind.String && a.GetString()!.Equals("true", StringComparison.OrdinalIgnoreCase)));
            bool settled = root.TryGetProperty("settled", out JsonElement b) && (b.ValueKind == JsonValueKind.True || (b.ValueKind == JsonValueKind.String && b.GetString()!.Equals("true", StringComparison.OrdinalIgnoreCase)));
            string why = root.TryGetProperty("why", out JsonElement w) && w.ValueKind == JsonValueKind.String ? w.GetString()!.Trim() : "";
            if (why.Length > 200)
            {
                why = why[..200];
            }
            return new FollowUpDecision(send, settled && !send, why);
        }
        catch (JsonException)
        {
            return new FollowUpDecision(false, false, "no decision");
        }
    }

    /// <summary>What prompts the follow-up she writes (in her full voice).</summary>
    public static string Cue(TimeSpan onRead, int followUpsSoFar, string why) =>
        "[No message from them: they read your last message " + Span(onRead) + " ago and haven't replied"
        + (followUpsSoFar > 0 ? ", not even to your follow-up" : "") + ". You've decided to send a short message of your own"
        + (why.Length > 0 ? " (" + why.TrimEnd('.') + ")" : "")
        + ". Write it the way you'd really text in this moment: brief, one or two short sentences, true to your personality and to how the conversation stands "
        + "(a nudge, a tease, a check-in, a worry or an afterthought, whatever fits). You may mention that they've seen it if you would. Don't repeat your last message and don't guilt-trip.]";

    /// <summary>Kept with the follow-up so later replies know why she wrote it.</summary>
    public static string Context(DateTimeOffset localNow, TimeSpan onRead) =>
        "[No message from them: you followed up on your own at " + localNow.ToString("h:mm tt", CultureInfo.InvariantCulture)
        + ", because they had read your last message " + Span(onRead) + " earlier without replying.]";

    public static string Span(TimeSpan t)
    {
        if (t < TimeSpan.FromMinutes(1))
        {
            return "under a minute";
        }
        if (t < TimeSpan.FromHours(1))
        {
            int m = (int)Math.Round(t.TotalMinutes);
            return m + (m == 1 ? " minute" : " minutes");
        }
        if (t < TimeSpan.FromDays(1))
        {
            double h = Math.Round(t.TotalHours * 2) / 2;
            return h.ToString("0.#", CultureInfo.InvariantCulture) + (h == 1 ? " hour" : " hours");
        }
        int d = (int)Math.Round(t.TotalDays);
        return d + (d == 1 ? " day" : " days");
    }
}
