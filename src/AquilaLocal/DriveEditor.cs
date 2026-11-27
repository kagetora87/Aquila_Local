using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AquilaLocal.Companion;

namespace AquilaLocal;

/// <summary>
/// Edits a persona's drives: rename them, change what they mean, add new ones or remove old ones, and set how
/// they build, fade and react. Nothing is saved here; the settings' Save button applies it (and keeps her values).
/// </summary>
public sealed class DriveEditor
{
	private static readonly (string Event, string Label)[] Events =
	{
		(DriveEvents.Interaction, "Every message from you"),
		(DriveEvents.SessionTurn, "Each message in the same chat"),
		(DriveEvents.Affection, "You're affectionate or flirty"),
		(DriveEvents.Novelty, "You bring up something new"),
		(DriveEvents.Conflict, "You argue or are harsh")
	};

	private sealed class Card
	{
		public string? OriginalName;
		public Expander Box = null!;
		public TextBox Name = null!;
		public ComboBox Kind = null!;
		public TextBox Description = null!;
		public TextBox Weight = null!, Start = null!, Rise = null!, Decay = null!, Rest = null!, Step = null!, Floor = null!, Ceiling = null!;
		public Dictionary<string, TextBox> Events = new Dictionary<string, TextBox>();
		public TextBox Cues = null!;
		public TextBox Low = null!, Mid = null!, High = null!;
	}

	private readonly bool setUp;

	private readonly StackPanel cards = new StackPanel();

	private readonly List<Card> list = new List<Card>();

	private readonly Button add;

	public UIElement View { get; }

	/// <summary>"female" or "male": the words used for them.</summary>
	private readonly string gender;

	private string W(string text) => Genders.Words(text, gender);

	public DriveEditor(PersonaConfig config, bool setUp)
	{
		this.setUp = setUp;
		gender = config.Gender;
		StackPanel panel = new StackPanel();
		TextBlock intro = Ui.Text(W("Each drive is either a need (the number is how much {s} wants it, like hunger: satisfying it lowers it) or a feeling level (the number is how much of it {s} has, like feeling safe: satisfying it raises it, and it presses on {o} when it's low). The one pressing hardest, times its weight, leads {p} reply. Renaming or removing keeps everything else {s} has; nothing changes until you save."), "Type.Small");
		intro.Margin = new Thickness(0, 0, 0, 8);
		panel.Children.Add(intro);
		panel.Children.Add(cards);
		foreach (DriveSpec d in config.Drives)
		{
			AddCard(d, d.Name);
		}
		add = Ui.Button("Add a drive", "Btn.Secondary", delegate
		{
			Card c = AddCard(new DriveSpec
			{
				Name = "new-drive",
				Weight = 1.0,
				Initial = 3.0,
				SatisfyStep = 1.0,
				Guidance = new DriveGuidance()
			}, null);
			c.Box.IsExpanded = true;
			c.Name.Focus();
			c.Name.SelectAll();
		});
		add.HorizontalAlignment = HorizontalAlignment.Left;
		add.Margin = new Thickness(0, 6, 0, 0);
		panel.Children.Add(add);
		View = panel;
		RefreshAdd();
	}

	private void RefreshAdd()
	{
		add.IsEnabled = list.Count < Drives.MaxDrives;
		add.ToolTip = list.Count < Drives.MaxDrives ? W("A new drive of {p} own") : W("{S} has the most drives allowed (") + Drives.MaxDrives + ")";
	}

	private static string N(double? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";

	private Card AddCard(DriveSpec d, string? original)
	{
		Card c = new Card { OriginalName = original };
		StackPanel body = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

		Grid top = new Grid();
		top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
		top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
		c.Name = new TextBox { Text = d.Name, MaxLength = 24 };
		StackPanel name = Labeled("Name (one word, like lust or music)", c.Name);
		top.Children.Add(name);
		c.Kind = new ComboBox();
		c.Kind.Items.Add("Need: satisfying it lowers it");
		c.Kind.Items.Add("Feeling level: satisfying it raises it");
		c.Kind.SelectedIndex = d.IsLevel ? 1 : 0;
		StackPanel kind = Labeled("What the number means", c.Kind);
		Grid.SetColumn(kind, 2);
		top.Children.Add(kind);
		body.Children.Add(top);

		c.Description = new TextBox { Text = d.Description, MaxLength = Drives.MaxDescriptionChars };
		body.Children.Add(Labeled("What it is, in your words (for you)", c.Description));

		string[] labels = { "Weight", "Start", "Builds /hour", "Fades /hour", "Settles at", "Step", "Lowest", "Highest" };
		string[] tips =
		{
			W("How much it matters when choosing what leads {p} reply (0 to 10)."),
			setUp && original != null ? W("Only used when {s}'s first set up (or for a drive you're adding).") : "Its value when it starts.",
			"Points it gains every hour, whatever happens (loneliness grows while you're away).",
			"Share of the distance to 'settles at' it loses every hour: 0.02 is 2% per hour.",
			"The level it drifts back to over time.",
			"How far one reply that satisfies it moves it (down for a need, up for a level).",
			"It never goes below this.",
			"It never goes above this (10 at most)."
		};
		Grid nums = new Grid { Margin = new Thickness(0, 6, 0, 0) };
		TextBox[] boxes = new TextBox[labels.Length];
		double?[] values = { d.Weight, d.Initial, d.RisePerHour, d.DecayPerHour, d.RestingLevel, d.SatisfyStep, d.Floor, d.Ceiling };
		for (int i = 0; i < labels.Length; i++)
		{
			nums.ColumnDefinitions.Add(new ColumnDefinition());
			boxes[i] = new TextBox { Text = N(values[i]), Margin = new Thickness(0, 2, 6, 0), ToolTip = tips[i] };
			StackPanel cell = new StackPanel { ToolTip = tips[i] };
			cell.Children.Add(Ui.Text(labels[i], "Type.Caption"));
			cell.Children.Add(boxes[i]);
			Grid.SetColumn(cell, i);
			nums.Children.Add(cell);
		}
		(c.Weight, c.Start, c.Rise, c.Decay, c.Rest, c.Step, c.Floor, c.Ceiling) = (boxes[0], boxes[1], boxes[2], boxes[3], boxes[4], boxes[5], boxes[6], boxes[7]);
		c.Start.IsEnabled = !(setUp && original != null);
		body.Children.Add(nums);

		body.Children.Add(Small("What moves it (a change per message, like 0.8 or -1.5; empty for none)", 10));
		Grid ev = new Grid();
		ev.ColumnDefinitions.Add(new ColumnDefinition());
		ev.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
		ev.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
		ev.ColumnDefinitions.Add(new ColumnDefinition());
		ev.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
		for (int i = 0; i < Events.Length; i++)
		{
			if (i % 2 == 0)
			{
				ev.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			}
			(string e, string label) = Events[i];
			TextBlock l = Ui.Text(label, "Type.Small");
			l.VerticalAlignment = VerticalAlignment.Center;
			TextBox b = new TextBox { Text = d.Events.TryGetValue(e, out double delta) ? N(delta) : "", Margin = new Thickness(4, 2, 0, 2) };
			int col = i % 2 == 0 ? 0 : 3;
			Grid.SetRow(l, i / 2);
			Grid.SetColumn(l, col);
			Grid.SetRow(b, i / 2);
			Grid.SetColumn(b, col + 1);
			ev.Children.Add(l);
			ev.Children.Add(b);
			c.Events[e] = b;
		}
		body.Children.Add(ev);

		bool horizon = d.Name == Drives.Horizon;
		bool builtIn = HeuristicScorer.KnowsDrive(d.Name);
		c.Cues = new TextBox { Text = string.Join(", ", d.Cues), AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, MinHeight = 34 };
		body.Children.Add(Labeled(horizon
			? W("Words that show {s} acted on it (horizon is mostly judged by what {s} does unprompted: things {s} made, chose or tried)")
			: builtIn
				? W("Extra words or phrases that show {s} acted on it, separated by commas (Aquila already knows some for ") + d.Name + ")"
				: W("Words or phrases in {p} reply that show {s} acted on it, separated by commas (needed for a drive of your own)"), c.Cues));

		body.Children.Add(Small(W("What it tells {o} (one line each; {s} reads the one that fits its number)"), 10));
		c.Low = Line(d.Guidance.Low);
		c.Mid = Line(d.Guidance.Mid);
		c.High = Line(d.Guidance.High);
		body.Children.Add(Labeled("Low (below 3.5)", c.Low));
		body.Children.Add(Labeled("Middle (3.5 to 7)", c.Mid));
		body.Children.Add(Labeled("High (7 and up)", c.High));

		Button remove = Ui.Button("Remove this drive", "Btn.Ghost", null);
		remove.HorizontalAlignment = HorizontalAlignment.Left;
		remove.Margin = new Thickness(0, 8, 0, 0);
		body.Children.Add(remove);

		c.Box = new Expander { Content = body, Style = Ui.Style("Expander.Card"), Margin = new Thickness(0, 0, 0, 6) };
		void Header()
		{
			string kindWord = c.Kind.SelectedIndex == 1 ? "feeling level" : "need";
			c.Box.Header = (c.Name.Text.Trim().Length > 0 ? c.Name.Text.Trim() : "(no name)") + "  ·  " + kindWord + "  ·  weight " + c.Weight.Text.Trim() +
				(c.OriginalName != null && c.OriginalName != c.Name.Text.Trim() ? "  ·  was " + c.OriginalName : c.OriginalName == null ? "  ·  new" : "");
		}
		Header();
		c.Name.TextChanged += delegate { Header(); };
		c.Weight.TextChanged += delegate { Header(); };
		c.Kind.SelectionChanged += delegate { Header(); };
		remove.Click += delegate
		{
			if (list.Count <= 1)
			{
				return;
			}
			list.Remove(c);
			cards.Children.Remove(c.Box);
			RefreshAdd();
		};
		list.Add(c);
		cards.Children.Add(c.Box);
		RefreshAdd();
		return c;
	}

	private static TextBox Line(string text) => new TextBox { Text = text, MaxLength = ContextInjector.MaxGuidanceChars };

	private static TextBlock Small(string text, double top)
	{
		TextBlock t = Ui.Text(text, "Type.Caption");
		t.Margin = new Thickness(0, top, 0, 2);
		return t;
	}

	private static StackPanel Labeled(string label, Control control)
	{
		StackPanel p = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
		p.Children.Add(Ui.Text(label, "Type.Caption"));
		control.Margin = new Thickness(0, 2, 0, 0);
		p.Children.Add(control);
		return p;
	}

	private static double Num(TextBox box, string what)
	{
		string t = box.Text.Trim();
		if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v))
		{
			return v;
		}
		throw new FormatException("'" + t + "' isn't a number (" + what + "). Use a dot for decimals, like 1.5.");
	}

	/// <summary>
	/// Puts the edited drives into the config (replacing its list) and returns, for each renamed drive, the name it
	/// had before, so her stored values follow it. Refuses (with a plain reason) anything that would leave her unable to run.
	/// </summary>
	public Dictionary<string, string> ApplyTo(PersonaConfig edited)
	{
		Dictionary<string, DriveSpec> before = edited.Drives.ToDictionary((DriveSpec d) => d.Name);
		List<DriveSpec> drives = new List<DriveSpec>();
		Dictionary<string, string> renamed = new Dictionary<string, string>();
		foreach (Card c in list)
		{
			string name = c.Name.Text.Trim().ToLowerInvariant().Replace(' ', '-');
			if (!Drives.IsValidName(name))
			{
				throw new FormatException("“" + c.Name.Text.Trim() + "” can't be a drive name: use one word of letters, digits or dashes, starting with a letter (like music or self-worth).");
			}
			DriveSpec d = c.OriginalName != null && before.TryGetValue(c.OriginalName, out DriveSpec? old) ? old : new DriveSpec();
			d.Name = name;
			d.Kind = c.Kind.SelectedIndex == 1 ? DriveKinds.Level : DriveKinds.Need;
			d.Description = c.Description.Text.Trim().Replace("\r", " ").Replace("\n", " ");
			d.Weight = Num(c.Weight, name + " weight");
			d.RisePerHour = Num(c.Rise, name + " builds per hour");
			d.DecayPerHour = Num(c.Decay, name + " fades per hour");
			d.RestingLevel = Num(c.Rest, name + " settles at");
			d.SatisfyStep = Num(c.Step, name + " step");
			d.Floor = Num(c.Floor, name + " lowest");
			d.Ceiling = Num(c.Ceiling, name + " highest");
			if (c.Start.IsEnabled)
			{
				d.Initial = Num(c.Start, name + " start");
			}
			else if (d.Initial is double start)
			{
				// Only used at setup, but it must still fit the drive's range.
				d.Initial = Math.Clamp(start, d.Floor, Math.Max(d.Floor, d.Ceiling));
			}
			d.Events = new Dictionary<string, double>();
			foreach ((string e, TextBox box) in c.Events)
			{
				if (box.Text.Trim().Length > 0)
				{
					double delta = Num(box, name + " " + e);
					if (delta != 0)
					{
						d.Events[e] = delta;
					}
				}
			}
			d.Cues = c.Cues.Text.Replace("\r", ",").Replace("\n", ",").Split(',').Select((string s) => s.Trim()).Where((string s) => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			d.Guidance = new DriveGuidance { Low = c.Low.Text.Trim(), Mid = c.Mid.Text.Trim(), High = c.High.Text.Trim() };
			if (d.Guidance.Low.Length == 0 || d.Guidance.Mid.Length == 0 || d.Guidance.High.Length == 0)
			{
				throw new FormatException("Write all three lines of what " + name + W(" tells {o} (low, middle and high)."));
			}
			if (name != Drives.Horizon && d.Cues.Count == 0 && !HeuristicScorer.KnowsDrive(name))
			{
				throw new FormatException("Give " + name + W(" a few words or phrases that show {s} acted on it, so Aquila can tell when it's satisfied."));
			}
			if (c.OriginalName != null && c.OriginalName != name)
			{
				renamed[name] = c.OriginalName;
			}
			drives.Add(d);
		}
		edited.Drives = drives;
		return renamed;
	}
}
