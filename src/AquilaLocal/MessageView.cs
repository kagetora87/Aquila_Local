using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Emoji.Wpf;

namespace AquilaLocal;

public sealed class MessageView : Border
{
	private readonly Emoji.Wpf.RichTextBox body;

	private readonly System.Windows.Controls.TextBox thoughts;

	private readonly Expander thoughtsExpander;

	private readonly System.Windows.Controls.TextBox activity;

	private readonly Expander activityExpander;

	private readonly System.Windows.Controls.TextBlock meta;

	private string text = "";

	public MessageView(string role)
	{
		base.Margin = new Thickness(0.0, 0.0, 0.0, 18.0);
		base.Padding = new Thickness(17.0);
		base.CornerRadius = new CornerRadius(9.0);
		base.Background = Brush((role == "You") ? "#1C303B" : "#17222E");
		base.BorderBrush = Brush((role == "You") ? "#35515C" : "#2A3848");
		base.BorderThickness = new Thickness(1.0);
		StackPanel stackPanel = (StackPanel)(Child = new StackPanel());
		DockPanel dockPanel = new DockPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		Button button = new Button
		{
			Content = "Copy",
			Padding = new Thickness(8.0, 3.0, 8.0, 3.0),
			FontSize = 10.0,
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			Foreground = Brush("#97A8BB")
		};
		button.Click += delegate
		{
			Clipboard.SetText((text.Length > 0) ? text : " ");
		};
		DockPanel.SetDock(button, Dock.Right);
		dockPanel.Children.Add(button);
		meta = new System.Windows.Controls.TextBlock
		{
			Text = ((role == "You") ? "YOU" : "AQUILA"),
			Foreground = Brush((role == "You") ? "#A9BCCB" : "#80DEC5"),
			FontSize = 10.0,
			FontWeight = FontWeights.SemiBold,
			VerticalAlignment = VerticalAlignment.Center
		};
		dockPanel.Children.Add(meta);
		stackPanel.Children.Add(dockPanel);
		thoughts = PlainBox();
		thoughts.FontSize = 12.0;
		thoughts.Foreground = Brush("#A4B3C4");
		thoughtsExpander = new Expander
		{
			Header = "Thinking",
			Content = thoughts,
			Visibility = Visibility.Collapsed,
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		stackPanel.Children.Add(thoughtsExpander);
		body = new Emoji.Wpf.RichTextBox
		{
			IsReadOnly = true,
			IsDocumentEnabled = true,
			Background = Brushes.Transparent,
			Foreground = Brush("#E7EDF5"),
			BorderThickness = new Thickness(0.0),
			Padding = new Thickness(0.0),
			FontSize = 14.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
		};
		CommandManager.AddPreviewExecutedHandler(this, CopyMessageSelection);
		stackPanel.Children.Add(body);
		activity = PlainBox();
		activity.FontFamily = new FontFamily("Consolas");
		activity.FontSize = 11.0;
		activity.Foreground = Brush("#A4B3C4");
		activityExpander = new Expander
		{
			Header = "Activity",
			Content = activity,
			Visibility = Visibility.Collapsed,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		stackPanel.Children.Add(activityExpander);
	}

	private static System.Windows.Controls.TextBox PlainBox()
	{
		return new System.Windows.Controls.TextBox
		{
			IsReadOnly = true,
			TextWrapping = TextWrapping.Wrap,
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			Padding = new Thickness(0.0, 8.0, 0.0, 0.0)
		};
	}

	private void CopyMessageSelection(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Command == ApplicationCommands.Copy && !body.Selection.IsEmpty && (e.OriginalSource == body || body.IsKeyboardFocusWithin))
		{
			try
			{
				Clipboard.SetText(SelectedMessageText());
			}
			catch (ExternalException)
			{
			}
			e.Handled = true;
		}
	}

	private string SelectedMessageText()
	{
		TextPointer textPointer = body.Selection.Start;
		TextPointer end = body.Selection.End;
		StringBuilder stringBuilder = new StringBuilder();
		foreach (EmojiInline emojiInline in body.EmojiInlines)
		{
			if (emojiInline.ContentEnd.CompareTo(textPointer) > 0)
			{
				if (emojiInline.ContentStart.CompareTo(end) >= 0)
				{
					break;
				}
				if (emojiInline.ElementStart.CompareTo(textPointer) > 0)
				{
					stringBuilder.Append(new TextRange(textPointer, emojiInline.ElementStart).Text);
				}
				stringBuilder.Append(emojiInline.Text);
				textPointer = emojiInline.ElementEnd;
			}
		}
		if (textPointer.CompareTo(end) < 0)
		{
			stringBuilder.Append(new TextRange(textPointer, end).Text);
		}
		return stringBuilder.ToString();
	}

	public void Update(string content, string reasoning = "", IEnumerable<string>? logs = null, string status = "", bool renderMarkdown = true, string? copyText = null)
	{
		text = copyText ?? content;
		body.Document = (renderMarkdown ? Markdown(content) : new FlowDocument(new Paragraph(new Run(content)))
		{
			PagePadding = new Thickness(0.0),
			FontFamily = new FontFamily("Segoe UI"),
			FontSize = 14.0,
			LineHeight = 22.0
		});
		thoughts.Text = reasoning;
		thoughtsExpander.Visibility = ((reasoning.Length <= 0) ? Visibility.Collapsed : Visibility.Visible);
		if (logs != null)
		{
			activity.Text = string.Join("\n\n", logs);
			activityExpander.Visibility = ((activity.Text.Length <= 0) ? Visibility.Collapsed : Visibility.Visible);
		}
		if (status.Length > 0)
		{
			meta.Text = "AQUILA  ·  " + status;
		}
	}

	private static SolidColorBrush Brush(string color)
	{
		return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
	}

	private static FlowDocument Markdown(string text)
	{
		FlowDocument flowDocument = new FlowDocument
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
					AddCode(flowDocument, stringBuilder.ToString());
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
				flowDocument.Blocks.Add(new Paragraph
				{
					Margin = new Thickness(0.0, 3.0, 0.0, 3.0),
					FontSize = 4.0
				});
				continue;
			}
			Paragraph paragraph = new Paragraph
			{
				Margin = new Thickness(0.0, 0.0, 0.0, 5.0)
			};
			string input = text2;
			Match match = Regex.Match(input, "^(#{1,6})\\s+(.*)$");
			if (match.Success)
			{
				input = match.Groups[2].Value;
				paragraph.FontSize = ((match.Groups[1].Length <= 2) ? 19 : 16);
				paragraph.FontWeight = FontWeights.SemiBold;
				paragraph.Margin = new Thickness(0.0, 9.0, 0.0, 7.0);
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
					InlineCollection inlines = paragraph.Inlines;
					text4 = text3;
					inlines.Add(new Bold(new Run(text4.Substring(2, text4.Length - 2 - 2))));
					continue;
				}
				if (text3.StartsWith('`') && text3.EndsWith('`') && text3.Length >= 2)
				{
					InlineCollection inlines2 = paragraph.Inlines;
					text4 = text3;
					inlines2.Add(new Run(text4.Substring(1, text4.Length - 1 - 1))
					{
						FontFamily = new FontFamily("Consolas"),
						Foreground = Brush("#A8E5D5"),
						Background = Brush("#233641")
					});
					continue;
				}
				int num = 0;
				int num2;
				foreach (Match item in Regex.Matches(text3, "\\[([^\\]]+)\\]\\((?:<(https?://[^\\s>]+)>|(https?://[^\\s)]+))\\)", RegexOptions.None, TimeSpan.FromMilliseconds(100.0)))
				{
					InlineCollection inlines3 = paragraph.Inlines;
					num2 = num;
					inlines3.Add(new Run(text3.Substring(num2, item.Index - num2)));
					try
					{
						Uri uri = WebAccess.ValidateUrl(item.Groups[2].Success ? item.Groups[2].Value : item.Groups[3].Value);
						Hyperlink hyperlink = new Hyperlink(new Run(item.Groups[1].Value))
						{
							Foreground = Brush("#80DEC5"),
							ToolTip = uri.AbsoluteUri
						};
						hyperlink.Click += delegate
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
						paragraph.Inlines.Add(hyperlink);
					}
					catch (ArgumentException)
					{
						paragraph.Inlines.Add(new Run(item.Value));
					}
					num = item.Index + item.Length;
				}
				InlineCollection inlines4 = paragraph.Inlines;
				text4 = text3;
				num2 = num;
				inlines4.Add(new Run(text4.Substring(num2, text4.Length - num2)));
			}
			flowDocument.Blocks.Add(paragraph);
		}
		if (stringBuilder.Length > 0)
		{
			AddCode(flowDocument, stringBuilder.ToString());
		}
		return flowDocument;
	}

	private static void AddCode(FlowDocument doc, string text)
	{
		doc.Blocks.Add(new Paragraph(new Run(text.TrimEnd('\n')))
		{
			FontFamily = new FontFamily("Consolas"),
			FontSize = 12.0,
			Background = Brush("#101923"),
			Padding = new Thickness(12.0),
			Margin = new Thickness(0.0, 6.0, 0.0, 9.0),
			LineHeight = 19.0
		});
	}
}
