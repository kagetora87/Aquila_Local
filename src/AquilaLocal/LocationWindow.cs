using System.Windows;
using System.Windows.Controls;

namespace AquilaLocal;

/// <summary>Change where the story is ("her apartment, kitchen"). She's told on her next reply.</summary>
public sealed class LocationWindow : AquilaWindow
{
	private readonly TextBox place;

	public string Place => place.Text.Trim();

	public LocationWindow(string personaName, string current)
	{
		base.Title = "Aquila · Where are we?";
		base.Width = 520;
		base.Height = 330;
		base.ResizeMode = ResizeMode.NoResize;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		CloseOnEscape = true;
		place = new TextBox { Text = current, MaxLength = Agent.MaxLocationLength };
		Ui.SetPlaceholder(place, "For example: her apartment, living room, rainy evening");
		StackPanel body = new StackPanel();
		body.Children.Add(Ui.Field("Where you are", place, "Short and concrete works best. " + personaName + " sees it on her next reply and pictures use it as the setting. She can also move you somewhere new.", 0));
		Button save = Ui.Button("Save", "Btn.Primary", delegate
		{
			DialogResult = true;
		});
		save.IsDefault = true;
		Button clear = Ui.Button("Clear", "Btn.Secondary", delegate
		{
			place.Text = "";
			DialogResult = true;
		});
		Button cancel = Ui.Button("Cancel", "Btn.Secondary", delegate
		{
			Close();
		});
		base.Content = Ui.DialogScaffold("Where are we?", "", body, null, clear, cancel, save);
		base.Loaded += delegate
		{
			place.Focus();
			place.SelectAll();
		};
	}
}
