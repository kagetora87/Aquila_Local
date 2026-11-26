using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AquilaLocal.Companion.Speech;
using NAudio.Wave;

namespace AquilaLocal;

/// <summary>Your listening settings (saved with Aquila's settings).</summary>
public sealed class ListeningOptions
{
	/// <summary>Microphone number in Windows' list; 0 is the first one.</summary>
	public int Device { get; set; }

	/// <summary>How long you stay quiet before what you said is sent.</summary>
	public double EndSilenceSeconds { get; set; } = 1.5;

	/// <summary>Send by itself when you pause; off means what you said waits in the message box for Enter.</summary>
	public bool AutoSend { get; set; } = true;

	/// <summary>Settings layout version, so older saved defaults can be moved to better ones once.</summary>
	public int Version { get; set; }

	/// <summary>"en", "ja" and so on, or "auto".</summary>
	public string Language { get; set; } = "en";

	/// <summary>Talking while she speaks stops her. Works best with headphones, so her voice isn't heard as yours.</summary>
	public bool Interrupt { get; set; }

	/// <summary>Your usual speaking level, learned as you talk, so "quietly" and "loudly" mean something.</summary>
	public double TypicalLevel { get; set; }
}

/// <param name="Text">What you said with notes: "[paused 1.8s]", "[trailed off]" and so on.</param>
/// <param name="Plain">Just the words.</param>
public sealed record SpokenMessage(string Text, string Plain, IReadOnlyList<string> Notes, double Seconds);

/// <summary>
/// The microphone side of listening: records, finds where you start and stop talking, shows live words as
/// you speak, and hands over each finished thing you said with its pauses and hesitations noted.
/// </summary>
public sealed class VoiceInput : IDisposable
{
	private readonly WhisperEngine engine;

	private readonly object gate = new object();

	private ListeningOptions options = new ListeningOptions();

	private VoiceActivityDetector vad = new VoiceActivityDetector(new ListeningSettings());

	private WaveInEvent? mic;

	private CancellationTokenSource? life;

	private bool partialBusy;

	private DateTime lastPartial;

	private double loudest;

	private double heardSeconds;

	private bool silenceChecked;

	private bool muted;

	private bool strict;

	private DateTime quietUntil;

	private readonly object holdGate = new object();

	/// <summary>What you said that's waiting a moment in case you go on (sent when the hold runs out).</summary>
	private SpokenMessage? held;

	private DateTime heldEnded;

	private DateTime lastActivity;

	private DateTime speechStartedAt;

	private int finishing;

	private int holdVersion;

	public bool Listening => mic != null;

	public bool Hearing
	{
		get
		{
			lock (gate)
			{
				return vad.InSpeech;
			}
		}
	}

	/// <summary>Current microphone level 0..1, for a level meter.</summary>
	public double Level { get; private set; }

	public event Action<string>? State;

	public event Action<string>? Partial;

	public event Action<SpokenMessage>? Heard;

	/// <summary>What you've said so far while it waits for you to go on (or empty when that's cleared).</summary>
	public event Action<string>? Waiting;

	/// <summary>Something was heard but it wasn't speech (noise Whisper turned into "Thank you." and the like).</summary>
	public event Action? Ignored;

	public event Action? SpeechStarted;

	/// <summary>Listening stopped because something is wrong (shown loudly, never ignored).</summary>
	public event Action<string>? Problem;

	public VoiceInput(WhisperEngine engine)
	{
		this.engine = engine;
	}

	public static List<string> Microphones()
	{
		List<string> list = new List<string>();
		for (int i = 0; i < WaveInEvent.DeviceCount; i++)
		{
			list.Add(WaveInEvent.GetCapabilities(i).ProductName);
		}
		return list;
	}

	public async Task StartAsync(ListeningOptions settings, CancellationToken ct)
	{
		Stop();
		options = settings;
		if (settings.Version < 2)
		{
			// The first default (1 second) cut people off mid-thought.
			settings.EndSilenceSeconds = Math.Max(settings.EndSilenceSeconds, 1.5);
			settings.Version = 2;
		}
		if (WaveInEvent.DeviceCount == 0)
		{
			throw new ListeningUnavailableException("No microphone found. Plug one in (or turn it on in Windows sound settings), then try again. Listening is off.");
		}
		int device = (settings.Device >= 0 && settings.Device < WaveInEvent.DeviceCount) ? settings.Device : 0;
		State?.Invoke("Loading the speech recognizer…");
		await engine.StartAsync(ct);
		lock (gate)
		{
			vad = new VoiceActivityDetector(new ListeningSettings { EndSilenceSeconds = Math.Clamp(settings.EndSilenceSeconds, 0.5, 4.0) });
		}
		life = new CancellationTokenSource();
		loudest = 0;
		heardSeconds = 0;
		silenceChecked = false;
		WaveInEvent wave = new WaveInEvent
		{
			DeviceNumber = device,
			WaveFormat = new WaveFormat(16000, 16, 1),
			BufferMilliseconds = 50,
			NumberOfBuffers = 4
		};
		wave.DataAvailable += OnData;
		wave.RecordingStopped += delegate(object? _, StoppedEventArgs e)
		{
			if (e.Exception != null && mic == wave)
			{
				Stop();
				Problem?.Invoke("The microphone stopped working: " + e.Exception.Message + ". Listening is off.");
			}
		};
		try
		{
			wave.StartRecording();
		}
		catch (NAudio.MmException ex)
		{
			wave.Dispose();
			throw new ListeningUnavailableException("Windows wouldn't open the microphone (" + ex.Result + "). If it's blocked, allow it in Settings → Privacy & security → Microphone → Let desktop apps access your microphone. Listening is off.", ex);
		}
		mic = wave;
		State?.Invoke("Listening…");
	}

	public void Stop()
	{
		WaveInEvent? wave = mic;
		mic = null;
		life?.Cancel();
		life = null;
		if (wave != null)
		{
			wave.DataAvailable -= OnData;
			try
			{
				wave.StopRecording();
			}
			catch (NAudio.MmException)
			{
			}
			wave.Dispose();
		}
		lock (gate)
		{
			vad.Reset();
		}
		lock (holdGate)
		{
			held = null;
			holdVersion++;
		}
		Level = 0;
	}

	/// <summary>Sends anything waiting right away (when you switch the microphone off after talking).</summary>
	public void Flush()
	{
		SpokenMessage? message;
		lock (holdGate)
		{
			message = held;
			held = null;
			holdVersion++;
		}
		if (message != null)
		{
			Heard?.Invoke(message);
		}
	}

	/// <summary>
	/// Called while she's speaking: with interrupting off, the microphone is ignored so her own voice isn't heard
	/// as yours; with it on, only clearly louder speech counts (and stops her).
	/// </summary>
	public void SetHerVoicePlaying(bool playing)
	{
		lock (gate)
		{
			if (!playing && (muted || strict))
			{
				// The room still echoes her last words for a moment; don't hear that as you.
				quietUntil = DateTime.UtcNow.AddMilliseconds(700);
				vad.Reset();
			}
			muted = playing && !options.Interrupt;
			strict = playing && options.Interrupt;
			vad.Settings.StartFactor = strict ? 8.0 : 3.0;
			vad.Settings.MinLevel = strict ? 0.03 : 0.008;
			if (muted && vad.InSpeech)
			{
				vad.Reset();
			}
		}
	}

	private void OnData(object? sender, WaveInEventArgs e)
	{
		int count = e.BytesRecorded / 2;
		float[] samples = new float[count];
		double peak = 0;
		for (int i = 0; i < count; i++)
		{
			float v = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
			samples[i] = v;
			peak = Math.Max(peak, Math.Abs(v));
		}
		Level = Math.Min(1.0, peak * 1.5);
		heardSeconds += count / 16000.0;
		loudest = Math.Max(loudest, peak);
		if (!silenceChecked && heardSeconds >= 3.0)
		{
			silenceChecked = true;
			if (loudest == 0)
			{
				Stop();
				Problem?.Invoke("The microphone is sending pure silence. Windows may be blocking it: Settings → Privacy & security → Microphone → Let desktop apps access your microphone. Or it may be muted. Listening is off.");
				return;
			}
		}
		List<VadEvent> events;
		Utterance? finished = null;
		lock (gate)
		{
			if (muted || DateTime.UtcNow < quietUntil)
			{
				return;
			}
			events = vad.Feed(samples);
			if (events.Contains(VadEvent.SpeechEnded))
			{
				finished = vad.TakeUtterance();
			}
		}
		if (events.Contains(VadEvent.SpeechStarted))
		{
			lastPartial = DateTime.UtcNow;
			speechStartedAt = DateTime.UtcNow.AddSeconds(-0.4);
			lastActivity = DateTime.UtcNow;
			State?.Invoke("Hearing you…");
			SpeechStarted?.Invoke();
		}
		CancellationToken ct = life?.Token ?? CancellationToken.None;
		if (finished != null)
		{
			Interlocked.Increment(ref finishing);
			DateTime started = speechStartedAt;
			_ = FinishAsync(finished, started, ct);
		}
		else if (Hearing && !partialBusy && DateTime.UtcNow - lastPartial > TimeSpan.FromMilliseconds(900))
		{
			lastPartial = DateTime.UtcNow;
			_ = PartialAsync(ct);
		}
	}

	private async Task PartialAsync(CancellationToken ct)
	{
		partialBusy = true;
		try
		{
			float[] audio;
			lock (gate)
			{
				audio = vad.CurrentAudio();
			}
			if (audio.Length < 8000)
			{
				return;
			}
			List<TranscriptSegment> segments = await engine.TranscribeAsync(Wav.Pcm16(audio, ListeningSettings.SampleRate), options.Language, ct);
			if (!ct.IsCancellationRequested && Hearing)
			{
				Partial?.Invoke(string.Join(" ", segments.Select((TranscriptSegment s) => s.Text)));
			}
		}
		catch (Exception) when (ct.IsCancellationRequested)
		{
		}
		catch (Exception)
		{
			// A missed live preview is harmless; the final transcript is what counts and reports errors.
		}
		finally
		{
			partialBusy = false;
		}
	}

	private async Task FinishAsync(Utterance utterance, DateTime started, CancellationToken ct)
	{
		State?.Invoke("Writing down what you said…");
		try
		{
			List<TranscriptSegment> segments = await engine.TranscribeAsync(utterance.ToWav(), options.Language, ct);
			if (ct.IsCancellationRequested)
			{
				return;
			}
			segments = PhantomFilter.Keep(segments, utterance, options.TypicalLevel > 0 ? options.TypicalLevel : null);
			if (segments.Count == 0)
			{
				State?.Invoke("Listening…");
				lastActivity = DateTime.UtcNow;
				if (held == null)
				{
					Ignored?.Invoke();
				}
				return;
			}
			ProsodyResult prosody = ProsodyAnnotator.Annotate(segments, utterance, options.TypicalLevel > 0 ? options.TypicalLevel : null);
			options.TypicalLevel = options.TypicalLevel > 0 ? options.TypicalLevel * 0.8 + prosody.Level * 0.2 : prosody.Level;
			string plain = string.Join(" ", segments.Select((TranscriptSegment s) => s.Text)).Trim();
			SpokenMessage message = new SpokenMessage(prosody.Text, plain, prosody.Notes, utterance.Seconds);
			State?.Invoke("Listening…");
			if (!options.AutoSend)
			{
				Heard?.Invoke(message);
				return;
			}
			Hold(message, started);
		}
		catch (Exception) when (ct.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			Stop();
			Problem?.Invoke("Couldn't write down what you said: " + ex.Message + " Listening is off.");
		}
		finally
		{
			Interlocked.Decrement(ref finishing);
		}
	}

	/// <summary>
	/// Waits a moment before sending, longer when the thought sounds unfinished. If you start talking again,
	/// what you say next is joined on (with the pause noted) and the wait starts over.
	/// </summary>
	private void Hold(SpokenMessage message, DateTime started)
	{
		SpokenMessage combined;
		int version;
		lock (holdGate)
		{
			if (held != null)
			{
				double gap = Math.Max(0, (started - heldEnded).TotalSeconds);
				string note = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"[paused {gap:0.0}s]");
				held = new SpokenMessage(SpeechEnding.Join(held.Text, message.Text, gap), held.Plain + " " + message.Plain, held.Notes.Append(note).Concat(message.Notes).ToList(), held.Seconds + gap + message.Seconds);
			}
			else
			{
				held = message;
			}
			heldEnded = DateTime.UtcNow;
			lastActivity = DateTime.UtcNow;
			combined = held;
			version = ++holdVersion;
		}
		Waiting?.Invoke(combined.Text);
		double hold = SpeechEnding.HoldSeconds(combined.Plain);
		_ = Task.Run(async delegate
		{
			while (true)
			{
				await Task.Delay(100);
				SpokenMessage? send = null;
				lock (holdGate)
				{
					if (version != holdVersion || held == null)
					{
						return;
					}
					if (Hearing || Volatile.Read(ref finishing) > 0)
					{
						lastActivity = DateTime.UtcNow;
						continue;
					}
					if ((DateTime.UtcNow - lastActivity).TotalSeconds >= hold)
					{
						send = held;
						held = null;
						holdVersion++;
					}
				}
				if (send != null)
				{
					Heard?.Invoke(send);
					return;
				}
			}
		});
	}

	public void Dispose()
	{
		Stop();
		engine.Dispose();
	}
}
