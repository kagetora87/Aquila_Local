using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AquilaLocal;

/// <summary>One message of an imported conversation: who said it, what, and when (if the file says).</summary>
public sealed record ImportedMessage(bool FromUser, string Text, DateTimeOffset? At);

public sealed record ImportedChat(string Title, List<ImportedMessage> Messages, string Format);

/// <summary>
/// Reads conversations saved by Aquila or by other chat apps, so they can be continued here:
/// Aquila chats (.json) and exports (.md), SillyTavern chats (.jsonl), and ChatGPT or Claude.ai data exports
/// (conversations.json). Anything else is refused with a plain explanation, never guessed at.
/// </summary>
public static class ChatImporter
{
	public const string Supported = "Aquila chats (.json) and exports (.md), SillyTavern chats (.jsonl), and the conversations.json file from a ChatGPT or Claude.ai data export.";

	public static List<ImportedChat> Read(string path)
	{
		string text = File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿');
		string name = Path.GetFileNameWithoutExtension(path);
		List<ImportedChat> chats;
		if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
		{
			chats = FromAquilaMarkdown(text, name);
		}
		else if (path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
		{
			chats = FromSillyTavern(text, name);
		}
		else
		{
			chats = FromJson(text, name);
		}
		chats = chats.Where((ImportedChat c) => c.Messages.Any((ImportedMessage m) => m.Text.Length > 0)).ToList();
		if (chats.Count == 0)
		{
			throw new InvalidDataException("No conversation was found in “" + Path.GetFileName(path) + "”. Aquila can import " + Supported);
		}
		return chats;
	}

	/// <summary>Turns messages into Aquila's you-then-reply turns. Several messages in a row from one side are joined;
	/// a reply with no message before it (she spoke first) becomes a turn with no message from you.</summary>
	public static List<(string User, string Answer, DateTimeOffset? At)> Pair(IEnumerable<ImportedMessage> messages)
	{
		var turns = new List<(string User, string Answer, DateTimeOffset? At)>();
		string? user = null;
		string? answer = null;
		DateTimeOffset? at = null;
		foreach (ImportedMessage m in messages.Where((ImportedMessage m) => m.Text.Trim().Length > 0))
		{
			string text = m.Text.Trim();
			if (m.FromUser)
			{
				if (answer != null)
				{
					turns.Add((user ?? "", answer, at));
					user = null;
					answer = null;
					at = null;
				}
				user = (user == null) ? text : (user + "\n\n" + text);
				at ??= m.At;
			}
			else
			{
				answer = (answer == null) ? text : (answer + "\n\n" + text);
				at ??= m.At;
			}
		}
		if (user != null || answer != null)
		{
			turns.Add((user ?? "", answer ?? "", at));
		}
		return turns;
	}

	// ---------------- Aquila's own Markdown export ----------------

	private static readonly Regex Heading = new Regex("^## (.+)$", RegexOptions.Multiline);

	private static readonly Regex Footer = new Regex("\\n_[a-z]+ · [^\\n]*_\\s*$");

	private static List<ImportedChat> FromAquilaMarkdown(string text, string fallbackTitle)
	{
		text = text.Replace("\r\n", "\n");
		Match title = Regex.Match(text, "^# (.+)$", RegexOptions.Multiline);
		List<Match> headings = Heading.Matches(text).Cast<Match>().Where((Match h) => h.Groups[1].Value.Trim() != "Context summary").ToList();
		if (!headings.Any((Match h) => h.Groups[1].Value.Trim() == "You"))
		{
			throw new InvalidDataException("This text file isn't an Aquila export (it has no “## You” sections). Aquila can import " + Supported);
		}
		var messages = new List<ImportedMessage>();
		for (int i = 0; i < headings.Count; i++)
		{
			int start = headings[i].Index + headings[i].Length;
			int end = (i + 1 < headings.Count) ? headings[i + 1].Index : text.Length;
			string body = text.Substring(start, end - start).Trim();
			bool fromUser = headings[i].Groups[1].Value.Trim() == "You";
			if (!fromUser)
			{
				body = Footer.Replace("\n" + body, "").Trim();
			}
			messages.Add(new ImportedMessage(fromUser, body, null));
		}
		return new List<ImportedChat> { new ImportedChat(title.Success ? title.Groups[1].Value.Trim() : fallbackTitle, messages, "Aquila export") };
	}

	// ---------------- SillyTavern (.jsonl: a header line, then one message per line) ----------------

	private static List<ImportedChat> FromSillyTavern(string text, string fallbackTitle)
	{
		var messages = new List<ImportedMessage>();
		string character = "";
		foreach (string line in text.Split('\n').Select((string l) => l.Trim()).Where((string l) => l.Length > 0))
		{
			using JsonDocument doc = Parse(line, "SillyTavern chat");
			JsonElement e = doc.RootElement;
			if (e.ValueKind != JsonValueKind.Object)
			{
				continue;
			}
			if (Str(e, "character_name") is string c && !e.TryGetProperty("mes", out _))
			{
				character = c;
				continue;
			}
			if (Str(e, "mes") is not string mes || (e.TryGetProperty("is_system", out JsonElement sys) && sys.ValueKind == JsonValueKind.True))
			{
				continue;
			}
			bool fromUser = e.TryGetProperty("is_user", out JsonElement u) && u.ValueKind == JsonValueKind.True;
			messages.Add(new ImportedMessage(fromUser, Actions(mes), Time(e, "send_date")));
		}
		return new List<ImportedChat> { new ImportedChat(character.Length > 0 ? ("Chat with " + character) : fallbackTitle, messages, "SillyTavern") };
	}

	private static readonly Regex Starred = new Regex("(?<![\\w*])\\*(?!\\s)([^*\\n]+?)(?<!\\s)\\*(?![\\w*])");

	/// <summary>Roleplay chats write actions *like this*; Aquila writes them as action tags so they show as actions.</summary>
	public static string Actions(string text)
	{
		return Starred.Replace(text, "<action>$1</action>");
	}

	// ---------------- JSON: Aquila chat, ChatGPT export, Claude.ai export ----------------

	private static List<ImportedChat> FromJson(string text, string fallbackTitle)
	{
		using JsonDocument doc = Parse(text, "chat file");
		JsonElement root = doc.RootElement;
		if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Turns", out JsonElement turns) && turns.ValueKind == JsonValueKind.Array)
		{
			return new List<ImportedChat> { FromAquilaJson(root, fallbackTitle) };
		}
		List<JsonElement> conversations = (root.ValueKind == JsonValueKind.Array) ? root.EnumerateArray().ToList() : new List<JsonElement> { root };
		var result = new List<ImportedChat>();
		foreach (JsonElement c in conversations.Where((JsonElement c) => c.ValueKind == JsonValueKind.Object))
		{
			if (c.TryGetProperty("mapping", out JsonElement mapping) && mapping.ValueKind == JsonValueKind.Object)
			{
				result.Add(FromChatGpt(c, mapping, fallbackTitle));
			}
			else if (c.TryGetProperty("chat_messages", out JsonElement claude) && claude.ValueKind == JsonValueKind.Array)
			{
				result.Add(FromClaude(c, claude, fallbackTitle));
			}
		}
		if (result.Count == 0)
		{
			throw new InvalidDataException("Aquila doesn't recognise this kind of file. It can import " + Supported);
		}
		return result;
	}

	private static ImportedChat FromAquilaJson(JsonElement root, string fallbackTitle)
	{
		var messages = new List<ImportedMessage>();
		foreach (JsonElement t in root.GetProperty("Turns").EnumerateArray())
		{
			DateTimeOffset? at = Time(t, "Created");
			bool unprompted = t.TryGetProperty("Unprompted", out JsonElement u) && u.ValueKind == JsonValueKind.True;
			if (!unprompted)
			{
				messages.Add(new ImportedMessage(true, Str(t, "User") ?? "", at));
			}
			messages.Add(new ImportedMessage(false, Str(t, "Answer") ?? "", at));
		}
		return new ImportedChat(Str(root, "Title") ?? fallbackTitle, messages, "Aquila chat");
	}

	/// <summary>ChatGPT keeps every edit as a branch; the conversation as last seen is the path from current_node back to the start.</summary>
	private static ImportedChat FromChatGpt(JsonElement c, JsonElement mapping, string fallbackTitle)
	{
		string? node = Str(c, "current_node");
		if (node == null)
		{
			// No current node: follow the first child from the root.
			node = mapping.EnumerateObject().FirstOrDefault((JsonProperty p) => !p.Value.TryGetProperty("parent", out JsonElement par) || par.ValueKind == JsonValueKind.Null).Name;
			while (node != null && mapping.TryGetProperty(node, out JsonElement n) && n.TryGetProperty("children", out JsonElement kids) && kids.GetArrayLength() > 0)
			{
				node = kids[0].GetString();
			}
		}
		var path = new List<JsonElement>();
		var seen = new HashSet<string>();
		while (node != null && seen.Add(node) && mapping.TryGetProperty(node, out JsonElement n))
		{
			path.Add(n);
			node = n.TryGetProperty("parent", out JsonElement parent) && parent.ValueKind == JsonValueKind.String ? parent.GetString() : null;
		}
		path.Reverse();
		var messages = new List<ImportedMessage>();
		foreach (JsonElement n in path)
		{
			if (!n.TryGetProperty("message", out JsonElement m) || m.ValueKind != JsonValueKind.Object)
			{
				continue;
			}
			string role = m.TryGetProperty("author", out JsonElement a) ? (Str(a, "role") ?? "") : "";
			if (role != "user" && role != "assistant")
			{
				continue;
			}
			if (m.TryGetProperty("metadata", out JsonElement meta) && meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("is_visually_hidden_from_conversation", out JsonElement hidden) && hidden.ValueKind == JsonValueKind.True)
			{
				continue;
			}
			string body = "";
			if (m.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Object)
			{
				string type = Str(content, "content_type") ?? "text";
				if (type != "text" && type != "multimodal_text")
				{
					continue;
				}
				if (content.TryGetProperty("parts", out JsonElement parts) && parts.ValueKind == JsonValueKind.Array)
				{
					body = string.Join("\n\n", parts.EnumerateArray().Where((JsonElement p) => p.ValueKind == JsonValueKind.String).Select((JsonElement p) => p.GetString()));
				}
			}
			messages.Add(new ImportedMessage(role == "user", body, Unix(m, "create_time")));
		}
		return new ImportedChat(Str(c, "title") ?? fallbackTitle, messages, "ChatGPT");
	}

	private static ImportedChat FromClaude(JsonElement c, JsonElement list, string fallbackTitle)
	{
		var messages = new List<ImportedMessage>();
		foreach (JsonElement m in list.EnumerateArray())
		{
			string sender = Str(m, "sender") ?? "";
			string body = Str(m, "text") ?? "";
			if (body.Length == 0 && m.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array)
			{
				body = string.Join("\n\n", content.EnumerateArray().Where((JsonElement p) => Str(p, "type") == "text").Select((JsonElement p) => Str(p, "text") ?? ""));
			}
			if (sender == "human" || sender == "assistant")
			{
				messages.Add(new ImportedMessage(sender == "human", body, Time(m, "created_at")));
			}
		}
		string title = Str(c, "name") ?? "";
		return new ImportedChat(title.Length > 0 ? title : fallbackTitle, messages, "Claude.ai");
	}

	// ---------------- helpers ----------------

	private static JsonDocument Parse(string text, string what)
	{
		try
		{
			return JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
		}
		catch (JsonException ex)
		{
			throw new InvalidDataException("This " + what + " couldn't be read (" + ex.Message + "). Aquila can import " + Supported, ex);
		}
	}

	private static string? Str(JsonElement e, string name)
	{
		return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
	}

	private static DateTimeOffset? Time(JsonElement e, string name)
	{
		if (Str(e, name) is string s && DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out DateTimeOffset at))
		{
			return at;
		}
		if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt64(out long ms))
		{
			return ms > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : DateTimeOffset.FromUnixTimeSeconds(ms);
		}
		return null;
	}

	private static DateTimeOffset? Unix(JsonElement e, string name)
	{
		if (e.TryGetProperty(name, out JsonElement n) && n.ValueKind == JsonValueKind.Number && n.TryGetDouble(out double seconds) && seconds > 0)
		{
			return DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000.0)).ToLocalTime();
		}
		return null;
	}
}
