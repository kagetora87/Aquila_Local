using AquilaLocal.Companion;
using AquilaLocal.Companion.Living;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>
/// Living mode: she thinks only when you've been away and the model is free, messages you first only when a
/// thought is strong enough (and not too often, not at night, not twice in a row), and her thoughts are kept
/// as her own, never confused with things said to you.
/// </summary>
public sealed class LivingTests
{
    private static readonly DateTimeOffset Now = TestPersona.T0;

    [Fact]
    public void She_thinks_only_when_you_are_away_and_the_model_is_free()
    {
        var policy = new LivingPolicy { MinMinutes = 15, MaxMinutes = 60, IdleMinutes = 10 };
        DateTimeOffset due = LivingSchedule.NextThought(Now.AddHours(-1), Now.AddHours(-5), policy, 1);
        Assert.Equal(Now, due);

        Assert.Null(LivingSchedule.WhyNotNow(Now, due, Now.AddMinutes(-11), policy, busy: false, engineReady: true));
        Assert.Contains("away", LivingSchedule.WhyNotNow(Now, due, Now.AddMinutes(-3), policy, false, true));
        Assert.Contains("reply", LivingSchedule.WhyNotNow(Now, due, Now.AddHours(-2), policy, true, true));
        Assert.Contains("load", LivingSchedule.WhyNotNow(Now, due, Now.AddHours(-2), policy, false, false));
        Assert.NotNull(LivingSchedule.WhyNotNow(Now, Now.AddMinutes(5), Now.AddHours(-2), policy, false, true));
    }

    [Fact]
    public void Thoughts_come_at_random_between_15_minutes_and_an_hour()
    {
        var policy = new LivingPolicy();
        Assert.Equal(Now.AddMinutes(15), LivingSchedule.NextThought(Now, Now, policy, 0));
        Assert.Equal(Now.AddMinutes(60), LivingSchedule.NextThought(Now, Now, policy, 1));
        Assert.Equal(Now.AddMinutes(37.5), LivingSchedule.NextThought(null, Now, policy, 0.5));
        Assert.Equal(Now.AddMinutes(120), LivingSchedule.NextThought(Now, Now, policy, 1, night: true));
        Assert.Equal(Now.AddMinutes(15), LivingSchedule.NextThought(Now, Now, new LivingPolicy { MinMinutes = 60, MaxMinutes = 15 }, 0));
    }

    [Theory]
    [InlineData(23, true)]
    [InlineData(3, true)]
    [InlineData(8, false)]
    [InlineData(20, false)]
    public void At_night_she_is_asleep(int hour, bool night)
    {
        Assert.Equal(night, LivingSchedule.IsNight(hour, new LivingPolicy()));
    }

    [Fact]
    public void Reads_her_dream_and_whether_she_is_in_it()
    {
        InnerThought d = InnerLifePrompt.Parse("{\"kind\":\"dream\",\"text\":\"I'm swimming through a library.\",\"she_is_in_it\":false}");
        Assert.Equal(ThoughtKinds.Dream, d.Kind);
        Assert.False(d.SheIsInIt);
        Assert.True(InnerLifePrompt.Parse("{\"kind\":\"dream\",\"text\":\"x\"}").SheIsInIt);
        Assert.Contains("asleep", InnerLifePrompt.DreamSystem("Vicki"));
    }

    [Theory]
    [InlineData(23, true)]
    [InlineData(2, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(15, false)]
    public void Quiet_hours_run_past_midnight(int hour, bool quiet)
    {
        Assert.Equal(quiet, LivingSchedule.IsQuiet(hour, new LivingPolicy()));
        Assert.False(LivingSchedule.IsQuiet(hour, new LivingPolicy { QuietHours = false }));
    }

    [Fact]
    public void She_messages_first_only_when_it_is_strong_enough_and_not_too_often()
    {
        var policy = new LivingPolicy { MaxMessagesPerDay = 2, MinHoursBetweenMessages = 2, ReachOutAbove = 0.75 };
        var none = Array.Empty<DateTimeOffset>();

        Assert.Null(LivingSchedule.WhyNoMessage(Now, 15, 0.8, none, false, policy));
        Assert.Contains("strong", LivingSchedule.WhyNoMessage(Now, 15, 0.5, none, false, policy));
        Assert.Contains("waiting", LivingSchedule.WhyNoMessage(Now, 15, 0.9, none, true, policy));
        Assert.Contains("quiet", LivingSchedule.WhyNoMessage(Now, 1, 0.9, none, false, policy));
        Assert.Contains("hours ago", LivingSchedule.WhyNoMessage(Now, 15, 0.9, new[] { Now.AddMinutes(-30) }, false, policy));
        Assert.Contains("today", LivingSchedule.WhyNoMessage(Now, 15, 0.9, new[] { Now.AddHours(-3), Now.AddHours(-9) }, false, policy));
        Assert.Null(LivingSchedule.WhyNoMessage(Now, 15, 0.9, new[] { Now.AddHours(-3), Now.AddHours(-30) }, false, policy));
    }

    [Fact]
    public void Reads_her_thought()
    {
        InnerThought t = InnerLifePrompt.Parse("<think>hm</think>```json\n{\"kind\":\"Curiosity\",\"text\":\"I wonder how cellos are made.\",\"look_up\":\"how are \\\"cellos\\\" made?\",\"urge_to_message\":\"1.7\",\"message_idea\":\"\"}\n```");

        Assert.Equal(ThoughtKinds.Curiosity, t.Kind);
        Assert.Equal("I wonder how cellos are made.", t.Text);
        Assert.Equal("how are cellos made", t.LookUp);
        Assert.Equal(1.0, t.Urge);
        Assert.Equal(ThoughtKinds.Thought, InnerLifePrompt.Parse("{\"kind\":\"rant\",\"text\":\"x\"}").Kind);
        Assert.Throws<PersonaConfigException>(() => InnerLifePrompt.Parse("{\"kind\":\"thought\",\"text\":\"  \"}"));
        Assert.Throws<PersonaConfigException>(() => InnerLifePrompt.Parse("no json here"));
    }

    [Fact]
    public void Her_searches_never_carry_her_name_and_stay_short()
    {
        Assert.Equal("", InnerLifePrompt.CleanLookUp("vicki's favourite songs", "Vicki"));
        Assert.True(InnerLifePrompt.CleanLookUp(string.Join(' ', Enumerable.Repeat("aurora", 40))).Length <= InnerLifePrompt.MaxLookUpChars);
        Assert.Equal("aurora borealis Iceland", InnerLifePrompt.CleanLookUp("  aurora <borealis> Iceland  "));
    }

    [Fact]
    public void The_writer_sees_who_she_is_but_not_lookups_when_they_are_off()
    {
        var input = new InnerLifeInput("Vicki", "Vicki is warm, wry and curious.", "Facts:\n- Takeru plays cello.", "[Inner state]\n- loneliness 7.0 TOP",
            new[] { new Exchange(1, "c1", "t1", Now, "Night!", "Sleep well.", false), new Exchange(2, InnerLife.ChatId, "thought-x", Now, "", "secret daydream", false) },
            new[] { "I miss the rain." }, "her flat", Now, TimeSpan.FromHours(3), "", CanLookUp: false);

        string user = InnerLifePrompt.WriterUser(input);
        Assert.Contains("wry and curious", user);
        Assert.Contains("Takeru plays cello", user);
        Assert.Contains("Them: Night!", user);
        Assert.Contains("I miss the rain.", user);
        Assert.Contains("3 hours ago", user);
        Assert.DoesNotContain("secret daydream", user.Split("recent thoughts")[0].Split("last things said")[^1]);
        Assert.Contains("lookups are off", InnerLifePrompt.WriterSystem("Vicki", false));
        Assert.DoesNotContain("lookups are off", InnerLifePrompt.WriterSystem("Vicki", true));
    }

    [Fact]
    public void Her_thoughts_are_remembered_as_hers_not_as_things_said()
    {
        using var dir = new TempDir();
        PersonaConfig config = TestPersona.Config();
        PersonaStore store = PersonaStore.Create(dir.File("vicki.db"), config, Now);
        var memory = new VectorMemory(store, new FakeEmbedder());
        var entry = new JournalEntry { PersonaId = "vicki", At = Now, Kind = ThoughtKinds.Curiosity, Text = "Cellos are carved from spruce.", Lookups = { new(JournalLookup.Search, "how cellos are made", ""), new(JournalLookup.Read, "https://example.org/cello", "Cello making") } };

        InnerLife.Remember(memory, entry);

        Exchange e = store.RecentExchanges(1).Single();
        Assert.Equal(InnerLife.ChatId, e.ChatId);
        Assert.Contains("how cellos are made", e.Reply);
        Assert.Contains("Cello making", e.Reply);
        string block = ContextInjector.RecalledBlock(new[] { new Recalled(e, 0.9) });
        Assert.Contains("you thought to yourself", block);
        Assert.DoesNotContain("they wrote", block);
    }

    [Fact]
    public void Her_message_first_is_tidied_and_explained_to_the_chat()
    {
        Assert.Equal("Hey you. Thinking of you.", InnerLifePrompt.CleanMessage("<think>ok</think>\"Hey you. Thinking of you.\""));
        Assert.Throws<PersonaConfigException>(() => InnerLifePrompt.CleanMessage("<think>only</think>"));
        var t = new InnerThought(ThoughtKinds.Feeling, "I miss him.", "", 0.9, "say hi");
        Assert.Contains("message them first", InnerLifePrompt.ReachOutCue(t, Now, TimeSpan.FromHours(4)));
        Assert.Contains("I miss him.", InnerLifePrompt.ReachOutContext(t, Now));
    }

    [Fact]
    public void The_journal_keeps_every_entry_and_names_a_damaged_line()
    {
        using var dir = new TempDir();
        var journal = new InnerLifeJournal(dir.File("living"));
        journal.Append(new JournalEntry { PersonaId = "vicki", At = Now, Text = "one", Lookups = { new(JournalLookup.Search, "q", "") } });
        journal.Append(new JournalEntry { PersonaId = "vicki", At = Now.AddMinutes(5), Text = "two", Message = "hi", Urge = 0.8 });

        List<JournalEntry> read = journal.Read("vicki");
        Assert.Equal(new[] { "one", "two" }, read.Select(e => e.Text));
        Assert.Equal("q", read[0].Lookups[0].What);
        Assert.True(read[1].MessagedYou);
        Assert.Empty(journal.Read("sophia"));

        File.AppendAllText(journal.PathOf("vicki"), "{broken\n");
        var ex = Assert.Throws<PersonaConfigException>(() => journal.Read("vicki"));
        Assert.Contains("Line 3", ex.Message);
        Assert.Throws<PersonaConfigException>(() => journal.PathOf("../etc"));
    }
}

public sealed class DreamVarietyTests
{
    [Fact]
    public void Dream_themes_vary_and_are_sometimes_led_by_her_strongest_drive()
    {
        var random = new Random(7);
        var picks = Enumerable.Range(0, 200).Select(_ => DreamThemes.Pick(random, "curiosity", "wants to learn and explore")).ToList();
        Assert.True(picks.Distinct().Count() > 50);
        int driven = picks.Count(p => p.Contains("strongest drive right now, curiosity"));
        Assert.InRange(driven, 40, 110);
        Assert.Contains(picks, p => p.Contains("are not in this dream"));
        Assert.Contains(picks, p => p.Contains("can be in this dream"));
        Assert.All(picks, p => Assert.Contains("Begin it ", p));
    }

    [Fact]
    public void Most_night_thoughts_are_dreams_but_not_all()
    {
        Assert.False(LivingSchedule.DreamsNow(night: false, 0.9));
        Assert.True(LivingSchedule.DreamsNow(night: true, 0.9));
        Assert.False(LivingSchedule.DreamsNow(night: true, 0.1));
    }

    [Fact]
    public void A_dream_prompt_carries_tonights_theme_recent_dreams_and_her_image_of_you()
    {
        var input = new InnerLifeInput("Mae", "sheet", "", "drives", new List<Exchange>(), new List<string>(), "", DateTimeOffset.Now, null, "", false, Asleep: true,
            ImageOfYou: "Tall, short dark hair, green jacket.", DreamTheme: "Tonight's dream is about flying.", UsedDreams: new[] { "Floating in a sea of silk." });
        string user = InnerLifePrompt.WriterUser(input);
        Assert.Contains("Tonight's dream is about flying.", user);
        Assert.Contains("Floating in a sea of silk.", user);
        Assert.Contains("green jacket", user);
        Assert.Contains("Never open with", InnerLifePrompt.DreamSystem("Mae"));
        InnerThought t = InnerLifePrompt.Parse("{\"kind\":\"dream\",\"text\":\"A train.\",\"she_is_in_it\":false,\"they_are_in_it\":true}");
        Assert.False(t.SheIsInIt);
        Assert.True(t.TheyAreInIt);
    }

    [Fact]
    public void A_journal_entry_can_be_updated_in_place_once_its_picture_is_painted()
    {
        using var dir = new TempDir();
        var journal = new InnerLifeJournal(dir.File("living"));
        var a = new JournalEntry { PersonaId = "mae", Kind = ThoughtKinds.Dream, Text = "one", PicturePending = true };
        var b = new JournalEntry { PersonaId = "mae", Kind = ThoughtKinds.Thought, Text = "two" };
        journal.Append(a);
        journal.Append(b);
        a.PicturePending = false;
        a.Picture = "x.png";
        journal.Replace(a);
        List<JournalEntry> read = journal.Read("mae");
        Assert.Equal(new[] { "one", "two" }, read.Select(e => e.Text));
        Assert.Equal("x.png", read[0].Picture);
        Assert.False(read[0].PicturePending);
    }

    [Fact]
    public void Her_image_of_you_is_parsed_and_capped()
    {
        Assert.Equal("Tall.", ImageOfYouPrompt.Parse("{\"image\":\"Tall.\"}"));
        Assert.Throws<PersonaConfigException>(() => ImageOfYouPrompt.Parse("{\"image\":\"\"}"));
        Assert.True(ImageOfYouPrompt.Parse("{\"image\":\"" + new string('a', 2000) + "\"}").Length <= PersonaConfig.MaxImageOfYouChars);
        Assert.Contains("green jacket", AquilaLocal.Companion.Pictures.PicturePrompt.WriterUser(new AquilaLocal.Companion.Pictures.PictureRequest("us", true, "scene"), "", "Mae", "Mae", false, "", Genders.Female, ("Ru", "green jacket")));
    }
}
