using AquilaLocal.Companion;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>Drives can be renamed, replaced and added; a drive is either a need (satisfying lowers it) or a feeling level (satisfying raises it).</summary>
public sealed class CustomDriveTests
{
    private const string Hello = "hey, how was your day?";

    [Fact]
    public void A_new_drive_with_her_own_words_is_scored_and_satisfied()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.Config();
        DriveSpec music = TestPersona.Spec("music", weight: 3, initial: 8);
        music.Cues = new List<string> { "cello", "song", "play for you" };
        c.Drives.Add(music);
        c.ValidateShape();
        Assert.Empty(c.MissingInputs());
        PersonaStore store = PersonaStore.Create(dir.File("v.db"), c, TestPersona.T0);
        var loop = new SatisfactionLoop(c, store, new HeuristicScorer());
        LoopResult r = loop.Run(new TurnObservation(Hello, "Let me play for you, I learned a new song on the cello! I'm so happy.", "music", null), TestPersona.T0.AddMinutes(1));
        Assert.True(r.Satisfied);
        Assert.Equal(7, store.ReadDrives(c.Drives.Select(d => d.Name))["music"].Value, 6);
    }

    [Fact]
    public void A_new_drive_without_words_blocks_setup_with_a_plain_reason()
    {
        PersonaConfig c = TestPersona.Config();
        c.Drives.Add(TestPersona.Spec("music", weight: 1, initial: 2));
        Assert.Contains(c.MissingInputs(), m => m.Contains("words that show she acted on music"));
    }

    [Fact]
    public void A_feeling_level_rises_when_satisfied_and_presses_when_low()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.WithTop(Drives.Comfort, 2);
        c.Drive(Drives.Comfort).Kind = DriveKinds.Level;
        PersonaStore store = PersonaStore.Create(dir.File("v.db"), c, TestPersona.T0);
        DriveSpec comfort = c.Drive(Drives.Comfort);
        Assert.True(DriveEngine.Priority(comfort, 2, 0) > DriveEngine.Priority(comfort, 8, 0));
        Assert.Equal(Drives.Comfort, DriveEngine.TopDrive(c, store.ReadDrives(c.Drives.Select(d => d.Name))));
        var loop = new SatisfactionLoop(c, store, new HeuristicScorer());
        LoopResult r = loop.Run(new TurnObservation(Hello, "You're safe here, relax, it's okay. I'm glad you're here and happy.", Drives.Comfort, null), TestPersona.T0.AddMinutes(1));
        Assert.True(r.Satisfied);
        Assert.Equal(3, store.ReadDrives(c.Drives.Select(d => d.Name))[Drives.Comfort].Value, 6);
        Assert.Contains("comfort 3.0", ContextInjector.DriveBlock(c, store.ReadDrives(c.Drives.Select(d => d.Name)), Drives.Comfort));
        Assert.Contains(ContextInjector.LevelMark, ContextInjector.DriveBlock(c, store.ReadDrives(c.Drives.Select(d => d.Name)), Drives.Comfort));
    }

    [Fact]
    public void Renaming_removing_and_adding_keeps_her_values_and_logs_it()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.Config();
        PersonaStore store = PersonaStore.Create(dir.File("v.db"), c, TestPersona.T0);
        store.Apply(new[] { new DriveChange(Drives.Lust, 5, 6.5, "test") }, TestPersona.T0);

        c.Drive(Drives.Lust).Name = "desire";
        c.Drive(Drives.Comfort).Ceiling = 4;
        c.Drive(Drives.Comfort).Initial = 4;
        c.Drives.RemoveAll(d => d.Name == Drives.Curiosity);
        DriveSpec music = TestPersona.Spec("music", weight: 1, initial: 2);
        music.Cues.Add("song");
        c.Drives.Add(music);
        c.ValidateShape();
        List<string> said = store.ReshapeDrives(c, new Dictionary<string, string> { ["desire"] = Drives.Lust }, TestPersona.T0.AddHours(1));

        Dictionary<string, DriveValue> now = store.ReadDrives(c.Drives.Select(d => d.Name));
        Assert.Equal(6.5, now["desire"].Value, 6);
        Assert.Equal(4, now[Drives.Comfort].Value, 6);
        Assert.Equal(2, now["music"].Value, 6);
        Assert.Throws<DriveStateUnavailableException>(() => store.ReadDrives(new[] { Drives.Curiosity }));
        Assert.Contains(said, s => s.Contains("lust is now desire"));
        Assert.Contains(said, s => s.Contains("curiosity removed"));
        Assert.Contains(store.DriveLog(drive: "desire"), e => e.Reason.Contains("renamed from lust"));
    }

    [Fact]
    public void Without_horizon_the_loop_still_runs()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.WithTop(Drives.Loneliness, 8);
        c.Drives.RemoveAll(d => d.Name == Drives.Horizon);
        c.ValidateShape();
        PersonaStore store = PersonaStore.Create(dir.File("v.db"), c, TestPersona.T0);
        LoopResult r = new SatisfactionLoop(c, store, new HeuristicScorer()).Run(new TurnObservation(Hello, "I missed you, stay with me and talk. I'm so glad you're here!", Drives.Loneliness, 0.1), TestPersona.T0.AddMinutes(1));
        Assert.True(r.Satisfied);
        Assert.Empty(r.Milestones);
    }

    [Theory]
    [InlineData("Music")]
    [InlineData("two words")]
    [InlineData("9lives")]
    [InlineData("")]
    public void Bad_drive_names_are_refused(string name)
    {
        PersonaConfig c = TestPersona.Config();
        c.Drives.Add(TestPersona.Spec(name, 1, 1));
        Assert.Throws<PersonaConfigException>(() => c.ValidateShape());
    }

    [Fact]
    public void Too_many_or_unknown_kinds_are_refused()
    {
        PersonaConfig c = TestPersona.Config();
        c.Drive(Drives.Lust).Kind = "meter";
        Assert.Throws<PersonaConfigException>(() => c.ValidateShape());
        c = TestPersona.Config();
        for (int i = 0; i < 4; i++)
        {
            c.Drives.Add(TestPersona.Spec("extra" + i, 1, 1));
        }
        Assert.Throws<PersonaConfigException>(() => c.ValidateShape());
    }
}
