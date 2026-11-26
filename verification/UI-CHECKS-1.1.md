# Native UI verification for 1.1.0

Used an isolated test profile. Its project folder was cleared before inference. Normal user Data was preserved.

Observed live per-app GPU activity and local-memory rows; context slider/dropdown changed from 16,384 to 131,072 with High memory risk, then back to 8,192 with Within recommendation. Native cap and green/red marker positions were visible.

Installed Deckard and its matching projector appeared. No model loaded on startup. The native picker selected synthetic injected.pdf and vision-fixture.png; both became removable attachment chips. A mixed question returned PDF-LANTERN-88 and the correct image color, red. The header showed 232 cumulative tokens (205 input, 27 output), matching saved chat data. Resident engine memory was attributed as Aquila model (~3.31 GB).

The physical cross-window drag was not completed by the automation helper. The window has AllowDrop and routed drag/drop handlers calling the same StageFilesAsync importer exercised by the picker and automated parser/content tests.

The isolated QA process was stopped after its answer was saved and verified. Final source additionally preserves a cleared projector setting across restart; an automated check covers that behavior.
