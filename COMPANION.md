# Companion mode

Companion mode is an optional second way to use Aquila Local. Normal chats work exactly as before. A
**companion chat** talks to a persona: an independent, persistent identity with her own personality sheet,
drives, private memory and chat history. Personas never see each other's memory.

Open it from **Companions** in the left sidebar.

## Where things live

```
Data\personas\
  registry.json            which personas exist, where their files are, which one is active
  vicki\persona.json       Vicki's settings (human-editable, see below)
  vicki\memory.db          Vicki's private state: drives, drive log, memory ledger, every exchange
                           with its embedding, achievements, sessions
```

Each persona has her own `memory.db`, and every row inside it also carries her id. The registry refuses two
personas that share a file.

## persona.json (the editable part)

Everything you might want to tune is in `persona.json`: the personality sheet, the drift clause, and for each
drive its weight, starting value, decay per hour, resting level, idle rise per hour, satisfy step, floor,
ceiling, how much each event moves it, and the one-line guidance she reads at low, middle and high levels.

You can edit it three ways:

1. **In the app:** Companions → Settings → Save settings.
2. **By hand:** Companions → Open her folder, edit `persona.json` in any text editor.
3. **Mid-chat:** the `/tune` command (below).

The file is re-read at the start of every companion chat, after every `/tune`, and after every save.

## /tune (mid-session tuning)

Type these in a companion chat. They are handled by Aquila and never sent to the model.

| Command | What it does |
|---|---|
| `/tune` | Shows this help |
| `/tune show` | Current drive values and every setting |
| `/tune <drive> <value>` | Sets a drive's value right now, e.g. `/tune lust 6.5` (logged as an operator change) |
| `/tune <drive> <setting> <number>` | Changes a setting and saves it to persona.json, e.g. `/tune horizon weight 2.0` |
| `/tune <drive> event <event> <change>` | How much an event moves a drive, e.g. `/tune comfort event conflict -2` |
| `/tune drift unlock` / `lock` | Changes the drift status she reads |
| `/tune reload` | Re-reads persona.json after a hand edit |

Settings: `weight`, `initial`, `decay`, `rest`, `rise`, `step`, `floor`, `ceiling`.
Drives: `lust`, `curiosity`, `comfort`, `loneliness`, `horizon`.
Events: `interaction`, `session_turn`, `conflict`, `novelty`, `affection`.

Invalid values are refused and the file is left unchanged (for example, horizon's floor can never go below 3).

## How a turn works

1. **Time passes.** Each drive moves for the hours since her last turn: lust loses 2 % per hour, curiosity
   settles toward its resting level, loneliness rises while you're away, horizon creeps back up. Never below
   horizon's floor of 3.
2. **Your message.** It can trigger events: every message is an *interaction* (loneliness falls), every turn
   after the first in a chat is a *session turn* (comfort rises), hostile wording is *conflict* (comfort
   falls), something unlike anything in her memory is *novelty* (curiosity spikes), affectionate wording is
   *affection* (lust rises).
3. **Her context, in this order.** (a) Her pinned long-term memory, in the stable system prompt (capped at
   2,000 tokens). (b) Her drives, put in front of your message as a short block (under 300 tokens): each
   drive, its value and one line of guidance, strongest first. (c) Her appearance card, verbatim, as her own
   current self-description (under 500 tokens; a change that would make it longer is refused up front). Then her latest achievements and the current time. She reads
   it all as the state of her world. Everything comes from her own store only, and assembly takes a few
   milliseconds (a test holds it under 100 ms).
4. **Her reply.**
5. **Satisfaction loop.** Did the reply address the top drive? Keyword and sentiment heuristics decide. If
   yes, that drive drops one step. If not, it stays and weighs heavier next turn. Horizon is scored by
   milestones instead: anything she does unprompted, an unprompted choice, or a reply unlike anything she's
   said before is a partial reward and is added to her achievements.
6. **Memory.** The exchange is embedded and stored. The five most relevant past exchanges (no duplicates)
   are recalled on later turns.

Every drive change is logged with its time and reason (Companions → Recent drive changes).

## Long-term memory (append-only)

Her pinned long-term memory has three layers: facts, feelings and hooks (open threads). It starts from the
seed summary in persona.json when she is set up, and is pinned at the top of her context, capped at 2,000
tokens. Every 20 turns a summary pass (her own, on the engine's second slot) folds older exchanges into it.
The 10 most recent exchanges are never folded, so they stay word for word.

Memory is **append-only**. Nothing is edited or deleted, by her or by you; the database itself refuses it.
A correction is a new entry that replaces an old one, with a timestamp and a reason, and the old entry stays
in her history. In Companions → Long-term memory you can add entries, correct one, or withdraw one (a
withdrawal is also a recorded, reasoned entry). "Show replaced entries too" shows the full history.

## Appearance card

Her canonical appearance card has two layers, kept in her own store like her memory:

- **Permanent traits**: bone structure, eyes, hair, skin, body, and the **identity anchor** (the one detail
  every picture of her must show, such as a beauty mark). Locked: she can't change them. You can amend one
  deliberately (Companions → Appearance → Amend a permanent trait), always with a reason, and the original
  stays in her history. The identity anchor can be reworded but never removed.
- **Wardrobe**: outfit, colour, fabric, condition. Hers to change. In a chat she uses the `change_outfit`
  tool, giving only what changed and a reason; a "Changed outfit" chip appears under that reply. You can
  also log a change in Companions. The wardrobe is **append-only**: each change is a new diary entry with the
  time and the reason, so her wardrobe history reads like a diary.

The starting card is seed data in persona.json (`seed.appearance`: `traits` as name/text pairs,
`identityAnchor`, `wardrobe`), copied into her store when she is set up. A persona set up before appearance
cards existed is set up once from Companions → Appearance.

**Image captions.** `AppearanceCaption.For` makes the caption for any picture of her from the card only:
her traits, what she's wearing now, and the identity anchor as the last line. Companions → Appearance shows
it with a Copy button. The picture generator and the vision self-check (comparing a render to her card and
flagging, say, a missing beauty mark) attach later through the `IImageRenderer` and `IRenderSelfCheck`
interfaces. Wardrobe changes she makes herself are handed to the satisfaction loop (`TurnObservation.SelfChanges`),
ready for scoring later.

## Reactions

She can react to your message with one emoji, like a chat-app reaction (the `react` tool). It shows on the
lower edge of your message bubble as soon as she reacts, and it's saved with the chat. Her instructions tell
her to react when a message really lands (good news, a joke, affection, a picture, a confession, a surprise),
to skip routine messages, not to be shy when it fits, and to pick the emoji that fits the moment. A reaction
never replaces her reply.

## About you

Companions → About you is your own profile: what to call you, age, appearance, background and anything else.
Every companion reads it (it's yours, not part of any persona's memory), in her stable system prompt, so it
costs nothing extra per turn. Your picture is shown to her (with the vision model) on the first message of
each chat, and once more whenever you change it. Files: `Data\personas\about-you.json` and
`about-you-picture.json`.

## Her voice and listening

- **Her own voice.** Companions → her settings → "Her voice" picks the voice she always speaks with (a
  Kitten or Qwen preset, a Windows voice, or a voice you cloned in Voice… → Import voice sample). It is
  saved in her persona.json as `voice`; empty means "same as Aquila's voice".
- **Save as audio.** Every finished reply has a speaker button next to Copy that saves it as a .wav in her
  voice, with the same moods and pacing as when she speaks it. This is how someone outside Aquila can hear her.
- **Talking to her.** The microphone button beside the message box listens through whisper.cpp running on
  this computer (nothing is sent anywhere). Live words appear while you talk; when you pause (1 second by
  default) what you said is sent. Right-click the microphone for the microphone, pause length, language
  and "talking interrupts her".
- **Not cutting you off.** After you pause (1.5 seconds by default), a sentence that sounds finished is sent
  after a short moment, while one that ends in "and", "so", a comma or "..." waits longer. If you go on, the
  parts are joined with the pause noted. Turn off "Send automatically when I pause" (right-click the
  microphone) to have what you said wait in the message box until you press Enter.
- **No phantom words.** Whisper makes up stock phrases ("Thank you.", "Oh.") from noise. Aquila drops them
  using Whisper's own no-speech and confidence scores plus how faint or brief the sound was, ignores the
  first half second while it learns the room's noise, and ignores the room's echo for a moment after she
  stops speaking.
- **How it was said.** Whisper writes tidy text and drops hesitations, so Aquila measures them from the
  audio itself and adds bracketed notes she can read: `[paused 1.8s]`, `[restarted]`, `[trailed off]`,
  `[spoke quietly]`/`[spoke loudly]` (against your usual level, learned as you talk), and
  `[spoke quickly]`/`[spoke slowly]`.
- **Remembered as heard.** Each spoken exchange is stored with a spoken flag, so later recall says "you
  heard them say aloud" instead of "they wrote".
- **Fail loud.** No microphone, a microphone Windows blocks, a microphone that sends pure silence, or a
  missing speech recognizer turns listening off with a message saying why. Aquila never quietly switches
  back to typing.

## "Aquila" voice commands

Right-click the microphone → **Listen for "Aquila" while the app is running**. In standby the microphone
checks short bursts of speech only for the wake word; everything else is thrown away at once (never shown,
sent or kept). After the wake word (a chime), say a command, or say it all at once ("Aquila, call Vicki"):

- **"Let's talk"** (or "let's chat", "talk to me", "can we talk"…): full listening in the current chat.
  Back to standby after "Aquila, stop listening" or 2 quiet minutes.
- **"Call Vicki"**: opens her ongoing "Call with Vicki" chat; she picks up and every reply is spoken in her
  own voice even when voice is otherwise off. "Aquila, hang up" or clicking the microphone ends the call.
- **"Hang up"**, **"Stop listening"**.

The wake word, the sound-alikes the recognizer sometimes writes, and every phrase are editable in
**Voice commands…** (right-click the microphone), saved to `Data\voice-commands.json`. Wordings not in the
list are matched by meaning (local embeddings), and persona names are matched by sound ("Vicky" is Vicki).

## Scenes (several personas together)

Sidebar → **Scenes**. A scene is three layers working as one:

1. **The world:** a shared block (where, when, what's happening, things there, notes) that every speaker
   is given first. Edit it at any time with "Update the world"; the change is logged so everyone notices.
2. **The cast:** up to 12 personas, each a full persona with her own sheet, drives, private memory and
   appearance card. Add or drop people from the cast bar at any time. Arrivals and departures are scene
   events everyone present hears. A persona only ever sees what happened while she was present, so a
   newcomer doesn't know what was said before she arrived.
3. **Who speaks next:** "Who answers" calls on someone, or leaves it on Automatic. Automatic blends being
   named, how relevant the last line is to each persona (local embeddings, no extra model call), and how
   strong her drives are, with a nudge against the same voices dominating. Someone named always answers.
   After your message up to 2 personas speak in a row before the scene waits for you; "Let them talk"
   keeps it going without you. "Nobody (just say it)" adds your line without asking anyone to answer.

Everything said and done goes into the scene's append-only log (`Data\scenes\<id>\log.db`, with the
manifest in `scene.json`). A speaker's reply also goes into her own private memory, and nowhere else. The
prompt puts the shared parts (world, cast, log) first and her private state last, so the model can reuse
the shared part between speakers.

To add personas: Companions → **Add a persona from a file…** (same format as Vicki's persona.json), then
set her up once.

## Thoughts and actions

- Under the message box, **Thought** and **Action** start a thought or an action at the cursor; press again to end it (Ctrl+T / Ctrl+J). Thoughts show in blue italics and actions in orange, as you type and in the chat. A message can mix several.
- In the text they are `<thought>...</thought>` and `<action>...</action>` (`ThoughtMarkup`).
- Your actions are seen by the persona. Your thoughts are private: the persona is told she can't know them, but may let them quietly shape the story when it fits. They are never saved into her memory (`CompanionRuntime.Complete` stores the message without them).
- Personas use both. Her thoughts never reach another persona in a scene (`SceneTranscript.Line`), and her voice reads only speech (`ThoughtMarkup.MaskForSpeech` in `VoiceDelivery.Plan`).
- The eye button shows or hides her thoughts. The palette button sets first person ("I smile") or third person ("Vicki smiles") for her thoughts and actions (applies to new replies) and the two colours. Saved in settings (`ShowPersonaThoughts`, `ThoughtPerson`, `ThoughtColor`, `ActionColor`).

## Soundboard

- **Soundboard** in the sidebar: add sound files (wav, mp3, wma, m4a, aac), give each a name, a type and a short description, put them in order. Saved in `Data\soundboard\` as `01 - Name.ext` files plus `soundboard.json`; removed sounds go to `removed\`, not the bin.
- When the soundboard is on and has sounds, each persona's system prompt lists them (`SoundCues.Prompt`). She cues one with `<sound>name</sound>`; the app matches the cue by name, close spelling, shared words in name/type/description, then meaning (`SoundCues.Match`), and plays it after her message, or after her voice when she's speaking. At most three per reply.
- The cue shows in the chat as an orange action ("🔊 Door knock"), is never read aloud, and is left out of her memory. Works in scenes too.
- Switch and volume are in the Soundboard window (`SoundboardEnabled`, `SoundboardVolume` in settings).

## Where you are (location)

- Persona chats show a location chip in the header ("Where are we?" until set). Click it to change or clear it (`ChatSession.Location`).
- She sees it every turn in her inner state (`[Where you are: …]`) and can move the story with the `change_location` tool; a "Moved to: …" chip appears under that reply and the header updates.
- A new chat (or call) with her starts where the last one left off. Pictures use the location as the setting unless the request says otherwise.

## Pictures (built-in image generator)

- **Pictures** in the sidebar: switch it on (off by default), choose the model, add each persona's reference pictures, and draw a test picture.
- Engine: stable-diffusion.cpp `sd-cli.exe` in `imagegen\sd\` next to the app; model files in `imagegen\models\` (`PictureKit` finds them and names anything missing).
  - **Fast:** FLUX 3 Image Klein Q8_0 + Qwen3-VL 4B text encoder + flux2 VAE. About 14 s per picture (25 s with references), about 11 GB, so it runs beside the chat model.
  - **Best:** Qwen-Image-2.1 Q8_0 + Qwen3-VL 8B + mmproj + its VAE. About 45 s per picture, 18 to 21 GB. The chat model is paused: its working memory is saved with llama-server's slot save (`--slot-save-path` in `Data\engine-cache`) and restored afterwards (`Engine.ParkAsync`/`ResumeAsync`).
- Vicki and Aquila get a `make_picture` tool (description, includes_you, framing) when pictures are on. The chat model writes the image prompt on slot 1 (`PicturePrompt`), from the request, the recent chat (without your thoughts or sound cues) and, when she's in it, her appearance card.
- Reference pictures live in her persona folder under `references\`; the first four by name are used. The prompt tells the model to take clothes, pose and setting from the description, not from the references.
- Pictures are saved in `Data\pictures\yyyy-MM\` with a `.txt` beside each (prompt, model, seed, references) and shown under her reply; click one to open it full size.

## Video (Wan 2.2 image-to-video)

Off until allowed in the Pictures window (needs the files in `imagegen\models`: Wan2.2-I2V-A14B High/LowNoise
Q8_0 GGUF, umt5-xxl-encoder Q8_0, wan_2.1_vae, and optionally `models\loras\wan22_i2v_lightning_{high,low}`).
The `make_video(description, motion, includes_you)` tool draws a 1536×864 still like a picture (appearance and
reference pictures), scales it to 960×544 and animates 81 frames (5 s, 16 fps, no sound) into a .webm saved beside
the still. The chat model is parked during the clip (peak ~23 GB). Measured on the laptop: full quality (8+10
steps, cfg 3.5, shift 3) 14.5 min at 832×480; Lightning (2+2 steps, cfg 1, shift 5) 202 s at 832×480 and 255 s at
960×544, same look. `--offload-to-cpu` fails for video ("RAM graph capacity exceeded"); `--mmap` works. LoRA tags:
`<lora:NAME:1>` (low-noise) and `<lora:|high_noise|NAME:1>` with `--lora-model-dir`. Living mode can also animate
dream pictures ("Also bring her dreams to life").

## Living mode (thoughts of her own)

Off until you turn it on (sidebar: **Living mode**). While Aquila is open and you've left it alone for a while
(10 minutes by default), each living persona has a thought after a random wait of 15 to 60 minutes:
a daydream, a feeling, a reflection, something she's curious about, or a memory she's turning over. It comes
from her personality sheet, her drives right now (ticked forward to the present, read only), her pinned
memory, the last things you said to each other and her recent thoughts (so she doesn't repeat herself).

- **Night:** from 11 PM to 8 AM she's asleep: thoughts become dreams (waits doubled, no lookups). If "Paint her
  dreams" is on, the picture model chosen in Pictures paints each dream (with her appearance and references when
  she's in it); the picture is in her journal and Data\pictures. "Have a dream now" tries it any time.
- **Where it runs:** the chat model's spare slot (slot 1), the same one the summary and picture-prompt writers
  use. No second model, nothing added to your chat's context. Sending a message stops a thought in progress.
- **Her memory:** each thought is remembered under the chat id `inner-life`. Recall shows it to her as
  "you thought to yourself (you never said this to them)", and her summary pass knows it was private.
- **Lookups:** if allowed, she may look something up: a Brave web search and the top page when a key is saved,
  otherwise Wikipedia. The query never contains her name. Every search and page is listed in her journal. This
  switch is separate from the chat's Internet setting.
- **Messaging you first:** when a thought's urge is above 0.75 she writes to you in her full chat voice (her
  persona prompt, inner state and your recent chat), and the message lands in your latest chat with her with a
  sound. She doesn't write again until you've answered, leaves 2 hours between messages, keeps to the daily
  limit (3 by default) and stays quiet between 11 PM and 8 AM. The turn replays to the chat model as "no
  message from them: you messaged first", so the conversation knows why she wrote.
- **Journal:** `Data\living\<persona>\journal.jsonl`, one line per thought, appended. A damaged line is reported
  by number, never skipped. The Living mode window shows it newest first and has "Have a thought now".

## Start from a saved chat (priming)

You can teach a companion from a conversation you had with an AI somewhere else, or from any document that
describes them (a character sheet, notes, a story).

- **While creating one:** Create a companion → step 5 → **Start from a saved chat…**
- **For one who already exists:** Companions → **Teach from a saved chat…**

Paste the text, or add files: text (.txt, .md), Word (.docx), PDF, saved web pages (.html), or chat exports
(ChatGPT or Claude.ai `conversations.json`, SillyTavern `.jsonl`, Aquila chats). If it's a conversation, Aquila
finds who is speaking ("Ru: …", "Sophia: …", or ChatGPT's copied "You said:" lines) and asks which one is you
and which one becomes the companion.

Your own chat model reads it on this computer, in parts if it's long (each part's notes first, then everything
together, so any length fits the context window). It drafts:

1. **The personality sheet**, in her voice, keeping what her current sheet or your notes already say unless the
   conversation shows otherwise.
2. **Her drives**: where each one should start and how much it should matter, each with a reason. It can also
   suggest up to 3 new drives for strong motives none of hers covers, and up to 3 of hers to take away because
   they don't fit (never all of them). New drives and removals are off unless you tick them.
3. **Memories** (up to 40): names, shared moments, inside jokes, promises, plans. You press **Keep** or **Skip**
   on each one and can change its words. A counter shows how much of her 2,000-token pinned memory they'd use.
4. **The conversation word for word** (optional): stored in her recall memory as "your past together, before
   this app", already folded, so moments come back when something reminds her, without being re-summarised or
   shown as the last things said.

Nothing is used until you press **Use this** (creating) or **Save to her** (existing, with a confirmation).
For an existing companion, kept memories are new entries (author `operator`, reason "primed from <file>,
approved by you"); nothing is replaced, and drive changes are logged like any other. Code:
`companion/Aquila.Companion/Priming/` (SourceText, Primer, PrimeApplier) and `src/AquilaLocal/PrimeWindow.cs`.

## Fail loud

If her state can't be read (file missing, damaged, belongs to another persona, a drive missing or impossible,
her appearance card missing, unreadable or over its cap),
the message is not sent and Aquila says exactly what's wrong. It never falls back to default drives, and it
never creates a new store on its own: setting a persona up is always an explicit button press. If her drives
can't be updated after a reply, the reply is kept and a warning says her drives were not updated.

## Personas and the public repository

This repository is public, so persona content (Vicki's sheet, numbers and summary) is not stored in it. The
built-in Vicki config has `[[PLACEHOLDER]]` markers and refuses to start until they're filled, either in the
app or by loading a persona file (Companions → Load persona file…).

## Tests

`companion/Aquila.Companion.Tests` (run on every build): the satisfaction-loop tests were written before the
loop, and watch real drive values move in a real store. Fail-loud, isolation, append-only, tuning, injector
size and summarizer tests are alongside them.
