using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace AquilaLocal;

internal static class PromptWindow
{
	internal static async Task<FittedPrompt> FitAsync(JsonArray messages, int oldPairs, int budget, Func<JsonArray, Task<int>> count)
	{
		if (await count(messages) <= budget)
		{
			return new FittedPrompt(messages, 0);
		}
		// Over budget: trim down to about 70% of the budget rather than the bare minimum. The cut point
		// then stays put for many turns, so the prompt cache survives instead of missing every turn.
		try
		{
			return await FitToAsync(messages, oldPairs, budget * 7 / 10, count);
		}
		catch (ArgumentException)
		{
			return await FitToAsync(messages, oldPairs, budget, count);
		}
	}

	private static async Task<FittedPrompt> FitToAsync(JsonArray messages, int oldPairs, int budget, Func<JsonArray, Task<int>> count)
	{
		int low = 0;
		int high = Math.Min(1, oldPairs);
		JsonArray fitted = null;
		while (high > low)
		{
			JsonArray candidate = WithoutPrefix(messages, high);
			if (await count(candidate) <= budget)
			{
				fitted = candidate;
				break;
			}
			low = high;
			high = Math.Min(oldPairs, high * 2);
		}
		if (fitted == null)
		{
			throw new ArgumentException("The system prompt and current request or tool results exceed the context window even after omitting older turns. Increase context, lower the response limit, or shorten the current input.");
		}
		while (high - low > 1)
		{
			int mid = low + (high - low) / 2;
			JsonArray candidate = WithoutPrefix(messages, mid);
			if (await count(candidate) <= budget)
			{
				high = mid;
				fitted = candidate;
			}
			else
			{
				low = mid;
			}
		}
		return new FittedPrompt(fitted, high);
	}

	private static JsonArray WithoutPrefix(JsonArray messages, int pairs)
	{
		JsonArray jsonArray = new JsonArray(messages[0]?.DeepClone());
		for (int i = 1 + pairs * 2; i < messages.Count; i++)
		{
			jsonArray.Add(messages[i]?.DeepClone());
		}
		return jsonArray;
	}
}
