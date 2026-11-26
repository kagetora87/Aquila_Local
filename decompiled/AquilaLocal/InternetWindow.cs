using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AquilaLocal;

public sealed class InternetWindow : Window
{
	public InternetWindow(Settings settings, Action save)
	{
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Expected O, but got Unknown
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Expected O, but got Unknown
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Expected O, but got Unknown
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dd: Expected O, but got Unknown
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Expected O, but got Unknown
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0120: Expected O, but got Unknown
		//IL_0127: Unknown result type (might be due to invalid IL or missing references)
		//IL_012c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0163: Unknown result type (might be due to invalid IL or missing references)
		//IL_0172: Expected O, but got Unknown
		//IL_0174: Unknown result type (might be due to invalid IL or missing references)
		//IL_017a: Expected O, but got Unknown
		//IL_01dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_0212: Unknown result type (might be due to invalid IL or missing references)
		//IL_0221: Expected O, but got Unknown
		//IL_0223: Unknown result type (might be due to invalid IL or missing references)
		//IL_0228: Unknown result type (might be due to invalid IL or missing references)
		//IL_0233: Unknown result type (might be due to invalid IL or missing references)
		//IL_0247: Expected O, but got Unknown
		//IL_026f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0274: Unknown result type (might be due to invalid IL or missing references)
		//IL_0298: Unknown result type (might be due to invalid IL or missing references)
		//IL_029f: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d3: Expected O, but got Unknown
		//IL_02d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02da: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ea: Expected O, but got Unknown
		//IL_0302: Unknown result type (might be due to invalid IL or missing references)
		//IL_0307: Unknown result type (might be due to invalid IL or missing references)
		//IL_0312: Unknown result type (might be due to invalid IL or missing references)
		//IL_0319: Unknown result type (might be due to invalid IL or missing references)
		//IL_033e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0348: Unknown result type (might be due to invalid IL or missing references)
		//IL_035c: Expected O, but got Unknown
		//IL_035e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0363: Unknown result type (might be due to invalid IL or missing references)
		//IL_036f: Expected O, but got Unknown
		//IL_0381: Unknown result type (might be due to invalid IL or missing references)
		//IL_0386: Unknown result type (might be due to invalid IL or missing references)
		//IL_0391: Unknown result type (might be due to invalid IL or missing references)
		//IL_0399: Expected O, but got Unknown
		//IL_03ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b8: Expected O, but got Unknown
		Settings settings2 = settings;
		Action save2 = save;
		((Window)this)._002Ector();
		InternetWindow internetWindow = this;
		((Window)this).Title = "Aquila · Internet";
		((FrameworkElement)this).Width = 580.0;
		((FrameworkElement)this).Height = 560.0;
		((FrameworkElement)this).MinWidth = 480.0;
		((FrameworkElement)this).MinHeight = 450.0;
		((Window)this).WindowStartupLocation = (WindowStartupLocation)2;
		((Control)this).Background = (Brush)new SolidColorBrush(Color.FromRgb((byte)16, (byte)23, (byte)32));
		((Control)this).Foreground = (Brush)Application.Current.Resources[(object)"Ink"];
		((Control)this).FontFamily = new FontFamily("Segoe UI");
		((Control)this).FontSize = 13.0;
		StackPanel val = new StackPanel
		{
			Margin = new Thickness(24.0)
		};
		((ContentControl)this).Content = (object)new ScrollViewer
		{
			Content = val,
			VerticalScrollBarVisibility = (ScrollBarVisibility)1
		};
		((Panel)val).Children.Add((UIElement)new TextBlock
		{
			Text = "Internet access",
			FontSize = 24.0
		});
		((Panel)val).Children.Add((UIElement)new TextBlock
		{
			Text = "Off sends no web requests. Ask first confirms each lookup. Automatic lets the assistant look things up while answering; it does not browse in the background.",
			TextWrapping = (TextWrapping)2,
			Margin = new Thickness(0.0, 12.0, 0.0, 12.0)
		});
		ComboBox val2 = new ComboBox();
		((ItemsControl)val2).ItemsSource = new string[3] { "Off", "Ask first", "Automatic" };
		((Selector)val2).SelectedItem = settings2.WebMode;
		ComboBox mode = val2;
		((Panel)val).Children.Add((UIElement)(object)mode);
		AutomationProperties.SetName((DependencyObject)(object)mode, "Internet access mode");
		((Panel)val).Children.Add((UIElement)new TextBlock
		{
			Text = "Brave Search API key",
			Margin = new Thickness(0.0, 18.0, 0.0, 6.0)
		});
		PasswordBox key = new PasswordBox
		{
			MaxLength = 2000,
			Height = 30.0
		};
		((Panel)val).Children.Add((UIElement)(object)key);
		AutomationProperties.SetName((DependencyObject)(object)key, "Brave Search API key");
		((Panel)val).Children.Add((UIElement)new TextBlock
		{
			Text = ((settings2.WebKeyProtected.Length == 0) ? "No key saved. Enter one to enable general web search. Direct page reading needs no key." : "A key is saved. Leave blank to keep it."),
			TextWrapping = (TextWrapping)2,
			Margin = new Thickness(0.0, 7.0, 0.0, 7.0)
		});
		CheckBox clear = new CheckBox
		{
			Content = "Remove saved API key"
		};
		((Panel)val).Children.Add((UIElement)(object)clear);
		((Panel)val).Children.Add((UIElement)new TextBlock
		{
			Text = "Search queries go to Brave; page requests go to websites. Model inference stays local. Credentials are encrypted for this Windows user. Create a Search API key at api-dashboard.search.brave.com. Pages requiring login or JavaScript are not supported.",
			TextWrapping = (TextWrapping)2,
			Margin = new Thickness(0.0, 12.0, 0.0, 12.0),
			FontSize = 11.0
		});
		TextBlock status = new TextBlock
		{
			TextWrapping = (TextWrapping)2
		};
		((Panel)val).Children.Add((UIElement)(object)status);
		Button val3 = new Button
		{
			Content = "Save",
			HorizontalAlignment = (HorizontalAlignment)0
		};
		((Panel)val).Children.Add((UIElement)(object)val3);
		((ButtonBase)val3).Click += (RoutedEventHandler)delegate
		{
			string webMode = settings2.WebMode;
			string webKeyProtected = settings2.WebKeyProtected;
			try
			{
				settings2.WebMode = (string)((Selector)mode).SelectedItem;
				if (((ToggleButton)clear).IsChecked.GetValueOrDefault())
				{
					settings2.WebKeyProtected = "";
				}
				else if (key.Password.Length > 0)
				{
					settings2.WebKeyProtected = WebSecrets.Protect(key.Password.Trim());
				}
				save2();
				((Window)internetWindow).Close();
			}
			catch (Exception ex)
			{
				settings2.WebMode = webMode;
				settings2.WebKeyProtected = webKeyProtected;
				status.Text = ex.Message;
			}
		};
	}
}
