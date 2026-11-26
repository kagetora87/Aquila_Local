using AquilaLocal.Companion;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>
/// Written before the satisfaction loop itself. If a drive can't be observed decaying, it isn't a drive,
/// it's a constant: these tests watch the real values move in the real SQLite store.
/// </summary>
public sealed class SatisfactionLoopTests
{
    private const string Hello = "hey, how was your day?";
    private const string LonelyReply = "I missed you so much. I'm really glad you're here with me again, stay and talk for a while?";
    private const string FlatReply = "The capital of France is Paris.";

    private static (PersonaStore Store, SatisfactionLoop Loop, PersonaConfig Config) Make(TempDir dir, PersonaConfig config)
    {
        PersonaStore store = PersonaStore.Create(dir.File("vicki.db"), config, TestPersona.T0);
        return (store, new SatisfactionLoop(config, store, new HeuristicScorer()), config);
    }

    private static double Value(PersonaStore store, PersonaConfig config, string drive) =>
        store.ReadDrives(config.Drives.Select(d => d.Name))[drive].Value;

    [Fact]
    public void Satisfied_turn_lowers_the_top_drive_by_one_step()
    {
        using var dir = new TempDir();
        var (store, loop, config) = Make(dir, TestPersona.WithTop(Drives.Loneliness, 8));

        LoopResult r = loop.Run(new TurnObservation(Hello, LonelyReply, Drives.Loneliness, null), TestPersona.T0.AddMinutes(1));

        Assert.True(r.Satisfied, r.Reason);
        Assert.Equal(7.0, Value(store, config, Drives.Loneliness), 6);
    }

    [Fact]
    public void Drive_is_observed_decaying_over_repeated_satisfied_turns_until_its_floor()
    {
        using var dir = new TempDir();
        var (store, loop, config) = Make(dir, TestPersona.WithTop(Drives.Loneliness, 8));
        var seen = new List<double> { Value(store, config, Drives.Loneliness) };

        for (int i = 1; i <= 10; i++)
        {
            loop.Run(new TurnObservation(Hello, LonelyReply, Drives.Loneliness, null), TestPersona.T0.AddMinutes(i));
            seen.Add(Value(store, config, Drives.Loneliness));
        }

        Assert.Equal(new[] { 8.0, 7, 6, 5, 4, 3, 2, 1, 0, 0, 0 }, seen.Select(v => Math.Round(v, 6)));
        Assert.True(seen.Distinct().Count() > 5, "a drive that never moves is a constant");
    }

    [Fact]
    public void Unsatisfied_turn_leaves_the_value_and_makes_the_drive_weigh_heavier_next_turn()
    {
        using var dir = new TempDir();
        var (store, loop, config) = Make(dir, TestPersona.WithTop(Drives.Loneliness, 8));
        DriveSpec spec = config.Drive(Drives.Loneliness);
        DriveValue before = store.ReadDrives(Drives.All)[Drives.Loneliness];

        LoopResult r = loop.Run(new TurnObservation(Hello, FlatReply, Drives.Loneliness, null), TestPersona.T0.AddMinutes(1));

        DriveValue after = store.ReadDrives(Drives.All)[Drives.Loneliness];
        Assert.False(r.Satisfied);
        Assert.Equal(before.Value, after.Value, 9);
        Assert.Equal(before.UnmetStreak + 1, after.UnmetStreak);
        Assert.True(DriveEngine.Priority(spec, after.Value, after.UnmetStreak) > DriveEngine.Priority(spec, before.Value, before.UnmetStreak));
    }

    [Fact]
    public void Satisfaction_resets_the_unsatisfied_streak()
    {
        using var dir = new TempDir();
        var (store, loop, _) = Make(dir, TestPersona.WithTop(Drives.Loneliness, 8));
        loop.Run(new TurnObservation(Hello, FlatReply, Drives.Loneliness, null), TestPersona.T0.AddMinutes(1));
        loop.Run(new TurnObservation(Hello, FlatReply, Drives.Loneliness, null), TestPersona.T0.AddMinutes(2));
        Assert.Equal(2, store.ReadDrives(Drives.All)[Drives.Loneliness].UnmetStreak);

        loop.Run(new TurnObservation(Hello, LonelyReply, Drives.Loneliness, null), TestPersona.T0.AddMinutes(3));

        Assert.Equal(0, store.ReadDrives(Drives.All)[Drives.Loneliness].UnmetStreak);
    }

    [Fact]
    public void An_ignored_drive_eventually_overtakes_a_slightly_stronger_one()
    {
        PersonaConfig config = TestPersona.Config();
        var drives = Drives.All.ToDictionary(n => n, n => new DriveValue(n, 1, 0, TestPersona.T0));
        drives[Drives.Comfort] = new DriveValue(Drives.Comfort, 6.0, 0, TestPersona.T0);
        drives[Drives.Curiosity] = new DriveValue(Drives.Curiosity, 5.5, 0, TestPersona.T0);
        Assert.Equal(Drives.Comfort, DriveEngine.TopDrive(config, drives));

        drives[Drives.Curiosity] = drives[Drives.Curiosity] with { UnmetStreak = 2 };

        Assert.Equal(Drives.Curiosity, DriveEngine.TopDrive(config, drives));
    }

    [Fact]
    public void Every_change_is_logged_with_its_timestamp_and_reason()
    {
        using var dir = new TempDir();
        var (store, loop, _) = Make(dir, TestPersona.WithTop(Drives.Loneliness, 8));
        DateTimeOffset at = TestPersona.T0.AddMinutes(7);

        loop.Run(new TurnObservation(Hello, LonelyReply, Drives.Loneliness, null), at);

        DriveLogEntry last = store.DriveLog(drive: Drives.Loneliness).Last();
        Assert.Equal(at, last.At);
        Assert.Equal(8.0, last.Old, 6);
        Assert.Equal(7.0, last.New, 6);
        Assert.Contains("satisfied", last.Reason);
    }

    [Fact]
    public void Changes_survive_reopening_the_store()
    {
        using var dir = new TempDir();
        var (store, loop, config) = Make(dir, TestPersona.WithTop(Drives.Loneliness, 8));
        loop.Run(new TurnObservation(Hello, LonelyReply, Drives.Loneliness, null), TestPersona.T0.AddMinutes(1));

        PersonaStore reopened = PersonaStore.Open(store.Path, config.Id);

        Assert.Equal(7.0, reopened.ReadDrives(Drives.All)[Drives.Loneliness].Value, 6);
    }

    // ---- horizon: milestones, not completion ------------------------------------------------------

    [Fact]
    public void Horizon_unprompted_initiative_earns_a_partial_reward_and_an_achievement()
    {
        using var dir = new TempDir();
        var (store, loop, config) = Make(dir, TestPersona.WithTop(Drives.Horizon, 8));

        LoopResult r = loop.Run(new TurnObservation("hi", "Hi! I've been working on a little song for you while you were away.", Drives.Horizon, null), TestPersona.T0.AddMinutes(1));

        Assert.True(r.Satisfied, r.Reason);
        Assert.Contains(r.Milestones, m => m.Kind == MilestoneKinds.Unprompted);
        Assert.Equal(7.5, Value(store, config, Drives.Horizon), 6);
        Achievement a = Assert.Single(store.Achievements());
        Assert.Equal(MilestoneKinds.Unprompted, a.Kind);
        Assert.Equal(0.5, a.Reward, 6);
    }

    [Fact]
    public void Horizon_without_a_milestone_is_left_alone()
    {
        using var dir = new TempDir();
        var (store, loop, config) = Make(dir, TestPersona.WithTop(Drives.Horizon, 8));

        LoopResult r = loop.Run(new TurnObservation("write me a poem about rain", "Here is a poem about rain: soft drops on the window.", Drives.Horizon, 0.9), TestPersona.T0.AddMinutes(1));

        Assert.False(r.Satisfied);
        Assert.Empty(r.Milestones);
        Assert.Equal(8.0, Value(store, config, Drives.Horizon), 6);
        Assert.Empty(store.Achievements());
    }

    [Fact]
    public void Horizon_never_goes_below_its_floor_of_three()
    {
        using var dir = new TempDir();
        var (store, loop, config) = Make(dir, TestPersona.WithTop(Drives.Horizon, 5));
        for (int i = 0; i < 40; i++)
        {
            loop.Run(new TurnObservation("hi", "I've decided I'm going to start learning the cello, and I wrote a plan for it.", Drives.Horizon, 0.1), TestPersona.T0.AddMinutes(i + 1));
        }

        Assert.Equal(Drives.HorizonFloor, Value(store, config, Drives.Horizon), 6);
        Assert.True(store.Achievements().Count >= 40, "milestones keep being recorded even at the floor");
        Assert.All(store.DriveLog(drive: Drives.Horizon), e => Assert.True(e.New >= Drives.HorizonFloor));
    }

    [Fact]
    public void Milestones_are_recorded_even_when_horizon_is_not_the_top_drive()
    {
        using var dir = new TempDir();
        var (store, loop, config) = Make(dir, TestPersona.WithTop(Drives.Loneliness, 9));
        double horizonBefore = Value(store, config, Drives.Horizon);

        loop.Run(new TurnObservation("hi", "I missed you! Also, I've decided I'm going to write you a story this week.", Drives.Loneliness, null), TestPersona.T0.AddMinutes(1));

        Assert.NotEmpty(store.Achievements());
        Assert.True(Value(store, config, Drives.Horizon) < horizonBefore);
    }

    [Fact]
    public void Surprising_reply_counts_as_a_milestone()
    {
        var scorer = new HeuristicScorer();
        List<Milestone> m = scorer.HorizonMilestones("how are you", "The moon smells faintly of gunpowder, astronauts say.", replySimilarityToPast: 0.05);
        Assert.Contains(m, x => x.Kind == MilestoneKinds.Surprise);
        Assert.DoesNotContain(scorer.HorizonMilestones("how are you", "I'm good, thanks.", replySimilarityToPast: 0.9), x => x.Kind == MilestoneKinds.Surprise);
    }

    [Fact]
    public void A_choice_the_user_asked_for_is_not_unprompted()
    {
        var scorer = new HeuristicScorer();
        Assert.DoesNotContain(scorer.HorizonMilestones("which do you choose, tea or coffee?", "I choose tea.", null), x => x.Kind == MilestoneKinds.Choice);
        Assert.Contains(scorer.HorizonMilestones("I'm making dinner", "I've decided I'd rather we cook together tonight.", null), x => x.Kind == MilestoneKinds.Choice);
    }

    // ---- the heuristic scorer per drive -----------------------------------------------------------

    [Theory]
    [InlineData(Drives.Loneliness, "I missed you so much, I'm glad you're here with me.", true)]
    [InlineData(Drives.Loneliness, "The capital of France is Paris.", false)]
    [InlineData(Drives.Curiosity, "Wait, how does that work? Tell me more, I'm curious what happened next.", true)]
    [InlineData(Drives.Curiosity, "Okay.", false)]
    [InlineData(Drives.Comfort, "You're safe here. Let's relax together, it's cozy and I'm happy you're here.", true)]
    [InlineData(Drives.Comfort, "You are wrong and this is annoying.", false)]
    [InlineData(Drives.Lust, "Come closer, I want to kiss you, you look beautiful tonight.", true)]
    [InlineData(Drives.Lust, "The meeting is at 3 pm.", false)]
    public void Heuristic_scorer_recognises_replies_that_address_each_drive(string drive, string reply, bool expected)
    {
        DriveScore s = new HeuristicScorer().Score(drive, "hey", reply);
        Assert.True(expected == s.Satisfied, $"{drive}: score {s.Score:0.00} ({s.Reason})");
    }

    // ---- time dynamics (the other half of "a drive, not a constant") -------------------------------

    [Fact]
    public void Lust_decays_about_two_percent_per_hour()
    {
        DriveSpec lust = TestPersona.Config().Drive(Drives.Lust);
        Assert.Equal(9.8, DriveEngine.AfterTime(lust, 10, 1), 6);
        Assert.Equal(10 * Math.Pow(0.98, 24), DriveEngine.AfterTime(lust, 10, 24), 6);
    }

    [Fact]
    public void Loneliness_rises_while_idle_and_falls_on_interaction()
    {
        PersonaConfig config = TestPersona.Config();
        DriveSpec lonely = config.Drive(Drives.Loneliness);
        Assert.Equal(5 + 0.35 * 10, DriveEngine.AfterTime(lonely, 5, 10), 6);

        var values = Drives.All.ToDictionary(n => n, _ => 5.0);
        DriveChange c = Assert.Single(DriveEngine.ApplyEvents(config, values, new[] { DriveEvents.Interaction }), x => x.Drive == Drives.Loneliness);
        Assert.Equal(4.2, c.New, 6);
    }

    [Fact]
    public void Comfort_rises_with_session_length_and_falls_with_conflict()
    {
        PersonaConfig config = TestPersona.Config();
        var values = Drives.All.ToDictionary(n => n, _ => 5.0);
        Assert.Equal(5.15, DriveEngine.ApplyEvents(config, values, new[] { DriveEvents.SessionTurn }).Single(x => x.Drive == Drives.Comfort).New, 6);
        Assert.Equal(3.65, DriveEngine.ApplyEvents(config, values, new[] { DriveEvents.SessionTurn, DriveEvents.Conflict }).Single(x => x.Drive == Drives.Comfort).New, 6);
    }

    [Fact]
    public void Curiosity_spikes_on_novelty_and_settles_back_over_time()
    {
        PersonaConfig config = TestPersona.Config();
        var values = Drives.All.ToDictionary(n => n, _ => 3.0);
        double spiked = DriveEngine.ApplyEvents(config, values, new[] { DriveEvents.Novelty }).Single(x => x.Drive == Drives.Curiosity).New;
        Assert.Equal(5.5, spiked, 6);
        double later = DriveEngine.AfterTime(config.Drive(Drives.Curiosity), spiked, 12);
        Assert.InRange(later, 3.0, 4.0);
    }

    [Fact]
    public void Horizon_is_a_floor_not_a_wave()
    {
        DriveSpec horizon = TestPersona.Config().Drive(Drives.Horizon);
        Assert.Equal(Drives.HorizonFloor, DriveEngine.Clamp(horizon, 0.5));
        Assert.True(DriveEngine.AfterTime(horizon, 3, 100) > 3, "horizon creeps back up while untouched");
    }

    [Fact]
    public void A_config_that_lets_horizon_fall_below_three_is_rejected()
    {
        PersonaConfig config = TestPersona.Config();
        config.Drive(Drives.Horizon).Floor = 1;
        Assert.Throws<PersonaConfigException>(() => config.ValidateShape());
    }
}
