using AquilaLocal.Companion;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>She notices when she's been interviewing them, and shares something of her own instead.</summary>
public sealed class BalanceTests
{
    [Theory]
    [InlineData("That sounds lovely. What did you eat?", true)]
    [InlineData("What did you eat?\" ", true)]
    [InlineData("Did you like it? 😊", true)]
    [InlineData("Did you like it? <action>I tilt my head</action>", true)]
    [InlineData("I loved it. <thought>Should I ask?</thought>", false)]
    [InlineData("Is it? Well, I think it was perfect.", false)]
    [InlineData("", false)]
    public void Knows_when_a_reply_ends_with_a_question(string reply, bool expected)
    {
        Assert.Equal(expected, ConversationBalance.EndsWithQuestion(reply));
    }

    [Fact]
    public void Nudges_her_after_two_replies_ending_in_questions()
    {
        Assert.Equal("", ConversationBalance.Cue(new[] { "Nice. How was it?" }));
        Assert.Equal("", ConversationBalance.Cue(new[] { "Nice. How was it?", "I'd love that too." }));
        Assert.Equal("", ConversationBalance.Cue(new[] { "I'd love that too.", "Nice. How was it?" }));
        string cue = ConversationBalance.Cue(new[] { "I'd love that.", "Really? How was it?", "And then what happened?" });
        Assert.Contains("last 2 replies", cue);
        Assert.Contains("ask nothing", cue);
    }

    [Fact]
    public void Nudges_her_when_she_piles_on_questions()
    {
        string cue = ConversationBalance.Cue(new[] { "Why? Where? When? Who? Anyway, I like it." , "Sounds good." });
        Assert.Contains("4 questions", cue);
    }

    [Fact]
    public void The_rule_is_in_her_system_prompt()
    {
        PersonaConfig config = TestPersona.Config();
        Assert.Contains("not an interview", ContextInjector.SystemPrompt(config, new StateBlock()));
    }

    [Fact]
    public void Recalled_moments_say_how_long_ago_and_that_they_are_over()
    {
        DateTimeOffset now = TestPersona.T0;
        var e = new Exchange(1, "c1", "t1", now.AddHours(-3), "Look at these pictures", "They're lovely!", false);
        string block = ContextInjector.RecalledBlock(new[] { new Recalled(e, 0.9) }, now);
        Assert.Contains("about 3 hours ago", block);
        Assert.Contains("already happened", block);
        Assert.Equal("2026-01-01", ContextInjector.When(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null));
    }
}
