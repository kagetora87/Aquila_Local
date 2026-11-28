using AquilaLocal.Companion.Living;
using Xunit;

namespace Aquila.Companion.Tests;

public sealed class PresenceTests
{
    [Fact]
    public void TheMoodTagCarriesWhatSheTouchedAndHerReadOnThem()
    {
        (List<string> about, string reading) = MoodMarkup.Extra("Hi. <mood strength=\"6\" why=\"he came back\" about=\"Loneliness, wanting\" reading=\"tired, wants comfort\">tender</mood>");
        Assert.Equal(new[] { "loneliness", "wanting" }, about);
        Assert.Equal("tired, wants comfort", reading);
        Assert.Equal("tender", MoodMarkup.Read("x <mood strength=\"6\" about=\"a\">tender</mood>", DateTimeOffset.Now)!.Word);
        Assert.Empty(MoodMarkup.Extra("no tag").About);
    }

    [Fact]
    public void ThemesThatKeepComingBackAreLetRest()
    {
        var recent = new List<IReadOnlyList<string>> { new[] { "loneliness" }, new[] { "work" }, new[] { "loneliness", "wanting" }, Array.Empty<string>() };
        string cue = Presence.RestCue(recent);
        Assert.Contains("loneliness", cue);
        Assert.DoesNotContain("work", cue);
        Assert.Equal("", Presence.RestCue(recent, "I've been feeling such loneliness lately"));
        Assert.Equal("", Presence.RestCue(new List<IReadOnlyList<string>> { new[] { "work" }, new[] { "past" } }));
    }

    [Fact]
    public void PetPhrasesAreNoticed()
    {
        var replies = new List<string>
        {
            "I'm so afraid to want things, you know.",
            "Sometimes I'm afraid to want things too much.",
            "Honestly? Still afraid to want things. <mood strength=\"5\">wistful</mood>",
            "Let's get pancakes."
        };
        List<string> worn = Presence.Overused(replies);
        Assert.Contains("afraid to want things", worn);
        Assert.DoesNotContain(worn, w => w == "afraid to want");
        Assert.Contains("afraid to want things", Presence.PhraseCue(replies));
        Assert.Equal("", Presence.PhraseCue(new[] { "a b c d", "e f g h" }));
    }

    [Fact]
    public void TodayHasAStateAndADayOfHerOwn()
    {
        DailyState? s = DailyState.Parse("{\"energy\":3,\"social\":12,\"lean\":\"quiet and inward\",\"slept\":\"badly, after a dream\",\"plan\":[{\"time\":\"9:00\",\"what\":\"coffee and the crossword\"},{\"time\":\"2:30 pm\",\"what\":\"pottery class\"}]}", "2026-11-28");
        Assert.NotNull(s);
        Assert.Equal(3, s!.Energy);
        Assert.Equal(10, s.Social);
        string line = s.Line(new DateTimeOffset(2026, 11, 28, 12, 0, 0, TimeSpan.Zero));
        Assert.Contains("Your day so far: 9:00 coffee and the crossword", line);
        Assert.Contains("Still to come: 2:30 pm pottery class", line);
        Assert.Equal(14 * 60 + 30, DailyState.Minutes("2:30 pm"));
        Assert.Equal(0, DailyState.Minutes("12am"));
        Assert.Null(DailyState.Parse("nope", "x"));

        string root = Path.Combine(Path.GetTempPath(), "aquila-today-" + Guid.NewGuid().ToString("N"));
        try
        {
            DailyStateStore store = new(root);
            store.Save("mae", s);
            Assert.Equal("quiet and inward", store.Load("mae", new DateTimeOffset(2026, 11, 28, 20, 0, 0, TimeSpan.Zero))!.Lean);
            Assert.Null(store.Load("mae", new DateTimeOffset(2026, 11, 29, 8, 0, 0, TimeSpan.Zero)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RecentWakingThoughtsAreOnHerMind()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        var journal = new List<JournalEntry>
        {
            new() { Kind = ThoughtKinds.Dream, Text = "a dream", At = now.AddHours(-3) },
            new() { Kind = ThoughtKinds.Curiosity, Text = "Why do cats knead?", At = now.AddHours(-1) },
            new() { Kind = ThoughtKinds.Thought, Text = "old", At = now.AddDays(-5) }
        };
        string cue = Presence.OnMindCue(journal, now);
        Assert.Contains("cats knead", cue);
        Assert.DoesNotContain("a dream", cue);
        Assert.DoesNotContain("old", cue);
    }
}
