using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace AquilaLocal;

public sealed class InternetWindow : AquilaWindow
{
	public InternetWindow(Settings settings, Action save)
	{
		InternetWindow internetWindow = this;
		base.Title = "Aquila · Internet";
		base.Width = 580.0;
		base.Height = 560.0;
		base.MinWidth = 480.0;
		base.MinHeight = 450.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.Background = new SolidColorBrush(Color.FromRgb(16, 23, 32));
		base.Foreground = (Brush)Application.Current.Resources["Ink"];
		base.FontFamily = new FontFamily("Segoe UI");
		base.FontSize = 13.0;
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(24.0)
		};
		base.Content = new ScrollViewer
		{
			Content = stackPanel,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Internet access",
			FontSize = 24.0
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Off sends no web requests. Ask first confirms each lookup. Automatic lets the assistant look things up while answering; it does not browse in the background.",
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 12.0, 0.0, 12.0)
		});
		ComboBox mode = new ComboBox
		{
			ItemsSource = new string[3] { "Off", "Ask first", "Automatic" },
			SelectedItem = settings.WebMode
		};
		stackPanel.Children.Add(mode);
		AutomationProperties.SetName(mode, "Internet access mode");
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Brave Search API key",
			Margin = new Thickness(0.0, 18.0, 0.0, 6.0)
		});
		PasswordBox key = new PasswordBox
		{
			MaxLength = 2000,
			Height = 30.0
		};
		stackPanel.Children.Add(key);
		AutomationProperties.SetName(key, "Brave Search API key");
		stackPanel.Children.Add(new TextBlock
		{
			Text = ((settings.WebKeyProtected.Length == 0) ? "No key saved. Enter one to enable general web search. Direct page reading needs no key." : "A key is saved. Leave blank to keep it."),
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 7.0, 0.0, 7.0)
		});
		CheckBox clear = new CheckBox
		{
			Content = "Remove saved API key"
		};
		stackPanel.Children.Add(clear);
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Search queries go to Brave; page requests go to websites. Model inference stays local. Credentials are encrypted for this Windows user. Create a Search API key at api-dashboard.search.brave.com. Pages requiring login or JavaScript are not supported.",
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 12.0, 0.0, 12.0),
			FontSize = 11.0
		});
		TextBlock status = new TextBlock
		{
			TextWrapping = TextWrapping.Wrap
		};
		stackPanel.Children.Add(status);
		Button button = new Button
		{
			Content = "Save",
			HorizontalAlignment = HorizontalAlignment.Left
		};
		stackPanel.Children.Add(button);
		button.Click += delegate
		{
			string webMode = settings.WebMode;
			string webKeyProtected = settings.WebKeyProtected;
			try
			{
				settings.WebMode = (string)mode.SelectedItem;
				if (clear.IsChecked == true)
				{
					settings.WebKeyProtected = "";
				}
				else if (key.Password.Length > 0)
				{
					settings.WebKeyProtected = WebSecrets.Protect(key.Password.Trim());
				}
				save();
				internetWindow.Close();
			}
			catch (Exception ex)
			{
				settings.WebMode = webMode;
				settings.WebKeyProtected = webKeyProtected;
				status.Text = ex.Message;
			}
		};
	}
}
