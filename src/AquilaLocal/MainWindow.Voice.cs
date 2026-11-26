using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Speech;

namespace AquilaLocal;

/// <summary>What the microphone is doing.</summary>
public enum VoiceMode
{
	Off,
	/// <summary>Waiting only for the wake word; everything else is thrown away.</summary>
	Standby,
	/// <summary>Heard the wake word; waiting a few seconds for a command.</summary>
	Command,
	/// <summary>Full attention: a normal spoken conversation in the current chat.</summary>
	Talk,
	/// <summary>On a call with a persona: her replies are always spoken in her voice.</summary>
	Call
}

/// <summary>
/// "Aquila" voice commands: standby listening for the wake word, then "let's talk", "call Vicki", "hang up"
/// and "stop listening". The wake word and every phrase are in Data\voice-commands.json (Voice commands…).
/// </summary>
public partial class MainWindow
{
	public const double CommandWaitSeconds = 8.0;

	public static readonly TimeSpan TalkIdle = TimeSpan.FromMinutes(2);

	private VoiceMode voiceMode = VoiceMode.Off;

	private VoiceMode modeBeforeCommand = VoiceMode.Standby;

	private DateTime commandDeadline;

	private DateTime lastSpeech = DateTime.UtcNow;

	private ChatSession? callSession;

	private VoiceCommandSet voiceCommands = VoiceCommandSet.Defaults();

	private DispatcherTimer? voiceTimer;

	private string VoiceCommandsPath => Path.Combine(store.Root, "voice-commands.json");

	/// <summary>Turns on standby listening at startup (or when the setting is switched on).</summary>
	private async Task StartWakeIfEnabledAsync()
	{
		if (!settings.Listening.WakeEnabled || voiceMode != VoiceMode.Off || featuresClosing)
		{
			return;
		}
		EnsureVoiceTimer();
		if (await EnsureListeningAsync())
		{
			EnterStandby();
		}
	}

	private void EnsureVoiceTimer()
	{
		if (voiceTimer != null)
		{
			return;
		}
		voiceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
		voiceTimer.Tick += delegate
		{
			if (featuresClosing)
			{
				voiceTimer.Stop();
				return;
			}
			if (voiceMode == VoiceMode.Command && DateTime.UtcNow > commandDeadline && listener?.Hearing != true)
			{
				ComposerHint.Text = "No command heard.";
				ResumeMode(modeBeforeCommand);
			}
			else if (voiceMode == VoiceMode.Talk && settings.Listening.WakeEnabled && !busy && !speech.Active && listener?.Hearing != true && DateTime.UtcNow - lastSpeech > TalkIdle)
			{
				EnterStandby();
			}
		};
		voiceTimer.Start();
	}

	/// <summary>Reloads the wake word and commands, and gives the recognizer the names to listen for.</summary>
	private void RefreshVoiceCommands()
	{
		try
		{
			voiceCommands = VoiceCommandSet.Load(VoiceCommandsPath);
		}
		catch (PersonaConfigException ex)
		{
			voiceCommands = VoiceCommandSet.Defaults();
			Notice("Your voice commands file has a problem, so the built-in commands are used until it's fixed: " + ex.Message);
		}
		if (listener != null)
		{
			listener.Commands = voiceCommands;
			listener.Hint = VoiceCommandMatcher.Hint(voiceCommands, VoicePersonas());
		}
	}

	private List<(string Id, string Name)> VoicePersonas()
	{
		try
		{
			return companion.Registry.Load().Personas.Select((PersonaEntry p) => (p.Id, p.Name)).ToList();
		}
		catch (PersonaConfigException)
		{
			return new List<(string, string)>();
		}
	}

	private void EnterStandby()
	{
		if (listener == null || !listener.Listening)
		{
			voiceMode = VoiceMode.Off;
			ShowListening(on: false);
			return;
		}
		EnsureVoiceTimer();
		listener.Flush();
		listener.Mode = ListenMode.Standby;
		voiceMode = VoiceMode.Standby;
		MicButton.Foreground = Ui.Brush("Accent");
		MicButton.Background = Brushes.Transparent;
		MicButton.ToolTip = "Waiting for “" + voiceCommands.WakeWord + "”. Everything else is ignored and not kept. Click to talk now · right-click for settings";
		ComposerHint.Text = "Say “" + voiceCommands.WakeWord + "” anytime · Enter to send · Shift+Enter for a new line";
	}

	private void EnterTalk()
	{
		if (listener == null || !listener.Listening)
		{
			return;
		}
		EnsureVoiceTimer();
		listener.Mode = ListenMode.Conversation;
		voiceMode = VoiceMode.Talk;
		lastSpeech = DateTime.UtcNow;
		if (current == null)
		{
			NewChat();
		}
		ShowListening(on: true);
		if (settings.Listening.WakeEnabled)
		{
			ComposerHint.Text += " Say “" + voiceCommands.WakeWord + ", stop listening” when you're done.";
		}
	}

	private void ResumeMode(VoiceMode mode)
	{
		switch (mode)
		{
		case VoiceMode.Talk:
			EnterTalk();
			break;
		case VoiceMode.Call when callSession != null && listener != null:
			listener.Mode = ListenMode.Conversation;
			voiceMode = VoiceMode.Call;
			ShowCall();
			break;
		default:
			EnterStandby();
			break;
		}
	}

	private void OnWake(string rest)
	{
		if (listener == null || voiceMode == VoiceMode.Off)
		{
			return;
		}
		if (voiceMode != VoiceMode.Command)
		{
			modeBeforeCommand = voiceMode;
		}
		if (rest.Trim().Length > 0)
		{
			OnCommand(rest);
			return;
		}
		UiSounds.Play(UiSound.Ready);
		voiceMode = VoiceMode.Command;
		listener.Mode = ListenMode.Command;
		commandDeadline = DateTime.UtcNow.AddSeconds(CommandWaitSeconds);
		string example = VoicePersonas().Select(((string Id, string Name) p) => p.Name).FirstOrDefault() ?? "someone";
		ComposerHint.Text = "Yes? Say a command, like “let's talk” or “call " + example + "”.";
	}

	private void OnCommand(string text)
	{
		VoiceMode previous = voiceMode == VoiceMode.Command ? modeBeforeCommand : voiceMode;
		CommandMatch? match = VoiceCommandMatcher.Match(text, voiceCommands, VoicePersonas(), Meaning);
		if (match == null)
		{
			Notice("“" + text + "” isn't one of your voice commands. Say “" + voiceCommands.WakeWord + "” and try again, or add it in Voice commands….");
			ResumeMode(previous);
			return;
		}
		switch (match.Action)
		{
		case VoiceActions.Talk:
			UiSounds.Play(UiSound.Ready);
			EnterTalk();
			break;
		case VoiceActions.Call:
			if (match.PersonaId == null)
			{
				Notice("There's no persona called “" + match.PersonaName + "”. Add her in Companions first.");
				UiSounds.Play(UiSound.Notice);
				ResumeMode(previous);
			}
			else
			{
				_ = StartCallAsync(match.PersonaId, match.PersonaName ?? match.PersonaId, previous);
			}
			break;
		case VoiceActions.HangUp:
			if (callSession != null)
			{
				EndCall();
			}
			else
			{
				Notice("There's no call to hang up.");
				ResumeMode(previous);
			}
			break;
		case VoiceActions.Stop:
			if (callSession != null)
			{
				EndCall();
			}
			else if (settings.Listening.WakeEnabled)
			{
				UiSounds.Play(UiSound.Stop);
				EnterStandby();
			}
			else
			{
				StopListening();
			}
			break;
		}
	}

	/// <summary>Similarity of meaning between what was said and a command phrase (local embeddings), for wordings not in the list.</summary>
	private double Meaning(string said, string phrase)
	{
		try
		{
			float[] a = companion.Embed(said);
			float[] b = companion.Embed(phrase);
			double dot = 0, na = 0, nb = 0;
			for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
			{
				dot += a[i] * b[i];
				na += a[i] * a[i];
				nb += b[i] * b[i];
			}
			return na == 0 || nb == 0 ? 0 : dot / Math.Sqrt(na * nb);
		}
		catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is UnauthorizedAccessException)
		{
			// Without the embedder only the listed phrases are understood.
			return 0;
		}
	}

	/// <summary>Opens (or continues) her call chat, answers aloud in her own voice, and keeps the microphone open.</summary>
	private async Task StartCallAsync(string personaId, string name, VoiceMode previous)
	{
		if (busy)
		{
			Notice("Wait until the current reply finishes, then call " + name + " again.");
			ResumeMode(previous);
			return;
		}
		if (!companion.IsSetUp(personaId))
		{
			Notice(name + " hasn't been set up yet, so she can't take a call. Set her up in Companions first.");
			UiSounds.Play(UiSound.Notice);
			ResumeMode(previous);
			return;
		}
		if (callSession != null && callSession.PersonaId != personaId)
		{
			EndCall();
		}
		ChatSession? session = chats.FirstOrDefault((ChatSession c) => c.IsCall && c.PersonaId == personaId);
		if (session == null)
		{
			session = new ChatSession { PersonaId = personaId, PersonaName = name, IsCall = true, Title = "Call with " + name };
			chats.Insert(0, session);
		}
		ChatsList.SelectedItem = session;
		if (WindowState == WindowState.Minimized)
		{
			WindowState = WindowState.Normal;
		}
		Activate();
		callSession = session;
		voiceMode = VoiceMode.Call;
		if (listener != null)
		{
			listener.Mode = ListenMode.Conversation;
		}
		lastSpeech = DateTime.UtcNow;
		UiSounds.Play(UiSound.Ready);
		ShowCall();
		// She picks up: a short ring line starts the call, and her answer is spoken in her own voice.
		Composer.Text = "📞 <action>calls you</action>";
		await SendAsync();
	}

	private void ShowCall()
	{
		MicButton.Foreground = Ui.Brush("Accent");
		MicButton.Background = Ui.Brush("Accent.Subtle");
		MicButton.ToolTip = "On a call with " + callSession?.PersonaName + " · click to hang up";
		ComposerHint.Text = "On a call with " + callSession?.PersonaName + ". Just talk. Say “" + voiceCommands.WakeWord + ", hang up” or click the microphone to end it.";
	}

	private void EndCall()
	{
		string name = callSession?.PersonaName ?? "";
		listener?.Flush();
		speech.Stop();
		callSession = null;
		UiSounds.Play(UiSound.Stop);
		if (settings.Listening.WakeEnabled && listener?.Listening == true)
		{
			EnterStandby();
		}
		else
		{
			StopListening();
		}
		ComposerHint.Text = "Call with " + name + " ended. " + ComposerHint.Text;
	}

	private async Task RestartListeningAsync()
	{
		VoiceMode mode = voiceMode == VoiceMode.Command ? modeBeforeCommand : voiceMode;
		listener?.Stop();
		voiceMode = VoiceMode.Off;
		if (await EnsureListeningAsync())
		{
			ResumeMode(mode);
		}
	}

	private void OpenVoiceCommands()
	{
		VoiceCommandsWindow window = new VoiceCommandsWindow(VoiceCommandsPath) { Owner = this };
		window.ShowDialog();
		RefreshVoiceCommands();
		if (voiceMode == VoiceMode.Standby)
		{
			EnterStandby();
		}
	}
}
