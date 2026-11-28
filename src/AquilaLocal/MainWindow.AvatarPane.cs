using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AquilaLocal.Companion;

namespace AquilaLocal;

/// <summary>Under the avatar: her name in her favourite colour, what she's doing, where you are, and who else is here.</summary>
public partial class MainWindow
{
	/// <summary>The last colour problem shown, so it's said once, not on every message.</summary>
	private string colourProblemShown = "";

	/// <summary>Her name and colour, for the chat that's open (Aquila's when it isn't a persona chat).</summary>
	private void RefreshAvatarIdentity()
	{
		bool persona = current != null && current.IsCompanion && current.PersonaId.Length > 0;
		string key = persona ? current!.PersonaId : "";
		ShowAvatarStatus();
		ShowMood();
		RefreshJournalButton();
		if (daydreamOf.Length > 0 && daydreamOf != key)
		{
			ClearDaydream();
		}
		PictureMomentButton.Visibility = (persona && settings.Pictures.Enabled) ? Visibility.Visible : Visibility.Collapsed;
		AvatarName.Text = persona ? current!.PersonaName : "Aquila";
		AvatarName.Foreground = Ui.Brush("Accent");
		if (!persona)
		{
			UseSpriteRoot(sharedSpriteRoot);
			return;
		}
		string colour = "";
		string avatarSetting = "";
		try
		{
			PersonaConfig config = companion.Registry.LoadConfig(key);
			colour = config.FavoriteColor.Trim();
			avatarSetting = config.Avatar;
		}
		catch (Exception ex) when (ex is PersonaConfigException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
		{
			SayColourProblem("Couldn't read " + current!.PersonaName + "'s settings for her colour and avatar: " + ex.Message);
			return;
		}
		UseSpriteRoot(companion.AvatarFolder(avatarSetting) ?? sharedSpriteRoot);
		if (colour.Length == 0)
		{
			return;
		}
		if (TryColour(colour, out Color c))
		{
			AvatarName.Foreground = new SolidColorBrush(c);
		}
		else
		{
			SayColourProblem(current!.PersonaName + "'s favourite colour \u201C" + colour + "\u201D isn't a colour Aquila knows. Use a name like teal or a code like #4FC3F7 (Companions \u2192 Settings).");
		}
	}

	private void SayColourProblem(string problem)
	{
		if (problem != colourProblemShown)
		{
			colourProblemShown = problem;
			Notice(problem);
		}
	}

	/// <summary>Switches the avatar to another set of animations (a persona's own, or the shared one).
	/// A new, empty set starts with the placeholder pictures so she's never blank.</summary>
	private void UseSpriteRoot(string root)
	{
		if (spriteView == null || root.Length == 0 || string.Equals(System.IO.Path.GetFullPath(root), System.IO.Path.GetFullPath(spriteRoot), StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		try
		{
			if (!System.IO.Directory.Exists(root) || SpriteLibrary.Load(root).Count == 0)
			{
				AvatarPlaceholders.EnsureIn(root);
			}
		}
		catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
		{
			SayColourProblem("Couldn't open the avatar folder " + root + ": " + ex.Message);
			return;
		}
		// The Animations window edits one set; close it so it never edits the wrong one.
		animationsWindow?.Close();
		spriteRoot = root;
		ReloadSpriteAvatar();
	}

	internal static bool TryColour(string text, out Color colour)
	{
		colour = default;
		try
		{
			string t = text.Trim();
			if (t.Length == 0)
			{
				return false;
			}
			object? parsed = ColorConverter.ConvertFromString(t.Replace(" ", ""));
			if (parsed is Color c)
			{
				colour = c;
				return true;
			}
		}
		catch (FormatException)
		{
		}
		return false;
	}

	private void ShowAvatarStatus()
	{
		AvatarStatus.Text = avatarState switch
		{
			"thinking" => "Thinking\u2026",
			"speaking" => "Talking",
			"talking" => "Talking",
			"listening" => "Listening",
			"interrupted" => "Interrupted",
			_ => "Idle",
		};
	}

	/// <summary>Everyone else in the open group scene, first to arrive on the left.</summary>
	private void ShowRoom(IReadOnlyList<(string Id, string Name)> cast)
	{
		string me = (current != null && current.IsCompanion) ? current.PersonaId : "";
		List<(string Id, string Name)> others = cast.Where(((string Id, string Name) m) => m.Id != me).ToList();
		RoomPeople.Children.Clear();
		foreach ((string id, string name) in others)
		{
			ImageSource? picture = null;
			try
			{
				picture = companion.ProfilePicture(id);
			}
			catch (Exception ex) when (ex is PersonaConfigException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
			{
			}
			StackPanel tile = new StackPanel { Width = 88.0, Margin = new Thickness(6.0, 0.0, 6.0, 12.0) };
			UIElement face = (picture != null)
				? new Ellipse { Width = 56.0, Height = 56.0, Fill = new ImageBrush(picture) { Stretch = Stretch.UniformToFill }, HorizontalAlignment = HorizontalAlignment.Center }
				: new Border
				{
					Width = 56.0,
					Height = 56.0,
					CornerRadius = new CornerRadius(28.0),
					Background = Ui.Brush("Accent.Subtle"),
					HorizontalAlignment = HorizontalAlignment.Center,
					Child = new TextBlock { Text = name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?", FontSize = 22.0, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("Accent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
				};
			tile.Children.Add(face);
			tile.Children.Add(new TextBlock { Text = name, FontSize = 13.0, Foreground = Ui.Brush("Text.Primary"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0.0, 6.0, 0.0, 0.0) });
			RoomPeople.Children.Add(tile);
		}
		RoomSection.Visibility = (others.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
	}
}
