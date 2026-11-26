using AquilaLocal.Companion;
using AquilaLocal.Companion.Scenes;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>
/// Multi-persona scenes: one shared world and one shared log, many private minds. These pin down who knows
/// what (only what they were present for), that the log can't be rewritten, and how the next speaker is chosen.
/// </summary>
public sealed class SceneTests
{
    private static readonly DateTimeOffset T0 = TestPersona.T0;

    private static WorldState Lab() => new()
    {
        Place = "Vicki's lab, late evening",
        Time = "Thursday, 9 pm",
        Situation = "Ru dropped by with takeout",
        Objects = { "a centrifuge", "two cartons of noodles" },
    };

    private static Scene NewScene(TempDir dir, params (string Id, string Name)[] cast) =>
        new SceneLibrary(dir.Path).Create("Late night at the lab", Lab(), cast.Select(c => new SceneMember(c.Id, c.Name)).ToList(), T0);

    [Fact]
    public void A_new_scene_saves_its_manifest_and_logs_who_is_there()
    {
        using var dir = new TempDir();
        Scene scene = NewScene(dir, ("vicki", "Vicki"), ("marcus", "Marcus"));

        Scene again = new SceneLibrary(dir.Path).Open(scene.Id);

        Assert.Equal("Late night at the lab", again.Manifest.Title);
        Assert.Equal(new[] { "vicki", "marcus" }, again.Manifest.Cast.Select(m => m.PersonaId));
        Assert.Equal("Vicki's lab, late evening", again.Manifest.World.Place);
        Assert.Equal(2, again.Log.All().Count(e => e.Kind == SceneEventKinds.Joined));
        Assert.Single(new SceneLibrary(dir.Path).List());
    }

    [Fact]
    public void The_log_is_append_only()
    {
        using var dir = new TempDir();
        Scene scene = NewScene(dir, ("vicki", "Vicki"));
        scene.Say(SceneLog.UserId, "Ru", "Hi!", T0.AddMinutes(1));

        Assert.ThrowsAny<Exception>(() => scene.Log.ExecuteForTests("UPDATE events SET text = 'changed'"));
        Assert.ThrowsAny<Exception>(() => scene.Log.ExecuteForTests("DELETE FROM events"));
        Assert.Equal("Hi!", scene.Log.All().Last().Text);
    }

    [Fact]
    public void Joining_and_leaving_are_events_everyone_present_hears()
    {
        using var dir = new TempDir();
        Scene scene = NewScene(dir, ("vicki", "Vicki"));
        scene.Join(new SceneMember("marcus", "Marcus"), T0.AddMinutes(2));
        scene.Leave("vicki", T0.AddMinutes(3));

        Assert.Equal(new[] { "marcus" }, scene.Manifest.Cast.Select(m => m.PersonaId));
        List<SceneEvent> seenByMarcus = scene.VisibleTo("marcus");
        Assert.Contains(seenByMarcus, e => e.Kind == SceneEventKinds.Left && e.SpeakerId == "vicki");
        string transcript = SceneTranscript.Render(seenByMarcus, "marcus");
        Assert.Contains("[Vicki leaves the scene]", transcript);
    }

    [Fact]
    public void A_newcomer_only_knows_what_happened_after_they_arrived()
    {
        using var dir = new TempDir();
        Scene scene = NewScene(dir, ("vicki", "Vicki"));
        scene.Say(SceneLog.UserId, "Ru", "Don't tell Marcus about the surprise party.", T0.AddMinutes(1));
        scene.Join(new SceneMember("marcus", "Marcus"), T0.AddMinutes(2));
        scene.Say(SceneLog.UserId, "Ru", "Hey Marcus, good to see you.", T0.AddMinutes(3));

        List<SceneEvent> marcus = scene.VisibleTo("marcus");
        List<SceneEvent> vicki = scene.VisibleTo("vicki");

        Assert.DoesNotContain(marcus, e => e.Text.Contains("surprise party"));
        Assert.Contains(marcus, e => e.Text.Contains("good to see you"));
        Assert.Contains(vicki, e => e.Text.Contains("surprise party"));
    }

    [Fact]
    public void Someone_who_stepped_out_misses_what_was_said_while_they_were_gone()
    {
        using var dir = new TempDir();
        Scene scene = NewScene(dir, ("vicki", "Vicki"), ("marcus", "Marcus"));
        scene.Leave("marcus", T0.AddMinutes(1));
        scene.Say("vicki", "Vicki", "Marcus is terrible at chess.", T0.AddMinutes(2));
        scene.Join(new SceneMember("marcus", "Marcus"), T0.AddMinutes(3));
        scene.Say("vicki", "Vicki", "Welcome back!", T0.AddMinutes(4));

        List<SceneEvent> marcus = scene.VisibleTo("marcus");

        Assert.DoesNotContain(marcus, e => e.Text.Contains("chess"));
        Assert.Contains(marcus, e => e.Text == "Welcome back!");
    }

    [Fact]
    public void Changing_the_world_is_saved_and_logged_so_everyone_notices()
    {
        using var dir = new TempDir();
        Scene scene = NewScene(dir, ("vicki", "Vicki"));
        WorldState next = Lab();
        next.Place = "The rooftop above the lab";
        scene.ChangeWorld(next, T0.AddMinutes(5));

        Assert.Equal("The rooftop above the lab", new SceneLibrary(dir.Path).Open(scene.Id).Manifest.World.Place);
        SceneEvent changed = scene.Log.All().Last();
        Assert.Equal(SceneEventKinds.WorldChanged, changed.Kind);
        Assert.Contains("The rooftop above the lab", changed.Text);
    }

    [Fact]
    public void Cast_is_capped_at_twelve()
    {
        using var dir = new TempDir();
        Scene scene = NewScene(dir, Enumerable.Range(1, 12).Select(i => ($"p{i}", $"P{i}")).ToArray());

        Assert.Throws<SceneException>(() => scene.Join(new SceneMember("p13", "P13"), T0.AddMinutes(1)));
    }

    [Fact]
    public void Shared_scene_header_is_identical_for_every_speaker_so_the_prompt_cache_is_reused()
    {
        using var dir = new TempDir();
        Scene scene = NewScene(dir, ("vicki", "Vicki"), ("marcus", "Marcus"));
        scene.Say(SceneLog.UserId, "Ru", "Evening, both of you.", T0.AddMinutes(1));

        var looks = new Dictionary<string, string> { ["vicki"] = "glasses, lab coat", ["marcus"] = "tall, leather jacket" };
        string forVicki = ScenePrompt.Header(scene.Manifest, looks, "Ru");
        string forMarcus = ScenePrompt.Header(scene.Manifest, looks, "Ru");

        Assert.Equal(forVicki, forMarcus);
        Assert.Contains("Vicki's lab, late evening", forVicki);
        Assert.Contains("Marcus: tall, leather jacket", forVicki);
        Assert.DoesNotContain("You are", forVicki);
    }

    [Fact]
    public void The_speakers_turn_prompt_names_them_and_forbids_speaking_for_others()
    {
        string cue = ScenePrompt.Cue("Vicki", new[] { "Marcus", "Ru" });

        Assert.Contains("You are Vicki", cue);
        Assert.Contains("Marcus", cue);
        Assert.Contains("only for yourself", cue);
    }

    [Fact]
    public void Persona_replies_have_a_leading_name_tag_removed()
    {
        Assert.Equal("Hi there.", ScenePrompt.CleanReply("Vicki: Hi there.", "Vicki"));
        Assert.Equal("Hi there.", ScenePrompt.CleanReply("**Vicki:** Hi there.", "Vicki"));
        Assert.Equal("Hi there.", ScenePrompt.CleanReply("Hi there.\nMarcus: And me!", "Vicki"));
    }
}

public sealed class TurnSchedulerTests
{
    private static readonly FakeEmbedder Embedder = new();

    private static SceneCandidate Who(string id, string name, string interests, double urge = 0.5) =>
        new(id, name, Array.Empty<string>(), Embedder.Embed(interests), urge);

    private static SceneEvent Line(string speakerId, string speaker, string text, long id = 10) =>
        new(id, TestPersona.T0, SceneEventKinds.Said, speakerId, speaker, text);

    private static readonly SceneCandidate[] Cast =
    {
        Who("vicki", "Vicki", "chemistry lab experiments science research molecules"),
        Who("marcus", "Marcus", "football cars engines racing motorcycles"),
        Who("lena", "Lena", "painting art gallery colours museums sketching"),
    };

    [Fact]
    public void Someone_named_answers()
    {
        SceneEvent last = Line(SceneLog.UserId, "Ru", "Lena, what do you think about engines?");

        List<RankedSpeaker> ranked = TurnScheduler.Rank(Cast, last, Array.Empty<SceneEvent>(), Embedder.Embed(last.Text), new TurnPolicy());

        Assert.Equal("lena", ranked[0].PersonaId);
        Assert.Contains("named", ranked[0].Why);
    }

    [Fact]
    public void Without_a_name_the_most_relevant_persona_leads()
    {
        SceneEvent last = Line(SceneLog.UserId, "Ru", "I rebuilt the engines on my motorcycles for racing");

        List<RankedSpeaker> ranked = TurnScheduler.Rank(Cast, last, Array.Empty<SceneEvent>(), Embedder.Embed(last.Text), new TurnPolicy());

        Assert.Equal("marcus", ranked[0].PersonaId);
    }

    [Fact]
    public void A_strong_drive_can_win_when_nothing_else_decides()
    {
        SceneCandidate[] cast =
        {
            Who("vicki", "Vicki", "chemistry", urge: 0.2),
            Who("marcus", "Marcus", "football", urge: 1.2),
        };
        SceneEvent last = Line(SceneLog.UserId, "Ru", "So, how is everyone?");

        List<RankedSpeaker> ranked = TurnScheduler.Rank(cast, last, Array.Empty<SceneEvent>(), Embedder.Embed(last.Text), new TurnPolicy());

        Assert.Equal("marcus", ranked[0].PersonaId);
    }

    [Fact]
    public void Whoever_just_spoke_does_not_answer_themselves()
    {
        SceneEvent last = Line("vicki", "Vicki", "Chemistry lab experiments with molecules are my research!");

        List<RankedSpeaker> ranked = TurnScheduler.Rank(Cast, last, Array.Empty<SceneEvent>(), Embedder.Embed(last.Text), new TurnPolicy());

        Assert.DoesNotContain(ranked, r => r.PersonaId == "vicki");
    }

    [Fact]
    public void The_scene_waits_for_you_after_the_chain_limit_or_when_nobody_would_naturally_speak()
    {
        var policy = new TurnPolicy { MaxChain = 2 };
        var named = new RankedSpeaker("lena", "Lena", 3.5, "named");
        var lukewarm = new RankedSpeaker("lena", "Lena", 0.6, "relevance 0.1");

        Assert.True(TurnScheduler.ShouldContinue(named, chainSoFar: 1, policy));
        Assert.False(TurnScheduler.ShouldContinue(named, chainSoFar: 2, policy));
        Assert.False(TurnScheduler.ShouldContinue(lukewarm, chainSoFar: 1, policy));
    }

    [Fact]
    public void Picking_is_lively_but_a_named_persona_always_gets_the_turn()
    {
        var ranked = new List<RankedSpeaker>
        {
            new("lena", "Lena", 3.6, "named"),
            new("marcus", "Marcus", 1.0, "drive"),
        };
        for (int seed = 0; seed < 20; seed++)
        {
            Assert.Equal("lena", TurnScheduler.Pick(ranked, new Random(seed)).PersonaId);
        }

        var close = new List<RankedSpeaker>
        {
            new("vicki", "Vicki", 1.0, "relevance"),
            new("marcus", "Marcus", 0.95, "drive"),
        };
        HashSet<string> picked = Enumerable.Range(0, 60).Select(s => TurnScheduler.Pick(close, new Random(s)).PersonaId).ToHashSet();
        Assert.Equal(2, picked.Count);
    }

    [Fact]
    public void Urge_comes_from_the_strongest_weighted_drive()
    {
        PersonaConfig c = TestPersona.Config();
        var low = c.Drives.ToDictionary(d => d.Name, d => new DriveValue(d.Name, 1.0, 0, TestPersona.T0));
        var high = c.Drives.ToDictionary(d => d.Name, d => new DriveValue(d.Name, 9.0, 0, TestPersona.T0));

        Assert.True(TurnScheduler.Urge(c, high) > TurnScheduler.Urge(c, low));
        Assert.InRange(TurnScheduler.Urge(c, high), 0.5, 2.0);
    }
}
