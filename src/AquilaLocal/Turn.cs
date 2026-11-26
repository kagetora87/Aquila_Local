using System;
using System.Collections.Generic;

namespace AquilaLocal;

public class Turn
{
	/// <summary>Per-turn reference context (recalled memories, date, initial web lookup). Stored so the
	/// exact same text is replayed in later requests and the engine prompt cache stays valid.</summary>
	public string Context { get; set; } = "";

	/// <summary>Companion chats: the drive/achievement block shown to the persona with this message. Kept for
	/// inspection only; it is never replayed in later requests (only the current message carries one).</summary>
	public string InnerState { get; set; } = "";

	/// <summary>Companion chats: what the satisfaction loop decided after this reply.</summary>
	public string DriveReport { get; set; } = "";

	/// <summary>Companion chats: memory ledger entries her summary pass wrote after this turn.</summary>
	public List<long> CompanionMemoryIds { get; set; } = new List<long>();

	public string SpeechMood { get; set; } = "neutral";

	public string SpeechMoodSource { get; set; } = "default";

	public List<VoiceTag> VoiceTags { get; set; } = new List<VoiceTag>();

	public string SpeechVoice { get; set; } = "";

	public string SpeechStatus { get; set; } = "";

	public List<WebSource> WebSources { get; set; } = new List<WebSource>();

	public bool ThinkingEnabled { get; set; }

	public int ThinkingTokenBudget { get; set; }

	public string PersonalityId { get; set; } = "default";

	public string PersonalityName { get; set; } = "";

	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public List<string> UsedMemoryIds { get; set; } = new List<string>();

	public List<string> LearnedMemoryIds { get; set; } = new List<string>();

	public long MemoryPromptTokens { get; set; }

	public long MemoryCompletionTokens { get; set; }

	public string MemoryLearningStatus { get; set; } = "";

	public List<Attachment> Attachments { get; set; } = new List<Attachment>();

	public long PromptTokens { get; set; }

	public long CompletionTokens { get; set; }

	public int UsageRequests { get; set; }

	public bool UsageIncomplete { get; set; }

	public string User { get; set; } = "";

	public string Answer { get; set; } = "";

	public string Reasoning { get; set; } = "";

	public string Status { get; set; } = "complete";

	public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;

	public List<string> Activity { get; set; } = new List<string>();

	public string Metrics { get; set; } = "";
}
