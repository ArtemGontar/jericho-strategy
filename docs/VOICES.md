# Voices in JERICHO

A plan for giving the game a voice: what is spoken, who speaks, how the lines are written, and how it is built.
Nothing here is implemented yet. It is meant to be read, argued with and edited before any code is written.

## 1. The idea in one paragraph

JERICHO is a relaxed builder. Most of it is quiet, and it should stay that way. A voice speaks only at the moments
the game already treats as *scenes*: a capital is founded, a wonder is finished, an age begins, someone declares war,
a game ends. One calm **narrator** carries the story; the **eight leaders** speak a few lines each when they meet,
threaten or trade with you. A player who never wants to hear a voice turns it off in one tap and loses no information,
because every spoken line is also on screen.

## 2. Principles

1. **Rare and meaningful.** A voice is a reward and a signal, never a stream. A normal turn is silent.
2. **Nothing is only spoken.** Every line has the same words on screen (the title cards already exist). Sound off, deaf players and a noisy train all lose nothing.
3. **Short.** At most about 12 words, 1–4 seconds. The camera moment it sits on lasts 3–4 seconds.
4. **Never in the way.** A voice never blocks a tap and never delays the game. If a line is late, it is dropped, not queued behind the next scene.
5. **Light to carry.** The whole voice set stays under about 2 MB, loaded in pieces, so the phone-first, offline-capable game stays light.
6. **Same for everyone.** Voices are presentation only. They are triggered on each player's own device from what that player's screen shows. They never touch the engine, the saves or the network messages.
7. **Easy to turn off, easy to change.** Its own setting, its own volume, and a catalog of lines that can be edited without touching code.

## 3. What gets a voice

Three tiers. Tier 1 is the first release. Tiers 2 and 3 are only added if tier 1 feels good.

### Tier 1: the narrator, key scenes only (about 35 lines)

| Scene | Where it already exists in the code | Example line (placeholder) |
|---|---|---|
| New game begins | game start, first view | "Here, the first fire is lit." |
| Capital founded | `moments.ts` found clip | "A city is born. Its name will be remembered." |
| An age begins (4 times) | `showEraBanner` / `moments.era` | "The Classical age begins." |
| Wonder completed | `moments.ts` wonder clip (name in the line) | "{Wonder} stands in {City}." |
| War declared on you | `moments.ts` war clip | "{Leader} has chosen war." |
| City lost / captured | `moments.ts` capture clip | "{City} has fallen." / "{City} is ours." |
| Victory (4 kinds) and defeat | `moments.finale` | "Your name will be remembered." |
| Culture or domination countdown begins (yours or a rival's) | `tickDomination`, culture hegemony notes | "{Civ} is close to ruling the world." |

### Tier 2: events and fortune (about 20 lines)

Spoken by the narrator, only for the bigger events (plague, drought, a find of coal or oil, a two-option question
arriving, "your people are restless"). Smaller events and routine news stay silent, as they are today.

### Tier 3: leaders (about 6 lines × 8 civilisations = 48 lines)

Each of the eight leaders (The Spring Warden, Queen Puabi, Hatshepsut, The Priest-King, Augustus, Cyrus, Pakal the Great,
Pericles) has: a greeting on first contact, a war declaration, a peace accepted, a trade signed, a sanction received,
a victory taunt or farewell. This is where the voice adds personality, so it is also where most of the writing and
most of the risk is. It comes last.

### What never gets a voice

Routine news (built, grew, adopted), tooltips and menus, the coach (it points; it does not talk), and every turn
that has nothing special in it.

## 4. Voice direction

* **Narrator:** warm, unhurried, a little wry; a storyteller by a fire, not a trailer announcer. One voice for the whole game.
* **Leaders:** distinct but not caricatures. Short, dignified, one flavour each (for example Pericles speaks in measured
  public-speech cadence; Cyrus is calm and generous; Augustus is clipped and formal). They are written in English and
  voiced by clearly *fictional* voices. We do not imitate any real person, living or historical recordings.
* **Tone by situation:** good news is bright; bad news is low and slow; war is clipped. The same line may exist in
  two takes if the situation changes its colour.
* **Writing rules:** present tense, concrete nouns, no jargon ("contentment" is "your people"), no numbers except
  names, no exclamation marks unless the whole line is a cheer. Each cue has 2–3 variants so a repeat is not word-for-word.

## 5. When a line plays (rules)

* **Priority.** Finale > war and city loss > wonder and era > fortune > everything else. A higher priority line cuts a lower one; a lower one that arrives while a higher one plays is dropped.
* **Cool-down.** At most one narrator line every ~20 seconds, and the same cue at most once every 10 turns (eras and endings: once per game).
* **Once-per-game flags** for first-time lines (first contact, first war, first wonder) so they stay special.
* **Busy screen.** If the player is in a dialog (tech tree, city panel), a line that can wait waits up to 3 seconds and is then dropped. Critical ones (war, finale) always play.
* **Hot-seat.** The line plays after the "pass the device" curtain, and only about the player now looking at the screen.
* **Online.** Each client speaks its own view. A rival's private business is never spoken. A war declared on someone else is one short narrator line, not a speech.
* **Offline return.** After coming back to a game or a long computer turn, lines older than the current turn are skipped.

## 6. How it is built

### 6.1 The decision: pre-made clips, played from the page

Three ways to get a voice into a browser game:

| Option | What it is | Verdict |
|---|---|---|
| **A. Pre-recorded clips** (recorded by actors, or generated once with a text-to-speech service and saved as audio files) | Files in `public/voices/`, played with Web Audio | **Chosen.** Sounds the same everywhere, works offline, costs nothing per player, and we control quality. |
| B. The browser's own speech engine (`speechSynthesis`) | The device reads the text aloud | Only as a **fallback** when a clip is missing. Voices differ per device and are often robotic; it cannot be art-directed. |
| C. Text-to-speech at play time through a service | A server turns text into audio on request | **No.** It adds cost, latency, a network dependency and an outside service that sees player text, and it breaks offline play. |

Since the lines are a fixed, small set (names are the only variables), option A is the natural fit.
Names (cities, wonders, leaders) are handled in section 6.4.

### 6.2 Pieces

```
docs/VOICES.md                     this plan
src/client/voice/lines.ts          the catalog: cue id → { speaker, tags, variants[{ id, text }] }
src/client/voice/voice.ts          the player: queue, priority, cool-downs, ducking, captions, fallback
src/client/voice/cues.ts           maps game happenings to cues (the one place that knows the game)
public/voices/<speaker>/<id>.m4a   the clips, one per variant (generated)
public/voices/manifest.json        id → file, duration, hash (generated)
scripts/voices.ts                  build step: reads lines.ts, asks the chosen speech service for missing clips, writes files and the manifest
tests/voice.test.ts                catalog checks (below)
```

* **Catalog (`lines.ts`)** is plain data, so a writer can edit it. Each variant has a stable `id` (never reused).
* **Player (`voice.ts`)** is small and independent of the game: `speak(cue, vars)`, `stop()`, `setLevel()`, `isSpeaking()`.
* **Cues (`cues.ts`)** are fed from the places that already detect scenes: `Moments.onView` (found, capture, wonder, war, finale, era) and the note handling in `gamescreen.ts` (events). Today `moments.ts` finds some scenes by reading note text with regular expressions. That is fragile for speech, so notes that deserve a voice should carry a structured `cue` field (the same way events now carry `luck`), and the cue layer reads that instead of the sentence.

### 6.3 Playback

* Load with `fetch` and decode with `AudioContext.decodeAudioData`, not an `<audio>` element. This keeps clips in the
  service worker cache as ordinary full responses (audio elements use range requests, which the cache cannot store),
  and gives exact control over volume and ducking.
* A **voice bus** (its own gain node) sits beside music and sound effects in `audio.ts`. While a line plays, music and
  effects duck by about 6 dB and return after 300 ms.
* Clips are loaded **lazily and in groups**: the narrator's core group at game start, a civilisation's lines when its
  leader is in the game, era and wonder lines when the age is near. Anything not yet loaded when needed is skipped
  (or falls back to caption only), never waited for.
* **Format:** mono AAC in `.m4a` at about 48 kbps is the one format every current browser decodes. Opus is smaller but
  Safari support is uneven; decide after a device check. Estimated total for ~110 clips averaging 3 seconds:
  about 1.5–2 MB, loaded in pieces.
* **Offline:** voice files join the existing service worker cache the first time they are fetched (a separate named
  cache so a new build does not throw them away). If a clip is not cached while offline, the caption still shows.
* **Phones:** audio must be started by a tap, which the game already satisfies (the first tap resumes the audio
  context). iPhones also silence Web Audio with the ring/silent switch; this needs a check on a real device, and the
  setting screen should say so if it turns out to matter.

### 6.4 Names and variables

Names make fully pre-recorded lines awkward ("{Wonder} stands in {City}"). Options, from simplest:

1. **Write lines that do not need names** and let the on-screen card carry the name: "A wonder is complete." The title card shows *Great Library · Wonder completed in Uruk*. Recommended for the first release.
2. **Record the name parts separately** (about 12 wonders, 8 civilisations, 80 city names) and join them: "[The Great Library] [stands in] [Uruk]". Needs consistent pacing and is a lot of clips.
3. **Generate with names in the line at build time** for the known set only (every wonder × narrator template). Large but bounded; names that are not on the list fall back to option 1.

Start with option 1 everywhere, and use option 3 only for wonders if the lines feel too generic.

### 6.5 Settings and accessibility

* New setting **Voices: Off / Low / Medium / High**, next to Music and Sound effects (same `Level` type and storage style, key `jericho.voice`). **Default: Medium**, but nothing plays until the player has tapped once, as with all browser audio.
* The mute button on the map silences voices too.
* **Captions:** while a line plays, its text is shown. Scenes with a title card already show the card, so the caption is only added for lines without one (a thin, short-lived band above the unit bar, never over the city drawer). A spoken line without a visible equivalent is a bug.
* The game has no screen-reader announcements today. Captions would be the first: a polite live region carrying the same text, so a screen-reader user hears the scene once. A screen reader and a voice talking over each other is a risk, so this needs a decision when it is built (suggested: the voice stays quiet for people who use a screen reader, who can switch it on).
* Reduced motion / Cinematics **Off** does not turn voices off, since they are a separate preference; but Cinematics **Short** shortens nothing in voices.

### 6.6 Content pipeline and who makes the audio

* `scripts/voices.ts` takes the catalog and a speaker table (speaker → voice id and style settings in the chosen service), and produces only the clips whose *text + voice settings hash* changed. Clips are committed, so a normal build never calls the service and CI needs no keys.
* The speech service is a **setting of the script, not of the game**, so it can be swapped, or replaced by a recorded
  set from human actors, with no game code change: the manifest is the contract.
* The text-to-speech route is cheap and quick to iterate; actors give a better result and are worth it for the
  narrator and the leaders once the lines are settled. A reasonable path is generated voices first, then
  re-record the narrator.

### 6.7 Tests and checks

* **Catalog:** every cue used in `cues.ts` exists; every variant has non-empty text, a speaker and a duration under 6 seconds; ids are unique; no line over 14 words.
* **Manifest:** every variant has a file with a matching hash; no orphan files.
* **Player:** priority, cool-down, drop-if-late and the volume setting, tested with a fake audio context.
* **Browser:** the end-to-end run plays a scene with a stubbed decoder and asserts that the caption appears and that muted or `Off` plays nothing.
* **Budget:** a script prints total size and fails the build over 2.5 MB.

## 7. Rights, safety and honesty

* Use a service whose terms allow shipping its generated audio in a game, and keep the terms with the repository.
* No voice cloning of any real person, living or dead, and no imitation of a recognisable actor. Leader voices are invented.
* If any voice is synthetic, say so in the credits / About, as the platform and the service may require.
* Lines are written by us and reviewed for tone and for historical sensitivity; the leaders are portrayed with respect.
* No player text is ever sent anywhere: the voice set is fixed, and names a player types are never spoken.

## 8. Phases

| Phase | What | Result |
|---|---|---|
| 0 | Prototype the player with the browser's own speech engine reading the existing title-card text for 6 scenes. | A feel for pacing and ducking, with no audio files. |
| 1 | Tier 1 narrator: catalog, player, cues, setting, captions, ~35 generated clips. | The first release of voices. |
| 2 | Tier 2 events and fortune; structured `cue` on notes; tests and budget check. | Fortune and ill fortune are heard. |
| 3 | Tier 3 leaders for 2–3 civilisations first, then all eight. | Personality in diplomacy. |
| 4 | Re-record the narrator (and leaders) with human voice actors if the project wants it. | Final quality. |
| later | More languages: the catalog already keys lines by id, so a language is a second set of lines and clips. | Not planned yet. |

## 9. Open questions for the owner

1. **Narrator only, or leaders too?** Tier 3 is where the personality is, and also most of the cost and risk.
2. **Generated or human voices?** I suggest generated first (fast to change), human later for the narrator.
3. **Which feeling for the narrator?** A warm storyteller (suggested), a solemn historian, or something lighter.
4. **Is "no names in the lines" acceptable at first**, with names on the card only?
5. **Default on or off?** Suggested: on at Medium, with a clear one-tap off. (The game is often played in public on a phone, so off by default is the safer choice if you prefer.)
6. **Language:** English only for now?
7. **Budget:** is a paid speech service acceptable for the one-off generation, and who owns the account? Cost for this volume is small, but it needs an owner.
