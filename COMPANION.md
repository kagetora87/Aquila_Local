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
   current self-description (under 400 tokens). Then her latest achievements and the current time. She reads
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
