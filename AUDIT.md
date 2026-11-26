# Aquila Local 2.0.1 — efficiency audit (2026-11-25)

**Basis:** There is no C# source on the machine, so I worked from the IL of the shipped assembly. It is mapped to the original
file:line through the PDB (`recovered/il_full.txt`). I added the engine's own `Data/logs/engine.log` (real timings from your
sessions) and your `Data/settings.json`. File references below are to the original Codex source files.

## Verdict

The code is not sloppy in the usual "AI slop" way. It is defensive and heavily validated. It has proper cancellation and
timeouts, an authenticated localhost engine, a job object that ties the engine to the app's lifetime, and good error messages.
Its **performance defaults and its request architecture are poor**. Codex tuned it for a cautious "don't disturb other work"
CPU setup on an RTX 4060 machine, never for 27-32B models on a 24 GB GPU. Most of the slowness you see comes from about 6
settings or design choices, not from deep rot. Ranked by impact:

| # | Problem | Evidence | Impact | Fix |
|---|---|---|---|---|
| 1 | **The GPU is never used.** `Profile = Custom` and `GpuLayers = 0` force CPU only | settings.json; `InferenceLaunch.Resolve` (InferenceProfiles.cs:141); engine.log never loads a Vulkan device | 27B runs at **1.75 tok/s**. On the RTX 5090 it should be roughly 30-50 tok/s generation and over 1000 tok/s prompt processing (an estimate, not measured) | Settings → Inference profile → **Auto/High**, or GPU layers **-1**. In code: default to Auto when a ≥ 8 GiB discrete GPU is detected |
| 2 | **Prompt cache is thrown away almost every turn.** Recalled memories, the web date and similar items are inserted into the *system* message (Agent.cs RunAsync). The system message is the first thing in the prompt, so whenever recall changes, the whole conversation is re-processed. In addition, the engine runs with `--parallel 1` (a single KV slot) | engine.log: many turns show `selected slot by LRU` and re-process the full 2-6k-token prompt. For example, the 9B model re-processed 2913 tokens (105 s) on turn 2 and 3949 tokens (153 s) later | On CPU this is minutes per turn. On GPU it is still the main source of latency on long chats | Keep the system prompt byte-stable. Put recalled memories, web results and the date into a message **after** the history, or append them to the current user message. Pass `cache_prompt: true` and pin the conversation to a slot (`id_slot`) |
| 3 | **Automatic memory learning runs a second model request after every reply.** It has its own system prompt and goes through the same single slot, which evicts the chat's KV cache | MemoryLearner.cs:18 → `v1/chat/completions` with `response_format: json_object`. engine.log shows a 200-300-token / 16-token request after replies, followed by an LRU miss | Doubles the work and destroys the cache from #2. The next turn is a full re-process | Run the engine with `--parallel 2` and send learning to its own `id_slot`. Or batch learning (every N turns or when idle), or turn **Learn from new messages** off |
| 4 | **Tiny batch sizes:** `--batch-size 256 --ubatch-size 128` are hard-coded | Engine.cs StartAsync args | Prompt processing on GPU is several times slower than with llama.cpp's defaults (2048/512) | Use the defaults, or 2048/512, when a GPU is in use |
| 5 | **Two CPU threads, and the engine at BelowNormal priority** | `CpuThreads: 2` (README: "Fresh settings use CPU-only, two low-priority threads"); `Process.PriorityClass = BelowNormal` (16384) for the engine, avatar and speech | On a 16-core / 32-thread 9955HX3D, CPU inference uses about 1/8 of the chip, and any other process takes priority over it | Default threads to the number of physical cores (16) and `--threads-batch` to the number of logical processors. Use Normal priority, or make it a setting |
| 6 | **Context trimming is O(n) HTTP calls and shifts the prefix one turn at a time** | `PromptWindow.FitAsync` (PromptWindow.cs:15) drops one oldest pair per iteration and re-tokenizes the whole prompt through `v1/chat/completions/input_tokens` each time | Long chats get a burst of tokenize calls before each send. Once the chat is over budget, every new turn moves the cut point, so the cache misses on every turn | Measure each message once and cache the token count per turn. Trim in large chunks (drop 25% at once) so the prefix stays stable for many turns |
| 7 | **The memory precheck uses system RAM even when offloading** | Engine.StartAsync: "Not enough free system memory to load this model comfortably…" | Could refuse a load that would fit in VRAM | Base the check on VRAM when GPU layers are used |
| 8 | **Thinking defaults are wasteful:** Thinking on, budget 512, response limit 1024 | settings.json | Up to half the response budget goes to reasoning on every chat message. There are "thinking without a final answer" failures | Turn thinking off for casual chat, or raise the response limit when it is on |
| 9 | **GPU detection is fragile** (see DIAGNOSIS.md) | InferenceProfiles.cs:52-94 | A spurious "No supported dedicated GPU found"; a fallback to `Vulkan0` (the AMD iGPU on this laptop) | Select by Vulkan device type or LUID, retry with a longer timeout, log the result, add a re-detect button |
| 10 | **Minor:** a GPU monitor polls WDDM/PDH counters every 2 s; about 20 `*Verification.cs` test harnesses ship inside the release exe; settings and chat JSON are rewritten in full on each save | GpuMonitor.cs, Verification*.cs, Storage | Small CPU and disk overhead; a bigger binary | Pause the monitor when its panel is hidden; move the tests to a separate project |

## Expected effect

- **#1 alone:** the 27B model moves from CPU to the RTX 5090. Generation should go from 1.75 tok/s to tens of tok/s, roughly 15-25× (an estimate; I did not measure it).
- **#2, #3 and #4:** follow-up turns in a long chat should start almost immediately instead of re-reading the whole conversation.

Note that 27B IQ4_XS is 17 GB. With 32K context and a q8_0 KV cache, it fits in the 24 GB card. At the 55K context you have
set it is tight, so stay around 32K or use the q4_0 cache.

## Workaround with no code changes
1. Settings → Inference profile: **Auto** (or **A · High VRAM**). Check that the panel names the RTX 5090.
2. CPU threads: 16, as a fallback for CPU-only models.
3. Context: 32768.
4. Consider turning off **Learn from new messages**. Keep **Recall saved memories** on only if you use it.
5. Turn Thinking off for casual chat.

Items #2-#4, #6 and #9 need source changes. The C# can be rebuilt from `recovered/AquilaLocal.dll` with ILSpy, or the
engine-facing layer can be rewritten. That layer is small: Engine.cs, Agent.cs, PromptWindow.cs and InferenceProfiles.cs total about 600 lines.

## Addendum: checked against the decompiled C# (2026-11-26)

`decompiled/` now holds the app decompiled with ILSpy 8.2 (95 files, about 17.9k lines). It is a readable reference, not yet a
buildable project: the XAML only exists as compiled BAML. Paths below are into `decompiled/AquilaLocal/`.

**Corrections to the table above**

- **#6:** `PromptWindow.FitAsync` uses a doubling then binary search, so it makes O(log n) tokenize calls, not one per pair.
  The part that matters still holds: it drops the *minimum* number of old turns, so once a chat is over budget the cut point
  moves every turn and the prompt cache misses every turn.
- **KV cache precision is already configurable.** Profile A forces `q8_0`, B uses `q8_0`/`q4_0`, and Custom offers
  f16/q8_0/q4_0 (`InferenceLaunch.Resolve`). Your settings already use `q8_0` at 55,296 context.

**Additional findings**

| # | Problem | Where | Impact |
|---|---|---|---|
| 10 | **Auto runs on CPU for most GPUs.** Auto picks A only for ≥ 20 GiB VRAM, and B only for ≤ 10 GiB VRAM *and* ≤ 20 GiB RAM. Everything else (12/16 GB cards, or 8 GB with 32 GB RAM) becomes Custom, whose default `GpuLayers = 0` is CPU-only | `InferenceProfile.Resolve`, `Settings.GpuLayers` | Most desktop GPUs never get used out of the box |
| 11 | **Profile A caps context at 65,536.** Custom allows 262,144 but with GPU layers manual | `InferenceProfile.Resolve`, `InferencePreferences.Validate` | Large-context users are pushed into Custom, where the GPU is off by default |
| 12 | **Hard-coded `Vulkan0` fallback** when detection hasn't produced a GPU | `InferenceLaunch.Resolve` | On this laptop Vulkan0 is the Radeon 610M iGPU |
| 13 | **Memory learning blocks the UI** after every eligible reply (up to 60 s) before you can send again. `Eligible` matches almost any message containing "I", "my" or "use" | `MainWindow.SendAsync`, `MemoryLearner.Eligible` | Adds a full extra request to most turns, on top of #3 |
| 14 | **tok/s readout is wrong.** It divides generated tokens by wall time that includes model loading and prompt processing | `Agent.RunAsync` (`turn.Metrics`) | Makes generation look slower than it is and hides where time goes |
| 15 | **Streaming render is O(n²).** `turn.Answer += token` per token, and every 100 ms the whole answer, reasoning and activity log are rebuilt into a new FlowDocument | `Agent.RunAsync`, `MainWindow.RenderLive`, `MessageView.Update` | UI stutter on long replies, worse at high tok/s on GPU |
| 16 | **Engine log opens, stats and closes the file for every line** | `Engine.Log` | Minor, but llama-server is chatty at load |
| 17 | **Detection is one-shot**, 6 s timeout, swallowed errors, exact-name match between DXGI and Vulkan | `HardwareInventory.DetectAsync`/`SelectGpu`, called once from `MainWindow` startup | Intermittent "No supported dedicated GPU found" with no log of why |
