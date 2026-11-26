using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AquilaLocal;

public sealed class ContextSummaryWindow : Window
{
	public ContextSummaryWindow(ContextCheckpoint summary, Action reset)
	{
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Expected O, but got Unknown
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Expected O, but got Unknown
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Expected O, but got Unknown
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Expected O, but got Unknown
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_0153: Unknown result type (might be due to invalid IL or missing references)
		//IL_015a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0165: Unknown result type (might be due to invalid IL or missing references)
		//IL_018a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0199: Expected O, but got Unknown
		//IL_01a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f6: Expected O, but got Unknown
		//IL_01f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0221: Unknown result type (might be due to invalid IL or missing references)
		//IL_022c: Expected O, but got Unknown
		//IL_0241: Unknown result type (might be due to invalid IL or missing references)
		//IL_0246: Unknown result type (might be due to invalid IL or missing references)
		//IL_0252: Expected O, but got Unknown
		//IL_0264: Unknown result type (might be due to invalid IL or missing references)
		//IL_0269: Unknown result type (might be due to invalid IL or missing references)
		//IL_0274: Unknown result type (might be due to invalid IL or missing references)
		//IL_027b: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ac: Expected O, but got Unknown
		//IL_02c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02cd: Expected O, but got Unknown
		//IL_02d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0300: Unknown result type (might be due to invalid IL or missing references)
		//IL_0314: Expected O, but got Unknown
		Action reset2 = reset;
		((Window)this)._002Ector();
		ContextSummaryWindow contextSummaryWindow = this;
		((Window)this).Title = "Aquila · Context summary";
		((FrameworkElement)this).Width = 700.0;
		((FrameworkElement)this).Height = 650.0;
		((FrameworkElement)this).MinWidth = 460.0;
		((FrameworkElement)this).MinHeight = 420.0;
		((Window)this).WindowStartupLocation = (WindowStartupLocation)2;
		((Control)this).Background = (Brush)new SolidColorBrush(Color.FromRgb((byte)16, (byte)23, (byte)32));
		DockPanel val = (DockPanel)(((ContentControl)this).Content = (object)new DockPanel
		{
			Margin = new Thickness(22.0)
		});
		StackPanel val2 = new StackPanel();
		DockPanel.SetDock((UIElement)(object)val2, (Dock)1);
		((Panel)val).Children.Add((UIElement)(object)val2);
		((Panel)val2).Children.Add((UIElement)new TextBlock
		{
			Text = "Conversation context",
			FontSize = 24.0
		});
		((Panel)val2).Children.Add((UIElement)new TextBlock
		{
			Text = $"{summary.CoveredTurns} earlier turns · {summary.Created.LocalDateTime:g}\n{summary.Model}",
			TextWrapping = (TextWrapping)2,
			Foreground = (Brush)(object)Brushes.LightGray,
			Margin = new Thickness(0.0, 10.0, 0.0, 12.0)
		});
		((Panel)val2).Children.Add((UIElement)new TextBlock
		{
			Text = "This summary replaces earlier turns only in future model requests. Full chat history remains saved. Summaries can omit or misstate details; image pixels are not included.",
			TextWrapping = (TextWrapping)2,
			Foreground = (Brush)(object)Brushes.LightGray,
			Margin = new Thickness(0.0, 0.0, 0.0, 14.0)
		});
		StackPanel val3 = new StackPanel
		{
			Margin = new Thickness(0.0, 14.0, 0.0, 0.0)
		};
		DockPanel.SetDock((UIElement)(object)val3, (Dock)3);
		((Panel)val).Children.Add((UIElement)(object)val3);
		TextBlock status = new TextBlock
		{
			TextWrapping = (TextWrapping)2
		};
		((Panel)val3).Children.Add((UIElement)(object)status);
		Button val4 = new Button
		{
			Content = "Use original history",
			HorizontalAlignment = (HorizontalAlignment)0,
			Margin = new Thickness(0.0, 7.0, 0.0, 0.0)
		};
		((Panel)val3).Children.Add((UIElement)(object)val4);
		((ButtonBase)val4).Click += (RoutedEventHandler)delegate
		{
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			//IL_001a: Invalid comparison between Unknown and I4
			if ((int)MessageBox.Show((Window)(object)contextSummaryWindow, "Stop using this summary and use original conversation history again, within the normal context limits? Full history and cumulative usage are kept.", "Reset compacted context?", (MessageBoxButton)4, (MessageBoxImage)32, (MessageBoxResult)7) != 6)
			{
				return;
			}
			try
			{
				reset2();
				((Window)contextSummaryWindow).Close();
			}
			catch (Exception ex)
			{
				status.Text = "Could not reset context: " + ex.Message;
			}
		};
		((Panel)val).Children.Add((UIElement)new TextBox
		{
			Text = summary.Summary,
			IsReadOnly = true,
			TextWrapping = (TextWrapping)2,
			VerticalScrollBarVisibility = (ScrollBarVisibility)1,
			AcceptsReturn = true,
			FontSize = 14.0
		});
	}
}
