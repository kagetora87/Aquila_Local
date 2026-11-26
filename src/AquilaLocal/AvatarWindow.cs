using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AquilaAvatarProtocol;
using Microsoft.Win32;

namespace AquilaLocal;

public sealed class AvatarWindow : Window
{
	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Foreground = Brushes.LightGray
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

	private readonly ComboBox fps = new ComboBox
	{
		ItemsSource = new int[3] { 15, 30, 60 }
	};

	private readonly StackPanel operations = new StackPanel();

	private readonly DispatcherTimer timer;

	private bool working;

	private string lastError = "";

	private string folder = "";

	private readonly ComboBox models = new ComboBox();

	private readonly TextBlock folderLabel = new TextBlock
	{
		TextTrimming = TextTrimming.CharacterEllipsis,
		Foreground = Brushes.LightGray
	};

	private readonly TextBlock modelInfo = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Foreground = Brushes.LightGray,
		FontSize = 11.0
	};

	public AvatarWindow(Settings settings, AvatarHost host, Func<AvatarPresentation, bool, bool, string, string, Task> apply, Func<string, Task> preview)
	{
		AvatarWindow avatarWindow = this;
		base.Title = "Aquila · Avatar";
		base.Width = 600.0;
		base.Height = 820.0;
		base.MinWidth = 480.0;
		base.MinHeight = 550.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.Background = new SolidColorBrush(Color.FromRgb(16, 23, 32));
		base.Foreground = (Brush)Application.Current.Resources["Ink"];
		base.FontFamily = new FontFamily("Segoe UI");
		base.FontSize = 13.0;
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
			Text = "Avatar",
			FontSize = 25.0
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Local 3D companion · VRM 1.0",
			Foreground = Brushes.LightGray,
			Margin = new Thickness(0.0, 8.0, 0.0, 12.0)
		});
		stackPanel.Children.Add(enabled);
		enabled.IsChecked = settings.AvatarEnabled;
		stackPanel.Children.Add(status);
		status.Margin = new Thickness(0.0, 10.0, 0.0, 12.0);
		stackPanel.Children.Add(operations);
		Label(operations, "AVATAR MODEL");
		operations.Children.Add(models);
		AutomationProperties.SetName(models, "Avatar model");
		models.SelectionChanged += delegate
		{
			avatarWindow.ShowModelInfo();
		};
		operations.Children.Add(folderLabel);
		folderLabel.Margin = new Thickness(0.0, 7.0, 0.0, 7.0);
		AutomationProperties.SetName(folderLabel, "Avatar library folder");
		WrapPanel wrapPanel = new WrapPanel();
		operations.Children.Add(wrapPanel);
		AddButton(wrapPanel, "Choose folder…", delegate
		{
			OpenFolderDialog openFolderDialog = new OpenFolderDialog
			{
				Title = "Choose a folder containing VRM 1.0 avatars",
				InitialDirectory = (Directory.Exists(avatarWindow.folder) ? avatarWindow.folder : AvatarCatalog.BundledFolder)
			};
			if (openFolderDialog.ShowDialog(avatarWindow) == true)
			{
				avatarWindow.folder = openFolderDialog.FolderName;
				avatarWindow.ReloadModels((avatarWindow.models.SelectedItem as AvatarChoice)?.Key ?? "");
			}
			return Task.CompletedTask;
		});
		AddButton(wrapPanel, "Choose file…", delegate
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Title = "Choose a VRM 1.0 avatar",
				Filter = "VRM 1.0 avatars (*.vrm)|*.vrm",
				InitialDirectory = (Directory.Exists(avatarWindow.folder) ? avatarWindow.folder : AvatarCatalog.BundledFolder)
			};
			if (openFileDialog.ShowDialog(avatarWindow) == true)
			{
				avatarWindow.folder = Path.GetDirectoryName(openFileDialog.FileName);
				avatarWindow.ReloadModels(AvatarCatalog.KeyFor(openFileDialog.FileName));
			}
			return Task.CompletedTask;
		});
		AddButton(wrapPanel, "Refresh", delegate
		{
			avatarWindow.ReloadModels((avatarWindow.models.SelectedItem as AvatarChoice)?.Key ?? "");
			return Task.CompletedTask;
		});
		operations.Children.Add(modelInfo);
		modelInfo.Margin = new Thickness(0.0, 0.0, 0.0, 10.0);
		folder = (string.IsNullOrEmpty(settings.AvatarFolder) ? AvatarCatalog.BundledFolder : settings.AvatarFolder);
		ReloadModels(settings.AvatarModelPath);
		WrapPanel wrapPanel2 = new WrapPanel();
		operations.Children.Add(wrapPanel2);
		AddButton(wrapPanel2, "Apply / open", () => Run(avatarWindow.enabled.IsChecked == true, restart: false));
		AddButton(wrapPanel2, "Restart", () => Run(on: true, restart: true));
		AddButton(wrapPanel2, "Stop avatar", () => Run(on: false, restart: false));
		Label(operations, "CAMERA DISTANCE");
		operations.Children.Add(distance);
		distance.Value = settings.Avatar.Distance;
		AutomationProperties.SetName(distance, "Avatar camera distance");
		Label(operations, "CAMERA HEIGHT");
		operations.Children.Add(height);
		height.Value = settings.Avatar.Height;
		AutomationProperties.SetName(height, "Avatar camera height");
		Label(operations, "FIELD OF VIEW");
		operations.Children.Add(fov);
		fov.Value = settings.Avatar.Fov;
		AutomationProperties.SetName(fov, "Avatar field of view");
		Label(operations, "FRAME RATE");
		operations.Children.Add(fps);
		fps.SelectedItem = settings.Avatar.Fps;
		AutomationProperties.SetName(fps, "Avatar frame rate");
		Label(operations, "STATE PREVIEWS");
		WrapPanel wrapPanel3 = new WrapPanel();
		operations.Children.Add(wrapPanel3);
		string[] states = AvatarMessage.States;
		foreach (string state in states)
		{
			ReadOnlySpan<char> readOnlySpan = new ReadOnlySpan<char>(char.ToUpperInvariant(state[0]));
			string text = state;
			AddButton(wrapPanel3, string.Concat(readOnlySpan, text.Substring(1, text.Length - 1)), async delegate
			{
				if (!host.Connected)
				{
					AvatarWindow avatarWindow2 = avatarWindow;
					string text2 = (avatarWindow.status.Text = "Open the avatar first.");
					avatarWindow2.lastError = text2;
				}
				else
				{
					avatarWindow.lastError = "";
					await preview(state);
				}
			});
		}
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Select a local .vrm file, then Apply / open. Changing characters restarts only the avatar. Folder lists show up to 200 VRM files directly inside that folder; unzip downloads first. Respect each creator's licence and embedded usage conditions. Listening and speaking remain visual previews; no microphone or audio is active.",
			Foreground = Brushes.LightGray,
			FontSize = 11.0,
			TextWrapping = TextWrapping.Wrap,
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
				avatarWindow.status.Text = ((avatarWindow.lastError.Length > 0) ? avatarWindow.lastError : (host.Connected ? ("Connected · " + host.LastAppliedState + " · VRM model ready") : (host.Running ? "Connecting…" : "Stopped · existing chat works independently")));
			}
		};
		timer.Start();
		base.Closed += delegate
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
			avatarWindow.operations.IsEnabled = false;
			avatarWindow.enabled.IsEnabled = false;
			try
			{
				string text2 = (avatarWindow.models.SelectedItem as AvatarChoice)?.Key ?? settings.AvatarModelPath;
				if (on)
				{
					AvatarCatalog.Inspect(text2);
				}
				avatarWindow.enabled.IsChecked = on;
				await apply(new AvatarPresentation
				{
					Distance = (float)avatarWindow.distance.Value,
					Height = (float)avatarWindow.height.Value,
					Fov = (float)avatarWindow.fov.Value,
					Fps = (int)avatarWindow.fps.SelectedItem
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
				avatarWindow.operations.IsEnabled = true;
				avatarWindow.enabled.IsEnabled = true;
			}
		}
	}

	private void ReloadModels(string selected)
	{
		lastError = "";
		string text = "";
		folderLabel.Text = folder;
		folderLabel.ToolTip = folder;
		try
		{
			models.ItemsSource = AvatarCatalog.List(folder, selected);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			text = "Avatar folder: " + ex.Message;
			models.ItemsSource = AvatarCatalog.List("", selected);
		}
		models.SelectedItem = models.Items.Cast<AvatarChoice>().FirstOrDefault((AvatarChoice c) => c.Key == selected) ?? models.Items[0];
		if (text.Length > 0)
		{
			lastError = text;
		}
	}

	private void ShowModelInfo()
	{
		if (!(models.SelectedItem is AvatarChoice avatarChoice))
		{
			return;
		}
		try
		{
			AvatarModelInfo avatarModelInfo = AvatarCatalog.Inspect(avatarChoice.Key);
			modelInfo.Text = $"{avatarModelInfo.Name} · {avatarModelInfo.Authors} · {(double)avatarModelInfo.Bytes / 1048576.0:0.0} MB\nBlink: {(avatarModelInfo.Blink ? "yes" : "not supplied")} · Mouth: {(avatarModelInfo.Mouth ? "yes" : "not supplied")}\n{avatarModelInfo.Usage}";
			modelInfo.ToolTip = avatarModelInfo.LicenseUrl + "\n" + AvatarCatalog.Resolve(avatarChoice.Key);
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
		panel.Children.Add(new TextBlock
		{
			Text = value,
			Foreground = Brushes.LightGray,
			FontSize = 10.0,
			Margin = new Thickness(0.0, 14.0, 0.0, 5.0)
		});
	}

	private static void AddButton(Panel panel, string title, Func<Task> action)
	{
		Button button = new Button
		{
			Content = title,
			Margin = new Thickness(0.0, 0.0, 7.0, 7.0),
			Padding = new Thickness(10.0, 7.0, 10.0, 7.0)
		};
		button.Click += async delegate
		{
			await action();
		};
		panel.Children.Add(button);
	}
}
