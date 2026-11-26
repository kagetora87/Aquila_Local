using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using AquilaAvatarProtocol;
using Microsoft.Win32;

namespace AquilaLocal;

public partial class MainWindow : Window
{
	private record InstalledModel(string Name, string Path);

	private readonly AvatarHost avatar;

	private AvatarWindow? avatarWindow;

	private string avatarState = "idle";

	private string avatarUtterance = "";

	private int avatarTransition;

	private SpriteAvatarView? spriteView;

	private GestureMatcher? gestures;

	private AvatarPopoutWindow? avatarPopout;

	private AnimationsWindow? animationsWindow;

	private string spriteRoot = "";

	private DispatcherTimer? listeningTimer;

	internal Func<ChatSession, bool>? ConfirmDeleteForVerification;

	private bool uiReady;

	private bool synchronizingContext;

	private bool refreshingModels;

	private bool attaching;

	private bool featuresClosing;

	private int contextTokens = 8192;

	private int modelGeneration;

	private ModelInfo modelInfo = new ModelInfo("Model", "unknown", 262144, 0L, 262144.0, MetadataKnown: false);

	private ContextAdvice? advice;

	private GpuSnapshot? gpuSnapshot;

	private readonly CancellationTokenSource monitorCancellation = new CancellationTokenSource();

	private readonly List<Attachment> pendingAttachments = new List<Attachment>();

	private bool refreshingPersonalities;

	private Task inferenceStartup = Task.CompletedTask;

	private bool modelInfoReady;

	private readonly SpeechService speech;

	private VoiceWindow? voiceWindow;

	private readonly Storage store;

	private Settings settings;

	private readonly Engine engine;

	private readonly MemoryStore memories;

	private readonly SemanticMemory semantic;

	private readonly PersonalityStore personalities;

	private readonly ObservableCollection<ChatSession> chats;

	private ChatSession? current;

	private CancellationTokenSource? operation;

	private bool busy;

	private bool followBottom = true;

	private readonly DispatcherTimer renderTimer;

	private MessageView? live;

	private Turn? activeTurn;

	private bool dirtyRender;















































































	internal AvatarHost AvatarForVerification => avatar;

	public static RoutedUICommand DeleteChatCommand { get; } = new RoutedUICommand("Delete chat", "DeleteChatCommand", typeof(MainWindow));

	private InferenceProfile SelectedProfile => InferenceProfile.Resolve(settings.Inference, settings.Hardware);

	internal SpeechService SpeechForVerification => speech;

	/// <summary>
	/// Sets up the picture avatar: a folder of animations (Data\avatar by default), placeholder
	/// pictures on first run, and the gesture matcher that reacts to what the assistant says.
	/// The old 3D avatar is no longer started.
	/// </summary>
	private void InitializeAvatar()
	{
		SpriteAvatarOptions options = settings.Sprite;
		spriteRoot = string.IsNullOrWhiteSpace(options.Folder) ? Path.Combine(store.Root, "avatar") : options.Folder;
		spriteView = new SpriteAvatarView();
		gestures = new GestureMatcher(Path.Combine(AppContext.BaseDirectory, "embeddings"))
		{
			Sensitivity = options.Sensitivity
		};
		gestures.Fired += delegate(string name)
		{
			base.Dispatcher.InvokeAsync(delegate
			{
				if (!featuresClosing)
				{
					spriteView?.PlayGesture(name);
				}
			});
		};
		try
		{
			if (!Directory.Exists(spriteRoot) || SpriteLibrary.Load(spriteRoot).Count == 0)
			{
				AvatarPlaceholders.EnsureIn(spriteRoot);
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
		{
			Notice("Avatar: could not create placeholder pictures (" + ex.Message + ").");
		}
		ReloadSpriteAvatar();
		Composer.TextChanged += delegate
		{
			ComposerTyping();
		};
		if (options.Enabled && options.PoppedOut)
		{
			PopOutAvatar();
		}
		else
		{
			DockAvatar();
		}
	}

	private void ReloadSpriteAvatar()
	{
		if (spriteView != null && gestures != null)
		{
			spriteView.Load(spriteRoot);
			spriteView.SetState(SpriteStateFor(avatarState));
			gestures.SetAnimations(spriteView.Animations);
			gestures.Sensitivity = settings.Sprite.Sensitivity;
		}
	}

	private static string SpriteStateFor(string state)
	{
		if (!(state == "interrupted"))
		{
			return state;
		}
		return "idle";
	}

	private void ComposerTyping()
	{
		if (spriteView == null || busy || featuresClosing || avatarState != "idle" || string.IsNullOrEmpty(Composer.Text))
		{
			return;
		}
		spriteView.SetState("listening");
		if (listeningTimer == null)
		{
			listeningTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(2.5)
			};
			listeningTimer.Tick += delegate
			{
				listeningTimer.Stop();
				if (avatarState == "idle")
				{
					spriteView?.SetState("idle");
				}
			};
		}
		listeningTimer.Stop();
		listeningTimer.Start();
	}

	private void AvatarPop_Click(object sender, RoutedEventArgs e)
	{
		if (avatarPopout != null)
		{
			DockAvatar();
		}
		else
		{
			PopOutAvatar();
		}
	}

	private void PopOutAvatar()
	{
		if (spriteView == null || avatarPopout != null)
		{
			return;
		}
		AvatarSlot.Content = null;
		AvatarSlot.Visibility = Visibility.Collapsed;
		AvatarPoppedText.Visibility = Visibility.Visible;
		avatarPopout = new AvatarPopoutWindow(spriteView, settings.Sprite);
		avatarPopout.DockRequested += DockAvatar;
		avatarPopout.Closed += delegate
		{
			// Closed by Windows (e.g. Alt+F4): treat as "put back".
			if (avatarPopout != null)
			{
				avatarPopout = null;
				if (!featuresClosing)
				{
					DockAvatar();
				}
			}
		};
		settings.Sprite.PoppedOut = true;
		avatarPopout.Show();
		SaveAvatarSettings();
	}

	private void DockAvatar()
	{
		if (spriteView == null)
		{
			return;
		}
		AvatarPopoutWindow? window = avatarPopout;
		avatarPopout = null;
		if (window != null)
		{
			window.Content = null;
			window.Close();
		}
		AvatarSlot.Content = spriteView;
		AvatarSlot.Visibility = Visibility.Visible;
		AvatarPoppedText.Visibility = Visibility.Collapsed;
		if (settings.Sprite.PoppedOut)
		{
			settings.Sprite.PoppedOut = false;
			SaveAvatarSettings();
		}
	}

	private void SaveAvatarSettings()
	{
		try
		{
			store.SaveSettings(settings);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			Notice("Avatar: settings were not saved (" + ex.Message + ").");
		}
	}

	private async Task StartSavedAvatar()
	{
		try
		{
			await ApplyAvatar(settings.Avatar, enabled: true, restart: false, settings.AvatarFolder, settings.AvatarModelPath);
		}
		catch (Exception ex)
		{
			Notice("Avatar: " + ex.Message + " Chat remains available.");
		}
	}

	private async Task ApplyAvatar(AvatarPresentation config, bool enabled, bool restart, string folder, string modelPath)
	{
		if (!enabled)
		{
			await avatar.StopAsync();
		}
		else if (!avatar.Running || restart || !AvatarCatalog.SamePath(avatar.ModelPath, AvatarCatalog.Resolve(modelPath)))
		{
			await avatar.StartAsync(config, CancellationToken.None, headless: false, modelPath);
			await avatar.SetStateAsync(avatarState, preview: false, "none", avatarUtterance);
		}
		else
		{
			await avatar.UpdatePresentationAsync(config);
		}
		settings.Avatar = config;
		settings.AvatarEnabled = enabled;
		settings.AvatarFolder = folder;
		settings.AvatarModelPath = modelPath;
		store.SaveSettings(settings);
	}

	private void Avatar_Click(object sender, RoutedEventArgs e)
	{
		if (gestures == null)
		{
			return;
		}
		if (animationsWindow != null)
		{
			animationsWindow.Activate();
			return;
		}
		animationsWindow = new AnimationsWindow(settings.Sprite, spriteRoot, gestures, delegate
		{
			ReloadSpriteAvatar();
			SaveAvatarSettings();
		})
		{
			Owner = this
		};
		animationsWindow.Closed += delegate
		{
			animationsWindow = null;
			SaveAvatarSettings();
		};
		animationsWindow.Show();
	}

	private void SetAvatarState(string state, string utterance = "")
	{
		avatarTransition++;
		avatarState = state;
		avatarUtterance = utterance;
		listeningTimer?.Stop();
		spriteView?.SetState(SpriteStateFor(state));
		avatar.SetStateAsync(state, preview: false, "none", utterance);
	}

	private async void AvatarInterrupted()
	{
		SetAvatarState("interrupted");
		int ticket = avatarTransition;
		await Task.Delay(1800);
		if (!featuresClosing && ticket == avatarTransition)
		{
			SetAvatarState("idle");
		}
	}

	private async Task PreviewAvatar(string state)
	{
		int ticket = ++avatarTransition;
		await avatar.SetStateAsync(state, preview: true, (state == "listening") ? "nod" : ((state == "speaking") ? "smile" : "none"));
		await Task.Delay(2500);
		if (!featuresClosing && ticket == avatarTransition)
		{
			await avatar.SetStateAsync(avatarState, preview: false, "none", avatarUtterance);
		}
	}

	private void Chat_RightClick(object sender, MouseButtonEventArgs e)
	{
		if (sender is ListBoxItem listBoxItem && !busy && !attaching)
		{
			listBoxItem.IsSelected = true;
			listBoxItem.Focus();
		}
	}

	private ChatSession? DeleteTarget(object? parameter)
	{
		return (parameter as ChatSession) ?? (ChatsList.SelectedItem as ChatSession);
	}

	private void DeleteChat_CanExecute(object sender, CanExecuteRoutedEventArgs e)
	{
		int canExecute;
		if (!busy && !attaching && !featuresClosing)
		{
			ChatSession chatSession = DeleteTarget(e.Parameter);
			if (chatSession != null)
			{
				canExecute = (chats.Contains(chatSession) ? 1 : 0);
				goto IL_0038;
			}
		}
		canExecute = 0;
		goto IL_0038;
		IL_0038:
		e.CanExecute = (byte)canExecute != 0;
		e.Handled = true;
	}

	private void DeleteChat_Executed(object sender, ExecutedRoutedEventArgs e)
	{
		e.Handled = true;
		ChatSession chatSession = DeleteTarget(e.Parameter);
		if (busy || attaching || featuresClosing || chatSession == null || !chats.Contains(chatSession) || !(ConfirmDeleteForVerification?.Invoke(chatSession) ?? (MessageBox.Show(this, "Delete “" + chatSession.Title.Truncate(100) + "” and its saved attachment snapshots?\n\nThis cannot be undone in the app. Memories learned from this chat are kept; use Memories to forget them. Existing exports and backups are kept.", "Delete chat?", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) == MessageBoxResult.Yes)))
		{
			return;
		}
		speech.Stop();
		try
		{
			store.DeleteChat(chatSession.Id);
			int val = chats.IndexOf(chatSession);
			bool num = current == chatSession;
			chats.Remove(chatSession);
			if (num)
			{
				if (chats.Count > 0)
				{
					ChatsList.SelectedItem = chats[Math.Min(val, chats.Count - 1)];
				}
				else
				{
					current = null;
					NewChat();
				}
			}
			CommandManager.InvalidateRequerySuggested();
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			Notice("Could not delete this chat: " + ex.Message);
		}
	}

	private void RefreshContextControls()
	{
		if (CompactButton != null && SummaryButton != null)
		{
			CompactButton.IsEnabled = !busy && !attaching && !featuresClosing && current != null && ConversationContext.CanCompact(current);
			SummaryButton.IsEnabled = !busy && !attaching;
			SummaryButton.Visibility = ((current?.CompactedContext == null) ? Visibility.Collapsed : Visibility.Visible);
		}
	}

	private async void Compact_Click(object sender, RoutedEventArgs e)
	{
		if (busy || attaching || current == null || !ConversationContext.CanCompact(current) || !ReadSettings() || MessageBox.Show(this, "Summarize earlier conversation for future model requests? The four most recent complete turns and the full visible history are kept.\n\nSummaries can omit or misstate details, including image content. Review or reset the result through View summary. This runs the selected local model and adds its token usage to this session.", "Compact context?", MessageBoxButton.YesNo, MessageBoxImage.Asterisk, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		ChatSession chat = current;
		ContextCheckpoint previous = chat.CompactedContext;
		operation = new CancellationTokenSource();
		SetBusy(value: true);
		StatusText.Text = "Compacting earlier conversation…";
		try
		{
			SetAvatarState("thinking");
			ContextCheckpoint contextCheckpoint = (chat.CompactedContext = await new ChatCompactor(engine, delegate(string text)
			{
				StatusText.Text = text;
			}).CompactAsync(settings, chat, operation.Token));
			try
			{
				store.SaveChat(chat);
			}
			catch
			{
				chat.CompactedContext = previous;
				throw;
			}
			int count = ConversationContext.ActiveTurns(chat).Count;
			Notice($"Context compacted through {contextCheckpoint.CoveredTurns} earlier turns. Full chat history is retained. Review the summary before relying on important details." + ((count > 4) ? $" {count} complete turns remain unsummarized; compact again to continue." : ""));
		}
		catch (OperationCanceledException)
		{
			Notice("Compaction stopped. Previous context and full history were kept.");
		}
		catch (Exception ex2)
		{
			Notice("Compaction: " + ex2.Message);
		}
		finally
		{
			SetAvatarState("idle");
			operation.Dispose();
			operation = null;
			SetBusy(value: false);
			RefreshUsage();
			StatusText.Text = (engine.Ready ? "Model ready · on this device" : "Model not loaded");
			try
			{
				store.SaveChat(chat);
			}
			catch (Exception ex3) when (((ex3 is IOException || ex3 is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
				Notice("Could not save session usage: " + ex3.Message);
			}
		}
	}

	private void Summary_Click(object sender, RoutedEventArgs e)
	{
		if (busy || attaching)
		{
			return;
		}
		ContextCheckpoint summary = current?.CompactedContext;
		if (summary == null)
		{
			return;
		}
		ChatSession chat = current;
		ContextSummaryWindow contextSummaryWindow = new ContextSummaryWindow(summary, delegate
		{
			chat.CompactedContext = null;
			try
			{
				store.SaveChat(chat);
				RefreshContextControls();
				RefreshUsage();
			}
			catch
			{
				chat.CompactedContext = summary;
				throw;
			}
		});
		contextSummaryWindow.Owner = this;
		contextSummaryWindow.ShowDialog();
	}

	private void RefreshPersonalities()
	{
		refreshingPersonalities = true;
		try
		{
			List<PersonalityProfile> list = personalities.List();
			PersonalityCombo.ItemsSource = list;
			PersonalityCombo.SelectedItem = list.FirstOrDefault((PersonalityProfile p) => p.Id == settings.PersonalityId) ?? list[0];
			UpdatePersonalitySummary();
		}
		finally
		{
			refreshingPersonalities = false;
		}
	}

	private void Personality_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (PersonalityCombo.SelectedItem is PersonalityProfile personalityProfile)
		{
			UpdatePersonalitySummary();
			if (uiReady && !refreshingPersonalities && !busy)
			{
				settings.PersonalityId = personalityProfile.Id;
				store.SaveSettings(settings);
				StatusText.Text = "Personality selected · applies to the next message";
			}
		}
	}

	private void UpdatePersonalitySummary()
	{
		if (PersonalitySummary != null && PersonalityCombo.SelectedItem is PersonalityProfile personalityProfile)
		{
			PersonalitySummary.Text = ((personalityProfile.Id == "default") ? "Uses your existing global instructions." : (personalityProfile.LearnMemories ? "Speaking style only · factual memory stays separate." : "Speaking style active · automatic memory learning paused for this profile."));
		}
	}

	private void Personalities_Click(object sender, RoutedEventArgs e)
	{
		if (busy || !ReadSettings())
		{
			return;
		}
		try
		{
			PersonalityWindow personalityWindow = new PersonalityWindow(personalities, settings.PersonalityId, delegate(string id)
			{
				settings.PersonalityId = id;
				store.SaveSettings(settings);
			});
			personalityWindow.Owner = this;
			personalityWindow.ShowDialog();
			RefreshPersonalities();
			settings.PersonalityId = ((PersonalityProfile)PersonalityCombo.SelectedItem).Id;
			store.SaveSettings(settings);
		}
		catch (Exception ex)
		{
			Notice(ex.Message);
		}
	}

	private void InitializeFeatures()
	{
		base.Width = Math.Min(base.Width, SystemParameters.WorkArea.Width - 20.0);
		base.Height = Math.Min(base.Height, SystemParameters.WorkArea.Height - 20.0);
		uiReady = true;
		SetContext(settings.Context);
		UpdateModelChoices();
		inferenceStartup = Task.WhenAll(RefreshModelInfoAsync(), DetectHardwareAsync());
		MonitorGpuAsync();
		RefreshUsage();
		RefreshThinkingSummary();
	}

	private void Reasoning_Changed(object sender, RoutedEventArgs e)
	{
		if (uiReady)
		{
			RefreshThinkingSummary();
		}
	}

	private void RefreshThinkingSummary()
	{
		if (ThinkingSummary == null)
		{
			return;
		}
		if (ThinkingCheck.IsChecked != true)
		{
			ThinkingSummary.Text = "Thinking off · direct replies";
			return;
		}
		int result;
		bool flag = !int.TryParse(ThinkingBudgetBox.Text, out result);
		if (!flag)
		{
			bool flag2 = ((result < 0 || result > 16384) ? true : false);
			flag = flag2;
		}
		if (flag || !int.TryParse(MaxTokensBox.Text, out var result2) || result2 < 128)
		{
			ThinkingSummary.Text = "Enter valid whole-token budgets to see the allowance.";
			return;
		}
		Settings settings = new Settings
		{
			Thinking = true,
			ThinkingBudget = result,
			MaxTokens = result2
		};
		ThinkingSummary.Text = ((settings.EffectiveThinkingBudget == 0) ? "Budget 0 · no reasoning tokens requested" : ($"Up to {settings.EffectiveThinkingBudget:N0} thinking tokens · at least {result2 - settings.EffectiveThinkingBudget:N0} tokens reserved for the answer" + ((result > settings.EffectiveThinkingBudget) ? " · capped by response limit" : "")));
	}

	private void RefreshUsage()
	{
		if (SessionUsageText != null && current != null)
		{
			SessionUsageText.Text = $"Session · {current.TotalTokens:N0} tokens · {current.PromptTokens:N0} in / {current.CompletionTokens:N0} out" + (current.UsageIncomplete ? " · partial counts" : "");
			long value = current.Turns.Sum((Turn t) => t.MemoryPromptTokens + t.MemoryCompletionTokens);
			long value2 = current.CompactionPromptTokens + current.CompactionCompletionTokens;
			SessionUsageText.ToolTip = $"Cumulative server-reported input/output across chat, tools, memory learning and context compaction. Includes repeated history, cached input and thinking. Memory learning: {value:N0} tokens included. Context compaction: {value2:N0} tokens included. This is not context occupancy. Interrupted requests can have incomplete counts.";
		}
	}

	private void UpdateModelChoices()
	{
		refreshingModels = true;
		try
		{
			string path = Path.Combine(AppContext.BaseDirectory, "models");
			List<InstalledModel> list = (Directory.Exists(path) ? (from p in Directory.EnumerateFiles(path, "*.gguf")
				where !Path.GetFileName(p).Contains("mmproj", StringComparison.OrdinalIgnoreCase)
				select new InstalledModel(Path.GetFileNameWithoutExtension(p), p) into p
				orderby p.Name
				select p).ToList() : new List<InstalledModel>());
			if (settings.ModelPath.Length > 0 && !list.Any((InstalledModel m) => m.Path.Equals(settings.ModelPath, StringComparison.OrdinalIgnoreCase)))
			{
				list.Add(new InstalledModel(Path.GetFileNameWithoutExtension(settings.ModelPath), settings.ModelPath));
			}
			InstalledModelsCombo.ItemsSource = list;
			InstalledModelsCombo.SelectedItem = list.FirstOrDefault((InstalledModel m) => m.Path.Equals(settings.ModelPath, StringComparison.OrdinalIgnoreCase));
		}
		finally
		{
			refreshingModels = false;
		}
	}

	private async void InstalledModels_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (uiReady && !refreshingModels && !busy && InstalledModelsCombo.SelectedItem is InstalledModel installedModel && !installedModel.Path.Equals(settings.ModelPath, StringComparison.OrdinalIgnoreCase))
		{
			if (!ReadSettings())
			{
				UpdateModelChoices();
			}
			else
			{
				await SelectModelAsync(installedModel.Path);
			}
		}
	}

	private async Task SelectModelAsync(string path)
	{
		if (Path.GetFileName(path).Contains("mmproj", StringComparison.OrdinalIgnoreCase))
		{
			Notice("Choose the language model here. Select mmproj under Vision projector.");
			return;
		}
		engine.Stop();
		settings.ModelPath = path;
		settings.ProjectorPath = ModelInfo.FindProjector(path);
		settings.VisionDisabled = false;
		RefreshPaths();
		UpdateModelChoices();
		await RefreshModelInfoAsync();
		settings.Context = contextTokens;
		if (int.TryParse(MaxTokensBox.Text, out var result))
		{
			settings.MaxTokens = result;
		}
		store.SaveSettings(settings);
		StatusText.Text = "Model selected · ready to load";
	}

	private async Task RefreshModelInfoAsync()
	{
		int generation = ++modelGeneration;
		string path = settings.ModelPath;
		modelInfoReady = false;
		UpdateAdvice();
		ModelInfo modelInfo = await Task.Run(() => ModelInfo.Read(path));
		if (generation == modelGeneration && !featuresClosing)
		{
			this.modelInfo = modelInfo;
			modelInfoReady = true;
			SetContext(RequestedContext());
		}
	}

	private void Projector_Click(object sender, RoutedEventArgs e)
	{
		if (!busy && ReadSettings())
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Title = "Choose this model's matching mmproj vision projector",
				Filter = "GGUF projectors (*.gguf)|*.gguf",
				InitialDirectory = Path.GetDirectoryName(settings.ModelPath)
			};
			if (openFileDialog.ShowDialog(this) == true)
			{
				engine.Stop();
				settings.ProjectorPath = openFileDialog.FileName;
				settings.VisionDisabled = false;
				store.SaveSettings(settings);
				RefreshPaths();
				UpdateAdvice();
			}
		}
	}

	private void ClearProjector_Click(object sender, RoutedEventArgs e)
	{
		if (!busy && ReadSettings())
		{
			engine.Stop();
			settings.ProjectorPath = "";
			settings.VisionDisabled = true;
			store.SaveSettings(settings);
			RefreshPaths();
			UpdateAdvice();
		}
	}

	private void SetContext(int value, bool remember = false)
	{
		if (ContextSlider == null)
		{
			return;
		}
		synchronizingContext = true;
		try
		{
			InferenceProfile selectedProfile = SelectedProfile;
			int num = Math.Min(selectedProfile.Maximum, modelInfoReady ? modelInfo.NativeContext : 262144);
			contextTokens = Math.Clamp((int)Math.Round((double)value / 1024.0) * 1024, 2048, num);
			if (remember)
			{
				selectedProfile.Remember(settings.Inference, contextTokens);
			}
			ContextSlider.Maximum = Math.Log2(num);
			ContextSlider.Value = Math.Log2(contextTokens);
			ComboBoxItem[] array = (from ComboBoxItem i in ContextCombo.Items
				where i.Tag?.ToString() == "custom"
				select i).ToArray();
			foreach (ComboBoxItem removeItem in array)
			{
				ContextCombo.Items.Remove(removeItem);
			}
			ComboBoxItem comboBoxItem = ContextCombo.Items.Cast<ComboBoxItem>().FirstOrDefault((ComboBoxItem i) => i.Content.ToString() == contextTokens.ToString());
			if (comboBoxItem == null)
			{
				comboBoxItem = new ComboBoxItem
				{
					Content = contextTokens.ToString(),
					Tag = "custom"
				};
				ContextCombo.Items.Add(comboBoxItem);
			}
			foreach (ComboBoxItem item in (IEnumerable)ContextCombo.Items)
			{
				item.IsEnabled = int.Parse(item.Content.ToString()) <= num;
			}
			ContextCombo.SelectedItem = comboBoxItem;
			ContextValue.Text = $"{contextTokens:N0} tokens";
			if (int.TryParse(MaxTokensBox.Text, out var result) && result > contextTokens / 2)
			{
				MaxTokensBox.Text = Math.Min(32768, contextTokens / 2).ToString();
			}
		}
		finally
		{
			synchronizingContext = false;
		}
		UpdateAdvice();
	}

	private void ContextSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (uiReady && !synchronizingContext)
		{
			SetContext((int)Math.Pow(2.0, e.NewValue), remember: true);
		}
	}

	private void ContextCombo_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (uiReady && !synchronizingContext && ContextCombo.SelectedItem is ComboBoxItem comboBoxItem && int.TryParse(comboBoxItem.Content.ToString(), out var result))
		{
			SetContext(result, remember: true);
		}
	}

	private void Thresholds_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		PositionMarkers();
	}

	private void Compute_Changed(object sender, TextChangedEventArgs e)
	{
		if (uiReady)
		{
			UpdateAdvice();
		}
	}

	private void UpdateAdvice()
	{
		if (!uiReady)
		{
			return;
		}
		InferenceLaunch inferenceLaunch = CurrentLaunch();
		long availableSystemMemory = GpuMonitor.AvailableSystemMemory;
		long? freeGpu = settings.Hardware?.Gpu?.FreeBytes;
		long num = 0L;
		InferenceGpu device = settings.Hardware?.Gpu;
		if ((object)device != null && (object)gpuSnapshot != null && gpuSnapshot.LocalUsage.TryGetValue(device.AdapterKey, out var value))
		{
			freeGpu = Math.Clamp(device.DedicatedBytes - value, 0L, device.DedicatedBytes);
			num = gpuSnapshot.Apps.Where((GpuApp a) => a.Name == "Aquila model" && a.AdapterKey == device.AdapterKey).Sum((GpuApp a) => a.LocalBytes);
		}
		RenderProfile(inferenceLaunch, availableSystemMemory, freeGpu);
		if (!modelInfoReady || (object)settings.Hardware == null || !modelInfo.MetadataKnown)
		{
			advice = null;
			ThresholdCanvas.Visibility = Visibility.Hidden;
			ContextThresholds.Text = ((!modelInfoReady || (object)settings.Hardware == null) ? "Reading model and hardware information…" : "Context metadata unavailable.");
			ContextRisk.Text = "";
		}
		else
		{
			long available = (inferenceLaunch.CpuOnly ? (availableSystemMemory + engine.WorkingSetBytes) : (freeGpu.GetValueOrDefault() + num));
			RenderContextAdvice(inferenceLaunch, available);
		}
	}

	private void RenderContextAdvice(InferenceLaunch launch, long available)
	{
		InferenceLaunch plan = AutoPlan;
		if (plan != null)
		{
			ThresholdCanvas.Visibility = Visibility.Collapsed;
			ContextThresholds.Text = "Set automatically: " + InferenceLaunch.Describe(plan.AutoContext) + ".";
			ContextRisk.Text = "Longer conversations are remembered in full up to this length. Older parts are left out after that.";
			ContextRisk.Foreground = (Brush)FindResource("Muted");
			return;
		}
		long projectorBytes = (File.Exists(settings.ProjectorPath) ? new FileInfo(settings.ProjectorPath).Length : 0);
		int num = Math.Min(SelectedProfile.Target, modelInfo.NativeContext);
		advice = modelInfo.Advice(available, projectorBytes, launch.Cache)with
		{
			Recommended = num
		};
		ThresholdCanvas.Visibility = Visibility.Visible;
		ContextThresholds.Text = $"Profile target {num:N0} · Native max {advice.NativeMaximum:N0}";
		ProfileTargetMarker.ToolTip = $"Profile target: {num:N0} tokens. This is the profile’s default allowance.";
		bool flag = launch.ExpertCpuLayers == 0;
		bool flag2 = !launch.CpuOnly && !launch.FullOffload;
		bool flag3 = advice.FitsMinimum && (advice.FitsNativeMaximum || contextTokens <= advice.Danger);
		string text = (launch.CpuOnly ? "RAM estimate" : "Full GPU estimate");
		string text2 = ((!advice.FitsMinimum) ? "below 2,048 tokens" : (advice.FitsNativeMaximum ? "covers the native maximum" : $"up to {advice.Danger:N0} tokens"));
		string value = ((launch.Layers == "auto") ? "Automatic GPU fit can place some layers in RAM." : "The selected GPU layer count can place some layers in RAM.");
		ContextRisk.Text = ((!flag) ? "Memory estimate varies with MoE expert placement." : (flag2 ? $"{text}: {text2}.\n{value}" : (flag3 ? (text + ": " + text2 + ".") : $"Selected allowance exceeds the {text.ToLowerInvariant()}.\n{text}: {text2}.")));
		ContextRisk.ToolTip = (flag ? "This estimate uses current free memory, model weights and KV cache. Other applications can change it." : "This estimate is unavailable while some MoE expert weights are assigned to system RAM.");
		ContextRisk.Foreground = (Brush)FindResource((!flag || flag3) ? "Muted" : (flag2 ? "ContextAdvisoryInk" : "ContextWarningInk"));
		MemoryEstimateLine.Background = (Brush)FindResource(flag2 ? "ContextAdvisoryInk" : "ContextWarningInk");
		MemoryEstimateMarker.Visibility = ((!flag || !advice.FitsMinimum || advice.FitsNativeMaximum || !(Math.Log2(advice.Danger) <= ContextSlider.Maximum)) ? Visibility.Hidden : Visibility.Visible);
		PositionMarkers();
	}

	private void PositionMarkers()
	{
		if ((object)advice != null && ThresholdCanvas != null && ContextSlider != null)
		{
			Canvas.SetLeft(ProfileTargetMarker, Position(advice.Recommended));
			Canvas.SetLeft(MemoryEstimateMarker, Position(advice.Danger));
		}
		double Position(int tokens)
		{
			return Math.Clamp((Math.Log2(tokens) - ContextSlider.Minimum) / Math.Max(1.0, ContextSlider.Maximum - ContextSlider.Minimum), 0.0, 1.0) * Math.Max(0.0, ThresholdCanvas.ActualWidth - ProfileTargetMarker.Width);
		}
	}

	private async Task MonitorGpuAsync()
	{
		try
		{
			await Task.Run(async delegate
			{
				using GpuMonitor monitor = new GpuMonitor();
				while (!monitorCancellation.IsCancellationRequested)
				{
					GpuSnapshot snapshot = monitor.Sample(await base.Dispatcher.InvokeAsync(() => engine.ProcessId));
					await base.Dispatcher.InvokeAsync(delegate
					{
						if (!featuresClosing)
						{
							gpuSnapshot = snapshot;
							RenderGpu();
							UpdateAdvice();
						}
					});
					await Task.Delay(2000, monitorCancellation.Token);
				}
			});
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			if (!featuresClosing)
			{
				GpuSummary.Text = "GPU monitoring unavailable: " + ex2.Message;
				GpuAppsList.ItemsSource = null;
			}
		}
	}

	private void GpuAdapter_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (uiReady)
		{
			RenderGpu(updateAdapters: false);
		}
	}

	private void RenderGpu(bool updateAdapters = true)
	{
		if ((object)gpuSnapshot == null)
		{
			return;
		}
		if (updateAdapters && (GpuAdapterCombo.Items.Count != gpuSnapshot.Adapters.Count || GpuAdapterCombo.Items.Count == 0))
		{
			string key = (GpuAdapterCombo.SelectedItem as GpuAdapter)?.Key;
			GpuAdapterCombo.ItemsSource = gpuSnapshot.Adapters;
			GpuAdapterCombo.SelectedItem = gpuSnapshot.Adapters.FirstOrDefault((GpuAdapter a) => a.Key == key) ?? gpuSnapshot.Adapters.OrderByDescending((GpuAdapter a) => a.DedicatedBytes).FirstOrDefault();
		}
		if (gpuSnapshot.Error != null)
		{
			GpuSummary.Text = gpuSnapshot.Error;
			GpuAppsList.ItemsSource = null;
			return;
		}
		object selectedItem = GpuAdapterCombo.SelectedItem;
		GpuAdapter selected = selectedItem as GpuAdapter;
		if ((object)selected == null)
		{
			GpuSummary.Text = "No WDDM GPU adapter was reported.";
			return;
		}
		List<GpuApp> list = gpuSnapshot.Apps.Where((GpuApp a) => a.AdapterKey == selected.Key).ToList();
		GpuAppsList.ItemsSource = list;
		long value;
		string text = (gpuSnapshot.LocalUsage.TryGetValue(selected.Key, out value) ? $"{(double)value / 1073741824.0:0.00} / {(double)selected.DedicatedBytes / 1073741824.0:0.0} GB local" : "Local memory total unavailable");
		GpuSummary.Text = $"GPU {gpuSnapshot.EngineUsage.GetValueOrDefault(selected.Key):0.0}% · " + text + ((list.Count == 0) ? "\nNo attributed app activity in this sample." : "");
		GpuFootnote.Text = $"Updated {gpuSnapshot.At.LocalDateTime:h:mm:ss tt} · 2s interval";
	}

	private async void Attach_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Attach files to this local conversation",
			Multiselect = true,
			Filter = "Supported files|*.txt;*.md;*.pdf;*.docx;*.csv;*.tsv;*.json;*.xml;*.yaml;*.yml;*.cs;*.gd;*.tscn;*.py;*.js;*.ts;*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files|*.*"
		};
		if (openFileDialog.ShowDialog(this) == true)
		{
			await StageFilesAsync(openFileDialog.FileNames);
		}
	}

	private void Files_DragOver(object sender, DragEventArgs e)
	{
		if (e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			e.Effects = ((!busy && !attaching) ? DragDropEffects.Copy : DragDropEffects.None);
			e.Handled = true;
		}
	}

	private async void Files_Drop(object sender, DragEventArgs e)
	{
		if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
		{
			e.Handled = true;
			await StageFilesAsync(paths);
		}
	}

	private async Task StageFilesAsync(IEnumerable<string> paths)
	{
		if (busy || attaching)
		{
			return;
		}
		attaching = true;
		AttachButton.IsEnabled = false;
		SendButton.IsEnabled = false;
		List<string> errors = new List<string>();
		try
		{
			foreach (string path in paths.Take(9))
			{
				try
				{
					if (pendingAttachments.Count >= 8)
					{
						throw new ArgumentException("At most 8 files can be attached to one message.");
					}
					Attachment attachment = await Task.Run(() => Attachments.Read(path));
					if (!pendingAttachments.Any((Attachment a) => a.ContentDigest == attachment.ContentDigest))
					{
						if (pendingAttachments.Sum((Attachment a) => a.Text.Length) + attachment.Text.Length > 120000)
						{
							throw new ArgumentException("Combined attached text exceeds 120,000 characters. Use smaller excerpts.");
						}
						if (pendingAttachments.Sum((Attachment a) => a.Base64.Length) + attachment.Base64.Length > 12582912)
						{
							throw new ArgumentException("Combined image data is too large. Attach fewer or smaller images.");
						}
						pendingAttachments.Add(attachment);
					}
				}
				catch (Exception ex)
				{
					errors.Add(Path.GetFileName(path) + ": " + ex.Message);
				}
			}
		}
		finally
		{
			attaching = false;
			AttachButton.IsEnabled = !busy;
			SendButton.IsEnabled = true;
			RenderAttachmentChips();
		}
		if (errors.Count > 0)
		{
			Notice(string.Join("\n", errors));
		}
	}

	private void RenderAttachmentChips()
	{
		AttachmentChips.Children.Clear();
		foreach (Attachment attachment in pendingAttachments)
		{
			Button button = new Button
			{
				Content = attachment.Name + "  ×",
				ToolTip = attachment.Label + " · click to remove",
				Margin = new Thickness(0.0, 0.0, 6.0, 5.0),
				Padding = new Thickness(8.0, 5.0, 8.0, 5.0),
				FontSize = 11.0
			};
			button.Click += delegate
			{
				pendingAttachments.Remove(attachment);
				RenderAttachmentChips();
			};
			AttachmentChips.Children.Add(button);
		}
	}

	private void ShowInferenceSettings()
	{
		InferencePreferences inference = settings.Inference;
		ProfileCombo.SelectedValue = inference.Profile;
		LaptopQ4Check.IsChecked = inference.LaptopQ4;
		CustomCacheCombo.SelectedValue = inference.CustomCache;
		ExpertLayersBox.Text = inference.ExpertCpuLayers.ToString(CultureInfo.InvariantCulture);
		CreativeCheck.IsChecked = inference.Creative;
		CreativeTempBox.Text = inference.CreativeTemperature.ToString(CultureInfo.InvariantCulture);
		TopPBox.Text = inference.TopP.ToString(CultureInfo.InvariantCulture);
		MinPBox.Text = inference.MinP.ToString(CultureInfo.InvariantCulture);
		DryMultiplierBox.Text = inference.DryMultiplier.ToString(CultureInfo.InvariantCulture);
		DryBaseBox.Text = inference.DryBase.ToString(CultureInfo.InvariantCulture);
		DryLengthBox.Text = inference.DryAllowedLength.ToString(CultureInfo.InvariantCulture);
		RefreshCreativeFields();
	}

	private InferencePreferences ReadInferenceSettings()
	{
		InferencePreferences inference = settings.Inference;
		return new InferencePreferences
		{
			Profile = ((ProfileCombo.SelectedValue as string) ?? "Auto"),
			HighContext = inference.HighContext,
			LaptopContext = inference.LaptopContext,
			CustomContext = inference.CustomContext,
			LaptopQ4 = (LaptopQ4Check.IsChecked == true),
			CustomCache = ((CustomCacheCombo.SelectedValue as string) ?? "f16"),
			ExpertCpuLayers = Integer(ExpertLayersBox, "MoE expert layers"),
			Creative = (CreativeCheck.IsChecked == true),
			CreativeTemperature = Number(CreativeTempBox, "creative temperature"),
			MinP = Number(MinPBox, "min-p"),
			TopP = Number(TopPBox, "top-p"),
			DryMultiplier = Number(DryMultiplierBox, "DRY multiplier"),
			DryBase = Number(DryBaseBox, "DRY base"),
			DryAllowedLength = Integer(DryLengthBox, "DRY allowed length")
		};
		static int Integer(TextBox field, string label)
		{
			if (!int.TryParse(field.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			{
				throw new ArgumentException("Enter a whole number for " + label + ".");
			}
			return result;
		}
		static double Number(TextBox field, string label)
		{
			if (!double.TryParse(field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
			{
				throw new ArgumentException("Enter a number for " + label + ".");
			}
			return result;
		}
	}

	private async Task DetectHardwareAsync()
	{
		HardwareInventory.LogPath = Path.Combine(store.Root, "logs", "hardware.log");
		HardwareInventory hardware = await Task.Run(() => HardwareInventory.DetectAsync(Engine.Executable));
		if (!featuresClosing)
		{
			settings.Hardware = hardware;
			SetContext(RequestedContext());
		}
	}

	private void Profile_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (uiReady)
		{
			InferencePreferences inference = settings.Inference;
			if (inference != null && ProfileCombo.SelectedValue is string profile)
			{
				inference.Profile = profile;
				SetContext(RequestedContext());
			}
		}
	}

	private void Cache_Changed(object sender, RoutedEventArgs e)
	{
		if (uiReady)
		{
			InferencePreferences inference = settings.Inference;
			if (inference != null)
			{
				inference.LaptopQ4 = LaptopQ4Check.IsChecked == true;
				inference.CustomCache = (CustomCacheCombo.SelectedValue as string) ?? "f16";
				UpdateAdvice();
			}
		}
	}

	private void Creative_Changed(object sender, RoutedEventArgs e)
	{
		if (uiReady)
		{
			RefreshCreativeFields();
		}
	}

	private void RefreshCreativeFields()
	{
		if (CreativeControls != null)
		{
			CreativeControls.IsEnabled = CreativeCheck.IsChecked == true;
		}
		if (TempBox != null)
		{
			TempBox.IsEnabled = CreativeCheck.IsChecked != true;
		}
	}

	private InferenceLaunch CurrentLaunch()
	{
		InferencePreferences inference = settings.Inference;
		int.TryParse(GpuBox.Text, out var result);
		int.TryParse(ExpertLayersBox.Text, out var result2);
		return InferenceLaunch.Resolve(new Settings
		{
			Hardware = settings.Hardware,
			GpuLayers = result,
			Inference = new InferencePreferences
			{
				Profile = inference.Profile,
				LaptopQ4 = inference.LaptopQ4,
				CustomCache = inference.CustomCache,
				ExpertCpuLayers = result2
			}
		}, modelInfo);
	}

	/// <summary>The automatic plan for the selected model and detected hardware, or null when the user
	/// picked a manual profile or detection hasn't finished.</summary>
	private InferenceLaunch? AutoPlan
	{
		get
		{
			if (SelectedProfile.Key != "Auto" || !modelInfoReady || (object)settings.Hardware == null)
			{
				return null;
			}
			long projector = ((settings.ProjectorPath.Length > 0 && File.Exists(settings.ProjectorPath)) ? new FileInfo(settings.ProjectorPath).Length : 0);
			return InferenceLaunch.PlanAuto(settings.Hardware, modelInfo, projector);
		}
	}

	private int RequestedContext()
	{
		return AutoPlan?.AutoContext ?? SelectedProfile.Requested(settings.Inference);
	}

	private void RenderProfile(InferenceLaunch launch, long freeSystem, long? freeGpu)
	{
		string label = SelectedProfile.Label;
		bool automatic = SelectedProfile.Key == "Auto";
		ContextSlider.IsEnabled = !automatic;
		ContextCombo.IsEnabled = !automatic;
		InferenceLaunch plan = AutoPlan;
		if (plan != null)
		{
			HardwareText.Text = plan.Summary + "\nAquila picks the best setup for your computer. Choose a manual profile only if you want to tune it yourself.";
			if (contextTokens != plan.AutoContext)
			{
				SetContext(plan.AutoContext);
			}
			return;
		}
		LaptopQ4Check.Visibility = ((!(SelectedProfile.Key == "Laptop")) ? Visibility.Collapsed : Visibility.Visible);
		CustomCachePanel.Visibility = ((!(SelectedProfile.Key == "Custom")) ? Visibility.Collapsed : Visibility.Visible);
		GpuBox.IsEnabled = SelectedProfile.Key == "Custom";
		ExpertLayersBox.IsEnabled = modelInfoReady && modelInfo.ExpertCount > 1 && !launch.CpuOnly && SelectedProfile.Key != "High";
		ExpertsHint.Text = ((!modelInfoReady) ? "" : ((modelInfo.ExpertCount <= 1) ? "This model does not have MoE experts." : ((SelectedProfile.Key == "High") ? "Profile A keeps all layers on the GPU." : (launch.CpuOnly ? "MoE expert placement needs a GPU." : $"0–{modelInfo.BlockCount} layers. Only the expert weights in those layers move to CPU."))));
		HardwareInventory hardware = settings.Hardware;
		if ((object)hardware == null)
		{
			HardwareText.Text = "Detecting hardware…";
			return;
		}
		long num = 1073741824L;
		InferenceGpu gpu = hardware.Gpu;
		string value = (((object)gpu != null) ? ($"{gpu.Name} · {(double)gpu.DedicatedBytes / (double)num:0.#} GiB dedicated" + (freeGpu.HasValue ? $" · {(double)freeGpu.Value / (double)num:0.0} GiB free" : "")) : "No supported dedicated GPU found");
		string value2 = (launch.CpuOnly ? "CPU" : ((launch.ExpertCpuLayers > 0) ? "GPU + CPU experts" : (launch.FullOffload ? "full GPU" : ((launch.Layers == "auto") ? "automatic GPU fit" : ("GPU layers " + launch.Layers)))));
		HardwareText.Text = $"{label} · {value2} · {launch.Cache} KV\n{value}\nRAM {(double)freeSystem / (double)num:0.0} GiB free / {(double)hardware.RamBytes / (double)num:0.0} GiB";
	}

	internal Task SendForVoiceVerification(string text)
	{
		Composer.Text = text;
		return SendAsync();
	}

	internal Task SpeakForVoiceVerification(ChatSession session, Turn turn)
	{
		return SpeakCompletedReply(session, turn);
	}

	private void InitializeVoice()
	{
		speech.Status += delegate(string text)
		{
			base.Dispatcher.InvokeAsync(delegate
			{
				if (!featuresClosing)
				{
					VoiceStatusText.Text = text;
					VoiceStopButton.IsEnabled = speech.Active;
				}
			});
		};
		speech.Frame += delegate(SpeechFrame frame)
		{
			base.Dispatcher.InvokeAsync(delegate
			{
				if (!featuresClosing)
				{
					if (frame.Playing && speech.CurrentUtterance == frame.Utterance)
					{
						avatarState = "speaking";
						avatarUtterance = frame.Utterance;
						spriteView?.SetState("speaking");
						avatar.SetSpeechFrameAsync(frame.Utterance, frame.Mood, frame.Mouth);
					}
					else if (!frame.Playing && avatarUtterance == frame.Utterance)
					{
						SetAvatarState("idle");
					}
				}
			});
		};
		VoiceStatusText.Text = (settings.Voice.Enabled ? ("Voice enabled · " + speech.VoiceLabel(settings.Voice.Speaker)) : "Voice off · choose Voice… to enable");
	}

	private void Voice_Click(object sender, RoutedEventArgs e)
	{
		if (voiceWindow != null)
		{
			voiceWindow.Activate();
		}
		else
		{
			if (busy || !ReadSettings())
			{
				return;
			}
			voiceWindow = new VoiceWindow(settings.Voice, speech, async delegate(VoiceOptions options, bool preview)
			{
				if (busy)
				{
					throw new InvalidOperationException("Finish or stop the current reply before changing its voice.");
				}
				if (preview)
				{
					await speech.SpeakAsync(new Turn
					{
						Answer = "Hello. I am here, and ready to talk with you.",
						SpeechMood = ((options.Mood == "Auto") ? "warm" : options.Mood)
					}, options);
				}
				else
				{
					bool num = settings.Voice.RevealTags != options.RevealTags;
					speech.Stop();
					settings.Voice = options;
					store.SaveSettings(settings);
					VoiceStatusText.Text = (options.Enabled ? ("Voice enabled · " + speech.VoiceLabel(options.Speaker)) : "Voice off");
					if (num)
					{
						RenderChat();
					}
				}
			})
			{
				Owner = this
			};
			voiceWindow.Closed += delegate
			{
				voiceWindow = null;
			};
			voiceWindow.Show();
		}
	}

	private async Task SpeakCompletedReply(ChatSession session, Turn turn)
	{
		if (!settings.Voice.Enabled || turn.Status != "complete")
		{
			return;
		}
		VoiceOptions options = JsonSerializer.Deserialize<VoiceOptions>(JsonSerializer.Serialize(settings.Voice));
		try
		{
			await speech.SpeakAsync(turn, options);
			if (!featuresClosing && chats.Contains(session))
			{
				store.SaveChat(session);
				if (current == session && !busy)
				{
					RenderChat();
				}
			}
		}
		catch (Exception ex)
		{
			if (!featuresClosing)
			{
				VoiceStatusText.Text = "Voice: " + ex.Message.Truncate(400);
			}
		}
	}

	private async void ReplayVoice_Click(object sender, RoutedEventArgs e)
	{
		if (busy || current == null)
		{
			return;
		}
		ChatSession session = current;
		Turn turn = session.Turns.LastOrDefault((Turn t) => t.Status == "complete");
		if (turn == null)
		{
			Notice("Complete a reply before replaying speech.");
			return;
		}
		VoiceOptions options = JsonSerializer.Deserialize<VoiceOptions>(JsonSerializer.Serialize(settings.Voice));
		try
		{
			await speech.SpeakAsync(turn, options);
			if (!featuresClosing && chats.Contains(session))
			{
				store.SaveChat(session);
			}
		}
		catch (Exception ex)
		{
			if (!featuresClosing)
			{
				VoiceStatusText.Text = "Voice: " + ex.Message.Truncate(400);
			}
		}
	}

	private void StopVoice_Click(object sender, RoutedEventArgs e)
	{
		speech.Stop();
	}

	public MainWindow(string? dataRoot = null, string? speechAssets = null)
	{
		InitializeComponent();
		base.CommandBindings.Add(new CommandBinding(DeleteChatCommand, DeleteChat_Executed, DeleteChat_CanExecute));
		store = new Storage(dataRoot);
		this.settings = store.LoadSettings();
		Settings settings = this.settings;
		if (settings.Inference == null)
		{
			InferencePreferences obj = new InferencePreferences
			{
				CustomContext = this.settings.Context
			};
			InferencePreferences inferencePreferences = obj;
			settings.Inference = obj;
		}
		memories = new MemoryStore(store.Root);
		semantic = new SemanticMemory(memories, store.Root);
		personalities = new PersonalityStore(store.Root);
		engine = new Engine(store.Root);
		speech = new SpeechService(store.Root, speechAssets);
		avatar = new AvatarHost(store.Root);
		engine.Status += delegate(string text)
		{
			base.Dispatcher.InvokeAsync(() => StatusText.Text = text);
		};
		chats = new ObservableCollection<ChatSession>(store.LoadChats());
		ChatsList.ItemsSource = chats;
		ShowSettings();
		StatusText.Text = "Model not loaded";
		if (chats.Count > 0)
		{
			ChatsList.SelectedItem = chats[0];
		}
		else
		{
			NewChat();
		}
		renderTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(100.0)
		};
		renderTimer.Tick += delegate
		{
			RenderLive(final: false);
		};
		renderTimer.Start();
		InitializeFeatures();
		InitializeAvatar();
		InitializeVoice();
		if (store.RecoveryNotice != null)
		{
			Notice(store.RecoveryNotice);
		}
	}

	private void ShowSettings()
	{
		RefreshPaths();
		MemoryBox.Text = settings.Memory;
		InstructionsBox.Text = settings.Instructions;
		ShowInferenceSettings();
		ReplyEmojiCheck.IsChecked = settings.ReplyEmoji;
		LearnMemoryCheck.IsChecked = settings.LearnMemories;
		UseMemoryCheck.IsChecked = settings.UseMemories;
		SemanticMemoryCheck.IsChecked = settings.SemanticMemories;
		RefreshPersonalities();
		TempBox.Text = settings.Temperature.ToString(CultureInfo.InvariantCulture);
		GpuBox.Text = settings.GpuLayers.ToString();
		MaxTokensBox.Text = settings.MaxTokens.ToString();
		ThinkingCheck.IsChecked = settings.Thinking;
		ToolsCheck.IsChecked = settings.WorkspaceTools;
		CpuBox.Text = settings.CpuThreads.ToString();
		ThinkingBudgetBox.Text = settings.ThinkingBudget.ToString();
		LookupCheck.IsChecked = settings.RequireLookup;
		ContextCombo.SelectedItem = ContextCombo.Items.Cast<ComboBoxItem>().FirstOrDefault((ComboBoxItem i) => i.Content.ToString() == settings.Context.ToString()) ?? ContextCombo.Items[1];
	}

	private void RefreshPaths()
	{
		ModelName.Text = (string.IsNullOrEmpty(settings.ModelPath) ? "No model selected" : Path.GetFileNameWithoutExtension(settings.ModelPath));
		ModelName.ToolTip = settings.ModelPath;
		VisionStatus.Text = (string.IsNullOrEmpty(settings.ProjectorPath) ? "Vision: no projector selected" : ("Vision ready · " + Path.GetFileName(settings.ProjectorPath)));
		WorkspaceName.Text = (string.IsNullOrEmpty(settings.Workspace) ? "No folder selected" : settings.Workspace);
		WorkspaceName.ToolTip = settings.Workspace;
	}

	private bool ReadSettings()
	{
		try
		{
			Settings settings = new Settings
			{
				ModelPath = this.settings.ModelPath,
				ProjectorPath = this.settings.ProjectorPath,
				VisionDisabled = this.settings.VisionDisabled,
				Workspace = this.settings.Workspace,
				Memory = MemoryBox.Text,
				Instructions = InstructionsBox.Text,
				Context = contextTokens,
				ReplyEmoji = (ReplyEmojiCheck.IsChecked == true),
				MaxTokens = int.Parse(MaxTokensBox.Text, CultureInfo.InvariantCulture),
				Temperature = double.Parse(TempBox.Text, CultureInfo.InvariantCulture),
				GpuLayers = int.Parse(GpuBox.Text, CultureInfo.InvariantCulture),
				CpuThreads = int.Parse(CpuBox.Text, CultureInfo.InvariantCulture),
				Inference = ReadInferenceSettings(),
				Hardware = this.settings.Hardware,
				ThinkingBudget = int.Parse(ThinkingBudgetBox.Text, CultureInfo.InvariantCulture),
				RequireLookup = (LookupCheck.IsChecked == true),
				LearnMemories = (LearnMemoryCheck.IsChecked == true),
				UseMemories = (UseMemoryCheck.IsChecked == true),
				SemanticMemories = (SemanticMemoryCheck.IsChecked == true),
				PersonalityId = ((PersonalityCombo.SelectedItem as PersonalityProfile)?.Id ?? "default"),
				Voice = this.settings.Voice,
				AvatarEnabled = this.settings.AvatarEnabled,
				Avatar = this.settings.Avatar,
				AvatarFolder = this.settings.AvatarFolder,
				AvatarModelPath = this.settings.AvatarModelPath,
				Sprite = this.settings.Sprite,
				WebMode = this.settings.WebMode,
				WebKeyProtected = this.settings.WebKeyProtected,
				Thinking = (ThinkingCheck.IsChecked == true),
				WorkspaceTools = (ToolsCheck.IsChecked == true)
			};
			settings.Validate();
			store.SaveSettings(settings);
			this.settings = settings;
			return true;
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is FormatException || ex is OverflowException || ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			Notice("Settings: " + ex.Message);
			return false;
		}
	}

	private void NewChat()
	{
		if (!busy)
		{
			ChatSession chatSession = chats.FirstOrDefault((ChatSession c) => c.Turns.Count == 0);
			if (chatSession == null)
			{
				chatSession = new ChatSession();
				chats.Insert(0, chatSession);
			}
			ChatsList.SelectedItem = chatSession;
		}
	}

	private void New_Click(object sender, RoutedEventArgs e)
	{
		NewChat();
	}

	private void Chats_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (ChatsList.SelectedItem is ChatSession chatSession)
		{
			speech.Stop();
			current = chatSession;
			RenderChat();
		}
	}

	private void RenderChat()
	{
		Messages.Children.Clear();
		MetricsText.Text = "";
		NoticeBox.Visibility = Visibility.Collapsed;
		if (current == null)
		{
			return;
		}
		ChatTitle.Text = current.Title;
		Welcome.Visibility = ((current.Turns.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		foreach (Turn turn in current.Turns)
		{
			MessageView messageView = new MessageView("You");
			messageView.Update(Attachments.VisibleText(turn), "", null, "", renderMarkdown: false);
			Messages.Children.Add(messageView);
			MessageView messageView2 = new MessageView("Aquila");
			messageView2.Update(VoiceDelivery.Display(turn, settings.Voice.RevealTags), turn.Reasoning, turn.Activity, Label(turn), renderMarkdown: true, turn.Answer);
			Messages.Children.Add(messageView2);
		}
		RefreshUsage();
		followBottom = true;
		ChatScroll.ScrollToEnd();
		RefreshContextControls();
	}

	private static string Label(Turn turn)
	{
		return turn.Status switch
		{
			"complete" => turn.Metrics + ((turn.PersonalityId != "default" && turn.PersonalityName.Length > 0) ? (" · " + turn.PersonalityName) : ""), 
			"generating" => "Working…", 
			"cancelled" => "Stopped", 
			"error" => "Error · see activity", 
			"limit" => "Response limit reached", 
			_ => "Interrupted", 
		};
	}

	private async void Send_Click(object sender, RoutedEventArgs e)
	{
		await SendAsync();
	}

	private Task? pendingLearning;

	private async Task LearnInBackgroundAsync(ChatSession session, Turn turn)
	{
		try
		{
			if ((object)(await new MemoryLearner(engine, memories, personalities).LearnAsync(settings, session, turn, monitorCancellation.Token)) != null)
			{
				turn.Activity.Add("Memory: " + turn.MemoryLearningStatus + $" · {turn.MemoryPromptTokens + turn.MemoryCompletionTokens:N0} tokens included in session usage.");
			}
		}
		catch (OperationCanceledException)
		{
			turn.MemoryLearningStatus = "stopped";
		}
		catch (Exception ex)
		{
			turn.MemoryLearningStatus = "failed";
			turn.Activity.Add("Memory update failed: " + ex.Message + ". Your completed answer is saved.");
		}
		try
		{
			store.SaveChat(session);
		}
		catch (Exception ex2) when (ex2 is IOException || ex2 is UnauthorizedAccessException)
		{
		}
	}

	private async Task SendAsync()
	{
		await inferenceStartup;
		if (pendingLearning != null)
		{
			Task learning = pendingLearning;
			pendingLearning = null;
			await learning;
		}
		if (busy || attaching || (string.IsNullOrWhiteSpace(Composer.Text) && pendingAttachments.Count == 0) || current == null || !ReadSettings())
		{
			return;
		}
		if (!File.Exists(settings.ModelPath))
		{
			Notice("Choose a GGUF model in Settings before sending a message.");
			return;
		}
		if (Composer.Text.Length > 100000)
		{
			Notice("This message is too long. Please use a smaller excerpt.");
			return;
		}
		if (speech.Active || !settings.Voice.KeepLoaded)
		{
			speech.Stop();
		}
		NoticeBox.Visibility = Visibility.Collapsed;
		ChatSession session = current;
		Turn turn = new Turn
		{
			User = (string.IsNullOrWhiteSpace(Composer.Text) ? "Please review the attached files." : Composer.Text.Trim()),
			Attachments = pendingAttachments.ToList(),
			Status = "generating"
		};
		if (session.Turns.Count == 0)
		{
			session.Title = turn.User.Replace('\n', ' ').Replace('\r', ' ').Truncate(42);
		}
		session.Turns.Add(turn);
		store.SaveChat(session);
		if (chats.IndexOf(session) > 0)
		{
			chats.Move(chats.IndexOf(session), 0);
		}
		ChatsList.Items.Refresh();
		ChatTitle.Text = session.Title;
		Welcome.Visibility = Visibility.Collapsed;
		MessageView messageView = new MessageView("You");
		messageView.Update(Attachments.VisibleText(turn), "", null, "", renderMarkdown: false);
		Messages.Children.Add(messageView);
		live = new MessageView("Aquila");
		live.Update("", "", null, "Working…");
		Messages.Children.Add(live);
		activeTurn = turn;
		Composer.Clear();
		pendingAttachments.Clear();
		RenderAttachmentChips();
		followBottom = true;
		SetBusy(value: true);
		operation = new CancellationTokenSource();
		Agent agent = new Agent(engine, memories, semantic, personalities);
		agent.ApproveWeb = (string action) => Task.FromResult(MessageBox.Show(this, action + "\n\nThis query or URL will be sent over the internet.", "Allow web lookup?", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);
		agent.AnswerStarted += delegate
		{
			SetAvatarState("thinking", turn.Id);
		};
		gestures?.Reset();
		agent.Token += delegate(string text, bool reasoning)
		{
			dirtyRender = true;
			if (!reasoning)
			{
				gestures?.Feed(text);
				base.Dispatcher.InvokeAsync(delegate
				{
					if (busy && avatarState == "thinking")
					{
						spriteView?.SetState("talking");
					}
				});
			}
		};
		agent.Activity += delegate(string text)
		{
			dirtyRender = true;
			StatusText.Text = (text.StartsWith("Recalled memory") ? "Using saved memories…" : "Working…");
		};
		try
		{
			await agent.RunAsync(settings, session, turn, operation.Token);
			gestures?.Flush();
			SetAvatarState("idle");
			store.SaveChat(session);
			RenderLive(final: true);
			if (turn.Status == "complete")
			{
				SpeakCompletedReply(session, turn);
			}
			if (settings.LearnMemories && turn.Status == "complete" && MemoryLearner.Eligible(turn.User))
			{
				// Learn in the background on the engine's second slot, so the chat stays usable and its
				// prompt cache is untouched. The next send waits for it only if it is still running.
				pendingLearning = LearnInBackgroundAsync(session, turn);
			}
			MetricsText.Text = turn.Metrics;
			StatusText.Text = "Model ready · on this device";
			if (turn.Status == "limit")
			{
				Notice(turn.Activity.LastOrDefault() ?? "Response reached the token limit.");
			}
		}
		catch (OperationCanceledException)
		{
			AvatarInterrupted();
			turn.Status = "cancelled";
			turn.Activity.Add("Generation stopped. Partial output has been saved.");
			StatusText.Text = (engine.Ready ? "Stopped · model remains loaded" : "Loading stopped");
		}
		catch (Exception ex4)
		{
			AvatarInterrupted();
			turn.Status = "error";
			turn.Activity.Add("Error: " + ex4.Message);
			Notice(ex4.Message);
			StatusText.Text = (engine.Ready ? "Model ready · last request failed" : "Model not loaded");
		}
		finally
		{
			RenderLive(final: true);
			store.SaveChat(session);
			activeTurn = null;
			live = null;
			operation.Dispose();
			operation = null;
			SetBusy(value: false);
			RefreshUsage();
			ChatsList.Items.Refresh();
			Composer.Focus();
		}
	}

	private void RenderLive(bool final)
	{
		if (live != null && activeTurn != null && (dirtyRender || final))
		{
			live.Update(VoiceDelivery.Display(activeTurn, settings.Voice.RevealTags), activeTurn.Reasoning, activeTurn.Activity, Label(activeTurn), final, activeTurn.Answer);
			RefreshUsage();
			dirtyRender = false;
			if (followBottom)
			{
				ChatScroll.ScrollToEnd();
			}
		}
	}

	private void SetBusy(bool value)
	{
		busy = value;
		NewButton.IsEnabled = !value;
		ChatsList.IsEnabled = !value;
		SetupControls.IsEnabled = !value;
		VoiceButton.IsEnabled = !value;
		VoiceReplayButton.IsEnabled = !value;
		MemoriesButton.IsEnabled = !value;
		RefreshContextControls();
		CommandManager.InvalidateRequerySuggested();
		AttachButton.IsEnabled = !value && !attaching;
		AttachmentChips.IsEnabled = !value;
		SendButton.Visibility = (value ? Visibility.Collapsed : Visibility.Visible);
		StopButton.Visibility = ((!value) ? Visibility.Collapsed : Visibility.Visible);
		StopButton.IsEnabled = value;
	}

	private void Stop_Click(object sender, RoutedEventArgs e)
	{
		operation?.Cancel();
		StopButton.IsEnabled = false;
		StatusText.Text = "Stopping…";
	}

	private void Composer_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
		{
			e.Handled = true;
			SendAsync();
		}
	}

	private void Chat_ScrollChanged(object sender, ScrollChangedEventArgs e)
	{
		if (e.ExtentHeightChange == 0.0)
		{
			followBottom = ChatScroll.ScrollableHeight - ChatScroll.VerticalOffset < 50.0;
		}
	}

	private async void Model_Click(object sender, RoutedEventArgs e)
	{
		if (ReadSettings())
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "GGUF models (*.gguf)|*.gguf",
				Title = "Choose a local language model"
			};
			string text = Path.Combine(AppContext.BaseDirectory, "models");
			if (Directory.Exists(text))
			{
				openFileDialog.InitialDirectory = text;
			}
			if (openFileDialog.ShowDialog(this) == true)
			{
				await SelectModelAsync(openFileDialog.FileName);
			}
		}
	}

	private async void Load_Click(object sender, RoutedEventArgs e)
	{
		await inferenceStartup;
		if (busy || !ReadSettings())
		{
			return;
		}
		operation = new CancellationTokenSource();
		SetBusy(value: true);
		NoticeBox.Visibility = Visibility.Collapsed;
		try
		{
			await engine.StartAsync(settings, operation.Token);
			StatusText.Text = "Model ready · on this device";
		}
		catch (OperationCanceledException)
		{
			StatusText.Text = "Loading stopped";
		}
		catch (Exception ex2)
		{
			Notice(ex2.Message);
			StatusText.Text = "Could not load model";
		}
		finally
		{
			operation.Dispose();
			operation = null;
			SetBusy(value: false);
		}
	}

	private void Unload_Click(object sender, RoutedEventArgs e)
	{
		if (!busy)
		{
			engine.Stop();
			StatusText.Text = "Model unloaded · memory released";
		}
	}

	private void Workspace_Click(object sender, RoutedEventArgs e)
	{
		if (!ReadSettings())
		{
			return;
		}
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = "Choose a project folder for file search and reading",
			Multiselect = false
		};
		if (openFolderDialog.ShowDialog(this) != true)
		{
			return;
		}
		try
		{
			new WorkspaceTools(openFolderDialog.FolderName);
			settings.Workspace = openFolderDialog.FolderName;
			store.SaveSettings(settings);
			RefreshPaths();
		}
		catch (Exception ex)
		{
			Notice(ex.Message);
		}
	}

	private void ClearWorkspace_Click(object sender, RoutedEventArgs e)
	{
		if (ReadSettings())
		{
			settings.Workspace = "";
			store.SaveSettings(settings);
			RefreshPaths();
		}
	}

	private void SaveSettings_Click(object sender, RoutedEventArgs e)
	{
		if (ReadSettings())
		{
			StatusText.Text = "Settings saved · applied on next message";
		}
	}

	private void Settings_Click(object sender, RoutedEventArgs e)
	{
		SettingsColumn.Width = ((SettingsColumn.Width.Value == 0.0) ? new GridLength(320.0) : new GridLength(0.0));
	}

	private void Internet_Click(object sender, RoutedEventArgs e)
	{
		if (!busy && !attaching && ReadSettings())
		{
			InternetWindow internetWindow = new InternetWindow(settings, delegate
			{
				store.SaveSettings(settings);
			});
			internetWindow.Owner = this;
			internetWindow.ShowDialog();
		}
	}

	private void Export_Click(object sender, RoutedEventArgs e)
	{
		if (current == null || current.Turns.Count == 0)
		{
			Notice("Send a message before exporting this conversation.");
			return;
		}
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Filter = "Markdown (*.md)|*.md",
			FileName = "Aquila conversation.md"
		};
		if (saveFileDialog.ShowDialog(this) != true)
		{
			return;
		}
		StringBuilder stringBuilder = new StringBuilder("# " + current.Title + "\n\n");
		stringBuilder.AppendLine($"Cumulative tokens: {current.PromptTokens:N0} input + {current.CompletionTokens:N0} output = {current.TotalTokens:N0}" + (current.UsageIncomplete ? " (partial counts)" : "") + "\n");
		ContextCheckpoint compactedContext = current.CompactedContext;
		if (compactedContext != null)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(98, 2, stringBuilder2);
			handler.AppendLiteral("## Context summary\n\n");
			handler.AppendFormatted(compactedContext.Summary);
			handler.AppendLiteral("\n\n_Summarizes ");
			handler.AppendFormatted(compactedContext.CoveredTurns);
			handler.AppendLiteral(" earlier turns for model input. Full original history follows._\n");
			stringBuilder3.AppendLine(ref handler);
		}
		foreach (Turn turn in current.Turns)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(29, 4, stringBuilder2);
			handler.AppendLiteral("## You\n\n");
			handler.AppendFormatted(Attachments.VisibleText(turn));
			handler.AppendLiteral("\n\n## Aquila\n\n");
			handler.AppendFormatted(turn.Answer);
			handler.AppendLiteral("\n\n_");
			handler.AppendFormatted(turn.Status);
			handler.AppendLiteral(" · ");
			handler.AppendFormatted(turn.Metrics);
			handler.AppendLiteral("_\n");
			stringBuilder4.AppendLine(ref handler);
		}
		try
		{
			File.WriteAllText(saveFileDialog.FileName, stringBuilder.ToString());
			StatusText.Text = "Conversation exported";
		}
		catch (Exception ex)
		{
			Notice(ex.Message);
		}
	}

	private void Data_Click(object sender, RoutedEventArgs e)
	{
		Process.Start(new ProcessStartInfo("explorer.exe")
		{
			ArgumentList = { store.Root }
		});
	}

	private void Memories_Click(object sender, RoutedEventArgs e)
	{
		if (busy || !ReadSettings())
		{
			return;
		}
		MemoryWindow memoryWindow = new MemoryWindow(memories, settings.Workspace, delegate(MemorySource source)
		{
			ChatSession chatSession = chats.FirstOrDefault((ChatSession c) => c.Id == source.ChatId);
			if (chatSession == null)
			{
				Notice("The source conversation is no longer available. The supporting quote is preserved in Memories.");
			}
			else
			{
				ChatsList.SelectedItem = chatSession;
				Notice("Memory source: “" + source.Quote + "”");
				int num = chatSession.Turns.FindIndex((Turn t) => t.Id == source.TurnId);
				if (num >= 0 && Messages.Children.Count > num * 2)
				{
					((FrameworkElement)Messages.Children[num * 2]).BringIntoView();
				}
			}
		});
		memoryWindow.Owner = this;
		memoryWindow.ShowDialog();
	}

	private void Notice(string text)
	{
		NoticeText.Text = text.Truncate(1800);
		NoticeBox.Visibility = Visibility.Visible;
	}

	private void Dismiss_Click(object sender, RoutedEventArgs e)
	{
		NoticeBox.Visibility = Visibility.Collapsed;
	}

	private async void Window_Closing(object? sender, CancelEventArgs e)
	{
		if (featuresClosing)
		{
			return;
		}
		if (busy || attaching || speech.Active)
		{
			e.Cancel = true;
			operation?.Cancel();
			speech.Stop();
			DispatcherTimer closer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(150.0)
			};
			closer.Tick += delegate
			{
				if (!busy && !attaching && !speech.Active)
				{
					closer.Stop();
					Close();
				}
			};
			closer.Start();
		}
		else
		{
			e.Cancel = true;
			ReadSettings();
			featuresClosing = true;
			monitorCancellation.Cancel();
			renderTimer.Stop();
			engine.Dispose();
			semantic.Dispose();
			await speech.DisposeAsync();
			await avatar.DisposeAsync();
			if (avatarPopout != null)
			{
				AvatarPopoutWindow window = avatarPopout;
				avatarPopout = null;
				window.Close();
			}
			animationsWindow?.Close();
			gestures?.Dispose();
			base.Dispatcher.BeginInvoke(new Action(base.Close));
		}
	}
}
