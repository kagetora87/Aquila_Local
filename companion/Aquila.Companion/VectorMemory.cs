namespace AquilaLocal.Companion;

/// <summary>Turns text into a unit-length vector. The app plugs in the bundled MiniLM model.</summary>
public interface IEmbedder
{
    float[] Embed(string text);
}

public sealed record Recalled(Exchange Exchange, double Similarity);

/// <summary>
/// The persona's private vector memory: every exchange is embedded and kept in her own store. Vectors are
/// cached in memory per persona (a few months of chat is a few tens of MB) and searched by brute force,
/// which is exact and fast enough at this size.
/// </summary>
public sealed class VectorMemory(PersonaStore store, IEmbedder embedder)
{
    /// <summary>Two recalled exchanges this similar are treated as the same memory.</summary>
    public const double DuplicateAbove = 0.95;

    private readonly object gate = new();
    private readonly List<(long Id, float[] Vector)> cache = new();
    private long loadedThrough;
    private bool loaded;

    public PersonaStore Store => store;

    public static string TextOf(string user, string reply) => "User: " + user.Trim() + "\nReply: " + reply.Trim();

    public float[] Embed(string text) => embedder.Embed(text);

    public long Add(string chatId, string turnId, DateTimeOffset at, string user, string reply)
    {
        float[] v = embedder.Embed(TextOf(user, reply));
        long id = store.AddExchange(chatId, turnId, at, user, reply, v);
        lock (gate)
        {
            if (loaded)
            {
                cache.RemoveAll(x => x.Id == id);
                cache.Add((id, v));
                loadedThrough = Math.Max(loadedThrough, id);
            }
        }
        return id;
    }

    private List<(long Id, float[] Vector)> Snapshot()
    {
        lock (gate)
        {
            if (!loaded)
            {
                cache.AddRange(store.Vectors());
                loadedThrough = cache.Count == 0 ? 0 : cache.Max(x => x.Id);
                loaded = true;
            }
            return cache.ToList();
        }
    }

    public int Count => Snapshot().Count;

    /// <summary>Highest similarity between <paramref name="vector"/> and any stored exchange (null when memory is too small to judge).</summary>
    public double? MaxSimilarity(float[] vector, int minimumMemories = 5)
    {
        List<(long Id, float[] Vector)> all = Snapshot();
        if (all.Count < minimumMemories)
        {
            return null;
        }
        double best = double.NegativeInfinity;
        foreach ((_, float[] v) in all)
        {
            best = Math.Max(best, Cosine(vector, v));
        }
        return best;
    }

    /// <summary>Top-k most relevant past exchanges, without near-duplicates and without the excluded turns.</summary>
    public List<Recalled> Search(string query, int topK, ISet<string>? excludeTurnIds = null, double minimumSimilarity = 0.2) =>
        string.IsNullOrWhiteSpace(query) ? new List<Recalled>() : Search(embedder.Embed(query), topK, excludeTurnIds, minimumSimilarity);

    public List<Recalled> Search(float[] q, int topK, ISet<string>? excludeTurnIds = null, double minimumSimilarity = 0.2)
    {
        if (topK <= 0)
        {
            return new List<Recalled>();
        }
        List<(long Id, double Sim, float[] Vector)> ranked = Snapshot()
            .Select(x => (x.Id, Sim: Cosine(q, x.Vector), x.Vector))
            .Where(x => x.Sim >= minimumSimilarity)
            .OrderByDescending(x => x.Sim)
            .Take(Math.Max(topK * 6, 30))
            .ToList();
        Dictionary<long, Exchange> rows = store.ExchangesById(ranked.Select(r => r.Id)).ToDictionary(e => e.Id);
        var chosen = new List<(Exchange E, double Sim, float[] V)>();
        var seenText = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((long id, double sim, float[] v) in ranked)
        {
            if (!rows.TryGetValue(id, out Exchange? e) || (excludeTurnIds?.Contains(e.TurnId) ?? false))
            {
                continue;
            }
            if (!seenText.Add(TextOf(e.User, e.Reply)) || chosen.Any(c => Cosine(c.V, v) >= DuplicateAbove))
            {
                continue;
            }
            chosen.Add((e, sim, v));
            if (chosen.Count == topK)
            {
                break;
            }
        }
        return chosen.Select(c => new Recalled(c.E, c.Sim)).ToList();
    }

    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            return 0;
        }
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / Math.Sqrt(na * nb);
    }
}
