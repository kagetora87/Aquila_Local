using AquilaLocal.Companion;
using AquilaLocal.Companion.Living;
using Xunit;

namespace Aquila.Companion.Tests;

public class MoodTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 9, 0, 0, TimeSpan.FromHours(-8));

    [Fact]
    public void ReadsAndStripsHerMoodTag()
    {
        string reply = "<action>I grin</action> Okay, you win.\n<mood strength=\"7\" why=\"he finally laughed at my joke\">Playful</mood>";
        MoodState? mood = MoodMarkup.Read(reply, Now);
        Assert.NotNull(mood);
        Assert.Equal("playful", mood!.Word);
        Assert.Equal(7, mood.Strength);
        Assert.Equal("he finally laughed at my joke", mood.Why);
        Assert.Equal("<action>I grin</action> Okay, you win.", MoodMarkup.Strip(reply));
    }

    [Fact]
    public void HidesAHalfWrittenTagWhileStreaming()
    {
        Assert.Equal("Hi there.", MoodMarkup.Strip("Hi there.\n<mo"));
        Assert.Equal("Hi there.", MoodMarkup.Strip("Hi there.\n<mood strength=\"5\" why=\"just"));
        Assert.Equal("Hi there.", MoodMarkup.Strip("Hi there.\n<mood strength=\"5\" why=\"x\">calm</mo"));
        Assert.Equal("I <3 you", MoodMarkup.Strip("I <3 you"));
        Assert.Equal("<action>waves</action>", MoodMarkup.Strip("<action>waves</action>"));
    }

    [Fact]
    public void MaskKeepsLengthForVoiceCues()
    {
        string reply = "Hello.<mood strength=\"4\" why=\"x\">calm</mood>";
        Assert.Equal(reply.Length, MoodMarkup.MaskForSpeech(reply).Length);
        Assert.Equal("Hello.", MoodMarkup.MaskForSpeech(reply).TrimEnd());
    }

    [Fact]
    public void NoTagMeansNoMood()
    {
        Assert.Null(MoodMarkup.Read("Just words.", Now));
        Assert.Null(MoodMarkup.Read("<mood strength=\"3\">  </mood>", Now));
    }

    [Fact]
    public void OddStrengthsAndPhrasesAreTidied()
    {
        MoodState? m = MoodMarkup.Read("x <mood why=\"eh\" strength=\"15/10\">a bit wistful!</mood>", Now);
        Assert.Equal("wistful", m!.Word);
        Assert.Equal(10, m.Strength);
        Assert.Equal(5, MoodMarkup.Read("<mood>calm</mood>", Now)!.Strength);
    }

    [Fact]
    public void MoodsFadeTowardCalm()
    {
        MoodState m = new("elated", 9, "", Now);
        Assert.Equal(9, MoodRules.Decayed(m, Now));
        Assert.Equal(7, MoodRules.Decayed(m, Now.AddHours(4)));
        Assert.Equal(MoodRules.Baseline, MoodRules.Decayed(m, Now.AddDays(2)));
        Assert.Equal(2, MoodRules.Decayed(m with { Strength = 2 }, Now.AddHours(10)));
    }

    [Fact]
    public void ThoughtsNudgeHerMoodButDontSwingIt()
    {
        MoodState strong = new("anxious", 8, "", Now);
        Assert.False(MoodRules.Replaces(strong, new MoodState("content", 4, "", Now.AddHours(1), MoodSources.Thought), Now.AddHours(1)));
        Assert.True(MoodRules.Replaces(strong, new MoodState("anxious", 4, "", Now.AddHours(1), MoodSources.Thought), Now.AddHours(1)));
        Assert.True(MoodRules.Replaces(strong, new MoodState("content", 4, "", Now.AddHours(9), MoodSources.Dream), Now.AddHours(9)));
        Assert.True(MoodRules.Replaces(strong, new MoodState("content", 2, "", Now, MoodSources.Chat), Now));
        Assert.True(MoodRules.Replaces(null, new MoodState("content", 2, "", Now, MoodSources.Thought), Now));
    }

    [Fact]
    public void StrongDreamsAreWorthTelling()
    {
        Assert.True(MoodRules.WorthTelling(8, 0.1));
        Assert.True(MoodRules.WorthTelling(3, 0.55));
        Assert.False(MoodRules.WorthTelling(5, 0.3));
    }

    [Fact]
    public void MoodLogKeepsTheLatestAndAppliesTheRules()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mood-" + Guid.NewGuid().ToString("N"));
        try
        {
            MoodLog log = new(dir);
            Assert.Null(log.Current("vicki"));
            log.Append("vicki", new MoodState("anxious", 8, "a nightmare", Now, MoodSources.Dream));
            MoodState kept = log.Offer("vicki", new MoodState("bored", 3, "", Now.AddMinutes(30), MoodSources.Thought), Now.AddMinutes(30));
            Assert.Equal("anxious", kept.Word);
            Assert.Equal("anxious", log.Current("vicki")!.Word);
            log.Offer("vicki", new MoodState("relieved", 8, "", Now.AddHours(1), MoodSources.Thought), Now.AddHours(1));
            Assert.Equal("relieved", log.Current("vicki")!.Word);
            Assert.Throws<PersonaConfigException>(() => log.Current("../x"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ToTellIsKeptThenLetGo()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tell-" + Guid.NewGuid().ToString("N"));
        try
        {
            ToTellStore store = new(dir);
            Assert.Null(store.Load("vicki", Now));
            store.Save("vicki", new ToTell("j1", Now, ThoughtKinds.Dream, "I was flying over the city.", "tell him about flying", "exhilarated", 1.4));
            Assert.Equal("j1", store.Load("vicki", Now.AddHours(10))!.JournalId);
            Assert.Contains("flying over the city", ToTellStore.Reminder(store.Load("vicki", Now)!, Now.AddHours(3)));
            Assert.Contains("dream", ToTellStore.Cue(store.Load("vicki", Now)!, Now.AddHours(3), TimeSpan.FromHours(9)));
            Assert.Null(store.Load("vicki", Now.AddHours(37)));
            Assert.False(File.Exists(Path.Combine(dir, "vicki", "to-tell.json")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ThoughtsCarryAMood()
    {
        InnerThought t = InnerLifePrompt.Parse("{\"kind\":\"dream\",\"text\":\"I'm underwater and can breathe.\",\"urge_to_message\":0.6,\"message_idea\":\"\",\"mood\":{\"word\":\"Awed\",\"strength\":7,\"why\":\"breathing underwater felt like magic\"}}");
        Assert.Equal("awed", t.MoodWord);
        Assert.Equal(7, t.MoodStrength);
        InnerThought none = InnerLifePrompt.Parse("{\"kind\":\"thought\",\"text\":\"Hm.\"}");
        Assert.Equal("", none.MoodWord);
        Assert.Equal(0, none.MoodStrength);
    }

    [Fact]
    public void HerSystemPromptTeachesTheTagOnce()
    {
        Assert.Contains("<mood strength=", MoodMarkup.Guidance);
        Assert.Contains("wistful, 6/10", MoodMarkup.TurnLine(new MoodState("wistful", 6, "rain", Now), Now));
    }
}
