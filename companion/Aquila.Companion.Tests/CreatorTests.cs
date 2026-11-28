using AquilaLocal.Companion;
using Xunit;

namespace Aquila.Companion.Tests;

public sealed class CreatorTests
{
    internal static CompanionDraft Finished(string gender = Genders.Male)
    {
        CompanionDraft d = new();
        d.Choose(CreatorKinds.Gender, gender);
        d.Choose(CreatorKinds.Ethnicity, "latino");
        d.Choose(CreatorKinds.Hair, CreatorOptions.For(CreatorKinds.Hair, gender)[0].Key);
        d.HairColour = "Dark brown";
        d.Choose(CreatorKinds.Body, CreatorOptions.For(CreatorKinds.Body, gender)[0].Key);
        d.Name = "Marcus Lee";
        d.Age = 31;
        d.Occupation = "a marine biologist";
        d.Relationship = "an old friend from university";
        d.Personality = "Warm, dry humour, loves the sea and terrible puns. Patient listener who remembers small details.";
        d.EyeColour = "Hazel";
        d.Feature = "a thin scar through his left eyebrow";
        return d;
    }

    [Fact]
    public void EachStepSaysWhatsMissing()
    {
        CompanionDraft d = new();
        for (int step = 1; step <= 5; step++)
        {
            Assert.NotEmpty(d.Missing(step));
        }
        CompanionDraft done = Finished();
        Assert.Empty(done.MissingAll());
        done.Age = 17;
        Assert.Contains(done.Missing(5), m => m.Contains("adults"));
    }

    [Fact]
    public void ChangingGenderClearsPicksThatNoLongerFit()
    {
        CompanionDraft d = CreatorTests.Finished(Genders.Female);
        Assert.NotEmpty(d.Hair);
        d.Choose(CreatorKinds.Gender, Genders.Male);
        Assert.Equal("", d.Hair);
        Assert.Equal("", d.Body);
        Assert.Equal("latino", d.Ethnicity);
        Assert.All(CreatorOptions.For(CreatorKinds.Hair, Genders.Male), o => Assert.True(o.Fits(Genders.Male)));
    }

    [Fact]
    public void BuildMakesACompletePersonaReadyToSetUp()
    {
        PersonaConfig c = Finished().Build("marcus-lee", "2026-11-27");
        Assert.Equal("Marcus Lee", c.Name);
        Assert.Equal(31, c.Age);
        Assert.Equal(Genders.Male, c.Gender);
        Assert.Empty(c.MissingInputs());
        Assert.StartsWith("Marcus Lee is a 31-year-old Latino man, working as a marine biologist.", c.PersonalitySheet);
        Assert.Contains("old friend from university", c.PersonalitySheet);
        Assert.True(c.Seed.Appearance.IsComplete);
        Assert.Contains(c.Seed.Appearance.Traits, t => t.Name == "eyes" && t.Text == "hazel eyes");
        Assert.Contains(c.Seed.Appearance.Traits, t => t.Name == "face" && t.Text.Contains("adult"));
        Assert.Equal("a soft grey henley shirt and dark jeans", c.Seed.Appearance.Wardrobe.Outfit);
        Assert.Equal(5, c.Drives.Count);
        Assert.True(c.Drives.Single(x => x.Name == Drives.Comfort).IsLevel);
        // Saved and loaded again, it's the same.
        string path = Path.Combine(Path.GetTempPath(), "creator-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            c.Save(path);
            PersonaConfig back = PersonaConfig.Load(path);
            Assert.Equal(Genders.Male, back.Gender);
            Assert.Empty(back.MissingInputs());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildRefusesAnUnfinishedDraft()
    {
        CompanionDraft d = Finished();
        d.Feature = "";
        PersonaConfigException ex = Assert.Throws<PersonaConfigException>(() => d.Build("x", "today"));
        Assert.Contains("distinctive feature", ex.Message);
    }

    [Fact]
    public void DraftDrivesAreCopiedNotShared()
    {
        CompanionDraft d = Finished();
        PersonaConfig c = d.Build("m", "today");
        c.Drives[0].Weight = 9;
        Assert.NotEqual(9, d.Drives[0].Weight);
    }

    [Fact]
    public void PortraitPromptIsAnAdultWithTheirLooksAndExtras()
    {
        string p = Finished().PortraitPrompt("wearing glasses");
        Assert.StartsWith("Portrait of an adult man", p);
        Assert.Contains("hazel eyes", p);
        Assert.Contains("dark brown", p);
        Assert.Contains("scar through his left eyebrow", p);
        Assert.Contains("wearing glasses.", p);
        Assert.Contains("anime illustration", p);
    }

    [Fact]
    public void IdsAreSafeAndFree()
    {
        Assert.Equal("marcus-lee", CompanionDraft.NewId("Marcus Lee", Array.Empty<string>()));
        Assert.Equal("marcus-lee-2", CompanionDraft.NewId("Marcus Lee", new[] { "marcus-lee" }));
        Assert.Equal("zoe", CompanionDraft.NewId("Zoë!", Array.Empty<string>()));
        Assert.Equal("companion", CompanionDraft.NewId("桜", Array.Empty<string>()));
        Assert.True(PersonaRegistry.IsValidId(CompanionDraft.NewId(new string('a', 80), Array.Empty<string>())));
    }

    [Fact]
    public void EveryOptionHasATileAndTileIdsAreUnique()
    {
        List<TileJob> jobs = CreatorOptions.TileJobs();
        Assert.Equal(jobs.Count, jobs.Select(j => j.Id).Distinct().Count());
        foreach (string g in new[] { Genders.Female, Genders.Male })
        {
            foreach (string kind in new[] { CreatorKinds.Gender, CreatorKinds.Ethnicity, CreatorKinds.Hair, CreatorKinds.Body })
            {
                List<CreatorOption> options = CreatorOptions.For(kind, g);
                Assert.True(options.Count >= 2);
                Assert.All(options, o => Assert.Contains(jobs, j => j.Id == CreatorOptions.TileId(o, g)));
            }
        }
        Assert.All(jobs, j => Assert.Contains("adult", j.Prompt));
    }
}

public sealed class PronounTests
{
    [Fact]
    public void ThoughtPromptsUseHisWordsForAMan()
    {
        string m = AquilaLocal.Companion.Living.InnerLifePrompt.WriterSystem("Marcus", canLookUp: true, Genders.Male);
        Assert.Contains("while he's on his own", m);
        Assert.Contains("keep to himself", m);
        Assert.DoesNotContain("{s}", m);
        Assert.DoesNotContain(" her ", m);
        string f = AquilaLocal.Companion.Living.InnerLifePrompt.WriterSystem("Vicki", canLookUp: false);
        Assert.Contains("while she's on her own", f);
        Assert.Contains("\"kind\":\"daydream", f);
        string d = AquilaLocal.Companion.Living.InnerLifePrompt.DreamSystem("Marcus", Genders.Male);
        Assert.Contains("he's asleep", d);
        Assert.Contains("she_is_in_it", d);
    }

    [Fact]
    public void OutsideTextKeepsItsBraces()
    {
        AquilaLocal.Companion.Living.InnerLifeInput input = new("Marcus", "Loves {s} and {p} literally.", "", "", Array.Empty<Exchange>(), Array.Empty<string>(), "", DateTimeOffset.Now, null, "", false, Gender: Genders.Male);
        string u = AquilaLocal.Companion.Living.InnerLifePrompt.WriterUser(input);
        Assert.Contains("Loves {s} and {p} literally.", u);
        Assert.Contains("Who he is:", u);
        Assert.Contains("Write his next thought.", u);
    }

    [Fact]
    public void MessageReasonsUseTheirWords()
    {
        AquilaLocal.Companion.Living.LivingPolicy policy = new();
        string? why = AquilaLocal.Companion.Living.LivingSchedule.WhyNoMessage(DateTimeOffset.Now, 14, 1.0, Array.Empty<DateTimeOffset>(), true, policy, Genders.Male);
        Assert.Equal("he's waiting for you to answer his last message", why);
    }

    [Fact]
    public void BodyDetailsCustomHairAndBackgroundReachThePictures()
    {
        CompanionDraft d = CreatorTests.Finished(Genders.Female);
        d.Bust = "Large";
        d.Butt = "Round";
        d.Choose(CreatorKinds.Hair, CreatorOptions.CustomHair);
        Assert.Contains(d.Missing(3), m => m.Contains("Describe the hair"));
        d.HairCustom = "long silver hair in a side braid";
        Assert.Empty(d.Missing(3));
        d.Background = "Beach";
        AppearanceSeed a = d.Appearance();
        Assert.Contains("large breasts", a.Traits.Single(t => t.Name == "body").Text);
        Assert.Contains("round, shapely butt", a.Traits.Single(t => t.Name == "body").Text);
        Assert.Contains("silver hair in a side braid", a.Traits.Single(t => t.Name == "hair").Text);
        Assert.Contains("a beach at golden hour", d.PortraitPrompt());
        d.Background = "a rooftop at sunset";
        Assert.Contains("Background: a rooftop at sunset", d.PortraitPrompt());
        // Switching to male drops the chest choice.
        d.Choose(CreatorKinds.Gender, Genders.Male);
        Assert.DoesNotContain("breasts", d.BodyText);
    }
}
