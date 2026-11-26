using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Scenes;

namespace AquilaLocal;

/// <summary>
/// Multi-persona scenes: pick or create a scene, then talk with several personas at once. The cast bar adds
/// and drops people (everyone present hears about it), the world can be changed at any time, and "Who
/// answers" either calls on someone or leaves it to the scheduler.
/// </summary>
public sealed class SceneWindow : AquilaWindow
{
	private readonly CompanionHost host;

	private readonly SceneDirector director;

	private readonly Func<Settings> settings;

	/// <summary>Speaks a line aloud in that persona's voice (null when voice is off).</summary>
	private readonly Func<string, string, CancellationToken, Task>? speakLine;

	private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap };

	private Scene? scene;

	private StackPanel messages = new StackPanel();

	private ScrollViewer scroll = new ScrollViewer();

	private TextBox composer = new TextBox();

	private ComboBox responder = new ComboBox();

	private Button sendButton = new Button();

	private Button talkButton = new Button();

	private Button stopButton = new Button();

	private WrapPanel castBar = new WrapPanel();

	private CancellationTokenSource? running;

	private bool busy;

	private string userName = "You";

	private const string AutoLabel = "Automatic";

	private const string NobodyLabel = "Nobody (just say it)";

	public SceneWindow(CompanionHost host, SceneDirector director, Func<Settings> settings, Func<string, string, CancellationToken, Task>? speakLine)
	{
		this.host = host;
		this.director = director;
		this.settings = settings;
		this.speakLine = speakLine;
		base.Title = "Aquila · Scenes";
		base.Width = 1000.0;
		base.Height = 880.0;
		base.MinWidth = 700.0;
		base.MinHeight = 600.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		try
		{
			string nick = host.LoadAboutYou().Nickname.Trim();
			userName = nick.Length > 0 ? nick : "You";
		}
		catch (PersonaConfigException)
		{
		}
		director.Token += delegate(string piece)
		{
			base.Dispatcher.InvokeAsync(delegate
			{
				liveText += piece;
				live?.Update(ScenePrompt.CleanReply(liveText, liveName), "", null, "", renderMarkdown: true);
				if (scroll.VerticalOffset >= scroll.ScrollableHeight - 80)
				{
					scroll.ScrollToEnd();
				}
			});
		};
		base.Closing += delegate(object? _, System.ComponentModel.CancelEventArgs e)
		{
			running?.Cancel();
		};
		ShowPicker();
	}

	private MessageView? live;

	private string liveText = "";

	private string liveName = "";

	private void Problem(string text)
	{
		status.Text = text;
		status.Foreground = Ui.Brush("Danger");
	}

	private void Ok(string text)
	{
		status.Text = text;
		status.Foreground = Ui.Brush("Text.Secondary");
	}

	// ----- Picking or creating a scene -----

	private void ShowPicker()
	{
		scene = null;
		StackPanel body = new StackPanel();
		List<(string Id, string Title, DateTimeOffset CreatedAt, string? Problem)> scenes = host.Scenes.List();
		StackPanel existing = new StackPanel();
		if (scenes.Count == 0)
		{
			existing.Children.Add(Ui.Text("No scenes yet. Make one below.", "Type.Small"));
		}
		foreach ((string id, string sceneTitle, DateTimeOffset created, string? problem) in scenes)
		{
			string sceneId = id;
			Button open = Ui.Button(problem == null ? sceneTitle + "   ·   " + created.ToLocalTime().ToString("MMM d, h:mm tt") : sceneTitle + " (can't be opened)", "Btn.Secondary", delegate
			{
				try
				{
					OpenScene(host.Scenes.Open(sceneId));
				}
				catch (SceneException ex)
				{
					Problem(ex.Message);
				}
			});
			open.HorizontalAlignment = HorizontalAlignment.Left;
			open.Margin = new Thickness(0, 0, 0, 6);
			open.ToolTip = problem;
			existing.Children.Add(open);
		}
		body.Children.Add(Ui.Group("Your scenes", existing));

		TextBox title = new TextBox();
		TextBox place = new TextBox();
		TextBox time = new TextBox { Text = DateTime.Now.ToString("dddd, h:mm tt") };
		TextBox situation = Multiline("", 60);
		TextBox things = Multiline("", 60);
		StackPanel castChoices = new StackPanel();
		List<(CheckBox Box, PersonaEntry Entry)> boxes = new List<(CheckBox, PersonaEntry)>();
		List<PersonaEntry> personas;
		try
		{
			personas = host.Registry.Load().Personas;
		}
		catch (PersonaConfigException ex)
		{
			personas = new List<PersonaEntry>();
			Problem(ex.Message);
		}
		foreach (PersonaEntry p in personas)
		{
			bool ready = host.IsSetUp(p.Id);
			CheckBox box = new CheckBox { Content = ready ? p.Name : p.Name + " (set her up in Companions first)", IsEnabled = ready, Margin = new Thickness(0, 2, 0, 2) };
			boxes.Add((box, p));
			castChoices.Children.Add(box);
		}
		if (personas.Count == 0)
		{
			castChoices.Children.Add(Ui.Text("No personas yet. Add them in Companions.", "Type.Small"));
		}
		StackPanel form = new StackPanel();
		form.Children.Add(Ui.Field("Scene name", title, "For example \"Late night at the lab\""));
		form.Children.Add(Ui.Field("Where", place));
		form.Children.Add(Ui.Field("When", time));
		form.Children.Add(Ui.Field("What's happening", situation));
		form.Children.Add(Ui.Field("Things there (one per line)", things));
		form.Children.Add(Ui.Field("Who's there (up to " + SceneManifest.MaxCast + ")", castChoices));
		Button create = Ui.Button("Start the scene", "Btn.Primary", delegate
		{
			List<SceneMember> cast = boxes.Where(((CheckBox Box, PersonaEntry Entry) b) => b.Box.IsChecked == true).Select(((CheckBox Box, PersonaEntry Entry) b) => new SceneMember(b.Entry.Id, b.Entry.Name)).ToList();
			if (cast.Count == 0)
			{
				Problem("Choose at least one persona for the scene.");
				return;
			}
			try
			{
				WorldState world = new WorldState { Place = place.Text.Trim(), Time = time.Text.Trim(), Situation = situation.Text.Trim(), Objects = Lines(things.Text) };
				OpenScene(host.Scenes.Create(title.Text, world, cast, DateTimeOffset.Now));
			}
			catch (Exception ex) when (ex is SceneException || ex is IOException || ex is UnauthorizedAccessException || ex is Microsoft.Data.Sqlite.SqliteException)
			{
				Problem(ex.Message);
			}
		});
		create.HorizontalAlignment = HorizontalAlignment.Left;
		create.Margin = new Thickness(0, 14, 0, 0);
		form.Children.Add(create);
		body.Children.Add(new Expander { Header = "New scene", Content = form, IsExpanded = scenes.Count == 0, Style = Ui.Style("Expander.Card"), Margin = new Thickness(0, 12, 0, 12) });
		Button close = Ui.Button("Close", "Btn.Secondary", delegate
		{
			Close();
		});
		base.Content = Ui.DialogScaffold("Scenes", "Several personas in one shared scene. They share the world and everything said out loud, but each keeps her own mind and memory. Anyone who joins later only knows what happened after they arrived.", body, status, close);
	}

	private static List<string> Lines(string text) => text.Split('\n').Select((string l) => l.Trim()).Where((string l) => l.Length > 0).ToList();

	private static TextBox Multiline(string text, double height) => new TextBox
	{
		Text = text,
		AcceptsReturn = true,
		TextWrapping = TextWrapping.Wrap,
		VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
		Height = height,
		VerticalContentAlignment = VerticalAlignment.Top
	};

	// ----- The scene itself -----

	private void OpenScene(Scene opened)
	{
		scene = opened;
		base.Title = "Aquila · " + opened.Manifest.Title;
		DockPanel root = new DockPanel { LastChildFill = true, Margin = new Thickness(20, 16, 20, 12) };

		StackPanel top = new StackPanel();
		DockPanel titleRow = new DockPanel();
		Button back = Ui.Button("All scenes", "Btn.Ghost", delegate
		{
			if (!busy)
			{
				base.Title = "Aquila · Scenes";
				ShowPicker();
			}
		});
		DockPanel.SetDock(back, Dock.Right);
		titleRow.Children.Add(back);
		titleRow.Children.Add(Ui.Text(opened.Manifest.Title, "Type.Title"));
		top.Children.Add(titleRow);
		castBar = new WrapPanel { Margin = new Thickness(0, 10, 0, 6) };
		top.Children.Add(castBar);
		top.Children.Add(WorldEditor());
		DockPanel.SetDock(top, Dock.Top);
		root.Children.Add(top);

		StackPanel bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
		composer = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 56, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top };
		Ui.SetPlaceholder(composer, "Say something, or describe what you do… (Enter to send, Shift+Enter for a new line)");
		composer.PreviewKeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.Key == Key.Return && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
			{
				e.Handled = true;
				_ = SendAsync();
			}
		};
		bottom.Children.Add(composer);
		StackPanel controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
		controls.Children.Add(new TextBlock { Text = "Who answers", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Foreground = Ui.Brush("Text.Secondary") });
		responder = new ComboBox { Width = 200, Margin = new Thickness(0, 0, 12, 0) };
		responder.SelectionChanged += delegate
		{
			if (scene != null && responder.SelectedItem is string choice)
			{
				string mode = choice == AutoLabel ? TurnPolicy.Automatic : TurnPolicy.Manual;
				if (scene.Manifest.Policy.Mode != mode)
				{
					scene.Manifest.Policy.Mode = mode;
					SaveQuietly();
				}
			}
		};
		controls.Children.Add(responder);
		sendButton = Ui.Button("Send", "Btn.Primary", delegate
		{
			_ = SendAsync();
		});
		sendButton.Margin = new Thickness(0, 0, 8, 0);
		controls.Children.Add(sendButton);
		talkButton = Ui.Button("Let them talk", "Btn.Secondary", delegate
		{
			_ = RunAsync(() => AutoAsync(forceFirst: true));
		});
		talkButton.ToolTip = "Someone speaks without you saying anything; others may react.";
		talkButton.Margin = new Thickness(0, 0, 8, 0);
		controls.Children.Add(talkButton);
		stopButton = Ui.Button("Stop", "Btn.Secondary", delegate
		{
			running?.Cancel();
		});
		stopButton.IsEnabled = false;
		controls.Children.Add(stopButton);
		bottom.Children.Add(controls);
		status.Margin = new Thickness(0, 8, 0, 0);
		bottom.Children.Add(status);
		DockPanel.SetDock(bottom, Dock.Bottom);
		root.Children.Add(bottom);

		messages = new StackPanel { Margin = new Thickness(0, 8, 8, 8) };
		scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = messages };
		root.Children.Add(scroll);
		base.Content = root;

		RefreshCast();
		foreach (SceneEvent e in opened.Log.All())
		{
			Render(e);
		}
		scroll.ScrollToEnd();
		Ok(opened.Manifest.Cast.Count == 0 ? "Nobody is here yet. Add someone above." : "Ready.");
		composer.Focus();
	}

	private void SaveQuietly()
	{
		try
		{
			scene?.Save();
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			Problem("Couldn't save the scene: " + ex.Message);
		}
	}

	private Expander WorldEditor()
	{
		WorldState w = scene!.Manifest.World;
		TextBox place = new TextBox { Text = w.Place };
		TextBox time = new TextBox { Text = w.Time };
		TextBox situation = Multiline(w.Situation, 54);
		TextBox things = Multiline(string.Join("\n", w.Objects), 54);
		TextBox notes = Multiline(w.Notes, 40);
		StackPanel form = new StackPanel();
		form.Children.Add(Ui.Field("Where", place, "", 4));
		form.Children.Add(Ui.Field("When", time, "", 8));
		form.Children.Add(Ui.Field("What's happening", situation, "", 8));
		form.Children.Add(Ui.Field("Things there (one per line)", things, "", 8));
		form.Children.Add(Ui.Field("Notes everyone knows", notes, "", 8));
		Button apply = Ui.Button("Update the world", "Btn.Secondary", delegate
		{
			if (scene == null || busy)
			{
				return;
			}
			try
			{
				WorldState next = new WorldState { Place = place.Text.Trim(), Time = time.Text.Trim(), Situation = situation.Text.Trim(), Objects = Lines(things.Text), Notes = notes.Text.Trim() };
				long id = scene.ChangeWorld(next, DateTimeOffset.Now);
				if (id == 0)
				{
					Ok("Nothing in the world changed.");
					return;
				}
				Render(scene.Log.All().Last());
				scroll.ScrollToEnd();
				Ok("The world changed. Everyone here will notice.");
			}
			catch (Exception ex) when (ex is SceneException || ex is IOException || ex is UnauthorizedAccessException || ex is Microsoft.Data.Sqlite.SqliteException)
			{
				Problem(ex.Message);
			}
		});
		apply.HorizontalAlignment = HorizontalAlignment.Left;
		apply.Margin = new Thickness(0, 10, 0, 0);
		form.Children.Add(apply);
		return new Expander { Header = "The world (shared by everyone here)", Content = form, IsExpanded = false, Style = Ui.Style("Expander.Card"), Margin = new Thickness(0, 4, 0, 0) };
	}

	private void RefreshCast()
	{
		if (scene == null)
		{
			return;
		}
		castBar.Children.Clear();
		foreach (SceneMember m in scene.Manifest.Cast)
		{
			castBar.Children.Add(CastChip(m));
		}
		Button add = Ui.Button(Ui.IconLabel(Ui.Glyph.Add, "Add someone"), "Btn.Ghost");
		add.Margin = new Thickness(0, 0, 8, 6);
		add.IsEnabled = !busy && scene.Manifest.Cast.Count < SceneManifest.MaxCast;
		add.Click += delegate
		{
			ContextMenu menu = new ContextMenu();
			List<PersonaEntry> all;
			try
			{
				all = host.Registry.Load().Personas;
			}
			catch (PersonaConfigException ex)
			{
				Problem(ex.Message);
				return;
			}
			foreach (PersonaEntry p in all.Where((PersonaEntry p) => scene.Manifest.Cast.All((SceneMember m) => m.PersonaId != p.Id)))
			{
				bool ready = host.IsSetUp(p.Id);
				MenuItem item = new MenuItem { Header = ready ? p.Name : p.Name + " (set her up in Companions first)", IsEnabled = ready };
				PersonaEntry entry = p;
				item.Click += delegate
				{
					Change(() => scene.Join(new SceneMember(entry.Id, entry.Name), DateTimeOffset.Now), entry.Name + " joined. Everyone here heard.");
				};
				menu.Items.Add(item);
			}
			if (menu.Items.Count == 0)
			{
				menu.Items.Add(new MenuItem { Header = "Everyone is already here (add personas in Companions)", IsEnabled = false });
			}
			menu.PlacementTarget = add;
			menu.IsOpen = true;
		};
		castBar.Children.Add(add);
		string? selected = responder.SelectedItem as string;
		List<string> choices = new List<string> { AutoLabel };
		choices.AddRange(scene.Manifest.Cast.Select((SceneMember m) => m.Name));
		choices.Add(NobodyLabel);
		responder.ItemsSource = choices;
		responder.SelectedItem = selected != null && choices.Contains(selected) ? selected : (scene.Manifest.Policy.Mode == TurnPolicy.Manual && scene.Manifest.Cast.Count > 0 ? scene.Manifest.Cast[0].Name : AutoLabel);
	}

	private Border CastChip(SceneMember m)
	{
		StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
		ImageSource? picture = host.ProfilePicture(m.PersonaId);
		if (picture != null)
		{
			row.Children.Add(new Ellipse { Width = 24, Height = 24, Margin = new Thickness(0, 0, 6, 0), Fill = new ImageBrush(picture) { Stretch = Stretch.UniformToFill } });
		}
		row.Children.Add(new TextBlock { Text = m.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
		Button drop = Ui.IconButton(Ui.Glyph.Close, "Drop " + m.Name + " from the scene (everyone here notices)");
		drop.Width = 24;
		drop.Height = 24;
		drop.FontSize = 10;
		drop.IsEnabled = !busy;
		drop.Click += delegate
		{
			Change(() => scene!.Leave(m.PersonaId, DateTimeOffset.Now), m.Name + " left. Everyone here noticed.");
		};
		row.Children.Add(drop);
		return new Border
		{
			Child = row,
			Background = Ui.Brush("Surface.2"),
			CornerRadius = new CornerRadius(14),
			Padding = new Thickness(6, 3, 4, 3),
			Margin = new Thickness(0, 0, 8, 6)
		};
	}

	private void Change(Func<long> change, string done)
	{
		if (scene == null || busy)
		{
			return;
		}
		try
		{
			long id = change();
			Render(scene.Log.All().Last((SceneEvent e) => e.Id == id));
			scroll.ScrollToEnd();
			RefreshCast();
			Ok(done);
		}
		catch (Exception ex) when (ex is SceneException || ex is IOException || ex is UnauthorizedAccessException || ex is Microsoft.Data.Sqlite.SqliteException)
		{
			Problem(ex.Message);
		}
	}

	private void Render(SceneEvent e)
	{
		if (e.Kind == SceneEventKinds.Said)
		{
			bool mine = e.SpeakerId == SceneLog.UserId;
			MessageView view = new MessageView(mine ? "You" : e.SpeakerName, animate: false, mine ? null : host.ProfilePicture(e.SpeakerId));
			view.Update(e.Text, "", null, "", renderMarkdown: !mine);
			messages.Children.Add(view);
			return;
		}
		TextBlock note = Ui.Text(SceneTranscript.Line(e, ""), "Type.Small");
		note.HorizontalAlignment = HorizontalAlignment.Center;
		note.TextAlignment = TextAlignment.Center;
		note.TextWrapping = TextWrapping.Wrap;
		note.FontStyle = FontStyles.Italic;
		note.Margin = new Thickness(40, 2, 40, 18);
		messages.Children.Add(note);
	}

	// ----- Talking -----

	private async Task SendAsync()
	{
		if (scene == null || busy)
		{
			return;
		}
		string text = composer.Text.Trim();
		string choice = responder.SelectedItem as string ?? AutoLabel;
		if (text.Length == 0 && choice == NobodyLabel)
		{
			return;
		}
		if (text.Length > 0)
		{
			try
			{
				long id = scene.Say(SceneLog.UserId, userName, text, DateTimeOffset.Now);
				Render(scene.Log.All().Last((SceneEvent e) => e.Id == id));
				composer.Clear();
				scroll.ScrollToEnd();
			}
			catch (Exception ex) when (ex is SceneException || ex is IOException || ex is Microsoft.Data.Sqlite.SqliteException)
			{
				Problem(ex.Message);
				return;
			}
		}
		if (choice == NobodyLabel)
		{
			Ok("Said. Nobody was asked to answer.");
			return;
		}
		if (choice == AutoLabel)
		{
			await RunAsync(() => AutoAsync(forceFirst: true));
			return;
		}
		SceneMember? member = scene.Manifest.Cast.FirstOrDefault((SceneMember m) => m.Name == choice);
		if (member == null)
		{
			Problem(choice + " isn't in the scene any more.");
			return;
		}
		await RunAsync(() => SpeakOneAsync(member.PersonaId, member.Name, "you called on her"));
	}

	private async Task AutoAsync(bool forceFirst)
	{
		Scene s = scene!;
		int chain = 0;
		while (true)
		{
			CancellationToken ct = running!.Token;
			ct.ThrowIfCancellationRequested();
			List<RankedSpeaker> ranked = await Task.Run(() => director.Rank(s), ct);
			if (ranked.Count == 0)
			{
				if (chain == 0)
				{
					Ok(s.Manifest.Cast.Count == 0 ? "Nobody is here to answer. Add someone above." : "Nobody else is here to answer.");
				}
				return;
			}
			if (!(chain == 0 && forceFirst) && !TurnScheduler.ShouldContinue(ranked[0], chain, s.Manifest.Policy))
			{
				Ok(chain == 0 ? "Nobody felt like speaking." : "Your turn.");
				return;
			}
			RankedSpeaker pick = director.Pick(ranked);
			await SpeakOneAsync(pick.PersonaId, pick.Name, pick.Why);
			chain++;
		}
	}

	private async Task SpeakOneAsync(string personaId, string name, string why)
	{
		Scene s = scene!;
		CancellationToken ct = running!.Token;
		Ok(name + " is speaking… (" + why + ")");
		liveText = "";
		liveName = name;
		live = new MessageView(name, animate: true, host.ProfilePicture(personaId));
		live.Update("", "", null, name + " is thinking…", renderMarkdown: true);
		messages.Children.Add(live);
		scroll.ScrollToEnd();
		SceneEvent said;
		try
		{
			said = await director.SpeakAsync(s, personaId, settings(), userName, ct);
		}
		catch
		{
			messages.Children.Remove(live);
			live = null;
			throw;
		}
		live.Update(said.Text, "", null, "", renderMarkdown: true);
		live = null;
		scroll.ScrollToEnd();
		if (speakLine != null)
		{
			Ok(name + " is speaking aloud…");
			await speakLine(personaId, said.Text, ct);
		}
	}

	private async Task RunAsync(Func<Task> work)
	{
		if (busy || scene == null)
		{
			return;
		}
		busy = true;
		running = new CancellationTokenSource();
		SetBusy(true);
		try
		{
			await work();
		}
		catch (OperationCanceledException)
		{
			Ok("Stopped.");
		}
		catch (Exception ex) when (ex is SceneException || ex is IOException || ex is HttpRequestException || ex is PersonaConfigException || ex is AppearanceCardUnavailableException || ex is InvalidOperationException || ex is Microsoft.Data.Sqlite.SqliteException)
		{
			Problem(ex.Message);
		}
		finally
		{
			busy = false;
			running.Dispose();
			running = null;
			SetBusy(false);
		}
	}

	private void SetBusy(bool on)
	{
		sendButton.IsEnabled = !on;
		talkButton.IsEnabled = !on;
		stopButton.IsEnabled = on;
		RefreshCast();
	}
}
