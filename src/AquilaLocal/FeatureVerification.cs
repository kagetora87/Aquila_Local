using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace AquilaLocal;

public static class FeatureVerification
{
	public static async Task RunAsync(string folder, string modelPath, bool gpu)
	{
		List<string> results = new List<string>();
		Storage store = new Storage(Path.Combine(folder, "data"));
		Settings settings = new Settings
		{
			ModelPath = modelPath,
			ProjectorPath = ModelInfo.FindProjector(modelPath),
			GpuLayers = (gpu ? (-1) : 0),
			CpuThreads = 2,
			Context = 8192,
			MaxTokens = 256,
			Temperature = 0.0,
			Thinking = false
		};
		ModelInfo modelInfo = ModelInfo.Read(modelPath);
		File.WriteAllText(Path.Combine(folder, "model-info.json"), JsonSerializer.Serialize(modelInfo));
		Check(modelInfo.MetadataKnown && modelInfo.NativeContext >= 32768 && modelInfo.FileBytes > 0, "Actual GGUF native context and model metadata read without loading weights");
		ContextAdvice contextAdvice = modelInfo.Advice(modelInfo.FileBytes + 1073741824 + 16384 * (long)modelInfo.KvBytesPerToken, 0L);
		ContextAdvice contextAdvice2 = modelInfo.Advice(modelInfo.FileBytes + 1073741824 + 65536 * (long)modelInfo.KvBytesPerToken, 0L);
		Check(contextAdvice.Danger < contextAdvice2.Danger && contextAdvice.Recommended <= contextAdvice.Danger && contextAdvice2.Danger <= modelInfo.NativeContext, "Context thresholds respond to available memory and obey native limit");
		Check(settings.ProjectorPath.Length > 0 && File.Exists(settings.ProjectorPath), "Deckard matching vision projector detected");
		store.SaveSettings(new Settings
		{
			ModelPath = modelPath,
			VisionDisabled = true
		});
		Check(store.LoadSettings().ProjectorPath.Length == 0, "Explicitly cleared vision projector stays disabled after settings reload");
		store.SaveSettings(settings);
		string path = Path.Combine(folder, "injected-note.txt");
		File.WriteAllText(path, "The warehouse access word is ORCHID-7319.");
		Attachment text = Attachments.Read(path);
		Check(text.Text.Contains("ORCHID-7319"), "Text attachment extracted fully");
		string text2 = Path.Combine(folder, "injected.docx");
		using (ZipArchive zipArchive = ZipFile.Open(text2, ZipArchiveMode.Create))
		{
			using StreamWriter streamWriter = new StreamWriter(zipArchive.CreateEntry("word/document.xml").Open());
			streamWriter.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>DOCX-CEDAR-42</w:t></w:r></w:p></w:body></w:document>");
		}
		Check(Attachments.Read(text2).Text.Contains("DOCX-CEDAR-42"), "DOCX attachment text extraction");
		PdfDocumentBuilder pdfDocumentBuilder = new PdfDocumentBuilder();
		pdfDocumentBuilder.AddPage(PageSize.A4).AddText(font: pdfDocumentBuilder.AddStandard14Font(Standard14Font.Helvetica), text: "PDF-LANTERN-88", fontSize: 12.0, position: new PdfPoint(30, 700));
		string path2 = Path.Combine(folder, "injected.pdf");
		File.WriteAllBytes(path2, pdfDocumentBuilder.Build());
		Check(Attachments.Read(path2).Text.Contains("PDF-LANTERN-88"), "PDF attachment selectable text extraction");
		string path3 = Path.Combine(folder, "oversized.txt");
		File.WriteAllText(path3, new string('x', 120001));
		bool ok = false;
		try
		{
			Attachments.Read(path3);
		}
		catch (ArgumentException)
		{
			ok = true;
		}
		Check(ok, "Oversized attachment rejected rather than silently truncated");
		string path4 = Path.Combine(folder, "binary.txt");
		File.WriteAllBytes(path4, new byte[4] { 65, 0, 66, 0 });
		ok = false;
		try
		{
			Attachments.Read(path4);
		}
		catch (ArgumentException)
		{
			ok = true;
		}
		Check(ok, "Binary file rejected for text injection");
		string path5 = Path.Combine(folder, "vision-fixture.png");
		DrawingVisual drawingVisual = new DrawingVisual();
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			drawingContext.DrawRectangle(Brushes.White, null, new Rect(0.0, 0.0, 256.0, 256.0));
			drawingContext.DrawRectangle(Brushes.Red, null, new Rect(32.0, 32.0, 192.0, 192.0));
		}
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(256, 256, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(drawingVisual);
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));
		using (FileStream stream = File.Create(path5))
		{
			pngBitmapEncoder.Save(stream);
		}
		Attachment image = Attachments.Read(path5);
		Check(image.Kind == "image" && image.MimeType == "image/png" && image.Base64.Length > 0, "Image validated, encoded, and carried as actual pixel data");
		ChatSession session = new ChatSession
		{
			Turns = new List<Turn>(2)
			{
				new Turn
				{
					User = "Question",
					Attachments = new List<Attachment>(2) { text, image },
					PromptTokens = 100L,
					CompletionTokens = 20L,
					UsageRequests = 1
				},
				new Turn
				{
					PromptTokens = 30L,
					CompletionTokens = 5L,
					UsageRequests = 1,
					UsageIncomplete = true
				}
			}
		};
		store.SaveChat(session);
		ChatSession chatSession = store.LoadChats().Single();
		Check(chatSession.TotalTokens == 155 && chatSession.PromptTokens == 130 && chatSession.CompletionTokens == 25 && chatSession.UsageIncomplete, "Cumulative input/output totals persist and incomplete usage is flagged");
		Check(chatSession.Turns[0].Attachments[0].Text == text.Text && chatSession.Turns[0].Attachments[1].Base64 == image.Base64, "Conversation attachments survive persistence as content snapshots");
		using (GpuMonitor monitor = new GpuMonitor())
		{
			await Task.Delay(2100);
			GpuSnapshot gpuSnapshot = monitor.Sample(null);
			File.WriteAllText(Path.Combine(folder, "gpu-idle.json"), JsonSerializer.Serialize(gpuSnapshot, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			Check(gpuSnapshot.Error == null && gpuSnapshot.Adapters.Any((GpuAdapter a) => a.DedicatedBytes > 0) && gpuSnapshot.LocalUsage.Count > 0, "Native Windows GPU counters and DXGI adapter mapping return live data");
			using Engine engine = new Engine(store.Root);
			using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8.0));
			Agent agent = new Agent(engine);
			ChatSession session2 = new ChatSession();
			Turn turn = new Turn
			{
				User = "What is the warehouse access word in the attached file? Reply with just the word and number.",
				Attachments = new List<Attachment>(1) { text },
				Status = "generating"
			};
			session2.Turns.Add(turn);
			await agent.RunAsync(settings, session2, turn, timeout.Token);
			File.WriteAllText(Path.Combine(folder, "text-injection.json"), JsonSerializer.Serialize(turn, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			Check(turn.Answer.Contains("ORCHID-7319", StringComparison.OrdinalIgnoreCase), "Actual model receives injected file text and answers from it");
			Check(turn.PromptTokens > 0 && turn.CompletionTokens > 0 && turn.UsageRequests == 1 && !turn.UsageIncomplete, "Actual server input/output usage captured exactly once");
			long previous = session2.TotalTokens;
			Turn vision = new Turn
			{
				User = "What color is the large square in this image? Reply with just the color.",
				Attachments = new List<Attachment>(1) { image },
				Status = "generating"
			};
			session2.Turns.Add(vision);
			Task inference = agent.RunAsync(settings, session2, vision, timeout.Token);
			await Task.Delay(700, timeout.Token);
			GpuSnapshot active = monitor.Sample(engine.ProcessId);
			for (int i = 0; i < 6; i++)
			{
				if (active.Error == null && active.Apps.Any((GpuApp a) => a.Name == "Aquila model" && (a.Utilization > 0.0 || a.LocalBytes > 100000000)))
				{
					break;
				}
				await Task.Delay(1100, timeout.Token);
				active = monitor.Sample(engine.ProcessId);
			}
			File.WriteAllText(Path.Combine(folder, "gpu-active.json"), JsonSerializer.Serialize(active, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			await inference;
			File.WriteAllText(Path.Combine(folder, "vision.json"), JsonSerializer.Serialize(vision, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			Check(vision.Answer.Contains("red", StringComparison.OrdinalIgnoreCase), "Actual Deckard vision recognizes red square through matching projector");
			Check(session2.TotalTokens > previous && session2.TotalTokens == session2.PromptTokens + session2.CompletionTokens, "Session usage accumulates across text and vision requests");
			if (gpu)
			{
				Check(active.Apps.Any((GpuApp a) => a.Name == "Aquila model" && (a.Utilization > 0.0 || a.LocalBytes > 100000000)), "Live GPU monitor attributes inference process activity/local memory to Aquila model");
			}
			Turn source = new Turn
			{
				User = "Reply with the word CONTINUED.",
				Status = "generating"
			};
			session2.Turns.Add(source);
			await agent.RunAsync(settings, session2, source, timeout.Token);
			Check(source.Answer.Contains("CONTINUED", StringComparison.OrdinalIgnoreCase), "Image-containing conversation supports follow-up history");
			store.SaveChat(session2);
			Check(store.LoadChats().Any((ChatSession s) => s.Id == session2.Id && s.TotalTokens == session2.TotalTokens), "Actual cumulative usage survives chat save/reload");
			engine.Stop();
			File.WriteAllText(Path.Combine(folder, "success.json"), JsonSerializer.Serialize(new
			{
				passed = results.Count,
				model = Path.GetFileName(modelPath),
				gpu = gpu,
				finished = DateTimeOffset.Now
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		void Check(bool flag, string label)
		{
			if (!flag)
			{
				throw new Exception("Failed: " + label);
			}
			results.Add("PASS " + label);
			File.WriteAllLines(Path.Combine(folder, "checks.txt"), results);
		}
	}
}
