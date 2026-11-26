using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AquilaLocal;

public sealed class Storage
{
	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		WriteIndented = true,
		NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
	};

	public string Root { get; }

	public string? RecoveryNotice { get; private set; }

	public Storage(string? root = null)
	{
		Root = root ?? Path.Combine(AppContext.BaseDirectory, "Data");
		Directory.CreateDirectory(Root);
		Directory.CreateDirectory(Path.Combine(Root, "chats"));
		Directory.CreateDirectory(Path.Combine(Root, "logs"));
	}

	public Settings LoadSettings()
	{
		Settings settings = Read<Settings>(Path.Combine(Root, "settings.json")) ?? new Settings();
		bool migrated = settings.Migrate();
		try
		{
			settings.Validate();
			if (migrated && File.Exists(Path.Combine(Root, "settings.json")))
			{
				File.Copy(Path.Combine(Root, "settings.json"), Path.Combine(Root, "settings.json.before-2.1"), overwrite: true);
				SaveSettings(settings);
			}
		}
		catch
		{
			RecoveryNotice = "Invalid settings were reset. Your original settings file is still in Data.";
			settings = new Settings();
		}
		if (string.IsNullOrEmpty(settings.ModelPath) || !File.Exists(settings.ModelPath))
		{
			settings.ModelPath = (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "models")) ? ((from p in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "models"), "*.gguf")
				where !Path.GetFileName(p).Contains("mmproj", StringComparison.OrdinalIgnoreCase)
				select p).FirstOrDefault() ?? "") : "");
		}
		if (!string.IsNullOrEmpty(settings.ProjectorPath) && !File.Exists(settings.ProjectorPath))
		{
			settings.ProjectorPath = "";
		}
		if (string.IsNullOrEmpty(settings.ProjectorPath) && !settings.VisionDisabled)
		{
			settings.ProjectorPath = ModelInfo.FindProjector(settings.ModelPath);
		}
		return settings;
	}

	public List<ChatSession> LoadChats()
	{
		List<ChatSession> list = new List<ChatSession>();
		foreach (string item in Directory.EnumerateFiles(Path.Combine(Root, "chats"), "*.json"))
		{
			ChatSession chatSession = Read<ChatSession>(item);
			if (chatSession == null)
			{
				continue;
			}
			foreach (Turn item2 in chatSession.Turns.Where((Turn t) => t.Status == "generating"))
			{
				item2.Status = "interrupted";
			}
			list.Add(chatSession);
		}
		return list.OrderByDescending((ChatSession s) => s.Updated).ToList();
	}

	private T? Read<T>(string path)
	{
		if (!File.Exists(path))
		{
			return default(T);
		}
		try
		{
			return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
		}
		catch (JsonException)
		{
			string destFileName = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
			File.Move(path, destFileName);
			RecoveryNotice = "An unreadable data file was preserved as a .corrupt backup in Data.";
			return default(T);
		}
	}

	public void SaveSettings(Settings settings)
	{
		settings.Validate();
		Write(Path.Combine(Root, "settings.json"), settings);
	}

	public void SaveChat(ChatSession session)
	{
		session.Updated = DateTimeOffset.Now;
		Write(Path.Combine(Root, "chats", session.Id + ".json"), session);
	}

	public void DeleteChat(string id)
	{
		if (!Guid.TryParseExact(id, "N", out var _))
		{
			throw new ArgumentException("Invalid conversation ID; no files were deleted.");
		}
		string text = Path.Combine(Root, "chats", id + ".json");
		File.Delete(text + ".tmp");
		File.Delete(text);
	}

	private static void Write<T>(string path, T value)
	{
		string text = path + ".tmp";
		File.WriteAllText(text, JsonSerializer.Serialize(value, Json));
		File.Move(text, path, overwrite: true);
	}
}
