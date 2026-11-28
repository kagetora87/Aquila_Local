using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Living;

namespace AquilaLocal;

/// <summary>
/// "How she's changed": every change she made to her own personality sheet, newest first, with what she took out
/// and added, why, and buttons to edit or undo it. Opening it marks them as seen.
/// </summary>
public sealed class GrowthWindow : AquilaWindow
{
	private readonly CompanionHost host;

	private readonly GrowthStore growths;

	private readonly string personaId;

	private readonly string name;

	private readonly string gender;

	private readonly StackPanel body = new StackPanel();

	private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap };

	private readonly HashSet<string> unseen;

	private string W(string text) => Genders.Words(text, gender);

	public GrowthWindow(CompanionHost host, GrowthStore growths, string personaId, string name, string gender)
	{
		this.host = host;
		this.growths = growths;
		this.personaId = personaId;
		this.name = name;
		this.gender = gender;
		base.Title = "Aquila · How " + name + " has changed";
		base.Width = 780;
		base.Height = 820;
		base.MinWidth = 560;
		base.MinHeight = 420;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		CloseOnEscape = true;
		List<GrowthChange> all = growths.Changes(personaId);
		unseen = all.Where((GrowthChange c) => !c.Seen).Select((GrowthChange c) => c.Id).ToHashSet();
		if (unseen.Count > 0)
		{
			foreach (GrowthChange c in all)
			{
				c.Seen = true;
			}
			growths.SaveChanges(personaId, all);
		}
		Button close = Ui.Button("Close", "Btn.Primary", delegate { Close(); });
		base.Content = Ui.DialogScaffold(W("How " + name + " has changed"), W("About once a week, while {s} sleeps, " + name + " looks back over what's happened and may change {p} own personality a little. The changes are already in place. Look them over here: edit any you'd word differently, or undo one that doesn't feel like {o}."), body, status, close);
		Build();
	}

	private void Build()
	{
		body.Children.Clear();
		List<GrowthChange> all = growths.Changes(personaId);
		if (all.Count == 0)
		{
			body.Children.Add(Ui.Text(W(name + " hasn't changed anything yet. Once Living mode is on, {s} goes over each day while {s} sleeps, and after a week or so of that {s} may grow a little."), "Type.Body"));
			return;
		}
		string current = Current();
		foreach (GrowthChange c in Enumerable.Reverse(all))
		{
			body.Children.Add(Card(c, current));
		}
	}

	private string Current()
	{
		try
		{
			return MainWindow.Normalize(PersonaConfig.Load(host.ConfigPath(personaId)).PersonalitySheet);
		}
		catch (PersonaConfigException ex)
		{
			Say(ex.Message, bad: true);
			return "";
		}
	}

	private UIElement Card(GrowthChange c, string current)
	{
		StackPanel panel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
		DateTimeOffset at = c.At.ToLocalTime();
		StackPanel head = new StackPanel { Orientation = Orientation.Horizontal };
		head.Children.Add(new TextBlock { Text = at.ToString("ddd, MMM d, yyyy"), FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("Text.Primary"), VerticalAlignment = VerticalAlignment.Center });
		bool standing = MainWindow.Normalize(c.After) == current;
		string state = c.YourChange == "undone" ? "You undid this" : c.YourChange == "edited" ? (standing ? "In place, with your edit" : "You edited this; changed again since") : standing ? "In place" : "Changed again since";
		head.Children.Add(new TextBlock { Text = "  ·  " + state, Foreground = Ui.Brush(c.YourChange == "undone" ? "Text.Tertiary" : "Accent"), VerticalAlignment = VerticalAlignment.Center });
		if (unseen.Contains(c.Id))
		{
			head.Children.Add(new Border
			{
				Background = Ui.Brush("Accent"),
				CornerRadius = new CornerRadius(8),
				Padding = new Thickness(7, 1, 7, 1),
				Margin = new Thickness(10, 0, 0, 0),
				VerticalAlignment = VerticalAlignment.Center,
				Child = new TextBlock { Text = "New", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("Bg.Sidebar") }
			});
		}
		Expander x = new Expander { Header = head, Style = Ui.Style("Expander.Card"), Margin = new Thickness(0, 0, 0, 8), IsExpanded = unseen.Contains(c.Id) || (standing && c.YourChange != "undone") };

		if (c.Summary.Length > 0)
		{
			TextBlock summary = Ui.Text("“" + c.Summary + "”", "Type.Body");
			summary.FontStyle = FontStyles.Italic;
			panel.Children.Add(summary);
		}
		if (c.Why.Length > 0)
		{
			TextBlock why = Ui.Text("Why: " + c.Why, "Type.Small");
			why.Margin = new Thickness(0, 6, 0, 0);
			panel.Children.Add(why);
		}
		(List<string> taken, List<string> added) = Diff(c.Before, c.After);
		AddLines(panel, W("What {s} took out or reworded"), taken, "Danger");
		AddLines(panel, W("What {s} wrote instead or added"), added, "Accent");

		if (c.YourChange != "undone")
		{
			TextBox edit = new TextBox { Text = c.After, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top };
			Expander sheet = new Expander { Header = W("{P} whole personality after this change (you can edit it)"), Margin = new Thickness(0, 12, 0, 0), Content = edit };
			panel.Children.Add(sheet);
			StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
			Button save = Ui.Button(Ui.IconLabel("", "Save my edit"), "Btn.Secondary", delegate { SaveEdit(c, edit.Text); });
			save.Margin = new Thickness(0, 0, 8, 0);
			save.IsEnabled = standing;
			Button undo = Ui.Button(Ui.IconLabel("", "Undo this change"), "Btn.Secondary", delegate { Undo(c); });
			undo.IsEnabled = standing;
			if (!standing)
			{
				string tip = W(name + "'s personality has changed since this (a later change, or an edit in {p} settings), so this one can't be undone on its own. Undo the later one first, or edit {p} personality in {p} settings.");
				save.ToolTip = tip;
				undo.ToolTip = tip;
				ToolTipService.SetShowOnDisabled(save, true);
				ToolTipService.SetShowOnDisabled(undo, true);
			}
			buttons.Children.Add(save);
			buttons.Children.Add(undo);
			panel.Children.Add(buttons);
		}
		x.Content = panel;
		return x;
	}

	private static void AddLines(StackPanel panel, string title, List<string> lines, string brush)
	{
		if (lines.Count == 0)
		{
			return;
		}
		panel.Children.Add(Ui.Section(title, 12));
		foreach (string line in lines)
		{
			TextBlock t = Ui.Text((brush == "Danger" ? "− " : "+ ") + line, "Type.Body");
			t.Foreground = Ui.Brush(brush == "Danger" ? "Text.Secondary" : "Text.Primary");
			t.Margin = new Thickness(0, 2, 0, 2);
			if (brush == "Danger")
			{
				t.TextDecorations = TextDecorations.Strikethrough;
			}
			panel.Children.Add(t);
		}
	}

	/// <summary>The sentences only in the old sheet, and those only in the new one.</summary>
	internal static (List<string> Taken, List<string> Added) Diff(string before, string after)
	{
		List<string> a = Sentences(before);
		List<string> b = Sentences(after);
		HashSet<string> inA = a.ToHashSet();
		HashSet<string> inB = b.ToHashSet();
		return (a.Where((string s) => !inB.Contains(s)).ToList(), b.Where((string s) => !inA.Contains(s)).ToList());
	}

	private static List<string> Sentences(string text) =>
		Regex.Split(MainWindow.Normalize(text), @"(?<=[.!?…])\s+|\n+").Select((string s) => s.Trim()).Where((string s) => s.Length > 0).ToList();

	private void SaveEdit(GrowthChange c, string text)
	{
		string edited = MainWindow.Normalize(text);
		if (edited.Length == 0)
		{
			Say("The personality can't be empty. Undo the change instead, if that's what you want.", bad: true);
			return;
		}
		if (Write(c, edited, "edited"))
		{
			Say("Saved. " + name + " is like that from the next message.", bad: false);
		}
	}

	private void Undo(GrowthChange c)
	{
		if (Write(c, MainWindow.Normalize(c.Before), "undone"))
		{
			Say(W("Undone. " + name + " is back to how {s} was before it."), bad: false);
		}
	}

	/// <summary>Writes a new sheet over this change, if her sheet is still what the change left it as.</summary>
	private bool Write(GrowthChange c, string sheet, string yourChange)
	{
		try
		{
			string path = host.ConfigPath(personaId);
			PersonaConfig config = PersonaConfig.Load(path);
			if (MainWindow.Normalize(config.PersonalitySheet) != MainWindow.Normalize(c.After))
			{
				Say(W(name + "'s personality has changed since this, so it wasn't touched. Edit it in {p} settings instead."), bad: true);
				Build();
				return false;
			}
			config.PersonalitySheet = sheet;
			config.Save(path);
			host.Forget(personaId);
			List<GrowthChange> all = growths.Changes(personaId);
			GrowthChange? mine = all.FirstOrDefault((GrowthChange g) => g.Id == c.Id);
			if (mine != null)
			{
				mine.YourChange = yourChange;
				if (yourChange == "edited")
				{
					mine.After = sheet;
				}
				growths.SaveChanges(personaId, all);
			}
			Build();
			return true;
		}
		catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException)
		{
			Say(ex.Message, bad: true);
			return false;
		}
	}

	private void Say(string text, bool bad)
	{
		status.Text = text;
		status.Foreground = Ui.Brush(bad ? "Danger" : "Text.Secondary");
	}
}
