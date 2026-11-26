# Native UI and rendered avatar checks — 1.4

Used a separate synthetic Data profile with CPU-only Qwen3, two threads, memory learning off and no workspace. The user's Data and model selections were not changed.

- Opened Avatar… from the resident sidebar in WPF. Inspected the actual rendered settings panel: enable checkbox, open/restart/stop, three camera sliders, frame-rate dropdown, all five preview buttons and the Phase 1 explanation were present and readable without scrolling at the default size.
- Restart opened the exported Godot child and enabled/persisted the setting. The panel reported Connected / idle / VRM model ready.
- Speaking preview reported speaking, then returned to idle. Preview labels explicitly distinguish the visual behavior from microphone/audio operation.
- Stop avatar unchecked the enabled option, removed the child and reported stopped. Chat remained available with no model loaded.
- The first main-window close exposed a synchronous-disposal WPF reentrancy error. Fixed by queuing the final Close on the dispatcher. The final binary's verifier subsequently opened and closed actual WPF windows with avatar disabled and enabled, checking process/listener cleanup in both cases. These are checks 25/26 of the avatar suite.
- Avatar's own viewport render mode produced idle, listening, thinking, speaking, interrupted, blink and smile PNGs. Inspected idle, speaking and blink: complete character on the platform, attached limbs, visible mouth-shape change and closed eyelids. Thinking/listening/interrupted are covered by applied-state acknowledgments as well as captured frames. No desktop capture is needed for these PNGs.
- No TTS, microphone, real audio playback or timed audio lip sync was tested or implemented. Arbitrary third-party VRMs and full-body authored animation are outside this slice.
- Released the exact native capture helper created for this test. No test app, avatar or model was left running after verification. The separate existing swift helper was not touched.

Current automated evidence: v1.4-avatar (26), v1.4-avatar-lifetime (1), and the six existing regression suites (121). Total 148 passing checks. Renderer captures are in v1.4-avatar-render. All verification used synthetic inputs; original Heretic 9B inference remains untested.
