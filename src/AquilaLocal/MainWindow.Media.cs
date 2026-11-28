using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Living;
using AquilaLocal.Companion.Pictures;

namespace AquilaLocal;

/// <summary>
/// My Media: every picture and video made on this computer, newest first, five to a row, with favorites, a
/// companion filter, and a card for each picture (Favorite, Edit, Remix, Make it a video, Delete).
/// </summary>
public partial class MainWindow
{
	private const int MediaColumns = 5;
	private const int MediaPageSize = 60;

	/// <summary>The companion My Media shows ("" for everyone).</summary>
	private string mediaFor = "";

	private bool mediaFavoritesOnly;

	/// <summary>"newest", "oldest" or "favorites" (favorites first, then newest).</summary>
	private string mediaSort = "newest";

	private int mediaShown = MediaPageSize;

	private MediaLibrary Media => new MediaLibrary(PicturesRoot);

	/// <summary>Opens My Media for one companion ("" for everyone).</summary>
	private void ShowMediaFor(string personaId)
	{
		mediaFor = personaId;
		mediaShown = MediaPageSize;
		ShowScreen(Screens.Media);
	}

	private FrameworkElement MediaPage()
	{
		DockPanel page = Page("My Media", "Every picture and video made on this computer, newest first. Click one to open its card.", null);
		List<PersonaEntry> people;
		try
		{
			people = companion.Registry.Load().Personas;
		}
		catch (PersonaConfigException)
		{
			people = new List<PersonaEntry>();
		}

		// Filters: whose, favorites only, order.
		StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
		ComboBox who = new ComboBox { Width = 220, VerticalAlignment = VerticalAlignment.Center };
		who.Items.Add("Everyone");
		foreach (PersonaEntry p in people)
		{
			who.Items.Add(p.Name);
		}
		int at = people.FindIndex(p => p.Id == mediaFor);
		who.SelectedIndex = at >= 0 ? at + 1 : 0;
		who.SelectionChanged += delegate
		{
			mediaFor = who.SelectedIndex > 0 ? people[who.SelectedIndex - 1].Id : "";
			mediaShown = MediaPageSize;
			ShowScreen(Screens.Media);
		};
		bar.Children.Add(who);
		Button favorites = Ui.Button(Ui.IconLabel(mediaFavoritesOnly ? "" : "", "Favorites only"), mediaFavoritesOnly ? "Btn.Primary" : "Btn.Secondary", delegate
		{
			mediaFavoritesOnly = !mediaFavoritesOnly;
			mediaShown = MediaPageSize;
			ShowScreen(Screens.Media);
		});
		favorites.Margin = new Thickness(10, 0, 0, 0);
		bar.Children.Add(favorites);
		ComboBox order = new ComboBox { Width = 190, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ItemsSource = new[] { "Newest first", "Oldest first", "Favorites first" } };
		order.SelectedIndex = mediaSort == "oldest" ? 1 : mediaSort == "favorites" ? 2 : 0;
		order.SelectionChanged += delegate
		{
			mediaSort = order.SelectedIndex == 1 ? "oldest" : order.SelectedIndex == 2 ? "favorites" : "newest";
			ShowScreen(Screens.Media);
		};
		bar.Children.Add(order);
		DockPanel.SetDock(bar, Dock.Top);
		page.Children.Add(bar);

		List<MediaItem> items;
		HashSet<string> favs;
		Dictionary<string, string> owners;
		try
		{
			items = Media.Scan();
			favs = Media.Favorites();
			owners = MediaOwners(people, items);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PersonaConfigException)
		{
			page.Children.Add(Ui.Text("Your pictures couldn't be read: " + ex.Message, "Type.Small"));
			return page;
		}
		MediaLibrary library = Media;
		if (mediaFor.Length > 0)
		{
			items = items.Where(i => owners.TryGetValue(i.Path, out string? o) && o == mediaFor).ToList();
		}
		if (mediaFavoritesOnly)
		{
			items = items.Where(i => library.IsFavorite(i.Path, favs)).ToList();
		}
		if (mediaSort == "oldest")
		{
			items.Reverse();
		}
		else if (mediaSort == "favorites")
		{
			items = items.OrderByDescending(i => library.IsFavorite(i.Path, favs)).ThenByDescending(i => i.At).ToList();
		}
		if (items.Count == 0)
		{
			TextBlock none = Ui.Text(mediaFavoritesOnly ? "No favorites here yet. Open a picture and press Favorite, or click the heart on it." : "No pictures here yet. Make some on Generate, or let your companions draw in your chats.", "Type.Body");
			none.Foreground = Ui.Brush("Text.Secondary");
			page.Children.Add(none);
			return page;
		}
		StackPanel list = new StackPanel { Margin = new Thickness(0, 0, 0, 28) };
		UniformGrid grid = new UniformGrid { Columns = MediaColumns };
		Dictionary<string, string> names = people.ToDictionary(p => p.Id, p => p.Name);
		foreach (MediaItem item in items.Take(mediaShown))
		{
			string owner = owners.TryGetValue(item.Path, out string? o) ? o : "";
			grid.Children.Add(MediaTile(item, library.IsFavorite(item.Path, favs), owner.Length > 0 && names.TryGetValue(owner, out string? n) ? n : "", owner));
		}
		list.Children.Add(grid);
		if (items.Count > mediaShown)
		{
			Button more = Ui.Button("Show more (" + (items.Count - mediaShown) + " left)", "Btn.Secondary", delegate
			{
				mediaShown += MediaPageSize;
				ShowScreen(Screens.Media);
			});
			more.HorizontalAlignment = HorizontalAlignment.Center;
			more.Margin = new Thickness(0, 10, 0, 0);
			list.Children.Add(more);
		}
		page.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
		return page;
	}

	/// <summary>
	/// Whose each picture is: the companion of the chat it's in, the journal it's in, or the name in its file
	/// name (Generate, portraits, dreams).
	/// </summary>
	private Dictionary<string, string> MediaOwners(List<PersonaEntry> people, List<MediaItem> items)
	{
		Dictionary<string, string> owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (ChatSession chat in chats)
		{
			if (!chat.IsCompanion || chat.PersonaId.Length == 0)
			{
				continue;
			}
			foreach (Turn t in chat.Turns)
			{
				foreach (string p in t.Pictures)
				{
					owners.TryAdd(MessageView.IsVideo(p) ? Path.ChangeExtension(p, ".png") : p, chat.PersonaId);
				}
			}
		}
		foreach (PersonaEntry person in people)
		{
			try
			{
				foreach (JournalEntry e in Journal.Read(person.Id))
				{
					if (e.Picture.Length > 0)
					{
						owners.TryAdd(e.Picture, person.Id);
					}
				}
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PersonaConfigException || ex is System.Text.Json.JsonException)
			{
			}
		}
		foreach (MediaItem item in items)
		{
			if (!owners.ContainsKey(item.Path))
			{
				PersonaEntry? named = people.FirstOrDefault(p => MediaLibrary.NamedFor(item.Path, p.Name));
				if (named != null)
				{
					owners[item.Path] = named.Id;
				}
			}
		}
		return owners;
	}

	private FrameworkElement MediaTile(MediaItem item, bool favorite, string ownerName, string ownerId)
	{
		Grid face = new Grid { Height = 240 };
		ImageSource? thumb = MediaThumb(item.Path, 360);
		if (thumb != null)
		{
			face.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = new ImageBrush(thumb) { Stretch = Stretch.UniformToFill } });
		}
		else
		{
			face.Children.Add(Ui.Icon("", 28, "Text.Tertiary"));
		}
		if (item.IsVideo)
		{
			face.Children.Add(new Border
			{
				Width = 40,
				Height = 40,
				CornerRadius = new CornerRadius(20),
				Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				IsHitTestVisible = false,
				Child = new TextBlock { Text = "▶", FontSize = 17, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) }
			});
		}
		Button heart = new Button
		{
			Style = Ui.Style("Btn.Icon"),
			Content = Ui.Icon(favorite ? "" : "", 14),
			Width = 30,
			Height = 30,
			Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0B, 0x12, 0x19)),
			Foreground = favorite ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x8B)) : Brushes.White,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0, 6, 6, 0),
			ToolTip = favorite ? "A favorite (click to unmark)" : "Mark as a favorite"
		};
		Ui.SetRadius(heart, new CornerRadius(15));
		heart.Click += delegate(object _, RoutedEventArgs e)
		{
			e.Handled = true;
			ToggleFavorite(item.Path);
			ShowScreen(Screens.Media);
		};
		face.Children.Add(heart);
		StackPanel content = new StackPanel();
		content.Children.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Background = Ui.Brush("Surface.2"), Child = face });
		content.Children.Add(new TextBlock
		{
			Text = (ownerName.Length > 0 ? ownerName + " · " : "") + item.At.ToLocalTime().ToString("MMM d, h:mm tt"),
			FontSize = 12,
			Foreground = Ui.Brush("Text.Secondary"),
			TextTrimming = TextTrimming.CharacterEllipsis,
			Margin = new Thickness(2, 5, 0, 0)
		});
		Button b = new Button { Style = Ui.Style("Btn.Ghost"), Content = content, Padding = new Thickness(0), Margin = new Thickness(0, 0, 10, 14), MinHeight = 0, HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = item.Prompt.Length > 0 ? item.Prompt.Truncate(300) : null };
		Ui.SetRadius(b, new CornerRadius(10));
		b.Click += delegate { OpenMediaCard(item, ownerName, ownerId); };
		return b;
	}

	private static ImageSource? MediaThumb(string path, int width)
	{
		try
		{
			BitmapImage image = new BitmapImage();
			image.BeginInit();
			image.CacheOption = BitmapCacheOption.OnLoad;
			image.StreamSource = new MemoryStream(File.ReadAllBytes(path));
			image.DecodePixelWidth = width;
			image.EndInit();
			image.Freeze();
			return image;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException || ex is FileFormatException)
		{
			return null;
		}
	}

	private bool ToggleFavorite(string path)
	{
		try
		{
			return Media.ToggleFavorite(path);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PersonaConfigException)
		{
			Notice("The favorite wasn't saved: " + ex.Message);
			return false;
		}
	}

	// ---------------- A picture's card ----------------

	private void OpenMediaCard(MediaItem item, string ownerName, string ownerId)
	{
		AquilaWindow card = new AquilaWindow { Title = "Aquila · " + (ownerName.Length > 0 ? ownerName + " · " : "") + item.At.ToLocalTime().ToString("MMM d, yyyy h:mm tt"), Width = 1080, Height = 820, MinWidth = 760, MinHeight = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this };
		card.CloseOnEscape = true;
		Grid layout = new Grid { Margin = new Thickness(20) };
		layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });

		ImageSource? big = MediaThumb(item.Path, 1400);
		Image image = new Image { Source = big, Stretch = Stretch.Uniform, Cursor = Cursors.Hand, ToolTip = item.IsVideo ? "Play the video" : "Open full size" };
		RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
		image.MouseLeftButtonUp += delegate { OpenFile(item.IsVideo ? item.Clip : item.Path); };
		layout.Children.Add(new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, Background = Ui.Brush("Surface.1"), Child = image, Margin = new Thickness(0, 0, 20, 0) });

		StackPanel side = new StackPanel();
		side.Children.Add(Ui.Text(ownerName.Length > 0 ? ownerName : "Picture", "Type.Title"));
		TextBlock when = Ui.Text(item.At.ToLocalTime().ToString("dddd, MMMM d, yyyy · h:mm tt") + (item.IsVideo ? " · video" : "") + (item.Model.Length > 0 ? " · " + item.Model : ""), "Type.Small");
		when.Margin = new Thickness(0, 4, 0, 12);
		side.Children.Add(when);
		TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
		void Say(string text, bool bad = false)
		{
			status.Text = text;
			status.Foreground = Ui.Brush(bad ? "Danger" : "Text.Secondary");
		}

		WrapPanel buttons = new WrapPanel();
		bool fav = Media.IsFavorite(item.Path);
		Button favorite = Ui.Button(Ui.IconLabel(fav ? "" : "", fav ? "Favorite" : "Mark favorite"), fav ? "Btn.Primary" : "Btn.Secondary", delegate { });
		favorite.Click += delegate
		{
			bool now = ToggleFavorite(item.Path);
			favorite.Content = Ui.IconLabel(now ? "" : "", now ? "Favorite" : "Mark favorite");
			favorite.Style = Ui.Style(now ? "Btn.Primary" : "Btn.Secondary");
		};
		Add(favorite);
		if (item.IsVideo)
		{
			Add(Ui.Button(Ui.IconLabel("", "Play video"), "Btn.Secondary", delegate { OpenFile(item.Clip); }));
		}
		else
		{
			Add(Ui.Button(Ui.IconLabel("", "Make it a video"), "Btn.Secondary", async delegate
			{
				string clip = await PictureToVideoAsync(item.Path, text => Say(text));
				if (clip.Length > 0)
				{
					card.Close();
					RefreshMediaIfShowing();
				}
			}));
		}
		Add(Ui.Button(Ui.IconLabel("", "Open"), "Btn.Ghost", delegate { OpenFile(item.IsVideo ? item.Clip : item.Path); }));
		Add(Ui.Button(Ui.IconLabel("", "Show in folder"), "Btn.Ghost", delegate { ShowInFolder(item.IsVideo ? item.Clip : item.Path); }));
		side.Children.Add(buttons);
		void Add(Button b)
		{
			b.Margin = new Thickness(0, 0, 8, 8);
			buttons.Children.Add(b);
		}

		// Edit: say what to change.
		side.Children.Add(Ui.Section("Edit", 14));
		TextBox change = new TextBox { TextWrapping = TextWrapping.Wrap, Height = 64, VerticalContentAlignment = VerticalAlignment.Top, MaxLength = 400 };
		Ui.SetPlaceholder(change, "Small changes: \"make her hair wet\", \"evening light\", \"she's smiling\"");
		side.Children.Add(change);
		Button edit = Ui.Button(Ui.IconLabel("", "Make the edited picture"), "Btn.Secondary", delegate { });
		edit.Margin = new Thickness(0, 6, 0, 0);
		edit.HorizontalAlignment = HorizontalAlignment.Left;
		side.Children.Add(edit);

		// Remix: new action, outfit or scene.
		side.Children.Add(Ui.Section("Remix", 14));
		ComboBox action = RemixPicker(PresetKinds.Action, ownerId);
		ComboBox outfit = RemixPicker(PresetKinds.Outfit, ownerId);
		ComboBox scene = RemixPicker(PresetKinds.Scene, ownerId);
		side.Children.Add(Ui.Field("Doing", action, "", 0));
		side.Children.Add(Ui.Field("Wearing", outfit, "", 6));
		side.Children.Add(Ui.Field("Where", scene, "", 6));
		Button remix = Ui.Button(Ui.IconLabel("", "Make the remix"), "Btn.Secondary", delegate { });
		remix.Margin = new Thickness(0, 8, 0, 0);
		remix.HorizontalAlignment = HorizontalAlignment.Left;
		side.Children.Add(remix);

		async Task Revise(string what)
		{
			edit.IsEnabled = false;
			remix.IsEnabled = false;
			try
			{
				string made = await RevisePictureAsync(item, ownerId, ownerName, what, text => Say(text), text => Say(text, bad: true));
				if (made.Length > 0)
				{
					card.Close();
					RefreshMediaIfShowing();
					MediaItem next = new MediaItem(made, "", DateTimeOffset.Now, MediaLibrary.ReadSidecar(made).Prompt, MediaLibrary.ReadSidecar(made).Model, MediaLibrary.ReadSidecar(made).References);
					OpenMediaCard(next, ownerName, ownerId);
				}
			}
			finally
			{
				edit.IsEnabled = true;
				remix.IsEnabled = true;
			}
		}
		edit.Click += async delegate
		{
			if (change.Text.Trim().Length == 0)
			{
				Say("Say what should change first.", bad: true);
				return;
			}
			await Revise(change.Text.Trim());
		};
		remix.Click += async delegate
		{
			List<string> parts = new List<string>();
			if (action.SelectedItem is ComboBoxItem { Tag: GenPreset a })
			{
				parts.Add("They are now " + a.Prompt + ".");
			}
			if (outfit.SelectedItem is ComboBoxItem { Tag: GenPreset o })
			{
				parts.Add("They now wear " + o.Prompt + " instead of their outfit in this picture.");
			}
			if (scene.SelectedItem is ComboBoxItem { Tag: GenPreset s })
			{
				parts.Add("The setting is now " + s.Prompt + ".");
			}
			if (parts.Count == 0)
			{
				Say("Pick something new for at least one of them.", bad: true);
				return;
			}
			await Revise(string.Join(" ", parts));
		};

		// The prompt, and Delete.
		side.Children.Add(Ui.Section("Its prompt", 14));
		side.Children.Add(new TextBox { Text = item.Prompt.Length > 0 ? item.Prompt : "(No prompt was saved with this picture.)", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Ui.Brush("Text.Secondary") });
		Button delete = Ui.Button(Ui.IconLabel("", "Delete"), "Btn.Ghost", delegate
		{
			if (!AquilaDialog.Confirm(card, "Delete this " + (item.IsVideo ? "picture and its video" : "picture") + "?", "It's removed from this computer for good, along with its prompt, and it disappears from any chat or journal it's in. This can't be undone.", "Delete", "Cancel", DialogKind.Danger))
			{
				return;
			}
			if (DeleteMedia(item, text => Say(text, bad: true)))
			{
				card.Close();
				RefreshMediaIfShowing();
			}
		});
		delete.Foreground = Ui.Brush("Danger");
		delete.Margin = new Thickness(0, 16, 0, 0);
		delete.HorizontalAlignment = HorizontalAlignment.Left;
		side.Children.Add(delete);
		side.Children.Add(status);

		ScrollViewer sideScroll = new ScrollViewer { Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
		Grid.SetColumn(sideScroll, 1);
		layout.Children.Add(sideScroll);
		card.Content = layout;
		card.ShowDialog();
	}

	private ComboBox RemixPicker(string kind, string ownerId)
	{
		ComboBox box = new ComboBox { MaxDropDownHeight = 360 };
		box.Items.Add(new ComboBoxItem { Content = "Keep as it is" });
		try
		{
			string gender = ownerId.Length > 0 ? GenderOf(ownerId) : "";
			foreach (GenPreset p in Presets.Load(kind).Where(p => gender.Length == 0 || p.Fits(gender)))
			{
				box.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p, ToolTip = p.Prompt });
			}
		}
		catch (PersonaConfigException)
		{
		}
		box.SelectedIndex = 0;
		return box;
	}

	private void RefreshMediaIfShowing()
	{
		if (screen == Screens.Media)
		{
			ShowScreen(Screens.Media);
		}
	}

	/// <summary>
	/// Draws a new picture from one you have, with a change: the chat model rewrites its prompt (or, without it,
	/// the change is added on), same picture model and size, the companion's references where it used them.
	/// Returns the new picture's path, or "".
	/// </summary>
	private async Task<string> RevisePictureAsync(MediaItem item, string ownerId, string ownerName, string what, Action<string> say, Action<string> fail)
	{
		if (generating || makingTiles || busy)
		{
			fail("Wait for the picture, video or reply in progress to finish first.");
			return "";
		}
		string model = settings.Pictures.Model;
		(bool ready, string problem) = PictureDrawer.Kit.Check(model);
		if (!ready)
		{
			fail("Pictures can't be made yet: " + problem);
			return "";
		}
		generating = true;
		genCts = new CancellationTokenSource();
		CancellationToken ct = genCts.Token;
		SetBusy(value: true);
		try
		{
			string original = item.Prompt.Length > 0 ? item.Prompt : (ownerName.Length > 0 ? "A picture of " + ownerName + "." : "A picture.");
			string prompt;
			if (engine.Ready)
			{
				say("Rewriting the prompt with your change…");
				prompt = await RewritePromptAsync(original, what, ct);
				if (prompt.Trim().Length == 0)
				{
					prompt = original + " " + what;
				}
			}
			else
			{
				prompt = original.TrimEnd() + " Change: " + what;
			}
			List<string> references = new List<string>();
			if (item.ReferenceCount > 0 && ownerId.Length > 0)
			{
				try
				{
					references = LooksFor(ownerId, withOutfit: true).References.Take(item.ReferenceCount).ToList();
				}
				catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException || ex is Microsoft.Data.Sqlite.SqliteException)
				{
				}
			}
			if (references.Count == 0)
			{
				prompt = PicturePrompt.WithoutReferences(prompt);
			}
			say("Drawing…");
			string made = await DrawAsync(model, prompt, PictureSize(item.Path), references, (ownerName.Length > 0 ? "edit " + ownerName : "edit"), ct);
			say("Done. It's saved as a new picture; the first one is kept.");
			return made;
		}
		catch (OperationCanceledException)
		{
			fail("Stopped.");
			return "";
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is HttpRequestException || ex is PersonaConfigException || ex is NotSupportedException)
		{
			fail("It didn't work: " + ex.Message);
			return "";
		}
		finally
		{
			generating = false;
			genCts = null;
			SetBusy(value: false);
			PictureStatus(null);
		}
	}

	/// <summary>The chat model (second slot) changes one thing in an image prompt and keeps the rest.</summary>
	private async Task<string> RewritePromptAsync(string original, string change, CancellationToken ct)
	{
		JsonObject payload = new JsonObject
		{
			["model"] = engine.Alias,
			["id_slot"] = 1,
			["messages"] = new JsonArray
			{
				new JsonObject { ["role"] = "system", ["content"] = "You edit prompts for an image model. You get the prompt a picture was drawn from and a change to make. Rewrite the prompt so the new picture shows the change and keeps everything else the same: the same people and their looks, the same art style, framing and mood, unless the change says otherwise. Keep any notes about reference pictures. Keep the same prompt style (sentences or tags). Reply with the new prompt only." },
				new JsonObject { ["role"] = "user", ["content"] = "Prompt:\n" + original + "\n\nChange:\n" + change }
			},
			["stream"] = false,
			["temperature"] = 0.4,
			["max_tokens"] = 700,
			["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false }
		};
		using HttpResponseMessage response = await engine.PostAsync("v1/chat/completions", payload, stream: false, ct);
		JsonNode? node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
		string written = node?["choices"]?.AsArray().FirstOrDefault()?["message"]?["content"]?.GetValue<string>() ?? "";
		return PicturePrompt.Clean(written);
	}

	/// <summary>A picture's size in pixels (multiples of 64), or a portrait default when it can't be read.</summary>
	private static (int Width, int Height) PictureSize(string path)
	{
		try
		{
			using FileStream stream = File.OpenRead(path);
			BitmapFrame frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
			int w = Math.Max(256, frame.PixelWidth / 64 * 64);
			int h = Math.Max(256, frame.PixelHeight / 64 * 64);
			return (w, h);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException || ex is FileFormatException)
		{
			return (896, 1152);
		}
	}

	/// <summary>Deletes a picture (and its video) and takes it out of any chat or journal entry that shows it.</summary>
	private bool DeleteMedia(MediaItem item, Action<string> fail)
	{
		try
		{
			foreach (ChatSession chat in chats)
			{
				bool changed = false;
				foreach (Turn t in chat.Turns)
				{
					changed |= t.Pictures.RemoveAll(p => string.Equals(p, item.Path, StringComparison.OrdinalIgnoreCase) || (item.Clip.Length > 0 && string.Equals(p, item.Clip, StringComparison.OrdinalIgnoreCase))) > 0;
				}
				if (changed)
				{
					store.SaveChat(chat);
					if (chat == current)
					{
						RenderChat();
					}
				}
			}
			foreach (PersonaEntry person in companion.Registry.Load().Personas)
			{
				foreach (JournalEntry e in Journal.Read(person.Id))
				{
					if (string.Equals(e.Picture, item.Path, StringComparison.OrdinalIgnoreCase))
					{
						e.Picture = "";
						e.Video = "";
						Journal.Replace(e);
					}
				}
			}
			Media.Delete(item);
			return true;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PersonaConfigException || ex is System.Text.Json.JsonException)
		{
			fail("It wasn't deleted: " + ex.Message);
			return false;
		}
	}
}
