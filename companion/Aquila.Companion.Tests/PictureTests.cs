using AquilaLocal.Companion;
using AquilaLocal.Companion.Pictures;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>
/// The built-in image generator: finds its files (and names what's missing), builds the engine command with the
/// settings measured on the laptop, reads picture requests, and tidies the prompt the chat model writes.
/// </summary>
public sealed class PictureTests
{
    private static PictureKit Kit(TempDir dir, bool fast = true, bool best = true)
    {
        var kit = new PictureKit(dir.File("imagegen"));
        Directory.CreateDirectory(kit.EngineFolder);
        Directory.CreateDirectory(kit.ModelsFolder);
        File.WriteAllText(Path.Combine(kit.EngineFolder, "sd-cli.exe"), "");
        if (fast)
        {
            foreach (string f in new[] { "flux3-image-klein-Q8_0.gguf", "Qwen3VL-4B-Instruct-Q4_K_M.gguf", "flux2-vae.safetensors" })
            {
                File.WriteAllText(Path.Combine(kit.ModelsFolder, f), "");
            }
        }
        if (best)
        {
            foreach (string f in new[] { "qwen_image_2.1-Q8_0.gguf", "qwen_image_2.1_vae_bf16.safetensors", "Qwen3VL-8B-Instruct-Q4_K_M.gguf", "mmproj-Qwen3VL-8B-Instruct-F16.gguf" })
            {
                File.WriteAllText(Path.Combine(kit.ModelsFolder, f), "");
            }
        }
        return kit;
    }

    [Fact]
    public void Finds_the_files_installed_on_the_laptop()
    {
        using var dir = new TempDir();
        PictureKit kit = Kit(dir);

        PictureModelFiles fast = kit.Resolve(PictureModels.Fast);
        PictureModelFiles best = kit.Resolve(PictureModels.Best);

        Assert.EndsWith("flux3-image-klein-Q8_0.gguf", fast.Diffusion);
        Assert.EndsWith("Qwen3VL-4B-Instruct-Q4_K_M.gguf", fast.TextEncoder);
        Assert.Null(fast.Vision);
        Assert.EndsWith("qwen_image_2.1-Q8_0.gguf", best.Diffusion);
        Assert.EndsWith("Qwen3VL-8B-Instruct-Q4_K_M.gguf", best.TextEncoder);
        Assert.EndsWith("mmproj-Qwen3VL-8B-Instruct-F16.gguf", best.Vision);
    }

    [Fact]
    public void Klein_can_read_prompts_with_the_uncensored_text_encoder()
    {
        using var dir = new TempDir();
        PictureKit kit = Kit(dir, best: false);
        Assert.EndsWith("Qwen3VL-4B-Instruct-Q4_K_M.gguf", kit.Resolve(PictureModels.Fast).TextEncoder);

        kit.KleinEncoder = KleinEncoders.UncensoredQ8;
        var missing = Assert.Throws<PersonaConfigException>(() => kit.Resolve(PictureModels.Fast));
        Assert.Contains("uncensored 8-bit", missing.Message);

        foreach (string f in new[] { "Qwen3-VL-4B-Instruct-Uncensored-Q8_0.gguf", "Qwen3-VL-4B-Instruct-Uncensored-BF16.gguf" })
        {
            File.WriteAllText(Path.Combine(kit.ModelsFolder, f), "");
        }
        Assert.EndsWith("Qwen3-VL-4B-Instruct-Uncensored-Q8_0.gguf", kit.Resolve(PictureModels.Fast).TextEncoder);
        kit.KleinEncoder = KleinEncoders.UncensoredBf16;
        Assert.EndsWith("Qwen3-VL-4B-Instruct-Uncensored-BF16.gguf", kit.Resolve(PictureModels.Fast).TextEncoder);

        // The standard choice never picks an uncensored file, even when its own is missing.
        File.Delete(Path.Combine(kit.ModelsFolder, "Qwen3VL-4B-Instruct-Q4_K_M.gguf"));
        kit.KleinEncoder = KleinEncoders.Standard;
        Assert.Throws<PersonaConfigException>(() => kit.Resolve(PictureModels.Fast));
        kit.KleinEncoder = "nope";
        Assert.Throws<PersonaConfigException>(() => kit.Resolve(PictureModels.Fast));
    }

    [Fact]
    public void Missing_files_are_named_not_guessed()
    {
        using var dir = new TempDir();
        PictureKit kit = Kit(dir, best: false);

        var ex = Assert.Throws<PersonaConfigException>(() => kit.Resolve(PictureModels.Best));
        Assert.Contains("Qwen-Image-2.1 model", ex.Message);
        Assert.Contains("vision file", ex.Message);
        Assert.True(kit.Check(PictureModels.Fast).Ready);
    }

    [Fact]
    public void The_fast_model_uses_16_steps_and_no_guidance()
    {
        using var dir = new TempDir();
        PictureModelFiles files = Kit(dir).Resolve(PictureModels.Fast);
        List<string> args = PictureCommand.Arguments(files, new PictureJob("a cat", 1024, 1024, new[] { "a.png" }, 42, "out.png"));

        Assert.Equal("16", args[args.IndexOf("--steps") + 1]);
        Assert.Equal("1.0", args[args.IndexOf("--cfg-scale") + 1]);
        Assert.Contains("--offload-to-cpu", args);
        Assert.Equal("a.png", args[args.IndexOf("-r") + 1]);
        Assert.DoesNotContain("--llm_vision", args);
    }

    [Fact]
    public void The_best_model_needs_its_vision_file_for_references_and_takes_at_most_four()
    {
        using var dir = new TempDir();
        PictureModelFiles files = Kit(dir).Resolve(PictureModels.Best);
        List<string> args = PictureCommand.Arguments(files, new PictureJob("a cat", 1024, 1024, new[] { "1.png", "2.png", "3.png", "4.png", "5.png" }, 7, "out.png"));

        Assert.Equal(4, args.Count(a => a == "-r"));
        Assert.Contains("--llm_vision", args);
        Assert.Equal("6.0", args[args.IndexOf("--cfg-scale") + 1]);
    }

    [Fact]
    public void The_anime_model_is_one_checkpoint_with_tags_a_negative_prompt_and_no_references()
    {
        using var dir = new TempDir();
        PictureKit kit = Kit(dir);
        var missing = Assert.Throws<PersonaConfigException>(() => kit.Resolve(PictureModels.Anime));
        Assert.Contains("WAI-Illustrious", missing.Message);
        Assert.Contains("sdxl_vae-fp16-fix", missing.Message);

        File.WriteAllText(Path.Combine(kit.ModelsFolder, "waiIllustriousSDXL_v170.safetensors"), "");
        File.WriteAllText(Path.Combine(kit.ModelsFolder, "sdxl_vae-fp16-fix.safetensors"), "");
        PictureModelFiles files = kit.Resolve(PictureModels.Anime);
        List<string> args = PictureCommand.Arguments(files, new PictureJob("1girl, solo, adult", 832, 1216, new[] { "a.png" }, 3, "out.png"));

        Assert.EndsWith("waiIllustriousSDXL_v170.safetensors", args[args.IndexOf("-m") + 1]);
        Assert.EndsWith("sdxl_vae-fp16-fix.safetensors", args[args.IndexOf("--vae") + 1]);
        Assert.DoesNotContain("--diffusion-model", args);
        Assert.DoesNotContain("--llm", args);
        Assert.DoesNotContain("-r", args);
        Assert.StartsWith("masterpiece, best quality", args[args.IndexOf("-p") + 1]);
        Assert.EndsWith("1girl, solo, adult", args[args.IndexOf("-p") + 1]);
        Assert.Contains("censored", args[args.IndexOf("-n") + 1]);
        Assert.Contains("loli", args[args.IndexOf("-n") + 1]);
        Assert.Equal("euler_a", args[args.IndexOf("--sampling-method") + 1]);
        Assert.Equal("2", args[args.IndexOf("--clip-skip") + 1]);
        Assert.True(PictureModels.UsesTags(PictureModels.Anime));
        Assert.False(PictureModels.TakesReferences(PictureModels.Anime));
        Assert.Equal(2, PictureModels.IndexOf(PictureModels.Anime));
    }

    [Fact]
    public void The_anime_model_draws_small_pictures_bigger_and_drops_the_reference_lead()
    {
        Assert.Equal((896, 1152), PictureModels.DrawSize(PictureModels.Anime, (896, 1152)));
        (int w, int h) = PictureModels.DrawSize(PictureModels.Anime, (512, 640));
        Assert.Equal((896, 1088), (w, h));
        Assert.Equal((512, 640), PictureModels.DrawSize(PictureModels.Fast, (512, 640)));

        Assert.Equal("A cat.", PicturePrompt.WithoutReferences(PicturePrompt.WithReferences("A cat.", 1)));
        Assert.Equal("A cat.", PicturePrompt.WithoutReferences(PicturePrompt.WithReferences("A cat.", 3)));
        Assert.Equal("A cat.", PicturePrompt.WithoutReferences("A cat."));
    }

    [Fact]
    public void Odd_sizes_are_refused()
    {
        using var dir = new TempDir();
        PictureModelFiles files = Kit(dir).Resolve(PictureModels.Fast);

        Assert.Throws<PersonaConfigException>(() => PictureCommand.Arguments(files, new PictureJob("x", 1000, 1024, Array.Empty<string>(), 1, "o.png")));
    }

    [Fact]
    public void Reads_a_picture_request_and_picks_a_shape()
    {
        PictureRequest r = PictureRequest.Parse("{\"description\":\"me on the sofa with coffee\",\"includes_you\":true}");

        Assert.True(r.IncludesPersona);
        Assert.Equal("half body", r.Framing);
        Assert.True(r.Size().Height > r.Size().Width);
        Assert.Equal("scene", PictureRequest.Parse("{\"description\":\"the lab at night\"}").Framing);
        Assert.Throws<PersonaConfigException>(() => PictureRequest.Parse("{\"includes_you\":true}"));
    }

    [Fact]
    public void The_writer_gets_her_appearance_only_when_she_is_in_the_picture()
    {
        var withHer = new PictureRequest("me waving", true, "portrait");
        var without = new PictureRequest("a sunset", false, "landscape");

        Assert.Contains("platinum", PicturePrompt.WriterUser(withHer, "", "Vicki", "Vicki, hair: platinum ponytail", false));
        Assert.DoesNotContain("platinum", PicturePrompt.WriterUser(without, "", "Vicki", "Vicki, hair: platinum ponytail", false));
    }

    [Fact]
    public void The_writer_is_told_where_they_are()
    {
        string user = PicturePrompt.WriterUser(new PictureRequest("me waving", true, "portrait"), "", "Vicki", "Vicki", false, "a rooftop bar at night");

        Assert.Contains("a rooftop bar at night", user);
        Assert.DoesNotContain("Where they are", PicturePrompt.WriterUser(new PictureRequest("me waving", true, "portrait"), "", "Vicki", "Vicki", false));
    }

    [Fact]
    public void The_written_prompt_is_tidied()
    {
        Assert.Equal("A woman in a lab, warm light.", PicturePrompt.Clean("<think>hmm</think>\n**Prompt:** \"A woman in a lab,\nwarm light.\""));
        Assert.Throws<PersonaConfigException>(() => PicturePrompt.Clean("<think>only thinking</think>"));
        Assert.True(PicturePrompt.Clean(new string('a', 5000)).Length <= PicturePrompt.MaxPromptChars);
    }

    [Fact]
    public void With_references_the_clothes_come_from_the_prompt_not_the_reference()
    {
        string p = PicturePrompt.WithReferences("She wears a red sweater.", 2);

        Assert.Contains("not from the references", p);
        Assert.EndsWith("She wears a red sweater.", p);
        Assert.Equal("x", PicturePrompt.WithReferences("x", 0));
    }

    [Fact]
    public void The_context_leaves_out_your_thoughts_and_sound_cues()
    {
        string c = PicturePrompt.Conversation(new[] { ("Hi <thought>secret</thought>", "Hello! <sound>Giggle</sound>") }, "Vicki");

        Assert.DoesNotContain("secret", c);
        Assert.DoesNotContain("Giggle", c);
        Assert.Contains("Vicki: Hello!", c);
    }

    [Fact]
    public void Pictures_get_dated_names_and_references_are_listed_in_order()
    {
        using var dir = new TempDir();
        string path = PictureFiles.NewOutput(dir.File("pictures"), "Vicki on the sofa!", TestPersona.T0);
        Assert.EndsWith(Path.Combine("2026-11", "20261126-200000-vicki-on-the-sofa.png"), path);

        string refs = PictureFiles.ReferenceFolder(dir.File("vicki"));
        Directory.CreateDirectory(refs);
        File.WriteAllText(Path.Combine(refs, "b.png"), "");
        File.WriteAllText(Path.Combine(refs, "a.jpg"), "");
        File.WriteAllText(Path.Combine(refs, "notes.txt"), "");
        Assert.Equal(new[] { "a.jpg", "b.png" }, PictureFiles.References(dir.File("vicki")).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("  5/16 - 0.65s/it", 5, 16)]
    [InlineData("|=====>      | 12/20 - 1.53s/it", 12, 20)]
    [InlineData("|===============>                                  |  5/16 -   1.14s/it - ETA 00:18\u001b[K", 5, 16)]
    public void Reads_progress_from_the_engine(string line, int step, int of)
    {
        Assert.Equal((step, of), PictureCommand.Progress(line));
    }
}
