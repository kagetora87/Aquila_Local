using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Pictures;

namespace AquilaLocal;

/// <summary>
/// "Custom" on the Generate screen (describe a new action, outfit or scene), or Edit on any preset: its name,
/// its words, and the prompt its small picture is drawn from (the standard one, your own, or one the AI writes).
/// </summary>
public sealed class CustomPresetWindow : AquilaWindow
{
	private readonly TextBox name;

	private readonly TextBox prompt;

	private readonly TextBox? motion;

	private readonly ComboBox? shot;

	private readonly TextBox tile;

	private readonly CheckBox drawNow;

	private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

	private static readonly string[] Shots = { "Half body", "Portrait", "Full body", "Scene" };

	/// <summary>The preset as saved (new or edited), or null when nothing was saved.</summary>
	public GenPreset? Added { get; private set; }

	/// <summary>Whether its small picture should be drawn now.</summary>
	public bool DrawPicture => drawNow.IsChecked == true;

	/// <param name="writeTilePrompt">Asks the chat model to write the picture's prompt from the standard one; null while no model is loaded.</param>
	public CustomPresetWindow(PresetLibrary library, string kind, GenPreset? editing = null, Func<string, string, CancellationToken, Task<string>>? writeTilePrompt = null)
	{
		string label = PresetKinds.Label(kind).ToLowerInvariant();
		string heading = editing == null ? "New " + label : "Edit " + label;
		base.Title = "Aquila · " + heading;
		base.Width = 620;
		base.SizeToContent = SizeToContent.Height;
		base.MaxHeight = 900;
		base.ResizeMode = ResizeMode.NoResize;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		CloseOnEscape = true;
		StackPanel body = new StackPanel();
		name = new TextBox { MaxLength = PresetLibrary.MaxNameChars, Text = editing?.Name ?? "" };
		Ui.SetPlaceholder(name, kind == PresetKinds.Action ? "For example: Painting" : kind == PresetKinds.Outfit ? "For example: Denim jacket" : "For example: Train at night");
		body.Children.Add(Ui.Field("Name", name, "Shown under its picture.", 0));
		prompt = new TextBox { MaxLength = PresetLibrary.MaxPromptChars, AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, Height = 70, VerticalContentAlignment = VerticalAlignment.Top, Text = editing?.Prompt ?? "" };
		string hint = kind == PresetKinds.Action
			? "What they're doing, in a few words: \"painting at an easel, brush in hand\"."
			: kind == PresetKinds.Outfit
				? "What they're wearing: \"a faded denim jacket over a white tank top and black jeans\"."
				: "The place: \"a night train compartment, city lights passing the window\".";
		body.Children.Add(Ui.Field("Description", prompt, hint + " This is what goes into your pictures."));
		if (kind == PresetKinds.Action)
		{
			int at = Array.FindIndex(Shots, s => s.Equals(editing?.Framing ?? "", StringComparison.OrdinalIgnoreCase));
			shot = new ComboBox { ItemsSource = Shots, SelectedIndex = Math.Max(0, at), Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
			body.Children.Add(Ui.Field("Shot", shot, "How much of them the picture shows."));
			motion = new TextBox { MaxLength = VideoRequest.MaxMotionChars, Text = editing?.Motion ?? "" };
			Ui.SetPlaceholder(motion, "For example: dips the brush and adds a stroke");
			body.Children.Add(Ui.Field("In a video (optional)", motion, "What moves when you make a video with this action."));
		}

		// Its small picture: the exact prompt, or empty for the standard one.
		tile = new TextBox { MaxLength = PresetLibrary.MaxTilePromptChars, AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, Height = 90, VerticalContentAlignment = VerticalAlignment.Top, Text = editing?.Tile ?? "", VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
		Ui.SetPlaceholder(tile, "Leave empty to draw it the standard way, from the description and Picture style.");
		body.Children.Add(Ui.Field("Its picture's prompt (optional)", tile, "The exact words the picture engine gets for this tile. Fill it in yourself, or start from one below and change what you like."));
		StackPanel tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
		Button standard = Ui.Button("Show the standard prompt", "Btn.Secondary", delegate
		{
			try
			{
				tile.Text = PresetLibrary.TilePrompt(Draft(kind), library.Style);
				SetStatus("This is what it would use. Change any of it, or clear it to go back to the standard one.", bad: false);
			}
			catch (PersonaConfigException ex)
			{
				SetStatus(ex.Message, bad: true);
			}
		});
		standard.Margin = new Thickness(0, 0, 8, 0);
		tools.Children.Add(standard);
		Button ai = Ui.Button(Ui.IconLabel("", "Write it with AI"), "Btn.Secondary", delegate { });
		ai.IsEnabled = writeTilePrompt != null;
		ai.ToolTip = writeTilePrompt != null ? "The chat model writes a fuller prompt from the description. You can edit it after." : "Load a chat model first.";
		ToolTipService.SetShowOnDisabled(ai, true);
		ai.Click += async delegate
		{
			if (writeTilePrompt == null)
			{
				return;
			}
			ai.IsEnabled = false;
			SetStatus("Writing the prompt…", bad: false);
			try
			{
				GenPreset draft = Draft(kind);
				string written = await writeTilePrompt(PresetLibrary.TilePrompt(draft, library.Style), draft.Framing.Length > 0 ? draft.Framing : (kind == PresetKinds.Scene ? "scene" : kind == PresetKinds.Outfit ? "full body" : "half body"), CancellationToken.None);
				if (written.Trim().Length == 0)
				{
					SetStatus("The model didn't write anything. Try again, or write it yourself.", bad: true);
				}
				else
				{
					tile.Text = written.Trim().Length > PresetLibrary.MaxTilePromptChars ? written.Trim().Substring(0, PresetLibrary.MaxTilePromptChars) : written.Trim();
					SetStatus("Written. Change anything you like before saving.", bad: false);
				}
			}
			catch (Exception ex) when (ex is InvalidOperationException || ex is System.Net.Http.HttpRequestException || ex is PersonaConfigException || ex is System.Text.Json.JsonException || ex is TaskCanceledException)
			{
				SetStatus("The prompt couldn't be written: " + ex.Message, bad: true);
			}
			finally
			{
				ai.IsEnabled = true;
			}
		};
		tools.Children.Add(ai);
		body.Children.Add(tools);
		drawNow = new CheckBox { Content = editing == null ? "Make its picture right after" : "Draw its picture again after saving", IsChecked = editing == null, Margin = new Thickness(0, 14, 0, 0) };
		body.Children.Add(drawNow);
		if (editing != null)
		{
			void Changed(object s, TextChangedEventArgs e) => drawNow.IsChecked = true;
			prompt.TextChanged += Changed;
			tile.TextChanged += Changed;
		}
		body.Children.Add(status);

		Button add = Ui.Button(editing == null ? "Add " + label : "Save", "Btn.Primary", delegate
		{
			try
			{
				string framing = shot == null ? "" : ((string)shot.SelectedItem).ToLowerInvariant();
				Added = editing == null
					? library.AddCustom(kind, name.Text, prompt.Text, framing, motion?.Text ?? "", tile.Text)
					: library.Update(editing.Id, name.Text, prompt.Text, framing, motion?.Text ?? "", tile.Text);
				DialogResult = true;
			}
			catch (Exception ex) when (ex is PersonaConfigException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
			{
				SetStatus(ex.Message, bad: true);
			}
		});
		add.IsDefault = true;
		Button cancel = Ui.Button("Cancel", "Btn.Secondary", delegate
		{
			Close();
		});
		Button? left = null;
		if (editing != null && !editing.Custom)
		{
			GenPreset? original = null;
			foreach (GenPreset p in PresetLibrary.BuiltIn)
			{
				if (p.Id == editing.Id)
				{
					original = p;
				}
			}
			if (original != null)
			{
				left = Ui.Button("Back to how it came", "Btn.Ghost", delegate
				{
					name.Text = original.Name;
					prompt.Text = original.Prompt;
					tile.Text = original.Tile;
					if (motion != null)
					{
						motion.Text = original.Motion;
					}
					if (shot != null)
					{
						shot.SelectedIndex = Math.Max(0, Array.FindIndex(Shots, s => s.Equals(original.Framing, StringComparison.OrdinalIgnoreCase)));
					}
					SetStatus("Its original words are back. Press Save to keep them.", bad: false);
				});
			}
		}
		base.Content = Ui.DialogScaffold(heading, editing == null ? "Its picture is made right after, when the picture engine is free." : "Changes apply to new pictures. Pictures you already made stay as they are.", body, null, left != null ? new[] { left, cancel, add } : new[] { cancel, add });
		base.Loaded += delegate
		{
			name.Focus();
		};
	}

	/// <summary>The preset as typed so far (for the standard prompt).</summary>
	private GenPreset Draft(string kind)
	{
		string framing = shot == null ? "" : ((string)shot.SelectedItem).ToLowerInvariant();
		string words = prompt.Text.Trim();
		if (words.Length == 0)
		{
			throw new PersonaConfigException("Write the description first.");
		}
		return new GenPreset("draft", kind, name.Text.Trim(), words, framing);
	}

	private void SetStatus(string text, bool bad)
	{
		status.Text = text;
		status.Foreground = Ui.Brush(bad ? "Danger" : "Text.Secondary");
	}
}
