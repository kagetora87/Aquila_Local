using AquilaLocal.Companion.Speech;
using Xunit;

namespace Aquila.Companion.Tests;

/// <summary>
/// Whisper invents stock phrases ("Thank you.", "Oh.") from noise. These must never be sent as if the user said them,
/// while real speech, including a real "thank you", still goes through.
/// </summary>
public sealed class PhantomSpeechTests
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
        for (int i = 0; i < audio.Length; i += 800)
        {
            events.AddRange(vad.Feed(audio.AsSpan(i, Math.Min(800, audio.Length - i))));
        }
        return events;
    }

    private static Utterance Hear(float[] audio)
    {
        var vad = new VoiceActivityDetector(new ListeningSettings());
        Assert.Contains(VadEvent.SpeechEnded, FeedAll(vad, audio));
        return vad.TakeUtterance();
    }

    [Fact]
    public void A_short_thump_is_not_speech()
    {
        var vad = new VoiceActivityDetector(new ListeningSettings());
        List<VadEvent> events = FeedAll(vad, Concat(Noise(1.0), Voice(0.25), Noise(2.0, seed: 2)));
        Assert.DoesNotContain(VadEvent.SpeechEnded, events);
    }

    [Fact]
    public void Nothing_counts_as_speech_until_the_room_has_been_heard()
    {
        // Listening starts while a noise is already going: the first half second only learns the room.
        var vad = new VoiceActivityDetector(new ListeningSettings());
        List<VadEvent> events = FeedAll(vad, Concat(Noise(0.4, 0.03f), Noise(3.0, 0.03f, seed: 4)));
        Assert.Empty(events);
    }

    [Fact]
    public void Whisper_saying_it_heard_no_speech_is_dropped()
    {
        Utterance u = Hear(Concat(Noise(1.0), Voice(0.8), Noise(1.5, seed: 2)));
        var segments = new[] { new TranscriptSegment(0, u.Seconds, "Thank you.") { NoSpeechProbability = 0.82 } };

        Assert.Empty(PhantomFilter.Keep(segments, u, typicalLevel: null));
    }

    [Fact]
    public void A_low_confidence_guess_is_dropped()
    {
        Utterance u = Hear(Concat(Noise(1.0), Voice(0.8), Noise(1.5, seed: 2)));
        var segments = new[] { new TranscriptSegment(0, u.Seconds, "Oh.") { AverageLogProbability = -1.4 } };

        Assert.Empty(PhantomFilter.Keep(segments, u, typicalLevel: null));
    }

    [Fact]
    public void A_stock_phrase_from_a_faint_sound_is_dropped_even_without_whisper_scores()
    {
        Utterance u = Hear(Concat(Noise(1.0, 0.001f), Voice(0.6, 0.03f), Noise(1.5, 0.001f, seed: 2)));

        Assert.Empty(PhantomFilter.Keep(new[] { new TranscriptSegment(0, u.Seconds, "Thank you.") }, u, typicalLevel: 0.2));
        Assert.Empty(PhantomFilter.Keep(new[] { new TranscriptSegment(0, u.Seconds, "Oh") }, u, typicalLevel: 0.2));
        Assert.Empty(PhantomFilter.Keep(new[] { new TranscriptSegment(0, u.Seconds, "Thanks for watching!") }, u, typicalLevel: 0.2));
    }

    [Fact]
    public void A_real_thank_you_at_your_normal_voice_is_kept()
    {
        Utterance u = Hear(Concat(Noise(1.0), Voice(0.8, 0.2f), Noise(1.5, seed: 2)));
        var segments = new[] { new TranscriptSegment(0, u.Seconds, "Thank you.") { NoSpeechProbability = 0.02, AverageLogProbability = -0.2 } };

        Assert.Single(PhantomFilter.Keep(segments, u, typicalLevel: 0.2));
    }

    [Fact]
    public void Ordinary_speech_is_kept_and_only_the_phantom_tail_is_removed()
    {
        Utterance u = Hear(Concat(Noise(1.0), Voice(2.0), Noise(1.5, seed: 2)));
        var segments = new[]
        {
            new TranscriptSegment(0, 2.0, "How was your day today?") { NoSpeechProbability = 0.01, AverageLogProbability = -0.3 },
            new TranscriptSegment(2.0, u.Seconds, "Thank you.") { NoSpeechProbability = 0.7, AverageLogProbability = -1.2 },
        };

        List<TranscriptSegment> kept = PhantomFilter.Keep(segments, u, typicalLevel: 0.2);

        Assert.Equal("How was your day today?", kept.Single().Text);
    }
}

/// <summary>Not cutting you off: a pause after an unfinished thought waits longer before sending.</summary>
public sealed class SpeechEndingTests
{
    [Theory]
    [InlineData("I was thinking that maybe we could", true)]
    [InlineData("I went to the store and", true)]
    [InlineData("So the thing is,", true)]
    [InlineData("Well, I guess...", true)]
    [InlineData("Because um", true)]
    [InlineData("How was your day today?", false)]
    [InlineData("I missed you.", false)]
    [InlineData("That's amazing!", false)]
    public void Knows_when_a_thought_sounds_unfinished(string text, bool unfinished)
    {
        Assert.Equal(unfinished, SpeechEnding.SoundsUnfinished(text));
    }

    [Fact]
    public void Waits_longer_after_an_unfinished_thought()
    {
        Assert.True(SpeechEnding.HoldSeconds("I went to the store and") > SpeechEnding.HoldSeconds("I went to the store."));
        Assert.InRange(SpeechEnding.HoldSeconds("I went to the store."), 0.5, 1.2);
        Assert.InRange(SpeechEnding.HoldSeconds("I went to the store and"), 2.0, 4.0);
    }

    [Fact]
    public void Joining_two_parts_notes_the_pause_between_them()
    {
        Assert.Equal("I went to the store and [paused 2.4s] bought apples.", SpeechEnding.Join("I went to the store and", "bought apples.", 2.4));
    }

    [Fact]
    public void A_quiet_word_inside_a_sentence_does_not_end_it()
    {
        // Loud start, a softer stretch (about half as loud), then loud again: still one utterance.
        static float[] Voice(double seconds, float level)
        {
            var s = new float[(int)(seconds * 16000)];
            for (int i = 0; i < s.Length; i++) s[i] = (float)(Math.Sin(2 * Math.PI * 180 * i / 16000) * level);
            return s;
        }
        var rng = new Random(3);
        float[] Noise(double seconds) => Enumerable.Range(0, (int)(seconds * 16000)).Select(_ => (float)((rng.NextDouble() * 2 - 1) * 0.004)).ToArray();
        var vad = new VoiceActivityDetector(new ListeningSettings { EndSilenceSeconds = 1.0 });
        float[] audio = Noise(1.0).Concat(Voice(1.0, 0.25f)).Concat(Voice(1.5, 0.045f)).Concat(Voice(1.0, 0.25f)).Concat(Noise(1.5)).ToArray();
        var events = new List<VadEvent>();
        for (int i = 0; i < audio.Length; i += 800) events.AddRange(vad.Feed(audio.AsSpan(i, Math.Min(800, audio.Length - i))));
        Assert.Equal(new[] { VadEvent.SpeechStarted, VadEvent.SpeechEnded }, events);
    }
}
