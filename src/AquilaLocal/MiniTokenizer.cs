using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AquilaLocal;

public sealed class MiniTokenizer
{
	private readonly Dictionary<string, int> vocabulary;

	public MiniTokenizer(string path)
	{
		vocabulary = File.ReadAllLines(path).Select((string word, int id) => (word: word, id: id)).ToDictionary<(string, int), string, int>(((string word, int id) p) => p.word, ((string word, int id) p) => p.id, StringComparer.Ordinal);
		if (vocabulary.GetValueOrDefault("[CLS]") != 101 || vocabulary.GetValueOrDefault("[SEP]") != 102 || vocabulary.GetValueOrDefault("[UNK]") != 100)
		{
			throw new InvalidDataException("Unexpected MiniLM vocabulary.");
		}
	}

	public long[] Encode(string text, int maxPieces = 254)
	{
		return new long[1] { 101L }.Concat(Pieces(text, maxPieces)).Append(102L).ToArray();
	}

	public List<long> Pieces(string text, int maxPieces = 1024)
	{
		List<long> list = new List<long>();
		string[] array = Regex.Split(text, "(\\[UNK\\]|\\[SEP\\]|\\[PAD\\]|\\[CLS\\]|\\[MASK\\])");
		foreach (string text2 in array)
		{
			if (text2.Length == 0)
			{
				continue;
			}
			bool flag;
			switch (text2)
			{
			case "[UNK]":
			case "[SEP]":
			case "[PAD]":
			case "[CLS]":
			case "[MASK]":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			StringBuilder word;
			List<string> tokens;
			if (flag)
			{
				list.Add(vocabulary[text2]);
			}
			else
			{
				StringBuilder stringBuilder = new StringBuilder();
				foreach (Rune item in text2.EnumerateRunes())
				{
					UnicodeCategory unicodeCategory = Rune.GetUnicodeCategory(item);
					int value = item.Value;
					flag = ((value == 0 || value == 65533) ? true : false);
					bool flag2 = flag;
					if (!flag2)
					{
						bool flag3 = (uint)(unicodeCategory - 14) <= 1u;
						bool flag4 = flag3;
						if (flag4)
						{
							bool flag5 = (((uint)(value - 9) <= 1u || value == 13) ? true : false);
							flag4 = !flag5;
						}
						flag2 = flag4;
					}
					if (!flag2)
					{
						if (Rune.IsWhiteSpace(item))
						{
							stringBuilder.Append(' ');
						}
						else if (IsChinese(value))
						{
							stringBuilder.Append(' ').Append(item).Append(' ');
						}
						else
						{
							stringBuilder.Append(item);
						}
					}
				}
				string text3 = string.Concat(from r in stringBuilder.ToString().ToLowerInvariant().Normalize(NormalizationForm.FormD)
						.EnumerateRunes()
					where Rune.GetUnicodeCategory(r) != UnicodeCategory.NonSpacingMark
					select r.ToString());
				word = new StringBuilder();
				tokens = new List<string>();
				foreach (Rune item2 in text3.EnumerateRunes())
				{
					int value2 = item2.Value;
					UnicodeCategory unicodeCategory2 = Rune.GetUnicodeCategory(item2);
					bool flag2;
					switch (value2)
					{
					case 33:
					case 34:
					case 35:
					case 36:
					case 37:
					case 38:
					case 39:
					case 40:
					case 41:
					case 42:
					case 43:
					case 44:
					case 45:
					case 46:
					case 47:
					case 58:
					case 59:
					case 60:
					case 61:
					case 62:
					case 63:
					case 64:
					case 91:
					case 92:
					case 93:
					case 94:
					case 95:
					case 96:
					case 123:
					case 124:
					case 125:
					case 126:
						flag2 = true;
						break;
					default:
						flag2 = false;
						break;
					}
					flag = flag2;
					if (!flag)
					{
						bool flag4 = (uint)(unicodeCategory2 - 18) <= 6u;
						flag = flag4;
					}
					bool flag6 = flag;
					if (Rune.IsWhiteSpace(item2))
					{
						Flush();
					}
					else if (flag6)
					{
						Flush();
						tokens.Add(item2.ToString());
					}
					else
					{
						word.Append(item2);
					}
				}
				Flush();
				foreach (string item3 in tokens)
				{
					string[] array2 = (from r in item3.EnumerateRunes()
						select r.ToString()).ToArray();
					List<long> list2 = new List<long>();
					bool flag7 = array2.Length > 100;
					int num = 0;
					while (!flag7 && num < array2.Length)
					{
						int num2 = array2.Length;
						int value3 = -1;
						while (num2 > num)
						{
							string key = string.Concat((num == 0) ? "" : "##", string.Concat(array2[num..num2]));
							if (vocabulary.TryGetValue(key, out value3))
							{
								break;
							}
							num2--;
						}
						if (num2 == num)
						{
							flag7 = true;
							continue;
						}
						list2.Add(value3);
						num = num2;
					}
					IEnumerable<long> collection;
					if (!flag7)
					{
						IEnumerable<long> enumerable = list2;
						collection = enumerable;
					}
					else
					{
						IEnumerable<long> enumerable = new long[1] { 100L };
						collection = enumerable;
					}
					list.AddRange(collection);
					if (list.Count >= maxPieces)
					{
						return list.Take(maxPieces).ToList();
					}
				}
			}
			if (list.Count >= maxPieces)
			{
				break;
			}
			void Flush()
			{
				if (word.Length > 0)
				{
					tokens.Add(word.ToString());
					word.Clear();
				}
			}
		}
		return list.Take(maxPieces).ToList();
	}

	private static bool IsChinese(int c)
	{
		if (c >= 131072)
		{
			if (c <= 178207)
			{
				if (c >= 173824)
				{
					if (c <= 177983)
					{
					}
				}
				else if (c > 173791)
				{
					goto IL_0080;
				}
				goto IL_007c;
			}
			if (c >= 194560)
			{
				if (c <= 195103)
				{
					goto IL_007c;
				}
			}
			else if (c <= 183983)
			{
				goto IL_007c;
			}
		}
		else if (c >= 19968)
		{
			if (c >= 63744)
			{
				if (c <= 64255)
				{
					goto IL_007c;
				}
			}
			else if (c <= 40959)
			{
				goto IL_007c;
			}
		}
		else if (c >= 13312 && c <= 19903)
		{
			goto IL_007c;
		}
		goto IL_0080;
		IL_007c:
		return true;
		IL_0080:
		return false;
	}
}
