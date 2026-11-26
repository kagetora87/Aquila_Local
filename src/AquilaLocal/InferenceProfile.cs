namespace AquilaLocal;

public sealed record InferenceProfile(string Key, string Label, int Maximum, int Target)
{
	public static InferenceProfile Resolve(InferencePreferences? preferences, HardwareInventory? hardware)
	{
		string text = preferences?.Profile ?? "Custom";
		object obj;
		if (text == "Auto")
		{
			long valueOrDefault = (hardware?.Gpu?.DedicatedBytes).GetValueOrDefault();
			if (valueOrDefault < 21474836480L)
			{
				if (valueOrDefault > 0 && valueOrDefault <= 10737418240L)
				{
					long ramBytes = hardware.RamBytes;
					if (ramBytes > 0 && ramBytes <= 21474836480L)
					{
						obj = "Laptop";
						goto IL_00a1;
					}
				}
				obj = "Custom";
			}
			else
			{
				obj = "High";
			}
			goto IL_00a1;
		}
		goto IL_00a2;
		IL_00a2:
		if (!(text == "High"))
		{
			if (text == "Laptop")
			{
				return new InferenceProfile(text, "B · Constrained laptop", 24576, 16384);
			}
			return new InferenceProfile("Custom", "Custom", 262144, 8192);
		}
		return new InferenceProfile(text, "A · High VRAM", 65536, 32768);
		IL_00a1:
		text = (string)obj;
		goto IL_00a2;
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
