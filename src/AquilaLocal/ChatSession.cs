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

	/// <summary>Finds the picture for a chat's row (her profile picture, or Aquila's crest). Set once by the main window.</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public static Func<ChatSession, System.Windows.Media.ImageSource?>? PictureFor { get; set; }

	[System.Text.Json.Serialization.JsonIgnore]
	public System.Windows.Media.ImageSource? Picture => PictureFor?.Invoke(this);

	[System.Text.Json.Serialization.JsonIgnore]
	public string Initial => Speaker.Length > 0 ? char.ToUpperInvariant(Speaker[0]).ToString() : "?";

	/// <summary>"Today", "Yesterday", "Tuesday" (this week) or "Mar 4".</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public string When
	{
		get
		{
			DateTime day = Updated.LocalDateTime.Date;
			DateTime today = DateTime.Today;
			if (day >= today)
			{
				return "Today";
			}
			if (day == today.AddDays(-1))
			{
				return "Yesterday";
			}
			if (day > today.AddDays(-7))
			{
				return day.ToString("dddd", System.Globalization.CultureInfo.CurrentCulture);
			}
			return day.Year == today.Year ? day.ToString("MMM d", System.Globalization.CultureInfo.CurrentCulture) : day.ToString("MMM d, yyyy", System.Globalization.CultureInfo.CurrentCulture);
		}
	}

	/// <summary>The page she starts this chat with: what she carried over from an earlier chat ("" for none).</summary>
	public string CarryOver { get; set; } = "";

	/// <summary>The title of the chat it was carried over from.</summary>
	public string CarriedFrom { get; set; } = "";

	/// <summary>Put away: hidden from the chat list unless archived chats are shown. Nothing is deleted.</summary>
	public bool Archived { get; set; }

	/// <summary>Replies answered after this count toward unread markers (set once by the main window).</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public static DateTimeOffset UnreadSince { get; set; } = DateTimeOffset.MaxValue;

	/// <summary>Her replies you haven't seen yet: the newest ones, after your last message, not yet read.</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public int Unread
	{
		get
		{
			if (!IsCompanion)
			{
				return 0;
			}
			int n = 0;
			for (int i = Turns.Count - 1; i >= 0; i--)
			{
				Turn t = Turns[i];
				if (t.ReadAt != null || t.Status != "complete" || t.AnsweredAt == null || t.AnsweredAt < UnreadSince || string.IsNullOrWhiteSpace(t.Answer))
				{
					break;
				}
				n++;
			}
			return n;
		}
	}

	[System.Text.Json.Serialization.JsonIgnore]
	public bool HasUnread => Unread > 0;

	[System.Text.Json.Serialization.JsonIgnore]
	public string UnreadText => Unread > 9 ? "9+" : Unread.ToString(System.Globalization.CultureInfo.InvariantCulture);

	[System.Text.Json.Serialization.JsonIgnore]
	public string ArchiveLabel => Archived ? "Unarchive" : "Archive";

	[System.Text.Json.Serialization.JsonIgnore]
	public string Subtitle => (IsCompanion ? PersonaName + " · " : "") + Updated.LocalDateTime.ToString("MMM d · h:mm tt");
}
