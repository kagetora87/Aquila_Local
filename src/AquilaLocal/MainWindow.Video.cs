using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using AquilaLocal.Companion;
using AquilaLocal.Companion.Pictures;

namespace AquilaLocal;

/// <summary>
/// Built-in video: a still is drawn like a picture (her appearance and references keep her looking right), then
/// Wan 2.2 animates it into a 5-second clip. The chat model is paused while the clip is made (it needs ~21 GB).
/// </summary>
public partial class MainWindow
{
	/// <summary>The make_video tool: returns what the model is told (never throws for an ordinary failure).</summary>
	private async Task<string> MakeVideoForTurnAsync(ChatSession session, Turn turn, string arguments, CancellationToken ct)
	{
		if (!settings.Video.Enabled)
		{
			return "Not made: video is switched off.";
		}
		VideoRequest request;
		try
		{
			request = VideoRequest.Parse(arguments);
		}
		catch (PersonaConfigException ex)
		{
			return "Not made: " + ex.Message;
		}
		string model = settings.Pictures.Model;
		(bool pictureReady, string pictureProblem) = PictureDrawer.Kit.Check(model);
		(bool videoReady, string videoProblem) = PictureDrawer.Video.Check();
		if (!pictureReady || !videoReady)
		{
			Notice("Video: " + (pictureReady ? videoProblem : pictureProblem));
			return "Not made: the video generator isn't installed. Tell them briefly that the video couldn't be made.";
		}
		try
		{
			string speaker = session.IsCompanion ? session.PersonaName : "Aquila";
			string? name = null;
			string? appearance = null;
			List<string> references = new List<string>();
			if (session.IsCompanion && request.Still.IncludesPersona)
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
			PictureStatus("Writing the video's first frame…");
			string prompt = await WritePicturePromptAsync(request.Still, PicturePrompt.Conversation(recent, speaker), name, appearance, references.Count > 0, session.Location, ct, session.IsCompanion ? GenderOf(session.PersonaId) : Genders.Female, model, session.IsCompanion ? YouAsSeenBy(session.PersonaId) : null);
			string still = await DrawAsync(model, PicturePrompt.WithReferences(prompt, references.Count), VideoRequest.StillSize, references, request.Still.Description, ct);
			string clip = await AnimateAsync(still, VideoRequest.ClipPrompt(request.Motion, prompt), ct);
			turn.Pictures.Add(clip);
			if (replyViews.TryGetValue(turn.Id, out MessageView? view))
			{
				view.ShowPictureFiles(turn.Pictures);
			}
			return "Video made and shown in the chat (5 seconds). It shows: " + prompt.Truncate(300) + " Then: " + request.Motion.Truncate(200) + " Don't describe it at length or mention the tool; carry on naturally.";
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex) when (ex is IOException || ex is PersonaConfigException || ex is HttpRequestException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is NotSupportedException)
		{
			Notice("Video: " + ex.Message);
			return "Not made: " + ex.Message.Truncate(300) + " Tell them briefly that the video didn't work.";
		}
		finally
		{
			PictureStatus(null);
		}
	}

	/// <summary>
	/// Animates a still into a clip saved beside it (same name, .webm), pausing the chat model while it runs.
	/// Returns the clip's path. The still is scaled to the clip's size first.
	/// </summary>
	private async Task<string> AnimateAsync(string still, string prompt, CancellationToken ct)
	{
		string output = Path.ChangeExtension(still, ".webm");
		string start = Path.Combine(Path.GetDirectoryName(still)!, Path.GetFileNameWithoutExtension(still) + ".start.png");
		ScaleTo(still, start, VideoCommand.DefaultWidth, VideoCommand.DefaultHeight);
		bool paused = false;
		bool restore = false;
		try
		{
			if (engine.Ready)
			{
				PictureStatus("Pausing the chat model to make room for the video…");
				restore = await engine.ParkAsync(ct);
				paused = true;
			}
			PictureStatus("Making a video…");
			long seed = Random.Shared.NextInt64(1, int.MaxValue);
			bool fast = settings.Video.Fast;
			await PictureDrawer.MakeVideoAsync(new VideoJob(prompt, start, VideoCommand.DefaultWidth, VideoCommand.DefaultHeight, VideoCommand.DefaultFrames, seed, output), fast, delegate(int step, int of)
			{
				base.Dispatcher.InvokeAsync(() => PictureStatus($"Making a video… step {step}/{of}"));
			}, ct);
			File.WriteAllText(Path.ChangeExtension(output, ".video.txt"), prompt + Environment.NewLine + Environment.NewLine + "Model: Wan 2.2 I2V A14B" + (fast && PictureDrawer.Video.Resolve().HasLightning ? " + Lightning 4-step" : "") + Environment.NewLine + "Seed: " + seed);
			try
			{
				File.Delete(start);
			}
			catch (IOException)
			{
			}
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

	private static void ScaleTo(string source, string target, int width, int height)
	{
		BitmapImage image = new BitmapImage();
		image.BeginInit();
		image.CacheOption = BitmapCacheOption.OnLoad;
		image.UriSource = new Uri(source, UriKind.Absolute);
		image.DecodePixelWidth = width;
		image.DecodePixelHeight = height;
		image.EndInit();
		image.Freeze();
		PngBitmapEncoder encoder = new PngBitmapEncoder();
		encoder.Frames.Add(BitmapFrame.Create(image));
		using FileStream stream = File.Create(target);
		encoder.Save(stream);
	}
}
