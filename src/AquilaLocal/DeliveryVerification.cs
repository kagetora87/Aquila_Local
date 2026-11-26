using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using NAudio.Wave;

namespace AquilaLocal;

public static class DeliveryVerification
{
	public static async Task RunAsync(string directory, string assets, string chatModel)
	{
		Storage store = new Storage(Path.Combine(directory, "data"));
		string checks = Path.Combine(directory, "checks.txt");
		CancellationToken ct;
		VoiceOptions voice;
		SpeechProcess worker;
		using (CancellationTokenSource deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6.0)))
		{
			ct = deadline.Token;
			voice = new VoiceOptions
			{
				AutoTags = true,
				Device = "gpu",
				Direction = "Start excited, then soften to gentle."
			};
			Check(!new VoiceOptions().AutoTags && !new VoiceOptions().RevealTags, "Passage tags and reveal are opt-in for existing profiles");
			Turn turn = new Turn();
			VoiceCueDecoder voiceCueDecoder = new VoiceCueDecoder(turn, passageTags: true, events: true);
			string[] array = new string[4] { "[[voi", "ce:excited]] We did it! [[voice:la", "ugh]]\n\n[[voice:gentle]] Take a breath. ", "`code [[voice:stern]]` Unknown [[voice:invalid]] cue. [[voice:su" };
			foreach (string fragment in array)
			{
				turn.Answer += voiceCueDecoder.Push(fragment);
			}
			turn.Answer += voiceCueDecoder.Finish();
			Check(turn.Answer == "We did it! \n\nTake a breath. `code [[voice:stern]]` Unknown cue. " && turn.VoiceTags.Select((VoiceTag x) => x.Name).SequenceEqual(new string[3] { "excited", "laugh", "gentle" }), "Fragmented tags stay out of the text and literal inline code survives");
			Check(VoiceDelivery.Display(turn, reveal: false) == turn.Answer && VoiceDelivery.Display(turn, reveal: true).Contains("[[voice:excited]] We did it!") && VoiceDelivery.Display(turn, reveal: true).Contains("[[voice:gentle]] Take a breath."), "Reveal restores tags at the correct positions without changing the saved text");
			IReadOnlyList<SpeechSegment> readOnlyList = VoiceDelivery.Plan(turn, voice);
			int condition;
			if (readOnlyList[0].Mood == "excited" && readOnlyList[0].Text.Contains("<laugh>"))
			{
				if (readOnlyList[readOnlyList.Count - 1].Mood == "gentle")
				{
					condition = (readOnlyList.All((SpeechSegment segment) => !segment.Text.Contains("[[voice:")) ? 1 : 0);
					goto IL_02d7;
				}
			}
			condition = 0;
			goto IL_02d7;
			IL_02d7:
			Check((byte)condition != 0, "The speech plan places Kitten events and mood changes at the right passage");
			Check(VoiceDelivery.Plan(turn, new VoiceOptions
			{
				Speaker = "Ryan"
			}).All((SpeechSegment segment) => !segment.Text.Contains("<laugh>")), "Qwen receives passage moods without unsupported Kitten event markup");
			Turn turn2 = new Turn();
			VoiceCueDecoder voiceCueDecoder2 = new VoiceCueDecoder(turn2);
			turn2.Answer = voiceCueDecoder2.Push("[[to");
			turn2.Answer += voiceCueDecoder2.Push("ne:warm]]\nHello.");
			Check(turn2.Answer == "Hello." && turn2.SpeechMood == "warm" && VoiceDelivery.Display(turn2, reveal: true).Contains("[[tone:warm]]"), "The existing opening-cue mode still works and can be revealed");
			Turn turn3 = new Turn();
			VoiceCueDecoder voiceCueDecoder3 = new VoiceCueDecoder(turn3, passageTags: true, events: true);
			turn3.Answer = voiceCueDecoder3.Push("```\n[[voice:excited]]\n");
			turn3.Answer += voiceCueDecoder3.Push("```\n[[voice:gentle]] A reply.");
			Check(turn3.VoiceTags.Count == 1 && turn3.Answer.Contains("[[voice:excited]]") && VoiceDelivery.Plan(turn3, voice).All((SpeechSegment segment) => !segment.Text.Contains("excited")), "Fenced examples do not become speech directives");
			Turn turn4 = new Turn();
			turn4.Answer = new VoiceCueDecoder(turn4, passageTags: true, events: true).Push(string.Concat(string.Concat(Enumerable.Repeat("[[voice:pause]] ", 24)), "Hello."));
			Check(turn4.VoiceTags.Count == 16 && turn4.Answer == "Hello.", "Repeated tags are bounded and stay hidden");
			Check(VoiceDelivery.Plan(new Turn
			{
				Answer = "Preview text."
			}, new VoiceOptions
			{
				Direction = "Use [joyful]."
			})[0].Mood == "joyful", "An explicit Kitten direction tag is audible in a preview without a chat request");
			Settings settings = new Settings
			{
				ModelPath = chatModel,
				Context = 4096,
				MaxTokens = 256,
				Temperature = 0.0,
				GpuLayers = 0,
				CpuThreads = 2,
				WorkspaceTools = false,
				UseMemories = false,
				LearnMemories = false,
				Voice = voice
			};
			store.SaveSettings(settings);
			Check(store.LoadSettings().Voice.AutoTags && !store.LoadSettings().Voice.RevealTags, "Voice picker settings persist independently of autoplay");
			Turn real = new Turn
			{
				User = "Give exactly these two short spoken lines, using the hidden delivery tag syntax. First line: excited tone, We did it! Second line: gentle tone, Take a deep breath.",
				Status = "generating"
			};
			ChatSession session = new ChatSession
			{
				Turns = new List<Turn>(1) { real }
			};
			using (Engine engine = new Engine(store.Root))
			{
				await new Agent(engine).RunAsync(settings, session, real, ct);
			}
			store.SaveChat(session);
			Turn turn5 = store.LoadChats().Single().Turns.Single();
			File.WriteAllText(Path.Combine(directory, "chat-test.json"), JsonSerializer.Serialize(turn5, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			Check(real.Status == "complete" && real.VoiceTags.Any() && !turn5.Answer.Contains("[[voice:") && turn5.VoiceTags.SequenceEqual(real.VoiceTags), "A real local chat response generates hidden tags that survive chat reload");
			Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			await using (SpeechService speech = new SpeechService(store.Root, assets))
			{
				VoiceWindow voiceWindow = new VoiceWindow(voice, speech, (VoiceOptions _, bool _) => Task.CompletedTask);
				DependencyObject[] source = Children(voiceWindow).ToArray();
				Check(source.OfType<ComboBox>().Any((ComboBox box) => AutomationName(box) == "Speaking pace" && box.IsEnabled) && source.OfType<TextBox>().Any((TextBox box) => AutomationName(box) == "Voice direction" && box.IsEnabled) && source.OfType<CheckBox>().Any((CheckBox box) => object.Equals(box.Content, "Reveal tags") && box.IsChecked == false), "Kitten exposes editable pace and direction, plus the reveal switch in the actual WPF window");
				voiceWindow.Close();
			}
			worker = new SpeechProcess(assets, Path.Combine(directory, "audio"), store.Root);
			try
			{
				double natural = await Generate("Natural");
				double slow = await Generate("Slow");
				double num = await Generate("Lively");
				Check(slow > natural * 1.07 && num < natural * 0.96, "Real Kitten speech honors Slow and Lively pacing with an emotion and inline event");
				File.WriteAllText(Path.Combine(directory, "metrics.json"), JsonSerializer.Serialize(new
				{
					natural = natural,
					slow = slow,
					lively = num,
					checks = File.ReadAllLines(checks).Length
				}));
			}
			finally
			{
				if (worker != null)
				{
					((IDisposable)worker).Dispose();
				}
			}
		}
		void Check(bool flag, string description)
		{
			if (!flag)
			{
				throw new InvalidOperationException("Failed: " + description);
			}
			File.AppendAllLines(checks, new string[] { "PASS " + description });
		}
		async Task<double> Generate(string pace)
		{
			voice.Pace = pace;
			string text = await worker.GenerateAsync("We did it! <laugh> What a wonderful day.", voice, "excited", ct);
			using WaveFileReader waveFileReader = new WaveFileReader(text);
			File.Copy(text, Path.Combine(directory, pace + ".wav"));
			return waveFileReader.TotalTime.TotalSeconds;
		}
	}

	private static string AutomationName(DependencyObject element)
	{
		return AutomationProperties.GetName(element);
	}

	private static IEnumerable<DependencyObject> Children(DependencyObject element)
	{
		yield return element;
		foreach (DependencyObject item in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
		{
			foreach (DependencyObject item2 in Children(item))
			{
				yield return item2;
			}
		}
	}
}
