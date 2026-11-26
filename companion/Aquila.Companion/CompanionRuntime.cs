using System.Globalization;

namespace AquilaLocal.Companion;

public sealed record PreparedTurn(
    string PersonaId,
    string SystemPrompt,
    string InnerState,
    string RecalledText,
    string TopDrive,
    IReadOnlyList<string> Events,
    IReadOnlyList<DriveChange> Changes,
    IReadOnlyList<Recalled> Recalled);

public sealed record CompletedTurn(LoopResult Loop, long ExchangeId, bool SummaryDue);

/// <summary>
/// The persona-agnostic pipeline for one persona: tick drives, apply message events, build her context,
/// then after the reply run the satisfaction loop and remember the exchange. Everything it reads and writes
/// goes through that persona's own store.
/// </summary>
public sealed class CompanionRuntime
{
    /// <summary>A user message less similar than this to everything in her memory is a novelty event.</summary>
    public const double NoveltyBelow = 0.45;

    public PersonaConfig Config { get; }

    public PersonaStore Store { get; }

    public VectorMemory Memory { get; }

    private readonly SatisfactionLoop loop;

    private CompanionRuntime(PersonaConfig config, PersonaStore store, IEmbedder embedder, ISatisfactionScorer scorer)
    {
        Config = config;
        Store = store;
        Memory = new VectorMemory(store, embedder);
        loop = new SatisfactionLoop(config, store, scorer);
    }

    /// <summary>Opens a persona for chatting. Throws (never falls back) if anything about her is missing or unreadable.</summary>
    public static CompanionRuntime Open(PersonaRegistry registry, string personaId, IEmbedder embedder, ISatisfactionScorer? scorer = null)
    {
        PersonaConfig config = registry.LoadConfig(personaId);
        List<string> missing = config.MissingInputs();
        if (missing.Count > 0)
        {
            throw new PersonaConfigException($"{config.Name} isn't ready. Still missing: {string.Join("; ", missing)}.");
        }
        PersonaStore store = registry.OpenStore(personaId);
        store.ReadDrives(config.Drives.Select(d => d.Name));
        return new CompanionRuntime(config, store, embedder, scorer ?? new HeuristicScorer());
    }

    public static CompanionRuntime ForTests(PersonaConfig config, PersonaStore store, IEmbedder embedder) =>
        new(config, store, embedder, new HeuristicScorer());

    /// <param name="verbatimTurnIds">Turns already in the prompt word for word, so recall doesn't repeat them.</param>
    /// <param name="sameSession">True when this isn't the first message of the chat.</param>
    public PreparedTurn Prepare(string userText, ISet<string> verbatimTurnIds, bool sameSession, DateTimeOffset now)
    {
        string[] names = Config.Drives.Select(d => d.Name).ToArray();
        Dictionary<string, DriveValue> drives = Store.ReadDrives(names);
        DateTimeOffset lastTick = Store.LastTick;

        List<DriveChange> changes = DriveEngine.Tick(Config, drives, lastTick, now);
        var values = drives.ToDictionary(kv => kv.Key, kv => kv.Value.Value);
        foreach (DriveChange c in changes)
        {
            values[c.Drive] = c.New;
        }

        float[] q = Memory.Embed(userText);
        double? closest = Memory.MaxSimilarity(q);
        bool novel = closest is double s && s < NoveltyBelow;
        List<string> events = HeuristicScorer.UserEvents(userText, novel, sameSession);
        List<DriveChange> eventChanges = DriveEngine.ApplyEvents(Config, values, events);
        if (novel && eventChanges.FirstOrDefault(c => c.Reason.Contains(DriveEvents.Novelty)) is DriveChange nc)
        {
            eventChanges[eventChanges.IndexOf(nc)] = nc with { Reason = nc.Reason + string.Create(CultureInfo.InvariantCulture, $" (closest memory {closest:0.00})") };
        }
        changes.AddRange(eventChanges);

        Store.Apply(changes, now, meta: new Dictionary<string, string>
        {
            ["last_tick"] = PersonaStore.Iso(now),
            ["last_interaction"] = PersonaStore.Iso(now)
        });

        drives = Store.ReadDrives(names);
        string top = DriveEngine.TopDrive(Config, drives);
        string inner = ContextInjector.DriveBlock(Config, drives, top);
        string achievements = ContextInjector.AchievementsBlock(Store.Achievements(ContextInjector.AchievementsShown), Store.AchievementCount());
        if (achievements.Length > 0)
        {
            inner += "\n" + achievements;
        }
        List<Recalled> recalled = Memory.Search(q, Config.Memory.RetrieveTopK, verbatimTurnIds);
        return new PreparedTurn(Config.Id, ContextInjector.SystemPrompt(Config, Store.ReadSummary()), inner,
            ContextInjector.RecalledBlock(recalled), top, events, changes, recalled);
    }

    public CompletedTurn Complete(PreparedTurn prepared, string chatId, string turnId, string userText, string reply, DateTimeOffset now)
    {
        if (prepared.PersonaId != Config.Id)
        {
            throw new InvalidOperationException($"A turn prepared for '{prepared.PersonaId}' can't be completed by '{Config.Id}'.");
        }
        double? similarity = Memory.MaxSimilarity(Memory.Embed(reply), minimumMemories: 10);
        LoopResult result = loop.Run(new TurnObservation(userText, reply, prepared.TopDrive, similarity), now);
        long id = Memory.Add(chatId, turnId, now, userText, reply);
        return new CompletedTurn(result, id, Store.TurnsSinceSummary >= Config.Memory.SummarizeEveryTurns);
    }

    public Summarizer Summarizer(ITextGenerator generator) => new(Config, Store, generator);
}
