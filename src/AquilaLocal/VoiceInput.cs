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
	public double EndSilenceSeconds { get; set; } = 1.0;

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
		Level = 0;
	}

	/// <summary>
	/// Called while she's speaking: with interrupting off, the microphone is ignored so her own voice isn't heard
	/// as yours; with it on, only clearly louder speech counts (and stops her).
	/// </summary>
	public void SetHerVoicePlaying(bool playing)
	{
		lock (gate)
		{
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
			if (muted)
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
			State?.Invoke("Hearing you…");
			SpeechStarted?.Invoke();
		}
		CancellationToken ct = life?.Token ?? CancellationToken.None;
		if (finished != null)
		{
			_ = FinishAsync(finished, ct);
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

	private async Task FinishAsync(Utterance utterance, CancellationToken ct)
	{
		State?.Invoke("Writing down what you said…");
		try
		{
			List<TranscriptSegment> segments = await engine.TranscribeAsync(utterance.ToWav(), options.Language, ct);
			if (ct.IsCancellationRequested)
			{
				return;
			}
			if (segments.Count == 0)
			{
				State?.Invoke("Listening… (didn't catch that)");
				return;
			}
			ProsodyResult prosody = ProsodyAnnotator.Annotate(segments, utterance, options.TypicalLevel > 0 ? options.TypicalLevel : null);
			options.TypicalLevel = options.TypicalLevel > 0 ? options.TypicalLevel * 0.8 + prosody.Level * 0.2 : prosody.Level;
			string plain = string.Join(" ", segments.Select((TranscriptSegment s) => s.Text)).Trim();
			State?.Invoke("Listening…");
			Heard?.Invoke(new SpokenMessage(prosody.Text, plain, prosody.Notes, utterance.Seconds));
		}
		catch (Exception) when (ct.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			Stop();
			Problem?.Invoke("Couldn't write down what you said: " + ex.Message + " Listening is off.");
		}
	}

	public void Dispose()
	{
		Stop();
		engine.Dispose();
	}
}
