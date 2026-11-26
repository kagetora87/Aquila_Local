using System;
using System.Text;

namespace AquilaLocal;

public sealed class VoiceCueDecoder(Turn turn, bool passageTags = false, bool events = false)
{
	private const string Tone = "[[tone:";

	private const string Voice = "[[voice:";

	private const int TagLimit = 48;

	private string pending = "";

	private int ticks;

	private int codeTicks;

	private bool trimAfterCue;

	private bool trimLineBreaks;

	public string Push(string fragment)
	{
		string text = pending + fragment;
		pending = "";
		StringBuilder stringBuilder = new StringBuilder(text.Length);
		int num = 0;
		while (num < text.Length)
		{
			char c = text[num];
			if (c == '`')
			{
				trimAfterCue = false;
				ticks++;
				stringBuilder.Append(c);
				num++;
				continue;
			}
			if (ticks > 0)
			{
				if (codeTicks == 0)
				{
					codeTicks = ticks;
				}
				else if (ticks == codeTicks || (codeTicks >= 3 && ticks >= codeTicks))
				{
					codeTicks = 0;
				}
				ticks = 0;
			}
			bool flag = trimAfterCue;
			if (flag)
			{
				bool flag2;
				if (trimLineBreaks)
				{
					flag2 = char.IsWhiteSpace(c);
				}
				else
				{
					bool flag3 = ((c == '\t' || c == ' ') ? true : false);
					flag2 = flag3;
				}
				flag = flag2;
			}
			if (flag)
			{
				num++;
				continue;
			}
			trimAfterCue = false;
			if (c == '[' && codeTicks == 0)
			{
				ReadOnlySpan<char> readOnlySpan = text.AsSpan(num);
				bool flag4 = readOnlySpan.StartsWith("[[tone:".AsSpan(), StringComparison.OrdinalIgnoreCase);
				if (flag4 || readOnlySpan.StartsWith("[[voice:".AsSpan(), StringComparison.OrdinalIgnoreCase))
				{
					int num2 = (flag4 ? "[[tone:".Length : "[[voice:".Length);
					int num3 = Math.Min(text.Length - num - num2, 48);
					int num4 = text.IndexOf("]]", num + num2, num3, StringComparison.Ordinal);
					int num5;
					if (num4 < 0)
					{
						if (num3 < 48)
						{
							string text2 = text;
							num5 = num;
							pending = text2.Substring(num5, text2.Length - num5);
							break;
						}
						num += num2 + num3;
						continue;
					}
					num5 = num + num2;
					string text3 = text.Substring(num5, num4 - num5).Trim().ToLowerInvariant();
					bool flag5 = VoiceDelivery.IsMood(text3);
					if (flag4 && flag5 && turn.SpeechMoodSource != "model")
					{
						turn.SpeechMood = text3;
						turn.SpeechMoodSource = "model";
						Record(text3, opening: true, stringBuilder.Length);
					}
					else if (!flag4 && passageTags && (flag5 || (events && VoiceDelivery.IsEvent(text3))))
					{
						Record(text3, opening: false, stringBuilder.Length);
					}
					trimLineBreaks = flag4;
					int num6;
					if (stringBuilder.Length <= 0)
					{
						if (turn.Answer.Length != 0)
						{
							string answer = turn.Answer;
							num6 = (char.IsWhiteSpace(answer[answer.Length - 1]) ? 1 : 0);
						}
						else
						{
							num6 = 1;
						}
					}
					else
					{
						num6 = (char.IsWhiteSpace(stringBuilder[stringBuilder.Length - 1]) ? 1 : 0);
					}
					trimAfterCue = (byte)num6 != 0;
					num = num4 + 2;
					continue;
				}
				if ("[[tone:".AsSpan().StartsWith(readOnlySpan, StringComparison.OrdinalIgnoreCase) || "[[voice:".AsSpan().StartsWith(readOnlySpan, StringComparison.OrdinalIgnoreCase))
				{
					string text2 = text;
					int num5 = num;
					pending = text2.Substring(num5, text2.Length - num5);
					break;
				}
			}
			stringBuilder.Append(c);
			num++;
		}
		return stringBuilder.ToString();
	}

	private void Record(string name, bool opening, int visibleLength)
	{
		if (turn.VoiceTags.Count < 16)
		{
			turn.VoiceTags.Add(new VoiceTag(turn.Answer.Length + visibleLength, name, opening));
		}
	}

	public string Finish()
	{
		pending = "";
		return "";
	}
}
