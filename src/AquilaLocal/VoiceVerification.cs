using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AquilaAvatarProtocol;
using NAudio.Wave;

namespace AquilaLocal;

public static class VoiceVerification
{
	public static async Task RunAsync(string folder, string assets, string modelPath)
	{
		List<string> results = new List<string>();
		AvatarHost avatar;
		using (CancellationTokenSource deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8.0)))
		{
			CancellationToken ct = deadline.Token;
			Storage store = new Storage(Path.Combine(folder, "data"));
			VoiceOptions options = new VoiceOptions
			{
				Enabled = true,
				Device = "auto",
				Speaker = "Ryan"
			};
			Settings settings = new Settings
			{
				ModelPath = modelPath,
				GpuLayers = 0,
				CpuThreads = 2,
				Context = 4096,
				MaxTokens = 180,
				Temperature = 0.0,
				WorkspaceTools = false,
				Voice = options
			};
			store.SaveSettings(settings);
			Check(store.LoadSettings().Voice.Enabled && store.LoadSettings().Voice.Speaker == "Ryan", "Voice selection, automatic speaking and delivery settings survive reload");
			bool ok = false;
			try
			{
				VoiceOptions voiceOptions = new VoiceOptions();
				voiceOptions.Speaker = "other.exe";
				voiceOptions.Validate();
			}
			catch (ArgumentException)
			{
				ok = true;
			}
			Check(ok, "Unknown speaker identities are rejected");
			ok = false;
			try
			{
				VoiceOptions voiceOptions2 = new VoiceOptions();
				voiceOptions2.Volume = float.NaN;
				voiceOptions2.Validate();
			}
			catch (ArgumentException)
			{
				ok = true;
			}
			Check(ok, "Invalid audio volume is rejected");
			ok = false;
			try
			{
				VoiceOptions voiceOptions3 = new VoiceOptions();
				voiceOptions3.MaxCharacters = 90000;
				voiceOptions3.Validate();
			}
			catch (ArgumentException)
			{
				ok = true;
			}
			Check(ok, "Spoken reply length is bounded");
			Turn turn = new Turn();
			VoiceCueDecoder voiceCueDecoder = new VoiceCueDecoder(turn);
			string text = voiceCueDecoder.Push("[[to") + voiceCueDecoder.Push("ne:gentle") + voiceCueDecoder.Push("]]\nI am here.") + voiceCueDecoder.Finish();
			Check(text == "I am here." && turn.SpeechMood == "gentle" && turn.SpeechMoodSource == "model", "Fragmented delivery cues are removed from text and recorded as model metadata");
			Turn turn2 = new Turn();
			VoiceCueDecoder voiceCueDecoder2 = new VoiceCueDecoder(turn2);
			Check(voiceCueDecoder2.Push("[[tone:unrecognized]]Hello") == "Hello" && turn2.SpeechMood == "neutral", "Unknown delivery labels fall back to neutral without being spoken");
			VoiceCueDecoder voiceCueDecoder3 = new VoiceCueDecoder(new Turn());
			voiceCueDecoder3.Push("[[tone:warm");
			Check(voiceCueDecoder3.Finish() == "", "Interrupted metadata cannot leak into spoken or visible text");
			VoiceCueDecoder voiceCueDecoder4 = new VoiceCueDecoder(new Turn());
			Check(voiceCueDecoder4.Push("A normal reply.") == "A normal reply.", "A model omitting the delivery cue still produces ordinary readable text");
			VoiceCueDecoder voiceCueDecoder5 = new VoiceCueDecoder(new Turn());
			Check(voiceCueDecoder5.Push("Hello [[to") + voiceCueDecoder5.Push("ne:warm]]there. [[tone:warm]]") + voiceCueDecoder5.Finish() == "Hello there. ", "Repeated or misplaced fragmented cues stay out of the visible and spoken reply");
			string text2 = SpokenText.Clean("**Hello.** [Guide](https://example.com)\n```cs\nprivate-secret-demo\n```\n\nSources consulted (search results may be snippets):\nhttps://example.com");
			Check(text2.Contains("Hello.") && text2.Contains("Guide") && !text2.Contains("private-secret-demo") && !text2.Contains("http") && !text2.Contains("Sources consulted"), "Speech cleanup omits code, raw URLs, Markdown markers and generated reference footers");
			List<string> list = SpokenText.Segments("Dr. Smith measured 3.14 units. That is useful. Next, we check the result.", 1000);
			Check(list[0].Contains("Dr. Smith") && list[0].Contains("3.14") && list.Count >= 2, "Sentence queue preserves abbreviations and decimal numbers");
			Check(SpokenText.Segments(new string('x', 9000), 500).Sum((string p) => p.Length) <= 560, "Very long replies respect the spoken length cap");
			Check(options.Instruction("cheerful") != options.Instruction("gentle") && options.Instruction("gentle").Contains("compassionate"), "Delivery instructions express different moods without changing speaker identity");
			Check(new AvatarMessage
			{
				Session = "s",
				State = "speaking",
				Mouth = 0.6f,
				Emotion = "warm"
			}.Valid("s") && !new AvatarMessage
			{
				Session = "s",
				Mouth = float.NaN
			}.Valid("s"), "Avatar protocol accepts bounded audio levels and rejects invalid mouth weights");
			using SpeechProcess worker = new SpeechProcess(assets, Path.Combine(folder, "generated"), store.Root);
			List<string> progress = new List<string>();
			worker.Progress += delegate(string message)
			{
				progress.Add(message);
			};
			Stopwatch watch = Stopwatch.StartNew();
			string warm = await worker.GenerateAsync("Hello. I am glad you are here.", options, "warm", ct);
			long cold = watch.ElapsedMilliseconds;
			using (WaveFileReader waveFileReader = new WaveFileReader(warm))
			{
				int ok2;
				if (waveFileReader.WaveFormat.SampleRate == 24000 && waveFileReader.WaveFormat.BitsPerSample == 16 && waveFileReader.WaveFormat.Channels == 1)
				{
					double totalSeconds = waveFileReader.TotalTime.TotalSeconds;
					ok2 = ((totalSeconds > 0.2 && totalSeconds < 20.0) ? 1 : 0);
				}
				else
				{
					ok2 = 0;
				}
				Check((byte)ok2 != 0, "Real bundled Qwen3 generates bounded 24 kHz mono speech");
			}
			File.Copy(warm, Path.Combine(folder, "Ryan-warm.wav"), overwrite: true);
			int pid = worker.ProcessId.GetValueOrDefault();
			watch.Restart();
			string text3 = await worker.GenerateAsync("Hello. I am glad you are here.", options, "gentle", ct);
			long warmTime = watch.ElapsedMilliseconds;
			Check(worker.ProcessId == pid && pid > 0, "Warm synthesis reuses the owned speech process and selected voice");
			Check(!File.ReadAllBytes(warm).SequenceEqual(File.ReadAllBytes(text3)), "Real model output changes with delivery instructions for identical text and speaker");
			File.Copy(text3, Path.Combine(folder, "Ryan-gentle.wav"), overwrite: true);
			VoiceOptions options2 = new VoiceOptions
			{
				Speaker = "Aiden",
				Device = "auto"
			};
			string text4 = await worker.GenerateAsync("Hello. It is good to meet you.", options2, "warm", ct);
			Check(!File.ReadAllBytes(warm).SequenceEqual(File.ReadAllBytes(text4)), "A second selected preset produces a distinct speech output");
			File.Copy(text4, Path.Combine(folder, "Aiden-warm.wav"), overwrite: true);
			await using SpeechService playback = new SpeechService(store.Root, assets);
			List<SpeechFrame> frames = new List<SpeechFrame>();
			playback.Frame += delegate(SpeechFrame frame)
			{
				frames.Add(frame);
			};
			avatar = new AvatarHost(store.Root);
			try
			{
				ConcurrentQueue<AvatarMessage> applied = new ConcurrentQueue<AvatarMessage>();
				avatar.Applied += delegate(AvatarMessage message)
				{
					applied.Enqueue(message);
				};
				await avatar.StartAsync(new AvatarPresentation(), ct, headless: true);
				playback.Frame += SendFrame;
				await playback.PlayAsync(warm, "audition", "warm", 0.5f, ct);
				await Task.Delay(120, ct);
				Check(applied.Any((AvatarMessage m) => m.State == "speaking" && m.Mouth > 0.01f && m.Emotion == "warm") && applied.Last().Mouth == 0f, "Exported Godot renderer acknowledges actual audio mouth levels, mood and final mouth closure");
				playback.Frame -= SendFrame;
				await avatar.StopAsync();
				Check(frames.Any((SpeechFrame f) => f.Mouth > 0.01f) && frames.Last().Mouth == 0f, "Actual audio playback drives nonzero mouth motion and resets it at the end");
				using (CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(ct))
				{
					cancel.CancelAfter(150);
					watch.Restart();
					bool stopped = false;
					try
					{
						await playback.PlayAsync(warm, "stop-test", "neutral", 0.3f, cancel.Token);
					}
					catch (OperationCanceledException)
					{
						stopped = true;
					}
					Check(stopped && watch.ElapsedMilliseconds < 1500, "Stop interrupts queued audio promptly");
				}
				CancellationTokenSource cancel2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
				try
				{
					worker.Progress += Cancel;
					bool stopped = false;
					try
					{
						await worker.GenerateAsync("A cancellation test should stop before this passage completes.", options, "neutral", cancel2.Token);
					}
					catch (OperationCanceledException)
					{
						stopped = true;
					}
					worker.Progress -= Cancel;
					Check(stopped && !worker.ProcessId.HasValue, "Stopping in-flight neural synthesis kills only the owned worker and releases its model");
				}
				finally
				{
					if (cancel2 != null)
					{
						((IDisposable)cancel2).Dispose();
					}
				}
				using (Engine engine = new Engine(store.Root))
				{
					Turn turn3 = new Turn
					{
						User = "I just passed my exam! Celebrate with me in one short sentence.",
						Status = "generating"
					};
					ChatSession chat = new ChatSession
					{
						Turns = new List<Turn>(1) { turn3 }
					};
					await new Agent(engine).RunAsync(settings, chat, turn3, ct);
					File.WriteAllText(Path.Combine(folder, "model-delivery.json"), JsonSerializer.Serialize(turn3, new JsonSerializerOptions
					{
						WriteIndented = true
					}));
					Check(turn3.Status == "complete" && !turn3.Answer.Contains("[[tone:") && VoiceOptions.Moods.Contains(turn3.SpeechMood), "Real local chat chooses a valid delivery cue without displaying it in the answer");
					bool flag = turn3.SpeechMoodSource == "model";
					if (flag)
					{
						string speechMood = turn3.SpeechMood;
						bool flag2 = ((speechMood == "cheerful" || speechMood == "warm") ? true : false);
						flag = flag2;
					}
					Check(flag, "Celebratory conversation receives a positive model-selected delivery");
					Check(turn3.Reasoning.Length == 0 && turn3.PromptTokens > 0 && turn3.UsageRequests > 0, "Delivery planning shares the existing chat request and exact token accounting");
					store.SaveChat(chat);
					Check(store.LoadChats().Single().Turns.Single().SpeechMood == turn3.SpeechMood, "Delivery metadata persists separately from chat text");
					File.WriteAllText(Path.Combine(folder, "model-delivery.json"), JsonSerializer.Serialize(turn3, new JsonSerializerOptions
					{
						WriteIndented = true
					}));
				}
				List<string> voices = SpeechService.WindowsVoices();
				if (voices.Count > 0)
				{
					Turn turn3 = new Turn
					{
						Answer = "The voice is ready.",
						SpeechMood = "warm"
					};
					await playback.SpeakAsync(turn3, new VoiceOptions
					{
						Speaker = voices[0]
					}, ct);
					Check(turn3.SpeechStatus == "complete" && !playback.ProcessId.HasValue, "Installed Windows voice provides an explicit lightweight playback option");
					Check(Directory.GetFiles(Path.Combine(store.Root, "speech-temp"), "*.wav").Length == 0, "Finished playback removes its temporary audio files");
					Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
					Storage uiStore = new Storage(Path.Combine(folder, "ui-data"));
					uiStore.SaveSettings(new Settings
					{
						ModelPath = modelPath,
						GpuLayers = 0,
						CpuThreads = 2,
						Context = 4096,
						MaxTokens = 180,
						Temperature = 0.0,
						LearnMemories = false,
						UseMemories = false,
						WorkspaceTools = false,
						Voice = new VoiceOptions
						{
							Enabled = true,
							Speaker = voices[0]
						}
					});
					MainWindow window = new MainWindow(uiStore.Root, assets)
					{
						Width = 1100.0,
						Height = 700.0
					};
					window.Show();
					window.UpdateLayout();
					Check(((ListBox)window.FindName("ChatsList")).ActualHeight >= 80.0, "Voice controls leave usable conversation-list space at the minimum window size");
					await window.SendForVoiceVerification("Say hello in one short sentence.");
					await Until(() => !window.SpeechForVerification.Active, ct);
					ChatSession chatSession = uiStore.LoadChats().Single((ChatSession c) => c.Turns.Count > 0);
					Check(chatSession.Turns.Last().Status == "complete" && chatSession.Turns.Last().SpeechStatus == "complete", "Actual main window automatically speaks a completed local-model reply and saves its voice status");
					Turn incompleteTurn = new Turn
					{
						Answer = "This is an incomplete answer.",
						Status = "limit"
					};
					await window.SpeakForVoiceVerification(chatSession, incompleteTurn);
					Check(incompleteTurn.SpeechStatus == "" && !window.SpeechForVerification.Active, "Incomplete replies are preserved without starting automatic speech");
					Turn longTurn = new Turn
					{
						Answer = string.Join(" ", Enumerable.Repeat("This is a longer playback passage for testing closing safely.", 35))
					};
					Task pending = window.SpeechForVerification.SpeakAsync(longTurn, new VoiceOptions
					{
						Speaker = voices[0]
					});
					await Until(() => longTurn.SpeechStatus == "speaking", ct);
					TaskCompletionSource closed = new TaskCompletionSource();
					window.Closed += delegate
					{
						closed.TrySetResult();
					};
					window.Close();
					await closed.Task.WaitAsync(TimeSpan.FromSeconds(8.0), ct);
					await pending;
					Check(longTurn.SpeechStatus == "stopped" && !window.SpeechForVerification.Active, "Closing the actual main window stops queued playback and completes disposal");
				}
				worker.Stop();
				Check(!worker.ProcessId.HasValue && !playback.Active, "Speech verification leaves no owned generation or playback active");
				File.WriteAllText(Path.Combine(folder, "metrics.json"), JsonSerializer.Serialize(new
				{
					checks = results.Count,
					coldMilliseconds = cold,
					warmMilliseconds = warmTime,
					speaker = "Ryan",
					backend = worker.Backend,
					cpuThreads = 2,
					windowsVoices = voices.Count,
					emotionJudgment = "Waveform differences verify instruction use, not subjective emotion quality. Listen to the supplied auditions."
				}, new JsonSerializerOptions
				{
					WriteIndented = true
				}));
				void Cancel(string text5)
				{
					if (text5.StartsWith("Generating"))
					{
						cancel2.Cancel();
					}
				}
			}
			finally
			{
				if (avatar != null)
				{
					await avatar.DisposeAsync();
				}
			}
		}
		void Check(bool flag3, string label)
		{
			if (!flag3)
			{
				throw new Exception("Failed: " + label);
			}
			results.Add("PASS " + label);
			File.WriteAllLines(Path.Combine(folder, "checks.txt"), results);
		}
		void SendFrame(SpeechFrame frame)
		{
			avatar.SetSpeechFrameAsync(frame.Utterance, frame.Mood, frame.Mouth);
		}
	}

	private static async Task Until(Func<bool> ready, CancellationToken ct)
	{
		Stopwatch watch = Stopwatch.StartNew();
		while (!ready())
		{
			if (watch.Elapsed > TimeSpan.FromSeconds(25.0))
			{
				throw new TimeoutException("Voice integration condition was not reached.");
			}
			await Task.Delay(40, ct);
		}
	}
}
