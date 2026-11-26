# Aquila Local 1.3 native UI verification

Ran work/release-1.3 with isolated synthetic Data in work/ui-data-1.3. Production Data was not used for inference or changed by these tests.

Observed in the running application:

- Active personality selector and Manage personalities open correctly. Default preserves existing global instructions.
- The profile window lists Default, Warm & direct, Analytical and Creative storyteller. Default's editor is protected.
- New profile enabled editable name/style/examples. Created UI proof voice, supplied a compact speaking style and example exchange, saved it, and activated it through Use selected profile. The main selector displayed the saved voice.
- The memory panel displayed Match memories by meaning and its CPU/one-thread/cache/fallback explanation.
- Generation settings displayed thinking off, then updated to “Up to 256 thinking tokens · at least 256 tokens reserved for the answer” when enabled with a 512-token response limit.
- Sent “Please remember that I prefer short explanations. What is 17 times 23?” The completed answer began “Briefly:” and correctly answered 391. A Thinking expander and UI proof voice label were visible. One preference was automatically learned from the typed message.
- A new chat asked “What length of explanation suits me?” It recalled the preference and answered in the same speaking style. Saved turn activity reported MiniLM + keywords, one eligible entry, two embedding passes, CPU one thread; UsedMemoryIds contained the source preference.
- The first chat total was 820 tokens, including 349 memory-learning tokens. The separate recall chat total was 554 tokens with no memory-learning request. Both thinking budgets were recorded as 256.
- Closed the isolated application after completed replies. Engine cleanup is independently checked by the verifiers and post-test process inventory.

The later final build disables ONNX telemetry before initialization; its numerical/reference tests were rerun. This has no layout change. Profile deletion was verified by isolated store tests rather than deleting through UI automation. Existing 1.1/1.2 UI evidence remains in the verification folder. Physical cross-window file drag remains unverified; picker/import and vision regressions pass separately.
