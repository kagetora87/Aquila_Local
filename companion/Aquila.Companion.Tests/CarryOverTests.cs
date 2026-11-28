using AquilaLocal.Companion;
using AquilaLocal.Companion.Priming;
using Xunit;

namespace Aquila.Companion.Tests;

public sealed class CarryOverTests
{
    private sealed class Scripted : ITextGenerator
    {
        public List<string> Users { get; } = new();

        public Task<string> GenerateJsonAsync(string system, string user, int maxTokens, CancellationToken ct)
        {
            Users.Add(user);
            return Task.FromResult(system.StartsWith("You read") ? """{"events":[{"when":"2026-11-27 21:00","what":"pancakes"}],"jokes":["the goose"]}"""
                : system.StartsWith("You merge") ? """{"events":[]}"""
                : """
                {"page":"What happened\nYou and Ru made pancakes.","memories":[
                  {"when":"2026-11-27 22:00","layer":"hooks","text":"Ru promised to take Mae to the coast.","why":"a plan"},
                  {"when":"2026-11-27 21:00","layer":"facts","text":"Ru and Mae made pancakes at midnight.","why":"shared moment"},
                  {"when":"2026-11-27 21:05","layer":"facts","text":"Ru hates cilantro.","why":"known"},
                  {"when":"1999-01-01 00:00","layer":"weird","text":"The goose joke started over breakfast.","why":""}]}
                """);
        }
    }

    private static CarryOverRequest Request()
    {
        DateTimeOffset t = new(new DateTime(2026, 11, 27, 21, 0, 0, DateTimeKind.Local));
        return new CarryOverRequest
        {
            CompanionName = "Mae",
            UserName = "Ru",
            ChatTitle = "Pancakes",
            Lines = new()
            {
                new CarryLine("Ru", t, "Pancakes?"),
                new CarryLine("Mae", t.AddMinutes(1), "*grabs the flour* Always."),
                new CarryLine("Ru", t.AddHours(1), "Coast next weekend, promise.")
            },
            KnownMemories = new() { "Ru hates cilantro!" }
        };
    }

    [Fact]
    public async Task WritesThePageAndOnlyNewMemoriesOldestFirst()
    {
        Scripted g = new();
        CarryOverResult r = await new CarryOver(g).RunAsync(Request(), null, CancellationToken.None);
        Assert.StartsWith("What happened", r.Page);
        Assert.Equal(new[] { "Ru and Mae made pancakes at midnight.", "Ru promised to take Mae to the coast.", "The goose joke started over breakfast." }, r.Memories.Select(m => m.Text));
        Assert.Equal(MemoryLayers.Facts, r.Memories[2].Layer);
        // A time outside the chat falls back to its end.
        Assert.Equal(Request().Lines.Max(l => l.At), r.Memories[2].At);
        Assert.Contains("Ru hates cilantro!", g.Users.Last());
        Assert.Contains("] Mae: *grabs the flour* Always.", g.Users.First());
    }

    [Fact]
    public async Task AnEmptyChatFailsLoud()
    {
        CarryOverRequest r = Request();
        r.Lines.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => new CarryOver(new Scripted()).RunAsync(r, null, CancellationToken.None));
    }
}
