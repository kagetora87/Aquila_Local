using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AquilaLocal.Companion;

public sealed partial class PersonaStore
{
    /// <summary>Chat ids of conversations remembered from before Aquila (primed from a saved chat) start with this.</summary>
    public const string ImportedChatPrefix = "imported:";

    public static bool IsImported(Exchange e) => e.ChatId.StartsWith(ImportedChatPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Remembers a conversation from before Aquila word for word, so she can recall its moments later. The exchanges
    /// are stored already folded (what mattered was approved into her long-term memory by hand), so the summary pass
    /// doesn't read them again, and they don't count as chat turns. Returns how many were new.
    /// </summary>
    /// <param name="source">A short name for where it came from (the file name), part of the chat id.</param>
    public int AddImportedExchanges(string source, IReadOnlyList<(string User, string Reply)> pairs, IReadOnlyList<float[]?> vectors, DateTimeOffset at)
    {
        if (pairs.Count != vectors.Count)
        {
            throw new ArgumentException("Each imported exchange needs its vector (or null).");
        }
        string chatId = ImportedChatPrefix + (string.IsNullOrWhiteSpace(source) ? "conversation" : source.Trim());
        string stamp = at.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        int added = 0;
        using SqliteConnection c = Connect();
        using SqliteTransaction tx = c.BeginTransaction();
        for (int i = 0; i < pairs.Count; i++)
        {
            (string user, string reply) = pairs[i];
            if (string.IsNullOrWhiteSpace(reply))
            {
                continue;
            }
            // A second of spacing keeps them in the conversation's order wherever they're sorted by time.
            DateTimeOffset when = at.AddSeconds(i - pairs.Count);
            added += Exec(c, tx, """
                INSERT OR IGNORE INTO exchanges(persona_id,chat_id,turn_id,at,user_text,reply_text,vector,folded,spoken) VALUES($p,$c,$tu,$t,$u,$r,$v,1,0)
                """,
                ("$p", PersonaId), ("$c", chatId), ("$tu", $"{chatId}#{stamp}#{i}"), ("$t", Iso(when)), ("$u", user.Trim()), ("$r", reply.Trim()),
                ("$v", vectors[i] is float[] v ? ToBytes(v) : DBNull.Value));
        }
        tx.Commit();
        return added;
    }
}
