using System.Collections.Generic;

namespace AquilaAvatarProtocol;

public interface IAvatarSpeechPlayback
{
	void Schedule(string utterance, IReadOnlyList<VisemeFrame> visemes, IReadOnlyList<ExpressionFrame> expressions, IReadOnlyList<GestureFrame> gestures);

	void Synchronize(SpeechPlaybackPosition actualAudioPosition);

	void Interrupt(string utterance);
}
