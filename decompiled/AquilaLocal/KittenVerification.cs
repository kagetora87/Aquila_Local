using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NAudio.Wave;

namespace AquilaLocal;

public static class KittenVerification
{
	public static async Task RunAsync(string folder, string assets, string modelPath, bool gpu)
	{
		string data = Path.Combine(folder, "data");
		Storage store = new Storage(data);
		VoiceLibrary library = new VoiceLibrary(store.Root);
		string checks;
		bool finalizedBeforeSpeech;
		using (CancellationTokenSource deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8.0)))
		{
			CancellationToken ct = deadline.Token;
			checks = Path.Combine(folder, "checks.txt");
			string fixture = Path.Combine(folder, "synthetic-reference.wav");
			SpeechSynthesizer val = new SpeechSynthesizer();
			try
			{
				val.SetOutputToWaveFile(fixture, new SpeechAudioFormatInfo(24000, (AudioBitsPerSample)16, (AudioChannel)1));
				val.Speak("Good morning. This is a synthetic reference recording for Aquila. The meadow beyond the river is quiet, and the sun is just beginning to rise.");
				val.SetOutputToNull();
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			bool flag = false;
			try
			{
				library.Prepare(fixture, "Synthetic test", "");
			}
			catch (ArgumentException)
			{
				flag = true;
			}
			Check(flag && library.List().Count == 0, "Import requires a transcript and leaves no profile on validation failure");
			VoiceReference draft = library.Prepare(fixture, "Synthetic test", "Good morning. This is a synthetic reference recording for Aquila. The meadow beyond the river is quiet, and the sun is just beginning to rise.");
			Check(library.List().Count == 0 && File.Exists(draft.AudioFile), "A local sample can be prepared without appearing as a saved voice");
			WaveFileReader val2 = new WaveFileReader(draft.AudioFile);
			try
			{
				int valid2;
				if (((WaveStream)val2).WaveFormat.SampleRate == 24000 && ((WaveStream)val2).WaveFormat.Channels == 1)
				{
					double totalSeconds = ((WaveStream)val2).TotalTime.TotalSeconds;
					valid2 = ((totalSeconds >= 5.0 && totalSeconds <= 30.0) ? 1 : 0);
				}
				else
				{
					valid2 = 0;
				}
				Check((byte)valid2 != 0, "Imported WAV is normalized and bounded");
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
			string speaker = "Clone: " + draft.Id;
			string device = (gpu ? "gpu" : "cpu");
			VoiceOptions options = new VoiceOptions
			{
				Speaker = speaker,
				Device = device,
				Enabled = true,
				KeepLoaded = true,
				Volume = 0f
			};
			Stopwatch timer = Stopwatch.StartNew();
			long coldClone;
			long warmPreset;
			using (SpeechProcess worker = new SpeechProcess(assets, Path.Combine(folder, "audio"), store.Root))
			{
				string clone = await worker.GenerateAsync("The morning train is ready to leave the station.", options, "neutral", ct, draft);
				coldClone = timer.ElapsedMilliseconds;
				Check(Playable(clone) && worker.Backend == (gpu ? "cuda:0" : "cpu"), "Bundled Kitten TTS 2 clones an imported draft offline");
				int? pid = worker.ProcessId;
				timer.Restart();
				string text = await worker.GenerateAsync("The morning train is ready to leave the station.", new VoiceOptions
				{
					Speaker = "Kitten: Luna",
					Device = device
				}, "neutral", ct);
				warmPreset = timer.ElapsedMilliseconds;
				Check(Playable(text) && worker.ProcessId == pid, "Kitten can play a preset through the same warm worker");
				Check(!File.ReadAllBytes(clone).SequenceEqual(File.ReadAllBytes(text)), "Clone and preset produce distinct audio for identical text");
				File.Copy(clone, Path.Combine(folder, "clone.wav"));
				File.Copy(text, Path.Combine(folder, "preset.wav"));
			}
			VoiceReference saved = library.Save(draft);
			Check(new VoiceLibrary(data).Find(saved.Id) == saved && !File.Exists(draft.AudioFile) && File.Exists(fixture), "Saved profile reloads and leaves the source recording intact");
			store.SaveSettings(new Settings
			{
				ModelPath = modelPath,
				GpuLayers = 0,
				CpuThreads = 2,
				Context = 4096,
				MaxTokens = 128,
				Temperature = 0.0,
				LearnMemories = false,
				UseMemories = false,
				WorkspaceTools = false,
				Voice = options
			});
			Check(store.LoadSettings().Voice.CloneId == saved.Id, "A named clone can be saved as the active voice");
			Check(SpokenText.Clean("Thanks \ud83d\ude0a and good luck! \ud83d\udc4d\ud83c\udffd") == "Thanks and good luck!", "Color emoji stay in chat and are omitted from speech");
			Application.Current.ShutdownMode = (ShutdownMode)2;
			MainWindow window = new MainWindow(data, assets);
			SpeechService service = window.SpeechForVerification;
			finalizedBeforeSpeech = false;
			service.Status += Seen;
			try
			{
				await window.SendForVoiceVerification("Say one short sentence to welcome a friend.");
				Check(finalizedBeforeSpeech, "Aquila saves the final local chat text before starting Kitten");
				while (service.Active)
				{
					await Task.Delay(40, ct);
				}
				ChatSession chatSession = store.LoadChats().Single((ChatSession item) => item.Turns.Count > 0);
				Turn turn2 = chatSession.Turns.Last();
				Check(turn2.SpeechStatus == "complete" && turn2.SpeechVoice == "Clone: Synthetic test", "The final chat reply plays using the saved clone");
				Turn interrupted = new Turn
				{
					Answer = "Partial text",
					Status = "cancelled"
				};
				await window.SpeakForVoiceVerification(chatSession, interrupted);
				Check(!service.Active && interrupted.SpeechStatus == "", "Cancelled chat text does not start speech");
			}
			finally
			{
				TaskCompletionSource closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				((Window)window).Closed += delegate
				{
					closed.TrySetResult();
				};
				((Window)window).Close();
				await closed.Task.WaitAsync(TimeSpan.FromSeconds(12.0));
			}
			Check(!service.ProcessId.HasValue, "Closing Aquila releases its owned speech process");
			library.Delete(saved.Id);
			Check((object)library.Find(saved.Id) == null && File.Exists(fixture), "Deleting a clone removes the local copy and preserves the original");
			File.WriteAllText(Path.Combine(folder, "metrics.json"), JsonSerializer.Serialize(new
			{
				checks = File.ReadAllLines(checks).Length,
				coldCloneMs = coldClone,
				warmPresetMs = warmPreset,
				compute = (gpu ? "CUDA / two torch CPU threads" : "CPU / two torch threads"),
				reference = "Synthetic Windows voice",
				speechLimits = "Local WAV and transcript"
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		void Check(bool valid, string description)
		{
			if (!valid)
			{
				throw new InvalidOperationException("Failed: " + description);
			}
			File.AppendAllLines(checks, new _003C_003Ez__ReadOnlySingleElementList<string>("PASS " + description));
		}
		void Seen(string status)
		{
			if (status.StartsWith("Preparing Clone:", StringComparison.Ordinal))
			{
				finalizedBeforeSpeech = store.LoadChats().Any((ChatSession chat) => chat.Turns.Any((Turn turn) => turn.Status == "complete" && turn.Answer.Length > 0));
			}
		}
	}

	private static bool Playable(string path)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Expected O, but got Unknown
		WaveFileReader val = new WaveFileReader(path);
		try
		{
			int result;
			if (((WaveStream)val).WaveFormat.SampleRate == 24000 && ((WaveStream)val).WaveFormat.Channels == 1 && ((WaveStream)val).WaveFormat.BitsPerSample == 16)
			{
				double totalSeconds = ((WaveStream)val).TotalTime.TotalSeconds;
				result = ((totalSeconds > 0.3 && totalSeconds < 20.0) ? 1 : 0);
			}
			else
			{
				result = 0;
			}
			return (byte)result != 0;
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}
}
