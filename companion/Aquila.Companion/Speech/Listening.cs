using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AquilaLocal.Companion.Speech;

/// <summary>How listening behaves. Audio is 16 kHz mono floats in -1..1.</summary>
public sealed class ListeningSettings
{
    public const int SampleRate = 16000;

    /// <summary>Silence that ends what you're saying and sends it.</summary>
    public double EndSilenceSeconds { get; set; } = 1.0;

    /// <summary>A silence at least this long inside an utterance is noted as a pause.</summary>
    public double PauseSeconds { get; set; } = 0.7;

    /// <summary>Less voiced audio than this is a cough or a click, not speech.</summary>
    public double MinSpeechSeconds { get; set; } = 0.25;

    /// <summary>One utterance is cut here and sent, so a stuck microphone can't record forever.</summary>
    public double MaxUtteranceSeconds { get; set; } = 60;

    /// <summary>Speech must be this many times louder than the room's background noise.</summary>
    public double StartFactor { get; set; } = 3.0;

    /// <summary>Absolute minimum level counted as voice, so a silent room doesn't make every breath "speech".</summary>
    public double MinLevel { get; set; } = 0.008;
}

public enum VadEvent { SpeechStarted, SpeechEnded }

public sealed record Pause(double Start, double Duration);

/// <summary>One thing the user said: the audio plus what was measured about it.</summary>
public sealed class Utterance
{
    public const double FrameSeconds = 0.02;

    public required float[] Samples { get; init; }

    /// <summary>Loudness (RMS) of each 20 ms frame.</summary>
    public required float[] Rms { get; init; }

    public required bool[] Voiced { get; init; }

    public required IReadOnlyList<Pause> Pauses { get; init; }

    public double Seconds => Samples.Length / (double)ListeningSettings.SampleRate;

    /// <summary>Typical loudness while speaking (median RMS of voiced frames).</summary>
    public double Level
    {
        get
        {
            float[] v = Rms.Where((_, i) => Voiced[i]).OrderBy(x => x).ToArray();
            return v.Length == 0 ? 0 : v[v.Length / 2];
        }
    }

    public double VoicedSeconds => Voiced.Count(x => x) * FrameSeconds;

    /// <summary>Voiced seconds between two times (seconds from the start of the utterance).</summary>
    public double VoicedBetween(double from, double to)
    {
        int a = Math.Max(0, (int)Math.Floor(from / FrameSeconds));
        int b = Math.Min(Voiced.Length, (int)Math.Ceiling(to / FrameSeconds));
        int n = 0;
        for (int i = a; i < b; i++)
        {
            if (Voiced[i]) n++;
        }
        return n * FrameSeconds;
    }

    /// <summary>The audio as a 16-bit mono WAV file, which is what the transcriber takes.</summary>
    public byte[] ToWav() => Wav.Pcm16(Samples, ListeningSettings.SampleRate);
}

public static class Wav
{
    public static byte[] Pcm16(ReadOnlySpan<float> samples, int rate)
    {
        var bytes = new byte[44 + samples.Length * 2];
        void Ascii(int at, string s) => Encoding.ASCII.GetBytes(s).CopyTo(bytes, at);
        Ascii(0, "RIFF");
        BitConverter.GetBytes(36 + samples.Length * 2).CopyTo(bytes, 4);
        Ascii(8, "WAVE");
        Ascii(12, "fmt ");
        BitConverter.GetBytes(16).CopyTo(bytes, 16);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 20);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 22);
        BitConverter.GetBytes(rate).CopyTo(bytes, 24);
        BitConverter.GetBytes(rate * 2).CopyTo(bytes, 28);
        BitConverter.GetBytes((short)2).CopyTo(bytes, 32);
        BitConverter.GetBytes((short)16).CopyTo(bytes, 34);
        Ascii(36, "data");
        BitConverter.GetBytes(samples.Length * 2).CopyTo(bytes, 40);
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)Math.Clamp(samples[i] * 32767f, -32768f, 32767f);
            BitConverter.GetBytes(v).CopyTo(bytes, 44 + i * 2);
        }
        return bytes;
    }
}

/// <summary>
/// Finds speech in a live microphone stream: an adaptive noise floor, a short confirmation before speech
/// "starts" (so clicks and coughs don't count), and a silence long enough to mean "I'm done".
/// </summary>
public sealed class VoiceActivityDetector
{
    private const int FrameSamples = 320;           // 20 ms at 16 kHz
    private const int ConfirmFrames = 5;            // 100 ms of voice before speech counts as started
    private const int PreRollFrames = 15;           // keep 300 ms before the start, so the first syllable isn't cut
    private const int TailFrames = 15;              // and 300 ms after the last voiced frame

    private readonly ListeningSettings settings;
    private readonly float[] pending = new float[FrameSamples];
    private int pendingCount;
    private double floor = -1;
    private double threshold;
    private readonly Queue<(float[] Samples, float Rms)> preRoll = new();
    private readonly List<float[]> frames = new();
    private readonly List<float> rms = new();
    private readonly List<bool> voiced = new();
    private int candidate;
    private int silentRun;
    private Utterance? finished;

    public ListeningSettings Settings => settings;

    public bool InSpeech { get; private set; }

    /// <summary>Background noise level (RMS) while nobody is speaking.</summary>
    public double NoiseFloor => Math.Max(floor, 0);

    public VoiceActivityDetector(ListeningSettings settings)
    {
        this.settings = settings;
    }

    public List<VadEvent> Feed(ReadOnlySpan<float> samples)
    {
        var events = new List<VadEvent>();
        while (samples.Length > 0)
        {
            int take = Math.Min(FrameSamples - pendingCount, samples.Length);
            samples[..take].CopyTo(pending.AsSpan(pendingCount));
            pendingCount += take;
            samples = samples[take..];
            if (pendingCount == FrameSamples)
            {
                pendingCount = 0;
                Frame((float[])pending.Clone(), events);
            }
        }
        return events;
    }

    private void Frame(float[] frame, List<VadEvent> events)
    {
        double sum = 0;
        foreach (float s in frame) sum += s * s;
        float level = (float)Math.Sqrt(sum / frame.Length);
        if (!InSpeech)
        {
            if (floor < 0)
            {
                floor = level;
            }
            double startAt = Math.Max(floor * settings.StartFactor, settings.MinLevel);
            if (level > startAt)
            {
                candidate++;
            }
            else
            {
                candidate = 0;
                floor = floor * 0.95 + level * 0.05;
            }
            preRoll.Enqueue((frame, level));
            while (preRoll.Count > PreRollFrames + ConfirmFrames)
            {
                preRoll.Dequeue();
            }
            if (candidate >= ConfirmFrames)
            {
                InSpeech = true;
                threshold = startAt * 0.7;
                silentRun = 0;
                frames.Clear();
                rms.Clear();
                voiced.Clear();
                int i = 0;
                int confirmStart = preRoll.Count - ConfirmFrames;
                foreach ((float[] f, float r) in preRoll)
                {
                    frames.Add(f);
                    rms.Add(r);
                    voiced.Add(i >= confirmStart);
                    i++;
                }
                preRoll.Clear();
                candidate = 0;
                events.Add(VadEvent.SpeechStarted);
            }
            return;
        }
        bool isVoice = level > threshold;
        frames.Add(frame);
        rms.Add(level);
        voiced.Add(isVoice);
        silentRun = isVoice ? 0 : silentRun + 1;
        bool tooLong = frames.Count * Utterance.FrameSeconds >= settings.MaxUtteranceSeconds;
        if (silentRun * Utterance.FrameSeconds >= settings.EndSilenceSeconds || tooLong)
        {
            InSpeech = false;
            Utterance? u = Build();
            if (u != null)
            {
                finished = u;
                events.Add(VadEvent.SpeechEnded);
            }
        }
    }

    /// <summary>The audio so far while someone is still speaking (for live partial transcripts).</summary>
    public float[] CurrentAudio() => InSpeech ? frames.SelectMany(f => f).ToArray() : Array.Empty<float>();

    /// <summary>The utterance that just ended. Throws if none is waiting.</summary>
    public Utterance TakeUtterance()
    {
        Utterance u = finished ?? throw new InvalidOperationException("No finished utterance is waiting.");
        finished = null;
        return u;
    }

    /// <summary>Drops whatever is being heard right now (for example when listening is switched off mid-sentence).</summary>
    public void Reset()
    {
        InSpeech = false;
        candidate = 0;
        frames.Clear();
        rms.Clear();
        voiced.Clear();
        preRoll.Clear();
        finished = null;
    }

    private Utterance? Build()
    {
        int last = voiced.LastIndexOf(true);
        if (last < 0 || voiced.Count(v => v) * Utterance.FrameSeconds < settings.MinSpeechSeconds)
        {
            frames.Clear();
            rms.Clear();
            voiced.Clear();
            return null;
        }
        int end = Math.Min(frames.Count, last + 1 + TailFrames);
        float[] samples = frames.Take(end).SelectMany(f => f).ToArray();
        float[] r = rms.Take(end).ToArray();
        bool[] v = voiced.Take(end).ToArray();
        int first = Array.IndexOf(v, true);
        var pauses = new List<Pause>();
        int runStart = -1;
        for (int i = first; i <= last; i++)
        {
            if (!v[i] && runStart < 0)
            {
                runStart = i;
            }
            else if (v[i] && runStart >= 0)
            {
                double length = (i - runStart) * Utterance.FrameSeconds;
                if (length >= settings.PauseSeconds)
                {
                    pauses.Add(new Pause(runStart * Utterance.FrameSeconds, length));
                }
                runStart = -1;
            }
        }
        frames.Clear();
        rms.Clear();
        voiced.Clear();
        return new Utterance { Samples = samples, Rms = r, Voiced = v, Pauses = pauses };
    }
}

/// <summary>A piece of transcript with its time span (seconds from the start of the utterance).</summary>
public sealed record TranscriptSegment(double Start, double End, string Text);

/// <param name="Text">The transcript with bracketed notes, e.g. "[spoke quietly] I was thinking [paused 1.8s] maybe...".</param>
/// <param name="Level">How loud they spoke (median RMS), to keep a running sense of their usual level.</param>
public sealed record ProsodyResult(string Text, double Level, IReadOnlyList<string> Notes);

/// <summary>
/// Turns a transcript into what was actually heard. Whisper tidies speech up and doesn't report pauses, so the
/// notes are measured from the audio itself: pauses, restarts, trailing off, speed and loudness.
/// </summary>
public static class ProsodyAnnotator
{
    public const double QuickWordsPerSecond = 4.5;
    public const double SlowWordsPerSecond = 1.5;

    private static readonly Regex Restart = new(@"\b(\w{1,12})(?:-|—|,|\.\.\.|…)\s*(\1)\b|\b(\w{1,3})\s+(\3)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> Trailers = new(StringComparer.OrdinalIgnoreCase) { "and", "but", "so", "or", "because", "like", "um", "uh", "if", "then", "the", "a" };

    public static ProsodyResult Annotate(IReadOnlyList<TranscriptSegment> segments, Utterance audio, double? typicalLevel)
    {
        var notes = new List<string>();
        var pieces = segments.Where(s => s.Text.Trim().Length > 0).Select(s => new StringBuilder(s.Text.Trim())).ToList();
        var clean = segments.Where(s => s.Text.Trim().Length > 0).ToList();
        var after = new Dictionary<int, List<string>>();   // markers to put after segment i

        foreach (Pause pause in audio.Pauses.OrderByDescending(p => p.Start))
        {
            string marker = string.Create(CultureInfo.InvariantCulture, $"[paused {pause.Duration:0.0}s]");
            int inside = clean.FindIndex(s => s.Start <= pause.Start && pause.Start < s.End);
            if (inside >= 0)
            {
                TranscriptSegment s = clean[inside];
                double total = audio.VoicedBetween(s.Start, s.End);
                double before = audio.VoicedBetween(s.Start, pause.Start);
                string text = pieces[inside].ToString();
                int at = total <= 0 ? text.Length : (int)Math.Round(text.Length * before / total);
                at = SnapToWordBoundary(text, at);
                if (at <= 0 || at >= text.Length)
                {
                    if (at <= 0 && inside > 0)
                    {
                        AddAfter(after, inside - 1, marker);
                    }
                    else if (at >= text.Length)
                    {
                        AddAfter(after, inside, marker);
                    }
                    continue;
                }
                pieces[inside].Insert(at, " " + marker);
                notes.Add(marker);
                continue;
            }
            int previous = clean.FindLastIndex(s => s.End <= pause.Start + 0.05);
            if (previous >= 0 && previous < clean.Count - 1)
            {
                AddAfter(after, previous, marker);
                notes.Add(marker);
            }
        }

        var sb = new StringBuilder();
        for (int i = 0; i < pieces.Count; i++)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(pieces[i]);
            if (after.TryGetValue(i, out List<string>? markers))
            {
                foreach (string m in markers) sb.Append(' ').Append(m);
            }
        }
        string body = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();

        body = Restart.Replace(body, m =>
        {
            notes.Add("[restarted]");
            return m.Groups[1].Success
                ? m.Value[..(m.Groups[2].Index - m.Index)].TrimEnd() + " [restarted] " + m.Groups[2].Value
                : m.Groups[3].Value + " [restarted] " + m.Groups[4].Value;
        });

        var prefix = new List<string>();
        double level = audio.Level;
        if (typicalLevel is double typical && typical > 0)
        {
            if (level < typical * 0.55) prefix.Add("[spoke quietly]");
            else if (level > typical * 1.8) prefix.Add("[spoke loudly]");
        }
        int words = Regex.Matches(Regex.Replace(body, @"\[[^\]]*\]", " "), @"[\p{L}\p{N}']+").Count;
        double voicedSeconds = audio.VoicedSeconds;
        if (words >= 5 && voicedSeconds >= 1.5)
        {
            double rate = words / voicedSeconds;
            if (rate > QuickWordsPerSecond) prefix.Add("[spoke quickly]");
            else if (rate < SlowWordsPerSecond) prefix.Add("[spoke slowly]");
        }
        notes.AddRange(prefix);

        string lastWord = Regex.Match(body, @"([\p{L}']+)[^\p{L}']*$").Groups[1].Value;
        bool ellipsis = body.EndsWith("...", StringComparison.Ordinal) || body.EndsWith('…');
        if (ellipsis || Trailers.Contains(lastWord) || FadesOut(audio))
        {
            body += " [trailed off]";
            notes.Add("[trailed off]");
        }

        string text2 = (prefix.Count > 0 ? string.Join(" ", prefix) + " " : "") + body;
        return new ProsodyResult(text2.Trim(), level, notes);
    }

    private static void AddAfter(Dictionary<int, List<string>> after, int index, string marker)
    {
        if (!after.TryGetValue(index, out List<string>? list))
        {
            after[index] = list = new List<string>();
        }
        list.Insert(0, marker);
    }

    private static int SnapToWordBoundary(string text, int at)
    {
        at = Math.Clamp(at, 0, text.Length);
        if (at == 0 || at == text.Length || text[at] == ' ' || text[at - 1] == ' ')
        {
            return text.Length > at && text[at] == ' ' ? at : (at > 0 && text[at - 1] == ' ' ? at - 1 : at);
        }
        int left = text.LastIndexOf(' ', at - 1);
        int right = text.IndexOf(' ', at);
        if (left < 0 && right < 0) return text.Length;
        if (left < 0) return right;
        if (right < 0) return left;
        return at - left <= right - at ? left : right;
    }

    /// <summary>The last half second of voice is much quieter than the rest: their voice faded away.</summary>
    private static bool FadesOut(Utterance audio)
    {
        int last = Array.LastIndexOf(audio.Voiced, true);
        int span = (int)(0.5 / Utterance.FrameSeconds);
        if (last < span * 3)
        {
            return false;
        }
        double end = 0;
        int n = 0;
        for (int i = last - span + 1; i <= last; i++)
        {
            if (audio.Voiced[i]) { end += audio.Rms[i]; n++; }
        }
        return n > 0 && end / n < audio.Level * 0.45;
    }
}
