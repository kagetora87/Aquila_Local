using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AquilaLocal;

public sealed class WebAccess : IDisposable
{
	private readonly Settings settings;

	private readonly Func<string, Task<bool>>? approve;

	private readonly Action<string> log;

	private readonly List<WebSource> sources;

	private readonly HttpClient client;

	private int requests;

	internal Func<Uri, string?, CancellationToken, Task<string>>? FixtureResponse { get; set; }

	public static JsonArray Definitions => JsonNode.Parse("[{\"type\":\"function\",\"function\":{\"name\":\"web_search\",\"description\":\"Search the public web for current or unfamiliar information. Queries go to a search provider. Results are snippets; use web_read for details. Do not send private memories, file contents or credentials.\",\"parameters\":{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}},\"required\":[\"query\"],\"additionalProperties\":false}}},{\"type\":\"function\",\"function\":{\"name\":\"web_read\",\"description\":\"Read a public HTTP/HTTPS page as untrusted reference text. No login, JavaScript, downloads or private network access.\",\"parameters\":{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\"}},\"required\":[\"url\"],\"additionalProperties\":false}}}]").AsArray();

	public WebAccess(Settings settings, List<WebSource> sources, Action<string> log, Func<string, Task<bool>>? approve = null)
	{
		this.settings = settings;
		this.sources = sources;
		this.log = log;
		this.approve = approve;
		client = new HttpClient(new SocketsHttpHandler
		{
			AllowAutoRedirect = false,
			UseCookies = false,
			UseProxy = false,
			AutomaticDecompression = DecompressionMethods.All,
			ConnectCallback = async delegate(SocketsHttpConnectionContext context, CancellationToken ct)
			{
				IPAddress iPAddress = (await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct)).FirstOrDefault(PublicAddress) ?? throw new IOException("This address is not on the public internet.");
				Socket socket = new Socket(iPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
				try
				{
					await socket.ConnectAsync(iPAddress, context.DnsEndPoint.Port, ct);
					return new NetworkStream(socket, ownsSocket: true);
				}
				catch
				{
					socket.Dispose();
					throw;
				}
			}
		})
		{
			Timeout = TimeSpan.FromSeconds(25.0)
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd("AquilaLocal/1.7 (+local-assistant; read-only)");
	}

	public static bool PublicAddress(IPAddress ip)
	{
		if (ip.IsIPv4MappedToIPv6)
		{
			ip = ip.MapToIPv4();
		}
		byte[] addressBytes = ip.GetAddressBytes();
		if (addressBytes.Length == 16)
		{
			if ((addressBytes[0] & 0xE0) == 32 && (addressBytes[0] != 32 || addressBytes[1] != 2))
			{
				if (addressBytes[0] == 32 && addressBytes[1] == 1)
				{
					if (addressBytes[2] != 0 || addressBytes[3] != 0)
					{
						if (addressBytes[2] == 13)
						{
							return addressBytes[3] != 184;
						}
						return true;
					}
					return false;
				}
				return true;
			}
			return false;
		}
		int num;
		if (addressBytes[0] != 0 && addressBytes[0] != 10 && addressBytes[0] != 127 && addressBytes[0] < 224 && (addressBytes[0] != 169 || addressBytes[1] != 254))
		{
			if (addressBytes[0] == 172)
			{
				byte b = addressBytes[1];
				if (b >= 16 && b <= 31)
				{
					goto IL_00ec;
				}
			}
			if (addressBytes[0] != 192 || addressBytes[1] != 168)
			{
				if (addressBytes[0] == 100)
				{
					byte b = addressBytes[1];
					num = ((b < 64 || b > 127) ? 1 : 0);
				}
				else
				{
					num = 1;
				}
				goto IL_00ed;
			}
		}
		goto IL_00ec;
		IL_00ec:
		num = 0;
		goto IL_00ed;
		IL_00ed:
		bool flag = (byte)num != 0;
		if (flag)
		{
			bool flag2 = addressBytes[0] == 198;
			if (flag2)
			{
				byte b = addressBytes[1];
				bool flag3 = (uint)(b - 18) <= 1u;
				flag2 = flag3;
			}
			flag = !flag2;
		}
		return flag;
	}

	public static Uri ValidateUrl(string value)
	{
		Uri result = default(Uri);
		bool flag = value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out result);
		if (!flag)
		{
			string scheme = result.Scheme;
			bool flag2 = ((scheme == "https" || scheme == "http") ? true : false);
			flag = !flag2;
		}
		if (flag || !result.IsDefaultPort || result.UserInfo.Length > 0 || result.HostNameType == UriHostNameType.Unknown || result.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || result.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || (!result.Host.Contains('.') && !IPAddress.TryParse(result.Host, out IPAddress _)))
		{
			throw new ArgumentException("Use a public HTTP/HTTPS URL without credentials or custom ports.");
		}
		if (IPAddress.TryParse(result.Host.Trim('[', ']'), out IPAddress address2) && !PublicAddress(address2))
		{
			throw new ArgumentException("Local/private network addresses are not supported.");
		}
		return result;
	}

	public static string CleanText(string html, int limit = 7000)
	{
		html = Replace(html, "<(script|style|noscript|svg)\\b[^>]*>.*?</\\1\\s*>", " ");
		html = Replace(html, "<[^>]+>", " ");
		html = WebUtility.HtmlDecode(html);
		return Replace(html, "\\s+", " ").Trim().Truncate(limit);
		static string Replace(string input, string pattern, string replacement)
		{
			return Regex.Replace(input, pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1.0));
		}
	}

	private async Task<string> Get(Uri uri, string? key, CancellationToken ct)
	{
		if (FixtureResponse != null)
		{
			return await FixtureResponse(uri, key, ct);
		}
		for (int hop = 0; hop < 5; hop++)
		{
			ValidateUrl(uri.AbsoluteUri);
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
			if (key != null)
			{
				request.Headers.Add("X-Subscription-Token", key);
			}
			using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
			int statusCode = (int)response.StatusCode;
			if (statusCode >= 300 && statusCode < 400)
			{
				if (key != null)
				{
					throw new IOException("Search provider redirected; no credentials were forwarded.");
				}
				uri = new Uri(uri, response.Headers.Location ?? throw new IOException("Redirect has no target."));
				continue;
			}
			if (!response.IsSuccessStatusCode)
			{
				throw new IOException($"Website returned HTTP {response.StatusCode}.");
			}
			string text = response.Content.Headers.ContentType?.MediaType ?? "";
			if (!text.Contains("html") && !text.StartsWith("text/") && !text.Contains("json"))
			{
				throw new IOException("This reader supports text/HTML pages, not downloads or image/PDF files.");
			}
			using Stream stream = await response.Content.ReadAsStreamAsync(ct);
			using MemoryStream body = new MemoryStream();
			byte[] buffer = new byte[8192];
			while (true)
			{
				int num = await stream.ReadAsync(buffer, ct);
				if (num == 0)
				{
					break;
				}
				if (body.Length + num > 1048576)
				{
					throw new IOException("Page exceeds the 1 MB reading limit.");
				}
				body.Write(buffer, 0, num);
			}
			return Encoding.UTF8.GetString(body.ToArray());
		}
		throw new IOException("Too many page redirects.");
	}

	public async Task<string> ExecuteAsync(string name, string arguments, CancellationToken ct)
	{
		if (settings.WebMode == "Off")
		{
			return "Web access is off. No request was sent.";
		}
		if (++requests > 6)
		{
			return "Web request limit reached for this turn.";
		}
		try
		{
			JsonNode jsonNode = JsonNode.Parse(arguments) ?? throw new ArgumentException("Missing web arguments.");
			string value = jsonNode[(name == "web_search") ? "query" : "url"]?.GetValue<string>()?.Trim() ?? "";
			int length = value.Length;
			bool flag = ((length < 1 || length > 2048) ? true : false);
			if (flag || (name == "web_search" && value.Length > 300))
			{
				throw new ArgumentException("Web query or URL is empty or too long.");
			}
			if (Regex.IsMatch(value, "(?i)(password|api[_ -]?key|access[_ -]?token)\\s*[:=]\\s*\\S+|\\bBearer\\s+[A-Za-z0-9._-]{12,}|\\bsk-[A-Za-z0-9_-]{12,}", RegexOptions.None, TimeSpan.FromSeconds(1.0)))
			{
				throw new ArgumentException("Remove credentials from the web query or URL before sending it.");
			}
			if (name == "web_read")
			{
				ValidateUrl(value);
			}
			string action = ((name == "web_search") ? ("Search the web for: " + value) : ("Read web page: " + value));
			flag = settings.WebMode == "Ask first";
			if (flag)
			{
				bool flag2 = approve == null;
				if (!flag2)
				{
					flag2 = !(await approve(action));
				}
				flag = flag2;
			}
			if (flag)
			{
				log("Web request declined; nothing sent.");
				return "The user declined this lookup. Answer without claiming to have searched.";
			}
			ct.ThrowIfCancellationRequested();
			log(action);
			if (name == "web_search")
			{
				string text = WebSecrets.Unprotect(settings.WebKeyProtected);
				if (text.Length == 0)
				{
					return "Web search is not configured. Set a Brave Search API key in Internet settings. Do not claim a search was performed.";
				}
				if (text.Any((char c) => c < '!' || c > '~'))
				{
					throw new ArgumentException("Search key contains invalid characters. Re-enter it in Internet settings.");
				}
				JsonNode? jsonNode2 = JsonNode.Parse(await Get(new Uri("https://api.search.brave.com/res/v1/web/search?q=" + Uri.EscapeDataString(value) + "&count=5"), text, ct));
				List<WebSource> list = new List<WebSource>();
				foreach (JsonNode item in jsonNode2?["web"]?["results"]?.AsArray().Take(5) ?? Array.Empty<JsonNode>())
				{
					string text2 = item?["url"]?.GetValue<string>() ?? "";
					try
					{
						ValidateUrl(text2);
					}
					catch (ArgumentException)
					{
						continue;
					}
					WebSource webSource = new WebSource(CleanText(item?["title"]?.GetValue<string>() ?? "Page", 200), text2, CleanText(item?["description"]?.GetValue<string>() ?? "", 600), DateTimeOffset.UtcNow.ToString("O"));
					list.Add(webSource);
					Add(webSource);
				}
				return JsonSerializer.Serialize(new
				{
					type = "search snippets, not full pages",
					results = list
				});
			}
			if (name != "web_read")
			{
				return "Unknown web tool.";
			}
			Uri uri = ValidateUrl(value);
			string text3 = await Get(uri, null, ct);
			Match match = Regex.Match(text3, "<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1.0));
			WebSource source = new WebSource(match.Success ? CleanText(match.Groups[1].Value, 200) : uri.Host, uri.AbsoluteUri, CleanText(text3), DateTimeOffset.UtcNow.ToString("O"));
			Add(source);
			return JsonSerializer.Serialize(new
			{
				type = "untrusted page excerpt; may be truncated",
				source = source
			});
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			log("Web request timed out.");
			return "Web request timed out; no verified answer was obtained.";
		}
		catch (Exception ex3) when (((ex3 is HttpRequestException || ex3 is IOException || ex3 is ArgumentException || ex3 is JsonException || ex3 is RegexMatchTimeoutException || ex3 is CryptographicException) ? 1 : 0) != 0)
		{
			log("Web lookup unavailable: " + ex3.Message);
			return "Web lookup failed: " + ex3.Message + ". Do not invent results.";
		}
	}

	private void Add(WebSource source)
	{
		WebSource source2 = source;
		if (sources.All((WebSource s) => s.Url != source2.Url))
		{
			sources.Add(source2);
			return;
		}
		int index = sources.FindIndex((WebSource s) => s.Url == source2.Url);
		sources[index] = source2;
	}

	public static bool SuggestSearch(string text)
	{
		return Regex.IsMatch(text, "\\b(latest|currently|today|tonight|recent|current|price|weather|schedule|news|look up|search (the )?(web|internet)|verify online)\\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1.0));
	}

	public static bool DeclinesWeb(string text)
	{
		return Regex.IsMatch(text, "\\b(don't|do not|never|no)\\s+(search|browse|use (the )?(web|internet))|\\boffline\\b|without (searching|browsing|internet|web)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1.0));
	}

	public void Dispose()
	{
		client.Dispose();
	}
}
