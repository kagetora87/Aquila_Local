using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Speech;

namespace AquilaLocal;

/// <summary>Edit the wake word and the phrases for each voice command (saved to voice-commands.json).</summary>
public sealed class VoiceCommandsWindow : AquilaWindow
{
	private readonly string path;

	private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap };

	private static readonly (string Action, string Label, string Hint)[] Sections =
	{
		(VoiceActions.Talk, "Start a conversation", "One per line, like \"let's talk\". Anything that contains one of these counts, and similar wordings are understood too."),
		(VoiceActions.Call, "Call a persona", "One per line. Put {name} where her name goes, like \"call {name}\"."),
		(VoiceActions.HangUp, "Hang up a call", "One per line."),
		(VoiceActions.Stop, "Stop listening (back to waiting for the wake word)", "One per line."),
	};

	public VoiceCommandsWindow(string path)
	{
		this.path = path;
		base.Title = "Aquila · Voice commands";
		base.Width = 640;
		base.Height = 780;
		base.MinWidth = 480;
		base.MinHeight = 480;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		CloseOnEscape = true;
		VoiceCommandSet set;
		try
		{
			set = VoiceCommandSet.Load(path);
		}
		catch (PersonaConfigException ex)
		{
			set = VoiceCommandSet.Defaults();
			Problem(ex.Message + " The built-in commands are shown; saving replaces the broken file.");
		}
		StackPanel body = new StackPanel();
		TextBox wake = new TextBox { Text = set.WakeWord, MaxLength = 40 };
		body.Children.Add(Ui.Field("Wake word", wake, "Say it first to get Aquila's attention, for example \"Aquila, call Vicki\".", 0));
		TextBox soundsLike = Box(set.WakeSoundsLike, 70);
		body.Children.Add(Ui.Field("Also counts as the wake word", soundsLike, "How the recognizer sometimes writes it. One per line."));
		Dictionary<string, TextBox> boxes = new Dictionary<string, TextBox>();
		foreach ((string action, string label, string hint) in Sections)
		{
			TextBox box = Box(set.Commands.Where((VoiceCommand c) => c.Action == action).SelectMany((VoiceCommand c) => c.Phrases), 100);
			boxes[action] = box;
			body.Children.Add(Ui.Field(label, box, hint));
		}
		Button save = Ui.Button("Save", "Btn.Primary", delegate
		{
			VoiceCommandSet edited = new VoiceCommandSet
			{
				WakeWord = wake.Text.Trim(),
				WakeSoundsLike = Lines(soundsLike.Text),
				Commands = Sections.Select(((string Action, string Label, string Hint) s) => new VoiceCommand { Action = s.Action, Phrases = Lines(boxes[s.Action].Text) }).ToList()
			};
			try
			{
				edited.Save(path);
				DialogResult = true;
			}
			catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException)
			{
				Problem(ex.Message);
			}
		});
		Button defaults = Ui.Button("Restore the built-in commands", "Btn.Secondary", delegate
		{
			VoiceCommandSet d = VoiceCommandSet.Defaults();
			wake.Text = d.WakeWord;
			soundsLike.Text = string.Join("\n", d.WakeSoundsLike);
			foreach ((string action, string _, string _) in Sections)
			{
				boxes[action].Text = string.Join("\n", d.Commands.Where((VoiceCommand c) => c.Action == action).SelectMany((VoiceCommand c) => c.Phrases));
			}
			status.Text = "Built-in commands restored. Press Save to keep them.";
		});
		Button cancel = Ui.Button("Cancel", "Btn.Secondary", delegate
		{
			Close();
		});
		base.Content = Ui.DialogScaffold("Voice commands", "When \"Listen for the wake word\" is on (right-click the microphone), Aquila ignores everything you say until it hears the wake word. Then it listens for one of these commands. Nothing else is kept or shown.", body, status, defaults, cancel, save);
	}

	private void Problem(string text)
	{
		status.Text = text;
		status.Foreground = Ui.Brush("Danger");
	}

	private static TextBox Box(IEnumerable<string> lines, double height) => new TextBox
	{
		Text = string.Join("\n", lines),
		AcceptsReturn = true,
		TextWrapping = TextWrapping.Wrap,
		VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
		Height = height,
		VerticalContentAlignment = VerticalAlignment.Top
	};

	private static List<string> Lines(string text) => text.Split('\n').Select((string l) => l.Trim()).Where((string l) => l.Length > 0).ToList();
}
