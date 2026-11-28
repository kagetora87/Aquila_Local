using AquilaLocal.Companion;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>Checks a real persona file when AQUILA_PERSONA_FILE points at one (persona data is kept out of this public repo).</summary>
public sealed class PersonaFileTests
{
    [Fact]
    public void Persona_file_if_given_is_complete_and_fits_the_caps()
    {
        string? path = Environment.GetEnvironmentVariable("AQUILA_PERSONA_FILE");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        PersonaConfig c = PersonaConfig.Load(path);
        Assert.Empty(c.MissingInputs());
        using var dir = new TempDir();
        PersonaStore store = PersonaStore.Create(dir.File("p.db"), c, TestPersona.T0);
        Assert.Equal(c.Seed.Summary.Render(), store.ReadSummary().Render());
        var drives = store.ReadDrives(Drives.All);
        string block = ContextInjector.DriveBlock(c, drives, DriveEngine.TopDrive(c, drives));
        Console.WriteLine(block);
        Console.WriteLine($"drive block ~{TokenEstimate.Of(block)} tokens; system prompt ~{TokenEstimate.Of(ContextInjector.SystemPrompt(c, store.ReadSummary()))} tokens");
    }
}
