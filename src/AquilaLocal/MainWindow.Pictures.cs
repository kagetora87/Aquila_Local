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

	private PictureEngine PictureDrawer => pictureEngine ??= new PictureEngine(Path.Combine(AppContext.BaseDirectory, "imagegen"));

	private string PicturesRoot => Path.Combine(store.Root, "pictures");

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
			string prompt = await WritePicturePromptAsync(request, conversation, name, appearance, references.Count > 0, session.Location, ct);
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
	private async Task<string> WritePicturePromptAsync(PictureRequest request, string conversation, string? name, string? appearance, bool hasReferences, string location, CancellationToken ct)
	{
		JsonObject payload = new JsonObject
		{
			["model"] = engine.Alias,
			["id_slot"] = 1,
			["messages"] = new JsonArray
			{
				new JsonObject { ["role"] = "system", ["content"] = PicturePrompt.WriterSystem },
				new JsonObject { ["role"] = "user", ["content"] = PicturePrompt.WriterUser(request, conversation, name, appearance, hasReferences, location) }
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
	private async Task<string> DrawAsync(string model, string prompt, (int Width, int Height) size, IReadOnlyList<string> references, string about, CancellationToken ct)
	{
		string output = PictureFiles.NewOutput(PicturesRoot, about, DateTimeOffset.Now);
		bool paused = false;
		bool restore = false;
		try
		{
			if (model == PictureModels.Best && engine.Ready)
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
