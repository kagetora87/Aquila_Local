using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AquilaLocal;

/// <summary>
/// A small syntax colourer for code blocks in replies. One combined regex per language family;
/// priority is comment, string, number, keyword, call name, type name, then plain text.
/// Unknown languages are left plain.
/// </summary>
public static class CodeColors
{
	public enum Kind
	{
		Plain,
		Keyword,
		String,
		Number,
		Comment,
		Function,
		Type
	}

	private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250.0);

	private const string CLikeKeywords = "abstract|async|await|bool|break|case|catch|char|class|const|continue|default|delete|do|double|else|enum|export|extends|false|final|finally|float|fn|for|foreach|func|function|go|if|impl|import|in|int|interface|let|long|match|mut|namespace|new|null|nil|override|package|private|protected|public|readonly|return|self|static|string|struct|super|switch|this|throw|true|try|type|typeof|uint|using|var|void|while|yield|undefined|val|when|is|as|from|of|pub|use|mod|trait|where|loop|defer|chan|select|range|map|instanceof|sealed|record|get|set";

	private const string ScriptKeywords = "and|as|assert|async|await|break|class|continue|def|del|elif|else|except|False|finally|for|from|global|if|import|in|is|lambda|None|nonlocal|not|or|pass|raise|return|True|try|while|with|yield|self|print|then|fi|do|done|esac|case|function|local|export|echo|begin|end|module|require|elsif|unless|until|true|false|null|param|foreach|switch";

	private const string SqlKeywords = "select|from|where|and|or|not|insert|into|values|update|set|delete|create|table|drop|alter|add|join|left|right|inner|outer|full|on|as|group|by|order|having|limit|offset|distinct|union|all|null|is|in|like|between|case|when|then|else|end|primary|key|foreign|references|index|view|exists|count|sum|avg|min|max|asc|desc|with|returning|default|unique|constraint";

	private static readonly Dictionary<string, Regex?> Cache = new Dictionary<string, Regex?>();

	private static string Family(string lang)
	{
		switch (lang.Trim().ToLowerInvariant())
		{
		case "c":
		case "h":
		case "cpp":
		case "c++":
		case "cc":
		case "hpp":
		case "cs":
		case "csharp":
		case "c#":
		case "java":
		case "js":
		case "jsx":
		case "javascript":
		case "ts":
		case "tsx":
		case "typescript":
		case "go":
		case "golang":
		case "rust":
		case "rs":
		case "swift":
		case "kotlin":
		case "kt":
		case "php":
		case "dart":
		case "scala":
			return "clike";
		case "py":
		case "python":
		case "rb":
		case "ruby":
		case "sh":
		case "bash":
		case "shell":
		case "zsh":
		case "powershell":
		case "ps1":
		case "pwsh":
		case "yaml":
		case "yml":
		case "toml":
		case "r":
		case "ini":
		case "dockerfile":
			return "script";
		case "sql":
		case "mysql":
		case "postgres":
		case "postgresql":
		case "sqlite":
			return "sql";
		case "html":
		case "xml":
		case "xaml":
		case "svg":
		case "vue":
			return "markup";
		case "json":
		case "jsonc":
			return "json";
		default:
			return "";
		}
	}

	private static Regex? Build(string family)
	{
		string text = "(?<number>\\b(?:0x[0-9a-fA-F]+|\\d+(?:\\.\\d+)?(?:[eE][+-]?\\d+)?)\\b)";
		string text2 = "(?<call>\\b[A-Za-z_][A-Za-z0-9_]*(?=\\s*\\())";
		string text3 = "(?<type>\\b[A-Z][A-Za-z0-9_]*\\b)";
		string pattern;
		RegexOptions regexOptions = RegexOptions.Multiline;
		switch (family)
		{
		case "clike":
			pattern = "(?<comment>//[^\\n]*|/\\*[\\s\\S]*?(?:\\*/|$))|(?<string>@?\"(?:\\\\.|[^\"\\\\\\n])*\"?|'(?:\\\\.|[^'\\\\\\n])*'?|`(?:\\\\.|[^`\\\\])*`?)|" + text + "|(?<keyword>\\b(?:" + CLikeKeywords + ")\\b)|" + text2 + "|" + text3;
			break;
		case "script":
			pattern = "(?<comment>#[^\\n]*)|(?<string>\"\"\"[\\s\\S]*?(?:\"\"\"|$)|'''[\\s\\S]*?(?:'''|$)|[fbrFBR]?\"(?:\\\\.|[^\"\\\\\\n])*\"?|[fbrFBR]?'(?:\\\\.|[^'\\\\\\n])*'?)|" + text + "|(?<keyword>\\b(?:" + ScriptKeywords + ")\\b)|" + text2 + "|" + text3;
			break;
		case "sql":
			pattern = "(?<comment>--[^\\n]*|/\\*[\\s\\S]*?(?:\\*/|$))|(?<string>'(?:''|[^'\\n])*'?|\"(?:[^\"\\n])*\"?)|" + text + "|(?<keyword>\\b(?:" + SqlKeywords + ")\\b)|" + text2;
			regexOptions |= RegexOptions.IgnoreCase;
			break;
		case "markup":
			pattern = "(?<comment><!--[\\s\\S]*?(?:-->|$))|(?<string>\"[^\"\\n]*\"?|'[^'\\n]*'?)|(?<keyword></?[A-Za-z][\\w:.-]*|/?>)|(?<call>\\b[A-Za-z_:][\\w:.-]*(?==))";
			break;
		case "json":
			pattern = "(?<call>\"(?:\\\\.|[^\"\\\\\\n])*\"(?=\\s*:))|(?<string>\"(?:\\\\.|[^\"\\\\\\n])*\"?)|" + text + "|(?<keyword>\\b(?:true|false|null)\\b)";
			break;
		default:
			return null;
		}
		return new Regex(pattern, regexOptions, Timeout);
	}

	/// <summary>Runs of (text, kind). Returns one plain run when the language is unknown or on time-out.</summary>
	public static List<(string Text, Kind Kind)> Tokenize(string code, string lang)
	{
		List<(string, Kind)> list = new List<(string, Kind)>();
		string text = Family(lang);
		Regex? value;
		lock (Cache)
		{
			if (!Cache.TryGetValue(text, out value))
			{
				value = Build(text);
				Cache[text] = value;
			}
		}
		if (value == null || code.Length > 20000)
		{
			list.Add((code, Kind.Plain));
			return list;
		}
		try
		{
			int num = 0;
			foreach (Match item in value.Matches(code))
			{
				if (item.Length == 0)
				{
					continue;
				}
				if (item.Index > num)
				{
					list.Add((code.Substring(num, item.Index - num), Kind.Plain));
				}
				Kind item2 = (item.Groups["comment"].Success ? Kind.Comment : (item.Groups["string"].Success ? Kind.String : (item.Groups["number"].Success ? Kind.Number : (item.Groups["keyword"].Success ? Kind.Keyword : (item.Groups["call"].Success ? Kind.Function : (item.Groups["type"].Success ? Kind.Type : Kind.Plain))))));
				list.Add((item.Value, item2));
				num = item.Index + item.Length;
			}
			if (num < code.Length)
			{
				list.Add((code.Substring(num), Kind.Plain));
			}
			return list;
		}
		catch (RegexMatchTimeoutException)
		{
			list.Clear();
			list.Add((code, Kind.Plain));
			return list;
		}
	}

	public static string BrushKey(Kind kind)
	{
		return kind switch
		{
			Kind.Keyword => "Syn.Keyword",
			Kind.String => "Syn.String",
			Kind.Number => "Syn.Number",
			Kind.Comment => "Syn.Comment",
			Kind.Function => "Syn.Function",
			Kind.Type => "Syn.Type",
			_ => "Syn.Plain",
		};
	}
}
