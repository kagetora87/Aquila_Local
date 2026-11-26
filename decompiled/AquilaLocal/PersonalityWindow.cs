using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace AquilaLocal;

public class PersonalityWindow : Window, IComponentConnector
{
	private readonly PersonalityStore store;

	private readonly Action<string> use;

	private PersonalityProfile? selected;

	internal TextBlock ProfileStatus;

	internal ListBox ProfileList;

	internal Button SaveProfileButton;

	internal Button DeleteProfileButton;

	internal TextBox ProfileNameBox;

	internal TextBox ProfileStyleBox;

	internal TextBox ProfileExamplesBox;

	internal CheckBox ProfileLearningCheck;

	private bool _contentLoaded;

	public PersonalityWindow(PersonalityStore store, string activeId, Action<string> use)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		InitializeComponent();
		this.store = store;
		this.use = use;
		double height = ((FrameworkElement)this).Height;
		Rect workArea = SystemParameters.WorkArea;
		((FrameworkElement)this).Height = Math.Min(height, ((Rect)(ref workArea)).Height - 30.0);
		Reload(activeId);
	}

	private void Reload(string? id = null)
	{
		string id2 = id;
		((ItemsControl)ProfileList).ItemsSource = store.List();
		((Selector)ProfileList).SelectedItem = ((IEnumerable)((ItemsControl)ProfileList).Items).Cast<PersonalityProfile>().FirstOrDefault((PersonalityProfile p) => p.Id == id2) ?? ((ItemsControl)ProfileList).Items[0];
		ProfileStatus.Text = store.RecoveryNotice ?? "Save your changes, then use the selected profile for future messages.";
	}

	private void Selection_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (((Selector)ProfileList).SelectedItem is PersonalityProfile profile)
		{
			Edit(profile);
		}
	}

	private void Edit(PersonalityProfile profile)
	{
		PersonalityProfile profile2 = profile;
		selected = profile2;
		ProfileNameBox.Text = profile2.Name;
		ProfileStyleBox.Text = profile2.Style;
		ProfileExamplesBox.Text = profile2.Examples;
		((ToggleButton)ProfileLearningCheck).IsChecked = profile2.LearnMemories;
		bool flag = profile2.Id != "default";
		((TextBoxBase)ProfileNameBox).IsReadOnly = !flag;
		((TextBoxBase)ProfileStyleBox).IsReadOnly = !flag;
		((TextBoxBase)ProfileExamplesBox).IsReadOnly = !flag;
		((UIElement)ProfileLearningCheck).IsEnabled = flag;
		((UIElement)SaveProfileButton).IsEnabled = flag;
		((UIElement)DeleteProfileButton).IsEnabled = flag && ((IEnumerable)((ItemsControl)ProfileList).Items).Cast<PersonalityProfile>().Any((PersonalityProfile p) => p.Id == profile2.Id);
		if (!flag)
		{
			ProfileStatus.Text = "Default uses your existing global instructions. Create or duplicate a profile to customize a speaking style.";
		}
	}

	private PersonalityProfile Read()
	{
		return new PersonalityProfile
		{
			Id = (selected?.Id ?? Guid.NewGuid().ToString("N")),
			Name = ProfileNameBox.Text,
			Style = ProfileStyleBox.Text,
			Examples = ProfileExamplesBox.Text,
			LearnMemories = ((ToggleButton)ProfileLearningCheck).IsChecked.GetValueOrDefault()
		};
	}

	private void New_Click(object sender, RoutedEventArgs e)
	{
		((Selector)ProfileList).SelectedItem = null;
		Edit(new PersonalityProfile());
		((UIElement)ProfileNameBox).Focus();
		((TextBoxBase)ProfileNameBox).SelectAll();
	}

	private void Duplicate_Click(object sender, RoutedEventArgs e)
	{
		PersonalityProfile personalityProfile = Read();
		personalityProfile.Id = Guid.NewGuid().ToString("N");
		personalityProfile.Name = (personalityProfile.Name + " copy").Truncate(60);
		((Selector)ProfileList).SelectedItem = null;
		Edit(personalityProfile);
	}

	private bool Save()
	{
		if (selected?.Id == "default")
		{
			return true;
		}
		try
		{
			PersonalityProfile personalityProfile = Read();
			store.Save(personalityProfile);
			Reload(personalityProfile.Id);
			ProfileStatus.Text = "Profile saved. Use selected profile to activate it.";
			return true;
		}
		catch (Exception ex)
		{
			ProfileStatus.Text = ex.Message;
			return false;
		}
	}

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		Save();
	}

	private void Use_Click(object sender, RoutedEventArgs e)
	{
		if (selected != null && Save())
		{
			use(selected.Id);
			((Window)this).Close();
		}
	}

	private void Delete_Click(object sender, RoutedEventArgs e)
	{
		if (selected == null)
		{
			return;
		}
		try
		{
			store.Delete(selected.Id);
			Reload("default");
			ProfileStatus.Text = "Profile removed. Default is selected.";
		}
		catch (Exception ex)
		{
			ProfileStatus.Text = ex.Message;
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.31.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri uri = new Uri("/Aquila Local;component/personalitywindow.xaml", UriKind.Relative);
			Application.LoadComponent((object)this, uri);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.31.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Expected O, but got Unknown
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Expected O, but got Unknown
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Expected O, but got Unknown
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Expected O, but got Unknown
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Expected O, but got Unknown
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Expected O, but got Unknown
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Expected O, but got Unknown
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Expected O, but got Unknown
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e2: Expected O, but got Unknown
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f9: Expected O, but got Unknown
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0106: Expected O, but got Unknown
		//IL_0109: Unknown result type (might be due to invalid IL or missing references)
		//IL_0113: Expected O, but got Unknown
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_0120: Expected O, but got Unknown
		//IL_0123: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Expected O, but got Unknown
		switch (connectionId)
		{
		case 1:
			ProfileStatus = (TextBlock)target;
			break;
		case 2:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(New_Click);
			break;
		case 3:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(Duplicate_Click);
			break;
		case 4:
			ProfileList = (ListBox)target;
			((Selector)ProfileList).SelectionChanged += new SelectionChangedEventHandler(Selection_Changed);
			break;
		case 5:
			SaveProfileButton = (Button)target;
			((ButtonBase)SaveProfileButton).Click += new RoutedEventHandler(Save_Click);
			break;
		case 6:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(Use_Click);
			break;
		case 7:
			DeleteProfileButton = (Button)target;
			((ButtonBase)DeleteProfileButton).Click += new RoutedEventHandler(Delete_Click);
			break;
		case 8:
			ProfileNameBox = (TextBox)target;
			break;
		case 9:
			ProfileStyleBox = (TextBox)target;
			break;
		case 10:
			ProfileExamplesBox = (TextBox)target;
			break;
		case 11:
			ProfileLearningCheck = (CheckBox)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
