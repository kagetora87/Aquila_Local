# Aquila Local 2.1 (rebuilt from the 2.0.1 release)

The original 2.0.1 source was lost, so `src/` was recovered by decompiling the shipped app (ILSpy 9.1 for C#,
ICSharpCode.BamlDecompiler for the window layouts). GitHub Actions (`.github/workflows/build.yml`) builds the
Windows exe on every push.

## What changed

- **Automatic setup is the default.** "Automatic (recommended)" detects the graphics card, puts the whole model on it
  when it fits, uses an 8-bit context cache and sizes the conversation length to the free video memory, keeping a
  margin for Windows. Old settings migrate to it once (a backup is kept as `Data/settings.json.before-2.1`).
- **The chat cache survives between turns.** Recalled memories, the date and web lookups are attached to the user
  message (and replayed verbatim later) instead of being injected into the system prompt, and long chats are trimmed
  in large steps. Follow-up messages only read the new text instead of the whole conversation.
- **Memory learning runs in the background** on a second engine slot (`--parallel 2 --kv-unified`), so it neither
  blocks the chat nor evicts its cache.
- **Faster prompt reading:** batch 2048 / micro-batch 512 on GPU (was 256/128). Automatic CPU thread count (was 2),
  engine at normal priority (was below normal).
- **GPU detection:** 20 s timeout with retries, tolerant name matching, no more hard-coded `Vulkan0` fallback (the
  integrated GPU on many laptops), re-detection right before loading, and a `Data/logs/hardware.log`.
- **Honest speed readout:** writing speed and prompt-reading time come from the engine's own timings.
- Pre-load memory check only requires the whole model in RAM when it actually runs on the CPU.
