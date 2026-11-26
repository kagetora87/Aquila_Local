using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AquilaLocal;

/// <summary>
/// The avatar floating on the desktop: no frame, transparent background (transparent PNGs show only
/// the character), drag it anywhere. Right-click for size, always-on-top and "put back".
/// </summary>
public sealed class AvatarPopoutWindow : Window
{
	private readonly SpriteAvatarOptions options;

	public event Action? DockRequested;

	public AvatarPopoutWindow(SpriteAvatarView view, SpriteAvatarOptions options)
	{
		this.options = options;
		base.Title = "Aquila avatar";
		base.WindowStyle = WindowStyle.None;
		base.AllowsTransparency = true;
		base.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
		base.ShowInTaskbar = false;
		base.ResizeMode = ResizeMode.NoResize;
		base.Topmost = options.Topmost;
		ApplySize();
		if (!double.IsNaN(options.Left) && !double.IsNaN(options.Top))
		{
			base.WindowStartupLocation = WindowStartupLocation.Manual;
			base.Left = Math.Clamp(options.Left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80.0);
			base.Top = Math.Clamp(options.Top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80.0);
		}
		else
		{
			base.WindowStartupLocation = WindowStartupLocation.Manual;
			base.Left = SystemParameters.WorkArea.Right - base.Width - 24.0;
			base.Top = SystemParameters.WorkArea.Bottom - base.Height - 24.0;
		}
		base.Content = view;
		base.ToolTip = "Drag to move · double-click or right-click to put it back in Aquila";
		base.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
		{
			if (e.ClickCount == 2)
			{
				this.DockRequested?.Invoke();
			}
			else
			{
				try
				{
					DragMove();
				}
				catch (InvalidOperationException)
				{
				}
			}
		};
		base.LocationChanged += delegate
		{
			options.Left = base.Left;
			options.Top = base.Top;
		};
		// Explicit dark text on a light menu: the app's default text colour is light, which made the
		// menu unreadable on Windows' white menu background.
		ContextMenu contextMenu = new ContextMenu
		{
			Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
			Foreground = MenuInk
		};
		contextMenu.Items.Add(Item("Put back in Aquila", delegate
		{
			this.DockRequested?.Invoke();
		}));
		contextMenu.Items.Add(new Separator());
		contextMenu.Items.Add(Item("Small", delegate
		{
			Resize(200);
		}));
		contextMenu.Items.Add(Item("Medium", delegate
		{
			Resize(320);
		}));
		contextMenu.Items.Add(Item("Large", delegate
		{
			Resize(480);
		}));
		MenuItem top = new MenuItem
		{
			Header = MenuText("Always on top"),
			IsCheckable = true,
			IsChecked = options.Topmost,
			Foreground = MenuInk
		};
		top.Click += delegate
		{
			options.Topmost = top.IsChecked;
			base.Topmost = top.IsChecked;
		};
		contextMenu.Items.Add(top);
		base.ContextMenu = contextMenu;
	}

	private static MenuItem Item(string header, Action action)
	{
		MenuItem menuItem = new MenuItem
		{
			Header = MenuText(header),
			Foreground = MenuInk
		};
		menuItem.Click += delegate
		{
			action();
		};
		return menuItem;
	}

	private static readonly Brush MenuInk = new SolidColorBrush(Color.FromRgb(17, 17, 17));

	private static TextBlock MenuText(string text)
	{
		return new TextBlock
		{
			Text = text,
			Foreground = MenuInk
		};
	}

	private void Resize(int height)
	{
		double num = base.Top + base.Height;
		options.Height = height;
		ApplySize();
		base.Top = num - base.Height;
	}

	private void ApplySize()
	{
		base.Height = Math.Clamp(options.Height, 120, 900);
		base.Width = base.Height * 0.82;
	}
}
