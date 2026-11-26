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
    IReadOnlyList<Recalled> Recalled,
    InjectedContext Context,
    AppearanceCard Appearance);

/// <param name="SelfChanges">Wardrobe changes she made herself during this turn (also handed to the satisfaction loop).</param>
public sealed record CompletedTurn(LoopResult Loop, long ExchangeId, bool SummaryDue, IReadOnlyList<WardrobeEntry> SelfChanges);

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

    private readonly List<WardrobeEntry> selfChanges = new();

    /// <summary>The user's own "About you" profile (shared by all companions; it isn't persona memory). Set by the app.</summary>
    public string AboutUser { get; set; } = "";

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
        store.ReadAppearance();
        return new CompanionRuntime(config, store, embedder, scorer ?? new HeuristicScorer());
    }

    public static CompanionRuntime ForTests(PersonaConfig config, PersonaStore store, IEmbedder embedder) =>
        new(config, store, embedder, new HeuristicScorer());

    /// <param name="verbatimTurnIds">Turns already in the prompt word for word, so recall doesn't repeat them.</param>
    /// <param name="sameSession">True when this isn't the first message of the chat.</param>
    public PreparedTurn Prepare(string userText, ISet<string> verbatimTurnIds, bool sameSession, DateTimeOffset now)
    {
        using IDisposable connection = Store.Hold();
        string[] names = Config.Drives.Select(d => d.Name).ToArray();
        Dictionary<string, DriveValue> drives = Store.ReadDrives(names);
        // Read (and size-check) her card before anything is changed, so a bad card stops the turn cleanly.
        AppearanceCard card = Store.ReadAppearance();
        ContextInjector.AppearanceBlock(Config.Name, card);
        lock (selfChanges)
        {
            selfChanges.Clear();
        }
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

        (InjectedContext context, string top, card) = Assemble();
        List<Recalled> recalled = Memory.Search(q, Config.Memory.RetrieveTopK, verbatimTurnIds);
        return new PreparedTurn(Config.Id, context.SystemPrompt, context.InnerState,
            ContextInjector.RecalledBlock(recalled), top, events, changes, recalled, context, card);
    }

    /// <summary>
    /// Reads her current state from her own store and assembles her context: pinned summary, drives, appearance
    /// card. Reads only; throws (naming the file) if anything is missing. This is the part that runs every turn
    /// and is held under 100 ms.
    /// </summary>
    public (InjectedContext Context, string TopDrive, AppearanceCard Card) Assemble()
    {
        using IDisposable connection = Store.Hold();
        Dictionary<string, DriveValue> drives = Store.ReadDrives(Config.Drives.Select(d => d.Name));
        AppearanceCard card = Store.ReadAppearance();
        string top = DriveEngine.TopDrive(Config, drives);
        string achievements = ContextInjector.AchievementsBlock(Store.Achievements(ContextInjector.AchievementsShown), Store.AchievementCount());
        return (ContextInjector.Assemble(Config, Store.ReadSummary(), drives, top, card, achievements, AboutUser), top, card);
    }

    /// <summary>
    /// Logs a wardrobe change (hers through the change_outfit tool, or the operator's). Anything the change
    /// leaves out stays as it was. Append-only: the old outfit stays in her diary.
    /// </summary>
    public WardrobeEntry ChangeWardrobe(WardrobeChange change, string reason, string author, DateTimeOffset now)
    {
        if (change.IsEmpty)
        {
            throw new ArgumentException("Say what changed: outfit, colour, fabric or condition.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A wardrobe change needs a reason.");
        }
        Wardrobe current = Store.ReadAppearance().Wardrobe.Wardrobe;
        var next = new Wardrobe
        {
            Outfit = Pick(change.Outfit, current.Outfit),
            Color = Pick(change.Color, current.Color),
            Fabric = Pick(change.Fabric, current.Fabric),
            Condition = Pick(change.Condition, current.Condition)
        };
        long id = Store.AppendWardrobe(next, author, reason, now);
        WardrobeEntry entry = Store.WardrobeHistory().Last(e => e.Id == id);
        if (author == MemoryAuthors.Persona)
        {
            lock (selfChanges)
            {
                selfChanges.Add(entry);
            }
        }
        return entry;
    }

    private static string Pick(string? change, string current) => string.IsNullOrWhiteSpace(change) ? current : change.Trim();

    /// <param name="spoken">True when the user said this aloud (it reached her as a transcript), so she can later tell hearing from reading.</param>
    public CompletedTurn Complete(PreparedTurn prepared, string chatId, string turnId, string userText, string reply, DateTimeOffset now, bool spoken = false)
    {
        if (prepared.PersonaId != Config.Id)
        {
            throw new InvalidOperationException($"A turn prepared for '{prepared.PersonaId}' can't be completed by '{Config.Id}'.");
        }
        using IDisposable connection = Store.Hold();
        double? similarity = Memory.MaxSimilarity(Memory.Embed(reply), minimumMemories: 10);
        List<WardrobeEntry> mine;
        lock (selfChanges)
        {
            mine = selfChanges.ToList();
            selfChanges.Clear();
        }
        LoopResult result = loop.Run(new TurnObservation(userText, reply, prepared.TopDrive, similarity, mine), now);
        long id = Memory.Add(chatId, turnId, now, userText, reply, spoken);
        return new CompletedTurn(result, id, Store.TurnsSinceSummary >= Config.Memory.SummarizeEveryTurns, mine);
    }

    public Summarizer Summarizer(ITextGenerator generator) => new(Config, Store, generator);
}
