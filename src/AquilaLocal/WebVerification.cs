using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace AquilaLocal;

public static class WebVerification
{
	public static async Task RunAsync(string folder, string model)
	{
		List<string> checks = new List<string>();
		string[] array = new string[9] { "127.0.0.1", "10.0.0.1", "192.168.1.1", "172.16.0.1", "169.254.169.254", "100.64.0.1", "::1", "fe80::1", "::ffff:127.0.0.1" };
		foreach (string text in array)
		{
			Check(!WebAccess.PublicAddress(IPAddress.Parse(text)), "Blocks nonpublic address " + text);
		}
		Check(WebAccess.PublicAddress(IPAddress.Parse("1.1.1.1")) && WebAccess.PublicAddress(IPAddress.Parse("2606:4700:4700::1111")), "Public IPv4 and IPv6 addresses pass");
		Check(WebAccess.DeclinesWeb("Don't search for the latest news") && WebAccess.DeclinesWeb("Answer offline") && !WebAccess.DeclinesWeb("Please find the latest news"), "Per-message offline instructions override automatic lookup cues");
		array = new string[5] { "file:///C:/Windows/win.ini", "https://user:password@example.com", "http://localhost/", "http://127.0.0.1/", "https://example.com:8080/" };
		foreach (string text2 in array)
		{
			bool ok = false;
			try
			{
				WebAccess.ValidateUrl(text2);
			}
			catch (ArgumentException)
			{
				ok = true;
			}
			Check(ok, "Rejects unsupported URL " + text2);
		}
		Check(WebAccess.CleanText("<script>secret script</script><style>hidden</style><p>Hello &amp; world</p>") == "Hello & world", "HTML extraction excludes scripts/styles and decodes text");
		MessageView messageView = new MessageView("Aquila");
		messageView.Update("[Source](<https://example.com/page_(test)>) [blocked](file:///C:/private)");
		RichTextBox richTextBox = messageView.ProseBoxes.First();
		Check(richTextBox.IsDocumentEnabled && richTextBox.Document.Blocks.OfType<Paragraph>().SelectMany((Paragraph p) => p.Inlines.OfType<Hyperlink>()).Count() == 1, "Source links render as clickable safe URLs, including bracketed parentheses");
		Check(WebAccess.CleanText(new string('x', 9000)).Length <= 7001 && WebAccess.SuggestSearch("What is the latest release?") && !WebAccess.SuggestSearch("Hello, how are you?"), "Excerpts are bounded and freshness cues distinguish ordinary chat");
		Settings settings = new Settings();
		List<WebSource> sources = new List<WebSource>();
		using (WebAccess off = new WebAccess(settings, sources, delegate
		{
			throw new Exception("Off must not request");
		}))
		{
			Check((await off.ExecuteAsync("web_read", "{}", default(CancellationToken))).Contains("off") && sources.Count == 0, "Off mode makes no request");
		}
		settings.WebMode = "Ask first";
		int approvals = 0;
		using (WebAccess off = new WebAccess(settings, sources, delegate
		{
		}, delegate
		{
			approvals++;
			return Task.FromResult(result: false);
		}))
		{
			Check((await off.ExecuteAsync("web_read", "{\"url\":\"https://example.com\"}", default(CancellationToken))).Contains("declined") && approvals == 1 && sources.Count == 0, "Declined lookup makes no request or source record");
		}
		settings.WebMode = "Automatic";
		using (WebAccess off = new WebAccess(settings, sources, delegate
		{
		}))
		{
			Check((await off.ExecuteAsync("web_search", "{\"query\":\"api_key=synthetic-private-value\"}", default(CancellationToken))).Contains("Remove credentials"), "Obvious credential assignments are refused before sending a request");
		}
		using (WebAccess off = new WebAccess(settings, sources, delegate
		{
		}))
		{
			Check((await off.ExecuteAsync("web_search", "{\"query\":\"latest Godot release\"}", default(CancellationToken))).Contains("not configured"), "Missing API key is explicit rather than fabricated search results");
		}
		settings.WebKeyProtected = WebSecrets.Protect("synthetic-test-key");
		Check(!settings.WebKeyProtected.Contains("synthetic-test-key") && WebSecrets.Unprotect(settings.WebKeyProtected) == "synthetic-test-key", "Windows-user encryption round trips without storing plaintext");
		Storage store = new Storage(Path.Combine(folder, "data"));
		store.SaveSettings(settings);
		Check(store.LoadSettings().WebMode == "Automatic" && !File.ReadAllText(Path.Combine(store.Root, "settings.json")).Contains("synthetic-test-key"), "Web mode and encrypted credential persist");
		int calls = 0;
		using (WebAccess off = new WebAccess(settings, sources, delegate
		{
		}))
		{
			off.FixtureResponse = delegate(Uri uri, string? key, CancellationToken ct)
			{
				calls++;
				if (uri.Host == "api.search.brave.com")
				{
					if (key != "synthetic-test-key")
					{
						throw new Exception("Wrong credential");
					}
					return Task.FromResult("{\"web\":{\"results\":[{\"title\":\"Fixture source\",\"url\":\"https://example.com/fixture\",\"description\":\"Fixture fact 7319\"}]}}");
				}
				if (key != null)
				{
					throw new Exception("Credential leaked to page");
				}
				return Task.FromResult("<title>Fixture page</title><p>Verified fixture 7319</p>");
			};
			Check((await off.ExecuteAsync("web_search", "{\"query\":\"fixture\"}", default(CancellationToken))).Contains("7319") && sources.Count == 1, "Provider fixture parses real search schema and records source URLs");
			Check((await off.ExecuteAsync("web_read", "{\"url\":\"https://example.com/fixture\"}", default(CancellationToken))).Contains("Verified fixture 7319") && sources.Count == 1 && sources[0].Title == "Fixture page", "Page reading updates source evidence without sending the API credential");
			for (int i2 = 0; i2 < 5; i2++)
			{
				await off.ExecuteAsync("web_read", "{\"url\":\"https://example.com/fixture\"}", default(CancellationToken));
			}
			Check(calls == 6, "Per-turn limit bounds outbound web requests");
		}
		settings.WebKeyProtected = "";
		sources.Clear();
		using (WebAccess off = new WebAccess(settings, sources, delegate
		{
		}))
		{
			Check((await off.ExecuteAsync("web_read", "{\"url\":\"https://example.com/\"}", default(CancellationToken))).Contains("Example Domain") && sources.Count == 1, "Actual public HTTPS reading works without a search key");
		}
		settings.ModelPath = model;
		settings.GpuLayers = 0;
		settings.CpuThreads = 2;
		settings.Context = 4096;
		settings.MaxTokens = 256;
		settings.WorkspaceTools = false;
		settings.UseMemories = false;
		using (Engine engine = new Engine(store.Root))
		{
			Turn turn = new Turn
			{
				User = "Read https://example.com/ and tell me the page heading in one sentence.",
				Status = "generating"
			};
			ChatSession chat = new ChatSession
			{
				Turns = new List<Turn>(1) { turn }
			};
			using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3.0));
			await new Agent(engine).RunAsync(settings, chat, turn, timeout.Token);
			Check(turn.WebSources.Any((WebSource s) => s.Url.Contains("example.com")) && turn.Activity.Any((string x) => x.StartsWith("Read web page")) && turn.Answer.Contains("Sources consulted"), "Real local-model turn initiates URL lookup and retains visible source links");
			store.SaveChat(chat);
			Check(store.LoadChats().Single().Turns[0].WebSources.Count > 0 && turn.PromptTokens > 0, "Source evidence and generation usage survive chat persistence");
			engine.Stop();
			Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			store.SaveSettings(settings);
			MainWindow mainWindow = new MainWindow(store.Root);
			mainWindow.Show();
			TaskCompletionSource closed = new TaskCompletionSource();
			mainWindow.Closed += delegate
			{
				closed.TrySetResult();
			};
			mainWindow.Close();
			await closed.Task.WaitAsync(TimeSpan.FromSeconds(8.0));
			Check(store.LoadSettings().WebMode == "Automatic", "Normal window saves preserve Internet settings");
			File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new
			{
				checks = checks.Count,
				answer = turn.Answer,
				sources = turn.WebSources,
				providerTest = "synthetic schema fixture; no paid search request",
				liveRead = "https://example.com/",
				cpuThreads = 2
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		void Check(bool flag, string label)
		{
			if (!flag)
			{
				throw new Exception("Failed: " + label);
			}
			checks.Add("PASS " + label);
			File.WriteAllLines(Path.Combine(folder, "checks.txt"), checks);
		}
	}
}
