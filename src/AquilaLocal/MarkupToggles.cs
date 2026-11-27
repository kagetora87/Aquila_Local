using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AquilaLocal.Companion;

namespace AquilaLocal;

/// <summary>
/// The Thought and Action buttons under the message box, and the eye that shows or hides the personas' thoughts.
/// A lit button means what you type now is a thought (blue) or an action (orange); press it again to end it.
/// </summary>
public sealed class MarkupToggles : StackPanel
{
	private readonly Button thought;

	private readonly Button action;

	private readonly Button eye;

	private MarkupComposer? composer;

	private bool showThoughts = true;

	private readonly Button style;

	/// <summary>Raised when the eye is pressed, with the new choice.</summary>
	public event Action<bool>? ShowThoughtsChanged;

	/// <summary>Raised when the colours or first/third person change (read them from MarkupColors and ThoughtMarkup.Person).</summary>
	public event System.Action? LookChanged;

	public MarkupToggles()
	{
		Orientation = Orientation.Horizontal;
		VerticalAlignment = VerticalAlignment.Center;
		thought = Pill("Thought", MarkupKind.Thought, "Type a private thought. She won't know it. Press again to stop. (Ctrl+T, or type _like this_)");
		action = Pill("Action", MarkupKind.Action, "Type something you do. She sees it. Press again to stop. (Ctrl+J, or type *like this*)");
		eye = Ui.Button("\uE890", "Btn.Icon", delegate
		{
			ShowThoughts = !showThoughts;
			ShowThoughtsChanged?.Invoke(showThoughts);
		});
		eye.Width = 36;
		eye.Height = 36;
		eye.Focusable = false;
		Children.Add(thought);
		Children.Add(action);
		style = Ui.Button("\uE790", "Btn.Icon", delegate
		{
			OpenLookMenu();
		});
		style.Width = 36;
		style.Height = 36;
		style.Focusable = false;
		style.ToolTip = "Colours, and first or third person for her thoughts and actions";
		Children.Add(eye);
		Children.Add(style);
		UpdateEye();
		System.Action recolor = delegate
		{
			thought.Foreground = MarkupColors.Thought;
			action.Foreground = MarkupColors.Action;
			Update();
		};
		Loaded += delegate
		{
			MarkupColors.Changed -= recolor;
			MarkupColors.Changed += recolor;
			recolor();
		};
		Unloaded += delegate
		{
			MarkupColors.Changed -= recolor;
		};
	}

	private void OpenLookMenu()
	{
		ContextMenu menu = new ContextMenu { PlacementTarget = style, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
		MenuItem first = new MenuItem { Header = "Her thoughts and actions in first person (\u201CI smile\u201D)", IsCheckable = true, IsChecked = ThoughtMarkup.Person == MarkupPerson.First };
		MenuItem third = new MenuItem { Header = "Her thoughts and actions in third person (\u201CVicki smiles\u201D)", IsCheckable = true, IsChecked = ThoughtMarkup.Person == MarkupPerson.Third };
		first.Click += delegate
		{
			ThoughtMarkup.Person = MarkupPerson.First;
			LookChanged?.Invoke();
		};
		third.Click += delegate
		{
			ThoughtMarkup.Person = MarkupPerson.Third;
			LookChanged?.Invoke();
		};
		menu.Items.Add(first);
		menu.Items.Add(third);
		menu.Items.Add(new Separator());
		menu.Items.Add(ColorMenu("Thought colour", MarkupKind.Thought));
		menu.Items.Add(ColorMenu("Action colour", MarkupKind.Action));
		MenuItem reset = new MenuItem { Header = "Restore blue and orange" };
		reset.Click += delegate
		{
			Recolor(MarkupColors.DefaultThought, MarkupColors.DefaultAction);
		};
		menu.Items.Add(reset);
		menu.IsOpen = true;
	}

	private MenuItem ColorMenu(string label, MarkupKind kind)
	{
		MenuItem parent = new MenuItem { Header = label };
		Color mine = kind == MarkupKind.Thought ? MarkupColors.ThoughtColor : MarkupColors.ActionColor;
		Color other = kind == MarkupKind.Thought ? MarkupColors.ActionColor : MarkupColors.ThoughtColor;
		foreach ((string name, Color color) in MarkupColors.Choices)
		{
			MenuItem item = new MenuItem
			{
				Header = new TextBlock { Text = name, Foreground = new SolidColorBrush(color), FontStyle = kind == MarkupKind.Thought ? FontStyles.Italic : FontStyles.Normal },
				IsCheckable = true,
				IsChecked = color == mine,
				IsEnabled = color != other,
				ToolTip = color == other ? "Already used for " + (kind == MarkupKind.Thought ? "actions" : "thoughts") : null
			};
			item.Click += delegate
			{
				if (kind == MarkupKind.Thought)
				{
					Recolor(color, MarkupColors.ActionColor);
				}
				else
				{
					Recolor(MarkupColors.ThoughtColor, color);
				}
			};
			parent.Items.Add(item);
		}
		return parent;
	}

	private void Recolor(Color thoughtColor, Color actionColor)
	{
		if (thoughtColor == actionColor)
		{
			return;
		}
		MarkupColors.Set(thoughtColor, actionColor);
		LookChanged?.Invoke();
	}

	public void Attach(MarkupComposer box)
	{
		composer = box;
		box.ModeChanged += delegate
		{
			Update();
		};
		box.PreviewKeyDown += (object sender, System.Windows.Input.KeyEventArgs e) =>
		{
			if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control)
			{
				return;
			}
			if (e.Key == System.Windows.Input.Key.T)
			{
				box.Toggle(MarkupKind.Thought);
				e.Handled = true;
			}
			else if (e.Key == System.Windows.Input.Key.J)
			{
				box.Toggle(MarkupKind.Action);
				e.Handled = true;
			}
		};
		Update();
	}

	/// <summary>Whether the eye shows the personas' thoughts.</summary>
	public bool ShowThoughts
	{
		get => showThoughts;
		set
		{
			showThoughts = value;
			UpdateEye();
		}
	}

	/// <summary>Hide the eye where there are no personas (it only affects their thoughts).</summary>
	public bool EyeVisible
	{
		set => eye.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
	}

	private Button Pill(string label, MarkupKind kind, string tip)
	{
		Button b = Ui.Button(new TextBlock
		{
			Text = label,
			FontFamily = Ui.FontUI,
			FontSize = 13,
			FontStyle = kind == MarkupKind.Thought ? FontStyles.Italic : FontStyles.Normal,
			VerticalAlignment = VerticalAlignment.Center
		}, "Btn.Ghost", delegate
		{
			composer?.Toggle(kind);
		});
		b.Foreground = MarkupColors.For(kind);
		b.SetValue(System.Windows.Controls.ToolTipService.ShowOnDisabledProperty, true);
		b.Height = 30;
		b.MinHeight = 0;
		b.Padding = new Thickness(10, 0, 10, 0);
		b.Margin = new Thickness(0, 0, 4, 0);
		b.BorderThickness = new Thickness(1);
		b.BorderBrush = Brushes.Transparent;
		b.Focusable = false;
		b.ToolTip = tip;
		return b;
	}

	private void Update()
	{
		MarkupKind mode = composer?.Mode ?? MarkupKind.Speech;
		Light(thought, mode == MarkupKind.Thought, MarkupColors.ThoughtSubtle, MarkupColors.Thought);
		thought.Foreground = MarkupColors.Thought;
		action.Foreground = MarkupColors.Action;
		Light(action, mode == MarkupKind.Action, MarkupColors.ActionSubtle, MarkupColors.Action);
	}

	private static void Light(Button b, bool on, Brush fill, Brush stroke)
	{
		b.Background = on ? fill : Brushes.Transparent;
		b.BorderBrush = on ? stroke : Brushes.Transparent;
	}

	private void UpdateEye()
	{
		eye.Content = showThoughts ? "\uE890" : "\uED1A";
		eye.ToolTip = showThoughts ? "Her thoughts are shown. Click to hide them." : "Her thoughts are hidden. Click to show them.";
	}
}
