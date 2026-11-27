using System;
using System.Collections.Generic;
using System.Linq;

namespace AquilaLocal;

public class ChatSession
{
	public ContextCheckpoint? CompactedContext { get; set; }

	/// <summary>Where the story is right now in this chat ("her apartment, living room"). Set by you or by her; empty until someone sets it.</summary>
	public string Location { get; set; } = "";

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

	/// <summary>The person named this chat themselves, so it's never renamed from the first message.</summary>
	public bool Renamed { get; set; }

	public DateTimeOffset Updated { get; set; } = DateTimeOffset.Now;

	public List<Turn> Turns { get; set; } = new List<Turn>();

	/// <summary>Companion persona this chat belongs to ("" = normal Aquila chat). Fixed for the chat's life, so
	/// one persona's conversation can never be continued by another.</summary>
	public string PersonaId { get; set; } = "";

	/// <summary>The About you picture the companion was last shown in this chat, so a new one is shown once.</summary>
	public string AboutYouPictureSeen { get; set; } = "";

	public string PersonaName { get; set; } = "";

	/// <summary>A voice call with this persona (started with "Aquila, call ..."): her replies are always spoken.</summary>
	public bool IsCall { get; set; }

	[System.Text.Json.Serialization.JsonIgnore]
	public bool IsCompanion => !string.IsNullOrEmpty(PersonaId);

	[System.Text.Json.Serialization.JsonIgnore]
	public string Speaker => IsCompanion ? PersonaName : "Aquila";

	public string Subtitle => (IsCompanion ? PersonaName + " · " : "") + Updated.LocalDateTime.ToString("MMM d · h:mm tt");
}
