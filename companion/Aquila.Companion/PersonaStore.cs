using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace AquilaLocal.Companion;

public sealed record DriveValue(string Name, double Value, int UnmetStreak, DateTimeOffset UpdatedAt);

public sealed record DriveChange(string Drive, double Old, double New, string Reason);

public sealed record DriveLogEntry(long Id, DateTimeOffset At, string Drive, double Old, double New, string Reason);

public sealed record Achievement(long Id, DateTimeOffset At, string Kind, string Note, double Reward);

/// <param name="Spoken">True when the user said it aloud (transcribed from speech) rather than typed it.</param>
public sealed record Exchange(long Id, string ChatId, string TurnId, DateTimeOffset At, string User, string Reply, bool Folded, bool Spoken = false);

public static class MemoryLayers
{
    public const string Facts = "facts";
    public const string Feelings = "feelings";
    public const string Hooks = "hooks";
    public static readonly IReadOnlyList<string> All = new[] { Facts, Feelings, Hooks };
}

public static class MemoryAuthors
{
    public const string Seed = "seed";
    /// <summary>Written by the persona's own summary pass.</summary>
    public const string Persona = "persona";
    public const string Operator = "operator";
}

/// <param name="Supersedes">The entry this one corrects, if any.</param>
/// <param name="SupersededBy">The newer entry that corrects this one, if any.</param>
public sealed record MemoryEntry(long Id, DateTimeOffset At, string Layer, string Text, string Author, long? Supersedes, string Reason, long? SupersededBy)
{
    /// <summary>In force: not corrected by a newer entry, and not a withdrawal.</summary>
    public bool Active => SupersededBy == null && Text.Length > 0;
}

public sealed record SessionInfo(string ChatId, DateTimeOffset StartedAt, DateTimeOffset LastAt, int Turns);

/// <summary>
/// One persona's private state: drives, the drive change log, achievements, the pinned summary block,
/// sessions and every exchange with its embedding. Each persona has its own SQLite file, and every row
/// also carries the persona id and every query filters on it, so one persona's state can never be read
/// through another's store.
/// </summary>
public sealed partial class PersonaStore
{
    public const int SchemaVersion = 1;

    private readonly string connectionString;

    public string PersonaId { get; }

    public string Path { get; }

    private PersonaStore(string path, string personaId, bool create)
    {
        Path = System.IO.Path.GetFullPath(path);
        PersonaId = personaId;
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();
    }

    /// <summary>Creates a brand-new store and writes the persona's starting drives and summary. Refuses to overwrite.</summary>
    public static PersonaStore Create(string path, PersonaConfig config, DateTimeOffset now)
    {
        List<string> missing = config.MissingInputs();
        if (missing.Count > 0)
        {
            throw new PersonaConfigException($"{config.Name} can't be set up yet. Still missing: {string.Join("; ", missing)}.");
        }
        if (File.Exists(path))
        {
            throw new PersonaConfigException($"{config.Name} already has a state store at {path}. It was not touched.");
        }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var store = new PersonaStore(path, config.Id, create: true);
        using SqliteConnection c = store.Connect();
        using SqliteTransaction tx = c.BeginTransaction();
        Exec(c, tx, """
            CREATE TABLE meta(persona_id TEXT NOT NULL, key TEXT NOT NULL, value TEXT NOT NULL, PRIMARY KEY(persona_id, key));
            CREATE TABLE drives(persona_id TEXT NOT NULL, name TEXT NOT NULL, value REAL NOT NULL, unmet_streak INTEGER NOT NULL DEFAULT 0, updated_at TEXT NOT NULL, PRIMARY KEY(persona_id, name));
            CREATE TABLE drive_log(id INTEGER PRIMARY KEY AUTOINCREMENT, persona_id TEXT NOT NULL, at TEXT NOT NULL, drive TEXT NOT NULL, old REAL NOT NULL, new REAL NOT NULL, reason TEXT NOT NULL);
            CREATE INDEX drive_log_by_persona ON drive_log(persona_id, id);
            CREATE TABLE exchanges(id INTEGER PRIMARY KEY AUTOINCREMENT, persona_id TEXT NOT NULL, chat_id TEXT NOT NULL, turn_id TEXT NOT NULL, at TEXT NOT NULL, user_text TEXT NOT NULL, reply_text TEXT NOT NULL, vector BLOB, folded INTEGER NOT NULL DEFAULT 0, spoken INTEGER NOT NULL DEFAULT 0, UNIQUE(persona_id, turn_id));
            CREATE INDEX exchanges_by_persona ON exchanges(persona_id, id);
            CREATE TABLE achievements(id INTEGER PRIMARY KEY AUTOINCREMENT, persona_id TEXT NOT NULL, at TEXT NOT NULL, kind TEXT NOT NULL, note TEXT NOT NULL, reward REAL NOT NULL);
            CREATE TABLE memory_entries(id INTEGER PRIMARY KEY AUTOINCREMENT, persona_id TEXT NOT NULL, at TEXT NOT NULL, layer TEXT NOT NULL CHECK(layer IN ('facts','feelings','hooks')), text TEXT NOT NULL, author TEXT NOT NULL, supersedes INTEGER REFERENCES memory_entries(id), reason TEXT NOT NULL DEFAULT '');
            CREATE UNIQUE INDEX memory_supersedes_once ON memory_entries(supersedes) WHERE supersedes IS NOT NULL;
            CREATE TABLE memory_folds(id INTEGER PRIMARY KEY AUTOINCREMENT, persona_id TEXT NOT NULL, at TEXT NOT NULL, exchanges INTEGER NOT NULL, added INTEGER NOT NULL, superseded INTEGER NOT NULL);
            CREATE TRIGGER memory_entries_no_update BEFORE UPDATE ON memory_entries BEGIN SELECT RAISE(ABORT, 'memory entries are append-only; add a superseding entry instead'); END;
            CREATE TRIGGER memory_entries_no_delete BEFORE DELETE ON memory_entries BEGIN SELECT RAISE(ABORT, 'memory entries are append-only; add a superseding entry instead'); END;
            CREATE TRIGGER exchanges_no_delete BEFORE DELETE ON exchanges BEGIN SELECT RAISE(ABORT, 'exchanges are append-only'); END;
            CREATE TRIGGER exchanges_immutable BEFORE UPDATE OF persona_id, chat_id, turn_id, at, user_text, reply_text, vector, spoken ON exchanges BEGIN SELECT RAISE(ABORT, 'exchanges are append-only'); END;
            CREATE TRIGGER achievements_no_update BEFORE UPDATE ON achievements BEGIN SELECT RAISE(ABORT, 'achievements are append-only'); END;
            CREATE TRIGGER achievements_no_delete BEFORE DELETE ON achievements BEGIN SELECT RAISE(ABORT, 'achievements are append-only'); END;
            CREATE TRIGGER drive_log_no_update BEFORE UPDATE ON drive_log BEGIN SELECT RAISE(ABORT, 'the drive log is append-only'); END;
            CREATE TRIGGER drive_log_no_delete BEFORE DELETE ON drive_log BEGIN SELECT RAISE(ABORT, 'the drive log is append-only'); END;
            CREATE TABLE sessions(persona_id TEXT NOT NULL, chat_id TEXT NOT NULL, started_at TEXT NOT NULL, last_at TEXT NOT NULL, turns INTEGER NOT NULL, PRIMARY KEY(persona_id, chat_id));
            """);
        Exec(c, tx, AppearanceSchema);
        store.SetMeta(c, tx, "schema", SchemaVersion.ToString(CultureInfo.InvariantCulture));
        store.SetMeta(c, tx, "created_at", Iso(now));
        store.SetMeta(c, tx, "last_tick", Iso(now));
        store.SetMeta(c, tx, "last_interaction", Iso(now));
        store.SetMeta(c, tx, "turns_since_summary", "0");
        store.SetMeta(c, tx, "baseline_from", config.BaselineFrom);
        store.InsertAppearance(c, tx, config.Seed.Appearance, now, $"seed appearance card (baseline from {config.BaselineFrom})");
        foreach (DriveSpec d in config.Drives)
        {
            double v = d.Initial!.Value;
            Exec(c, tx, "INSERT INTO drives(persona_id,name,value,unmet_streak,updated_at) VALUES($p,$n,$v,0,$t)",
                ("$p", config.Id), ("$n", d.Name), ("$v", v), ("$t", Iso(now)));
            Exec(c, tx, "INSERT INTO drive_log(persona_id,at,drive,old,new,reason) VALUES($p,$t,$n,$v,$v,$r)",
                ("$p", config.Id), ("$t", Iso(now)), ("$n", d.Name), ("$v", v), ("$r", $"initial value (baseline from {config.BaselineFrom})"));
        }
        foreach ((string layer, List<string> items) in new[] { (MemoryLayers.Facts, config.Seed.Summary.Facts), (MemoryLayers.Feelings, config.Seed.Summary.Feelings), (MemoryLayers.Hooks, config.Seed.Summary.Hooks) })
        {
            foreach (string text in items.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                store.InsertMemory(c, tx, now, layer, text, MemoryAuthors.Seed, null, $"seed summary block (baseline from {config.BaselineFrom})");
            }
        }
        tx.Commit();
        return store;
    }

    /// <summary>Opens an existing store and proves it belongs to this persona. Never creates a store or any state.</summary>
    public static PersonaStore Open(string path, string personaId)
    {
        if (!File.Exists(path))
        {
            throw new DriveStateUnavailableException(personaId, $"the state store file is missing ({path}).");
        }
        var store = new PersonaStore(path, personaId, create: false);
        try
        {
            using SqliteConnection c = store.Connect();
            string check = Scalar<string>(c, null, "PRAGMA quick_check") ?? "";
            if (check != "ok")
            {
                throw new DriveStateUnavailableException(personaId, $"the state store is damaged ({check}).");
            }
            string? owner = Scalar<string>(c, null, "SELECT persona_id FROM meta WHERE key='schema' LIMIT 1");
            if (owner == null)
            {
                throw new DriveStateUnavailableException(personaId, "the state store has no owner record.");
            }
            if (owner != personaId)
            {
                throw new DriveStateUnavailableException(personaId, $"the state store at {path} belongs to '{owner}', not '{personaId}'.");
            }
            string? schema = store.GetMeta(c, "schema");
            if (schema != SchemaVersion.ToString(CultureInfo.InvariantCulture))
            {
                throw new DriveStateUnavailableException(personaId, $"the state store has schema {schema ?? "none"}, expected {SchemaVersion}.");
            }
            // Stores set up before appearance cards existed get the (empty) tables; her card is then set up
            // once from her persona file. Adding tables never touches existing data.
            Exec(c, null, AppearanceSchema);
            // Stores made before spoken exchanges were tracked: everything in them was typed.
            if (Scalar<long>(c, null, "SELECT COUNT(*) FROM pragma_table_info('exchanges') WHERE name='spoken'") == 0)
            {
                Exec(c, null, "ALTER TABLE exchanges ADD COLUMN spoken INTEGER NOT NULL DEFAULT 0; CREATE TRIGGER IF NOT EXISTS exchanges_spoken_immutable BEFORE UPDATE OF spoken ON exchanges BEGIN SELECT RAISE(ABORT, 'exchanges are append-only'); END;");
            }
        }
        catch (SqliteException ex)
        {
            throw new DriveStateUnavailableException(personaId, $"the state store could not be opened ({ex.Message}).", ex);
        }
        return store;
    }

    /// <summary>A connection that ignores Dispose until its <see cref="Hold"/> scope ends.</summary>
    private sealed class HeldConnection(string connectionString) : SqliteConnection(connectionString)
    {
        public bool Release { get; set; }

        protected override void Dispose(bool disposing)
        {
            if (Release)
            {
                base.Dispose(disposing);
            }
        }
    }

    private sealed class HoldScope(PersonaStore store, HeldConnection? connection) : IDisposable
    {
        public void Dispose()
        {
            if (connection != null)
            {
                store.held = null;
                connection.Release = true;
                connection.Dispose();
            }
        }
    }

    private HeldConnection? held;

    private int heldThread;

    /// <summary>
    /// Keeps one connection open for the calling thread until disposed, so a turn's many small reads don't each
    /// reopen the file (the slowest part of assembling her context on Windows). Other threads still get their own.
    /// Nested holds reuse the outer one.
    /// </summary>
    public IDisposable Hold()
    {
        if (held != null && heldThread == Environment.CurrentManagedThreadId)
        {
            return new HoldScope(this, null);
        }
        var c = new HeldConnection(connectionString);
        Configure(c);
        heldThread = Environment.CurrentManagedThreadId;
        held = c;
        return new HoldScope(this, c);
    }

    internal SqliteConnection Connect()
    {
        if (held is HeldConnection h && heldThread == Environment.CurrentManagedThreadId)
        {
            return h;
        }
        var c = new SqliteConnection(connectionString);
        Configure(c);
        return c;
    }

    private static void Configure(SqliteConnection c)
    {
        c.Open();
        using SqliteCommand pragma = c.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
    }

    // ---- drives -------------------------------------------------------------------------------------

    /// <summary>Reads every drive. Throws if any expected drive is missing or holds an impossible value.</summary>
    public Dictionary<string, DriveValue> ReadDrives(IEnumerable<string> expected)
    {
        var result = new Dictionary<string, DriveValue>();
        try
        {
            using SqliteConnection c = Connect();
            using SqliteCommand cmd = c.CreateCommand();
            cmd.CommandText = "SELECT name,value,unmet_streak,updated_at FROM drives WHERE persona_id=$p";
            cmd.Parameters.AddWithValue("$p", PersonaId);
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                double v = r.GetDouble(1);
                if (!double.IsFinite(v) || v < Drives.Min || v > Drives.Max)
                {
                    throw new DriveStateUnavailableException(PersonaId, $"the stored '{r.GetString(0)}' value ({v}) is impossible.");
                }
                result[r.GetString(0)] = new DriveValue(r.GetString(0), v, r.GetInt32(2), ParseIso(r.GetString(3)));
            }
        }
        catch (SqliteException ex)
        {
            throw new DriveStateUnavailableException(PersonaId, $"the drives table could not be read ({ex.Message}).", ex);
        }
        foreach (string name in expected)
        {
            if (!result.ContainsKey(name))
            {
                throw new DriveStateUnavailableException(PersonaId, $"the '{name}' drive is missing from the state store.");
            }
        }
        return result;
    }

    /// <summary>Writes drive changes and their log lines in one transaction. Unchanged values are not logged.</summary>
    public void Apply(IEnumerable<DriveChange> changes, DateTimeOffset at, IReadOnlyDictionary<string, int>? streaks = null, IEnumerable<Milestone>? achievements = null, IReadOnlyDictionary<string, string>? meta = null)
    {
        using SqliteConnection c = Connect();
        using SqliteTransaction tx = c.BeginTransaction();
        foreach (DriveChange ch in changes)
        {
            if (!double.IsFinite(ch.New) || ch.New < Drives.Min || ch.New > Drives.Max)
            {
                throw new InvalidOperationException($"Refusing to store impossible {ch.Drive} value {ch.New}.");
            }
            int n = Exec(c, tx, "UPDATE drives SET value=$v, updated_at=$t WHERE persona_id=$p AND name=$n",
                ("$v", ch.New), ("$t", Iso(at)), ("$p", PersonaId), ("$n", ch.Drive));
            if (n != 1)
            {
                throw new DriveStateUnavailableException(PersonaId, $"the '{ch.Drive}' drive row is missing.");
            }
            if (Math.Abs(ch.New - ch.Old) > 1e-9)
            {
                Exec(c, tx, "INSERT INTO drive_log(persona_id,at,drive,old,new,reason) VALUES($p,$t,$n,$o,$v,$r)",
                    ("$p", PersonaId), ("$t", Iso(at)), ("$n", ch.Drive), ("$o", ch.Old), ("$v", ch.New), ("$r", ch.Reason));
            }
        }
        if (streaks != null)
        {
            foreach ((string name, int streak) in streaks)
            {
                Exec(c, tx, "UPDATE drives SET unmet_streak=$s WHERE persona_id=$p AND name=$n", ("$s", streak), ("$p", PersonaId), ("$n", name));
            }
        }
        foreach (Milestone m in achievements ?? Array.Empty<Milestone>())
        {
            Exec(c, tx, "INSERT INTO achievements(persona_id,at,kind,note,reward) VALUES($p,$t,$k,$n,$r)",
                ("$p", PersonaId), ("$t", Iso(at)), ("$k", m.Kind), ("$n", m.Note), ("$r", m.Reward));
        }
        foreach ((string key, string value) in meta ?? new Dictionary<string, string>())
        {
            SetMeta(c, tx, key, value);
        }
        tx.Commit();
    }

    /// <summary>
    /// After her drives were edited (renamed, added, removed, re-ranged): keeps each value she had, under its new
    /// name, clamped to its new range; a new drive starts at its starting value; a removed one is dropped. Every
    /// change is written to the drive log, all in one transaction. Returns what changed, in plain words.
    /// </summary>
    /// <param name="renamedFrom">New drive name to the name it had before, for drives that were renamed.</param>
    public List<string> ReshapeDrives(PersonaConfig config, IReadOnlyDictionary<string, string> renamedFrom, DateTimeOffset now)
    {
        if (config.Id != PersonaId)
        {
            throw new InvalidOperationException($"Drives for '{config.Id}' can't be written to '{PersonaId}'s store.");
        }
        using SqliteConnection c = Connect();
        using SqliteTransaction tx = c.BeginTransaction();
        var old = new Dictionary<string, (double Value, int Streak)>();
        using (SqliteCommand cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT name,value,unmet_streak FROM drives WHERE persona_id=$p";
            cmd.Parameters.AddWithValue("$p", PersonaId);
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                old[r.GetString(0)] = (r.GetDouble(1), r.GetInt32(2));
            }
        }
        var done = new List<string>();
        var kept = new HashSet<string>();
        var rows = new List<(string Name, double Value, int Streak, string? From, string Reason)>();
        foreach (DriveSpec d in config.Drives)
        {
            string? from = renamedFrom.TryGetValue(d.Name, out string? f) ? f : d.Name;
            if (from != null && old.TryGetValue(from, out (double Value, int Streak) was) && kept.Add(from))
            {
                double v = DriveEngine.Clamp(d, was.Value);
                string reason = from != d.Name ? $"renamed from {from}" : "";
                if (Math.Abs(v - was.Value) > 1e-9)
                {
                    reason = (reason.Length > 0 ? reason + "; " : "") + $"kept inside its new range {d.Floor}-{d.Ceiling}";
                }
                rows.Add((d.Name, v, was.Streak, from, reason));
                if (from != d.Name)
                {
                    done.Add($"{from} is now {d.Name} (kept at {v:0.0})");
                }
                else if (Math.Abs(v - was.Value) > 1e-9)
                {
                    done.Add($"{d.Name} moved to {v:0.0} to fit its new range");
                }
            }
            else
            {
                double v = d.Initial ?? throw new PersonaConfigException($"The new drive '{d.Name}' needs a starting value.");
                rows.Add((d.Name, DriveEngine.Clamp(d, v), 0, null, "new drive, starting value"));
                done.Add($"{d.Name} added at {DriveEngine.Clamp(d, v):0.0}");
            }
        }
        foreach ((string name, (double value, _)) in old.Where(kv => !kept.Contains(kv.Key)))
        {
            Exec(c, tx, "INSERT INTO drive_log(persona_id,at,drive,old,new,reason) VALUES($p,$t,$n,$o,$o,$r)",
                ("$p", PersonaId), ("$t", Iso(now)), ("$n", name), ("$o", value), ("$r", "drive removed"));
            done.Add($"{name} removed");
        }
        Exec(c, tx, "DELETE FROM drives WHERE persona_id=$p", ("$p", PersonaId));
        foreach ((string name, double value, int streak, string? from, string reason) in rows)
        {
            Exec(c, tx, "INSERT INTO drives(persona_id,name,value,unmet_streak,updated_at) VALUES($p,$n,$v,$s,$t)",
                ("$p", PersonaId), ("$n", name), ("$v", value), ("$s", streak), ("$t", Iso(now)));
            if (reason.Length > 0)
            {
                double before = from != null && old.TryGetValue(from, out (double Value, int Streak) w) ? w.Value : value;
                Exec(c, tx, "INSERT INTO drive_log(persona_id,at,drive,old,new,reason) VALUES($p,$t,$n,$o,$v,$r)",
                    ("$p", PersonaId), ("$t", Iso(now)), ("$n", name), ("$o", before), ("$v", value), ("$r", "operator: " + reason));
            }
        }
        tx.Commit();
        return done;
    }

    public List<DriveLogEntry> DriveLog(int limit = 200, string? drive = null)
    {
        using SqliteConnection c = Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,at,drive,old,new,reason FROM drive_log WHERE persona_id=$p" + (drive != null ? " AND drive=$d" : "") + " ORDER BY id DESC LIMIT $l";
        cmd.Parameters.AddWithValue("$p", PersonaId);
        cmd.Parameters.AddWithValue("$l", limit);
        if (drive != null)
        {
            cmd.Parameters.AddWithValue("$d", drive);
        }
        var list = new List<DriveLogEntry>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new DriveLogEntry(r.GetInt64(0), ParseIso(r.GetString(1)), r.GetString(2), r.GetDouble(3), r.GetDouble(4), r.GetString(5)));
        }
        list.Reverse();
        return list;
    }

    // ---- meta ---------------------------------------------------------------------------------------

    public DateTimeOffset LastTick => ParseIso(RequireMeta("last_tick"));

    public DateTimeOffset LastInteraction => ParseIso(RequireMeta("last_interaction"));

    public int TurnsSinceSummary => int.Parse(RequireMeta("turns_since_summary"), CultureInfo.InvariantCulture);

    public void SetMeta(string key, string value)
    {
        using SqliteConnection c = Connect();
        SetMeta(c, null, key, value);
    }

    public string? GetMeta(string key)
    {
        using SqliteConnection c = Connect();
        return GetMeta(c, key);
    }

    private string RequireMeta(string key)
    {
        try
        {
            return GetMeta(key) ?? throw new DriveStateUnavailableException(PersonaId, $"the state store has no '{key}' record.");
        }
        catch (SqliteException ex)
        {
            throw new DriveStateUnavailableException(PersonaId, $"the state store could not be read ({ex.Message}).", ex);
        }
    }

    private void SetMeta(SqliteConnection c, SqliteTransaction? tx, string key, string value) =>
        Exec(c, tx, "INSERT INTO meta(persona_id,key,value) VALUES($p,$k,$v) ON CONFLICT(persona_id,key) DO UPDATE SET value=excluded.value",
            ("$p", PersonaId), ("$k", key), ("$v", value));

    private string? GetMeta(SqliteConnection c, string key, SqliteTransaction? tx = null) =>
        Scalar<string>(c, tx, "SELECT value FROM meta WHERE persona_id=$p AND key=$k", ("$p", PersonaId), ("$k", key));

    // ---- achievements -------------------------------------------------------------------------------

    public void AddAchievement(DateTimeOffset at, string kind, string note, double reward)
    {
        using SqliteConnection c = Connect();
        Exec(c, null, "INSERT INTO achievements(persona_id,at,kind,note,reward) VALUES($p,$t,$k,$n,$r)",
            ("$p", PersonaId), ("$t", Iso(at)), ("$k", kind), ("$n", note), ("$r", reward));
    }

    public List<Achievement> Achievements(int limit = 1000)
    {
        using SqliteConnection c = Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,at,kind,note,reward FROM achievements WHERE persona_id=$p ORDER BY id DESC LIMIT $l";
        cmd.Parameters.AddWithValue("$p", PersonaId);
        cmd.Parameters.AddWithValue("$l", limit);
        var list = new List<Achievement>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Achievement(r.GetInt64(0), ParseIso(r.GetString(1)), r.GetString(2), r.GetString(3), r.GetDouble(4)));
        }
        list.Reverse();
        return list;
    }

    public int AchievementCount()
    {
        using SqliteConnection c = Connect();
        return (int)Scalar<long>(c, null, "SELECT COUNT(*) FROM achievements WHERE persona_id=$p", ("$p", PersonaId));
    }

    // ---- exchanges and vectors ----------------------------------------------------------------------

    public long AddExchange(string chatId, string turnId, DateTimeOffset at, string user, string reply, float[]? vector, bool spoken = false)
    {
        using SqliteConnection c = Connect();
        using SqliteTransaction tx = c.BeginTransaction();
        int inserted = Exec(c, tx, """
            INSERT OR IGNORE INTO exchanges(persona_id,chat_id,turn_id,at,user_text,reply_text,vector,spoken) VALUES($p,$c,$tu,$t,$u,$r,$v,$s)
            """,
            ("$p", PersonaId), ("$c", chatId), ("$tu", turnId), ("$t", Iso(at)), ("$u", user), ("$r", reply), ("$v", vector == null ? DBNull.Value : ToBytes(vector)), ("$s", spoken ? 1 : 0));
        if (inserted == 0)
        {
            // Already remembered (exchanges are append-only), so nothing changes and nothing is counted twice.
            tx.Rollback();
            using SqliteConnection c2 = Connect();
            return Scalar<long>(c2, null, "SELECT id FROM exchanges WHERE persona_id=$p AND turn_id=$tu", ("$p", PersonaId), ("$tu", turnId));
        }
        long id = Scalar<long>(c, tx, "SELECT id FROM exchanges WHERE persona_id=$p AND turn_id=$tu", ("$p", PersonaId), ("$tu", turnId));
        Exec(c, tx, """
            INSERT INTO sessions(persona_id,chat_id,started_at,last_at,turns) VALUES($p,$c,$t,$t,1)
            ON CONFLICT(persona_id,chat_id) DO UPDATE SET last_at=excluded.last_at, turns=turns+1
            """, ("$p", PersonaId), ("$c", chatId), ("$t", Iso(at)));
        int since = int.Parse(GetMeta(c, "turns_since_summary", tx) ?? "0", CultureInfo.InvariantCulture);
        SetMeta(c, tx, "turns_since_summary", (since + 1).ToString(CultureInfo.InvariantCulture));
        tx.Commit();
        return id;
    }

    public List<(long Id, float[] Vector)> Vectors(long afterId = 0)
    {
        using SqliteConnection c = Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,vector FROM exchanges WHERE persona_id=$p AND id>$a AND vector IS NOT NULL ORDER BY id";
        cmd.Parameters.AddWithValue("$p", PersonaId);
        cmd.Parameters.AddWithValue("$a", afterId);
        var list = new List<(long, float[])>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add((r.GetInt64(0), FromBytes((byte[])r[1])));
        }
        return list;
    }

    public List<Exchange> ExchangesById(IEnumerable<long> ids)
    {
        long[] wanted = ids.Distinct().ToArray();
        if (wanted.Length == 0)
        {
            return new List<Exchange>();
        }
        using SqliteConnection c = Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT id,chat_id,turn_id,at,user_text,reply_text,folded,spoken FROM exchanges WHERE persona_id=$p AND id IN ({string.Join(",", wanted.Select((_, i) => "$i" + i))})";
        cmd.Parameters.AddWithValue("$p", PersonaId);
        for (int i = 0; i < wanted.Length; i++)
        {
            cmd.Parameters.AddWithValue("$i" + i, wanted[i]);
        }
        return ReadExchanges(cmd);
    }

    public List<Exchange> UnfoldedExchanges()
    {
        using SqliteConnection c = Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,chat_id,turn_id,at,user_text,reply_text,folded,spoken FROM exchanges WHERE persona_id=$p AND folded=0 ORDER BY id";
        cmd.Parameters.AddWithValue("$p", PersonaId);
        return ReadExchanges(cmd);
    }

    public List<Exchange> RecentExchanges(int count)
    {
        using SqliteConnection c = Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,chat_id,turn_id,at,user_text,reply_text,folded,spoken FROM exchanges WHERE persona_id=$p ORDER BY id DESC LIMIT $l";
        cmd.Parameters.AddWithValue("$p", PersonaId);
        cmd.Parameters.AddWithValue("$l", count);
        List<Exchange> list = ReadExchanges(cmd);
        list.Reverse();
        return list;
    }

    public HashSet<string> FoldedTurnIds(string chatId)
    {
        using SqliteConnection c = Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT turn_id FROM exchanges WHERE persona_id=$p AND chat_id=$c AND folded=1";
        cmd.Parameters.AddWithValue("$p", PersonaId);
        cmd.Parameters.AddWithValue("$c", chatId);
        var set = new HashSet<string>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            set.Add(r.GetString(0));
        }
        return set;
    }

    public int ExchangeCount()
    {
        using SqliteConnection c = Connect();
        return (int)Scalar<long>(c, null, "SELECT COUNT(*) FROM exchanges WHERE persona_id=$p", ("$p", PersonaId));
    }

    private static List<Exchange> ReadExchanges(SqliteCommand cmd)
    {
        var list = new List<Exchange>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Exchange(r.GetInt64(0), r.GetString(1), r.GetString(2), ParseIso(r.GetString(3)), r.GetString(4), r.GetString(5), r.GetInt32(6) != 0, r.GetInt32(7) != 0));
        }
        return list;
    }

    // ---- append-only memory ledger ------------------------------------------------------------------

    /// <summary>Every memory entry ever written, oldest first, including superseded ones.</summary>
    public List<MemoryEntry> MemoryHistory()
    {
        try
        {
            using SqliteConnection c = Connect();
            using SqliteCommand cmd = c.CreateCommand();
            cmd.CommandText = """
                SELECT e.id,e.at,e.layer,e.text,e.author,e.supersedes,e.reason,(SELECT s.id FROM memory_entries s WHERE s.supersedes=e.id AND s.persona_id=$p)
                FROM memory_entries e WHERE e.persona_id=$p ORDER BY e.id
                """;
            cmd.Parameters.AddWithValue("$p", PersonaId);
            var list = new List<MemoryEntry>();
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new MemoryEntry(r.GetInt64(0), ParseIso(r.GetString(1)), r.GetString(2), r.GetString(3), r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetInt64(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetInt64(7)));
            }
            return list;
        }
        catch (SqliteException ex)
        {
            throw new DriveStateUnavailableException(PersonaId, $"her memory could not be read ({ex.Message}).", ex);
        }
    }

    /// <summary>Entries in force: not superseded and not withdrawn.</summary>
    public List<MemoryEntry> ActiveMemory() => MemoryHistory().Where(e => e.Active).ToList();

    /// <summary>The pinned state block built from the entries in force (oldest first in each layer).</summary>
    public StateBlock ReadSummary()
    {
        List<MemoryEntry> active = ActiveMemory();
        return new StateBlock
        {
            Facts = active.Where(e => e.Layer == MemoryLayers.Facts).Select(e => e.Text).ToList(),
            Feelings = active.Where(e => e.Layer == MemoryLayers.Feelings).Select(e => e.Text).ToList(),
            Hooks = active.Where(e => e.Layer == MemoryLayers.Hooks).Select(e => e.Text).ToList()
        };
    }

    /// <summary>Appends a new memory entry. Author is "persona" (her own summary pass) or "operator".</summary>
    public long AddMemory(string layer, string text, string author, string reason, DateTimeOffset at)
    {
        using SqliteConnection c = Connect();
        using SqliteTransaction tx = c.BeginTransaction();
        long id = InsertMemory(c, tx, at, layer, text, author, null, reason);
        tx.Commit();
        return id;
    }

    /// <summary>
    /// Corrects an entry by appending a superseding one; the original is never changed. An empty
    /// <paramref name="newText"/> withdraws the entry. A reason is required.
    /// </summary>
    public long Supersede(long entryId, string newText, string author, string reason, DateTimeOffset at)
    {
        using SqliteConnection c = Connect();
        using SqliteTransaction tx = c.BeginTransaction();
        long id = SupersedeIn(c, tx, entryId, newText, author, reason, at);
        tx.Commit();
        return id;
    }

    /// <summary>One summary pass, atomically: new entries, superseding entries, folded exchanges, counter reset.</summary>
    /// <returns>Ids of the entries written (corrections first, then additions).</returns>
    public List<long> ApplySummaryPass(IEnumerable<(string Layer, string Text)> additions, IEnumerable<(long Id, string Text, string Reason)> corrections,
        IEnumerable<long> foldedExchangeIds, DateTimeOffset at)
    {
        var written = new List<long>();
        using SqliteConnection c = Connect();
        using SqliteTransaction tx = c.BeginTransaction();
        int added = 0, superseded = 0, folded = 0;
        foreach ((long id, string text, string reason) in corrections)
        {
            written.Add(SupersedeIn(c, tx, id, text, MemoryAuthors.Persona, reason, at));
            superseded++;
        }
        foreach ((string layer, string text) in additions)
        {
            written.Add(InsertMemory(c, tx, at, layer, text, MemoryAuthors.Persona, null, "summary pass"));
            added++;
        }
        foreach (long id in foldedExchangeIds)
        {
            folded += Exec(c, tx, "UPDATE exchanges SET folded=1 WHERE persona_id=$p AND id=$i", ("$p", PersonaId), ("$i", id));
        }
        Exec(c, tx, "INSERT INTO memory_folds(persona_id,at,exchanges,added,superseded) VALUES($p,$t,$f,$a,$s)",
            ("$p", PersonaId), ("$t", Iso(at)), ("$f", folded), ("$a", added), ("$s", superseded));
        SetMeta(c, tx, "turns_since_summary", "0");
        tx.Commit();
        return written;
    }

    private long SupersedeIn(SqliteConnection c, SqliteTransaction tx, long entryId, string newText, string author, string reason, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A correction needs a reason.");
        }
        string? layer = Scalar<string>(c, tx, "SELECT layer FROM memory_entries WHERE persona_id=$p AND id=$i", ("$p", PersonaId), ("$i", entryId));
        if (layer == null)
        {
            throw new ArgumentException($"Memory entry {entryId} doesn't exist.");
        }
        if (Scalar<long>(c, tx, "SELECT COUNT(*) FROM memory_entries WHERE persona_id=$p AND supersedes=$i", ("$p", PersonaId), ("$i", entryId)) > 0)
        {
            throw new ArgumentException($"Memory entry {entryId} was already superseded; correct the newer entry instead.");
        }
        return InsertMemory(c, tx, at, layer, newText.Trim(), author, entryId, reason.Trim());
    }

    private long InsertMemory(SqliteConnection c, SqliteTransaction? tx, DateTimeOffset at, string layer, string text, string author, long? supersedes, string reason)
    {
        if (!MemoryLayers.All.Contains(layer))
        {
            throw new ArgumentException($"Unknown memory layer '{layer}'.");
        }
        if (supersedes == null && string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("A memory entry needs text.");
        }
        Exec(c, tx, "INSERT INTO memory_entries(persona_id,at,layer,text,author,supersedes,reason) VALUES($p,$t,$l,$x,$a,$s,$r)",
            ("$p", PersonaId), ("$t", Iso(at)), ("$l", layer), ("$x", text.Trim()), ("$a", author), ("$s", supersedes.HasValue ? supersedes.Value : DBNull.Value), ("$r", reason));
        return Scalar<long>(c, tx, "SELECT last_insert_rowid()");
    }

    public List<SessionInfo> Sessions()
    {
        using SqliteConnection c = Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT chat_id,started_at,last_at,turns FROM sessions WHERE persona_id=$p ORDER BY last_at";
        cmd.Parameters.AddWithValue("$p", PersonaId);
        var list = new List<SessionInfo>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new SessionInfo(r.GetString(0), ParseIso(r.GetString(1)), ParseIso(r.GetString(2)), r.GetInt32(3)));
        }
        return list;
    }

    // ---- helpers ------------------------------------------------------------------------------------

    internal static string Iso(DateTimeOffset t) => t.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    internal static DateTimeOffset ParseIso(string s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static byte[] ToBytes(float[] v) => MemoryMarshal.AsBytes(v.AsSpan()).ToArray();

    private static float[] FromBytes(byte[] b) => MemoryMarshal.Cast<byte, float>(b).ToArray();

    private static int Exec(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object Value)[] args)
    {
        using SqliteCommand cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach ((string name, object value) in args)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        return cmd.ExecuteNonQuery();
    }

    private static T? Scalar<T>(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object Value)[] args)
    {
        using SqliteCommand cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach ((string name, object value) in args)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        object? o = cmd.ExecuteScalar();
        return o == null || o is DBNull ? default : (T)Convert.ChangeType(o, typeof(T), CultureInfo.InvariantCulture);
    }
}
