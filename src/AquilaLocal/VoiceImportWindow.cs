using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
		TextWrapping = TextWrapping.Wrap
	};

	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
	};

	private string? selected;

	private bool previewing;

	public VoiceReference? Imported { get; private set; }

	public VoiceImportWindow(SpeechService speech, VoiceOptions options)
	{
		VoiceImportWindow voiceImportWindow = this;
		base.Title = "Aquila · Import voice sample";
		base.Width = 550.0;
		base.SizeToContent = SizeToContent.Height;
		base.MaxHeight = SystemParameters.WorkArea.Height - 30.0;
		base.ResizeMode = ResizeMode.NoResize;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(22.0)
		};
		base.Content = new ScrollViewer
		{
			Content = stackPanel,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Choose a 5–30 second WAV of one speaker, then enter the words spoken in the clip. Aquila stores the sample and transcript on this computer.",
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		});
		Button choose = new Button
		{
			Content = "Choose WAV…"
		};
		stackPanel.Children.Add(choose);
		AddField(stackPanel, "Voice name", name);
		AddField(stackPanel, "Transcript", transcript);
		WrapPanel wrapPanel = new WrapPanel
		{
			Margin = new Thickness(0.0, 14.0, 0.0, 0.0)
		};
		stackPanel.Children.Add(wrapPanel);
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
		Button[] array = new Button[3] { preview, stop, save };
		foreach (Button button in array)
		{
			button.Margin = new Thickness(0.0, 0.0, 8.0, 0.0);
			wrapPanel.Children.Add(button);
		}
		stackPanel.Children.Add(status);
		choose.Click += delegate
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "WAV recordings (*.wav)|*.wav",
				CheckFileExists = true
			};
			if (openFileDialog.ShowDialog(voiceImportWindow) == true)
			{
				voiceImportWindow.selected = openFileDialog.FileName;
				choose.Content = Path.GetFileName(voiceImportWindow.selected).Truncate(44);
				if (string.IsNullOrWhiteSpace(voiceImportWindow.name.Text))
				{
					voiceImportWindow.name.Text = Path.GetFileNameWithoutExtension(voiceImportWindow.selected).Truncate(60);
				}
				voiceImportWindow.status.Text = "";
				Refresh();
			}
		};
		name.TextChanged += delegate
		{
			Refresh();
		};
		transcript.TextChanged += delegate
		{
			Refresh();
		};
		stop.Click += delegate
		{
			speech.Stop();
		};
		preview.Click += async delegate
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
				await speech.SpeakAsync(turn, new VoiceOptions
				{
					Speaker = "Clone: " + draft.Id,
					Device = options.Device,
					Volume = options.Volume,
					Pace = options.Pace
				}, default(CancellationToken), draft);
				voiceImportWindow.status.Text = ((turn.SpeechStatus == "complete") ? "Preview complete. Save the voice to use it in chat." : speech.LastStatus);
			}
			catch (Exception ex)
			{
				voiceImportWindow.status.Text = ex.Message.Truncate(400);
			}
			finally
			{
				if ((object)draft != null)
				{
					try
					{
						speech.Voices.Discard(draft);
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
		save.Click += delegate
		{
			VoiceReference voiceReference = null;
			try
			{
				voiceReference = Prepare();
				voiceImportWindow.Imported = speech.Voices.Save(voiceReference);
				voiceImportWindow.DialogResult = true;
			}
			catch (Exception ex)
			{
				if ((object)voiceReference != null)
				{
					try
					{
						speech.Voices.Discard(voiceReference);
					}
					catch (IOException)
					{
					}
				}
				voiceImportWindow.status.Text = ex.Message.Truncate(400);
			}
		};
		speech.Status += Progress;
		base.Closing += delegate(object? _, CancelEventArgs e)
		{
			if (voiceImportWindow.previewing)
			{
				e.Cancel = true;
				speech.Stop();
			}
		};
		base.Closed += delegate
		{
			speech.Status -= Progress;
		};
		VoiceReference Prepare()
		{
			return speech.Voices.Prepare(voiceImportWindow.selected, voiceImportWindow.name.Text, voiceImportWindow.transcript.Text);
		}
		void Progress(string message)
		{
			voiceImportWindow.Dispatcher.InvokeAsync(delegate
			{
				if (voiceImportWindow.previewing)
				{
					voiceImportWindow.status.Text = message;
				}
			});
		}
		void Refresh()
		{
			Button button2 = choose;
			TextBox textBox = voiceImportWindow.name;
			bool flag = (voiceImportWindow.transcript.IsEnabled = !voiceImportWindow.previewing);
			bool isEnabled = (textBox.IsEnabled = flag);
			button2.IsEnabled = isEnabled;
			stop.IsEnabled = voiceImportWindow.previewing;
			Button button3 = preview;
			isEnabled = (save.IsEnabled = !voiceImportWindow.previewing && voiceImportWindow.selected != null && !string.IsNullOrWhiteSpace(voiceImportWindow.name.Text) && !string.IsNullOrWhiteSpace(voiceImportWindow.transcript.Text));
			button3.IsEnabled = isEnabled;
		}
	}

	private static void AddField(StackPanel panel, string title, TextBox field)
	{
		panel.Children.Add(new TextBlock
		{
			Text = title,
			Margin = new Thickness(0.0, 12.0, 0.0, 6.0)
		});
		AutomationProperties.SetName(field, title);
		panel.Children.Add(field);
	}
}
