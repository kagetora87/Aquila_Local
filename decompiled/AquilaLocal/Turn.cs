using System;
using System.Collections.Generic;

namespace AquilaLocal;

public class Turn
{
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
