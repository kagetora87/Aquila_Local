using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AquilaLocal;

/// <summary>
/// The window "shell": the settings drawer, header chips, status dot, welcome screen, composer
/// states, keyboard shortcuts and interface sounds.
/// </summary>
public partial class MainWindow
{
	private bool drawerOpen;

	private string shellState = "";

	private string dotState = "";

	private bool composerHighlight;

	private DateTime sendStarted;

	private void InitializeShell()
	{
		UiSounds.IsSpeaking = () => speech.Active;
		ApplyUiSoundSettings();
		// Any button in the More menu closes the menu after it runs.
		MoreMenu.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(delegate
		{
			MoreMenu.IsOpen = false;
		}), handledEventsToo: true);
		RefreshShell();
		UpdateSendEnabled();
	}

	private void ApplyUiSoundSettings()
	{
		UiSounds.Enabled = settings.UiSounds;
		UiSounds.Volume = settings.UiSoundVolume;
	}

	/// <summary>Called on the render tick: updates the dot, welcome screen and chips only when something changed.</summary>
	private void RefreshShell()
	{
		bool ready = engine.Ready;
		bool hasModel = !string.IsNullOrEmpty(settings.ModelPath) && File.Exists(settings.ModelPath);
		string model = string.IsNullOrEmpty(settings.ModelPath) ? "" : Path.GetFileNameWithoutExtension(settings.ModelPath);
		bool empty = current == null || current.Turns.Count == 0;
		string state = $"{ready}|{hasModel}|{model}|{empty}|{busy}|{settings.WebMode}";
		string dot = busy ? "busy" : (ready ? "ready" : "off");
		if (dot != dotState)
		{
			dotState = dot;
			Motion.StopPulse(StatusDot);
			StatusDot.Fill = Ui.Brush((dot == "off") ? "Text.Tertiary" : "Accent");
			if (dot == "busy")
			{
				Motion.Pulse(StatusDot, 0.3, TimeSpan.FromMilliseconds(1200.0));
			}
		}
		if (state == shellState)
		{
			return;
		}
		shellState = state;
		ModelChipText.Text = (model.Length == 0) ? "Choose a model" : model;
		ModelChip.ToolTip = (model.Length == 0) ? "Choose the model Aquila uses" : (settings.ModelPath + "\nClick to change model settings.");
		WebChip.Visibility = (settings.WebMode == "Off") ? Visibility.Collapsed : Visibility.Visible;
		WebChipText.Text = (settings.WebMode == "Automatic") ? "Web: on" : "Web: ask first";
		Welcome.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
		WelcomeNoModel.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
		WelcomeReady.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
		if (!hasModel)
		{
			WelcomeLoadButton.Content = "Choose a model…";
			WelcomeLoadButton.IsEnabled = !busy;
			WelcomeLoadHint.Text = "Pick a .gguf model file on this computer to get started.";
		}
		else if (busy)
		{
			WelcomeLoadButton.Content = "Starting…";
			WelcomeLoadButton.IsEnabled = false;
			WelcomeLoadHint.Text = "Loading " + model + ". This can take a minute.";
		}
		else
		{
			WelcomeLoadButton.Content = "Start Aquila";
			WelcomeLoadButton.IsEnabled = true;
			WelcomeLoadHint.Text = "Loads " + model + ". This can take a minute the first time.";
		}
	}

	private void UpdateSendEnabled()
	{
		SendButton.IsEnabled = !busy && !attaching && (Composer.Text.Trim().Length > 0 || pendingAttachments.Count > 0);
	}

	// ---------------- Settings drawer ----------------

	private void OpenDrawer(RadioButton? tab = null)
	{
		if (tab != null)
		{
			tab.IsChecked = true;
		}
		if (drawerOpen)
		{
			return;
		}
		drawerOpen = true;
		MoreMenu.IsOpen = false;
		DrawerScrim.Visibility = Visibility.Visible;
		SettingsDrawer.Visibility = Visibility.Visible;
		Motion.Animate(DrawerScrim, UIElement.OpacityProperty, 1.0, Motion.Base, Motion.EaseOut);
		Motion.Animate(DrawerShift, TranslateTransform.XProperty, 0.0, Motion.Slow, Motion.EaseOut);
		SettingsDrawer.Focus();
	}

	/// <summary>Closes the drawer, saving what was changed. Stays open if a value is not valid.</summary>
	private void CloseDrawer()
	{
		if (!drawerOpen)
		{
			return;
		}
		if (!busy && !ReadSettings())
		{
			UiSounds.Play(UiSound.Notice);
			return;
		}
		drawerOpen = false;
		Motion.Animate(DrawerScrim, UIElement.OpacityProperty, 0.0, Motion.Base, Motion.EaseIn, delegate
		{
			if (!drawerOpen)
			{
				DrawerScrim.Visibility = Visibility.Collapsed;
			}
		});
		Motion.Animate(DrawerShift, TranslateTransform.XProperty, 420.0, Motion.Base, Motion.EaseIn, delegate
		{
			if (!drawerOpen)
			{
				SettingsDrawer.Visibility = Visibility.Collapsed;
			}
		});
		RefreshShell();
		Composer.Focus();
	}

	private void Settings_Click(object sender, RoutedEventArgs e)
	{
		if (drawerOpen)
		{
			CloseDrawer();
		}
		else
		{
			OpenDrawer();
		}
	}

	private void CloseDrawer_Click(object sender, RoutedEventArgs e)
	{
		CloseDrawer();
	}

	private void DrawerScrim_Click(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		CloseDrawer();
	}

	private void SettingsTab_Checked(object sender, RoutedEventArgs e)
	{
		if (GeneralPanel == null || ModelPanel == null || AdvancedPanel == null)
		{
			return;
		}
		GeneralPanel.Visibility = (sender == TabGeneral) ? Visibility.Visible : Visibility.Collapsed;
		ModelPanel.Visibility = (sender == TabModel) ? Visibility.Visible : Visibility.Collapsed;
		AdvancedPanel.Visibility = (sender == TabAdvanced) ? Visibility.Visible : Visibility.Collapsed;
		DrawerScroll?.ScrollToTop();
	}

	private void SaveSettings_Click(object sender, RoutedEventArgs e)
	{
		if (ReadSettings())
		{
			StatusText.Text = "Settings saved";
			CloseDrawer();
		}
		else
		{
			UiSounds.Play(UiSound.Notice);
		}
	}

	private void UiSounds_Changed(object sender, RoutedEventArgs e)
	{
		if (UiSoundsCheck == null)
		{
			return;
		}
		UiSounds.Enabled = UiSoundsCheck.IsChecked == true;
		if (UiSoundVolumePanel != null)
		{
			UiSoundVolumePanel.IsEnabled = UiSounds.Enabled;
		}
	}

	private void UiSoundVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		UiSounds.Volume = e.NewValue;
	}

	private void UiSoundVolume_Released(object sender, MouseButtonEventArgs e)
	{
		// A short preview at the new volume.
		UiSounds.Play(UiSound.Reply);
	}

	// ---------------- Header ----------------

	private void More_Click(object sender, RoutedEventArgs e)
	{
		RefreshContextControls();
		MoreMenu.IsOpen = !MoreMenu.IsOpen;
	}

	private void ModelChip_Click(object sender, RoutedEventArgs e)
	{
		OpenDrawer(TabModel);
	}

	// ---------------- Welcome ----------------

	private void WelcomeLoad_Click(object sender, RoutedEventArgs e)
	{
		if (busy)
		{
			return;
		}
		if (string.IsNullOrEmpty(settings.ModelPath) || !File.Exists(settings.ModelPath))
		{
			Model_Click(sender, e);
		}
		else
		{
			Load_Click(sender, e);
		}
	}

	private void Starter_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: string text })
		{
			Composer.Text = text;
			Composer.Focus();
			Composer.CaretIndex = Composer.Text.Length;
		}
	}

	// ---------------- Composer ----------------

	private void Composer_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (SendButton != null)
		{
			UpdateSendEnabled();
		}
	}

	private void Composer_FocusChanged(object sender, KeyboardFocusChangedEventArgs e)
	{
		UpdateComposerFocus();
	}

	private void UpdateComposerFocus()
	{
		if (ComposerFocus != null)
		{
			Motion.Animate(ComposerFocus, UIElement.OpacityProperty, (composerHighlight || Composer.IsKeyboardFocusWithin) ? 1.0 : 0.0, Motion.Fast, Motion.EaseOut);
		}
	}

	private void Files_DragLeave(object sender, DragEventArgs e)
	{
		composerHighlight = false;
		UpdateComposerFocus();
	}

	// ---------------- Keyboard ----------------

	private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			if (MoreMenu.IsOpen)
			{
				MoreMenu.IsOpen = false;
				e.Handled = true;
			}
			else if (drawerOpen)
			{
				CloseDrawer();
				e.Handled = true;
			}
			else if (busy && operation != null)
			{
				Stop_Click(this, new RoutedEventArgs());
				e.Handled = true;
			}
		}
		else if (Keyboard.Modifiers == ModifierKeys.Control)
		{
			if (e.Key == Key.N)
			{
				NewChat();
				Composer.Focus();
				e.Handled = true;
			}
			else if (e.Key == Key.OemComma)
			{
				Settings_Click(this, new RoutedEventArgs());
				e.Handled = true;
			}
		}
	}

	// ---------------- Reply meta ----------------

	/// <summary>Plain words after "Aquila" on a reply, its colour, and the full technical line as a tooltip.</summary>
	private (string Text, string Brush, string Tooltip) Meta(Turn turn)
	{
		string personality = (turn.PersonalityId != "default" && turn.PersonalityName.Length > 0) ? turn.PersonalityName : "";
		return turn.Status switch
		{
			"complete" => (settings.ShowTechnicalDetails ? string.Join(" · ", new[] { personality, turn.Metrics }.Where((string s) => s.Length > 0)) : personality, "Text.Tertiary", turn.Metrics),
			"generating" => ("", "Text.Tertiary", ""),
			"cancelled" => ("Stopped", "Text.Tertiary", turn.Metrics),
			"error" => ("Something went wrong", "Danger", turn.Activity.LastOrDefault() ?? ""),
			"limit" => ("Reply cut short", "Warning", "The reply reached its length limit. You can raise it under Settings › Advanced."),
			_ => ("Interrupted", "Warning", ""),
		};
	}

	private void ShowReply(MessageView view, Turn turn, bool generating)
	{
		var (text, brush, tooltip) = Meta(turn);
		view.ShowTechnicalDetails = settings.ShowTechnicalDetails;
		view.SetGenerating(generating);
		view.SetMeta(brush, tooltip);
		view.Update(VoiceDelivery.Display(turn, settings.Voice.RevealTags), turn.Reasoning, turn.Activity, text, renderMarkdown: true, turn.Answer);
	}
}
