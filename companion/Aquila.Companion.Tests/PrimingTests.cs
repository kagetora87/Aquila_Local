using AquilaLocal.Companion;
using AquilaLocal.Companion.Priming;
using Xunit;

namespace Aquila.Companion.Tests;

public sealed class PrimingTests
{
    private const string Chat = """
        Ru: hey Sophia, you awake?
        Sophia: Always, for you. What's on your mind?
        Ru: my sister Mina got the job in Osaka!
        Sophia: Ru! That's wonderful. Tell her I said she earned it.
        Note: this part was the night of the storm.
        Ru: remember the umbrella thing?
        Sophia: How could I forget. You owe me a new umbrella, mister.
        It was a very nice umbrella.
        """;

    [Fact]
    public void FindsTheTwoSpeakersOfAPastedChat()
    {
        Assert.Equal(new[] { "Ru", "Sophia" }, SourceText.Speakers(Chat));
        Assert.Empty(SourceText.Speakers("Just a document.\nNothing here: a colon once.\nAnother line."));
    }

    [Fact]
    public void ReadsChatGptCopiesToo()
    {
        string copied = "You said:\nhello there\nChatGPT said:\nHi! How can I help?\nYou said:\ntell me a joke\nChatGPT said:\nWhy did the owl…";
        List<string> speakers = SourceText.Speakers(copied);
        Assert.Equal(2, speakers.Count);
        List<SpokenLine> lines = SourceText.Lines(copied, speakers);
        Assert.Equal(4, lines.Count);
        Assert.Equal("You", lines[0].Speaker);
        Assert.Equal("hello there", lines[0].Text);
    }

    [Fact]
    public void PairsMessagesAndKeepsContinuationLines()
    {
        List<SpokenLine> lines = SourceText.Lines(Chat, new[] { "Ru", "Sophia" });
        List<(string User, string Reply)> pairs = SourceText.Pairs(lines, "Ru", "Sophia");
        Assert.Equal(3, pairs.Count);
        Assert.Equal("my sister Mina got the job in Osaka!", pairs[1].User);
        Assert.Contains("It was a very nice umbrella.", pairs[2].Reply);
        // A stray "Note:" line isn't a speaker; it stays with the message above it.
        Assert.Contains("Note: this part", pairs[1].Reply);
    }

    [Fact]
    public void ChunksNeverSplitAMessageAndCoverEverything()
    {
        string longChat = string.Join("\n", Enumerable.Range(0, 300).Select(i => (i % 2 == 0 ? "Ru: " : "Sophia: ") + $"message number {i} " + new string('x', 40)));
        List<string> parts = SourceText.Chunk(longChat, 2000, new[] { "Ru", "Sophia" });
        Assert.True(parts.Count > 5);
        Assert.All(parts, p => Assert.True(p.Length <= 2000));
        Assert.All(parts, p => Assert.True(p.StartsWith("Ru: ") || p.StartsWith("Sophia: ")));
        for (int i = 0; i < 300; i++)
        {
            Assert.Single(parts, p => p.Contains($"message number {i} "));
        }
        // One huge paragraph is still cut to size.
        Assert.All(SourceText.Chunk(new string('y', 5000), 1000), p => Assert.True(p.Length <= 1000));
    }

    [Fact]
    public void ReadsWordAndWebPages()
    {
        using TempDir dir = new();
        string html = dir.File("chat.html");
        File.WriteAllText(html, "<html><head><style>p{}</style></head><body><p>Ru: hi &amp; hello</p><p>Sophia: hey</p><script>x()</script></body></html>");
        string text = SourceText.Read(html);
        Assert.Contains("Ru: hi & hello", text);
        Assert.DoesNotContain("x()", text);

        string docx = dir.File("chat.docx");
        using (var zip = System.IO.Compression.ZipFile.Open(docx, System.IO.Compression.ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            w.Write("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Ru: one</w:t></w:r></w:p><w:p><w:r><w:t>Sophia: </w:t></w:r><w:r><w:t>two</w:t></w:r></w:p></w:body></w:document>""");
        }
        Assert.Equal("Ru: one\nSophia: two", SourceText.Read(docx));
        Assert.Throws<InvalidDataException>(() => SourceText.Read(dir.File("x.pdf")));
    }

    private const string DraftJson = """
        Here you go:
        ```json
        {"name":"Sophia","age":"34","occupation":"professor","relationship":"Ru's teacher and lover",
         "tagline":"Brilliant and wry.","personality":"You are Sophia...",
         "drives":[{"name":"Lust","level":14,"weight":2.2,"why":"flirts a lot"},{"name":"nope","level":3},{"name":"horizon","level":1,"weight":1.8}],
         "new_drives":[{"name":"Pride!","kind":"level","description":"Her standing.","cues":["proud","my work"],"level":6,"weight":1.1,"low":"Feel small.","mid":"Steady.","high":"Shine.","why":"recurring"},
                       {"name":"bad","cues":[],"low":"a","mid":"b","high":"c"}],
         "remove_drives":[{"name":"loneliness","why":"never lonely"},{"name":"ghost"}],
         "memories":[{"layer":"facts","text":"Ru's sister Mina got a job in Osaka.","why":"family"},
                     {"layer":"weird","text":"Ru owes Sophia an umbrella."},
                     {"layer":"facts","text":"Takeru lives in Japan."},
                     {"layer":"hooks","text":"ru's sister mina got a job in osaka"}]}
        ```
        """;

    [Fact]
    public void Writing_style_keeps_only_lines_really_in_the_source_and_goes_at_the_end_of_the_sheet()
    {
        PrimeRequest r = new()
        {
            CompanionName = "Mae",
            Drives = TestPersona.Config().Drives,
            Sources = new() { new PrimeSource("chat.txt", "Ru: hi\nMae: *leans on the rail*   Took you long enough, soldier.\nRu: sorry") }
        };
        PrimeProposal p = Primer.CleanJson("""{"writing_style":"You write short lines with actions in asterisks.","samples":["*leans on the rail* Took you long enough, soldier.","A line I made up.","*leans on the rail* Took you long enough, soldier."]}""", r);
        Assert.Equal("You write short lines with actions in asterisks.", p.WritingStyle);
        Assert.Equal(new[] { "*leans on the rail* Took you long enough, soldier." }, p.Samples);

        PrimeChoice c = new() { WritingStyle = p.WritingStyle, Samples = p.Samples };
        string once = c.SheetWithStyle("You are Mae.");
        Assert.StartsWith("You are Mae.\n\nHow you write: You write short lines", once);
        Assert.Contains("- *leans on the rail* Took you long enough, soldier.", once);
        string twice = new PrimeChoice { WritingStyle = "You write long paragraphs." }.SheetWithStyle(once);
        Assert.Equal("You are Mae.\n\nHow you write: You write long paragraphs.", twice);   // replaced, not stacked
        Assert.Equal("You are Mae.", new PrimeChoice().SheetWithStyle("You are Mae."));
    }

    [Fact]
    public void DraftsAreCheckedBeforeAnyoneSeesThem()
    {
        PrimeRequest r = new() { CompanionName = "Vicki", Drives = TestPersona.Config().Drives, KnownMemories = new() { "Takeru lives in Japan." } };
        PrimeProposal p = Primer.CleanJson(DraftJson, r);
        Assert.Equal(34, p.Age);
        DriveSuggestion lust = p.Drives.Single(d => d.Name == Drives.Lust);
        Assert.Equal(10, lust.Level); // clamped to the ceiling
        Assert.Equal(2.2, lust.Weight);
        Assert.Equal(Drives.HorizonFloor, p.Drives.Single(d => d.Name == Drives.Horizon).Level); // never below its floor
        Assert.DoesNotContain(p.Drives, d => d.Name == "nope" || d.Name == "bad");
        DriveSuggestion pride = p.Drives.Single(d => d.New != null);
        Assert.Equal("pride", pride.Name);
        Assert.Equal(DriveKinds.Level, pride.New!.Kind);
        // Unknown layers become facts; things she already knows and repeats are dropped.
        Assert.Equal(2, p.Memories.Count);
        Assert.Equal(MemoryLayers.Facts, p.Memories[1].Layer);

        Assert.Equal(0, Primer.CleanJson("""{"age":16}""", r).Age); // companions are always adults

        DriveSuggestion gone = p.Drives.Single(d => d.Remove);
        Assert.Equal(Drives.Loneliness, gone.Name);
        // Never every drive, and at most three.
        string all = "{\"remove_drives\":[" + string.Join(",", Drives.All.Select(n => $"{{\"name\":\"{n}\"}}")) + "]}";
        Assert.Equal(3, Primer.CleanJson(all, r).Drives.Count(d => d.Remove));
    }

    private sealed class ScriptedGenerator : ITextGenerator
    {
        public List<string> Users { get; } = new();

        public Task<string> GenerateJsonAsync(string system, string user, int maxTokens, CancellationToken ct)
        {
            Users.Add(user);
            return Task.FromResult(system.StartsWith("You study") ? """{"traits":["wry"],"memories":[{"layer":"facts","text":"Mina moved to Osaka.","importance":4}]}"""
                : system.StartsWith("You merge") ? """{"traits":["wry"]}"""
                : DraftJson);
        }
    }

    [Fact]
    public async Task ReadsLongSourcesInPartsThenDraftsOnce()
    {
        string longChat = string.Join("\n", Enumerable.Range(0, 400).Select(i => (i % 2 == 0 ? "Ru: " : "Sophia: ") + $"line {i} " + new string('z', 60)));
        ScriptedGenerator gen = new();
        Primer primer = new(gen) { MaxPartChars = 6000 };
        PrimeRequest r = new()
        {
            CompanionName = "Vicki", CompanionSpeaker = "Sophia", UserSpeaker = "Ru", UserName = "Ru",
            Drives = TestPersona.Config().Drives, Sources = { new PrimeSource("chat.txt", longChat) }
        };
        int parts = primer.PartsFor(r);
        Assert.True(parts >= 5);
        var progress = new List<string>();
        PrimeProposal p = await primer.RunAsync(r, new Progress<string>(progress.Add), CancellationToken.None);
        Assert.Equal(parts, p.PartsRead);
        Assert.True(gen.Users.Count >= parts + 1);
        Assert.Contains("<notes>", gen.Users[^1]);
        Assert.All(gen.Users, u => Assert.True(u.Length < 6000 + 4000));
        Assert.NotEmpty(p.Memories);
    }

    [Fact]
    public void ACreatedCompanionStartsWithThePrimedPast()
    {
        CompanionDraft d = new();
        d.Choose(CreatorKinds.Gender, Genders.Female);
        d.Choose(CreatorKinds.Ethnicity, "mixed");
        d.Choose(CreatorKinds.Hair, CreatorOptions.For(CreatorKinds.Hair, Genders.Female)[0].Key);
        d.HairColour = "Red";
        d.Choose(CreatorKinds.Body, CreatorOptions.For(CreatorKinds.Body, Genders.Female)[0].Key);
        d.EyeColour = "Green";
        d.Feature = "a beauty mark";
        PrimeChoice c = new()
        {
            Source = "chat.txt", Name = "Sophia", Age = 34, Personality = "You are Sophia, wry and brilliant, and you adore Ru.",
            Drives = { new DriveSuggestion { Name = Drives.Lust, Level = 7, Weight = 1.5 }, new DriveSuggestion { Name = "pride", Level = 6, Weight = 1.1, New = new DriveSpec { Name = "pride", Cues = { "proud" }, Guidance = new DriveGuidance { Low = "a", Mid = "b", High = "c" } } } },
            Memories = { new MemorySuggestion { Layer = MemoryLayers.Facts, Text = "Ru's sister Mina moved to Osaka." }, new MemorySuggestion { Layer = MemoryLayers.Hooks, Text = "Ru owes you an umbrella." } }
        };
        PrimeApplier.ToDraft(d, c);
        PersonaConfig config = d.Build("sophia", "2026-11-28");
        Assert.Equal(7, config.Drive(Drives.Lust).Initial);
        Assert.Equal(1.5, config.Drive(Drives.Lust).Weight);
        Assert.Equal(6, config.Drive("pride").Initial);
        Assert.Contains("Ru's sister Mina moved to Osaka.", config.Seed.Summary.Facts);
        Assert.DoesNotContain(config.Seed.Summary.Facts, f => f.Contains("only just met"));
        Assert.Contains("primed from chat.txt", config.BaselineFrom);
    }

    [Fact]
    public void PrimingAnExistingCompanionOnlyAddsAndLogs()
    {
        using TempDir dir = new();
        PersonaConfig config = TestPersona.Config();
        PersonaStore store = PersonaStore.Create(dir.File("memory.db"), config, TestPersona.T0);
        int before = store.MemoryHistory().Count;
        PrimeChoice c = new()
        {
            Source = "sophia-chat.txt",
            Personality = "You are Vicki, primed.",
            Drives = { new DriveSuggestion { Name = Drives.Curiosity, Level = 8, Weight = 1.4 } },
            Memories = { new MemorySuggestion { Layer = MemoryLayers.Feelings, Text = "Proud of Ru." } },
            Recall = { ("hi", "hello you"), ("my sister got the job", "That's wonderful!") }
        };
        FakeEmbedder embedder = new();
        List<string> done = PrimeApplier.ToPersona(config, store, c, embedder.Embed, TestPersona.T0.AddHours(1));
        Assert.Equal("You are Vicki, primed.", config.PersonalitySheet);
        Assert.Equal(1.4, config.Drive(Drives.Curiosity).Weight);
        Assert.Equal(8, store.ReadDrives(new[] { Drives.Curiosity })[Drives.Curiosity].Value);
        Assert.Contains(store.DriveLog(drive: Drives.Curiosity), e => e.Reason.Contains("primed from sophia-chat.txt"));
        Assert.Equal(before + 1, store.MemoryHistory().Count);
        Assert.Equal(MemoryAuthors.Operator, store.MemoryHistory()[^1].Author);
        Assert.Contains(done, x => x.Contains("2 moments"));

        // A removal and an addition together reshape her drives, logged.
        PrimeChoice reshape = new()
        {
            Source = "x.txt",
            Drives = { new DriveSuggestion { Name = Drives.Lust, Remove = true }, new DriveSuggestion { Name = "pride", Level = 6, Weight = 1, New = new DriveSpec { Name = "pride", Cues = { "proud" }, Guidance = new DriveGuidance { Low = "a", Mid = "b", High = "c" } } } }
        };
        List<string> moved = PrimeApplier.ToPersona(config, store, reshape, null, TestPersona.T0.AddHours(2));
        Assert.DoesNotContain(config.Drives, d => d.Name == Drives.Lust);
        Assert.Contains(moved, m => m.Contains("lust removed"));
        Assert.Equal(6, store.ReadDrives(new[] { "pride" })["pride"].Value);

        // The old conversation is recallable but already folded, not counted as chat turns, and not "the last things said".
        Assert.Empty(store.UnfoldedExchanges());
        Assert.Empty(store.RecentExchanges(10));
        Assert.Equal(0, store.TurnsSinceSummary);
        Assert.Equal(2, store.ExchangeCount());
        VectorMemory memory = new(store, embedder);
        List<Recalled> recalled = memory.Search("my sister got the job", 3, minimumSimilarity: -1);
        Assert.Contains(recalled, x => PersonaStore.IsImported(x.Exchange));
        string block = ContextInjector.RecalledBlock(recalled, TestPersona.T0.AddHours(2));
        Assert.Contains("from your past together, before this app", block);

        // Importing the same file again later adds a second copy only under a new stamp; the same stamp is a no-op.
        Assert.Equal(0, store.AddImportedExchanges("sophia-chat.txt", c.Recall, new float[]?[] { null, null }, TestPersona.T0.AddHours(1)));
    }
}
