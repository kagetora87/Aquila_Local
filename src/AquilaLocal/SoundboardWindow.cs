using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Sounds;
using Microsoft.Win32;

namespace AquilaLocal;

/// <summary>
/// Make and edit the soundboard: add sound files, name them, give each a type and a short description, put them
/// in order, and switch persona sounds on or off. Saved in Data\soundboard as numbered files.
/// </summary>
public sealed class SoundboardWindow : AquilaWindow
{
	private readonly SoundboardLibrary library;

	private readonly Settings settings;

	private readonly Action saveSettings;

	private readonly SoundboardSet set;

	private readonly List<string> addedThisTime = new List<string>();

	private readonly StackPanel rows = new StackPanel();

	private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap };

	private readonly SoundboardPlayer preview = new SoundboardPlayer();

	private bool saved;

	public SoundboardWindow(SoundboardLibrary library, Settings settings, Action saveSettings)
	{
		this.library = library;
		this.settings = settings;
		this.saveSettings = saveSettings;
		base.Title = "Aquila · Soundboard";
		base.Width = 820;
		base.Height = 820;
		base.MinWidth = 620;
		base.MinHeight = 480;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		CloseOnEscape = true;
		try
		{
			set = library.Load();
		}
		catch (PersonaConfigException ex)
		{
			set = new SoundboardSet();
			Problem(ex.Message + " An empty soundboard is shown; saving replaces the broken list (your sound files stay in the folder).");
		}

		StackPanel body = new StackPanel();
		CheckBox enabled = new CheckBox
		{
			Content = "Let personas play sounds from the soundboard",
			IsChecked = settings.SoundboardEnabled,
			Margin = new Thickness(0, 0, 0, 4)
		};
		body.Children.Add(enabled);
		body.Children.Add(Ui.Text("When something she does would make a sound, she looks for a match by name, type or description and it plays after her message (after her voice, if she's speaking).", "Type.Small"));
		Slider volume = new Slider { Minimum = 0, Maximum = 1, Value = settings.SoundboardVolume, Width = 220, HorizontalAlignment = HorizontalAlignment.Left, SmallChange = 0.05, LargeChange = 0.1 };
		body.Children.Add(Ui.Field("Volume", volume));

		StackPanel tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 8) };
		Button add = Ui.Button("Add sounds…", "Btn.Secondary", delegate
		{
			AddSounds();
		});
		add.Margin = new Thickness(0, 0, 8, 0);
		tools.Children.Add(add);
		tools.Children.Add(Ui.Button("Open the soundboard folder", "Btn.Ghost", delegate
		{
			try
			{
				Directory.CreateDirectory(library.Folder);
				Process.Start(new ProcessStartInfo { FileName = library.Folder, UseShellExecute = true });
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
			{
				Problem(ex.Message);
			}
		}));
		body.Children.Add(tools);
		body.Children.Add(rows);
		Redraw();

		Button save = Ui.Button("Save", "Btn.Primary", delegate
		{
			try
			{
				library.Save(set);
				settings.SoundboardEnabled = enabled.IsChecked == true;
				settings.SoundboardVolume = Math.Round(volume.Value, 2);
				saveSettings();
				saved = true;
				DialogResult = true;
			}
			catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException)
			{
				Problem(ex.Message);
				Redraw();
			}
		});
		Button cancel = Ui.Button("Cancel", "Btn.Secondary", delegate
		{
			Close();
		});
		base.Content = Ui.DialogScaffold("Soundboard", "Sounds your personas can play. Give each a clear name, and a type and description if it helps (for example name \"Door knock\", type \"door\", description \"three firm knocks on a wooden door\"). The order here is the order of the files in the folder.", body, status, cancel, save);
		base.Closed += delegate
		{
			preview.Stop();
			if (!saved)
			{
				ForgetAdded();
			}
		};
	}

	private void AddSounds()
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "Add sounds to the soundboard",
			Filter = "Sounds (*.wav;*.mp3;*.wma;*.m4a;*.aac)|*.wav;*.mp3;*.wma;*.m4a;*.aac",
			Multiselect = true
		};
		if (dialog.ShowDialog(this) != true)
		{
			return;
		}
		List<string> problems = new List<string>();
		int added = 0;
		foreach (string file in dialog.FileNames)
		{
			try
			{
				SoundEntry entry = library.Import(set, file);
				addedThisTime.Add(library.PathOf(entry));
				added++;
			}
			catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException)
			{
				problems.Add(ex.Message);
			}
		}
		Redraw();
		if (problems.Count > 0)
		{
			Problem(string.Join(" ", problems));
		}
		else
		{
			Ok(added == 1 ? "Added 1 sound. Name it, then press Save." : $"Added {added} sounds. Name them, then press Save.");
		}
	}

	/// <summary>Sounds added but never saved are taken back out of the folder.</summary>
	private void ForgetAdded()
	{
		foreach (string path in addedThisTime)
		{
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				// Left behind; the next save moves it into "removed".
			}
		}
	}

	private void Redraw()
	{
		rows.Children.Clear();
		if (set.Sounds.Count == 0)
		{
			rows.Children.Add(Ui.Text("No sounds yet. Press Add sounds… to pick some sound files.", "Type.Small"));
			return;
		}
		for (int i = 0; i < set.Sounds.Count; i++)
		{
			rows.Children.Add(Row(i, set.Sounds[i]));
		}
	}

	private UIElement Row(int index, SoundEntry entry)
	{
		Border card = new Border
		{
			Background = Ui.Brush("Surface.1"),
			BorderBrush = Ui.Brush("Stroke.Default"),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(8),
			Padding = new Thickness(10, 8, 10, 8),
			Margin = new Thickness(0, 0, 0, 6)
		};
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

		TextBlock number = Ui.Text((index + 1).ToString(), "Type.Small");
		number.VerticalAlignment = VerticalAlignment.Center;
		grid.Children.Add(number);

		TextBox name = new TextBox { Text = entry.Name, MaxLength = SoundboardSet.MaxNameLength, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Name (what she asks for)" };
		Ui.SetPlaceholder(name, "Name");
		name.TextChanged += delegate
		{
			entry.Name = name.Text;
		};
		Grid.SetColumn(name, 1);
		grid.Children.Add(name);

		TextBox type = new TextBox { Text = entry.Type, MaxLength = SoundboardSet.MaxNameLength, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Type, like door, laugh, phone, animal" };
		Ui.SetPlaceholder(type, "Type");
		type.TextChanged += delegate
		{
			entry.Type = type.Text;
		};
		Grid.SetColumn(type, 2);
		grid.Children.Add(type);

		StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal };
		buttons.Children.Add(Ui.IconButton("", "Play", async delegate
		{
			try
			{
				await preview.PlayAsync(new[] { library.PathOf(entry) }, settings.SoundboardVolume);
			}
			catch (Exception ex) when (ex is IOException || ex is UriFormatException)
			{
				Problem(ex.Message);
			}
		}));
		Button up = Ui.IconButton("", "Move up", delegate
		{
			Move(index, -1);
		});
		up.IsEnabled = index > 0;
		buttons.Children.Add(up);
		Button down = Ui.IconButton("", "Move down", delegate
		{
			Move(index, 1);
		});
		down.IsEnabled = index < set.Sounds.Count - 1;
		buttons.Children.Add(down);
		buttons.Children.Add(Ui.IconButton("", "Remove (the file is moved to the \"removed\" folder when you save)", delegate
		{
			set.Sounds.Remove(entry);
			Redraw();
		}));
		Grid.SetColumn(buttons, 3);
		grid.Children.Add(buttons);

		TextBox description = new TextBox { Text = entry.Description, MaxLength = SoundboardSet.MaxDescriptionLength, Margin = new Thickness(0, 6, 0, 0), ToolTip = "A short description, so she can tell when it fits" };
		Ui.SetPlaceholder(description, "Description (optional), like \"three firm knocks on a wooden door\"");
		description.TextChanged += delegate
		{
			entry.Description = description.Text;
		};
		Grid.SetColumn(description, 1);
		Grid.SetColumnSpan(description, 3);
		Grid.SetRow(description, 1);
		grid.Children.Add(description);

		card.Child = grid;
		return card;
	}

	private void Move(int index, int by)
	{
		int to = index + by;
		if (to < 0 || to >= set.Sounds.Count)
		{
			return;
		}
		SoundEntry entry = set.Sounds[index];
		set.Sounds.RemoveAt(index);
		set.Sounds.Insert(to, entry);
		Redraw();
	}

	private void Ok(string text)
	{
		status.Text = text;
		status.Foreground = Ui.Brush("Text.Secondary");
	}

	private void Problem(string text)
	{
		status.Text = text;
		status.Foreground = Ui.Brush("Danger");
	}
}
