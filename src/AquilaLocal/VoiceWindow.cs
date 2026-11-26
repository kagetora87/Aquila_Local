using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace AquilaLocal;

public sealed class VoiceWindow : AquilaWindow
{
	private sealed record VoiceChoice(string Key, string Label);

	private readonly CheckBox enabled = new CheckBox
	{
		Content = "Speak completed replies automatically"
	};

	private readonly CheckBox keep = new CheckBox
	{
		Content = "Keep expressive model loaded between replies"
	};

	private readonly CheckBox autoTags = new CheckBox
	{
		Content = "Apply voice tags automatically"
	};

	private readonly CheckBox revealTags = new CheckBox
	{
		Content = "Reveal tags"
	};

	private readonly TextBlock directionHint = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Foreground = Ui.Brush("Text.Secondary"),
		FontSize = 12.0,
		Margin = new Thickness(0.0, 7.0, 0.0, 0.0)
	};

	private readonly ComboBox speaker = new ComboBox();

	private readonly ComboBox device = new ComboBox();

	private readonly ComboBox mood = new ComboBox();

	private readonly ComboBox pace = new ComboBox();

	private readonly Slider intensity = new Slider
	{
		Minimum = 0.0,
		Maximum = 1.0
	};

	private readonly Slider volume = new Slider
	{
		Minimum = 0.0,
		Maximum = 1.0
	};

	private readonly TextBox direction = new TextBox
	{
		AcceptsReturn = true,
		TextWrapping = TextWrapping.Wrap,
		Height = 65.0,
		MaxLength = 300
	};

	private readonly TextBox maximum = new TextBox();

	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Foreground = Ui.Brush("Text.Secondary")
	};

	private readonly Func<VoiceOptions, bool, Task> apply;

	private readonly SpeechService speech;

	private readonly Button remove = new Button
	{
		Content = "Delete selected clone",
		Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
	};

	public VoiceWindow(VoiceOptions options, SpeechService speech, Func<VoiceOptions, bool, Task> apply)
	{
		VoiceWindow voiceWindow = this;
		this.speech = speech;
		this.apply = apply;
		base.Title = "Aquila · Voice";
		base.Width = 650.0;
		base.Height = Math.Min(850.0, SystemParameters.WorkArea.Height - 30.0);
		base.MinWidth = 560.0;
		base.MinHeight = 600.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		DockPanel dockPanel = (DockPanel)(base.Content = new DockPanel
		{
			Margin = new Thickness(22.0)
		});
		StackPanel stackPanel = new StackPanel();
		DockPanel.SetDock(stackPanel, Dock.Top);
		dockPanel.Children.Add(stackPanel);
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Voice & delivery",
			Style = Ui.Style("Type.Title")
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Choose a preset or import a local voice sample for Kitten TTS 2.",
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 10.0, 0.0, 15.0),
			Foreground = Ui.Brush("Text.Secondary")
		});
		StackPanel stackPanel2 = new StackPanel();
		DockPanel.SetDock(stackPanel2, Dock.Bottom);
		dockPanel.Children.Add(stackPanel2);
		WrapPanel wrapPanel = new WrapPanel
		{
			Margin = new Thickness(0.0, 15.0, 0.0, 12.0)
		};
		stackPanel2.Children.Add(wrapPanel);
		AddButton(wrapPanel, "Save voice", async delegate
		{
			await apply(voiceWindow.Read(), arg2: false);
			voiceWindow.status.Text = "Voice saved · applies to the next reply";
		});
		AddButton(wrapPanel, "Play sample", async delegate
		{
			await apply(voiceWindow.Read(), arg2: true);
		});
		AddButton(wrapPanel, "Stop speaking", delegate
		{
			speech.Stop();
			return Task.CompletedTask;
		});
		stackPanel2.Children.Add(status);
		ScrollViewer scrollViewer = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		};
		dockPanel.Children.Add(scrollViewer);
		StackPanel stackPanel3 = (StackPanel)(scrollViewer.Content = new StackPanel());
		stackPanel3.Children.Add(enabled);
		Label(stackPanel3, "SELECTED VOICE");
		speaker.DisplayMemberPath = "Label";
		speaker.SelectedValuePath = "Key";
		stackPanel3.Children.Add(speaker);
		Name(speaker, "Selected voice");
		WrapPanel wrapPanel2 = new WrapPanel
		{
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
		};
		stackPanel3.Children.Add(wrapPanel2);
		AddButton(wrapPanel2, "Import voice sample…", delegate
		{
			VoiceImportWindow voiceImportWindow = new VoiceImportWindow(speech, voiceWindow.Read())
			{
				Owner = voiceWindow
			};
			if (voiceImportWindow.ShowDialog() == true)
			{
				VoiceReference imported = voiceImportWindow.Imported;
				if ((object)imported != null)
				{
					voiceWindow.RefreshVoices("Clone: " + imported.Id);
					voiceWindow.status.Text = "Voice imported. Save voice in this window to use it for replies.";
				}
			}
			return Task.CompletedTask;
		});
		wrapPanel2.Children.Add(remove);
		remove.Click += async delegate
		{
			try
			{
				VoiceOptions current = voiceWindow.Read();
				string id = current.CloneId;
				if (id != null && AquilaDialog.Confirm(voiceWindow, "Delete this voice?", "The saved sample and transcript for this voice will be deleted.", "Delete", "Cancel", DialogKind.Danger))
				{
					current.Speaker = "Kitten: Luna";
					await apply(current, arg2: false);
					speech.Voices.Delete(id);
					voiceWindow.RefreshVoices(current.Speaker);
					voiceWindow.status.Text = "Cloned voice deleted.";
				}
			}
			catch (Exception ex)
			{
				voiceWindow.status.Text = ex.Message.Truncate(400);
			}
		};
		Label(stackPanel3, "DELIVERY");
		mood.ItemsSource = new string[1] { "Auto" }.Concat(VoiceOptions.Moods);
		mood.SelectedItem = options.Mood;
		stackPanel3.Children.Add(mood);
		Name(mood, "Voice mood");
		autoTags.IsChecked = options.AutoTags;
		revealTags.IsChecked = options.RevealTags;
		autoTags.ToolTip = "Let the chat model change the delivery at suitable points or follow your instructions in the message.";
		revealTags.ToolTip = "Show saved voice tags in the chat. Copy and Export chat still use the written reply.";
		stackPanel3.Children.Add(autoTags);
		stackPanel3.Children.Add(revealTags);
		Label(stackPanel3, "EXPRESSION · SUBTLE TO CLEAR");
		intensity.Value = options.Intensity;
		stackPanel3.Children.Add(intensity);
		Name(intensity, "Expression intensity");
		Label(stackPanel3, "PACE");
		pace.ItemsSource = new string[3] { "Slow", "Natural", "Lively" };
		pace.SelectedItem = options.Pace;
		stackPanel3.Children.Add(pace);
		Name(pace, "Speaking pace");
		Label(stackPanel3, "OPTIONAL DELIVERY DIRECTION");
		direction.Text = options.Direction;
		stackPanel3.Children.Add(direction);
		Name(direction, "Voice direction");
		stackPanel3.Children.Add(directionHint);
		Label(stackPanel3, "VOLUME");
		volume.Value = options.Volume;
		stackPanel3.Children.Add(volume);
		Name(volume, "Voice volume");
		Label(stackPanel3, "EXPRESSIVE SPEECH COMPUTE");
		device.ItemsSource = new string[3] { "auto", "gpu", "cpu" };
		device.SelectedItem = options.Device;
		stackPanel3.Children.Add(device);
		Name(device, "Speech compute");
		keep.IsChecked = options.KeepLoaded;
		stackPanel3.Children.Add(keep);
		stackPanel3.Children.Add(new TextBlock
		{
			Text = "Auto prefers CPU for Kitten when enough RAM is available and can use GPU when it has room. Keeping the model loaded reduces the next reply’s delay and uses more memory.",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Ui.Brush("Text.Secondary"),
			FontSize = 12.0,
			Margin = new Thickness(0.0, 7.0, 0.0, 0.0)
		});
		Label(stackPanel3, "MAXIMUM SPOKEN CHARACTERS PER REPLY (500–12,000)");
		maximum.Text = options.MaxCharacters.ToString();
		stackPanel3.Children.Add(maximum);
		Name(maximum, "Maximum spoken characters");
		enabled.IsChecked = options.Enabled;
		status.Text = speech.LastStatus;
		speech.Status += UpdateStatus;
		base.Closed += delegate
		{
			speech.Status -= voiceWindow.UpdateStatus;
		};
		speaker.SelectionChanged += delegate
		{
			voiceWindow.UpdateVoiceControls();
		};
		RefreshVoices(options.Speaker);
	}

	private void RefreshVoices(string key)
	{
		VoiceChoice[] array = VoiceOptions.KittenSpeakers.Select((string name) => new VoiceChoice("Kitten: " + name, "Kitten: " + name)).Concat(from voice in speech.Voices.List()
			select new VoiceChoice("Clone: " + voice.Id, "Clone: " + voice.Name)).Concat(VoiceOptions.Speakers.Select((string name) => new VoiceChoice(name, "Qwen: " + name)))
			.Concat(from name in SpeechService.WindowsVoices()
				select new VoiceChoice(name, name))
			.ToArray();
		speaker.ItemsSource = array;
		speaker.SelectedValue = (array.Any((VoiceChoice voice) => voice.Key == key) ? key : "Kitten: Luna");
	}

	private void UpdateVoiceControls()
	{
		VoiceOptions voiceOptions = new VoiceOptions
		{
			Speaker = ((speaker.SelectedValue as string) ?? "Kitten: Luna")
		};
		remove.IsEnabled = voiceOptions.CloneId != null;
		TextBox textBox = direction;
		CheckBox checkBox = autoTags;
		bool flag = (intensity.IsEnabled = !voiceOptions.UsesWindows);
		bool isEnabled = (checkBox.IsEnabled = flag);
		textBox.IsEnabled = isEnabled;
		directionHint.Text = (voiceOptions.UsesKitten ? "Describe how replies should sound. For example, “start reassuring, then get excited for good news.” For Play sample, choose a mood or include a tag like [joyful]." : (voiceOptions.UsesWindows ? "Windows voices support pace and volume." : "Describe the delivery you want the voice to use."));
	}

	private void UpdateStatus(string text)
	{
		base.Dispatcher.InvokeAsync(() => status.Text = text);
	}

	private VoiceOptions Read()
	{
		if (!int.TryParse(maximum.Text, out var result))
		{
			throw new ArgumentException("Enter a whole-number speech length limit.");
		}
		VoiceOptions voiceOptions = new VoiceOptions();
		voiceOptions.Enabled = enabled.IsChecked == true;
		voiceOptions.AutoTags = autoTags.IsChecked == true;
		voiceOptions.RevealTags = revealTags.IsChecked == true;
		voiceOptions.Speaker = (speaker.SelectedValue as string) ?? "Kitten: Luna";
		voiceOptions.Device = device.SelectedItem?.ToString() ?? "auto";
		voiceOptions.Mood = mood.SelectedItem?.ToString() ?? "Auto";
		voiceOptions.Pace = pace.SelectedItem?.ToString() ?? "Natural";
		voiceOptions.Direction = direction.Text;
		voiceOptions.Intensity = intensity.Value;
		voiceOptions.Volume = (float)volume.Value;
		voiceOptions.KeepLoaded = keep.IsChecked == true;
		voiceOptions.MaxCharacters = result;
		voiceOptions.Validate();
		return voiceOptions;
	}

	private void AddButton(Panel panel, string text, Func<Task> action)
	{
		Button button = new Button
		{
			Content = text,
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
		};
		button.Click += async delegate
		{
			try
			{
				await action();
			}
			catch (Exception ex)
			{
				status.Text = ex.Message;
			}
		};
		panel.Children.Add(button);
	}

	private static void Label(Panel p, string text)
	{
		p.Children.Add(new TextBlock
		{
			Text = text,
			FontSize = 12.0,
			Foreground = Ui.Brush("Text.Secondary"),
			Margin = new Thickness(0.0, 14.0, 0.0, 7.0)
		});
	}

	private new static void Name(DependencyObject element, string name)
	{
		AutomationProperties.SetName(element, name);
	}
}
