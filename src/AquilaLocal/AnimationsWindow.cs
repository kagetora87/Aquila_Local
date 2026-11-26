using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AquilaLocal;

/// <summary>
/// Where the user manages the avatar's animations: see each one, preview it, rename it, change its
/// speed, drop new pictures on it, add new ones, import a folder, and test which animation a sentence
/// would trigger.
/// </summary>
public sealed class AnimationsWindow : AquilaWindow
{
	private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(151, 168, 187));

	private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(128, 222, 197));

	private static readonly Brush Card = new SolidColorBrush(Color.FromRgb(20, 31, 43));

	private readonly SpriteAvatarOptions options;

	private readonly string root;

	private readonly GestureMatcher matcher;

	private readonly Action changed;

	private readonly SpriteAvatarView preview = new SpriteAvatarView();

	private readonly StackPanel list = new StackPanel();

	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Foreground = Accent,
		Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
	};

	public AnimationsWindow(SpriteAvatarOptions options, string root, GestureMatcher matcher, Action changed)
	{
		this.options = options;
		this.root = root;
		this.matcher = matcher;
		this.changed = changed;
		base.Title = "Aquila · Avatar animations";
		base.Width = 1040.0;
		base.Height = 760.0;
		base.MinWidth = 820.0;
		base.MinHeight = 560.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.Background = new SolidColorBrush(Color.FromRgb(16, 23, 32));
		Grid grid = new Grid
		{
			Margin = new Thickness(22.0)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(300.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(22.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		base.Content = grid;
		grid.Children.Add(BuildLeft());
		ScrollViewer scrollViewer = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Content = list
		};
		Grid.SetColumn(scrollViewer, 2);
		grid.Children.Add(scrollViewer);
		Reload();
		base.Closed += delegate
		{
			preview.SetState("idle");
		};
	}

	private UIElement BuildLeft()
	{
		StackPanel stackPanel = new StackPanel();
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Avatar animations",
			FontSize = 22.0
		});
		stackPanel.Children.Add(Note("Each animation is a set of pictures played in order. Name it in plain words. \"idle\", \"talking\", \"thinking\" and \"listening\" play automatically; any other name is a gesture that plays when the reply fits it.", 8.0));
		Border border = new Border
		{
			Background = Card,
			CornerRadius = new CornerRadius(8.0),
			Height = 300.0,
			Margin = new Thickness(0.0, 14.0, 0.0, 0.0),
			Padding = new Thickness(8.0),
			Child = preview
		};
		stackPanel.Children.Add(border);
		WrapPanel wrapPanel = new WrapPanel
		{
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		wrapPanel.Children.Add(Button("New animation…", NewAnimation));
		wrapPanel.Children.Add(Button("Import folder…", ImportFolder));
		wrapPanel.Children.Add(Button("Open folder", delegate
		{
			Process.Start(new ProcessStartInfo("explorer.exe", "\"" + root + "\"")
			{
				UseShellExecute = true
			});
		}));
		wrapPanel.Children.Add(Button("Add placeholders", delegate
		{
			Run(() => (AvatarPlaceholders.EnsureIn(root) == 0) ? "All placeholder sets are already there." : "Missing placeholder sets were added.");
		}));
		stackPanel.Children.Add(wrapPanel);
		stackPanel.Children.Add(Label("HOW EXPRESSIVE"));
		Slider slider = new Slider
		{
			Minimum = 0.0,
			Maximum = 1.0,
			Value = options.Sensitivity,
			SmallChange = 0.05,
			LargeChange = 0.1
		};
		slider.ValueChanged += delegate
		{
			options.Sensitivity = slider.Value;
			matcher.Sensitivity = slider.Value;
		};
		stackPanel.Children.Add(slider);
		Grid grid = new Grid();
		grid.Children.Add(new TextBlock
		{
			Text = "Only obvious moments",
			FontSize = 10.0,
			Foreground = Muted
		});
		grid.Children.Add(new TextBlock
		{
			Text = "Very expressive",
			FontSize = 10.0,
			Foreground = Muted,
			HorizontalAlignment = HorizontalAlignment.Right
		});
		stackPanel.Children.Add(grid);
		stackPanel.Children.Add(Label("TRY A SENTENCE"));
		TextBox test = new TextBox
		{
			Text = "Hi there, nice to see you!"
		};
		stackPanel.Children.Add(test);
		TextBlock result = new TextBlock
		{
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0)
		};
		Button button = Button("Test", delegate
		{
			GestureMatcher.GestureMatch gestureMatch = matcher.Best(test.Text);
			if (gestureMatch == null)
			{
				result.Text = "There are no gestures yet. Add an animation with a name like \"waving hello\".";
			}
			else if (gestureMatch.Score >= matcher.Threshold)
			{
				result.Text = $"Would play \"{gestureMatch.Name}\" (match {gestureMatch.Score:P0}).";
				preview.PlayGesture(gestureMatch.Name);
			}
			else
			{
				result.Text = $"Nothing would play. Closest is \"{gestureMatch.Name}\" at {gestureMatch.Score:P0}; it needs {matcher.Threshold:P0}. Move the slider toward \"Very expressive\" to make it react more.";
			}
		});
		button.HorizontalAlignment = HorizontalAlignment.Left;
		button.Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
		stackPanel.Children.Add(button);
		stackPanel.Children.Add(result);
		stackPanel.Children.Add(status);
		return new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Content = stackPanel
		};
	}

	private void Reload()
	{
		preview.Load(root);
		matcher.SetAnimations(preview.Animations);
		changed();
		list.Children.Clear();
		if (preview.Animations.Count == 0)
		{
			list.Children.Add(Note("No animations yet. Use \"Add placeholders\" for a starter set, or \"New animation…\" to add your own pictures.", 0.0));
			return;
		}
		foreach (SpriteAnimation animation in preview.Animations)
		{
			list.Children.Add(BuildCard(animation));
		}
	}

	private UIElement BuildCard(SpriteAnimation animation)
	{
		Border border = new Border
		{
			Background = Card,
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(14.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0),
			AllowDrop = true
		};
		StackPanel stackPanel = (StackPanel)(border.Child = new StackPanel());
		DockPanel dockPanel = new DockPanel();
		stackPanel.Children.Add(dockPanel);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		DockPanel.SetDock(stackPanel2, Dock.Right);
		dockPanel.Children.Add(stackPanel2);
		stackPanel2.Children.Add(Button("▶ Play", delegate
		{
			preview.Preview(animation.Name);
		}));
		stackPanel2.Children.Add(Button("Replace pictures…", delegate
		{
			string[] array = PickPictures();
			if (array != null)
			{
				Run(delegate
				{
					SpriteLibrary.SaveFrames(root, animation.Name, array);
					return $"\"{animation.Name}\" now has {array.Length} pictures.";
				});
			}
		}));
		stackPanel2.Children.Add(Button("Delete", delegate
		{
			if (AquilaDialog.Confirm(this, "Delete this animation?", "\"" + animation.Name + "\" and its pictures will be removed from the avatar folder.", "Delete", "Cancel", DialogKind.Danger))
			{
				Run(delegate
				{
					Directory.Delete(animation.Folder, recursive: true);
					return "Deleted \"" + animation.Name + "\".";
				});
			}
		}));
		TextBox name = new TextBox
		{
			Text = animation.Name,
			FontSize = 15.0,
			Margin = new Thickness(0.0, 0.0, 12.0, 0.0),
			ToolTip = "The name in plain words. Press Enter to rename."
		};
		name.KeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.Key == Key.Return)
			{
				Rename();
			}
		};
		name.LostFocus += delegate
		{
			Rename();
		};
		dockPanel.Children.Add(name);
		stackPanel.Children.Add(Note(Describe(animation), 6.0));
		WrapPanel wrapPanel = new WrapPanel
		{
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
		};
		stackPanel.Children.Add(wrapPanel);
		for (int i = 0; i < animation.Frames.Count; i++)
		{
			wrapPanel.Children.Add(Thumb(animation, i));
		}
		Grid grid = new Grid
		{
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(220.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		stackPanel.Children.Add(grid);
		grid.Children.Add(new TextBlock
		{
			Text = "Speed",
			Foreground = Muted,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
		});
		Slider speed = new Slider
		{
			Minimum = 0.25,
			Maximum = 3.0,
			Value = animation.Meta.Speed,
			VerticalAlignment = VerticalAlignment.Center,
			ToolTip = "Slower ← → Faster"
		};
		Grid.SetColumn(speed, 1);
		grid.Children.Add(speed);
		speed.PreviewMouseUp += delegate
		{
			animation.Meta.Speed = Math.Round(speed.Value, 2);
			SaveMeta(animation, reload: false);
			preview.Load(root);
			preview.Preview(animation.Name);
		};
		CheckBox loop = new CheckBox
		{
			Content = "Repeat while active",
			IsChecked = animation.Loops,
			Margin = new Thickness(16.0, 0.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center,
			ToolTip = "Base states usually repeat; gestures usually play once."
		};
		Grid.SetColumn(loop, 2);
		grid.Children.Add(loop);
		loop.Click += delegate
		{
			animation.Meta.Loop = loop.IsChecked == true;
			SaveMeta(animation, reload: false);
		};
		if (animation.Kind == SpriteKind.Gesture)
		{
			stackPanel.Children.Add(new TextBlock
			{
				Text = "When should it play? (optional, helps matching)",
				Foreground = Muted,
				FontSize = 11.0,
				Margin = new Thickness(0.0, 10.0, 0.0, 4.0)
			});
			TextBox when = new TextBox
			{
				Text = animation.Meta.Description,
				ToolTip = "For example: greeting someone, saying hi or goodbye"
			};
			when.LostFocus += delegate
			{
				if (when.Text.Trim() != animation.Meta.Description)
				{
					animation.Meta.Description = when.Text.Trim();
					SaveMeta(animation, reload: true);
				}
			};
			stackPanel.Children.Add(when);
		}
		border.DragOver += delegate(object _, DragEventArgs e)
		{
			e.Effects = (DroppedPictures(e).Length != 0) ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		};
		border.Drop += delegate(object _, DragEventArgs e)
		{
			string[] files = DroppedPictures(e);
			if (files.Length != 0)
			{
				Run(delegate
				{
					SpriteLibrary.SaveFrames(root, animation.Name, files.OrderBy((string f) => Path.GetFileName(f), Comparer<string>.Create(SpriteLibrary.NaturalCompare)).ToList());
					return $"\"{animation.Name}\" now has {files.Length} pictures.";
				});
			}
		};
		return border;
		void Rename()
		{
			if (!(name.Text.Trim() == animation.Name) && name.Text.Trim().Length != 0)
			{
				string newName = name.Text;
				Run(delegate
				{
					SpriteLibrary.Rename(animation, newName);
					return "Renamed to \"" + SpriteLibrary.SafeName(newName) + "\".";
				});
			}
		}
	}

	private UIElement Thumb(SpriteAnimation animation, int index)
	{
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(0.0, 0.0, 8.0, 8.0)
		};
		Image image = new Image
		{
			Width = 72.0,
			Height = 88.0,
			Stretch = Stretch.Uniform,
			ToolTip = Path.GetFileName(animation.Frames[index])
		};
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
			bitmapImage.DecodePixelHeight = 176;
			bitmapImage.UriSource = new Uri(animation.Frames[index]);
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			image.Source = bitmapImage;
		}
		catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is ArgumentException)
		{
		}
		stackPanel.Children.Add(new Border
		{
			Background = new SolidColorBrush(Color.FromRgb(16, 23, 32)),
			CornerRadius = new CornerRadius(4.0),
			Child = image
		});
		UniformGrid uniformGrid = new UniformGrid
		{
			Columns = 2
		};
		Button button = Button("◀", delegate
		{
			Move(animation, index, -1);
		});
		Button button2 = Button("▶", delegate
		{
			Move(animation, index, 1);
		});
		button.IsEnabled = index > 0;
		button2.IsEnabled = index < animation.Frames.Count - 1;
		button.Margin = (button2.Margin = new Thickness(0.0, 3.0, 0.0, 0.0));
		button.Padding = (button2.Padding = new Thickness(2.0));
		button.ToolTip = "Play this picture earlier";
		button2.ToolTip = "Play this picture later";
		uniformGrid.Children.Add(button);
		uniformGrid.Children.Add(button2);
		stackPanel.Children.Add(uniformGrid);
		return stackPanel;
	}

	private void Move(SpriteAnimation animation, int index, int delta)
	{
		List<string> list = animation.Frames.ToList();
		int num = index + delta;
		if (num < 0 || num >= list.Count)
		{
			return;
		}
		List<string> list2 = list;
		int index2 = index;
		int index3 = num;
		string value = list[num];
		string value2 = list[index];
		list2[index2] = value;
		list[index3] = value2;
		Run(delegate
		{
			SpriteLibrary.SaveFrames(root, animation.Name, list);
			return "";
		});
	}

	private void NewAnimation()
	{
		string text = Ask("Name the new animation in plain words, for example \"waving hello\", \"eating\" or \"laughing\".\nUse \"idle\", \"talking\", \"thinking\" or \"listening\" to replace one of the automatic states.");
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		string[] files = PickPictures();
		if (files != null)
		{
			Run(delegate
			{
				SpriteLibrary.SaveFrames(root, text, files.OrderBy((string f) => Path.GetFileName(f), Comparer<string>.Create(SpriteLibrary.NaturalCompare)).ToList());
				return $"Added \"{SpriteLibrary.SafeName(text)}\" with {files.Length} pictures.";
			});
		}
	}

	private void ImportFolder()
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = "Choose a folder of avatar pictures"
		};
		if (openFolderDialog.ShowDialog(this) == true)
		{
			string folder = openFolderDialog.FolderName;
			Run(delegate
			{
				List<string> list = SpriteLibrary.Import(folder, root);
				return (list.Count == 0) ? "No pictures were found in that folder." : ("Imported: " + string.Join(", ", list) + ".");
			});
		}
	}

	private string[]? PickPictures()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Choose the pictures, in any order (they play in file name order)",
			Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
			Multiselect = true
		};
		if (openFileDialog.ShowDialog(this) != true || openFileDialog.FileNames.Length == 0)
		{
			return null;
		}
		return openFileDialog.FileNames;
	}

	private static string[] DroppedPictures(DragEventArgs e)
	{
		if (!(e.Data.GetData(DataFormats.FileDrop) is string[] source))
		{
			return Array.Empty<string>();
		}
		return source.Where(SpriteLibrary.IsImage).ToArray();
	}

	private void SaveMeta(SpriteAnimation animation, bool reload)
	{
		try
		{
			SpriteLibrary.WriteMeta(animation.Folder, animation.Meta);
			if (reload)
			{
				matcher.SetAnimations(SpriteLibrary.Load(root));
				changed();
			}
			else
			{
				changed();
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			status.Text = "Could not save: " + ex.Message;
		}
	}

	private void Run(Func<string> action)
	{
		try
		{
			status.Text = action();
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
		{
			status.Text = ex.Message;
		}
		Reload();
	}

	private string? Ask(string prompt)
	{
		Window window = new AquilaWindow
		{
			Title = "New animation",
			Width = 460.0,
			SizeToContent = SizeToContent.Height,
			Owner = this,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = base.Background,
			ResizeMode = ResizeMode.NoResize
		};
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(18.0)
		};
		stackPanel.Children.Add(Note(prompt, 0.0));
		TextBox box = new TextBox
		{
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
		};
		stackPanel.Children.Add(box);
		Button button = Button("Next: choose pictures…", delegate
		{
			window.DialogResult = true;
		});
		button.IsDefault = true;
		button.HorizontalAlignment = HorizontalAlignment.Right;
		button.Margin = new Thickness(0.0, 12.0, 0.0, 0.0);
		stackPanel.Children.Add(button);
		window.Content = stackPanel;
		window.Loaded += delegate
		{
			box.Focus();
		};
		return (window.ShowDialog() == true) ? box.Text.Trim() : null;
	}

	private static string Describe(SpriteAnimation animation)
	{
		string text = animation.Kind switch
		{
			SpriteKind.Idle => "Plays when nothing is happening.",
			SpriteKind.Talking => "Plays while a reply is being written or spoken.",
			SpriteKind.Thinking => "Plays while the model is working on its answer.",
			SpriteKind.Listening => "Plays while you type.",
			_ => "Gesture: plays once when a reply fits it, then goes back to idle.",
		};
		return $"{text}  ·  {animation.Frames.Count} pictures";
	}

	private static TextBlock Note(string text, double top)
	{
		return new TextBlock
		{
			Text = text,
			TextWrapping = TextWrapping.Wrap,
			Foreground = Muted,
			FontSize = 12.0,
			Margin = new Thickness(0.0, top, 0.0, 0.0)
		};
	}

	private static TextBlock Label(string text)
	{
		return new TextBlock
		{
			Text = text,
			FontSize = 10.0,
			Foreground = Accent,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, 18.0, 0.0, 6.0)
		};
	}

	private static Button Button(string text, Action click)
	{
		Button button = new Button
		{
			Content = text,
			Margin = new Thickness(0.0, 0.0, 6.0, 6.0),
			Padding = new Thickness(8.0, 4.0, 8.0, 4.0),
			FontSize = 12.0
		};
		button.Click += delegate
		{
			click();
		};
		return button;
	}
}
