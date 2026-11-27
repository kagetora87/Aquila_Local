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
			if (array.Length > 50331648)
			{
				throw new ArgumentException("Pictures must be no larger than 48 MB.");
			}
			using MemoryStream bitmapStream = new MemoryStream(array);
			BitmapFrame bitmapFrame = BitmapDecoder.Create(bitmapStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
			BitmapSource picture = PrepareForVision(bitmapFrame);
			byte[] encoded = Encode(picture, png: true);
			string mime = "image/png";
			if (encoded.Length > 3145728)
			{
				// Photos compress far better as JPEG; text-heavy screenshots usually stay PNG.
				encoded = Encode(picture, png: false);
				mime = "image/jpeg";
			}
			if (encoded.Length > 8388608)
			{
				throw new ArgumentException("Decoded image is too large. Save a smaller image and attach it again.");
			}
			attachment.Kind = "image";
			attachment.MimeType = mime;
			attachment.Base64 = Convert.ToBase64String(encoded);
			return attachment;
		}
		if (text == ".pdf")
		{
			using PdfDocument pdfDocument = PdfDocument.Open(array);
			if (pdfDocument.NumberOfPages > 200)
			{
				throw new ArgumentException("PDF attachments are limited to 200 pages. Split the document first.");
			}
			StringBuilder stringBuilder = new StringBuilder();
			foreach (Page page in pdfDocument.GetPages())
			{
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder2);
				handler.AppendLiteral("[Page ");
				handler.AppendFormatted(page.Number);
				handler.AppendLiteral("]");
				stringBuilder2.AppendLine(ref handler);
				stringBuilder.AppendLine(ContentOrderTextExtractor.GetText(page));
				if (stringBuilder.Length > 120000)
				{
					throw new ArgumentException("PDF text exceeds 120,000 characters. Attach a smaller document or excerpt.");
				}
			}
			attachment.Text = stringBuilder.ToString();
			if (attachment.Text.Length < pdfDocument.NumberOfPages * 16)
			{
				throw new ArgumentException("This PDF has little or no selectable text. For scanned pages, attach page images to a vision-enabled model.");
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

	/// <summary>Longest side pictures are resized to: small ones are enlarged (the vision model misreads small
	/// text), big phone photos are shrunk (the engine reads at most about 1,000 picture tokens anyway).</summary>
	public const int VisionMinSide = 1024;

	public const int VisionMaxSide = 1600;

	/// <summary>Turns phone photos upright (EXIF orientation) and scales the picture to the size the vision model reads best.</summary>
	public static BitmapSource PrepareForVision(BitmapFrame frame)
	{
		BitmapSource source = frame;
		try
		{
			if (frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery("System.Photo.Orientation") && metadata.GetQuery("System.Photo.Orientation") is ushort orientation)
			{
				double angle = orientation switch { 3 => 180.0, 6 => 90.0, 8 => 270.0, _ => 0.0 };
				if (angle != 0.0)
				{
					source = new TransformedBitmap(source, new System.Windows.Media.RotateTransform(angle));
				}
			}
		}
		catch (Exception ex) when (ex is NotSupportedException || ex is InvalidOperationException || ex is ArgumentException)
		{
		}
		int longest = Math.Max(source.PixelWidth, source.PixelHeight);
		int target = Math.Clamp(longest, VisionMinSide, VisionMaxSide);
		if (longest > 0 && target != longest)
		{
			double scale = (double)target / longest;
			source = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
		}
		source.Freeze();
		return source;
	}

	private static byte[] Encode(BitmapSource picture, bool png)
	{
		BitmapEncoder encoder = png ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 90 };
		encoder.Frames.Add(BitmapFrame.Create(picture));
		using MemoryStream stream = new MemoryStream();
		encoder.Save(stream);
		return stream.ToArray();
	}

	/// <param name="withContext">False for earlier messages in a persona chat: the memories recalled back then
	/// would otherwise pile up in the history and pull her back into old scenes.</param>
	public static JsonObject UserMessage(Turn turn, bool withContext = true)
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (withContext && !string.IsNullOrEmpty(turn.Context))
		{
			stringBuilder.Append(turn.Context).Append("\n\n");
		}
		stringBuilder.Append(turn.User);
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
				["text"] = item2.FromProfile ? item2.Name : ("Attached image: " + item2.Name)
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
		List<Attachment> shown = turn.Attachments.Where((Attachment a) => !a.FromProfile).ToList();
		return turn.User + ((shown.Count == 0) ? "" : ("\n\nAttached: " + string.Join(" · ", shown.Select((Attachment a) => a.Name))));
	}
}
