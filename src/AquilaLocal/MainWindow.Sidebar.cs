using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace AquilaLocal;

/// <summary>The sidebar and the avatar pane can be made wider or narrower by dragging its edge; the width is remembered.</summary>
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

	private void AvatarSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
	{
		settings.AvatarWidth = Math.Round(AvatarColumn.ActualWidth);
		FitSidebar();
		SaveLayout();
	}

	private void AvatarSplitter_DoubleClick(object sender, MouseButtonEventArgs e)
	{
		AvatarColumn.Width = new GridLength(360.0);
		settings.AvatarWidth = 360.0;
		UpdateLayout();
		FitSidebar();
		SaveLayout();
		e.Handled = true;
	}

	private void AvatarPane_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		if (e.WidthChanged)
		{
			FitSidebar();
		}
	}

	private void SaveSidebarWidth(double width)
	{
		settings.SidebarWidth = Math.Round(width);
		SaveLayout();
	}

	private void SaveLayout()
	{
		try
		{
			store.SaveSettings(settings);
		}
		catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
		{
			Notice("Couldn't save the layout: " + ex.Message);
		}
	}
}
