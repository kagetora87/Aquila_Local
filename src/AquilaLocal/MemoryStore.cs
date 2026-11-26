using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace AquilaLocal;

public sealed class MemoryStore
{
	private readonly string path;

	private readonly string mutexName;

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	public static readonly string[] Kinds = new string[5] { "preference", "fact", "decision", "constraint", "goal" };

	private static readonly HashSet<string> StopWords = new HashSet<string>("a an the and or is are was were to for of in on my me i it this that what how you your please remember use with as be about do does have has can we our".Split(' '));

	public string? RecoveryNotice { get; private set; }

	public event Action? Changed;

	public MemoryStore(string dataRoot)
	{
		Directory.CreateDirectory(dataRoot);
		path = Path.Combine(Path.GetFullPath(dataRoot), "memories.json");
		mutexName = "Local\\AquilaMemory-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()))).Substring(0, 24);
	}

	public static string ScopeFor(string workspace)
	{
		if (!string.IsNullOrWhiteSpace(workspace))
		{
			return "project:" + Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace)).ToLowerInvariant();
		}
		return "personal";
	}

	public static string TopicKey(string text)
	{
		return string.Join('_', from m in Regex.Matches(text.ToLowerInvariant(), "[\\p{L}\\p{Nd}]+")
			select m.Value).Truncate(80);
	}

	public static string Normalize(string text)
	{
		return Regex.Replace(text.Trim(), "\\s+", " ");
	}

	private static HashSet<string> Words(string text)
	{
		return (from m in Regex.Matches(text.ToLowerInvariant(), "[\\p{L}\\p{Nd}]{2,}")
			select m.Value into w
			where !StopWords.Contains(w)
			select w).ToHashSet();
	}

	private T Access<T>(bool write, Func<MemoryCatalog, T> action)
	{
		using Mutex mutex = new Mutex(initiallyOwned: false, mutexName);
		bool flag = false;
		try
		{
			try
			{
				flag = mutex.WaitOne(TimeSpan.FromSeconds(5.0));
			}
			catch (AbandonedMutexException)
			{
				flag = true;
			}
			if (!flag)
			{
				throw new IOException("Memory catalog is busy. Try again in a moment.");
			}
			MemoryCatalog memoryCatalog = Load();
			T result = action(memoryCatalog);
			if (write)
			{
				string text = JsonSerializer.Serialize(memoryCatalog, Json);
				if (Encoding.UTF8.GetByteCount(text) > 12582912)
				{
					throw new IOException("Memory catalog reached its size limit. Forget unused entries before adding more.");
				}
				string sourceFileName = path + ".tmp";
				File.WriteAllText(sourceFileName, text);
				File.Move(sourceFileName, path, overwrite: true);
			}
			return result;
		}
		finally
		{
			if (flag)
			{
				mutex.ReleaseMutex();
			}
		}
	}

	private MemoryCatalog Load()
	{
		if (!File.Exists(path))
		{
			return new MemoryCatalog();
		}
		try
		{
			if (new FileInfo(path).Length > 12582912)
			{
				throw new JsonException("Oversized memory catalog");
			}
			MemoryCatalog memoryCatalog = JsonSerializer.Deserialize<MemoryCatalog>(File.ReadAllText(path)) ?? throw new JsonException();
			if (memoryCatalog.Version != 1 || memoryCatalog.Entries == null || memoryCatalog.Entries.Count > 5000 || memoryCatalog.Entries.Any((MemoryEntry e) => e == null || e.Id == null || e.Scope == null || (e.Scope != "personal" && !e.Scope.StartsWith("project:")) || e.Topic == null || e.Value == null || e.Sources == null || e.Sources.Any((MemorySource s) => s == null || s.ChatId == null || s.TurnId == null || s.Quote == null)))
			{
				throw new JsonException("Unsupported memory catalog");
			}
			return memoryCatalog;
		}
		catch (JsonException)
		{
			string destFileName = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
			File.Move(path, destFileName);
			RecoveryNotice = "Unreadable memory data was preserved in a .corrupt backup. A fresh catalog is being used.";
			return new MemoryCatalog();
		}
	}

	public List<MemoryEntry> Snapshot()
	{
		return Access(write: false, (MemoryCatalog c) => (from e in c.Entries
			orderby e.Pinned descending, e.Updated descending
			select e).ToList());
	}

	public List<MemoryEntry> Retrieve(string workspace, string query, int budget = 3000, bool markUsed = true, IReadOnlyDictionary<string, float>? semantic = null)
	{
		string scope = ScopeFor(workspace);
		HashSet<string> words = Words(query);
		return Access(markUsed, (Func<MemoryCatalog, List<MemoryEntry>>)delegate(MemoryCatalog c)
		{
			List<MemoryEntry> eligible = c.Entries.Where((MemoryEntry e) => e.State == "active" && (e.Scope == "personal" || e.Scope == scope)).ToList();
			Dictionary<string, double> weights = words.ToDictionary((string w) => w, (string w) => 1.0 + Math.Log(1.0 + (double)eligible.Count / (1.0 + (double)eligible.Count((MemoryEntry e) => (e.Topic + " " + e.Value).Contains(w, StringComparison.OrdinalIgnoreCase)))));
			List<MemoryEntry> list = new List<MemoryEntry>();
			int num = 0;
			foreach (var item in from e in eligible
				select (Entry: e, Score: Score(e)) into p
				where p.Score > 0.0
				orderby p.Score descending, p.Entry.Updated descending
				select p)
			{
				int num2 = item.Entry.Topic.Length + item.Entry.Value.Length + (item.Entry.Sources.LastOrDefault()?.Quote.Length ?? 0) + 100;
				if (num2 + num <= budget)
				{
					list.Add(item.Entry);
					num += num2;
					if (markUsed)
					{
						item.Entry.LastUsed = DateTimeOffset.Now;
					}
					if (list.Count >= 8)
					{
						break;
					}
				}
			}
			return list;
			double Score(MemoryEntry entry)
			{
				HashSet<string> second = Words(entry.Topic.Replace('_', ' ') + " " + entry.Value + " " + string.Join(' ', entry.Sources.Select((MemorySource s) => s.Quote)));
				double num3 = words.Intersect(second).Sum((string w) => weights[w]);
				if (semantic != null && semantic.TryGetValue(entry.Id, out var value) && value >= 0.35f)
				{
					num3 += 8.0 * ((double)value - 0.25);
				}
				bool flag = Regex.IsMatch(query, "(?i)remember|recall|preference|about me|what do you know");
				if (entry.Pinned)
				{
					num3 += 100.0;
				}
				else if (entry.Kind == "preference" && semantic == null)
				{
					num3 += 2.0;
				}
				else if (flag)
				{
					num3 += 0.5;
				}
				if (!(num3 > 0.0))
				{
					return 0.0;
				}
				return num3 + ((entry.Scope == scope) ? 0.25 : 0.0) + 0.25 / (1.0 + Math.Max(0.0, (DateTimeOffset.Now - entry.Updated).TotalDays));
			}
		});
	}

	public MemoryChange Apply(IEnumerable<MemoryCandidate> candidates, string workspace, ChatSession session, Turn turn)
	{
		string project = ScopeFor(workspace);
		string user = Normalize(turn.User);
		MemoryChange memoryChange = Access(write: true, delegate(MemoryCatalog catalog)
		{
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			List<string> list = new List<string>();
			foreach (MemoryCandidate input in candidates.Take(3))
			{
				string topic = TopicKey(input.Topic);
				string value = Normalize(input.Value);
				string text = Normalize(input.Quote);
				bool flag = project == "personal" || Regex.IsMatch(user, "(?i)across all (projects|chats)|in (all|every) (projects?|chats?|conversations?)|personal preference|globally");
				string scope = ((input.Scope == "personal" && flag) ? "personal" : project);
				bool flag2 = topic.Length < 3;
				if (!flag2)
				{
					int length = value.Length;
					bool flag3 = ((length < 2 || length > 300) ? true : false);
					flag2 = flag3;
				}
				bool flag4 = flag2;
				if (!flag4)
				{
					int length = text.Length;
					bool flag3 = ((length < 8 || length > 500) ? true : false);
					flag4 = flag3;
				}
				if (flag4 || input.Confidence < 0.85 || !double.IsFinite(input.Confidence) || !Kinds.Contains(input.Kind) || !user.Contains(text, StringComparison.OrdinalIgnoreCase) || !text.Contains(value, StringComparison.OrdinalIgnoreCase))
				{
					num4++;
				}
				else if (catalog.Entries.Any((MemoryEntry e) => e.State == "forgotten" && e.Scope == scope && e.Topic == topic))
				{
					num4++;
				}
				else
				{
					MemoryEntry memoryEntry = catalog.Entries.FirstOrDefault((MemoryEntry e) => e.State == "active" && e.Scope == scope && e.Topic == topic && e.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
					MemorySource memorySource = new MemorySource
					{
						ChatId = session.Id,
						TurnId = turn.Id,
						Quote = text,
						At = turn.Created
					};
					if (memoryEntry != null)
					{
						if (!memoryEntry.Sources.Any((MemorySource s) => s.ChatId == session.Id && s.TurnId == turn.Id))
						{
							memoryEntry.Sources.Add(memorySource);
							memoryEntry.Confirmations++;
							memoryEntry.Updated = DateTimeOffset.Now;
							if (memoryEntry.Sources.Count > 8)
							{
								memoryEntry.Sources.RemoveAt(0);
							}
						}
						list.Add(memoryEntry.Id);
					}
					else if (catalog.Entries.Count >= 5000)
					{
						num4++;
					}
					else
					{
						List<MemoryEntry> list2 = catalog.Entries.Where((MemoryEntry e) => e.Scope == scope && e.State == "active" && (e.Topic == topic || input.Replaces.Contains(e.Id))).ToList();
						MemoryEntry obj = new MemoryEntry
						{
							Scope = scope,
							Topic = topic,
							Value = value,
							Kind = input.Kind
						};
						int length = 1;
						List<MemorySource> list3 = new List<MemorySource>(length);
						CollectionsMarshal.SetCount(list3, length);
						Span<MemorySource> span = CollectionsMarshal.AsSpan(list3);
						int num5 = 0;
						span[num5] = memorySource;
						num5++;
						obj.Sources = list3;
						obj.State = (list2.Any((MemoryEntry e) => e.Pinned) ? "review" : "active");
						MemoryEntry memoryEntry2 = obj;
						if (memoryEntry2.State == "review")
						{
							num3++;
						}
						else
						{
							foreach (MemoryEntry item in list2)
							{
								item.State = "superseded";
								item.ReplacedBy = memoryEntry2.Id;
								item.Updated = DateTimeOffset.Now;
							}
							if (list2.Count > 0)
							{
								num2++;
							}
							else
							{
								num++;
							}
						}
						catalog.Entries.Add(memoryEntry2);
						list.Add(memoryEntry2.Id);
					}
				}
			}
			return new MemoryChange(num, num2, num3, num4, list);
		});
		if (memoryChange.Ids.Count > 0)
		{
			Action? action = this.Changed;
			if (action == null)
			{
				return memoryChange;
			}
			action();
		}
		return memoryChange;
	}

	public MemoryEntry SaveManual(string? id, string workspace, bool personal, string topic, string value, string kind, bool pinned)
	{
		topic = TopicKey(topic);
		value = Normalize(value);
		bool flag = topic.Length < 3;
		if (!flag)
		{
			int length = value.Length;
			bool flag2 = ((length < 2 || length > 1000) ? true : false);
			flag = flag2;
		}
		if (flag || !Kinds.Contains(kind))
		{
			throw new ArgumentException("Enter a topic of at least 3 characters, a value of 2–1,000 characters and a valid kind.");
		}
		MemoryEntry result = Access(write: true, delegate(MemoryCatalog c)
		{
			MemoryEntry memoryEntry = c.Entries.FirstOrDefault((MemoryEntry e) => e.Id == id);
			string scope = (personal ? "personal" : ScopeFor(workspace));
			if (!personal && scope == "personal")
			{
				throw new ArgumentException("Choose a project folder in the main window before adding project memory.");
			}
			MemoryEntry entry = memoryEntry ?? new MemoryEntry();
			if (memoryEntry == null && c.Entries.Count >= 5000)
			{
				throw new IOException("Memory catalog reached 5,000 entries.");
			}
			foreach (MemoryEntry item in c.Entries.Where(delegate(MemoryEntry e)
			{
				bool flag3 = e.Id != entry.Id && e.Scope == scope && e.Topic == topic;
				if (flag3)
				{
					bool flag4;
					switch (e.State)
					{
					case "active":
					case "review":
					case "forgotten":
						flag4 = true;
						break;
					default:
						flag4 = false;
						break;
					}
					flag3 = flag4;
				}
				return flag3;
			}))
			{
				item.State = "superseded";
				item.ReplacedBy = entry.Id;
				item.Updated = DateTimeOffset.Now;
			}
			entry.Scope = scope;
			entry.Topic = topic;
			entry.Value = value;
			entry.Kind = kind;
			entry.Pinned = pinned;
			entry.State = "active";
			entry.Origin = "manual";
			entry.Updated = DateTimeOffset.Now;
			if (memoryEntry == null)
			{
				c.Entries.Add(entry);
			}
			return entry;
		});
		Action? action = this.Changed;
		if (action != null)
		{
			action();
			return result;
		}
		return result;
	}

	public void Forget(string id)
	{
		Access(write: true, delegate(MemoryCatalog c)
		{
			MemoryEntry target = c.Entries.FirstOrDefault((MemoryEntry e) => e.Id == id) ?? throw new ArgumentException("Memory no longer exists.");
			foreach (MemoryEntry item in c.Entries.Where((MemoryEntry e) => e.Scope == target.Scope && e.Topic == target.Topic))
			{
				item.State = "forgotten";
				item.Value = "";
				item.Sources.Clear();
				item.Pinned = false;
				item.ReplacedBy = "";
				item.Updated = DateTimeOffset.Now;
			}
			return true;
		});
		this.Changed?.Invoke();
	}

	public void AllowRelearning(string id)
	{
		Access(write: true, delegate(MemoryCatalog c)
		{
			MemoryEntry target = c.Entries.FirstOrDefault((MemoryEntry e) => e.Id == id) ?? throw new ArgumentException("Memory no longer exists.");
			foreach (MemoryEntry item in c.Entries.Where((MemoryEntry e) => e.Scope == target.Scope && e.Topic == target.Topic && e.State == "forgotten"))
			{
				item.State = "superseded";
			}
			return true;
		});
	}
}
