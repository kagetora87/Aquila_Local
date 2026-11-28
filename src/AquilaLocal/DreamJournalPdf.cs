using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AquilaLocal.Companion.Living;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace AquilaLocal;

/// <summary>
/// Saves journal entries as a PDF: her journal picture and name on top, then each entry with its date, kind,
/// mood, text and picture. Videos can't go in a PDF; the window copies them next to it and each entry names its clip.
/// Uses the app's own Inter font (embedded), so the PDF looks the same on any computer.
/// </summary>
public static class DreamJournalPdf
{
	public const string Font = "Inter";

	/// <summary>One entry, with its picture ready to embed (a JPEG or PNG file) and its clip's saved name.</summary>
	public sealed record Item(JournalEntry Entry, string? Picture, string? VideoName);

	private static readonly object gate = new object();

	private static bool fontsReady;

	/// <summary>Gives PDFsharp the Inter font files (read once, by name: Inter-Regular.ttf, Inter-SemiBold.ttf, Inter-Italic.ttf).</summary>
	public static void UseFonts(Func<string, byte[]> load)
	{
		lock (gate)
		{
			if (fontsReady)
			{
				return;
			}
			GlobalFontSettings.FontResolver = new InterResolver(load);
			fontsReady = true;
		}
	}

	public static void Write(string path, string title, string subtitle, string? icon, IReadOnlyList<Item> items)
	{
		if (!fontsReady)
		{
			throw new InvalidOperationException("The PDF fonts weren't loaded.");
		}
		Document doc = new Document();
		doc.Info.Title = Clean(title);
		doc.Info.Author = "Aquila Local";
		Style normal = doc.Styles[StyleNames.Normal]!;
		normal.Font.Name = Font;
		normal.Font.Size = 10.5;
		normal.Font.Color = new Color(0x22, 0x2B, 0x36);
		normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);
		normal.ParagraphFormat.LineSpacingRule = MigraDoc.DocumentObjectModel.LineSpacingRule.Multiple;
		normal.ParagraphFormat.LineSpacing = 1.25;

		Section section = doc.AddSection();
		section.PageSetup.PageFormat = PageFormat.A4;
		section.PageSetup.TopMargin = Unit.FromCentimeter(2.2);
		section.PageSetup.BottomMargin = Unit.FromCentimeter(2.2);
		section.PageSetup.LeftMargin = Unit.FromCentimeter(2.3);
		section.PageSetup.RightMargin = Unit.FromCentimeter(2.3);
		Paragraph footer = section.Footers.Primary.AddParagraph();
		footer.Format.Alignment = ParagraphAlignment.Center;
		footer.Format.Font.Size = 8.5;
		footer.Format.Font.Color = new Color(0x8A, 0x96, 0xA3);
		footer.AddText(Clean(title) + "  ·  page ");
		footer.AddPageField();
		footer.AddText(" of ");
		footer.AddNumPagesField();

		if (icon != null && File.Exists(icon))
		{
			Paragraph p = section.AddParagraph();
			p.Format.Alignment = ParagraphAlignment.Center;
			p.Format.SpaceAfter = Unit.FromPoint(8);
			MigraDoc.DocumentObjectModel.Shapes.Image img = p.AddImage(icon);
			img.Width = Unit.FromCentimeter(3.2);
			img.LockAspectRatio = true;
		}
		Paragraph head = section.AddParagraph(Clean(title));
		head.Format.Alignment = ParagraphAlignment.Center;
		head.Format.Font.Size = 22;
		head.Format.Font.Bold = true;
		head.Format.Font.Color = new Color(0x15, 0x28, 0x3A);
		head.Format.SpaceAfter = Unit.FromPoint(2);
		Paragraph sub = section.AddParagraph(Clean(subtitle));
		sub.Format.Alignment = ParagraphAlignment.Center;
		sub.Format.Font.Size = 9.5;
		sub.Format.Font.Color = new Color(0x6B, 0x78, 0x86);
		sub.Format.SpaceAfter = Unit.FromPoint(18);

		foreach (Item item in items)
		{
			JournalEntry e = item.Entry;
			Paragraph when = section.AddParagraph();
			when.Format.KeepWithNext = true;
			when.Format.SpaceBefore = Unit.FromPoint(14);
			when.Format.SpaceAfter = Unit.FromPoint(3);
			when.Format.Borders.Top.Width = Unit.FromPoint(0.6);
			when.Format.Borders.Top.Color = new Color(0xD5, 0xDD, 0xE5);
			when.Format.Borders.DistanceFromTop = Unit.FromPoint(10);
			FormattedText date = when.AddFormattedText(e.At.ToLocalTime().ToString("dddd, MMMM d, yyyy · h:mm tt", CultureInfo.InvariantCulture));
			date.Bold = true;
			date.Size = 11;
			date.Color = new Color(0x15, 0x28, 0x3A);
			FormattedText kind = when.AddFormattedText("   " + KindName(e.Kind));
			kind.Size = 9.5;
			kind.Color = new Color(0x2E, 0x9C, 0x85);
			kind.Bold = true;
			if (e.MoodWord.Length > 0)
			{
				Paragraph mood = section.AddParagraph();
				mood.Format.KeepWithNext = true;
				mood.Format.Font.Size = 9.5;
				mood.Format.Font.Italic = true;
				mood.Format.Font.Color = new Color(0x6B, 0x78, 0x86);
				mood.AddText(Clean("Feeling " + e.MoodWord + (e.MoodStrength > 0 ? " (" + e.MoodStrength.ToString("0", CultureInfo.InvariantCulture) + "/10)" : "") + (e.MoodWhy.Length > 0 ? ": " + e.MoodWhy : "")));
			}
			section.AddParagraph(Clean(e.Text));
			if (item.Picture != null && File.Exists(item.Picture))
			{
				Paragraph p = section.AddParagraph();
				p.Format.SpaceBefore = Unit.FromPoint(4);
				p.Format.SpaceAfter = Unit.FromPoint(4);
				MigraDoc.DocumentObjectModel.Shapes.Image img = p.AddImage(item.Picture);
				img.Width = Unit.FromCentimeter(12);
				img.LockAspectRatio = true;
			}
			if (item.VideoName != null)
			{
				Small(section, "Video saved alongside this PDF: " + item.VideoName);
			}
			foreach (JournalLookup l in e.Lookups)
			{
				Small(section, l.Kind == JournalLookup.Read ? "Read: " + (l.Title.Length > 0 ? l.Title + " (" + l.What + ")" : l.What) : "Searched: " + l.What);
			}
			if (e.MessagedYou)
			{
				Small(section, "She messaged you: " + AquilaLocal.Companion.ThoughtMarkup.Plain(e.Message));
			}
			if (e.Note.Length > 0)
			{
				Small(section, e.Note);
			}
		}

		PdfDocumentRenderer renderer = new PdfDocumentRenderer { Document = doc };
		renderer.RenderDocument();
		string temp = path + ".saving";
		renderer.PdfDocument.Save(temp);
		File.Move(temp, path, overwrite: true);
	}

	private static void Small(Section section, string text)
	{
		Paragraph p = section.AddParagraph(Clean(text));
		p.Format.Font.Size = 9;
		p.Format.Font.Color = new Color(0x6B, 0x78, 0x86);
		p.Format.SpaceAfter = Unit.FromPoint(2);
	}

	public static string KindName(string kind) => kind switch
	{
		ThoughtKinds.Dream => "DREAM",
		ThoughtKinds.Daydream => "DAYDREAM",
		ThoughtKinds.Curiosity => "CURIOSITY",
		ThoughtKinds.Feeling => "FEELING",
		ThoughtKinds.Memory => "MEMORY",
		_ => "THOUGHT"
	};

	/// <summary>Emoji and pictographs aren't in the font (they'd print as boxes), so they're left out.</summary>
	public static string Clean(string text)
	{
		StringBuilder sb = new StringBuilder(text.Length);
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (char.IsHighSurrogate(c))
			{
				i++;
				continue;
			}
			if (char.IsLowSurrogate(c) || c == '️' || c == '‍' || (c >= '☀' && c <= '➿') || (c >= '⬀' && c <= '⯿'))
			{
				continue;
			}
			if (char.IsControl(c) && c != '\n' && c != '\t')
			{
				continue;
			}
			sb.Append(c);
		}
		return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"[ \t]{2,}", " ").Trim();
	}

	private sealed class InterResolver : IFontResolver
	{
		private readonly Func<string, byte[]> load;

		private readonly Dictionary<string, byte[]> faces = new Dictionary<string, byte[]>();

		public InterResolver(Func<string, byte[]> load)
		{
			this.load = load;
		}

		public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
		{
			// Every family resolves to Inter: the document only asks for it (and PDFsharp's own default).
			if (bold && italic)
			{
				return new FontResolverInfo("Inter-Italic.ttf", mustSimulateBold: true, mustSimulateItalic: false);
			}
			if (bold)
			{
				return new FontResolverInfo("Inter-SemiBold.ttf");
			}
			if (italic)
			{
				return new FontResolverInfo("Inter-Italic.ttf");
			}
			return new FontResolverInfo("Inter-Regular.ttf");
		}

		public byte[]? GetFont(string faceName)
		{
			lock (faces)
			{
				if (!faces.TryGetValue(faceName, out byte[]? bytes))
				{
					bytes = load(faceName);
					faces[faceName] = bytes;
				}
				return bytes;
			}
		}
	}
}
