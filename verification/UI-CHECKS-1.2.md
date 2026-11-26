# Aquila Local 1.2 native UI verification

Used work/release-1.2 with a separate synthetic profile in work/ui-data-1.2. The production Data folder was not used for inference or modified by testing.

Observed in the running WPF app:

- Memories opens its own dark window with personal/current-project filtering, history, source quotes and an editor.
- Selecting an automatically extracted codename shows its verbatim source quote, chat reference, timestamps and state.
- Pin this memory + Save / approve persisted Pinned=true, displayed a star, and marked the explicit save manual.
- Show history / forgotten displayed the old superseded codename alongside the active value.
- Open latest source chat returned to the supporting conversation and displayed the quoted source in a notice.
- Sending “Please remember that my favorite trail snack is mango crackers. Acknowledge briefly.” completed the answer, automatically saved one preference, and persisted a supporting quote and turn ID.
- A new conversation asking “What is my favorite trail snack? Reply with just its name.” answered “mango crackers”. The catalog retained only the validated preference; the question created no new memory.
- The learning turn total was 581 tokens, including 366 memory-learning tokens. The recall turn total was 712 tokens, including 449 memory-learning tokens. The session tooltip disclosed those subsets.
- Unload displayed “Model unloaded · memory released”. The QA window then closed cleanly.

Tests used Deckard Q4_K_M, 4,096 context, two CPU threads and automatic GPU offload. The model was unloaded afterward. Automated verification independently covers store forgetting/relearning, pinned conflict protection, corrections, project isolation and disabling learning/recall. Deletion through the UI was not exercised.

The final build additionally labels the search box and uses a memory-specific progress label. Both are minor presentation changes after the interaction checks. Version 1.1's picker/context/GPU evidence remains in UI-CHECKS-1.1.md; physical cross-window drag was not completed by that helper.
