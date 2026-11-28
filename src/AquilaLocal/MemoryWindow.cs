using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace AquilaLocal;

public sealed class MemoryWindow : AquilaWindow
{
	private sealed record ScopeChoice(string Label, string Workspace, bool Personal);

	private readonly MemoryStore store;

	private readonly string workspace;

	private readonly Action<MemorySource> openSource;

	private readonly ListBox entries = new ListBox
	{
		DisplayMemberPath = "Display"
	};

	private readonly TextBox search = new TextBox();

	private readonly TextBox topic = new TextBox();

	private readonly TextBox value = new TextBox();

	private readonly TextBox sources = new TextBox();

	private readonly ComboBox scope = new ComboBox();

	private readonly ComboBox kind = new ComboBox();

	private readonly CheckBox pinned = new CheckBox
	{
		Content = "Pin this memory"
	};

	private readonly CheckBox all = new CheckBox
	{
		Content = "Other projects"
	};

	private readonly CheckBox history = new CheckBox
	{
		Content = "Forgotten"
	};

	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Foreground = Ui.Brush("Text.Secondary")
	};

	private readonly TextBlock details = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Foreground = Ui.Brush("Text.Secondary")
	};

	private readonly StackPanel emptyState = new StackPanel
	{
		VerticalAlignment = VerticalAlignment.Center,
		HorizontalAlignment = HorizontalAlignment.Center,
		IsHitTestVisible = false
	};

	private MemoryEntry? selected;

	private bool refreshing;

	public MemoryWindow(MemoryStore store, string workspace, Action<MemorySource> openSource)
	{
		MemoryWindow memoryWindow = this;
		this.store = store;
		this.workspace = workspace;
		this.openSource = openSource;
		base.Title = "Aquila · Memories";
		base.Width = 1060.0;
		base.Height = 780.0;
		base.MinWidth = 900.0;
		base.MinHeight = 640.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.Height = Math.Min(base.Height, SystemParameters.WorkArea.Height - 30.0);
		DockPanel dockPanel = (DockPanel)(base.Content = new DockPanel());
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(24.0, 20.0, 24.0, 12.0)
		};
		DockPanel.SetDock(stackPanel, Dock.Top);
		dockPanel.Children.Add(stackPanel);
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Memories",
			Style = Ui.Style("Type.Title"),
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Things Aquila has learned about you. Personal memories apply to every chat; project memories only to the folder they came from.",
			TextWrapping = TextWrapping.Wrap,
			Style = Ui.Style("Type.Small")
		});
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 14.0, 0.0, 12.0)
		};
		stackPanel.Children.Add(stackPanel2);
		search.Width = 340.0;
		search.Margin = new Thickness(0.0, 0.0, 20.0, 0.0);
		Ui.SetPlaceholder(search, "Search memories");
		all.Margin = new Thickness(0.0, 0.0, 20.0, 0.0);
		all.VerticalAlignment = VerticalAlignment.Center;
		history.VerticalAlignment = VerticalAlignment.Center;
		SetAccessibleName(search, "Search memories");
		stackPanel2.Children.Add(search);
		stackPanel2.Children.Add(all);
		stackPanel2.Children.Add(history);
		Button closeButton = Ui.Button("Close", "Btn.Secondary", delegate
		{
			memoryWindow.Close();
		});
		closeButton.IsCancel = true;
		Border footer = Ui.Footer(status, closeButton);
		DockPanel.SetDock(footer, Dock.Bottom);
		dockPanel.Children.Add(footer);
		Grid grid = new Grid
		{
			Margin = new Thickness(24.0, 4.0, 24.0, 20.0),
			ColumnDefinitions = 
			{
				new ColumnDefinition
				{
					Width = new GridLength(0.95, GridUnitType.Star)
				},
				new ColumnDefinition
				{
					Width = new GridLength(1.05, GridUnitType.Star)
				}
			}
		};
		dockPanel.Children.Add(grid);
		SetAccessibleName(entries, "Memory entries");
		emptyState.Children.Add(Ui.Icon("\uE8F1", 28.0, "Text.Tertiary"));
		TextBlock emptyTitle = Ui.Text("Nothing remembered yet", "Type.BodyStrong");
		emptyTitle.Margin = new Thickness(0.0, 10.0, 0.0, 4.0);
		emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
		emptyState.Children.Add(emptyTitle);
		TextBlock emptyHint = Ui.Text("Aquila adds memories as you chat, or you can add one yourself.", "Type.Small");
		emptyHint.TextAlignment = TextAlignment.Center;
		emptyHint.MaxWidth = 260.0;
		emptyState.Children.Add(emptyHint);
		Grid listHost = new Grid();
		listHost.Children.Add(entries);
		listHost.Children.Add(emptyState);
		Border listCard = Ui.Card(listHost, 4.0);
		listCard.Margin = new Thickness(0.0, 0.0, 16.0, 0.0);
		grid.Children.Add(listCard);
		ScrollViewer scrollViewer = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
		};
		Border detailCard = Ui.Card(scrollViewer, 0.0);
		scrollViewer.Padding = new Thickness(16.0, 12.0, 8.0, 16.0);
		Grid.SetColumn(detailCard, 1);
		grid.Children.Add(detailCard);
		StackPanel stackPanel4 = (StackPanel)(scrollViewer.Content = new StackPanel());
		WrapPanel wrapPanel = new WrapPanel();
		stackPanel4.Children.Add(wrapPanel);
		Button(wrapPanel, "New memory", delegate
		{
			memoryWindow.entries.SelectedItem = null;
			memoryWindow.Edit(null);
		});
		Button(wrapPanel, "Save", Save).Style = Ui.Style("Btn.Primary");
		Button forgetButton = Button(wrapPanel, "Forget…", Forget);
		forgetButton.Style = Ui.Style("Btn.DangerGhost");
		forgetButton.ToolTip = "Clears this topic's saved values and quotes, including history, and stops Aquila relearning it automatically. Your original conversations are kept.";
		Label(stackPanel4, "TOPIC");
		stackPanel4.Children.Add(topic);
		SetAccessibleName(topic, "Memory topic");
		topic.MaxLength = 80;
		Label(stackPanel4, "REMEMBERED VALUE");
		stackPanel4.Children.Add(value);
		SetAccessibleName(value, "Memory value");
		value.Height = 90.0;
		value.AcceptsReturn = true;
		value.TextWrapping = TextWrapping.Wrap;
		value.MaxLength = 1000;
		value.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
		Label(stackPanel4, "SCOPE");
		scope.DisplayMemberPath = "Label";
		stackPanel4.Children.Add(scope);
		SetAccessibleName(scope, "Memory scope");
		Label(stackPanel4, "KIND");
		kind.ItemsSource = MemoryStore.Kinds;
		kind.ItemTemplate = SentenceCaseTemplate();
		stackPanel4.Children.Add(kind);
		SetAccessibleName(kind, "Memory kind");
		stackPanel4.Children.Add(pinned);
		stackPanel4.Children.Add(details);
		Label(stackPanel4, "SUPPORTING SOURCES");
		sources.IsReadOnly = true;
		sources.AcceptsReturn = true;
		sources.TextWrapping = TextWrapping.Wrap;
		sources.Height = 145.0;
		sources.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
		stackPanel4.Children.Add(sources);
		SetAccessibleName(sources, "Memory sources");
		WrapPanel wrapPanel2 = new WrapPanel();
		stackPanel4.Children.Add(wrapPanel2);
		Button(wrapPanel2, "Open latest source chat", delegate
		{
			MemorySource memorySource = memoryWindow.selected?.Sources.LastOrDefault();
			if (memorySource != null)
			{
				memoryWindow.Close();
				openSource(memorySource);
			}
			else
			{
				memoryWindow.status.Text = "This entry has no linked conversation.";
			}
		});
		Button(wrapPanel2, "Allow relearning", delegate
		{
			if (memoryWindow.selected?.State != "forgotten")
			{
				memoryWindow.status.Text = "Select a forgotten topic first.";
			}
			else
			{
				store.AllowRelearning(memoryWindow.selected.Id);
				memoryWindow.Refresh();
				memoryWindow.status.Text = "Automatic learning is allowed for this topic again.";
			}
		});
		search.TextChanged += delegate
		{
			memoryWindow.Refresh();
		};
		all.Click += delegate
		{
			memoryWindow.Refresh();
		};
		history.Click += delegate
		{
			memoryWindow.Refresh();
		};
		entries.SelectionChanged += delegate
		{
			if (!memoryWindow.refreshing)
			{
				memoryWindow.Edit(memoryWindow.entries.SelectedItem as MemoryEntry);
			}
		};
		Refresh();
		Edit(null);
	}

	/// <summary>Shows lower-case values such as "fact" as "Fact" without changing the stored value.</summary>
	private static DataTemplate SentenceCaseTemplate()
	{
		FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock));
		text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding
		{
			Converter = new SentenceCase()
		});
		return new DataTemplate
		{
			VisualTree = text
		};
	}

	private sealed class SentenceCase : System.Windows.Data.IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			string text = value?.ToString() ?? "";
			return (text.Length == 0) ? text : (char.ToUpperInvariant(text[0]) + text.Substring(1));
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			return System.Windows.Data.Binding.DoNothing;
		}
	}

	private static void SetAccessibleName(DependencyObject control, string name)
	{
		AutomationProperties.SetName(control, name);
	}

	private static void Label(Panel panel, string text)
	{
		panel.Children.Add(Ui.Section(text));
	}

	private Button Button(Panel panel, string text, Action action)
	{
		Button button = new Button
		{
			Content = text,
			Margin = new Thickness(0.0, 0.0, 8.0, 8.0)
		};
		panel.Children.Add(button);
		button.Click += delegate
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				status.Text = ex.Message;
			}
		};
		return button;
	}

	/// <summary>Opens on one memory (from a chat's "New memory" notice), even if it belongs to another project.</summary>
	public void Select(string id)
	{
		all.IsChecked = true;
		Refresh(id);
		if (entries.SelectedItem != null)
		{
			entries.ScrollIntoView(entries.SelectedItem);
		}
	}

	private void Refresh(string? selectId = null)
	{
		try
		{
			string scopeKey = MemoryStore.ScopeFor(workspace);
			string query = search.Text.Trim();
			List<MemoryEntry> list = store.Snapshot().Where(delegate(MemoryEntry e)
			{
				bool flag = all.IsChecked == true || e.Scope == "personal" || e.Scope == scopeKey;
				if (flag)
				{
					bool flag2 = history.IsChecked == true;
					if (!flag2)
					{
						string state = e.State;
						bool flag3 = ((state == "active" || state == "review") ? true : false);
						flag2 = flag3;
					}
					flag = flag2;
				}
				return flag && (query.Length == 0 || (e.Topic + " " + e.Value + " " + e.Scope).Contains(query, StringComparison.OrdinalIgnoreCase));
			}).ToList();
			refreshing = true;
			entries.ItemsSource = list;
			entries.SelectedItem = list.FirstOrDefault((MemoryEntry e) => e.Id == (selectId ?? selected?.Id));
			refreshing = false;
			if (entries.SelectedItem is MemoryEntry entry)
			{
				Edit(entry);
			}
			else
			{
				Edit(null);
			}
			emptyState.Visibility = (list.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
			status.Text = ((list.Count == 1) ? "1 memory" : $"{list.Count} memories") + ((store.RecoveryNotice != null) ? (" · " + store.RecoveryNotice) : "");
		}
		catch (Exception ex)
		{
			refreshing = false;
			status.Text = ex.Message;
		}
	}

	private void Edit(MemoryEntry? entry)
	{
		selected = entry;
		topic.Text = entry?.Topic ?? "";
		value.Text = entry?.Value ?? "";
		pinned.IsChecked = entry?.Pinned ?? false;
		List<ScopeChoice> list = new List<ScopeChoice>
		{
			new ScopeChoice("Personal · all chats", "", Personal: true)
		};
		if (!string.IsNullOrWhiteSpace(workspace))
		{
			list.Add(new ScopeChoice("Current project · " + Path.GetFileName(workspace), workspace, Personal: false));
		}
		if (entry != null && entry.Scope != "personal" && entry.Scope != MemoryStore.ScopeFor(workspace))
		{
			string scopeLabel = entry.ScopeLabel;
			string text = entry.Scope;
			list.Add(new ScopeChoice(scopeLabel, text.Substring(8, text.Length - 8), Personal: false));
		}
		scope.ItemsSource = list;
		scope.SelectedItem = ((entry == null) ? list.Last() : list.First((ScopeChoice c) => c.Personal == (entry.Scope == "personal") && (c.Personal || MemoryStore.ScopeFor(c.Workspace) == entry.Scope)));
		kind.SelectedItem = entry?.Kind ?? "fact";
		details.Text = ((entry == null) ? "New entries are saved as manual memories." : ($"{entry.State} · {entry.Origin} · updated {entry.Updated.LocalDateTime:g}\n{entry.Confirmations} supporting message(s) · last recalled {entry.LastUsed?.LocalDateTime.ToString("g") ?? "never"}" + ((entry.State == "review") ? "\nA pinned memory conflicts. Save / approve makes this value active and replaces the old value." : "")));
		TextBox textBox = sources;
		MemoryEntry? memoryEntry = entry;
		textBox.Text = ((memoryEntry != null && memoryEntry.Sources.Count > 0) ? string.Join("\n\n", entry.Sources.Select((MemorySource s) => $"{s.At.LocalDateTime:g} · chat {s.ChatId.Substring(0, Math.Min(8, s.ChatId.Length))}\n“{s.Quote}”")) : "No supporting chat quotes. Manual entries reflect your edits.");
	}

	private void Save()
	{
		if (scope.SelectedItem is ScopeChoice scopeChoice && kind.SelectedItem is string text)
		{
			MemoryEntry memoryEntry = store.SaveManual(selected?.Id, scopeChoice.Workspace, scopeChoice.Personal, topic.Text, value.Text, text, pinned.IsChecked == true);
			Refresh(memoryEntry.Id);
			status.Text = "Memory saved. It will be available on the next message.";
		}
	}

	private void Forget()
	{
		if (selected == null)
		{
			status.Text = "Select a saved entry first.";
			return;
		}
		store.Forget(selected.Id);
		Refresh();
		status.Text = "Topic forgotten. Enable history to inspect the relearning block.";
	}
}
