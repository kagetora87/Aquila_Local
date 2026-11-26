# Aquila Local 2.0.1

Open **Aquila Local.exe** and keep this entire folder together. The inference engine, .NET runtime, PDF reader, C++ libraries, ONNX runtime and MiniLM CPU embedding model are bundled. The optional Godot avatar and its .NET runtime are also bundled; no Godot installation is needed. Your existing **Data/** is preserved.

## Models and vision

The **Installed models** menu lists GGUF models in `models/`. Choose one and click **Load model**, or send a message to load it. **Unload** releases the main model. MiniLM, when used, remains in process until the app closes. No model loads at startup.

Included: the existing Heretic 9B and lightweight Qwen3 0.6B, plus **Deckard-4B-Q4_K_M.gguf** (2.71 GB) and **Deckard-4B-mmproj-Q8_0.gguf** (367 MB). Deckard is a mradermacher GGUF conversion of the linked DavidAU model. Sources, pinned revisions and verified hashes are in BUILD-MANIFEST.json.

Selecting Deckard automatically selects its matching projector. Vision was tested on the RTX 4060 with actual pixels. Projectors are model-specific: Deckard's projector cannot be used with the 9B model. A projector for 9B is not included. Use **Choose vision projector** for another compatible model, or **Clear** to disable it until a projector or model is selected again.

## Resident GPU panel

The left sidebar displays live GPU activity even with Settings hidden. Choose the GPU to inspect. Every ~2 seconds it shows overall utilization, local resident memory, and utilization/memory by application. Same-name processes are grouped; the owned engine is **Aquila model**.

The app uses Windows WDDM/PDH and DXGI directly, without an external monitor. GPU utilization uses the busiest engine. Percentages and memory rows are not additive; engine work and shared allocations can overlap. New processes may need another sample before rate counters become valid. Counter failures show unavailable rather than a false zero. Some older drivers do not expose these counters.

## Context and cumulative tokens

The context slider and existing dropdown stay synchronized, including custom values. The slider is logarithmic, rounded to 1,024-token steps. Context includes instructions, memory, chat history, attachments, tool results and the response allowance. Reducing context also reduces an incompatible response limit.

Choose **Inference profile** to use automatic hardware detection or override it. The blue **Profile target** marks the default for the selected profile and model. The **Full GPU estimate** assumes the whole model is on the GPU. Automatic GPU fit can place some layers in system RAM. The allowance stays in its normal text color, with memory advisories shown separately. See **INFERENCE.md** for quantized cache, MoE placement and creative samplers.

**Session tokens** are cumulative input + output across all requests in that saved conversation, including tool rounds, memory learning, repeated history, cached input and thinking tokens. This is not current context occupancy. Totals persist across restart and appear in exports. Missing usage from cancelled streams or older chats is labeled **partial counts**, never presented as exact.

## File injection

Use **Attach files** or drop files into the app. Removable chips stage content for the next message. No project folder is required, and original files are not changed.

Supported: text/source files, Markdown, JSON/CSV/XML and common configuration files; selectable-text PDF; DOCX body text; PNG/JPEG/BMP and Windows-supported WebP. Images require a matching projector and are sent as pixels. Embedded document images are not extracted. Scanned PDFs need page images attached to a vision-enabled model; OCR is not bundled.

Limits: 8 files per message, 20 MB per original file, 120,000 combined text characters, 200 PDF pages; input images up to 4 MB and 4096 pixels per side, with an additional decoded-image data limit. Unsupported or oversized content gives an explanation. Text is not silently truncated. Attachments may still exceed the chosen context allowance; use a smaller excerpt or increase context.

Content snapshots are saved in the conversation. Later changes to the source files do not alter those snapshots. Attachment-heavy history can grow the Data folder.

## Semantic memory search

**Settings → Match memories by meaning** enables the bundled MiniLM model. It matches paraphrases as well as keywords, and keeps personal/project boundaries and pin priority. The main chat model still decides what deserves saving and handles corrections. MiniLM supplies retrieval, not an importance or truth judgment.

MiniLM runs inside Aquila on CPU with one compute thread; it does not load onto the GPU. It loads only when an eligible memory needs searching. Vectors are computed on first use and cached in **Data/memory-vectors.bin**. Edits invalidate old vectors; forgetting removes the affected vectors. Cache damage is recoverable from the memory catalog. Missing/invalid model assets produce a keyword fallback reported in response Activity. Turning semantic matching off restores keyword retrieval.

The model is English-oriented and designed for short text. Semantic matches can still be wrong or miss a reference. Fewer irrelevant memories can reduce main-model input; indexing adds some CPU work, especially on the first search. No fixed speedup is promised. The pinned model and tokenizer are included, with no downloads at runtime. ONNX telemetry is disabled before initialization.

## Local voice and cloning

Open **Voice… → Import voice sample…** and choose a clean 5–30 second WAV of one speaker. Enter its transcript, preview and save the new voice, then enable **Speak completed replies automatically** and save the voice settings. Kitten and the other speech engines run locally. Under **Voice…**, use **Apply voice tags automatically** for per-passage delivery, and **Reveal tags** to show the cues in chat. Pace and delivery direction are available for Kitten voices. See **VOICE.md** for available presets, replay, compute settings and deletion.

## Color emoji

Enable **Settings → Generation settings → Use occasional emoji in replies** and save settings to encourage emoji in casual replies. The setting defaults off. Existing and new messages display supported emoji in full color using the Windows emoji font. Saved chats, copy and exports keep the Unicode characters.

## Thinking and personality

**Generation settings → Enable model thinking** asks a supported model to generate reasoning before its final answer. **Thinking budget** controls its allowance; the displayed effective budget is capped to leave part of the response limit available for the answer. Reasoning can end when that budget is reached. It shares response limits and cumulative token usage. More reasoning can take longer without improving every answer. Existing thinking settings are preserved during upgrade.

**Settings → Personality** selects a speaking style. **Manage personalities** lets you create, duplicate, edit, save and activate named profiles with style instructions and optional example exchanges. Included: **Default**, **Warm & direct**, **Analytical** and **Creative storyteller**. Default preserves your existing global instructions. Your global instructions and the current task still apply with other styles. Completed replies record the profile used.

Profiles are stored separately from factual memory in **Data/personalities.json**. Their examples and assistant output are not fed to the memory extractor. Creative storyteller starts with automatic learning paused to avoid saving story details; the profile editor can control this for each voice. Recall remains independently controlled by the main memory settings. Style consistency depends on the selected model and task; a profile does not fine-tune the weights.


## Memory, tools and data

**Automatic memory** now learns durable facts, preferences and project decisions after completed replies, then recalls relevant entries in later conversations. It uses the selected local model and adds a short inference step after the answer. Its token cost is included in the session total; hover over the total to see memory overhead.

Open **Memories** in the left sidebar to inspect supporting quotes, open the source chat, create/edit entries, pin important memories, approve conflicting corrections or forget a topic. Unpinned corrections replace the active fact; pinned conflicts wait for your review. Enable **Show history / forgotten** to inspect earlier versions or allow relearning.

**Personal** memories follow you across conversations. **Project** memories are recalled only with the same selected project folder. New facts in a project stay there unless you explicitly say a preference applies across projects. The manager can show other projects for inspection; that does not make their entries available to chat.

Settings has independent **Learn from new messages** and **Recall saved memories** switches. Both default on. The existing **Memory** text box remains manual notes included in every chat. Existing chats are not scanned or backfilled, and attachment/file contents and assistant output are not automatically learned.

Learning is selective and model-dependent. Retrieval combines semantic similarity and keywords within a small context budget. It can still miss indirect references. Review important facts in Memories. **Forget topic** clears saved values/quotes for that topic and blocks its automatic relearning by topic key; original chats, exports and backups remain. Different topic wording or current chat history can still contain the information. Use the manager to forget; asking in chat does not execute deletion. A manual save or **Allow relearning** explicitly restores eligibility.

Optional project-folder tools list, read and search text files. Activity appears below responses. They do not edit files or execute commands. MCP integration is not included.

Existing compute settings are preserved. Fresh settings use CPU-only, two low-priority threads. GPU layers **-1** enables automatic Vulkan0 offload (RTX 4060 on this computer); **0** means CPU; positive values limit GPU layers. The engine keeps a VRAM margin but does not coordinate other programs. A free-system-memory check can refuse a constrained load. Thinking is optional and uses part of the response budget.

Chats, attachment snapshots, automatic/manual memory, personality profiles, derived memory vectors, settings and bounded logs stay in **Data/** beside the executable. Back it up through **Open data folder**. The folder must be writable. Inference remains local; optional web requests contact external search providers and websites. No runtime model downloads or telemetry are used. The engine uses authenticated localhost and a Windows job object ties it to app lifetime.

## Avatar — phase 1

Open **Avatar…** in the left sidebar. Enable it and click **Apply / open**, or use **Restart**. **Stop avatar** disables it and releases its renderer. It defaults off; an enabled setting reopens it with Aquila next time. Chat remains available if it is closed, stopped or unavailable.

The separate local 3D window displays your selected VRM 1.0 avatar with blinking, subtle breathing, head motion and facial morphs. Adjust camera distance/height/field of view and 15/30/60 fps in the panel, then Apply. The default is 30 fps; minimized it drops to 5. It adds a small rendering workload; use 15 fps or Stop when other GPU work needs priority.

Real chat drives Thinking, Idle and Interrupted. All five states can be previewed. Speaking can follow actual audio playback, using its amplitude to animate the mouth. Listening remains a visual preview. Mira and the original rigid-body guide are selectable; authored body gestures remain future work.

The avatar runs independently through an authenticated, session/version checked loopback WebSocket. It can reconnect or restart independently. A Windows process-lifetime guard closes it if Aquila exits unexpectedly. No chat text or memories are sent to the renderer. Keep **avatar/** beside the main executable.

## Delete chats and compact context

Right-click a conversation and choose **Delete chat…**, or select it and press **Delete**. Both ask for confirmation, defaulting to No. Deletion removes its saved history and attachment snapshots. Learned memories stay separate; use Memories to forget them. Exports, backups and original attached files remain.

Use **Compact context…** near Settings after at least five complete turns. It summarizes earlier conversation with the selected local model while preserving the latest four complete turns verbatim and the full visible/saved history. Future requests use the summary instead of those earlier turns. **View summary… → Use original history** resets it under the ordinary context limits. Summaries can lose details; review important facts. Raw image pixels are not summarized.

Compaction respects your thinking and CPU/GPU settings and adds its usage to session totals. It does not reset cumulative tokens or increase the model's context window. Stop cancels it. A large chat may need several compactions; an oversized earliest turn produces an explanation. See **CONVERSATIONS.md** for scope, limits, failure handling and verification.

## Mira and local avatar folders

**Mira** is now the default avatar, with the original **Aquila Guide** also available. Open **Avatar…**, choose **Mira · bundled**, then enable and **Apply / open** or **Restart**.

Use **Choose folder…** to list VRM files in another folder, or **Choose file…** for a specific model. Select the character and Apply / open. **Refresh** discovers files added later. Unzip downloaded avatars first. Folder/model selections persist when applied; external files stay in their original locations. Missing or invalid files are explained before replacing a working avatar.

Models need self-contained **VRM 1.0** format (up to 128 MB). Each creator's licence and usage restrictions still apply. Mira's original ZIP, file, README and embedded licence metadata are retained. See **AVATAR-LIBRARY.md** for details and compatibility limits.

Mira has facial expressions, blinking and a neutral arms-down pose. The speaking state follows local audio playback. Authored body gestures remain future work.

## Internet access

Open **Internet…**. Choose **Off**, **Ask first**, or **Automatic**. Enter a **Brave Search API key** for general search, then Save. Existing profiles default Off. Create your provider key at https://api-dashboard.search.brave.com; the provider's plan/quota applies. The key is masked and encrypted for this Windows user. Direct public-page reading needs no key when web access is enabled.

Automatic allows the model to search/read while answering without an explicit search request. Short freshness questions and supplied URLs also receive initial lookups. Ask first shows the query/URL before sending it. Phrases such as “don't search” or “answer offline” disable web use for that turn. There is no background browsing or conversation initiation.

Activity shows lookups and failures. Sources consulted are saved with the turn and displayed as clickable links. Search snippets are distinguished from page evidence. Lookup judgment and factual synthesis remain model-dependent. Pages requiring login, JavaScript, downloads or private-network access are not supported in this slice.

Queries go to Brave and page requests go to websites; inference stays local. Use Ask first to review outgoing requests. See **INTERNET.md** for bounds, credential storage and limitations.

## Verification

**222 checks passed**: previous 190 plus 32 web checks. Public HTTPS reading and a real local-model URL answer were live-tested. Search-provider parsing/auth forwarding was tested using fixtures; a live general search needs your configured Brave key. Native Internet settings UI was inspected for modes, masked credential controls and readability using synthetic data. Test models/capture helpers were released afterward.

Evidence: **verification/v1.7-*/** and **verification/UI-CHECKS-1.7.md**. Details: **INTERNET.md**, **AVATAR-LIBRARY.md**, **CONVERSATIONS.md**. Source: **AquilaLocal-2.0.1-Source.zip**. Existing 9B inference, physical file dragging and arbitrary external-avatar compatibility remain outside these tests.
