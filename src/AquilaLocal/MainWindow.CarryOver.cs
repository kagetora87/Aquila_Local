using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Priming;

namespace AquilaLocal;

/// <summary>
/// "Carry on in a new chat" (a chat's right-click menu): she reads the chat into a page she starts the next chat
/// with, adds the memories she's missing (you approve them), and the old chat can be archived.
/// </summary>
public partial class MainWindow
{
	public static RoutedUICommand CarryOverCommand { get; } = new RoutedUICommand("Carry on in a new chat", "CarryOverCommand", typeof(MainWindow));

	private void CarryOver_CanExecute(object sender, CanExecuteRoutedEventArgs e)
	{
		ChatSession? chat = DeleteTarget(e.Parameter);
		e.CanExecute = !busy && !attaching && !featuresClosing && chat != null && chat.IsCompanion && chat.Turns.Any((Turn t) => t.Status == "complete");
		e.Handled = true;
	}

	private void CarryOver_Executed(object sender, ExecutedRoutedEventArgs e)
	{
		e.Handled = true;
		ChatSession? chat = DeleteTarget(e.Parameter);
		if (chat == null || !chat.IsCompanion || busy || attaching)
		{
			return;
		}
		PrimeKit? kit = PrimeKitNow();
		string user = kit?.UserName.Length > 0 ? kit.UserName : "You";
		List<string> known;
		try
		{
			known = companion.Registry.OpenStore(chat.PersonaId).ActiveMemory().Select((AquilaLocal.Companion.MemoryEntry m) => m.Text).ToList();
		}
		catch (Exception ex) when (ex is PersonaConfigException || ex is DriveStateUnavailableException || ex is IOException || ex is UnauthorizedAccessException)
		{
			Notice("Carry over: " + chat.PersonaName + "'s memories couldn't be read (" + ex.Message + ").");
			return;
		}
		CarryOverRequest request = new CarryOverRequest
		{
			CompanionName = chat.PersonaName,
			Gender = GenderOf(chat.PersonaId),
			UserName = user,
			ChatTitle = chat.Title,
			KnownMemories = known
		};
		if (chat.CarryOver.Trim().Length > 0)
		{
			// What this chat itself started from, so nothing from before it is lost.
			request.Lines.Add(new CarryLine("(notes " + chat.PersonaName + " started this chat with)", chat.Turns.FirstOrDefault()?.Created ?? chat.Updated, chat.CarryOver.Trim()));
		}
		foreach (Turn t in chat.Turns.Where((Turn t) => t.Status == "complete"))
		{
			if (!string.IsNullOrWhiteSpace(t.User) && !t.User.TrimStart().StartsWith("[", StringComparison.Ordinal))
			{
				request.Lines.Add(new CarryLine(user, t.Created, t.User));
			}
			if (!string.IsNullOrWhiteSpace(t.Answer))
			{
				request.Lines.Add(new CarryLine(chat.PersonaName, t.AnsweredAt ?? t.Created, t.Answer));
			}
		}
		CarryOverWindow window = new CarryOverWindow(request, kit?.Generator, kit?.ContextTokens ?? settings.Context) { Owner = this };
		SetBusy(value: true);
		bool done;
		try
		{
			done = window.ShowDialog() == true;
		}
		finally
		{
			SetBusy(value: false);
		}
		if (!done)
		{
			return;
		}
		int added = 0;
		try
		{
			added = CarryOver.Remember(companion.Registry.OpenStore(chat.PersonaId), window.Memories, chat.Title);
		}
		catch (Exception ex) when (ex is PersonaConfigException || ex is DriveStateUnavailableException || ex is IOException || ex is UnauthorizedAccessException || ex is Microsoft.Data.Sqlite.SqliteException)
		{
			Notice("Carry over: the memories weren't saved (" + ex.Message + "). The new chat still starts with the page.");
		}
		if (window.ArchiveOld && !chat.Archived)
		{
			chat.Archived = true;
			try
			{
				store.SaveChat(chat);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				chat.Archived = false;
				Notice("The old chat couldn't be archived: " + ex.Message);
			}
		}
		ShowScreen(Screens.Chats);
		NewChat(chat.PersonaId, chat.PersonaName);
		if (current != null && current.PersonaId == chat.PersonaId && current.Turns.Count == 0)
		{
			current.CarryOver = window.Page;
			current.CarriedFrom = chat.Title;
			current.Title = chat.Title.StartsWith("Chat with ", StringComparison.Ordinal) ? chat.Title : chat.Title + " (continued)";
			try
			{
				store.SaveChat(current);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				Notice("The new chat couldn't be saved: " + ex.Message);
			}
			ChatTitle.Text = current.Title;
		}
		ApplyChatFilter();
		RefreshChatRows();
		Notice(chat.PersonaName + " carried over “" + chat.Title + "”" + (added > 0 ? " and remembered " + added + " new thing" + (added == 1 ? "" : "s") : "") + ". This chat picks up where you left off.");
	}
}
