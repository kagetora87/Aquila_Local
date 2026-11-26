using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AquilaLocal.Companion;

namespace AquilaLocal;

/// <summary>The colours of thoughts (blue by default) and actions (orange by default), shared by the composer and the chat.</summary>
public static class MarkupColors
{
	public static readonly Color DefaultThought = Color.FromRgb(0x6C, 0xB4, 0xFF);

	public static readonly Color DefaultAction = Color.FromRgb(0xFF, 0xA1, 0x4A);

	/// <summary>Colours you can pick from (none of them the normal text colour, so speech is never mistaken for a thought).</summary>
	public static readonly (string Name, Color Color)[] Choices =
	{
		("Sky blue", DefaultThought),
		("Lavender", Color.FromRgb(0xB3, 0x9D, 0xFF)),
		("Teal", Color.FromRgb(0x5E, 0xD6, 0xC8)),
		("Green", Color.FromRgb(0x8B, 0xD6, 0x7A)),
		("Orange", DefaultAction),
		("Amber", Color.FromRgb(0xFF, 0xD1, 0x66)),
		("Coral", Color.FromRgb(0xFF, 0x7F, 0x6E)),
		("Pink", Color.FromRgb(0xFF, 0x8F, 0xC7)),
		("Silver", Color.FromRgb(0x9A, 0xA7, 0xB5))
	};

	private static readonly List<MarkupComposer> Live = new List<MarkupComposer>();

	public static Color ThoughtColor { get; private set; } = DefaultThought;

	public static Color ActionColor { get; private set; } = DefaultAction;

	public static SolidColorBrush Thought { get; private set; } = Frozen(DefaultThought);

	public static SolidColorBrush Action { get; private set; } = Frozen(DefaultAction);

	public static SolidColorBrush ThoughtSubtle { get; private set; } = Frozen(Color.FromArgb(0x33, DefaultThought.R, DefaultThought.G, DefaultThought.B));

	public static SolidColorBrush ActionSubtle { get; private set; } = Frozen(Color.FromArgb(0x33, DefaultAction.R, DefaultAction.G, DefaultAction.B));

	/// <summary>Changes the colours. Text already typed in a message box keeps being a thought or an action.</summary>
	public static void Set(Color thought, Color action)
	{
		if (thought == action)
		{
			throw new ArgumentException("Thoughts and actions need different colours.");
		}
		List<(MarkupComposer Box, string Text)> typed = Live.Select((MarkupComposer b) => (b, b.Text)).ToList();
		ThoughtColor = thought;
		ActionColor = action;
		Thought = Frozen(thought);
		Action = Frozen(action);
		ThoughtSubtle = Frozen(Color.FromArgb(0x33, thought.R, thought.G, thought.B));
		ActionSubtle = Frozen(Color.FromArgb(0x33, action.R, action.G, action.B));
		foreach ((MarkupComposer box, string text) in typed)
		{
			box.Text = text;
		}
		Changed?.Invoke();
	}

	/// <summary>Raised after the colours change.</summary>
	public static event System.Action? Changed;

	/// <summary>Reads "#RRGGBB" (falls back to the default when it isn't a colour).</summary>
	public static Color Parse(string? hex, Color fallback)
	{
		try
		{
			return string.IsNullOrWhiteSpace(hex) ? fallback : (Color)ColorConverter.ConvertFromString(hex);
		}
		catch (FormatException)
		{
			return fallback;
		}
	}

	public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

	internal static void Track(MarkupComposer box, bool live)
	{
		Live.Remove(box);
		if (live)
		{
			Live.Add(box);
		}
	}

	private static SolidColorBrush Frozen(Color c)
	{
		SolidColorBrush b = new SolidColorBrush(c);
		b.Freeze();
		return b;
	}

	public static Brush For(MarkupKind kind) => kind == MarkupKind.Thought ? Thought : Action;

	/// <summary>A span coloured (and for thoughts, italic) for a thought or an action.</summary>
	public static Span Span(MarkupKind kind)
	{
		Span span = new Span { Foreground = For(kind) };
		if (kind == MarkupKind.Thought)
		{
			span.FontStyle = FontStyles.Italic;
		}
		return span;
	}

	public static MarkupKind KindOf(object? foreground)
	{
		if (foreground is SolidColorBrush b)
		{
			if (b.Color == ThoughtColor)
			{
				return MarkupKind.Thought;
			}
			if (b.Color == ActionColor)
			{
				return MarkupKind.Action;
			}
		}
		return MarkupKind.Speech;
	}
}

/// <summary>
/// The message box. Works like a plain text box (Text, CaretIndex, Clear), but thoughts and actions show in their
/// colours while you type. Toggle(Thought) starts a thought at the caret; toggling again ends it. Text is the
/// message with &lt;thought&gt;/&lt;action&gt; tags.
/// </summary>
public sealed class MarkupComposer : RichTextBox
{
	private MarkupKind mode = MarkupKind.Speech;

	private bool placing;

	public event EventHandler? ModeChanged;

	public MarkupComposer()
	{
		Background = Brushes.Transparent;
		BorderThickness = new Thickness(0);
		AcceptsReturn = true;
		AcceptsTab = false;
		IsDocumentEnabled = false;
		FocusVisualStyle = null;
		VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
		HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
		SetResourceReference(ForegroundProperty, "Text.Primary");
		SetResourceReference(CaretBrushProperty, "Accent");
		SetResourceReference(SelectionBrushProperty, "Selection");
		SelectionOpacity = 0.6;
		FontFamily = Ui.FontUI;
		Document = NewDocument();
		// Only thoughts and actions carry formatting; the usual rich-text shortcuts (bold, centre, bullets…) do nothing.
		foreach (RoutedUICommand command in new[] { EditingCommands.ToggleBold, EditingCommands.ToggleItalic, EditingCommands.ToggleUnderline, EditingCommands.AlignCenter, EditingCommands.AlignJustify, EditingCommands.AlignLeft, EditingCommands.AlignRight, EditingCommands.IncreaseFontSize, EditingCommands.DecreaseFontSize, EditingCommands.ToggleBullets, EditingCommands.ToggleNumbering, EditingCommands.IncreaseIndentation, EditingCommands.DecreaseIndentation, EditingCommands.ToggleSubscript, EditingCommands.ToggleSuperscript })
		{
			CommandBindings.Add(new CommandBinding(command, delegate { }, (object s, CanExecuteRoutedEventArgs e) =>
			{
				e.CanExecute = false;
				e.Handled = true;
			}));
		}
		DataObject.AddPastingHandler(this, OnPasting);
		DataObject.AddCopyingHandler(this, OnCopying);
		PreviewTextInput += OnPreviewTextInput;
		SelectionChanged += delegate
		{
			if (!placing)
			{
				SetMode(KindAtCaret());
			}
		};
		TextChanged += delegate
		{
			Ui.SetIsEmpty(this, IsEmpty);
		};
		Loaded += delegate
		{
			MarkupColors.Track(this, live: true);
		};
		Unloaded += delegate
		{
			MarkupColors.Track(this, live: false);
		};
	}

	/// <summary>Whether what you type next is speech, a thought or an action.</summary>
	public MarkupKind Mode => mode;

	public bool IsEmpty => Text.Length == 0;

	/// <summary>The message, with thoughts and actions written as tags.</summary>
	public string Text
	{
		get
		{
			List<MarkupPart> parts = new List<MarkupPart>();
			bool first = true;
			foreach (Block block in Document.Blocks)
			{
				if (!first)
				{
					parts.Add(new MarkupPart(MarkupKind.Speech, "\n"));
				}
				first = false;
				if (block is Paragraph p)
				{
					Collect(p.Inlines, parts);
				}
			}
			return ThoughtMarkup.Write(Merge(parts));
		}
		set
		{
			placing = true;
			try
			{
				FlowDocument doc = NewDocument();
				doc.Blocks.Clear();
				Paragraph line = NewParagraph();
				foreach (MarkupPart part in ThoughtMarkup.Parse(value ?? ""))
				{
					string[] pieces = part.Text.Replace("\r\n", "\n").Split('\n');
					for (int i = 0; i < pieces.Length; i++)
					{
						if (i > 0)
						{
							doc.Blocks.Add(line);
							line = NewParagraph();
						}
						if (pieces[i].Length > 0)
						{
							line.Inlines.Add(NewRun(part.Kind, pieces[i]));
						}
					}
				}
				doc.Blocks.Add(line);
				Document = doc;
				CaretPosition = Document.ContentEnd;
			}
			finally
			{
				placing = false;
			}
			SetMode(MarkupKind.Speech);
		}
	}

	/// <summary>Only the end is ever set (put the caret after the text).</summary>
	public int CaretIndex
	{
		get => new TextRange(Document.ContentStart, CaretPosition).Text.Length;
		set => CaretPosition = Document.ContentEnd;
	}

	public void Clear()
	{
		Text = "";
	}

	/// <summary>Starts a thought or action at the caret, or ends it if one is already going.</summary>
	public void Toggle(MarkupKind kind)
	{
		Focus();
		SetMode(mode == kind ? MarkupKind.Speech : kind);
	}

	private void SetMode(MarkupKind next)
	{
		if (next == mode)
		{
			return;
		}
		mode = next;
		ModeChanged?.Invoke(this, EventArgs.Empty);
	}

	private MarkupKind KindAtCaret()
	{
		object value = Selection.GetPropertyValue(TextElement.ForegroundProperty);
		return value == DependencyProperty.UnsetValue ? mode : MarkupColors.KindOf(value);
	}

	/// <summary>Typed text always takes the current mode, even right after a thought ends or before one starts.</summary>
	private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
	{
		if (string.IsNullOrEmpty(e.Text) || e.Text == "\r" || e.Text == "\n" || IsReadOnly)
		{
			return;
		}
		if (Selection.IsEmpty && KindAtCaret() == mode)
		{
			return;
		}
		e.Handled = true;
		Insert(e.Text, mode);
	}

	private void Insert(string text, MarkupKind kind)
	{
		placing = true;
		try
		{
			BeginChange();
			if (!Selection.IsEmpty)
			{
				Selection.Text = "";
			}
			TextPointer at = CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
			Run run = NewRun(kind, text);
			if (at.Paragraph == null)
			{
				Paragraph p = NewParagraph();
				Document.Blocks.Add(p);
				at = p.ContentEnd;
			}
			Place(run, at);
			CaretPosition = run.ContentEnd;
			EndChange();
		}
		finally
		{
			placing = false;
		}
	}

	/// <summary>Puts the new run where the caret was: split the run under the caret and slot it in between.</summary>
	private static void Place(Run run, TextPointer at)
	{
		Paragraph p = at.Paragraph!;
		if (at.Parent is Run host && host.Parent == p)
		{
			string all = host.Text;
			int offset = new TextRange(host.ContentStart, at).Text.Length;
			if (offset <= 0)
			{
				p.Inlines.InsertBefore(host, run);
				return;
			}
			if (offset >= all.Length)
			{
				p.Inlines.InsertAfter(host, run);
				return;
			}
			Run tail = new Run(all.Substring(offset));
			CopyLook(host, tail);
			host.Text = all.Substring(0, offset);
			p.Inlines.InsertAfter(host, run);
			p.Inlines.InsertAfter(run, tail);
			return;
		}
		Inline? next = null;
		foreach (Inline inline in p.Inlines)
		{
			if (inline.ContentStart.CompareTo(at) >= 0)
			{
				next = inline;
				break;
			}
		}
		if (next != null)
		{
			p.Inlines.InsertBefore(next, run);
		}
		else
		{
			p.Inlines.Add(run);
		}
	}

	private static void CopyLook(Run from, Run to)
	{
		if (from.ReadLocalValue(TextElement.ForegroundProperty) != DependencyProperty.UnsetValue)
		{
			to.Foreground = from.Foreground;
		}
		if (from.ReadLocalValue(TextElement.FontStyleProperty) != DependencyProperty.UnsetValue)
		{
			to.FontStyle = from.FontStyle;
		}
	}

	private static Run NewRun(MarkupKind kind, string text)
	{
		Run run = new Run(text);
		if (kind != MarkupKind.Speech)
		{
			run.Foreground = MarkupColors.For(kind);
			if (kind == MarkupKind.Thought)
			{
				run.FontStyle = FontStyles.Italic;
			}
		}
		return run;
	}

	private static void Collect(InlineCollection inlines, List<MarkupPart> parts)
	{
		foreach (Inline inline in inlines)
		{
			switch (inline)
			{
			case Run run:
				if (run.Text.Length > 0)
				{
					parts.Add(new MarkupPart(MarkupColors.KindOf(run.Foreground), run.Text));
				}
				break;
			case LineBreak:
				parts.Add(new MarkupPart(MarkupKind.Speech, "\n"));
				break;
			case Span span:
				Collect(span.Inlines, parts);
				break;
			}
		}
	}

	private static List<MarkupPart> Merge(List<MarkupPart> parts)
	{
		List<MarkupPart> merged = new List<MarkupPart>();
		foreach (MarkupPart p in parts)
		{
			if (merged.Count > 0 && merged[^1].Kind == p.Kind)
			{
				merged[^1] = merged[^1] with { Text = merged[^1].Text + p.Text };
			}
			else
			{
				merged.Add(p);
			}
		}
		return merged;
	}

	private static FlowDocument NewDocument()
	{
		FlowDocument doc = new FlowDocument { PagePadding = new Thickness(0) };
		Style flat = new Style(typeof(Paragraph));
		flat.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0)));
		doc.Resources.Add(typeof(Paragraph), flat);
		doc.Blocks.Add(NewParagraph());
		return doc;
	}

	private static Paragraph NewParagraph() => new Paragraph { Margin = new Thickness(0) };

	/// <summary>Pasted text is plain text in the current mode; pasted tags become coloured thoughts and actions.</summary>
	private void OnPasting(object sender, DataObjectPastingEventArgs e)
	{
		string? text = e.SourceDataObject.GetData(DataFormats.UnicodeText, true) as string;
		e.CancelCommand();
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		text = text.Replace("\r\n", "\n");
		if (!ThoughtMarkup.HasMarkup(text))
		{
			Insert(text, mode);
			return;
		}
		foreach (MarkupPart part in ThoughtMarkup.Parse(text))
		{
			Insert(part.Text, part.Kind);
		}
		SetMode(KindAtCaret());
	}

	/// <summary>Copying keeps the tags, so a copied thought pastes back as a thought.</summary>
	private void OnCopying(object sender, DataObjectCopyingEventArgs e)
	{
		if (Selection.IsEmpty)
		{
			return;
		}
		List<MarkupPart> parts = new List<MarkupPart>();
		TextPointer? p = Selection.Start;
		while (p != null && p.CompareTo(Selection.End) < 0)
		{
			TextPointerContext context = p.GetPointerContext(LogicalDirection.Forward);
			if (context == TextPointerContext.Text)
			{
				string run = p.GetTextInRun(LogicalDirection.Forward);
				int room = p.GetOffsetToPosition(Selection.End);
				if (run.Length > room)
				{
					run = run.Substring(0, room);
				}
				parts.Add(new MarkupPart(MarkupColors.KindOf(p.Parent is TextElement el ? el.Foreground : null), run));
				p = p.GetPositionAtOffset(run.Length, LogicalDirection.Forward);
				continue;
			}
			if (context == TextPointerContext.ElementEnd && p.Parent is Paragraph && parts.Count > 0)
			{
				parts.Add(new MarkupPart(MarkupKind.Speech, "\n"));
			}
			else if (context == TextPointerContext.EmbeddedElement || (context == TextPointerContext.ElementStart && p.GetAdjacentElement(LogicalDirection.Forward) is LineBreak))
			{
				parts.Add(new MarkupPart(MarkupKind.Speech, "\n"));
			}
			p = p.GetNextContextPosition(LogicalDirection.Forward);
		}
		string tagged = ThoughtMarkup.Write(Merge(parts)).TrimEnd('\n').Replace("\n", Environment.NewLine);
		if (ThoughtMarkup.HasMarkup(tagged))
		{
			e.DataObject.SetData(DataFormats.UnicodeText, tagged);
			e.DataObject.SetData(DataFormats.Text, tagged);
		}
	}
}
