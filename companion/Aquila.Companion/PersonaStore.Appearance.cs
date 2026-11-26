using Microsoft.Data.Sqlite;

namespace AquilaLocal.Companion;

/// <param name="Supersedes">The trait entry this one amends, if any.</param>
/// <param name="SupersededBy">The newer entry that amends this one, if any.</param>
public sealed record TraitEntry(long Id, DateTimeOffset At, string Name, string Text, string Author, long? Supersedes, string Reason, long? SupersededBy)
{
    public bool Active => SupersededBy == null;
}

public sealed record WardrobeEntry(long Id, DateTimeOffset At, Wardrobe Wardrobe, string Author, string Reason);

/// <summary>A partial wardrobe change: anything left null stays as it is.</summary>
public sealed record WardrobeChange(string? Outfit = null, string? Color = null, string? Fabric = null, string? Condition = null)
{
    public bool IsEmpty => new[] { Outfit, Color, Fabric, Condition }.All(string.IsNullOrWhiteSpace);
}

/// <summary>The canonical appearance card as it stands: permanent traits, identity anchor, current wardrobe.</summary>
public sealed record AppearanceCard(string PersonaId, IReadOnlyList<TraitEntry> Traits, string IdentityAnchor, WardrobeEntry Wardrobe);

public sealed partial class PersonaStore
{
    /// <summary>The trait name the identity anchor is stored under.</summary>
    public const string AnchorTrait = "identity anchor";

    internal const string AppearanceSchema = """
        CREATE TABLE IF NOT EXISTS appearance_traits(id INTEGER PRIMARY KEY AUTOINCREMENT, persona_id TEXT NOT NULL, at TEXT NOT NULL, name TEXT NOT NULL, text TEXT NOT NULL, author TEXT NOT NULL, supersedes INTEGER REFERENCES appearance_traits(id), reason TEXT NOT NULL DEFAULT '');
        CREATE UNIQUE INDEX IF NOT EXISTS appearance_traits_supersedes_once ON appearance_traits(supersedes) WHERE supersedes IS NOT NULL;
        CREATE TRIGGER IF NOT EXISTS appearance_traits_no_update BEFORE UPDATE ON appearance_traits BEGIN SELECT RAISE(ABORT, 'permanent traits are append-only; the operator records an amendment instead'); END;
        CREATE TRIGGER IF NOT EXISTS appearance_traits_no_delete BEFORE DELETE ON appearance_traits BEGIN SELECT RAISE(ABORT, 'permanent traits are append-only; the operator records an amendment instead'); END;
        CREATE TABLE IF NOT EXISTS wardrobe_log(id INTEGER PRIMARY KEY AUTOINCREMENT, persona_id TEXT NOT NULL, at TEXT NOT NULL, outfit TEXT NOT NULL, color TEXT NOT NULL, fabric TEXT NOT NULL, condition TEXT NOT NULL, author TEXT NOT NULL, reason TEXT NOT NULL);
        CREATE TRIGGER IF NOT EXISTS wardrobe_log_no_update BEFORE UPDATE ON wardrobe_log BEGIN SELECT RAISE(ABORT, 'the wardrobe diary is append-only; log a new change instead'); END;
        CREATE TRIGGER IF NOT EXISTS wardrobe_log_no_delete BEFORE DELETE ON wardrobe_log BEGIN SELECT RAISE(ABORT, 'the wardrobe diary is append-only; log a new change instead'); END;
        """;

    /// <summary>True when her appearance card has been set up in this store.</summary>
    public bool HasAppearance
    {
        get
        {
            using SqliteConnection c = Connect();
            return Scalar<long>(c, null, "SELECT COUNT(*) FROM appearance_traits WHERE persona_id=$p", ("$p", PersonaId)) > 0;
        }
    }

    /// <summary>Reads her card exactly as stored. Throws, naming this file, if any part is missing or unreadable.</summary>
    public AppearanceCard ReadAppearance()
    {
        List<TraitEntry> traits;
        List<WardrobeEntry> wardrobe;
        try
        {
            traits = TraitHistory();
            wardrobe = WardrobeHistory();
        }
        catch (SqliteException ex)
        {
            throw new AppearanceCardUnavailableException(PersonaId, $"it could not be read from {Path} ({ex.Message}).", ex);
        }
        List<TraitEntry> active = traits.Where(t => t.Active).ToList();
        TraitEntry? anchor = active.FirstOrDefault(t => t.Name == AnchorTrait);
        List<TraitEntry> permanent = active.Where(t => t.Name != AnchorTrait && t.Text.Length > 0).ToList();
        if (permanent.Count == 0)
        {
            throw new AppearanceCardUnavailableException(PersonaId, $"{Path} has no permanent traits. Set up her appearance in Companions first.");
        }
        if (anchor == null || anchor.Text.Length == 0)
        {
            throw new AppearanceCardUnavailableException(PersonaId, $"{Path} has no identity anchor.");
        }
        if (wardrobe.Count == 0)
        {
            throw new AppearanceCardUnavailableException(PersonaId, $"{Path} has no wardrobe entry.");
        }
        return new AppearanceCard(PersonaId, permanent, anchor.Text, wardrobe[^1]);
    }

    public List<TraitEntry> TraitHistory()
    {
        using SqliteConnection c = Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT t.id,t.at,t.name,t.text,t.author,t.supersedes,t.reason,(SELECT n.id FROM appearance_traits n WHERE n.supersedes=t.id)
            FROM appearance_traits t WHERE t.persona_id=$p ORDER BY t.id
            """;
        cmd.Parameters.AddWithValue("$p", PersonaId);
        var list = new List<TraitEntry>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new TraitEntry(r.GetInt64(0), ParseIso(r.GetString(1)), r.GetString(2), r.GetString(3), r.GetString(4),
                r.IsDBNull(5) ? null : r.GetInt64(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetInt64(7)));
        }
        return list;
    }

    /// <summary>Her wardrobe diary, oldest first. The last entry is what she's wearing now.</summary>
    public List<WardrobeEntry> WardrobeHistory()
    {
        using SqliteConnection c = Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,at,outfit,color,fabric,condition,author,reason FROM wardrobe_log WHERE persona_id=$p ORDER BY id";
        cmd.Parameters.AddWithValue("$p", PersonaId);
        var list = new List<WardrobeEntry>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new WardrobeEntry(r.GetInt64(0), ParseIso(r.GetString(1)),
                new Wardrobe { Outfit = r.GetString(2), Color = r.GetString(3), Fabric = r.GetString(4), Condition = r.GetString(5) },
                r.GetString(6), r.GetString(7)));
        }
        return list;
    }

    /// <summary>Writes her card from seed data into a store that has none yet (one set up before cards existed).</summary>
    public void SeedAppearance(AppearanceSeed seed, DateTimeOffset at)
    {
        if (!seed.IsComplete)
        {
            throw new PersonaConfigException("Her appearance card isn't complete yet: it needs at least one permanent trait, an identity anchor and an outfit.");
        }
        using SqliteConnection c = Connect();
        using SqliteTransaction tx = c.BeginTransaction();
        if (Scalar<long>(c, tx, "SELECT COUNT(*) FROM appearance_traits WHERE persona_id=$p", ("$p", PersonaId)) > 0)
        {
            throw new InvalidOperationException("Her appearance card is already set up. Permanent traits can only be amended, one at a time, with a reason.");
        }
        InsertAppearance(c, tx, seed, at, "appearance card set up from her persona file");
        tx.Commit();
    }

    private void InsertAppearance(SqliteConnection c, SqliteTransaction tx, AppearanceSeed seed, DateTimeOffset at, string reason)
    {
        foreach (AppearanceTrait t in seed.Traits)
        {
            InsertTrait(c, tx, at, t.Name.Trim(), t.Text.Trim(), MemoryAuthors.Seed, null, reason);
        }
        InsertTrait(c, tx, at, AnchorTrait, seed.IdentityAnchor.Trim(), MemoryAuthors.Seed, null, reason);
        InsertWardrobe(c, tx, at, seed.Wardrobe, MemoryAuthors.Seed, "starting wardrobe from her appearance card");
    }

    /// <summary>
    /// Records a deliberate amendment to a permanent trait (or adds a new one). Only the operator can do this,
    /// and always with a reason; the original stays in her history. The persona herself can't touch her traits.
    /// </summary>
    public long AmendTrait(string name, string text, string author, string reason, DateTimeOffset at)
    {
        if (author != MemoryAuthors.Operator)
        {
            throw new InvalidOperationException("Permanent traits are locked: only the operator can amend them.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("An amendment to a permanent trait needs a reason.");
        }
        name = name.Trim();
        text = text.Trim();
        if (name.Length == 0 || name.Length > 60 || text.Length > AppearanceSeed.MaxTraitChars)
        {
            throw new ArgumentException($"A trait needs a name of up to 60 characters and a description of up to {AppearanceSeed.MaxTraitChars}.");
        }
        if (name == AnchorTrait && text.Length == 0)
        {
            throw new InvalidOperationException("The identity anchor can be reworded but never removed.");
        }
        using SqliteConnection c = Connect();
        using SqliteTransaction tx = c.BeginTransaction();
        long found = Scalar<long>(c, tx, """
            SELECT t.id FROM appearance_traits t WHERE t.persona_id=$p AND t.name=$n
            AND NOT EXISTS (SELECT 1 FROM appearance_traits n WHERE n.supersedes=t.id) ORDER BY t.id DESC LIMIT 1
            """, ("$p", PersonaId), ("$n", name));
        long? current = found > 0 ? found : null;
        if (current == null && text.Length == 0)
        {
            throw new ArgumentException($"She has no '{name}' trait to withdraw.");
        }
        long id = InsertTrait(c, tx, at, name, text, author, current, reason.Trim());
        tx.Commit();
        return id;
    }

    /// <summary>Logs a wardrobe change. Never overwrites: the new entry becomes what she's wearing, and the old one stays in her diary.</summary>
    public long AppendWardrobe(Wardrobe wardrobe, string author, string reason, DateTimeOffset at)
    {
        if (author != MemoryAuthors.Persona && author != MemoryAuthors.Operator)
        {
            throw new ArgumentException($"Unknown author '{author}' for a wardrobe change.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A wardrobe change needs a reason.");
        }
        if (string.IsNullOrWhiteSpace(wardrobe.Outfit))
        {
            throw new ArgumentException("A wardrobe entry needs an outfit.");
        }
        using SqliteConnection c = Connect();
        using SqliteTransaction tx = c.BeginTransaction();
        long id = InsertWardrobe(c, tx, at, wardrobe, author, reason.Trim());
        tx.Commit();
        return id;
    }

    private long InsertTrait(SqliteConnection c, SqliteTransaction tx, DateTimeOffset at, string name, string text, string author, long? supersedes, string reason)
    {
        Exec(c, tx, "INSERT INTO appearance_traits(persona_id,at,name,text,author,supersedes,reason) VALUES($p,$t,$n,$x,$a,$s,$r)",
            ("$p", PersonaId), ("$t", Iso(at)), ("$n", name), ("$x", text), ("$a", author), ("$s", supersedes.HasValue ? supersedes.Value : DBNull.Value), ("$r", reason));
        return Scalar<long>(c, tx, "SELECT last_insert_rowid()");
    }

    private long InsertWardrobe(SqliteConnection c, SqliteTransaction tx, DateTimeOffset at, Wardrobe w, string author, string reason)
    {
        Exec(c, tx, "INSERT INTO wardrobe_log(persona_id,at,outfit,color,fabric,condition,author,reason) VALUES($p,$t,$o,$c,$f,$k,$a,$r)",
            ("$p", PersonaId), ("$t", Iso(at)), ("$o", Clip(w.Outfit)), ("$c", Clip(w.Color)), ("$f", Clip(w.Fabric)), ("$k", Clip(w.Condition)), ("$a", author), ("$r", reason));
        return Scalar<long>(c, tx, "SELECT last_insert_rowid()");
    }

    private static string Clip(string? s)
    {
        s = (s ?? "").Replace('\n', ' ').Trim();
        return s.Length > 200 ? s[..200] : s;
    }
}
