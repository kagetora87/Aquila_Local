using AquilaLocal.Companion;
using AquilaLocal.Companion.Living;
using Xunit;

namespace Aquila.Companion.Tests;

public sealed class GrowthTests
{
    private const string Sheet = "You are Mae, a potter who lives by the sea. You are afraid to want things, because wanting has hurt you before. You love bad puns and strong tea. You call Ru 'sunshine' when you tease him.";

    [Fact]
    public void ReflectionsAreNewInsightsOnly()
    {
        List<Reflection> r = Reflections.Parse("{\"reflections\":[{\"layer\":\"facts\",\"text\":\"Ru goes quiet when work is stressful.\",\"why\":\"twice this week\"},{\"layer\":\"odd\",\"text\":\"I feel safer asking him for things now.\"},{\"layer\":\"facts\",\"text\":\"Ru loves pancakes!\"}]}", new[] { "Ru loves pancakes" });
        Assert.Equal(2, r.Count);
        Assert.Equal(MemoryLayers.Feelings, r[1].Layer);
        Assert.Empty(Reflections.Parse("nothing", Array.Empty<string>()));
    }

    [Fact]
    public void GrowthIsASmallChangeOrNothing()
    {
        string after = Sheet.Replace("You are afraid to want things, because wanting has hurt you before.", "You are afraid to want things, because wanting has hurt you before, though with Ru you've started to ask a little.");
        GrowthChange? g = Growth.Parse("{\"changed\":true,\"sheet\":" + System.Text.Json.JsonSerializer.Serialize(after) + ",\"summary\":\"I ask for things a little more\",\"why\":\"he kept saying yes\"}", Sheet, DateTimeOffset.Now);
        Assert.NotNull(g);
        Assert.Equal(Sheet, g!.Before);
        Assert.Equal("I ask for things a little more", g.Summary);
        Assert.Null(Growth.Parse("{\"changed\":false}", Sheet, DateTimeOffset.Now));
        Assert.Null(Growth.Parse("{\"changed\":true,\"sheet\":\"You are someone else entirely, a pilot.\"}", Sheet, DateTimeOffset.Now));
    }

    [Fact]
    public void TheLogKeepsChangesAndWhenSheLastGrew()
    {
        string root = Path.Combine(Path.GetTempPath(), "aquila-growth-" + Guid.NewGuid().ToString("N"));
        try
        {
            GrowthStore store = new(root);
            Assert.Null(store.LastGrew("mae"));
            store.Add("mae", new GrowthChange { Before = "a", After = "b", Summary = "s" });
            Assert.Equal(1, store.Unseen("mae"));
            DateTimeOffset at = DateTimeOffset.Now;
            store.MarkGrew("mae", at);
            store.MarkReflected("mae", at);
            Assert.Equal(at, store.LastGrew("mae"));
            Assert.Equal(at, store.LastReflected("mae"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
