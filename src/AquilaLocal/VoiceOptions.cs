using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace AquilaLocal;

public sealed class VoiceOptions
{
	public const string KittenPrefix = "Kitten: ";

	public const string ClonePrefix = "Clone: ";

	public const string DefaultSpeaker = "Kitten: Luna";

	public static readonly string[] Speakers = new string[9] { "Ryan", "Aiden", "Vivian", "Serena", "Uncle_Fu", "Dylan", "Eric", "Ono_Anna", "Sohee" };

	public static readonly string[] KittenSpeakers = new string[8] { "Bella", "Jasper", "Luna", "Bruno", "Rosie", "Hugo", "Kiki", "Leo" };

	public static readonly string[] Moods = new string[16]
	{
		"neutral", "warm", "cheerful", "gentle", "serious", "concerned", "angry", "contemplative", "excited", "joyful",
		"mundane", "nervous", "sad", "stern", "surprised", "tender"
	};

	public bool Enabled { get; set; }

	public bool AutoTags { get; set; }

	public bool RevealTags { get; set; }

	public string Speaker { get; set; } = "Kitten: Luna";

	public string Device { get; set; } = "auto";

	public string Mood { get; set; } = "Auto";

	public string Pace { get; set; } = "Natural";

	public string Direction { get; set; } = "";

	public double Intensity { get; set; } = 0.5;

	public float Volume { get; set; } = 0.75f;

	public bool KeepLoaded { get; set; }

	public int MaxCharacters { get; set; } = 6000;

	[JsonIgnore]
	public bool UsesKitten
	{
		get
		{
			if (!Speaker.StartsWith("Kitten: ", StringComparison.Ordinal))
			{
				return CloneId != null;
			}
			return true;
		}
	}

	[JsonIgnore]
	public string? CloneId
	{
		get
		{
			if (Speaker.StartsWith("Clone: ", StringComparison.Ordinal))
			{
				string speaker = Speaker;
				int length = "Clone: ".Length;
				if (Guid.TryParseExact(speaker.Substring(length, speaker.Length - length), "N", out var result))
				{
					return result.ToString("N");
				}
			}
			return null;
		}
	}

	[JsonIgnore]
	public bool UsesWindows => Speaker.StartsWith("Windows: ", StringComparison.Ordinal);

	public void Validate()
	{
		if (Speaker == null)
		{
			goto IL_0086;
		}
		if (!Speakers.Contains(Speaker))
		{
			if (Speaker.StartsWith("Kitten: ", StringComparison.Ordinal))
			{
				string[] kittenSpeakers = KittenSpeakers;
				string speaker = Speaker;
				int length = "Kitten: ".Length;
				if (kittenSpeakers.Contains(speaker.Substring(length, speaker.Length - length)))
				{
					goto IL_0091;
				}
			}
			if (CloneId == null && (!Speaker.StartsWith("Windows: ") || Speaker.Length > 150))
			{
				goto IL_0086;
			}
		}
		goto IL_0091;
		IL_0086:
		throw new ArgumentException("Choose an available voice.");
		IL_0091:
		bool flag;
		switch (Device)
		{
		case "auto":
		case "cpu":
		case "gpu":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		bool flag2 = !flag || (Mood != "Auto" && !Moods.Contains(Mood));
		bool flag3;
		if (!flag2)
		{
			switch (Pace)
			{
			case "Slow":
			case "Natural":
			case "Lively":
				flag3 = true;
				break;
			default:
				flag3 = false;
				break;
			}
			flag2 = !flag3;
		}
		if (flag2)
		{
			throw new ArgumentException("Invalid voice delivery settings.");
		}
		flag2 = Direction == null || Direction.Length > 300 || !double.IsFinite(Intensity);
		if (!flag2)
		{
			double intensity = Intensity;
			bool flag4 = ((intensity < 0.0 || intensity > 1.0) ? true : false);
			flag2 = flag4;
		}
		flag = flag2 || !float.IsFinite(Volume);
		if (!flag)
		{
			float volume = Volume;
			bool flag4 = ((volume < 0f || volume > 1f) ? true : false);
			flag = flag4;
		}
		flag3 = flag;
		if (!flag3)
		{
			int length = MaxCharacters;
			bool flag4 = ((length < 500 || length > 12000) ? true : false);
			flag3 = flag4;
		}
		if (flag3)
		{
			throw new ArgumentException("Voice settings are outside their supported range.");
		}
	}

	public string Instruction(string mood)
	{
		string text;
		switch (mood)
		{
		case "warm":
			text = "Use a friendly, warm and welcoming tone. ";
			break;
		case "cheerful":
			text = "Sound pleased and lightly enthusiastic, without shouting. ";
			break;
		case "gentle":
			text = "Speak softly and reassuringly, with patient, compassionate phrasing. ";
			break;
		case "serious":
			text = "Use a calm, focused and serious tone, without sounding angry. ";
			break;
		case "concerned":
			text = "Sound sincerely concerned and supportive, without melodrama. ";
			break;
		case "joyful":
		case "tender":
		case "nervous":
		case "excited":
		case "mundane":
		case "surprised":
		case "angry":
		case "stern":
		case "contemplative":
		case "sad":
			text = "Use a " + mood + " delivery, with natural expression. ";
			break;
		default:
			text = "Use a relaxed, matter-of-fact conversational tone. ";
			break;
		}
		return "Speak English clearly, like a natural conversation. Keep the selected speaker's identity. " + text + ((Intensity < 0.35) ? "Keep the emotion very subtle. " : ((Intensity > 0.7) ? "Make the intended emotion clearly audible while staying natural. " : "Use restrained, natural expression. ")) + ((Pace == "Slow") ? "Speak a little more slowly, with comfortable pauses. " : ((Pace == "Lively") ? "Use a lively, slightly quicker pace without rushing. " : "Use a comfortable conversational pace. ")) + Direction;
	}
}
