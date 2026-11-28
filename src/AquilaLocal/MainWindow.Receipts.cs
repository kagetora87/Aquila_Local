using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Living;

namespace AquilaLocal;

/// <summary>
/// Emoji (picker and reactions on her messages) and read receipts: she knows when you've seen her message, sees
/// your reactions, and may send a short follow-up if you leave her on read, when that's what she'd do.
/// </summary>
public partial class MainWindow
{
	private DispatcherTimer? followUpTimer;

	private CancellationTokenSource? followUpCts;

	private bool followUpRunning;

	private void InitializeReceipts()
	{
		base.Activated += delegate
		{
			MarkRead();
		};
		base.StateChanged += delegate
		{
			MarkRead();
		};
		followUpTimer = new DispatcherTimer { Interval = ReadReceipts.CheckEvery };
		followUpTimer.Tick += async delegate
		{
			await FollowUpTickAsync();
		};
		followUpTimer.Start();
		AddReceiptsMenuItem();
	}

	/// <summary>Her replies in the open chat count as read once Aquila is in front (not minimised) with that chat showing.</summary>
	private void MarkRead()
	{
		if (current == null || !current.IsCompanion || !base.IsActive || base.WindowState == WindowState.Minimized)
		{
			return;
		}
		DateTimeOffset now = DateTimeOffset.Now;
		bool changed = false;
		foreach (Turn t in current.Turns)
		{
			if (t.ReadAt == null && t.Status == "complete" && t.AnsweredAt != null && !string.IsNullOrWhiteSpace(t.Answer))
			{
				t.ReadAt = now;
				changed = true;
			}
		}
		if (changed)
		{
			try
			{
				store.SaveChat(current);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				Notice("The chat couldn't be saved: " + ex.Message);
			}
		}
	}

	/// <summary>Your reaction to one of her replies: pick one, pick it again to take it back.</summary>
	private void ShowReactionPicker(MessageView view, ChatSession session, Turn turn)
	{
		WrapPanel grid = new WrapPanel { MaxWidth = 300 };
		Popup popup = new Popup
		{
			Placement = PlacementMode.MousePoint,
			StaysOpen = false,
			AllowsTransparency = true,
			Child = new Border { Style = Ui.Style("Popup.Surface"), Padding = new Thickness(6), Child = grid }
		};
		foreach (string emoji in EmojiShortcodes.Reactions)
		{
			string e = emoji;
			Button b = EmojiButton(e, delegate
			{
				popup.IsOpen = false;
				turn.UserReaction = turn.UserReaction == e ? "" : e;
				view.SetReaction(turn.UserReaction, "You");
				try
				{
					store.SaveChat(session);
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					Notice("The reaction couldn't be saved: " + ex.Message);
				}
			});
			if (turn.UserReaction == e)
			{
				b.Background = Ui.Brush("Accent.Subtle");
				b.ToolTip = "Take back your reaction";
			}
			grid.Children.Add(b);
		}
		popup.IsOpen = true;
	}

	/// <summary>The emoji button beside the message box: a grid of emoji that go in where you're typing.</summary>
	private void EmojiPicker_Click(object sender, RoutedEventArgs e)
	{
		WrapPanel grid = new WrapPanel { MaxWidth = 340 };
		Popup popup = new Popup
		{
			PlacementTarget = sender as UIElement,
			Placement = PlacementMode.Top,
			VerticalOffset = -6,
			StaysOpen = false,
			AllowsTransparency = true,
			Child = new Border
			{
				Style = Ui.Style("Popup.Surface"),
				Padding = new Thickness(6),
				Child = new StackPanel
				{
					Children =
					{
						grid,
						new TextBlock { Text = "Tip: type :heart: or :) and it becomes an emoji.", Style = Ui.Style("Type.Caption"), Margin = new Thickness(6, 6, 6, 2) }
					}
				}
			}
		};
		foreach (string emoji in EmojiShortcodes.Picker)
		{
			string pick = emoji;
			grid.Children.Add(EmojiButton(pick, delegate
			{
				Composer.InsertText(pick);
			}));
		}
		popup.IsOpen = true;
	}

	private static Button EmojiButton(string emoji, RoutedEventHandler click)
	{
		Button b = new Button
		{
			Style = Ui.Style("Btn.Ghost"),
			Width = 38,
			Height = 38,
			MinHeight = 0,
			Padding = new Thickness(0),
			Content = new Emoji.Wpf.TextBlock { Text = emoji, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
		};
		b.Click += click;
		return b;
	}

	/// <summary>The switch, in the ⋯ menu.</summary>
	private void AddReceiptsMenuItem()
	{
		if (MoreMenu.Child is not Border { Child: StackPanel menu })
		{
			return;
		}
		CheckBox box = new CheckBox
		{
			Content = "Companions see when I've read their messages",
			IsChecked = settings.ReadReceipts,
			Margin = new Thickness(12, 8, 12, 8),
			ToolTip = "They know when you've seen their message and how long you took to answer, and may send a short follow-up if you leave them on read (only when it fits who they are and how the talk stands)."
		};
		box.Click += delegate
		{
			settings.ReadReceipts = box.IsChecked == true;
			try
			{
				store.SaveSettings(settings);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				Notice("The setting couldn't be saved: " + ex.Message);
			}
			if (!settings.ReadReceipts)
			{
				followUpCts?.Cancel();
			}
		};
		int at = Math.Max(0, menu.Children.Count - 2);
		menu.Children.Insert(at, box);
	}

	/// <summary>
	/// Every couple of minutes: if you've read her latest message and not answered, a small side request asks, as her,
	/// whether she'd write again now. If yes, she writes it in her full voice (like a Living-mode message).
	/// </summary>
	private async Task FollowUpTickAsync()
	{
		ChatSession? chat = current;
		if (followUpRunning || featuresClosing || !settings.ReadReceipts || !engine.Ready || LivingBusy || chat == null || !chat.IsCompanion || chat.IsCall || chat.PersonaId.Length == 0 || !Composer.IsEmpty)
		{
			return;
		}
		Turn? last = chat.Turns.LastOrDefault();
		if (last == null || last.Status != "complete" || last.AnsweredAt == null || last.ReadAt == null)
		{
			return;
		}
		int followUps = Enumerable.Reverse(chat.Turns).TakeWhile((Turn t) => t.FollowUp).Count();
		DateTimeOffset now = DateTimeOffset.Now;
		if (!ReadReceipts.Due(last.ReadAt, followUps, last.FollowUpChecks, last.FollowUpSettled, now))
		{
			return;
		}
		followUpRunning = true;
		followUpCts = new CancellationTokenSource();
		CancellationToken ct = followUpCts.Token;
		string name = chat.PersonaName;
		try
		{
			last.FollowUpChecks++;
			CompanionRuntime runtime = companion.Open(chat.PersonaId);
			string you = companion.LoadAboutYou().Nickname.Trim();
			if (you.Length == 0)
			{
				you = "Them";
			}
			List<(string, string)> recent = new List<(string, string)>();
			foreach (Turn t in chat.Turns.Where((Turn t) => t.Status == "complete").TakeLast(6))
			{
				if (!t.Unprompted && t.User.Length > 0)
				{
					recent.Add((you, ThoughtMarkup.Plain(t.User)));
				}
				recent.Add((name, ThoughtMarkup.Plain(MoodMarkup.Strip(t.Answer))));
			}
			string answer = await AskSideAsync(
				ReadReceipts.DecisionSystem(name, runtime.Config.PersonalitySheet),
				ReadReceipts.DecisionUser(name, recent, last.AnsweredAt.Value, last.ReadAt.Value, now, last.UserReaction, followUps),
				ct);
			FollowUpDecision decision = ReadReceipts.Parse(answer);
			LogFollowUp(name, last, decision);
			if (decision.Settled)
			{
				last.FollowUpSettled = true;
			}
			// You may have started typing or sent something while she was deciding.
			if (decision.Send && !ct.IsCancellationRequested && current == chat && chat.Turns.LastOrDefault() == last && Composer.IsEmpty && !LivingBusy)
			{
				TimeSpan onRead = now - last.ReadAt.Value;
				InnerThought nudge = new InnerThought("follow-up", decision.Why, "", 1.0, "");
				(ChatSession into, Turn turn) = await MessageFirstAsync(runtime, chat.PersonaId, name, chat, nudge, onRead, ct,
					ReadReceipts.Cue(onRead, followUps, decision.Why), ReadReceipts.Context(DateTimeOffset.Now, onRead));
				turn.FollowUp = true;
				store.SaveChat(into);
				MarkRead();
			}
			else
			{
				store.SaveChat(chat);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is PersonaConfigException || ex is InvalidOperationException || ex is JsonException || ex is UnauthorizedAccessException)
		{
			LogFollowUp(name, last, new FollowUpDecision(false, false, "failed: " + ex.Message));
		}
		finally
		{
			followUpRunning = false;
			followUpCts?.Dispose();
			followUpCts = null;
		}
	}

	/// <summary>A short, cheap request on the engine's second slot (the conversation's cache is untouched).</summary>
	private async Task<string> AskSideAsync(string system, string user, CancellationToken ct)
	{
		JsonObject payload = new JsonObject
		{
			["model"] = engine.Alias,
			["id_slot"] = 1,
			["messages"] = new JsonArray
			{
				new JsonObject { ["role"] = "system", ["content"] = system },
				new JsonObject { ["role"] = "user", ["content"] = user }
			},
			["stream"] = false,
			["temperature"] = 0.4,
			["max_tokens"] = 160,
			["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },
			["response_format"] = new JsonObject { ["type"] = "json_object" }
		};
		using HttpResponseMessage response = await engine.PostAsync("v1/chat/completions", payload, stream: false, ct);
		JsonNode? node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
		return node?["choices"]?.AsArray().FirstOrDefault()?["message"]?["content"]?.GetValue<string>() ?? "";
	}

	/// <summary>Each decision goes in Data\logs\follow-ups.log, so "why did she (not) text me?" has an answer.</summary>
	private void LogFollowUp(string name, Turn last, FollowUpDecision decision)
	{
		try
		{
			string folder = Path.Combine(store.Root, "logs");
			Directory.CreateDirectory(folder);
			File.AppendAllText(Path.Combine(folder, "follow-ups.log"),
				DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + name + "  check " + last.FollowUpChecks + ": "
				+ (decision.Send ? "send" : decision.Settled ? "settled" : "wait") + (decision.Why.Length > 0 ? " (" + decision.Why + ")" : "") + Environment.NewLine);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
		}
	}
}
