# Avatar library native UI and visual verification — 1.6

Used an isolated synthetic Data profile. No user's saved conversations, settings or LLM model was loaded for UI work.

- Opened Avatar… and inspected the rendered panel: model dropdown, library folder label, Choose folder…, Choose file…, Refresh, model creator/size/facial-preset metadata, existing lifecycle/camera/FPS controls and state previews fit the scrollable panel.
- Opened the native folder picker, entered the synthetic external folder with spaces, selected it and verified that the panel showed that folder.
- Opened the dropdown and selected Mira custom from that folder. Metadata reported Mira / rengasato / 16.3 MB with blink and mouth presets.
- Restart enabled and launched that external model. The panel reported Connected / idle / VRM model ready. Verified persisted settings contained the exact external folder and file, and the child log identified Mira with 198 bones and 15 expression tracks.
- Stop disabled and closed the child. Closed the settings and main windows cleanly. Released the exact native capture helper created for this test; the existing swift helper was not touched.
- Choose file… was visually present; its native dialog selection was not separately automated. The external-file resolution/validation/loading path is covered by the integration suite.

Rendered evidence uses the avatar's own viewport, without desktop recording. Inspected Mira idle, blink and speaking: complete character, intact clothing/limbs, relaxed arms, closed eyelids and mouth movement. Early diagnostic renders exposed incorrect extension ordering (spring-node lookup errors) and source skin distortion from axis retargeting. Final importer preserves original bind/rest transforms, normalizes bone names, registers extensions in dependency order before mesh conversion and uses embedded original texture channels. Final Mira logs contain no import/runtime errors. Original VRM bytes match the downloaded asset hash.

The guide remains selectable and its headless loading/switching is also tested. State motion remains the procedural Phase 1 layer, with a neutral arm pose for humanoid models. No speech engine, audio lip sync or authored body-animation library was added.

190 checks: prior 170 plus 20 avatar-library checks. Existing regression suites were rerun with the updated settings and renderer. Tests use synthetic profiles and sequential bounded inference. No test model or avatar remains running after verification.
