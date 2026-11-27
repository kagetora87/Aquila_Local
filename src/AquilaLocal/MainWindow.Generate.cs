using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Pictures;

namespace AquilaLocal;

/// <summary>
/// The Generate screen: pick a companion (their looks go into the prompt by themselves), an action, an outfit
/// and a scene from the presets on the right (or describe your own with Custom), and make a picture or a video.
/// </summary>
public partial class MainWindow
{
	private PresetLibrary? presetLibrary;

	private PresetLibrary Presets => presetLibrary ??= new PresetLibrary(Path.Combine(store.Root, "presets"));

	// What's picked, kept while you move around the app.
	private string genAction = "";
	private string genOutfit = "";
	private string genScene = "";
	private string genExtra = "";
	private string genMotion = "";
	private string genShot = "";
	private string genTab = PresetKinds.Action;
	private bool genVideo;
	private string genModel = "";

	/// <summary>What was made this session, newest first.</summary>
	private readonly List<(string File, string Clip, string Who, string Prompt)> genResults = new List<(string File, string Clip, string Who, string Prompt)>();

	private bool generating;
	private bool makingTiles;
	private CancellationTokenSource? genCts;
	private string genStatusText = "";
	private bool genStatusBad;

	// The parts of the page that refill in place (so scroll positions and typing aren't lost).
	private WrapPanel? genCompanions;
	private StackPanel? genPicks;
	private WrapPanel? genTiles;
	private StackPanel? genResultsPanel;
	private TextBlock? genStatus;
	private Button? genGo;
	private Button? genStop;
	private StackPanel? genTileBanner;
	private FrameworkElement? genShotRow;
	private FrameworkElement? genMotionRow;
	private TextBox? genMotionBox;
	private readonly Dictionary<string, RadioButton> genTabs = new Dictionary<string, RadioButton>();

	private static readonly (string Key, string Label)[] Shots =
	{
		("", "Auto (suits the action)"), ("portrait", "Portrait"), ("half body", "Half body"), ("full body", "Full body"), ("scene", "Wide scene"), ("landscape", "Landscape")
	};

	private FrameworkElement GeneratePage()
	{
		if (genModel.Length == 0)
		{
			genModel = PictureModels.IsKnown(settings.Pictures.Model) ? settings.Pictures.Model : PictureModels.Fast;
		}
		List<PersonaEntry> entries = new List<PersonaEntry>();
		try
		{
			RegistryManifest m = companion.Registry.Load();
			entries = m.Personas;
			if (generateFor.Length == 0 || !entries.Any((PersonaEntry p) => p.Id == generateFor))
			{
				generateFor = current != null && current.IsCompanion && entries.Any((PersonaEntry p) => p.Id == current.PersonaId) ? current.PersonaId : (m.Active.Length > 0 ? m.Active : entries.FirstOrDefault()?.Id ?? "");
			}
		}
		catch (PersonaConfigException ex)
		{
			ShowGenStatus("Your companions couldn't be read: " + ex.Message, bad: true);
		}

		Grid layout = new Grid();
		layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 380 });
		layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(452) });

		// ---- Left: the creation menu, then what you made ----
		StackPanel left = new StackPanel { Margin = new Thickness(36, 28, 28, 36) };
		left.Children.Add(Ui.Text("Generate", "Type.Display"));
		TextBlock sub = Ui.Text("Pick a companion: their looks go into the prompt by themselves. Then pick an action, an outfit and a scene on the right.", "Type.Small");
		sub.Margin = new Thickness(0, 4, 0, 18);
		left.Children.Add(sub);

		left.Children.Add(Ui.Section("Companion", 0));
		genCompanions = new WrapPanel();
		left.Children.Add(genCompanions);
		if (entries.Count == 0)
		{
			left.Children.Add(Ui.Text("No companions yet. Create one first.", "Type.Small"));
		}

		left.Children.Add(Ui.Section("Make"));
		Border kindBox = new Border { Background = Ui.Brush("Surface.1"), CornerRadius = new CornerRadius(8), Padding = new Thickness(3), Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
		UniformGrid kinds = new UniformGrid { Columns = 2 };
		RadioButton picture = new RadioButton { Style = Ui.Style("SegmentedTab"), Content = "Picture", GroupName = "GenKind", IsChecked = !genVideo };
		RadioButton video = new RadioButton { Style = Ui.Style("SegmentedTab"), Content = "Video (5 s)", GroupName = "GenKind", IsChecked = genVideo };
		picture.Checked += delegate { genVideo = false; ShowKindRows(); };
		video.Checked += delegate { genVideo = true; ShowKindRows(); };
		kinds.Children.Add(picture);
		kinds.Children.Add(video);
		kindBox.Child = kinds;
		left.Children.Add(kindBox);

		left.Children.Add(Ui.Section("Your picks"));
		genPicks = new StackPanel();
		left.Children.Add(genPicks);

		ComboBox shot = new ComboBox { ItemsSource = Shots.Select(((string Key, string Label) s) => s.Label).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(Shots, ((string Key, string Label) s) => s.Key == genShot)), Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
		shot.SelectionChanged += delegate { genShot = Shots[Math.Max(0, shot.SelectedIndex)].Key; };
		genShotRow = Ui.Field("Shot", shot, "", 14);
		left.Children.Add(genShotRow);

		TextBox extra = new TextBox { Text = genExtra, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 74, VerticalContentAlignment = VerticalAlignment.Top, MaxLength = GenerateRecipe.MaxExtraChars, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
		Ui.SetPlaceholder(extra, "Anything else: expression, a prop, the light, the time of day…");
		extra.TextChanged += delegate { genExtra = extra.Text; };
		left.Children.Add(Ui.Field("Extra details", extra, "", 14));

		genMotionBox = new TextBox { Text = genMotion, MaxLength = VideoRequest.MaxMotionChars };
		genMotionBox.TextChanged += delegate { genMotion = genMotionBox.Text; };
		genMotionRow = Ui.Field("What happens in the video", genMotionBox, "Leave empty to use the action's own movement.", 14);
		left.Children.Add(genMotionRow);

		ComboBox model = new ComboBox { ItemsSource = new[] { PictureModels.Label(PictureModels.Fast), PictureModels.Label(PictureModels.Best) }, SelectedIndex = genModel == PictureModels.Best ? 1 : 0, Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
		model.SelectionChanged += delegate { genModel = model.SelectedIndex == 1 ? PictureModels.Best : PictureModels.Fast; };
		left.Children.Add(Ui.Field("Picture engine", model, "Fast takes about 15 seconds. Best quality takes about a minute and pauses the chat model while it draws.", 14));

		StackPanel go = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
		genGo = Ui.Button(Ui.IconLabel("", "Generate"), "Btn.Large", delegate { _ = GenerateAsync(); });
		genStop = Ui.Button("Stop", "Btn.Secondary", delegate { genCts?.Cancel(); });
		genStop.Margin = new Thickness(10, 0, 0, 0);
		go.Children.Add(genGo);
		go.Children.Add(genStop);
		left.Children.Add(go);
		genStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontSize = 13 };
		left.Children.Add(genStatus);

		genResultsPanel = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
		left.Children.Add(genResultsPanel);

		ScrollViewer leftScroll = new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
		layout.Children.Add(leftScroll);

		// ---- Right: the presets ----
		DockPanel right = new DockPanel { Margin = new Thickness(18, 28, 18, 0) };
		Border tabBox = new Border { Background = Ui.Brush("Surface.1"), CornerRadius = new CornerRadius(8), Padding = new Thickness(3), Margin = new Thickness(0, 0, 0, 12) };
		UniformGrid tabs = new UniformGrid { Columns = 3 };
		genTabs.Clear();
		foreach (string kind in PresetKinds.All)
		{
			string k = kind;
			RadioButton tab = new RadioButton { Style = Ui.Style("SegmentedTab"), Content = PresetKinds.Label(k), GroupName = "GenTabs", IsChecked = genTab == k };
			tab.Checked += delegate
			{
				genTab = k;
				FillTiles();
			};
			genTabs[k] = tab;
			tabs.Children.Add(tab);
		}
		tabBox.Child = tabs;
		DockPanel.SetDock(tabBox, Dock.Top);
		right.Children.Add(tabBox);
		genTileBanner = new StackPanel();
		DockPanel.SetDock(genTileBanner, Dock.Top);
		right.Children.Add(genTileBanner);
		genTiles = new WrapPanel { Margin = new Thickness(0, 0, 0, 24) };
		right.Children.Add(new ScrollViewer { Content = genTiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
		Border rightPane = new Border { Background = Ui.Brush("Bg.Sidebar"), BorderBrush = Ui.Brush("Stroke.Subtle"), BorderThickness = new Thickness(1, 0, 0, 0), Child = right };
		Grid.SetColumn(rightPane, 1);
		layout.Children.Add(rightPane);

		FillCompanions(entries);
		FillPicks();
		FillTiles();
		FillResults();
		ShowKindRows();
		ShowGenStatus(genStatusText, genStatusBad);
		return layout;
	}

	private void ShowKindRows()
	{
		if (genShotRow != null)
		{
			genShotRow.Visibility = genVideo ? Visibility.Collapsed : Visibility.Visible;
		}
		if (genMotionRow != null)
		{
			genMotionRow.Visibility = genVideo ? Visibility.Visible : Visibility.Collapsed;
		}
		if (genMotionBox != null)
		{
			try
			{
				Ui.SetPlaceholder(genMotionBox, Recipe().Motion(""));
			}
			catch (PersonaConfigException)
			{
			}
		}
	}

	private void ShowGenStatus(string text, bool bad = false)
	{
		genStatusText = text;
		genStatusBad = bad;
		if (genStatus != null)
		{
			genStatus.Text = text;
			genStatus.Foreground = Ui.Brush(bad ? "Danger" : "Text.Secondary");
			genStatus.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
		}
		if (genGo != null)
		{
			genGo.IsEnabled = !generating && !makingTiles;
		}
		if (genStop != null)
		{
			genStop.Visibility = generating || makingTiles ? Visibility.Visible : Visibility.Collapsed;
		}
	}

	private string GenderOf(string personaId)
	{
		try
		{
			return companion.Registry.LoadConfig(personaId).Gender;
		}
		catch (PersonaConfigException)
		{
			return Genders.Female;
		}
	}

	private void FillCompanions(List<PersonaEntry> entries)
	{
		if (genCompanions == null)
		{
			return;
		}
		genCompanions.Children.Clear();
		foreach (PersonaEntry entry in entries)
		{
			PersonaEntry e = entry;
			bool on = e.Id == generateFor;
			Grid face = new Grid { Width = 34, Height = 34, Margin = new Thickness(0, 0, 10, 0) };
			face.Children.Add(new System.Windows.Shapes.Ellipse { Fill = Ui.Brush("Accent.Subtle") });
			face.Children.Add(new TextBlock { Text = e.Name.Length > 0 ? char.ToUpperInvariant(e.Name[0]).ToString() : "?", Foreground = Ui.Brush("Accent"), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
			ImageSource? pic = companion.ProfilePicture(e.Id);
			if (pic != null)
			{
				face.Children.Add(new System.Windows.Shapes.Ellipse { Fill = new ImageBrush(pic) { Stretch = Stretch.UniformToFill } });
			}
			StackPanel content = new StackPanel { Orientation = Orientation.Horizontal };
			content.Children.Add(face);
			content.Children.Add(new TextBlock { Text = e.Name, VerticalAlignment = VerticalAlignment.Center, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal });
			Button chip = new Button
			{
				Style = Ui.Style("Btn.Secondary"),
				Content = content,
				Padding = new Thickness(6, 6, 16, 6),
				Margin = new Thickness(0, 0, 8, 8),
				BorderBrush = on ? Ui.Brush("Accent") : Ui.Brush("Stroke.Default"),
				BorderThickness = new Thickness(on ? 2 : 1),
				Background = on ? Ui.Brush("Accent.Subtle") : Ui.Brush("Surface.2"),
				ToolTip = "Make pictures of " + e.Name
			};
			Ui.SetRadius(chip, new CornerRadius(24));
			chip.Click += delegate
			{
				generateFor = e.Id;
				FillCompanions(entries);
				FillTiles();
			};
			genCompanions.Children.Add(chip);
		}
	}

	private GenerateRecipe Recipe()
	{
		return new GenerateRecipe(Presets.Find(genAction), Presets.Find(genOutfit), Presets.Find(genScene), genExtra, genShot);
	}

	private void FillPicks()
	{
		if (genPicks == null)
		{
			return;
		}
		genPicks.Children.Clear();
		GenPreset? a = null, o = null, s = null;
		try
		{
			a = Presets.Find(genAction);
			o = Presets.Find(genOutfit);
			s = Presets.Find(genScene);
		}
		catch (PersonaConfigException ex)
		{
			ShowGenStatus(ex.Message, bad: true);
		}
		genPicks.Children.Add(PickRow(PresetKinds.Action, "", a, "Natural and relaxed", delegate { genAction = ""; }));
		genPicks.Children.Add(PickRow(PresetKinds.Outfit, "", o, "Their usual outfit", delegate { genOutfit = ""; }));
		genPicks.Children.Add(PickRow(PresetKinds.Scene, "", s, "Wherever suits it", delegate { genScene = ""; }));
	}

	/// <summary>One pick: its kind, what's chosen (or the default), a ✕ to clear it; clicking shows that kind's presets.</summary>
	private FrameworkElement PickRow(string kind, string glyph, GenPreset? chosen, string none, Action clear)
	{
		Grid row = new Grid();
		row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		StackPanel words = new StackPanel { Orientation = Orientation.Horizontal };
		TextBlock icon = Ui.Icon(glyph, 15, chosen != null ? "Accent" : "Text.Tertiary");
		icon.Width = 24;
		words.Children.Add(icon);
		words.Children.Add(new TextBlock { Text = PresetKinds.Label(kind), Width = 70, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Brush("Text.Secondary") });
		words.Children.Add(new TextBlock { Text = chosen?.Name ?? none, VerticalAlignment = VerticalAlignment.Center, FontWeight = chosen != null ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Ui.Brush(chosen != null ? "Text.Primary" : "Text.Tertiary"), TextTrimming = TextTrimming.CharacterEllipsis });
		Button pick = new Button { Style = Ui.Style("Btn.Menu"), Content = words, Height = 40, MinHeight = 40, ToolTip = "Choose a " + PresetKinds.Label(kind).ToLowerInvariant() + " on the right" };
		pick.Click += delegate
		{
			if (genTabs.TryGetValue(kind, out RadioButton? tab))
			{
				tab.IsChecked = true;
			}
		};
		row.Children.Add(pick);
		if (chosen != null)
		{
			Button x = Ui.IconButton("", "Clear", delegate
			{
				clear();
				FillPicks();
				FillTiles();
				ShowKindRows();
			});
			x.FontSize = 11;
			Grid.SetColumn(x, 1);
			row.Children.Add(x);
		}
		return new Border { Background = Ui.Brush("Surface.1"), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 6), Child = row, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Stretch };
	}

	private string ChosenFor(string kind) => kind == PresetKinds.Action ? genAction : kind == PresetKinds.Outfit ? genOutfit : genScene;

	private void Choose(string kind, string id)
	{
		bool same = ChosenFor(kind) == id;
		string value = same ? "" : id;
		if (kind == PresetKinds.Action)
		{
			genAction = value;
		}
		else if (kind == PresetKinds.Outfit)
		{
			genOutfit = value;
		}
		else
		{
			genScene = value;
		}
		FillPicks();
		FillTiles();
		ShowKindRows();
	}

	private void FillTiles()
	{
		if (genTiles == null || genTileBanner == null)
		{
			return;
		}
		genTiles.Children.Clear();
		genTileBanner.Children.Clear();
		List<GenPreset> presets;
		int missing;
		try
		{
			string gender = generateFor.Length > 0 ? GenderOf(generateFor) : "";
			presets = Presets.Load(genTab).Where((GenPreset p) => gender.Length == 0 || p.Fits(gender)).ToList();
			missing = Presets.MissingTiles().Count;
		}
		catch (PersonaConfigException ex)
		{
			genTileBanner.Children.Add(Ui.Text(ex.Message, "Type.Small"));
			return;
		}
		if (missing > 0 || makingTiles)
		{
			Border banner = new Border { Background = Ui.Brush("Accent.Subtle"), BorderBrush = Ui.Brush("Accent.SubtleStroke"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 12) };
			StackPanel b = new StackPanel();
			if (makingTiles)
			{
				b.Children.Add(Ui.Text("Making the preset pictures… They appear here as they're done. You can stop any time and carry on later.", "Type.Small"));
			}
			else
			{
				int minutes = Math.Max(1, (int)Math.Ceiling(missing * 12 / 60.0));
				b.Children.Add(Ui.Text(missing + " preset picture" + (missing == 1 ? " hasn't" : "s haven't") + " been made yet. Your picture engine makes them once, on this computer (about " + minutes + " minute" + (minutes == 1 ? "" : "s") + ").", "Type.Small"));
				Button make = Ui.Button(Ui.IconLabel("", "Make the preset pictures"), "Btn.Primary", delegate { _ = MakeTilesAsync(); });
				make.HorizontalAlignment = HorizontalAlignment.Left;
				make.Margin = new Thickness(0, 8, 0, 0);
				make.IsEnabled = !generating;
				b.Children.Add(make);
			}
			banner.Child = b;
			genTileBanner.Children.Add(banner);
		}
		genTiles.Children.Add(CustomTile(genTab));
		string chosen = ChosenFor(genTab);
		foreach (GenPreset p in presets)
		{
			genTiles.Children.Add(Tile(p, p.Id == chosen));
		}
	}

	private const double TileWidth = 128.0;
	private const double TileHeight = 160.0;

	private Button TileButton(UIElement face, string name, bool on, string tip)
	{
		Border frame = new Border
		{
			Width = TileWidth,
			Height = TileHeight,
			CornerRadius = new CornerRadius(10),
			BorderThickness = new Thickness(on ? 3 : 1),
			BorderBrush = on ? Ui.Brush("Accent") : Ui.Brush("Stroke.Subtle"),
			Background = Ui.Brush("Surface.2"),
			Child = face,
			ClipToBounds = true
		};
		StackPanel content = new StackPanel();
		content.Children.Add(frame);
		content.Children.Add(new TextBlock { Text = name, Width = TileWidth, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0), FontSize = 13, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Ui.Brush(on ? "Text.Primary" : "Text.Secondary") });
		Button b = new Button { Style = Ui.Style("Btn.Ghost"), Content = content, Padding = new Thickness(0), Margin = new Thickness(0, 0, 10, 14), MinHeight = 0, ToolTip = tip };
		Ui.SetRadius(b, new CornerRadius(10));
		return b;
	}

	private Button Tile(GenPreset p, bool on)
	{
		Grid face = new Grid();
		ImageSource? picture = TilePicture(Presets.StyledTilePath(p.Id));
		if (picture != null)
		{
			face.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = new ImageBrush(picture) { Stretch = Stretch.UniformToFill } });
		}
		else
		{
			string glyph = p.Kind == PresetKinds.Action ? "" : p.Kind == PresetKinds.Outfit ? "" : "";
			face.Children.Add(Ui.Icon(glyph, 30, "Text.Tertiary"));
		}
		if (on)
		{
			Border check = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Ui.Brush("Accent"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0), Child = Ui.Icon("", 12, "Accent.On") };
			face.Children.Add(check);
		}
		Button b = TileButton(face, p.Name, on, (on ? "Picked (click again to unpick): " : "") + p.Prompt + (p.Custom ? "\n\nOne of your own. Right-click to remove it." : ""));
		b.Click += delegate { Choose(p.Kind, p.Id); };
		if (p.Custom)
		{
			ContextMenu menu = new ContextMenu();
			MenuItem remove = new MenuItem { Header = "Remove this preset", Icon = Ui.Icon("", 14, "Danger"), Foreground = Ui.Brush("Danger") };
			remove.Click += delegate
			{
				if (!AquilaDialog.Confirm(this, "Remove preset", "Remove “" + p.Name + "” and its picture?", "Remove", "Cancel", DialogKind.Danger))
				{
					return;
				}
				try
				{
					Presets.Remove(p.Id);
					if (ChosenFor(p.Kind) == p.Id)
					{
						Choose(p.Kind, p.Id);
					}
				}
				catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException)
				{
					ShowGenStatus("The preset wasn't removed: " + ex.Message, bad: true);
				}
				FillTiles();
			};
			menu.Items.Add(remove);
			b.ContextMenu = menu;
		}
		return b;
	}

	private Button CustomTile(string kind)
	{
		StackPanel face = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
		face.Children.Add(Ui.Icon("", 26, "Accent"));
		face.Children.Add(new TextBlock { Text = "Describe your own", FontSize = 12, Foreground = Ui.Brush("Text.Secondary"), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
		Button b = TileButton(face, "Custom", false, "Add your own " + PresetKinds.Label(kind).ToLowerInvariant());
		((Border)((StackPanel)b.Content).Children[0]).BorderBrush = Ui.Brush("Accent.SubtleStroke");
		b.Click += delegate
		{
			CustomPresetWindow window = new CustomPresetWindow(Presets, kind) { Owner = this };
			if (window.ShowDialog() == true && window.Added != null)
			{
				Choose(kind, window.Added.Id);
				_ = MakeTilesAsync(new List<GenPreset> { window.Added });
			}
		};
		return b;
	}

	/// <summary>A small picture read into memory (the file isn't kept open), or null.</summary>
	private static ImageSource? TilePicture(string path)
	{
		if (!File.Exists(path))
		{
			return null;
		}
		try
		{
			BitmapImage image = new BitmapImage();
			image.BeginInit();
			image.CacheOption = BitmapCacheOption.OnLoad;
			image.StreamSource = new MemoryStream(File.ReadAllBytes(path));
			image.DecodePixelWidth = 256;
			image.EndInit();
			image.Freeze();
			return image;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException || ex is FileFormatException)
		{
			return null;
		}
	}

	/// <summary>Makes the presets' small pictures (all missing ones, or just these).</summary>
	private Task MakeTilesAsync(List<GenPreset>? only = null)
	{
		List<TileJob> jobs;
		try
		{
			jobs = (only ?? Presets.MissingTiles()).Select((GenPreset p) => new TileJob(p.Id, p.Name, PresetLibrary.TilePrompt(p))).ToList();
		}
		catch (PersonaConfigException ex)
		{
			ShowGenStatus(ex.Message, bad: true);
			return Task.CompletedTask;
		}
		return RunTilesAsync(jobs, FillTiles, ShowGenStatus);
	}

	/// <summary>Draws small pictures with the fast model, one at a time; refresh shows each as it's done.</summary>
	private async Task RunTilesAsync(List<TileJob> todo, Action refresh, Action<string, bool> status)
	{
		if (makingTiles || generating || busy || todo.Count == 0)
		{
			return;
		}
		(bool ready, string problem) = PictureDrawer.Kit.Check(PictureModels.Fast);
		if (!ready)
		{
			status("The pictures can't be made yet: " + problem, true);
			return;
		}
		makingTiles = true;
		genCts = new CancellationTokenSource();
		CancellationToken ct = genCts.Token;
		SetBusy(value: true);
		refresh();
		int done = 0;
		string end = "";
		bool bad = false;
		try
		{
			foreach (TileJob job in todo)
			{
				ct.ThrowIfCancellationRequested();
				status("Making pictures… " + (done + 1) + " of " + todo.Count + " (" + job.Name + ")", false);
				string tilePath = Presets.StyledTilePath(job.Id);
				Directory.CreateDirectory(Path.GetDirectoryName(tilePath)!);
				await DrawAsync(PictureModels.Fast, job.Prompt, PresetLibrary.TileSize, Array.Empty<string>(), "preset", ct, tilePath);
				done++;
				refresh();
			}
			end = done == 1 ? "Its picture is ready." : "All " + done + " pictures are ready.";
		}
		catch (OperationCanceledException)
		{
			end = "Stopped. " + done + " picture" + (done == 1 ? " was" : "s were") + " made; the rest can be made later.";
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is HttpRequestException || ex is PersonaConfigException)
		{
			end = "The pictures stopped after " + done + ": " + ex.Message;
			bad = true;
		}
		finally
		{
			makingTiles = false;
			genCts = null;
			SetBusy(value: false);
			PictureStatus(null);
			refresh();
		}
		status(end, bad);
	}

	/// <summary>Their looks for the prompt: the appearance card (or the persona file before set-up), without their usual outfit when another was picked.</summary>
	private (string Name, string Gender, string Appearance, List<string> References) LooksFor(string personaId, bool withOutfit)
	{
		PersonaConfig config = companion.Registry.LoadConfig(personaId);
		string appearance;
		if (companion.IsSetUp(personaId))
		{
			CompanionRuntime runtime = companion.Open(personaId);
			try
			{
				appearance = SeedCaption.ForCard(config.Name, runtime.Store.ReadAppearance(), withOutfit);
			}
			catch (AppearanceCardUnavailableException)
			{
				appearance = CompanionHost.Looks(runtime);
			}
		}
		else
		{
			appearance = SeedCaption.For(config.Name, config.Seed.Appearance, withOutfit);
		}
		List<string> references = settings.Pictures.UseReferences ? PictureFiles.References(companion.Folder(personaId)).Take(PictureCommand.MaxReferences).ToList() : new List<string>();
		return (config.Name, config.Gender, appearance, references);
	}

	private async Task GenerateAsync()
	{
		if (generating || makingTiles)
		{
			return;
		}
		if (busy)
		{
			ShowGenStatus("Wait for the current reply to finish, then press Generate.", bad: true);
			return;
		}
		if (generateFor.Length == 0)
		{
			ShowGenStatus("Pick a companion first.", bad: true);
			return;
		}
		string model = genModel;
		(bool ready, string problem) = PictureDrawer.Kit.Check(model);
		if (!ready)
		{
			ShowGenStatus("The picture engine isn't ready: " + problem, bad: true);
			return;
		}
		bool video = genVideo;
		if (video)
		{
			(bool videoReady, string videoProblem) = PictureDrawer.Video.Check();
			if (!videoReady)
			{
				ShowGenStatus("Video isn't ready: " + videoProblem, bad: true);
				return;
			}
		}
		GenerateRecipe recipe;
		try
		{
			recipe = Recipe();
		}
		catch (PersonaConfigException ex)
		{
			ShowGenStatus(ex.Message, bad: true);
			return;
		}
		generating = true;
		genCts = new CancellationTokenSource();
		CancellationToken ct = genCts.Token;
		SetBusy(value: true);
		FillTiles();
		ShowGenStatus("Getting ready…");
		try
		{
			(string name, string gender, string appearance, List<string> references) = LooksFor(generateFor, withOutfit: recipe.Outfit == null);
			PictureRequest request = recipe.Request(name, gender);
			if (video)
			{
				request = request with { Framing = "landscape" };
			}
			string prompt;
			if (engine.Ready)
			{
				ShowGenStatus("Writing the prompt from " + name + "'s looks and your picks…");
				PictureStatus("Writing the picture prompt…");
				prompt = await WritePicturePromptAsync(request, "", name, appearance, references.Count > 0, "", ct, gender);
			}
			else
			{
				prompt = recipe.DirectPrompt(name, gender, appearance);
			}
			ShowGenStatus(video ? "Drawing the video's first frame…" : "Drawing…");
			(int, int) size = video ? VideoRequest.StillSize : request.Size();
			string still = await DrawAsync(model, PicturePrompt.WithReferences(prompt, references.Count), size, references, "generate " + name, ct);
			string clip = "";
			if (video)
			{
				ShowGenStatus("Making the video (this takes a few minutes; the chat model is paused meanwhile)…");
				clip = await AnimateAsync(still, VideoRequest.ClipPrompt(recipe.Motion(genMotion), prompt), ct);
			}
			genResults.Insert(0, (still, clip, name, prompt));
			ShowGenStatus(video ? "Your video is ready." : "Your picture is ready.");
			FillResults();
		}
		catch (OperationCanceledException)
		{
			ShowGenStatus("Stopped.");
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is HttpRequestException || ex is PersonaConfigException || ex is NotSupportedException || ex is Microsoft.Data.Sqlite.SqliteException)
		{
			ShowGenStatus("It didn't work: " + ex.Message, bad: true);
		}
		finally
		{
			generating = false;
			genCts = null;
			SetBusy(value: false);
			PictureStatus(null);
			FillTiles();
			ShowGenStatus(genStatusText, genStatusBad);
		}
	}

	private void FillResults()
	{
		if (genResultsPanel == null)
		{
			return;
		}
		genResultsPanel.Children.Clear();
		if (genResults.Count == 0)
		{
			return;
		}
		genResultsPanel.Children.Add(Ui.Section("Made this session", 0));
		foreach ((string file, string clip, string who, string prompt) in genResults.Take(12))
		{
			string f = file;
			string c = clip;
			StackPanel item = new StackPanel();
			ImageSource? picture = LoadPreview(f);
			if (picture != null)
			{
				Image image = new Image { Source = picture, MaxHeight = 460, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Click to open" };
				image.MouseLeftButtonUp += delegate { OpenFile(c.Length > 0 ? c : f); };
				item.Children.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = image, HorizontalAlignment = HorizontalAlignment.Left });
			}
			TextBlock caption = Ui.Text(who + (c.Length > 0 ? " · video" : "") + " · " + prompt.Truncate(220), "Type.Caption");
			caption.Margin = new Thickness(0, 6, 0, 6);
			caption.MaxWidth = 640;
			caption.HorizontalAlignment = HorizontalAlignment.Left;
			item.Children.Add(caption);
			StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal };
			if (c.Length > 0)
			{
				buttons.Children.Add(Ui.Button(Ui.IconLabel("", "Play video"), "Btn.Secondary", delegate { OpenFile(c); }));
			}
			Button open = Ui.Button(Ui.IconLabel("", "Open picture"), "Btn.Secondary", delegate { OpenFile(f); });
			open.Margin = new Thickness(c.Length > 0 ? 8 : 0, 0, 0, 0);
			buttons.Children.Add(open);
			Button folder = Ui.Button(Ui.IconLabel("", "Show in folder"), "Btn.Ghost", delegate { ShowInFolder(c.Length > 0 ? c : f); });
			folder.Margin = new Thickness(8, 0, 0, 0);
			buttons.Children.Add(folder);
			item.Children.Add(buttons);
			item.Margin = new Thickness(0, 0, 0, 22);
			genResultsPanel.Children.Add(item);
		}
	}

	private static ImageSource? LoadPreview(string path)
	{
		try
		{
			BitmapImage image = new BitmapImage();
			image.BeginInit();
			image.CacheOption = BitmapCacheOption.OnLoad;
			image.StreamSource = new MemoryStream(File.ReadAllBytes(path));
			image.DecodePixelHeight = 920;
			image.EndInit();
			image.Freeze();
			return image;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException || ex is FileFormatException)
		{
			return null;
		}
	}

	private void OpenFile(string path)
	{
		try
		{
			Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
		}
		catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
		{
			ShowGenStatus("Couldn't open it: " + ex.Message, bad: true);
		}
	}

	private void ShowInFolder(string path)
	{
		try
		{
			Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
		}
		catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
		{
			ShowGenStatus("Couldn't open the folder: " + ex.Message, bad: true);
		}
	}
}
