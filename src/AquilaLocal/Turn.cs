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

	/// <summary>Why she spoke without a message from you (a new chat opening, a picture you had her draw).
	/// It's her stage note for that turn, kept in the turn's context so later replies know what happened.</summary>
	public string Cue { get; set; } = "";

	/// <summary>Companion chats: how she felt after this reply (from her hidden mood tag): one word, 1 to 10, and why.</summary>
	public string Mood { get; set; } = "";

	public double MoodStrength { get; set; }

	public string MoodWhy { get; set; } = "";

	/// <summary>The parts of her inner world this reply touched (from her mood tag), so themes can rest when they keep coming back.</summary>
	public List<string> About { get; set; } = new List<string>();

	/// <summary>Her quick read on how you felt and what you wanted, when she wrote this reply.</summary>
	public string Reading { get; set; } = "";

	/// <summary>You said this aloud (it was transcribed from the microphone) rather than typed it.</summary>
	public bool Spoken { get; set; }

	/// <summary>The companion's emoji reaction to this message, if she gave one.</summary>
	public string Reaction { get; set; } = "";

	/// <summary>Your emoji reaction to her reply, if you gave one (she's told about it with your next message).</summary>
	public string UserReaction { get; set; } = "";

	/// <summary>When her reply finished.</summary>
	public DateTimeOffset? AnsweredAt { get; set; }

	/// <summary>When you saw her reply (Aquila was in front with this chat open).</summary>
	public DateTimeOffset? ReadAt { get; set; }

	/// <summary>She wrote this on her own because you'd read her last message and not replied.</summary>
	public bool FollowUp { get; set; }

	/// <summary>How many times she's considered following up on this reply, and whether she's let it rest.</summary>
	public int FollowUpChecks { get; set; }

	public bool FollowUpSettled { get; set; }

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

	/// <summary>How much of the context window the conversation filled after this reply (the last request's input plus output).</summary>
	public long ContextTokens { get; set; }

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
