namespace AquilaLocal;

public sealed record InferenceProfile(string Key, string Label, int Maximum, int Target)
{
	public static InferenceProfile Resolve(InferencePreferences? preferences, HardwareInventory? hardware)
	{
		// "Auto" is now a real mode (see InferenceLaunch.PlanAuto): it sizes the context to the
		// graphics card instead of mapping onto one of the fixed profiles.
		return (preferences?.Profile ?? "Auto") switch
		{
			"Auto" => new InferenceProfile("Auto", "Automatic (recommended)", 262144, 32768),
			"High" => new InferenceProfile("High", "A · High VRAM", 262144, 32768),
			"Laptop" => new InferenceProfile("Laptop", "B · Constrained laptop", 24576, 16384),
			_ => new InferenceProfile("Custom", "Custom", 262144, 8192),
		};
	}

	public int Requested(InferencePreferences options)
	{
		string key = Key;
		if (!(key == "High"))
		{
			if (key == "Laptop")
			{
				return options.LaptopContext;
			}
			return options.CustomContext;
		}
		return options.HighContext;
	}

	public void Remember(InferencePreferences options, int context)
	{
		string key = Key;
		if (!(key == "High"))
		{
			if (key == "Laptop")
			{
				options.LaptopContext = context;
			}
			else
			{
				options.CustomContext = context;
			}
		}
		else
		{
			options.HighContext = context;
		}
	}
}
