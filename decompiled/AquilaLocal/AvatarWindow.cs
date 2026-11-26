using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using AquilaAvatarProtocol;
using Microsoft.Win32;

namespace AquilaLocal;

public sealed class AvatarWindow : Window
{
	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = (TextWrapping)2,
		Foreground = (Brush)(object)Brushes.LightGray
	};

	private readonly CheckBox enabled = new CheckBox
	{
		Content = "Enable avatar / open with Aquila"
	};

	private readonly Slider distance = new Slider
	{
		Minimum = 2.0,
		Maximum = 5.0,
		TickFrequency = 0.1
	};

	private readonly Slider height = new Slider
	{
		Minimum = 0.6,
		Maximum = 2.0,
		TickFrequency = 0.05
	};

	private readonly Slider fov = new Slider
	{
		Minimum = 25.0,
		Maximum = 55.0,
		TickFrequency = 1.0
	};

	private readonly ComboBox fps;

	private readonly StackPanel operations;

	private readonly DispatcherTimer timer;

	private bool working;

	private string lastError;

	private string folder;

	private readonly ComboBox models;

	private readonly TextBlock folderLabel;

	private readonly TextBlock modelInfo;

	public AvatarWindow(Settings settings, AvatarHost host, Func<AvatarPresentation, bool, bool, string, string, Task> apply, Func<string, Task> preview)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Expected O, but got Unknown
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Expected O, but got Unknown
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Expected O, but got Unknown
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00db: Expected O, but got Unknown
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e2: Expected O, but got Unknown
		//IL_0100: Unknown result type (might be due to invalid IL or missing references)
		//IL_010a: Expected O, but got Unknown
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_012b: Expected O, but got Unknown
		//IL_012c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0131: Unknown result type (might be due to invalid IL or missing references)
		//IL_0138: Unknown result type (might be due to invalid IL or missing references)
		//IL_0148: Expected O, but got Unknown
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0155: Unknown result type (might be due to invalid IL or missing references)
		//IL_0160: Unknown result type (might be due to invalid IL or missing references)
		//IL_0174: Expected O, but got Unknown
		//IL_01f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0208: Expected O, but got Unknown
		//IL_021d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0227: Expected O, but got Unknown
		//IL_022d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0237: Expected O, but got Unknown
		//IL_0246: Unknown result type (might be due to invalid IL or missing references)
		//IL_024b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0255: Unknown result type (might be due to invalid IL or missing references)
		//IL_0260: Expected O, but got Unknown
		//IL_0261: Unknown result type (might be due to invalid IL or missing references)
		//IL_0266: Unknown result type (might be due to invalid IL or missing references)
		//IL_026d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0279: Expected O, but got Unknown
		//IL_027f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0284: Unknown result type (might be due to invalid IL or missing references)
		//IL_028f: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a3: Expected O, but got Unknown
		//IL_02aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_02af: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f9: Expected O, but got Unknown
		//IL_0363: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_03cd: Expected O, but got Unknown
		//IL_040e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0428: Unknown result type (might be due to invalid IL or missing references)
		//IL_042e: Expected O, but got Unknown
		//IL_04c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_050b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0512: Expected O, but got Unknown
		//IL_06cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_06d4: Expected O, but got Unknown
		//IL_0775: Unknown result type (might be due to invalid IL or missing references)
		//IL_077a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0785: Unknown result type (might be due to invalid IL or missing references)
		//IL_0790: Unknown result type (might be due to invalid IL or missing references)
		//IL_079f: Unknown result type (might be due to invalid IL or missing references)
		//IL_07a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_07cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_07da: Expected O, but got Unknown
		//IL_07dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_07e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_07fa: Expected O, but got Unknown
		ComboBox val = new ComboBox();
		((ItemsControl)val).ItemsSource = new int[3] { 15, 30, 60 };
		fps = val;
		operations = new StackPanel();
		lastError = "";
		folder = "";
		models = new ComboBox();
		folderLabel = new TextBlock
		{
			TextTrimming = (TextTrimming)1,
			Foreground = (Brush)(object)Brushes.LightGray
		};
		modelInfo = new TextBlock
		{
			TextWrapping = (TextWrapping)2,
			Foreground = (Brush)(object)Brushes.LightGray,
			FontSize = 11.0
		};
		Settings settings2 = settings;
		Func<AvatarPresentation, bool, bool, string, string, Task> apply2 = apply;
		AvatarHost host2 = host;
		Func<string, Task> preview2 = preview;
		((Window)this)._002Ector();
		AvatarWindow avatarWindow = this;
		((Window)this).Title = "Aquila · Avatar";
		((FrameworkElement)this).Width = 600.0;
		((FrameworkElement)this).Height = 820.0;
		((FrameworkElement)this).MinWidth = 480.0;
		((FrameworkElement)this).MinHeight = 550.0;
		((Window)this).WindowStartupLocation = (WindowStartupLocation)2;
		((Control)this).Background = (Brush)new SolidColorBrush(Color.FromRgb((byte)16, (byte)23, (byte)32));
		((Control)this).Foreground = (Brush)Application.Current.Resources[(object)"Ink"];
		((Control)this).FontFamily = new FontFamily("Segoe UI");
		((Control)this).FontSize = 13.0;
		StackPanel val2 = new StackPanel
		{
			Margin = new Thickness(22.0)
		};
		((ContentControl)this).Content = (object)new ScrollViewer
		{
			Content = val2,
			VerticalScrollBarVisibility = (ScrollBarVisibility)1
		};
		((Panel)val2).Children.Add((UIElement)new TextBlock
		{
			Text = "Avatar",
			FontSize = 25.0
		});
		((Panel)val2).Children.Add((UIElement)new TextBlock
		{
			Text = "Local 3D companion · VRM 1.0",
			Foreground = (Brush)(object)Brushes.LightGray,
			Margin = new Thickness(0.0, 8.0, 0.0, 12.0)
		});
		((Panel)val2).Children.Add((UIElement)(object)enabled);
		((ToggleButton)enabled).IsChecked = settings2.AvatarEnabled;
		((Panel)val2).Children.Add((UIElement)(object)status);
		((FrameworkElement)status).Margin = new Thickness(0.0, 10.0, 0.0, 12.0);
		((Panel)val2).Children.Add((UIElement)(object)operations);
		Label((Panel)(object)operations, "AVATAR MODEL");
		((Panel)operations).Children.Add((UIElement)(object)models);
		AutomationProperties.SetName((DependencyObject)(object)models, "Avatar model");
		((Selector)models).SelectionChanged += (SelectionChangedEventHandler)delegate
		{
			avatarWindow.ShowModelInfo();
		};
		((Panel)operations).Children.Add((UIElement)(object)folderLabel);
		((FrameworkElement)folderLabel).Margin = new Thickness(0.0, 7.0, 0.0, 7.0);
		AutomationProperties.SetName((DependencyObject)(object)folderLabel, "Avatar library folder");
		WrapPanel val3 = new WrapPanel();
		((Panel)operations).Children.Add((UIElement)(object)val3);
		AddButton((Panel)(object)val3, "Choose folder…", delegate
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Unknown result type (might be due to invalid IL or missing references)
			//IL_003b: Expected O, but got Unknown
			OpenFolderDialog val7 = new OpenFolderDialog
			{
				Title = "Choose a folder containing VRM 1.0 avatars",
				InitialDirectory = (Directory.Exists(avatarWindow.folder) ? avatarWindow.folder : AvatarCatalog.BundledFolder)
			};
			if (((CommonDialog)val7).ShowDialog((Window)(object)avatarWindow).GetValueOrDefault())
			{
				avatarWindow.folder = val7.FolderName;
				avatarWindow.ReloadModels((((Selector)avatarWindow.models).SelectedItem as AvatarChoice)?.Key ?? "");
			}
			return Task.CompletedTask;
		});
		AddButton((Panel)(object)val3, "Choose file…", delegate
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Unknown result type (might be due to invalid IL or missing references)
			//IL_001b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0046: Expected O, but got Unknown
			OpenFileDialog val6 = new OpenFileDialog
			{
				Title = "Choose a VRM 1.0 avatar",
				Filter = "VRM 1.0 avatars (*.vrm)|*.vrm",
				InitialDirectory = (Directory.Exists(avatarWindow.folder) ? avatarWindow.folder : AvatarCatalog.BundledFolder)
			};
			if (((CommonDialog)val6).ShowDialog((Window)(object)avatarWindow).GetValueOrDefault())
			{
				avatarWindow.folder = Path.GetDirectoryName(((FileDialog)val6).FileName);
				avatarWindow.ReloadModels(AvatarCatalog.KeyFor(((FileDialog)val6).FileName));
			}
			return Task.CompletedTask;
		});
		AddButton((Panel)(object)val3, "Refresh", delegate
		{
			avatarWindow.ReloadModels((((Selector)avatarWindow.models).SelectedItem as AvatarChoice)?.Key ?? "");
			return Task.CompletedTask;
		});
		((Panel)operations).Children.Add((UIElement)(object)modelInfo);
		((FrameworkElement)modelInfo).Margin = new Thickness(0.0, 0.0, 0.0, 10.0);
		folder = (string.IsNullOrEmpty(settings2.AvatarFolder) ? AvatarCatalog.BundledFolder : settings2.AvatarFolder);
		ReloadModels(settings2.AvatarModelPath);
		WrapPanel val4 = new WrapPanel();
		((Panel)operations).Children.Add((UIElement)(object)val4);
		AddButton((Panel)(object)val4, "Apply / open", () => Run(((ToggleButton)avatarWindow.enabled).IsChecked.GetValueOrDefault(), restart: false));
		AddButton((Panel)(object)val4, "Restart", () => Run(on: true, restart: true));
		AddButton((Panel)(object)val4, "Stop avatar", () => Run(on: false, restart: false));
		Label((Panel)(object)operations, "CAMERA DISTANCE");
		((Panel)operations).Children.Add((UIElement)(object)distance);
		((RangeBase)distance).Value = settings2.Avatar.Distance;
		AutomationProperties.SetName((DependencyObject)(object)distance, "Avatar camera distance");
		Label((Panel)(object)operations, "CAMERA HEIGHT");
		((Panel)operations).Children.Add((UIElement)(object)height);
		((RangeBase)height).Value = settings2.Avatar.Height;
		AutomationProperties.SetName((DependencyObject)(object)height, "Avatar camera height");
		Label((Panel)(object)operations, "FIELD OF VIEW");
		((Panel)operations).Children.Add((UIElement)(object)fov);
		((RangeBase)fov).Value = settings2.Avatar.Fov;
		AutomationProperties.SetName((DependencyObject)(object)fov, "Avatar field of view");
		Label((Panel)(object)operations, "FRAME RATE");
		((Panel)operations).Children.Add((UIElement)(object)fps);
		((Selector)fps).SelectedItem = settings2.Avatar.Fps;
		AutomationProperties.SetName((DependencyObject)(object)fps, "Avatar frame rate");
		Label((Panel)(object)operations, "STATE PREVIEWS");
		WrapPanel val5 = new WrapPanel();
		((Panel)operations).Children.Add((UIElement)(object)val5);
		string[] states = AvatarMessage.States;
		foreach (string state in states)
		{
			char reference = char.ToUpperInvariant(state[0]);
			ReadOnlySpan<char> readOnlySpan = new ReadOnlySpan<char>(ref reference);
			string text = state;
			AddButton((Panel)(object)val5, string.Concat(readOnlySpan, text.Substring(1, text.Length - 1)), async delegate
			{
				if (!host2.Connected)
				{
					AvatarWindow avatarWindow3 = avatarWindow;
					string text5 = (avatarWindow.status.Text = "Open the avatar first.");
					avatarWindow3.lastError = text5;
				}
				else
				{
					avatarWindow.lastError = "";
					await preview2(state);
				}
			});
		}
		((Panel)val2).Children.Add((UIElement)new TextBlock
		{
			Text = "Select a local .vrm file, then Apply / open. Changing characters restarts only the avatar. Folder lists show up to 200 VRM files directly inside that folder; unzip downloads first. Respect each creator's licence and embedded usage conditions. Listening and speaking remain visual previews; no microphone or audio is active.",
			Foreground = (Brush)(object)Brushes.LightGray,
			FontSize = 11.0,
			TextWrapping = (TextWrapping)2,
			Margin = new Thickness(0.0, 14.0, 0.0, 0.0)
		});
		timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(500.0)
		};
		timer.Tick += delegate
		{
			if (!avatarWindow.working)
			{
				avatarWindow.status.Text = ((avatarWindow.lastError.Length > 0) ? avatarWindow.lastError : (host2.Connected ? ("Connected · " + host2.LastAppliedState + " · VRM model ready") : (host2.Running ? "Connecting…" : "Stopped · existing chat works independently")));
			}
		};
		timer.Start();
		((Window)this).Closed += delegate
		{
			avatarWindow.timer.Stop();
		};
		async Task Run(bool on, bool restart)
		{
			if (avatarWindow.working)
			{
				return;
			}
			avatarWindow.working = true;
			avatarWindow.lastError = "";
			((UIElement)avatarWindow.operations).IsEnabled = false;
			((UIElement)avatarWindow.enabled).IsEnabled = false;
			try
			{
				string text2 = (((Selector)avatarWindow.models).SelectedItem as AvatarChoice)?.Key ?? settings2.AvatarModelPath;
				if (on)
				{
					AvatarCatalog.Inspect(text2);
				}
				((ToggleButton)avatarWindow.enabled).IsChecked = on;
				await apply2(new AvatarPresentation
				{
					Distance = (float)((RangeBase)avatarWindow.distance).Value,
					Height = (float)((RangeBase)avatarWindow.height).Value,
					Fov = (float)((RangeBase)avatarWindow.fov).Value,
					Fps = (int)((Selector)avatarWindow.fps).SelectedItem
				}, on, restart, avatarWindow.folder, text2);
			}
			catch (Exception ex)
			{
				AvatarWindow avatarWindow2 = avatarWindow;
				string text3 = (avatarWindow.status.Text = ex.Message);
				avatarWindow2.lastError = text3;
			}
			finally
			{
				avatarWindow.working = false;
				((UIElement)avatarWindow.operations).IsEnabled = true;
				((UIElement)avatarWindow.enabled).IsEnabled = true;
			}
		}
	}

	private void ReloadModels(string selected)
	{
		string selected2 = selected;
		lastError = "";
		string text = "";
		folderLabel.Text = folder;
		((FrameworkElement)folderLabel).ToolTip = folder;
		try
		{
			((ItemsControl)models).ItemsSource = AvatarCatalog.List(folder, selected2);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			text = "Avatar folder: " + ex.Message;
			((ItemsControl)models).ItemsSource = AvatarCatalog.List("", selected2);
		}
		((Selector)models).SelectedItem = ((IEnumerable)((ItemsControl)models).Items).Cast<AvatarChoice>().FirstOrDefault((AvatarChoice c) => c.Key == selected2) ?? ((ItemsControl)models).Items[0];
		if (text.Length > 0)
		{
			lastError = text;
		}
	}

	private void ShowModelInfo()
	{
		if (!(((Selector)models).SelectedItem is AvatarChoice avatarChoice))
		{
			return;
		}
		try
		{
			AvatarModelInfo avatarModelInfo = AvatarCatalog.Inspect(avatarChoice.Key);
			modelInfo.Text = $"{avatarModelInfo.Name} · {avatarModelInfo.Authors} · {(double)avatarModelInfo.Bytes / 1048576.0:0.0} MB\nBlink: {(avatarModelInfo.Blink ? "yes" : "not supplied")} · Mouth: {(avatarModelInfo.Mouth ? "yes" : "not supplied")}\n{avatarModelInfo.Usage}";
			((FrameworkElement)modelInfo).ToolTip = avatarModelInfo.LicenseUrl + "\n" + AvatarCatalog.Resolve(avatarChoice.Key);
			lastError = "";
		}
		catch (Exception ex)
		{
			modelInfo.Text = "Unavailable: " + ex.Message;
			lastError = modelInfo.Text;
		}
	}

	private static void Label(Panel panel, string value)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Expected O, but got Unknown
		panel.Children.Add((UIElement)new TextBlock
		{
			Text = value,
			Foreground = (Brush)(object)Brushes.LightGray,
			FontSize = 10.0,
			Margin = new Thickness(0.0, 14.0, 0.0, 5.0)
		});
	}

	private static void AddButton(Panel panel, string title, Func<Task> action)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Expected O, but got Unknown
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_008a: Expected O, but got Unknown
		Func<Task> action2 = action;
		Button val = new Button
		{
			Content = title,
			Margin = new Thickness(0.0, 0.0, 7.0, 7.0),
			Padding = new Thickness(10.0, 7.0, 10.0, 7.0)
		};
		((ButtonBase)val).Click += (RoutedEventHandler)async delegate
		{
			await action2();
		};
		panel.Children.Add((UIElement)(object)val);
	}
}
