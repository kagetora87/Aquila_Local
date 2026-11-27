using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Emoji.Wpf;
using TextBlock = System.Windows.Controls.TextBlock;
using RichTextBox = System.Windows.Controls.RichTextBox;

namespace AquilaLocal;

/// <summary>
/// One message in the conversation. Your messages are right-aligned bubbles; Aquila's replies sit
/// unboxed in the reading column with a small header, an optional "Thought about it" disclosure,
/// the reply (prose and code blocks as separate pieces), a Copy action and, when useful, Details.
/// </summary>
public sealed class MessageView : Border
{
	private readonly bool user;

	private readonly StackPanel bodyHost = new StackPanel();

	/// <summary>Attached pictures, shown in the user's bubble.</summary>
	private readonly WrapPanel pictures = new WrapPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0.0, 0.0, 0.0, 6.0) };

	/// <summary>Small clickable notices under a reply's header, such as "New memory created".</summary>
	private readonly WrapPanel notices = new WrapPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0.0, 0.0, 0.0, 8.0) };

	/// <summary>The persona's emoji reaction to this (user) message, overlapping the bubble's lower edge.</summary>
	private readonly Border reaction = new Border
	{
		Visibility = Visibility.Collapsed,
		HorizontalAlignment = HorizontalAlignment.Right,
		Margin = new Thickness(0.0, -10.0, 14.0, 0.0),
		Padding = new Thickness(7.0, 1.0, 7.0, 2.0),
		CornerRadius = new CornerRadius(11.0),
		BorderThickness = new Thickness(1.0)
	};

	private readonly System.Windows.Controls.TextBox? thoughts;

	private readonly Expander? thoughtsExpander;

	private readonly System.Windows.Controls.TextBox? activity;

	private readonly Expander? activityExpander;

	private readonly TextBlock? meta;

	private readonly StackPanel? waiting;

	private readonly StackPanel actions;

	private readonly Button copyButton;

	private string text = "";

	private bool generating;

	private bool hovering;

	/// <summary>Show "Details" (tool activity) on every reply, not just when something happened.</summary>
	public bool ShowTechnicalDetails { get; set; }

	/// <summary>Whether the personas' thoughts (blue) are shown. Your own thoughts are always shown.</summary>
	public static bool ShowPersonaThoughts { get; set; } = true;

	/// <summary>The prose pieces of the message, in order (for tests and copy handling).</summary>
	public IEnumerable<Emoji.Wpf.RichTextBox> ProseBoxes => bodyHost.Children.OfType<Emoji.Wpf.RichTextBox>();

	public MessageView(string role, bool animate = false, ImageSource? avatar = null)
	{
		user = role == "You";
		base.Background = Brushes.Transparent;
		base.Margin = new Thickness(0.0, 0.0, 0.0, user ? 24.0 : 28.0);
		copyButton = Ui.IconButton(Ui.Glyph.Copy, "Copy message", delegate
		{
			CopyAll();
		});
		// Re-assigning the tooltip closes it, so it can't linger after the pointer leaves.
		copyButton.MouseLeave += delegate
		{
			copyButton.ToolTip = "Copy message";
		};
		copyButton.Width = 28.0;
		copyButton.Height = 28.0;
		copyButton.FontSize = 14.0;
		actions = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(-6.0, 6.0, 0.0, 0.0)
		};
		actions.Children.Add(copyButton);
		CommandManager.AddPreviewExecutedHandler(this, CopyMessageSelection);
		if (user)
		{
			Border border = new Border
			{
				HorizontalAlignment = HorizontalAlignment.Right,
				MaxWidth = 620.0,
				Background = Ui.Brush("Surface.2"),
				CornerRadius = new CornerRadius(16.0, 16.0, 4.0, 16.0),
				Padding = new Thickness(14.0, 10.0, 14.0, 10.0),
				Child = new StackPanel { Children = { pictures, bodyHost } }
			};
			// The copy button sits beside the bubble on hover, so it reserves no space below it.
			actions.HorizontalAlignment = HorizontalAlignment.Right;
			actions.VerticalAlignment = VerticalAlignment.Bottom;
			actions.Margin = new Thickness(0.0, 0.0, 6.0, 0.0);
			actions.Opacity = 0.0;
			DockPanel dock = new DockPanel
			{
				HorizontalAlignment = HorizontalAlignment.Right,
				LastChildFill = true
			};
			DockPanel.SetDock(actions, Dock.Left);
			dock.Children.Add(actions);
			dock.Children.Add(border);
			reaction.Background = Ui.Brush("Surface.3");
			reaction.BorderBrush = Ui.Brush("Surface.1");
			base.Child = new StackPanel { Children = { dock, reaction } };
		}
		else
		{
			StackPanel stackPanel2 = new StackPanel();
			StackPanel stackPanel3 = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Height = 28.0,
				Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
			};
			UIElement element = (avatar != null) ? new Ellipse
			{
				Width = 28.0,
				Height = 28.0,
				VerticalAlignment = VerticalAlignment.Center,
				Fill = new ImageBrush(avatar) { Stretch = Stretch.UniformToFill }
			} : new Border
			{
				Width = 24.0,
				Height = 24.0,
				CornerRadius = new CornerRadius(7.0),
				Background = Ui.Brush("Accent.Subtle"),
				VerticalAlignment = VerticalAlignment.Center,
				Child = new TextBlock
				{
					Text = (role.Length > 0) ? role.Substring(0, 1).ToUpperInvariant() : "A",
					FontSize = 13.0,
					FontWeight = FontWeights.SemiBold,
					Foreground = Ui.Brush("Accent"),
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center
				}
			};
			stackPanel3.Children.Add(element);
			stackPanel3.Children.Add(new TextBlock
			{
				Text = role,
				FontSize = 13.0,
				FontWeight = FontWeights.SemiBold,
				Foreground = Ui.Brush("Text.Primary"),
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(10.0, 0.0, 0.0, 0.0)
			});
			meta = new TextBlock
			{
				Style = Ui.Style("Type.Caption"),
				VerticalAlignment = VerticalAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis
			};
			stackPanel3.Children.Add(meta);
			stackPanel2.Children.Add(stackPanel3);
			thoughts = PlainBox(13.0, "Text.Secondary");
			thoughtsExpander = new Expander
			{
				Style = Ui.Style("Expander.Quiet"),
				Header = "Thought about it",
				Content = new Border
				{
					BorderBrush = Ui.Brush("Stroke.Default"),
					BorderThickness = new Thickness(2.0, 0.0, 0.0, 0.0),
					Padding = new Thickness(12.0, 0.0, 0.0, 0.0),
					Child = thoughts
				},
				Visibility = Visibility.Collapsed,
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			};
			stackPanel2.Children.Add(thoughtsExpander);
			stackPanel2.Children.Add(notices);
			waiting = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Height = 24.0,
				Visibility = Visibility.Collapsed
			};
			for (int i = 0; i < 3; i++)
			{
				Ellipse ellipse = new Ellipse
				{
					Width = 6.0,
					Height = 6.0,
					Fill = Ui.Brush("Text.Tertiary"),
					Margin = new Thickness(0.0, 0.0, 6.0, 0.0),
					VerticalAlignment = VerticalAlignment.Center
				};
				waiting.Children.Add(ellipse);
			}
			stackPanel2.Children.Add(waiting);
			stackPanel2.Children.Add(bodyHost);
			actions.Opacity = 0.7;
			stackPanel2.Children.Add(actions);
			activity = PlainBox(12.0, "Text.Secondary");
			activity.FontFamily = Ui.FontMono;
			activityExpander = new Expander
			{
				Style = Ui.Style("Expander.Quiet"),
				Header = "Details",
				Content = new Border
				{
					Background = Ui.Brush("Surface.1"),
					CornerRadius = new CornerRadius(8.0),
					Padding = new Thickness(12.0),
					Child = activity
				},
				Visibility = Visibility.Collapsed,
				Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
			};
			stackPanel2.Children.Add(activityExpander);
			base.Child = stackPanel2;
		}
		base.MouseEnter += delegate
		{
			hovering = true;
			RefreshActions();
		};
		base.MouseLeave += delegate
		{
			hovering = false;
			RefreshActions();
		};
		if (animate)
		{
			base.Loaded += delegate
			{
				Motion.RiseIn(this);
			};
		}
	}

	/// <summary>Adds an extra button next to Copy (once per name), such as "Save as audio".</summary>
	public void AddAction(string glyph, string tooltip, Action click)
	{
		if (actions.Children.OfType<Button>().Any((Button b) => b.Tag as string == tooltip))
		{
			return;
		}
		Button button = Ui.IconButton(glyph, tooltip, delegate
		{
			click();
		});
		button.Tag = tooltip;
		button.Width = 28.0;
		button.Height = 28.0;
		button.FontSize = 14.0;
		actions.Children.Add(button);
	}

	/// <summary>Shows (or, with an empty string, hides) the persona's emoji reaction to this message.</summary>
	public void SetReaction(string emoji, string who)
	{
		if (string.IsNullOrEmpty(emoji))
		{
			reaction.Visibility = Visibility.Collapsed;
			return;
		}
		reaction.Child = new Emoji.Wpf.TextBlock { Text = emoji, FontSize = 15.0 };
		reaction.ToolTip = who + " reacted " + emoji;
		reaction.Visibility = Visibility.Visible;
	}

	/// <summary>Shows the attached pictures as thumbnails; clicking one opens it larger.</summary>
	private string shownPictureFiles = "";

	/// <summary>Pictures drawn for a reply (files). Click one to open it full size in Windows' picture viewer.</summary>
	public void ShowPictureFiles(IReadOnlyList<string> files)
	{
		string key = string.Join("|", files);
		if (key == shownPictureFiles)
		{
			return;
		}
		shownPictureFiles = key;
		pictures.Children.Clear();
		foreach (string file in files)
		{
			// A video is shown as its first still (saved beside it as .png) with a play mark; clicking plays it.
			bool video = IsVideo(file);
			string still = video ? System.IO.Path.ChangeExtension(file, ".png") : file;
			System.Windows.Media.Imaging.BitmapImage image;
			try
			{
				image = new System.Windows.Media.Imaging.BitmapImage();
				image.BeginInit();
				image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
				image.DecodePixelWidth = 640;
				image.UriSource = new Uri(still, UriKind.Absolute);
				image.EndInit();
				image.Freeze();
			}
			catch (Exception ex) when (ex is System.IO.IOException || ex is NotSupportedException || ex is UriFormatException || ex is UnauthorizedAccessException || ex is ArgumentException)
			{
				pictures.Children.Add(new TextBlock { Text = "Picture missing: " + System.IO.Path.GetFileName(file), Foreground = Ui.Brush("Text.Tertiary"), Margin = new Thickness(0, 0, 6, 6) });
				continue;
			}
			System.Windows.Controls.Image thumb = new System.Windows.Controls.Image { Source = image, Stretch = Stretch.Uniform, MaxWidth = 420.0, MaxHeight = 520.0, Cursor = Cursors.Hand, ToolTip = (video ? "Play the video · " : "Open full size · ") + System.IO.Path.GetFileName(file) };
			RenderOptions.SetBitmapScalingMode(thumb, BitmapScalingMode.HighQuality);
			string path = file;
			thumb.MouseLeftButtonUp += delegate
			{
				try
				{
					System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
				}
				catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is System.IO.IOException)
				{
				}
			};
			UIElement content = thumb;
			if (video)
			{
				Grid g = new Grid();
				g.Children.Add(thumb);
				Border play = new Border
				{
					Width = 56.0,
					Height = 56.0,
					CornerRadius = new CornerRadius(28.0),
					Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
					IsHitTestVisible = false,
					Child = new TextBlock { Text = "\u25B6", FontSize = 24.0, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4.0, 0.0, 0.0, 0.0) }
				};
				g.Children.Add(play);
				content = g;
			}
			pictures.Children.Add(new Border
			{
				CornerRadius = new CornerRadius(10.0),
				ClipToBounds = true,
				Margin = new Thickness(0.0, 0.0, 6.0, 6.0),
				Child = content
			});
		}
		pictures.Visibility = (pictures.Children.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
	}

	public static bool IsVideo(string file)
	{
		string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
		return ext == ".webm" || ext == ".avi" || ext == ".mp4";
	}

	public void ShowPictures(IEnumerable<Attachment> attachments)
	{
		pictures.Children.Clear();
		foreach (Attachment a in attachments.Where((Attachment x) => x.Kind == "image" && x.Base64.Length > 0 && !x.FromProfile))
		{
			System.Windows.Media.Imaging.BitmapImage image;
			try
			{
				image = Decode(a.Base64, 560);
			}
			catch (Exception ex) when (ex is FormatException || ex is NotSupportedException || ex is System.IO.IOException)
			{
				continue;
			}
			System.Windows.Controls.Image thumb = new System.Windows.Controls.Image { Source = image, Stretch = Stretch.Uniform, MaxWidth = 320.0, MaxHeight = 260.0, Cursor = Cursors.Hand, ToolTip = a.Name };
			RenderOptions.SetBitmapScalingMode(thumb, BitmapScalingMode.HighQuality);
			string base64 = a.Base64;
			string name = a.Name;
			thumb.MouseLeftButtonUp += delegate
			{
				ShowLarge(name, base64);
			};
			pictures.Children.Add(new Border
			{
				CornerRadius = new CornerRadius(10.0),
				ClipToBounds = true,
				Margin = new Thickness(0.0, 0.0, 6.0, 6.0),
				Child = thumb
			});
		}
		pictures.Visibility = (pictures.Children.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
	}

	private static System.Windows.Media.Imaging.BitmapImage Decode(string base64, int width)
	{
		System.Windows.Media.Imaging.BitmapImage image = new System.Windows.Media.Imaging.BitmapImage();
		image.BeginInit();
		image.StreamSource = new System.IO.MemoryStream(Convert.FromBase64String(base64));
		image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
		if (width > 0)
		{
			image.DecodePixelWidth = width;
		}
		image.EndInit();
		image.Freeze();
		return image;
	}

	private void ShowLarge(string name, string base64)
	{
		try
		{
			AquilaWindow viewer = new AquilaWindow
			{
				Title = name,
				Width = 1000.0,
				Height = 800.0,
				CloseOnEscape = true,
				WindowStartupLocation = WindowStartupLocation.CenterOwner,
				Owner = Window.GetWindow(this),
				Content = new System.Windows.Controls.Image { Source = Decode(base64, 0), Stretch = Stretch.Uniform, Margin = new Thickness(12.0) }
			};
			viewer.Show();
		}
		catch (Exception ex) when (ex is FormatException || ex is NotSupportedException || ex is InvalidOperationException)
		{
		}
	}

	/// <summary>Replaces the clickable notices under the reply header (empty hides them).</summary>
	public void SetNotices(IEnumerable<(string Text, Action Open)> items)
	{
		notices.Children.Clear();
		foreach ((string label, Action open) in items)
		{
			Button chip = Ui.Button(Ui.IconLabel("\uE734", label), "Btn.Chip", delegate
			{
				open();
			});
			chip.Margin = new Thickness(0.0, 0.0, 6.0, 0.0);
			chip.ToolTip = "Open it";
			notices.Children.Add(chip);
		}
		notices.Visibility = (notices.Children.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
	}

	private void RefreshActions()
	{
		double to = ((!generating) ? (hovering ? 1.0 : (user ? 0.0 : 0.7)) : 0.0);
		Motion.Animate(actions, UIElement.OpacityProperty, to, Motion.Fast, Motion.EaseOut);
		actions.IsHitTestVisible = !generating;
	}

	private static System.Windows.Controls.TextBox PlainBox(double size, string brush)
	{
		return new System.Windows.Controls.TextBox
		{
			Style = Ui.Style("Text.Plain"),
			FontSize = size,
			Foreground = Ui.Brush(brush)
		};
	}

	private static Emoji.Wpf.RichTextBox NewProse()
	{
		Emoji.Wpf.RichTextBox richTextBox = new Emoji.Wpf.RichTextBox
		{
			IsReadOnly = true,
			IsDocumentEnabled = true,
			Background = Brushes.Transparent,
			Foreground = Ui.Brush("Text.Primary"),
			BorderThickness = new Thickness(0.0),
			Padding = new Thickness(0.0),
			// The text view keeps a small caret gutter; pull it back so text lines up with the mark and code blocks.
			Margin = new Thickness(-3.0, 0.0, 0.0, 0.0),
			FontFamily = Ui.FontUI,
			FontSize = 15.0,
			SelectionBrush = Ui.Brush("Selection"),
			SelectionOpacity = 0.6,
			CaretBrush = Ui.Brush("Accent"),
			FocusVisualStyle = null,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
		};
		return richTextBox;
	}

	private void CopyMessageSelection(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Command != ApplicationCommands.Copy)
		{
			return;
		}
		Emoji.Wpf.RichTextBox? richTextBox = ProseBoxes.FirstOrDefault((Emoji.Wpf.RichTextBox b) => !b.Selection.IsEmpty && (e.OriginalSource == b || b.IsKeyboardFocusWithin));
		if (richTextBox != null)
		{
			try
			{
				Clipboard.SetText(SelectedText(richTextBox));
			}
			catch (ExternalException)
			{
			}
			e.Handled = true;
		}
	}

	private static string SelectedText(Emoji.Wpf.RichTextBox box)
	{
		TextPointer textPointer = box.Selection.Start;
		TextPointer end = box.Selection.End;
		StringBuilder stringBuilder = new StringBuilder();
		foreach (EmojiInline emojiInline in box.EmojiInlines)
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

	private void CopyAll()
	{
		try
		{
			Clipboard.SetText((text.Length > 0) ? text : " ");
		}
		catch (ExternalException)
		{
			return;
		}
		UiSounds.Play(UiSound.Copy);
		copyButton.Content = Ui.Icon(Ui.Glyph.Accept, 14.0, "Accent");
		DispatcherTimer timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1.2)
		};
		timer.Tick += delegate
		{
			timer.Stop();
			copyButton.Content = Ui.Icon(Ui.Glyph.Copy, 14.0);
		};
		timer.Start();
	}

	/// <summary>
	/// Updates the message. For replies, <paramref name="status"/> is the meta text after "Aquila"
	/// (already in plain words). Markdown is rendered while streaming; unchanged pieces are kept.
	/// </summary>
	public void Update(string content, string reasoning = "", IEnumerable<string>? logs = null, string status = "", bool renderMarkdown = true, string? copyText = null)
	{
		text = copyText ?? content;
		if (!user)
		{
			content = AquilaLocal.Companion.Sounds.SoundCues.ForDisplay(content);
		}
		if (AquilaLocal.Companion.ThoughtMarkup.HasMarkup(content))
		{
			if (!user && !ShowPersonaThoughts)
			{
				content = AquilaLocal.Companion.ThoughtMarkup.StripThoughts(content);
			}
			content = AquilaLocal.Companion.ThoughtMarkup.BalancePerLine(content);
		}
		if (user || !renderMarkdown)
		{
			Emoji.Wpf.RichTextBox richTextBox = ProseBoxes.FirstOrDefault() ?? AddProse();
			if (!(richTextBox.Tag is string text2) || text2 != content)
			{
				richTextBox.Tag = content;
				richTextBox.Document = MarkdownRenderer.Plain(content);
			}
		}
		else
		{
			RenderSegments(content, !generating);
		}
		if (user)
		{
			return;
		}
		thoughts.Text = reasoning;
		thoughtsExpander.Header = ((generating && content.Length == 0) ? "Thinking…" : "Thought about it");
		thoughtsExpander.Visibility = ((reasoning.Length <= 0) ? Visibility.Collapsed : Visibility.Visible);
		if (logs != null)
		{
			List<string> list = logs.ToList();
			activity.Text = string.Join("\n\n", list);
			bool flag = ShowTechnicalDetails || list.Any((string l) => l.Trim().Length > 0 && !Housekeeping(l));
			activityExpander.Visibility = ((activity.Text.Length <= 0 || !flag) ? Visibility.Collapsed : Visibility.Visible);
		}
		bool flag2 = generating && content.Length == 0 && reasoning.Length == 0;
		if (flag2 != (waiting.Visibility == Visibility.Visible))
		{
			waiting.Visibility = ((!flag2) ? Visibility.Collapsed : Visibility.Visible);
			for (int i = 0; i < waiting.Children.Count; i++)
			{
				if (flag2)
				{
					Motion.Pulse(waiting.Children[i], 0.25, TimeSpan.FromMilliseconds(900.0), TimeSpan.FromMilliseconds(150 * i));
				}
				else
				{
					Motion.StopPulse(waiting.Children[i]);
				}
			}
		}
		if (status.Length > 0 || !generating)
		{
			meta.Text = ((status.Length > 0) ? ("  ·  " + status) : "");
		}
	}

	/// <summary>Routine notes (memory, context trimming) that are not worth a "Details" line on their own.</summary>
	private static bool Housekeeping(string line)
	{
		string[] prefixes = new string[8] { "Memory", "Recalled memory", "Learned", "Using compacted context", "Omitted", "Generation stopped", "Response reached its token limit", "Error:" };
		return prefixes.Any((string p) => line.StartsWith(p, StringComparison.Ordinal));
	}

	/// <summary>Meta colour and the full technical line as a tooltip.</summary>
	public void SetMeta(string brushKey, string tooltip)
	{
		if (meta != null)
		{
			meta.Foreground = Ui.Brush(brushKey);
			meta.ToolTip = ((tooltip.Length > 0) ? tooltip : null);
		}
	}

	/// <summary>While generating, the action row is hidden and the waiting dots may show.</summary>
	public void SetGenerating(bool value)
	{
		generating = value;
		RefreshActions();
	}

	private Emoji.Wpf.RichTextBox AddProse()
	{
		Emoji.Wpf.RichTextBox richTextBox = NewProse();
		bodyHost.Children.Add(richTextBox);
		return richTextBox;
	}

	private void RenderSegments(string content, bool final)
	{
		List<MarkdownRenderer.Segment> list = MarkdownRenderer.Split(content);
		int i;
		for (i = 0; i < list.Count; i++)
		{
			MarkdownRenderer.Segment segment = list[i];
			UIElement? uIElement = ((i < bodyHost.Children.Count) ? bodyHost.Children[i] : null);
			if (segment.IsCode)
			{
				CodeBlockView? codeBlockView = uIElement as CodeBlockView;
				if (codeBlockView == null)
				{
					codeBlockView = new CodeBlockView();
					Replace(i, codeBlockView);
				}
				codeBlockView.Set(segment.Source, segment.Language, segment.Closed, final);
				continue;
			}
			Emoji.Wpf.RichTextBox? richTextBox = uIElement as Emoji.Wpf.RichTextBox;
			if (richTextBox == null)
			{
				richTextBox = NewProse();
				Replace(i, richTextBox);
			}
			if (!(richTextBox.Tag is string text) || text != segment.Source)
			{
				richTextBox.Tag = segment.Source;
				richTextBox.Document = MarkdownRenderer.Prose(segment.Source);
			}
			richTextBox.Margin = new Thickness(-3.0, 0.0, 0.0, (i < list.Count - 1) ? 12.0 : 0.0);
		}
		while (bodyHost.Children.Count > i)
		{
			bodyHost.Children.RemoveAt(bodyHost.Children.Count - 1);
		}
	}

	private void Replace(int index, UIElement element)
	{
		if (index < bodyHost.Children.Count)
		{
			bodyHost.Children.RemoveAt(index);
			bodyHost.Children.Insert(index, element);
		}
		else
		{
			bodyHost.Children.Add(element);
		}
	}
}
