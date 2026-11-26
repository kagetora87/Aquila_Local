using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;

namespace AquilaLocal;

public sealed class SemanticMemory : IDisposable
{
	private sealed record CachedVector(string Key, float[] Vector);

	private readonly MemoryStore store;

	private readonly string directory;

	private readonly string cachePath;

	private readonly object gate = new object();

	private EmbeddingModel? model;

	private readonly Dictionary<string, CachedVector> cache = new Dictionary<string, CachedVector>();

	private bool disposed;

	public string LastStatus { get; private set; } = "MiniLM · CPU, 1 thread · loads on first recall";


	public int ModelRuns => model?.Runs ?? 0;

	public TimeSpan LastElapsed { get; private set; }

	public SemanticMemory(MemoryStore store, string dataRoot, string? directory = null)
	{
		this.store = store;
		this.directory = directory ?? Path.Combine(AppContext.BaseDirectory, "embeddings");
		cachePath = Path.Combine(dataRoot, "memory-vectors.bin");
		LoadCache();
		store.Changed += Invalidate;
	}

	private static string Text(MemoryEntry e)
	{
		return e.Topic.Replace('_', ' ') + ": " + e.Value + ". " + e.Sources.LastOrDefault()?.Quote;
	}

	private static string Key(MemoryEntry e)
	{
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("MiniLM-1110a243-quint8-avx2-tokenizer-v1" + Text(e))));
	}

	public Task<List<MemoryEntry>> RetrieveAsync(string workspace, string query, int budget, CancellationToken ct)
	{
		string workspace2 = workspace;
		string query2 = query;
		return Task.Run(delegate
		{
			lock (gate)
			{
				ct.ThrowIfCancellationRequested();
				ObjectDisposedException.ThrowIf(disposed, this);
				Stopwatch stopwatch = Stopwatch.StartNew();
				try
				{
					List<MemoryEntry> list = store.Snapshot();
					Prune(list);
					string scope = MemoryStore.ScopeFor(workspace2);
					List<MemoryEntry> list2 = list.Where((MemoryEntry e) => e.State == "active" && (e.Scope == "personal" || e.Scope == scope)).ToList();
					if (list2.Count == 0)
					{
						SaveCache();
						LastStatus = "MiniLM · no eligible memories to search";
						return new List<MemoryEntry>();
					}
					if (model == null)
					{
						model = new EmbeddingModel(directory);
					}
					int runs = model.Runs;
					float[] a = model.Encode(query2, ct);
					Dictionary<string, float> dictionary = new Dictionary<string, float>();
					foreach (MemoryEntry item in list2)
					{
						ct.ThrowIfCancellationRequested();
						string text = Key(item);
						if (!cache.TryGetValue(item.Id, out CachedVector value) || value.Key != text)
						{
							value = (cache[item.Id] = new CachedVector(text, model.Encode(Text(item), ct)));
						}
						dictionary[item.Id] = EmbeddingModel.Similarity(a, value.Vector);
					}
					SaveCache();
					ct.ThrowIfCancellationRequested();
					List<MemoryEntry> result = store.Retrieve(workspace2, query2, budget, markUsed: true, dictionary);
					LastStatus = $"MiniLM + keywords · {list2.Count} eligible · {model.Runs - runs} embedding pass(es) · {stopwatch.Elapsed.TotalMilliseconds:N0} ms · CPU 1 thread";
					return result;
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException || ex2 is OnnxRuntimeException || ex2 is InvalidOperationException || ex2 is ArgumentException || ex2 is DllNotFoundException || ex2 is EntryPointNotFoundException || ex2 is BadImageFormatException) ? 1 : 0) != 0)
				{
					LastStatus = "Keyword fallback · " + ex2.Message.Truncate(170);
					return store.Retrieve(workspace2, query2, budget);
				}
				finally
				{
					LastElapsed = stopwatch.Elapsed;
				}
			}
		}, ct);
	}

	private void Prune(List<MemoryEntry> entries)
	{
		Dictionary<string, string> valid = entries.Where((MemoryEntry e) => e.State == "active").ToDictionary((MemoryEntry e) => e.Id, Key);
		string value;
		string[] array = cache.Keys.Where((string id) => !valid.TryGetValue(id, out value) || cache[id].Key != value).ToArray();
		foreach (string key in array)
		{
			cache.Remove(key);
		}
	}

	private void Invalidate()
	{
		lock (gate)
		{
			if (disposed)
			{
				return;
			}
			try
			{
				Prune(store.Snapshot());
				SaveCache();
			}
			catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
				LastStatus = "Vector cache refresh deferred · " + ex.Message.Truncate(100);
			}
		}
	}

	private void LoadCache()
	{
		if (!File.Exists(cachePath))
		{
			return;
		}
		try
		{
			if (new FileInfo(cachePath).Length > 10485760)
			{
				throw new InvalidDataException();
			}
			using FileStream fileStream = File.OpenRead(cachePath);
			using BinaryReader binaryReader = new BinaryReader(fileStream);
			if (binaryReader.ReadString() != "AQUILA-VEC-1" || binaryReader.ReadString() != "MiniLM-1110a243-quint8-avx2-tokenizer-v1")
			{
				return;
			}
			int num = binaryReader.ReadInt32();
			if ((num < 0 || num > 5000) ? true : false)
			{
				throw new InvalidDataException();
			}
			for (int i = 0; i < num; i++)
			{
				string text = binaryReader.ReadString();
				string text2 = binaryReader.ReadString();
				if (text.Length != 32 || text2.Length != 64)
				{
					throw new InvalidDataException();
				}
				float[] array = new float[384];
				for (int j = 0; j < array.Length; j++)
				{
					array[j] = binaryReader.ReadSingle();
					if (!float.IsFinite(array[j]))
					{
						throw new InvalidDataException();
					}
				}
				double num2 = array.Sum((float x) => (double)x * (double)x);
				if ((num2 < 0.8 || num2 > 1.2) ? true : false)
				{
					throw new InvalidDataException();
				}
				cache[text] = new CachedVector(text2, array);
			}
			if (fileStream.Position == fileStream.Length)
			{
				return;
			}
			throw new InvalidDataException();
		}
		catch (Exception ex) when (((ex is IOException || ex is FormatException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			cache.Clear();
			LastStatus = "MiniLM · derived cache unreadable; rebuilding on recall";
		}
	}

	private void SaveCache()
	{
		string text = cachePath + "." + Environment.ProcessId + ".tmp";
		try
		{
			using (FileStream output = File.Create(text))
			{
				using BinaryWriter binaryWriter = new BinaryWriter(output);
				binaryWriter.Write("AQUILA-VEC-1");
				binaryWriter.Write("MiniLM-1110a243-quint8-avx2-tokenizer-v1");
				binaryWriter.Write(cache.Count);
				foreach (var (value, cachedVector2) in cache)
				{
					binaryWriter.Write(value);
					binaryWriter.Write(cachedVector2.Key);
					float[] vector = cachedVector2.Vector;
					foreach (float value2 in vector)
					{
						binaryWriter.Write(value2);
					}
				}
			}
			File.Move(text, cachePath, overwrite: true);
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	public void Dispose()
	{
		lock (gate)
		{
			if (!disposed)
			{
				disposed = true;
				store.Changed -= Invalidate;
				model?.Dispose();
			}
		}
	}
}
