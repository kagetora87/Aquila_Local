using System;
using System.Windows;
using System.Windows.Controls;

namespace AquilaLocal;

public sealed class ContextSummaryWindow : AquilaWindow
{
	public ContextSummaryWindow(ContextCheckpoint summary, Action reset)
	{
		ContextSummaryWindow window = this;
		base.Title = "Aquila · Conversation summary";
		base.Width = 720.0;
		base.Height = 680.0;
		base.MinWidth = 480.0;
		base.MinHeight = 420.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		Grid body = new Grid();
		body.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		body.RowDefinitions.Add(new RowDefinition());
		TextBlock meta = Ui.Text($"Covers {summary.CoveredTurns} earlier exchanges · made {summary.Created.LocalDateTime:g} · {summary.Model}", "Type.Caption");
		meta.Margin = new Thickness(0.0, 0.0, 0.0, 10.0);
		meta.TextWrapping = TextWrapping.Wrap;
		body.Children.Add(meta);
		TextBox text = new TextBox
		{
			Text = summary.Summary,
			IsReadOnly = true,
			TextWrapping = TextWrapping.Wrap,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			AcceptsReturn = true,
			FontSize = 14.0,
			VerticalContentAlignment = VerticalAlignment.Top
		};
		Grid.SetRow(text, 1);
		body.Children.Add(text);
		TextBlock status = new TextBlock();
		Button useOriginal = Ui.Button("Use original history", "Btn.Secondary");
		useOriginal.Click += delegate
		{
			if (!AquilaDialog.Confirm(window, "Use the original history again?", "Stop using this summary and use the original conversation history again, within the normal length limits.\n\nThe full history and usage totals are kept.", "Use original"))
			{
				return;
			}
			try
			{
				reset();
				window.Close();
			}
			catch (Exception ex)
			{
				status.Text = "Could not reset: " + ex.Message;
				status.Foreground = Ui.Brush("Danger");
			}
		};
		Button close = Ui.Button("Close", "Btn.Primary", delegate
		{
			window.Close();
		});
		close.IsDefault = true;
		base.Content = Ui.DialogScaffold("Conversation summary", "Aquila uses this summary instead of the earlier part of the chat. Everything you can see in the chat is still saved. Summaries can leave out or get details wrong, and pictures are not included.", body, status, useOriginal, close);
	}
}
