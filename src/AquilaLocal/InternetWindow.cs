using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace AquilaLocal;

public sealed class InternetWindow : AquilaWindow
{
	public InternetWindow(Settings settings, Action save)
	{
		InternetWindow window = this;
		base.Title = "Aquila · Internet";
		base.Width = 600.0;
		base.SizeToContent = SizeToContent.Height;
		base.ResizeMode = ResizeMode.NoResize;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		StackPanel body = new StackPanel();
		string[] modes = new string[3] { "Off", "Ask first", "Automatic" };
		string[] titles = new string[3] { "Off", "Ask me first", "Automatic" };
		string[] hints = new string[3]
		{
			"Aquila never contacts the internet.",
			"Aquila asks before each web search or page it wants to read.",
			"Aquila looks things up on its own while answering. It never browses in the background."
		};
		RadioButton[] choices = new RadioButton[3];
		for (int i = 0; i < 3; i++)
		{
			RadioButton radioButton = new RadioButton
			{
				Content = titles[i],
				GroupName = "WebMode",
				Style = Ui.Style("ChoiceCard"),
				IsChecked = settings.WebMode == modes[i]
			};
			Ui.SetHint(radioButton, hints[i]);
			AutomationProperties.SetName(radioButton, titles[i]);
			choices[i] = radioButton;
			body.Children.Add(radioButton);
		}
		if (Array.TrueForAll(choices, (RadioButton c) => c.IsChecked != true))
		{
			choices[0].IsChecked = true;
		}
		PasswordBox key = new PasswordBox
		{
			MaxLength = 2000
		};
		Ui.SetPlaceholder(key, (settings.WebKeyProtected.Length == 0) ? "Paste a Brave Search API key" : "A key is saved · leave blank to keep it");
		AutomationProperties.SetName(key, "Brave Search API key");
		CheckBox clear = new CheckBox
		{
			Content = "Remove the saved key",
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0),
			Visibility = (settings.WebKeyProtected.Length == 0) ? Visibility.Collapsed : Visibility.Visible
		};
		StackPanel keyPanel = new StackPanel();
		keyPanel.Children.Add(Ui.Field("Search key (optional)", key, "Needed only for general web searches. Reading a page from a link needs no key. You can create one at api-dashboard.search.brave.com.", 0.0));
		keyPanel.Children.Add(clear);
		Expander expander = new Expander
		{
			Header = "Web search key",
			Style = Ui.Style("Expander.Card"),
			Content = keyPanel,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		body.Children.Add(expander);
		TextBlock privacy = Ui.Text("Searches go to Brave and page requests go to the websites themselves. Your conversations and the model stay on this computer. The key is encrypted for your Windows account. Pages that need a sign-in or JavaScript can't be read.", "Type.Caption");
		privacy.Margin = new Thickness(4.0, 16.0, 4.0, 24.0);
		body.Children.Add(privacy);
		TextBlock status = new TextBlock();
		Button cancel = Ui.Button("Cancel", "Btn.Ghost", delegate
		{
			window.Close();
		});
		Button saveButton = Ui.Button("Save", "Btn.Primary");
		saveButton.IsDefault = true;
		saveButton.Click += delegate
		{
			string webMode = settings.WebMode;
			string webKeyProtected = settings.WebKeyProtected;
			try
			{
				int index = Array.FindIndex(choices, (RadioButton c) => c.IsChecked == true);
				settings.WebMode = modes[Math.Max(0, index)];
				if (clear.IsChecked == true)
				{
					settings.WebKeyProtected = "";
				}
				else if (key.Password.Length > 0)
				{
					settings.WebKeyProtected = WebSecrets.Protect(key.Password.Trim());
				}
				save();
				window.Close();
			}
			catch (Exception ex)
			{
				settings.WebMode = webMode;
				settings.WebKeyProtected = webKeyProtected;
				status.Text = ex.Message;
				status.Foreground = Ui.Brush("Danger");
			}
		};
		base.Content = Ui.DialogScaffold("Internet access", "Choose whether Aquila may look things up on the web.", body, status, cancel, saveButton);
	}
}
