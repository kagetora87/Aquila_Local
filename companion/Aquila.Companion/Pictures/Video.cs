using System.Globalization;

namespace AquilaLocal.Companion.Pictures;

/// <summary>The files Wan 2.2 image-to-video needs, found in the image folder (imagegen\models and imagegen\models\loras).</summary>
/// <param name="LightningHigh">The 4-step Lightning LoRA for the high-noise model, or null (then the full-quality steps are used).</param>
public sealed record VideoModelFiles(string Engine, string HighNoise, string LowNoise, string TextEncoder, string Vae, string? LightningHigh, string? LightningLow)
{
    public bool HasLightning => LightningHigh != null && LightningLow != null;
}

/// <summary>Finds the video model (Wan 2.2 I2V A14B) files. Missing files are named, never guessed.</summary>
public sealed class VideoKit
{
    public VideoKit(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }

    public string EngineFolder => Path.Combine(Folder, "sd");

    public string ModelsFolder => Path.Combine(Folder, "models");

    public string LorasFolder => Path.Combine(ModelsFolder, "loras");

    public VideoModelFiles Resolve()
    {
        var missing = new List<string>();
        string engine = new[] { "sd-cli.exe", "sd.exe" }.Select(n => Path.Combine(EngineFolder, n)).FirstOrDefault(File.Exists) ?? Missing("the image engine (sd-cli.exe)", missing);
        string high = Find(ModelsFolder, new[] { "Wan2.2-I2V-A14B-HighNoise*.gguf", "wan2.2_i2v_high_noise*.gguf", "wan2.2_i2v_high_noise*.safetensors" }) ?? Missing("the Wan 2.2 high-noise model", missing);
        string low = Find(ModelsFolder, new[] { "Wan2.2-I2V-A14B-LowNoise*.gguf", "wan2.2_i2v_low_noise*.gguf", "wan2.2_i2v_low_noise*.safetensors" }) ?? Missing("the Wan 2.2 low-noise model", missing);
        string t5 = Find(ModelsFolder, new[] { "umt5-xxl-encoder*.gguf", "umt5_xxl*.safetensors" }) ?? Missing("its text encoder (umt5-xxl)", missing);
        string vae = Find(ModelsFolder, new[] { "wan_2.1_vae*.safetensors" }) ?? Missing("its VAE (wan_2.1_vae)", missing);
        if (missing.Count > 0)
        {
            throw new PersonaConfigException($"Video can't be made yet: missing {string.Join(", ", missing)} in {Folder}.");
        }
        return new VideoModelFiles(engine, high, low, t5, vae,
            Find(LorasFolder, new[] { "wan22_i2v_lightning_high*.safetensors" }),
            Find(LorasFolder, new[] { "wan22_i2v_lightning_low*.safetensors" }));
    }

    public (bool Ready, string Problem) Check()
    {
        try
        {
            Resolve();
            return (true, "");
        }
        catch (PersonaConfigException ex)
        {
            return (false, ex.Message);
        }
    }

    private static string? Find(string folder, string[] patterns)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }
        foreach (string p in patterns)
        {
            string? hit = Directory.GetFiles(folder, p).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (hit != null)
            {
                return hit;
            }
        }
        return null;
    }

    private static string Missing(string what, List<string> missing)
    {
        missing.Add(what);
        return "";
    }
}

/// <summary>One clip: the motion prompt, the still it starts from, its size, length and seed.</summary>
public sealed record VideoJob(string Prompt, string StartImage, int Width, int Height, int Frames, long Seed, string Output);

/// <summary>The engine's command line for one clip (settings measured on Takeru's laptop, Nov 2026).</summary>
public static class VideoCommand
{
    /// <summary>Wan 2.2 makes 16 frames a second; 81 frames is 5 seconds.</summary>
    public const int FramesPerSecond = 16;

    public const int DefaultFrames = 81;

    /// <summary>960×544 with Lightning took 255 s on the laptop (832×480: 202 s) and looked sharper.</summary>
    public const int DefaultWidth = 960;

    public const int DefaultHeight = 544;

    public const string NegativePrompt = "blurry, distorted face, extra fingers, deformed hands, flicker, jpeg artifacts, static, frozen, low quality, watermark, text";

    /// <param name="fast">Use the Lightning LoRAs (4 steps instead of 18) when they're installed.</param>
    public static List<string> Arguments(VideoModelFiles files, VideoJob job, bool fast)
    {
        if (job.Width % 16 != 0 || job.Height % 16 != 0 || job.Width < 256 || job.Height < 256 || job.Width > 1280 || job.Height > 1280)
        {
            throw new PersonaConfigException($"A video must be 256 to 1280 pixels on each side, in steps of 16 ({job.Width}×{job.Height} isn't).");
        }
        if (job.Frames < 17 || job.Frames > 161 || (job.Frames - 1) % 4 != 0)
        {
            throw new PersonaConfigException($"A video's frame count must be 4k+1 between 17 and 161 ({job.Frames} isn't).");
        }
        bool lightning = fast && files.HasLightning;
        string prompt = job.Prompt;
        if (lightning)
        {
            prompt += " " + LoraTag(files.LightningLow!, highNoise: false) + " " + LoraTag(files.LightningHigh!, highNoise: true);
        }
        var a = new List<string>
        {
            "-M", "vid_gen",
            "--diffusion-model", files.LowNoise,
            "--high-noise-diffusion-model", files.HighNoise,
            "--vae", files.Vae,
            "--t5xxl", files.TextEncoder,
            "-p", prompt,
            "-n", NegativePrompt,
            "-i", job.StartImage,
            "-W", job.Width.ToString(CultureInfo.InvariantCulture),
            "-H", job.Height.ToString(CultureInfo.InvariantCulture),
            "--video-frames", job.Frames.ToString(CultureInfo.InvariantCulture),
            "--seed", job.Seed.ToString(CultureInfo.InvariantCulture),
            "--sampling-method", "euler",
            "--diffusion-fa",
            // --offload-to-cpu failed on the laptop ("RAM graph capacity exceeded"); memory-mapped weights worked.
            "--mmap",
            "-o", job.Output,
        };
        if (lightning)
        {
            a.AddRange(new[] { "--lora-model-dir", Path.GetDirectoryName(files.LightningLow!)!, "--cfg-scale", "1.0", "--steps", "2", "--high-noise-steps", "2", "--flow-shift", "5.0" });
        }
        else
        {
            a.AddRange(new[] { "--cfg-scale", "3.5", "--steps", "10", "--high-noise-steps", "8", "--flow-shift", "3.0" });
        }
        return a;
    }

    /// <summary>The prompt tag that applies a LoRA to the low-noise model, or (highNoise) to the high-noise model.</summary>
    public static string LoraTag(string file, bool highNoise) =>
        "<lora:" + (highNoise ? "|high_noise|" : "") + Path.GetFileNameWithoutExtension(file) + ":1>";

    public static double Seconds(int frames) => frames / (double)FramesPerSecond;
}

/// <summary>A make_video request: the first frame (drawn like a picture) and what happens in the clip.</summary>
public sealed record VideoRequest(PictureRequest Still, string Motion)
{
    /// <summary>The still is drawn at about the clip's shape, larger (16:9), then scaled down to start the clip.</summary>
    public static readonly (int Width, int Height) StillSize = (1536, 864);

    public const int MaxMotionChars = 600;

    public static VideoRequest Parse(string json)
    {
        PictureRequest still = PictureRequest.Parse(json);
        string motion;
        try
        {
            System.Text.Json.JsonElement root = System.Text.Json.JsonDocument.Parse(json).RootElement;
            motion = root.TryGetProperty("motion", out System.Text.Json.JsonElement m) && m.ValueKind == System.Text.Json.JsonValueKind.String ? m.GetString()!.Trim() : "";
        }
        catch (System.Text.Json.JsonException)
        {
            throw new PersonaConfigException("The video request wasn't valid JSON.");
        }
        if (motion.Length == 0)
        {
            throw new PersonaConfigException("Say what happens in the video (motion).");
        }
        return new VideoRequest(still with { Framing = "landscape" }, motion.Length > MaxMotionChars ? motion[..MaxMotionChars] : motion);
    }

    /// <summary>The clip's prompt: the movement first (what Wan follows most), then the scene from the still's prompt.</summary>
    public static string ClipPrompt(string motion, string stillPrompt)
    {
        string scene = stillPrompt.Length > 400 ? stillPrompt[..400] : stillPrompt;
        return motion.TrimEnd('.', ' ') + ". Natural, smooth motion, consistent face and clothing. " + scene;
    }
}
