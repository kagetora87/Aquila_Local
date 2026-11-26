using System.Diagnostics;
using AquilaLocal.Companion;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>
/// The canonical appearance card: permanent traits (locked), an identity anchor, and an append-only wardrobe
/// diary. Written before the implementation, like the satisfaction-loop tests.
/// </summary>
public sealed class AppearanceTests
{
    private static (CompanionRuntime Rt, PersonaStore Store) NewPersona(TempDir dir, PersonaConfig? config = null)
    {
        PersonaConfig c = config ?? TestPersona.Config();
        PersonaStore store = PersonaStore.Create(dir.File(c.Id + ".db"), c, TestPersona.T0);
        return (CompanionRuntime.ForTests(c, store, new FakeEmbedder()), store);
    }

    // ---- injector: three sources, in order, within their caps ---------------------------------------

    [Fact]
    public void Injector_assembles_summary_then_drives_then_appearance()
    {
        using var dir = new TempDir();
        (CompanionRuntime rt, _) = NewPersona(dir);

        PreparedTurn p = rt.Prepare("hello", new HashSet<string>(), false, TestPersona.T0);
        string full = p.Context.Full;

        int summary = full.IndexOf("Takeru lives in Japan.", StringComparison.Ordinal);
        int drives = full.IndexOf("[Inner state", StringComparison.Ordinal);
        int appearance = full.IndexOf("[Your appearance", StringComparison.Ordinal);
        Assert.True(summary >= 0 && drives > summary && appearance > drives, $"order was summary {summary}, drives {drives}, appearance {appearance}");
        Assert.Contains(TestPersona.Anchor, p.InnerState);
        Assert.Contains("black linen shirt dress", p.InnerState);
        Assert.Contains("eyes: grey-green", p.InnerState);
    }

    [Fact]
    public void Injector_respects_every_token_cap()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.Config();
        c.Seed.Summary.Facts.Clear();
        for (int i = 0; i < 400; i++)
        {
            c.Seed.Summary.Facts.Add($"Fact number {i}: a long sentence about their shared history that takes some room.");
        }
        (CompanionRuntime rt, _) = NewPersona(dir, c);

        PreparedTurn p = rt.Prepare("hello", new HashSet<string>(), false, TestPersona.T0);

        Assert.True(TokenEstimate.Of(p.Context.SummaryBlock) <= c.Memory.SummaryTokenCap);
        Assert.True(TokenEstimate.Of(p.Context.DriveBlock) <= ContextInjector.MaxDriveBlockTokens);
        Assert.True(TokenEstimate.Of(p.Context.AppearanceBlock) <= ContextInjector.MaxAppearanceTokens);
    }

    [Fact]
    public void Oversized_appearance_card_is_refused_loudly_not_truncated()
    {
        PersonaConfig c = TestPersona.Config();
        c.Seed.Appearance.Traits.Add(new AppearanceTrait { Name = "history", Text = new string('x', 380) });
        c.Seed.Appearance.Traits.Add(new AppearanceTrait { Name = "more", Text = new string('y', 380) });
        c.Seed.Appearance.Traits.Add(new AppearanceTrait { Name = "even more", Text = new string('z', 380) });
        c.Seed.Appearance.Traits.Add(new AppearanceTrait { Name = "and more", Text = new string('w', 380) });
        using var dir = new TempDir();
        (CompanionRuntime rt, _) = NewPersona(dir, c);

        var ex = Assert.Throws<AppearanceCardUnavailableException>(() => rt.Prepare("hello", new HashSet<string>(), false, TestPersona.T0));
        Assert.Contains("token", ex.Message);
    }

    [Fact]
    public void Two_personas_never_see_each_others_appearance()
    {
        using var dir = new TempDir();
        var reg = new PersonaRegistry(dir.Path);
        PersonaConfig sophiaConfig = TestPersona.Config("sophia", "Sophia");
        sophiaConfig.Seed.Appearance.IdentityAnchor = "A thin silver scar through her left eyebrow.";
        sophiaConfig.Seed.Appearance.Wardrobe = new Wardrobe { Outfit = "tailored lab coat", Color = "white", Fabric = "cotton twill", Condition = "pressed" };
        reg.Register(TestPersona.Config(), "vicki", makeActive: true);
        reg.Register(sophiaConfig, "sophia", makeActive: false);
        reg.Provision("vicki", TestPersona.T0);
        reg.Provision("sophia", TestPersona.T0);
        var emb = new FakeEmbedder();
        CompanionRuntime vicki = CompanionRuntime.Open(reg, "vicki", emb);
        CompanionRuntime sophia = CompanionRuntime.Open(reg, "sophia", emb);

        sophia.ChangeWardrobe(new WardrobeChange(Outfit: "ball gown"), "a gala tonight", MemoryAuthors.Persona, TestPersona.T0.AddMinutes(1));
        PreparedTurn vp = vicki.Prepare("what are you wearing?", new HashSet<string>(), false, TestPersona.T0.AddMinutes(2));
        PreparedTurn sp = sophia.Prepare("what are you wearing?", new HashSet<string>(), false, TestPersona.T0.AddMinutes(2));

        Assert.Contains(TestPersona.Anchor, vp.Context.Full);
        Assert.DoesNotContain("silver scar", vp.Context.Full);
        Assert.DoesNotContain("ball gown", vp.Context.Full);
        Assert.DoesNotContain("lab coat", vp.Context.Full);
        Assert.Contains("ball gown", sp.Context.Full);
        Assert.DoesNotContain(TestPersona.Anchor, sp.Context.Full);
        Assert.Single(vicki.Store.WardrobeHistory());
        Assert.Equal(2, sophia.Store.WardrobeHistory().Count);
        Assert.DoesNotContain("silver scar", AppearanceCaption.For("Vicki", vicki.Store.ReadAppearance()));
    }

    [Fact]
    public void Missing_card_stops_the_turn_and_names_the_file()
    {
        using var dir = new TempDir();
        (CompanionRuntime rt, PersonaStore store) = NewPersona(dir);
        using (SqliteConnection c = store.Connect())
        {
            using SqliteCommand cmd = c.CreateCommand();
            cmd.CommandText = "DROP TABLE appearance_traits";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var ex = Assert.Throws<AppearanceCardUnavailableException>(() => rt.Prepare("hello", new HashSet<string>(), false, TestPersona.T0));
        Assert.Contains(store.Path, ex.Message);
        Assert.Contains("nothing was sent", ex.Message);
    }

    [Fact]
    public void A_store_set_up_before_appearance_existed_stops_until_her_card_is_set_up()
    {
        using var dir = new TempDir();
        (CompanionRuntime rt, PersonaStore store) = NewPersona(dir);
        using (SqliteConnection c = store.Connect())
        {
            using SqliteCommand cmd = c.CreateCommand();
            cmd.CommandText = "DROP TABLE appearance_traits; DROP TABLE wardrobe_log;";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        PersonaStore reopened = PersonaStore.Open(store.Path, "vicki");
        Assert.False(reopened.HasAppearance);

        Assert.Throws<AppearanceCardUnavailableException>(() => reopened.ReadAppearance());
        reopened.SeedAppearance(TestPersona.Config().Seed.Appearance, TestPersona.T0.AddDays(1));
        Assert.True(reopened.HasAppearance);
        Assert.Contains(TestPersona.Anchor, AppearanceCaption.For("Vicki", reopened.ReadAppearance()));
        Assert.Throws<InvalidOperationException>(() => reopened.SeedAppearance(TestPersona.Config().Seed.Appearance, TestPersona.T0.AddDays(2)));
    }

    [Fact]
    public void Config_without_an_appearance_card_is_not_ready()
    {
        PersonaConfig c = TestPersona.Config();
        c.Seed.Appearance = new AppearanceSeed();

        Assert.Contains(c.MissingInputs(), m => m.Contains("appearance"));
        using var dir = new TempDir();
        Assert.Throws<PersonaConfigException>(() => PersonaStore.Create(dir.File("v.db"), c, TestPersona.T0));
    }

    // ---- caption export --------------------------------------------------------------------------------

    [Fact]
    public void Caption_comes_from_the_card_and_carries_the_identity_anchor()
    {
        using var dir = new TempDir();
        (CompanionRuntime rt, PersonaStore store) = NewPersona(dir);

        string caption = AppearanceCaption.For("Vicki", store.ReadAppearance());
        Assert.Contains(TestPersona.Anchor, caption);
        Assert.Contains("grey-green", caption);
        Assert.Contains("black linen shirt dress", caption);

        rt.ChangeWardrobe(new WardrobeChange(Outfit: "oversized hoodie", Color: "faded red", Condition: "rumpled"), "cold night, wanted comfort", MemoryAuthors.Persona, TestPersona.T0.AddHours(1));
        string after = AppearanceCaption.For("Vicki", store.ReadAppearance());
        Assert.Contains("oversized hoodie", after);
        Assert.Contains("faded red", after);
        Assert.Contains("linen", after);                 // fabric not mentioned in the change, so it carries over
        Assert.DoesNotContain("shirt dress", after);
        Assert.EndsWith(TestPersona.Anchor, after.TrimEnd());
    }

    // ---- read-write interface: wardrobe is an append-only diary, traits are locked ---------------------

    [Fact]
    public void Wardrobe_changes_are_appended_with_time_and_reason_never_overwritten()
    {
        using var dir = new TempDir();
        (CompanionRuntime rt, PersonaStore store) = NewPersona(dir);

        rt.ChangeWardrobe(new WardrobeChange(Outfit: "oversized hoodie"), "cold night", MemoryAuthors.Persona, TestPersona.T0.AddHours(1));
        rt.ChangeWardrobe(new WardrobeChange(Condition: "slept in"), "fell asleep reading", MemoryAuthors.Persona, TestPersona.T0.AddHours(9));

        List<WardrobeEntry> diary = store.WardrobeHistory();
        Assert.Equal(3, diary.Count);
        Assert.Equal("black linen shirt dress", diary[0].Wardrobe.Outfit);
        Assert.Equal("oversized hoodie", diary[2].Wardrobe.Outfit);
        Assert.Equal("slept in", diary[2].Wardrobe.Condition);
        Assert.Equal("fell asleep reading", diary[2].Reason);
        Assert.Equal(TestPersona.T0.AddHours(9), diary[2].At);
        Assert.Equal(MemoryAuthors.Persona, diary[2].Author);
        Assert.Throws<ArgumentException>(() => rt.ChangeWardrobe(new WardrobeChange(Outfit: "robe"), "  ", MemoryAuthors.Persona, TestPersona.T0));
        Assert.Throws<ArgumentException>(() => rt.ChangeWardrobe(new WardrobeChange(), "nothing changed", MemoryAuthors.Persona, TestPersona.T0));

        using SqliteConnection c = store.Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE wardrobe_log SET outfit='rewritten'";
        Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
        cmd.CommandText = "DELETE FROM wardrobe_log";
        Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
    }

    [Fact]
    public void Permanent_traits_are_locked_to_her_and_only_amended_deliberately_by_the_operator()
    {
        using var dir = new TempDir();
        (_, PersonaStore store) = NewPersona(dir);

        Assert.Throws<InvalidOperationException>(() => store.AmendTrait("eyes", "violet", MemoryAuthors.Persona, "I want violet eyes", TestPersona.T0));
        Assert.Throws<ArgumentException>(() => store.AmendTrait("eyes", "grey", MemoryAuthors.Operator, "", TestPersona.T0));

        store.AmendTrait("eyes", "grey-green with a gold ring", MemoryAuthors.Operator, "described wrong at seed", TestPersona.T0.AddHours(1));

        AppearanceCard card = store.ReadAppearance();
        Assert.Equal("grey-green with a gold ring", card.Traits.Single(t => t.Name == "eyes").Text);
        List<TraitEntry> history = store.TraitHistory();
        TraitEntry old = history.Single(t => t.Name == "eyes" && t.SupersededBy != null);
        Assert.Equal("grey-green", old.Text);
        Assert.Equal("described wrong at seed", history.Single(t => t.Supersedes == old.Id).Reason);
        Assert.Throws<InvalidOperationException>(() => store.AmendTrait(PersonaStore.AnchorTrait, "", MemoryAuthors.Operator, "remove it", TestPersona.T0));

        using SqliteConnection c = store.Connect();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE appearance_traits SET text='rewritten'";
        Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
    }

    [Fact]
    public void Her_own_wardrobe_changes_reach_the_satisfaction_loop_hook()
    {
        using var dir = new TempDir();
        (CompanionRuntime rt, _) = NewPersona(dir);
        PreparedTurn p = rt.Prepare("get comfortable", new HashSet<string>(), false, TestPersona.T0);
        rt.ChangeWardrobe(new WardrobeChange(Outfit: "pyjamas"), "getting comfortable", MemoryAuthors.Persona, TestPersona.T0.AddMinutes(1));

        CompletedTurn done = rt.Complete(p, "chat", "t1", "get comfortable", "There. Much better.", TestPersona.T0.AddMinutes(1));

        Assert.Single(done.SelfChanges);
        Assert.Equal("pyjamas", done.SelfChanges[0].Wardrobe.Outfit);
    }

    // ---- speed -------------------------------------------------------------------------------------------

    [Fact]
    public void Full_context_assembly_takes_under_100_ms()
    {
        using var dir = new TempDir();
        (CompanionRuntime rt, _) = NewPersona(dir);
        rt.Prepare("warm up", new HashSet<string>(), false, TestPersona.T0);

        long best = long.MaxValue;
        for (int i = 1; i <= 5; i++)
        {
            var watch = Stopwatch.StartNew();
            rt.Assemble();
            best = Math.Min(best, watch.ElapsedMilliseconds);
        }
        Assert.True(best < 100, $"assembly took {best} ms");
    }
}
