using System.IO;
using System;
using System.Globalization;

namespace AquilaLocal;

public sealed record InferenceLaunch(string Device, string Layers, string Cache, int ExpertCpuLayers, bool UseMmap, bool FullOffload)
{
	public const long GiB = 1073741824L;

	public bool CpuOnly => Device == "none";

	/// <summary>Context chosen by Automatic mode, or 0 when the user's own setting applies.</summary>
	public int AutoContext { get; init; }

	/// <summary>Plain-language description of what Automatic mode decided.</summary>
	public string Summary { get; init; } = "";

	public string Flash => CpuOnly ? "auto" : "on";

	/// <summary>Prompt-reading batch sizes. llama.cpp's defaults (2048/512) read prompts several times
	/// faster on a GPU than the 256/128 this app used to hard-code.</summary>
	public int Batch => CpuOnly ? 512 : 2048;

	public int MicroBatch => 512;

	/// <summary>CPU threads to use when the setting is 0 (automatic). With every layer on the GPU the
	/// CPU only feeds the GPU, so a few threads are enough; otherwise use the physical cores but leave
	/// a little headroom so the computer stays responsive.</summary>
	public int AutoThreads
	{
		get
		{
			int physical = Math.Max(1, Environment.ProcessorCount / 2);
			if (!CpuOnly && FullOffload)
			{
				return Math.Clamp(physical / 2, 2, 8);
			}
			return Math.Max(1, physical - Math.Max(1, physical / 8));
		}
	}

	public static InferenceLaunch Resolve(Settings settings, ModelInfo model)
	{
		InferenceProfile inferenceProfile = InferenceProfile.Resolve(settings.Inference, settings.Hardware);
		string key = inferenceProfile.Key;
		if (key == "Auto")
		{
			long projector = ((settings.ProjectorPath.Length > 0 && File.Exists(settings.ProjectorPath)) ? new FileInfo(settings.ProjectorPath).Length : 0);
			return PlanAuto(settings.Hardware, model, projector);
		}
		string cache = ((key == "High") ? "q8_0" : ((key == "Laptop") ? (settings.Inference.LaptopQ4 ? "q4_0" : "q8_0") : (settings.Inference?.CustomCache ?? "f16")));
		// Use the GPU only when detection actually found one. The old code fell back to "Vulkan0",
		// which on many laptops is the weak integrated GPU.
		string gpuDevice = settings.Hardware?.Gpu?.Device;
		bool useGpu = gpuDevice != null && (key != "Custom" || settings.GpuLayers != 0);
		string device = (useGpu ? gpuDevice : "none");
		int expertCpuLayers = ((useGpu && key != "High" && model.ExpertCount > 1) ? Math.Clamp(settings.Inference?.ExpertCpuLayers ?? 0, 0, model.BlockCount) : 0);
		bool fullOffload = useGpu && (key == "High" || expertCpuLayers > 0);
		string layers = ((!useGpu) ? "0" : (fullOffload ? "all" : ((key != "Custom" || settings.GpuLayers < 0) ? "auto" : settings.GpuLayers.ToString(CultureInfo.InvariantCulture))));
		return new InferenceLaunch(device, layers, cache, expertCpuLayers, key == "Laptop" || expertCpuLayers > 0, fullOffload);
	}

	/// <summary>
	/// Automatic mode. With a graphics card that can hold the whole model, put every layer on it with
	/// an 8-bit context cache and make the context as large as the remaining video memory allows,
	/// keeping a safety margin for Windows and other apps. Otherwise split the model between GPU and
	/// CPU with a moderate context, or run on the CPU.
	/// </summary>
	public static InferenceLaunch PlanAuto(HardwareInventory? hardware, ModelInfo model, long projectorBytes)
	{
		InferenceGpu gpu = hardware?.Gpu;
		int native = Math.Clamp(model.NativeContext, 2048, 262144);
		if (gpu == null)
		{
			int ctx = Math.Min(8192, native);
			return new InferenceLaunch("none", "0", "q8_0", 0, UseMmap: false, FullOffload: false)
			{
				AutoContext = ctx,
				Summary = $"No graphics card found, so the processor runs the model (slower). Conversations up to {Describe(ctx)}."
			};
		}
		int fit = FullGpuContext(gpu, model, projectorBytes, "q8_0");
		if (fit > 0)
		{
			int ctx = fit;
			return new InferenceLaunch(gpu.Device, "auto", "q8_0", 0, UseMmap: false, FullOffload: true)
			{
				AutoContext = ctx,
				Summary = $"Running fully on {gpu.Name}. Conversations up to {Describe(ctx)}."
			};
		}
		int partial = Math.Min(16384, native);
		return new InferenceLaunch(gpu.Device, "auto", "q8_0", 0, UseMmap: false, FullOffload: false)
		{
			AutoContext = partial,
			Summary = $"This model is bigger than {gpu.Name}'s memory, so part of it runs on the processor (slower). Conversations up to {Describe(partial)}."
		};
	}

	/// <summary>Context cache size relative to f16 for each cache type (8.5 and 4.5 bits per value).</summary>
	public static double CacheFactor(string cache) => cache switch
	{
		"q8_0" => 17.0 / 32.0,
		"q4_0" => 9.0 / 32.0,
		_ => 1.0
	};

	/// <summary>
	/// The largest context (a multiple of 4,096, at least 8,192, at most the model's own maximum) that fits
	/// with the whole model on the graphics card, keeping a safety margin for Windows and other apps; 0 when
	/// not even 8,192 fits. With a projector the engine runs 1024-token micro-batches and the vision encoder,
	/// which need a little more working memory. Calibrated on the laptop: Gemma 4 31B (23.1 GB free) plans 64K,
	/// measured 22.3 GB at peak while reading an image, and spilled past ~72K.
	/// </summary>
	public static int FullGpuContext(InferenceGpu gpu, ModelInfo model, long projectorBytes, string cache)
	{
		int native = Math.Clamp(model.NativeContext, 2048, 262144);
		double factor = CacheFactor(cache);
		long usable = Math.Min(gpu.FreeBytes > 0 ? gpu.FreeBytes : gpu.DedicatedBytes, gpu.DedicatedBytes);
		long reserve = Math.Max(3 * GiB / 2, gpu.DedicatedBytes / 16);
		long workspace = GiB + (projectorBytes > 0 ? GiB / 4 + projectorBytes : 0);
		long kvBudget = usable - reserve - workspace - model.FileBytes - (long)(model.FixedKvBytes * factor);
		double kvPerToken = Math.Max(1024.0, model.KvBytesPerToken) * factor;
		if (kvBudget < (long)(kvPerToken * Math.Min(8192, native)))
		{
			return 0;
		}
		if (native <= 8192)
		{
			return native;
		}
		int ctx = (int)Math.Clamp(kvBudget / kvPerToken, 8192.0, native);
		return Math.Min(native, Math.Max(8192, ctx / 4096 * 4096));
	}

	/// <summary>Tokens in plain words: roughly 0.75 words per token, about 400 words per page.</summary>
	public static string Describe(int tokens)
	{
		int pages = Math.Max(1, (int)Math.Round(tokens * 0.75 / 400.0 / 10.0) * 10);
		return $"{tokens / 1024:N0}K tokens (about {pages:N0} pages)";
	}

	public void AddArguments(Action<string[]> add)
	{
		add(new string[10] { "--device", Device, "--gpu-layers", Layers, "--flash-attn", Flash, "--cache-type-k", Cache, "--cache-type-v", Cache });
		if (FullOffload && Layers == "all")
		{
			add(new string[2] { "--fit", "off" });
		}
		if (UseMmap)
		{
			add(new string[4] { "--load-mode", "mmap", "--lazy-mode", "off" });
		}
		else if (FullOffload && !CpuOnly)
		{
			// The whole model lives on the GPU: read the file once instead of keeping it mapped, which tied
			// up about 17 GB of RAM for nothing (measured on the laptop; VRAM and speed unchanged). No mlock.
			add(new string[2] { "--load-mode", "none" });
		}
		if (ExpertCpuLayers > 0)
		{
			add(new string[2]
			{
				"--n-cpu-moe",
				ExpertCpuLayers.ToString(CultureInfo.InvariantCulture)
			});
		}
	}
}
