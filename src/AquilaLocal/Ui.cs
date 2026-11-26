using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AquilaLocal;

/// <summary>
/// Shared design helpers: fonts, theme lookups and attached properties used by the control
/// templates in Theme/Controls.xaml (corner radius, placeholder text, hint lines).
/// </summary>
public static class Ui
{
	public static readonly FontFamily FontUI = new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Inter, Segoe UI");

	public static readonly FontFamily FontMono = new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#JetBrains Mono, Cascadia Mono, Consolas");

	public static readonly FontFamily FontIcon = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

	public static class Glyph
	{
		public const string Add = "";
		public const string Settings = "";
		public const string More = "";
		public const string Send = "";
		public const string Stop = "";
		public const string Copy = "";
		public const string Accept = "";
		public const string Delete = "";
		public const string Close = "";
		public const string ChevronDown = "";
		public const string ChevronRight = "";
		public const string Attach = "";
		public const string Microphone = "";
		public const string Volume = "";
		public const string Play = "";
		public const string Globe = "";
		public const string Folder = "";
		public const string PopOut = "";
		public const string Edit = "";
		public const string Search = "";
		public const string Save = "";
		public const string Export = "";
		public const string Person = "";
		public const string Library = "";
		public const string Back = "";
		public const string Forward = "";
		public const string Info = "";
		public const string Warning = "";
		public const string Shield = "";
		public const string Help = "";
		public const string Error = "";
		public const string Dock = "";
		public const string Audio = "";
		public const string VolumeLow = "";
		public const string VolumeHigh = "";
	}

	public static Style Style(string key)
	{
		return (Style)Application.Current.FindResource(key);
	}

	public static Brush Brush(string key)
	{
		return (Brush)Application.Current.FindResource(key);
	}

	public static Color Color(string key)
	{
		return (Color)Application.Current.FindResource("C." + key);
	}

	public static T Styled<T>(this T element, string key) where T : FrameworkElement
	{
		element.Style = Style(key);
		return element;
	}

	/// <summary>A TextBlock in one of the type styles (Type.Body, Type.Small, Type.Caption, …).</summary>
	public static TextBlock Text(string text, string style = "Type.Body")
	{
		return new TextBlock
		{
			Text = text,
			Style = Style(style),
			TextWrapping = TextWrapping.Wrap
		};
	}

	/// <summary>A small section heading in sentence case ("VOLUME" becomes "Volume").</summary>
	public static TextBlock Section(string text, double top = 18.0)
	{
		string value = text;
		if (value.Length > 1 && value == value.ToUpperInvariant())
		{
			value = value.Substring(0, 1) + value.Substring(1).ToLowerInvariant();
		}
		TextBlock textBlock = Text(value, "Type.BodyStrong");
		textBlock.Foreground = Brush("Text.Secondary");
		textBlock.FontSize = 13.0;
		textBlock.TextWrapping = TextWrapping.Wrap;
		textBlock.Margin = new Thickness(0.0, top, 0.0, 8.0);
		return textBlock;
	}

	/// <summary>A Segoe Fluent Icons glyph.</summary>
	public static TextBlock Icon(string glyph, double size = 16.0, string brush = "")
	{
		TextBlock textBlock = new TextBlock
		{
			Text = glyph,
			FontFamily = FontIcon,
			FontSize = size,
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		if (brush.Length > 0)
		{
			textBlock.Foreground = Brush(brush);
		}
		return textBlock;
	}

	/// <summary>Button content: an icon, then a label.</summary>
	public static object IconLabel(string glyph, string label)
	{
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		TextBlock textBlock = Icon(glyph);
		textBlock.Margin = new Thickness(0.0, 0.0, 8.0, 0.0);
		stackPanel.Children.Add(textBlock);
		stackPanel.Children.Add(new TextBlock
		{
			Text = label,
			VerticalAlignment = VerticalAlignment.Center
		});
		return stackPanel;
	}

	public static Button Button(object content, string style = "Btn.Secondary", RoutedEventHandler? click = null)
	{
		Button button = new Button
		{
			Content = content,
			Style = Style(style)
		};
		if (click != null)
		{
			button.Click += click;
		}
		return button;
	}

	public static Button IconButton(string glyph, string tooltip, RoutedEventHandler? click = null)
	{
		Button button = Button(Icon(glyph), "Btn.Icon", click);
		button.ToolTip = tooltip;
		AutomationNameFor(button, tooltip);
		return button;
	}

	private static void AutomationNameFor(DependencyObject element, string name)
	{
		System.Windows.Automation.AutomationProperties.SetName(element, name);
	}

	public static Border Card(UIElement child, double padding = 16.0)
	{
		return new Border
		{
			Style = Style("Card"),
			Padding = new Thickness(padding),
			Child = child
		};
	}

	/// <summary>A section label followed by a card holding the content.</summary>
	public static StackPanel Group(string label, UIElement content)
	{
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 20.0)
		};
		TextBlock textBlock = Text(label, "Type.Label");
		textBlock.Margin = new Thickness(4.0, 0.0, 0.0, 8.0);
		stackPanel.Children.Add(textBlock);
		stackPanel.Children.Add(Card(content));
		return stackPanel;
	}

	/// <summary>
	/// The shared secondary-window layout: title and description, a scrolling body, and a sticky
	/// footer with a status line on the left and buttons on the right (primary last).
	/// </summary>
	public static DockPanel DialogScaffold(string title, string description, UIElement body, TextBlock? status, params Button[] buttons)
	{
		DockPanel dockPanel = new DockPanel
		{
			LastChildFill = true
		};
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(24.0, 20.0, 24.0, 16.0)
		};
		stackPanel.Children.Add(Text(title, "Type.Title"));
		if (!string.IsNullOrWhiteSpace(description))
		{
			TextBlock textBlock = Text(description, "Type.Small");
			textBlock.Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
			textBlock.MaxWidth = 640.0;
			textBlock.HorizontalAlignment = HorizontalAlignment.Left;
			stackPanel.Children.Add(textBlock);
		}
		DockPanel.SetDock(stackPanel, Dock.Top);
		dockPanel.Children.Add(stackPanel);
		if (status != null || buttons.Length != 0)
		{
			Border footer = Footer(status, buttons);
			DockPanel.SetDock(footer, Dock.Bottom);
			dockPanel.Children.Add(footer);
		}
		if (body is ScrollViewer || body is Grid)
		{
			if (body is FrameworkElement frameworkElement && frameworkElement.Margin == default(Thickness))
			{
				frameworkElement.Margin = new Thickness(24.0, 0.0, 24.0, 20.0);
			}
			dockPanel.Children.Add(body);
		}
		else
		{
			dockPanel.Children.Add(new ScrollViewer
			{
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
				Padding = new Thickness(24.0, 0.0, 14.0, 24.0),
				Content = body
			});
		}
		return dockPanel;
	}

	public static Border Footer(TextBlock? status, params Button[] buttons)
	{
		DockPanel dockPanel = new DockPanel
		{
			LastChildFill = true
		};
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		for (int i = 0; i < buttons.Length; i++)
		{
			if (i > 0)
			{
				buttons[i].Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
			}
			stackPanel.Children.Add(buttons[i]);
		}
		DockPanel.SetDock(stackPanel, Dock.Right);
		dockPanel.Children.Add(stackPanel);
		if (status != null)
		{
			if (status.Parent is Panel panel)
			{
				panel.Children.Remove(status);
			}
			status.Style = Style("Type.Small");
			status.TextWrapping = TextWrapping.NoWrap;
			status.TextTrimming = TextTrimming.CharacterEllipsis;
			status.VerticalAlignment = VerticalAlignment.Center;
			status.Margin = new Thickness(0.0, 0.0, 16.0, 0.0);
			status.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding("Text")
			{
				RelativeSource = System.Windows.Data.RelativeSource.Self
			});
			dockPanel.Children.Add(status);
		}
		else
		{
			dockPanel.Children.Add(new Border());
		}
		return new Border
		{
			Background = Brush("Surface.1"),
			BorderBrush = Brush("Stroke.Subtle"),
			BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
			Padding = new Thickness(24.0, 12.0, 24.0, 12.0),
			Child = dockPanel
		};
	}

	/// <summary>A labelled form row: label, optional hint, then the control.</summary>
	public static StackPanel Field(string label, UIElement control, string hint = "", double top = 12.0)
	{
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(0.0, top, 0.0, 0.0)
		};
		TextBlock textBlock = Text(label, "Type.BodyStrong");
		textBlock.Margin = new Thickness(0.0, 0.0, 0.0, 6.0);
		stackPanel.Children.Add(textBlock);
		stackPanel.Children.Add(control);
		if (hint.Length > 0)
		{
			TextBlock textBlock2 = Text(hint, "Type.Small");
			textBlock2.Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
			stackPanel.Children.Add(textBlock2);
		}
		return stackPanel;
	}

	/// <summary>Slider with caption labels under each end.</summary>
	public static StackPanel SliderWithEnds(Slider slider, string left, string right)
	{
		StackPanel stackPanel = new StackPanel();
		stackPanel.Children.Add(slider);
		DockPanel dockPanel = new DockPanel
		{
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
		};
		TextBlock textBlock = Text(right, "Type.Caption");
		DockPanel.SetDock(textBlock, Dock.Right);
		dockPanel.Children.Add(textBlock);
		dockPanel.Children.Add(Text(left, "Type.Caption"));
		stackPanel.Children.Add(dockPanel);
		return stackPanel;
	}

	// ---- Attached properties used by templates ----

	public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached("Radius", typeof(CornerRadius), typeof(Ui), new FrameworkPropertyMetadata(new CornerRadius(8.0)));

	public static CornerRadius GetRadius(DependencyObject d)
	{
		return (CornerRadius)d.GetValue(RadiusProperty);
	}

	public static void SetRadius(DependencyObject d, CornerRadius value)
	{
		d.SetValue(RadiusProperty, value);
	}

	public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(Ui), new PropertyMetadata(null, PlaceholderChanged));

	public static string? GetPlaceholder(DependencyObject d)
	{
		return (string)d.GetValue(PlaceholderProperty);
	}

	public static void SetPlaceholder(DependencyObject d, string? value)
	{
		d.SetValue(PlaceholderProperty, value);
	}

	public static readonly DependencyProperty IsEmptyProperty = DependencyProperty.RegisterAttached("IsEmpty", typeof(bool), typeof(Ui), new PropertyMetadata(true));

	public static bool GetIsEmpty(DependencyObject d)
	{
		return (bool)d.GetValue(IsEmptyProperty);
	}

	public static void SetIsEmpty(DependencyObject d, bool value)
	{
		d.SetValue(IsEmptyProperty, value);
	}

	private static void PlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is PasswordBox passwordBox && e.OldValue == null)
		{
			SetIsEmpty(passwordBox, passwordBox.Password.Length == 0);
			passwordBox.PasswordChanged += delegate
			{
				SetIsEmpty(passwordBox, passwordBox.Password.Length == 0);
			};
		}
	}

	public static readonly DependencyProperty HintProperty = DependencyProperty.RegisterAttached("Hint", typeof(string), typeof(Ui), new PropertyMetadata(null));

	public static string? GetHint(DependencyObject d)
	{
		return (string)d.GetValue(HintProperty);
	}

	public static void SetHint(DependencyObject d, string? value)
	{
		d.SetValue(HintProperty, value);
	}

	public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached("Glyph", typeof(string), typeof(Ui), new PropertyMetadata(null));

	public static string? GetGlyph(DependencyObject d)
	{
		return (string)d.GetValue(GlyphProperty);
	}

	public static void SetGlyph(DependencyObject d, string? value)
	{
		d.SetValue(GlyphProperty, value);
	}
}
