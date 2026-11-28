using AquilaLocal.Companion;
using AquilaLocal.Companion.Pictures;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>Built-in video (Wan 2.2 image-to-video): finds its files, builds the command measured on the laptop, reads requests.</summary>
public sealed class VideoTests
{
    private static VideoKit Kit(TempDir dir, bool lightning = true)
    {
        var kit = new VideoKit(dir.File("imagegen"));
        Directory.CreateDirectory(kit.EngineFolder);
        Directory.CreateDirectory(kit.LorasFolder);
        File.WriteAllText(Path.Combine(kit.EngineFolder, "sd-cli.exe"), "");
        foreach (string f in new[] { "Wan2.2-I2V-A14B-HighNoise-Q8_0.gguf", "Wan2.2-I2V-A14B-LowNoise-Q8_0.gguf", "umt5-xxl-encoder-Q8_0.gguf", "wan_2.1_vae.safetensors" })
        {
            File.WriteAllText(Path.Combine(kit.ModelsFolder, f), "");
        }
        if (lightning)
        {
            File.WriteAllText(Path.Combine(kit.LorasFolder, "wan22_i2v_lightning_high.safetensors"), "");
            File.WriteAllText(Path.Combine(kit.LorasFolder, "wan22_i2v_lightning_low.safetensors"), "");
        }
        return kit;
    }

    private static VideoJob Job(int frames = 81) => new("she waves", "start.png", 960, 544, frames, 42, "out.webm");

    [Fact]
    public void Lightning_uses_two_plus_two_steps_with_both_loras()
    {
        using var dir = new TempDir();
        List<string> a = VideoCommand.Arguments(Kit(dir).Resolve(), Job(), fast: true);

        Assert.Equal("vid_gen", a[a.IndexOf("-M") + 1]);
        Assert.Equal("2", a[a.IndexOf("--steps") + 1]);
        Assert.Equal("2", a[a.IndexOf("--high-noise-steps") + 1]);
        Assert.Equal("1.0", a[a.IndexOf("--cfg-scale") + 1]);
        Assert.Equal("5.0", a[a.IndexOf("--flow-shift") + 1]);
        string prompt = a[a.IndexOf("-p") + 1];
        Assert.Contains("<lora:wan22_i2v_lightning_low:1>", prompt);
        Assert.Contains("<lora:|high_noise|wan22_i2v_lightning_high:1>", prompt);
        Assert.Contains("--mmap", a);
        Assert.DoesNotContain("--offload-to-cpu", a);
        Assert.Equal("start.png", a[a.IndexOf("-i") + 1]);
    }

    [Fact]
    public void Without_lightning_it_uses_the_full_quality_steps()
    {
        using var dir = new TempDir();
        List<string> a = VideoCommand.Arguments(Kit(dir, lightning: false).Resolve(), Job(), fast: true);

        Assert.Equal("10", a[a.IndexOf("--steps") + 1]);
        Assert.Equal("8", a[a.IndexOf("--high-noise-steps") + 1]);
        Assert.DoesNotContain("<lora:", a[a.IndexOf("-p") + 1]);
        Assert.DoesNotContain("--lora-model-dir", a);
    }

    [Fact]
    public void Missing_files_are_named_and_bad_lengths_refused()
    {
        using var dir = new TempDir();
        VideoKit kit = Kit(dir);
        File.Delete(Path.Combine(kit.ModelsFolder, "wan_2.1_vae.safetensors"));
        var ex = Assert.Throws<PersonaConfigException>(() => kit.Resolve());
        Assert.Contains("VAE", ex.Message);

        using var dir2 = new TempDir();
        VideoModelFiles files = Kit(dir2).Resolve();
        Assert.Throws<PersonaConfigException>(() => VideoCommand.Arguments(files, Job(80), true));
        Assert.Throws<PersonaConfigException>(() => VideoCommand.Arguments(files, Job() with { Width = 970 }, true));
    }

    [Fact]
    public void Reads_a_video_request()
    {
        VideoRequest r = VideoRequest.Parse("{\"description\":\"me on the sofa\",\"motion\":\"I wave\",\"includes_you\":true}");
        Assert.Equal("I wave", r.Motion);
        Assert.True(r.Still.IncludesPersona);
        Assert.Throws<PersonaConfigException>(() => VideoRequest.Parse("{\"description\":\"me\"}"));
        Assert.StartsWith("I wave.", VideoRequest.ClipPrompt("I wave.", "A woman on a sofa."));
        Assert.Equal(5.0625, VideoCommand.Seconds(81));
    }
}
