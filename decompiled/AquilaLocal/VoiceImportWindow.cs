using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AquilaLocal;

internal sealed class VoiceImportWindow : Window
{
	private readonly TextBox name = new TextBox
	{
		MaxLength = 60
	};

	private readonly TextBox transcript = new TextBox
	{
		MaxLength = 1000,
		Height = 110.0,
		AcceptsReturn = true,
		TextWrapping = (TextWrapping)2
	};

	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = (TextWrapping)2,
		Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
	};

	private string? selected;

	private bool previewing;

	public VoiceReference? Imported { get; private set; }

	public VoiceImportWindow(SpeechService speech, VoiceOptions options)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Expected O, but got Unknown
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Expected O, but got Unknown
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Expected O, but got Unknown
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Expected O, but got Unknown
		//IL_011d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0122: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Expected O, but got Unknown
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0140: Unknown result type (might be due to invalid IL or missing references)
		//IL_014b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0152: Unknown result type (might be due to invalid IL or missing references)
		//IL_0177: Unknown result type (might be due to invalid IL or missing references)
		//IL_0186: Expected O, but got Unknown
		//IL_0188: Unknown result type (might be due to invalid IL or missing references)
		//IL_018d: Unknown result type (might be due to invalid IL or missing references)
		//IL_019d: Expected O, but got Unknown
		//IL_01d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0206: Expected O, but got Unknown
		//IL_0214: Unknown result type (might be due to invalid IL or missing references)
		//IL_0219: Unknown result type (might be due to invalid IL or missing references)
		//IL_0224: Unknown result type (might be due to invalid IL or missing references)
		//IL_0230: Expected O, but got Unknown
		//IL_0231: Unknown result type (might be due to invalid IL or missing references)
		//IL_0236: Unknown result type (might be due to invalid IL or missing references)
		//IL_0241: Unknown result type (might be due to invalid IL or missing references)
		//IL_024d: Expected O, but got Unknown
		//IL_024e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0253: Unknown result type (might be due to invalid IL or missing references)
		//IL_025e: Unknown result type (might be due to invalid IL or missing references)
		//IL_026a: Expected O, but got Unknown
		//IL_02bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_0304: Unknown result type (might be due to invalid IL or missing references)
		//IL_030e: Expected O, but got Unknown
		//IL_031b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0325: Expected O, but got Unknown
		//IL_0332: Unknown result type (might be due to invalid IL or missing references)
		//IL_033c: Expected O, but got Unknown
		//IL_034e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0358: Expected O, but got Unknown
		//IL_0365: Unknown result type (might be due to invalid IL or missing references)
		//IL_036f: Expected O, but got Unknown
		//IL_0381: Unknown result type (might be due to invalid IL or missing references)
		//IL_038b: Expected O, but got Unknown
		SpeechService speech2 = speech;
		VoiceOptions options2 = options;
		((Window)this)._002Ector();
		VoiceImportWindow voiceImportWindow = this;
		((Window)this).Title = "Aquila · Import voice sample";
		((FrameworkElement)this).Width = 550.0;
		((Window)this).SizeToContent = (SizeToContent)2;
		Rect workArea = SystemParameters.WorkArea;
		((FrameworkElement)this).MaxHeight = ((Rect)(ref workArea)).Height - 30.0;
		((Window)this).ResizeMode = (ResizeMode)0;
		((Window)this).WindowStartupLocation = (WindowStartupLocation)2;
		StackPanel val = new StackPanel
		{
			Margin = new Thickness(22.0)
		};
		((ContentControl)this).Content = (object)new ScrollViewer
		{
			Content = val,
			VerticalScrollBarVisibility = (ScrollBarVisibility)1
		};
		((Panel)val).Children.Add((UIElement)new TextBlock
		{
			Text = "Choose a 5–30 second WAV of one speaker, then enter the words spoken in the clip. Aquila stores the sample and transcript on this computer.",
			TextWrapping = (TextWrapping)2,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		});
		Button choose = new Button
		{
			Content = "Choose WAV…"
		};
		((Panel)val).Children.Add((UIElement)(object)choose);
		AddField(val, "Voice name", name);
		AddField(val, "Transcript", transcript);
		WrapPanel val2 = new WrapPanel
		{
			Margin = new Thickness(0.0, 14.0, 0.0, 0.0)
		};
		((Panel)val).Children.Add((UIElement)(object)val2);
		Button preview = new Button
		{
			Content = "Preview clone",
			IsEnabled = false
		};
		Button stop = new Button
		{
			Content = "Stop preview",
			IsEnabled = false
		};
		Button save = new Button
		{
			Content = "Save voice",
			IsEnabled = false
		};
		Button[] array = (Button[])(object)new Button[3] { preview, stop, save };
		foreach (Button val3 in array)
		{
			((FrameworkElement)val3).Margin = new Thickness(0.0, 0.0, 8.0, 0.0);
			((Panel)val2).Children.Add((UIElement)(object)val3);
		}
		((Panel)val).Children.Add((UIElement)(object)status);
		((ButtonBase)choose).Click += (RoutedEventHandler)delegate
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Unknown result type (might be due to invalid IL or missing references)
			//IL_0018: Expected O, but got Unknown
			OpenFileDialog val4 = new OpenFileDialog
			{
				Filter = "WAV recordings (*.wav)|*.wav",
				CheckFileExists = true
			};
			if (((CommonDialog)val4).ShowDialog((Window)(object)voiceImportWindow).GetValueOrDefault())
			{
				voiceImportWindow.selected = ((FileDialog)val4).FileName;
				((ContentControl)choose).Content = Path.GetFileName(voiceImportWindow.selected).Truncate(44);
				if (string.IsNullOrWhiteSpace(voiceImportWindow.name.Text))
				{
					voiceImportWindow.name.Text = Path.GetFileNameWithoutExtension(voiceImportWindow.selected).Truncate(60);
				}
				voiceImportWindow.status.Text = "";
				Refresh();
			}
		};
		((TextBoxBase)name).TextChanged += (TextChangedEventHandler)delegate
		{
			Refresh();
		};
		((TextBoxBase)transcript).TextChanged += (TextChangedEventHandler)delegate
		{
			Refresh();
		};
		((ButtonBase)stop).Click += (RoutedEventHandler)delegate
		{
			speech2.Stop();
		};
		((ButtonBase)preview).Click += (RoutedEventHandler)async delegate
		{
			VoiceReference draft = null;
			voiceImportWindow.previewing = true;
			Refresh();
			try
			{
				draft = Prepare();
				Turn turn = new Turn
				{
					Answer = "This is a preview of your new voice. How does it sound?",
					SpeechMood = "neutral"
				};
				await speech2.SpeakAsync(turn, new VoiceOptions
				{
					Speaker = "Clone: " + draft.Id,
					Device = options2.Device,
					Volume = options2.Volume,
					Pace = options2.Pace
				}, default(CancellationToken), draft);
				voiceImportWindow.status.Text = ((turn.SpeechStatus == "complete") ? "Preview complete. Save the voice to use it in chat." : speech2.LastStatus);
			}
			catch (Exception ex3)
			{
				voiceImportWindow.status.Text = ex3.Message.Truncate(400);
			}
			finally
			{
				if ((object)draft != null)
				{
					try
					{
						speech2.Voices.Discard(draft);
					}
					catch (IOException)
					{
						voiceImportWindow.status.Text = "The temporary preview could not be removed.";
					}
				}
				voiceImportWindow.previewing = false;
				Refresh();
			}
		};
		((ButtonBase)save).Click += (RoutedEventHandler)delegate
		{
			VoiceReference voiceReference = null;
			try
			{
				voiceReference = Prepare();
				voiceImportWindow.Imported = speech2.Voices.Save(voiceReference);
				((Window)voiceImportWindow).DialogResult = true;
			}
			catch (Exception ex2)
			{
				if ((object)voiceReference != null)
				{
					try
					{
						speech2.Voices.Discard(voiceReference);
					}
					catch (IOException)
					{
					}
				}
				voiceImportWindow.status.Text = ex2.Message.Truncate(400);
			}
		};
		speech2.Status += Progress;
		((Window)this).Closing += delegate(object? _, CancelEventArgs e)
		{
			if (voiceImportWindow.previewing)
			{
				e.Cancel = true;
				speech2.Stop();
			}
		};
		((Window)this).Closed += delegate
		{
			speech2.Status -= Progress;
		};
		VoiceReference Prepare()
		{
			return speech2.Voices.Prepare(voiceImportWindow.selected, voiceImportWindow.name.Text, voiceImportWindow.transcript.Text);
		}
		void Progress(string message)
		{
			string message2 = message;
			((DispatcherObject)voiceImportWindow).Dispatcher.InvokeAsync((Action)delegate
			{
				if (voiceImportWindow.previewing)
				{
					voiceImportWindow.status.Text = message2;
				}
			});
		}
		void Refresh()
		{
			Button obj = choose;
			TextBox obj2 = voiceImportWindow.name;
			bool flag2 = (((UIElement)voiceImportWindow.transcript).IsEnabled = !voiceImportWindow.previewing);
			bool isEnabled = (((UIElement)obj2).IsEnabled = flag2);
			((UIElement)obj).IsEnabled = isEnabled;
			((UIElement)stop).IsEnabled = voiceImportWindow.previewing;
			Button obj3 = preview;
			isEnabled = (((UIElement)save).IsEnabled = !voiceImportWindow.previewing && voiceImportWindow.selected != null && !string.IsNullOrWhiteSpace(voiceImportWindow.name.Text) && !string.IsNullOrWhiteSpace(voiceImportWindow.transcript.Text));
			((UIElement)obj3).IsEnabled = isEnabled;
		}
	}

	private static void AddField(StackPanel panel, string title, TextBox field)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Expected O, but got Unknown
		((Panel)panel).Children.Add((UIElement)new TextBlock
		{
			Text = title,
			Margin = new Thickness(0.0, 12.0, 0.0, 6.0)
		});
		AutomationProperties.SetName((DependencyObject)(object)field, title);
		((Panel)panel).Children.Add((UIElement)(object)field);
	}
}
