using System;
using System.Collections.Generic;
using System.Linq;

namespace AquilaLocal;

public class ChatSession
{
	public ContextCheckpoint? CompactedContext { get; set; }

	/// <summary>Number of oldest active turns left out of the prompt to fit the context window. Kept
	/// between turns (and only ever grown in large steps) so the start of the prompt stays identical
	/// and the engine's prompt cache keeps working on long chats.</summary>
	public int TrimmedTurns { get; set; }

	/// <summary>The compaction checkpoint TrimmedTurns was measured against.</summary>
	public string TrimBase { get; set; } = "";

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

	/// <summary>Companion persona this chat belongs to ("" = normal Aquila chat). Fixed for the chat's life, so
	/// one persona's conversation can never be continued by another.</summary>
	public string PersonaId { get; set; } = "";

	public string PersonaName { get; set; } = "";

	[System.Text.Json.Serialization.JsonIgnore]
	public bool IsCompanion => !string.IsNullOrEmpty(PersonaId);

	[System.Text.Json.Serialization.JsonIgnore]
	public string Speaker => IsCompanion ? PersonaName : "Aquila";

	public string Subtitle => (IsCompanion ? PersonaName + " · " : "") + Updated.LocalDateTime.ToString("MMM d · h:mm tt");
}
