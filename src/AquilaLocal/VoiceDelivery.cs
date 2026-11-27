using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AquilaLocal;

public static class VoiceDelivery
{
	public const int MaxTags = 16;

	private static readonly string[] Events = new string[10] { "gasp", "giggle", "growl", "gulp", "laugh", "pause", "scoff", "sigh", "sob", "um" };

	private static readonly Regex NativeEvent = new Regex("<(" + string.Join('|', Events) + ")>", RegexOptions.IgnoreCase);

	public static bool IsMood(string name)
	{
		return VoiceOptions.Moods.Contains(name);
	}

	public static bool IsEvent(string name)
	{
		return Events.Contains(name);
	}

	public static bool UsePassageTags(VoiceOptions voice)
	{
		if (voice.AutoTags)
		{
			return !voice.UsesWindows;
		}
		return false;
	}

	public static bool UseOpeningCue(VoiceOptions voice)
	{
		if (!voice.UsesWindows)
		{
			if (!(voice.Mood == "Auto"))
			{
				if (voice.UsesKitten)
				{
					return !string.IsNullOrWhiteSpace(voice.Direction);
				}
				return false;
			}
			return true;
		}
		return false;
	}

	public static string Prompt(VoiceOptions voice)
	{
		bool flag = UsePassageTags(voice);
		if (!flag && !UseOpeningCue(voice))
		{
			return "";
		}
		StringBuilder stringBuilder = new StringBuilder();
		if (flag)
		{
			stringBuilder.Append("\nThe chat app supports hidden voice tags in final prose. Add an optional [[voice:MOOD]] at the start of a sentence or paragraph when its delivery should change; that mood applies until another tag. Use tags sparingly or as the user requests. Valid moods: ").Append(string.Join(", ", VoiceOptions.Moods)).Append(". Example: [[voice:excited]] We did it! [[voice:gentle]] Take a minute to breathe.");
			if (voice.UsesKitten)
			{
				stringBuilder.Append(" You may add inline cues at natural pauses, for example: That was close. [[voice:laugh]] Let's try again. Valid inline cues: ").Append(string.Join(", ", Events)).Append('.');
			}
			stringBuilder.Append(" Use at most 12 tags. Never place tags inside code, links, tool calls or structured data. Do not describe the tags or change the written wording to explain delivery.");
		}
		else
		{
			stringBuilder.Append("\nVoice output is enabled. Start each final natural-language answer with exactly one delivery cue: [[tone:neutral]], [[tone:warm]], [[tone:cheerful]], [[tone:gentle]], [[tone:serious]] or [[tone:concerned]]. Then write the answer normally. The app removes this cue; do not explain it or put directions in the spoken text. Choose based on the current conversation: gentle for reassurance, concerned for distress, cheerful for good news, serious for careful factual guidance, warm for friendly conversation, neutral otherwise. Keep expression proportionate. Tool-call JSON must remain unchanged.");
			if (voice.UsesKitten)
			{
				stringBuilder.Append(" For Kitten you may also use one of: ").Append(string.Join(", ", VoiceOptions.Moods.Skip(6))).Append(" (same [[tone:MOOD]] syntax).");
			}
		}
		if (!string.IsNullOrWhiteSpace(voice.Direction))
		{
			stringBuilder.Append("\nUser's delivery direction, apply only to the speech tags: ").Append(voice.Direction.Trim());
		}
		return stringBuilder.ToString();
	}

	public static string Display(Turn turn, bool reveal)
	{
		if (reveal)
		{
			List<VoiceTag> voiceTags = turn.VoiceTags;
			if (voiceTags != null && voiceTags.Count > 0)
			{
				return Insert(turn, turn.Answer, (VoiceTag tag) => (!tag.Opening) ? (" [[voice:" + tag.Name + "]] ") : ("[[tone:" + tag.Name + "]] "));
			}
		}
		return turn.Answer;
	}

	private static string Insert(Turn turn, string answer, Func<VoiceTag, string> render)
	{
		StringBuilder stringBuilder = new StringBuilder(answer.Length + 128);
		int num = 0;
		foreach (VoiceTag item in from tag in turn.VoiceTags.Where((VoiceTag tag) => (object)tag != null && tag.Offset >= 0 && tag.Offset <= answer.Length && (IsMood(tag.Name) || (!tag.Opening && IsEvent(tag.Name)))).Take(16)
			orderby tag.Offset
			select tag)
		{
			stringBuilder.Append(answer.AsSpan(num, item.Offset - num));
			stringBuilder.Append(render(item));
			num = item.Offset;
		}
		stringBuilder.Append(answer.AsSpan(num));
		return stringBuilder.ToString();
	}

	public static string AvatarMood(string mood)
	{
		switch (mood)
		{
		case "joyful":
		case "excited":
		case "surprised":
			return "cheerful";
		case "nervous":
		case "sad":
			return "concerned";
		case "tender":
			return "gentle";
		case "angry":
		case "stern":
		case "contemplative":
			return "serious";
		case "mundane":
			return "neutral";
		default:
			return IsMood(mood) ? mood : "neutral";
		}
	}

	private static string BaseMood(Turn turn, VoiceOptions options)
	{
		string text = ((options.Mood == "Auto" || (options.UsesKitten && !string.IsNullOrWhiteSpace(options.Direction) && turn.SpeechMoodSource == "model")) ? turn.SpeechMood : options.Mood);
		if (options.UsesKitten && turn.SpeechMoodSource != "model")
		{
			Match match = Regex.Match(options.Direction, "\\[([a-z]+)\\]", RegexOptions.IgnoreCase);
			if (match.Success && IsMood(match.Groups[1].Value.ToLowerInvariant()))
			{
				return match.Groups[1].Value.ToLowerInvariant();
			}
		}
		if (!IsMood(text))
		{
			return "neutral";
		}
		return text;
	}

	public static IReadOnlyList<SpeechSegment> Plan(Turn turn, VoiceOptions options)
	{
		string mood = BaseMood(turn, options);
		// Only speech is read aloud: thoughts and actions become blanks of the same length, so voice cue offsets still line up.
		string answer = AquilaLocal.Companion.ThoughtMarkup.MaskForSpeech(AquilaLocal.Companion.Sounds.SoundCues.MaskForSpeech(AquilaLocal.Companion.Living.MoodMarkup.MaskForSpeech(turn.Answer)));
		List<SpeechSegment> result;
		StringBuilder section;
		int left;
		if (!options.UsesWindows)
		{
			List<VoiceTag> voiceTags = turn.VoiceTags;
			if (voiceTags != null && voiceTags.Count > 0)
			{
				string marker = "Aquila" + Guid.NewGuid().ToString("N") + "_";
				string text = Insert(turn, answer, (VoiceTag tag) => (!tag.Opening) ? (marker + tag.Name + "_") : "");
				string text2 = SpokenText.Clean(options.UsesKitten ? StripNativeMarkup(text) : text);
				result = new List<SpeechSegment>();
				section = new StringBuilder();
				left = options.MaxCharacters;
				int num = 0;
				int num2;
				foreach (Match item in Regex.Matches(text2, marker + "([a-z]+)_"))
				{
					num2 = num;
					if (!Append(text2.Substring(num2, item.Index - num2)))
					{
						return result;
					}
					string value = item.Groups[1].Value;
					if (IsMood(value))
					{
						if (value != mood)
						{
							Flush();
							mood = value;
						}
					}
					else if (options.UsesKitten && IsEvent(value))
					{
						section.Append(" <").Append(value).Append("> ");
					}
					num = item.Index + item.Length;
				}
				string text3 = text2;
				num2 = num;
				if (Append(text3.Substring(num2, text3.Length - num2)))
				{
					Flush();
				}
				return result;
			}
		}
		return (from text4 in SpokenText.Segments(options.UsesKitten ? StripNativeMarkup(answer) : answer, options.MaxCharacters)
			select new SpeechSegment(text4, mood)).ToArray();
		bool Append(string text4)
		{
			if (text4.Length <= left)
			{
				section.Append(text4);
				left -= text4.Length;
				return true;
			}
			int length = ((left > 0 && left < text4.Length && char.IsHighSurrogate(text4[left - 1])) ? (left - 1) : left);
			section.Append(text4.AsSpan(0, length)).Append(" The rest is in the written reply.");
			Flush();
			return false;
		}
		void Flush()
		{
			foreach (string item2 in SpokenText.SplitClean(section.ToString()))
			{
				if (string.IsNullOrWhiteSpace(NativeEvent.Replace(item2, "")))
				{
					if (result.Count > 0)
					{
						List<SpeechSegment> list = result;
						SpeechSegment speechSegment = list[list.Count - 1];
						List<SpeechSegment> list2 = result;
						list2[list2.Count - 1] = speechSegment with
						{
							Text = speechSegment.Text + " " + item2
						};
					}
				}
				else
				{
					result.Add(new SpeechSegment(item2, mood));
				}
			}
			section.Clear();
		}
	}

	private static string StripNativeMarkup(string text)
	{
		text = NativeEvent.Replace(text, "$1");
		text = Regex.Replace(text, "\\[([a-z]+)\\]", (Match match) => (!IsMood(match.Groups[1].Value.ToLowerInvariant())) ? match.Value : match.Groups[1].Value, RegexOptions.IgnoreCase);
		return text.Replace("(((", "(").Replace(")))", ")");
	}
}
