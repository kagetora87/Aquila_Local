using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AquilaLocal;

public enum DialogKind
{
	Question,
	Danger,
	Error
}

/// <summary>
/// Dark, themed replacement for MessageBox. Buttons are verbs ("Delete chat", "Cancel"),
/// never Yes/No. Esc cancels; Danger dialogs start with focus on Cancel.
/// </summary>
public sealed class AquilaDialog : AquilaWindow
{
	private bool confirmed;

	private AquilaDialog(Window? owner, string title, string message, string confirmLabel, string? cancelLabel, DialogKind kind)
	{
		base.Title = "Aquila Local";
		base.Width = 460.0;
		base.SizeToContent = SizeToContent.Height;
		base.ResizeMode = ResizeMode.NoResize;
		base.ShowInTaskbar = owner == null;
		base.WindowStartupLocation = ((owner != null) ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen);
		base.CloseOnEscape = true;
		if (owner != null && owner.IsLoaded)
		{
			base.Owner = owner;
		}
		string glyph = kind switch
		{
			DialogKind.Danger => Ui.Glyph.Delete,
			DialogKind.Error => Ui.Glyph.Error,
			_ => Ui.Glyph.Help,
		};
		bool flag = kind != DialogKind.Question;
		Border border = new Border
		{
			Width = 36.0,
			Height = 36.0,
			CornerRadius = new CornerRadius(18.0),
			Background = Ui.Brush(flag ? "Danger.Subtle" : "Accent.Subtle"),
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0.0, 0.0, 16.0, 0.0),
			Child = Ui.Icon(glyph, 16.0, flag ? "Danger" : "Accent")
		};
		StackPanel stackPanel = new StackPanel();
		stackPanel.Children.Add(new TextBlock
		{
			Text = title,
			Style = Ui.Style("Type.Subtitle"),
			TextWrapping = TextWrapping.Wrap
		});
		string[] array = message.Replace("\r", "").Split(new string[1] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length; i++)
		{
			stackPanel.Children.Add(new TextBlock
			{
				Text = array[i].Trim(),
				Style = Ui.Style((i == 0) ? "Type.Body" : "Type.Small"),
				Foreground = Ui.Brush((i == 0) ? "Text.Primary" : "Text.Secondary"),
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, (i == 0) ? 8.0 : 8.0, 0.0, 0.0)
			});
		}
		DockPanel dockPanel = new DockPanel
		{
			Margin = new Thickness(24.0)
		};
		DockPanel.SetDock(border, Dock.Left);
		dockPanel.Children.Add(border);
		dockPanel.Children.Add(stackPanel);
		Button confirm = Ui.Button(confirmLabel, kind switch
		{
			DialogKind.Danger => "Btn.Danger",
			_ => "Btn.Primary",
		}, delegate
		{
			confirmed = true;
			Close();
		});
		confirm.IsDefault = true;
		Button? cancel = null;
		Border footer;
		if (cancelLabel != null)
		{
			cancel = Ui.Button(cancelLabel, "Btn.Ghost", delegate
			{
				Close();
			});
			cancel.IsCancel = true;
			footer = Ui.Footer(null, cancel, confirm);
		}
		else
		{
			footer = Ui.Footer(null, confirm);
		}
		footer.Padding = new Thickness(16.0, 12.0, 16.0, 12.0);
		DockPanel dockPanel2 = new DockPanel();
		DockPanel.SetDock(footer, Dock.Bottom);
		dockPanel2.Children.Add(footer);
		dockPanel2.Children.Add(dockPanel);
		base.Content = new Border
		{
			BorderBrush = Ui.Brush("Stroke.Subtle"),
			BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
			Child = dockPanel2
		};
		base.Loaded += delegate
		{
			((kind == DialogKind.Danger && cancel != null) ? cancel : confirm).Focus();
		};
		if (kind == DialogKind.Error)
		{
			UiSounds.Play(UiSound.Notice);
		}
	}

	/// <summary>Ask a question; true when the user chose the confirm button.</summary>
	public static bool Confirm(Window? owner, string title, string message, string confirmLabel, string cancelLabel = "Cancel", DialogKind kind = DialogKind.Question)
	{
		AquilaDialog aquilaDialog = new AquilaDialog(owner, title, message, confirmLabel, cancelLabel, kind);
		aquilaDialog.ShowDialog();
		return aquilaDialog.confirmed;
	}

	public static void Alert(Window? owner, string title, string message, string okLabel = "OK")
	{
		new AquilaDialog(owner, title, message, okLabel, null, DialogKind.Error).ShowDialog();
	}
}
