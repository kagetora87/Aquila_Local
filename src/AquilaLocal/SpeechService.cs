using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NAudio.Wave;

namespace AquilaLocal;

public sealed class SpeechService : IAsyncDisposable
{
	private readonly SpeechProcess worker;

	private readonly string temporary;

	private readonly SemaphoreSlim lifecycle = new SemaphoreSlim(1, 1);

	private CancellationTokenSource? stop;

	private Task running = Task.CompletedTask;

	private bool disposed;

	public string CurrentUtterance { get; private set; } = "";

	public bool Active => !running.IsCompleted;

	public int? ProcessId => worker.ProcessId;

	public VoiceLibrary Voices { get; }

	public string LastStatus { get; private set; } = "Voice ready · select a voice to enable spoken replies";

	public event Action<string>? Status;

	public event Action<SpeechFrame>? Frame;

	public SpeechService(string dataRoot, string? assets = null)
	{
		temporary = Path.Combine(dataRoot, "speech-temp");
		Directory.CreateDirectory(temporary);
		Voices = new VoiceLibrary(dataRoot);
		worker = new SpeechProcess(assets ?? Path.Combine(AppContext.BaseDirectory, "speech"), temporary, dataRoot);
		worker.Progress += Report;
	}

	private void Report(string text)
	{
		LastStatus = text;
		this.Status?.Invoke(text);
	}

	public string VoiceLabel(string speaker)
	{
		string cloneId = new VoiceOptions
		{
			Speaker = speaker
		}.CloneId;
		if (cloneId == null)
		{
			return speaker;
		}
		return "Clone: " + (Voices.Find(cloneId)?.Name ?? "missing voice");
	}

	public static List<string> WindowsVoices()
	{
		try
		{
			using SpeechSynthesizer speechSynthesizer = new SpeechSynthesizer();
			return (from v in speechSynthesizer.GetInstalledVoices()
				where v.Enabled
				select "Windows: " + v.VoiceInfo.Name).ToList();
		}
		catch
		{
			return new List<string>();
		}
	}

	public async Task SpeakAsync(Turn turn, VoiceOptions options, CancellationToken ct = default(CancellationToken), VoiceReference? preview = null)
	{
		options.Validate();
		await lifecycle.WaitAsync(ct);
		Task task;
		try
		{
			ObjectDisposedException.ThrowIf(disposed, this);
			if (!running.IsCompleted)
			{
				Stop();
			}
			try
			{
				await running;
			}
			catch
			{
			}
			stop?.Dispose();
			stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
			task = (running = RunAsync(turn, options, preview, stop.Token));
		}
		finally
		{
			lifecycle.Release();
		}
		await task;
	}

	public void Stop()
	{
		try
		{
			stop?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
		worker.Stop();
	}

	private async Task RunAsync(Turn turn, VoiceOptions options, VoiceReference? reference, CancellationToken cancel)
	{
		using CancellationTokenSource scope = CancellationTokenSource.CreateLinkedTokenSource(cancel);
		CancellationToken ct = scope.Token;
		string utterance = (CurrentUtterance = Guid.NewGuid().ToString("N"));
		turn.SpeechStatus = "preparing";
		List<string> files = new List<string>();
		Channel<(string Path, string Mood)> channel = Channel.CreateBounded<(string, string)>(1);
		Task producer = Task.CompletedTask;
		try
		{
			_ = 3;
			try
			{
				string cloneId = options.CloneId;
				if (cloneId != null)
				{
					reference = ((reference?.Id == cloneId) ? reference : (Voices.Find(cloneId) ?? throw new IOException("This voice sample is missing. Choose another voice.")));
				}
				string voiceLabel = (turn.SpeechVoice = (((object)reference == null) ? options.Speaker : ("Clone: " + reference.Name)));
				IReadOnlyList<SpeechSegment> segments = VoiceDelivery.Plan(turn, options);
				if (segments.Count == 0)
				{
					turn.SpeechStatus = "no spoken text";
					return;
				}
				Report($"Preparing {voiceLabel} · {segments[0].Mood}…");
				producer = Task.Run(async delegate
				{
					_ = 2;
					try
					{
						foreach (SpeechSegment segment in segments)
						{
							ct.ThrowIfCancellationRequested();
							string text3 = ((!options.UsesWindows) ? (await worker.GenerateAsync(segment.Text, options, segment.Mood, ct, reference)) : (await WindowsAudio(segment.Text, options, ct)));
							string text4 = text3;
							lock (files)
							{
								files.Add(text4);
							}
							await channel.Writer.WriteAsync((text4, segment.Mood), ct);
						}
						channel.Writer.TryComplete();
					}
					catch (Exception error)
					{
						channel.Writer.TryComplete(error);
					}
				}, ct);
				await foreach ((string, string) item in channel.Reader.ReadAllAsync(ct))
				{
					turn.SpeechStatus = "speaking";
					Report("Speaking · " + voiceLabel + " · " + item.Item2);
					await PlayAsync(item.Item1, utterance, VoiceDelivery.AvatarMood(item.Item2), options.Volume, ct);
				}
				await producer;
				turn.SpeechStatus = "complete";
				Report("Voice ready · completed reply");
			}
			catch (OperationCanceledException)
			{
				turn.SpeechStatus = "stopped";
				Report("Speech stopped");
			}
			catch (Exception) when (ct.IsCancellationRequested)
			{
				turn.SpeechStatus = "stopped";
				Report("Speech stopped");
			}
			catch (Exception ex3)
			{
				turn.SpeechStatus = "failed";
				Report("Voice: " + ex3.Message.Truncate(800));
			}
		}
		finally
		{
			scope.Cancel();
			if (!options.KeepLoaded || turn.SpeechStatus != "complete")
			{
				worker.Stop();
			}
			try
			{
				await producer;
			}
			catch
			{
			}
			this.Frame?.Invoke(new SpeechFrame(utterance, "neutral", Playing: false, 0f));
			if (CurrentUtterance == utterance)
			{
				CurrentUtterance = "";
			}
			lock (files)
			{
				foreach (string item2 in files)
				{
					try
					{
						File.Delete(item2);
					}
					catch (IOException)
					{
					}
				}
			}
		}
	}

	private async Task<string> WindowsAudio(string text, VoiceOptions options, CancellationToken ct)
	{
		string path = Path.Combine(temporary, Guid.NewGuid().ToString("N") + ".wav");
		SpeechSynthesizer synth = new SpeechSynthesizer();
		try
		{
			SpeechSynthesizer speechSynthesizer = synth;
			string speaker = options.Speaker;
			speechSynthesizer.SelectVoice(speaker.Substring(9, speaker.Length - 9));
			synth.Rate = ((options.Pace == "Slow") ? (-2) : ((options.Pace == "Lively") ? 1 : 0));
			synth.SetOutputToWaveFile(path, new SpeechAudioFormatInfo(24000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
			TaskCompletionSource completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			synth.SpeakCompleted += delegate(object? _, SpeakCompletedEventArgs e)
			{
				if (e.Error != null)
				{
					completion.TrySetException(e.Error);
				}
				else if (e.Cancelled)
				{
					completion.TrySetCanceled(ct);
				}
				else
				{
					completion.TrySetResult();
				}
			};
			using (ct.Register(delegate
			{
				synth.SpeakAsyncCancelAll();
			}))
			{
				try
				{
					ct.ThrowIfCancellationRequested();
					synth.SpeakAsync(text);
					await completion.Task.WaitAsync(ct);
					return path;
				}
				catch
				{
					synth.SetOutputToNull();
					try
					{
						File.Delete(path);
					}
					catch (IOException)
					{
					}
					throw;
				}
			}
		}
		finally
		{
			if (synth != null)
			{
				((IDisposable)synth).Dispose();
			}
		}
	}

	public async Task PlayAsync(string path, string utterance, string mood, float volume, CancellationToken ct)
	{
		using WaveFileReader reader = new WaveFileReader(path);
		if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm || reader.WaveFormat.BitsPerSample != 16 || reader.WaveFormat.Channels != 1 || reader.TotalTime.TotalSeconds > 90.0)
		{
			throw new IOException("Speech playback requires bounded mono PCM16 audio.");
		}
		byte[] pcm = new byte[checked((int)reader.Length)];
		reader.ReadExactly(pcm);
		reader.Position = 0L;
		WaveOutEvent player = new WaveOutEvent
		{
			DesiredLatency = 100,
			NumberOfBuffers = 2,
			Volume = volume
		};
		try
		{
			TaskCompletionSource completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			player.PlaybackStopped += delegate(object? _, StoppedEventArgs e)
			{
				if (e.Exception != null)
				{
					completion.TrySetException(e.Exception);
				}
				else
				{
					completion.TrySetResult();
				}
			};
			player.Init(reader);
			using (ct.Register(delegate
			{
				player.Stop();
				completion.TrySetCanceled(ct);
			}))
			{
				ct.ThrowIfCancellationRequested();
				player.Play();
				try
				{
					while (!completion.Task.IsCompleted)
					{
						ct.ThrowIfCancellationRequested();
						long num = Math.Min(pcm.Length / 2, player.GetPosition() / 2);
						int num2 = (int)Math.Min(720L, pcm.Length / 2 - num);
						double num3 = 0.0;
						for (long num4 = num; num4 < num + num2; num4++)
						{
							double num5 = (double)BitConverter.ToInt16(pcm, (int)num4 * 2) / 32768.0;
							num3 += num5 * num5;
						}
						float mouth = ((num2 > 0) ? ((float)Math.Clamp((Math.Sqrt(num3 / (double)num2) * (double)volume - 0.005) * 5.0, 0.0, 0.9)) : 0f);
						this.Frame?.Invoke(new SpeechFrame(utterance, mood, Playing: true, mouth));
						await Task.WhenAny(completion.Task, Task.Delay(40, ct));
					}
					await completion.Task;
				}
				finally
				{
					player.Stop();
					this.Frame?.Invoke(new SpeechFrame(utterance, mood, Playing: true, 0f));
				}
			}
		}
		finally
		{
			if (player != null)
			{
				((IDisposable)player).Dispose();
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		disposed = true;
		Stop();
		try
		{
			await running;
		}
		catch
		{
		}
		stop?.Dispose();
		worker.Dispose();
	}
}
