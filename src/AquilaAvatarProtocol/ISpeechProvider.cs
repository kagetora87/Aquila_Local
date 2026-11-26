using System;
using System.Collections.Generic;
using System.Threading;

namespace AquilaAvatarProtocol;

public interface ISpeechProvider : IAsyncDisposable
{
	string Id { get; }

	IAsyncEnumerable<SpeechAudioChunk> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken);
}
