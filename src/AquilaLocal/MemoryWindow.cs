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
		Content = "Show other projects"
	};

	private readonly CheckBox history = new CheckBox
	{
		Content = "Show history / forgotten"
	};

	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Foreground = Brushes.LightGray
	};

	private readonly TextBlock details = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Foreground = Brushes.LightGray
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
		base.Background = new SolidColorBrush(Color.FromRgb(16, 23, 32));
		base.Foreground = (Brush)Application.Current.Resources["Ink"];
		base.FontFamily = new FontFamily("Segoe UI");
		base.FontSize = 13.0;
		DockPanel dockPanel = (DockPanel)(base.Content = new DockPanel
		{
			Margin = new Thickness(22.0)
		});
		StackPanel stackPanel = new StackPanel();
		DockPanel.SetDock(stackPanel, Dock.Top);
		dockPanel.Children.Add(stackPanel);
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Memories",
			FontSize = 24.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Personal memories follow you across chats. Project memories are recalled only in the same folder. Pinned entries need your approval before a correction replaces them.",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brushes.LightGray
		});
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 14.0, 0.0, 12.0)
		};
		stackPanel.Children.Add(stackPanel2);
		stackPanel2.Children.Add(new TextBlock
		{
			Text = "Search",
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
		});
		search.Width = 270.0;
		search.Margin = new Thickness(0.0, 0.0, 16.0, 0.0);
		SetAccessibleName(search, "Search memories");
		stackPanel2.Children.Add(search);
		stackPanel2.Children.Add(all);
		stackPanel2.Children.Add(history);
		StackPanel stackPanel3 = new StackPanel
		{
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		DockPanel.SetDock(stackPanel3, Dock.Bottom);
		dockPanel.Children.Add(stackPanel3);
		stackPanel3.Children.Add(status);
		stackPanel3.Children.Add(new TextBlock
		{
			Text = "Forget clears this topic's saved values and quotes, including history, and blocks automatic relearning of that topic. Original conversations and backups remain. Topic names are retained for the block.",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brushes.LightGray,
			FontSize = 11.0,
			Margin = new Thickness(0.0, 9.0, 0.0, 0.0)
		});
		Grid grid = new Grid
		{
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
		entries.Margin = new Thickness(0.0, 0.0, 18.0, 0.0);
		SetAccessibleName(entries, "Memory entries");
		grid.Children.Add(entries);
		ScrollViewer scrollViewer = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
		};
		Grid.SetColumn(scrollViewer, 1);
		grid.Children.Add(scrollViewer);
		StackPanel stackPanel4 = (StackPanel)(scrollViewer.Content = new StackPanel());
		WrapPanel wrapPanel = new WrapPanel();
		stackPanel4.Children.Add(wrapPanel);
		Button(wrapPanel, "New memory", delegate
		{
			memoryWindow.entries.SelectedItem = null;
			memoryWindow.Edit(null);
		});
		Button(wrapPanel, "Save / approve", Save);
		Button(wrapPanel, "Forget topic", Forget);
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

	private static void SetAccessibleName(DependencyObject control, string name)
	{
		AutomationProperties.SetName(control, name);
	}

	private static void Label(Panel panel, string text)
	{
		panel.Children.Add(new TextBlock
		{
			Text = text,
			FontSize = 10.0,
			Foreground = Brushes.LightGray,
			Margin = new Thickness(0.0, 14.0, 0.0, 7.0)
		});
	}

	private void Button(Panel panel, string text, Action action)
	{
		Button button = new Button
		{
			Content = text,
			Margin = new Thickness(0.0, 0.0, 8.0, 7.0),
			Padding = new Thickness(10.0, 7.0, 10.0, 7.0)
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
			status.Text = $"{list.Count} entries shown · " + (store.RecoveryNotice ?? "Learning and recall controls are in the main Settings panel.");
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
