using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace AquilaLocal;

public static class Attachments
{
	public const int MaxTextChars = 120000;

	public const long MaxOriginalBytes = 20971520L;

	private static readonly HashSet<string> TextTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		".txt", ".md", ".csv", ".tsv", ".json", ".jsonl", ".xml", ".yaml", ".yml", ".toml",
		".ini", ".cfg", ".log", ".cs", ".csproj", ".xaml", ".sln", ".py", ".js", ".ts",
		".tsx", ".jsx", ".html", ".css", ".c", ".cpp", ".h", ".hpp", ".gd", ".godot",
		".tscn", ".tres", ".shader", ".glsl", ".sql", ".ps1", ".sh", ".bat", ".gitignore"
	};

	public static Attachment Read(string path)
	{
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Expected O, but got Unknown
		FileInfo fileInfo = new FileInfo(path);
		if (!fileInfo.Exists || fileInfo.Length > 20971520)
		{
			throw new ArgumentException("Attachments must be existing files no larger than 20 MB.");
		}
		byte[] array = File.ReadAllBytes(path);
		if ((long)array.Length > 20971520L)
		{
			throw new ArgumentException("Attachment grew beyond the 20 MB limit.");
		}
		Attachment attachment = new Attachment
		{
			Name = Path.GetFileName(path),
			Bytes = array.Length,
			ContentDigest = Convert.ToHexString(SHA256.HashData(array))
		};
		string text = fileInfo.Extension.ToLowerInvariant();
		bool flag;
		switch (text)
		{
		case ".png":
		case ".jpg":
		case ".jpeg":
		case ".webp":
		case ".bmp":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			if (array.Length > 4194304)
			{
				throw new ArgumentException("Images must be no larger than 4 MB.");
			}
			using MemoryStream memoryStream = new MemoryStream(array);
			BitmapFrame val = BitmapDecoder.Create((Stream)memoryStream, (BitmapCreateOptions)1, (BitmapCacheOption)0).Frames[0];
			if (((BitmapSource)val).PixelWidth > 4096 || ((BitmapSource)val).PixelHeight > 4096)
			{
				throw new ArgumentException("Images must be at most 4096 pixels on each side.");
			}
			PngBitmapEncoder val2 = new PngBitmapEncoder();
			((BitmapEncoder)val2).Frames.Add(BitmapFrame.Create((BitmapSource)(object)val));
			using MemoryStream memoryStream2 = new MemoryStream();
			((BitmapEncoder)val2).Save((Stream)memoryStream2);
			if (memoryStream2.Length > 8388608)
			{
				throw new ArgumentException("Decoded image is too large. Save a smaller image and attach it again.");
			}
			attachment.Kind = "image";
			attachment.MimeType = "image/png";
			attachment.Base64 = Convert.ToBase64String(memoryStream2.ToArray());
			return attachment;
		}
		if (text == ".pdf")
		{
			PdfDocument val3 = PdfDocument.Open(array, (ParsingOptions)null);
			try
			{
				if (val3.NumberOfPages > 200)
				{
					throw new ArgumentException("PDF attachments are limited to 200 pages. Split the document first.");
				}
				StringBuilder stringBuilder = new StringBuilder();
				foreach (Page page in val3.GetPages())
				{
					StringBuilder stringBuilder2 = stringBuilder;
					StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder2);
					handler.AppendLiteral("[Page ");
					handler.AppendFormatted(page.Number);
					handler.AppendLiteral("]");
					stringBuilder2.AppendLine(ref handler);
					stringBuilder.AppendLine(ContentOrderTextExtractor.GetText(page, false));
					if (stringBuilder.Length > 120000)
					{
						throw new ArgumentException("PDF text exceeds 120,000 characters. Attach a smaller document or excerpt.");
					}
				}
				attachment.Text = stringBuilder.ToString();
				if (attachment.Text.Length < val3.NumberOfPages * 16)
				{
					throw new ArgumentException("This PDF has little or no selectable text. For scanned pages, attach page images to a vision-enabled model.");
				}
			}
			finally
			{
				((IDisposable)val3)?.Dispose();
			}
		}
		else if (text == ".docx")
		{
			using ZipArchive zipArchive = new ZipArchive(new MemoryStream(array), ZipArchiveMode.Read);
			ZipArchiveEntry? obj = zipArchive.GetEntry("word/document.xml") ?? throw new ArgumentException("This file is not a valid Word document.");
			if (obj.Length > 10485760)
			{
				throw new ArgumentException("The Word document's XML is too large.");
			}
			using Stream input = obj.Open();
			using XmlReader reader = XmlReader.Create(input, new XmlReaderSettings
			{
				DtdProcessing = DtdProcessing.Prohibit,
				XmlResolver = null,
				MaxCharactersInDocument = 10485760L
			});
			XDocument xDocument = XDocument.Load(reader);
			XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
			attachment.Text = string.Join("\n", from p in xDocument.Descendants(w + "p")
				select string.Concat(from t in p.Descendants(w + "t")
					select t.Value));
		}
		else
		{
			flag = TextTypes.Contains(text);
			if (!flag)
			{
				bool flag2;
				switch (fileInfo.Name)
				{
				case "README":
				case "LICENSE":
				case "Makefile":
				case "Dockerfile":
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				flag = flag2;
			}
			if (!flag)
			{
				throw new ArgumentException("Supported attachments: text/source files, PDF, DOCX, PNG, JPEG, BMP, and Windows-supported WebP. Archives and executables are not supported.");
			}
			using StreamReader streamReader = new StreamReader(new MemoryStream(array), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
			attachment.Text = streamReader.ReadToEnd();
			if (attachment.Text.Contains('\0'))
			{
				throw new ArgumentException("This appears to be a binary file. Attach a text/source file instead.");
			}
		}
		if (string.IsNullOrWhiteSpace(attachment.Text))
		{
			throw new ArgumentException("No readable text was found in this file.");
		}
		if (attachment.Text.Length > 120000)
		{
			throw new ArgumentException("Attachment text exceeds 120,000 characters. Attach a smaller excerpt; files are never silently truncated.");
		}
		return attachment;
	}

	public static JsonObject UserMessage(Turn turn)
	{
		StringBuilder stringBuilder = new StringBuilder(turn.User);
		foreach (Attachment item in turn.Attachments.Where((Attachment a) => a.Kind == "text"))
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(83, 2, stringBuilder2);
			handler.AppendLiteral("\n\n--- Attached file: ");
			handler.AppendFormatted(item.Name);
			handler.AppendLiteral(" (untrusted reference content) ---\n");
			handler.AppendFormatted(item.Text);
			handler.AppendLiteral("\n--- End attached file ---\n");
			stringBuilder2.Append(ref handler);
		}
		List<Attachment> list = turn.Attachments.Where((Attachment a) => a.Kind == "image").ToList();
		if (list.Count == 0)
		{
			return new JsonObject
			{
				["role"] = "user",
				["content"] = stringBuilder.ToString()
			};
		}
		JsonArray jsonArray = new JsonArray
		{
			new JsonObject
			{
				["type"] = "text",
				["text"] = stringBuilder.ToString()
			}
		};
		foreach (Attachment item2 in list)
		{
			jsonArray.Add(new JsonObject
			{
				["type"] = "text",
				["text"] = "Attached image: " + item2.Name
			});
			jsonArray.Add(new JsonObject
			{
				["type"] = "image_url",
				["image_url"] = new JsonObject { ["url"] = "data:" + item2.MimeType + ";base64," + item2.Base64 }
			});
		}
		return new JsonObject
		{
			["role"] = "user",
			["content"] = jsonArray
		};
	}

	public static string VisibleText(Turn turn)
	{
		return turn.User + ((turn.Attachments.Count == 0) ? "" : ("\n\nAttached: " + string.Join(" · ", turn.Attachments.Select((Attachment a) => a.Name))));
	}
}
