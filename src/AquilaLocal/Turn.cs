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

	/// <summary>Wardrobe diary entries she wrote during this turn (through change_outfit).</summary>
	public List<long> CompanionWardrobeIds { get; set; } = new List<long>();

	/// <summary>Where she moved the story during this turn (through change_location), or empty.</summary>
	public string MovedTo { get; set; } = "";

	/// <summary>Living mode: she wrote this message to you first, on her own (there's no message from you).</summary>
	public bool Unprompted { get; set; }

	/// <summary>You said this aloud (it was transcribed from the microphone) rather than typed it.</summary>
	public bool Spoken { get; set; }

	/// <summary>The companion's emoji reaction to this message, if she gave one.</summary>
	public string Reaction { get; set; } = "";

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

	/// <summary>Pictures drawn for this reply (files in Data\pictures).</summary>
	public List<string> Pictures { get; set; } = new List<string>();

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
