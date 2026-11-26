using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AquilaLocal;

/// <summary>Talking to Aquila or a companion: the microphone button, live words, auto-send on a pause.</summary>
public partial class MainWindow
{
	private readonly WhisperEngine whisper = new WhisperEngine();

	private VoiceInput? listener;

	/// <summary>The next message was said aloud (set when a spoken message is put in the composer).</summary>
	private bool pendingSpoken;

	/// <summary>Something you said while she was still replying; sent as soon as she's done.</summary>
	private SpokenMessage? queuedSpoken;

	private bool listeningStarting;

	private VoiceInput Listener()
	{
		if (listener != null)
		{
			return listener;
		}
		VoiceInput input = new VoiceInput(whisper);
		input.State += delegate(string text)
		{
			base.Dispatcher.InvokeAsync(delegate
			{
				if (!featuresClosing)
				{
					MicButton.ToolTip = text + " (click to stop listening · right-click for settings)";
					ComposerHint.Text = text;
				}
			});
		};
		input.Partial += delegate(string text)
		{
			base.Dispatcher.InvokeAsync(delegate
			{
				if (!featuresClosing && listener?.Hearing == true && !busy)
				{
					Composer.Text = text + " …";
					Composer.CaretIndex = Composer.Text.Length;
				}
			});
		};
		input.SpeechStarted += delegate
		{
			base.Dispatcher.InvokeAsync(delegate
			{
				if (!featuresClosing && speech.Active && settings.Listening.Interrupt)
				{
					// You started talking while she was speaking: she stops and listens.
					speech.Stop();
				}
			});
		};
		input.Heard += delegate(SpokenMessage message)
		{
			base.Dispatcher.InvokeAsync(async delegate
			{
				if (featuresClosing)
				{
					return;
				}
				if (busy || current == null)
				{
					queuedSpoken = (queuedSpoken == null) ? message : new SpokenMessage(queuedSpoken.Text + " " + message.Text, queuedSpoken.Plain + " " + message.Plain, queuedSpoken.Notes.Concat(message.Notes).ToList(), queuedSpoken.Seconds + message.Seconds);
					ComposerHint.Text = "Got it. I'll send what you said when the reply finishes.";
					return;
				}
				await SendSpokenAsync(message);
			});
		};
		input.Problem += delegate(string text)
		{
			base.Dispatcher.InvokeAsync(delegate
			{
				if (!featuresClosing)
				{
					ShowListening(on: false);
					Notice(text);
					UiSounds.Play(UiSound.Notice);
				}
			});
		};
		listener = input;
		return input;
	}

	private async Task SendSpokenAsync(SpokenMessage message)
	{
		Composer.Text = message.Text;
		pendingSpoken = true;
		await SendAsync();
		pendingSpoken = false;
	}

	/// <summary>Sends anything you said while she was replying.</summary>
	private async Task SendQueuedSpokenAsync()
	{
		if (queuedSpoken != null && !busy && current != null)
		{
			SpokenMessage message = queuedSpoken;
			queuedSpoken = null;
			await SendSpokenAsync(message);
		}
	}

	private async void Mic_Click(object sender, RoutedEventArgs e)
	{
		if (listeningStarting)
		{
			return;
		}
		VoiceInput input = Listener();
		if (input.Listening)
		{
			input.Stop();
			ShowListening(on: false);
			return;
		}
		listeningStarting = true;
		MicButton.IsEnabled = false;
		ComposerHint.Text = "Loading the speech recognizer…";
		try
		{
			await input.StartAsync(settings.Listening, monitorCancellation.Token);
			ShowListening(on: true);
			input.SetHerVoicePlaying(speech.Active);
		}
		catch (ListeningUnavailableException ex)
		{
			ShowListening(on: false);
			Notice(ex.Message);
			UiSounds.Play(UiSound.Notice);
		}
		catch (Exception ex2) when (ex2 is System.IO.IOException || ex2 is System.Net.Http.HttpRequestException || ex2 is System.ComponentModel.Win32Exception || ex2 is UnauthorizedAccessException)
		{
			ShowListening(on: false);
			Notice("Listening couldn't start: " + ex2.Message);
			UiSounds.Play(UiSound.Notice);
		}
		catch (OperationCanceledException)
		{
			ShowListening(on: false);
		}
		finally
		{
			listeningStarting = false;
			MicButton.IsEnabled = true;
		}
	}

	private void ShowListening(bool on)
	{
		MicButton.Foreground = on ? Ui.Brush("Accent") : Ui.Brush("Text.Secondary");
		MicButton.Background = on ? Ui.Brush("Accent.Subtle") : Brushes.Transparent;
		MicButton.ToolTip = on ? "Listening (click to stop · right-click for settings)" : "Talk instead of typing (right-click for settings)";
		ComposerHint.Text = on ? "Listening… talk, then pause and it sends. Click the microphone to stop." : "Enter to send · Shift+Enter for a new line · Drop files to attach";
	}

	/// <summary>Right-click on the microphone: which microphone, how long a pause sends, language, interrupting.</summary>
	private void Mic_ContextMenuOpening(object sender, ContextMenuEventArgs e)
	{
		ContextMenu menu = MicButton.ContextMenu ?? new ContextMenu();
		menu.Items.Clear();
		MenuItem mics = new MenuItem { Header = "Microphone" };
		List<string> names;
		try
		{
			names = VoiceInput.Microphones();
		}
		catch (NAudio.MmException)
		{
			names = new List<string>();
		}
		if (names.Count == 0)
		{
			mics.Items.Add(new MenuItem { Header = "No microphone found", IsEnabled = false });
		}
		for (int i = 0; i < names.Count; i++)
		{
			int index = i;
			MenuItem item = new MenuItem { Header = names[i], IsCheckable = true, IsChecked = settings.Listening.Device == i };
			item.Click += delegate
			{
				settings.Listening.Device = index;
				SaveListening();
			};
			mics.Items.Add(item);
		}
		menu.Items.Add(mics);
		MenuItem pause = new MenuItem { Header = "Send after a pause of" };
		foreach (double seconds in new[] { 0.7, 1.0, 1.5, 2.0, 3.0 })
		{
			MenuItem item = new MenuItem { Header = seconds.ToString("0.0") + " seconds", IsCheckable = true, IsChecked = Math.Abs(settings.Listening.EndSilenceSeconds - seconds) < 0.05 };
			item.Click += delegate
			{
				settings.Listening.EndSilenceSeconds = seconds;
				SaveListening();
			};
			pause.Items.Add(item);
		}
		menu.Items.Add(pause);
		MenuItem language = new MenuItem { Header = "Language" };
		foreach ((string code, string label) in new[] { ("en", "English"), ("ja", "Japanese"), ("auto", "Detect automatically") })
		{
			MenuItem item = new MenuItem { Header = label, IsCheckable = true, IsChecked = settings.Listening.Language == code };
			item.Click += delegate
			{
				settings.Listening.Language = code;
				SaveListening();
			};
			language.Items.Add(item);
		}
		menu.Items.Add(language);
		MenuItem interrupt = new MenuItem { Header = "Talking interrupts her (use headphones)", IsCheckable = true, IsChecked = settings.Listening.Interrupt };
		interrupt.Click += delegate
		{
			settings.Listening.Interrupt = interrupt.IsChecked;
			SaveListening();
		};
		menu.Items.Add(interrupt);
		if (MicButton.ContextMenu == null)
		{
			MicButton.ContextMenu = menu;
		}
	}

	private void SaveListening()
	{
		try
		{
			store.SaveSettings(settings);
		}
		catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
		{
			Notice("Couldn't save the listening setting: " + ex.Message);
		}
		if (listener?.Listening == true)
		{
			// Restart so the new microphone or pause length takes effect right away.
			listener.Stop();
			ShowListening(on: false);
			Mic_Click(MicButton, new RoutedEventArgs());
		}
	}

	private void HerVoicePlaying(bool playing)
	{
		listener?.SetHerVoicePlaying(playing);
	}

	private void DisposeListening()
	{
		listener?.Dispose();
		listener = null;
		whisper.Dispose();
	}
}
