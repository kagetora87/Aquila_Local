using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Living;

namespace AquilaLocal;

/// <summary>
/// Her mood ("Feeling… wistful" under her status, why on hover), her dream journal button, and what her
/// thoughts do to the avatar while you're away (a matching animation, or her daydream picture in its place).
/// </summary>
public partial class MainWindow
{
	/// <summary>The persona whose daydream picture is on the avatar now, or "".</summary>
	private string daydreamOf = "";

	/// <summary>
	/// The lines for her next reply: how she feels coming in, and a dream she's still waiting to tell you about
	/// (returned too, so it's let go once she's said it).
	/// </summary>
	private (string Extra, string? ToldAbout) MoodInnerState(string personaId)
	{
		DateTimeOffset now = DateTimeOffset.Now;
		string extra;
		try
		{
			extra = MoodMarkup.TurnLine(companion.Moods.Current(personaId), now);
		}
		catch (PersonaConfigException ex)
		{
			Notice("Mood: " + ex.Message);
			extra = MoodMarkup.TurnLine(null, now);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			Notice("Mood: couldn't read " + Genders.Possessive(GenderOf(personaId)) + " mood (" + ex.Message + ").");
			extra = MoodMarkup.TurnLine(null, now);
		}
		string? told = null;
		try
		{
			ToTell? waiting = companion.ToTell.Load(personaId, now);
			if (waiting != null)
			{
				extra += "\n" + ToTellStore.Reminder(waiting, now);
				told = waiting.JournalId;
			}
		}
		catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException)
		{
			Notice("Living mode: " + ex.Message);
		}
		return (extra, told);
	}

	/// <summary>After her reply: keeps the mood she chose, and lets go of the dream she was waiting to tell you about.</summary>
	private void RecordMood(ChatSession session, Turn turn, string? toldAbout)
	{
		try
		{
			if (turn.Mood.Length > 0)
			{
				companion.Moods.Append(session.PersonaId, new MoodState(turn.Mood, turn.MoodStrength, turn.MoodWhy, DateTimeOffset.Now, MoodSources.Chat));
			}
			if (toldAbout != null && companion.ToTell.Load(session.PersonaId, DateTimeOffset.Now)?.JournalId == toldAbout)
			{
				companion.ToTell.Clear(session.PersonaId);
			}
		}
		catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException)
		{
			Notice("Mood: " + Genders.Possessive(GenderOf(session.PersonaId)) + " mood wasn't saved (" + ex.Message + ").");
		}
		ShowMood();
	}

	/// <summary>"Feeling… word" under her status, with why (and since when) on hover. Hidden outside her chats or before her first mood.</summary>
	private void ShowMood()
	{
		string personaId = (current != null && current.IsCompanion) ? current.PersonaId : "";
		MoodState? mood = null;
		if (personaId.Length > 0)
		{
			try
			{
				mood = companion.Moods.Current(personaId);
			}
			catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException)
			{
				AvatarMood.Text = "Mood unreadable";
				AvatarMood.ToolTip = ex.Message;
				AvatarMood.Visibility = Visibility.Visible;
				return;
			}
		}
		if (mood == null)
		{
			AvatarMood.Visibility = Visibility.Collapsed;
			return;
		}
		AvatarMood.Text = "Feeling… " + mood.Word;
		string from = mood.Source switch
		{
			MoodSources.Dream => "after a dream",
			MoodSources.Thought => "after a thought of " + Genders.Possessive(GenderOf(personaId)) + " own",
			_ => "in your chat"
		};
		AvatarMood.ToolTip = (mood.Why.Length > 0 ? char.ToUpperInvariant(mood.Why[0]) + mood.Why.Substring(1) : Genders.Words("{S} didn't say why.", GenderOf(personaId))) +
			"\n\nSince " + mood.At.ToLocalTime().ToString("ddd h:mm tt") + ", " + from + " · " + MoodRules.Decayed(mood, DateTimeOffset.Now).ToString("0") + "/10 now";
		AvatarMood.Visibility = Visibility.Visible;
	}

	// ---------------- Dream journal ----------------

	private void RefreshJournalButton()
	{
		bool persona = current != null && current.IsCompanion && current.PersonaId.Length > 0;
		JournalButton.Visibility = persona ? Visibility.Visible : Visibility.Collapsed;
		if (!persona)
		{
			return;
		}
		ImageSource? picture = null;
		try
		{
			picture = DreamJournal.LoadIcon(companion.LivingRoot, current!.PersonaId);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException || ex is FileFormatException)
		{
			Notice("Dream journal: her journal picture couldn't be shown (" + ex.Message + ").");
		}
		JournalPicture.Fill = picture != null ? new ImageBrush(picture) { Stretch = Stretch.UniformToFill } : null;
		JournalPicture.Visibility = picture != null ? Visibility.Visible : Visibility.Collapsed;
		JournalGlyph.Visibility = picture != null ? Visibility.Collapsed : Visibility.Visible;
		JournalButton.ToolTip = current!.PersonaName + "'s dream journal";
	}

	private void Journal_Click(object sender, RoutedEventArgs e)
	{
		if (current == null || !current.IsCompanion)
		{
			return;
		}
		string personaId = current.PersonaId;
		DreamJournalWindow window = new DreamJournalWindow(personaId, current.PersonaName, Journal, companion.LivingRoot, Path.Combine(store.Root, "pictures"), GenderOf(personaId))
		{
			Owner = this
		};
		window.IconChanged += RefreshJournalButton;
		void Changed() => window.Refresh();
		LivingChanged += Changed;
		try
		{
			window.ShowDialog();
		}
		finally
		{
			LivingChanged -= Changed;
		}
	}

	// ---------------- While you're away ----------------

	/// <summary>
	/// After a thought or dream: it can change her mood (gently), a strong dream is kept to tell you later, and
	/// her avatar shows it: her picture in the daydream slot, or else a matching animation if one fits.
	/// </summary>
	private async Task AfterThoughtAsync(string personaId, JournalEntry entry, InnerThought thought, bool asleep)
	{
		DateTimeOffset now = entry.At;
		if (thought.MoodWord.Length > 0)
		{
			MoodState next = new MoodState(thought.MoodWord, thought.MoodStrength, thought.MoodWhy, now, asleep ? MoodSources.Dream : MoodSources.Thought);
			MoodState kept = companion.Moods.Offer(personaId, next, now);
			if (kept != next)
			{
				entry.Note = (entry.Note + (Genders.IsMale(GenderOf(personaId)) ? " His" : " Her") + " mood stayed " + kept.Word + ": this feeling wasn't strong enough to change it.").Trim();
			}
		}
		if (entry.Kind == ThoughtKinds.Dream && !entry.MessagedYou && MoodRules.WorthTelling(thought.MoodStrength, thought.Urge))
		{
			double weight = thought.MoodStrength / 10.0 + thought.Urge;
			ToTell? waiting = companion.ToTell.Load(personaId, now);
			if (waiting == null || weight >= waiting.Weight)
			{
				companion.ToTell.Save(personaId, new ToTell(entry.Id, now, entry.Kind, entry.Text, thought.MessageIdea, thought.MoodWord, weight));
				entry.Note = (entry.Note + Genders.Words(" It stayed with {o}: {s}'ll tell you about it when {s} can.", GenderOf(personaId))).Trim();
			}
		}
		if (current == null || current.PersonaId != personaId || spriteView == null)
		{
			return;
		}
		if (entry.Picture.Length > 0 && File.Exists(entry.Picture))
		{
			ShowDaydream(personaId, entry.Picture, entry.Video);
			return;
		}
		if (gestures == null)
		{
			return;
		}
		GestureMatcher matcher = gestures;
		string text = entry.Text;
		GestureMatcher.GestureMatch? match = await Task.Run(() => matcher.Best(text));
		if (match != null && match.Score >= matcher.Threshold && !featuresClosing && current?.PersonaId == personaId)
		{
			spriteView.PlayGesture(match.Name);
		}
	}

	private void ShowDaydream(string personaId, string picture, string video)
	{
		if (spriteView == null)
		{
			return;
		}
		try
		{
			spriteView.ShowDaydream(picture, video.Length > 0 && File.Exists(video) ? video : "");
			daydreamOf = personaId;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException || ex is FileFormatException)
		{
			Notice("Avatar: her daydream picture couldn't be shown (" + ex.Message + ").");
		}
	}

	/// <summary>Her daydream picture leaves the avatar slot when she speaks to you, or her avatar changes.</summary>
	private void ClearDaydream()
	{
		if (daydreamOf.Length > 0)
		{
			daydreamOf = "";
			spriteView?.ClearDaydream();
		}
	}
}
