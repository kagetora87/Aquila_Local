# Aquila Local voice output

Open **Voice…**, select a speaker, enable **Speak completed replies automatically**, and choose **Save voice**. **Play sample** auditions the settings. The main sidebar provides **Replay** and **Stop voice**. Automatic speech defaults off so an upgrade does not unexpectedly play audio. Playback uses the Windows default output device. Microphone input is outside this release.

## Import a cloned voice

Open **Voice… → Import voice sample…**. Choose a clean WAV with 5–30 seconds of one speaker. Enter a name and the exact words spoken, then select **Preview clone**. **Save voice** adds it to the local library and selects it in the voice picker. Back in Voice, enable automatic speech if desired and select **Save voice** again to apply the selection to chat. The first version uses one reference clip per voice profile.

The saved voice consists of a normalized WAV and transcript in **Data/voice-samples/**. Entering a transcript keeps transcription out of the runtime and lets the bundled model work offline. **Delete selected clone** removes Aquila's stored copy. The original WAV stays where you imported it from.

## Delivery

Kitten TTS 2 has eight bundled presets and supports local voice references. Its delivery uses emotion tags and an expression setting. The Qwen3-TTS presets also accept natural-language directions and pacing. Installed Windows voices provide a lighter playback option with less control over delivery.

**Pace** works with Kitten presets and clones. Slow and Lively adjust generated audio tempo while preserving pitch. **Optional delivery direction** tells the chat model how to use Kitten's supported emotions. You can also pick a mood directly. For a quick audition without a chat request, **Play sample** uses the selected mood or an explicit direction such as `[joyful]`. Qwen still sends its direction to the speech engine.

Enable **Apply voice tags automatically** to let the chat model change the delivery within a reply or follow a request such as “start excited and get gentle in the second paragraph.” Kitten can also use inline cues such as a laugh, sigh, or pause. **Reveal tags** shows the saved cues at their positions in the chat after you save voice settings. Cues are stored separately from the written reply. The message's Copy button and Export chat use the clean reply. Replay reuses the saved cues with the selected voice.

With Auto mood and passage tags off, the chat model can supply one opening delivery cue in its existing response request. Aquila removes that cue from the answer and records it separately. It also supports the full set of Kitten emotions, such as excited, sad, stern, and surprised. The Mood selection provides the default delivery for a passage without a cue.

Tag selection depends on the chat model and expression depends on the selected voice. An emotion tag can steer the delivery without guaranteeing a particular acting performance. Avatar expression uses the model's available facial morphs.

## Timing and resources

Speech starts preparing when the final answer is complete, before background memory extraction finishes. Thinking, tool activity, cancelled output and token-limited replies do not automatically speak. The answer is divided into bounded sentence chunks; one plays while the next is synthesized. Preparation is not instantaneous. Cold loading adds latency and long answers can contain pauses between chunks. Stop voice cancels queued playback and the owned speech process. A new message or conversation interrupts current speech. Closing Aquila waits for cancellation and disposes audio, speech and avatar resources.

Auto prefers CPU for Kitten when approximately 6 GiB RAM is free, and can choose CUDA when approximately 6 GiB VRAM is free. Auto prefers CUDA for Qwen. Explicit GPU or CPU modes keep the selected compute preference and report resource problems. The worker uses two CPU threads and below-normal priority. The chat model, context cache, vision projector and avatar share resources with speech.

By default, speech unloads after playback. Keep-loaded reserves its memory to reduce later loading delays. Stop voice, changing voice settings, changing conversations and exiting unload it. If an active utterance is interrupted by a new message, its worker is also unloaded. The app never silently switches the chosen voice to a Windows voice after an expressive-voice failure. Text replies remain available when audio fails. Replay is an explicit action and works even when automatic speaking is disabled.

## Saving a reply as audio

The speaker button next to Copy on a finished reply renders it to one 16-bit .wav file (same voice,
moods and pacing as playback, with a short pause between parts) without playing it. Anything she is
saying is stopped first.

## Microphone input

Listening needs whisper.cpp's `whisper-server.exe` and one `ggml-*.bin` model in a `whisper` folder next
to Aquila (the largest model there is used). Aquila starts the server on 127.0.0.1 on a random port, inside
a job object so it closes with Aquila, and records 16 kHz mono from the chosen microphone. Speech is found
with an adaptive-noise-floor detector (20 ms frames, 300 ms pre-roll), partial transcripts refresh about
every 0.9 s, and a pause of the chosen length ends and sends the utterance. While she speaks the microphone
is ignored, unless "talking interrupts her" is on, in which case only clearly louder speech counts and stops
her (use headphones).

## Architecture and privacy

`Agent` adds delivery instructions to the existing request only for automatic mood. `VoiceCueDecoder` hides reserved cues while streaming. `MainWindow.Voice` starts speech only after a complete reply. `SpeechService` owns cancellation, a bounded audio queue, temporary WAVs, playback and level frames. `SpeechProcess` owns an isolated Python worker through private JSON-lines pipes and a kill-on-close Windows Job. `SpeechWorker.py` loads only pinned bundled files using local-files-only/offline flags; socket connections are disabled inside that process. It has no HTTP listener or runtime downloader. Python, PyTorch and model weights are shipped inside `speech/`; users need no separately installed Python, LM Studio or voice application.

Only cleaned final-answer text and bounded delivery instructions reach the worker. Reasoning, memories, system prompts, attachments and API credentials are not sent to it. Code blocks and raw URLs are replaced by short written-reply references. Long replies stop at the configured spoken-character cap and say that the remainder is written. Temporary PCM16 mono 24 kHz WAVs are removed after playback. A crash may leave temporary files; they remain local in `Data/speech-temp`. Bounded diagnostic logs live in `Data/logs/speech.log`. Do not remove that folder while speech is active.

The optional avatar receives only authenticated state, mood and measured playback amplitude, never text or audio. Its mouth follows the actual playback position, closes on silence/end/stop, and falls closed if updates stop arriving. This is amplitude-driven mouth movement, not phoneme-aligned lip synchronization. Happy/supportive facial morphs are deliberately restrained.

## Build, provenance and verification

Build.ps1 publishes the app and SpeechWorker.py. Retain the supplied `speech/` assets alongside engine, embeddings, models, avatar and licenses when rebuilding. Speech assets are supplied with the application. See `speech/kitten-manifest.json`, `speech/model-manifest.json`, the runtime package lists, and bundled licenses for provenance. Aquila uses the full Kitten TTS 2 cloning model and the student_w4 decoder. The bundled emb4 tensors were expanded once to safetensors shards to reduce cold startup time.

Run `Aquila Local.exe --verify-voice <new-output-directory> <speech-assets-directory> <small-GGUF-model>` for isolated tests. It exercises real expressive generation, speaker changes, different delivery directions, playback, stopping, exported avatar acknowledgements, local-model cues, token accounting, Windows fallback, main-window automatic speech and closing during playback. Generated audition WAVs are evidence, not proof of subjective naturalness. Existing regression suites remain available; consult DEVELOPMENT.md. Test profiles do not use production Data.

Use `--verify-kitten <new-output-directory> <speech-assets-directory> <small-GGUF-model> [--gpu]` to import a synthetic test voice, synthesize a preview and preset, save/reload the clone and speak a completed local reply. It uses an isolated profile and muted playback.

Use `--verify-delivery <new-output-directory> <speech-assets-directory> <small-GGUF-model>` to exercise streamed tags, clean text, reveal, model-selected delivery, available controls, and Kitten audio pace. It writes its test audio without playing it.
