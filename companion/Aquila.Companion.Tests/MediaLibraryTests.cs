using AquilaLocal.Companion;
using AquilaLocal.Companion.Pictures;
using Xunit;

namespace Aquila.Companion.Tests;

public sealed class MediaLibraryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "aquila-media-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string Make(string month, string name, string? prompt = null)
    {
        string folder = Path.Combine(root, month);
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, name + ".png");
        File.WriteAllText(file, "x");
        if (prompt != null)
        {
            File.WriteAllText(Path.ChangeExtension(file, ".txt"), prompt + "\n\nModel: Anime\nSeed: 5\nReferences: a.png, b.png");
        }
        return file;
    }

    [Fact]
    public void ScansNewestFirstWithVideosAndPrompts()
    {
        string older = Make("2026-10", "20261001-120000-generate-vicki", "Vicki smiling");
        string newer = Make("2026-11", "20261127-030000-vicki-dream");
        File.WriteAllText(Path.ChangeExtension(newer, ".webm"), "v");
        File.WriteAllText(Path.Combine(root, "2026-11", "20261127-030000-vicki-dream.start.png"), "x");
        Directory.CreateDirectory(Path.Combine(root, "references"));
        File.WriteAllText(Path.Combine(root, "references", "r.png"), "x");
        List<MediaItem> items = new MediaLibrary(root).Scan();
        Assert.Equal(new[] { newer, older }, items.Select(i => i.Path));
        Assert.True(items[0].IsVideo);
        Assert.Equal("Vicki smiling", items[1].Prompt);
        Assert.Equal("Anime", items[1].Model);
        Assert.Equal(2, items[1].ReferenceCount);
        Assert.True(MediaLibrary.NamedFor(older, "Vicki"));
        Assert.False(MediaLibrary.NamedFor(older, "Vic"));
    }

    [Fact]
    public void FavoritesToggleAndDeleteRemovesEverything()
    {
        string file = Make("2026-11", "20261128-080000-a-picture", "p");
        File.WriteAllText(Path.ChangeExtension(file, ".webm"), "v");
        MediaLibrary lib = new(root);
        Assert.True(lib.ToggleFavorite(file));
        Assert.True(new MediaLibrary(root).IsFavorite(file));
        Assert.False(lib.ToggleFavorite(file));
        Assert.True(lib.ToggleFavorite(file));
        lib.Delete(lib.Scan().Single());
        Assert.Empty(lib.Scan());
        Assert.False(File.Exists(Path.ChangeExtension(file, ".webm")));
        Assert.False(File.Exists(Path.ChangeExtension(file, ".txt")));
        Assert.Empty(lib.Favorites());
        Assert.Throws<PersonaConfigException>(() => lib.Delete(new MediaItem(Path.Combine(Path.GetTempPath(), "x.png"), "", DateTimeOffset.Now, "", "", 0)));
    }
}
