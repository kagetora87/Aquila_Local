using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Pictures;

namespace AquilaLocal;

/// <summary>
/// "Picture style" for the small choice pictures (Generate presets and Create Companion): who's in them and how
/// they're drawn, with a Test that draws one sample before you save.
/// </summary>
public sealed class TileStyleWindow : AquilaWindow
{
	private readonly PresetLibrary library;

	private readonly Func<string, string, CancellationToken, Task> draw;

	private readonly TextBox woman;

	private readonly TextBox man;

	private readonly TextBox art;

	private readonly ComboBox sample;

	private readonly Image preview = new Image { Stretch = Stretch.Uniform };

	private readonly Border previewBox;

	private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

	private readonly Button test;

	private readonly Button save;

	private CancellationTokenSource? testing;

	private readonly List<(string Label, Func<TileStyle, string> Prompt)> samples = new();

	/// <summary>True once a new style was saved (the small pictures will be offered again).</summary>
	public bool Saved { get; private set; }

	/// <param name="draw">Draws a prompt to a file with the fast picture model (throws if it can't).</param>
	public TileStyleWindow(PresetLibrary library, Func<string, string, CancellationToken, Task> draw)
	{
		this.library = library;
		this.draw = draw;
		base.Title = "Aquila · Picture style";
		base.Width = 860;
		base.Height = 720;
		base.MinWidth = 720;
		base.MinHeight = 560;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.Height = Math.Min(base.Height, SystemParameters.WorkArea.Height - 30.0);
		CloseOnEscape = true;
		TileStyle current = library.Style;

		Grid layout = new Grid();
		layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

		StackPanel form = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
		woman = Box(current.Woman);
		form.Children.Add(Ui.Field("The woman in the pictures", woman, "Used for every preset except the men's outfits, and for the women in Create Companion. For example: \"a young adult woman with long hair and a gentle smile\".", 0));
		man = Box(current.Man);
		form.Children.Add(Ui.Field("The man in the pictures", man, "Used for the men's outfits and the men in Create Companion."));
		art = Box(current.Art);
		form.Children.Add(Ui.Field("Art style", art, "How they're drawn. For example: \"anime illustration, clean line art, soft cel shading\"."));
		WrapPanel styles = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
		foreach ((string label, string text) in CreatorOptions.ArtStyles)
		{
			string words = text;
			Button chip = Ui.Button(label, "Btn.Secondary", delegate { art.Text = words; });
			chip.Margin = new Thickness(0, 0, 6, 6);
			chip.Padding = new Thickness(10, 4, 10, 4);
			styles.Children.Add(chip);
		}
		form.Children.Add(styles);
		Button reset = Ui.Button("Back to the starting style", "Btn.Link", delegate
		{
			woman.Text = TileStyle.DefaultWoman;
			man.Text = TileStyle.DefaultMan;
			art.Text = TileStyle.DefaultArt;
		});
		reset.HorizontalAlignment = HorizontalAlignment.Left;
		reset.Margin = new Thickness(0, 10, 0, 0);
		form.Children.Add(reset);
		TextBlock note = Ui.Text("Saving a new style makes all the small pictures again: the Generate screen and Create Companion each offer a button for it (about 10 minutes each). Your companions' own pictures aren't affected.", "Type.Caption");
		note.Margin = new Thickness(0, 14, 0, 0);
		note.TextWrapping = TextWrapping.Wrap;
		form.Children.Add(note);
		layout.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

		// Right: try it on one picture.
		StackPanel side = new StackPanel();
		side.Children.Add(Ui.Text("Try it", "Type.Label"));
		foreach (GenPreset p in PresetLibrary.BuiltIn)
		{
			GenPreset preset = p;
			samples.Add((PresetKinds.Label(p.Kind) + ": " + p.Name, s => PresetLibrary.TilePrompt(preset, s)));
		}
		foreach (CreatorOption o in CreatorOptions.All)
		{
			CreatorOption option = o;
			string g = o.For.Length > 0 ? o.For : Genders.Female;
			samples.Add(("Create: " + o.Label + (o.Kind == CreatorKinds.Ethnicity ? " (woman)" : ""), s => CreatorOptions.TilePrompt(option, g, s)));
		}
		sample = new ComboBox { ItemsSource = samples.Select(x => x.Label).ToList(), SelectedIndex = 0, Margin = new Thickness(0, 6, 0, 0), MaxDropDownHeight = 360 };
		side.Children.Add(sample);
		test = Ui.Button(Ui.IconLabel("", "Test"), "Btn.Secondary", delegate { _ = TestAsync(); });
		test.HorizontalAlignment = HorizontalAlignment.Left;
		test.Margin = new Thickness(0, 10, 0, 0);
		side.Children.Add(test);
		previewBox = new Border
		{
			Width = 300,
			Height = 375,
			Margin = new Thickness(0, 12, 0, 0),
			CornerRadius = new CornerRadius(10),
			Background = Ui.Brush("Surface.1"),
			ClipToBounds = true,
			Child = new TextBlock { Text = "Press Test to draw this one with the words on the left (about 15 seconds). Nothing is saved until you press Save.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, Foreground = Ui.Brush("Text.Secondary") }
		};
		side.Children.Add(previewBox);
		side.Children.Add(status);
		Grid.SetColumn(side, 1);
		layout.Children.Add(side);

		save = Ui.Button("Save", "Btn.Primary", delegate { Save(); });
		Button cancel = Ui.Button("Cancel", "Btn.Secondary", delegate { Close(); });
		base.Content = Ui.DialogScaffold("Picture style", "Who appears in the small choice pictures and how they're drawn.", layout, null, cancel, save);
		base.Closing += delegate { testing?.Cancel(); };
	}

	private static TextBox Box(string text)
	{
		return new TextBox { Text = text, MaxLength = TileStyle.MaxChars, AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, MinHeight = 56, VerticalContentAlignment = VerticalAlignment.Top };
	}

	private TileStyle Edited()
	{
		TileStyle s = new TileStyle { Woman = woman.Text, Man = man.Text, Art = art.Text };
		s.Validate();
		return s;
	}

	private void Show(string text, bool bad)
	{
		status.Text = text;
		status.Foreground = Ui.Brush(bad ? "Danger" : "Text.Secondary");
	}

	private async Task TestAsync()
	{
		if (testing != null)
		{
			testing.Cancel();
			return;
		}
		TileStyle s;
		try
		{
			s = Edited();
		}
		catch (PersonaConfigException ex)
		{
			Show(ex.Message, bad: true);
			return;
		}
		string prompt = samples[Math.Max(0, sample.SelectedIndex)].Prompt(s);
		string path = Path.Combine(library.Root, "style-test.png");
		testing = new CancellationTokenSource();
		test.Content = Ui.IconLabel("", "Stop");
		save.IsEnabled = false;
		Show("Drawing…", bad: false);
		try
		{
			Directory.CreateDirectory(library.Root);
			await draw(prompt, path, testing.Token);
			BitmapImage image = new BitmapImage();
			image.BeginInit();
			image.CacheOption = BitmapCacheOption.OnLoad;
			image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
			image.UriSource = new Uri(path);
			image.DecodePixelWidth = 600;
			image.EndInit();
			image.Freeze();
			preview.Source = image;
			previewBox.Child = preview;
			previewBox.Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x14));
			Show("This is how it comes out. Change the words and test again, or Save.", bad: false);
		}
		catch (OperationCanceledException)
		{
			Show("Stopped.", bad: false);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is HttpRequestException || ex is PersonaConfigException || ex is NotSupportedException)
		{
			Show("The test picture couldn't be made: " + ex.Message, bad: true);
		}
		finally
		{
			testing = null;
			if (IsLoaded)
			{
				test.Content = Ui.IconLabel("", "Test");
				save.IsEnabled = true;
			}
		}
	}

	private void Save()
	{
		try
		{
			TileStyle s = Edited();
			TileStyle old = library.Style;
			if (s.Woman == old.Woman && s.Man == old.Man && s.Art == old.Art)
			{
				Close();
				return;
			}
			if (!AquilaDialog.Confirm(this, "Use this style?", "All the small choice pictures will be made again in the new style. The Generate screen and Create Companion each offer a button for it.", "Use it"))
			{
				return;
			}
			library.SaveStyle(s);
			Saved = true;
			DialogResult = true;
		}
		catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException)
		{
			Show(ex.Message, bad: true);
		}
	}
}
