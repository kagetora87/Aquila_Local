using System;
using System.Text.Json;

namespace AquilaAvatarProtocol;

public sealed class AvatarMessage
{
	public static readonly string[] States = new string[5] { "idle", "listening", "thinking", "speaking", "interrupted" };

	public static readonly string[] Cues = new string[3] { "none", "nod", "smile" };

	public int Version { get; set; } = 1;


	public string Session { get; set; } = "";


	public long Sequence { get; set; }

	public string Type { get; set; } = "state";


	public string State { get; set; } = "idle";


	public string Utterance { get; set; } = "";


	public string Cue { get; set; } = "none";


	public bool Preview { get; set; }

	public float Mouth { get; set; }

	public string Emotion { get; set; } = "neutral";


	public AvatarPresentation? Presentation { get; set; }

	public string Detail { get; set; } = "";


	public bool Valid(string session)
	{
		int num;
		if (Version == 1 && float.IsFinite(Mouth))
		{
			float mouth = Mouth;
			num = ((mouth >= 0f && mouth <= 1f) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		bool flag = (byte)num != 0;
		if (flag)
		{
			bool flag2;
			switch (Emotion)
			{
			case "neutral":
			case "warm":
			case "cheerful":
			case "gentle":
			case "serious":
			case "concerned":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		bool flag3 = flag && Session == session && Sequence >= 0 && Utterance != null && Detail != null && Utterance.Length <= 80 && Detail.Length <= 500 && Array.IndexOf(States, State) >= 0 && Array.IndexOf(Cues, Cue) >= 0;
		if (flag3)
		{
			bool flag2;
			switch (Type)
			{
			case "hello":
			case "ready":
			case "state":
			case "applied":
			case "shutdown":
			case "error":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag3 = flag2;
		}
		if (flag3)
		{
			return Presentation?.Valid() ?? true;
		}
		return false;
	}

	public byte[] Encode()
	{
		return JsonSerializer.SerializeToUtf8Bytes(this);
	}
}
