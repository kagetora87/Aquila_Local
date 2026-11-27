using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Pictures;

namespace AquilaLocal;

/// <summary>
/// Create Companion, in six steps: male or female, ethnicity, hair, body type, who they are and how they look
/// (with their drives), then a summary where you make their portrait and bring them to life.
/// </summary>
public partial class MainWindow
{
	private CompanionDraft draft = new CompanionDraft();

	private int createStep = 1;

	/// <summary>Holds the draft's drives for the drive editor on step 5.</summary>
	private PersonaConfig? draftDrives;

	private DriveEditor? draftDriveEditor;

	private string draftPortrait = "";

	private string draftPortraitExtra = "";

	private string draftModel = "";

	private string createStatusText = "";

	private bool createStatusBad;

	private TextBlock? createStatus;

	private Grid? createBody;

	/// <summary>The companion just brought to life ("" while creating), so the page can say they're ready.</summary>
	private string createdId = "";

	private static readonly string[] StepNames = { "Gender", "Ethnicity", "Hair", "Body", "Details", "Summary" };

	private FrameworkElement CreatePage()
	{
		if (draftModel.Length == 0)
		{
			draftModel = PictureModels.IsKnown(settings.Pictures.Model) ? settings.Pictures.Model : PictureModels.Fast;
		}
		DockPanel page = new DockPanel { Margin = new Thickness(36, 28, 36, 0) };

		// Title and the six steps.
		StackPanel head = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
		head.Children.Add(Ui.Text("Create a companion", "Type.Display"));
		if (createdId.Length == 0)
		{
			head.Children.Add(StepBar());
		}
		DockPanel.SetDock(head, Dock.Top);
		page.Children.Add(head);

		if (createdId.Length > 0)
		{
			page.Children.Add(CreatedPanel());
			return page;
		}

		// Back / Next.
		Grid nav = new Grid { Margin = new Thickness(0, 12, 0, 22) };
		createStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(110, 0, 140, 0), FontSize = 13 };
		nav.Children.Add(createStatus);
		Button back = Ui.Button(Ui.IconLabel("", "Back"), "Btn.Secondary", delegate { GoStep(createStep - 1); });
		back.HorizontalAlignment = HorizontalAlignment.Left;
		back.Visibility = createStep > 1 ? Visibility.Visible : Visibility.Hidden;
		nav.Children.Add(back);
		if (createStep < CompanionDraft.Steps)
		{
			Button next = Ui.Button(new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = "Next", VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = "", FontFamily = Ui.FontIcon, FontSize = 14, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center } } }, "Btn.Primary", delegate { GoStep(createStep + 1); });
			next.HorizontalAlignment = HorizontalAlignment.Right;
			nav.Children.Add(next);
		}
		DockPanel.SetDock(nav, Dock.Bottom);
		page.Children.Add(nav);

		createBody = new Grid();
		createBody.Children.Add(StepContent());
		page.Children.Add(createBody);
		ShowCreateStatus(createStatusText, createStatusBad);
		return page;
	}

	private FrameworkElement StepBar()
	{
		Grid bar = new Grid { Margin = new Thickness(0, 14, 0, 0), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
		for (int i = 0; i < StepNames.Length; i++)
		{
			bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			int step = i + 1;
			bool done = step < createStep;
			bool now = step == createStep;
			StackPanel cell = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
			cell.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Brush(done || now ? "Accent" : "Stroke.Default"), Opacity = now ? 1.0 : (done ? 0.6 : 1.0) });
			TextBlock label = new TextBlock { Text = step + ". " + StepNames[i], FontSize = 12, Margin = new Thickness(0, 6, 0, 0), Foreground = Ui.Brush(now ? "Text.Primary" : "Text.Tertiary"), FontWeight = now ? FontWeights.SemiBold : FontWeights.Normal };
			cell.Children.Add(label);
			Button jump = new Button { Style = Ui.Style("Btn.Ghost"), Content = cell, Padding = new Thickness(0), MinHeight = 0, HorizontalContentAlignment = HorizontalAlignment.Stretch, IsEnabled = step <= createStep || draft.MissingAll().Count == 0 || Enumerable.Range(1, step - 1).All((int s) => draft.Missing(s).Count == 0), ToolTip = "Go to " + StepNames[i] };
			jump.Click += delegate { GoStep(step); };
			Grid.SetColumn(jump, i);
			bar.Children.Add(jump);
		}
		return bar;
	}

	private void ShowCreateStatus(string text, bool bad = false)
	{
		createStatusText = text;
		createStatusBad = bad;
		if (createStatus != null)
		{
			createStatus.Text = text;
			createStatus.Foreground = Ui.Brush(bad ? "Danger" : "Text.Secondary");
		}
	}

	/// <summary>Moving forward needs the steps before it done; moving back is always fine.</summary>
	private void GoStep(int step)
	{
		step = Math.Clamp(step, 1, CompanionDraft.Steps);
		if (createStep == 5 && !TakeDrives())
		{
			return;
		}
		if (step > createStep)
		{
			for (int s = createStep; s < step; s++)
			{
				List<string> missing = draft.Missing(s);
				if (missing.Count > 0)
				{
					createStep = s;
					ShowCreateStatus(string.Join(" ", missing), bad: true);
					ShowScreen(Screens.Create);
					return;
				}
			}
		}
		createStep = step;
		ShowCreateStatus("");
		ShowScreen(Screens.Create);
	}

	/// <summary>Takes the drives from the drive editor on step 5; false (with the reason shown) when one isn't right.</summary>
	private bool TakeDrives()
	{
		if (draftDriveEditor == null || draftDrives == null)
		{
			return true;
		}
		try
		{
			draftDriveEditor.ApplyTo(draftDrives);
			draftDrives.ValidateShape();
			draft.Drives = draftDrives.Drives;
			return true;
		}
		catch (Exception ex) when (ex is FormatException || ex is PersonaConfigException)
		{
			ShowCreateStatus("Drives: " + ex.Message, bad: true);
			return false;
		}
	}

	private FrameworkElement StepContent()
	{
		switch (createStep)
		{
			case 1:
				return ChoiceStep(CreatorKinds.Gender, "Is your companion a man or a woman?", "This sets the words Aquila uses for them (he or she) and the choices in the next steps.");
			case 2:
				return ChoiceStep(CreatorKinds.Ethnicity, "Ethnicity", "Choose the look that fits the companion you have in mind.");
			case 3:
				return ChoiceStep(CreatorKinds.Hair, "Hair style", "Then pick a colour below.");
			case 4:
				return ChoiceStep(CreatorKinds.Body, "Body type", "");
			case 5:
				return DetailsStep();
			default:
				return SummaryStep();
		}
	}

	// ---------------- Steps 1 to 4: pick a picture ----------------

	private FrameworkElement ChoiceStep(string kind, string title, string hint)
	{
		string gender = draft.Gender.Length > 0 ? draft.Gender : Genders.Female;
		StackPanel body = new StackPanel();
		body.Children.Add(Ui.Text(title, "Type.Title"));
		if (hint.Length > 0)
		{
			TextBlock h = Ui.Text(hint, "Type.Small");
			h.Margin = new Thickness(0, 4, 0, 0);
			body.Children.Add(h);
		}
		List<TileJob> missing = CreatorOptions.TileJobs().Where((TileJob j) => !File.Exists(Presets.StyledTilePath(j.Id))).ToList();
		if (missing.Count > 0 || makingTiles)
		{
			body.Children.Add(TileBanner(missing));
		}
		WrapPanel tiles = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
		CreatorOption? chosen = draft.Option(kind);
		foreach (CreatorOption o in CreatorOptions.For(kind, gender))
		{
			CreatorOption option = o;
			bool on = chosen?.Key == o.Key;
			bool big = kind == CreatorKinds.Gender;
			Button tile = ChoiceTile(Presets.StyledTilePath(CreatorOptions.TileId(o, gender)), o.Label, on, big ? 220 : 150, big ? 280 : 190);
			tile.Click += delegate
			{
				draft.Choose(kind, option.Key);
				ShowCreateStatus("");
				// A picture pick moves on by itself, except hair, which still needs a colour.
				if (kind == CreatorKinds.Hair)
				{
					ShowScreen(Screens.Create);
				}
				else
				{
					GoStep(createStep + 1);
				}
			};
			tiles.Children.Add(tile);
		}
		body.Children.Add(tiles);
		if (kind == CreatorKinds.Hair)
		{
			body.Children.Add(Ui.Section("Hair colour"));
			body.Children.Add(Chips(CreatorOptions.HairColours, draft.HairColour, delegate(string c)
			{
				draft.HairColour = c;
				ShowCreateStatus("");
				ShowScreen(Screens.Create);
			}));
		}
		return new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
	}

	private FrameworkElement TileBanner(List<TileJob> missing)
	{
		Border banner = new Border { Background = Ui.Brush("Accent.Subtle"), BorderBrush = Ui.Brush("Accent.SubtleStroke"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 14, 0, 0), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
		StackPanel b = new StackPanel();
		if (makingTiles)
		{
			b.Children.Add(Ui.Text("Making the pictures for these choices… They appear as they're done.", "Type.Small"));
			Button stop = Ui.Button("Stop", "Btn.Secondary", delegate { genCts?.Cancel(); });
			stop.HorizontalAlignment = HorizontalAlignment.Left;
			stop.Margin = new Thickness(0, 8, 0, 0);
			b.Children.Add(stop);
		}
		else
		{
			int minutes = Math.Max(1, (int)Math.Ceiling(missing.Count * 12 / 60.0));
			b.Children.Add(Ui.Text(missing.Count + " of the pictures for these choices haven't been made yet. Your picture engine makes them once, on this computer (about " + minutes + " minute" + (minutes == 1 ? "" : "s") + "). You can choose without them.", "Type.Small"));
			Button make = Ui.Button(Ui.IconLabel("", "Make the pictures"), "Btn.Primary", delegate
			{
				_ = RunTilesAsync(missing, delegate
				{
					if (screen == Screens.Create && createStep <= 4)
					{
						ShowScreen(Screens.Create);
					}
				}, ShowCreateStatus);
			});
			make.HorizontalAlignment = HorizontalAlignment.Left;
			make.Margin = new Thickness(0, 8, 0, 0);
			make.IsEnabled = !generating && !busy;
			b.Children.Add(make);
		}
		banner.Child = b;
		return banner;
	}

	private Button ChoiceTile(string picturePath, string label, bool on, double width, double height)
	{
		Grid face = new Grid();
		ImageSource? picture = TilePicture(picturePath);
		if (picture != null)
		{
			face.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = new ImageBrush(picture) { Stretch = Stretch.UniformToFill } });
		}
		else
		{
			face.Children.Add(Ui.Icon("", 34, "Text.Tertiary"));
		}
		if (on)
		{
			face.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.Brush("Accent"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0), Child = Ui.Icon("", 13, "Accent.On") });
		}
		Border frame = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(on ? 3 : 1), BorderBrush = Ui.Brush(on ? "Accent" : "Stroke.Subtle"), Background = Ui.Brush("Surface.2"), Child = face, ClipToBounds = true };
		StackPanel content = new StackPanel();
		content.Children.Add(frame);
		content.Children.Add(new TextBlock { Text = label, Width = width, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0), FontSize = 14, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Ui.Brush(on ? "Text.Primary" : "Text.Secondary") });
		Button b = new Button { Style = Ui.Style("Btn.Ghost"), Content = content, Padding = new Thickness(0), Margin = new Thickness(0, 0, 14, 16), MinHeight = 0 };
		Ui.SetRadius(b, new CornerRadius(12));
		return b;
	}

	/// <summary>A row of round choices (hair colour, eye colour); the chosen one is filled.</summary>
	private static WrapPanel Chips(IEnumerable<string> choices, string chosen, Action<string> pick)
	{
		WrapPanel row = new WrapPanel();
		foreach (string c in choices)
		{
			string choice = c;
			bool on = string.Equals(c, chosen, StringComparison.OrdinalIgnoreCase);
			Button chip = new Button
			{
				Style = Ui.Style("Btn.Chip"),
				Content = c,
				Margin = new Thickness(0, 0, 8, 8),
				MinHeight = 32,
				Height = 32,
				Background = on ? Ui.Brush("Accent") : Ui.Brush("Surface.2"),
				Foreground = on ? Ui.Brush("Accent.On") : Ui.Brush("Text.Primary"),
				BorderBrush = on ? Ui.Brush("Accent") : Ui.Brush("Stroke.Default"),
				FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal
			};
			chip.Click += delegate { pick(choice); };
			row.Children.Add(chip);
		}
		return row;
	}

	// ---------------- Step 5: who they are and how they look ----------------

	private FrameworkElement DetailsStep()
	{
		string they = Genders.Subject(draft.Gender);
		string their = Genders.Possessive(draft.Gender);
		Grid columns = new Grid();
		columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
		columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
		columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

		// Left: who they are.
		StackPanel who = new StackPanel();
		who.Children.Add(Ui.Text("Who " + they + " is", "Type.Title"));
		TextBox name = Box(draft.Name, 40, "For example: " + (Genders.IsMale(draft.Gender) ? "Marcus" : "Elena"), (string v) => draft.Name = v);
		TextBox age = Box(draft.Age.ToString(CultureInfo.InvariantCulture), 3, "", delegate(string v)
		{
			draft.Age = int.TryParse(v.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;
		});
		age.Width = 80;
		Grid nameAge = new Grid();
		nameAge.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		nameAge.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		nameAge.Children.Add(Ui.Field("Name", name, "", 12));
		FrameworkElement ageField = Ui.Field("Age", age, "", 12);
		ageField.Margin = new Thickness(12, 0, 0, 0);
		Grid.SetColumn(ageField, 1);
		nameAge.Children.Add(ageField);
		who.Children.Add(nameAge);
		who.Children.Add(Ui.Field("Occupation (optional)", Box(draft.Occupation, CompanionDraft.MaxShortChars * 2, "For example: a nurse, a jazz pianist, a student", (string v) => draft.Occupation = v)));
		who.Children.Add(Ui.Field("How " + they + " knows you (optional)", Box(draft.Relationship, CompanionDraft.MaxShortChars * 2, "For example: your neighbour, an old friend, your partner of two years", (string v) => draft.Relationship = v)));
		TextBox personality = new TextBox { Text = draft.Personality, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 180, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = CompanionDraft.MaxPersonalityChars };
		Ui.SetPlaceholder(personality, "Personality, how " + they + " talks, what " + they + " loves, " + their + " history… A few lines is enough; more makes " + Genders.Object(draft.Gender) + " richer.");
		personality.TextChanged += delegate { draft.Personality = personality.Text; };
		who.Children.Add(Ui.Field("Personality", personality, "", 12));
		Button help = Ui.Button(Ui.IconLabel("", "Help me write it"), "Btn.Secondary", null);
		help.HorizontalAlignment = HorizontalAlignment.Left;
		help.Margin = new Thickness(0, 8, 0, 0);
		help.ToolTip = "The chat model turns your notes into a fuller personality. Your notes are kept in it.";
		string before = "";
		Button undo = Ui.Button("Undo", "Btn.Ghost", delegate
		{
			personality.Text = before;
		});
		undo.Visibility = Visibility.Collapsed;
		undo.Margin = new Thickness(8, 8, 0, 0);
		help.Click += async delegate
		{
			before = personality.Text;
			help.IsEnabled = false;
			ShowCreateStatus("Writing " + their + " personality…");
			try
			{
				personality.Text = await WriteSheetAsync(draft, CancellationToken.None);
				undo.Visibility = Visibility.Visible;
				ShowCreateStatus("Done. Change anything you like.");
			}
			catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is InvalidOperationException || ex is PersonaConfigException || ex is TaskCanceledException)
			{
				ShowCreateStatus(ex.Message, bad: true);
			}
			finally
			{
				help.IsEnabled = true;
			}
		};
		who.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { help, undo } });
		TextBox tagline = Box(draft.Tagline, PersonaConfig.MaxTaglineChars, "A line or two for " + their + " card in My Companions", (string v) => draft.Tagline = v);
		who.Children.Add(Ui.Field("Card description (optional)", tagline));
		columns.Children.Add(who);

		// Right: how they look.
		StackPanel looks = new StackPanel();
		looks.Children.Add(Ui.Text("How " + they + " looks", "Type.Title"));
		ComboBox style = new ComboBox { ItemsSource = CreatorOptions.ArtStyles.Select(((string Label, string Text) a) => a.Label).ToList(), Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
		style.SelectedItem = draft.ArtStyle;
		style.SelectionChanged += delegate { draft.ArtStyle = (string)style.SelectedItem; };
		looks.Children.Add(Ui.Field("Picture style", style, "", 12));
		looks.Children.Add(Ui.Field("Eye colour", Chips(CreatorOptions.EyeColours, draft.EyeColour, delegate(string c)
		{
			draft.EyeColour = c;
			ShowCreateStatus("");
			RefillStep();
		}), "", 12));
		looks.Children.Add(Ui.Field("Distinctive feature", Box(draft.Feature, CompanionDraft.MaxShortChars * 2, "One small thing always visible: a beauty mark, freckles, a scar, glasses…", (string v) => draft.Feature = v), "Every picture of " + Genders.Object(draft.Gender) + " keeps this, so " + they + " always looks like " + Genders.Object(draft.Gender) + "self."));
		looks.Children.Add(Ui.Field("What " + they + "'s wearing at the start", Box(draft.Outfit, CompanionDraft.MaxShortChars * 2, draft.DefaultOutfit, (string v) => draft.Outfit = v), "Leave empty for " + draft.DefaultOutfit + ". " + Genders.Subject(draft.Gender).ToUpperInvariant()[0] + Genders.Subject(draft.Gender).Substring(1) + " can change clothes later."));
		TextBox extra = new TextBox { Text = draft.Looks, AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, Height = 64, VerticalContentAlignment = VerticalAlignment.Top, MaxLength = AppearanceSeed.MaxTraitChars };
		Ui.SetPlaceholder(extra, "Anything else about " + their + " looks: height, face shape, tattoos…");
		extra.TextChanged += delegate { draft.Looks = extra.Text; };
		looks.Children.Add(Ui.Field("More about " + their + " looks (optional)", extra));
		Grid.SetColumn(looks, 2);
		columns.Children.Add(looks);

		// Below: drives.
		StackPanel all = new StackPanel();
		all.Children.Add(columns);
		all.Children.Add(Ui.Section("Drives", 28));
		TextBlock drivesHint = Ui.Text("What moves " + Genders.Object(draft.Gender) + ". Every companion starts with these five; change the numbers, rename them, or add your own, just like in Companions settings.", "Type.Small");
		drivesHint.Margin = new Thickness(0, 0, 0, 8);
		all.Children.Add(drivesHint);
		draftDrives = new PersonaConfig { Id = "draft", Name = draft.Name.Trim().Length > 0 ? draft.Name.Trim() : "Your companion", Drives = draft.Drives };
		draftDriveEditor = new DriveEditor(draftDrives, false);
		all.Children.Add(draftDriveEditor.View);
		return new ScrollViewer { Content = new Border { Child = all, Padding = new Thickness(0, 0, 16, 16) }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
	}

	/// <summary>Rebuilds the step in place, keeping the drive edits made so far.</summary>
	private void RefillStep()
	{
		if (createStep == 5)
		{
			TakeDrives();
		}
		ShowScreen(Screens.Create);
	}

	private static TextBox Box(string text, int max, string placeholder, Action<string> changed)
	{
		TextBox box = new TextBox { Text = text, MaxLength = max };
		if (placeholder.Length > 0)
		{
			Ui.SetPlaceholder(box, placeholder);
		}
		box.TextChanged += delegate { changed(box.Text); };
		return box;
	}

	/// <summary>The chat model writes a fuller personality from your notes (on its second slot, so chats keep their cache).</summary>
	private async Task<string> WriteSheetAsync(CompanionDraft d, CancellationToken ct)
	{
		if (!engine.Ready)
		{
			throw new InvalidOperationException("Load the chat model first (Settings), then try again.");
		}
		string they = Genders.Subject(d.Gender);
		string system = "You write personality sheets for companion characters in a private chat app. Write in the second person (\"You are…\"), 180 to 320 words, plain prose in two to four short paragraphs, no headings or lists. "
			+ "Cover personality, how they talk, what they love and dislike, a little history, and quirks. Keep every detail the notes give and stay true to them; invent the rest so it fits. The character is an adult. Output only the sheet.";
		string user = "Name: " + d.Name.Trim() + "\nAge: " + d.Age + "\nGender: " + d.Word + " (" + they + ")\n"
			+ (d.Option(CreatorKinds.Ethnicity) != null ? "Ethnicity: " + d.Option(CreatorKinds.Ethnicity)!.Text + "\n" : "")
			+ (d.Occupation.Trim().Length > 0 ? "Occupation: " + d.Occupation.Trim() + "\n" : "")
			+ (d.Relationship.Trim().Length > 0 ? "How they know the user: " + d.Relationship.Trim() + "\n" : "")
			+ "Notes: " + (d.Personality.Trim().Length > 0 ? d.Personality.Trim() : "(none yet: invent a warm, distinct personality)") + "\n\nWrite the sheet now.";
		JsonObject payload = new JsonObject
		{
			["model"] = engine.Alias,
			["id_slot"] = 1,
			["messages"] = new JsonArray
			{
				new JsonObject { ["role"] = "system", ["content"] = system },
				new JsonObject { ["role"] = "user", ["content"] = user }
			},
			["stream"] = false,
			["temperature"] = 0.8,
			["max_tokens"] = 900,
			["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false }
		};
		using HttpResponseMessage response = await engine.PostAsync("v1/chat/completions", payload, stream: false, ct);
		JsonNode? node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
		string written = node?["choices"]?.AsArray().FirstOrDefault()?["message"]?["content"]?.GetValue<string>() ?? "";
		written = System.Text.RegularExpressions.Regex.Replace(written, @"(?s)<think>.*?</think>", "").Trim();
		if (written.Length < 40)
		{
			throw new InvalidOperationException("The model didn't write a personality. Try again.");
		}
		return written.Length > CompanionDraft.MaxPersonalityChars ? written.Substring(0, CompanionDraft.MaxPersonalityChars) : written;
	}

	// ---------------- Step 6: summary, portrait, bring to life ----------------

	private FrameworkElement SummaryStep()
	{
		string name = draft.Name.Trim();
		Grid columns = new Grid();
		columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
		columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });

		// Left: everything chosen, each with Change.
		StackPanel sum = new StackPanel();
		sum.Children.Add(Ui.Text(name + (draft.Age > 0 ? ", " + draft.Age : ""), "Type.Title"));
		if (draft.Occupation.Trim().Length > 0)
		{
			sum.Children.Add(Ui.Text(draft.Occupation.Trim(), "Type.Small"));
		}
		sum.Children.Add(SummaryRow("Gender", draft.Option(CreatorKinds.Gender)?.Label ?? "", 1));
		sum.Children.Add(SummaryRow("Ethnicity", draft.Option(CreatorKinds.Ethnicity)?.Label ?? "", 2));
		sum.Children.Add(SummaryRow("Hair", draft.HairColour + ", " + (draft.Option(CreatorKinds.Hair)?.Label ?? "").ToLowerInvariant(), 3));
		sum.Children.Add(SummaryRow("Body", draft.Option(CreatorKinds.Body)?.Label ?? "", 4));
		sum.Children.Add(SummaryRow("Eyes", draft.EyeColour, 5));
		sum.Children.Add(SummaryRow("Always visible", draft.Feature.Trim(), 5));
		sum.Children.Add(SummaryRow("Wearing", draft.Outfit.Trim().Length > 0 ? draft.Outfit.Trim() : draft.DefaultOutfit, 5));
		sum.Children.Add(SummaryRow("Style", draft.ArtStyle, 5));
		if (draft.Relationship.Trim().Length > 0)
		{
			sum.Children.Add(SummaryRow("Knows you as", draft.Relationship.Trim(), 5));
		}
		sum.Children.Add(SummaryRow("Drives", string.Join(", ", draft.Drives.Select((DriveSpec d) => d.Name)), 5));
		sum.Children.Add(Ui.Section("Personality"));
		TextBlock sheet = Ui.Text(draft.Personality.Trim(), "Type.Small");
		sheet.MaxHeight = 220;
		sheet.TextTrimming = TextTrimming.WordEllipsis;
		sum.Children.Add(sheet);
		columns.Children.Add(new ScrollViewer { Content = new Border { Child = sum, Padding = new Thickness(0, 0, 12, 12) }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

		// Right: the portrait, with Regenerate (and extra details) on top, Generate and Bring to life below.
		StackPanel right = new StackPanel();
		bool has = draftPortrait.Length > 0 && File.Exists(draftPortrait);
		if (has)
		{
			Grid redo = new Grid { Margin = new Thickness(0, 0, 0, 8) };
			redo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			redo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			TextBox details = new TextBox { Text = draftPortraitExtra, MaxLength = 400 };
			Ui.SetPlaceholder(details, "Add details for the next try: a smile, glasses, softer light…");
			details.TextChanged += delegate { draftPortraitExtra = details.Text; };
			redo.Children.Add(details);
			Button again = Ui.Button(Ui.IconLabel("", "Regenerate"), "Btn.Secondary", delegate { _ = PortraitAsync(); });
			again.Margin = new Thickness(8, 0, 0, 0);
			again.IsEnabled = !generating && !makingTiles;
			Grid.SetColumn(again, 1);
			redo.Children.Add(again);
			right.Children.Add(redo);
		}
		Border frame = new Border { Width = 420, Height = 525, CornerRadius = new CornerRadius(14), BorderBrush = Ui.Brush("Stroke.Subtle"), BorderThickness = new Thickness(1), Background = Ui.Brush("Surface.1"), ClipToBounds = true };
		ImageSource? portrait = has ? LoadPreview(draftPortrait) : null;
		if (portrait != null)
		{
			frame.Background = new ImageBrush(portrait) { Stretch = Stretch.UniformToFill };
		}
		else
		{
			StackPanel empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
			empty.Children.Add(Ui.Icon("", 40, "Text.Tertiary"));
			TextBlock t = Ui.Text(generating ? "Drawing " + name + "…" : "Press Generate to see " + name + ".", "Type.Small");
			t.Margin = new Thickness(0, 10, 0, 0);
			t.TextAlignment = TextAlignment.Center;
			empty.Children.Add(t);
			frame.Child = empty;
		}
		right.Children.Add(frame);
		ComboBox model = new ComboBox { ItemsSource = new[] { PictureModels.Label(PictureModels.Fast), PictureModels.Label(PictureModels.Best) }, SelectedIndex = draftModel == PictureModels.Best ? 1 : 0, Width = 420 };
		model.SelectionChanged += delegate { draftModel = model.SelectedIndex == 1 ? PictureModels.Best : PictureModels.Fast; };
		right.Children.Add(Ui.Field("Picture engine", model, "", 10));
		Grid actions = new Grid { Margin = new Thickness(0, 12, 0, 0) };
		actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		Button generate = Ui.Button(Ui.IconLabel("", has ? "Generate again" : "Generate"), "Btn.Secondary", delegate { _ = PortraitAsync(); });
		generate.MinHeight = 44;
		generate.IsEnabled = !generating && !makingTiles;
		generate.ToolTip = "Make a photo of " + name + " from everything you chose";
		actions.Children.Add(generate);
		Button life = Ui.Button(Ui.IconLabel("", "Bring " + name + " to Life"), "Btn.Large", delegate { BringToLife(); });
		life.Margin = new Thickness(10, 0, 0, 0);
		life.IsEnabled = !generating && !makingTiles;
		life.ToolTip = has ? "Create " + name + " with this portrait as " + Genders.Possessive(draft.Gender) + " picture" : "Create " + name + " (you can add a picture later)";
		Grid.SetColumn(life, 1);
		actions.Children.Add(life);
		right.Children.Add(actions);
		Grid.SetColumn(right, 2);
		columns.Children.Add(new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0) });
		Grid.SetColumn(columns.Children[^1], 2);
		return columns;
	}

	private FrameworkElement SummaryRow(string label, string value, int step)
	{
		Grid row = new Grid { Margin = new Thickness(0, 8, 0, 0), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
		row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
		row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		row.Children.Add(new TextBlock { Text = label, Foreground = Ui.Brush("Text.Tertiary"), VerticalAlignment = VerticalAlignment.Center });
		TextBlock v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush("Text.Primary"), VerticalAlignment = VerticalAlignment.Center };
		Grid.SetColumn(v, 1);
		row.Children.Add(v);
		Button change = Ui.Button("Change", "Btn.Link", delegate { GoStep(step); });
		change.Margin = new Thickness(12, 0, 0, 0);
		change.VerticalAlignment = VerticalAlignment.Center;
		Grid.SetColumn(change, 2);
		row.Children.Add(change);
		return row;
	}

	/// <summary>Draws their portrait from everything chosen (no chat model needed).</summary>
	private async Task PortraitAsync()
	{
		if (generating || makingTiles)
		{
			return;
		}
		if (busy)
		{
			ShowCreateStatus("Wait for the current reply to finish, then press Generate.", bad: true);
			return;
		}
		string model = draftModel;
		(bool ready, string problem) = PictureDrawer.Kit.Check(model);
		if (!ready)
		{
			ShowCreateStatus("The picture engine isn't ready: " + problem, bad: true);
			return;
		}
		generating = true;
		genCts = new CancellationTokenSource();
		CancellationToken ct = genCts.Token;
		SetBusy(value: true);
		ShowCreateStatus("Drawing " + draft.Name.Trim() + "…");
		RefreshCreate();
		try
		{
			string drawn = await DrawAsync(model, draft.PortraitPrompt(draftPortraitExtra), (896, 1152), Array.Empty<string>(), "portrait " + draft.Name.Trim(), ct);
			draftPortrait = drawn;
			ShowCreateStatus("Like it? Bring " + Genders.Object(draft.Gender) + " to life, or add details and regenerate.");
		}
		catch (OperationCanceledException)
		{
			ShowCreateStatus("Stopped.");
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is HttpRequestException)
		{
			ShowCreateStatus("The portrait didn't work: " + ex.Message, bad: true);
		}
		finally
		{
			generating = false;
			genCts = null;
			SetBusy(value: false);
			PictureStatus(null);
			RefreshCreate();
		}
	}

	private void RefreshCreate()
	{
		if (screen == Screens.Create)
		{
			ShowScreen(Screens.Create);
		}
	}

	/// <summary>Creates the companion: their persona file, their private memory, their portrait as profile and reference picture.</summary>
	private void BringToLife()
	{
		if (generating || makingTiles || busy)
		{
			return;
		}
		string id;
		PersonaConfig config;
		try
		{
			id = CompanionDraft.NewId(draft.Name, companion.Registry.Load().Personas.Select((PersonaEntry p) => p.Id));
			config = draft.Build(id, DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
		}
		catch (PersonaConfigException ex)
		{
			ShowCreateStatus(ex.Message, bad: true);
			return;
		}
		if (!companion.Registry.Register(config, id, makeActive: false))
		{
			ShowCreateStatus("A companion with the id “" + id + "” already exists. Nothing was changed.", bad: true);
			return;
		}
		List<string> warnings = new List<string>();
		try
		{
			companion.Registry.Provision(id, DateTimeOffset.Now);
			companion.Forget(id);
		}
		catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException || ex is Microsoft.Data.Sqlite.SqliteException)
		{
			warnings.Add(config.Name + " was added but not set up yet (" + ex.Message + "). Open " + Genders.Possessive(config.Gender) + " settings and press Set up.");
		}
		if (draftPortrait.Length > 0 && File.Exists(draftPortrait))
		{
			try
			{
				companion.SetProfilePicture(id, draftPortrait);
				string refs = PictureFiles.ReferenceFolder(companion.Folder(id));
				Directory.CreateDirectory(refs);
				File.Copy(draftPortrait, Path.Combine(refs, "portrait" + Path.GetExtension(draftPortrait)), overwrite: false);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException || ex is PersonaConfigException)
			{
				warnings.Add("The portrait couldn't be saved as " + Genders.Possessive(config.Gender) + " picture: " + ex.Message);
			}
		}
		createdId = id;
		createStatusText = string.Join(" ", warnings);
		createStatusBad = warnings.Count > 0;
		draft = new CompanionDraft();
		draftPortrait = "";
		draftPortraitExtra = "";
		draftDrives = null;
		draftDriveEditor = null;
		createStep = 1;
		ChatsList.Items.Refresh();
		ShowScreen(Screens.Create);
	}

	private FrameworkElement CreatedPanel()
	{
		string id = createdId;
		PersonaConfig? config = null;
		try
		{
			config = companion.Registry.LoadConfig(id);
		}
		catch (PersonaConfigException)
		{
		}
		string name = config?.Name ?? id;
		string gender = config?.Gender ?? Genders.Female;
		StackPanel panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
		ImageSource? picture = companion.ProfilePicture(id);
		if (picture != null)
		{
			panel.Children.Add(new Border { Width = 200, Height = 200, CornerRadius = new CornerRadius(100), Background = new ImageBrush(picture) { Stretch = Stretch.UniformToFill }, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16) });
		}
		panel.Children.Add(Ui.Text(name + " is ready.", "Type.Title"));
		TextBlock sub = Ui.Text("Say hello, make pictures of " + Genders.Object(gender) + ", or fine-tune " + Genders.Possessive(gender) + " settings.", "Type.Small");
		sub.Margin = new Thickness(0, 4, 0, 16);
		panel.Children.Add(sub);
		if (createStatusText.Length > 0)
		{
			TextBlock warn = Ui.Text(createStatusText, "Type.Small");
			warn.Foreground = Ui.Brush(createStatusBad ? "Danger" : "Text.Secondary");
			warn.Margin = new Thickness(0, 0, 0, 16);
			warn.MaxWidth = 640;
			panel.Children.Add(warn);
		}
		WrapPanel buttons = new WrapPanel();
		Button message = Ui.Button(Ui.IconLabel("", "Message " + name), "Btn.Primary", delegate
		{
			createdId = "";
			createStatusText = "";
			ShowScreen(Screens.Chats);
			NewChat(id, name);
		});
		message.IsEnabled = companion.IsSetUp(id);
		buttons.Children.Add(message);
		Button pictures = Ui.Button(Ui.IconLabel("", "Make pictures"), "Btn.Secondary", delegate
		{
			createdId = "";
			createStatusText = "";
			generateFor = id;
			ShowScreen(Screens.Generate);
		});
		pictures.Margin = new Thickness(8, 0, 0, 0);
		buttons.Children.Add(pictures);
		Button settingsButton = Ui.Button(Ui.IconLabel("", "Settings"), "Btn.Secondary", delegate
		{
			OpenCompanions(id, null);
			RefreshPage();
		});
		settingsButton.Margin = new Thickness(8, 0, 0, 0);
		buttons.Children.Add(settingsButton);
		Button another = Ui.Button(Ui.IconLabel("", "Create another"), "Btn.Ghost", delegate
		{
			createdId = "";
			createStatusText = "";
			ShowScreen(Screens.Create);
		});
		another.Margin = new Thickness(8, 0, 0, 0);
		buttons.Children.Add(another);
		panel.Children.Add(buttons);
		return panel;
	}
}
