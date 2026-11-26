using System;
using System.Collections.Generic;

namespace AquilaAvatarProtocol;

public sealed record SpeechAudioChunk(string Utterance, long Sequence, int SampleRate, int Channels, ReadOnlyMemory<short> Pcm16, IReadOnlyList<VisemeFrame> Visemes);
