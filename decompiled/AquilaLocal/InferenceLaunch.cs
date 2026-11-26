using System;
using System.Globalization;

namespace AquilaLocal;

public sealed record InferenceLaunch(string Device, string Layers, string Cache, int ExpertCpuLayers, bool UseMmap, bool FullOffload)
{
	public bool CpuOnly => Device == "none";

	public string Flash
	{
		get
		{
			if (!(Cache == "f16"))
			{
				return "on";
			}
			return "auto";
		}
	}

	public static InferenceLaunch Resolve(Settings settings, ModelInfo model)
	{
		InferenceProfile inferenceProfile = InferenceProfile.Resolve(settings.Inference, settings.Hardware);
		string key = inferenceProfile.Key;
		string text = ((key == "High") ? "q8_0" : ((!(key == "Laptop")) ? (settings.Inference?.CustomCache ?? "f16") : (settings.Inference.LaptopQ4 ? "q4_0" : "q8_0")));
		string cache = text;
		bool flag = inferenceProfile.Key != "Custom";
		int num;
		if (!flag)
		{
			if (settings.GpuLayers == 0)
			{
				num = 0;
				goto IL_00ff;
			}
			if ((object)settings.Hardware == null)
			{
				num = 1;
				goto IL_00d6;
			}
			num = (((object)settings.Hardware.Gpu != null) ? 1 : 0);
		}
		else
		{
			num = (((object)settings.Hardware?.Gpu != null) ? 1 : 0);
		}
		if (num != 0)
		{
			goto IL_00d6;
		}
		goto IL_00ff;
		IL_00ff:
		object obj = "none";
		goto IL_0104;
		IL_0104:
		string device = (string)obj;
		int num2 = ((num != 0 && inferenceProfile.Key != "High" && model.ExpertCount > 1) ? Math.Clamp(settings.Inference?.ExpertCpuLayers ?? 0, 0, model.BlockCount) : 0);
		bool flag2 = num != 0 && (inferenceProfile.Key == "High" || num2 > 0);
		string layers = ((num == 0) ? "0" : (flag2 ? "all" : ((flag || settings.GpuLayers < 0) ? "auto" : settings.GpuLayers.ToString(CultureInfo.InvariantCulture))));
		return new InferenceLaunch(device, layers, cache, num2, inferenceProfile.Key == "Laptop" || num2 > 0, flag2);
		IL_00d6:
		obj = settings.Hardware?.Gpu?.Device ?? "Vulkan0";
		goto IL_0104;
	}

	public void AddArguments(Action<string[]> add)
	{
		add(new string[10] { "--device", Device, "--gpu-layers", Layers, "--flash-attn", Flash, "--cache-type-k", Cache, "--cache-type-v", Cache });
		if (FullOffload)
		{
			add(new string[2] { "--fit", "off" });
		}
		if (UseMmap)
		{
			add(new string[4] { "--load-mode", "mmap", "--lazy-mode", "off" });
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
