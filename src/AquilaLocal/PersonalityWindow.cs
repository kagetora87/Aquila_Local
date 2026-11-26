using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace AquilaLocal;

public partial class PersonalityWindow : Window
{
	private readonly PersonalityStore store;

	private readonly Action<string> use;

	private PersonalityProfile? selected;










	public PersonalityWindow(PersonalityStore store, string activeId, Action<string> use)
	{
		InitializeComponent();
		this.store = store;
		this.use = use;
		base.Height = Math.Min(base.Height, SystemParameters.WorkArea.Height - 30.0);
		Reload(activeId);
	}

	private void Reload(string? id = null)
	{
		ProfileList.ItemsSource = store.List();
		ProfileList.SelectedItem = ProfileList.Items.Cast<PersonalityProfile>().FirstOrDefault((PersonalityProfile p) => p.Id == id) ?? ProfileList.Items[0];
		ProfileStatus.Text = store.RecoveryNotice ?? "Save your changes, then use the selected profile for future messages.";
	}

	private void Selection_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (ProfileList.SelectedItem is PersonalityProfile profile)
		{
			Edit(profile);
		}
	}

	private void Edit(PersonalityProfile profile)
	{
		selected = profile;
		ProfileNameBox.Text = profile.Name;
		ProfileStyleBox.Text = profile.Style;
		ProfileExamplesBox.Text = profile.Examples;
		ProfileLearningCheck.IsChecked = profile.LearnMemories;
		bool flag = profile.Id != "default";
		ProfileNameBox.IsReadOnly = !flag;
		ProfileStyleBox.IsReadOnly = !flag;
		ProfileExamplesBox.IsReadOnly = !flag;
		ProfileLearningCheck.IsEnabled = flag;
		SaveProfileButton.IsEnabled = flag;
		DeleteProfileButton.IsEnabled = flag && ProfileList.Items.Cast<PersonalityProfile>().Any((PersonalityProfile p) => p.Id == profile.Id);
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
			LearnMemories = (ProfileLearningCheck.IsChecked == true)
		};
	}

	private void New_Click(object sender, RoutedEventArgs e)
	{
		ProfileList.SelectedItem = null;
		Edit(new PersonalityProfile());
		ProfileNameBox.Focus();
		ProfileNameBox.SelectAll();
	}

	private void Duplicate_Click(object sender, RoutedEventArgs e)
	{
		PersonalityProfile personalityProfile = Read();
		personalityProfile.Id = Guid.NewGuid().ToString("N");
		personalityProfile.Name = (personalityProfile.Name + " copy").Truncate(60);
		ProfileList.SelectedItem = null;
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
			Close();
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
}
