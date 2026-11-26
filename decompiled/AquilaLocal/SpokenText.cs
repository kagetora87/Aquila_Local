using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AquilaLocal;

public static class SpokenText
{
	public const string CueInstructions = "\nVoice output is enabled. Start each final natural-language answer with exactly one delivery cue: [[tone:neutral]], [[tone:warm]], [[tone:cheerful]], [[tone:gentle]], [[tone:serious]] or [[tone:concerned]]. Then write the answer normally. The app removes this cue; do not explain it or put directions in the spoken text. Choose based on the current conversation: gentle for reassurance, concerned for distress, cheerful for good news, serious for careful factual guidance, warm for friendly conversation, neutral otherwise. Keep expression proportionate. Tool-call JSON must remain unchanged.";

	public static string Clean(string text)
	{
		int num = text.IndexOf("\n\nSources consulted (search results may be snippets):", StringComparison.Ordinal);
		if (num >= 0)
		{
			text = text.Substring(0, num);
		}
		text = Regex.Replace(text, "(```|~~~)[\\s\\S]*?(\\1|\\z)", "\nCode is available in the written reply.\n");
		text = Regex.Replace(text, "!\\[[^\\]]*\\]\\([^)]*\\)", "");
		text = Regex.Replace(text, "\\[([^\\]]+)\\]\\(https?://[^)]*\\)", "$1");
		text = Regex.Replace(text, "https?://\\S+", "link in the written reply");
		text = Regex.Replace(text, "\\[\\[(?:tone|voice):[^\\]]*\\]\\]", "", RegexOptions.IgnoreCase);
		text = Regex.Replace(text, "(?m)^\\s*(#{1,6}\\s*|>\\s*|[-*+]\\s+|\\d+[.)]\\s+)", "");
		text = text.Replace("**", "").Replace("__", "").Replace("`", "");
		StringBuilder stringBuilder = new StringBuilder();
		TextElementEnumerator textElementEnumerator = StringInfo.GetTextElementEnumerator(text);
		while (textElementEnumerator.MoveNext())
		{
			string textElement = textElementEnumerator.GetTextElement();
			if (Rune.GetUnicodeCategory(Rune.GetRuneAt(textElement, 0)) != UnicodeCategory.OtherSymbol && !textElement.Contains('\ufe0f') && !textElement.Contains('\u20e3'))
			{
				stringBuilder.Append(textElement);
			}
		}
		text = stringBuilder.ToString();
		text = Regex.Replace(text, "(?m)^\\s*\\|?\\s*:?-{2,}[^\\r\\n]*$", "");
		text = text.Replace('|', ',');
		return Regex.Replace(text, "[ \\t]+", " ").Trim();
	}

	public static List<string> Segments(string text, int maximum)
	{
		text = Clean(text);
		if (text.Length > maximum)
		{
			int num = text.LastIndexOfAny(new char[4] { '.', '!', '?', '\n' }, maximum - 1);
			text = text.Substring(0, (num > maximum / 2) ? (num + 1) : maximum) + " The rest is in the written reply.";
		}
		return SplitClean(text);
	}

	internal static List<string> SplitClean(string text)
	{
		List<string> list = new List<string>();
		StringBuilder stringBuilder = new StringBuilder();
		string[] array = Regex.Split(text, "\\s+");
		foreach (string text2 in array)
		{
			if (text2.Length == 0)
			{
				continue;
			}
			if (text2.Length > 210)
			{
				if (stringBuilder.Length > 0)
				{
					list.Add(stringBuilder.ToString());
					stringBuilder.Clear();
				}
				for (int j = 0; j < text2.Length; j += 210)
				{
					list.Add(text2.Substring(j, Math.Min(210, text2.Length - j)));
				}
				continue;
			}
			if (stringBuilder.Length + text2.Length > 210 && stringBuilder.Length > 0)
			{
				list.Add(stringBuilder.ToString());
				stringBuilder.Clear();
			}
			if (stringBuilder.Length > 0)
			{
				stringBuilder.Append(' ');
			}
			stringBuilder.Append(text2);
			if (Regex.IsMatch(text2, "[!?][\"')]*$|\\.[\"')]*$") && !Regex.IsMatch(text2, "(?i)^(mr|mrs|ms|dr|prof|sr|jr|st|vs|etc|e\\.g|i\\.e)\\.$|^[A-Z]\\.$") && stringBuilder.Length >= 25)
			{
				list.Add(stringBuilder.ToString());
				stringBuilder.Clear();
			}
		}
		if (stringBuilder.Length > 0)
		{
			list.Add(stringBuilder.ToString());
		}
		return list;
	}
}
