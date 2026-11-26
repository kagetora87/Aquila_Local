using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AquilaLocal;

public sealed class ContextSummaryWindow : AquilaWindow
{
	public ContextSummaryWindow(ContextCheckpoint summary, Action reset)
	{
		ContextSummaryWindow contextSummaryWindow = this;
		base.Title = "Aquila · Context summary";
		base.Width = 700.0;
		base.Height = 650.0;
		base.MinWidth = 460.0;
		base.MinHeight = 420.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.Background = new SolidColorBrush(Color.FromRgb(16, 23, 32));
		DockPanel dockPanel = (DockPanel)(base.Content = new DockPanel
		{
			Margin = new Thickness(22.0)
		});
		StackPanel stackPanel = new StackPanel();
		DockPanel.SetDock(stackPanel, Dock.Top);
		dockPanel.Children.Add(stackPanel);
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Conversation context",
			FontSize = 24.0
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"{summary.CoveredTurns} earlier turns · {summary.Created.LocalDateTime:g}\n{summary.Model}",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brushes.LightGray,
			Margin = new Thickness(0.0, 10.0, 0.0, 12.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "This summary replaces earlier turns only in future model requests. Full chat history remains saved. Summaries can omit or misstate details; image pixels are not included.",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brushes.LightGray,
			Margin = new Thickness(0.0, 0.0, 0.0, 14.0)
		});
		StackPanel stackPanel2 = new StackPanel
		{
			Margin = new Thickness(0.0, 14.0, 0.0, 0.0)
		};
		DockPanel.SetDock(stackPanel2, Dock.Bottom);
		dockPanel.Children.Add(stackPanel2);
		TextBlock status = new TextBlock
		{
			TextWrapping = TextWrapping.Wrap
		};
		stackPanel2.Children.Add(status);
		Button button = new Button
		{
			Content = "Use original history",
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(0.0, 7.0, 0.0, 0.0)
		};
		stackPanel2.Children.Add(button);
		button.Click += delegate
		{
			if (!AquilaDialog.Confirm(contextSummaryWindow, "Use the original history again?", "Stop using this summary and use the original conversation history again, within the normal length limits.\n\nThe full history and usage totals are kept.", "Use original"))
			{
				return;
			}
			try
			{
				reset();
				contextSummaryWindow.Close();
			}
			catch (Exception ex)
			{
				status.Text = "Could not reset context: " + ex.Message;
			}
		};
		dockPanel.Children.Add(new TextBox
		{
			Text = summary.Summary,
			IsReadOnly = true,
			TextWrapping = TextWrapping.Wrap,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			AcceptsReturn = true,
			FontSize = 14.0
		});
	}
}
