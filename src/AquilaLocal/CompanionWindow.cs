using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AquilaLocal.Companion;
using Microsoft.Win32;

namespace AquilaLocal;

/// <summary>
/// Companions: pick a persona, see her drives, memory and achievements, edit her settings (saved to her
/// persona.json), set her up, and start a chat with her.
/// </summary>
public sealed class CompanionWindow : AquilaWindow
{
	private readonly CompanionHost host;

	private readonly Action<string, string> startChat;

	private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap };

	private string personaId = "";

	private bool showHistory;

	/// <summary>Memory entries to highlight and scroll to (from a chat's "New memory" notice).</summary>
	private readonly HashSet<long> highlight;

	public CompanionWindow(CompanionHost host, Action<string, string> startChat, string? focusPersona = null, IEnumerable<long>? highlightMemory = null)
	{
		this.host = host;
		this.startChat = startChat;
		highlight = new HashSet<long>(highlightMemory ?? Enumerable.Empty<long>());
		base.Title = "Aquila · Companions";
		base.Width = 860.0;
		base.Height = 820.0;
		base.MinWidth = 620.0;
		base.MinHeight = 520.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		try
		{
			RegistryManifest m = host.Registry.Load();
			personaId = m.Active.Length > 0 ? m.Active : (m.Personas.FirstOrDefault()?.Id ?? "");
			if (focusPersona != null && m.Personas.Any((PersonaEntry p) => p.Id == focusPersona))
			{
				personaId = focusPersona;
			}
		}
		catch (PersonaConfigException ex)
		{
			Problem(ex.Message);
		}
		Build();
	}

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

	private void Build()
	{
		StackPanel body = new StackPanel();
		List<PersonaEntry> entries;
		try
		{
			entries = host.Registry.Load().Personas;
		}
		catch (PersonaConfigException ex)
		{
			Problem(ex.Message);
			entries = new List<PersonaEntry>();
		}
		if (host.StartupProblem != null)
		{
			body.Children.Add(Warning(host.StartupProblem));
		}
		ComboBox picker = new ComboBox { DisplayMemberPath = "Name", ItemsSource = entries, Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
		picker.SelectedItem = entries.FirstOrDefault((PersonaEntry e) => e.Id == personaId);
		picker.SelectionChanged += delegate
		{
			if (picker.SelectedItem is PersonaEntry e && e.Id != personaId)
			{
				personaId = e.Id;
				try
				{
					host.Registry.SetActive(e.Id);
				}
				catch (PersonaConfigException ex)
				{
					Problem(ex.Message);
				}
				Build();
			}
		};
		body.Children.Add(Ui.Group("Persona", picker));
		Button primary = Ui.Button("Start a chat", "Btn.Primary");
		primary.IsEnabled = false;
		if (personaId.Length > 0)
		{
			BuildPersona(body, primary);
		}
		Button close = Ui.Button("Close", "Btn.Secondary", delegate
		{
			Close();
		});
		base.Content = Ui.DialogScaffold("Companions", "Companions are personas with their own personality, drives, private memory and chat history. Nothing one companion remembers is ever shown to another. Type /tune in a companion chat to adjust her on the fly.", body, status, close, primary);
	}

	private void BuildPersona(StackPanel body, Button primary)
	{
		PersonaStatus st;
		PersonaConfig config;
		try
		{
			st = host.Registry.Status(personaId);
			config = host.Registry.LoadConfig(personaId);
		}
		catch (PersonaConfigException ex)
		{
			body.Children.Add(Warning(ex.Message));
			body.Children.Add(Actions(null, storeExists: false));
			return;
		}
		if (!st.Ready)
		{
			body.Children.Add(Warning(string.Join(" ", st.Problems)));
		}
		body.Children.Add(Actions(config, st.StoreExists));
		primary.Content = "Start a chat with " + config.Name;
		primary.IsEnabled = st.Ready;
		primary.Click += delegate
		{
			startChat(personaId, config.Name);
			Close();
		};
		PersonaStore store = null;
		if (st.StoreExists)
		{
			try
			{
				store = host.Registry.OpenStore(personaId);
				body.Children.Add(Ui.Group(config.Name + " right now", DrivesView(config, store)));
			}
			catch (DriveStateUnavailableException ex)
			{
				body.Children.Add(Warning(ex.Message));
				store = null;
			}
		}
		body.Children.Add(Section("Settings (saved to persona.json)", SettingsEditor(config, st.StoreExists), expanded: !st.Ready));
		if (store != null)
		{
			Expander memorySection = Section("Long-term memory", MemoryView(config, store), expanded: highlight.Count > 0);
			body.Children.Add(memorySection);
			if (highlight.Count > 0)
			{
				memorySection.Loaded += delegate
				{
					memorySection.BringIntoView();
				};
			}
			body.Children.Add(Section("Achievements", AchievementsView(store), expanded: false));
			body.Children.Add(Section("Recent drive changes", LogView(store), expanded: false));
		}
	}

	private FrameworkElement Actions(PersonaConfig? config, bool storeExists)
	{
		WrapPanel row = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
		if (config != null && !storeExists)
		{
			Button setUp = Ui.Button("Set up " + config.Name, "Btn.Primary", delegate
			{
				if (!AquilaDialog.Confirm(this, "Set up " + config.Name + "?", "This creates " + config.Name + "'s private memory from her settings: her starting drive values and her starting summary. It's done once; after that her memory only grows.", "Set up"))
				{
					return;
				}
				try
				{
					host.Registry.Provision(personaId, DateTimeOffset.Now);
					host.Forget(personaId);
					Ok(config.Name + " is set up.");
				}
				catch (Exception ex) when (ex is PersonaConfigException || ex is IOException)
				{
					Problem(ex.Message);
				}
				Build();
			});
			setUp.IsEnabled = config.MissingInputs().Count == 0;
			setUp.Margin = new Thickness(0, 0, 8, 8);
			row.Children.Add(setUp);
			Button import = Ui.Button("Load persona file…", "Btn.Secondary", delegate
			{
				OpenFileDialog dialog = new OpenFileDialog { Filter = "Persona file (*.json)|*.json", Title = "Choose " + config.Name + "'s persona file" };
				if (dialog.ShowDialog(this) != true)
				{
					return;
				}
				try
				{
					host.ImportConfig(personaId, dialog.FileName);
					Ok("Loaded " + Path.GetFileName(dialog.FileName) + ".");
				}
				catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException)
				{
					Problem(ex.Message);
				}
				Build();
			});
			import.Margin = new Thickness(0, 0, 8, 8);
			row.Children.Add(import);
		}
		Button folder = Ui.Button("Open her folder", "Btn.Secondary", delegate
		{
			try
			{
				Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { host.Folder(personaId) } });
			}
			catch (Exception ex)
			{
				Problem(ex.Message);
			}
		});
		folder.Margin = new Thickness(0, 0, 8, 8);
		row.Children.Add(folder);
		if (config != null)
		{
			row.Children.Add(ProfilePictureRow(config));
		}
		return row;
	}

	/// <summary>Her small round profile picture, shown beside her name in the chat and at the top of it.</summary>
	private FrameworkElement ProfilePictureRow(PersonaConfig config)
	{
		StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 8) };
		ImageSource? picture = host.ProfilePicture(personaId);
		if (picture != null)
		{
			row.Children.Add(new System.Windows.Shapes.Ellipse
			{
				Width = 32,
				Height = 32,
				Margin = new Thickness(0, 0, 8, 0),
				VerticalAlignment = VerticalAlignment.Center,
				Fill = new ImageBrush(picture) { Stretch = Stretch.UniformToFill }
			});
		}
		row.Children.Add(Ui.Button(picture == null ? "Choose profile picture…" : "Change picture…", "Btn.Secondary", delegate
		{
			OpenFileDialog dialog = new OpenFileDialog { Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff", Title = "Choose " + config.Name + "'s profile picture" };
			if (dialog.ShowDialog(this) != true)
			{
				return;
			}
			try
			{
				host.SetProfilePicture(personaId, dialog.FileName);
				Ok(config.Name + "'s profile picture is set.");
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException || ex is FileFormatException || ex is PersonaConfigException)
			{
				Problem("That picture couldn't be used: " + ex.Message);
			}
			Build();
		}));
		if (picture != null)
		{
			Button remove = Ui.Button("Remove", "Btn.Ghost", delegate
			{
				try
				{
					host.RemoveProfilePicture(personaId);
					Ok(config.Name + "'s profile picture was removed.");
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PersonaConfigException)
				{
					Problem(ex.Message);
				}
				Build();
			});
			remove.Margin = new Thickness(8, 0, 0, 0);
			row.Children.Add(remove);
		}
		return row;
	}

	// ---- drives ------------------------------------------------------------------------------------

	private UIElement DrivesView(PersonaConfig config, PersonaStore store)
	{
		Dictionary<string, DriveValue> drives = store.ReadDrives(config.Drives.Select((DriveSpec d) => d.Name));
		string top = DriveEngine.TopDrive(config, drives);
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
		int row = 0;
		foreach (DriveSpec d in config.Drives)
		{
			grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			DriveValue v = drives[d.Name];
			TextBlock name = Ui.Text(d.Name + (d.Name == top ? "  ★" : ""), "Type.Body");
			name.Margin = new Thickness(0, 4, 0, 4);
			Grid.SetRow(name, row);
			grid.Children.Add(name);
			Grid bar = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
			bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(v.Value, 0.0001), GridUnitType.Star) });
			bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(10 - v.Value, 0.0001), GridUnitType.Star) });
			Border track = new Border { Background = Ui.Brush("Surface.3"), CornerRadius = new CornerRadius(4) };
			Grid.SetColumnSpan(track, 2);
			bar.Children.Add(track);
			bar.Children.Add(new Border { Background = Ui.Brush("Accent"), CornerRadius = new CornerRadius(4) });
			Grid.SetRow(bar, row);
			Grid.SetColumn(bar, 1);
			grid.Children.Add(bar);
			TextBlock value = Ui.Text(string.Format(CultureInfo.InvariantCulture, "{0:0.0}  ·  weight {1}", v.Value, d.Weight), "Type.Small");
			value.VerticalAlignment = VerticalAlignment.Center;
			Grid.SetRow(value, row);
			Grid.SetColumn(value, 2);
			grid.Children.Add(value);
			row++;
		}
		StackPanel panel = new StackPanel();
		panel.Children.Add(grid);
		TextBlock note = Ui.Text("★ is the drive leading her right now (value × weight, plus a boost when it has gone unsatisfied).", "Type.Caption");
		note.Margin = new Thickness(0, 8, 0, 0);
		note.TextWrapping = TextWrapping.Wrap;
		panel.Children.Add(note);
		return panel;
	}

	// ---- settings editor -----------------------------------------------------------------------------

	private UIElement SettingsEditor(PersonaConfig config, bool setUp)
	{
		StackPanel panel = new StackPanel();
		TextBox sheet = Multiline(config.PersonalitySheet, 120);
		panel.Children.Add(Label("Personality sheet"));
		panel.Children.Add(sheet);
		TextBox drift = Multiline(config.Drift.Text, 70);
		panel.Children.Add(Label("Drift clause (she can read this)"));
		panel.Children.Add(drift);
		CheckBox unlocked = new CheckBox { Content = "Drift unlocked", IsChecked = config.Drift.Unlocked, Margin = new Thickness(0, 6, 0, 0) };
		panel.Children.Add(unlocked);

		panel.Children.Add(Label("Drives"));
		string[] columns = { "Weight", "Start", "Decay /h", "Rest", "Rise /h", "Step", "Floor", "Ceiling" };
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
		foreach (string _ in columns)
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		}
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		for (int i = 0; i < columns.Length; i++)
		{
			TextBlock h = Ui.Text(columns[i], "Type.Caption");
			Grid.SetColumn(h, i + 1);
			grid.Children.Add(h);
		}
		var fields = new Dictionary<string, TextBox[]>();
		int r = 1;
		foreach (DriveSpec d in config.Drives)
		{
			grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			TextBlock n = Ui.Text(d.Name, "Type.Body");
			n.VerticalAlignment = VerticalAlignment.Center;
			Grid.SetRow(n, r);
			grid.Children.Add(n);
			double?[] vals = { d.Weight, d.Initial, d.DecayPerHour, d.RestingLevel, d.RisePerHour, d.SatisfyStep, d.Floor, d.Ceiling };
			TextBox[] boxes = new TextBox[vals.Length];
			for (int i = 0; i < vals.Length; i++)
			{
				boxes[i] = new TextBox { Text = vals[i]?.ToString(CultureInfo.InvariantCulture) ?? "", Margin = new Thickness(2) };
				Grid.SetRow(boxes[i], r);
				Grid.SetColumn(boxes[i], i + 1);
				grid.Children.Add(boxes[i]);
			}
			boxes[1].IsEnabled = !setUp;
			boxes[1].ToolTip = setUp ? "Only used when she is first set up." : "Her value on the day she is set up.";
			fields[d.Name] = boxes;
			r++;
		}
		panel.Children.Add(grid);

		var guidance = new Dictionary<string, TextBox[]>();
		foreach (DriveSpec d in config.Drives)
		{
			StackPanel g = new StackPanel();
			TextBox low = new TextBox { Text = d.Guidance.Low, MaxLength = ContextInjector.MaxGuidanceChars, Margin = new Thickness(0, 2, 0, 2) };
			TextBox mid = new TextBox { Text = d.Guidance.Mid, MaxLength = ContextInjector.MaxGuidanceChars, Margin = new Thickness(0, 2, 0, 2) };
			TextBox high = new TextBox { Text = d.Guidance.High, MaxLength = ContextInjector.MaxGuidanceChars, Margin = new Thickness(0, 2, 0, 2) };
			g.Children.Add(Ui.Text("Low (below 3.5)", "Type.Caption"));
			g.Children.Add(low);
			g.Children.Add(Ui.Text("Middle", "Type.Caption"));
			g.Children.Add(mid);
			g.Children.Add(Ui.Text("High (7 and up)", "Type.Caption"));
			g.Children.Add(high);
			guidance[d.Name] = new[] { low, mid, high };
			panel.Children.Add(new Expander { Header = "What " + d.Name + " tells her", Content = g, Style = Ui.Style("Expander.Quiet"), Margin = new Thickness(0, 6, 0, 0) });
		}

		TextBox seed = null;
		if (!setUp)
		{
			panel.Children.Add(Label("Starting summary (one item per line; Facts:, Feelings:, Hooks: headings)"));
			seed = Multiline(SeedText(config.Seed.Summary), 160);
			panel.Children.Add(seed);
		}

		Button save = Ui.Button("Save settings", "Btn.Primary", delegate
		{
			try
			{
				PersonaConfig edited = host.Registry.LoadConfig(personaId);
				edited.PersonalitySheet = sheet.Text.Trim();
				edited.Drift.Text = drift.Text.Trim();
				edited.Drift.Unlocked = unlocked.IsChecked == true;
				foreach (DriveSpec d in edited.Drives)
				{
					TextBox[] b = fields[d.Name];
					d.Weight = Num(b[0], d.Name + " weight", optional: true);
					if (!setUp)
					{
						d.Initial = Num(b[1], d.Name + " start", optional: true);
					}
					d.DecayPerHour = Num(b[2], d.Name + " decay").Value;
					d.RestingLevel = Num(b[3], d.Name + " rest").Value;
					d.RisePerHour = Num(b[4], d.Name + " rise").Value;
					d.SatisfyStep = Num(b[5], d.Name + " step").Value;
					d.Floor = Num(b[6], d.Name + " floor").Value;
					d.Ceiling = Num(b[7], d.Name + " ceiling").Value;
					d.Guidance.Low = guidance[d.Name][0].Text.Trim();
					d.Guidance.Mid = guidance[d.Name][1].Text.Trim();
					d.Guidance.High = guidance[d.Name][2].Text.Trim();
				}
				if (seed != null)
				{
					edited.Seed.Summary = ParseSeed(seed.Text);
				}
				edited.ValidateShape();
				edited.Save(host.ConfigPath(personaId));
				host.Forget(personaId);
				Ok("Saved. Changes apply from her next message.");
				Build();
			}
			catch (Exception ex) when (ex is PersonaConfigException || ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
			{
				Problem("Not saved: " + ex.Message);
			}
		});
		save.HorizontalAlignment = HorizontalAlignment.Left;
		save.Margin = new Thickness(0, 14, 0, 0);
		panel.Children.Add(save);
		return panel;
	}

	private static double? Num(TextBox box, string what, bool optional = false)
	{
		string t = box.Text.Trim();
		if (t.Length == 0 && optional)
		{
			return null;
		}
		if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v))
		{
			return v;
		}
		throw new FormatException("'" + t + "' isn't a number (" + what + "). Use a dot for decimals, like 1.5.");
	}

	private static string SeedText(StateBlock b)
	{
		return "Facts:\n" + string.Join("\n", b.Facts) + "\n\nFeelings:\n" + string.Join("\n", b.Feelings) + "\n\nHooks:\n" + string.Join("\n", b.Hooks);
	}

	private static StateBlock ParseSeed(string text)
	{
		StateBlock b = new StateBlock();
		List<string> target = null;
		foreach (string raw in text.Replace("\r", "").Split('\n'))
		{
			string line = raw.Trim();
			string head = line.TrimEnd(':').ToLowerInvariant();
			if (line.EndsWith(":") && (head == "facts" || head == "feelings" || head == "hooks"))
			{
				target = head == "facts" ? b.Facts : head == "feelings" ? b.Feelings : b.Hooks;
			}
			else if (line.Length > 0)
			{
				(target ?? throw new FormatException("Start the summary with a 'Facts:', 'Feelings:' or 'Hooks:' line.")).Add(line.TrimStart('-', ' '));
			}
		}
		return b;
	}

	// ---- memory ledger -------------------------------------------------------------------------------

	private UIElement MemoryView(PersonaConfig config, PersonaStore store)
	{
		StackPanel panel = new StackPanel();
		TextBlock intro = Ui.Text("Her memory only grows. Nothing is ever edited or deleted: a correction is a new entry that replaces the old one, with the date and your reason, and the old one stays in her history.", "Type.Small");
		intro.TextWrapping = TextWrapping.Wrap;
		intro.Margin = new Thickness(0, 0, 0, 8);
		panel.Children.Add(intro);
		CheckBox history = new CheckBox { Content = "Show replaced entries too", IsChecked = showHistory, Margin = new Thickness(0, 0, 0, 8) };
		history.Click += delegate
		{
			showHistory = history.IsChecked == true;
			Build();
		};
		panel.Children.Add(history);
		List<AquilaLocal.Companion.MemoryEntry> all = store.MemoryHistory();
		if (all.Any((AquilaLocal.Companion.MemoryEntry e) => highlight.Contains(e.Id) && !e.Active))
		{
			showHistory = true;
			history.IsChecked = true;
		}
		ListBox list = new ListBox { Height = 260 };
		foreach (AquilaLocal.Companion.MemoryEntry e in all.Where((AquilaLocal.Companion.MemoryEntry e) => showHistory || e.Active))
		{
			string prefix = e.Active ? "" : (e.Text.Length == 0 ? "[withdrawn] " : "[replaced] ");
			string meta = $"{e.Layer} · {e.At.LocalDateTime:MMM d, yyyy h:mm tt} · {e.Author}" + (e.Supersedes.HasValue ? $" · replaces #{e.Supersedes} ({e.Reason})" : "");
			StackPanel item = new StackPanel { Tag = e, Margin = new Thickness(0, 2, 0, 2) };
			TextBlock text = Ui.Text(prefix + (e.Text.Length > 0 ? e.Text : "(withdrawn)"), "Type.Body");
			text.TextWrapping = TextWrapping.Wrap;
			text.MaxWidth = 700;
			item.Children.Add(text);
			item.Children.Add(Ui.Text("#" + e.Id + " · " + meta, "Type.Caption"));
			if (highlight.Contains(e.Id))
			{
				// A memory opened from the chat's "New memory" notice.
				list.Items.Add(new Border { Tag = e, Child = item, Background = Ui.Brush("Accent.Subtle"), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 2, 6, 2) });
				list.SelectedItem ??= list.Items[list.Items.Count - 1];
			}
			else
			{
				list.Items.Add(item);
			}
		}
		panel.Children.Add(list);
		if (list.SelectedItem != null)
		{
			list.Loaded += delegate
			{
				list.ScrollIntoView(list.SelectedItem);
			};
		}

		ComboBox layer = new ComboBox { ItemsSource = MemoryLayers.All, SelectedIndex = 0, Width = 120, Margin = new Thickness(0, 0, 8, 0) };
		TextBox text2 = new TextBox { Margin = new Thickness(0, 0, 0, 4) };
		TextBox reason = new TextBox();
		panel.Children.Add(Label("New text"));
		DockPanel newRow = new DockPanel();
		DockPanel.SetDock(layer, Dock.Left);
		newRow.Children.Add(layer);
		newRow.Children.Add(text2);
		panel.Children.Add(newRow);
		panel.Children.Add(Label("Reason (required for corrections)"));
		panel.Children.Add(reason);
		WrapPanel buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
		buttons.Children.Add(Ui.Button("Add entry", "Btn.Secondary", delegate
		{
			Try(() => store.AddMemory((string)layer.SelectedItem, text2.Text, MemoryAuthors.Operator, reason.Text.Trim().Length > 0 ? reason.Text.Trim() : "added by you", DateTimeOffset.Now), "Added to " + config.Name + "'s memory.");
		}));
		Button correct = Ui.Button("Correct selected", "Btn.Secondary", delegate
		{
			if (Selected(list) is AquilaLocal.Companion.MemoryEntry e)
			{
				Try(() => store.Supersede(e.Id, text2.Text, MemoryAuthors.Operator, reason.Text, DateTimeOffset.Now), "Correction recorded; the original is kept in her history.");
			}
			else
			{
				Problem("Select an entry first.");
			}
		});
		correct.Margin = new Thickness(8, 0, 0, 0);
		buttons.Children.Add(correct);
		Button withdraw = Ui.Button("Withdraw selected", "Btn.DangerGhost", delegate
		{
			if (Selected(list) is AquilaLocal.Companion.MemoryEntry e)
			{
				Try(() => store.Supersede(e.Id, "", MemoryAuthors.Operator, reason.Text, DateTimeOffset.Now), "Withdrawn; it stays in her history.");
			}
			else
			{
				Problem("Select an entry first.");
			}
		});
		withdraw.Margin = new Thickness(8, 0, 0, 0);
		buttons.Children.Add(withdraw);
		panel.Children.Add(buttons);
		return panel;
	}

	private static AquilaLocal.Companion.MemoryEntry? Selected(ListBox list)
	{
		return (list.SelectedItem as FrameworkElement)?.Tag as AquilaLocal.Companion.MemoryEntry;
	}

	private void Try(Func<long> action, string done)
	{
		try
		{
			action();
			host.Forget(personaId);
			Ok(done);
			Build();
		}
		catch (Exception ex) when (ex is ArgumentException || ex is Microsoft.Data.Sqlite.SqliteException || ex is DriveStateUnavailableException)
		{
			Problem(ex.Message);
		}
	}

	private static UIElement AchievementsView(PersonaStore store)
	{
		List<Achievement> all = store.Achievements();
		if (all.Count == 0)
		{
			return Ui.Text("None yet. Every time she does something unprompted, makes her own choice, or surprises, it's added here, and she can read the latest ones.", "Type.Small");
		}
		ListBox list = new ListBox { Height = 220 };
		foreach (Achievement a in Enumerable.Reverse(all))
		{
			TextBlock t = Ui.Text($"{a.At.LocalDateTime:MMM d h:mm tt} · {a.Note}", "Type.Body");
			t.TextWrapping = TextWrapping.Wrap;
			t.MaxWidth = 700;
			list.Items.Add(t);
		}
		return list;
	}

	private static UIElement LogView(PersonaStore store)
	{
		TextBox box = Multiline(string.Join("\n", Enumerable.Reverse(store.DriveLog(60)).Select((DriveLogEntry e) =>
			string.Format(CultureInfo.InvariantCulture, "{0:MMM d h:mm tt}  {1,-10} {2:0.00} → {3:0.00}  {4}", e.At.LocalDateTime, e.Drive, e.Old, e.New, e.Reason))), 220);
		box.IsReadOnly = true;
		box.FontFamily = Ui.FontMono;
		box.FontSize = 12;
		return box;
	}

	// ---- helpers -------------------------------------------------------------------------------------

	private static Expander Section(string title, UIElement content, bool expanded)
	{
		return new Expander { Header = title, Content = content, IsExpanded = expanded, Style = Ui.Style("Expander.Card"), Margin = new Thickness(0, 0, 0, 12) };
	}

	private static TextBlock Label(string text)
	{
		TextBlock t = Ui.Text(text, "Type.Label");
		t.Margin = new Thickness(0, 12, 0, 6);
		return t;
	}

	private static TextBox Multiline(string text, double height)
	{
		return new TextBox
		{
			Text = text,
			AcceptsReturn = true,
			TextWrapping = TextWrapping.Wrap,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Height = height,
			VerticalContentAlignment = VerticalAlignment.Top
		};
	}

	private static Border Warning(string text)
	{
		TextBlock t = Ui.Text(text, "Type.Small");
		t.TextWrapping = TextWrapping.Wrap;
		t.Foreground = Ui.Brush("Danger");
		return new Border
		{
			Child = t,
			Background = Ui.Brush("Danger.Subtle"),
			BorderBrush = Ui.Brush("Danger.SubtleStroke"),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(8),
			Padding = new Thickness(12),
			Margin = new Thickness(0, 0, 0, 12)
		};
	}
}
