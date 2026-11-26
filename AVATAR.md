# Aquila Local 1.6 — avatar architecture

Current model selection, Mira integration, importer compatibility changes and folder controls are documented in **AVATAR-LIBRARY.md**. Mira is the default; the guide remains available.

## Use

Open **Avatar…** in the left sidebar. Check **Enable avatar / open with Aquila**, then **Apply / open**. **Restart** also enables and opens it. **Stop avatar** disables it and releases its process, rendering resources and local connection. Closing the avatar window leaves chat available; use Apply / open to reopen it. A persisted enabled setting opens it the next time Aquila starts. Upgraded and fresh profiles default off.

Camera distance, height, field of view and frame rate apply through Apply / open. The default is 30 fps, with 15/30/60 choices. A minimized avatar processes control messages but reduces its update rate to 5 fps. It uses Godot's OpenGL compatibility renderer, adds no inference model and has a separate GPU allocation. Use 15 fps or Stop while other GPU applications need priority. It does not coordinate GPU scheduling with other applications.

The fallback **Aquila Guide** is an original procedural VRM 1.0 placeholder: blinking, breathing, small head motion and three facial expressions/morphs (blink, aa, happy). Its body is rigidly attached to the hips; articulated body animation is deferred. Presentation geometry lives in a separate file. It is intentionally a replaceable test asset rather than a finished character. Model and folder selection are available in 1.6; authored body animation is still deferred.

Real chat drives **Thinking** when the main answer request begins, **Idle** when that request completes, and **Interrupted** on cancellation/error before returning to idle. Memory extraction after the answer does not keep the avatar in Thinking. The **Listening** and **Speaking** buttons are 2.5-second visual previews, and are explicitly labeled in the avatar. Speaking preview moves the mouth synthetically; it is not lip sync. Version 1.8 adds real speech playback and amplitude-driven mouth movement, separate from these preview buttons. Microphone input is still deferred. See VOICE.md.

## Components and isolation

The main application remains WPF / .NET 8. `AvatarHost.cs` owns the optional child, authenticated WebSocket, state snapshot, camera settings and bounded log. `MainWindow.Avatar.cs` connects the answer lifecycle and a modeless `AvatarWindow.cs` settings panel. No LLM, memory, attachment or tool implementation is moved to Godot.

`AquilaAvatar` is an independent Godot 4.7.2 .NET project. Its exported Windows process and .NET runtime are in **avatar/**. Users need no Godot editor, .NET SDK, Python, Ollama or LM Studio to run the release. Keep the whole folder together. `Main.tscn` selects the presentation root; `AvatarView.cs` builds the stage, lights and configurable camera. `VrmLoader.gd` handles VRM import separately from presentation. `IAvatarFace` and `IAvatarMotion` separate facial and procedural motion controllers from transport.

The main app launches only its bundled executable, at below-normal priority. A Windows Job with kill-on-close owns that child, so app exit/crash releases it. Stop attempts an orderly shutdown before forcing the owned process to exit. Child exit does not crash or unload the chat model. Restart replaces credentials and session. Launch failure is surfaced in settings and leaves chat available; no restart loop continually consumes resources.

The raw **avatar/Models/aquila-guide.vrm** is needed by the runtime importer and is shipped outside the PCK. Godot's editor-generated resource remap is not a replacement for those GLB bytes. Replacing the model at development time or supplying `AQUILA_AVATAR_MODEL` when launching the avatar directly exercises the same loader. The harness validates the selected VRM and supplies its explicit absolute path to the child. Future model selection should validate user-selected VRM 1.0 files, bounds and framing explicitly. Mira and the guide are verified in 1.6. Other third-party VRMs and shader/physics variations still need compatibility checks.

## Transport version 1

`AvatarShared/AvatarProtocol.cs` is compiled into both applications. Every UTF-8 JSON message carries Version=1, Session, nonnegative Sequence and Type. Types: hello, state, applied, shutdown (ready/error names are reserved). State carries one of idle/listening/thinking/speaking/interrupted, optional utterance ID, preview flag, cue (none/nod/smile) and bounded presentation. Messages contain no prompts, answers, memories, file contents or executable commands. `applied` reports the state and actual camera/FPS configuration back from Godot after application on its main thread.

Each launch receives a random 256-bit Bearer token and new session via child environment variables. The listener binds only 127.0.0.1 on a random port; the token is absent from command-line arguments and logs. The HTTP upgrade validates path, Host, Upgrade, Connection, WebSocket key/version and authorization, rejects browser Origin and duplicate peers, and has an 8 KiB/3-second handshake bound. Text messages are bounded at 16 KiB and the renderer queue at 32. Session/version/state/field checks reject unsupported inputs. This authenticates the local peer; it is not a sandbox against another process running as the same Windows user.

Sequence numbers increase for state/camera/shutdown updates. Godot ignores older or duplicate sequence numbers. On a connection drop it retries with bounded backoff, while the host replays the latest state snapshot. The child stops retrying after 20 consecutive failures. A restart creates a fresh session, so old commands cannot control the new instance. The connection transports state only; no network-facing service or external server is required.

## Future speech and animation

`AvatarShared/SpeechContracts.cs` defines an interchangeable `ISpeechProvider` yielding utterance-tagged PCM16 chunks with sample rate, channels, sequence and timed visemes. It has no concrete provider in phase 1. `IAvatarSpeechPlayback` reserves scheduling of visemes, expressions and gestures, actual playback position updates, and interruption. `IAvatarFace.Viseme` already applies named morph weights. `VisemeFrame`, `ExpressionFrame`, `GestureFrame` and `SpeechPlaybackPosition` use time in seconds relative to an utterance.

The future harness audio player should own audio and report its actual played sample position. Avatar cues should follow that clock, not text-generation time. Stop/cancel must clear queued speech and mouth shapes by utterance. The v1 state receiver does not consume these future speech contracts yet: adding tested timeline messages requires a protocol extension/version negotiation and a concrete playback controller. No existing fields should be silently reinterpreted as audio. Providers can then change without changing chat/memory code or the character controller.

## Dependencies and build

Godot **4.7.2.stable.mono.official.ed1daf0bf**, .NET 8. VRM 1.0 import uses V-Sekai/godot-vrm revision **e15199f980064028bfa4fbee5e70dddb82dd55c3**, including its Godot-MToon-Shader addon, retained with licenses and an opt-in preserve-bind-pose patch documented in AVATAR-LIBRARY.md. No upstream sample model is distributed. Godot's LICENSE/COPYRIGHT and both addon licenses are in the application's licenses folder. The original placeholder and generator carry a separate CC0 notice.

Build computer: .NET 8 SDK, Godot 4.7.2 .NET editor, matching Windows export templates and NuGet access for first restore. `Build.ps1 -GodotPath '<Godot console exe>'` publishes WPF, imports resources and exports the avatar. Omit GodotPath to retain the supplied avatar binary while rebuilding the harness. Python is only needed to regenerate the included original VRM with `AquilaAvatar/make_robot.py`; the prebuilt VRM is included. The `.sln` beside the Godot C# project is required for export. Keep the source projects as siblings so shared compile links resolve.

VRM GLTF extensions register ahead of built-in scene conversion, with named skin binds (import flag 16), and unregister after loading. This is necessary for expression tracks and skeleton mapping. The face controller maps both Value and BlendShape animation tracks to the actual mesh properties. Do not infer working facial animation from a successful C# build alone; render and inspect it.

## Verification and debugging

`--verify-avatar <fresh-evidence> <small-model.gguf>` checks the exported renderer with synthetic data, CPU-only Qwen3 and two CPU threads. It verifies default-off behavior, protocol validation, model loading, all five acknowledged states, unauthorized/browser connection rejection, actual camera/FPS settings, reconnection, independent failure/restart, real chat with the avatar enabled and disabled, missing executable cleanup, persistence, log secrecy and actual WPF window closure with the avatar both enabled and disabled. There are 26 checks.

`--avatar-lifetime-fixture <directory>` writes the host and avatar PIDs after startup; an external test terminated only that isolated host and confirmed its avatar exited through the Job guard. The renderer's `-- --capture-dir <directory>` QA mode captures its own viewport for five states plus blink/smile, then exits. It does not capture the desktop. Native settings-panel checks separately exercised open, speaking preview and Stop; the automation capture helper was released afterward.

Version 1.4 evidence is under **verification/v1.4-*/**. The 121 existing regression checks were also rerun for embeddings, thinking, personalities, memory, files/vision/GPU and core chat. Large Heretic 9B inference remains outside these tests. All data used for verification is synthetic and separate from the user's Data. No test model or avatar remains running after the checks.

For failure, inspect **Data/logs/avatar.log** (bounded current/previous logs), the settings-panel status and **avatar/** file integrity. Missing exe/model/runtime, incompatible imports and startup deadlines are surfaced without disabling chat. If an avatar window was closed manually, Apply / open launches a new instance. Changing FPS or camera needs Apply / open. Stop avatar disables automatic reopening as well as closing the current child.

Version 1.6 adds the Mira/default and external-folder tests described in AVATAR-LIBRARY.md, for 190 total checks. Its UI evidence and current suites are under verification/v1.6-* and UI-CHECKS-1.6.md. Existing phase-1 captures remain as historical evidence; the new Mira/guide captures verify the current renderer.


## Speech extension in 1.8

SpeechService reports actual playback-position RMS levels every 40 ms. Avatar state messages now carry optional bounded Mouth and Emotion fields. Real speaking uses those levels with a 300 ms stale-frame cutoff; preview speaking retains its synthetic animation. The renderer acknowledges received mouth and mood values. SpeechContracts.cs remains a future phoneme/gesture interface; current speech uses amplitude frames and does not claim phoneme alignment. See VOICE.md.
