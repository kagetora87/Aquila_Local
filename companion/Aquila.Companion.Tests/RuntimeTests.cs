using AquilaLocal.Companion;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>Fail loud: if her state can't be read, nothing runs. A silent default is a stranger.</summary>
public sealed class FailLoudTests
{
    private static PersonaRegistry RegistryWith(TempDir dir, params PersonaConfig[] configs)
    {
        var reg = new PersonaRegistry(dir.Path);
        foreach (PersonaConfig c in configs)
        {
            reg.Register(c, c.Id, makeActive: false);
        }
        return reg;
    }

    [Fact]
    public void Missing_state_store_stops_with_a_message_instead_of_defaults()
    {
        using var dir = new TempDir();
        PersonaRegistry reg = RegistryWith(dir, TestPersona.Config());

        var ex = Assert.Throws<DriveStateUnavailableException>(() => CompanionRuntime.Open(reg, "vicki", new FakeEmbedder()));
        Assert.Contains("missing", ex.Message);
        Assert.Contains("nothing was sent", ex.Message);
        Assert.False(File.Exists(dir.File("vicki/memory.db")), "opening must never create a store with default drives");
    }

    [Fact]
    public void Corrupt_state_store_stops()
    {
        using var dir = new TempDir();
        PersonaRegistry reg = RegistryWith(dir, TestPersona.Config());
        File.WriteAllText(reg.Resolve("vicki/memory.db"), "this is not a database");

        Assert.Throws<DriveStateUnavailableException>(() => CompanionRuntime.Open(reg, "vicki", new FakeEmbedder()));
    }

    [Fact]
    public void Missing_drive_row_stops()
    {
        using var dir = new TempDir();
        PersonaRegistry reg = RegistryWith(dir, TestPersona.Config());
        PersonaStore store = reg.Provision("vicki", TestPersona.T0);
        using (var c = store.Connect())
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM drives WHERE name='loneliness'";
            cmd.ExecuteNonQuery();
        }

        var ex = Assert.Throws<DriveStateUnavailableException>(() => CompanionRuntime.Open(reg, "vicki", new FakeEmbedder()));
        Assert.Contains("loneliness", ex.Message);
    }

    [Fact]
    public void Impossible_drive_value_stops()
    {
        using var dir = new TempDir();
        PersonaRegistry reg = RegistryWith(dir, TestPersona.Config());
        PersonaStore store = reg.Provision("vicki", TestPersona.T0);
        using (var c = store.Connect())
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE drives SET value=42 WHERE name='lust'";
            cmd.ExecuteNonQuery();
        }

        Assert.Throws<DriveStateUnavailableException>(() => CompanionRuntime.Open(reg, "vicki", new FakeEmbedder()));
    }

    [Fact]
    public void Store_deleted_mid_session_stops_the_next_turn()
    {
        using var dir = new TempDir();
        PersonaRegistry reg = RegistryWith(dir, TestPersona.Config());
        reg.Provision("vicki", TestPersona.T0);
        CompanionRuntime rt = CompanionRuntime.Open(reg, "vicki", new FakeEmbedder());
        SqliteConnection.ClearAllPools();
        File.Delete(reg.Resolve("vicki/memory.db"));

        Assert.ThrowsAny<Exception>(() => rt.Prepare("hello", new HashSet<string>(), false, TestPersona.T0.AddHours(1)));
        Assert.False(File.Exists(reg.Resolve("vicki/memory.db")), "a deleted store must not be silently recreated");
    }

    [Fact]
    public void Another_personas_store_is_refused()
    {
        using var dir = new TempDir();
        PersonaRegistry reg = RegistryWith(dir, TestPersona.Config(), TestPersona.Config("sophia", "Sophia"));
        reg.Provision("sophia", TestPersona.T0);
        File.Copy(reg.Resolve("sophia/memory.db"), reg.Resolve("vicki/memory.db"));

        var ex = Assert.Throws<DriveStateUnavailableException>(() => CompanionRuntime.Open(reg, "vicki", new FakeEmbedder()));
        Assert.Contains("belongs to 'sophia'", ex.Message);
    }

    [Fact]
    public void Placeholder_persona_cannot_be_set_up_or_opened()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.Config();
        c.PersonalitySheet = PersonaConfig.Placeholder + ": paste Sophia's sheet]]";
        c.Drive(Drives.Lust).Weight = null;
        PersonaRegistry reg = RegistryWith(dir, c);

        var ex = Assert.Throws<PersonaConfigException>(() => reg.Provision("vicki", TestPersona.T0));
        Assert.Contains("personality sheet", ex.Message);
        Assert.Contains("weight for lust", ex.Message);
        Assert.Throws<PersonaConfigException>(() => CompanionRuntime.Open(reg, "vicki", new FakeEmbedder()));
        Assert.False(reg.Status("vicki").Ready);
    }

    [Fact]
    public void Setting_up_twice_never_overwrites_her_state()
    {
        using var dir = new TempDir();
        PersonaRegistry reg = RegistryWith(dir, TestPersona.Config());
        reg.Provision("vicki", TestPersona.T0);
        Assert.Throws<PersonaConfigException>(() => reg.Provision("vicki", TestPersona.T0));
    }
}

/// <summary>No persona's memory may leak into another's context.</summary>
public sealed class IsolationTests
{
    [Fact]
    public void Each_persona_only_recalls_and_changes_her_own_state()
    {
        using var dir = new TempDir();
        var reg = new PersonaRegistry(dir.Path);
        reg.Register(TestPersona.Config(), "vicki", makeActive: true);
        reg.Register(TestPersona.Config("sophia", "Sophia"), "sophia", makeActive: false);
        reg.Provision("vicki", TestPersona.T0);
        reg.Provision("sophia", TestPersona.T0);
        var emb = new FakeEmbedder();
        CompanionRuntime vicki = CompanionRuntime.Open(reg, "vicki", emb);
        CompanionRuntime sophia = CompanionRuntime.Open(reg, "sophia", emb);

        for (int i = 0; i < 6; i++)
        {
            PreparedTurn p = sophia.Prepare($"secret garden plan number {i}", new HashSet<string>(), i > 0, TestPersona.T0.AddMinutes(i));
            sophia.Complete(p, "chat-s", "s" + i, $"secret garden plan number {i}", "The secret garden plan is safe with me.", TestPersona.T0.AddMinutes(i));
        }
        double vickiLonelyBefore = vicki.Store.ReadDrives(Drives.All)[Drives.Loneliness].Value;

        PreparedTurn vp = vicki.Prepare("tell me about the secret garden plan", new HashSet<string>(), false, TestPersona.T0);

        Assert.Empty(vp.Recalled);
        Assert.DoesNotContain("garden", vp.InnerState + vp.RecalledText + vp.SystemPrompt);
        Assert.Equal(0, vicki.Store.ExchangeCount());
        Assert.Equal(6, sophia.Store.ExchangeCount());
        Assert.Empty(vicki.Store.Achievements());
        Assert.NotEqual(vicki.Store.Path, sophia.Store.Path);
        Assert.True(vicki.Store.ReadDrives(Drives.All)[Drives.Loneliness].Value <= vickiLonelyBefore);
    }

    [Fact]
    public void Registry_rejects_two_personas_sharing_a_memory_file()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, PersonaRegistry.ManifestName), """
            {"version":1,"active":"a","personas":[
              {"id":"a","name":"A","configPath":"a/persona.json","memoryPath":"shared.db"},
              {"id":"b","name":"B","configPath":"b/persona.json","memoryPath":"shared.db"}]}
            """);
        var ex = Assert.Throws<PersonaConfigException>(() => new PersonaRegistry(dir.Path).Load());
        Assert.Contains("leak", ex.Message);
    }

    [Fact]
    public void Switching_the_active_persona_is_persisted()
    {
        using var dir = new TempDir();
        var reg = new PersonaRegistry(dir.Path);
        reg.Register(TestPersona.Config(), "vicki", makeActive: true);
        reg.Register(TestPersona.Config("sophia", "Sophia"), "sophia", makeActive: false);
        Assert.Equal("vicki", reg.Load().Active);

        reg.SetActive("sophia");

        Assert.Equal("sophia", new PersonaRegistry(dir.Path).Load().Active);
    }
}

public sealed class InjectorAndMemoryTests
{
    [Fact]
    public void Drive_block_stays_under_300_tokens_even_with_the_longest_allowed_guidance()
    {
        PersonaConfig c = TestPersona.Config();
        foreach (DriveSpec d in c.Drives)
        {
            string max = new string('x', ContextInjector.MaxGuidanceChars);
            d.Guidance = new DriveGuidance { Low = max, Mid = max, High = max };
        }
        c.ValidateShape();
        var drives = Drives.All.ToDictionary(n => n, n => new DriveValue(n, 9.9, 0, TestPersona.T0));

        string block = ContextInjector.DriveBlock(c, drives, Drives.Lust);

        Assert.True(TokenEstimate.Of(block) <= ContextInjector.MaxDriveBlockTokens, $"{TokenEstimate.Of(block)} tokens");
        foreach (string n in Drives.All)
        {
            Assert.Contains("- " + n + " 9.9", block);
        }
    }

    [Fact]
    public void Drive_block_lists_the_top_drive_first_with_its_guidance()
    {
        PersonaConfig c = TestPersona.Config();
        var drives = Drives.All.ToDictionary(n => n, n => new DriveValue(n, 2, 0, TestPersona.T0));
        drives[Drives.Curiosity] = new DriveValue(Drives.Curiosity, 8.5, 0, TestPersona.T0);

        string block = ContextInjector.DriveBlock(c, drives, DriveEngine.TopDrive(c, drives));

        string[] lines = block.Split('\n');
        Assert.StartsWith("- curiosity 8.5 TOP: curiosity is pressing", lines[1]);
        Assert.Contains("- lust 2.0: lust is quiet.", block);
    }

    [Fact]
    public void System_prompt_carries_the_sheet_the_visible_drift_clause_and_the_pinned_summary()
    {
        PersonaConfig c = TestPersona.Config();
        string p = ContextInjector.SystemPrompt(c, c.Seed.Summary);
        Assert.Contains("You are Vicki.", p);
        Assert.Contains(c.PersonalitySheet, p);
        Assert.Contains("Divergence is success, not error.", p);
        Assert.Contains("Drift status: locked", p);
        Assert.Contains("Facts:\n- Takeru lives in Japan.", p);
        Assert.Contains("Hooks:\n- Ask about the cello.", p);
    }

    [Fact]
    public void Summary_block_is_hard_capped_at_2000_tokens()
    {
        var big = new StateBlock();
        for (int i = 0; i < 400; i++)
        {
            big.Facts.Add($"Fact number {i} about a long shared history with many details.");
            big.Feelings.Add($"Feeling number {i}, warm and complicated.");
            big.Hooks.Add($"Hook number {i}: come back to this.");
        }
        StateBlock capped = big.CappedTo(2000);
        Assert.True(TokenEstimate.Of(capped.Render()) <= 2000);
        Assert.NotEmpty(capped.Facts);
        Assert.NotEmpty(capped.Feelings);
        Assert.NotEmpty(capped.Hooks);
        Assert.Equal("Fact number 399 about a long shared history with many details.", capped.Facts.Last());
    }

    [Fact]
    public void Retrieval_returns_top_five_without_duplicates_or_turns_already_in_the_prompt()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.Config();
        PersonaStore store = PersonaStore.Create(dir.File("v.db"), c, TestPersona.T0);
        var mem = new VectorMemory(store, new FakeEmbedder());
        for (int i = 0; i < 4; i++)
        {
            mem.Add("chat", "dup" + i, TestPersona.T0, "we talked about the cello", "yes, the cello lessons");
        }
        for (int i = 0; i < 8; i++)
        {
            mem.Add("chat", "c" + i, TestPersona.T0, $"cello practice day {i}", $"you practiced cello scales {i}");
        }
        mem.Add("chat", "other", TestPersona.T0, "the weather is rainy", "rain again");

        List<Recalled> r = mem.Search("cello", 5, new HashSet<string> { "c7" });

        Assert.Equal(5, r.Count);
        Assert.Single(r, x => x.Exchange.TurnId.StartsWith("dup"));
        Assert.DoesNotContain(r, x => x.Exchange.TurnId == "c7");
        Assert.DoesNotContain(r, x => x.Exchange.TurnId == "other");
    }

    [Fact]
    public void Every_exchange_is_embedded_and_kept()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.Config();
        PersonaStore store = PersonaStore.Create(dir.File("v.db"), c, TestPersona.T0);
        var emb = new FakeEmbedder();
        CompanionRuntime rt = CompanionRuntime.ForTests(c, store, emb);
        for (int i = 0; i < 3; i++)
        {
            PreparedTurn p = rt.Prepare("hi " + i, new HashSet<string>(), i > 0, TestPersona.T0.AddMinutes(i));
            rt.Complete(p, "chat", "t" + i, "hi " + i, "hello " + i, TestPersona.T0.AddMinutes(i));
        }
        Assert.Equal(3, store.Vectors().Count);
        Assert.Equal(3, store.Sessions().Single().Turns);
    }
}

public sealed class RuntimeFlowTests
{
    [Fact]
    public void Prepare_applies_idle_time_and_message_events_and_logs_them()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.Config();
        PersonaStore store = PersonaStore.Create(dir.File("v.db"), c, TestPersona.T0);
        CompanionRuntime rt = CompanionRuntime.ForTests(c, store, new FakeEmbedder());

        PreparedTurn p = rt.Prepare("I missed you, darling", new HashSet<string>(), false, TestPersona.T0.AddHours(10));

        Dictionary<string, DriveValue> d = store.ReadDrives(Drives.All);
        Assert.Equal(5 * Math.Pow(0.98, 10) + 0.8, d[Drives.Lust].Value, 6);          // decayed 10 h, then affection
        Assert.Equal(5 + 0.35 * 10 - 0.8, d[Drives.Loneliness].Value, 6);              // idle rise, then interaction
        Assert.Contains(DriveEvents.Affection, p.Events);
        Assert.Contains(store.DriveLog(), e => e.Reason.StartsWith("time: 10 h"));
        Assert.Contains(store.DriveLog(), e => e.Reason.StartsWith("event:"));
        Assert.Equal(TestPersona.T0.AddHours(10), store.LastTick);
        Assert.Contains("TOP", p.InnerState);
    }

    [Fact]
    public void Full_turn_reaches_the_summarizer_after_twenty_turns_and_folds_all_but_the_recent_ones()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.Config();
        PersonaStore store = PersonaStore.Create(dir.File("v.db"), c, TestPersona.T0);
        CompanionRuntime rt = CompanionRuntime.ForTests(c, store, new FakeEmbedder());
        CompletedTurn? last = null;
        for (int i = 0; i < 20; i++)
        {
            PreparedTurn p = rt.Prepare("message " + i, new HashSet<string>(), i > 0, TestPersona.T0.AddMinutes(i));
            last = rt.Complete(p, "chat", "t" + i, "message " + i, "reply " + i, TestPersona.T0.AddMinutes(i));
            Assert.Equal(i == 19, last.SummaryDue);
        }

        var gen = new FakeGenerator("""{"add":[{"layer":"facts","text":"They talked twenty times."},{"layer":"hooks","text":"Ask about message 5."}],"supersede":[{"id":3,"text":"Ask about the cello recital date.","reason":"he booked the recital"}]}""");
        SummaryResult? r = rt.Summarizer(gen).RunAsync(TestPersona.T0.AddMinutes(30), CancellationToken.None).GetAwaiter().GetResult();

        Assert.NotNull(r);
        Assert.Equal(20 - c.Memory.KeepVerbatimTurns, r!.FoldedExchanges);
        Assert.Equal(0, store.TurnsSinceSummary);
        Assert.Equal(c.Memory.KeepVerbatimTurns, store.UnfoldedExchanges().Count);
        Assert.Equal(10, store.FoldedTurnIds("chat").Count);
        Assert.Contains("They talked twenty times.", ContextInjector.SystemPrompt(c, store.ReadSummary()));
        Assert.Contains("Takeru lives in Japan.", gen.LastUser);
        Assert.Equal(2, r.Added);
        Assert.Equal(1, r.Corrected);
        Assert.Equal(3, r.NewEntryIds.Count);
        List<MemoryEntry> history = store.MemoryHistory();
        MemoryEntry original = history.Single(e => e.Id == 3);
        Assert.Equal("Ask about the cello.", original.Text);             // the original is untouched...
        Assert.False(original.Active);                                    // ...but no longer in force
        MemoryEntry fix = history.Single(e => e.Supersedes == 3);
        Assert.Equal("he booked the recital", fix.Reason);
        Assert.Equal(TestPersona.T0.AddMinutes(30), fix.At);
        Assert.Equal(new[] { "Ask about the cello recital date.", "Ask about message 5." }, store.ReadSummary().Hooks);
    }

    [Fact]
    public void Every_fourth_message_she_takes_quick_notes_without_folding_anything()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.Config();
        PersonaStore store = PersonaStore.Create(dir.File("v.db"), c, TestPersona.T0);
        CompanionRuntime rt = CompanionRuntime.ForTests(c, store, new FakeEmbedder());
        var gen = new FakeGenerator("""{"add":[{"layer":"facts","text":"He folds sheets on Sundays."}],"supersede":[]}""");
        var due = new List<bool>();
        for (int i = 0; i < 8; i++)
        {
            PreparedTurn p = rt.Prepare("message " + i, new HashSet<string>(), i > 0, TestPersona.T0.AddMinutes(i));
            CompletedTurn done = rt.Complete(p, "chat", "t" + i, "message " + i, "reply " + i, TestPersona.T0.AddMinutes(i));
            due.Add(done.NotesDue);
            if (done.NotesDue)
            {
                SummaryResult? r = rt.Summarizer(gen).NoteAsync(TestPersona.T0.AddMinutes(i), CancellationToken.None).GetAwaiter().GetResult();
                Assert.NotNull(r);
                Assert.Equal(0, r!.FoldedExchanges);
                Assert.Contains("message " + i, gen.LastUser);
                Assert.DoesNotContain("message " + (i - 4), gen.LastUser);     // only what's new since the last notes
            }
        }
        Assert.Equal(new[] { false, false, false, true, false, false, false, true }, due);
        Assert.Contains("He folds sheets on Sundays.", store.ReadSummary().Facts);
        Assert.Single(store.ReadSummary().Facts, f => f == "He folds sheets on Sundays.");   // not added twice
        Assert.Equal(8, store.UnfoldedExchanges().Count);                                    // all still word for word
        Assert.Equal(8, store.TurnsSinceSummary);                                            // the summary pass still comes at 20
    }

    [Fact]
    public void Notes_can_be_switched_off()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.Config();
        c.Memory.NoteEveryTurns = 0;
        PersonaStore store = PersonaStore.Create(dir.File("v.db"), c, TestPersona.T0);
        CompanionRuntime rt = CompanionRuntime.ForTests(c, store, new FakeEmbedder());
        for (int i = 0; i < 6; i++)
        {
            PreparedTurn p = rt.Prepare("m" + i, new HashSet<string>(), i > 0, TestPersona.T0.AddMinutes(i));
            Assert.False(rt.Complete(p, "chat", "t" + i, "m" + i, "r" + i, TestPersona.T0.AddMinutes(i)).NotesDue);
        }
    }

    [Fact]
    public void A_reply_tried_again_is_set_aside_but_kept_with_its_reason()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.Config();
        PersonaStore store = PersonaStore.Create(dir.File("v.db"), c, TestPersona.T0);
        CompanionRuntime rt = CompanionRuntime.ForTests(c, store, new FakeEmbedder());
        for (int i = 0; i < 3; i++)
        {
            PreparedTurn p = rt.Prepare("m" + i, new HashSet<string>(), i > 0, TestPersona.T0.AddMinutes(i));
            rt.Complete(p, "chat", "t" + i, "m" + i, "r" + i, TestPersona.T0.AddMinutes(i));
        }

        Assert.True(store.WithdrawExchange("t2", "they asked her to try again", TestPersona.T0.AddMinutes(5)));
        Assert.False(store.WithdrawExchange("t2", "again", TestPersona.T0.AddMinutes(6)));   // already set aside
        Assert.False(store.WithdrawExchange("nope", "never stored", TestPersona.T0));
        Assert.Throws<ArgumentException>(() => store.WithdrawExchange("t1", " ", TestPersona.T0));

        Assert.Equal(new[] { "r0", "r1" }, store.RecentExchanges(10).Select(e => e.Reply));
        Assert.Equal(2, store.UnfoldedExchanges().Count);
        Assert.Equal(2, store.Vectors().Count);
        Assert.Equal(3, store.ExchangeCount());                                    // still in the file

        // The flag can't be cleared, and the log can't be changed.
        using var raw = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + dir.File("v.db"));
        raw.Open();
        using var undo = raw.CreateCommand();
        undo.CommandText = "UPDATE exchanges SET withdrawn=0 WHERE turn_id='t2'";
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => undo.ExecuteNonQuery());
        using var erase = raw.CreateCommand();
        erase.CommandText = "DELETE FROM exchange_withdrawals";
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => erase.ExecuteNonQuery());
    }

    [Fact]
    public void A_bad_summary_keeps_the_previous_block_and_folds_nothing()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.Config();
        PersonaStore store = PersonaStore.Create(dir.File("v.db"), c, TestPersona.T0);
        CompanionRuntime rt = CompanionRuntime.ForTests(c, store, new FakeEmbedder());
        for (int i = 0; i < 20; i++)
        {
            PreparedTurn p = rt.Prepare("m" + i, new HashSet<string>(), true, TestPersona.T0.AddMinutes(i));
            rt.Complete(p, "chat", "t" + i, "m" + i, "r" + i, TestPersona.T0.AddMinutes(i));
        }

        Assert.ThrowsAsync<InvalidDataException>(() => rt.Summarizer(new FakeGenerator("not json")).RunAsync(TestPersona.T0, CancellationToken.None)).GetAwaiter().GetResult();

        Assert.Equal("Takeru lives in Japan.", store.ReadSummary().Facts.Single());
        Assert.Empty(store.FoldedTurnIds("chat"));
        Assert.Equal(20, store.TurnsSinceSummary);
    }

    private sealed class FakeGenerator(string answer) : ITextGenerator
    {
        public string LastUser { get; private set; } = "";

        public Task<string> GenerateJsonAsync(string system, string user, int maxTokens, CancellationToken ct)
        {
            LastUser = user;
            return Task.FromResult(answer);
        }
    }
}
