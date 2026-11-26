using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace AquilaLocal;

/// <summary>
/// Base for every Aquila window: dark theme defaults, the Inter font, and a dark native title bar
/// (Windows 11 immersive dark mode plus caption/border colours through DWM). Esc closes secondary
/// windows when <see cref="CloseOnEscape"/> is set.
/// </summary>
public class AquilaWindow : Window
{
	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

	public bool CloseOnEscape { get; set; }

	/// <summary>Fade and scale the content in when the window first shows.</summary>
	public bool AnimateIn { get; set; } = true;

	public AquilaWindow()
	{
		base.Background = Ui.Brush("Bg.Window");
		base.Foreground = Ui.Brush("Text.Primary");
		base.FontFamily = Ui.FontUI;
		base.FontSize = 14.0;
		base.UseLayoutRounding = true;
		base.SnapsToDevicePixels = true;
		TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
		base.Loaded += delegate
		{
			if (AnimateIn && base.Content is UIElement element)
			{
				Motion.FadeScaleIn(element);
			}
		};
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		ApplyDarkTitleBar(this);
	}

	public static void ApplyDarkTitleBar(Window window)
	{
		try
		{
			IntPtr handle = new WindowInteropHelper(window).Handle;
			if (handle == IntPtr.Zero)
			{
				return;
			}
			int value = 1;
			DwmSetWindowAttribute(handle, 20, ref value, 4);
			int value2 = 0x00140F0B;
			DwmSetWindowAttribute(handle, 35, ref value2, 4);
			int value3 = 0x00F3EDE8;
			DwmSetWindowAttribute(handle, 36, ref value3, 4);
			int value4 = 0x0036291E;
			DwmSetWindowAttribute(handle, 34, ref value4, 4);
		}
		catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
		{
		}
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (!e.Handled && CloseOnEscape && e.Key == Key.Escape)
		{
			e.Handled = true;
			Close();
		}
	}
}
