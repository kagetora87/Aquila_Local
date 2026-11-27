using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AquilaLocal.Companion;

namespace AquilaLocal;

/// <summary>Asks who the imported conversation was with: Aquila, or one of the companions.</summary>
public sealed class ImportChatWindow : AquilaWindow
{
	private readonly ComboBox who;

	/// <summary>The chosen persona, or null for a regular Aquila chat.</summary>
	public PersonaEntry? Persona => (who.SelectedItem as ComboBoxItem)?.Tag as PersonaEntry;

	public ImportChatWindow(string summary, IReadOnlyList<PersonaEntry> personas)
	{
		base.Title = "Aquila · Import chats";
		base.Width = 540;
		base.SizeToContent = SizeToContent.Height;
		base.ResizeMode = ResizeMode.NoResize;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		CloseOnEscape = true;
		who = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 260 };
		who.Items.Add(new ComboBoxItem { Content = "Aquila (a regular chat)" });
		foreach (PersonaEntry p in personas)
		{
			who.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p });
		}
		who.SelectedIndex = personas.Count > 0 ? 1 : 0;
		StackPanel body = new StackPanel();
		TextBlock found = Ui.Text(summary, "Type.Body");
		found.TextWrapping = TextWrapping.Wrap;
		body.Children.Add(found);
		body.Children.Add(Ui.Field("Who was it with?", who, "A companion's imported chat looks and continues like one of hers, and she reads it as your past conversation. It isn't added to her long-term memory.", 16));
		Button import = Ui.Button("Import", "Btn.Primary", delegate
		{
			DialogResult = true;
		});
		import.IsDefault = true;
		Button cancel = Ui.Button("Cancel", "Btn.Secondary", delegate
		{
			Close();
		});
		base.Content = Ui.DialogScaffold("Import chats", "", body, null, cancel, import);
	}
}
