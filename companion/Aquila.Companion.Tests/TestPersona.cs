using AquilaLocal.Companion;

namespace Aquila.Companion.Tests;

/// <summary>A complete, ready persona config for tests, plus a temp folder that cleans itself up.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aquila-companion-tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

internal static class TestPersona
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 26, 20, 0, 0, TimeSpan.Zero);

    public static PersonaConfig Config(string id = "vicki", string name = "Vicki") => new()
    {
        Id = id,
        Name = name,
        PersonalitySheet = $"{name} is warm, wry and curious.",
        BaselineFrom = "Sophia",
        Drift = new DriftClause { Text = "Baseline cloned from Sophia. Divergence is success, not error." },
        Drives = new List<DriveSpec>
        {
            Spec(Drives.Lust, weight: 1.0, initial: 5, decay: 0.02, events: new() { [DriveEvents.Affection] = 0.8 }),
            Spec(Drives.Curiosity, weight: 1.0, initial: 4, decay: 0.08, resting: 3, events: new() { [DriveEvents.Novelty] = 2.5 }),
            Spec(Drives.Comfort, weight: 1.0, initial: 5, events: new() { [DriveEvents.SessionTurn] = 0.15, [DriveEvents.Conflict] = -1.5 }),
            Spec(Drives.Loneliness, weight: 1.0, initial: 5, rise: 0.35, events: new() { [DriveEvents.Interaction] = -0.8 }),
            Spec(Drives.Horizon, weight: 1.0, initial: 6, rise: 0.03, floor: Drives.HorizonFloor, step: 0.5),
        },
        Seed = new PersonaSeed { Summary = new StateBlock { Facts = { "Takeru lives in Japan." }, Feelings = { "Fond of Takeru." }, Hooks = { "Ask about the cello." } } }
    };

    public static DriveSpec Spec(string name, double weight, double initial, double decay = 0, double resting = 0, double rise = 0,
        double floor = 0, double step = 1.0, Dictionary<string, double>? events = null) => new()
    {
        Name = name,
        Weight = weight,
        Initial = initial,
        DecayPerHour = decay,
        RestingLevel = resting,
        RisePerHour = rise,
        Floor = floor,
        SatisfyStep = step,
        Events = events ?? new(),
        Guidance = new DriveGuidance { Low = $"{name} is quiet.", Mid = $"{name} is present.", High = $"{name} is pressing; act on it." }
    };

    /// <summary>Makes one drive the clear top drive by giving it a big weight.</summary>
    public static PersonaConfig WithTop(string drive, double initial = 8)
    {
        PersonaConfig c = Config();
        DriveSpec s = c.Drive(drive);
        s.Weight = 3.0;
        s.Initial = initial;
        return c;
    }
}

/// <summary>Deterministic bag-of-words embedder: similar wording gives similar vectors.</summary>
internal sealed class FakeEmbedder : IEmbedder
{
    public const int Dim = 256;

    public int Calls { get; private set; }

    public float[] Embed(string text)
    {
        Calls++;
        var v = new float[Dim];
        foreach (string w in text.ToLowerInvariant().Split(new[] { ' ', '\n', '.', ',', '!', '?', ':', ';', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int h = (int)((uint)StableHash(w) % Dim);
            v[h] += 1f;
        }
        double n = Math.Sqrt(v.Sum(x => (double)x * x));
        if (n == 0) { v[0] = 1; n = 1; }
        for (int i = 0; i < Dim; i++) v[i] = (float)(v[i] / n);
        return v;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int h = 23;
            foreach (char ch in s) h = h * 31 + ch;
            return h;
        }
    }
}
