using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Pictures;
using Microsoft.Win32;

namespace AquilaLocal;

/// <summary>
/// Pictures: allow Vicki and Aquila to draw, pick the fast or best model, manage each persona's reference
/// pictures (so she looks the same every time), and draw a test picture.
/// </summary>
public sealed class PicturesWindow : AquilaWindow
{
	public delegate Task<string> Draw(string model, string prompt, (int Width, int Height) size, IReadOnlyList<string> references, CancellationToken ct);

	private readonly Settings settings;

	private readonly PictureEngine drawer;

	private readonly CompanionHost companion;

	private readonly string picturesRoot;

	private readonly Draw draw;

	private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap };

	private readonly ComboBox persona = new ComboBox { Width = 240, HorizontalAlignment = HorizontalAlignment.Left };

	private readonly WrapPanel references = new WrapPanel();

	private readonly Image result = new Image { MaxHeight = 420, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };

	private readonly RadioButton fast = new RadioButton { GroupName = "model" };

	private readonly RadioButton best = new RadioButton { GroupName = "model" };

	private readonly RadioButton anime = new RadioButton { GroupName = "model" };

	private string ChosenModel => best.IsChecked == true ? PictureModels.Best : anime.IsChecked == true ? PictureModels.Anime : PictureModels.Fast;

	private CancellationTokenSource? drawing;

	public PicturesWindow(Settings settings, PictureEngine drawer, CompanionHost companion, string picturesRoot, Action saveSettings, Draw draw)
	{
		this.settings = settings;
		this.drawer = drawer;
		this.companion = companion;
		this.picturesRoot = picturesRoot;
		this.draw = draw;
		base.Title = "Aquila · Pictures";
		base.Width = 760;
		base.Height = 900;
		base.MinWidth = 600;
		base.MinHeight = 560;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		CloseOnEscape = true;

		StackPanel body = new StackPanel();
		CheckBox enabled = new CheckBox { Content = "Let Vicki and Aquila draw pictures", IsChecked = settings.Pictures.Enabled };
		body.Children.Add(enabled);
		body.Children.Add(Ui.Text("When it's on, they can draw a picture when you ask for one, or when showing something fits the moment. Pictures appear in the chat and are saved in the pictures folder.", "Type.Small"));

		(bool fastReady, string fastProblem) = drawer.Kit.Check(PictureModels.Fast);
		(bool bestReady, string bestProblem) = drawer.Kit.Check(PictureModels.Best);
		fast.Content = ModelLabel("FLUX 3 Image Klein (fast)", "About 15 seconds a picture (25 with reference pictures). Your chat model stays loaded.", fastReady, fastProblem);
		best.Content = ModelLabel("Qwen-Image-2.1 (best quality)", "About 45 seconds a picture. The chat model pauses while it draws, then picks up where it was.", bestReady, bestProblem);
		(bool animeReady, string animeProblem) = drawer.Kit.Check(PictureModels.Anime);
		anime.Content = ModelLabel("WAI-Illustrious (anime, uncensored)", "Anime only, about 16 seconds a picture. The chat model pauses while it draws. Doesn't censor bodies, but can't use reference pictures, so faces vary more.", animeReady, animeProblem);
		fast.IsChecked = settings.Pictures.Model != PictureModels.Best && settings.Pictures.Model != PictureModels.Anime;
		best.IsChecked = settings.Pictures.Model == PictureModels.Best;
		anime.IsChecked = settings.Pictures.Model == PictureModels.Anime;
		StackPanel models = new StackPanel();
		fast.Margin = new Thickness(0, 0, 0, 8);
		models.Children.Add(fast);
		best.Margin = new Thickness(0, 0, 0, 8);
		models.Children.Add(best);
		models.Children.Add(anime);
		body.Children.Add(Ui.Field("Model", models));
		ComboBox encoder = new ComboBox { ItemsSource = KleinEncoders.All.Select(e => e.Label).ToList(), Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
		encoder.SelectedIndex = Math.Max(0, KleinEncoders.All.ToList().FindIndex(e => e.Key == settings.Pictures.KleinEncoder));
		encoder.SelectionChanged += delegate
		{
			// Try it below uses the choice straight away; Save keeps it.
			drawer.Kit.KleinEncoder = KleinEncoders.All[Math.Max(0, encoder.SelectedIndex)].Key;
		};
		body.Children.Add(Ui.Field("Text encoder (FLUX 3 Image Klein)", encoder, "How the fast model reads the prompt. The uncensored versions have their refusal training removed; 8-bit is the recommended one, 16-bit is twice the size for a tiny difference."));

		TextBox avoid = new TextBox { Text = settings.Pictures.AnimeAvoid ?? PictureCommand.AnimeAvoidDefault, AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxLength = PictureCommand.MaxAvoidChars, VerticalContentAlignment = VerticalAlignment.Top };
		avoid.TextChanged += delegate
		{
			// Try it below uses the list straight away; Save keeps it, Cancel puts the saved one back.
			drawer.Kit.AnimeAvoid = avoid.Text;
		};
		Button avoidReset = Ui.Button("Back to the starting list", "Btn.Link", delegate { avoid.Text = PictureCommand.AnimeAvoidDefault; });
		avoidReset.HorizontalAlignment = HorizontalAlignment.Left;
		avoidReset.Margin = new Thickness(0, 4, 0, 0);
		StackPanel avoidSection = new StackPanel();
		avoidSection.Children.Add(avoid);
		avoidSection.Children.Add(avoidReset);
		body.Children.Add(Ui.Field("Avoid (anime engine)", avoidSection, "Words separated by commas that the anime engine steers away from. Remove one to allow it, add one to keep it out. \"" + PictureCommand.AnimeAlwaysAvoid + "\" is always added, so everyone stays an adult."));

		CheckBox useRefs = new CheckBox { Content = "Use her reference pictures so she looks the same every time", IsChecked = settings.Pictures.UseReferences, Margin = new Thickness(0, 16, 0, 0) };
		body.Children.Add(useRefs);

		List<PersonaEntry> personas;
		try
		{
			personas = companion.Registry.Load().Personas.ToList();
		}
		catch (PersonaConfigException ex)
		{
			personas = new List<PersonaEntry>();
			Problem(ex.Message);
		}
		foreach (PersonaEntry p in personas)
		{
			persona.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
		}
		persona.SelectionChanged += delegate
		{
			ShowReferences();
		};
		StackPanel refBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
		refBar.Children.Add(persona);
		Button addRefs = Ui.Button("Add pictures…", "Btn.Secondary", delegate
		{
			AddReferences();
		});
		addRefs.Margin = new Thickness(8, 0, 8, 0);
		refBar.Children.Add(addRefs);
		refBar.Children.Add(Ui.Button("Open folder", "Btn.Ghost", delegate
		{
			string? folder = ReferenceFolder();
			if (folder != null)
			{
				Directory.CreateDirectory(folder);
				Open(folder);
			}
		}));
		StackPanel refSection = new StackPanel();
		refSection.Children.Add(refBar);
		refSection.Children.Add(references);
		body.Children.Add(Ui.Field("Reference pictures", refSection, "3 or 4 clear pictures of her, 768 pixels or bigger: her face, and half-body, in plain clothes. The first four (by file name) are used. Her clothes in a picture come from the conversation, not from these."));

		(bool videoReady, string videoProblem) = drawer.Video.Check();
		bool lightning = videoReady && drawer.Video.Resolve().HasLightning;
		StackPanel videoSection = new StackPanel();
		CheckBox videoOn = new CheckBox { Content = "Let Vicki and Aquila make short videos", IsChecked = settings.Video.Enabled && videoReady, IsEnabled = videoReady };
		CheckBox videoFast = new CheckBox { Content = "Faster videos (Lightning: a few minutes instead of about 15)", IsChecked = settings.Video.Fast, IsEnabled = lightning, Margin = new Thickness(0, 6, 0, 0) };
		videoSection.Children.Add(videoOn);
		videoSection.Children.Add(videoFast);
		TextBlock videoState = Ui.Text(videoReady ? (lightning ? "Wan 2.2 installed, with the Lightning speed-up." : "Wan 2.2 installed (no Lightning speed-up, so about 15 minutes a clip).") : videoProblem, "Type.Small");
		videoState.Foreground = videoReady ? Ui.Brush("Accent") : Ui.Brush("Danger");
		videoState.TextWrapping = TextWrapping.Wrap;
		videoSection.Children.Add(videoState);
		body.Children.Add(Ui.Field("Video", videoSection, "A 5-second clip without sound: a still is drawn with the model above (and her reference pictures), then Wan 2.2 animates it. The chat model pauses while the clip is made. Clips show in the chat; click one to play it."));

		TextBox prompt = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 70, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Text = "She sits on a sofa with a mug of coffee, smiling at the viewer, evening lamp light." };
		Button test = Ui.Button("Draw a test picture", "Btn.Secondary", null);
		test.HorizontalAlignment = HorizontalAlignment.Left;
		test.Margin = new Thickness(0, 8, 0, 0);
		test.Click += async delegate
		{
			if (drawing != null)
			{
				drawing.Cancel();
				return;
			}
			string model = ChosenModel;
			List<string> refs = useRefs.IsChecked == true ? CurrentReferences().Take(PictureCommand.MaxReferences).ToList() : new List<string>();
			string text = prompt.Text.Trim();
			string? name = (persona.SelectedItem as ComboBoxItem)?.Content as string;
			string? look = AppearanceOf();
			if (look != null)
			{
				text = look + ". " + text;
			}
			drawing = new CancellationTokenSource();
			test.Content = "Stop";
			Ok("Drawing" + (refs.Count > 0 ? " with " + refs.Count + " reference picture" + (refs.Count > 1 ? "s" : "") : "") + "…");
			try
			{
				string file = await draw(model, PicturePrompt.WithReferences(text, refs.Count), (896, 1152), refs, drawing.Token);
				result.Source = Load(file, 900);
				Ok("Done. Saved as " + file);
			}
			catch (OperationCanceledException)
			{
				Ok("Stopped.");
			}
			catch (Exception ex) when (ex is IOException || ex is PersonaConfigException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is System.Net.Http.HttpRequestException)
			{
				Problem(ex.Message);
			}
			finally
			{
				drawing.Dispose();
				drawing = null;
				test.Content = "Draw a test picture";
			}
		};
		StackPanel testSection = new StackPanel();
		testSection.Children.Add(prompt);
		testSection.Children.Add(test);
		testSection.Children.Add(result);
		body.Children.Add(Ui.Field("Try it", testSection, "Uses the model chosen above and, if it's on, the selected persona's appearance card and reference pictures."));

		Button folder = Ui.Button("Open the pictures folder", "Btn.Ghost", delegate
		{
			Directory.CreateDirectory(picturesRoot);
			Open(picturesRoot);
		});
		Button save = Ui.Button("Save", "Btn.Primary", delegate
		{
			string model = ChosenModel;
			(bool ready, string problem) = drawer.Kit.Check(model);
			if (enabled.IsChecked == true && !ready)
			{
				Problem(problem);
				return;
			}
			settings.Pictures.Enabled = enabled.IsChecked == true;
			settings.Pictures.Model = model;
			settings.Pictures.KleinEncoder = KleinEncoders.All[Math.Max(0, encoder.SelectedIndex)].Key;
			settings.Pictures.UseReferences = useRefs.IsChecked == true;
			try
			{
				PictureCommand.AnimeNegative(avoid.Text);
			}
			catch (PersonaConfigException ex)
			{
				Problem(ex.Message);
				return;
			}
			settings.Pictures.AnimeAvoid = avoid.Text.Trim();
			drawer.Kit.AnimeAvoid = settings.Pictures.AnimeAvoid;
			settings.Video.Enabled = videoOn.IsChecked == true;
			settings.Video.Fast = videoFast.IsChecked == true || !lightning;
			try
			{
				saveSettings();
				DialogResult = true;
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
			{
				Problem(ex.Message);
			}
		});
		Button cancel = Ui.Button("Cancel", "Btn.Secondary", delegate
		{
			Close();
		});
		base.Content = Ui.DialogScaffold("Pictures", "A built-in image generator that runs on your laptop. The chat model writes each picture's prompt from the conversation and her appearance card.", body, status, folder, cancel, save);
		base.Closed += delegate
		{
			drawing?.Cancel();
			drawer.Kit.AnimeAvoid = settings.Pictures.AnimeAvoid ?? PictureCommand.AnimeAvoidDefault;
		};
		if (persona.Items.Count > 0)
		{
			persona.SelectedIndex = 0;
		}
	}

	private static UIElement ModelLabel(string name, string detail, bool ready, string problem)
	{
		StackPanel p = new StackPanel();
		p.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold });
		p.Children.Add(Ui.Text(detail, "Type.Small"));
		TextBlock state = Ui.Text(ready ? "Installed" : problem, "Type.Small");
		state.Foreground = ready ? Ui.Brush("Accent") : Ui.Brush("Danger");
		state.TextWrapping = TextWrapping.Wrap;
		p.Children.Add(state);
		return p;
	}

	private string? PersonaId => (persona.SelectedItem as ComboBoxItem)?.Tag as string;

	private string? ReferenceFolder()
	{
		string? id = PersonaId;
		if (id == null)
		{
			return null;
		}
		try
		{
			return PictureFiles.ReferenceFolder(companion.Folder(id));
		}
		catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is ArgumentException)
		{
			Problem(ex.Message);
			return null;
		}
	}

	private List<string> CurrentReferences()
	{
		string? id = PersonaId;
		if (id == null)
		{
			return new List<string>();
		}
		try
		{
			return PictureFiles.References(companion.Folder(id));
		}
		catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is ArgumentException)
		{
			Problem(ex.Message);
			return new List<string>();
		}
	}

	private string? AppearanceOf()
	{
		string? id = PersonaId;
		string? name = (persona.SelectedItem as ComboBoxItem)?.Content as string;
		if (id == null || name == null || !companion.IsSetUp(id))
		{
			return null;
		}
		try
		{
			return AppearanceCaption.For(name, companion.Open(id).Store.ReadAppearance());
		}
		catch (Exception ex) when (ex is AppearanceCardUnavailableException || ex is PersonaConfigException || ex is IOException)
		{
			return null;
		}
	}

	private void ShowReferences()
	{
		references.Children.Clear();
		List<string> files = CurrentReferences();
		if (files.Count == 0)
		{
			references.Children.Add(Ui.Text("No reference pictures yet.", "Type.Small"));
			return;
		}
		for (int i = 0; i < files.Count; i++)
		{
			string file = files[i];
			Grid tile = new Grid { Margin = new Thickness(0, 0, 8, 8) };
			Image img = new Image { Source = Load(file, 240), Height = 120, Stretch = Stretch.Uniform, ToolTip = Path.GetFileName(file) + (i >= PictureCommand.MaxReferences ? " (not used: only the first four are)" : ""), Opacity = i >= PictureCommand.MaxReferences ? 0.45 : 1.0 };
			tile.Children.Add(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = img });
			Button remove = Ui.IconButton("", "Remove this reference picture", delegate
			{
				try
				{
					string removed = Path.Combine(Path.GetDirectoryName(file)!, "removed");
					Directory.CreateDirectory(removed);
					File.Move(file, Path.Combine(removed, Path.GetFileName(file)), overwrite: true);
					ShowReferences();
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					Problem(ex.Message);
				}
			});
			remove.HorizontalAlignment = HorizontalAlignment.Right;
			remove.VerticalAlignment = VerticalAlignment.Top;
			remove.Background = Ui.Brush("Surface.2");
			tile.Children.Add(remove);
			references.Children.Add(tile);
		}
	}

	private void AddReferences()
	{
		string? folder = ReferenceFolder();
		if (folder == null)
		{
			Problem("Choose a persona first.");
			return;
		}
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "Add reference pictures",
			Filter = "Pictures (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp",
			Multiselect = true
		};
		if (dialog.ShowDialog(this) != true)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(folder);
			int next = PictureFiles.References(Path.GetDirectoryName(folder)!).Count + 1;
			List<string> small = new List<string>();
			foreach (string source in dialog.FileNames)
			{
				string target = Path.Combine(folder, next.ToString("00") + " - " + Path.GetFileName(source));
				File.Copy(source, target, overwrite: false);
				next++;
				BitmapFrame frame = BitmapFrame.Create(new Uri(target), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
				if (Math.Min(frame.PixelWidth, frame.PixelHeight) < 512)
				{
					small.Add(Path.GetFileName(source));
				}
			}
			ShowReferences();
			if (small.Count > 0)
			{
				Problem("Added, but these are small, so her face may come out less exact: " + string.Join(", ", small) + ". Pictures of 768 pixels or more work best.");
			}
			else
			{
				Ok("Added. Try a test picture below.");
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is FileFormatException)
		{
			Problem(ex.Message);
		}
	}

	private static BitmapImage? Load(string file, int width)
	{
		try
		{
			BitmapImage image = new BitmapImage();
			image.BeginInit();
			image.CacheOption = BitmapCacheOption.OnLoad;
			image.DecodePixelWidth = width;
			image.UriSource = new Uri(file, UriKind.Absolute);
			image.EndInit();
			image.Freeze();
			return image;
		}
		catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is UriFormatException || ex is ArgumentException)
		{
			return null;
		}
	}

	private void Open(string path)
	{
		try
		{
			Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
		}
		catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is IOException)
		{
			Problem(ex.Message);
		}
	}

	private void Ok(string text)
	{
		status.Text = text;
		status.Foreground = Ui.Brush("Text.Secondary");
	}

	private void Problem(string text)
	{
		status.Text = text;
		status.Foreground = Ui.Brush("Danger");
	}
}
