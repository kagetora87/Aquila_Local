using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AquilaLocal;

public static class EmbeddingVerification
{
	private sealed record Reference(string Text, long[] Ids, float[] Vector);

	public static async Task RunAsync(string directory, string referencePath)
	{
		List<string> results = new List<string>();
		using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3.0)))
		{
			CancellationToken ct = timeout.Token;
			Check(Environment.GetEnvironmentVariable("ORT_DISABLE_TELEMETRY") == "1", "ONNX telemetry is disabled before embedding runtime initialization");
			using EmbeddingModel model = new EmbeddingModel(Path.Combine(AppContext.BaseDirectory, "embeddings"));
			using JsonDocument reference = JsonDocument.Parse(File.ReadAllText(referencePath));
			List<Reference> list = reference.RootElement.GetProperty("cases").Deserialize<List<Reference>>();
			foreach (Reference item in list)
			{
				Check(model.Tokenizer.Encode(item.Text).SequenceEqual(item.Ids), "Reference tokenizer parity: fixture " + list.IndexOf(item));
			}
			foreach (Reference item2 in list.Take(8))
			{
				float[] array = model.Encode(item2.Text, ct);
				Check(EmbeddingModel.Similarity(array, item2.Vector) > 0.99999f && (double)array.Zip(item2.Vector).Max(((float First, float Second) p) => Math.Abs(p.First - p.Second)) < 0.0002, "Reference mean-pooling/normalization parity: fixture " + list.IndexOf(item2));
			}
			float close = EmbeddingModel.Similarity(model.Encode(list[0].Text, ct), model.Encode(list[3].Text, ct));
			float distant = EmbeddingModel.Similarity(model.Encode(list[0].Text, ct), model.Encode(list[2].Text, ct));
			Check(close > distant + 0.15f, "Semantic paraphrase similarity outranks unrelated text");
			string root = Path.Combine(directory, "data");
			Directory.CreateDirectory(root);
			MemoryStore store = new MemoryStore(root);
			MemoryEntry detail = store.SaveManual(null, "", personal: true, "communication", "Keep explanations brief and to the point.", "fact", pinned: false);
			string a = Path.Combine(directory, "project-a");
			string workspace = Path.Combine(directory, "project-b");
			MemoryEntry privateEntry = store.SaveManual(null, workspace, personal: false, "explanation_depth", "Long explanations with every detail.", "fact", pinned: true);
			using SemanticMemory semantic = new SemanticMemory(store, root);
			Stopwatch clock = Stopwatch.StartNew();
			List<MemoryEntry> source = await semantic.RetrieveAsync(a, "I want a concise response with minimal verbosity.", 2000, ct);
			long cold = clock.ElapsedMilliseconds;
			Check(source.Any((MemoryEntry e) => e.Id == detail.Id) && source.All((MemoryEntry e) => e.Id != privateEntry.Id), "Semantic recall finds a paraphrase while excluding another project's pinned memory");
			int before = semantic.ModelRuns;
			clock.Restart();
			await semantic.RetrieveAsync(a, "I want a concise response with minimal verbosity.", 2000, ct);
			long warm = clock.ElapsedMilliseconds;
			Check(semantic.ModelRuns == before + 1, "Cached memories require only the new query embedding on repeated recall");
			using (SemanticMemory restored = new SemanticMemory(store, root))
			{
				await restored.RetrieveAsync(a, "brief response", 2000, ct);
				Check(restored.ModelRuns == 1, "Memory vectors persist and are reused by a fresh app instance");
			}
			new MemorySource
			{
				Quote = "I want brief replies.",
				ChatId = "test",
				TurnId = "test"
			};
			store.SaveManual(detail.Id, "", personal: true, detail.Topic, "Use detailed, exhaustive explanations.", "fact", pinned: false);
			before = semantic.ModelRuns;
			await semantic.RetrieveAsync(a, "detailed explanations", 2000, ct);
			Check(semantic.ModelRuns == before + 2, "Editing a memory invalidates its old vector before the next query");
			long length = new FileInfo(Path.Combine(root, "memory-vectors.bin")).Length;
			store.Forget(detail.Id);
			bool flag = new FileInfo(Path.Combine(root, "memory-vectors.bin")).Length < length;
			if (flag)
			{
				flag = (await semantic.RetrieveAsync(a, "brief response", 2000, ct)).All((MemoryEntry e) => e.Id != detail.Id);
			}
			Check(flag, "Forgetting immediately removes cached vectors and prevents their retrieval");
			MemoryEntry next = store.SaveManual(null, a, personal: false, "project_food", "The crew prefers vegetable noodles.", "fact", pinned: true);
			using (SemanticMemory restored = new SemanticMemory(store, root, Path.Combine(directory, "missing-assets")))
			{
				Check((await restored.RetrieveAsync(a, "crew food", 2000, ct)).Any((MemoryEntry e) => e.Id == next.Id) && restored.LastStatus.StartsWith("Keyword fallback"), "Unavailable embeddings fall back to working keyword recall");
			}
			Check((await semantic.RetrieveAsync(a, "crew food", 1, ct)).Count == 0, "Semantic recall still obeys the memory prompt budget");
			using (CancellationTokenSource cancelled = new CancellationTokenSource())
			{
				cancelled.Cancel();
				bool stopped = false;
				try
				{
					await semantic.RetrieveAsync(a, "test", 2000, cancelled.Token);
				}
				catch (OperationCanceledException)
				{
					stopped = true;
				}
				Check(stopped, "Embedding search honors cancellation");
			}
			File.WriteAllText(Path.Combine(root, "memory-vectors.bin"), "broken");
			using (SemanticMemory restored = new SemanticMemory(store, root))
			{
				Check((await restored.RetrieveAsync(a, "crew food", 2000, ct)).Any((MemoryEntry e) => e.Id == next.Id), "Corrupt derived vector cache rebuilds from the intact memory catalog");
			}
			string[] array2 = new string[4] { "My bicycle uses tubeless tires.", "My favorite dessert is chocolate cake.", "I grow tomatoes in the garden.", "I collect antique postage stamps." };
			for (int num = 0; num < array2.Length; num++)
			{
				store.SaveManual(null, a, personal: false, "hobby_" + num, array2[num], "preference", pinned: false);
			}
			List<MemoryEntry> legacy = store.Retrieve(a, "crew food", 2000, markUsed: false);
			List<MemoryEntry> list2 = await semantic.RetrieveAsync(a, "crew food", 2000, ct);
			Check(list2.Any((MemoryEntry e) => e.Id == next.Id) && list2.Count < legacy.Count, "Hybrid selection excludes unrelated preferences while retaining relevant pinned facts");
			File.WriteAllText(Path.Combine(directory, "metrics.json"), JsonSerializer.Serialize(new
			{
				coldMilliseconds = cold,
				warmMilliseconds = warm,
				closeSimilarity = close,
				unrelatedSimilarity = distant,
				legacyMemories = legacy.Count,
				hybridMemories = list2.Count,
				cpuThreads = 1,
				gpuUsed = false,
				referenceRuntime = reference.RootElement.GetProperty("runtime").GetString(),
				checks = results.Count
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
			File.WriteAllLines(Path.Combine(directory, "checks.txt"), results);
		}
	}
}
