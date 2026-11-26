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
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using AquilaAvatarProtocol;
using Microsoft.Win32;

namespace AquilaLocal;

public class MainWindow : Window, IComponentConnector, IStyleConnector
{
	private record InstalledModel(string Name, string Path);

	private readonly AvatarHost avatar;

	private AvatarWindow? avatarWindow;

	private string avatarState = "idle";

	private string avatarUtterance = "";

	private int avatarTransition;

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

	internal ColumnDefinition SettingsColumn;

	internal Button NewButton;

	internal ComboBox GpuAdapterCombo;

	internal TextBlock GpuSummary;

	internal ItemsControl GpuAppsList;

	internal TextBlock GpuFootnote;

	internal Button VoiceButton;

	internal Button VoiceReplayButton;

	internal Button VoiceStopButton;

	internal TextBlock VoiceStatusText;

	internal Button MemoriesButton;

	internal ListBox ChatsList;

	internal Button CompactButton;

	internal Button SummaryButton;

	internal TextBlock ChatTitle;

	internal TextBlock StatusText;

	internal TextBlock SessionUsageText;

	internal ScrollViewer ChatScroll;

	internal StackPanel Messages;

	internal StackPanel Welcome;

	internal Border NoticeBox;

	internal TextBlock NoticeText;

	internal WrapPanel AttachmentChips;

	internal Button SendButton;

	internal Button StopButton;

	internal TextBox Composer;

	internal Button AttachButton;

	internal TextBlock MetricsText;

	internal StackPanel SetupControls;

	internal ComboBox PersonalityCombo;

	internal TextBlock PersonalitySummary;

	internal TextBlock ModelName;

	internal ComboBox InstalledModelsCombo;

	internal Button LoadButton;

	internal Button UnloadButton;

	internal TextBlock VisionStatus;

	internal ComboBox ProfileCombo;

	internal TextBlock HardwareText;

	internal CheckBox LaptopQ4Check;

	internal StackPanel CustomCachePanel;

	internal ComboBox CustomCacheCombo;

	internal TextBlock ExpertsLabel;

	internal TextBox ExpertLayersBox;

	internal TextBlock ExpertsHint;

	internal ComboBox ContextCombo;

	internal TextBlock ContextValue;

	internal Slider ContextSlider;

	internal Canvas ThresholdCanvas;

	internal Border ProfileTargetMarker;

	internal Border MemoryEstimateMarker;

	internal Border MemoryEstimateLine;

	internal TextBlock ContextThresholds;

	internal TextBlock ContextRisk;

	internal TextBlock WorkspaceName;

	internal CheckBox ToolsCheck;

	internal CheckBox LookupCheck;

	internal CheckBox LearnMemoryCheck;

	internal CheckBox UseMemoryCheck;

	internal CheckBox SemanticMemoryCheck;

	internal TextBox MemoryBox;

	internal TextBox MaxTokensBox;

	internal TextBox TempBox;

	internal CheckBox CreativeCheck;

	internal StackPanel CreativeControls;

	internal TextBox CreativeTempBox;

	internal TextBox MinPBox;

	internal TextBox TopPBox;

	internal TextBox DryMultiplierBox;

	internal TextBox DryBaseBox;

	internal TextBox DryLengthBox;

	internal TextBox GpuBox;

	internal TextBox CpuBox;

	internal CheckBox ThinkingCheck;

	internal TextBox ThinkingBudgetBox;

	internal TextBlock ThinkingSummary;

	internal CheckBox ReplyEmojiCheck;

	internal TextBox InstructionsBox;

	private bool _contentLoaded;

	internal AvatarHost AvatarForVerification => avatar;

	public static RoutedUICommand DeleteChatCommand { get; } = new RoutedUICommand("Delete chat", "DeleteChatCommand", typeof(MainWindow));


	private InferenceProfile SelectedProfile => InferenceProfile.Resolve(settings.Inference, settings.Hardware);

	internal SpeechService SpeechForVerification => speech;

	private void InitializeAvatar()
	{
		if (settings.AvatarEnabled)
		{
			StartSavedAvatar();
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
		if (avatarWindow != null)
		{
			((Window)avatarWindow).Activate();
			return;
		}
		AvatarWindow obj = new AvatarWindow(settings, avatar, ApplyAvatar, PreviewAvatar);
		((Window)obj).Owner = (Window)(object)this;
		avatarWindow = obj;
		((Window)avatarWindow).Closed += delegate
		{
			avatarWindow = null;
		};
		((Window)avatarWindow).Show();
	}

	private void SetAvatarState(string state, string utterance = "")
	{
		avatarTransition++;
		avatarState = state;
		avatarUtterance = utterance;
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
		ListBoxItem val = (ListBoxItem)((sender is ListBoxItem) ? sender : null);
		if (val != null && !busy && !attaching)
		{
			val.IsSelected = true;
			((UIElement)val).Focus();
		}
	}

	private ChatSession? DeleteTarget(object? parameter)
	{
		return (parameter as ChatSession) ?? (((Selector)ChatsList).SelectedItem as ChatSession);
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
		((RoutedEventArgs)e).Handled = true;
	}

	private void DeleteChat_Executed(object sender, ExecutedRoutedEventArgs e)
	{
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Invalid comparison between Unknown and I4
		((RoutedEventArgs)e).Handled = true;
		ChatSession chatSession = DeleteTarget(e.Parameter);
		if (busy || attaching || featuresClosing || chatSession == null || !chats.Contains(chatSession) || !(ConfirmDeleteForVerification?.Invoke(chatSession) ?? ((int)MessageBox.Show((Window)(object)this, "Delete “" + chatSession.Title.Truncate(100) + "” and its saved attachment snapshots?\n\nThis cannot be undone in the app. Memories learned from this chat are kept; use Memories to forget them. Existing exports and backups are kept.", "Delete chat?", (MessageBoxButton)4, (MessageBoxImage)48, (MessageBoxResult)7) == 6)))
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
					((Selector)ChatsList).SelectedItem = chats[Math.Min(val, chats.Count - 1)];
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
			((UIElement)CompactButton).IsEnabled = !busy && !attaching && !featuresClosing && current != null && ConversationContext.CanCompact(current);
			((UIElement)SummaryButton).IsEnabled = !busy && !attaching;
			((UIElement)SummaryButton).Visibility = (Visibility)((current?.CompactedContext == null) ? 2 : 0);
		}
	}

	private async void Compact_Click(object sender, RoutedEventArgs e)
	{
		if (busy || attaching || current == null || !ConversationContext.CanCompact(current) || !ReadSettings() || (int)MessageBox.Show((Window)(object)this, "Summarize earlier conversation for future model requests? The four most recent complete turns and the full visible history are kept.\n\nSummaries can omit or misstate details, including image content. Review or reset the result through View summary. This runs the selected local model and adds its token usage to this session.", "Compact context?", (MessageBoxButton)4, (MessageBoxImage)64, (MessageBoxResult)7) != 6)
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
			ContextCheckpoint contextCheckpoint2 = (chat.CompactedContext = await new ChatCompactor(engine, delegate(string text)
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
			Notice($"Context compacted through {contextCheckpoint2.CoveredTurns} earlier turns. Full chat history is retained. Review the summary before relying on important details." + ((count > 4) ? $" {count} complete turns remain unsummarized; compact again to continue." : ""));
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
		((Window)contextSummaryWindow).Owner = (Window)(object)this;
		((Window)contextSummaryWindow).ShowDialog();
	}

	private void RefreshPersonalities()
	{
		refreshingPersonalities = true;
		try
		{
			List<PersonalityProfile> list = personalities.List();
			((ItemsControl)PersonalityCombo).ItemsSource = list;
			((Selector)PersonalityCombo).SelectedItem = list.FirstOrDefault((PersonalityProfile p) => p.Id == settings.PersonalityId) ?? list[0];
			UpdatePersonalitySummary();
		}
		finally
		{
			refreshingPersonalities = false;
		}
	}

	private void Personality_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (((Selector)PersonalityCombo).SelectedItem is PersonalityProfile personalityProfile)
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
		if (PersonalitySummary != null && ((Selector)PersonalityCombo).SelectedItem is PersonalityProfile personalityProfile)
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
			((Window)personalityWindow).Owner = (Window)(object)this;
			((Window)personalityWindow).ShowDialog();
			RefreshPersonalities();
			settings.PersonalityId = ((PersonalityProfile)((Selector)PersonalityCombo).SelectedItem).Id;
			store.SaveSettings(settings);
		}
		catch (Exception ex)
		{
			Notice(ex.Message);
		}
	}

	private void InitializeFeatures()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		double width = ((FrameworkElement)this).Width;
		Rect workArea = SystemParameters.WorkArea;
		((FrameworkElement)this).Width = Math.Min(width, ((Rect)(ref workArea)).Width - 20.0);
		double height = ((FrameworkElement)this).Height;
		workArea = SystemParameters.WorkArea;
		((FrameworkElement)this).Height = Math.Min(height, ((Rect)(ref workArea)).Height - 20.0);
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
		if (!((ToggleButton)ThinkingCheck).IsChecked.GetValueOrDefault())
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
			((FrameworkElement)SessionUsageText).ToolTip = $"Cumulative server-reported input/output across chat, tools, memory learning and context compaction. Includes repeated history, cached input and thinking. Memory learning: {value:N0} tokens included. Context compaction: {value2:N0} tokens included. This is not context occupancy. Interrupted requests can have incomplete counts.";
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
			((ItemsControl)InstalledModelsCombo).ItemsSource = list;
			((Selector)InstalledModelsCombo).SelectedItem = list.FirstOrDefault((InstalledModel m) => m.Path.Equals(settings.ModelPath, StringComparison.OrdinalIgnoreCase));
		}
		finally
		{
			refreshingModels = false;
		}
	}

	private async void InstalledModels_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (uiReady && !refreshingModels && !busy && ((Selector)InstalledModelsCombo).SelectedItem is InstalledModel installedModel && !installedModel.Path.Equals(settings.ModelPath, StringComparison.OrdinalIgnoreCase))
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
			SetContext(SelectedProfile.Requested(settings.Inference));
		}
	}

	private void Projector_Click(object sender, RoutedEventArgs e)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Expected O, but got Unknown
		if (!busy && ReadSettings())
		{
			OpenFileDialog val = new OpenFileDialog
			{
				Title = "Choose this model's matching mmproj vision projector",
				Filter = "GGUF projectors (*.gguf)|*.gguf",
				InitialDirectory = Path.GetDirectoryName(settings.ModelPath)
			};
			if (((CommonDialog)val).ShowDialog((Window)(object)this).GetValueOrDefault())
			{
				engine.Stop();
				settings.ProjectorPath = ((FileDialog)val).FileName;
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
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_013a: Unknown result type (might be due to invalid IL or missing references)
		//IL_014b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0157: Expected O, but got Unknown
		//IL_0184: Unknown result type (might be due to invalid IL or missing references)
		//IL_0189: Unknown result type (might be due to invalid IL or missing references)
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
			((RangeBase)ContextSlider).Maximum = Math.Log2(num);
			((RangeBase)ContextSlider).Value = Math.Log2(contextTokens);
			ComboBoxItem[] array = (from ComboBoxItem i in (IEnumerable)((ItemsControl)ContextCombo).Items
				where ((FrameworkElement)i).Tag?.ToString() == "custom"
				select i).ToArray();
			foreach (ComboBoxItem val in array)
			{
				((ItemsControl)ContextCombo).Items.Remove((object)val);
			}
			ComboBoxItem val2 = ((IEnumerable)((ItemsControl)ContextCombo).Items).Cast<ComboBoxItem>().FirstOrDefault((Func<ComboBoxItem, bool>)((ComboBoxItem i) => ((ContentControl)i).Content.ToString() == contextTokens.ToString()));
			if (val2 == null)
			{
				val2 = new ComboBoxItem
				{
					Content = contextTokens.ToString(),
					Tag = "custom"
				};
				((ItemsControl)ContextCombo).Items.Add((object)val2);
			}
			foreach (ComboBoxItem item in (IEnumerable)((ItemsControl)ContextCombo).Items)
			{
				((UIElement)item).IsEnabled = int.Parse(((ContentControl)item).Content.ToString()) <= num;
			}
			((Selector)ContextCombo).SelectedItem = val2;
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
		if (uiReady && !synchronizingContext)
		{
			object selectedItem = ((Selector)ContextCombo).SelectedItem;
			ComboBoxItem val = (ComboBoxItem)((selectedItem is ComboBoxItem) ? selectedItem : null);
			if (val != null && int.TryParse(((ContentControl)val).Content.ToString(), out var result))
			{
				SetContext(result, remember: true);
			}
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
			((UIElement)ThresholdCanvas).Visibility = (Visibility)1;
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
		//IL_0328: Unknown result type (might be due to invalid IL or missing references)
		//IL_0332: Expected O, but got Unknown
		//IL_034d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0357: Expected O, but got Unknown
		long projectorBytes = (File.Exists(settings.ProjectorPath) ? new FileInfo(settings.ProjectorPath).Length : 0);
		int num = Math.Min(SelectedProfile.Target, modelInfo.NativeContext);
		advice = modelInfo.Advice(available, projectorBytes, launch.Cache)with
		{
			Recommended = num
		};
		((UIElement)ThresholdCanvas).Visibility = (Visibility)0;
		ContextThresholds.Text = $"Profile target {num:N0} · Native max {advice.NativeMaximum:N0}";
		((FrameworkElement)ProfileTargetMarker).ToolTip = $"Profile target: {num:N0} tokens. This is the profile’s default allowance.";
		bool flag = launch.ExpertCpuLayers == 0;
		bool flag2 = !launch.CpuOnly && !launch.FullOffload;
		bool flag3 = advice.FitsMinimum && (advice.FitsNativeMaximum || contextTokens <= advice.Danger);
		string text = (launch.CpuOnly ? "RAM estimate" : "Full GPU estimate");
		string text2 = ((!advice.FitsMinimum) ? "below 2,048 tokens" : (advice.FitsNativeMaximum ? "covers the native maximum" : $"up to {advice.Danger:N0} tokens"));
		string value = ((launch.Layers == "auto") ? "Automatic GPU fit can place some layers in RAM." : "The selected GPU layer count can place some layers in RAM.");
		ContextRisk.Text = ((!flag) ? "Memory estimate varies with MoE expert placement." : (flag2 ? $"{text}: {text2}.\n{value}" : (flag3 ? (text + ": " + text2 + ".") : $"Selected allowance exceeds the {text.ToLowerInvariant()}.\n{text}: {text2}.")));
		((FrameworkElement)ContextRisk).ToolTip = (flag ? "This estimate uses current free memory, model weights and KV cache. Other applications can change it." : "This estimate is unavailable while some MoE expert weights are assigned to system RAM.");
		ContextRisk.Foreground = (Brush)((FrameworkElement)this).FindResource((object)((!flag || flag3) ? "Muted" : (flag2 ? "ContextAdvisoryInk" : "ContextWarningInk")));
		MemoryEstimateLine.Background = (Brush)((FrameworkElement)this).FindResource((object)(flag2 ? "ContextAdvisoryInk" : "ContextWarningInk"));
		((UIElement)MemoryEstimateMarker).Visibility = (Visibility)((!flag || !advice.FitsMinimum || advice.FitsNativeMaximum || !(Math.Log2(advice.Danger) <= ((RangeBase)ContextSlider).Maximum)) ? 1 : 0);
		PositionMarkers();
	}

	private void PositionMarkers()
	{
		if ((object)advice != null && ThresholdCanvas != null && ContextSlider != null)
		{
			Canvas.SetLeft((UIElement)(object)ProfileTargetMarker, Position(advice.Recommended));
			Canvas.SetLeft((UIElement)(object)MemoryEstimateMarker, Position(advice.Danger));
		}
		double Position(int tokens)
		{
			return Math.Clamp((Math.Log2(tokens) - ((RangeBase)ContextSlider).Minimum) / Math.Max(1.0, ((RangeBase)ContextSlider).Maximum - ((RangeBase)ContextSlider).Minimum), 0.0, 1.0) * Math.Max(0.0, ((FrameworkElement)ThresholdCanvas).ActualWidth - ((FrameworkElement)ProfileTargetMarker).Width);
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
					GpuSnapshot snapshot = monitor.Sample(await ((DispatcherObject)this).Dispatcher.InvokeAsync<int?>((Func<int?>)(() => engine.ProcessId)));
					await ((DispatcherObject)this).Dispatcher.InvokeAsync((Action)delegate
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
		if (updateAdapters && (((CollectionView)((ItemsControl)GpuAdapterCombo).Items).Count != gpuSnapshot.Adapters.Count || ((CollectionView)((ItemsControl)GpuAdapterCombo).Items).Count == 0))
		{
			string key = (((Selector)GpuAdapterCombo).SelectedItem as GpuAdapter)?.Key;
			((ItemsControl)GpuAdapterCombo).ItemsSource = gpuSnapshot.Adapters;
			((Selector)GpuAdapterCombo).SelectedItem = gpuSnapshot.Adapters.FirstOrDefault((GpuAdapter a) => a.Key == key) ?? gpuSnapshot.Adapters.OrderByDescending((GpuAdapter a) => a.DedicatedBytes).FirstOrDefault();
		}
		if (gpuSnapshot.Error != null)
		{
			GpuSummary.Text = gpuSnapshot.Error;
			GpuAppsList.ItemsSource = null;
			return;
		}
		object selectedItem = ((Selector)GpuAdapterCombo).SelectedItem;
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
		OpenFileDialog val = new OpenFileDialog
		{
			Title = "Attach files to this local conversation",
			Multiselect = true,
			Filter = "Supported files|*.txt;*.md;*.pdf;*.docx;*.csv;*.tsv;*.json;*.xml;*.yaml;*.yml;*.cs;*.gd;*.tscn;*.py;*.js;*.ts;*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files|*.*"
		};
		if (((CommonDialog)val).ShowDialog((Window)(object)this).GetValueOrDefault())
		{
			await StageFilesAsync(((FileDialog)val).FileNames);
		}
	}

	private void Files_DragOver(object sender, DragEventArgs e)
	{
		if (e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			e.Effects = (DragDropEffects)((!busy && !attaching) ? 1 : 0);
			((RoutedEventArgs)e).Handled = true;
		}
	}

	private async void Files_Drop(object sender, DragEventArgs e)
	{
		if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
		{
			((RoutedEventArgs)e).Handled = true;
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
		((UIElement)AttachButton).IsEnabled = false;
		((UIElement)SendButton).IsEnabled = false;
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
			((UIElement)AttachButton).IsEnabled = !busy;
			((UIElement)SendButton).IsEnabled = true;
			RenderAttachmentChips();
		}
		if (errors.Count > 0)
		{
			Notice(string.Join("\n", errors));
		}
	}

	private void RenderAttachmentChips()
	{
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e4: Expected O, but got Unknown
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Expected O, but got Unknown
		((Panel)AttachmentChips).Children.Clear();
		foreach (Attachment attachment in pendingAttachments)
		{
			Button val = new Button
			{
				Content = attachment.Name + "  ×",
				ToolTip = attachment.Label + " · click to remove",
				Margin = new Thickness(0.0, 0.0, 6.0, 5.0),
				Padding = new Thickness(8.0, 5.0, 8.0, 5.0),
				FontSize = 11.0
			};
			((ButtonBase)val).Click += (RoutedEventHandler)delegate
			{
				pendingAttachments.Remove(attachment);
				RenderAttachmentChips();
			};
			((Panel)AttachmentChips).Children.Add((UIElement)(object)val);
		}
	}

	private void ShowInferenceSettings()
	{
		InferencePreferences inference = settings.Inference;
		((Selector)ProfileCombo).SelectedValue = inference.Profile;
		((ToggleButton)LaptopQ4Check).IsChecked = inference.LaptopQ4;
		((Selector)CustomCacheCombo).SelectedValue = inference.CustomCache;
		ExpertLayersBox.Text = inference.ExpertCpuLayers.ToString(CultureInfo.InvariantCulture);
		((ToggleButton)CreativeCheck).IsChecked = inference.Creative;
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
			Profile = ((((Selector)ProfileCombo).SelectedValue as string) ?? "Auto"),
			HighContext = inference.HighContext,
			LaptopContext = inference.LaptopContext,
			CustomContext = inference.CustomContext,
			LaptopQ4 = ((ToggleButton)LaptopQ4Check).IsChecked.GetValueOrDefault(),
			CustomCache = ((((Selector)CustomCacheCombo).SelectedValue as string) ?? "f16"),
			ExpertCpuLayers = Integer(ExpertLayersBox, "MoE expert layers"),
			Creative = ((ToggleButton)CreativeCheck).IsChecked.GetValueOrDefault(),
			CreativeTemperature = Number(CreativeTempBox, "creative temperature"),
			MinP = Number(MinPBox, "min-p"),
			TopP = Number(TopPBox, "top-p"),
			DryMultiplier = Number(DryMultiplierBox, "DRY multiplier"),
			DryBase = Number(DryBaseBox, "DRY base"),
			DryAllowedLength = Integer(DryLengthBox, "DRY allowed length")
		};
		static int Integer(TextBox field, string label)
		{
			if (!int.TryParse(field.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result2))
			{
				throw new ArgumentException("Enter a whole number for " + label + ".");
			}
			return result2;
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
		HardwareInventory hardware = await Task.Run(() => HardwareInventory.DetectAsync(Engine.Executable));
		if (!featuresClosing)
		{
			settings.Hardware = hardware;
			SetContext(SelectedProfile.Requested(settings.Inference));
		}
	}

	private void Profile_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (uiReady)
		{
			InferencePreferences inference = settings.Inference;
			if (inference != null && ((Selector)ProfileCombo).SelectedValue is string profile)
			{
				inference.Profile = profile;
				SetContext(SelectedProfile.Requested(inference));
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
				inference.LaptopQ4 = ((ToggleButton)LaptopQ4Check).IsChecked.GetValueOrDefault();
				inference.CustomCache = (((Selector)CustomCacheCombo).SelectedValue as string) ?? "f16";
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
			((UIElement)CreativeControls).IsEnabled = ((ToggleButton)CreativeCheck).IsChecked.GetValueOrDefault();
		}
		if (TempBox != null)
		{
			((UIElement)TempBox).IsEnabled = !((ToggleButton)CreativeCheck).IsChecked.GetValueOrDefault();
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

	private void RenderProfile(InferenceLaunch launch, long freeSystem, long? freeGpu)
	{
		string label = SelectedProfile.Label;
		((UIElement)LaptopQ4Check).Visibility = (Visibility)((!(SelectedProfile.Key == "Laptop")) ? 2 : 0);
		((UIElement)CustomCachePanel).Visibility = (Visibility)((!(SelectedProfile.Key == "Custom")) ? 2 : 0);
		((UIElement)GpuBox).IsEnabled = SelectedProfile.Key == "Custom";
		((UIElement)ExpertLayersBox).IsEnabled = modelInfoReady && modelInfo.ExpertCount > 1 && !launch.CpuOnly && SelectedProfile.Key != "High";
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
			string text2 = text;
			((DispatcherObject)this).Dispatcher.InvokeAsync((Action)delegate
			{
				if (!featuresClosing)
				{
					VoiceStatusText.Text = text2;
					((UIElement)VoiceStopButton).IsEnabled = speech.Active;
				}
			});
		};
		speech.Frame += delegate(SpeechFrame frame)
		{
			SpeechFrame frame2 = frame;
			((DispatcherObject)this).Dispatcher.InvokeAsync((Action)delegate
			{
				if (!featuresClosing)
				{
					if (frame2.Playing && speech.CurrentUtterance == frame2.Utterance)
					{
						avatarState = "speaking";
						avatarUtterance = frame2.Utterance;
						avatar.SetSpeechFrameAsync(frame2.Utterance, frame2.Mood, frame2.Mouth);
					}
					else if (!frame2.Playing && avatarUtterance == frame2.Utterance)
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
			((Window)voiceWindow).Activate();
		}
		else
		{
			if (busy || !ReadSettings())
			{
				return;
			}
			VoiceWindow obj = new VoiceWindow(settings.Voice, speech, async delegate(VoiceOptions options, bool preview)
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
			});
			((Window)obj).Owner = (Window)(object)this;
			voiceWindow = obj;
			((Window)voiceWindow).Closed += delegate
			{
				voiceWindow = null;
			};
			((Window)voiceWindow).Show();
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
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Expected O, but got Unknown
		//IL_00a3: Expected O, but got Unknown
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Expected O, but got Unknown
		//IL_0202: Unknown result type (might be due to invalid IL or missing references)
		//IL_0207: Unknown result type (might be due to invalid IL or missing references)
		//IL_0220: Expected O, but got Unknown
		InitializeComponent();
		((UIElement)this).CommandBindings.Add(new CommandBinding((ICommand)DeleteChatCommand, new ExecutedRoutedEventHandler(DeleteChat_Executed), new CanExecuteRoutedEventHandler(DeleteChat_CanExecute)));
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
			string text2 = text;
			((DispatcherObject)this).Dispatcher.InvokeAsync<string>((Func<string>)(() => StatusText.Text = text2));
		};
		chats = new ObservableCollection<ChatSession>(store.LoadChats());
		((ItemsControl)ChatsList).ItemsSource = chats;
		ShowSettings();
		StatusText.Text = "Model not loaded";
		if (chats.Count > 0)
		{
			((Selector)ChatsList).SelectedItem = chats[0];
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
		((ToggleButton)ReplyEmojiCheck).IsChecked = settings.ReplyEmoji;
		((ToggleButton)LearnMemoryCheck).IsChecked = settings.LearnMemories;
		((ToggleButton)UseMemoryCheck).IsChecked = settings.UseMemories;
		((ToggleButton)SemanticMemoryCheck).IsChecked = settings.SemanticMemories;
		RefreshPersonalities();
		TempBox.Text = settings.Temperature.ToString(CultureInfo.InvariantCulture);
		GpuBox.Text = settings.GpuLayers.ToString();
		MaxTokensBox.Text = settings.MaxTokens.ToString();
		((ToggleButton)ThinkingCheck).IsChecked = settings.Thinking;
		((ToggleButton)ToolsCheck).IsChecked = settings.WorkspaceTools;
		CpuBox.Text = settings.CpuThreads.ToString();
		ThinkingBudgetBox.Text = settings.ThinkingBudget.ToString();
		((ToggleButton)LookupCheck).IsChecked = settings.RequireLookup;
		((Selector)ContextCombo).SelectedItem = ((IEnumerable)((ItemsControl)ContextCombo).Items).Cast<ComboBoxItem>().FirstOrDefault((Func<ComboBoxItem, bool>)((ComboBoxItem i) => ((ContentControl)i).Content.ToString() == settings.Context.ToString())) ?? ((ItemsControl)ContextCombo).Items[1];
	}

	private void RefreshPaths()
	{
		ModelName.Text = (string.IsNullOrEmpty(settings.ModelPath) ? "No model selected" : Path.GetFileNameWithoutExtension(settings.ModelPath));
		((FrameworkElement)ModelName).ToolTip = settings.ModelPath;
		VisionStatus.Text = (string.IsNullOrEmpty(settings.ProjectorPath) ? "Vision: no projector selected" : ("Vision ready · " + Path.GetFileName(settings.ProjectorPath)));
		WorkspaceName.Text = (string.IsNullOrEmpty(settings.Workspace) ? "No folder selected" : settings.Workspace);
		((FrameworkElement)WorkspaceName).ToolTip = settings.Workspace;
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
				ReplyEmoji = ((ToggleButton)ReplyEmojiCheck).IsChecked.GetValueOrDefault(),
				MaxTokens = int.Parse(MaxTokensBox.Text, CultureInfo.InvariantCulture),
				Temperature = double.Parse(TempBox.Text, CultureInfo.InvariantCulture),
				GpuLayers = int.Parse(GpuBox.Text, CultureInfo.InvariantCulture),
				CpuThreads = int.Parse(CpuBox.Text, CultureInfo.InvariantCulture),
				Inference = ReadInferenceSettings(),
				Hardware = this.settings.Hardware,
				ThinkingBudget = int.Parse(ThinkingBudgetBox.Text, CultureInfo.InvariantCulture),
				RequireLookup = ((ToggleButton)LookupCheck).IsChecked.GetValueOrDefault(),
				LearnMemories = ((ToggleButton)LearnMemoryCheck).IsChecked.GetValueOrDefault(),
				UseMemories = ((ToggleButton)UseMemoryCheck).IsChecked.GetValueOrDefault(),
				SemanticMemories = ((ToggleButton)SemanticMemoryCheck).IsChecked.GetValueOrDefault(),
				PersonalityId = ((((Selector)PersonalityCombo).SelectedItem as PersonalityProfile)?.Id ?? "default"),
				Voice = this.settings.Voice,
				AvatarEnabled = this.settings.AvatarEnabled,
				Avatar = this.settings.Avatar,
				AvatarFolder = this.settings.AvatarFolder,
				AvatarModelPath = this.settings.AvatarModelPath,
				WebMode = this.settings.WebMode,
				WebKeyProtected = this.settings.WebKeyProtected,
				Thinking = ((ToggleButton)ThinkingCheck).IsChecked.GetValueOrDefault(),
				WorkspaceTools = ((ToggleButton)ToolsCheck).IsChecked.GetValueOrDefault()
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
			((Selector)ChatsList).SelectedItem = chatSession;
		}
	}

	private void New_Click(object sender, RoutedEventArgs e)
	{
		NewChat();
	}

	private void Chats_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (((Selector)ChatsList).SelectedItem is ChatSession chatSession)
		{
			speech.Stop();
			current = chatSession;
			RenderChat();
		}
	}

	private void RenderChat()
	{
		((Panel)Messages).Children.Clear();
		MetricsText.Text = "";
		((UIElement)NoticeBox).Visibility = (Visibility)2;
		if (current == null)
		{
			return;
		}
		ChatTitle.Text = current.Title;
		((UIElement)Welcome).Visibility = (Visibility)((current.Turns.Count != 0) ? 2 : 0);
		foreach (Turn turn in current.Turns)
		{
			MessageView messageView = new MessageView("You");
			messageView.Update(Attachments.VisibleText(turn), "", null, "", renderMarkdown: false);
			((Panel)Messages).Children.Add((UIElement)(object)messageView);
			MessageView messageView2 = new MessageView("Aquila");
			messageView2.Update(VoiceDelivery.Display(turn, settings.Voice.RevealTags), turn.Reasoning, turn.Activity, Label(turn), renderMarkdown: true, turn.Answer);
			((Panel)Messages).Children.Add((UIElement)(object)messageView2);
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

	private async Task SendAsync()
	{
		await inferenceStartup;
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
		((UIElement)NoticeBox).Visibility = (Visibility)2;
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
		((CollectionView)((ItemsControl)ChatsList).Items).Refresh();
		ChatTitle.Text = session.Title;
		((UIElement)Welcome).Visibility = (Visibility)2;
		MessageView messageView = new MessageView("You");
		messageView.Update(Attachments.VisibleText(turn), "", null, "", renderMarkdown: false);
		((Panel)Messages).Children.Add((UIElement)(object)messageView);
		live = new MessageView("Aquila");
		live.Update("", "", null, "Working…");
		((Panel)Messages).Children.Add((UIElement)(object)live);
		activeTurn = turn;
		Composer.Clear();
		pendingAttachments.Clear();
		RenderAttachmentChips();
		followBottom = true;
		SetBusy(value: true);
		operation = new CancellationTokenSource();
		Agent agent = new Agent(engine, memories, semantic, personalities);
		agent.ApproveWeb = (string action) => Task.FromResult((int)MessageBox.Show((Window)(object)this, action + "\n\nThis query or URL will be sent over the internet.", "Allow web lookup?", (MessageBoxButton)4, (MessageBoxImage)32, (MessageBoxResult)7) == 6);
		agent.AnswerStarted += delegate
		{
			SetAvatarState("thinking", turn.Id);
		};
		agent.Token += delegate
		{
			dirtyRender = true;
		};
		agent.Activity += delegate(string text)
		{
			dirtyRender = true;
			StatusText.Text = (text.StartsWith("Recalled memory") ? "Using saved memories…" : "Working…");
		};
		try
		{
			await agent.RunAsync(settings, session, turn, operation.Token);
			SetAvatarState("idle");
			store.SaveChat(session);
			RenderLive(final: true);
			if (turn.Status == "complete")
			{
				SpeakCompletedReply(session, turn);
			}
			if (settings.LearnMemories && turn.Status == "complete" && MemoryLearner.Eligible(turn.User))
			{
				StatusText.Text = "Updating memory…";
				try
				{
					if ((object)(await new MemoryLearner(engine, memories, personalities).LearnAsync(settings, session, turn, operation.Token)) != null)
					{
						turn.Activity.Add("Memory: " + turn.MemoryLearningStatus + $" · {turn.MemoryPromptTokens + turn.MemoryCompletionTokens:N0} tokens included in session usage.");
					}
				}
				catch (OperationCanceledException)
				{
					turn.MemoryLearningStatus = "stopped";
					turn.Activity.Add("Memory update stopped. Your completed answer is saved.");
				}
				catch (Exception ex2)
				{
					turn.MemoryLearningStatus = "failed";
					turn.Activity.Add("Memory update failed: " + ex2.Message + ". Your completed answer is saved.");
				}
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
			((CollectionView)((ItemsControl)ChatsList).Items).Refresh();
			((UIElement)Composer).Focus();
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
		((UIElement)NewButton).IsEnabled = !value;
		((UIElement)ChatsList).IsEnabled = !value;
		((UIElement)SetupControls).IsEnabled = !value;
		((UIElement)VoiceButton).IsEnabled = !value;
		((UIElement)VoiceReplayButton).IsEnabled = !value;
		((UIElement)MemoriesButton).IsEnabled = !value;
		RefreshContextControls();
		CommandManager.InvalidateRequerySuggested();
		((UIElement)AttachButton).IsEnabled = !value && !attaching;
		((UIElement)AttachmentChips).IsEnabled = !value;
		((UIElement)SendButton).Visibility = (Visibility)(value ? 2 : 0);
		((UIElement)StopButton).Visibility = (Visibility)((!value) ? 2 : 0);
		((UIElement)StopButton).IsEnabled = value;
	}

	private void Stop_Click(object sender, RoutedEventArgs e)
	{
		operation?.Cancel();
		((UIElement)StopButton).IsEnabled = false;
		StatusText.Text = "Stopping…";
	}

	private void Composer_KeyDown(object sender, KeyEventArgs e)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Invalid comparison between Unknown and I4
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		if ((int)e.Key == 6 && (int)Keyboard.Modifiers == 0)
		{
			((RoutedEventArgs)e).Handled = true;
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
			OpenFileDialog val = new OpenFileDialog
			{
				Filter = "GGUF models (*.gguf)|*.gguf",
				Title = "Choose a local language model"
			};
			string text = Path.Combine(AppContext.BaseDirectory, "models");
			if (Directory.Exists(text))
			{
				((CommonItemDialog)val).InitialDirectory = text;
			}
			if (((CommonDialog)val).ShowDialog((Window)(object)this).GetValueOrDefault())
			{
				await SelectModelAsync(((FileDialog)val).FileName);
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
		((UIElement)NoticeBox).Visibility = (Visibility)2;
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
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Expected O, but got Unknown
		if (!ReadSettings())
		{
			return;
		}
		OpenFolderDialog val = new OpenFolderDialog
		{
			Title = "Choose a project folder for file search and reading",
			Multiselect = false
		};
		if (!((CommonDialog)val).ShowDialog((Window)(object)this).GetValueOrDefault())
		{
			return;
		}
		try
		{
			new WorkspaceTools(val.FolderName);
			settings.Workspace = val.FolderName;
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
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		ColumnDefinition settingsColumn = SettingsColumn;
		GridLength width = SettingsColumn.Width;
		settingsColumn.Width = ((((GridLength)(ref width)).Value == 0.0) ? new GridLength(320.0) : new GridLength(0.0));
	}

	private void Internet_Click(object sender, RoutedEventArgs e)
	{
		if (!busy && !attaching && ReadSettings())
		{
			InternetWindow internetWindow = new InternetWindow(settings, delegate
			{
				store.SaveSettings(settings);
			});
			((Window)internetWindow).Owner = (Window)(object)this;
			((Window)internetWindow).ShowDialog();
		}
	}

	private void Export_Click(object sender, RoutedEventArgs e)
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Expected O, but got Unknown
		if (current == null || current.Turns.Count == 0)
		{
			Notice("Send a message before exporting this conversation.");
			return;
		}
		SaveFileDialog val = new SaveFileDialog
		{
			Filter = "Markdown (*.md)|*.md",
			FileName = "Aquila conversation.md"
		};
		if (!((CommonDialog)val).ShowDialog((Window)(object)this).GetValueOrDefault())
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
			File.WriteAllText(((FileDialog)val).FileName, stringBuilder.ToString());
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
			//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
			MemorySource source2 = source;
			ChatSession chatSession = chats.FirstOrDefault((ChatSession c) => c.Id == source2.ChatId);
			if (chatSession == null)
			{
				Notice("The source conversation is no longer available. The supporting quote is preserved in Memories.");
			}
			else
			{
				((Selector)ChatsList).SelectedItem = chatSession;
				Notice("Memory source: “" + source2.Quote + "”");
				int num = chatSession.Turns.FindIndex((Turn t) => t.Id == source2.TurnId);
				if (num >= 0 && ((Panel)Messages).Children.Count > num * 2)
				{
					((FrameworkElement)((Panel)Messages).Children[num * 2]).BringIntoView();
				}
			}
		});
		((Window)memoryWindow).Owner = (Window)(object)this;
		((Window)memoryWindow).ShowDialog();
	}

	private void Notice(string text)
	{
		NoticeText.Text = text.Truncate(1800);
		((UIElement)NoticeBox).Visibility = (Visibility)0;
	}

	private void Dismiss_Click(object sender, RoutedEventArgs e)
	{
		((UIElement)NoticeBox).Visibility = (Visibility)2;
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
					((Window)this).Close();
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
			((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)new Action(base.Close), Array.Empty<object>());
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.31.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri uri = new Uri("/Aquila Local;component/mainwindow.xaml", UriKind.Relative);
			Application.LoadComponent((object)this, uri);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.31.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		//IL_01a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ab: Expected O, but got Unknown
		//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c2: Expected O, but got Unknown
		//IL_01c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cf: Expected O, but got Unknown
		//IL_01d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dc: Expected O, but got Unknown
		//IL_01e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f3: Expected O, but got Unknown
		//IL_01f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0200: Expected O, but got Unknown
		//IL_020d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0217: Expected O, but got Unknown
		//IL_021a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0224: Expected O, but got Unknown
		//IL_0227: Unknown result type (might be due to invalid IL or missing references)
		//IL_0231: Expected O, but got Unknown
		//IL_0234: Unknown result type (might be due to invalid IL or missing references)
		//IL_023e: Expected O, but got Unknown
		//IL_0241: Unknown result type (might be due to invalid IL or missing references)
		//IL_024b: Expected O, but got Unknown
		//IL_0258: Unknown result type (might be due to invalid IL or missing references)
		//IL_0262: Expected O, but got Unknown
		//IL_0265: Unknown result type (might be due to invalid IL or missing references)
		//IL_026f: Expected O, but got Unknown
		//IL_027c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0286: Expected O, but got Unknown
		//IL_0289: Unknown result type (might be due to invalid IL or missing references)
		//IL_0293: Expected O, but got Unknown
		//IL_02a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02aa: Expected O, but got Unknown
		//IL_02ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b7: Expected O, but got Unknown
		//IL_02b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02cf: Expected O, but got Unknown
		//IL_02d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_02dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e7: Expected O, but got Unknown
		//IL_02ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f4: Expected O, but got Unknown
		//IL_0301: Unknown result type (might be due to invalid IL or missing references)
		//IL_030b: Expected O, but got Unknown
		//IL_030d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0319: Unknown result type (might be due to invalid IL or missing references)
		//IL_0323: Expected O, but got Unknown
		//IL_0325: Unknown result type (might be due to invalid IL or missing references)
		//IL_0331: Unknown result type (might be due to invalid IL or missing references)
		//IL_033b: Expected O, but got Unknown
		//IL_033e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0348: Expected O, but got Unknown
		//IL_0355: Unknown result type (might be due to invalid IL or missing references)
		//IL_035f: Expected O, but got Unknown
		//IL_0361: Unknown result type (might be due to invalid IL or missing references)
		//IL_036d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0377: Expected O, but got Unknown
		//IL_037a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0384: Expected O, but got Unknown
		//IL_0391: Unknown result type (might be due to invalid IL or missing references)
		//IL_039b: Expected O, but got Unknown
		//IL_039e: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a8: Expected O, but got Unknown
		//IL_03b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_03bf: Expected O, but got Unknown
		//IL_03c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_03cc: Expected O, but got Unknown
		//IL_03cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d9: Expected O, but got Unknown
		//IL_03dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e6: Expected O, but got Unknown
		//IL_03e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_03f3: Expected O, but got Unknown
		//IL_0400: Unknown result type (might be due to invalid IL or missing references)
		//IL_040a: Expected O, but got Unknown
		//IL_040d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0417: Expected O, but got Unknown
		//IL_041a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0424: Expected O, but got Unknown
		//IL_0427: Unknown result type (might be due to invalid IL or missing references)
		//IL_0431: Expected O, but got Unknown
		//IL_0433: Unknown result type (might be due to invalid IL or missing references)
		//IL_043f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0449: Expected O, but got Unknown
		//IL_044c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0456: Expected O, but got Unknown
		//IL_0459: Unknown result type (might be due to invalid IL or missing references)
		//IL_0463: Expected O, but got Unknown
		//IL_0466: Unknown result type (might be due to invalid IL or missing references)
		//IL_0470: Expected O, but got Unknown
		//IL_047d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0487: Expected O, but got Unknown
		//IL_048a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0494: Expected O, but got Unknown
		//IL_04a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ab: Expected O, but got Unknown
		//IL_04ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_04b8: Expected O, but got Unknown
		//IL_04c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_04cf: Expected O, but got Unknown
		//IL_04d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_04dc: Expected O, but got Unknown
		//IL_04e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_04f3: Expected O, but got Unknown
		//IL_04f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0500: Expected O, but got Unknown
		//IL_0503: Unknown result type (might be due to invalid IL or missing references)
		//IL_050d: Expected O, but got Unknown
		//IL_0510: Unknown result type (might be due to invalid IL or missing references)
		//IL_051a: Expected O, but got Unknown
		//IL_0527: Unknown result type (might be due to invalid IL or missing references)
		//IL_0531: Expected O, but got Unknown
		//IL_0533: Unknown result type (might be due to invalid IL or missing references)
		//IL_053f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0549: Expected O, but got Unknown
		//IL_054c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0556: Expected O, but got Unknown
		//IL_0559: Unknown result type (might be due to invalid IL or missing references)
		//IL_0563: Expected O, but got Unknown
		//IL_0566: Unknown result type (might be due to invalid IL or missing references)
		//IL_0570: Expected O, but got Unknown
		//IL_057d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0587: Expected O, but got Unknown
		//IL_0589: Unknown result type (might be due to invalid IL or missing references)
		//IL_0595: Unknown result type (might be due to invalid IL or missing references)
		//IL_059f: Expected O, but got Unknown
		//IL_05a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_05ac: Expected O, but got Unknown
		//IL_05b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_05c3: Expected O, but got Unknown
		//IL_05c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_05d0: Expected O, but got Unknown
		//IL_05dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_05e7: Expected O, but got Unknown
		//IL_05ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_05f4: Expected O, but got Unknown
		//IL_05f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0602: Unknown result type (might be due to invalid IL or missing references)
		//IL_060c: Expected O, but got Unknown
		//IL_060e: Unknown result type (might be due to invalid IL or missing references)
		//IL_061a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0624: Expected O, but got Unknown
		//IL_0627: Unknown result type (might be due to invalid IL or missing references)
		//IL_0631: Expected O, but got Unknown
		//IL_063e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0648: Expected O, but got Unknown
		//IL_064b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0655: Expected O, but got Unknown
		//IL_0658: Unknown result type (might be due to invalid IL or missing references)
		//IL_0662: Expected O, but got Unknown
		//IL_066f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0679: Expected O, but got Unknown
		//IL_0686: Unknown result type (might be due to invalid IL or missing references)
		//IL_0690: Expected O, but got Unknown
		//IL_0693: Unknown result type (might be due to invalid IL or missing references)
		//IL_069d: Expected O, but got Unknown
		//IL_06a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_06aa: Expected O, but got Unknown
		//IL_06b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_06c1: Expected O, but got Unknown
		//IL_06c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_06ce: Expected O, but got Unknown
		//IL_06d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_06db: Expected O, but got Unknown
		//IL_06e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_06f2: Expected O, but got Unknown
		//IL_06f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_06ff: Expected O, but got Unknown
		//IL_0702: Unknown result type (might be due to invalid IL or missing references)
		//IL_070c: Expected O, but got Unknown
		//IL_0719: Unknown result type (might be due to invalid IL or missing references)
		//IL_0723: Expected O, but got Unknown
		//IL_0726: Unknown result type (might be due to invalid IL or missing references)
		//IL_0730: Expected O, but got Unknown
		//IL_0733: Unknown result type (might be due to invalid IL or missing references)
		//IL_073d: Expected O, but got Unknown
		//IL_0757: Unknown result type (might be due to invalid IL or missing references)
		//IL_0761: Expected O, but got Unknown
		//IL_076e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0778: Expected O, but got Unknown
		//IL_077b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0785: Expected O, but got Unknown
		//IL_0788: Unknown result type (might be due to invalid IL or missing references)
		//IL_0792: Expected O, but got Unknown
		//IL_0795: Unknown result type (might be due to invalid IL or missing references)
		//IL_079f: Expected O, but got Unknown
		//IL_07a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_07ac: Expected O, but got Unknown
		//IL_07af: Unknown result type (might be due to invalid IL or missing references)
		//IL_07b9: Expected O, but got Unknown
		//IL_07bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_07c6: Expected O, but got Unknown
		//IL_07c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_07d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_07de: Expected O, but got Unknown
		//IL_07e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_07ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_07f6: Expected O, but got Unknown
		//IL_07f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0803: Expected O, but got Unknown
		//IL_0806: Unknown result type (might be due to invalid IL or missing references)
		//IL_0810: Expected O, but got Unknown
		//IL_0813: Unknown result type (might be due to invalid IL or missing references)
		//IL_081d: Expected O, but got Unknown
		//IL_0820: Unknown result type (might be due to invalid IL or missing references)
		//IL_082a: Expected O, but got Unknown
		//IL_082d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0837: Expected O, but got Unknown
		//IL_083a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0844: Expected O, but got Unknown
		//IL_0847: Unknown result type (might be due to invalid IL or missing references)
		//IL_0851: Expected O, but got Unknown
		//IL_085e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0868: Expected O, but got Unknown
		//IL_086b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0875: Expected O, but got Unknown
		//IL_0878: Unknown result type (might be due to invalid IL or missing references)
		//IL_0882: Expected O, but got Unknown
		//IL_088f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0899: Expected O, but got Unknown
		//IL_08a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_08b0: Expected O, but got Unknown
		//IL_08b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_08bd: Expected O, but got Unknown
		//IL_08c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_08ca: Expected O, but got Unknown
		//IL_08cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_08d7: Expected O, but got Unknown
		//IL_08da: Unknown result type (might be due to invalid IL or missing references)
		//IL_08e4: Expected O, but got Unknown
		//IL_08e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_08f1: Expected O, but got Unknown
		//IL_08f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_08fe: Expected O, but got Unknown
		//IL_0901: Unknown result type (might be due to invalid IL or missing references)
		//IL_090b: Expected O, but got Unknown
		//IL_090e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0918: Expected O, but got Unknown
		//IL_0925: Unknown result type (might be due to invalid IL or missing references)
		//IL_092f: Expected O, but got Unknown
		//IL_0932: Unknown result type (might be due to invalid IL or missing references)
		//IL_093c: Expected O, but got Unknown
		//IL_093f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0949: Expected O, but got Unknown
		//IL_0956: Unknown result type (might be due to invalid IL or missing references)
		//IL_0960: Expected O, but got Unknown
		//IL_096d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0977: Expected O, but got Unknown
		//IL_097a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0984: Expected O, but got Unknown
		//IL_0991: Unknown result type (might be due to invalid IL or missing references)
		//IL_099b: Expected O, but got Unknown
		//IL_099e: Unknown result type (might be due to invalid IL or missing references)
		//IL_09a8: Expected O, but got Unknown
		//IL_09ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_09b5: Expected O, but got Unknown
		//IL_09b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_09c2: Expected O, but got Unknown
		//IL_09c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_09d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_09da: Expected O, but got Unknown
		switch (connectionId)
		{
		case 1:
			((Window)(MainWindow)target).Closing += Window_Closing;
			((UIElement)(MainWindow)target).PreviewDragOver += new DragEventHandler(Files_DragOver);
			((UIElement)(MainWindow)target).PreviewDrop += new DragEventHandler(Files_Drop);
			break;
		case 2:
			SettingsColumn = (ColumnDefinition)target;
			break;
		case 3:
			NewButton = (Button)target;
			((ButtonBase)NewButton).Click += new RoutedEventHandler(New_Click);
			break;
		case 4:
			GpuAdapterCombo = (ComboBox)target;
			((Selector)GpuAdapterCombo).SelectionChanged += new SelectionChangedEventHandler(GpuAdapter_Changed);
			break;
		case 5:
			GpuSummary = (TextBlock)target;
			break;
		case 6:
			GpuAppsList = (ItemsControl)target;
			break;
		case 7:
			GpuFootnote = (TextBlock)target;
			break;
		case 8:
			VoiceButton = (Button)target;
			((ButtonBase)VoiceButton).Click += new RoutedEventHandler(Voice_Click);
			break;
		case 9:
			VoiceReplayButton = (Button)target;
			((ButtonBase)VoiceReplayButton).Click += new RoutedEventHandler(ReplayVoice_Click);
			break;
		case 10:
			VoiceStopButton = (Button)target;
			((ButtonBase)VoiceStopButton).Click += new RoutedEventHandler(StopVoice_Click);
			break;
		case 11:
			VoiceStatusText = (TextBlock)target;
			break;
		case 12:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(Internet_Click);
			break;
		case 13:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(Avatar_Click);
			break;
		case 14:
			MemoriesButton = (Button)target;
			((ButtonBase)MemoriesButton).Click += new RoutedEventHandler(Memories_Click);
			break;
		case 15:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(Export_Click);
			break;
		case 16:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(Data_Click);
			break;
		case 17:
			ChatsList = (ListBox)target;
			((Selector)ChatsList).SelectionChanged += new SelectionChangedEventHandler(Chats_Changed);
			break;
		case 19:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(Settings_Click);
			break;
		case 20:
			CompactButton = (Button)target;
			((ButtonBase)CompactButton).Click += new RoutedEventHandler(Compact_Click);
			break;
		case 21:
			SummaryButton = (Button)target;
			((ButtonBase)SummaryButton).Click += new RoutedEventHandler(Summary_Click);
			break;
		case 22:
			ChatTitle = (TextBlock)target;
			break;
		case 23:
			StatusText = (TextBlock)target;
			break;
		case 24:
			SessionUsageText = (TextBlock)target;
			break;
		case 25:
			ChatScroll = (ScrollViewer)target;
			ChatScroll.ScrollChanged += new ScrollChangedEventHandler(Chat_ScrollChanged);
			break;
		case 26:
			Messages = (StackPanel)target;
			break;
		case 27:
			Welcome = (StackPanel)target;
			break;
		case 28:
			NoticeBox = (Border)target;
			break;
		case 29:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(Dismiss_Click);
			break;
		case 30:
			NoticeText = (TextBlock)target;
			break;
		case 31:
			AttachmentChips = (WrapPanel)target;
			break;
		case 32:
			SendButton = (Button)target;
			((ButtonBase)SendButton).Click += new RoutedEventHandler(Send_Click);
			break;
		case 33:
			StopButton = (Button)target;
			((ButtonBase)StopButton).Click += new RoutedEventHandler(Stop_Click);
			break;
		case 34:
			Composer = (TextBox)target;
			((UIElement)Composer).PreviewKeyDown += new KeyEventHandler(Composer_KeyDown);
			break;
		case 35:
			AttachButton = (Button)target;
			((ButtonBase)AttachButton).Click += new RoutedEventHandler(Attach_Click);
			break;
		case 36:
			MetricsText = (TextBlock)target;
			break;
		case 37:
			SetupControls = (StackPanel)target;
			break;
		case 38:
			PersonalityCombo = (ComboBox)target;
			((Selector)PersonalityCombo).SelectionChanged += new SelectionChangedEventHandler(Personality_Changed);
			break;
		case 39:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(Personalities_Click);
			break;
		case 40:
			PersonalitySummary = (TextBlock)target;
			break;
		case 41:
			ModelName = (TextBlock)target;
			break;
		case 42:
			InstalledModelsCombo = (ComboBox)target;
			((Selector)InstalledModelsCombo).SelectionChanged += new SelectionChangedEventHandler(InstalledModels_Changed);
			break;
		case 43:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(Model_Click);
			break;
		case 44:
			LoadButton = (Button)target;
			((ButtonBase)LoadButton).Click += new RoutedEventHandler(Load_Click);
			break;
		case 45:
			UnloadButton = (Button)target;
			((ButtonBase)UnloadButton).Click += new RoutedEventHandler(Unload_Click);
			break;
		case 46:
			VisionStatus = (TextBlock)target;
			break;
		case 47:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(Projector_Click);
			break;
		case 48:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(ClearProjector_Click);
			break;
		case 49:
			ProfileCombo = (ComboBox)target;
			((Selector)ProfileCombo).SelectionChanged += new SelectionChangedEventHandler(Profile_Changed);
			break;
		case 50:
			HardwareText = (TextBlock)target;
			break;
		case 51:
			LaptopQ4Check = (CheckBox)target;
			((ToggleButton)LaptopQ4Check).Checked += new RoutedEventHandler(Cache_Changed);
			((ToggleButton)LaptopQ4Check).Unchecked += new RoutedEventHandler(Cache_Changed);
			break;
		case 52:
			CustomCachePanel = (StackPanel)target;
			break;
		case 53:
			CustomCacheCombo = (ComboBox)target;
			((Selector)CustomCacheCombo).SelectionChanged += new SelectionChangedEventHandler(Cache_Changed);
			break;
		case 54:
			ExpertsLabel = (TextBlock)target;
			break;
		case 55:
			ExpertLayersBox = (TextBox)target;
			((TextBoxBase)ExpertLayersBox).TextChanged += new TextChangedEventHandler(Compute_Changed);
			break;
		case 56:
			ExpertsHint = (TextBlock)target;
			break;
		case 57:
			ContextCombo = (ComboBox)target;
			((Selector)ContextCombo).SelectionChanged += new SelectionChangedEventHandler(ContextCombo_Changed);
			break;
		case 58:
			ContextValue = (TextBlock)target;
			break;
		case 59:
			ContextSlider = (Slider)target;
			((RangeBase)ContextSlider).ValueChanged += ContextSlider_Changed;
			break;
		case 60:
			ThresholdCanvas = (Canvas)target;
			((FrameworkElement)ThresholdCanvas).SizeChanged += new SizeChangedEventHandler(Thresholds_SizeChanged);
			break;
		case 61:
			ProfileTargetMarker = (Border)target;
			break;
		case 62:
			MemoryEstimateMarker = (Border)target;
			break;
		case 63:
			MemoryEstimateLine = (Border)target;
			break;
		case 64:
			ContextThresholds = (TextBlock)target;
			break;
		case 65:
			ContextRisk = (TextBlock)target;
			break;
		case 66:
			WorkspaceName = (TextBlock)target;
			break;
		case 67:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(Workspace_Click);
			break;
		case 68:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(ClearWorkspace_Click);
			break;
		case 69:
			ToolsCheck = (CheckBox)target;
			break;
		case 70:
			LookupCheck = (CheckBox)target;
			break;
		case 71:
			LearnMemoryCheck = (CheckBox)target;
			break;
		case 72:
			UseMemoryCheck = (CheckBox)target;
			break;
		case 73:
			SemanticMemoryCheck = (CheckBox)target;
			break;
		case 74:
			MemoryBox = (TextBox)target;
			break;
		case 75:
			MaxTokensBox = (TextBox)target;
			((TextBoxBase)MaxTokensBox).TextChanged += new TextChangedEventHandler(Reasoning_Changed);
			break;
		case 76:
			TempBox = (TextBox)target;
			break;
		case 77:
			CreativeCheck = (CheckBox)target;
			((ToggleButton)CreativeCheck).Checked += new RoutedEventHandler(Creative_Changed);
			((ToggleButton)CreativeCheck).Unchecked += new RoutedEventHandler(Creative_Changed);
			break;
		case 78:
			CreativeControls = (StackPanel)target;
			break;
		case 79:
			CreativeTempBox = (TextBox)target;
			break;
		case 80:
			MinPBox = (TextBox)target;
			break;
		case 81:
			TopPBox = (TextBox)target;
			break;
		case 82:
			DryMultiplierBox = (TextBox)target;
			break;
		case 83:
			DryBaseBox = (TextBox)target;
			break;
		case 84:
			DryLengthBox = (TextBox)target;
			break;
		case 85:
			GpuBox = (TextBox)target;
			((TextBoxBase)GpuBox).TextChanged += new TextChangedEventHandler(Compute_Changed);
			break;
		case 86:
			CpuBox = (TextBox)target;
			break;
		case 87:
			ThinkingCheck = (CheckBox)target;
			((ToggleButton)ThinkingCheck).Checked += new RoutedEventHandler(Reasoning_Changed);
			((ToggleButton)ThinkingCheck).Unchecked += new RoutedEventHandler(Reasoning_Changed);
			break;
		case 88:
			ThinkingBudgetBox = (TextBox)target;
			((TextBoxBase)ThinkingBudgetBox).TextChanged += new TextChangedEventHandler(Reasoning_Changed);
			break;
		case 89:
			ThinkingSummary = (TextBlock)target;
			break;
		case 90:
			ReplyEmojiCheck = (CheckBox)target;
			break;
		case 91:
			InstructionsBox = (TextBox)target;
			break;
		case 92:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(SaveSettings_Click);
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.31.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IStyleConnector.Connect(int connectionId, object target)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Expected O, but got Unknown
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Expected O, but got Unknown
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		if (connectionId == 18)
		{
			EventSetter val = new EventSetter();
			val.Event = UIElement.PreviewMouseRightButtonDownEvent;
			val.Handler = (Delegate)new MouseButtonEventHandler(Chat_RightClick);
			((Collection<SetterBase>)(object)((Style)target).Setters).Add((SetterBase)(object)val);
		}
	}
}
