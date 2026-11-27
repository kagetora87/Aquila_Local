using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace AquilaLocal;

/// <summary>The sidebar can be made wider or narrower by dragging its edge; the width is remembered.</summary>
public partial class MainWindow
{
	private void SidebarSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
	{
		SaveSidebarWidth(SidebarColumn.ActualWidth);
	}

	private void SidebarSplitter_DoubleClick(object sender, MouseButtonEventArgs e)
	{
		SidebarColumn.Width = new GridLength(272.0);
		SaveSidebarWidth(272.0);
		e.Handled = true;
	}

	private void SaveSidebarWidth(double width)
	{
		settings.SidebarWidth = Math.Round(width);
		try
		{
			store.SaveSettings(settings);
		}
		catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
		{
			Notice("Couldn't save the sidebar width: " + ex.Message);
		}
	}
}
