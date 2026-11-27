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
		return page;
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
		string about = problem.Length > 0 ? "Her settings couldn't be read: " + problem : Describe(config!);
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
		return new Border
		{
			Style = Ui.Style("Card"),
			Padding = new Thickness(0),
			Width = CardWidth,
			Margin = new Thickness(0, 0, 16, 16),
			Child = card,
			VerticalAlignment = VerticalAlignment.Top
		};
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

	private FrameworkElement CreatePage()
	{
		DockPanel page = Page("Create a companion", "Bring someone new to life.", null);
		Border card = new Border { Style = Ui.Style("Card"), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
		StackPanel body = new StackPanel();
		body.Children.Add(Ui.Text("The step-by-step creator arrives in an upcoming update.", "Type.Subtitle"));
		TextBlock more = Ui.Text("Six steps: male or female, ethnicity, hair style, body type, then their personality, looks and drives, and finally a summary where you generate their portrait. Until then you can add a companion from a persona file.", "Type.Small");
		more.Margin = new Thickness(0, 8, 0, 16);
		body.Children.Add(more);
		Button fromFile = Ui.Button(Ui.IconLabel("", "Add one from a file…"), "Btn.Secondary", delegate
		{
			if (busy)
			{
				return;
			}
			OpenCompanions(null, null);
			RefreshPage();
		});
		fromFile.HorizontalAlignment = HorizontalAlignment.Left;
		body.Children.Add(fromFile);
		card.Child = body;
		page.Children.Add(card);
		return page;
	}
}
