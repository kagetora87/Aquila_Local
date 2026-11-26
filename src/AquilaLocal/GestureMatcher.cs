using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AquilaLocal;

/// <summary>
/// Picks a gesture for what the assistant is saying. Each finished sentence of a reply is compared
/// with every gesture's name (and optional "when to use it" text) using the small MiniLM meaning model
/// that also powers memory search. It runs on the CPU in a few milliseconds, works with any chat model
/// and adds nothing to the prompt. A literal word match ("eat" in "eating") counts as a strong match.
/// </summary>
public sealed class GestureMatcher : IDisposable
{
	public sealed record GestureMatch(string Name, double Score);

	private readonly string modelDirectory;

	private readonly object gate = new object();

	private readonly StringBuilder pending = new StringBuilder();

	private EmbeddingModel? model;

	private sealed record Target(SpriteAnimation Animation, List<float[]> Vectors, string[] NameStems, string[] DescriptionStems);

	private List<Target> targets = new List<Target>();

	private DateTime lastFired = DateTime.MinValue;

	private bool disposed;

	/// <summary>0 = only obvious moments, 1 = very expressive.</summary>
	public double Sensitivity { get; set; } = 0.5;

	public TimeSpan Cooldown { get; set; } = TimeSpan.FromSeconds(4.0);

	public event Action<string>? Fired;

	public GestureMatcher(string modelDirectory)
	{
		this.modelDirectory = modelDirectory;
	}

	/// <summary>Similarity needed to fire: 0.62 at the lowest sensitivity, 0.44 in the middle, 0.26 at the highest.</summary>
	public double Threshold => 0.62 - 0.36 * Math.Clamp(Sensitivity, 0.0, 1.0);

	/// <summary>Score for a sentence that literally uses the gesture's name ("eating").</summary>
	private const double NameWordScore = 0.75;

	/// <summary>Score for a sentence that uses a word from "when should it play" ("breakfast").</summary>
	private const double DescriptionWordScore = 0.6;

	public void SetAnimations(IEnumerable<SpriteAnimation> animations)
	{
		List<SpriteAnimation> list = animations.Where((SpriteAnimation a) => a.Kind == SpriteKind.Gesture).ToList();
		Task.Run(delegate
		{
			List<Target> value = list.Select(BuildTarget).ToList();
			lock (gate)
			{
				targets = value;
			}
		});
	}

	/// <summary>
	/// Each phrase of the description ("saying hi, hello, welcome or goodbye") is compared on its
	/// own, because one long list blurs the meaning of every item in it.
	/// </summary>
	private Target BuildTarget(SpriteAnimation animation)
	{
		List<string> phrases = new List<string> { animation.Name };
		string description = animation.Meta.Description ?? "";
		if (!string.IsNullOrWhiteSpace(description))
		{
			phrases.Add(animation.Name + ". " + description);
			phrases.AddRange(from p in Regex.Split(description, "[,;/\\n]|\\bor\\b|\\band\\b", RegexOptions.IgnoreCase)
				select p.Trim() into p
				where p.Length >= 3
				select p);
		}
		List<float[]> vectors = (from p in phrases.Distinct(StringComparer.OrdinalIgnoreCase)
			select Encode(p) into v
			where v != null
			select v).ToList();
		string[] nameStems = Stems(animation.Name);
		string[] descriptionStems = Stems(description).Except(nameStems).ToArray();
		return new Target(animation, vectors, nameStems, descriptionStems);
	}

	/// <summary>Call at the start of each reply.</summary>
	public void Reset()
	{
		lock (gate)
		{
			pending.Clear();
		}
	}

	/// <summary>Feed streamed reply text; finished sentences are checked in the background.</summary>
	public void Feed(string text)
	{
		List<string> list = new List<string>();
		lock (gate)
		{
			pending.Append(text);
			string text2 = pending.ToString();
			int num = 0;
			for (int i = 0; i < text2.Length; i++)
			{
				char c = text2[i];
				if ((c == '.' || c == '!' || c == '?' || c == '\n') && (i + 1 >= text2.Length || char.IsWhiteSpace(text2[i + 1])) && i + 1 < text2.Length)
				{
					string text3 = text2.Substring(num, i + 1 - num).Trim();
					if (text3.Length >= 3)
					{
						list.Add(text3);
					}
					num = i + 1;
				}
			}
			pending.Remove(0, num);
		}
		foreach (string item in list)
		{
			Task.Run(delegate
			{
				Check(item);
			});
		}
	}

	/// <summary>Call when the reply ends so a final sentence without trailing space is checked too.</summary>
	public void Flush()
	{
		string text;
		lock (gate)
		{
			text = pending.ToString().Trim();
			pending.Clear();
		}
		if (text.Length >= 3)
		{
			Task.Run(delegate
			{
				Check(text);
			});
		}
	}

	private void Check(string sentence)
	{
		GestureMatch match = Best(sentence);
		if (match != null && match.Score >= Threshold)
		{
			lock (gate)
			{
				if (DateTime.UtcNow - lastFired < Cooldown)
				{
					return;
				}
				lastFired = DateTime.UtcNow;
			}
			this.Fired?.Invoke(match.Name);
		}
	}

	/// <summary>The best-matching gesture and its score, for the Test box. Null when there are none.</summary>
	public GestureMatch? Best(string sentence)
	{
		List<Target> list;
		lock (gate)
		{
			list = targets;
		}
		if (list.Count == 0 || string.IsNullOrWhiteSpace(sentence))
		{
			return null;
		}
		float[] array = Encode(sentence);
		HashSet<string> words = Stems(sentence).ToHashSet();
		GestureMatch match = null;
		foreach (Target item in list)
		{
			double num = 0.0;
			if (array != null)
			{
				foreach (float[] vector in item.Vectors)
				{
					num = Math.Max(num, EmbeddingModel.Similarity(array, vector));
				}
			}
			if (item.NameStems.Any(words.Contains))
			{
				num = Math.Max(num, NameWordScore);
			}
			else if (item.DescriptionStems.Any(words.Contains))
			{
				num = Math.Max(num, DescriptionWordScore);
			}
			if (match == null || num > match.Score)
			{
				match = new GestureMatch(item.Animation.Name, num);
			}
		}
		return match;
	}

	private float[]? Encode(string text)
	{
		try
		{
			lock (gate)
			{
				if (disposed)
				{
					return null;
				}
				if (model == null)
				{
					model = new EmbeddingModel(modelDirectory);
				}
				return model.Encode(text, CancellationToken.None);
			}
		}
		catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is ArgumentException || ex is UnauthorizedAccessException || ex is DllNotFoundException || ex is TypeInitializationException || ex.GetType().Name == "OnnxRuntimeException")
		{
			return null;
		}
	}

	/// <summary>Crude word stems ("eating" → "eat", "waving" → "wav") for literal matches.</summary>
	private static string[] Stems(string text)
	{
		return (from System.Text.RegularExpressions.Match m in Regex.Matches(text.ToLowerInvariant(), "[a-z]{3,}")
			select m.Value into w
			where !Stop.Contains(w)
			select Stem(w)).Distinct().ToArray();
	}

	private static string Stem(string word)
	{
		string[] array = new string[6] { "ing", "ed", "es", "s", "ly", "er" };
		foreach (string text in array)
		{
			if (word.Length > text.Length + 2 && word.EndsWith(text))
			{
				return word.Substring(0, word.Length - text.Length);
			}
		}
		return word;
	}

	private static readonly HashSet<string> Stop = new HashSet<string>
	{
		"the", "and", "you", "your", "for", "with", "that", "this", "are", "was",
		"but", "not", "have", "has", "can", "will", "just", "from", "what", "when",
		"some", "something", "someone", "saying", "say", "being", "having", "use",
		"like", "about", "them", "they", "then", "there", "here", "really", "very", "much",
		"want", "would", "could", "should", "time", "thing", "things", "one", "all", "out",
		"get", "got", "make", "made", "know", "think", "good", "great", "well", "now", "how",
		"who", "why", "where", "which", "its", "it's", "our", "ours", "his", "her", "him", "she"
	};

	public void Dispose()
	{
		lock (gate)
		{
			disposed = true;
			model?.Dispose();
			model = null;
		}
	}
}
