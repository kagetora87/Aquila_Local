using AquilaLocal.Companion;
using AquilaLocal.Companion.Speech;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>
/// "Aquila" voice commands: standby hears only the wake word and ignores everything else; after it, a short
/// command ("let's talk", "call Vicki", "hang up") is matched loosely, the way a person would understand it.
/// </summary>
public sealed class VoiceCommandTests
{
    private static readonly VoiceCommandSet Set = VoiceCommandSet.Defaults();

    private static readonly IReadOnlyList<(string Id, string Name)> People = new[] { ("vicki", "Vicki"), ("sophia", "Sophia") };

    [Theory]
    [InlineData("Aquila.", "")]
    [InlineData("Aquila, let's talk.", "let's talk.")]
    [InlineData("Akila, call Vicky", "call Vicky")]
    [InlineData("Okay Aquila call Sophia please", "call Sophia please")]
    [InlineData("Hey Aquilla!", "")]
    public void Standby_hears_the_wake_word_and_keeps_what_follows(string heard, string rest)
    {
        WakeResult r = VoiceCommandMatcher.FindWake(heard, Set);

        Assert.True(r.Heard);
        Assert.Equal(rest, r.Rest);
    }

    [Theory]
    [InlineData("I was telling her about the weekend")]
    [InlineData("Thank you.")]
    [InlineData("She has an aquiline nose")]
    [InlineData("")]
    public void Standby_ignores_everything_else(string heard)
    {
        Assert.False(VoiceCommandMatcher.FindWake(heard, Set).Heard);
    }

    [Theory]
    [InlineData("Let's talk")]
    [InlineData("let's chat")]
    [InlineData("Can we talk for a bit?")]
    [InlineData("talk to me")]
    [InlineData("I want to have a conversation")]
    public void Lets_talk_and_its_equivalents_start_a_conversation(string said)
    {
        Assert.Equal(VoiceActions.Talk, VoiceCommandMatcher.Match(said, Set, People)?.Action);
    }

    [Theory]
    [InlineData("call Vicki", "vicki")]
    [InlineData("Call Vicky.", "vicki")]
    [InlineData("could you call Sophia please", "sophia")]
    [InlineData("phone Sofia", "sophia")]
    public void Call_finds_the_persona_even_when_the_name_is_spelled_differently(string said, string id)
    {
        CommandMatch? m = VoiceCommandMatcher.Match(said, Set, People);

        Assert.Equal(VoiceActions.Call, m?.Action);
        Assert.Equal(id, m?.PersonaId);
    }

    [Fact]
    public void Calling_someone_unknown_says_who_was_asked_for_instead_of_guessing()
    {
        CommandMatch? m = VoiceCommandMatcher.Match("call Marcus", Set, People);

        Assert.Equal(VoiceActions.Call, m?.Action);
        Assert.Null(m?.PersonaId);
        Assert.Equal("Marcus", m?.PersonaName);
    }

    [Theory]
    [InlineData("hang up", VoiceActions.HangUp)]
    [InlineData("end the call", VoiceActions.HangUp)]
    [InlineData("stop listening", VoiceActions.Stop)]
    [InlineData("go to sleep", VoiceActions.Stop)]
    public void Ending_commands_are_understood(string said, string action)
    {
        Assert.Equal(action, VoiceCommandMatcher.Match(said, Set, People)?.Action);
    }

    [Theory]
    [InlineData("what's the weather like")]
    [InlineData("I love noodles")]
    public void Anything_else_after_the_wake_word_is_not_a_command(string said)
    {
        Assert.Null(VoiceCommandMatcher.Match(said, Set, People));
    }

    [Fact]
    public void A_meaning_check_can_catch_wordings_that_are_not_in_the_list()
    {
        // The app passes a local embedding similarity; here a fake that knows "natter" means talk.
        double Similar(string a, string b) => a.Contains("natter") && b.Contains("talk") ? 0.9 : 0.0;

        Assert.Equal(VoiceActions.Talk, VoiceCommandMatcher.Match("fancy a natter", Set, People, Similar)?.Action);
    }

    [Fact]
    public void Commands_are_saved_in_an_editable_file_and_edits_take_effect()
    {
        using var dir = new TempDir();
        VoiceCommandSet set = VoiceCommandSet.Defaults();
        set.WakeWord = "Nova";
        set.Commands.Single(c => c.Action == VoiceActions.Talk).Phrases.Add("I need you");
        set.Save(dir.File("voice-commands.json"));

        VoiceCommandSet loaded = VoiceCommandSet.Load(dir.File("voice-commands.json"));

        Assert.True(VoiceCommandMatcher.FindWake("Nova, I need you", loaded).Heard);
        Assert.False(VoiceCommandMatcher.FindWake("Aquila, I need you", loaded).Heard);
        Assert.Equal(VoiceActions.Talk, VoiceCommandMatcher.Match("I need you", loaded, People)?.Action);
    }

    [Fact]
    public void A_missing_file_gives_the_defaults_and_a_broken_one_is_refused_loudly()
    {
        using var dir = new TempDir();
        Assert.Equal("Aquila", VoiceCommandSet.Load(dir.File("none.json")).WakeWord);

        File.WriteAllText(dir.File("bad.json"), "{ \"wakeWord\": \"\", \"commands\": [] }");
        Assert.Throws<PersonaConfigException>(() => VoiceCommandSet.Load(dir.File("bad.json")));

        var noName = VoiceCommandSet.Defaults();
        noName.Commands.Single(c => c.Action == VoiceActions.Call).Phrases.Add("ring");
        Assert.Throws<PersonaConfigException>(() => noName.Validate());
    }

    [Fact]
    public void The_whisper_hint_includes_the_wake_word_and_persona_names()
    {
        string hint = VoiceCommandMatcher.Hint(Set, People);

        Assert.Contains("Aquila", hint);
        Assert.Contains("Vicki", hint);
        Assert.Contains("Sophia", hint);
    }
}
