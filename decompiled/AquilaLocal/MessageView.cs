using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Emoji.Wpf;

namespace AquilaLocal;

public sealed class MessageView : Border
{
	private readonly RichTextBox body;

	private readonly TextBox thoughts;

	private readonly Expander thoughtsExpander;

	private readonly TextBox activity;

	private readonly Expander activityExpander;

	private readonly TextBlock meta;

	private string text = "";

	public MessageView(string role)
	{
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Expected O, but got Unknown
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0106: Expected O, but got Unknown
		//IL_0106: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0145: Unknown result type (might be due to invalid IL or missing references)
		//IL_0154: Unknown result type (might be due to invalid IL or missing references)
		//IL_015f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0169: Unknown result type (might be due to invalid IL or missing references)
		//IL_0173: Unknown result type (might be due to invalid IL or missing references)
		//IL_0184: Expected O, but got Unknown
		//IL_018c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0196: Expected O, but got Unknown
		//IL_01ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0202: Unknown result type (might be due to invalid IL or missing references)
		//IL_0203: Unknown result type (might be due to invalid IL or missing references)
		//IL_020d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0219: Expected O, but got Unknown
		//IL_026d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0272: Unknown result type (might be due to invalid IL or missing references)
		//IL_027d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0289: Unknown result type (might be due to invalid IL or missing references)
		//IL_0290: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c4: Expected O, but got Unknown
		//IL_02d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0305: Unknown result type (might be due to invalid IL or missing references)
		//IL_030f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0319: Unknown result type (might be due to invalid IL or missing references)
		//IL_0323: Unknown result type (might be due to invalid IL or missing references)
		//IL_032d: Unknown result type (might be due to invalid IL or missing references)
		//IL_033c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0343: Unknown result type (might be due to invalid IL or missing references)
		//IL_034f: Expected O, but got Unknown
		//IL_0357: Unknown result type (might be due to invalid IL or missing references)
		//IL_0361: Expected O, but got Unknown
		//IL_0389: Unknown result type (might be due to invalid IL or missing references)
		//IL_0393: Expected O, but got Unknown
		//IL_03bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_03cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0405: Unknown result type (might be due to invalid IL or missing references)
		//IL_0414: Expected O, but got Unknown
		((FrameworkElement)this).Margin = new Thickness(0.0, 0.0, 0.0, 18.0);
		((Border)this).Padding = new Thickness(17.0);
		((Border)this).CornerRadius = new CornerRadius(9.0);
		((Border)this).Background = (Brush)(object)Brush((role == "You") ? "#1C303B" : "#17222E");
		((Border)this).BorderBrush = (Brush)(object)Brush((role == "You") ? "#35515C" : "#2A3848");
		((Border)this).BorderThickness = new Thickness(1.0);
		StackPanel val2 = (StackPanel)(object)(((Decorator)this).Child = (UIElement)new StackPanel());
		DockPanel val3 = new DockPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		Button val4 = new Button
		{
			Content = "Copy",
			Padding = new Thickness(8.0, 3.0, 8.0, 3.0),
			FontSize = 10.0,
			Background = (Brush)(object)Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			Foreground = (Brush)(object)Brush("#97A8BB")
		};
		((ButtonBase)val4).Click += (RoutedEventHandler)delegate
		{
			Clipboard.SetText((text.Length > 0) ? text : " ");
		};
		DockPanel.SetDock((UIElement)(object)val4, (Dock)2);
		((Panel)val3).Children.Add((UIElement)(object)val4);
		meta = new TextBlock
		{
			Text = ((role == "You") ? "YOU" : "AQUILA"),
			Foreground = (Brush)(object)Brush((role == "You") ? "#A9BCCB" : "#80DEC5"),
			FontSize = 10.0,
			FontWeight = FontWeights.SemiBold,
			VerticalAlignment = (VerticalAlignment)1
		};
		((Panel)val3).Children.Add((UIElement)(object)meta);
		((Panel)val2).Children.Add((UIElement)(object)val3);
		thoughts = PlainBox();
		((Control)thoughts).FontSize = 12.0;
		((Control)thoughts).Foreground = (Brush)(object)Brush("#A4B3C4");
		thoughtsExpander = new Expander
		{
			Header = "Thinking",
			Content = thoughts,
			Visibility = (Visibility)2,
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		((Panel)val2).Children.Add((UIElement)(object)thoughtsExpander);
		body = new RichTextBox
		{
			IsReadOnly = true,
			IsDocumentEnabled = true,
			Background = (Brush)(object)Brushes.Transparent,
			Foreground = (Brush)(object)Brush("#E7EDF5"),
			BorderThickness = new Thickness(0.0),
			Padding = new Thickness(0.0),
			FontSize = 14.0,
			VerticalScrollBarVisibility = (ScrollBarVisibility)0,
			HorizontalScrollBarVisibility = (ScrollBarVisibility)0
		};
		CommandManager.AddPreviewExecutedHandler((UIElement)(object)this, new ExecutedRoutedEventHandler(CopyMessageSelection));
		((Panel)val2).Children.Add((UIElement)(object)body);
		activity = PlainBox();
		((Control)activity).FontFamily = new FontFamily("Consolas");
		((Control)activity).FontSize = 11.0;
		((Control)activity).Foreground = (Brush)(object)Brush("#A4B3C4");
		activityExpander = new Expander
		{
			Header = "Activity",
			Content = activity,
			Visibility = (Visibility)2,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		((Panel)val2).Children.Add((UIElement)(object)activityExpander);
	}

	private static TextBox PlainBox()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Expected O, but got Unknown
		return new TextBox
		{
			IsReadOnly = true,
			TextWrapping = (TextWrapping)2,
			Background = (Brush)(object)Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			Padding = new Thickness(0.0, 8.0, 0.0, 0.0)
		};
	}

	private void CopyMessageSelection(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Command == ApplicationCommands.Copy && !((TextRange)body.Selection).IsEmpty && (((RoutedEventArgs)e).OriginalSource == body || ((UIElement)body).IsKeyboardFocusWithin))
		{
			try
			{
				Clipboard.SetText(SelectedMessageText());
			}
			catch (ExternalException)
			{
			}
			((RoutedEventArgs)e).Handled = true;
		}
	}

	private string SelectedMessageText()
	{
		//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		TextPointer val = ((TextRange)body.Selection).Start;
		TextPointer end = ((TextRange)body.Selection).End;
		StringBuilder stringBuilder = new StringBuilder();
		foreach (EmojiInline emojiInline in body.EmojiInlines)
		{
			if (((TextElement)emojiInline).ContentEnd.CompareTo(val) > 0)
			{
				if (((TextElement)emojiInline).ContentStart.CompareTo(end) >= 0)
				{
					break;
				}
				if (((TextElement)emojiInline).ElementStart.CompareTo(val) > 0)
				{
					stringBuilder.Append(new TextRange(val, ((TextElement)emojiInline).ElementStart).Text);
				}
				stringBuilder.Append(emojiInline.Text);
				val = ((TextElement)emojiInline).ElementEnd;
			}
		}
		if (val.CompareTo(end) < 0)
		{
			stringBuilder.Append(new TextRange(val, end).Text);
		}
		return stringBuilder.ToString();
	}

	public void Update(string content, string reasoning = "", IEnumerable<string>? logs = null, string status = "", bool renderMarkdown = true, string? copyText = null)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Expected O, but got Unknown
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Expected O, but got Unknown
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Expected O, but got Unknown
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		text = copyText ?? content;
		((RichTextBox)body).Document = (FlowDocument)(renderMarkdown ? ((object)Markdown(content)) : ((object)new FlowDocument((Block)new Paragraph((Inline)new Run(content)))
		{
			PagePadding = new Thickness(0.0),
			FontFamily = new FontFamily("Segoe UI"),
			FontSize = 14.0,
			LineHeight = 22.0
		}));
		thoughts.Text = reasoning;
		((UIElement)thoughtsExpander).Visibility = (Visibility)((reasoning.Length <= 0) ? 2 : 0);
		if (logs != null)
		{
			activity.Text = string.Join("\n\n", logs);
			((UIElement)activityExpander).Visibility = (Visibility)((activity.Text.Length <= 0) ? 2 : 0);
		}
		if (status.Length > 0)
		{
			meta.Text = "AQUILA  ·  " + status;
		}
	}

	private static SolidColorBrush Brush(string color)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected O, but got Unknown
		return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
	}

	private static FlowDocument Markdown(string text)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Expected O, but got Unknown
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Expected O, but got Unknown
		//IL_041f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0429: Expected O, but got Unknown
		//IL_0355: Unknown result type (might be due to invalid IL or missing references)
		//IL_035f: Expected O, but got Unknown
		//IL_0481: Unknown result type (might be due to invalid IL or missing references)
		//IL_048b: Expected O, but got Unknown
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0141: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Expected O, but got Unknown
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0112: Expected O, but got Unknown
		//IL_03be: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c8: Expected O, but got Unknown
		//IL_03c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ec: Expected O, but got Unknown
		//IL_03f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0400: Expected O, but got Unknown
		//IL_01a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_026e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0278: Expected O, but got Unknown
		//IL_0273: Unknown result type (might be due to invalid IL or missing references)
		//IL_027d: Expected O, but got Unknown
		//IL_02c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d5: Expected O, but got Unknown
		//IL_02d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fa: Expected O, but got Unknown
		FlowDocument val = new FlowDocument
		{
			PagePadding = new Thickness(0.0),
			FontFamily = new FontFamily("Segoe UI"),
			FontSize = 14.0,
			LineHeight = 22.0
		};
		bool flag = false;
		StringBuilder stringBuilder = new StringBuilder();
		string[] array = text.Replace("\r\n", "\n").Split('\n');
		foreach (string text2 in array)
		{
			if (text2.TrimStart().StartsWith("```"))
			{
				if (flag)
				{
					AddCode(val, stringBuilder.ToString());
					stringBuilder.Clear();
				}
				flag = !flag;
				continue;
			}
			if (flag)
			{
				stringBuilder.AppendLine(text2);
				continue;
			}
			if (string.IsNullOrWhiteSpace(text2))
			{
				((TextElementCollection<Block>)(object)val.Blocks).Add((Block)new Paragraph
				{
					Margin = new Thickness(0.0, 3.0, 0.0, 3.0),
					FontSize = 4.0
				});
				continue;
			}
			Paragraph val2 = new Paragraph
			{
				Margin = new Thickness(0.0, 0.0, 0.0, 5.0)
			};
			string input = text2;
			Match match = Regex.Match(input, "^(#{1,6})\\s+(.*)$");
			if (match.Success)
			{
				input = match.Groups[2].Value;
				((TextElement)val2).FontSize = ((match.Groups[1].Length <= 2) ? 19 : 16);
				((TextElement)val2).FontWeight = FontWeights.SemiBold;
				((Block)val2).Margin = new Thickness(0.0, 9.0, 0.0, 7.0);
			}
			else if (Regex.IsMatch(input, "^\\s*[-*]\\s+"))
			{
				input = Regex.Replace(input, "^\\s*[-*]\\s+", "•  ");
			}
			string[] array2 = Regex.Split(input, "(\\*\\*.*?\\*\\*|`[^`]+`)", RegexOptions.None, TimeSpan.FromMilliseconds(100.0));
			foreach (string text3 in array2)
			{
				string text4;
				if (text3.StartsWith("**") && text3.EndsWith("**") && text3.Length >= 4)
				{
					InlineCollection inlines = val2.Inlines;
					text4 = text3;
					((TextElementCollection<Inline>)(object)inlines).Add((Inline)new Bold((Inline)new Run(text4.Substring(2, text4.Length - 2 - 2))));
					continue;
				}
				if (text3.StartsWith('`') && text3.EndsWith('`') && text3.Length >= 2)
				{
					InlineCollection inlines2 = val2.Inlines;
					text4 = text3;
					((TextElementCollection<Inline>)(object)inlines2).Add((Inline)new Run(text4.Substring(1, text4.Length - 1 - 1))
					{
						FontFamily = new FontFamily("Consolas"),
						Foreground = (Brush)(object)Brush("#A8E5D5"),
						Background = (Brush)(object)Brush("#233641")
					});
					continue;
				}
				int num = 0;
				int num2;
				foreach (Match item in Regex.Matches(text3, "\\[([^\\]]+)\\]\\((?:<(https?://[^\\s>]+)>|(https?://[^\\s)]+))\\)", RegexOptions.None, TimeSpan.FromMilliseconds(100.0)))
				{
					InlineCollection inlines3 = val2.Inlines;
					num2 = num;
					((TextElementCollection<Inline>)(object)inlines3).Add((Inline)new Run(text3.Substring(num2, item.Index - num2)));
					try
					{
						Uri uri = WebAccess.ValidateUrl(item.Groups[2].Success ? item.Groups[2].Value : item.Groups[3].Value);
						Hyperlink val3 = new Hyperlink((Inline)new Run(item.Groups[1].Value))
						{
							Foreground = (Brush)(object)Brush("#80DEC5"),
							ToolTip = uri.AbsoluteUri
						};
						val3.Click += (RoutedEventHandler)delegate
						{
							try
							{
								Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
								{
									UseShellExecute = true
								});
							}
							catch
							{
							}
						};
						((TextElementCollection<Inline>)(object)val2.Inlines).Add((Inline)(object)val3);
					}
					catch (ArgumentException)
					{
						((TextElementCollection<Inline>)(object)val2.Inlines).Add((Inline)new Run(item.Value));
					}
					num = item.Index + item.Length;
				}
				InlineCollection inlines4 = val2.Inlines;
				text4 = text3;
				num2 = num;
				((TextElementCollection<Inline>)(object)inlines4).Add((Inline)new Run(text4.Substring(num2, text4.Length - num2)));
			}
			((TextElementCollection<Block>)(object)val.Blocks).Add((Block)(object)val2);
		}
		if (stringBuilder.Length > 0)
		{
			AddCode(val, stringBuilder.ToString());
		}
		return val;
	}

	private static void AddCode(FlowDocument doc, string text)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Expected O, but got Unknown
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Expected O, but got Unknown
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Expected O, but got Unknown
		((TextElementCollection<Block>)(object)doc.Blocks).Add((Block)new Paragraph((Inline)new Run(text.TrimEnd('\n')))
		{
			FontFamily = new FontFamily("Consolas"),
			FontSize = 12.0,
			Background = (Brush)(object)Brush("#101923"),
			Padding = new Thickness(12.0),
			Margin = new Thickness(0.0, 6.0, 0.0, 9.0),
			LineHeight = 19.0
		});
	}
}
