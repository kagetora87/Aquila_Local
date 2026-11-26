using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace AquilaLocal;

public sealed class VoiceWindow : Window
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
		TextWrapping = (TextWrapping)2,
		Foreground = (Brush)(object)Brushes.LightGray,
		FontSize = 11.0,
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
		TextWrapping = (TextWrapping)2,
		Height = 65.0,
		MaxLength = 300
	};

	private readonly TextBox maximum = new TextBox();

	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = (TextWrapping)2,
		Foreground = (Brush)(object)Brushes.LightGray
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
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Expected O, but got Unknown
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Expected O, but got Unknown
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Expected O, but got Unknown
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Expected O, but got Unknown
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Expected O, but got Unknown
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Expected O, but got Unknown
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Expected O, but got Unknown
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d4: Expected O, but got Unknown
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Expected O, but got Unknown
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Expected O, but got Unknown
		//IL_0109: Unknown result type (might be due to invalid IL or missing references)
		//IL_010e: Unknown result type (might be due to invalid IL or missing references)
		//IL_011d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0131: Expected O, but got Unknown
		//IL_0132: Unknown result type (might be due to invalid IL or missing references)
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0145: Unknown result type (might be due to invalid IL or missing references)
		//IL_0154: Unknown result type (might be due to invalid IL or missing references)
		//IL_0164: Expected O, but got Unknown
		//IL_0165: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Expected O, but got Unknown
		//IL_0170: Unknown result type (might be due to invalid IL or missing references)
		//IL_0175: Unknown result type (might be due to invalid IL or missing references)
		//IL_017c: Unknown result type (might be due to invalid IL or missing references)
		//IL_018c: Expected O, but got Unknown
		//IL_018d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0192: Unknown result type (might be due to invalid IL or missing references)
		//IL_019d: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d1: Expected O, but got Unknown
		//IL_022e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0233: Unknown result type (might be due to invalid IL or missing references)
		//IL_027c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0281: Unknown result type (might be due to invalid IL or missing references)
		//IL_028b: Expected O, but got Unknown
		//IL_02a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02aa: Expected O, but got Unknown
		//IL_02b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ba: Expected O, but got Unknown
		//IL_02c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e3: Expected O, but got Unknown
		//IL_02ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f0: Expected O, but got Unknown
		//IL_030a: Unknown result type (might be due to invalid IL or missing references)
		//IL_030f: Unknown result type (might be due to invalid IL or missing references)
		//IL_031a: Unknown result type (might be due to invalid IL or missing references)
		//IL_032e: Expected O, but got Unknown
		//IL_0335: Unknown result type (might be due to invalid IL or missing references)
		//IL_033a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0345: Unknown result type (might be due to invalid IL or missing references)
		//IL_034c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0371: Unknown result type (might be due to invalid IL or missing references)
		//IL_037b: Unknown result type (might be due to invalid IL or missing references)
		//IL_038b: Expected O, but got Unknown
		//IL_038c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0392: Expected O, but got Unknown
		//IL_03a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_03dc: Expected O, but got Unknown
		//IL_0447: Unknown result type (might be due to invalid IL or missing references)
		//IL_044c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0455: Expected O, but got Unknown
		//IL_0463: Unknown result type (might be due to invalid IL or missing references)
		//IL_046a: Expected O, but got Unknown
		//IL_04d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_04da: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_050b: Expected O, but got Unknown
		//IL_0553: Unknown result type (might be due to invalid IL or missing references)
		//IL_055d: Expected O, but got Unknown
		//IL_0808: Unknown result type (might be due to invalid IL or missing references)
		//IL_080d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0818: Unknown result type (might be due to invalid IL or missing references)
		//IL_081f: Unknown result type (might be due to invalid IL or missing references)
		//IL_082a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0839: Unknown result type (might be due to invalid IL or missing references)
		//IL_085e: Unknown result type (might be due to invalid IL or missing references)
		//IL_086d: Expected O, but got Unknown
		//IL_0919: Unknown result type (might be due to invalid IL or missing references)
		//IL_0923: Expected O, but got Unknown
		Func<VoiceOptions, bool, Task> apply2 = apply;
		SpeechService speech2 = speech;
		((Window)this)._002Ector();
		VoiceWindow voiceWindow = this;
		this.speech = speech2;
		this.apply = apply2;
		((Window)this).Title = "Aquila · Voice";
		((FrameworkElement)this).Width = 650.0;
		Rect workArea = SystemParameters.WorkArea;
		((FrameworkElement)this).Height = Math.Min(850.0, ((Rect)(ref workArea)).Height - 30.0);
		((FrameworkElement)this).MinWidth = 560.0;
		((FrameworkElement)this).MinHeight = 600.0;
		((Window)this).WindowStartupLocation = (WindowStartupLocation)2;
		((Control)this).Background = (Brush)new SolidColorBrush(Color.FromRgb((byte)16, (byte)23, (byte)32));
		((Control)this).Foreground = (Brush)Application.Current.Resources[(object)"Ink"];
		((Control)this).FontFamily = new FontFamily("Segoe UI");
		((Control)this).FontSize = 13.0;
		DockPanel val = (DockPanel)(((ContentControl)this).Content = (object)new DockPanel
		{
			Margin = new Thickness(22.0)
		});
		StackPanel val2 = new StackPanel();
		DockPanel.SetDock((UIElement)(object)val2, (Dock)1);
		((Panel)val).Children.Add((UIElement)(object)val2);
		((Panel)val2).Children.Add((UIElement)new TextBlock
		{
			Text = "Voice & delivery",
			FontSize = 25.0
		});
		((Panel)val2).Children.Add((UIElement)new TextBlock
		{
			Text = "Choose a preset or import a local voice sample for Kitten TTS 2.",
			TextWrapping = (TextWrapping)2,
			Margin = new Thickness(0.0, 10.0, 0.0, 15.0),
			Foreground = (Brush)(object)Brushes.LightGray
		});
		StackPanel val3 = new StackPanel();
		DockPanel.SetDock((UIElement)(object)val3, (Dock)3);
		((Panel)val).Children.Add((UIElement)(object)val3);
		WrapPanel val4 = new WrapPanel
		{
			Margin = new Thickness(0.0, 15.0, 0.0, 12.0)
		};
		((Panel)val3).Children.Add((UIElement)(object)val4);
		AddButton((Panel)(object)val4, "Save voice", async delegate
		{
			await apply2(voiceWindow.Read(), arg2: false);
			voiceWindow.status.Text = "Voice saved · applies to the next reply";
		});
		AddButton((Panel)(object)val4, "Play sample", async delegate
		{
			await apply2(voiceWindow.Read(), arg2: true);
		});
		AddButton((Panel)(object)val4, "Stop speaking", delegate
		{
			speech2.Stop();
			return Task.CompletedTask;
		});
		((Panel)val3).Children.Add((UIElement)(object)status);
		ScrollViewer val5 = new ScrollViewer
		{
			VerticalScrollBarVisibility = (ScrollBarVisibility)1
		};
		((Panel)val).Children.Add((UIElement)(object)val5);
		StackPanel val6 = (StackPanel)(((ContentControl)val5).Content = (object)new StackPanel());
		((Panel)val6).Children.Add((UIElement)(object)enabled);
		Label((Panel)(object)val6, "SELECTED VOICE");
		((ItemsControl)speaker).DisplayMemberPath = "Label";
		((Selector)speaker).SelectedValuePath = "Key";
		((Panel)val6).Children.Add((UIElement)(object)speaker);
		Name((DependencyObject)(object)speaker, "Selected voice");
		WrapPanel val7 = new WrapPanel
		{
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
		};
		((Panel)val6).Children.Add((UIElement)(object)val7);
		AddButton((Panel)(object)val7, "Import voice sample…", delegate
		{
			VoiceImportWindow voiceImportWindow = new VoiceImportWindow(speech2, voiceWindow.Read());
			((Window)voiceImportWindow).Owner = (Window)(object)voiceWindow;
			VoiceImportWindow voiceImportWindow2 = voiceImportWindow;
			if (((Window)voiceImportWindow2).ShowDialog().GetValueOrDefault())
			{
				VoiceReference imported = voiceImportWindow2.Imported;
				if ((object)imported != null)
				{
					voiceWindow.RefreshVoices("Clone: " + imported.Id);
					voiceWindow.status.Text = "Voice imported. Save voice in this window to use it for replies.";
				}
			}
			return Task.CompletedTask;
		});
		((Panel)val7).Children.Add((UIElement)(object)remove);
		((ButtonBase)remove).Click += (RoutedEventHandler)async delegate
		{
			try
			{
				VoiceOptions current = voiceWindow.Read();
				string id = current.CloneId;
				if (id != null && (int)MessageBox.Show((Window)(object)voiceWindow, "Delete the saved sample and transcript for this voice?", "Delete voice", (MessageBoxButton)4, (MessageBoxImage)48, (MessageBoxResult)7) == 6)
				{
					current.Speaker = "Kitten: Luna";
					await apply2(current, arg2: false);
					speech2.Voices.Delete(id);
					voiceWindow.RefreshVoices(current.Speaker);
					voiceWindow.status.Text = "Cloned voice deleted.";
				}
			}
			catch (Exception ex)
			{
				voiceWindow.status.Text = ex.Message.Truncate(400);
			}
		};
		Label((Panel)(object)val6, "DELIVERY");
		((ItemsControl)mood).ItemsSource = new string[1] { "Auto" }.Concat(VoiceOptions.Moods);
		((Selector)mood).SelectedItem = options.Mood;
		((Panel)val6).Children.Add((UIElement)(object)mood);
		Name((DependencyObject)(object)mood, "Voice mood");
		((ToggleButton)autoTags).IsChecked = options.AutoTags;
		((ToggleButton)revealTags).IsChecked = options.RevealTags;
		((FrameworkElement)autoTags).ToolTip = "Let the chat model change the delivery at suitable points or follow your instructions in the message.";
		((FrameworkElement)revealTags).ToolTip = "Show saved voice tags in the chat. Copy and Export chat still use the written reply.";
		((Panel)val6).Children.Add((UIElement)(object)autoTags);
		((Panel)val6).Children.Add((UIElement)(object)revealTags);
		Label((Panel)(object)val6, "EXPRESSION · SUBTLE TO CLEAR");
		((RangeBase)intensity).Value = options.Intensity;
		((Panel)val6).Children.Add((UIElement)(object)intensity);
		Name((DependencyObject)(object)intensity, "Expression intensity");
		Label((Panel)(object)val6, "PACE");
		((ItemsControl)pace).ItemsSource = new string[3] { "Slow", "Natural", "Lively" };
		((Selector)pace).SelectedItem = options.Pace;
		((Panel)val6).Children.Add((UIElement)(object)pace);
		Name((DependencyObject)(object)pace, "Speaking pace");
		Label((Panel)(object)val6, "OPTIONAL DELIVERY DIRECTION");
		direction.Text = options.Direction;
		((Panel)val6).Children.Add((UIElement)(object)direction);
		Name((DependencyObject)(object)direction, "Voice direction");
		((Panel)val6).Children.Add((UIElement)(object)directionHint);
		Label((Panel)(object)val6, "VOLUME");
		((RangeBase)volume).Value = options.Volume;
		((Panel)val6).Children.Add((UIElement)(object)volume);
		Name((DependencyObject)(object)volume, "Voice volume");
		Label((Panel)(object)val6, "EXPRESSIVE SPEECH COMPUTE");
		((ItemsControl)device).ItemsSource = new string[3] { "auto", "gpu", "cpu" };
		((Selector)device).SelectedItem = options.Device;
		((Panel)val6).Children.Add((UIElement)(object)device);
		Name((DependencyObject)(object)device, "Speech compute");
		((ToggleButton)keep).IsChecked = options.KeepLoaded;
		((Panel)val6).Children.Add((UIElement)(object)keep);
		((Panel)val6).Children.Add((UIElement)new TextBlock
		{
			Text = "Auto prefers CPU for Kitten when enough RAM is available and can use GPU when it has room. Keeping the model loaded reduces the next reply’s delay and uses more memory.",
			TextWrapping = (TextWrapping)2,
			Foreground = (Brush)(object)Brushes.LightGray,
			FontSize = 11.0,
			Margin = new Thickness(0.0, 7.0, 0.0, 0.0)
		});
		Label((Panel)(object)val6, "MAXIMUM SPOKEN CHARACTERS PER REPLY (500–12,000)");
		maximum.Text = options.MaxCharacters.ToString();
		((Panel)val6).Children.Add((UIElement)(object)maximum);
		Name((DependencyObject)(object)maximum, "Maximum spoken characters");
		((ToggleButton)enabled).IsChecked = options.Enabled;
		status.Text = speech2.LastStatus;
		speech2.Status += UpdateStatus;
		((Window)this).Closed += delegate
		{
			speech2.Status -= voiceWindow.UpdateStatus;
		};
		((Selector)speaker).SelectionChanged += (SelectionChangedEventHandler)delegate
		{
			voiceWindow.UpdateVoiceControls();
		};
		RefreshVoices(options.Speaker);
	}

	private void RefreshVoices(string key)
	{
		string key2 = key;
		VoiceChoice[] array = VoiceOptions.KittenSpeakers.Select((string name) => new VoiceChoice("Kitten: " + name, "Kitten: " + name)).Concat(from voice in speech.Voices.List()
			select new VoiceChoice("Clone: " + voice.Id, "Clone: " + voice.Name)).Concat(VoiceOptions.Speakers.Select((string name) => new VoiceChoice(name, "Qwen: " + name)))
			.Concat(from name in SpeechService.WindowsVoices()
				select new VoiceChoice(name, name))
			.ToArray();
		((ItemsControl)speaker).ItemsSource = array;
		((Selector)speaker).SelectedValue = (array.Any((VoiceChoice voice) => voice.Key == key2) ? key2 : "Kitten: Luna");
	}

	private void UpdateVoiceControls()
	{
		VoiceOptions voiceOptions = new VoiceOptions
		{
			Speaker = ((((Selector)speaker).SelectedValue as string) ?? "Kitten: Luna")
		};
		((UIElement)remove).IsEnabled = voiceOptions.CloneId != null;
		TextBox obj = direction;
		CheckBox obj2 = autoTags;
		bool flag2 = (((UIElement)intensity).IsEnabled = !voiceOptions.UsesWindows);
		bool isEnabled = (((UIElement)obj2).IsEnabled = flag2);
		((UIElement)obj).IsEnabled = isEnabled;
		directionHint.Text = (voiceOptions.UsesKitten ? "Describe how replies should sound. For example, “start reassuring, then get excited for good news.” For Play sample, choose a mood or include a tag like [joyful]." : (voiceOptions.UsesWindows ? "Windows voices support pace and volume." : "Describe the delivery you want the voice to use."));
	}

	private void UpdateStatus(string text)
	{
		string text2 = text;
		((DispatcherObject)this).Dispatcher.InvokeAsync<string>((Func<string>)(() => status.Text = text2));
	}

	private VoiceOptions Read()
	{
		if (!int.TryParse(maximum.Text, out var result))
		{
			throw new ArgumentException("Enter a whole-number speech length limit.");
		}
		VoiceOptions voiceOptions = new VoiceOptions();
		voiceOptions.Enabled = ((ToggleButton)enabled).IsChecked.GetValueOrDefault();
		voiceOptions.AutoTags = ((ToggleButton)autoTags).IsChecked.GetValueOrDefault();
		voiceOptions.RevealTags = ((ToggleButton)revealTags).IsChecked.GetValueOrDefault();
		voiceOptions.Speaker = (((Selector)speaker).SelectedValue as string) ?? "Kitten: Luna";
		voiceOptions.Device = ((Selector)device).SelectedItem?.ToString() ?? "auto";
		voiceOptions.Mood = ((Selector)mood).SelectedItem?.ToString() ?? "Auto";
		voiceOptions.Pace = ((Selector)pace).SelectedItem?.ToString() ?? "Natural";
		voiceOptions.Direction = direction.Text;
		voiceOptions.Intensity = ((RangeBase)intensity).Value;
		voiceOptions.Volume = (float)((RangeBase)volume).Value;
		voiceOptions.KeepLoaded = ((ToggleButton)keep).IsChecked.GetValueOrDefault();
		voiceOptions.MaxCharacters = result;
		voiceOptions.Validate();
		return voiceOptions;
	}

	private void AddButton(Panel panel, string text, Func<Task> action)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Expected O, but got Unknown
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Expected O, but got Unknown
		Func<Task> action2 = action;
		Button val = new Button
		{
			Content = text,
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
		};
		((ButtonBase)val).Click += (RoutedEventHandler)async delegate
		{
			try
			{
				await action2();
			}
			catch (Exception ex)
			{
				status.Text = ex.Message;
			}
		};
		panel.Children.Add((UIElement)(object)val);
	}

	private static void Label(Panel p, string text)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Expected O, but got Unknown
		p.Children.Add((UIElement)new TextBlock
		{
			Text = text,
			FontSize = 10.0,
			Foreground = (Brush)(object)Brushes.LightGray,
			Margin = new Thickness(0.0, 14.0, 0.0, 7.0)
		});
	}

	private static void Name(DependencyObject element, string name)
	{
		AutomationProperties.SetName(element, name);
	}
}
