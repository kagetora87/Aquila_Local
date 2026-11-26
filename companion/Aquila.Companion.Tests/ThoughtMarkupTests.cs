using AquilaLocal.Companion;
using AquilaLocal.Companion.Scenes;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>
/// Thoughts and actions: &lt;thought&gt; is private (shown blue), &lt;action&gt; is seen by everyone (shown orange).
/// The user's thoughts never become something a character knows, and a persona's thoughts never reach another persona.
/// </summary>
public sealed class ThoughtMarkupTests
{
    [Fact]
    public void Splits_a_message_into_speech_thoughts_and_actions()
    {
        List<MarkupPart> parts = ThoughtMarkup.Parse("Hi. <action>waves</action> <thought>she looks tired</thought> How are you?");

        Assert.Equal(new[] { MarkupKind.Speech, MarkupKind.Action, MarkupKind.Speech, MarkupKind.Thought, MarkupKind.Speech }, parts.Select(p => p.Kind));
        Assert.Equal("waves", parts[1].Text);
        Assert.Equal("she looks tired", parts[3].Text);
    }

    [Fact]
    public void An_unclosed_tag_runs_to_the_end()
    {
        List<MarkupPart> parts = ThoughtMarkup.Parse("Okay <thought>I hope she says yes");

        Assert.Equal(MarkupKind.Thought, parts.Last().Kind);
        Assert.Equal("I hope she says yes", parts.Last().Text);
    }

    [Fact]
    public void Writes_parts_back_as_markup()
    {
        var parts = new List<MarkupPart> { new(MarkupKind.Speech, "Hi "), new(MarkupKind.Action, "hugs her") };

        Assert.Equal("Hi <action>hugs her</action>", ThoughtMarkup.Write(parts));
    }

    [Fact]
    public void A_thought_across_lines_is_closed_and_reopened_on_each_line_for_display()
    {
        string balanced = ThoughtMarkup.BalancePerLine("Hello <thought>first line\nsecond line</thought> done");

        Assert.Equal("Hello <thought>first line</thought>\n<thought>second line</thought> done", balanced);
    }

    [Fact]
    public void Thoughts_can_be_hidden_and_actions_stay()
    {
        Assert.Equal("Hi. <action>waves</action> How are you?", ThoughtMarkup.StripThoughts("Hi. <action>waves</action> <thought>she looks tired</thought> How are you?"));
    }

    [Fact]
    public void Plain_text_drops_the_tags_but_keeps_the_words()
    {
        Assert.Equal("Hi waves", ThoughtMarkup.Plain("Hi <action>waves</action>"));
    }

    [Fact]
    public void Voice_masks_thoughts_and_actions_without_moving_the_spoken_words()
    {
        string text = "Hi. <action>waves</action> <thought>hmm</thought>Bye.";
        string masked = ThoughtMarkup.MaskForSpeech(text);

        Assert.Equal(text.Length, masked.Length);
        Assert.Equal("Hi.", masked[..3]);
        Assert.EndsWith("Bye.", masked);
        Assert.DoesNotContain("waves", masked);
        Assert.DoesNotContain("hmm", masked);
    }

    [Fact]
    public void Her_memory_of_the_exchange_leaves_out_what_you_only_thought()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.Config();
        PersonaStore store = PersonaStore.Create(dir.File("v.db"), c, TestPersona.T0);
        CompanionRuntime rt = CompanionRuntime.ForTests(c, store, new FakeEmbedder());
        string user = "I made dinner. <thought>I'm going to propose tonight</thought> <action>sets the table</action>";
        PreparedTurn p = rt.Prepare(user, new HashSet<string>(), false, TestPersona.T0);

        rt.Complete(p, "chat", "t1", user, "Smells wonderful! <thought>he seems nervous</thought>", TestPersona.T0);

        Exchange saved = store.RecentExchanges(1).Single();
        Assert.DoesNotContain("propose", saved.User);
        Assert.Contains("sets the table", saved.User);
        Assert.Contains("he seems nervous", saved.Reply);
    }

    [Fact]
    public void The_persona_is_told_how_thoughts_and_actions_work()
    {
        string system = ContextInjector.SystemPrompt(TestPersona.Config(), new StateBlock());

        Assert.Contains("<thought>", system);
        Assert.Contains("<action>", system);
        Assert.Contains("cannot hear or know", system);
    }

    [Fact]
    public void Personas_can_be_told_to_write_thoughts_and_actions_in_third_person()
    {
        try
        {
            ThoughtMarkup.Person = MarkupPerson.Third;
            Assert.Contains("<action>Vicki smiles</action>", ContextInjector.SystemPrompt(TestPersona.Config(), new StateBlock()).Replace(TestPersona.Config().Name, "Vicki"));
            Assert.Contains("third person", ScenePrompt.Cue("Vicki", new[] { "Vicki" }));
        }
        finally
        {
            ThoughtMarkup.Person = MarkupPerson.First;
        }
        Assert.Contains("<action>I smile</action>", ThoughtMarkup.Guidance("Vicki"));
    }

    [Fact]
    public void In_a_scene_nobody_reads_another_personas_thoughts()
    {
        var vicki = new SceneEvent(1, TestPersona.T0, SceneEventKinds.Said, "vicki", "Vicki", "Sure! <thought>Marcus is lying</thought> <action>smiles</action>");

        string line = SceneTranscript.Line(vicki, "");

        Assert.DoesNotContain("lying", line);
        Assert.Contains("<action>smiles</action>", line);
    }
}
