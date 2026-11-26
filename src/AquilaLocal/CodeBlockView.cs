using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AquilaLocal;

/// <summary>
/// A code block inside a reply: a header with the language and a Copy button, then the code in
/// JetBrains Mono with syntax colours, scrolling sideways instead of wrapping.
/// </summary>
public sealed class CodeBlockView : Border
{
	private readonly TextBlock language;

	private readonly System.Windows.Controls.RichTextBox box;

	private readonly Button copy;

	private string code = "";

	private string lang = "";

	private bool colored;

	public string Source { get; private set; } = "";

	public CodeBlockView()
	{
		base.Background = Ui.Brush("Surface.Code");
		base.BorderBrush = Ui.Brush("Stroke.Subtle");
		base.BorderThickness = new Thickness(1.0);
		base.CornerRadius = new CornerRadius(12.0);
		base.Margin = new Thickness(0.0, 4.0, 0.0, 16.0);
		base.SnapsToDevicePixels = true;
		language = Ui.Text("code", "Type.Caption");
		language.VerticalAlignment = VerticalAlignment.Center;
		copy = Ui.Button(Ui.IconLabel(Ui.Glyph.Copy, "Copy"), "Btn.Ghost", delegate
		{
			CopyCode();
		});
		copy.MinHeight = 28.0;
		copy.Height = 28.0;
		copy.Padding = new Thickness(10.0, 0.0, 10.0, 0.0);
		copy.FontSize = 12.0;
		DockPanel dockPanel = new DockPanel();
		DockPanel.SetDock(copy, Dock.Right);
		dockPanel.Children.Add(copy);
		dockPanel.Children.Add(language);
		Border border = new Border
		{
			Height = 36.0,
			Background = Ui.Brush("Surface.1"),
			BorderBrush = Ui.Brush("Stroke.Subtle"),
			BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0),
			CornerRadius = new CornerRadius(11.0, 11.0, 0.0, 0.0),
			Padding = new Thickness(14.0, 0.0, 4.0, 0.0),
			Child = dockPanel
		};
		box = new System.Windows.Controls.RichTextBox
		{
			IsReadOnly = true,
			IsDocumentEnabled = false,
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			Padding = new Thickness(0.0),
			Foreground = Ui.Brush("Syn.Plain"),
			CaretBrush = Ui.Brush("Accent"),
			SelectionBrush = Ui.Brush("Selection"),
			SelectionOpacity = 0.6,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			FocusVisualStyle = null
		};
		box.Template = BareTemplate();
		box.PreviewMouseWheel += delegate(object _, MouseWheelEventArgs e)
		{
			// Let the chat scroll; a code block never swallows the wheel.
			e.Handled = true;
			MouseWheelEventArgs mouseWheelEventArgs = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
			{
				RoutedEvent = UIElement.MouseWheelEvent,
				Source = this
			};
			(base.Parent as UIElement)?.RaiseEvent(mouseWheelEventArgs);
		};
		DockPanel dockPanel2 = new DockPanel();
		DockPanel.SetDock(border, Dock.Top);
		dockPanel2.Children.Add(border);
		dockPanel2.Children.Add(box);
		base.Child = dockPanel2;
	}

	private static ControlTemplate BareTemplate()
	{
		// A RichTextBox template with no border, using the themed scroll viewer.
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
		frameworkElementFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(14.0, 12.0, 14.0, 10.0));
		frameworkElementFactory.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
		frameworkElementFactory.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
		frameworkElementFactory.SetValue(UIElement.FocusableProperty, false);
		return new ControlTemplate(typeof(System.Windows.Controls.RichTextBox))
		{
			VisualTree = frameworkElementFactory
		};
	}

	/// <summary>Shows the code. Colouring is skipped while a long block is still streaming.</summary>
	public void Set(string code, string lang, bool closed, bool final)
	{
		string text = (lang.Length == 0) ? "code" : lang;
		bool flag = code.Length <= 4000 || final || closed;
		if (code == this.code && lang == this.lang && (colored || !flag))
		{
			return;
		}
		this.code = code;
		this.lang = lang;
		Source = code;
		language.Text = text;
		FlowDocument flowDocument = new FlowDocument
		{
			PagePadding = new Thickness(0.0),
			FontFamily = Ui.FontMono,
			FontSize = 13.0,
			LineHeight = 20.0,
			PageWidth = 4000.0,
			Foreground = Ui.Brush("Syn.Plain")
		};
		Paragraph paragraph = new Paragraph
		{
			Margin = new Thickness(0.0)
		};
		if (flag)
		{
			foreach (var (text2, kind) in CodeColors.Tokenize(code, lang))
			{
				Run run = new Run(text2);
				if (kind != CodeColors.Kind.Plain)
				{
					run.Foreground = Ui.Brush(CodeColors.BrushKey(kind));
				}
				if (kind == CodeColors.Kind.Comment)
				{
					run.FontStyle = FontStyles.Italic;
				}
				paragraph.Inlines.Add(run);
			}
			colored = true;
		}
		else
		{
			paragraph.Inlines.Add(new Run(code));
			colored = false;
		}
		flowDocument.Blocks.Add(paragraph);
		box.Document = flowDocument;
		// Size the page to the longest line so short blocks do not scroll needlessly.
		box.Document.PageWidth = Math.Max(200.0, LongestLine(code) * 8.2 + 40.0);
	}

	private static int LongestLine(string code)
	{
		int num = 0;
		foreach (string item in code.Split('\n'))
		{
			num = Math.Max(num, item.Replace("\t", "    ").Length);
		}
		return num;
	}

	private void CopyCode()
	{
		try
		{
			Clipboard.SetText((code.Length > 0) ? code : " ");
		}
		catch (ExternalException)
		{
			return;
		}
		UiSounds.Play(UiSound.Copy);
		copy.Content = Ui.IconLabel(Ui.Glyph.Accept, "Copied");
		DispatcherTimer timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1.2)
		};
		timer.Tick += delegate
		{
			timer.Stop();
			copy.Content = Ui.IconLabel(Ui.Glyph.Copy, "Copy");
		};
		timer.Start();
	}
}
