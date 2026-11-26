# Native UI verification — 2026-11-11

Checked the published executable through Windows accessibility and screenshots, using `--data-dir` with an isolated test profile. User-facing Data was not used for test conversations.

* App opened with no model loaded; CPU / two-thread status was visible.
* Dark theme, sidebar, model controls, memory field, composer and response panels were visually inspected. A main-panel background contrast issue was corrected and rechecked.
* A message entered into the composer and sent through the native Send button loaded the small model and produced a real streamed response.
* The completed response showed formatted heading/bold/bullets and inference timing.
* The conversation appeared in the saved-history sidebar.
* Unload released the small model; the status changed to 'Model unloaded · memory released'.
* After removing unused bundled CLI tools and RPC components, the Load button successfully reloaded the small model using only the shipped engine files.
* The test app was closed with the small model loaded to exercise parent-owned engine cleanup.

Inference used Qwen3 0.6B Q4_K_M in CPU-only mode with two threads and below-normal priority. No Heretic 9B inference or GPU generation was run. Screenshots were inspected in the computer-use tool; no screenshot files are included.
