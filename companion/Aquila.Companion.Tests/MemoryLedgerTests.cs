using AquilaLocal.Companion;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>Persona memory, including the seed block, is append-only. Corrections are new entries with a time and reason.</summary>
public sealed class MemoryLedgerTests
{
    private static PersonaStore Make(TempDir dir) => PersonaStore.Create(dir.File("v.db"), TestPersona.Config(), TestPersona.T0);

    [Fact]
    public void Seed_block_is_stored_as_entries_authored_by_the_seed()
    {
        using var dir = new TempDir();
        List<MemoryEntry> h = Make(dir).MemoryHistory();
        Assert.Equal(3, h.Count);
        Assert.All(h, e => Assert.Equal(MemoryAuthors.Seed, e.Author));
        Assert.All(h, e => Assert.Equal(TestPersona.T0, e.At));
    }

    [Theory]
    [InlineData("UPDATE memory_entries SET text='rewritten'")]
    [InlineData("DELETE FROM memory_entries")]
    [InlineData("UPDATE exchanges SET reply_text='rewritten'")]
    [InlineData("DELETE FROM exchanges")]
    [InlineData("DELETE FROM achievements")]
    [InlineData("UPDATE drive_log SET new=0")]
    [InlineData("DELETE FROM drive_log")]
    public void The_database_itself_refuses_to_rewrite_or_delete_history(string sql)
    {
        using var dir = new TempDir();
        PersonaStore store = Make(dir);
        new VectorMemory(store, new FakeEmbedder()).Add("chat", "t1", TestPersona.T0, "hi", "hello");
        store.AddAchievement(TestPersona.T0, MilestoneKinds.Choice, "chose", 0.5);
        using SqliteConnection c = store.Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = sql;
        var ex = Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
        Assert.Contains("append-only", ex.Message);
    }

    [Fact]
    public void Operator_can_add_and_correct_entries_and_the_chain_is_kept()
    {
        using var dir = new TempDir();
        PersonaStore store = Make(dir);
        long added = store.AddMemory(MemoryLayers.Facts, "Her first words were 'Is the door still broken?'", MemoryAuthors.Operator, "first boot", TestPersona.T0.AddMinutes(1));
        long fix = store.Supersede(added, "Her first words were 'Is the door fixed yet?'", MemoryAuthors.Operator, "misquoted", TestPersona.T0.AddMinutes(2));

        List<MemoryEntry> h = store.MemoryHistory();
        Assert.Equal("Her first words were 'Is the door still broken?'", h.Single(e => e.Id == added).Text);
        Assert.Equal(fix, h.Single(e => e.Id == added).SupersededBy);
        Assert.Equal("misquoted", h.Single(e => e.Id == fix).Reason);
        Assert.Contains("Her first words were 'Is the door fixed yet?'", store.ReadSummary().Facts);
        Assert.DoesNotContain("Her first words were 'Is the door still broken?'", store.ReadSummary().Facts);
    }

    [Fact]
    public void Withdrawing_an_entry_keeps_it_in_history()
    {
        using var dir = new TempDir();
        PersonaStore store = Make(dir);
        long hook = store.ActiveMemory().Single(e => e.Layer == MemoryLayers.Hooks).Id;

        store.Supersede(hook, "", MemoryAuthors.Operator, "resolved: he started cello lessons", TestPersona.T0.AddDays(1));

        Assert.Empty(store.ReadSummary().Hooks);
        Assert.Equal(4, store.MemoryHistory().Count);
    }

    [Fact]
    public void A_correction_needs_a_reason_and_can_only_supersede_once()
    {
        using var dir = new TempDir();
        PersonaStore store = Make(dir);
        long id = store.ActiveMemory()[0].Id;
        Assert.Throws<ArgumentException>(() => store.Supersede(id, "x", MemoryAuthors.Operator, " ", TestPersona.T0));
        store.Supersede(id, "x", MemoryAuthors.Operator, "fix", TestPersona.T0);
        Assert.Throws<ArgumentException>(() => store.Supersede(id, "y", MemoryAuthors.Operator, "fix again", TestPersona.T0));
    }

    [Fact]
    public void Remembering_the_same_turn_twice_changes_nothing()
    {
        using var dir = new TempDir();
        PersonaStore store = Make(dir);
        var mem = new VectorMemory(store, new FakeEmbedder());
        long a = mem.Add("chat", "t1", TestPersona.T0, "hi", "hello");
        long b = mem.Add("chat", "t1", TestPersona.T0.AddMinutes(5), "hi (edited)", "different");
        Assert.Equal(a, b);
        Assert.Equal("hello", store.RecentExchanges(5).Single().Reply);
        Assert.Equal(1, store.TurnsSinceSummary);
    }
}

public sealed class TuningTests
{
    private static (string ConfigPath, PersonaStore Store) Make(TempDir dir)
    {
        var reg = new PersonaRegistry(dir.Path);
        reg.Register(TestPersona.Config(), "vicki", makeActive: true);
        return (reg.Resolve("vicki/persona.json"), reg.Provision("vicki", TestPersona.T0));
    }

    [Fact]
    public void Setting_changes_are_written_to_the_human_editable_file()
    {
        using var dir = new TempDir();
        var (path, store) = Make(dir);
        TuningResult r = PersonaTuning.Run("/tune horizon weight 2.0", path, store, TestPersona.T0);
        Assert.True(r.ConfigChanged);
        Assert.Equal(2.0, PersonaConfig.Load(path).Drive(Drives.Horizon).Weight);
        Assert.Contains("\"weight\": 2", File.ReadAllText(path));
    }

    [Fact]
    public void Value_changes_are_applied_and_logged_as_operator_changes()
    {
        using var dir = new TempDir();
        var (path, store) = Make(dir);
        PersonaTuning.Run("/tune lust 6.5", path, store, TestPersona.T0.AddMinutes(3));
        Assert.Equal(6.5, store.ReadDrives(Drives.All)[Drives.Lust].Value, 6);
        DriveLogEntry e = store.DriveLog(drive: Drives.Lust).Last();
        Assert.Equal("operator: /tune", e.Reason);
        Assert.Equal(TestPersona.T0.AddMinutes(3), e.At);
    }

    [Theory]
    [InlineData("/tune horizon floor 1")]
    [InlineData("/tune lust 11")]
    [InlineData("/tune banana 3")]
    [InlineData("/tune lust weight lots")]
    public void Invalid_tuning_is_refused_and_the_file_is_unchanged(string command)
    {
        using var dir = new TempDir();
        var (path, store) = Make(dir);
        string before = File.ReadAllText(path);
        Assert.ThrowsAny<Exception>(() => PersonaTuning.Run(command, path, store, TestPersona.T0));
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void Help_and_show_are_available()
    {
        using var dir = new TempDir();
        var (path, store) = Make(dir);
        Assert.Contains("/tune <drive> <setting> <number>", PersonaTuning.Run("/tune", path, store, TestPersona.T0).Message);
        Assert.Contains("horizon: 6.0", PersonaTuning.Run("/tune show", path, store, TestPersona.T0).Message);
        Assert.True(PersonaTuning.IsCommand("/tune show"));
        Assert.False(PersonaTuning.IsCommand("/tuned in"));
    }

    [Fact]
    public void Edited_file_is_picked_up_when_the_persona_is_reopened()
    {
        using var dir = new TempDir();
        var reg = new PersonaRegistry(dir.Path);
        reg.Register(TestPersona.Config(), "vicki", makeActive: true);
        reg.Provision("vicki", TestPersona.T0);
        PersonaConfig c = PersonaConfig.Load(reg.Resolve("vicki/persona.json"));
        c.PersonalitySheet = "Edited by hand.";
        c.Save(reg.Resolve("vicki/persona.json"));

        CompanionRuntime rt = CompanionRuntime.Open(reg, "vicki", new FakeEmbedder());

        Assert.Contains("Edited by hand.", rt.Prepare("hi", new HashSet<string>(), false, TestPersona.T0).SystemPrompt);
    }
}
