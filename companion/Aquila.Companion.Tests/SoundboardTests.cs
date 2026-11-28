using AquilaLocal.Companion;
using AquilaLocal.Companion.Sounds;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>
/// The soundboard: sounds kept in order as numbered files, names you choose, and personas cueing a sound with
/// &lt;sound&gt;name&lt;/sound&gt; that is matched by name, type or description and never read aloud.
/// </summary>
public sealed class SoundboardTests
{
    private static string FakeSound(TempDir dir, string name)
    {
        string path = dir.File(name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        return path;
    }

    private static SoundboardSet Board() => new()
    {
        Sounds =
        {
            new SoundEntry { Name = "Door knock", Type = "door", Description = "three firm knocks on a wooden door", File = "01 - Door knock.wav" },
            new SoundEntry { Name = "Giggle", Type = "laugh", Description = "a short, bright girlish laugh", File = "02 - Giggle.wav" },
            new SoundEntry { Name = "Phone buzz", Type = "phone", Description = "a phone vibrating on a table", File = "03 - Phone buzz.wav" },
        }
    };

    [Fact]
    public void Sounds_are_saved_as_numbered_files_in_list_order()
    {
        using var dir = new TempDir();
        var lib = new SoundboardLibrary(dir.File("soundboard"));
        SoundboardSet set = lib.Load();
        lib.Import(set, FakeSound(dir, "knock_knock.wav"));
        lib.Import(set, FakeSound(dir, "giggle.mp3"));
        lib.Save(set);

        Assert.Equal(new[] { "01 - knock knock.wav", "02 - giggle.mp3" }, set.Sounds.Select(s => s.File));
        Assert.True(File.Exists(lib.PathOf(set.Sounds[0])));

        // Reorder and rename in the app: the files follow.
        set.Sounds.Reverse();
        set.Sounds[0].Name = "Giggle";
        lib.Save(set);
        SoundboardSet again = lib.Load();

        Assert.Equal(new[] { "01 - Giggle.mp3", "02 - knock knock.wav" }, again.Sounds.Select(s => s.File));
        Assert.True(File.Exists(lib.PathOf(again.Sounds[0])));
        Assert.True(File.Exists(lib.PathOf(again.Sounds[1])));
    }

    [Fact]
    public void A_removed_sound_is_moved_aside_not_deleted()
    {
        using var dir = new TempDir();
        var lib = new SoundboardLibrary(dir.File("soundboard"));
        SoundboardSet set = lib.Load();
        lib.Import(set, FakeSound(dir, "a.wav"));
        lib.Import(set, FakeSound(dir, "b.wav"));
        lib.Save(set);
        set.Sounds.RemoveAt(0);
        lib.Save(set);

        Assert.True(File.Exists(Path.Combine(lib.Folder, "removed", "01 - a.wav")));
        Assert.Equal("01 - b.wav", lib.Load().Sounds.Single().File);
    }

    [Fact]
    public void Two_sounds_cant_share_a_name_and_odd_files_are_refused()
    {
        using var dir = new TempDir();
        var lib = new SoundboardLibrary(dir.File("soundboard"));
        SoundboardSet set = lib.Load();
        lib.Import(set, FakeSound(dir, "a.wav"));
        SoundEntry second = lib.Import(set, FakeSound(dir, "a2.wav"), "a");

        Assert.Equal("a 2", second.Name);
        second.Name = "A";
        Assert.Throws<PersonaConfigException>(() => lib.Save(set));
        Assert.Throws<PersonaConfigException>(() => lib.Import(set, FakeSound(dir, "notes.txt")));
    }

    [Fact]
    public void A_broken_list_is_refused_loudly()
    {
        using var dir = new TempDir();
        var lib = new SoundboardLibrary(dir.File("soundboard"));
        Directory.CreateDirectory(lib.Folder);
        File.WriteAllText(lib.ListPath, "{ not json");

        Assert.Throws<PersonaConfigException>(() => lib.Load());
    }

    [Theory]
    [InlineData("Door knock", "Door knock")]
    [InlineData("door knock", "Door knock")]
    [InlineData("knocking", "Door knock")]
    [InlineData("knock on the door", "Door knock")]
    [InlineData("laugh", "Giggle")]
    [InlineData("Giggles", "Giggle")]
    [InlineData("phone vibrating", "Phone buzz")]
    public void A_cue_matches_by_name_type_or_description(string cue, string expected)
    {
        Assert.Equal(expected, SoundCues.Match(cue, Board())?.Name);
    }

    [Fact]
    public void A_cue_with_no_good_match_plays_nothing()
    {
        Assert.Null(SoundCues.Match("thunderstorm", Board()));
    }

    [Fact]
    public void Meaning_can_find_a_sound_the_words_miss()
    {
        double Meaning(string a, string b) => b.StartsWith("Giggle", StringComparison.Ordinal) ? 0.8 : 0.1;

        Assert.Equal("Giggle", SoundCues.Match("chuckle", Board(), Meaning)?.Name);
    }

    [Fact]
    public void Cues_are_found_shown_as_actions_and_never_spoken()
    {
        string reply = "Someone's here! <sound>Door knock</sound> <action>I go to the door</action>";

        Assert.Equal(new[] { "Door knock" }, SoundCues.Names(reply));
        Assert.Contains("<action>🔊 Door knock</action>", SoundCues.ForDisplay(reply));
        string masked = SoundCues.MaskForSpeech(reply);
        Assert.Equal(reply.Length, masked.Length);
        Assert.DoesNotContain("Door knock", masked);
        Assert.Equal("Someone's here! <action>I go to the door</action>", SoundCues.Strip(reply));
    }

    [Fact]
    public void A_cue_still_being_written_is_hidden()
    {
        Assert.Equal("Hi ", SoundCues.ForDisplay("Hi <sound>Door kn"));
        Assert.Equal("Hi ", SoundCues.ForDisplay("Hi <sou"));
    }

    [Fact]
    public void At_most_three_sounds_play_for_one_reply()
    {
        Assert.Equal(3, SoundCues.Names("<sound>a</sound><sound>b</sound><sound>c</sound><sound>d</sound>").Count);
    }

    [Fact]
    public void The_persona_is_told_about_the_sounds_only_when_there_are_some()
    {
        Assert.Equal("", SoundCues.Prompt(new SoundboardSet()));
        string prompt = SoundCues.Prompt(Board());
        Assert.Contains("- Door knock - door: three firm knocks", prompt);
        Assert.Contains("<sound>", prompt);

        string system = ContextInjector.SystemPrompt(TestPersona.Config(), new StateBlock(), "", prompt);
        Assert.Contains("Phone buzz", system);
        Assert.DoesNotContain("Soundboard:", ContextInjector.SystemPrompt(TestPersona.Config(), new StateBlock()));
    }

    [Fact]
    public void Her_memory_keeps_the_reply_without_the_cue()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.Config();
        PersonaStore store = PersonaStore.Create(dir.File("v.db"), c, TestPersona.T0);
        CompanionRuntime rt = CompanionRuntime.ForTests(c, store, new FakeEmbedder());
        PreparedTurn p = rt.Prepare("Who's that?", new HashSet<string>(), false, TestPersona.T0);

        rt.Complete(p, "chat", "t1", "Who's that?", "Let me look. <sound>Door knock</sound>", TestPersona.T0);

        Assert.DoesNotContain("<sound>", store.RecentExchanges(1).Single().Reply);
    }

    [Fact]
    public void A_made_up_take_number_finds_the_real_take_and_unknown_cues_are_named()
    {
        var set = new SoundboardSet
        {
            Sounds =
            {
                new SoundEntry { Name = "(excited)wow1", Type = "exclamation", File = "01.wav" },
                new SoundEntry { Name = "(excited)wow2", Type = "exclamation", File = "02.wav" },
                new SoundEntry { Name = "(funny)laughter1", Type = "exclamation", File = "03.wav" },
            }
        };
        Assert.Equal("(excited)wow1", SoundCues.Match("(excited)wow3", set)!.Name);
        Assert.Equal("(funny)laughter1", SoundCues.Match("laughter2", set)!.Name);
        List<string> unknown = SoundCues.Unknown("Ha! <sound>door slam</sound> <sound>(excited)wow2</sound>", set);
        Assert.Equal(new[] { "door slam" }, unknown);
        Assert.Contains("isn't one of your sounds", SoundCues.UnknownNote(unknown));
        Assert.Equal("", SoundCues.UnknownNote(new List<string>()));
    }
}
