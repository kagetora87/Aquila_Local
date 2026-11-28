using System;
using System.Windows;
using System.Windows.Controls;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Pictures;

namespace AquilaLocal;

/// <summary>"Custom" on the Generate screen: describe a new action, outfit or scene and it becomes a preset of your own.</summary>
public sealed class CustomPresetWindow : AquilaWindow
{
	private readonly TextBox name;

	private readonly TextBox prompt;

	private readonly TextBox? motion;

	private readonly ComboBox? shot;

	private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap };

	public GenPreset? Added { get; private set; }

	public CustomPresetWindow(PresetLibrary library, string kind)
	{
		string label = PresetKinds.Label(kind).ToLowerInvariant();
		base.Title = "Aquila · New " + label;
		base.Width = 560;
		base.SizeToContent = SizeToContent.Height;
		base.ResizeMode = ResizeMode.NoResize;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		CloseOnEscape = true;
		StackPanel body = new StackPanel();
		name = new TextBox { MaxLength = PresetLibrary.MaxNameChars };
		Ui.SetPlaceholder(name, kind == PresetKinds.Action ? "For example: Painting" : kind == PresetKinds.Outfit ? "For example: Denim jacket" : "For example: Train at night");
		body.Children.Add(Ui.Field("Name", name, "Shown under its picture.", 0));
		prompt = new TextBox { MaxLength = PresetLibrary.MaxPromptChars, AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, Height = 70, VerticalContentAlignment = VerticalAlignment.Top };
		string hint = kind == PresetKinds.Action
			? "What they're doing, in a few words: \"painting at an easel, brush in hand\"."
			: kind == PresetKinds.Outfit
				? "What they're wearing: \"a faded denim jacket over a white tank top and black jeans\"."
				: "The place: \"a night train compartment, city lights passing the window\".";
		body.Children.Add(Ui.Field("Description", prompt, hint));
		if (kind == PresetKinds.Action)
		{
			shot = new ComboBox { ItemsSource = new[] { "Half body", "Portrait", "Full body", "Scene" }, SelectedIndex = 0, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
			body.Children.Add(Ui.Field("Shot", shot, "How much of them the picture shows."));
			motion = new TextBox { MaxLength = VideoRequest.MaxMotionChars };
			Ui.SetPlaceholder(motion, "For example: dips the brush and adds a stroke");
			body.Children.Add(Ui.Field("In a video (optional)", motion, "What moves when you make a video with this action."));
		}
		body.Children.Add(status);
		Button add = Ui.Button("Add " + label, "Btn.Primary", delegate
		{
			try
			{
				string framing = shot == null ? "" : ((string)shot.SelectedItem).ToLowerInvariant();
				Added = library.AddCustom(kind, name.Text, prompt.Text, framing, motion?.Text ?? "");
				DialogResult = true;
			}
			catch (Exception ex) when (ex is PersonaConfigException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
			{
				status.Text = ex.Message;
				status.Foreground = Ui.Brush("Danger");
			}
		});
		add.IsDefault = true;
		Button cancel = Ui.Button("Cancel", "Btn.Secondary", delegate
		{
			Close();
		});
		base.Content = Ui.DialogScaffold("New " + label, "Its picture is made right after, when the picture engine is free.", body, null, cancel, add);
		base.Loaded += delegate
		{
			name.Focus();
		};
	}
}
