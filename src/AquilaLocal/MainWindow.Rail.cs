using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Pictures;

namespace AquilaLocal;

/// <summary>
/// The icon rail on the far left (Create Companion, Chats, Generate, My Companions, Soundboard, Avatars, Voices,
/// and the Settings menu at the bottom), the chats panel's search, and the pages that open over the conversation.
/// </summary>
public partial class MainWindow
{
	private const double RailNarrow = 64.0;
	private const double RailWide = 220.0;

	private static class Screens
	{
		public const string Chats = "chats";
		public const string Create = "create";
		public const string Generate = "generate";
		public const string Companions = "companions";
	}

	/// <summary>Which page is showing: the conversation (chats) or one that covers it.</summary>
	private string screen = Screens.Chats;

	/// <summary>The companion the Generate page was opened for ("" = none chosen yet).</summary>
	private string generateFor = "";

	private readonly Dictionary<string, Button> railScreens = new Dictionary<string, Button>();

	private readonly List<TextBlock> railLabels = new List<TextBlock>();

	private readonly List<Button> railButtons = new List<Button>();

	private Button? railExpand;

	private Button? railVoices;

	private ImageSource? crest;

	private double chatsPanelWidth = 272.0;

	private void BuildRail()
	{
		crest = TryFindResource("Logo.Crest") as ImageSource;
		chatsPanelWidth = settings.SidebarWidth;
		ChatSession.PictureFor = (ChatSession s) => s.IsCompanion ? companion.ProfilePicture(s.PersonaId) : crest;
		RailPanel.Children.Clear();

		// Logo, then Expand.
		Grid brand = new Grid { Height = 44, Margin = new Thickness(0, 0, 0, 6) };
		brand.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
		brand.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		brand.Children.Add(new Image { Source = crest, Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
		TextBlock brandName = Ui.Text("Aquila", "Type.Subtitle");
		brandName.VerticalAlignment = VerticalAlignment.Center;
		brandName.TextWrapping = TextWrapping.NoWrap;
		Grid.SetColumn(brandName, 1);
		brand.Children.Add(brandName);
		railLabels.Add(brandName);
		DockPanel.SetDock(brand, Dock.Top);
		RailPanel.Children.Add(brand);

		railExpand = RailItem("", "Collapse", "Show the names of these buttons", delegate { SetRailExpanded(!settings.RailExpanded, save: true); });
		Top(railExpand);
		Border line = new Border { Height = 1, Background = Ui.Brush("Stroke.Subtle"), Margin = new Thickness(8, 6, 8, 8) };
		DockPanel.SetDock(line, Dock.Top);
		RailPanel.Children.Add(line);

		Top(Screen(Screens.Create, "", "Create Companion", "Create a new companion, step by step"));
		Top(Screen(Screens.Chats, "", "Chats", "Your conversations"));
		Top(Screen(Screens.Generate, "", "Generate", "Make pictures and videos of your companions"));
		Top(Screen(Screens.Companions, "", "My Companions", "See all your companions at once"));
		Top(RailItem("", "Soundboard", "Sounds your companions can play", delegate { Soundboard_Click(this, new RoutedEventArgs()); }));
		Top(RailItem("", "Avatars", "Change the avatar's pictures and gestures", delegate { Avatar_Click(this, new RoutedEventArgs()); }));
		railVoices = RailItem("", "Voices", "How Aquila and your companions sound when replies are read aloud", delegate { Voice_Click(this, new RoutedEventArgs()); });
		Top(railVoices);

		Button settingsButton = RailItem("", "Settings", "Settings: memories, internet, pictures and everything else", null);
		settingsButton.Click += delegate { OpenSettingsMenu(settingsButton); };
		DockPanel.SetDock(settingsButton, Dock.Bottom);
		RailPanel.Children.Add(settingsButton);

		// Clicking a chat (or New chat) always brings you back to the conversation.
		ChatsList.PreviewMouseLeftButtonDown += delegate
		{
			if (screen != Screens.Chats)
			{
				ShowScreen(Screens.Chats);
			}
		};
		NewButton.Click += delegate { ShowScreen(Screens.Chats); };
		SetRailExpanded(settings.RailExpanded, save: false);
		HighlightRail();
	}

	private void Top(Button button)
	{
		DockPanel.SetDock(button, Dock.Top);
		RailPanel.Children.Add(button);
	}

	private Button Screen(string key, string glyph, string label, string tip)
	{
		Button b = RailItem(glyph, label, tip, delegate { ShowScreen(key); });
		railScreens[key] = b;
		return b;
	}

	/// <summary>One rail button: its icon in a fixed 48 px column, and its name beside it (shown when the rail is expanded).</summary>
	private Button RailItem(string glyph, string label, string tip, RoutedEventHandler? click)
	{
		Grid content = new Grid { Height = 44 };
		content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
		content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		TextBlock icon = Ui.Icon(glyph, 18);
		icon.Tag = "icon";
		content.Children.Add(icon);
		TextBlock name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
		Grid.SetColumn(name, 1);
		content.Children.Add(name);
		railLabels.Add(name);
		Button b = new Button
		{
			Style = Ui.Style("Btn.Ghost"),
			Content = content,
			Padding = new Thickness(0),
			MinHeight = 0,
			Height = 44,
			Margin = new Thickness(0, 0, 0, 4),
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
			ToolTip = label + "\n" + tip,
		};
		ToolTipService.SetPlacement(b, PlacementMode.Right);
		ToolTipService.SetInitialShowDelay(b, 250);
		AutomationProperties(b, label);
		if (click != null)
		{
			b.Click += click;
		}
		railButtons.Add(b);
		return b;
	}

	private static void AutomationProperties(Button b, string label)
	{
		System.Windows.Automation.AutomationProperties.SetName(b, label);
	}

	/// <summary>Expanded: the rail shows its names and takes the chats panel's place. Narrow: icons only, chats panel beside it.</summary>
	private void SetRailExpanded(bool expanded, bool save)
	{
		if (expanded && !settings.RailExpanded && SidebarColumn.ActualWidth > 0)
		{
			chatsPanelWidth = SidebarColumn.ActualWidth;
		}
		settings.RailExpanded = expanded;
		Rail.Width = expanded ? RailWide : RailNarrow;
		foreach (TextBlock label in railLabels)
		{
			label.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
		}
		if (railExpand != null)
		{
			Grid g = (Grid)railExpand.Content;
			((TextBlock)g.Children[0]).Text = expanded ? "" : "";
			((TextBlock)g.Children[1]).Text = "Collapse";
			railExpand.ToolTip = expanded ? "Collapse\nIcons only, with your chats beside them" : "Expand\nShow the names of these buttons";
		}
		ChatsPanel.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
		SidebarSplitter.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
		if (expanded)
		{
			SidebarColumn.MinWidth = 0;
			SidebarColumn.Width = new GridLength(0);
		}
		else
		{
			SidebarColumn.MinWidth = 200;
			SidebarColumn.Width = new GridLength(Math.Clamp(chatsPanelWidth, 200, SidebarColumn.MaxWidth));
		}
		if (save)
		{
			SaveLayout();
		}
	}

	private void HighlightRail()
	{
		foreach (KeyValuePair<string, Button> pair in railScreens)
		{
			bool on = pair.Key == screen;
			pair.Value.Background = on ? Ui.Brush("Accent.Subtle") : Brushes.Transparent;
			Grid g = (Grid)pair.Value.Content;
			((TextBlock)g.Children[0]).Foreground = on ? Ui.Brush("Accent") : Ui.Brush("Text.Secondary");
			((TextBlock)g.Children[1]).Foreground = on ? Ui.Brush("Text.Primary") : Ui.Brush("Text.Secondary");
			((TextBlock)g.Children[1]).FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
		}
	}

	/// <summary>Voices can't change mid-reply.</summary>
	private void SetRailBusy(bool value)
	{
		if (railVoices != null)
		{
			railVoices.IsEnabled = !value;
		}
	}

	private void ShowScreen(string key)
	{
		if (key == Screens.Chats && settings.RailExpanded && screen == Screens.Chats)
		{
			// Already on your chats with the rail expanded: step it aside so the chat list shows.
			SetRailExpanded(false, save: true);
		}
		if (key != Screens.Companions)
		{
			companionDetail = null;
		}
		screen = key;
		HighlightRail();
		if (key == Screens.Chats)
		{
			PageHost.Child = null;
			PageHost.Visibility = Visibility.Collapsed;
			return;
		}
		PageHost.Child = key switch
		{
			Screens.Companions => CompanionsPage(),
			Screens.Generate => GeneratePage(),
			_ => CreatePage()
		};
		PageHost.Visibility = Visibility.Visible;
	}

	/// <summary>Rebuilds the page on show (after a companion's settings or picture changed).</summary>
	private void RefreshPage()
	{
		if (screen != Screens.Chats)
		{
			ShowScreen(screen);
		}
		ChatsList.Items.Refresh();
	}

	// ---------------- Settings menu ----------------

	private void OpenSettingsMenu(Button anchor)
	{
		ContextMenu menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Right, HorizontalOffset = 6 };
		menu.Items.Add(MenuEntry("", "Memories…", "What Aquila remembers about you", !busy, delegate { Memories_Click(this, new RoutedEventArgs()); }));
		menu.Items.Add(MenuEntry("", "Internet…", "Choose whether Aquila may look things up online", true, delegate { Internet_Click(this, new RoutedEventArgs()); }));
		menu.Items.Add(MenuEntry("", "Pictures…", "Let your companions and Aquila draw pictures", true, delegate { Pictures_Click(this, new RoutedEventArgs()); }));
		menu.Items.Add(new Separator());
		menu.Items.Add(MenuEntry("", "All settings…", "Model, voice, appearance and more", true, delegate { Settings_Click(this, new RoutedEventArgs()); }));
		menu.IsOpen = true;
	}

	private static MenuItem MenuEntry(string glyph, string header, string tip, bool enabled, RoutedEventHandler click)
	{
		MenuItem item = new MenuItem { Header = header, ToolTip = tip, IsEnabled = enabled, Icon = Ui.Icon(glyph, 14) };
		item.Click += click;
		return item;
	}

	// ---------------- Chat search ----------------

	private void ChatSearch_TextChanged(object sender, TextChangedEventArgs e)
	{
		string q = ChatSearch.Text.Trim();
		ICollectionView view = CollectionViewSource.GetDefaultView(ChatsList.ItemsSource);
		if (view == null)
		{
			return;
		}
		view.Filter = q.Length == 0 ? null : (object o) => o is ChatSession c && Matches(c, q);
		ChatSearchEmpty.Visibility = q.Length > 0 && view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
	}

	/// <summary>A chat matches by its name, who it's with, or anything said in it.</summary>
	private static bool Matches(ChatSession c, string q)
	{
		const StringComparison Ci = StringComparison.CurrentCultureIgnoreCase;
		return c.Title.Contains(q, Ci) || c.PersonaName.Contains(q, Ci) || c.Turns.Any((Turn t) => t.User.Contains(q, Ci) || t.Answer.Contains(q, Ci));
	}

	// ---------------- Pages ----------------

	private static DockPanel Page(string title, string subtitle, FrameworkElement? action)
	{
		DockPanel page = new DockPanel { Margin = new Thickness(36, 28, 36, 0), LastChildFill = true };
		Grid head = new Grid { Margin = new Thickness(0, 0, 0, 20) };
		StackPanel words = new StackPanel();
		words.Children.Add(Ui.Text(title, "Type.Display"));
		TextBlock sub = Ui.Text(subtitle, "Type.Small");
		sub.Margin = new Thickness(0, 4, 0, 0);
		words.Children.Add(sub);
		head.Children.Add(words);
		if (action != null)
		{
			action.HorizontalAlignment = HorizontalAlignment.Right;
			action.VerticalAlignment = VerticalAlignment.Top;
			head.Children.Add(action);
		}
		DockPanel.SetDock(head, Dock.Top);
		page.Children.Add(head);
		return page;
	}

	private FrameworkElement CompanionsPage()
	{
		Button create = Ui.Button(Ui.IconLabel("", "Create companion"), "Btn.Primary", delegate { ShowScreen(Screens.Create); });
		DockPanel page = Page("My Companions", "Everyone you've brought to life. Message them, make pictures of them, or change who they are.", create);
		WrapPanel cards = new WrapPanel { Margin = new Thickness(0, 0, 0, 28) };
		List<PersonaEntry> entries;
		try
		{
			entries = companion.Registry.Load().Personas;
		}
		catch (PersonaConfigException ex)
		{
			page.Children.Add(Ui.Text("Your companions couldn't be read: " + ex.Message, "Type.Small"));
			return page;
		}
		if (entries.Count == 0)
		{
			TextBlock none = Ui.Text("No companions yet. Press Create companion to make your first one.", "Type.Body");
			none.Foreground = Ui.Brush("Text.Secondary");
			page.Children.Add(none);
			return page;
		}
		foreach (PersonaEntry entry in entries)
		{
			cards.Children.Add(CompanionCard(entry));
		}
		page.Children.Add(new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
		PersonaEntry? open = companionDetail == null ? null : entries.FirstOrDefault((PersonaEntry e) => e.Id == companionDetail);
		if (open == null)
		{
			companionDetail = null;
			return page;
		}
		Grid both = new Grid();
		both.Children.Add(page);
		both.Children.Add(CompanionDetail(open));
		return both;
	}

	// ---------------- A companion's detailed card ----------------

	/// <summary>The companion whose detailed card is open on My Companions (null: none), and which picture it shows.</summary>
	private string? companionDetail;

	private int detailIndex;

	private bool detailMore;

	private void OpenCompanionDetail(string id)
	{
		companionDetail = id;
		detailIndex = 0;
		detailMore = false;
		ShowScreen(Screens.Companions);
	}

	private void CloseCompanionDetail()
	{
		companionDetail = null;
		ShowScreen(Screens.Companions);
	}

	/// <summary>Their pictures for the card: references (their portrait first), then ones made on the Generate screen.</summary>
	private List<string> DetailPictures(string personaId, string name)
	{
		List<string> pictures = new List<string>();
		try
		{
			List<string> refs = PictureFiles.References(companion.Folder(personaId));
			pictures.AddRange(refs.Where((string f) => Path.GetFileNameWithoutExtension(f).Equals("portrait", StringComparison.OrdinalIgnoreCase)));
			pictures.AddRange(refs.Where((string f) => !pictures.Contains(f)));
			pictures.AddRange(PictureFiles.MadeOf(PicturesRoot, name));
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
		}
		return pictures;
	}

	private static ImageSource? DetailImage(string path, int width)
	{
		try
		{
			System.Windows.Media.Imaging.BitmapImage image = new System.Windows.Media.Imaging.BitmapImage();
			image.BeginInit();
			image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
			image.UriSource = new Uri(path);
			image.DecodePixelWidth = width;
			image.EndInit();
			image.Freeze();
			return image;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException || ex is UriFormatException)
		{
			return null;
		}
	}

	private FrameworkElement CompanionDetail(PersonaEntry entry)
	{
		PersonaConfig? config = null;
		string problem = "";
		try
		{
			config = companion.Registry.LoadConfig(entry.Id);
		}
		catch (PersonaConfigException ex)
		{
			problem = ex.Message;
		}
		string name = config?.Name ?? entry.Name;
		bool setUp = companion.IsSetUp(entry.Id);

		// The dimmed backdrop: clicking it closes the card.
		Grid overlay = new Grid();
		Border scrim = new Border { Background = Ui.Brush("Scrim") };
		scrim.MouseLeftButtonDown += delegate { CloseCompanionDetail(); };
		overlay.Children.Add(scrim);

		Border panel = new Border
		{
			Background = Ui.Brush("Bg.Window"),
			BorderBrush = Ui.Brush("Stroke.Default"),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(16),
			Padding = new Thickness(24, 18, 24, 22),
			Width = 720,
			Margin = new Thickness(24),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Top
		};
		StackPanel body = new StackPanel();
		Button close = new Button { Style = Ui.Style("Btn.Icon"), Content = Ui.Icon("\uE711", 14), Width = 36, Height = 36, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Close" };
		Ui.SetRadius(close, new CornerRadius(18));
		close.Click += delegate { CloseCompanionDetail(); };
		body.Children.Add(close);

		// Pictures: the current one large, its neighbours dimmed either side, arrows to move.
		List<string> pictures = DetailPictures(entry.Id, name);
		Grid stage = new Grid { Height = 480, Margin = new Thickness(0, 4, 0, 0) };
		if (pictures.Count == 0)
		{
			ImageSource? profile = companion.ProfilePicture(entry.Id);
			Border only = new Border { Width = 360, Height = 480, CornerRadius = new CornerRadius(14), Background = Ui.Brush("Accent.Subtle") };
			if (profile != null)
			{
				only.Background = new ImageBrush(profile) { Stretch = Stretch.UniformToFill };
			}
			else
			{
				only.Child = new TextBlock { Text = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?", FontSize = 96, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("Accent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
			}
			stage.Children.Add(only);
		}
		else
		{
			detailIndex = ((detailIndex % pictures.Count) + pictures.Count) % pictures.Count;
			if (pictures.Count > 1)
			{
				string before = pictures[(detailIndex - 1 + pictures.Count) % pictures.Count];
				string after = pictures[(detailIndex + 1) % pictures.Count];
				stage.Children.Add(SidePicture(before, HorizontalAlignment.Left));
				stage.Children.Add(SidePicture(after, HorizontalAlignment.Right));
			}
			string shown = pictures[detailIndex];
			ImageSource? big = DetailImage(shown, 720);
			Border main = new Border
			{
				Width = 360,
				Height = 480,
				CornerRadius = new CornerRadius(14),
				Background = big != null ? new ImageBrush(big) { Stretch = Stretch.UniformToFill } : Ui.Brush("Surface.1"),
				Cursor = Cursors.Hand,
				ToolTip = "Open this picture"
			};
			main.MouseLeftButtonUp += delegate { OpenFile(shown); };
			stage.Children.Add(main);
			if (pictures.Count > 1)
			{
				stage.Children.Add(DetailArrow("\uE76B", "Previous picture", -1));
				stage.Children.Add(DetailArrow("\uE76C", "Next picture", 1));
			}
		}
		body.Children.Add(stage);
		if (pictures.Count > 1)
		{
			TextBlock count = Ui.Text((detailIndex + 1).ToString(CultureInfo.InvariantCulture) + " of " + pictures.Count.ToString(CultureInfo.InvariantCulture), "Type.Caption");
			count.HorizontalAlignment = HorizontalAlignment.Center;
			count.Margin = new Thickness(0, 8, 0, 0);
			body.Children.Add(count);
		}

		TextBlock title = new TextBlock { Text = name, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("Text.Primary"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 16), TextTrimming = TextTrimming.CharacterEllipsis };
		body.Children.Add(title);

		// Chat and Generate, side by side.
		Grid buttons = new Grid();
		buttons.ColumnDefinitions.Add(new ColumnDefinition());
		buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
		buttons.ColumnDefinitions.Add(new ColumnDefinition());
		Button chat = Ui.Button(Ui.IconLabel("\uE8BD", "Chat"), "Btn.Primary", delegate
		{
			if (busy)
			{
				return;
			}
			companionDetail = null;
			ShowScreen(Screens.Chats);
			NewChat(entry.Id, name);
		});
		chat.Height = 48;
		chat.HorizontalAlignment = HorizontalAlignment.Stretch;
		chat.IsEnabled = setUp && config != null;
		chat.ToolTip = chat.IsEnabled ? "Message " + name : name + " isn't set up yet: open their settings and press Set up first.";
		ToolTipService.SetShowOnDisabled(chat, true);
		buttons.Children.Add(chat);
		Button generate = Ui.Button(Ui.IconLabel("\uE91B", "Generate"), "Btn.Secondary", delegate
		{
			generateFor = entry.Id;
			ShowScreen(Screens.Generate);
		});
		generate.Height = 48;
		generate.HorizontalAlignment = HorizontalAlignment.Stretch;
		generate.ToolTip = "Make pictures or videos of " + name;
		Grid.SetColumn(generate, 2);
		buttons.Children.Add(generate);
		body.Children.Add(buttons);

		// Age and gender, then what they're like.
		TextBlock facts = new TextBlock { Margin = new Thickness(0, 18, 0, 0), FontSize = 15 };
		if (config != null)
		{
			if (config.Age > 0)
			{
				facts.Inlines.Add(new System.Windows.Documents.Run("Age: ") { Foreground = Ui.Brush("Text.Secondary") });
				facts.Inlines.Add(new System.Windows.Documents.Run(config.Age.ToString(CultureInfo.InvariantCulture) + "    ") { Foreground = Ui.Brush("Text.Primary"), FontWeight = FontWeights.SemiBold });
			}
			facts.Inlines.Add(new System.Windows.Documents.Run("Gender: ") { Foreground = Ui.Brush("Text.Secondary") });
			facts.Inlines.Add(new System.Windows.Documents.Run(Genders.IsMale(config.Gender) ? "Male" : "Female") { Foreground = Ui.Brush("Text.Primary"), FontWeight = FontWeights.SemiBold });
			body.Children.Add(facts);
		}
		string shortText = problem.Length > 0 ? "The settings couldn't be read: " + problem : Describe(config!);
		string fullText = config == null ? shortText : FullDescription(config);
		TextBlock about = new TextBlock
		{
			Text = detailMore ? fullText : shortText,
			TextWrapping = TextWrapping.Wrap,
			FontSize = 15,
			Foreground = Ui.Brush("Text.Primary"),
			Margin = new Thickness(0, 10, 0, 0),
			LineHeight = 22
		};
		body.Children.Add(about);
		if (fullText.Length > shortText.Length + 10)
		{
			Button more = Ui.Button(detailMore ? "See less" : "See more", "Btn.Link", delegate
			{
				detailMore = !detailMore;
				ShowScreen(Screens.Companions);
			});
			more.HorizontalAlignment = HorizontalAlignment.Left;
			more.Margin = new Thickness(0, 4, 0, 0);
			body.Children.Add(more);
		}
		Button settingsLink = Ui.Button(name + "'s settings", "Btn.Link", delegate
		{
			if (busy)
			{
				return;
			}
			OpenCompanions(entry.Id, null);
			RefreshPage();
		});
		settingsLink.HorizontalAlignment = HorizontalAlignment.Left;
		settingsLink.Margin = new Thickness(0, 12, 0, 0);
		body.Children.Add(settingsLink);

		panel.Child = body;
		ScrollViewer scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
		// Clicks on the empty space around the card close it too.
		scroll.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
		{
			if (!panel.IsMouseOver)
			{
				CloseCompanionDetail();
			}
		};
		overlay.Children.Add(scroll);
		return overlay;
	}

	private static FrameworkElement SidePicture(string path, HorizontalAlignment side)
	{
		ImageSource? image = DetailImage(path, 400);
		return new Border
		{
			Width = 260,
			Height = 380,
			CornerRadius = new CornerRadius(12),
			Background = image != null ? new ImageBrush(image) { Stretch = Stretch.UniformToFill } : Ui.Brush("Surface.1"),
			Opacity = 0.35,
			HorizontalAlignment = side,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(side == HorizontalAlignment.Left ? 8 : 0, 0, side == HorizontalAlignment.Right ? 8 : 0, 0),
			IsHitTestVisible = false
		};
	}

	private Button DetailArrow(string glyph, string tip, int step)
	{
		Button b = new Button
		{
			Style = Ui.Style("Btn.Icon"),
			Content = Ui.Icon(glyph, 16),
			Width = 40,
			Height = 40,
			Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0B, 0x12, 0x19)),
			Foreground = Brushes.White,
			ToolTip = tip,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			// Just inside the big picture's edges (it's 360 wide, centred).
			Margin = new Thickness(step < 0 ? -300 : 300, 0, 0, 0)
		};
		Ui.SetRadius(b, new CornerRadius(20));
		b.Click += delegate
		{
			detailIndex += step;
			ShowScreen(Screens.Companions);
		};
		return b;
	}

	/// <summary>Everything about them for "See more": the card description, then the personality sheet without markup.</summary>
	private static string FullDescription(PersonaConfig config)
	{
		List<string> parts = new List<string>();
		if (config.Tagline.Trim().Length > 0)
		{
			parts.Add(config.Tagline.Trim());
		}
		List<string> lines = new List<string>();
		foreach (string raw in config.PersonalitySheet.Split('\n'))
		{
			string line = Regex.Replace(raw, @"[*_`#>]+", "").Trim();
			if (line.Length == 0 || line.StartsWith(PersonaConfig.Placeholder, StringComparison.Ordinal))
			{
				continue;
			}
			lines.Add(line);
		}
		if (lines.Count > 0)
		{
			parts.Add(string.Join("\n", lines));
		}
		return parts.Count == 0 ? Describe(config) : string.Join("\n\n", parts);
	}

	private const double CardWidth = 236.0;

	private FrameworkElement CompanionCard(PersonaEntry entry)
	{
		PersonaConfig? config = null;
		string problem = "";
		try
		{
			config = companion.Registry.LoadConfig(entry.Id);
		}
		catch (PersonaConfigException ex)
		{
			problem = ex.Message;
		}
		string name = config?.Name ?? entry.Name;
		bool setUp = companion.IsSetUp(entry.Id);

		// Picture (or a big initial), with Message / Generate / Settings on top.
		ImageSource? picture = companion.ProfilePicture(entry.Id);
		Grid top = new Grid { Height = 250 };
		Border photo = new Border { CornerRadius = new CornerRadius(12, 12, 0, 0), Background = Ui.Brush("Accent.Subtle") };
		if (picture != null)
		{
			photo.Background = new ImageBrush(picture) { Stretch = Stretch.UniformToFill };
		}
		else
		{
			photo.Child = new TextBlock { Text = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?", FontSize = 72, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("Accent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
		}
		top.Children.Add(photo);
		StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 10, 0) };
		Button message = CardAction("", setUp ? "Message " + name : name + " isn't set up yet: open Settings and press Set up first.", delegate
		{
			if (busy)
			{
				return;
			}
			ShowScreen(Screens.Chats);
			NewChat(entry.Id, name);
		});
		message.IsEnabled = setUp && config != null;
		actions.Children.Add(message);
		actions.Children.Add(CardAction("", "Make pictures or videos of " + name, delegate
		{
			generateFor = entry.Id;
			ShowScreen(Screens.Generate);
		}));
		actions.Children.Add(CardAction("", name + "'s settings", delegate
		{
			if (busy)
			{
				return;
			}
			OpenCompanions(entry.Id, null);
			RefreshPage();
		}));
		top.Children.Add(actions);

		// Name, age, description.
		StackPanel words = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
		TextBlock title = new TextBlock { FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("Text.Primary"), TextTrimming = TextTrimming.CharacterEllipsis };
		title.Inlines.Add(new System.Windows.Documents.Run(name));
		if (config != null && config.Age > 0)
		{
			title.Inlines.Add(new System.Windows.Documents.Run(", " + config.Age.ToString(CultureInfo.InvariantCulture)) { Foreground = Ui.Brush("Text.Secondary"), FontWeight = FontWeights.Normal });
		}
		words.Children.Add(title);
		if (!setUp)
		{
			TextBlock pending = Ui.Text("Not set up yet", "Type.Caption");
			pending.Foreground = Ui.Brush("Warning");
			pending.Margin = new Thickness(0, 2, 0, 0);
			words.Children.Add(pending);
		}
		string about = problem.Length > 0 ? "The settings couldn't be read: " + problem : Describe(config!);
		TextBlock description = Ui.Text(about, "Type.Small");
		description.Margin = new Thickness(0, 6, 0, 0);
		description.MaxHeight = 72;
		description.TextTrimming = TextTrimming.WordEllipsis;
		description.ToolTip = about.Length > 120 ? about : null;
		words.Children.Add(description);

		Grid card = new Grid();
		card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		card.Children.Add(top);
		Grid.SetRow(words, 1);
		card.Children.Add(words);
		Border cardBorder = new Border
		{
			Style = Ui.Style("Card"),
			Padding = new Thickness(0),
			Width = CardWidth,
			Margin = new Thickness(0, 0, 16, 16),
			Child = card,
			VerticalAlignment = VerticalAlignment.Top,
			Cursor = Cursors.Hand
		};
		// The buttons on the picture handle their own clicks; anywhere else opens the detailed card.
		cardBorder.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
		{
			if (!e.Handled)
			{
				OpenCompanionDetail(entry.Id);
			}
		};
		return cardBorder;
	}

	private static Button CardAction(string glyph, string tip, RoutedEventHandler click)
	{
		Button b = new Button
		{
			Style = Ui.Style("Btn.Icon"),
			Content = Ui.Icon(glyph, 15),
			Width = 34,
			Height = 34,
			Margin = new Thickness(6, 0, 0, 0),
			Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0B, 0x12, 0x19)),
			Foreground = Brushes.White,
			ToolTip = tip
		};
		Ui.SetRadius(b, new CornerRadius(17));
		ToolTipService.SetShowOnDisabled(b, true);
		b.Click += click;
		return b;
	}

	/// <summary>Their card description, or else the start of their personality sheet (headings and markup left out).</summary>
	internal static string Describe(PersonaConfig config)
	{
		if (config.Tagline.Trim().Length > 0)
		{
			return config.Tagline.Trim();
		}
		foreach (string raw in config.PersonalitySheet.Split('\n'))
		{
			string line = Regex.Replace(raw, @"[*_`#>]+", "").Trim();
			if (line.Length < 20 || line.StartsWith(PersonaConfig.Placeholder, StringComparison.Ordinal) || line.EndsWith(':'))
			{
				continue;
			}
			Match two = Regex.Match(line, @"^(.+?[.!?])(\s+.+?[.!?])?(\s|$)");
			string text = two.Success ? (two.Groups[1].Value + two.Groups[2].Value) : line;
			return text.Length > 180 ? text.Substring(0, 177).TrimEnd() + "…" : text;
		}
		return "No description yet. Add one in their settings.";
	}
}
