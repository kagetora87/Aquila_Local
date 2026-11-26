using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace AquilaLocal.Companion;

public sealed record DriveValue(string Name, double Value, int UnmetStreak, DateTimeOffset UpdatedAt);

public sealed record DriveChange(string Drive, double Old, double New, string Reason);

public sealed record DriveLogEntry(long Id, DateTimeOffset At, string Drive, double Old, double New, string Reason);

public sealed record Achievement(long Id, DateTimeOffset At, string Kind, string Note, double Reward);

public sealed record Exchange(long Id, string ChatId, string TurnId, DateTimeOffset At, string User, string Reply, bool Folded);

public sealed record SessionInfo(string ChatId, DateTimeOffset StartedAt, DateTimeOffset LastAt, int Turns);

/// <summary>
/// One persona's private state: drives, the drive change log, achievements, the pinned summary block,
/// sessions and every exchange with its embedding. Each persona has its own SQLite file, and every row
/// also carries the persona id and every query filters on it, so one persona's state can never be read
/// through another's store.
/// </summary>
public sealed class PersonaStore
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
            CREATE TABLE exchanges(id INTEGER PRIMARY KEY AUTOINCREMENT, persona_id TEXT NOT NULL, chat_id TEXT NOT NULL, turn_id TEXT NOT NULL, at TEXT NOT NULL, user_text TEXT NOT NULL, reply_text TEXT NOT NULL, vector BLOB, folded INTEGER NOT NULL DEFAULT 0, UNIQUE(persona_id, turn_id));
            CREATE INDEX exchanges_by_persona ON exchanges(persona_id, id);
            CREATE TABLE achievements(id INTEGER PRIMARY KEY AUTOINCREMENT, persona_id TEXT NOT NULL, at TEXT NOT NULL, kind TEXT NOT NULL, note TEXT NOT NULL, reward REAL NOT NULL);
            CREATE TABLE summary(persona_id TEXT PRIMARY KEY, block TEXT NOT NULL, updated_at TEXT NOT NULL, folded_count INTEGER NOT NULL);
            CREATE TABLE sessions(persona_id TEXT NOT NULL, chat_id TEXT NOT NULL, started_at TEXT NOT NULL, last_at TEXT NOT NULL, turns INTEGER NOT NULL, PRIMARY KEY(persona_id, chat_id));
            """);
        store.SetMeta(c, tx, "schema", SchemaVersion.ToString(CultureInfo.InvariantCulture));
        store.SetMeta(c, tx, "created_at", Iso(now));
        store.SetMeta(c, tx, "last_tick", Iso(now));
        store.SetMeta(c, tx, "last_interaction", Iso(now));
        store.SetMeta(c, tx, "turns_since_summary", "0");
        store.SetMeta(c, tx, "baseline_from", config.BaselineFrom);
        foreach (DriveSpec d in config.Drives)
        {
            double v = d.Initial!.Value;
            Exec(c, tx, "INSERT INTO drives(persona_id,name,value,unmet_streak,updated_at) VALUES($p,$n,$v,0,$t)",
                ("$p", config.Id), ("$n", d.Name), ("$v", v), ("$t", Iso(now)));
            Exec(c, tx, "INSERT INTO drive_log(persona_id,at,drive,old,new,reason) VALUES($p,$t,$n,$v,$v,$r)",
                ("$p", config.Id), ("$t", Iso(now)), ("$n", d.Name), ("$v", v), ("$r", $"initial value (baseline from {config.BaselineFrom})"));
        }
        StateBlock seed = config.Seed.Summary.CappedTo(config.Memory.SummaryTokenCap);
        Exec(c, tx, "INSERT INTO summary(persona_id,block,updated_at,folded_count) VALUES($p,$b,$t,0)",
            ("$p", config.Id), ("$b", System.Text.Json.JsonSerializer.Serialize(seed, PersonaConfig.Json)), ("$t", Iso(now)));
        tx.Commit();
        return store;
    }

    /// <summary>Opens an existing store and proves it belongs to this persona. Never creates anything.</summary>
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
        }
        catch (SqliteException ex)
        {
            throw new DriveStateUnavailableException(personaId, $"the state store could not be opened ({ex.Message}).", ex);
        }
        return store;
    }

    internal SqliteConnection Connect()
    {
        var c = new SqliteConnection(connectionString);
        c.Open();
        using SqliteCommand pragma = c.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return c;
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

    private string? GetMeta(SqliteConnection c, string key) =>
        Scalar<string>(c, null, "SELECT value FROM meta WHERE persona_id=$p AND key=$k", ("$p", PersonaId), ("$k", key));

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

    public long AddExchange(string chatId, string turnId, DateTimeOffset at, string user, string reply, float[]? vector)
    {
        using SqliteConnection c = Connect();
        using SqliteTransaction tx = c.BeginTransaction();
        Exec(c, tx, """
            INSERT INTO exchanges(persona_id,chat_id,turn_id,at,user_text,reply_text,vector) VALUES($p,$c,$tu,$t,$u,$r,$v)
            ON CONFLICT(persona_id,turn_id) DO UPDATE SET user_text=excluded.user_text, reply_text=excluded.reply_text, vector=excluded.vector, at=excluded.at
            """,
            ("$p", PersonaId), ("$c", chatId), ("$tu", turnId), ("$t", Iso(at)), ("$u", user), ("$r", reply), ("$v", vector == null ? DBNull.Value : ToBytes(vector)));
        long id = Scalar<long>(c, tx, "SELECT id FROM exchanges WHERE persona_id=$p AND turn_id=$tu", ("$p", PersonaId), ("$tu", turnId));
        Exec(c, tx, """
            INSERT INTO sessions(persona_id,chat_id,started_at,last_at,turns) VALUES($p,$c,$t,$t,1)
            ON CONFLICT(persona_id,chat_id) DO UPDATE SET last_at=excluded.last_at, turns=turns+1
            """, ("$p", PersonaId), ("$c", chatId), ("$t", Iso(at)));
        int since = int.Parse(GetMeta(c, "turns_since_summary") ?? "0", CultureInfo.InvariantCulture);
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
        cmd.CommandText = $"SELECT id,chat_id,turn_id,at,user_text,reply_text,folded FROM exchanges WHERE persona_id=$p AND id IN ({string.Join(",", wanted.Select((_, i) => "$i" + i))})";
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
        cmd.CommandText = "SELECT id,chat_id,turn_id,at,user_text,reply_text,folded FROM exchanges WHERE persona_id=$p AND folded=0 ORDER BY id";
        cmd.Parameters.AddWithValue("$p", PersonaId);
        return ReadExchanges(cmd);
    }

    public List<Exchange> RecentExchanges(int count)
    {
        using SqliteConnection c = Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,chat_id,turn_id,at,user_text,reply_text,folded FROM exchanges WHERE persona_id=$p ORDER BY id DESC LIMIT $l";
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
            list.Add(new Exchange(r.GetInt64(0), r.GetString(1), r.GetString(2), ParseIso(r.GetString(3)), r.GetString(4), r.GetString(5), r.GetInt32(6) != 0));
        }
        return list;
    }

    // ---- summary ------------------------------------------------------------------------------------

    public StateBlock ReadSummary()
    {
        try
        {
            using SqliteConnection c = Connect();
            string? json = Scalar<string>(c, null, "SELECT block FROM summary WHERE persona_id=$p", ("$p", PersonaId));
            if (json == null)
            {
                throw new DriveStateUnavailableException(PersonaId, "the summary block is missing from the state store.");
            }
            return System.Text.Json.JsonSerializer.Deserialize<StateBlock>(json, PersonaConfig.Json)
                ?? throw new DriveStateUnavailableException(PersonaId, "the summary block is empty.");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new DriveStateUnavailableException(PersonaId, "the summary block is unreadable.", ex);
        }
    }

    /// <summary>Replaces the summary block, marks the folded exchanges and resets the summary counter, atomically.</summary>
    public void WriteSummary(StateBlock block, IEnumerable<long> foldedExchangeIds, DateTimeOffset at)
    {
        using SqliteConnection c = Connect();
        using SqliteTransaction tx = c.BeginTransaction();
        int folded = 0;
        foreach (long id in foldedExchangeIds)
        {
            folded += Exec(c, tx, "UPDATE exchanges SET folded=1 WHERE persona_id=$p AND id=$i", ("$p", PersonaId), ("$i", id));
        }
        Exec(c, tx, "UPDATE summary SET block=$b, updated_at=$t, folded_count=folded_count+$f WHERE persona_id=$p",
            ("$b", System.Text.Json.JsonSerializer.Serialize(block, PersonaConfig.Json)), ("$t", Iso(at)), ("$f", folded), ("$p", PersonaId));
        SetMeta(c, tx, "turns_since_summary", "0");
        tx.Commit();
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
