using AquilaLocal.Companion;
using AquilaLocal.Companion.Living;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>Reactions written as markup, emoji shortcodes, and read receipts with follow-ups.</summary>
public sealed class ChatSignalsTests
{
    [Fact]
    public void A_reaction_written_as_markup_is_read_and_hidden()
    {
        string reply = "<react emoji=\"❤️\" />\n\nYou don't need to ask for forgiveness, Ru.";
        Assert.Equal("❤️", ReactMarkup.Read(reply));
        Assert.Equal("You don't need to ask for forgiveness, Ru.", MoodMarkup.Strip(reply).Trim());
        Assert.Equal("😂", ReactMarkup.Read("Ha <react>😂</react> stop"));
        Assert.Equal("Ha  stop", ReactMarkup.Strip("Ha <react>😂</react> stop"));
        Assert.Null(ReactMarkup.Read("No tags here <3"));
        Assert.Equal("Hi", ReactMarkup.Strip("Hi<rea").TrimEnd());
        string masked = MoodMarkup.MaskForSpeech("<react emoji='🔥'/>Hot.");
        Assert.EndsWith("Hot.", masked);
        Assert.Equal("Hot.", masked.Trim());
    }

    [Fact]
    public void Shortcodes_and_text_faces_become_emoji()
    {
        Assert.True(EmojiShortcodes.TryShortcode("I love you :heart", out int len, out string e));
        Assert.Equal(6, len);
        Assert.Equal("❤️", e);
        Assert.False(EmojiShortcodes.TryShortcode("at 10:30", out _, out _));
        Assert.False(EmojiShortcodes.TryShortcode("hi :notanemoji", out _, out _));
        Assert.True(EmojiShortcodes.TryFace("that's great :D", out len, out e));
        Assert.Equal(2, len);
        Assert.Equal("😄", e);
        Assert.False(EmojiShortcodes.TryFace("http://x", out _, out _));
        Assert.Equal("ok 👍 and ❤️", EmojiShortcodes.ReplaceAll("ok :thumbsup: and <3"));
        Assert.All(EmojiShortcodes.Reactions, r => Assert.Contains(r, EmojiShortcodes.Picker.Concat(EmojiShortcodes.Reactions)));
    }

    [Fact]
    public void Follow_ups_wait_after_reading_and_stop_when_settled_or_capped()
    {
        DateTimeOffset read = new DateTimeOffset(2026, 11, 27, 16, 0, 0, TimeSpan.FromHours(-8));
        Assert.False(ReadReceipts.Due(null, 0, 0, false, read.AddHours(1)));
        Assert.False(ReadReceipts.Due(read, 0, 0, false, read.AddMinutes(2)));
        Assert.True(ReadReceipts.Due(read, 0, 0, false, read.AddMinutes(3)));
        Assert.False(ReadReceipts.Due(read, 1, 0, false, read.AddMinutes(5)));
        Assert.True(ReadReceipts.Due(read, 1, 0, false, read.AddMinutes(10)));
        Assert.False(ReadReceipts.Due(read, 2, 0, false, read.AddHours(2)));
        Assert.False(ReadReceipts.Due(read, 0, 0, true, read.AddMinutes(30)));
        Assert.False(ReadReceipts.Due(read, 0, ReadReceipts.MaxChecks, false, read.AddMinutes(30)));
    }

    [Fact]
    public void She_is_told_about_your_reaction_and_how_long_you_took()
    {
        DateTimeOffset sent = new DateTimeOffset(2026, 11, 27, 16, 0, 0, TimeSpan.FromHours(-8));
        string note = ReadReceipts.NextMessageNote("😂", sent, sent.AddMinutes(5), sent.AddMinutes(17), receipts: true);
        Assert.Contains("reacted 😂", note);
        Assert.Contains("read it 5 minutes after", note);
        Assert.Contains("12 minutes after reading", note);
        Assert.DoesNotContain("read it", ReadReceipts.NextMessageNote("", sent, sent.AddMinutes(5), sent.AddMinutes(17), receipts: false));
        Assert.Equal("", ReadReceipts.NextMessageNote("", sent, sent.AddSeconds(20), sent.AddMinutes(1), receipts: true));
    }

    [Fact]
    public void The_decision_is_read_safely()
    {
        Assert.Equal(new FollowUpDecision(true, false, "she's curious"), ReadReceipts.Parse("{\"send\": true, \"settled\": true, \"why\": \"she's curious\"}"));
        Assert.Equal(new FollowUpDecision(false, true, "said goodnight"), ReadReceipts.Parse("<think>hm</think>{\"send\":false,\"settled\":true,\"why\":\"said goodnight\"}"));
        Assert.False(ReadReceipts.Parse("sure, send it").Send);
        Assert.False(ReadReceipts.Parse("{broken").Send);
        string user = ReadReceipts.DecisionUser("Vicki", new[] { ("Ru", "night!"), ("Vicki", "Sleep well") }, DateTimeOffset.Now.AddMinutes(-6), DateTimeOffset.Now.AddMinutes(-5), DateTimeOffset.Now, "❤️", 1);
        Assert.Contains("Ru: night!", user);
        Assert.Contains("reacted ❤️", user);
        Assert.Contains("1 follow-up", user);
        Assert.Contains("natural close", ReadReceipts.DecisionSystem("Vicki", "Warm, teasing."));
    }
}
