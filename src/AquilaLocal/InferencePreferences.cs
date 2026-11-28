using System;
using System.Collections.Generic;
using System.IO;

namespace AquilaLocal;

public sealed class InferencePreferences
{
	public string Profile { get; set; } = "Auto";

	public int HighContext { get; set; } = 32768;

	public int LaptopContext { get; set; } = 16384;

	public int CustomContext { get; set; } = 8192;

	/// <summary>In the manual profiles: switching models sets the context to the one you last chose for that
	/// model, or for a model you haven't tuned, the largest that fits on the graphics card. On by default.</summary>
	public bool AutoContextPerModel { get; set; } = true;

	/// <summary>Your context choice for each model, by file name (lower case).</summary>
	public Dictionary<string, int> ModelContexts { get; set; } = new Dictionary<string, int>();

	public static string ModelKey(string modelPath) => Path.GetFileName(modelPath ?? "").ToLowerInvariant();

	/// <summary>What you chose for this model, if anything sensible.</summary>
	public int? ContextFor(string modelPath) =>
		ModelContexts != null && ModelContexts.TryGetValue(ModelKey(modelPath), out int v) && v >= 2048 && v <= 262144 ? v : null;

	public bool LaptopQ4 { get; set; }

	public string CustomCache { get; set; } = "f16";

	public int ExpertCpuLayers { get; set; }

	public bool Creative { get; set; } = true;

	public double CreativeTemperature { get; set; } = 0.95;

	public double TopP { get; set; } = 1.0;

	public double MinP { get; set; } = 0.06;

	public double DryMultiplier { get; set; } = 0.8;

	public double DryBase { get; set; } = 1.75;

	public int DryAllowedLength { get; set; } = 2;

	public void Validate()
	{
		bool flag;
		switch (Profile)
		{
		case "Auto":
		case "High":
		case "Laptop":
		case "Custom":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		bool flag2 = !flag;
		if (!flag2)
		{
			int highContext = HighContext;
			bool flag3 = ((highContext < 2048 || highContext > 262144) ? true : false);
			flag2 = flag3;
		}
		bool flag4 = flag2;
		if (!flag4)
		{
			int highContext = LaptopContext;
			bool flag3 = ((highContext < 2048 || highContext > 24576) ? true : false);
			flag4 = flag3;
		}
		bool flag5 = flag4;
		if (!flag5)
		{
			int highContext = CustomContext;
			bool flag3 = ((highContext < 2048 || highContext > 262144) ? true : false);
			flag5 = flag3;
		}
		bool flag6 = flag5;
		if (!flag6)
		{
			bool flag3;
			switch (CustomCache)
			{
			case "f16":
			case "q8_0":
			case "q4_0":
				flag3 = true;
				break;
			default:
				flag3 = false;
				break;
			}
			flag6 = !flag3;
		}
		bool flag7 = flag6;
		if (!flag7)
		{
			int highContext = ExpertCpuLayers;
			bool flag3 = ((highContext < 0 || highContext > 999) ? true : false);
			flag7 = flag3;
		}
		if (flag7)
		{
			throw new ArgumentException("Invalid inference profile, context, cache, or MoE settings.");
		}
		flag7 = !In(CreativeTemperature, 0.0, 2.0) || !In(TopP, 0.01, 1.0) || !In(MinP, 0.0, 1.0) || !In(DryMultiplier, 0.0, 5.0) || !In(DryBase, 1.0, 4.0);
		if (!flag7)
		{
			int highContext = DryAllowedLength;
			flag6 = ((highContext < 1 || highContext > 32) ? true : false);
			flag7 = flag6;
		}
		if (flag7)
		{
			throw new ArgumentException("Invalid creative sampler settings.");
		}
		static bool In(double value, double min, double max)
		{
			if (double.IsFinite(value) && value >= min)
			{
				return value <= max;
			}
			return false;
		}
	}
}
