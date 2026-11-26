using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace AquilaLocal;

/// <summary>
/// The small hand-written Markdown renderer for replies. Text is split into prose and code
/// segments at ``` fences (so code blocks can be real controls), and prose becomes a FlowDocument
/// with headings, paragraphs, nested lists, quotes, rules, tables and inline formatting.
/// Safe to call on partial (still streaming) text: an unclosed fence is treated as code.
/// </summary>
public static class MarkdownRenderer
{
	public sealed record Segment(bool IsCode, string Source, string Language, bool Closed);

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100.0);

	private static readonly Regex Heading = new Regex("^(#{1,6})\\s+(.*?)\\s*#*\\s*$", RegexOptions.None, RegexTimeout);

	private static readonly Regex ListItem = new Regex("^(\\s*)([-*+]|\\d{1,9}[.)])\\s+(.*)$", RegexOptions.None, RegexTimeout);

	private static readonly Regex Rule = new Regex("^\\s{0,3}(?:-\\s*){3,}$|^\\s{0,3}(?:\\*\\s*){3,}$|^\\s{0,3}(?:_\\s*){3,}$", RegexOptions.None, RegexTimeout);

	private static readonly Regex TableDivider = new Regex("^\\s*\\|?\\s*:?-{2,}:?\\s*(\\|\\s*:?-{2,}:?\\s*)*\\|?\\s*$", RegexOptions.None, RegexTimeout);

	private static readonly Regex InlineToken = new Regex("(\\*\\*[^*\\n]+?\\*\\*|__[^_\\n]+?__|~~[^~\\n]+?~~|`[^`\\n]+`|\\[[^\\]\\n]+\\]\\((?:<https?://[^\\s>]+>|https?://[^\\s)]+(?:\\([^\\s)]*\\))?[^\\s)]*)\\)|(?<![\\w*])\\*(?![\\s*])[^*\\n]+?(?<!\\s)\\*(?![\\w*])|(?<![\\w_])_(?![\\s_])[^_\\n]+?(?<!\\s)_(?![\\w_]))", RegexOptions.None, RegexTimeout);

	private static readonly Regex Link = new Regex("^\\[([^\\]]+)\\]\\((?:<(https?://[^\\s>]+)>|(https?://[^\\s)]+(?:\\([^\\s)]*\\))?[^\\s)]*))\\)$", RegexOptions.None, RegexTimeout);

	public const double ReadingSize = 15.0;

	public const double ReadingLineHeight = 24.0;

	/// <summary>Splits reply text into prose and fenced-code segments.</summary>
	public static List<Segment> Split(string text)
	{
		List<Segment> list = new List<Segment>();
		StringBuilder stringBuilder = new StringBuilder();
		bool flag = false;
		string language = "";
		string fence = "```";
		foreach (string item in text.Replace("\r\n", "\n").Split('\n'))
		{
			string text2 = item.TrimStart();
			if (!flag && (text2.StartsWith("```") || text2.StartsWith("~~~")))
			{
				if (stringBuilder.ToString().Trim().Length > 0)
				{
					list.Add(new Segment(IsCode: false, stringBuilder.ToString().Trim('\n'), "", Closed: true));
				}
				stringBuilder.Clear();
				fence = text2.Substring(0, 3);
				language = text2.TrimStart('`', '~').Trim().Split(' ', '{')[0].ToLowerInvariant();
				flag = true;
				continue;
			}
			if (flag && text2.StartsWith(fence) && text2.Trim().Trim(fence[0]).Length == 0)
			{
				list.Add(new Segment(IsCode: true, TrimCode(stringBuilder.ToString()), language, Closed: true));
				stringBuilder.Clear();
				flag = false;
				continue;
			}
			stringBuilder.Append(item).Append('\n');
		}
		if (flag)
		{
			list.Add(new Segment(IsCode: true, TrimCode(stringBuilder.ToString()), language, Closed: false));
		}
		else if (stringBuilder.ToString().Trim().Length > 0)
		{
			list.Add(new Segment(IsCode: false, stringBuilder.ToString().Trim('\n'), "", Closed: true));
		}
		return list;
	}

	private static string TrimCode(string code)
	{
		return code.TrimEnd('\n', ' ', '\t', '\r');
	}

	public static FlowDocument NewDocument()
	{
		return new FlowDocument
		{
			PagePadding = new Thickness(0.0),
			FontFamily = Ui.FontUI,
			FontSize = ReadingSize,
			LineHeight = ReadingLineHeight,
			Foreground = Ui.Brush("Text.Primary")
		};
	}

	/// <summary>Plain text in a document (user messages).</summary>
	public static FlowDocument Plain(string text)
	{
		FlowDocument flowDocument = NewDocument();
		Paragraph paragraph = new Paragraph
		{
			Margin = new Thickness(0.0)
		};
		string[] array = AquilaLocal.Companion.ThoughtMarkup.BalancePerLine(text.Replace("\r\n", "\n")).Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			if (i > 0)
			{
				paragraph.Inlines.Add(new LineBreak());
			}
			if (!AquilaLocal.Companion.ThoughtMarkup.HasMarkup(array[i]))
			{
				paragraph.Inlines.Add(new Run(array[i]));
				continue;
			}
			foreach (AquilaLocal.Companion.MarkupPart part in AquilaLocal.Companion.ThoughtMarkup.Parse(array[i]))
			{
				if (part.Kind == AquilaLocal.Companion.MarkupKind.Speech)
				{
					paragraph.Inlines.Add(new Run(part.Text));
				}
				else
				{
					Span span = MarkupColors.Span(part.Kind);
					span.Inlines.Add(new Run(part.Text));
					paragraph.Inlines.Add(span);
				}
			}
		}
		flowDocument.Blocks.Add(paragraph);
		return flowDocument;
	}

	/// <summary>Renders a prose segment (no code fences) into a document.</summary>
	public static FlowDocument Prose(string text)
	{
		FlowDocument flowDocument = NewDocument();
		string[] lines = text.Replace("\r\n", "\n").Split('\n');
		int i = 0;
		Paragraph? paragraph = null;
		List<(int Indent, List List)> lists = new List<(int, List)>();
		Paragraph? lastItemParagraph = null;
		void EndParagraph()
		{
			paragraph = null;
		}
		void EndLists()
		{
			lists.Clear();
			lastItemParagraph = null;
		}
		while (i < lines.Length)
		{
			string line = lines[i];
			string trimmed = line.Trim();
			if (trimmed.Length == 0)
			{
				EndParagraph();
				// A blank line inside a list keeps the list going if the next line continues it.
				if (lists.Count > 0 && (i + 1 >= lines.Length || !ListItem.IsMatch(lines[i + 1])))
				{
					EndLists();
				}
				i++;
				continue;
			}
			Match match = Heading.Match(line);
			if (match.Success)
			{
				EndParagraph();
				EndLists();
				int length = match.Groups[1].Length;
				Paragraph paragraph2 = new Paragraph
				{
					FontWeight = FontWeights.SemiBold,
					FontSize = ((length <= 2) ? 20.0 : 16.0),
					LineHeight = ((length <= 2) ? 28.0 : 22.0),
					Margin = ((length <= 2) ? new Thickness(0.0, 20.0, 0.0, 8.0) : new Thickness(0.0, 16.0, 0.0, 6.0))
				};
				AddInlines(paragraph2.Inlines, match.Groups[2].Value);
				flowDocument.Blocks.Add(paragraph2);
				i++;
				continue;
			}
			if (Rule.IsMatch(line))
			{
				EndParagraph();
				EndLists();
				flowDocument.Blocks.Add(new Paragraph
				{
					BorderBrush = Ui.Brush("Stroke.Subtle"),
					BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
					Margin = new Thickness(0.0, 8.0, 0.0, 16.0),
					FontSize = 1.0,
					LineHeight = 1.0
				});
				i++;
				continue;
			}
			if (trimmed.StartsWith("|") && i + 1 < lines.Length && TableDivider.IsMatch(lines[i + 1]))
			{
				EndParagraph();
				EndLists();
				List<string> list = new List<string> { line };
				i += 2;
				while (i < lines.Length && lines[i].Trim().StartsWith("|"))
				{
					list.Add(lines[i]);
					i++;
				}
				flowDocument.Blocks.Add(BuildTable(list));
				continue;
			}
			if (trimmed.StartsWith(">"))
			{
				EndParagraph();
				EndLists();
				Paragraph paragraph3 = new Paragraph
				{
					BorderBrush = Ui.Brush("Stroke.Strong"),
					BorderThickness = new Thickness(2.0, 0.0, 0.0, 0.0),
					Padding = new Thickness(12.0, 0.0, 0.0, 0.0),
					Foreground = Ui.Brush("Text.Secondary"),
					Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
				};
				bool flag = true;
				while (i < lines.Length && lines[i].Trim().StartsWith(">"))
				{
					if (!flag)
					{
						paragraph3.Inlines.Add(new LineBreak());
					}
					AddInlines(paragraph3.Inlines, lines[i].Trim().Substring(1).TrimStart());
					flag = false;
					i++;
				}
				flowDocument.Blocks.Add(paragraph3);
				continue;
			}
			Match match2 = ListItem.Match(line);
			if (match2.Success)
			{
				EndParagraph();
				int indent = match2.Groups[1].Value.Replace("\t", "    ").Length;
				string value = match2.Groups[2].Value;
				bool flag2 = char.IsDigit(value[0]);
				while (lists.Count > 0 && indent < lists[lists.Count - 1].Indent)
				{
					lists.RemoveAt(lists.Count - 1);
				}
				List list2;
				if (lists.Count == 0 || indent >= lists[lists.Count - 1].Indent + 2)
				{
					list2 = new List
					{
						MarkerStyle = (flag2 ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc),
						MarkerOffset = 8.0,
						Padding = new Thickness(22.0, 0.0, 0.0, 0.0),
						Margin = ((lists.Count == 0) ? new Thickness(0.0, 0.0, 0.0, 12.0) : new Thickness(0.0, 4.0, 0.0, 0.0))
					};
					if (flag2 && int.TryParse(value.TrimEnd('.', ')'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result > 1)
					{
						list2.StartIndex = result;
					}
					if (lists.Count == 0)
					{
						flowDocument.Blocks.Add(list2);
					}
					else
					{
						ListItem? listItem = lists[lists.Count - 1].List.ListItems.LastListItem;
						if (listItem != null)
						{
							listItem.Blocks.Add(list2);
						}
						else
						{
							flowDocument.Blocks.Add(list2);
						}
					}
					lists.Add((indent, list2));
				}
				else
				{
					list2 = lists[lists.Count - 1].List;
				}
				Paragraph paragraph4 = new Paragraph
				{
					Margin = new Thickness(0.0, 0.0, 0.0, 4.0)
				};
				AddInlines(paragraph4.Inlines, match2.Groups[3].Value);
				list2.ListItems.Add(new ListItem(paragraph4));
				lastItemParagraph = paragraph4;
				i++;
				continue;
			}
			if (lists.Count > 0 && lastItemParagraph != null && (line.StartsWith(" ") || line.StartsWith("\t")))
			{
				lastItemParagraph.Inlines.Add(new LineBreak());
				AddInlines(lastItemParagraph.Inlines, trimmed);
				i++;
				continue;
			}
			EndLists();
			if (paragraph == null)
			{
				paragraph = new Paragraph
				{
					Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
				};
				flowDocument.Blocks.Add(paragraph);
			}
			else
			{
				paragraph.Inlines.Add(new LineBreak());
			}
			AddInlines(paragraph.Inlines, line.TrimEnd());
			i++;
		}
		if (flowDocument.Blocks.FirstBlock is Paragraph paragraph5 && paragraph5.Margin.Top > 0.0)
		{
			paragraph5.Margin = new Thickness(0.0, 0.0, 0.0, paragraph5.Margin.Bottom);
		}
		if (flowDocument.Blocks.LastBlock is Block block)
		{
			block.Margin = new Thickness(block.Margin.Left, block.Margin.Top, block.Margin.Right, 0.0);
		}
		return flowDocument;
	}

	private static Table BuildTable(List<string> rows)
	{
		Table table = new Table
		{
			CellSpacing = 0.0,
			Margin = new Thickness(0.0, 4.0, 0.0, 16.0),
			FontSize = 14.0,
			LineHeight = 20.0
		};
		List<string[]> list = rows.Select(SplitRow).ToList();
		int num = list.Max((string[] r) => r.Length);
		for (int i = 0; i < num; i++)
		{
			table.Columns.Add(new TableColumn());
		}
		TableRowGroup tableRowGroup = new TableRowGroup();
		for (int j = 0; j < list.Count; j++)
		{
			TableRow tableRow = new TableRow();
			for (int k = 0; k < num; k++)
			{
				Paragraph paragraph = new Paragraph
				{
					Margin = new Thickness(0.0)
				};
				AddInlines(paragraph.Inlines, (k < list[j].Length) ? list[j][k] : "");
				TableCell tableCell = new TableCell(paragraph)
				{
					Padding = new Thickness(8.0, 6.0, 8.0, 6.0),
					BorderBrush = Ui.Brush((j == 0) ? "Stroke.Default" : "Stroke.Subtle"),
					BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0)
				};
				if (j == 0)
				{
					tableCell.FontWeight = FontWeights.SemiBold;
				}
				tableRow.Cells.Add(tableCell);
			}
			tableRowGroup.Rows.Add(tableRow);
		}
		table.RowGroups.Add(tableRowGroup);
		return table;
	}

	private static string[] SplitRow(string row)
	{
		string text = row.Trim();
		if (text.StartsWith("|"))
		{
			text = text.Substring(1);
		}
		if (text.EndsWith("|"))
		{
			text = text.Substring(0, text.Length - 1);
		}
		return (from c in text.Split('|')
			select c.Trim()).ToArray();
	}

	/// <summary>Bold, italic, strikethrough, inline code and safe links; thoughts in blue and actions in orange.</summary>
	public static void AddInlines(InlineCollection inlines, string text)
	{
		if (!AquilaLocal.Companion.ThoughtMarkup.HasMarkup(text))
		{
			AddFormatted(inlines, text);
			return;
		}
		foreach (AquilaLocal.Companion.MarkupPart part in AquilaLocal.Companion.ThoughtMarkup.Parse(text))
		{
			if (part.Kind == AquilaLocal.Companion.MarkupKind.Speech)
			{
				AddFormatted(inlines, part.Text);
			}
			else
			{
				Span span = MarkupColors.Span(part.Kind);
				AddFormatted(span.Inlines, part.Text);
				inlines.Add(span);
			}
		}
	}

	private static void AddFormatted(InlineCollection inlines, string text)
	{
		string[] array;
		try
		{
			array = InlineToken.Split(text);
		}
		catch (RegexMatchTimeoutException)
		{
			inlines.Add(new Run(text));
			return;
		}
		foreach (string text2 in array)
		{
			if (text2.Length == 0)
			{
				continue;
			}
			if (text2.Length >= 4 && ((text2.StartsWith("**") && text2.EndsWith("**")) || (text2.StartsWith("__") && text2.EndsWith("__"))))
			{
				Span span = new Span
				{
					FontWeight = FontWeights.SemiBold
				};
				AddInlines(span.Inlines, text2.Substring(2, text2.Length - 4));
				inlines.Add(span);
			}
			else if (text2.Length >= 4 && text2.StartsWith("~~") && text2.EndsWith("~~"))
			{
				Span span2 = new Span
				{
					TextDecorations = TextDecorations.Strikethrough
				};
				AddInlines(span2.Inlines, text2.Substring(2, text2.Length - 4));
				inlines.Add(span2);
			}
			else if (text2.Length >= 2 && text2.StartsWith("`") && text2.EndsWith("`"))
			{
				inlines.Add(new Run(" " + text2.Substring(1, text2.Length - 2) + " ")
				{
					FontFamily = Ui.FontMono,
					FontSize = 13.0,
					Foreground = Ui.Brush("Code.Inline"),
					Background = Ui.Brush("Surface.2")
				});
			}
			else if (text2.StartsWith("[") && text2.EndsWith(")") && Link.Match(text2) is Match { Success: not false } match)
			{
				AddLink(inlines, match);
			}
			else if (text2.Length >= 3 && ((text2.StartsWith("*") && text2.EndsWith("*")) || (text2.StartsWith("_") && text2.EndsWith("_"))))
			{
				Span span3 = new Span
				{
					FontStyle = FontStyles.Italic
				};
				AddInlines(span3.Inlines, text2.Substring(1, text2.Length - 2));
				inlines.Add(span3);
			}
			else
			{
				inlines.Add(new Run(text2));
			}
		}
	}

	private static void AddLink(InlineCollection inlines, Match match)
	{
		string value = match.Groups[1].Value;
		try
		{
			Uri uri = WebAccess.ValidateUrl(match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value);
			Hyperlink hyperlink = new Hyperlink(new Run(value))
			{
				ToolTip = uri.AbsoluteUri,
				Foreground = Ui.Brush("Accent"),
				TextDecorations = null,
				Cursor = System.Windows.Input.Cursors.Hand
			};
			hyperlink.MouseEnter += delegate
			{
				hyperlink.TextDecorations = TextDecorations.Underline;
			};
			hyperlink.MouseLeave += delegate
			{
				hyperlink.TextDecorations = null;
			};
			hyperlink.Click += delegate
			{
				try
				{
					Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
					{
						UseShellExecute = true
					});
				}
				catch
				{
				}
			};
			inlines.Add(hyperlink);
		}
		catch (ArgumentException)
		{
			inlines.Add(new Run(match.Value));
		}
	}
}
