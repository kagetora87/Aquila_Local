using AquilaLocal.Companion;
using AquilaLocal.Companion.Speech;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>Listening: speech detection on raw audio, and honest prosody notes measured from the audio itself.</summary>
public sealed class SpeechTests
{
    private const int Rate = 16000;

    private static float[] Noise(double seconds, float level = 0.004f, int seed = 1)
    {
        var rng = new Random(seed);
        var s = new float[(int)(seconds * Rate)];
        for (int i = 0; i < s.Length; i++) s[i] = (float)((rng.NextDouble() * 2 - 1) * level);
        return s;
    }

    private static float[] Voice(double seconds, float level = 0.25f)
    {
        var s = new float[(int)(seconds * Rate)];
        for (int i = 0; i < s.Length; i++) s[i] = (float)(Math.Sin(2 * Math.PI * 180 * i / Rate) * level * (0.8 + 0.2 * Math.Sin(2 * Math.PI * 4 * i / Rate)));
        return s;
    }

    private static float[] Concat(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    private static List<VadEvent> FeedAll(VoiceActivityDetector vad, float[] audio)
    {
        var events = new List<VadEvent>();
        for (int i = 0; i < audio.Length; i += 1600)
        {
            events.AddRange(vad.Feed(audio.AsSpan(i, Math.Min(1600, audio.Length - i))));
        }
        return events;
    }

    [Fact]
    public void Detects_one_utterance_with_a_mid_sentence_pause_and_ends_after_the_silence()
    {
        var vad = new VoiceActivityDetector(new ListeningSettings { EndSilenceSeconds = 1.0 });
        float[] audio = Concat(Noise(1.0), Voice(1.2), Noise(0.6, seed: 2), Voice(0.8), Noise(1.5, seed: 3));

        List<VadEvent> events = FeedAll(vad, audio);

        Assert.Equal(new[] { VadEvent.SpeechStarted, VadEvent.SpeechEnded }, events);
        Utterance u = vad.TakeUtterance();
        Assert.InRange(u.Seconds, 2.6, 4.2);   // both bursts, the pause between them, a little pre-roll and tail
        Assert.False(vad.InSpeech);
    }

    [Fact]
    public void Ignores_background_noise_and_tiny_clicks()
    {
        var vad = new VoiceActivityDetector(new ListeningSettings());
        float[] audio = Concat(Noise(2.0, 0.01f), Voice(0.06), Noise(2.0, 0.01f, seed: 5));

        List<VadEvent> events = FeedAll(vad, audio);

        Assert.DoesNotContain(VadEvent.SpeechEnded, events);
        Assert.False(vad.InSpeech);
    }

    [Fact]
    public void Pause_inside_the_utterance_becomes_a_marker_at_the_right_place()
    {
        var vad = new VoiceActivityDetector(new ListeningSettings { EndSilenceSeconds = 2.0 });
        FeedAll(vad, Concat(Noise(0.5), Voice(1.0), Noise(1.6, seed: 2), Voice(1.0), Noise(2.5, seed: 3)));
        Utterance u = vad.TakeUtterance();
        double pauseStart = u.Pauses.Single().Start;
        var segments = new List<TranscriptSegment>
        {
            new(0.0, pauseStart, "I was thinking"),
            new(pauseStart + 1.6, u.Seconds, "maybe we could go out tonight."),
        };

        ProsodyResult p = ProsodyAnnotator.Annotate(segments, u, typicalLevel: null);

        Assert.Matches(@"^I was thinking \[paused 1\.[4-8]s\] maybe we could go out tonight\.$", p.Text);
    }

    [Fact]
    public void Pause_in_the_middle_of_one_long_segment_is_placed_at_a_word_boundary()
    {
        var vad = new VoiceActivityDetector(new ListeningSettings { EndSilenceSeconds = 2.0 });
        FeedAll(vad, Concat(Noise(0.5), Voice(1.0), Noise(1.5, seed: 2), Voice(1.0), Noise(2.5, seed: 3)));
        Utterance u = vad.TakeUtterance();

        ProsodyResult p = ProsodyAnnotator.Annotate(new[] { new TranscriptSegment(0, u.Seconds, "one two three four five six") }, u, null);

        Assert.Matches(@"^one two three \[paused 1\.[3-7]s\] four five six$", p.Text);
    }

    [Fact]
    public void Restarts_and_trailing_off_are_marked()
    {
        var vad = new VoiceActivityDetector(new ListeningSettings());
        FeedAll(vad, Concat(Noise(0.5), Voice(1.5), Noise(1.5, seed: 2)));
        Utterance u = vad.TakeUtterance();

        ProsodyResult p = ProsodyAnnotator.Annotate(new[] { new TranscriptSegment(0, u.Seconds, "I- I just wanted to say that, and") }, u, null);

        Assert.Contains("[restarted]", p.Text);
        Assert.EndsWith("[trailed off]", p.Text);
    }

    [Fact]
    public void Quiet_speech_is_noted_against_their_usual_level()
    {
        var vad = new VoiceActivityDetector(new ListeningSettings());
        FeedAll(vad, Concat(Noise(0.5, 0.001f), Voice(1.5, 0.05f), Noise(1.5, 0.001f, seed: 2)));
        Utterance u = vad.TakeUtterance();

        ProsodyResult p = ProsodyAnnotator.Annotate(new[] { new TranscriptSegment(0, u.Seconds, "I missed you.") }, u, typicalLevel: 0.2);

        Assert.StartsWith("[spoke quietly]", p.Text);
        Assert.InRange(p.Level, 0.02, 0.06);
    }

    [Fact]
    public void Plain_speech_gets_no_markers()
    {
        var vad = new VoiceActivityDetector(new ListeningSettings());
        FeedAll(vad, Concat(Noise(0.5), Voice(2.0), Noise(1.5, seed: 2)));
        Utterance u = vad.TakeUtterance();

        ProsodyResult p = ProsodyAnnotator.Annotate(new[] { new TranscriptSegment(0, u.Seconds, "How was your day today?") }, u, typicalLevel: 0.2);

        Assert.Equal("How was your day today?", p.Text);
    }

    [Fact]
    public void Utterance_is_written_as_16_bit_mono_wav_for_the_transcriber()
    {
        var vad = new VoiceActivityDetector(new ListeningSettings());
        FeedAll(vad, Concat(Noise(0.5), Voice(1.0), Noise(1.5, seed: 2)));
        Utterance u = vad.TakeUtterance();

        byte[] wav = u.ToWav();

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(Rate, BitConverter.ToInt32(wav, 24));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));
        Assert.Equal(44 + u.Samples.Length * 2, wav.Length);
    }

    [Fact]
    public void Spoken_exchanges_are_remembered_as_heard_not_read()
    {
        using var dir = new TempDir();
        PersonaConfig c = TestPersona.Config();
        PersonaStore store = PersonaStore.Create(dir.File("v.db"), c, TestPersona.T0);
        CompanionRuntime rt = CompanionRuntime.ForTests(c, store, new FakeEmbedder());
        PreparedTurn p1 = rt.Prepare("the lighthouse trip next summer", new HashSet<string>(), false, TestPersona.T0);
        rt.Complete(p1, "chat", "t1", "the lighthouse trip next summer", "Yes, the lighthouse.", TestPersona.T0, spoken: true);
        PreparedTurn p2 = rt.Prepare("typed note about the garden", new HashSet<string>(), true, TestPersona.T0.AddMinutes(1));
        rt.Complete(p2, "chat", "t2", "typed note about the garden", "The garden, yes.", TestPersona.T0.AddMinutes(1));

        PreparedTurn p3 = rt.Prepare("remember the lighthouse trip?", new HashSet<string>(), true, TestPersona.T0.AddMinutes(2));

        Assert.Contains("you heard them say aloud: \"the lighthouse trip next summer\"", p3.RecalledText);
        Assert.True(store.RecentExchanges(5).Single(e => e.TurnId == "t1").Spoken);
        Assert.False(store.RecentExchanges(5).Single(e => e.TurnId == "t2").Spoken);
    }
}
