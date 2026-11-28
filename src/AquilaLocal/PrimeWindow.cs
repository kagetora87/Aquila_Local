using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Priming;
using Microsoft.Win32;

namespace AquilaLocal;

/// <summary>What the review window needs from the app: the chat model, how much it can read at once, and the user's nickname.</summary>
public sealed record PrimeKit(ITextGenerator Generator, int ContextTokens, string UserName);

/// <summary>
/// "Start from a saved chat or document": paste a conversation or add files, the chat model reads them on this
/// computer and drafts a personality sheet, drives and long-term memories, and you review every piece before
/// anything is used. Memories are kept one by one. Used by Create Companion (fills the draft) and by an existing
/// companion's settings (saved to her when you press Save).
/// </summary>
public sealed class PrimeWindow : AquilaWindow
{
	private readonly PrimeKit? kit;

	private readonly PrimeRequest request;

	private readonly bool existing;

	private readonly int memoryCap;

	private readonly int memoryUsed;

	private readonly List<PrimeSource> files = new List<PrimeSource>();

	private readonly TextBlock status = new TextBlock();

	private TextBox? paste;

	private ComboBox? youBox;

	private ComboBox? themBox;

	private List<string> speakers = new List<string>();

	private PrimeProposal? proposal;

	private CancellationTokenSource? cts;

	/// <summary>What you kept, once you pressed Use this / Save.</summary>
	public PrimeChoice? Result { get; private set; }

	private string Name => request.CompanionName.Trim().Length > 0 ? request.CompanionName.Trim() : "your companion";

	private string W(string text) => Genders.Words(text, request.Gender);

	/// <param name="request">Who the companion is: name, gender, drives, and for an existing one her sheet and memories.</param>
	/// <param name="existing">True for a companion who already exists (Save writes to her), false while creating one.</param>
	/// <param name="memoryUsed">Tokens her long-term memory already uses, of <paramref name="memoryCap"/>.</param>
	public PrimeWindow(PrimeKit? kit, PrimeRequest request, bool existing, int memoryCap, int memoryUsed)
	{
		this.kit = kit;
		this.request = request;
		this.existing = existing;
		this.memoryCap = memoryCap;
		this.memoryUsed = memoryUsed;
		base.Title = "Aquila · Start from a saved chat";
		base.Width = 900.0;
		base.Height = 820.0;
		base.MinWidth = 640.0;
		base.MinHeight = 520.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		CloseOnEscape = true;
		ShowAdd();
	}

	private void Say(string text, bool bad = false)
	{
		status.Text = text;
		status.Foreground = Ui.Brush(bad ? "Danger" : "Text.Secondary");
	}

	// ---------------- 1. Add what to learn from ----------------

	private void ShowAdd()
	{
		StackPanel body = new StackPanel();
		if (paste == null)
		{
			paste = new TextBox { AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap, Height = 220, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13 };
			paste.TextChanged += delegate { Refresh(); };
		}
		if (paste.Parent is Panel old)
		{
			old.Children.Remove(paste);
		}
		Ui.SetPlaceholder(paste, "Paste a conversation here, for example:\n\nMe: hey, how was your day?\nSophia: Long, but better now that you're here…\n\nOr paste anything that describes " + W("{o}") + ": a character sheet, notes, a story.");
		body.Children.Add(Ui.Field("Paste a conversation or notes", paste, "", 0));

		Button add = Ui.Button(Ui.IconLabel(Ui.Glyph.Attach, "Add a file…"), "Btn.Secondary", delegate { AddFiles(); });
		add.HorizontalAlignment = HorizontalAlignment.Left;
		add.Margin = new Thickness(0, 10, 0, 0);
		add.ToolTip = "Text files, Word documents, PDFs, saved web pages, and chat exports from ChatGPT, Claude.ai, SillyTavern or Aquila";
		body.Children.Add(add);
		StackPanel list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
		foreach (PrimeSource f in files.ToList())
		{
			PrimeSource file = f;
			DockPanel row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
			Button remove = Ui.Button("Remove", "Btn.Link", delegate
			{
				files.Remove(file);
				ShowAdd();
			});
			DockPanel.SetDock(remove, Dock.Right);
			row.Children.Add(remove);
			row.Children.Add(Ui.Text(file.Name + "  ·  " + Size(file.Text.Length), "Type.Body"));
			list.Children.Add(row);
		}
		body.Children.Add(list);

		// Who's who, when it's a conversation.
		speakers = Speakers();
		if (speakers.Count >= 2)
		{
			body.Children.Add(Ui.Section("Who's who"));
			body.Children.Add(Ui.Text("This looks like a conversation between " + string.Join(" and ", speakers.Take(2)) + (speakers.Count > 2 ? " (and others)" : "") + ". Tell Aquila which one is you.", "Type.Small"));
			youBox = new ComboBox { ItemsSource = speakers, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
			themBox = new ComboBox { ItemsSource = speakers, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
			(string you, string them) = Guess(speakers);
			youBox.SelectedItem = you;
			themBox.SelectedItem = them;
			youBox.SelectionChanged += delegate
			{
				if (Equals(youBox.SelectedItem, themBox.SelectedItem))
				{
					themBox.SelectedItem = speakers.FirstOrDefault((string s) => !Equals(s, youBox.SelectedItem));
				}
			};
			themBox.SelectionChanged += delegate
			{
				if (Equals(youBox.SelectedItem, themBox.SelectedItem))
				{
					youBox.SelectedItem = speakers.FirstOrDefault((string s) => !Equals(s, themBox.SelectedItem));
				}
			};
			Grid who = new Grid();
			who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			who.Children.Add(Ui.Field("Which one is you?", youBox));
			FrameworkElement theirs = Ui.Field(existing ? "Which one is " + Name + " (or who " + W("{s}") + " was before)?" : "Which one becomes your companion?", themBox);
			theirs.Margin = new Thickness(24, 0, 0, 0);
			Grid.SetColumn(theirs, 1);
			who.Children.Add(theirs);
			body.Children.Add(who);
		}
		else
		{
			youBox = null;
			themBox = null;
		}

		TextBlock how = Ui.Text("", "Type.Small");
		how.Margin = new Thickness(0, 18, 0, 0);
		how.TextWrapping = TextWrapping.Wrap;
		body.Children.Add(how);
		Border privacy = new Border { Background = Ui.Brush("Accent.Subtle"), BorderBrush = Ui.Brush("Accent.SubtleStroke"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 14, 0, 0) };
		privacy.Child = Ui.Text("Everything is read by your own chat model, on this computer. Nothing is sent anywhere, and nothing is saved until you've reviewed it.", "Type.Small");
		((TextBlock)privacy.Child).TextWrapping = TextWrapping.Wrap;
		body.Children.Add(privacy);

		Button cancel = Ui.Button("Cancel", "Btn.Secondary", delegate { Close(); });
		Button read = Ui.Button(Ui.IconLabel(Ui.Glyph.Forward, "Read it"), "Btn.Primary", delegate { _ = ReadAsync(); });
		read.IsDefault = true;
		string title = existing ? "Teach " + Name + " from a saved chat" : "Start from a saved chat or document";
		string description = existing
			? W("Paste a conversation or add files. The chat model reads them and suggests changes to {p} personality, {p} drives, and memories to add. You choose what to keep.")
			: "Paste a conversation you had with an AI somewhere else, or add files. The chat model reads them and drafts your companion's personality, drives and memories. You review and change everything before it's used.";
		base.Content = Ui.DialogScaffold(title, description, body, status, cancel, read);
		howText = how;
		readButton = read;
		Refresh();
	}

	private TextBlock? howText;

	private Button? readButton;

	private static string Size(int chars) => chars < 1000 ? chars + " characters" : (chars / 1000.0).ToString("0.#") + " thousand characters";

	private List<PrimeSource> Sources()
	{
		List<PrimeSource> all = files.ToList();
		string pasted = SourceText.Normalise(paste?.Text ?? "");
		if (pasted.Length > 0)
		{
			all.Insert(0, new PrimeSource("pasted text", pasted));
		}
		return all;
	}

	/// <summary>Speakers across everything added, most lines first.</summary>
	private List<string> Speakers()
	{
		return Sources().SelectMany((PrimeSource s) => SourceText.Speakers(s.Text)).GroupBy((string s) => s, StringComparer.OrdinalIgnoreCase).OrderByDescending((IGrouping<string, string> g) => g.Count()).Select((IGrouping<string, string> g) => g.First()).Take(6).ToList();
	}

	private static readonly string[] YouWords = { "you", "me", "user", "human", "i" };

	private (string You, string Them) Guess(List<string> found)
	{
		string? you = found.FirstOrDefault((string s) => kit != null && kit.UserName.Length > 0 && s.Equals(kit.UserName, StringComparison.OrdinalIgnoreCase))
			?? found.FirstOrDefault((string s) => YouWords.Contains(s.ToLowerInvariant()));
		string? them = found.FirstOrDefault((string s) => s.Equals(request.CompanionName.Trim(), StringComparison.OrdinalIgnoreCase) && s != you);
		you ??= found.FirstOrDefault((string s) => s != them) ?? found[0];
		them ??= found.FirstOrDefault((string s) => s != you) ?? found[^1];
		return (you, them);
	}

	private Primer MakePrimer()
	{
		int ctx = kit?.ContextTokens ?? 16384;
		return new Primer(kit?.Generator ?? new NoModel()) { MaxPartChars = Math.Clamp((ctx - 8000) * 3, 6000, Primer.DefaultMaxPartChars) };
	}

	private sealed class NoModel : ITextGenerator
	{
		public Task<string> GenerateJsonAsync(string system, string user, int maxTokens, CancellationToken ct) => throw new InvalidOperationException("Load the chat model first (Settings), then try again.");
	}

	private PrimeRequest Request()
	{
		PrimeRequest r = new PrimeRequest
		{
			Sources = Sources(),
			CompanionName = request.CompanionName,
			Gender = request.Gender,
			UserName = kit?.UserName ?? "",
			ExistingSheet = request.ExistingSheet,
			Notes = request.Notes,
			Drives = request.Drives,
			KnownMemories = request.KnownMemories
		};
		if (youBox?.SelectedItem is string you && themBox?.SelectedItem is string them && you != them)
		{
			r.UserSpeaker = you;
			r.CompanionSpeaker = them;
		}
		return r;
	}

	private void Refresh()
	{
		List<string> now = Speakers();
		bool changed = now.Count >= 2 ? (speakers.Count < 2 || !new HashSet<string>(now.Take(2), StringComparer.OrdinalIgnoreCase).SetEquals(speakers.Take(2))) : speakers.Count >= 2;
		if (changed && howText != null)
		{
			// The conversation's speakers changed (something new was pasted): rebuild so who's who matches.
			speakers = now;
			Dispatcher.BeginInvoke(new Action(delegate
			{
				ShowAdd();
				paste?.Focus();
				if (paste != null)
				{
					paste.CaretIndex = paste.Text.Length;
				}
			}));
			return;
		}
		if (howText == null || readButton == null)
		{
			return;
		}
		List<PrimeSource> all = Sources();
		int chars = all.Sum((PrimeSource s) => s.Text.Length);
		readButton.IsEnabled = chars > 0 && kit != null;
		if (kit == null)
		{
			howText.Text = "The chat model isn't loaded. Load it in Settings first, then come back.";
			howText.Foreground = Ui.Brush("Danger");
			return;
		}
		if (chars == 0)
		{
			howText.Text = "A long conversation (30 messages or more) gives the best result. Several files are fine.";
			return;
		}
		int parts = MakePrimer().PartsFor(Request());
		TimeSpan t = Primer.Estimate(parts, chars);
		int minutes = Math.Max(1, (int)Math.Round(t.TotalMinutes));
		howText.Text = Size(chars) + (parts > 1 ? ", read in " + parts + " parts" : "") + ". It takes about " + minutes + " minute" + (minutes == 1 ? "" : "s") + ".";
		howText.Foreground = Ui.Brush("Text.Secondary");
	}

	private void AddFiles()
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "Choose saved chats or documents",
			Multiselect = true,
			Filter = "Chats and documents|*.txt;*.md;*.markdown;*.log;*.html;*.htm;*.docx;*.rtf;*.pdf;*.json;*.jsonl|All files (*.*)|*.*"
		};
		if (dialog.ShowDialog(this) != true)
		{
			return;
		}
		foreach (string path in dialog.FileNames)
		{
			try
			{
				files.Add(new PrimeSource(Path.GetFileName(path), ReadFile(path)));
			}
			catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is System.Text.Json.JsonException || ex is InvalidOperationException || ex is KeyNotFoundException || ex is System.Xml.XmlException || ex is ArgumentException || ex is UglyToad.PdfPig.Core.PdfDocumentFormatException)
			{
				Say(Path.GetFileName(path) + ": " + ex.Message, bad: true);
			}
		}
		ShowAdd();
	}

	/// <summary>Chat exports (.json, .jsonl) become a plain "You: … / Them: …" conversation; other files are read as text.</summary>
	private string ReadFile(string path)
	{
		string ext = Path.GetExtension(path).ToLowerInvariant();
		if (ext == ".pdf")
		{
			using UglyToad.PdfPig.PdfDocument pdf = UglyToad.PdfPig.PdfDocument.Open(path);
			StringBuilder pages = new StringBuilder();
			foreach (UglyToad.PdfPig.Content.Page page in pdf.GetPages())
			{
				pages.AppendLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page));
				if (pages.Length > SourceText.MaxSourceChars)
				{
					throw new InvalidDataException("This PDF is too long to read at once. Split it into smaller files.");
				}
			}
			string text = SourceText.Normalise(pages.ToString());
			if (text.Length < pdf.NumberOfPages * 16)
			{
				throw new InvalidDataException("This PDF has almost no text in it (it may be scanned pictures of pages). Copy the text and paste it instead.");
			}
			return text;
		}
		if (ext != ".json" && ext != ".jsonl")
		{
			return SourceText.Read(path);
		}
		List<ImportedChat> chats = ChatImporter.Read(path);
		string them = request.CompanionName.Trim().Length > 0 ? "Them" : "AI";
		StringBuilder sb = new StringBuilder();
		foreach (ImportedChat chat in chats)
		{
			if (chats.Count > 1)
			{
				sb.Append("\n--- Conversation: ").Append(chat.Title).Append(" ---\n");
			}
			foreach (ImportedMessage m in chat.Messages.Where((ImportedMessage m) => m.Text.Trim().Length > 0))
			{
				sb.Append(m.FromUser ? "You" : them).Append(": ").Append(m.Text.Trim()).Append('\n');
			}
		}
		string joined = SourceText.Normalise(sb.ToString());
		if (joined.Length > SourceText.MaxSourceChars)
		{
			throw new InvalidDataException("This export holds " + chats.Count + " conversations, too much to read at once. Export or copy just the conversation you want.");
		}
		return joined;
	}

	// ---------------- 2. Reading ----------------

	private async Task ReadAsync()
	{
		if (kit == null)
		{
			Say("Load the chat model first (Settings), then try again.", bad: true);
			return;
		}
		PrimeRequest r = Request();
		if (r.Sources.Count == 0)
		{
			Say("Paste a conversation or add a file first.", bad: true);
			return;
		}
		Primer primer = MakePrimer();
		int parts = primer.PartsFor(r);
		cts = new CancellationTokenSource();
		StackPanel body = new StackPanel { Margin = new Thickness(0, 40, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
		TextBlock big = Ui.Text("Reading…", "Type.Title");
		big.HorizontalAlignment = HorizontalAlignment.Center;
		body.Children.Add(big);
		body.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 360, Height = 4, Margin = new Thickness(0, 16, 0, 16) });
		TextBlock note = Ui.Text("Your chat model is reading " + (parts > 1 ? "it in " + parts + " parts" : "it") + " and taking notes, then it writes the draft. You can leave this window open and do something else.", "Type.Small");
		note.TextWrapping = TextWrapping.Wrap;
		note.MaxWidth = 460;
		note.TextAlignment = TextAlignment.Center;
		body.Children.Add(note);
		Button stop = Ui.Button("Stop", "Btn.Secondary", delegate { cts?.Cancel(); });
		base.Content = Ui.DialogScaffold(existing ? "Teach " + Name + " from a saved chat" : "Start from a saved chat or document", "", body, status, stop);
		Progress<string> progress = new Progress<string>((string s) =>
		{
			big.Text = s;
			Say(s);
		});
		try
		{
			proposal = await primer.RunAsync(r, progress, cts.Token);
			lastRequest = r;
			Say("");
			ShowReview();
		}
		catch (OperationCanceledException)
		{
			Say("Stopped. Nothing was saved.");
			ShowAdd();
		}
		catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is InvalidOperationException || ex is InvalidDataException || ex is System.Text.Json.JsonException)
		{
			Say(ex.Message, bad: true);
			ShowAdd();
		}
		finally
		{
			cts = null;
		}
	}

	private PrimeRequest? lastRequest;

	// ---------------- 3. Review ----------------

	private sealed class MemoryRow
	{
		public MemorySuggestion Memory = new MemorySuggestion();
		public bool? Keep;
		public TextBox Box = new TextBox();
		public Border Card = new Border();
		public Button KeepButton = new Button();
		public Button SkipButton = new Button();
	}

	private readonly List<MemoryRow> memoryRows = new List<MemoryRow>();

	private readonly List<(DriveSuggestion Drive, CheckBox Use)> driveRows = new List<(DriveSuggestion, CheckBox)>();

	private TextBlock? memoryCount;

	private void ShowReview()
	{
		PrimeProposal p = proposal!;
		memoryCount = null;
		StackPanel body = new StackPanel();
		TextBlock intro = Ui.Text("Here's what the chat model drafted from " + (p.PartsRead > 1 ? p.PartsRead + " parts" : "it") + ". Change anything you like. Only what you keep is used.", "Type.Small");
		intro.TextWrapping = TextWrapping.Wrap;
		body.Children.Add(intro);

		// Details (creating only).
		TextBox? nameBox = null, ageBox = null, jobBox = null, relBox = null, tagBox = null;
		if (!existing)
		{
			body.Children.Add(Ui.Section("Who they are"));
			nameBox = Box(p.Name.Length > 0 ? p.Name : request.CompanionName, 40);
			ageBox = Box(p.Age > 0 ? p.Age.ToString() : "", 3);
			ageBox.Width = 80;
			jobBox = Box(p.Occupation, CompanionDraft.MaxShortChars * 2);
			relBox = Box(p.Relationship, CompanionDraft.MaxShortChars * 2);
			tagBox = Box(p.Tagline, PersonaConfig.MaxTaglineChars);
			Grid row = new Grid();
			row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			row.Children.Add(Ui.Field("Name", nameBox, "", 6));
			FrameworkElement age = Ui.Field("Age (optional)", ageBox, "", 6);
			age.Margin = new Thickness(12, 0, 0, 0);
			Grid.SetColumn(age, 1);
			row.Children.Add(age);
			body.Children.Add(row);
			body.Children.Add(Ui.Field("Occupation (optional)", jobBox));
			body.Children.Add(Ui.Field("How they know you (optional)", relBox));
			body.Children.Add(Ui.Field("Card description (optional)", tagBox));
		}

		// Personality.
		body.Children.Add(Ui.Section("Personality"));
		TextBox sheet = new TextBox { Text = p.Personality, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 240, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = CompanionDraft.MaxPersonalityChars };
		CheckBox useSheet = new CheckBox { Content = existing ? W("Replace {p} personality sheet with this one") : "Use this personality", IsChecked = p.Personality.Trim().Length > 0, Margin = new Thickness(0, 0, 0, 8) };
		if (existing)
		{
			TextBlock was = Ui.Text(W("It was written to keep what {p} current sheet says, unless the conversation shows otherwise. Untick to keep {p} sheet as it is."), "Type.Small");
			was.TextWrapping = TextWrapping.Wrap;
			was.Margin = new Thickness(0, 0, 0, 6);
			body.Children.Add(was);
		}
		body.Children.Add(useSheet);
		body.Children.Add(sheet);

		// Drives.
		driveRows.Clear();
		if (p.Drives.Count > 0)
		{
			body.Children.Add(Ui.Section("Drives"));
			TextBlock dh = Ui.Text(existing ? W("Where each drive should be now, and how much it matters to {o}, judged from how {s} acts in the conversation.") : "Where each drive starts, and how much it matters, judged from how they act in the conversation.", "Type.Small");
			dh.TextWrapping = TextWrapping.Wrap;
			body.Children.Add(dh);
			foreach (DriveSuggestion d in p.Drives)
			{
				DriveSpec? was = request.Drives.FirstOrDefault((DriveSpec x) => x.Name == d.Name);
				StackPanel line = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
				string what = d.Remove
					? "Remove the \u201C" + d.Name + "\u201D drive"
					: was != null
					? d.Name + ":  " + (existing ? "set to " : "starts at ") + d.Level.ToString("0.0") + (was.Initial is double i && !existing ? " (was " + i.ToString("0.0") + ")" : "") + ",  weight " + d.Weight.ToString("0.0") + " (was " + (was.Weight ?? 1).ToString("0.0") + ")"
					: "New drive “" + d.Name + "”" + (d.New!.Description.Length > 0 ? " (" + d.New.Description.TrimEnd('.') + ")" : "") + ":  starts at " + d.Level.ToString("0.0") + ",  weight " + d.Weight.ToString("0.0");
				CheckBox useDrive = new CheckBox { Content = what, IsChecked = was != null && !d.Remove, FontWeight = FontWeights.SemiBold };
				line.Children.Add(useDrive);
				if (d.Why.Length > 0)
				{
					TextBlock why = Ui.Text(d.Why, "Type.Small");
					why.Margin = new Thickness(24, 2, 0, 0);
					why.TextWrapping = TextWrapping.Wrap;
					line.Children.Add(why);
				}
				if (d.Remove)
				{
					TextBlock extra = Ui.Text(W("Takes this drive away from {o}. Its history stays in the log. Leave it unticked to keep it."), "Type.Small");
					extra.Margin = new Thickness(24, 2, 0, 0);
					extra.TextWrapping = TextWrapping.Wrap;
					extra.Foreground = Ui.Brush("Text.Tertiary");
					line.Children.Add(extra);
				}
				else if (was == null)
				{
					TextBlock extra = Ui.Text("Adds a new drive (you can rename or remove it later in settings). Low: " + d.New!.Guidance.Low + "  Middle: " + d.New.Guidance.Mid + "  High: " + d.New.Guidance.High, "Type.Small");
					extra.Margin = new Thickness(24, 2, 0, 0);
					extra.TextWrapping = TextWrapping.Wrap;
					extra.Foreground = Ui.Brush("Text.Tertiary");
					line.Children.Add(extra);
				}
				body.Children.Add(line);
				driveRows.Add((d, useDrive));
			}
		}

		// Memories, kept one by one.
		memoryRows.Clear();
		body.Children.Add(Ui.Section("Memories"));
		if (p.Memories.Count == 0)
		{
			body.Children.Add(Ui.Text("No memories were found worth keeping.", "Type.Small"));
		}
		else
		{
			TextBlock mh = Ui.Text(W("Things {s} would remember for a long time. Press Keep on each one you want " + (existing ? "added to {p} long-term memory" : "{o} to start with") + ", and change the words if needed. Skipped ones are left out."), "Type.Small");
			mh.TextWrapping = TextWrapping.Wrap;
			body.Children.Add(mh);
			WrapPanel all = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
			Button keepAll = Ui.Button("Keep all", "Btn.Secondary", delegate
			{
				foreach (MemoryRow m in memoryRows) { m.Keep = true; Paint(m); }
				Count();
			});
			Button skipAll = Ui.Button("Skip all", "Btn.Ghost", delegate
			{
				foreach (MemoryRow m in memoryRows) { m.Keep = false; Paint(m); }
				Count();
			});
			skipAll.Margin = new Thickness(8, 0, 0, 0);
			all.Children.Add(keepAll);
			all.Children.Add(skipAll);
			memoryCount = Ui.Text("", "Type.Small");
			memoryCount.Margin = new Thickness(16, 0, 0, 0);
			memoryCount.VerticalAlignment = VerticalAlignment.Center;
			all.Children.Add(memoryCount);
			body.Children.Add(all);
			int n = 0;
			foreach (MemorySuggestion m in p.Memories)
			{
				n++;
				body.Children.Add(MemoryCard(m, n, p.Memories.Count));
			}
		}

		// Word for word.
		List<(string User, string Reply)> pairs = Pairs();
		CheckBox recall = new CheckBox { IsChecked = pairs.Count > 0, Margin = new Thickness(0, 4, 0, 0) };
		if (pairs.Count > 0)
		{
			body.Children.Add(Ui.Section("The conversation itself"));
			recall.Content = W("Also let {o} remember the conversation word for word (" + pairs.Count + " moments)");
			body.Children.Add(recall);
			TextBlock rh = Ui.Text(W("{S} won't reread it all the time. When something you say reminds {o} of a moment from it, that moment comes back to {o}, as part of your past together."), "Type.Small");
			rh.TextWrapping = TextWrapping.Wrap;
			rh.Margin = new Thickness(24, 2, 0, 0);
			body.Children.Add(rh);
		}

		Button again = Ui.Button("Start over", "Btn.Ghost", delegate { proposal = null; Say(""); ShowAdd(); });
		Button cancel = Ui.Button("Cancel", "Btn.Secondary", delegate { Close(); });
		Button use = Ui.Button(existing ? "Save to " + Name : "Use this", "Btn.Primary", delegate
		{
			int undecided = memoryRows.Count((MemoryRow m) => m.Keep == null);
			if (undecided > 0 && !AquilaDialog.Confirm(this, undecided + " memor" + (undecided == 1 ? "y" : "ies") + " not decided", "You haven't pressed Keep or Skip on " + undecided + " of the memories. They'll be left out. Go ahead?", "Leave them out", "Go back"))
			{
				return;
			}
			PrimeChoice c = new PrimeChoice
			{
				Source = string.Join(", ", (lastRequest?.Sources ?? new List<PrimeSource>()).Select((PrimeSource s) => s.Name)),
				Personality = useSheet.IsChecked == true ? sheet.Text.Trim() : "",
				Drives = driveRows.Where(((DriveSuggestion Drive, CheckBox Use) d) => d.Use.IsChecked == true).Select(((DriveSuggestion Drive, CheckBox Use) d) => d.Drive).ToList(),
				Memories = memoryRows.Where((MemoryRow m) => m.Keep == true && m.Box.Text.Trim().Length > 0).Select((MemoryRow m) => new MemorySuggestion { Layer = m.Memory.Layer, Text = m.Box.Text.Trim(), Why = m.Memory.Why }).ToList(),
				Recall = recall.IsChecked == true ? pairs : new List<(string, string)>()
			};
			if (!existing)
			{
				c.Name = nameBox!.Text.Trim();
				c.Age = int.TryParse(ageBox!.Text.Trim(), out int a) ? a : 0;
				if (ageBox.Text.Trim().Length > 0 && (c.Age < PersonaConfig.MinAge || c.Age > PersonaConfig.MaxAge))
				{
					Say("Age must be " + PersonaConfig.MinAge + " or older (companions are always adults), or left empty.", bad: true);
					return;
				}
				c.Occupation = jobBox!.Text.Trim();
				c.Relationship = relBox!.Text.Trim();
				c.Tagline = tagBox!.Text.Trim();
			}
			if (existing && c.Personality.Length == 0 && c.Drives.Count == 0 && c.Memories.Count == 0 && c.Recall.Count == 0)
			{
				Say("Nothing is chosen to save yet.", bad: true);
				return;
			}
			if (existing && !AquilaDialog.Confirm(this, "Save to " + Name + "?", Summary(c) + W(" Memories can be withdrawn later in {p} settings, but they stay in {p} history, like everything {s} remembers."), "Save"))
			{
				return;
			}
			Result = c;
			DialogResult = true;
		});
		use.IsDefault = false;
		base.Content = Ui.DialogScaffold(existing ? "Review what " + Name + " learned" : "Review your companion", "", body, status, again, cancel, use);
		Count();
	}

	private string Summary(PrimeChoice c)
	{
		List<string> parts = new List<string>();
		if (c.Personality.Length > 0)
		{
			parts.Add(W("replaces {p} personality sheet"));
		}
		if (c.Drives.Count > 0)
		{
			parts.Add("changes " + c.Drives.Count + " drive" + (c.Drives.Count == 1 ? "" : "s"));
		}
		if (c.Memories.Count > 0)
		{
			parts.Add("adds " + c.Memories.Count + " memor" + (c.Memories.Count == 1 ? "y" : "ies"));
		}
		if (c.Recall.Count > 0)
		{
			parts.Add("remembers the conversation word for word");
		}
		string s = "This " + string.Join(", ", parts) + ".";
		return s;
	}

	private static TextBox Box(string text, int max) => new TextBox { Text = text, MaxLength = max };

	private static string LayerLabel(string layer) => layer switch
	{
		"feelings" => "Feeling",
		"hooks" => "Open thread",
		_ => "Fact"
	};

	private FrameworkElement MemoryCard(MemorySuggestion m, int n, int of)
	{
		MemoryRow row = new MemoryRow { Memory = m };
		row.Box = new TextBox { Text = m.Text, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, MaxLength = 400 };
		row.Box.TextChanged += delegate { Count(); };
		DockPanel head = new DockPanel();
		row.KeepButton = Ui.Button(Ui.IconLabel(Ui.Glyph.Accept, "Keep"), "Btn.Secondary", delegate
		{
			row.Keep = true;
			Paint(row);
			Count();
		});
		row.SkipButton = Ui.Button("Skip", "Btn.Ghost", delegate
		{
			row.Keep = false;
			Paint(row);
			Count();
		});
		row.SkipButton.Margin = new Thickness(6, 0, 0, 0);
		StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { row.KeepButton, row.SkipButton } };
		DockPanel.SetDock(buttons, Dock.Right);
		head.Children.Add(buttons);
		head.Children.Add(Ui.Text(n + " of " + of + "  ·  " + LayerLabel(m.Layer) + (m.Why.Length > 0 ? "  ·  " + m.Why : ""), "Type.Small"));
		StackPanel inner = new StackPanel();
		inner.Children.Add(head);
		row.Box.Margin = new Thickness(0, 6, 0, 0);
		inner.Children.Add(row.Box);
		row.Card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 10), Margin = new Thickness(0, 8, 0, 0), Child = inner };
		memoryRows.Add(row);
		Paint(row);
		return row.Card;
	}

	private static void Paint(MemoryRow row)
	{
		row.Card.BorderBrush = Ui.Brush(row.Keep == true ? "Accent" : "Stroke.Subtle");
		row.Card.Background = Ui.Brush(row.Keep == true ? "Accent.Subtle" : "Surface.1");
		row.Card.Opacity = row.Keep == false ? 0.5 : 1.0;
		row.KeepButton.Style = Ui.Style(row.Keep == true ? "Btn.Primary" : "Btn.Secondary");
	}

	private void Count()
	{
		if (memoryCount == null)
		{
			return;
		}
		List<MemoryRow> kept = memoryRows.Where((MemoryRow m) => m.Keep == true).ToList();
		int tokens = kept.Sum((MemoryRow m) => TokenEstimate.Of("- " + m.Box.Text.Trim() + "\n"));
		int undecided = memoryRows.Count((MemoryRow m) => m.Keep == null);
		int total = memoryUsed + tokens;
		memoryCount.Text = "Kept " + kept.Count + " of " + memoryRows.Count + (undecided > 0 ? " (" + undecided + " to decide)" : "") + "  ·  memory used: about " + total.ToString("N0") + " of " + memoryCap.ToString("N0");
		memoryCount.Foreground = Ui.Brush(total > memoryCap ? "Danger" : "Text.Secondary");
		memoryCount.ToolTip = total > memoryCap ? W("That's more than {p} pinned memory shows at once. Everything is still saved, but the oldest entries drop out of view first. Keep fewer, or keep them short.") : null;
	}

	/// <summary>The conversation as you-then-reply pairs, when you said who's who.</summary>
	private List<(string User, string Reply)> Pairs()
	{
		if (lastRequest == null || lastRequest.UserSpeaker.Length == 0 || lastRequest.CompanionSpeaker.Length == 0)
		{
			return new List<(string, string)>();
		}
		string[] two = { lastRequest.UserSpeaker, lastRequest.CompanionSpeaker };
		return lastRequest.Sources.SelectMany((PrimeSource s) => SourceText.Pairs(SourceText.Lines(s.Text, SourceText.Speakers(s.Text).Union(two, StringComparer.OrdinalIgnoreCase).ToList()), two[0], two[1])).ToList();
	}

	protected override void OnClosed(EventArgs e)
	{
		cts?.Cancel();
		base.OnClosed(e);
	}
}
