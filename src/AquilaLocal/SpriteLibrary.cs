using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AquilaLocal;

/// <summary>What an animation is used for. The four base states play automatically; everything else
/// is a one-shot gesture picked from what the assistant says.</summary>
public enum SpriteKind
{
	Idle,
	Talking,
	Thinking,
	Listening,
	Gesture
}

/// <summary>Optional per-animation settings, stored as animation.json inside the animation's folder.
/// Written by the Animations window; users never need to edit it.</summary>
public sealed class SpriteMeta
{
	public double Speed { get; set; } = 1.0;

	public bool? Loop { get; set; }

	public string Description { get; set; } = "";
}

public sealed class SpriteAnimation
{
	public string Name { get; init; } = "";

	public string Folder { get; init; } = "";

	public List<string> Frames { get; init; } = new List<string>();

	public SpriteMeta Meta { get; init; } = new SpriteMeta();

	public SpriteKind Kind => SpriteLibrary.KindOf(Name);

	/// <summary>Base states loop by default; gestures play once.</summary>
	public bool Loops => Meta.Loop ?? (Kind != SpriteKind.Gesture);

	/// <summary>Frames per second: 6 is a calm default for hand-made frames.</summary>
	public double Fps => Math.Clamp(6.0 * Meta.Speed, 0.5, 30.0);

	/// <summary>Text the meaning matcher compares sentences against.</summary>
	public string MatchText => string.IsNullOrWhiteSpace(Meta.Description) ? Name : (Name + ". " + Meta.Description);
}

/// <summary>
/// The avatar's animations, read from a folder: one subfolder per animation, named in plain words,
/// containing its pictures in order. Example: avatar/waving hello/01.png, 02.png, 03.png.
/// </summary>
public static class SpriteLibrary
{
	public static readonly string[] ImageExtensions = new string[5] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

	private const string MetaFile = "animation.json";

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	public static bool IsImage(string path)
	{
		return ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
	}

	public static SpriteKind KindOf(string name)
	{
		string text = name.Trim().ToLowerInvariant();
		if (text.StartsWith("idle"))
		{
			return SpriteKind.Idle;
		}
		if (text.StartsWith("talk") || text.StartsWith("speak"))
		{
			return SpriteKind.Talking;
		}
		if (text.StartsWith("think"))
		{
			return SpriteKind.Thinking;
		}
		if (text.StartsWith("listen"))
		{
			return SpriteKind.Listening;
		}
		return SpriteKind.Gesture;
	}

	/// <summary>Sorts names like "frame2" before "frame10".</summary>
	public static int NaturalCompare(string a, string b)
	{
		string[] array = Regex.Split(a.ToLowerInvariant(), "(\\d+)");
		string[] array2 = Regex.Split(b.ToLowerInvariant(), "(\\d+)");
		for (int i = 0; i < Math.Min(array.Length, array2.Length); i++)
		{
			int num = ((long.TryParse(array[i], out var result) && long.TryParse(array2[i], out var result2)) ? result.CompareTo(result2) : string.CompareOrdinal(array[i], array2[i]));
			if (num != 0)
			{
				return num;
			}
		}
		return array.Length.CompareTo(array2.Length);
	}

	public static List<string> FramesIn(string folder)
	{
		if (!Directory.Exists(folder))
		{
			return new List<string>();
		}
		List<string> list = Directory.EnumerateFiles(folder).Where(IsImage).ToList();
		list.Sort((string x, string y) => NaturalCompare(Path.GetFileName(x), Path.GetFileName(y)));
		return list;
	}

	public static List<SpriteAnimation> Load(string root)
	{
		List<SpriteAnimation> list = new List<SpriteAnimation>();
		if (!Directory.Exists(root))
		{
			return list;
		}
		foreach (string item in Directory.EnumerateDirectories(root))
		{
			List<string> list2 = FramesIn(item);
			if (list2.Count != 0)
			{
				list.Add(new SpriteAnimation
				{
					Name = Path.GetFileName(item),
					Folder = item,
					Frames = list2,
					Meta = ReadMeta(item)
				});
			}
		}
		list.Sort((SpriteAnimation x, SpriteAnimation y) => (x.Kind != y.Kind) ? x.Kind.CompareTo(y.Kind) : NaturalCompare(x.Name, y.Name));
		return list;
	}

	public static SpriteMeta ReadMeta(string folder)
	{
		try
		{
			string path = Path.Combine(folder, MetaFile);
			if (File.Exists(path))
			{
				return JsonSerializer.Deserialize<SpriteMeta>(File.ReadAllText(path)) ?? new SpriteMeta();
			}
		}
		catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
		{
		}
		return new SpriteMeta();
	}

	public static void WriteMeta(string folder, SpriteMeta meta)
	{
		File.WriteAllText(Path.Combine(folder, MetaFile), JsonSerializer.Serialize(meta, Json));
	}

	public static string SafeName(string name)
	{
		string text = new string(name.Trim().Select((char c) => (Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c)).ToArray());
		text = Regex.Replace(text, "\\s+", " ").Trim().Trim('.');
		if (text.Length == 0)
		{
			throw new ArgumentException("Give the animation a name, for example \"waving hello\".");
		}
		return (text.Length > 60) ? text.Substring(0, 60).Trim() : text;
	}

	/// <summary>Creates (or replaces the pictures of) an animation from the given picture files, copying
	/// them in order as 01.png, 02.png, … so the order is obvious in Explorer too.</summary>
	public static string SaveFrames(string root, string name, IReadOnlyList<string> files)
	{
		string text = Path.Combine(root, SafeName(name));
		Directory.CreateDirectory(text);
		string text2 = Path.Combine(text, ".incoming");
		Directory.CreateDirectory(text2);
		List<string> list = new List<string>();
		for (int i = 0; i < files.Count; i++)
		{
			string text3 = Path.Combine(text2, $"{i + 1:00}{Path.GetExtension(files[i]).ToLowerInvariant()}");
			File.Copy(files[i], text3, overwrite: true);
			list.Add(text3);
		}
		foreach (string item in FramesIn(text))
		{
			File.Delete(item);
		}
		foreach (string item2 in list)
		{
			File.Move(item2, Path.Combine(text, Path.GetFileName(item2)), overwrite: true);
		}
		Directory.Delete(text2, recursive: true);
		return text;
	}

	public static string Rename(SpriteAnimation animation, string newName)
	{
		string text = SafeName(newName);
		string text2 = Path.Combine(Path.GetDirectoryName(animation.Folder), text);
		if (string.Equals(animation.Folder, text2, StringComparison.Ordinal))
		{
			return text2;
		}
		if (Directory.Exists(text2) && !string.Equals(animation.Folder, text2, StringComparison.OrdinalIgnoreCase))
		{
			throw new IOException("An animation called \"" + text + "\" already exists.");
		}
		if (string.Equals(animation.Folder, text2, StringComparison.OrdinalIgnoreCase))
		{
			string text3 = animation.Folder + ".renaming";
			Directory.Move(animation.Folder, text3);
			Directory.Move(text3, text2);
		}
		else
		{
			Directory.Move(animation.Folder, text2);
		}
		return text2;
	}

	/// <summary>
	/// Imports a folder of pictures. Subfolders become animations as they are. Loose pictures are grouped
	/// by name, so wave_1.png, wave_2.png, eating-01.png, eating-02.png become "wave" and "eating".
	/// Returns the names of the animations created.
	/// </summary>
	public static List<string> Import(string source, string root)
	{
		List<string> list = new List<string>();
		foreach (string item in Directory.EnumerateDirectories(source))
		{
			List<string> list2 = FramesIn(item);
			if (list2.Count > 0)
			{
				string name = Path.GetFileName(item);
				SaveFrames(root, name, list2);
				SpriteMeta spriteMeta = ReadMeta(item);
				WriteMeta(Path.Combine(root, SafeName(name)), spriteMeta);
				list.Add(SafeName(name));
			}
		}
		foreach (IGrouping<string, string> item2 in from f in FramesIn(source)
			group f by GroupName(Path.GetFileNameWithoutExtension(f)))
		{
			List<string> list3 = item2.ToList();
			list3.Sort((string x, string y) => NaturalCompare(Path.GetFileName(x), Path.GetFileName(y)));
			SaveFrames(root, item2.Key, list3);
			list.Add(SafeName(item2.Key));
		}
		return list;
	}

	/// <summary>"wave_01" → "wave", "Eating 3" → "Eating", "blink" → "blink".</summary>
	public static string GroupName(string fileName)
	{
		string text = Regex.Replace(fileName, "[\\s_\\-.]*(frame)?[\\s_\\-.]*\\d+$", "", RegexOptions.IgnoreCase).Replace('_', ' ').Replace('-', ' ').Trim();
		return (text.Length == 0) ? "animation" : text;
	}
}
