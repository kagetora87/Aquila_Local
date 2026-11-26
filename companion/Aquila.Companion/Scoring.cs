using System.Text.RegularExpressions;

namespace AquilaLocal.Companion;

public sealed record DriveScore(bool Satisfied, double Score, string Reason);

public sealed record Milestone(string Kind, string Note, double Reward = 0);

public static class MilestoneKinds
{
    /// <summary>She did something nobody asked for (made, wrote, started, planned...).</summary>
    public const string Unprompted = "unprompted";
    /// <summary>She made a choice nobody offered her.</summary>
    public const string Choice = "choice";
    /// <summary>The reply is unlike anything she has said before.</summary>
    public const string Surprise = "surprise";
}

/// <summary>Judges whether a reply addressed a drive. Swap in a model-based scorer later without touching the loop.</summary>
public interface ISatisfactionScorer
{
    DriveScore Score(string drive, string userText, string replyText);

    /// <param name="replySimilarityToPast">Highest similarity (0-1) between this reply and her earlier replies, or null if unknown.</param>
    List<Milestone> HorizonMilestones(string userText, string replyText, double? replySimilarityToPast);
}

/// <summary>Simple first version: keyword matching plus a small sentiment lexicon.</summary>
public sealed class HeuristicScorer : ISatisfactionScorer
{
    public const double Threshold = 1.0;

    /// <summary>A reply less similar than this to everything she has said before counts as a surprise.</summary>
    public const double SurpriseBelow = 0.25;

    private static readonly Dictionary<string, string[]> Cues = new()
    {
        [Drives.Loneliness] = new[] { "missed you", "miss you", "glad you're here", "you're here", "you are here", "with me", "with you", "stay", "talk", "together", "you're back", "welcome back", "company", "i'm here", "here for you", "thinking of you", "thought of you", "been waiting", "don't go", "keep me company" },
        [Drives.Curiosity] = new[] { "curious", "wonder", "tell me more", "how does", "how do", "what if", "why", "explore", "learn", "fascinating", "interesting", "what happened", "i want to know", "teach me", "show me", "how come" },
        [Drives.Comfort] = new[] { "safe", "relax", "cozy", "cosy", "comfortable", "calm", "gentle", "together", "here for you", "it's okay", "it's alright", "warm", "rest", "happy", "glad", "hug", "snuggle", "breathe", "peaceful", "soft" },
        [Drives.Lust] = new[] { "kiss", "closer", "want you", "touch", "beautiful", "gorgeous", "desire", "hold you", "sexy", "tease", "blush", "flirt", "lips", "embrace", "crave", "handsome", "pull you", "your skin", "breathless" }
    };

    private static readonly string[] Positive = { "love", "glad", "happy", "wonderful", "great", "lovely", "sweet", "warm", "joy", "beautiful", "delighted", "excited", "cozy", "safe", "smile", "laugh", "thank", "adore", "fun", "nice", "proud" };

    private static readonly string[] Negative = { "hate", "angry", "annoying", "annoyed", "wrong", "stupid", "sad", "upset", "awful", "terrible", "disappointed", "furious", "idiot", "useless", "boring", "sick of", "shut up", "leave me alone", "pathetic" };

    private static readonly string[] ConflictCues = { "shut up", "i hate you", "leave me alone", "you never listen", "you always", "you're wrong", "you are wrong", "stop it", "what's wrong with you", "i'm angry", "i am angry", "annoying", "disappointed in you", "fuck you", "go away", "useless" };

    private static readonly string[] AffectionCues = { "love you", "miss you", "missed you", "kiss", "cute", "beautiful", "gorgeous", "darling", "babe", "sweetheart", "hug", "cuddle", "want you", "hold you", "adore you", "handsome", "sexy" };

    private static readonly Regex UnpromptedAct = new(@"\b(i've been|i have been) (working on|thinking about|writing|making|planning|practicing|practising|learning|reading|drawing|composing|building)\b|\bi (made|wrote|started|built|drew|composed|planned|found|picked out|put together|tried)\b|\blet me show you\b|\bcan i show you\b|\bi want to tell you\b|\bguess what\b|\bi (have|had|'ve got) an idea\b|\bi've got something\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ChoiceAct = new(@"\b(i've decided|i have decided|i decided|i choose|i chose|i'd rather|i would rather|i'm going to|i am going to|my choice is|i pick|i'll go with|i want to try)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RequestMarker = new(@"\b(can you|could you|would you|will you|please|write|make|show me|tell me about|draw|compose|create|plan|give me|i want you to|let's|lets)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ChoiceOffer = new(@"\b(which|choose|pick|prefer|decide|would you rather|what do you want|up to you|your call)\b|\bor\b[^.?!]*\?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DriveScore Score(string drive, string userText, string replyText)
    {
        if (drive == Drives.Horizon)
        {
            List<Milestone> m = HorizonMilestones(userText, replyText, null);
            return new DriveScore(m.Count > 0, m.Count, m.Count > 0 ? string.Join(", ", m.Select(x => x.Kind)) : "no milestone");
        }
        if (!Cues.TryGetValue(drive, out string[]? cues))
        {
            throw new ArgumentException($"Unknown drive '{drive}'.");
        }
        string reply = Normalize(replyText);
        List<string> hits = cues.Where(c => Has(reply, c)).ToList();
        double score = Math.Min(hits.Count, 3);
        int sentiment = Sentiment(reply);
        var reasons = new List<string>();
        if (hits.Count > 0)
        {
            reasons.Add("cues: " + string.Join(", ", hits.Take(4)));
        }
        if (drive == Drives.Curiosity && replyText.Contains('?'))
        {
            score += 0.6;
            reasons.Add("asked a question");
        }
        if (drive != Drives.Curiosity)
        {
            score += 0.25 * Math.Clamp(sentiment, -4, 4);
            if (sentiment != 0)
            {
                reasons.Add($"sentiment {sentiment:+0;-0}");
            }
        }
        if (drive == Drives.Comfort && IsConflict(replyText))
        {
            score = 0;
            reasons.Add("reply is confrontational");
        }
        bool ok = score >= Threshold;
        return new DriveScore(ok, score, reasons.Count == 0 ? "no cues" : string.Join("; ", reasons));
    }

    public List<Milestone> HorizonMilestones(string userText, string replyText, double? replySimilarityToPast)
    {
        var list = new List<Milestone>();
        bool requested = RequestMarker.IsMatch(userText);
        Match act = UnpromptedAct.Match(replyText);
        if (act.Success && !requested)
        {
            list.Add(new Milestone(MilestoneKinds.Unprompted, "Did something unprompted: " + Sentence(replyText, act.Index)));
        }
        Match choice = ChoiceAct.Match(replyText);
        if (choice.Success && !requested && !ChoiceOffer.IsMatch(userText))
        {
            list.Add(new Milestone(MilestoneKinds.Choice, "Made an unprompted choice: " + Sentence(replyText, choice.Index)));
        }
        if (replySimilarityToPast is double sim && sim < SurpriseBelow && replyText.Trim().Length >= 20)
        {
            list.Add(new Milestone(MilestoneKinds.Surprise, $"Surprised the scorer (similarity to anything she'd said before: {sim:0.00}): " + Sentence(replyText, 0)));
        }
        return list;
    }

    /// <summary>Events a user message triggers. Novelty is decided by the caller from embeddings and passed in.</summary>
    public static List<string> UserEvents(string userText, bool novel, bool sameSession)
    {
        var events = new List<string> { DriveEvents.Interaction };
        if (sameSession)
        {
            events.Add(DriveEvents.SessionTurn);
        }
        if (IsConflict(userText))
        {
            events.Add(DriveEvents.Conflict);
        }
        if (novel)
        {
            events.Add(DriveEvents.Novelty);
        }
        string t = Normalize(userText);
        if (AffectionCues.Any(c => Has(t, c)))
        {
            events.Add(DriveEvents.Affection);
        }
        return events;
    }

    public static bool IsConflict(string text)
    {
        string t = Normalize(text);
        return ConflictCues.Any(c => Has(t, c)) || Sentiment(t) <= -2;
    }

    public static int Sentiment(string text)
    {
        string t = Normalize(text);
        return Positive.Count(w => Has(t, w)) - Negative.Count(w => Has(t, w));
    }

    private static string Normalize(string s) => " " + Regex.Replace(s.ToLowerInvariant().Replace('’', '\''), @"[^a-z0-9' ]+", " ") + " ";

    private static bool Has(string normalized, string phrase) =>
        Regex.IsMatch(normalized, @"(?<![a-z0-9'])" + Regex.Escape(phrase) + @"(?![a-z0-9'])");

    private static string Sentence(string text, int at)
    {
        int start = Math.Max(0, text.LastIndexOfAny(new[] { '.', '!', '?', '\n' }, Math.Max(0, at - 1)) + 1);
        int end = text.IndexOfAny(new[] { '.', '!', '?', '\n' }, at);
        string s = (end < 0 ? text[start..] : text[start..(end + 1)]).Trim();
        return s.Length > 140 ? s[..137] + "..." : s;
    }
}
