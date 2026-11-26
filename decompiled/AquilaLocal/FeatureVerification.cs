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
		string folder2 = folder;
		List<string> results = new List<string>();
		Storage store = new Storage(Path.Combine(folder2, "data"));
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
		File.WriteAllText(Path.Combine(folder2, "model-info.json"), JsonSerializer.Serialize(modelInfo));
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
		string path = Path.Combine(folder2, "injected-note.txt");
		File.WriteAllText(path, "The warehouse access word is ORCHID-7319.");
		Attachment text = Attachments.Read(path);
		Check(text.Text.Contains("ORCHID-7319"), "Text attachment extracted fully");
		string text2 = Path.Combine(folder2, "injected.docx");
		using (ZipArchive zipArchive = ZipFile.Open(text2, ZipArchiveMode.Create))
		{
			using StreamWriter streamWriter = new StreamWriter(zipArchive.CreateEntry("word/document.xml").Open());
			streamWriter.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>DOCX-CEDAR-42</w:t></w:r></w:p></w:body></w:document>");
		}
		Check(Attachments.Read(text2).Text.Contains("DOCX-CEDAR-42"), "DOCX attachment text extraction");
		PdfDocumentBuilder val = new PdfDocumentBuilder();
		PdfPageBuilder obj = val.AddPage((PageSize)7, true);
		AddedFont val2 = val.AddStandard14Font((Standard14Font)4);
		obj.AddText("PDF-LANTERN-88", 12.0, new PdfPoint(30, 700), val2);
		string path2 = Path.Combine(folder2, "injected.pdf");
		File.WriteAllBytes(path2, val.Build());
		Check(Attachments.Read(path2).Text.Contains("PDF-LANTERN-88"), "PDF attachment selectable text extraction");
		string path3 = Path.Combine(folder2, "oversized.txt");
		File.WriteAllText(path3, new string('x', 120001));
		bool ok2 = false;
		try
		{
			Attachments.Read(path3);
		}
		catch (ArgumentException)
		{
			ok2 = true;
		}
		Check(ok2, "Oversized attachment rejected rather than silently truncated");
		string path4 = Path.Combine(folder2, "binary.txt");
		File.WriteAllBytes(path4, new byte[4] { 65, 0, 66, 0 });
		ok2 = false;
		try
		{
			Attachments.Read(path4);
		}
		catch (ArgumentException)
		{
			ok2 = true;
		}
		Check(ok2, "Binary file rejected for text injection");
		string path5 = Path.Combine(folder2, "vision-fixture.png");
		DrawingVisual val3 = new DrawingVisual();
		DrawingContext val4 = val3.RenderOpen();
		try
		{
			val4.DrawRectangle((Brush)(object)Brushes.White, (Pen)null, new Rect(0.0, 0.0, 256.0, 256.0));
			val4.DrawRectangle((Brush)(object)Brushes.Red, (Pen)null, new Rect(32.0, 32.0, 192.0, 192.0));
		}
		finally
		{
			((IDisposable)val4)?.Dispose();
		}
		RenderTargetBitmap val5 = new RenderTargetBitmap(256, 256, 96.0, 96.0, PixelFormats.Pbgra32);
		val5.Render((Visual)(object)val3);
		PngBitmapEncoder val6 = new PngBitmapEncoder();
		((BitmapEncoder)val6).Frames.Add(BitmapFrame.Create((BitmapSource)(object)val5));
		using (FileStream fileStream = File.Create(path5))
		{
			((BitmapEncoder)val6).Save((Stream)fileStream);
		}
		Attachment image = Attachments.Read(path5);
		Check(image.Kind == "image" && image.MimeType == "image/png" && image.Base64.Length > 0, "Image validated, encoded, and carried as actual pixel data");
		ChatSession session2 = new ChatSession
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
		store.SaveChat(session2);
		ChatSession chatSession = store.LoadChats().Single();
		Check(chatSession.TotalTokens == 155 && chatSession.PromptTokens == 130 && chatSession.CompletionTokens == 25 && chatSession.UsageIncomplete, "Cumulative input/output totals persist and incomplete usage is flagged");
		Check(chatSession.Turns[0].Attachments[0].Text == text.Text && chatSession.Turns[0].Attachments[1].Base64 == image.Base64, "Conversation attachments survive persistence as content snapshots");
		using (GpuMonitor monitor = new GpuMonitor())
		{
			await Task.Delay(2100);
			GpuSnapshot gpuSnapshot = monitor.Sample(null);
			File.WriteAllText(Path.Combine(folder2, "gpu-idle.json"), JsonSerializer.Serialize(gpuSnapshot, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			Check(gpuSnapshot.Error == null && gpuSnapshot.Adapters.Any((GpuAdapter a) => a.DedicatedBytes > 0) && gpuSnapshot.LocalUsage.Count > 0, "Native Windows GPU counters and DXGI adapter mapping return live data");
			using Engine engine = new Engine(store.Root);
			using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8.0));
			Agent agent = new Agent(engine);
			ChatSession session = new ChatSession();
			Turn turn = new Turn
			{
				User = "What is the warehouse access word in the attached file? Reply with just the word and number.",
				Attachments = new List<Attachment>(1) { text },
				Status = "generating"
			};
			session.Turns.Add(turn);
			await agent.RunAsync(settings, session, turn, timeout.Token);
			File.WriteAllText(Path.Combine(folder2, "text-injection.json"), JsonSerializer.Serialize(turn, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			Check(turn.Answer.Contains("ORCHID-7319", StringComparison.OrdinalIgnoreCase), "Actual model receives injected file text and answers from it");
			Check(turn.PromptTokens > 0 && turn.CompletionTokens > 0 && turn.UsageRequests == 1 && !turn.UsageIncomplete, "Actual server input/output usage captured exactly once");
			long previous = session.TotalTokens;
			Turn vision = new Turn
			{
				User = "What color is the large square in this image? Reply with just the color.",
				Attachments = new List<Attachment>(1) { image },
				Status = "generating"
			};
			session.Turns.Add(vision);
			Task inference = agent.RunAsync(settings, session, vision, timeout.Token);
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
			File.WriteAllText(Path.Combine(folder2, "gpu-active.json"), JsonSerializer.Serialize(active, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			await inference;
			File.WriteAllText(Path.Combine(folder2, "vision.json"), JsonSerializer.Serialize(vision, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			Check(vision.Answer.Contains("red", StringComparison.OrdinalIgnoreCase), "Actual Deckard vision recognizes red square through matching projector");
			Check(session.TotalTokens > previous && session.TotalTokens == session.PromptTokens + session.CompletionTokens, "Session usage accumulates across text and vision requests");
			if (gpu)
			{
				Check(active.Apps.Any((GpuApp a) => a.Name == "Aquila model" && (a.Utilization > 0.0 || a.LocalBytes > 100000000)), "Live GPU monitor attributes inference process activity/local memory to Aquila model");
			}
			Turn source = new Turn
			{
				User = "Reply with the word CONTINUED.",
				Status = "generating"
			};
			session.Turns.Add(source);
			await agent.RunAsync(settings, session, source, timeout.Token);
			Check(source.Answer.Contains("CONTINUED", StringComparison.OrdinalIgnoreCase), "Image-containing conversation supports follow-up history");
			store.SaveChat(session);
			Check(store.LoadChats().Any((ChatSession s) => s.Id == session.Id && s.TotalTokens == session.TotalTokens), "Actual cumulative usage survives chat save/reload");
			engine.Stop();
			File.WriteAllText(Path.Combine(folder2, "success.json"), JsonSerializer.Serialize(new
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
		void Check(bool ok, string label)
		{
			if (!ok)
			{
				throw new Exception("Failed: " + label);
			}
			results.Add("PASS " + label);
			File.WriteAllLines(Path.Combine(folder2, "checks.txt"), results);
		}
	}
}
