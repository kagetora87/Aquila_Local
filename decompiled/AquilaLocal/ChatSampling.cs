using System.Text.Json.Nodes;

namespace AquilaLocal;

public static class ChatSampling
{
	public static void Apply(JsonObject payload, Settings settings, bool supportsDry)
	{
		InferencePreferences inference = settings.Inference;
		if (inference == null || !inference.Creative)
		{
			payload["temperature"] = settings.Temperature;
			payload["top_p"] = (settings.Thinking ? 0.95 : 0.8);
			payload["top_k"] = 20;
			payload["min_p"] = 0;
			payload["repeat_penalty"] = 1;
			return;
		}
		payload["temperature"] = inference.CreativeTemperature;
		payload["top_p"] = inference.TopP;
		payload["top_k"] = 0;
		payload["min_p"] = inference.MinP;
		payload["repeat_penalty"] = ((supportsDry || inference.DryMultiplier == 0.0) ? 1.0 : 1.05);
		if (supportsDry)
		{
			payload["dry_multiplier"] = inference.DryMultiplier;
			payload["dry_base"] = inference.DryBase;
			payload["dry_allowed_length"] = inference.DryAllowedLength;
			payload["dry_penalty_last_n"] = 4096;
		}
	}
}
