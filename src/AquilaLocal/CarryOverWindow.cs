using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Priming;

namespace AquilaLocal;

/// <summary>
/// "Carry on in a new chat": she reads the chat, writes the page she'll start the next chat with (you can edit
/// it), and lists the memories she's missing for you to keep or skip.
/// </summary>
public sealed class CarryOverWindow : AquilaWindow
{
	private readonly CarryOverRequest request;

	private readonly CarryOver reader;

	private readonly StackPanel body = new StackPanel();

	private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap };

	private readonly Button start;

	private readonly Button run;

	private readonly TextBox page = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, MaxLength = CarryOver.MaxPageChars };

	private readonly List<(CheckBox Box, CarriedMemory Memory)> picks = new List<(CheckBox, CarriedMemory)>();

	private readonly CheckBox archive = new CheckBox { Content = "Archive this chat afterwards (nothing is deleted)", IsChecked = true, Margin = new Thickness(0, 14, 0, 0) };

	private CancellationTokenSource? cts;

	private string W(string text) => Genders.Words(text, request.Gender);

	/// <summary>The page as you left it, once you press Start.</summary>
	public string Page { get; private set; } = "";

	/// <summary>The memories you kept.</summary>
	public List<CarriedMemory> Memories { get; } = new List<CarriedMemory>();

	public bool ArchiveOld => archive.IsChecked == true;

	public CarryOverWindow(CarryOverRequest request, ITextGenerator? generator, int contextTokens)
	{
		this.request = request;
		reader = new CarryOver(generator ?? new NoModel()) { MaxPartChars = Math.Clamp((contextTokens - 10000) * 3, 6000, Primer.DefaultMaxPartChars) };
		base.Title = "Aquila · Carry on in a new chat";
		base.Width = 760;
		base.Height = 860;
		base.MinWidth = 600;
		base.MinHeight = 520;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		CloseOnEscape = true;

		int parts = reader.PartsFor(request);
		int chars = CarryOver.Transcript(request.Lines).Length;
		TimeSpan estimate = Primer.Estimate(parts, chars);
		body.Children.Add(Ui.Text(W(request.CompanionName + " reads all of “" + request.ChatTitle + "” (" + request.Lines.Count + " messages) and keeps what {s}'d carry with {o}: what happened, your stories and jokes, the little details, and where you left off. That becomes a page {s} starts the new chat with. {S} also checks {p} memories and suggests the ones {s}'s missing, in the order they happened. You choose what's kept."), "Type.Body"));
		TextBlock time = Ui.Text("About " + Math.Max(1, (int)Math.Round(estimate.TotalMinutes)) + " minute" + (estimate.TotalMinutes >= 1.5 ? "s" : "") + " with the chat model (" + parts + (parts == 1 ? " part" : " parts") + "). Your chats can't be used meanwhile.", "Type.Small");
		time.Margin = new Thickness(0, 8, 0, 0);
		body.Children.Add(time);
		if (generator == null)
		{
			TextBlock none = Ui.Text("Load the chat model first (Settings), then open this again.", "Type.Small");
			none.Foreground = Ui.Brush("Warning");
			none.Margin = new Thickness(0, 8, 0, 0);
			body.Children.Add(none);
		}
		run = Ui.Button(Ui.IconLabel("", "Read the chat"), "Btn.Primary", async delegate { await RunAsync(); });
		run.IsEnabled = generator != null && parts > 0;
		start = Ui.Button(Ui.IconLabel("", "Start the new chat"), "Btn.Primary", delegate { Finish(); });
		start.Visibility = Visibility.Collapsed;
		Button cancel = Ui.Button("Cancel", "Btn.Secondary", delegate
		{
			cts?.Cancel();
			Close();
		});
		base.Content = Ui.DialogScaffold("Carry on in a new chat", W("Pick up where you left off, with everything {s} should remember."), body, status, cancel, run, start);
		base.Closed += delegate { cts?.Cancel(); };
	}

	private async Task RunAsync()
	{
		run.IsEnabled = false;
		cts = new CancellationTokenSource();
		Progress<string> progress = new Progress<string>(s => Say(s, bad: false));
		try
		{
			CarryOverResult result = await reader.RunAsync(request, progress, cts.Token);
			Show(result);
			Say("Read " + result.PartsRead + (result.PartsRead == 1 ? " part" : " parts") + ". Change anything you like, then start the new chat.", bad: false);
		}
		catch (OperationCanceledException)
		{
			Say("Stopped. Nothing was saved.", bad: false);
			run.IsEnabled = true;
		}
		catch (Exception ex) when (ex is InvalidDataException || ex is InvalidOperationException || ex is System.Net.Http.HttpRequestException || ex is IOException || ex is System.Text.Json.JsonException)
		{
			Say(ex.Message, bad: true);
			run.IsEnabled = true;
		}
	}

	private void Show(CarryOverResult result)
	{
		body.Children.Clear();
		page.Text = result.Page;
		body.Children.Add(Ui.Field(W("The page {s} starts with"), page, W("Written for {o}, about you both. Fix anything that's wrong or add what it missed. It isn't shown in the chat."), 0));
		body.Children.Add(Ui.Section(result.Memories.Count == 0 ? W("Memories: {s} already has everything") : W("Memories {s} doesn't have yet (" + result.Memories.Count + ")"), 18));
		if (result.Memories.Count > 0)
		{
			TextBlock hint = Ui.Text("Ticked ones are added, oldest first. Untick any you don't want.", "Type.Small");
			hint.Margin = new Thickness(0, 0, 0, 6);
			body.Children.Add(hint);
		}
		foreach (CarriedMemory m in result.Memories)
		{
			CheckBox box = new CheckBox
			{
				IsChecked = true,
				Margin = new Thickness(0, 0, 0, 6),
				Content = new TextBlock { Text = m.At.ToLocalTime().ToString("MMM d") + " · " + Label(m.Layer) + " · " + m.Text, TextWrapping = TextWrapping.Wrap },
				ToolTip = m.Why.Length > 0 ? m.Why : null
			};
			picks.Add((box, m));
			body.Children.Add(box);
		}
		body.Children.Add(archive);
		run.Visibility = Visibility.Collapsed;
		start.Visibility = Visibility.Visible;
	}

	private static string Label(string layer) => layer switch
	{
		MemoryLayers.Feelings => "feeling",
		MemoryLayers.Hooks => "to come back to",
		_ => "fact"
	};

	private void Finish()
	{
		if (page.Text.Trim().Length == 0)
		{
			Say("The page is empty. Write something there, or cancel.", bad: true);
			return;
		}
		Page = page.Text.Trim();
		Memories.AddRange(picks.Where(p => p.Box.IsChecked == true).Select(p => p.Memory));
		DialogResult = true;
	}

	private void Say(string text, bool bad)
	{
		status.Text = text;
		status.Foreground = Ui.Brush(bad ? "Danger" : "Text.Secondary");
	}

	private sealed class NoModel : ITextGenerator
	{
		public Task<string> GenerateJsonAsync(string system, string user, int maxTokens, CancellationToken ct) => throw new InvalidOperationException("Load the chat model first (Settings), then try again.");
	}
}
