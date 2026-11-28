using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Pictures;

namespace AquilaLocal;

/// <summary>
/// Built-in pictures: Vicki or Aquila call make_picture, the chat model writes the image prompt (from the request,
/// the recent chat and her appearance card), and the image engine draws it with her reference pictures.
/// The best-quality model needs the chat model paused; its working memory is saved and restored around it.
/// </summary>
public partial class MainWindow
{
	private PictureEngine? pictureEngine;

	private PictureEngine PictureDrawer
	{
		get
		{
			if (pictureEngine == null)
			{
				pictureEngine = new PictureEngine(Path.Combine(AppContext.BaseDirectory, "imagegen"));
				// Set once here; the Pictures window changes it while you try things and puts it back on Cancel.
				pictureEngine.Kit.AnimeAvoid = settings.Pictures.AnimeAvoid ?? PictureCommand.AnimeAvoidDefault;
			}
			// The chosen text encoder for FLUX 3 Image Klein (settings, Pictures window or the Generate screen).
			pictureEngine.Kit.KleinEncoder = KleinEncoders.IsKnown(settings.Pictures.KleinEncoder) ? settings.Pictures.KleinEncoder : KleinEncoders.Standard;
			return pictureEngine;
		}
	}

	private string PicturesRoot => Path.Combine(store.Root, "pictures");

	private bool drawingMoment;

	/// <summary>
	/// "Picture this moment": she draws what she imagines the two of you are doing right now, from the last few
	/// messages, where you are and how she looks, the same way her make_picture tool does. It's shown under her latest reply.
	/// </summary>
	/// <summary>A small "are you sure?" right at the mouse, so a stray click doesn't start a picture.</summary>
	private void PictureMoment_Click(object sender, RoutedEventArgs e)
	{
		if (busy || drawingMoment || current == null || !current.IsCompanion)
		{
			return;
		}
		System.Windows.Controls.Primitives.Popup ask = new System.Windows.Controls.Primitives.Popup
		{
			Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
			StaysOpen = false,
			AllowsTransparency = true,
			PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade
		};
		System.Windows.Controls.StackPanel panel = new System.Windows.Controls.StackPanel();
		panel.Children.Add(Ui.Text("Picture this moment?", "Type.Body"));
		System.Windows.Controls.StackPanel buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
		System.Windows.Controls.Button no = Ui.Button("No", "Btn.Secondary", delegate
		{
			ask.IsOpen = false;
		});
		string her = (current.IsCompanion ? Genders.Object(GenderOf(current.PersonaId)) : "them");
		System.Windows.Controls.Button secret = Ui.Button("Just for me", "Btn.Secondary", delegate
		{
			ask.IsOpen = false;
			_ = PictureMomentAsync(tell: false);
		});
		secret.ToolTip = current.PersonaName + " won't know you took it: the picture is only shown to you, and " + her + " isn't told.";
		secret.Margin = new Thickness(8, 0, 0, 0);
		System.Windows.Controls.Button yes = Ui.Button("Yes", "Btn.Primary", delegate
		{
			ask.IsOpen = false;
			_ = PictureMomentAsync();
		});
		yes.ToolTip = current.PersonaName + " sees the picture and reacts to it.";
		yes.Margin = new Thickness(8, 0, 0, 0);
		buttons.Children.Add(no);
		buttons.Children.Add(secret);
		buttons.Children.Add(yes);
		panel.Children.Add(buttons);
		ask.Child = new System.Windows.Controls.Border { Style = Ui.Style("Popup.Surface"), Padding = new Thickness(14, 12, 14, 12), Child = panel };
		ask.Opened += delegate
		{
			yes.Focus();
		};
		ask.IsOpen = true;
	}

	/// <param name="tell">False: "Just for me". The picture is shown to you only; she isn't told and doesn't react.</param>
	private async Task PictureMomentAsync(bool tell = true)
	{
		if (pendingSpeechResume is { IsCompleted: false })
		{
			await pendingSpeechResume;
		}
		ChatSession? session = current;
		if (busy || drawingMoment || session == null || !session.IsCompanion)
		{
			return;
		}
		Turn? last = session.Turns.LastOrDefault((Turn t) => t.Status == "complete");
		if (last == null)
		{
			Notice("Talk with " + session.PersonaName + " first; then she can picture the moment.");
			return;
		}
		if (!engine.Ready)
		{
			Notice("Load the model first: " + session.PersonaName + " writes the picture's description herself.");
			return;
		}
		(string File, string Result)? drawn = null;
		drawingMoment = true;
		// Hold new messages while she draws: the picture uses the model and may pause it.
		SetBusy(value: true);
		try
		{
			string request = System.Text.Json.JsonSerializer.Serialize(new
			{
				description = "This very moment, as you imagine it: what the two of you are doing right now in the latest messages, where you are, your poses, expressions and the light. Show both of you if you're together.",
				includes_you = true,
				framing = "scene"
			});
			// Failures are already shown as a notice by the picture tool itself.
			int before = last.Pictures.Count;
			string result = await MakePictureForTurnAsync(session, last, request, CancellationToken.None);
			store.SaveChat(session);
			if (last.Pictures.Count > before)
			{
				drawn = (last.Pictures[^1], result);
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
		{
			Notice("Picture this moment: " + ex.Message);
		}
		finally
		{
			drawingMoment = false;
			SetBusy(value: false);
		}
		if (drawn.HasValue && !tell)
		{
			Notice("Picture saved just for you. " + session.PersonaName + " doesn't know you took it.");
		}
		if (drawn.HasValue && tell && current == session)
		{
			await ReactToMomentAsync(session, drawn.Value.File, drawn.Value.Result);
		}
	}

	/// <summary>She's told they asked her to picture the moment, and what she drew, and replies to it in her own words.
	/// With a vision model she also sees the picture itself.</summary>
	private async Task ReactToMomentAsync(ChatSession session, string file, string result)
	{
		int shows = result.IndexOf("It shows: ", StringComparison.Ordinal);
		string what = (shows >= 0) ? result.Substring(shows + "It shows: ".Length) : "";
		int tail = what.IndexOf(" Don't describe it", StringComparison.Ordinal);
		if (tail >= 0)
		{
			what = what.Substring(0, tail);
		}
		Attachment? picture = null;
		if (!string.IsNullOrWhiteSpace(settings.ProjectorPath))
		{
			try
			{
				picture = Attachments.Read(file);
				picture.Name = "The picture you just drew of this moment:";
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is InvalidOperationException || ex is ArgumentException)
			{
				picture = null;
			}
		}
		string cue = "[No message from them. They just pressed \"Picture this moment\", asking you to picture what the two of you are doing right now, and you drew it. It's shown in the chat under your last message."
			+ ((what.Length > 0) ? (" Your picture shows: " + what.Trim()) : "")
			+ " React to it now, in your own voice and in the moment: how you feel about it, what you notice or like, or what it makes you want to do. Keep it short. Don't describe the picture back to them or mention prompts or tools.]";
		await SendAsync(cue, picture, quiet: true);
	}

	/// <summary>The make_picture tool: returns what the model is told (never throws for an ordinary failure).</summary>
	private async Task<string> MakePictureForTurnAsync(ChatSession session, Turn turn, string arguments, CancellationToken ct)
	{
		if (!settings.Pictures.Enabled)
		{
			return "Not drawn: pictures are switched off.";
		}
		PictureRequest request;
		try
		{
			request = PictureRequest.Parse(arguments);
		}
		catch (PersonaConfigException ex)
		{
			return "Not drawn: " + ex.Message;
		}
		string model = settings.Pictures.Model;
		(bool ready, string problem) = PictureDrawer.Kit.Check(model);
		if (!ready)
		{
			Notice("Pictures: " + problem);
			return "Not drawn: the image generator isn't installed. Tell them briefly that the picture couldn't be made.";
		}
		try
		{
			string speaker = session.IsCompanion ? session.PersonaName : "Aquila";
			string? name = null;
			string? appearance = null;
			List<string> references = new List<string>();
			if (session.IsCompanion && request.IncludesPersona)
			{
				name = session.PersonaName;
				CompanionRuntime runtime = companion.Open(session.PersonaId);
				try
				{
					appearance = AppearanceCaption.For(name, runtime.Store.ReadAppearance());
				}
				catch (AppearanceCardUnavailableException)
				{
					appearance = CompanionHost.Looks(runtime);
				}
				if (settings.Pictures.UseReferences)
				{
					references = PictureFiles.References(companion.Folder(session.PersonaId)).Take(PictureCommand.MaxReferences).ToList();
				}
			}
			List<(string, string)> recent = session.Turns.Where((Turn t) => t != turn && t.Status == "complete").TakeLast(4).Select((Turn t) => (t.User, t.Answer)).ToList();
			recent.Add((turn.User, turn.Answer));
			string conversation = PicturePrompt.Conversation(recent, speaker);
			PictureStatus("Writing the picture prompt…");
			string prompt = await WritePicturePromptAsync(request, conversation, name, appearance, references.Count > 0, session.Location, ct, session.IsCompanion ? GenderOf(session.PersonaId) : Genders.Female, model);
			string output = await DrawAsync(model, PicturePrompt.WithReferences(prompt, references.Count), request.Size(), references, request.Description, ct);
			turn.Pictures.Add(output);
			if (replyViews.TryGetValue(turn.Id, out MessageView? view))
			{
				view.ShowPictureFiles(turn.Pictures);
			}
			return "Picture drawn and shown in the chat. It shows: " + prompt.Truncate(500) + " Don't describe it at length or mention the tool; carry on naturally.";
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex) when (ex is IOException || ex is PersonaConfigException || ex is HttpRequestException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
		{
			Notice("Picture: " + ex.Message);
			return "Not drawn: " + ex.Message.Truncate(300) + " Tell them briefly that the picture didn't work.";
		}
		finally
		{
			PictureStatus(null);
		}
	}

	/// <summary>The chat model writes the image prompt on its second slot, so the conversation's cache stays as it is.</summary>
	private async Task<string> WritePicturePromptAsync(PictureRequest request, string conversation, string? name, string? appearance, bool hasReferences, string location, CancellationToken ct, string gender = Genders.Female, string model = PictureModels.Fast)
	{
		hasReferences &= PictureModels.TakesReferences(model);
		JsonObject payload = new JsonObject
		{
			["model"] = engine.Alias,
			["id_slot"] = 1,
			["messages"] = new JsonArray
			{
				new JsonObject { ["role"] = "system", ["content"] = PictureModels.UsesTags(model) ? PicturePrompt.WriterSystemTags : PicturePrompt.WriterSystem },
				new JsonObject { ["role"] = "user", ["content"] = PicturePrompt.WriterUser(request, conversation, name, appearance, hasReferences, location, gender) }
			},
			["stream"] = false,
			["temperature"] = 0.7,
			["max_tokens"] = 400,
			["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false }
		};
		using HttpResponseMessage response = await engine.PostAsync("v1/chat/completions", payload, stream: false, ct);
		JsonNode? node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
		string written = node?["choices"]?.AsArray().FirstOrDefault()?["message"]?["content"]?.GetValue<string>() ?? "";
		return PicturePrompt.Clean(written);
	}

	/// <summary>Draws one picture (pausing the chat model for the best model) and saves the prompt beside it.</summary>
	private async Task<string> DrawAsync(string model, string prompt, (int Width, int Height) size, IReadOnlyList<string> references, string about, CancellationToken ct, string? saveAs = null)
	{
		if (!PictureModels.TakesReferences(model))
		{
			references = Array.Empty<string>();
			prompt = PicturePrompt.WithoutReferences(prompt);
		}
		size = PictureModels.DrawSize(model, size);
		string output = saveAs ?? PictureFiles.NewOutput(PicturesRoot, about, DateTimeOffset.Now);
		bool paused = false;
		bool restore = false;
		try
		{
			// The anime model peaked at about 10 GB on the laptop, more than is left beside the chat model.
			if ((model == PictureModels.Best || model == PictureModels.Anime) && engine.Ready)
			{
				PictureStatus("Pausing the chat model to make room for the picture…");
				restore = await engine.ParkAsync(ct);
				paused = true;
			}
			PictureStatus("Drawing a picture…");
			long seed = Random.Shared.NextInt64(1, int.MaxValue);
			PictureJob job = new PictureJob(prompt, size.Width, size.Height, references, seed, output);
			Action<int, int> progress = delegate(int step, int of)
			{
				base.Dispatcher.InvokeAsync(() => PictureStatus($"Drawing a picture… {step}/{of}"));
			};
			try
			{
				await PictureDrawer.DrawAsync(model, job, progress, ct);
			}
			catch (IOException first) when (!paused && engine.Ready && !ct.IsCancellationRequested)
			{
				// The fast model usually fits beside the chat model, but a long chat (or reference pictures)
				// can leave too little graphics memory. Pause the chat model and try once more; if that
				// fails too, both errors are shown.
				PictureStatus("Not enough graphics memory beside the chat model. Pausing it and trying again…");
				restore = await engine.ParkAsync(ct);
				paused = true;
				try
				{
					await PictureDrawer.DrawAsync(model, job, progress, ct);
				}
				catch (IOException second)
				{
					throw new IOException(second.Message + Environment.NewLine + Environment.NewLine + "(It also failed beside the chat model: " + first.Message + ")", second);
				}
			}
			File.WriteAllText(Path.ChangeExtension(output, ".txt"), prompt + Environment.NewLine + Environment.NewLine + "Model: " + PictureModels.Label(model) + Environment.NewLine + "Seed: " + seed + Environment.NewLine + "References: " + string.Join(", ", references.Select(Path.GetFileName)));
			return output;
		}
		finally
		{
			if (paused)
			{
				PictureStatus("Loading the chat model again…");
				await engine.ResumeAsync(settings, restore, CancellationToken.None);
			}
		}
	}

	/// <summary>Where the story is, shown in the header of persona chats.</summary>
	private void ShowLocation()
	{
		if (current == null || !current.IsCompanion)
		{
			LocationChip.Visibility = Visibility.Collapsed;
			return;
		}
		LocationChip.Visibility = Visibility.Visible;
		LocationChipText.Text = current.Location.Length > 0 ? current.Location : "Where are we?";
		ShowOutfit();
	}

	/// <summary>What she's wearing, under the location: the latest entry of her wardrobe diary.</summary>
	private void ShowOutfit()
	{
		if (current == null || !current.IsCompanion || current.PersonaId.Length == 0)
		{
			OutfitChip.Visibility = Visibility.Collapsed;
			return;
		}
		try
		{
			AquilaLocal.Companion.PersonaStore herStore = companion.Open(current.PersonaId).Store;
			if (!herStore.HasAppearance)
			{
				OutfitChip.Visibility = Visibility.Collapsed;
				return;
			}
			AquilaLocal.Companion.Wardrobe w = herStore.ReadAppearance().Wardrobe.Wardrobe;
			string colour = w.Color.Trim();
			string outfit = w.Outfit.Trim();
			OutfitChipText.Text = (colour.Length > 0 && !outfit.Contains(colour, StringComparison.OrdinalIgnoreCase)) ? (colour + " " + outfit) : outfit;
			OutfitChip.ToolTip = "What she's wearing: " + AquilaLocal.Companion.ContextInjector.WardrobeText(w) + "\nClick to see her wardrobe diary or change it.";
			OutfitChip.Visibility = outfit.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
		}
		catch (Exception ex) when (ex is AquilaLocal.Companion.PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is Microsoft.Data.Sqlite.SqliteException)
		{
			OutfitChip.Visibility = Visibility.Visible;
			OutfitChipText.Text = "Outfit unknown";
			OutfitChip.ToolTip = "Her outfit couldn't be read: " + ex.Message;
		}
	}

	private void Outfit_Click(object sender, RoutedEventArgs e)
	{
		if (current == null || !current.IsCompanion)
		{
			return;
		}
		if (busy)
		{
			Notice("Wait for the reply to finish, then open her wardrobe diary.");
			return;
		}
		OpenCompanions(current.PersonaId, null, new List<long>());
	}

	/// <summary>A new chat with her starts where the last one left off.</summary>
	private string LastLocation(string personaId)
	{
		if (personaId.Length == 0)
		{
			return "";
		}
		return chats.Where((ChatSession c) => c.PersonaId == personaId && c.Location.Length > 0).OrderByDescending((ChatSession c) => c.Updated).FirstOrDefault()?.Location ?? "";
	}

	private void Location_Click(object sender, RoutedEventArgs e)
	{
		if (current == null || !current.IsCompanion)
		{
			return;
		}
		ChatSession session = current;
		LocationWindow window = new LocationWindow(session.PersonaName, session.Location) { Owner = this };
		if (window.ShowDialog() == true)
		{
			session.Location = window.Place;
			try
			{
				store.SaveChat(session);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				Notice("Couldn't save the location: " + ex.Message);
			}
			ShowLocation();
		}
	}

	private void PictureStatus(string? text)
	{
		if (featuresClosing)
		{
			return;
		}
		StatusText.Text = text ?? (engine.Ready ? "Ready" : "No model loaded");
	}

	private void Pictures_Click(object sender, RoutedEventArgs e)
	{
		if (busy)
		{
			Notice("Wait until the current reply finishes, then open Pictures.");
			return;
		}
		PicturesWindow window = new PicturesWindow(settings, PictureDrawer, companion, PicturesRoot, delegate
		{
			store.SaveSettings(settings);
		}, (string model, string prompt, (int, int) size, IReadOnlyList<string> refs, CancellationToken ct) => DrawAsync(model, prompt, size, refs, "test", ct))
		{
			Owner = this
		};
		window.ShowDialog();
	}
}
