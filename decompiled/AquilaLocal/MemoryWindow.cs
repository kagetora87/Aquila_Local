using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AquilaLocal;

public sealed class MemoryWindow : Window
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
		TextWrapping = (TextWrapping)2,
		Foreground = (Brush)(object)Brushes.LightGray
	};

	private readonly TextBlock details = new TextBlock
	{
		TextWrapping = (TextWrapping)2,
		Foreground = (Brush)(object)Brushes.LightGray
	};

	private MemoryEntry? selected;

	private bool refreshing;

	public MemoryWindow(MemoryStore store, string workspace, Action<MemorySource> openSource)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Expected O, but got Unknown
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Expected O, but got Unknown
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Expected O, but got Unknown
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Expected O, but got Unknown
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Expected O, but got Unknown
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Expected O, but got Unknown
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Expected O, but got Unknown
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Expected O, but got Unknown
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Expected O, but got Unknown
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Expected O, but got Unknown
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Expected O, but got Unknown
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d4: Expected O, but got Unknown
		//IL_0169: Unknown result type (might be due to invalid IL or missing references)
		//IL_016e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0192: Unknown result type (might be due to invalid IL or missing references)
		//IL_0197: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a1: Expected O, but got Unknown
		//IL_01b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c0: Expected O, but got Unknown
		//IL_01c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d0: Expected O, but got Unknown
		//IL_01df: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f9: Expected O, but got Unknown
		//IL_0200: Unknown result type (might be due to invalid IL or missing references)
		//IL_0206: Expected O, but got Unknown
		//IL_0220: Unknown result type (might be due to invalid IL or missing references)
		//IL_0225: Unknown result type (might be due to invalid IL or missing references)
		//IL_0230: Unknown result type (might be due to invalid IL or missing references)
		//IL_023f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0264: Unknown result type (might be due to invalid IL or missing references)
		//IL_0273: Expected O, but got Unknown
		//IL_027a: Unknown result type (might be due to invalid IL or missing references)
		//IL_027f: Unknown result type (might be due to invalid IL or missing references)
		//IL_028a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0291: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a1: Expected O, but got Unknown
		//IL_02a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02de: Expected O, but got Unknown
		//IL_02f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0301: Unknown result type (might be due to invalid IL or missing references)
		//IL_0308: Unknown result type (might be due to invalid IL or missing references)
		//IL_032d: Unknown result type (might be due to invalid IL or missing references)
		//IL_033c: Expected O, but got Unknown
		//IL_037b: Unknown result type (might be due to invalid IL or missing references)
		//IL_03cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_03f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0401: Expected O, but got Unknown
		//IL_0431: Unknown result type (might be due to invalid IL or missing references)
		//IL_0436: Unknown result type (might be due to invalid IL or missing references)
		//IL_0441: Unknown result type (might be due to invalid IL or missing references)
		//IL_0448: Unknown result type (might be due to invalid IL or missing references)
		//IL_0453: Unknown result type (might be due to invalid IL or missing references)
		//IL_0462: Unknown result type (might be due to invalid IL or missing references)
		//IL_0487: Unknown result type (might be due to invalid IL or missing references)
		//IL_0496: Expected O, but got Unknown
		//IL_0497: Unknown result type (might be due to invalid IL or missing references)
		//IL_049e: Expected O, but got Unknown
		//IL_04a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_04aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_04b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_04c4: Expected O, but got Unknown
		//IL_04cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_04d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_04db: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ea: Expected O, but got Unknown
		//IL_0522: Unknown result type (might be due to invalid IL or missing references)
		//IL_054f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0554: Unknown result type (might be due to invalid IL or missing references)
		//IL_055b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0564: Expected O, but got Unknown
		//IL_057b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0582: Expected O, but got Unknown
		//IL_058b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0592: Expected O, but got Unknown
		//IL_07b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_07bd: Expected O, but got Unknown
		//IL_080b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0815: Expected O, but got Unknown
		//IL_0822: Unknown result type (might be due to invalid IL or missing references)
		//IL_082c: Expected O, but got Unknown
		//IL_0839: Unknown result type (might be due to invalid IL or missing references)
		//IL_0843: Expected O, but got Unknown
		//IL_0850: Unknown result type (might be due to invalid IL or missing references)
		//IL_085a: Expected O, but got Unknown
		Action<MemorySource> openSource2 = openSource;
		MemoryStore store2 = store;
		((Window)this)._002Ector();
		MemoryWindow memoryWindow = this;
		this.store = store2;
		this.workspace = workspace;
		this.openSource = openSource2;
		((Window)this).Title = "Aquila · Memories";
		((FrameworkElement)this).Width = 1060.0;
		((FrameworkElement)this).Height = 780.0;
		((FrameworkElement)this).MinWidth = 900.0;
		((FrameworkElement)this).MinHeight = 640.0;
		((Window)this).WindowStartupLocation = (WindowStartupLocation)2;
		double height = ((FrameworkElement)this).Height;
		Rect workArea = SystemParameters.WorkArea;
		((FrameworkElement)this).Height = Math.Min(height, ((Rect)(ref workArea)).Height - 30.0);
		((Control)this).Background = (Brush)new SolidColorBrush(Color.FromRgb((byte)16, (byte)23, (byte)32));
		((Control)this).Foreground = (Brush)Application.Current.Resources[(object)"Ink"];
		((Control)this).FontFamily = new FontFamily("Segoe UI");
		((Control)this).FontSize = 13.0;
		DockPanel val = (DockPanel)(((ContentControl)this).Content = (object)new DockPanel
		{
			Margin = new Thickness(22.0)
		});
		StackPanel val2 = new StackPanel();
		DockPanel.SetDock((UIElement)(object)val2, (Dock)1);
		((Panel)val).Children.Add((UIElement)(object)val2);
		((Panel)val2).Children.Add((UIElement)new TextBlock
		{
			Text = "Memories",
			FontSize = 24.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		});
		((Panel)val2).Children.Add((UIElement)new TextBlock
		{
			Text = "Personal memories follow you across chats. Project memories are recalled only in the same folder. Pinned entries need your approval before a correction replaces them.",
			TextWrapping = (TextWrapping)2,
			Foreground = (Brush)(object)Brushes.LightGray
		});
		StackPanel val3 = new StackPanel
		{
			Orientation = (Orientation)0,
			Margin = new Thickness(0.0, 14.0, 0.0, 12.0)
		};
		((Panel)val2).Children.Add((UIElement)(object)val3);
		((Panel)val3).Children.Add((UIElement)new TextBlock
		{
			Text = "Search",
			VerticalAlignment = (VerticalAlignment)1,
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
		});
		((FrameworkElement)search).Width = 270.0;
		((FrameworkElement)search).Margin = new Thickness(0.0, 0.0, 16.0, 0.0);
		SetAccessibleName((DependencyObject)(object)search, "Search memories");
		((Panel)val3).Children.Add((UIElement)(object)search);
		((Panel)val3).Children.Add((UIElement)(object)all);
		((Panel)val3).Children.Add((UIElement)(object)history);
		StackPanel val4 = new StackPanel
		{
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		DockPanel.SetDock((UIElement)(object)val4, (Dock)3);
		((Panel)val).Children.Add((UIElement)(object)val4);
		((Panel)val4).Children.Add((UIElement)(object)status);
		((Panel)val4).Children.Add((UIElement)new TextBlock
		{
			Text = "Forget clears this topic's saved values and quotes, including history, and blocks automatic relearning of that topic. Original conversations and backups remain. Topic names are retained for the block.",
			TextWrapping = (TextWrapping)2,
			Foreground = (Brush)(object)Brushes.LightGray,
			FontSize = 11.0,
			Margin = new Thickness(0.0, 9.0, 0.0, 0.0)
		});
		Grid val5 = new Grid();
		val5.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(0.95, (GridUnitType)2)
		});
		val5.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.05, (GridUnitType)2)
		});
		((Panel)val).Children.Add((UIElement)(object)val5);
		((FrameworkElement)entries).Margin = new Thickness(0.0, 0.0, 18.0, 0.0);
		SetAccessibleName((DependencyObject)(object)entries, "Memory entries");
		((Panel)val5).Children.Add((UIElement)(object)entries);
		ScrollViewer val6 = new ScrollViewer
		{
			VerticalScrollBarVisibility = (ScrollBarVisibility)1,
			HorizontalScrollBarVisibility = (ScrollBarVisibility)0
		};
		Grid.SetColumn((UIElement)(object)val6, 1);
		((Panel)val5).Children.Add((UIElement)(object)val6);
		StackPanel val7 = (StackPanel)(((ContentControl)val6).Content = (object)new StackPanel());
		WrapPanel val8 = new WrapPanel();
		((Panel)val7).Children.Add((UIElement)(object)val8);
		Button((Panel)(object)val8, "New memory", delegate
		{
			((Selector)memoryWindow.entries).SelectedItem = null;
			memoryWindow.Edit(null);
		});
		Button((Panel)(object)val8, "Save / approve", Save);
		Button((Panel)(object)val8, "Forget topic", Forget);
		Label((Panel)(object)val7, "TOPIC");
		((Panel)val7).Children.Add((UIElement)(object)topic);
		SetAccessibleName((DependencyObject)(object)topic, "Memory topic");
		topic.MaxLength = 80;
		Label((Panel)(object)val7, "REMEMBERED VALUE");
		((Panel)val7).Children.Add((UIElement)(object)value);
		SetAccessibleName((DependencyObject)(object)value, "Memory value");
		((FrameworkElement)value).Height = 90.0;
		((TextBoxBase)value).AcceptsReturn = true;
		value.TextWrapping = (TextWrapping)2;
		value.MaxLength = 1000;
		((TextBoxBase)value).VerticalScrollBarVisibility = (ScrollBarVisibility)1;
		Label((Panel)(object)val7, "SCOPE");
		((ItemsControl)scope).DisplayMemberPath = "Label";
		((Panel)val7).Children.Add((UIElement)(object)scope);
		SetAccessibleName((DependencyObject)(object)scope, "Memory scope");
		Label((Panel)(object)val7, "KIND");
		((ItemsControl)kind).ItemsSource = MemoryStore.Kinds;
		((Panel)val7).Children.Add((UIElement)(object)kind);
		SetAccessibleName((DependencyObject)(object)kind, "Memory kind");
		((Panel)val7).Children.Add((UIElement)(object)pinned);
		((Panel)val7).Children.Add((UIElement)(object)details);
		Label((Panel)(object)val7, "SUPPORTING SOURCES");
		((TextBoxBase)sources).IsReadOnly = true;
		((TextBoxBase)sources).AcceptsReturn = true;
		sources.TextWrapping = (TextWrapping)2;
		((FrameworkElement)sources).Height = 145.0;
		((TextBoxBase)sources).VerticalScrollBarVisibility = (ScrollBarVisibility)1;
		((Panel)val7).Children.Add((UIElement)(object)sources);
		SetAccessibleName((DependencyObject)(object)sources, "Memory sources");
		WrapPanel val9 = new WrapPanel();
		((Panel)val7).Children.Add((UIElement)(object)val9);
		Button((Panel)(object)val9, "Open latest source chat", delegate
		{
			MemorySource memorySource = memoryWindow.selected?.Sources.LastOrDefault();
			if (memorySource != null)
			{
				((Window)memoryWindow).Close();
				openSource2(memorySource);
			}
			else
			{
				memoryWindow.status.Text = "This entry has no linked conversation.";
			}
		});
		Button((Panel)(object)val9, "Allow relearning", delegate
		{
			if (memoryWindow.selected?.State != "forgotten")
			{
				memoryWindow.status.Text = "Select a forgotten topic first.";
			}
			else
			{
				store2.AllowRelearning(memoryWindow.selected.Id);
				memoryWindow.Refresh();
				memoryWindow.status.Text = "Automatic learning is allowed for this topic again.";
			}
		});
		((TextBoxBase)search).TextChanged += (TextChangedEventHandler)delegate
		{
			memoryWindow.Refresh();
		};
		((ButtonBase)all).Click += (RoutedEventHandler)delegate
		{
			memoryWindow.Refresh();
		};
		((ButtonBase)history).Click += (RoutedEventHandler)delegate
		{
			memoryWindow.Refresh();
		};
		((Selector)entries).SelectionChanged += (SelectionChangedEventHandler)delegate
		{
			if (!memoryWindow.refreshing)
			{
				memoryWindow.Edit(((Selector)memoryWindow.entries).SelectedItem as MemoryEntry);
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
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Expected O, but got Unknown
		panel.Children.Add((UIElement)new TextBlock
		{
			Text = text,
			FontSize = 10.0,
			Foreground = (Brush)(object)Brushes.LightGray,
			Margin = new Thickness(0.0, 14.0, 0.0, 7.0)
		});
	}

	private void Button(Panel panel, string text, Action action)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Expected O, but got Unknown
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Expected O, but got Unknown
		Action action2 = action;
		Button val = new Button
		{
			Content = text,
			Margin = new Thickness(0.0, 0.0, 8.0, 7.0),
			Padding = new Thickness(10.0, 7.0, 10.0, 7.0)
		};
		panel.Children.Add((UIElement)(object)val);
		((ButtonBase)val).Click += (RoutedEventHandler)delegate
		{
			try
			{
				action2();
			}
			catch (Exception ex)
			{
				status.Text = ex.Message;
			}
		};
	}

	private void Refresh(string? selectId = null)
	{
		string selectId2 = selectId;
		try
		{
			string scopeKey = MemoryStore.ScopeFor(workspace);
			string query = search.Text.Trim();
			List<MemoryEntry> list = store.Snapshot().Where(delegate(MemoryEntry e)
			{
				bool flag = ((ToggleButton)all).IsChecked.GetValueOrDefault() || e.Scope == "personal" || e.Scope == scopeKey;
				if (flag)
				{
					bool flag2 = ((ToggleButton)history).IsChecked.GetValueOrDefault();
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
			((ItemsControl)entries).ItemsSource = list;
			((Selector)entries).SelectedItem = list.FirstOrDefault((MemoryEntry e) => e.Id == (selectId2 ?? selected?.Id));
			refreshing = false;
			if (((Selector)entries).SelectedItem is MemoryEntry entry)
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
		MemoryEntry entry2 = entry;
		selected = entry2;
		topic.Text = entry2?.Topic ?? "";
		value.Text = entry2?.Value ?? "";
		((ToggleButton)pinned).IsChecked = entry2?.Pinned ?? false;
		List<ScopeChoice> list = new List<ScopeChoice>
		{
			new ScopeChoice("Personal · all chats", "", Personal: true)
		};
		if (!string.IsNullOrWhiteSpace(workspace))
		{
			list.Add(new ScopeChoice("Current project · " + Path.GetFileName(workspace), workspace, Personal: false));
		}
		if (entry2 != null && entry2.Scope != "personal" && entry2.Scope != MemoryStore.ScopeFor(workspace))
		{
			string scopeLabel = entry2.ScopeLabel;
			string text = entry2.Scope;
			list.Add(new ScopeChoice(scopeLabel, text.Substring(8, text.Length - 8), Personal: false));
		}
		((ItemsControl)scope).ItemsSource = list;
		((Selector)scope).SelectedItem = ((entry2 == null) ? list.Last() : list.First((ScopeChoice c) => c.Personal == (entry2.Scope == "personal") && (c.Personal || MemoryStore.ScopeFor(c.Workspace) == entry2.Scope)));
		((Selector)kind).SelectedItem = entry2?.Kind ?? "fact";
		details.Text = ((entry2 == null) ? "New entries are saved as manual memories." : ($"{entry2.State} · {entry2.Origin} · updated {entry2.Updated.LocalDateTime:g}\n{entry2.Confirmations} supporting message(s) · last recalled {entry2.LastUsed?.LocalDateTime.ToString("g") ?? "never"}" + ((entry2.State == "review") ? "\nA pinned memory conflicts. Save / approve makes this value active and replaces the old value." : "")));
		TextBox obj = sources;
		MemoryEntry memoryEntry = entry2;
		obj.Text = ((memoryEntry != null && memoryEntry.Sources.Count > 0) ? string.Join("\n\n", entry2.Sources.Select((MemorySource s) => $"{s.At.LocalDateTime:g} · chat {s.ChatId.Substring(0, Math.Min(8, s.ChatId.Length))}\n“{s.Quote}”")) : "No supporting chat quotes. Manual entries reflect your edits.");
	}

	private void Save()
	{
		if (((Selector)scope).SelectedItem is ScopeChoice scopeChoice && ((Selector)kind).SelectedItem is string text)
		{
			MemoryEntry memoryEntry = store.SaveManual(selected?.Id, scopeChoice.Workspace, scopeChoice.Personal, topic.Text, value.Text, text, ((ToggleButton)pinned).IsChecked.GetValueOrDefault());
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
