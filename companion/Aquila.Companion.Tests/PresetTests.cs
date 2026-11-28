using AquilaLocal.Companion;
using AquilaLocal.Companion.Pictures;
using Xunit;

namespace Aquila.Companion.Tests;

public sealed class PresetTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "aquila-presets-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuiltInPresetsHaveUniqueIdsAndKnownKinds()
    {
        Assert.Equal(PresetLibrary.BuiltIn.Count, PresetLibrary.BuiltIn.Select(p => p.Id).Distinct().Count());
        Assert.All(PresetLibrary.BuiltIn, p => Assert.True(PresetKinds.IsKnown(p.Kind)));
        Assert.All(PresetLibrary.BuiltIn.Where(p => p.Kind == PresetKinds.Action), p =>
        {
            Assert.Contains(p.Framing, PictureRequest.Framings);
            Assert.NotEmpty(p.Motion);
        });
        foreach (string kind in PresetKinds.All)
        {
            Assert.True(PresetLibrary.BuiltIn.Count(p => p.Kind == kind) >= 12);
        }
    }

    [Fact]
    public void CustomPresetsAreAddedSavedAndRemoved()
    {
        PresetLibrary lib = new(root);
        GenPreset added = lib.AddCustom(PresetKinds.Scene, "  Grandma's   porch ", "a wooden porch with a swing at dusk");
        Assert.Equal("Grandma's porch", added.Name);
        Assert.StartsWith("custom-scene-", added.Id);
        Assert.True(added.Custom);
        PresetLibrary again = new(root);
        Assert.Contains(again.Load(PresetKinds.Scene), p => p.Id == added.Id && p.Custom);
        Assert.Contains(again.MissingTiles(), p => p.Id == added.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(again.StyledTilePath(added.Id))!);
        File.WriteAllText(again.StyledTilePath(added.Id), "x");
        Assert.DoesNotContain(again.MissingTiles(), p => p.Id == added.Id);
        again.Remove(added.Id);
        Assert.DoesNotContain(again.Load(), p => p.Id == added.Id);
        Assert.False(File.Exists(again.StyledTilePath(added.Id)));
    }

    [Fact]
    public void CustomPresetsFailLoud()
    {
        PresetLibrary lib = new(root);
        Assert.Throws<PersonaConfigException>(() => lib.AddCustom(PresetKinds.Action, "", "x"));
        Assert.Throws<PersonaConfigException>(() => lib.AddCustom(PresetKinds.Action, "Jumping", ""));
        Assert.Throws<PersonaConfigException>(() => lib.AddCustom("pose", "Jumping", "jumping"));
        Assert.Throws<PersonaConfigException>(() => lib.AddCustom(PresetKinds.Action, "smiling", "again"));
        Assert.Throws<PersonaConfigException>(() => lib.Remove("action-smile"));
        GenPreset a = lib.AddCustom(PresetKinds.Action, "Jumping", "jumping in the air", "full body", "jumps up and lands");
        Assert.Equal("full body", a.Framing);
        Assert.Equal("jumps up and lands", a.Motion);
        GenPreset o = lib.AddCustom(PresetKinds.Outfit, "Cape", "a red cape", "full body", "flutters");
        Assert.Equal("", o.Framing);
        Assert.Equal("", o.Motion);
    }

    [Fact]
    public void BrokenPresetsFileFailsLoud()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "presets.json"), "{ not json");
        Assert.Throws<PersonaConfigException>(() => new PresetLibrary(root).Load());
    }

    [Fact]
    public void RecipeDescribesWhoWhatWhereWithTheRightWords()
    {
        GenPreset act = PresetLibrary.BuiltIn.First(p => p.Id == "action-read");
        GenPreset outfit = PresetLibrary.BuiltIn.First(p => p.Id == "outfit-sweater");
        GenPreset scene = PresetLibrary.BuiltIn.First(p => p.Id == "scene-library");
        GenerateRecipe r = new(act, outfit, scene, "rain on the window");
        string her = r.Describe("Vicki", Genders.Female);
        Assert.Contains("Vicki, an adult, reading a book", her);
        Assert.Contains("She is wearing an oversized cream knit sweater (instead of her usual outfit)", her);
        Assert.Contains("Setting: a quiet old library", her);
        Assert.EndsWith("clothing included): rain on the window.", her);
        string him = r.Describe("Marcus", Genders.Male);
        Assert.Contains("He is wearing", him);
        Assert.Contains("instead of his usual outfit", him);
        Assert.Equal("half body", r.Shot);
        Assert.Equal("portrait", (r with { Framing = "portrait" }).Shot);
        Assert.Equal("half body", new GenerateRecipe(null, null, null, "").Shot);
        Assert.True(new GenerateRecipe(null, null, null, " ").IsEmpty);
        PictureRequest req = r.Request("Vicki", Genders.Female);
        Assert.True(req.IncludesPersona);
        Assert.Equal("half body", req.Framing);
    }

    [Fact]
    public void MotionComesFromWhatYouTypedThenTheAction()
    {
        GenPreset wave = PresetLibrary.BuiltIn.First(p => p.Id == "action-wave");
        Assert.Equal("spins around", new GenerateRecipe(wave, null, null, "").Motion(" spins around "));
        Assert.Equal(wave.Motion, new GenerateRecipe(wave, null, null, "").Motion(""));
        Assert.NotEmpty(new GenerateRecipe(null, null, null, "").Motion(""));
    }

    [Fact]
    public void OutfitsCanBeForOneGender()
    {
        Assert.Contains(PresetLibrary.BuiltIn, p => p.For == Genders.Female);
        Assert.Contains(PresetLibrary.BuiltIn, p => p.For == Genders.Male);
        GenPreset gown = PresetLibrary.BuiltIn.First(p => p.Id == "outfit-gown");
        Assert.True(gown.Fits(Genders.Female));
        Assert.False(gown.Fits(Genders.Male));
        Assert.Contains("young adult woman", PresetLibrary.TilePrompt(gown));
        Assert.Contains("No people", PresetLibrary.TilePrompt(PresetLibrary.BuiltIn.First(p => p.Kind == PresetKinds.Scene)));
        GenPreset smile = PresetLibrary.BuiltIn.First(p => p.Id == "action-smile");
        Assert.Contains("young adult woman", PresetLibrary.TilePrompt(smile));
        Assert.Contains("Anime", PresetLibrary.TilePrompt(smile));
        Assert.DoesNotContain("realistic", PresetLibrary.TilePrompt(smile));
    }

    [Fact]
    public void DirectPromptIncludesAppearanceAndStaysShort()
    {
        GenerateRecipe r = new(null, null, null, new string('x', 3000));
        string prompt = r.DirectPrompt("Vicki", Genders.Female, "grey-green eyes, black hair");
        Assert.StartsWith("Picture of Vicki", prompt);
        Assert.True(prompt.Length <= PicturePrompt.MaxPromptChars);
        Assert.Contains("grey-green eyes", new GenerateRecipe(null, null, null, "").DirectPrompt("Vicki", Genders.Female, "grey-green eyes"));
    }

    [Fact]
    public void WriterUsesHisForMaleCompanions()
    {
        PictureRequest req = new("standing", true, "portrait");
        string male = PicturePrompt.WriterUser(req, "", "Marcus", "tall", true, "", Genders.Male);
        Assert.Contains("His appearance", male);
        Assert.Contains("Reference pictures of him", male);
        Assert.Contains("Her appearance", PicturePrompt.WriterUser(req, "", "Vicki", "short", false));
    }

    [Fact]
    public void SeedCaptionCanLeaveOutTheOutfit()
    {
        AppearanceSeed seed = new()
        {
            Traits = new List<AppearanceTrait> { new() { Name = "eyes", Text = "grey-green." } },
            IdentityAnchor = "a small star tattoo",
            Wardrobe = new Wardrobe { Outfit = "red hoodie" }
        };
        Assert.Contains("red hoodie", SeedCaption.For("Vicki", seed, withOutfit: true));
        string bare = SeedCaption.For("Vicki", seed, withOutfit: false);
        Assert.DoesNotContain("red hoodie", bare);
        Assert.Contains("eyes: grey-green", bare);
        Assert.Contains("star tattoo", bare);
    }

    [Fact]
    public void TileStyleSetsWhoAndHowAndMovesTheTiles()
    {
        PresetLibrary lib = new(root);
        Assert.True(lib.Style.IsDefault);
        Assert.EndsWith(Path.Combine("tiles", "anime", "action-smile.png"), lib.StyledTilePath("action-smile"));
        GenPreset smile = PresetLibrary.BuiltIn.First(p => p.Id == "action-smile");
        GenPreset tux = PresetLibrary.BuiltIn.First(p => p.Id == "outfit-tux");
        TileStyle mine = new() { Woman = "a petite woman in her twenties with freckles.", Man = "a tall bearded man", Art = "watercolour painting" };
        lib.SaveStyle(mine);
        Assert.Equal("a petite woman in her twenties with freckles", mine.Woman);
        string prompt = PresetLibrary.TilePrompt(smile, lib.Style);
        Assert.StartsWith("Watercolour painting. A petite woman in her twenties with freckles", prompt);
        Assert.Contains("a tall bearded man".Substring(1), PresetLibrary.TilePrompt(tux, lib.Style));
        Assert.DoesNotContain("anime", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.Combine("tiles", "anime"), lib.StyledTilePath("action-smile"));
        Assert.Contains(lib.Load(), p => p.Id == "action-smile");
        Assert.Contains(lib.MissingTiles(), p => p.Id == "action-smile");

        PresetLibrary again = new(root);
        Assert.Equal("watercolour painting", again.Style.Art);
        Assert.Equal(lib.StyledTilePath("x"), again.StyledTilePath("x"));
        string creator = CreatorOptions.TilePrompt(CreatorOptions.Find(CreatorKinds.Gender, Genders.Male)!, Genders.Female, again.Style);
        Assert.Contains("tall bearded man", creator);
        Assert.StartsWith("Watercolour painting.", creator);

        Assert.Throws<PersonaConfigException>(() => lib.SaveStyle(new TileStyle { Woman = "  " }));
        Assert.Throws<PersonaConfigException>(() => lib.SaveStyle(new TileStyle { Art = new string('a', TileStyle.MaxChars + 1) }));
        File.WriteAllText(lib.StylePath, "{ not json");
        Assert.Throws<PersonaConfigException>(() => new PresetLibrary(root).Style);
    }

    [Fact]
    public void MadeOfFindsTheirGeneratedPicturesNewestFirst()
    {
        string pics = Path.Combine(root, "pictures");
        string a = PictureFiles.NewOutput(pics, "generate Vicki", new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero));
        string b = PictureFiles.NewOutput(pics, "generate Vicki", new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero));
        string other = PictureFiles.NewOutput(pics, "generate Vickie", new DateTimeOffset(2026, 11, 27, 11, 0, 0, TimeSpan.Zero));
        string dream = PictureFiles.NewOutput(pics, "Vicki dream", new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));
        foreach (string f in new[] { a, b, other, dream })
        {
            File.WriteAllText(f, "x");
        }
        Assert.Equal(new[] { b, a }, PictureFiles.MadeOf(pics, "Vicki"));
        Assert.Empty(PictureFiles.MadeOf(Path.Combine(root, "none"), "Vicki"));
    }

    [Fact]
    public void StyleReferencesAreUsedForPeopleNotScenesOrMen()
    {
        PresetLibrary lib = new(root);
        GenPreset smile = PresetLibrary.BuiltIn.First(p => p.Id == "action-smile");
        GenPreset tux = PresetLibrary.BuiltIn.First(p => p.Id == "outfit-tux");
        GenPreset scene = PresetLibrary.BuiltIn.First(p => p.Kind == PresetKinds.Scene);
        Assert.Empty(lib.TileJobFor(smile).References!);
        Assert.DoesNotContain("reference", lib.TileJobFor(smile).Prompt);

        string src = Path.Combine(root, "face.png");
        Directory.CreateDirectory(root);
        File.WriteAllText(src, "x");
        string added = lib.AddStyleReference(src);
        Assert.Single(lib.StyleReferences());
        TileJob job = lib.TileJobFor(smile);
        Assert.Equal(new[] { added }, job.References);
        Assert.StartsWith("The same person as in the reference picture", job.Prompt);
        Assert.Empty(lib.TileJobFor(tux).References!);
        Assert.Empty(lib.TileJobFor(scene).References!);

        File.WriteAllText(Path.Combine(root, "notes.txt"), "x");
        Assert.Throws<PersonaConfigException>(() => lib.AddStyleReference(Path.Combine(root, "notes.txt")));
        for (int i = 0; i < 3; i++)
        {
            lib.AddStyleReference(src);
        }
        Assert.Equal(PresetLibrary.MaxStyleReferences, lib.StyleReferences().Count);
        Assert.Throws<PersonaConfigException>(() => lib.AddStyleReference(src));
        Assert.Throws<PersonaConfigException>(() => lib.RemoveStyleReference(src));
        lib.RemoveStyleReference(added);
        Assert.Equal(3, lib.StyleReferences().Count);
    }

    [Fact]
    public void TileStyleClothesDressTheActionPictures()
    {
        GenPreset smile = PresetLibrary.BuiltIn.First(p => p.Id == "action-smile");
        Assert.Contains("wearing simple casual clothes", PresetLibrary.TilePrompt(smile));
        TileStyle mine = new() { Clothes = "a white lab coat open over black lace lingerie" };
        mine.Validate();
        Assert.False(mine.IsDefault);
        string prompt = PresetLibrary.TilePrompt(smile, mine);
        Assert.Contains("wearing a white lab coat open over black lace lingerie", prompt);
        Assert.DoesNotContain("casual", prompt);
        Assert.Contains("lab coat", CreatorOptions.TilePrompt(CreatorOptions.Find(CreatorKinds.Gender, Genders.Female)!, Genders.Female, mine));
        // An older saved style without clothes keeps its folder.
        TileStyle older = new() { Art = "watercolour painting" };
        Assert.Equal(older.Folder, new TileStyle { Art = "watercolour painting", Clothes = TileStyle.DefaultClothes }.Folder);
        Assert.NotEqual(older.Folder, new TileStyle { Art = "watercolour painting", Clothes = "a red dress" }.Folder);
        Assert.NotEqual("anime", new TileStyle { Engine = PictureModels.Anime }.Folder);
        Assert.Throws<PersonaConfigException>(() => new TileStyle { Engine = "nope" }.Validate());
    }
}
