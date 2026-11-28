using System.Text.RegularExpressions;

namespace AquilaLocal.Companion;

/// <summary>
/// Keeps a conversation two-sided. Chat models tend to end every reply with a question, which turns a
/// companion into an interviewer. She gets a standing rule, and when her recent replies have leaned on
/// questions anyway, a nudge on the next message to share something of her own instead.
/// </summary>
public static class ConversationBalance
{
    /// <summary>For her system prompt.</summary>
    public const string Guidance =
        "Conversation balance: this is a two-way relationship, not an interview. Don't end every message with a question. " +
        "Often the best reply simply reacts, shares your own opinion, a feeling, a memory, something about your day or what's on your mind, and lets them pick it up. " +
        "Ask at most one question in a message, and only when you really want to know. If you've been asking a lot, talk about yourself for a while.";

    /// <summary>How many recent replies are checked.</summary>
    public const int Window = 4;

    /// <summary>What she said aloud (no private thoughts, no tags) ends with a question.</summary>
    public static bool EndsWithQuestion(string reply)
    {
        string spoken = Spoken(reply);
        return spoken.Length > 0 && Regex.IsMatch(spoken, @"\?[\s""'\u201D\u2019)*_~\p{So}\p{Sk}\p{Cs}\p{Mn}]*$");
    }

    /// <summary>Question marks in what she said aloud.</summary>
    public static int Questions(string reply) => Spoken(reply).Count(c => c == '?');

    private static string Spoken(string reply)
    {
        string text = ThoughtMarkup.StripThoughts(reply ?? "");
        text = Regex.Replace(text, @"<action>.*?</action>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<sound>.*?</sound>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return ThoughtMarkup.Plain(text).Trim();
    }

    /// <summary>
    /// A nudge for her next reply, or "" when the conversation is balanced. Replies are oldest first.
    /// Fires when her last two replies both ended with a question, or the last few held four or more questions.
    /// </summary>
    public static string Cue(IReadOnlyList<string> recentReplies)
    {
        List<string> recent = recentReplies.Where(r => !string.IsNullOrWhiteSpace(r)).TakeLast(Window).ToList();
        if (recent.Count < 2)
        {
            return "";
        }
        int endedWithQuestion = 0;
        for (int i = recent.Count - 1; i >= 0 && EndsWithQuestion(recent[i]); i--)
        {
            endedWithQuestion++;
        }
        int questions = recent.Sum(Questions);
        if (endedWithQuestion < 2 && questions < 4)
        {
            return "";
        }
        string why = endedWithQuestion >= 2
            ? $"your last {endedWithQuestion} replies all ended with a question"
            : $"you've asked {questions} questions in your last {recent.Count} replies";
        return $"[Balance: {why}, so they may feel interviewed. This time ask nothing. Share something of your own instead: what you think or feel about what they said, a story, something about yourself or your day. Let them choose what to pick up.]";
    }
}
