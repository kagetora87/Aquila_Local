using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Living;
using Microsoft.Win32;

namespace AquilaLocal;

/// <summary>Her journal picture (Data\living\&lt;persona&gt;\journal-icon.*), shown on the journal and next to her name.</summary>
public static class DreamJournal
{
	private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" };

	public static string? IconPath(string livingRoot, string personaId)
	{
		if (!PersonaRegistry.IsValidId(personaId))
		{
			throw new ArgumentException("\"" + personaId + "\" isn't a valid persona id.");
		}
		string folder = System.IO.Path.Combine(livingRoot, personaId);
		return Extensions.Select((string x) => System.IO.Path.Combine(folder, "journal-icon" + x)).FirstOrDefault(File.Exists);
	}

	public static ImageSource? LoadIcon(string livingRoot, string personaId, int decodeWidth = 256)
	{
		string? path = IconPath(livingRoot, personaId);
		if (path == null)
		{
			return null;
		}
		BitmapImage bitmap = new BitmapImage();
		bitmap.BeginInit();
		bitmap.StreamSource = new MemoryStream(File.ReadAllBytes(path));
		bitmap.DecodePixelWidth = decodeWidth;
		bitmap.CacheOption = BitmapCacheOption.OnLoad;
		bitmap.EndInit();
		bitmap.Freeze();
		return bitmap;
	}

	/// <summary>Copies a picture in as her journal picture (checked first, so a file that isn't a picture changes nothing).</summary>
	public static void SetIcon(string livingRoot, string personaId, string source)
	{
		string ext = System.IO.Path.GetExtension(source).ToLowerInvariant();
		if (!Extensions.Contains(ext))
		{
			throw new NotSupportedException("Choose a PNG, JPEG, WebP, BMP or GIF picture.");
		}
		BitmapDecoder.Create(new MemoryStream(File.ReadAllBytes(source)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
		string folder = System.IO.Path.Combine(livingRoot, personaId);
		Directory.CreateDirectory(folder);
		string target = System.IO.Path.Combine(folder, "journal-icon" + ext);
		string temp = target + ".saving";
		File.Copy(source, temp, overwrite: true);
		foreach (string x in Extensions)
		{
			string old = System.IO.Path.Combine(folder, "journal-icon" + x);
			if (File.Exists(old) && !string.Equals(old, target, StringComparison.OrdinalIgnoreCase))
			{
				File.Delete(old);
			}
		}
		File.Move(temp, target, overwrite: true);
	}
}

/// <summary>
/// Her journal in its own window: her journal picture on top (click to choose it), then every entry, newest
/// first, each one folded to its date, time and kind. Searchable, with fold all / unfold all, and Save as PDF.
/// </summary>
public sealed class DreamJournalWindow : AquilaWindow
{
	private static readonly (string Label, string? Kind)[] Kinds =
	{
		("Everything", null),
		("Dreams", ThoughtKinds.Dream),
		("Daydreams", ThoughtKinds.Daydream),
		("Thoughts", ThoughtKinds.Thought),
		("Curiosities", ThoughtKinds.Curiosity),
		("Feelings", ThoughtKinds.Feeling),
		("Memories", ThoughtKinds.Memory)
	};

	private readonly string personaId;

	private readonly string name;

	private readonly InnerLifeJournal journal;

	private readonly string livingRoot;

	private readonly Ellipse icon = new Ellipse { Width = 132, Height = 132, Stroke = Ui.Brush("Stroke.Strong"), StrokeThickness = 2 };

	private readonly TextBlock iconGlyph = new TextBlock { Text = "", FontFamily = Ui.FontIcon, FontSize = 44, Foreground = Ui.Brush("Accent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };

	private readonly TextBox search = new TextBox { MinWidth = 220 };

	private readonly ComboBox kind = new ComboBox { Width = 140, Margin = new Thickness(8, 0, 0, 0) };

	private readonly StackPanel list = new StackPanel();

	private readonly TextBlock count = new TextBlock();

	private readonly TextBlock footer = new TextBlock { TextWrapping = TextWrapping.Wrap };

	private readonly HashSet<string> open = new HashSet<string>();

	private readonly DispatcherTimer searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };

	private List<JournalEntry> shown = new List<JournalEntry>();

	private bool closed;

	/// <summary>Raised after her journal picture changes, so the button by her name updates.</summary>
	public event Action? IconChanged;

	private readonly string gender;

	private string W(string text) => Genders.Words(text, gender);

	/// <summary>Entries written after this (when you last opened the journal) are tagged New.</summary>
	private readonly DateTimeOffset seenBefore;

	public DreamJournalWindow(string personaId, string name, InnerLifeJournal journal, string livingRoot, string picturesRoot, string gender = Genders.Female, DateTimeOffset? seenBefore = null)
	{
		this.seenBefore = seenBefore ?? DateTimeOffset.MaxValue;
		this.gender = gender;
		this.personaId = personaId;
		this.name = name;
		this.journal = journal;
		this.livingRoot = livingRoot;
		base.Title = "Aquila · " + name + "'s dream journal";
		base.Width = 760;
		base.Height = 900;
		base.MinWidth = 560;
		base.MinHeight = 520;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		CloseOnEscape = true;

		Grid iconHolder = new Grid { Width = 132, Height = 132, HorizontalAlignment = HorizontalAlignment.Center, Cursor = Cursors.Hand, ToolTip = W("Click to choose a picture for {p} journal (it shows next to {p} name too)") };
		icon.Fill = Ui.Brush("Accent.Subtle");
		iconHolder.Children.Add(icon);
		iconHolder.Children.Add(iconGlyph);
		iconHolder.MouseEnter += delegate { icon.Stroke = Ui.Brush("Accent"); };
		iconHolder.MouseLeave += delegate { icon.Stroke = Ui.Brush("Stroke.Strong"); };
		iconHolder.MouseLeftButtonUp += delegate { ChooseIcon(); };

		TextBlock title = Ui.Text(name + "'s dream journal", "Type.Title");
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.Margin = new Thickness(0, 10, 0, 0);
		count.Style = Ui.Style("Type.Small");
		count.HorizontalAlignment = HorizontalAlignment.Center;
		count.Margin = new Thickness(0, 2, 0, 14);

		Ui.SetPlaceholder(search, W("Search {p} journal"));
		foreach ((string label, _) in Kinds)
		{
			kind.Items.Add(label);
		}
		kind.SelectedIndex = 0;
		Button fold = Ui.Button("Collapse all", "Btn.Ghost", delegate { SetAll(false); });
		Button unfold = Ui.Button("Expand all", "Btn.Ghost", delegate { SetAll(true); });
		DockPanel bar = new DockPanel { Margin = new Thickness(0, 0, 0, 10), LastChildFill = true };
		StackPanel right = new StackPanel { Orientation = Orientation.Horizontal };
		right.Children.Add(kind);
		right.Children.Add(fold);
		right.Children.Add(unfold);
		DockPanel.SetDock(right, Dock.Right);
		bar.Children.Add(right);
		bar.Children.Add(search);

		StackPanel top = new StackPanel { Margin = new Thickness(24, 20, 24, 0) };
		top.Children.Add(iconHolder);
		top.Children.Add(title);
		top.Children.Add(count);
		top.Children.Add(bar);

		ScrollViewer scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(24, 0, 14, 16) };
		Button pdf = Ui.Button("Save as PDF…", "Btn.Secondary", null);
		pdf.ToolTip = "Saves the entries shown (after search and filter) as a PDF with their pictures. Videos are copied into a folder next to it.";
		pdf.Click += async delegate
		{
			pdf.IsEnabled = false;
			try
			{
				await SavePdfAsync(picturesRoot);
			}
			finally
			{
				pdf.IsEnabled = true;
			}
		};
		Button close = Ui.Button("Close", "Btn.Primary", delegate { Close(); });
		Border foot = Ui.Footer(footer, pdf, close);
		DockPanel root = new DockPanel { LastChildFill = true };
		DockPanel.SetDock(top, Dock.Top);
		DockPanel.SetDock(foot, Dock.Bottom);
		root.Children.Add(top);
		root.Children.Add(foot);
		root.Children.Add(scroll);
		base.Content = root;

		searchDelay.Tick += delegate
		{
			searchDelay.Stop();
			Refresh();
		};
		search.TextChanged += delegate
		{
			searchDelay.Stop();
			searchDelay.Start();
		};
		kind.SelectionChanged += delegate { Refresh(); };
		base.Closed += delegate { closed = true; };
		ShowIcon();
		Refresh();
	}

	private void ShowIcon()
	{
		ImageSource? picture = null;
		try
		{
			picture = DreamJournal.LoadIcon(livingRoot, personaId, 400);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException || ex is FileFormatException)
		{
			Problem(W("{P} journal picture couldn't be shown: ") + ex.Message);
		}
		icon.Fill = picture != null ? new ImageBrush(picture) { Stretch = Stretch.UniformToFill } : Ui.Brush("Accent.Subtle");
		iconGlyph.Visibility = picture != null ? Visibility.Collapsed : Visibility.Visible;
	}

	private void ChooseIcon()
	{
		OpenFileDialog dialog = new OpenFileDialog { Title = "Choose a picture for " + name + "'s journal", Filter = "Pictures|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif" };
		if (dialog.ShowDialog(this) != true)
		{
			return;
		}
		try
		{
			DreamJournal.SetIcon(livingRoot, personaId, dialog.FileName);
			ShowIcon();
			IconChanged?.Invoke();
			Ok(W("{P} journal picture is set."));
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException || ex is FileFormatException)
		{
			Problem("That picture couldn't be used: " + ex.Message);
		}
	}

	/// <summary>Shows her journal again (search and filter applied), keeping open entries open.</summary>
	public void Refresh()
	{
		if (closed)
		{
			return;
		}
		List<JournalEntry> all;
		try
		{
			all = journal.Read(personaId);
		}
		catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException)
		{
			list.Children.Clear();
			list.Children.Add(Ui.Text(ex.Message, "Type.Small"));
			return;
		}
		string? wanted = Kinds[Math.Max(0, kind.SelectedIndex)].Kind;
		string[] words = (search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
		shown = Enumerable.Reverse(all).Where((JournalEntry e) => (wanted == null || e.Kind == wanted) && words.All((string w) => Matches(e, w))).ToList();
		int dreams = all.Count((JournalEntry e) => e.Kind == ThoughtKinds.Dream);
		count.Text = all.Count == 0 ? W("Nothing yet. {P} thoughts and dreams appear here once living mode is on and you've been away a while.")
			: all.Count + (all.Count == 1 ? " entry" : " entries") + " · " + dreams + (dreams == 1 ? " dream" : " dreams") + (shown.Count != all.Count ? " · showing " + shown.Count : "");
		list.Children.Clear();
		foreach (JournalEntry e in shown)
		{
			list.Children.Add(Entry(e));
		}
		if (all.Count > 0 && shown.Count == 0)
		{
			list.Children.Add(Ui.Text("Nothing matches.", "Type.Small"));
		}
	}

	private static bool Matches(JournalEntry e, string word)
	{
		bool In(string s) => s.Contains(word, StringComparison.CurrentCultureIgnoreCase);
		return In(e.Text) || In(e.Message) || In(e.Note) || In(e.Kind) || In(e.MoodWord) || In(e.MoodWhy)
			|| In(e.At.ToLocalTime().ToString("dddd MMMM d yyyy")) || e.Lookups.Any((JournalLookup l) => In(l.What) || In(l.Title));
	}

	/// <summary>One entry, folded to its date, time and kind. Its body (and picture) is built the first time it opens.</summary>
	private Expander Entry(JournalEntry e)
	{
		DateTimeOffset at = e.At.ToLocalTime();
		StackPanel header = new StackPanel { Orientation = Orientation.Horizontal };
		header.Children.Add(new TextBlock { Text = at.ToString("ddd, MMM d, yyyy"), FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("Text.Primary"), VerticalAlignment = VerticalAlignment.Center });
		header.Children.Add(new TextBlock { Text = "  ·  " + at.ToString("h:mm tt"), Foreground = Ui.Brush("Text.Secondary"), VerticalAlignment = VerticalAlignment.Center });
		header.Children.Add(new TextBlock { Text = "  ·  " + ThoughtKinds.Icon(e.Kind) + " " + char.ToUpperInvariant(e.Kind[0]) + e.Kind.Substring(1), Foreground = Ui.Brush("Accent"), VerticalAlignment = VerticalAlignment.Center });
		if (e.At > seenBefore)
		{
			header.Children.Add(new Border
			{
				Background = Ui.Brush("Accent"),
				CornerRadius = new CornerRadius(8),
				Padding = new Thickness(7, 1, 7, 1),
				Margin = new Thickness(10, 0, 0, 0),
				VerticalAlignment = VerticalAlignment.Center,
				ToolTip = "Written since you last opened the journal",
				Child = new TextBlock { Text = "New", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("Bg.Sidebar") }
			});
		}
		Expander x = new Expander { Header = header, Style = Ui.Style("Expander.Card"), Margin = new Thickness(0, 0, 0, 8), IsExpanded = open.Contains(e.Id) };
		void Build()
		{
			if (x.Content == null)
			{
				x.Content = LivingWindow.View(e, showHead: false);
			}
		}
		if (x.IsExpanded)
		{
			Build();
		}
		x.Expanded += delegate
		{
			open.Add(e.Id);
			Build();
		};
		x.Collapsed += delegate { open.Remove(e.Id); };
		return x;
	}

	private void SetAll(bool expanded)
	{
		foreach (Expander x in list.Children.OfType<Expander>())
		{
			x.IsExpanded = expanded;
		}
	}

	// ---------------- Save as PDF ----------------

	private async Task SavePdfAsync(string picturesRoot)
	{
		if (shown.Count == 0)
		{
			Problem("There's nothing to save: no entries are shown.");
			return;
		}
		string filter = Kinds[Math.Max(0, kind.SelectedIndex)].Label;
		SaveFileDialog dialog = new SaveFileDialog
		{
			Title = "Save " + name + "'s journal as a PDF",
			Filter = "PDF|*.pdf",
			FileName = name + "'s " + (filter == "Everything" ? "journal" : filter.ToLowerInvariant()) + " " + DateTime.Now.ToString("yyyy-MM-dd") + ".pdf",
			InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
		};
		if (dialog.ShowDialog(this) != true)
		{
			return;
		}
		string pdf = dialog.FileName;
		// Oldest first, like a journal you read through.
		List<JournalEntry> entries = Enumerable.Reverse(shown).ToList();
		string work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aquila-journal-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(work);
		Ok("Saving the PDF…");
		try
		{
			string? iconPng = RoundIcon(work);
			DreamJournalPdf.UseFonts(LoadFont);
			string videos = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(pdf)!, System.IO.Path.GetFileNameWithoutExtension(pdf) + " videos");
			string title = name + "'s " + (filter == "Everything" ? "dream journal" : filter.ToLowerInvariant());
			string subtitle = "From " + entries[0].At.ToLocalTime().ToString("MMMM d, yyyy") + " to " + entries[^1].At.ToLocalTime().ToString("MMMM d, yyyy") + " · " + entries.Count + (entries.Count == 1 ? " entry" : " entries") + (search.Text.Trim().Length > 0 ? " matching “" + search.Text.Trim() + "”" : "");
			(int copied, List<string> missing) = await Task.Run(delegate
			{
				List<string> gone = new List<string>();
				int clips = 0;
				List<DreamJournalPdf.Item> items = new List<DreamJournalPdf.Item>();
				for (int i = 0; i < entries.Count; i++)
				{
					JournalEntry e = entries[i];
					string? picture = null;
					if (e.Picture.Length > 0)
					{
						if (File.Exists(e.Picture))
						{
							picture = AsJpeg(e.Picture, System.IO.Path.Combine(work, i + ".jpg"));
						}
						else
						{
							gone.Add(System.IO.Path.GetFileName(e.Picture));
						}
					}
					string? clipName = null;
					if (e.Video.Length > 0)
					{
						if (File.Exists(e.Video))
						{
							Directory.CreateDirectory(videos);
							clipName = e.At.ToLocalTime().ToString("yyyy-MM-dd HHmm") + " " + e.Kind + System.IO.Path.GetExtension(e.Video);
							File.Copy(e.Video, System.IO.Path.Combine(videos, clipName), overwrite: true);
							clipName = System.IO.Path.GetFileName(videos) + "\\" + clipName;
							clips++;
						}
						else
						{
							gone.Add(System.IO.Path.GetFileName(e.Video));
						}
					}
					items.Add(new DreamJournalPdf.Item(e, picture, clipName));
				}
				DreamJournalPdf.Write(pdf, title, subtitle, iconPng, items);
				return (clips, gone);
			});
			Ok("Saved " + System.IO.Path.GetFileName(pdf) + (copied > 0 ? ", and " + copied + (copied == 1 ? " video" : " videos") + " in “" + System.IO.Path.GetFileName(videos) + "” next to it" : "") + "." +
				(missing.Count > 0 ? " Missing files left out: " + string.Join(", ", missing.Take(5)) + (missing.Count > 5 ? "…" : "") + "." : ""));
			Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + pdf + "\"") { UseShellExecute = true });
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is InvalidOperationException || ex is ArgumentException || ex is FileFormatException || ex is System.Runtime.InteropServices.COMException)
		{
			Problem("The PDF wasn't saved: " + ex.Message);
		}
		finally
		{
			try
			{
				Directory.Delete(work, recursive: true);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
			}
		}
	}

	private static byte[] LoadFont(string file)
	{
		System.Windows.Resources.StreamResourceInfo? info = Application.GetResourceStream(new Uri("pack://application:,,,/Fonts/" + file));
		if (info == null)
		{
			throw new IOException("The font " + file + " is missing from Aquila.");
		}
		using Stream s = info.Stream;
		using MemoryStream m = new MemoryStream();
		s.CopyTo(m);
		return m.ToArray();
	}

	/// <summary>A picture made smaller (at most 1600 pixels wide) and saved as JPEG, so the PDF stays a sensible size.</summary>
	private static string AsJpeg(string source, string target)
	{
		BitmapDecoder decoder = BitmapDecoder.Create(new MemoryStream(File.ReadAllBytes(source)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
		BitmapSource frame = decoder.Frames[0];
		if (frame.PixelWidth > 1600)
		{
			double scale = 1600.0 / frame.PixelWidth;
			frame = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
		}
		if (frame.Format != PixelFormats.Bgr24 && frame.Format != PixelFormats.Bgr32)
		{
			frame = new FormatConvertedBitmap(frame, PixelFormats.Bgr24, null, 0);
		}
		JpegBitmapEncoder encoder = new JpegBitmapEncoder { QualityLevel = 88 };
		encoder.Frames.Add(BitmapFrame.Create(frame));
		using FileStream f = File.Create(target);
		encoder.Save(f);
		return target;
	}

	/// <summary>Her journal picture cut into a circle (as it shows here), for the PDF's first page. Null when there's none.</summary>
	private string? RoundIcon(string work)
	{
		ImageSource? picture = DreamJournal.LoadIcon(livingRoot, personaId, 512);
		if (picture == null)
		{
			return null;
		}
		const int size = 512;
		DrawingVisual v = new DrawingVisual();
		using (DrawingContext dc = v.RenderOpen())
		{
			dc.DrawEllipse(new ImageBrush(picture) { Stretch = Stretch.UniformToFill }, new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x9C, 0x85)), 8), new Point(size / 2.0, size / 2.0), size / 2.0 - 4, size / 2.0 - 4);
		}
		RenderTargetBitmap bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
		bitmap.Render(v);
		PngBitmapEncoder encoder = new PngBitmapEncoder();
		encoder.Frames.Add(BitmapFrame.Create(bitmap));
		string path = System.IO.Path.Combine(work, "icon.png");
		using FileStream f = File.Create(path);
		encoder.Save(f);
		return path;
	}

	private void Ok(string text)
	{
		footer.Text = text;
		footer.Foreground = Ui.Brush("Text.Secondary");
	}

	private void Problem(string text)
	{
		footer.Text = text;
		footer.Foreground = Ui.Brush("Danger");
	}
}
