using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AquilaAvatarProtocol;

namespace AquilaLocal;

public static class AvatarCatalog
{
	public static string BundledFolder => Path.Combine(AppContext.BaseDirectory, "avatar", "Models");

	public static string DefaultPath => Path.Combine(BundledFolder, File.Exists(Path.Combine(BundledFolder, "Mira.vrm")) ? "Mira.vrm" : "aquila-guide.vrm");

	public static string Resolve(string key)
	{
		if (key == null || key.Length != 0)
		{
			if (key == "builtin:guide")
			{
				return Path.Combine(BundledFolder, "aquila-guide.vrm");
			}
			return Path.GetFullPath(key);
		}
		return DefaultPath;
	}

	public static string KeyFor(string path)
	{
		if (!SamePath(path, DefaultPath))
		{
			if (!SamePath(path, Resolve("builtin:guide")))
			{
				return Path.GetFullPath(path);
			}
			return "builtin:guide";
		}
		return "";
	}

	public static bool SamePath(string a, string b)
	{
		return Path.GetFullPath(a).Equals(Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
	}

	public static List<AvatarChoice> List(string folder, string selected)
	{
		List<AvatarChoice> list = new List<AvatarChoice>
		{
			new AvatarChoice("", Path.GetFileNameWithoutExtension(DefaultPath) + " · bundled")
		};
		if (!SamePath(DefaultPath, Resolve("builtin:guide")))
		{
			list.Add(new AvatarChoice("builtin:guide", "Aquila Guide · bundled"));
		}
		if (!string.IsNullOrWhiteSpace(folder))
		{
			foreach (string item in Directory.EnumerateFiles(folder, "*.vrm", SearchOption.TopDirectoryOnly).Take(200).OrderBy<string, string>(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
			{
				string key = KeyFor(item);
				if (list.All((AvatarChoice c) => c.Key != key))
				{
					list.Add(new AvatarChoice(key, Path.GetFileNameWithoutExtension(item)));
				}
			}
		}
		if (list.All((AvatarChoice c) => c.Key != selected))
		{
			list.Add(new AvatarChoice(selected, Path.GetFileNameWithoutExtension(selected) + " · saved selection"));
		}
		return list;
	}

	public static AvatarModelInfo Inspect(string key)
	{
		return AvatarModelFile.Inspect(Resolve(key));
	}
}
