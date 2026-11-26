using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Living;

namespace AquilaLocal;

/// <summary>
/// Living mode: the switches (on/off, how often, internet lookups, messaging you first) and each persona's
/// journal of her own thoughts, with every lookup she made.
/// </summary>
public sealed class LivingWindow : AquilaWindow
{
	public delegate Task<JournalEntry?> ThinkNow(string personaId, string name, bool dream);

	private static readonly (int Min, int Max)[] Ranges = { (5, 30), (15, 60), (30, 90), (60, 180) };

	private static readonly int[] Idles = { 5, 10, 20, 30, 60 };

	private static readonly int[] PerDay = { 1, 2, 3, 5, 8 };

	private readonly InnerLifeJournal journal;

	private readonly Func<string> status;

	private readonly TextBlock statusLine = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };

	private readonly TextBlock footer = new TextBlock { TextWrapping = TextWrapping.Wrap };

	private readonly ComboBox persona = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };

	private readonly StackPanel entries = new StackPanel();

	private bool closed;

	public LivingWindow(Settings settings, CompanionHost companion, InnerLifeJournal journal, Func<string> status, Action saveSettings, ThinkNow thinkNow)
	{
		this.journal = journal;
		this.status = status;
		base.Title = "Aquila · Living mode";
		base.Width = 760;
		base.Height = 940;
		base.MinWidth = 600;
		base.MinHeight = 560;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		CloseOnEscape = true;
		LivingOptions o = settings.Living;

		StackPanel body = new StackPanel();
		CheckBox enabled = new CheckBox { Content = "Let them live while you're away", IsChecked = o.Enabled };
		body.Children.Add(enabled);
		body.Children.Add(Ui.Text("While Aquila is open and you haven't used it for a while, she has thoughts of her own: daydreams, feelings, things she's curious about. They come from her personality, her drives and what she remembers. They're kept in her journal and her memory, so she may bring one up later. It uses the chat model you already have loaded, on its spare slot, and never adds to your chat's length.", "Type.Small"));

		ComboBox interval = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
		foreach ((int min, int max) in Ranges)
		{
			interval.Items.Add($"At random, every {Span(min)} to {Span(max)}");
		}
		interval.SelectedIndex = Math.Max(0, Array.FindIndex(Ranges, ((int Min, int Max) r) => r.Min == o.MinMinutes && r.Max == o.MaxMinutes));
		if (Array.FindIndex(Ranges, ((int Min, int Max) r) => r.Min == o.MinMinutes && r.Max == o.MaxMinutes) < 0)
		{
			interval.SelectedIndex = 1;
		}
		ComboBox idle = Choice(Idles, o.IdleMinutes, (int m) => $"After {m} minutes away");
		body.Children.Add(Ui.Field("How often", interval, "Each wait is picked at random in that range, so her thoughts don't come like clockwork. At night (11 PM to 8 AM) she's asleep: her thoughts become dreams, and come half as often. Each thought takes about 10 to 20 seconds of graphics card time."));
		body.Children.Add(Ui.Field("Only when you're away", idle, "She thinks only after you've left Aquila alone this long, and stops the moment you send a message, so she never slows your chat."));

		CheckBox dreams = new CheckBox { Content = "Paint her dreams", IsChecked = o.DrawDreams, Margin = new Thickness(0, 16, 0, 0) };
		body.Children.Add(dreams);
		body.Children.Add(Ui.Text("When she dreams, the picture model chosen in Pictures paints the dream, with her appearance and reference pictures when she's in it. The pictures are in her journal and the pictures folder. With the best-quality model the chat model pauses while it paints.", "Type.Small"));

		CheckBox web = new CheckBox { Content = "Let her look things up on the internet for her own curiosity", IsChecked = o.WebLookups, Margin = new Thickness(0, 16, 0, 0) };
		body.Children.Add(web);
		body.Children.Add(Ui.Text("Uses your Brave Search key if one is saved in Internet settings, otherwise Wikipedia. She never searches for names or anything private, and every search and page she reads is listed in her journal. This switch is separate from the chat's Internet setting.", "Type.Small"));

		CheckBox first = new CheckBox { Content = "Let her message you first when a thought is strong enough", IsChecked = o.MessageFirst, Margin = new Thickness(0, 16, 0, 0) };
		body.Children.Add(first);
		body.Children.Add(Ui.Text("Her message appears in your latest chat with her, with a sound. She waits for your answer before writing again, and leaves at least two hours between messages.", "Type.Small"));
		ComboBox perDay = Choice(PerDay, o.MaxMessagesPerDay, (int n) => n == 1 ? "At most once a day" : $"At most {n} times a day");
		body.Children.Add(Ui.Field("How often she may write", perDay));
		CheckBox quiet = new CheckBox { Content = "Never between 11 PM and 8 AM", IsChecked = o.QuietHours, Margin = new Thickness(0, 8, 0, 0) };
		body.Children.Add(quiet);

		List<PersonaEntry> personas;
		try
		{
			personas = companion.Registry.Load().Personas.Where((PersonaEntry p) => companion.IsSetUp(p.Id)).ToList();
		}
		catch (PersonaConfigException ex)
		{
			personas = new List<PersonaEntry>();
			Problem(ex.Message);
		}
		StackPanel who = new StackPanel();
		List<CheckBox> lives = new List<CheckBox>();
		foreach (PersonaEntry p in personas)
		{
			CheckBox c = new CheckBox { Content = p.Name, Tag = p.Id, IsChecked = o.PersonaIds.Count == 0 || o.PersonaIds.Contains(p.Id), Margin = new Thickness(0, 0, 0, 4) };
			lives.Add(c);
			who.Children.Add(c);
			persona.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
		}
		if (personas.Count == 0)
		{
			who.Children.Add(Ui.Text("No persona is set up yet.", "Type.Small"));
		}
		body.Children.Add(Ui.Field("Who lives", who));

		StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal };
		bar.Children.Add(persona);
		Button think = Ui.Button("Have a thought now", "Btn.Secondary", null);
		Button dreamNow = Ui.Button("Have a dream now", "Btn.Secondary", null);
		think.Margin = new Thickness(8, 0, 0, 0);
		dreamNow.Margin = new Thickness(8, 0, 0, 0);
		async Task Now(bool dream)
		{
			if (persona.SelectedItem is not ComboBoxItem item)
			{
				return;
			}
			think.IsEnabled = false;
			dreamNow.IsEnabled = false;
			try
			{
				JournalEntry? entry = await thinkNow((string)item.Tag, (string)item.Content, dream);
				Ok(entry != null ? "Done." : status());
			}
			catch (InvalidOperationException ex)
			{
				Problem(ex.Message);
			}
			finally
			{
				think.IsEnabled = true;
				dreamNow.IsEnabled = true;
				Refresh();
			}
		}
		think.Click += async delegate { await Now(dream: false); };
		dreamNow.Click += async delegate { await Now(dream: true); };
		bar.Children.Add(think);
		bar.Children.Add(dreamNow);
		StackPanel journalSection = new StackPanel();
		journalSection.Children.Add(bar);
		journalSection.Children.Add(statusLine);
		journalSection.Children.Add(new ScrollViewer { Content = entries, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
		body.Children.Add(Ui.Field("Her journal", journalSection, "Newest first. The buttons work even while you're here, with the switches as they were last saved."));
		persona.SelectionChanged += delegate
		{
			Refresh();
		};

		Button folder = Ui.Button("Open the journal folder", "Btn.Ghost", delegate
		{
			Directory.CreateDirectory(journal.Folder);
			Process.Start(new ProcessStartInfo(journal.Folder) { UseShellExecute = true });
		});
		Button save = Ui.Button("Save", "Btn.Primary", delegate
		{
			o.Enabled = enabled.IsChecked == true;
			(o.MinMinutes, o.MaxMinutes) = Ranges[Math.Max(0, interval.SelectedIndex)];
			o.DrawDreams = dreams.IsChecked == true;
			o.IdleMinutes = Idles[Math.Max(0, idle.SelectedIndex)];
			o.WebLookups = web.IsChecked == true;
			o.MessageFirst = first.IsChecked == true;
			o.MaxMessagesPerDay = PerDay[Math.Max(0, perDay.SelectedIndex)];
			o.QuietHours = quiet.IsChecked == true;
			o.PersonaIds = lives.All((CheckBox c) => c.IsChecked == true) ? new List<string>() : lives.Where((CheckBox c) => c.IsChecked == true).Select((CheckBox c) => (string)c.Tag).ToList();
			try
			{
				saveSettings();
				Ok("Saved.");
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
			{
				Problem(ex.Message);
			}
		});
		Button close = Ui.Button("Close", "Btn.Secondary", delegate
		{
			Close();
		});
		base.Content = Ui.DialogScaffold("Living mode", "Your personas think on their own while you're away, and keep a journal.", body, footer, folder, close, save);
		base.Closed += delegate
		{
			closed = true;
		};
		if (persona.Items.Count > 0)
		{
			persona.SelectedIndex = 0;
		}
		Refresh();
	}

	private static string Span(int minutes) => minutes < 60 ? minutes + " minutes" : minutes == 60 ? "1 hour" : $"{minutes / 60.0:0.#} hours";

	private static ComboBox Choice(int[] values, int current, Func<int, string> label)
	{
		ComboBox box = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
		int best = 0;
		for (int i = 0; i < values.Length; i++)
		{
			box.Items.Add(label(values[i]));
			if (Math.Abs(values[i] - current) < Math.Abs(values[best] - current))
			{
				best = i;
			}
		}
		box.SelectedIndex = best;
		return box;
	}

	/// <summary>Shows the latest status and journal (called when she has a new thought).</summary>
	public void Refresh()
	{
		if (closed)
		{
			return;
		}
		statusLine.Text = status();
		entries.Children.Clear();
		if (persona.SelectedItem is not ComboBoxItem item)
		{
			return;
		}
		List<JournalEntry> list;
		try
		{
			list = journal.Read((string)item.Tag);
		}
		catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException)
		{
			entries.Children.Add(Ui.Text(ex.Message, "Type.Small"));
			return;
		}
		if (list.Count == 0)
		{
			entries.Children.Add(Ui.Text("No thoughts yet. They start once living mode is on and you've been away a while.", "Type.Small"));
			return;
		}
		foreach (JournalEntry e in Enumerable.Reverse(list).Take(150))
		{
			entries.Children.Add(View(e));
		}
	}

	private static UIElement View(JournalEntry e)
	{
		StackPanel p = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
		TextBlock head = Ui.Text(e.At.ToLocalTime().ToString("ddd MMM d · h:mm tt") + " · " + ThoughtKinds.Icon(e.Kind) + " " + e.Kind, "Type.Small");
		head.FontWeight = FontWeights.SemiBold;
		p.Children.Add(head);
		TextBlock text = Ui.Text(e.Text);
		text.TextWrapping = TextWrapping.Wrap;
		p.Children.Add(text);
		if (e.Picture.Length > 0 && File.Exists(e.Picture))
		{
			try
			{
				System.Windows.Media.Imaging.BitmapImage bmp = new System.Windows.Media.Imaging.BitmapImage();
				bmp.BeginInit();
				bmp.UriSource = new Uri(e.Picture);
				bmp.DecodePixelWidth = 480;
				bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
				bmp.EndInit();
				bmp.Freeze();
				Image img = new Image { Source = bmp, MaxHeight = 260, Stretch = System.Windows.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 4), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Open the picture" };
				string file = e.Picture;
				img.MouseLeftButtonUp += delegate
				{
					Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
				};
				p.Children.Add(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = img, HorizontalAlignment = HorizontalAlignment.Left });
			}
			catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is UriFormatException || ex is System.Runtime.InteropServices.COMException)
			{
				p.Children.Add(Ui.Text("(Couldn't show the dream picture: " + ex.Message + ")", "Type.Small"));
			}
		}
		foreach (JournalLookup l in e.Lookups)
		{
			TextBlock line = Ui.Text("", "Type.Small");
			line.TextWrapping = TextWrapping.Wrap;
			if (l.Kind == JournalLookup.Read && Uri.TryCreate(l.What, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
			{
				line.Inlines.Add(new Run("📄 Read: "));
				Hyperlink link = new Hyperlink(new Run(l.Title.Length > 0 ? l.Title : uri.Host)) { NavigateUri = uri, ToolTip = uri.AbsoluteUri };
				link.RequestNavigate += delegate(object _, System.Windows.Navigation.RequestNavigateEventArgs a)
				{
					Process.Start(new ProcessStartInfo(a.Uri.AbsoluteUri) { UseShellExecute = true });
				};
				line.Inlines.Add(link);
			}
			else
			{
				line.Inlines.Add(new Run("🔎 Searched: " + l.What));
			}
			p.Children.Add(line);
		}
		if (e.MessagedYou)
		{
			TextBlock m = Ui.Text("💬 She messaged you: " + ThoughtMarkup.Plain(e.Message).Truncate(300), "Type.Small");
			m.TextWrapping = TextWrapping.Wrap;
			m.Foreground = Ui.Brush("Accent");
			p.Children.Add(m);
		}
		if (e.Note.Length > 0)
		{
			TextBlock n = Ui.Text(e.Note, "Type.Small");
			n.TextWrapping = TextWrapping.Wrap;
			n.Opacity = 0.75;
			p.Children.Add(n);
		}
		return p;
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
