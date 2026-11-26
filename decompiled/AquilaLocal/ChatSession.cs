using System;
using System.Collections.Generic;
using System.Linq;

namespace AquilaLocal;

public class ChatSession
{
	public ContextCheckpoint? CompactedContext { get; set; }

	public long CompactionPromptTokens { get; set; }

	public long CompactionCompletionTokens { get; set; }

	public bool CompactionUsageIncomplete { get; set; }

	public long PromptTokens => Turns.Sum((Turn t) => t.PromptTokens) + CompactionPromptTokens;

	public long CompletionTokens => Turns.Sum((Turn t) => t.CompletionTokens) + CompactionCompletionTokens;

	public long TotalTokens => PromptTokens + CompletionTokens;

	public bool UsageIncomplete
	{
		get
		{
			if (!CompactionUsageIncomplete)
			{
				return Turns.Any((Turn t) => t.UsageIncomplete || (t.Answer.Length > 0 && t.UsageRequests == 0));
			}
			return true;
		}
	}

	public string Id { get; set; } = Guid.NewGuid().ToString("N");


	public string Title { get; set; } = "New conversation";


	public DateTimeOffset Updated { get; set; } = DateTimeOffset.Now;


	public List<Turn> Turns { get; set; } = new List<Turn>();


	public string Subtitle => Updated.LocalDateTime.ToString("MMM d · h:mm tt");
}
