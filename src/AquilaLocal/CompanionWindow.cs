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

	/// <summary>Wardrobe diary entries to highlight (from a chat's "Changed outfit" notice).</summary>
	private readonly HashSet<long> highlightWardrobe;

	/// <summary>The voices she can be given (from the app's voice library).</summary>
	private readonly Func<List<(string Key, string Label)>>? voices;

	public CompanionWindow(CompanionHost host, Action<string, string> startChat, string? focusPersona = null, IEnumerable<long>? highlightMemory = null, IEnumerable<long>? highlightWardrobe = null, Func<List<(string Key, string Label)>>? voices = null)
	{
		this.voices = voices;
		this.host = host;
		this.startChat = startChat;
		highlight = new HashSet<long>(highlightMemory ?? Enumerable.Empty<long>());
		this.highlightWardrobe = new HashSet<long>(highlightWardrobe ?? Enumerable.Empty<long>());
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

	/// <summary>Sections the person has open, kept open when the window rebuilds after a change (adding a memory etc.).</summary>
	private readonly HashSet<string> openSections = new HashSet<string>();

	private string builtFor = "";

	private void Build()
	{
		// Rebuilding after a change (Add entry, Save…) keeps the person where they were: same sections open, same scroll.
		double keepScroll = (builtFor == personaId) ? ((Content as DockPanel)?.Children.OfType<ScrollViewer>().FirstOrDefault()?.VerticalOffset ?? 0.0) : 0.0;
		builtFor = personaId;
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
		Button addPersona = Ui.Button("Add a persona from a file…", "Btn.Secondary", delegate
		{
			OpenFileDialog dialog = new OpenFileDialog { Filter = "Persona file (*.json)|*.json", Title = "Choose the new persona's file" };
			if (dialog.ShowDialog(this) != true)
			{
				return;
			}
			try
			{
				PersonaConfig added = host.AddPersonaFromFile(dialog.FileName);
				personaId = added.Id;
				Ok("Added " + added.Name + ". Check her settings, then press \"Set up " + added.Name + "\" once to create her private memory.");
			}
			catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException)
			{
				Problem(ex.Message);
			}
			Build();
		});
		addPersona.Margin = new Thickness(12, 0, 0, 0);
		StackPanel personaRow = new StackPanel { Orientation = Orientation.Horizontal };
		personaRow.Children.Add(picker);
		personaRow.Children.Add(addPersona);
		body.Children.Add(Ui.Group("Persona", personaRow));
		AboutYou? about = null;
		try
		{
			about = host.LoadAboutYou();
		}
		catch (PersonaConfigException ex)
		{
			body.Children.Add(Warning(ex.Message));
		}
		if (about != null)
		{
			body.Children.Add(Section("About you (every companion reads this)", AboutYouView(about), expanded: about.Render().Length == 0));
		}
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
		DockPanel scaffold = Ui.DialogScaffold("Companions", "Companions are personas with their own personality, drives, private memory and chat history. Nothing one companion remembers is ever shown to another. Type /tune in a companion chat to adjust her on the fly.", body, status, close, primary);
		base.Content = scaffold;
		if (keepScroll > 0.0 && scaffold.Children.OfType<ScrollViewer>().FirstOrDefault() is ScrollViewer scroll)
		{
			scroll.Loaded += delegate
			{
				scroll.UpdateLayout();
				scroll.ScrollToVerticalOffset(keepScroll);
			};
		}
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
		bool hasAppearance = false;
		if (store != null)
		{
			try
			{
				hasAppearance = store.HasAppearance;
			}
			catch (Microsoft.Data.Sqlite.SqliteException ex)
			{
				body.Children.Add(Warning("Her appearance card couldn't be read: " + ex.Message));
			}
			Expander appearanceSection = Section("Appearance", AppearanceView(config, store, hasAppearance), expanded: highlightWardrobe.Count > 0 || !hasAppearance);
			body.Children.Add(appearanceSection);
			if (highlightWardrobe.Count > 0)
			{
				appearanceSection.Loaded += delegate
				{
					appearanceSection.BringIntoView();
				};
			}
		}
		body.Children.Add(Section("Settings (saved to persona.json)", SettingsEditor(config, st.StoreExists, editAppearance: !st.StoreExists || (store != null && !hasAppearance)), expanded: !st.Ready || (store != null && !hasAppearance)));
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
			name.ToolTip = (d.Description.Length > 0 ? d.Description + "\n\n" : "") + (d.IsLevel ? "A feeling level: the number is how much of it she has. Low presses on her." : "A need: the number is how much she wants it. High presses on her.");
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

	private UIElement SettingsEditor(PersonaConfig config, bool setUp, bool editAppearance)
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

		panel.Children.Add(Label("Age, and the words used for them"));
		TextBox age = new TextBox { Text = config.Age > 0 ? config.Age.ToString(System.Globalization.CultureInfo.InvariantCulture) : "", Width = 80, MaxLength = 3, ToolTip = "Companions are always adults: 18 or older. Leave empty to not show an age." };
		ComboBox gender = new ComboBox { ItemsSource = new[] { "Female (she/her)", "Male (he/him)" }, SelectedIndex = Genders.IsMale(config.Gender) ? 1 : 0, Width = 180, Margin = new Thickness(10, 0, 0, 0) };
		panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { age, gender } });
		panel.Children.Add(Label("Card description (shown in My Companions)"));
		TextBox tagline = Multiline(config.Tagline, 60);
		tagline.MaxLength = PersonaConfig.MaxTaglineChars;
		panel.Children.Add(tagline);

		panel.Children.Add(Label("Her voice (when replies are spoken)"));
		List<(string Key, string Label)> choices = new List<(string Key, string Label)> { ("", "Same as Aquila's voice (set in Voice…)") };
		try
		{
			choices.AddRange(voices?.Invoke() ?? new List<(string Key, string Label)>());
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
		{
		}
		if (config.Voice.Length > 0 && !choices.Any(((string Key, string Label) c) => c.Key == config.Voice))
		{
			choices.Add((config.Voice, config.Voice + " (not found)"));
		}
		ComboBox voice = new ComboBox { ItemsSource = choices.Select(((string Key, string Label) c) => c.Label).ToList(), SelectedIndex = Math.Max(0, choices.FindIndex(((string Key, string Label) c) => c.Key == config.Voice)), Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
		panel.Children.Add(voice);
		TextBlock voiceNote = Ui.Text("To give her a voice of her own, import a short clip in Voice… → Import voice sample, then pick it here.", "Type.Caption");
		voiceNote.TextWrapping = TextWrapping.Wrap;
		panel.Children.Add(voiceNote);

		panel.Children.Add(Label("Her favourite colour (her name is shown in it, under the avatar)"));
		TextBox favourite = new TextBox { Text = config.FavoriteColor, Width = 320, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "A colour name like teal, crimson or lavender, or a code like #4FC3F7. Leave empty for the app's colour." };
		Border swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(4), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
		void ShowSwatch()
		{
			swatch.Background = MainWindow.TryColour(favourite.Text, out Color c) ? new SolidColorBrush(c) : Brushes.Transparent;
		}
		favourite.TextChanged += delegate { ShowSwatch(); };
		ShowSwatch();
		panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { favourite, swatch } });

		panel.Children.Add(Label("Her avatar (the animated pictures beside the chat)"));
		// Choices: the shared avatar, any set in Data\avatars, a new set of her own, or a folder anywhere.
		string ownName = SpriteLibrary.SafeName(config.Name.Length > 0 ? config.Name : personaId);
		List<(string Key, string Label)> avatarChoices = new List<(string Key, string Label)> { ("", "The shared avatar (Data\\avatar)") };
		try
		{
			if (Directory.Exists(host.AvatarsRoot))
			{
				foreach (string dir in Directory.EnumerateDirectories(host.AvatarsRoot).OrderBy((string d) => d, StringComparer.OrdinalIgnoreCase))
				{
					string n = Path.GetFileName(dir);
					avatarChoices.Add((n, n + " (Data\\avatars\\" + n + ")"));
				}
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
		}
		if (!avatarChoices.Any(((string Key, string Label) c) => string.Equals(c.Key, ownName, StringComparison.OrdinalIgnoreCase)))
		{
			avatarChoices.Add((ownName, "A new avatar of her own (Data\\avatars\\" + ownName + ", starts with placeholder pictures)"));
		}
		if (config.Avatar.Length > 0 && !avatarChoices.Any(((string Key, string Label) c) => string.Equals(c.Key, config.Avatar, StringComparison.OrdinalIgnoreCase)))
		{
			avatarChoices.Add((config.Avatar, config.Avatar));
		}
		const string ChooseFolder = "\u0001choose";
		avatarChoices.Add((ChooseFolder, "Choose a folder…"));
		ComboBox avatarBox = new ComboBox { ItemsSource = avatarChoices.Select(((string Key, string Label) c) => c.Label).ToList(), Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
		avatarBox.SelectedIndex = Math.Max(0, avatarChoices.FindIndex(((string Key, string Label) c) => string.Equals(c.Key, config.Avatar, StringComparison.OrdinalIgnoreCase)));
		int lastAvatar = avatarBox.SelectedIndex;
		avatarBox.SelectionChanged += delegate
		{
			if (avatarBox.SelectedIndex < 0 || avatarChoices[avatarBox.SelectedIndex].Key != ChooseFolder)
			{
				lastAvatar = avatarBox.SelectedIndex;
				return;
			}
			OpenFolderDialog pick = new OpenFolderDialog { Title = "Choose the folder with her animations (one subfolder per animation)" };
			if (pick.ShowDialog(this) == true)
			{
				avatarChoices.Insert(avatarChoices.Count - 1, (pick.FolderName, pick.FolderName));
				avatarBox.ItemsSource = avatarChoices.Select(((string Key, string Label) c) => c.Label).ToList();
				avatarBox.SelectedIndex = avatarChoices.Count - 2;
			}
			else
			{
				avatarBox.SelectedIndex = lastAvatar;
			}
		};
		panel.Children.Add(avatarBox);
		TextBlock avatarNote = Ui.Text("Her chats show this avatar, and Animations… edits it while her chat is open.", "Type.Caption");
		avatarNote.TextWrapping = TextWrapping.Wrap;
		panel.Children.Add(avatarNote);

		panel.Children.Add(Label("Drives"));
		DriveEditor driveEditor = new DriveEditor(config, setUp);
		panel.Children.Add(driveEditor.View);

		TextBox seed = null;
		if (!setUp)
		{
			panel.Children.Add(Label("Starting summary (one item per line; Facts:, Feelings:, Hooks: headings)"));
			seed = Multiline(SeedText(config.Seed.Summary), 160);
			panel.Children.Add(seed);
		}

		TextBox traits = null, anchor = null, outfit = null, color = null, fabric = null, condition = null;
		if (editAppearance)
		{
			AppearanceSeed a = config.Seed.Appearance;
			panel.Children.Add(Label("Appearance: permanent traits (one per line, like  eyes: grey-green). Locked once her card is set up."));
			traits = Multiline(string.Join("\n", a.Traits.Select((AppearanceTrait t) => t.Name + ": " + t.Text)), 120);
			panel.Children.Add(traits);
			panel.Children.Add(Label("Identity anchor (the one detail every picture of her must show)"));
			anchor = new TextBox { Text = a.IdentityAnchor, MaxLength = AppearanceSeed.MaxTraitChars };
			panel.Children.Add(anchor);
			panel.Children.Add(Label("What she's wearing at the start (she can change it later; every change is logged)"));
			outfit = Field(panel, "Outfit", a.Wardrobe.Outfit);
			color = Field(panel, "Colour", a.Wardrobe.Color);
			fabric = Field(panel, "Fabric", a.Wardrobe.Fabric);
			condition = Field(panel, "Condition", a.Wardrobe.Condition);
		}

		Button save = Ui.Button("Save settings", "Btn.Primary", delegate
		{
			try
			{
				PersonaConfig edited = host.Registry.LoadConfig(personaId);
				edited.PersonalitySheet = sheet.Text.Trim();
				edited.Drift.Text = drift.Text.Trim();
				edited.Drift.Unlocked = unlocked.IsChecked == true;
				edited.Voice = choices[Math.Max(0, voice.SelectedIndex)].Key;
				if (favourite.Text.Trim().Length > 0 && !MainWindow.TryColour(favourite.Text, out _))
				{
					throw new PersonaConfigException("\u201C" + favourite.Text.Trim() + "\u201D isn't a colour Aquila knows. Use a name like teal or a code like #4FC3F7.");
				}
				edited.FavoriteColor = favourite.Text.Trim();
				string ageText = age.Text.Trim();
				if (ageText.Length == 0)
				{
					edited.Age = 0;
				}
				else if (!int.TryParse(ageText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int years) || years < PersonaConfig.MinAge || years > PersonaConfig.MaxAge)
				{
					throw new PersonaConfigException("Age must be a number from " + PersonaConfig.MinAge + " to " + PersonaConfig.MaxAge + ": companions are always adults.");
				}
				else
				{
					edited.Age = years;
				}
				edited.Gender = gender.SelectedIndex == 1 ? Genders.Male : Genders.Female;
				edited.Tagline = tagline.Text.Trim();
				string avatarKey = avatarChoices[Math.Max(0, avatarBox.SelectedIndex)].Key;
				edited.Avatar = avatarKey == ChooseFolder ? edited.Avatar : avatarKey;
				Dictionary<string, string> renamed = driveEditor.ApplyTo(edited);
				if (seed != null)
				{
					edited.Seed.Summary = ParseSeed(seed.Text);
				}
				if (traits != null)
				{
					edited.Seed.Appearance = new AppearanceSeed
					{
						Traits = ParseTraits(traits.Text),
						IdentityAnchor = anchor.Text.Trim(),
						Wardrobe = new Wardrobe { Outfit = outfit.Text.Trim(), Color = color.Text.Trim(), Fabric = fabric.Text.Trim(), Condition = condition.Text.Trim() }
					};
				}
				edited.ValidateShape();
				List<string> moved = new List<string>();
				if (setUp)
				{
					// Her stored values follow her drives (renamed, added, removed), all logged, before the new settings are saved.
					PersonaStore driveStore = host.Registry.OpenStore(personaId);
					moved = driveStore.ReshapeDrives(edited, renamed, DateTimeOffset.Now);
				}
				edited.Save(host.ConfigPath(personaId));
				host.Forget(personaId);
				Ok("Saved. Changes apply from her next message." + (moved.Count > 0 ? " Drives: " + string.Join("; ", moved) + "." : ""));
				Build();
			}
			catch (Exception ex) when (ex is PersonaConfigException || ex is FormatException || ex is IOException || ex is UnauthorizedAccessException || ex is DriveStateUnavailableException || ex is Microsoft.Data.Sqlite.SqliteException)
			{
				Problem("Not saved: " + ex.Message);
			}
		});
		save.HorizontalAlignment = HorizontalAlignment.Left;
		save.Margin = new Thickness(0, 14, 0, 0);
		panel.Children.Add(save);
		return panel;
	}

	private static TextBox Field(Panel panel, string caption, string text)
	{
		panel.Children.Add(Ui.Text(caption, "Type.Caption"));
		TextBox box = new TextBox { Text = text, MaxLength = 200, Margin = new Thickness(0, 2, 0, 6) };
		panel.Children.Add(box);
		return box;
	}

	private static List<AppearanceTrait> ParseTraits(string text)
	{
		List<AppearanceTrait> list = new List<AppearanceTrait>();
		foreach (string raw in text.Replace("\r", "").Split('\n'))
		{
			string line = raw.Trim().TrimStart('-', '•').Trim();
			if (line.Length == 0)
			{
				continue;
			}
			int colon = line.IndexOf(':');
			if (colon <= 0 || colon == line.Length - 1)
			{
				throw new FormatException("Write each trait as  name: description  (for example  eyes: grey-green). This line has no name: " + line);
			}
			list.Add(new AppearanceTrait { Name = line.Substring(0, colon).Trim(), Text = line.Substring(colon + 1).Trim() });
		}
		return list;
	}

	// ---- about you -----------------------------------------------------------------------------------

	private UIElement AboutYouView(AboutYou about)
	{
		StackPanel panel = new StackPanel();
		TextBlock intro = Ui.Text("Tell your companions who you are. They read this at the start of every reply. Your picture is shown to them at the start of each chat, and again whenever you change it (this needs a vision model, which you have).", "Type.Small");
		intro.TextWrapping = TextWrapping.Wrap;
		intro.Margin = new Thickness(0, 0, 0, 8);
		panel.Children.Add(intro);
		TextBox nickname = Field(panel, "What they should call you (nickname)", about.Nickname);
		TextBox age = Field(panel, "Age", about.Age);
		panel.Children.Add(Ui.Text("Your appearance", "Type.Caption"));
		TextBox appearance = Multiline(about.Appearance, 70);
		appearance.MaxLength = AboutYou.MaxFieldChars;
		panel.Children.Add(appearance);
		panel.Children.Add(Ui.Text("Your background", "Type.Caption"));
		TextBox background = Multiline(about.Background, 90);
		background.MaxLength = AboutYou.MaxFieldChars;
		panel.Children.Add(background);
		panel.Children.Add(Ui.Text("Anything else they should know", "Type.Caption"));
		TextBox other = Multiline(about.Other, 70);
		other.MaxLength = AboutYou.MaxFieldChars;
		panel.Children.Add(other);
		Button save = Ui.Button("Save About you", "Btn.Primary", delegate
		{
			try
			{
				host.SaveAboutYou(new AboutYou { Nickname = nickname.Text.Trim(), Age = age.Text.Trim(), Appearance = appearance.Text.Trim(), Background = background.Text.Trim(), Other = other.Text.Trim() });
				Ok("Saved. Your companions read it from their next reply.");
				Build();
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				Problem("Not saved: " + ex.Message);
			}
		});
		save.HorizontalAlignment = HorizontalAlignment.Left;
		save.Margin = new Thickness(0, 10, 0, 0);
		panel.Children.Add(save);

		panel.Children.Add(Label("Your picture"));
		WrapPanel row = new WrapPanel();
		Attachment? picture = null;
		try
		{
			picture = host.AboutYouPicture();
		}
		catch (PersonaConfigException ex)
		{
			panel.Children.Add(Warning(ex.Message));
		}
		if (picture != null)
		{
			try
			{
				System.Windows.Media.Imaging.BitmapImage image = new System.Windows.Media.Imaging.BitmapImage();
				image.BeginInit();
				image.StreamSource = new MemoryStream(Convert.FromBase64String(picture.Base64));
				image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
				image.DecodePixelHeight = 160;
				image.EndInit();
				image.Freeze();
				row.Children.Add(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Margin = new Thickness(0, 0, 12, 8), Child = new System.Windows.Controls.Image { Source = image, Height = 120, Stretch = Stretch.Uniform } });
			}
			catch (Exception ex) when (ex is FormatException || ex is NotSupportedException || ex is IOException)
			{
			}
		}
		Button choose = Ui.Button(picture == null ? "Choose a picture of you…" : "Change picture…", "Btn.Secondary", delegate
		{
			OpenFileDialog dialog = new OpenFileDialog { Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff", Title = "Choose a picture of you" };
			if (dialog.ShowDialog(this) != true)
			{
				return;
			}
			try
			{
				host.SetAboutYouPicture(dialog.FileName);
				Ok("Picture saved. Your companions will see it in their next reply.");
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException || ex is FileFormatException || ex is InvalidOperationException)
			{
				Problem("That picture couldn't be used: " + ex.Message);
			}
			Build();
		});
		choose.Margin = new Thickness(0, 0, 8, 8);
		choose.VerticalAlignment = VerticalAlignment.Bottom;
		row.Children.Add(choose);
		if (picture != null)
		{
			Button remove = Ui.Button("Remove", "Btn.Ghost", delegate
			{
				try
				{
					host.RemoveAboutYouPicture();
					Ok("Picture removed.");
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					Problem(ex.Message);
				}
				Build();
			});
			remove.Margin = new Thickness(0, 0, 8, 8);
			remove.VerticalAlignment = VerticalAlignment.Bottom;
			row.Children.Add(remove);
		}
		panel.Children.Add(row);
		return panel;
	}

	// ---- appearance ----------------------------------------------------------------------------------

	private UIElement AppearanceView(PersonaConfig config, PersonaStore store, bool hasAppearance)
	{
		StackPanel panel = new StackPanel();
		if (!hasAppearance)
		{
			TextBlock note = Ui.Text(config.Seed.Appearance.IsComplete
				? config.Name + " doesn't have an appearance card yet, so she can't chat until it's set up. Her settings below have one ready: set it up to lock in her permanent traits and start her wardrobe diary."
				: config.Name + " doesn't have an appearance card yet, so she can't chat until it's set up. Fill in her appearance in Settings below, save, then set it up here.", "Type.Body");
			note.TextWrapping = TextWrapping.Wrap;
			panel.Children.Add(note);
			Button setUp = Ui.Button("Set up her appearance card", "Btn.Primary", delegate
			{
				if (!AquilaDialog.Confirm(this, "Lock in " + config.Name + "'s appearance?", "Her permanent traits and identity anchor become part of her and can only be amended deliberately, one at a time, with a reason. Her outfit stays hers to change.", "Set up"))
				{
					return;
				}
				try
				{
					store.SeedAppearance(host.Registry.LoadConfig(personaId).Seed.Appearance, DateTimeOffset.Now);
					host.Forget(personaId);
					Ok(config.Name + "'s appearance card is set up.");
				}
				catch (Exception ex) when (ex is PersonaConfigException || ex is InvalidOperationException || ex is Microsoft.Data.Sqlite.SqliteException)
				{
					Problem(ex.Message);
				}
				Build();
			});
			setUp.IsEnabled = config.Seed.Appearance.IsComplete;
			setUp.HorizontalAlignment = HorizontalAlignment.Left;
			setUp.Margin = new Thickness(0, 10, 0, 0);
			panel.Children.Add(setUp);
			return panel;
		}
		AppearanceCard card;
		List<WardrobeEntry> diary;
		List<TraitEntry> traitHistory;
		try
		{
			card = store.ReadAppearance();
			diary = store.WardrobeHistory();
			traitHistory = store.TraitHistory();
		}
		catch (Exception ex) when (ex is AppearanceCardUnavailableException || ex is Microsoft.Data.Sqlite.SqliteException)
		{
			panel.Children.Add(Warning(ex.Message));
			return panel;
		}
		TextBlock intro = Ui.Text("Her canonical appearance card. She reads it before every reply, and any picture of her is described from it, never guessed. Permanent traits are locked; her clothes are hers to change, and every change goes into her wardrobe diary with the time and a reason.", "Type.Small");
		intro.TextWrapping = TextWrapping.Wrap;
		intro.Margin = new Thickness(0, 0, 0, 8);
		panel.Children.Add(intro);

		panel.Children.Add(Label("Permanent traits (locked)"));
		foreach (TraitEntry t in card.Traits)
		{
			TextBlock line = Ui.Text(t.Name + ": " + t.Text, "Type.Body");
			line.TextWrapping = TextWrapping.Wrap;
			panel.Children.Add(line);
		}
		TextBlock anchorLine = Ui.Text("Identity anchor: " + card.IdentityAnchor, "Type.Body");
		anchorLine.TextWrapping = TextWrapping.Wrap;
		anchorLine.FontWeight = FontWeights.SemiBold;
		anchorLine.Margin = new Thickness(0, 4, 0, 0);
		panel.Children.Add(anchorLine);

		panel.Children.Add(Label("Wearing now"));
		TextBlock wearing = Ui.Text(ContextInjector.WardrobeText(card.Wardrobe.Wardrobe), "Type.Body");
		wearing.TextWrapping = TextWrapping.Wrap;
		panel.Children.Add(wearing);
		panel.Children.Add(Ui.Text($"since {card.Wardrobe.At.LocalDateTime:MMM d, yyyy h:mm tt} · {Who(card.Wardrobe.Author, config.Name)} · {card.Wardrobe.Reason}", "Type.Caption"));

		TextBox caption = Multiline(AppearanceCaption.For(config.Name, card), 70);
		caption.IsReadOnly = true;
		panel.Children.Add(Label("Image caption (made from her card, for the picture generator)"));
		panel.Children.Add(caption);
		Button copy = Ui.Button(Ui.IconLabel("\uE8C8", "Copy image caption"), "Btn.Secondary", delegate
		{
			try
			{
				Clipboard.SetText(caption.Text);
				Ok("Caption copied.");
			}
			catch (System.Runtime.InteropServices.ExternalException ex)
			{
				Problem("Couldn't copy: " + ex.Message);
			}
		});
		copy.HorizontalAlignment = HorizontalAlignment.Left;
		copy.Margin = new Thickness(0, 6, 0, 0);
		panel.Children.Add(copy);

		panel.Children.Add(Label("Wardrobe diary (newest first)"));
		ListBox list = new ListBox { Height = 200 };
		foreach (WardrobeEntry e in Enumerable.Reverse(diary))
		{
			StackPanel item = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
			TextBlock text = Ui.Text(ContextInjector.WardrobeText(e.Wardrobe), "Type.Body");
			text.TextWrapping = TextWrapping.Wrap;
			text.MaxWidth = 700;
			item.Children.Add(text);
			item.Children.Add(Ui.Text($"{e.At.LocalDateTime:MMM d, yyyy h:mm tt} · {Who(e.Author, config.Name)} · {e.Reason}", "Type.Caption"));
			if (highlightWardrobe.Contains(e.Id))
			{
				list.Items.Add(new Border { Child = item, Background = Ui.Brush("Accent.Subtle"), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 2, 6, 2) });
				list.SelectedItem ??= list.Items[list.Items.Count - 1];
			}
			else
			{
				list.Items.Add(item);
			}
		}
		panel.Children.Add(list);

		StackPanel change = new StackPanel();
		Wardrobe now = card.Wardrobe.Wardrobe;
		TextBox outfit = Field(change, "Outfit", now.Outfit);
		TextBox color = Field(change, "Colour", now.Color);
		TextBox fabric = Field(change, "Fabric", now.Fabric);
		TextBox condition = Field(change, "Condition", now.Condition);
		TextBox reason = Field(change, "Reason (required)", "");
		Button log = Ui.Button("Log outfit change", "Btn.Secondary", delegate
		{
			Wardrobe next = new Wardrobe { Outfit = outfit.Text.Trim(), Color = color.Text.Trim(), Fabric = fabric.Text.Trim(), Condition = condition.Text.Trim() };
			Try(() => store.AppendWardrobe(next, MemoryAuthors.Operator, reason.Text, DateTimeOffset.Now), "Logged in her wardrobe diary.");
		});
		log.HorizontalAlignment = HorizontalAlignment.Left;
		change.Children.Add(log);
		panel.Children.Add(new Expander { Header = "Change her outfit yourself", Content = change, Style = Ui.Style("Expander.Quiet"), Margin = new Thickness(0, 8, 0, 0) });

		StackPanel amend = new StackPanel();
		TextBlock amendNote = Ui.Text("Only for correcting a mistake or a deliberate change to who she is. The original stays in her history with your reason.", "Type.Small");
		amendNote.TextWrapping = TextWrapping.Wrap;
		amend.Children.Add(amendNote);
		ComboBox traitName = new ComboBox { IsEditable = true, ItemsSource = card.Traits.Select((TraitEntry t) => t.Name).Append(PersonaStore.AnchorTrait).ToList(), Margin = new Thickness(0, 6, 0, 6), Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
		amend.Children.Add(Ui.Text("Trait (pick one, or type a new name to add a trait)", "Type.Caption"));
		amend.Children.Add(traitName);
		TextBox traitText = Field(amend, "New description (empty withdraws the trait)", "");
		TextBox traitReason = Field(amend, "Reason (required)", "");
		traitName.SelectionChanged += delegate
		{
			if (traitName.SelectedItem is string n)
			{
				traitText.Text = n == PersonaStore.AnchorTrait ? card.IdentityAnchor : card.Traits.FirstOrDefault((TraitEntry t) => t.Name == n)?.Text ?? "";
			}
		};
		Button record = Ui.Button("Record amendment", "Btn.DangerGhost", delegate
		{
			string n = traitName.Text.Trim();
			if (!AquilaDialog.Confirm(this, "Amend " + config.Name + "'s permanent trait?", "'" + n + "' will read: " + (traitText.Text.Trim().Length > 0 ? traitText.Text.Trim() : "(withdrawn)") + "\n\nThe original stays in her history.", "Amend"))
			{
				return;
			}
			Try(() => store.AmendTrait(n, traitText.Text, MemoryAuthors.Operator, traitReason.Text, DateTimeOffset.Now), "Amendment recorded; the original is kept in her history.");
		});
		record.HorizontalAlignment = HorizontalAlignment.Left;
		amend.Children.Add(record);
		List<TraitEntry> replaced = traitHistory.Where((TraitEntry t) => !t.Active).ToList();
		if (replaced.Count > 0)
		{
			amend.Children.Add(Label("Earlier versions"));
			foreach (TraitEntry t in replaced)
			{
				TraitEntry? by = traitHistory.FirstOrDefault((TraitEntry x) => x.Id == t.SupersededBy);
				TextBlock old = Ui.Text($"{t.Name}: {t.Text}  ·  replaced {by?.At.LocalDateTime:MMM d, yyyy} ({by?.Reason})", "Type.Small");
				old.TextWrapping = TextWrapping.Wrap;
				amend.Children.Add(old);
			}
		}
		panel.Children.Add(new Expander { Header = "Amend a permanent trait", Content = amend, Style = Ui.Style("Expander.Quiet"), Margin = new Thickness(0, 6, 0, 0) });
		if (list.SelectedItem != null)
		{
			list.Loaded += delegate
			{
				list.ScrollIntoView(list.SelectedItem);
			};
		}
		return panel;
	}

	private static string Who(string author, string name)
	{
		return author switch
		{
			MemoryAuthors.Persona => "by " + name,
			MemoryAuthors.Operator => "by you",
			_ => "from her appearance card"
		};
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
		catch (Exception ex) when (ex is ArgumentException || ex is Microsoft.Data.Sqlite.SqliteException || ex is DriveStateUnavailableException || ex is InvalidOperationException || ex is AppearanceCardUnavailableException)
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

	private Expander Section(string title, UIElement content, bool expanded)
	{
		Expander section = new Expander { Header = title, Content = content, IsExpanded = expanded || openSections.Contains(title), Style = Ui.Style("Expander.Card"), Margin = new Thickness(0, 0, 0, 12) };
		if (section.IsExpanded)
		{
			openSections.Add(title);
		}
		section.Expanded += delegate
		{
			openSections.Add(title);
		};
		section.Collapsed += delegate
		{
			openSections.Remove(title);
		};
		return section;
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
