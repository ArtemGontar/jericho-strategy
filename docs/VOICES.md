# Voices in JERICHO: design and technical specification

Status: **plan, nothing built.** This document is meant to be complete enough to write the lines, record or
generate the audio, and build the feature from it. Section 14 lists the decisions still needed from the owner, each
with the default I would use so work can start without waiting.

Contents

1. Goal and non-goals
2. Principles
3. What a game sounds like (the budget)
4. The cast
5. When a voice plays (rules)
6. The line catalog (every cue and its script)
7. Where each cue comes from in the game
8. The player (client architecture)
9. Files, formats, loading and offline
10. Production pipeline
11. Settings, captions and accessibility
12. Edge cases
13. Tests, acceptance and risks
14. Phases, work breakdown, decisions needed
15. Appendices (pronunciations, direction notes, sample code)

---

## 1. Goal and non-goals

**Goal.** Give the game a spoken voice for its *critical scenes*, so that a founding, a wonder, a new age, a war, a loss
and an ending are felt as well as read. The voice must make the game warmer and more memorable without making it
louder, longer, heavier to download, or harder to play.

**In scope.** A narrator; the eight civilisation leaders as AI opponents; captions for every spoken line; a Voices
setting; a build step that produces the audio; tests.

**Not in scope.**

* Voiced menus, tooltips, tutorials or the coach (the coach points; it does not talk).
* Voice commands or speech input.
* Reading the whole game aloud as a screen reader would. (Accessibility is handled separately, see 11.)
* Text-to-speech at play time, voice cloning of anyone, or any player-typed text being spoken.
* Changing the engine's rules, the save format or the network protocol in a way that older versions would break.

## 2. Principles

1. **Rare and meaningful.** A normal turn is silent. Target: **about 20 to 40 spoken lines in a 160-turn game**, never more than one narrator line in any 20 seconds.
2. **Nothing is only spoken.** Every spoken line is also shown as a caption, and the information it carries is already in the game's text (title cards, notes, the log). Turning voices off loses nothing.
3. **Short.** At most 14 words and about 4 seconds. Most are 6 to 10 words.
4. **Never in the way.** A voice never blocks a tap, never delays a turn, never holds a screen open. A line that cannot play on time is dropped, not queued behind the next scene.
5. **Light.** The whole set stays under **3 MB** on disk, a typical game downloads about 2 MB of it in pieces, and the first minute needs only about 0.5 MB.
6. **Presentation only.** Voices are triggered on each player's own device from what that player's view shows. The engine, saves, server and other players are unaffected, so determinism and online play cannot be broken by this feature.
7. **Editable.** Lines live in one catalog file that a writer can change without touching code. The audio is generated from it by a script.
8. **Honest.** Synthetic voices are labelled as such. No real person is imitated.

## 3. What a game sounds like (the budget)

Estimates are drawn from the 24-game simulation set (about 12 events per civilisation per game, about 1.5 wars,
about 2 city captures, 4 era changes, one to three wonders per player).

| When | Typical voiced lines |
|---|---|
| Turn 1 to 20 | Game start, capital founded, first contact, first leader greeting: **3 to 5** |
| Turn 20 to 80 | First wonder, a trade agreement, a bad event or two, possibly a war: **5 to 10** |
| Turn 80 to 130 | Two era changes, wonders, a rival's countdown, captures or losses: **6 to 12** |
| Turn 130 to the end | Industrial age, the race for the Enlightenment, the ending: **5 to 10** |
| **Whole game** | **about 20 to 40** (about one line per 4 to 8 turns) |

A script (section 13) replays the simulation set through the cue rules and **fails if a game averages more than 45
lines, or if any 10-turn window holds more than 6**. Voices must feel like punctuation, not narration.

## 4. The cast

All voices are fictional performances. None imitates a real recording or a living person. Names in the game are
those already in `CIVS` in `src/shared/data.ts`.

### 4.1 The Narrator (speaker id `narrator`)

* A warm storyteller by a fire. Unhurried, a little wry, never a trailer announcer.
* Adult, neutral accent, low to middle register, about 130 words per minute, slow on bad news and slightly brighter on good.
* Speaks in the present tense, concrete nouns, no numbers, no jargon ("your people", never "contentment").
* Owns every line that is not a leader's: starting, founding, eras, wonders, war and loss, events, countdowns, endings.

### 4.2 The eight leaders (speaker ids = civilisation ids)

Leaders speak **only for computer-controlled civilisations.** When the rival is a human in hot-seat or online play,
the narrator speaks instead (a person is not Pericles).

| Speaker id | Leader | Manner | Imagery to lean on |
|---|---|---|---|
| `jericho` | The Spring Warden | Gentle, serene guardian of the oldest town. Slow. | Water, wells, walls |
| `sumer` | Queen Puabi | Formal and ceremonial, court speech. | Clay tablets, temples, barley |
| `egypt` | Hatshepsut | Confident builder, calm authority. | The Nile, monuments |
| `indus` | The Priest-King | Measured, orderly, contemplative. | Straight streets, weights, drains |
| `rome` | Augustus | Clipped, formal, quietly ambitious. | Legions, roads, marble |
| `persia` | Cyrus | Generous and tolerant, a host to many peoples. | Roads, guests, the sky |
| `maya` | Pakal the Great | Patient, ceremonial, star-watching. | The calendar, omens, jade |
| `greece` | Pericles | Orator: reasoned, public-speech cadence. | Words, the city, ideas |

Each leader has 7 cues (section 6.3). Each is one line at first; a second variant is added only if repeats are noticed.

## 5. When a voice plays (rules)

These rules live in one module (`voice.ts`) and are the same for every cue.

**Priority** (higher wins). 5 ending (victory, defeat, elimination); 4 war, city lost, being attacked; 3 wonder, new
age, countdown, leader lines; 2 events and fortune; 1 everything else.

* A cue with higher priority than the line now playing **cuts it** with a 120 ms fade, then plays.
* A cue with the same or lower priority arriving during a line is **dropped**, except cues marked *can wait*, which wait up to 3 seconds and are then dropped.
* Priority 4 and 5 are never dropped for being busy.

**Cool-downs.**

* **Global:** at least 20 seconds between two narrator lines (priority 5 excepted).
* **Per cue group:** each cue has a cool-down in turns (listed in the catalog). A cue inside its cool-down is silent.
* **Once flags:** cues marked *once* play at most once per game (the first contact, the first war, and so on). The flag is kept in memory for the session and also in `localStorage`, keyed by the game's seed and the turn it last played, so it survives a reload without touching the save file. After a reload a *once* cue may play again only if the game is at least 30 turns past that turn.

**Variants.** Each cue has 2 or 3 variants. The player draws from a shuffled bag so the same variant never plays twice in a row.

**Freshness.** Lines about events older than the current turn are skipped. On loading a game, rejoining online or
returning to a tab after more than 10 seconds, notes already in the log are not replayed (the first view marks all
existing notes as seen; this is how toasts already work).

**Tab hidden or device muted.** Nothing plays while `document.hidden`, the map mute button is on, or Voices is Off.

**Lateness.** A clip that is not loaded when its cue fires is requested, but is played only if it can start within
**400 ms** and the scene is still on screen; otherwise the caption alone is shown (see 11).

**Hot-seat.** Voices play for the player currently looking at the screen, after the "pass the device" curtain. Cues
are never played during the curtain.

**Online.** Each client plays its own view. Notes that concern everyone ("X captured Y", "X completed a wonder") reach all players, so a global-note cue plays for a viewer only when it is *important to them* (section 7.3).

**Computer turns.** When many things happen at once during the computer's turn (three wars, two captures), the
priority and cool-down rules pick one; the rest stay in the log.

## 6. The line catalog

Every line is final-quality text, not a placeholder, so it can be reviewed, recorded and captioned as written.
Names of cities and wonders are deliberately **not** spoken (see 9.5): the caption and the title card carry them.
Word counts are at most 14. Each variant gets a permanent id (`<cue>.<n>`) which is never reused.

### 6.1 Narrator: tier 1, the scenes (38 cues, 76 lines)

| Cue id | Prio | Cool-down / once | Plays when | Variants |
|---|---|---|---|---|
| `game.start` | 3 | once | A new game opens on turn 1 (not when loading a save) | 1. "A river, a hill, and a people with somewhere to go." 2. "Every great city began as a few fires on open ground." 3. "The world is wide and quiet. Begin." |
| `city.capital` | 3 | once | The player's first city is founded | 1. "Your first city. Everything else grows from here." 2. "A capital is founded. Let it be remembered." 3. "Smoke rises from the first hearths." |
| `contact.first` | 3 | once | The first other civilisation is met | 1. "Smoke on the horizon. You are not alone." 2. "Strangers approach. Greet them wisely." 3. "Another people. Another story." |
| `era.classical` | 3 | once | Classical age begins | 1. "The Classical age begins. Cities learn to dream." 2. "Stone and law: the Classical age." |
| `era.medieval` | 3 | once | Medieval age begins | 1. "The Medieval age begins. Walls rise, and so do ideas." 2. "Faith, steel and long winters: the Medieval age." |
| `era.renaissance` | 3 | once | Renaissance begins | 1. "The Renaissance begins. Curiosity is the new crown." 2. "Old books are opened, and the world grows larger." |
| `era.industrial` | 3 | once | Industrial age begins | 1. "The Industrial age begins. The furnaces never sleep." 2. "Coal, iron and noise: a new age." |
| `wonder.own` | 3 | 10 turns | The player completes a wonder | 1. "A wonder is raised. Travellers will speak of it for centuries." 2. "It will outlive everyone who built it." 3. "Stone, patience and pride: a wonder stands." |
| `wonder.rival` | 3 | 15 turns, can wait | A civilisation the player has met completes a wonder | 1. "A rival raises a wonder. The race goes on." 2. "Another people builds for eternity." |
| `war.on_you` | 4 | 5 turns | A civilisation declares war on the player | 1. "War comes to your gates." 2. "Someone has chosen war. Be ready." 3. "The peace is over." |
| `war.by_you` | 4 | 5 turns | The player declares war | 1. "The die is cast." 2. "You have chosen war. History is watching." |
| `city.attacked` | 4 | 30 turns | A city of the player is attacked (not units) | 1. "Your city is under attack." 2. "Hold the walls." |
| `city.lost` | 4 | 3 turns | A city of the player is captured | 1. "A city has fallen." 2. "Its lights go out, one by one." 3. "The banners come down." |
| `city.captured` | 4 | 3 turns | The player captures a city | 1. "A city is yours." 2. "Its gates open. Rule it gently." 3. "A new banner flies." |
| `peace.signed` | 3 | 10 turns | The player's treaty is signed | 1. "The spears are lowered." 2. "Peace, for now." |
| `trade.first` | 3 | once | The player's first trade agreement | 1. "Caravans now walk between you, and both of you grow richer." 2. "Trade: the quiet way to prosper." |
| `sanction.on_you` | 3 | 15 turns | Sanctions are imposed on the player | 1. "Doors close, and trade falters." 2. "Sanctions: an enemy that never draws a sword." |
| `people.restless` | 2 | 20 turns | The people become restless (contentment below zero) | 1. "Your people are restless." 2. "Murmurs in the streets: the people want more than bread." |
| `treasury.empty` | 2 | 20 turns | The treasury runs dry | 1. "The treasury is empty." 2. "The coffers echo." |
| `race.science.near_rival` | 3 | once per rival | A rival is one discovery from the Enlightenment | 1. "A rival is one discovery from the Enlightenment." |
| `race.science.own` | 3 | once | The player reaches the Enlightenment (countdown) | 1. "The Enlightenment is within your grasp. Hold your capital." 2. "Your scholars have done it. Now keep them safe." |
| `race.science.rival` | 3 | once | A rival reaches the Enlightenment | 1. "A rival has reached the Enlightenment. Stop them." 2. "The clock is ticking, and it is not yours." |
| `race.culture.own` | 3 | once | The player's culture dominates (countdown) | 1. "Your culture rules the hearts of the world. Hold on." 2. "The world sings your songs. Keep it that way." |
| `race.culture.rival` | 3 | once | A rival's culture dominates | 1. "A rival's culture is conquering hearts. Out-build them." 2. "Their songs are in every market. Answer them." |
| `race.dominion.own` | 3 | once | The player holds most of the world's cities (countdown) | 1. "Most of the world is yours. Hold it." 2. "Your banner flies everywhere. Do not let it slip." |
| `race.dominion.rival` | 3 | once | A rival holds most cities (countdown) | 1. "A rival is swallowing the world. Take cities back." 2. "One banner is rising over all the others." |
| `race.final` | 4 | each countdown | Three turns remain in any countdown (own or rival) | 1. "Time is nearly out." 2. "The last turns of this race." |
| `race.broken` | 3 | each | A countdown against the player's interest ends (hegemony or domination slipped, capital fell) | 1. "The danger passes. For now." 2. "They have stumbled. The race is open again." |
| `end.win.science` | 5 | once | The player wins by science | 1. "The Enlightenment is yours. Reason lights the whole world." 2. "Your scholars have changed the world for good." |
| `end.win.culture` | 5 | once | Culture victory | 1. "Your culture is the memory of the world." 2. "They will sing of you for a thousand years." |
| `end.win.domination` | 5 | once | Domination victory | 1. "The world bends to your banner." 2. "All roads lead to you." |
| `end.win.score` | 5 | once | Win on score at the turn limit | 1. "Time is called, and history names you its favourite." 2. "The ages close, and yours shone brightest." |
| `end.lose.science` | 5 | once | A rival wins by science | 1. "Another people lit the Enlightenment. Your story was a fine one." |
| `end.lose.culture` | 5 | once | A rival wins by culture | 1. "Another culture fills the world. Yours will not be forgotten." |
| `end.lose.domination` | 5 | once | A rival wins by domination | 1. "One banner covers the map. Yours is remembered." |
| `end.lose.score` | 5 | once | A rival wins on score | 1. "The ages close, and another people stands highest." |
| `end.eliminated` | 5 | once | The player's last city falls | 1. "Your last city is gone. Even so, you were remembered." |
| `end.turnlimit` | 3 | once | Ten turns before the turn limit (needs a new engine note, see 7.2) | 1. "The ages are drawing to a close." |

Count: 38 cues, **76 lines** (45 for the core scenes and eras, 13 for the three races, 4 for the countdown's last turns and its ending, 14 for the endings).

### 6.2 Narrator: tier 2, fortune (13 cues, 26 lines)

Only bigger events are voiced. Small ones (a harvest, travelling merchants, scholars, ruins, a village gift) stay silent.

| Cue id | Prio | Cool-down | Plays when | Variants |
|---|---|---|---|---|
| `event.drought` | 2 | 10 turns | Drought event | 1. "The rains fail, and the fields grow thin." 2. "A dry year. The granaries are watched." |
| `event.plague` | 2 | 15 turns | Plague event | 1. "Sickness walks the streets." 2. "A plague comes, and the healers are overwhelmed." |
| `event.earthquake` | 2 | 15 turns | Earthquake event | 1. "The ground shakes, and stone remembers its weight." 2. "An earthquake. Walls do not forgive." |
| `event.flood` | 2 | 15 turns | Flood event | 1. "The river rises and takes what it likes." 2. "Floodwater, and a long clean-up." |
| `event.wildfire` | 2 | 15 turns | Wildfire event | 1. "Fire runs through the dry land." 2. "A wildfire. Smoke over the fields." |
| `event.raiders` | 2 | 15 turns | Raiders event | 1. "Raiders from the hills. Count your stores." 2. "Strangers with knives take what they can carry." |
| `event.coal` | 2 | once | Coal is found | 1. "Black rock beneath the hills: coal." 2. "The land gives up a darker treasure." |
| `event.oil` | 2 | once | Oil is found | 1. "Dark gold bubbles from the ground: oil." 2. "A new fuel for a new age." |
| `event.luxury` | 2 | 15 turns | A new luxury is found | 1. "Prospectors find something beautiful." 2. "A rare treasure comes to light." |
| `event.q.dispute` | 3 | 20 turns | The dispute question appears | 1. "Scholars and priests disagree. You must choose." 2. "Two voices, one treasury. Whose will you hear?" |
| `event.q.envoy` | 3 | 20 turns | The envoy question appears | 1. "An envoy arrives from far away." 2. "A stranger with gifts and questions." |
| `event.q.rumours` | 3 | 20 turns | The rumours question appears | 1. "Sickness is whispered in the streets." 2. "Rumours of fever. Will you act?" |
| `city.starving` | 2 | 20 turns | A city loses population from hunger | 1. "Hunger walks the streets of a city." 2. "A city goes hungry." |

### 6.3 Leaders: tier 3 (56 lines)

Seven cues for each leader. Each is a single line at first. These play **only when the rival is computer-controlled**
and only when the narrator's global cool-down allows; if a narrator line and a leader line collide, the
higher priority wins and the other is dropped.

Cue meanings (the id is `leader.<civ>.<name>`):

| Name | Prio | Plays when | Cool-down |
|---|---|---|---|
| `greet` | 3 | The player first meets this civilisation | once |
| `war` | 4 | This civilisation declares war on the player | 8 turns |
| `peace` | 3 | A peace treaty with this civilisation is signed | 10 turns |
| `trade` | 3 | A trade agreement with this civilisation is signed | 10 turns |
| `refuse` | 2 | This civilisation rejects a proposal from the player | 15 turns |
| `sanction` | 3 | This civilisation imposes sanctions on the player | 15 turns |
| `fall` | 4 | This civilisation's last city falls | once |

| Leader | `greet` | `war` | `peace` | `trade` | `refuse` | `sanction` | `fall` |
|---|---|---|---|---|---|---|---|
| The Spring Warden (Jericho) | "Peace on your road, traveller. Our spring is open to you." | "You will drink from our spring no more." | "Let the water run between us again." | "Our jars for your grain. May both be filled." | "The spring does not flow that way." | "You dam the water. Remember who guards the well." | "The spring runs dry, but it will not be forgotten." |
| Queen Puabi (Sumer) | "I am Puabi, queen of Ur. You may approach." | "You have insulted the throne. The temples will curse your name." | "The queen accepts. Let it be written in clay." | "Barley and silver, sealed upon the tablet." | "The queen declines." | "Our gates are closed to you. Let the tablets record why." | "Ur burns. Let the tablets remember me." |
| Hatshepsut (Egypt) | "I am Hatshepsut. Egypt welcomes you, and measures you." | "Egypt does not forget an insult. Prepare." | "Peace is also a monument. We shall build it together." | "Gold for gold. The Nile is generous." | "Egypt thinks otherwise." | "Our granaries are closed to you." | "The Nile outlasts empires, even this one." |
| The Priest-King (Indus) | "Welcome. Our streets are straight, and our weights are fair." | "Order has been broken. Order will be restored." | "Balance returns. That is good." | "Fair weights, fair measures. We agree." | "The scales do not balance. No." | "Our merchants will not cross your borders." | "The cities are quiet now. The river remembers." |
| Augustus (Rome) | "Rome greets you. Choose your friends carefully." | "Rome has spoken. The legions march." | "Rome grants peace. Do not mistake it for weakness." | "Roads and coin: the Roman way. Agreed." | "Rome has no need of it." | "Rome will not trade with you. Remember that." | "Rome falls. Rome will be remembered." |
| Cyrus (Persia) | "Cyrus, king of kings, welcomes you as a guest." | "I offered a hand. You chose a sword." | "Let every people live in peace under the sky." | "A fair road for all. I accept." | "Not this time, friend." | "I will not trade with those who break their word." | "An empire falls, but its roads remain." |
| Pakal the Great (Maya) | "Pakal of Palenque greets you. The stars foretold your coming." | "The calendar turns toward war." | "The stars align for peace." | "Jade and cacao, by the sun's blessing." | "The omens say no." | "The omens are dark. We close our roads to you." | "The stone remembers what the jungle hides." |
| Pericles (Greece) | "Athens greets you, friend. Come, let us talk." | "Words have failed. Let the spears speak." | "Reason prevails. Let us talk again." | "A fair exchange. Athens agrees." | "Let us talk again when you propose something wise." | "Athens will trade with the honest, not with you." | "Athens falls, but ideas do not." |

### 6.4 Totals

| Group | Cues | Lines |
|---|---|---|
| Narrator tier 1 | 38 | 76 |
| Narrator tier 2 | 13 | 26 |
| Leaders tier 3 | 56 | 56 |
| **Total (phases 1 to 3)** | **107** | **158** |
| Optional per-wonder lines (phase 3, section 9.5) | 9 | 9 |

### 6.5 Writing rules for every future line

1. At most 14 words. Read it aloud once: if it takes more than 4 seconds, cut it.
2. No names of cities, wonders, players or rival civilisations (the caption cannot be wrong if the line is generic). Leaders may name their own people ("Athens", "Rome", "Ur"), since that is fixed.
3. No numbers, no game jargon, no UI words ("click", "tap", "turn"). Say "your people", not "contentment".
4. One idea per line. A good line is one image and one feeling.
5. No exclamation marks unless it is a cheer. No sarcasm at the player's expense.
6. Every cue has at least two variants once it can repeat more than twice in a game.
7. Bad news is serious, never mocking. War lines are about the situation, not the player's failure.

## 7. Where each cue comes from in the game

### 7.1 The problem with today's notes

The game already produces nearly every moment as a *note* (`Game.note`), and the client already turns notes into
toasts and camera moments. But some code reads the note's English sentence with a regular expression (for
example `moments.ts` recognises "declared war on" and "completed the wonder"). That is acceptable for visuals and
fragile for voices: rewording a sentence would silently silence a cue.

**Decision:** notes that deserve a voice carry a structured, optional `cue` field. The text is untouched.

```ts
// src/shared/types.ts
interface Note {
  /* existing: id, turn, players, text, kind, at, luck */
  /** a spoken scene: what it is and who it concerns. Optional, so old saves and old clients are unaffected. */
  cue?: { id: string; by?: number; on?: number };
}
```

* `id` is a cue id from the catalog (for example `war`, `capture`, `wonder`), *not* a variant; the client picks the variant and decides, from `by` and `on` and the viewer's id, whether it becomes `war.on_you`, `war.by_you` or a leader line.
* `by` and `on` are player ids (the actor and the one acted upon).
* The field is optional and additive: no `STATE_VERSION` bump, no change to saves, snapshots, transfer codes or the websocket protocol beyond one more optional field on a note.

### 7.2 Emission sites (all in `src/shared`)

Current line numbers (they drift; search by the note text):

| Cue (client id) | Where the note is made | New field |
|---|---|---|
| `start` | `game.ts` `create`, "The age of cities begins" | `{ id: 'start' }` |
| `capital` | `game.ts` `foundCity`, "founded" note, when the city is the owner's first | `{ id: 'capital', by: owner }` |
| `contact` | `game.ts` `meet`, both "You have met" notes | `{ id: 'contact', by: other }` |
| `war` | `game.ts` `declareWar`, "declared war on" | `{ id: 'war', by: a, on: b }` |
| `attacked` | `combat.ts` "is under attack!" | `{ id: 'attacked', on: owner }` |
| `capture` | `combat.ts` "captured … from …" | `{ id: 'capture', by: new, on: old }` |
| `peace` | `game.ts` "signed a peace treaty" | `{ id: 'peace', by: a, on: b }` |
| `deal` | `game.ts` "Trade agreement signed" | `{ id: 'deal', by: a, on: b }` |
| `refuse` | `game.ts` the three "rejected / turned down / not interested" notes | `{ id: 'refuse', by: refuser, on: proposer }` |
| `sanction` | `game.ts` "imposed sanctions" | `{ id: 'sanction', by, on }` |
| `wonder` | `game.ts` "completed the wonder" | `{ id: 'wonder', by: owner }` |
| `restless`, `treasury`, `starving` | `game.ts` upkeep notes | `{ id, on: owner }` |
| `science.near`, `science.start`, `science.final`, `science.broken` | `game.ts` the five Enlightenment notes | `{ id, by: candidate }` |
| `culture.start`, `culture.final`, `culture.broken` | `game.ts` the culture notes | same |
| `dominion.start`, `dominion.final`, `dominion.broken` | `game.ts` the domination notes | same |
| `eliminated` | `game.ts` "has been eliminated!" | `{ id: 'eliminated', on: p }` |
| `end` | `game.ts` `finish` | `{ id: 'end', by: winner }` (the victory kind is on `view.victory`) |
| `turnlimit` | **new note**: `game.ts` at `turnLimit - 10`, for all human players | `{ id: 'turnlimit' }` |
| `event.<id>` | `events.ts` where the resolution note is made (the two sites that pass `luck`) | `{ id: 'event.plague' }` etc. |

Also **not notes**, so the client triggers these directly:

| Cue | Trigger in the client |
|---|---|
| `era.*` | `GameScreen.showEraBanner` (already receives the era index) |
| `event.q.*` | `GameScreen` when a pending question opens the event dialog (`askedKey` changes) |
| `end.*` | `GameScreen` when `v.over` first becomes true (same place as `moments.finale`), choosing win/lose by `v.winner === v.me` and `v.victory` |

### 7.3 The cue layer (`src/client/voice/cues.ts`)

One function, `onNotes(fresh, view)`, receives only the notes that are new to this screen (the same `fresh` list the
toast code builds). For each note with a `cue`, it:

1. Resolves **perspective** from `view.me`: `war` with `on === me` becomes `war.on_you`; with `by === me` becomes `war.by_you`; otherwise it is a note about other people.
2. Decides **relevance** for notes about others (global notes reach every viewer): play only for the important kinds, listed here.

| Kind | Played for a viewer when |
|---|---|
| `wonder` by someone else | The viewer has met that civilisation; `wonder.rival` |
| `race.*` countdowns | Always (they affect every player) |
| `capture` between two others | Never |
| `war` between two others | Never |
| `deal`/`peace` between two others | Never |
| `eliminated` of someone else | `leader.<civ>.fall` only if the viewer was at war with them or had met them |

3. Chooses **narrator or leader**: if the other side is an AI player and the cue has a leader version (`greet`, `war`, `peace`, `trade`, `refuse`, `sanction`, `fall`), the leader of that civilisation speaks (`leader.<civ.id>.<name>`); a human rival gets the narrator's general line.
4. Calls `voice.cue(finalId, { caption: true })`.

Because the `fresh` list is the same one toasts use, **the first view of a game or a reconnect never plays voices**
(the existing `firstView` handling marks old notes as seen).

## 8. The player (client architecture)

New folder `src/client/voice/`:

```
lines.ts      the catalog: cue definitions and variants (data only; also imported by scripts/voices.ts)
cues.ts       game → cue mapping (section 7.3); the only file that knows about notes and views
voice.ts      the player: queue, priority, cool-downs, ducking, clip loading, captions
manifest.ts   typed reader of public/voices/manifest.json
```

and small changes to `audio.ts` (a voice bus and ducking), `gamescreen.ts` (call sites), `menus.ts` / `screens.ts`
(the setting), `style.css` (the caption), and `public/sw.js` (the voice cache).

### 8.1 Types

```ts
// lines.ts
export type SpeakerId = 'narrator' | CivId;
export interface Variant { id: string; text: string }          // id like 'war.on_you.2', never reused
export interface CueDef {
  id: string;                                                   // 'war.on_you', 'leader.rome.greet'
  speaker: SpeakerId;
  priority: 1 | 2 | 3 | 4 | 5;
  once?: boolean;
  cooldownTurns?: number;
  canWait?: boolean;                                            // may wait up to 3 s for a free voice
  group: string;                                                // loading group, see 9.3
  variants: Variant[];
}
export const CUES: CueDef[] = [ /* 107 entries */ ];

// voice.ts
export interface VoiceApi {
  cue(id: string, ctx?: { turn: number; viewer: number }): boolean;   // true if it will play
  stop(): void;                                                       // fade out the current line
  setLevel(l: 'off' | 'captions' | 'low' | 'medium' | 'high'): void;
  preload(group: string): void;                                       // fetch and cache, not decode
  readonly speaking: boolean;
}
```

### 8.2 Algorithm of `cue()`

```
cue(id):
  if level == off or document.hidden: return false
  def = CUES[id]; if !def: return false
  if def.once and playedOnce(id, gameKey): return false
  if turn < lastPlayedTurn[id] + def.cooldownTurns: return false
  if def.priority < 5 and now < nextNarratorAt and def.speaker == 'narrator': return false   // 20 s
  if current:
      if def.priority > current.priority: fade(current, 120 ms)
      else if def.canWait: park(def, 3000 ms); return true
      else: return false
  v = draw(def.variants)                       // shuffled bag, never the same one twice
  if level == captions or isMuted(): showCaption(v.text, estimate(v.text)); return true   // text only, no clip needed
  clip = loader.getDecoded(v.id)               // from memory if decoded
  if !clip:
      loader.fetch(v.id)                       // from the SW cache or network
      deadline = now + 400 ms
      await clip or deadline
      if !clip or sceneIsOver(): showCaption(v.text, estimatedMs); return true   // text only
  play(clip) on voiceBus; duck(music, 6 dB); showCaption(v.text, clip.duration + 600 ms)
  on ended: restore(music, 400 ms); nextNarratorAt = now + 20 s; mark once; record turn
```

### 8.3 Audio graph

`audio.ts` today creates one `AudioContext` and connects each one-shot to `destination`. The change is small:

```
sfx tones ──────► sfxBus (gain, level) ─┐
ambience ───────► musicBus (gain, level) ├─► master ─► destination
voice clips ────► voiceBus (gain, level, compressor) ─┘
```

* The voice bus gets a gentle compressor (threshold -18 dB, ratio 3:1) so quiet and loud phones both sit well.
* **Ducking:** while a voice plays, `musicBus` goes to 50% (-6 dB) over 150 ms and `sfxBus` to 70%; both return over 400 ms after the line ends (`setTargetAtTime`, no clicks).
* Gain levels match the existing `LEVEL_GAIN`: low 0.35, medium 0.7, high 1.
* Voice clips are normalised to the same loudness at build time (10.3) so the three levels mean the same thing for every speaker.
* Same `AudioContext` and the same first-tap resume as the rest of the game. If the context becomes `interrupted` (a phone call) the current line ends and the queue is cleared.

### 8.4 Memory

`decodeAudioData` gives uncompressed float samples at the context rate (44.1 or 48 kHz). A 3.2-second mono clip is
about 0.6 MB decoded, so decoding everything would use about 90 MB. The loader therefore:

* keeps the **encoded bytes** in the Cache API (cheap, persistent), not in memory;
* decodes a clip only when its cue is likely soon (its group is loaded) or fires;
* keeps at most **24 decoded clips (about 15 MB)** in an LRU, dropping the oldest;
* never decodes during a camera clip or animation burst: decoding is scheduled with `requestIdleCallback` (or a 50 ms timeout fallback) except for priority 4 and 5.

### 8.5 Captions

See 11. The caption is created by `voice.ts` itself so that a cue without audio still shows its text.

## 9. Files, formats, loading and offline

### 9.1 Layout

```
public/voices/manifest.json
public/voices/narrator/<variantId>.<hash8>.m4a
public/voices/<civId>/<variantId>.<hash8>.m4a
```

The file name includes an 8-character hash of the text, the voice settings and the provider version, so a changed line
gets a new URL (no stale cache) and unchanged lines are never regenerated.

### 9.2 Format

* **AAC-LC in `.m4a`, mono, 24 kHz, about 40 kbps.** It is the one format every current browser decodes. Opus in `.webm` would be about 25% smaller, but Safari support is uneven; revisit after a real-device check (this is the one thing that changes the numbers below).
* Normalised to **-16 LUFS integrated, -1.5 dBTP true peak**, 80 ms of silence at the head and 200 ms at the tail, silence trimmed beyond that.
* Mono because it is a voice and the phone's speaker is mono anyway.

### 9.3 Loading groups

| Group | Contents | Lines | Approx. size | Loaded |
|---|---|---|---|---|
| `core` | start, capital, contact, war, attacked, lost, captured, peace, trade, sanction, restless, treasury | 32 | 0.5 MB | At the game screen's first idle moment |
| `eras` | the four age lines | 8 | 0.13 MB | The next age's pair, when the current age begins |
| `late` | wonders, the three race types, final, broken, turnlimit | 23 | 0.37 MB | At turn 40, or when the Classical age begins |
| `endings` | wins, losses, eliminated | 13 | 0.21 MB | With `late` |
| `events` | tier 2 | 26 | 0.42 MB | At turn 10 |
| `leader.<civ>` | the seven lines | 7 each | 0.11 MB each | For each AI civilisation in the game, at the game screen's first idle moment |

Estimate: 158 lines × about 3.2 s × 5 KB/s ≈ **2.5 MB** in total; a typical game with three AI rivals fetches
core + eras + late + endings + events + 3 leaders ≈ **2.0 MB**, spread across the first 40 turns. The page makes no
voice request at all while Voices is Off.

### 9.4 Offline and the service worker

* `fetch` + `decodeAudioData` is used, not an `<audio>` element, because audio elements make range requests (partial responses) that the Cache API cannot store.
* `public/sw.js` gets one rule: requests under `/voices/` are **cache-first** into a separate cache named `jericho-voices-v1` (not the build-id cache, so a new build does not discard 2 MB of audio). Because clip URLs contain the content hash, cache-first is always correct. The manifest itself is fetched network-first.
* If the user is offline and a clip is not cached, the cue shows the caption only.
* When a new manifest arrives, clips whose URLs are no longer in it are deleted from the cache.

### 9.5 Names

Pre-recorded lines cannot say a city's name. First release: **lines contain no player-visible names**; the title card
and the caption supply them (for example the card reads "Great Library · Wonder completed in Uruk" while the narrator
says "A wonder is raised."). Optional later step (phase 3): 9 per-wonder lines, one for each wonder in
`BUILDINGS` (`pyramids`, `great_library`, `hanging_gardens`, `oracle`, `colossus`, `great_wall`, `lighthouse`,
`notre_dame`, `crystal_palace`) such as "The Pyramids rise, a promise made to eternity." They replace `wonder.own` for
the wonder's builder when present.

## 10. Production pipeline

### 10.1 Overview

```
src/client/voice/lines.ts ──► scripts/voices.ts ──► speech service or recorded files
                                      │
                                      ├─► ffmpeg (trim, loudness, encode)
                                      ├─► public/voices/<speaker>/<id>.<hash>.m4a
                                      └─► public/voices/manifest.json
```

Generated clips and the manifest are **committed**, so a normal build, CI and a contributor never need service keys.

### 10.2 The script

`scripts/voices.ts` (run with `npx tsx scripts/voices.ts`):

| Flag | Effect |
|---|---|
| (none) | Generate every clip whose hash has no file; delete orphan files; rewrite the manifest; print the summary |
| `--dry` | List what would be generated; call nothing |
| `--check` | Fail (exit 1) if the manifest is stale, a clip is missing, a line is over 14 words, or the total size is over 3 MB. Used by CI. |
| `--only <speaker>` | Limit to one speaker |
| `--import <dir>` | Take a folder of recorded files named `<variantId>.wav`, process them the same way (this is the route for human actors) |
| `--provider <name>` | Pick the speech service adapter |

Provider adapters are 30-line modules with one function, `synthesize(text, voice) → wav`. The service is chosen in
`scripts/voices.config.json` (speaker → voice id and style settings), **not** in the game, so it can be changed or
replaced by recorded audio with no game code change.

Requirements for any service: allows shipping generated audio in a commercial game; accepts SSML or a
pronunciation dictionary (Appendix A); stable voice ids across versions; 24 kHz or higher output; styles or
settings for pace and emotion.

### 10.3 Audio processing (per clip)

```
ffmpeg -i in.wav -af "silenceremove=start_periods=1:start_threshold=-50dB, \
  areverse,silenceremove=start_periods=1:start_threshold=-50dB,areverse, \
  adelay=80, apad=pad_dur=0.2, loudnorm=I=-16:TP=-1.5:LRA=7" \
  -ac 1 -ar 24000 -c:a aac -b:a 40k out.m4a
```

The manifest records for each clip the duration in milliseconds, the byte size and the text hash. The player uses the
duration to time captions and the budget check uses the size.

### 10.4 Manifest

```json
{
  "version": 1,
  "generated": "2026-11-02",
  "provider": "example-tts-2",
  "groups": { "core": ["game.start.1", "game.start.2"], "leader.rome": ["leader.rome.greet.1"] },
  "clips": {
    "game.start.1": { "speaker": "narrator", "file": "narrator/game.start.1.3fa91c0b.m4a", "ms": 3100, "bytes": 15200, "text": "A river, a hill, and a people with somewhere to go." }
  }
}
```

### 10.5 Review and quality gate

For every generated or recorded clip a reviewer listens once and fills a sheet (kept in `docs/voices-review.csv`
when the first batch exists): pronunciation correct, pace right, emotion fits, no artefacts, caption matches. A clip
that fails is regenerated with different settings (which changes its hash, so the old file is removed).

Pronunciations are fixed through the lexicon in Appendix A, never by respelling the caption.

### 10.6 Cost and effort

* Generated: about 158 lines × about 60 characters ≈ 9,500 characters per full pass; with 3 takes to choose from, about 28,500. That is small for any commercial speech service. It needs an account, an owner for it, and a line in the credits.
* Human actors: one narrator session and one session for the eight leaders (one actor with range or several actors). Lines are short, so recording takes hours, not days; direction notes are in Appendix B. The import route means the game code is identical either way.

## 11. Settings, captions and accessibility

### 11.1 The setting

* Menu → Sound gets **Voices: Off / Captions only / Low / Medium / High**, stored as `jericho.voice` (the same storage style as music and sound effects; "Captions only" shows the text and plays no audio, and requests no clips). Gain for Low, Medium and High matches the existing levels.
* **Default: Medium.** Nothing plays before the first tap on the page (browser rule), and the device's mute switch and the map's mute button silence voices too.
* A first-run note is **not** shown. If the owner prefers off by default (public places), changing one constant does it.
* The setting is per device, like the other sound settings.

### 11.2 Captions (a spoken line is never heard only)

* Shown for every voiced line while it plays: the speaker's name in small capitals ("NARRATOR", "PERICLES") and the line.
* Position: a centred band above the unit bar and the turn button, above the safe-area inset, never over the city drawer or a dialog; maximum width 560 px; 15 px text (13 px on compact screens); background with at least 4.5:1 contrast against the text, including with the colour-blind and high-contrast options.
* Timing: appears 100 ms before audio, stays for the clip length + 600 ms (or an estimate of 55 ms per character when there is no clip), fades in 120 ms.
* Tap to dismiss. Tap again on a just-dismissed caption within 3 seconds, or tap the caption while it plays, **replays** the line.
* Markup: `role="status"` and `aria-live="polite"`, so a screen reader announces it once.
* Captions follow Voices: when Voices is Off, no caption is shown (Captions only shows them without audio); a device that is muted still shows captions only if the level is Captions only (the toasts, title cards and log already carry the news).
* A line without a visible caption is a bug; the e2e test (13) asserts it.

### 11.3 Screen readers and other needs

* The game has no screen-reader announcements today; the caption's live region would be the first. A screen reader reading the caption while the voice speaks the same words would be noise. A web page cannot reliably detect that a screen reader is running, so the plan is not to guess: the Voices setting also offers a **Captions only** level (text, no audio), and a screen-reader user can choose it. The owner's decision is item 6 in section 14.
* Deaf and hard-of-hearing players get the same information from captions, title cards and the log.
* Reduced motion and the Cinematics setting do not affect voices; the sound is its own preference.
* Haptics are unchanged (the earlier event buzz stays).

## 12. Edge cases

| Situation | Behaviour |
|---|---|
| Three wars declared in one computer turn | The war cue with the highest relevance (against the player) wins; the others stay in the log |
| A wonder is completed while a war line is playing | Dropped (lower priority); the wonder card still shows |
| The player ends turns very fast | Lines are tied to turns and notes; stale ones (older than the current turn) are skipped |
| The tab is hidden when a cue fires | Nothing plays; nothing is queued |
| The game is loaded from a save | The first view marks all notes as seen; no cues. `once` cues do not replay unless 30 or more turns have passed since the last time (stored per game seed in `localStorage`) |
| A game is rejoined online | Same as a load |
| Hot-seat: the curtain is showing | No cues until the curtain closes; those that fired during it are dropped |
| Two civilisations use the same civ id | The leader line is attributed to the player whose civ it is; only one speaks at a time anyway |
| A rival is a human | The narrator's general line is used; the leader lines are not |
| Voices Off | No manifest request and no audio request at all |
| Offline, clip not cached | Caption only |
| `decodeAudioData` fails (bad file) | Mark that variant bad for the session, fall to caption only, log nothing to the player |
| The audio context is suspended or interrupted | The line is dropped; the next tap resumes the context |
| The player opens the tech tree during a line | The line continues (it is short); new cues that `canWait` wait up to 3 s |
| A very long city name | Not spoken; the caption excludes names |
| The screen is rotated mid-line | Caption re-lays out; audio continues |
| iPhone silent switch | Web Audio is silenced by the ring/silent switch on some versions; the setting screen shows a one-line hint if a test on a real device shows it matters |
| Low-memory phone | LRU limit lowers to 8 clips if `navigator.deviceMemory` is 2 or less |

## 13. Tests, acceptance and risks

### 13.1 Automated tests

`tests/voice.test.ts` (unit, no browser):

* **Catalog:** every cue has text, a speaker, a priority and a group; variant ids are unique and never reused (compared with a committed `voices.ids.json`); every line is 14 words or fewer; no line contains a digit, a banned UI word or a player-visible name placeholder; every leader has all seven cues.
* **Manifest:** every variant has a file with a matching hash; no orphan files; total size at most 3 MB; every group exists.
* **Rules:** with a fake audio context and fake clock: priority cuts and drops, can-wait expiry, global 20 s gap, per-cue cool-down, once flags, the bag never repeats a variant back to back, level `off` plays nothing.
* **Cues:** a table of notes (with `cue`, `by`, `on`, viewer ids) maps to the expected final cue ids, including human-rival → narrator and AI-rival → leader, and global notes about others being ignored.
* **Engine:** `Note.cue` is set at each emission site in section 7.2 (a small simulation drives a war, a capture, a wonder, a treaty and a deal and checks the cues); old notes without `cue` are harmless; `luck` is unchanged.
* **Budget:** `tests/voice.budget.test.ts` runs 6 seeded AI games, treats seat 0 as the viewer, feeds its notes through the cue rules with the real cool-downs, and fails if a game averages more than 45 lines or any 10-turn window has more than 6.

`scripts/e2e.ts` additions (stubbed decoder, no real audio):

* With Voices on, a founded capital shows the caption within 300 ms; with Voices Off, no caption and no request under `/voices/`.
* Offline: a missing clip still shows its caption.
* Phone sizes 360×640, 390×844, 667×375, 844×390, 932×430: the caption never overlaps the turn button, the unit bar or the city drawer, and does not cause horizontal scroll.
* The caption dismisses on tap and replays on a second tap.

### 13.2 Manual checks (a real phone)

iPhone Safari and Android Chrome: first-tap unlock; silent switch behaviour; a phone call mid-line; headphones plug/unplug; Bluetooth latency of the first line; loudness at each level next to the music; battery use over a 30-minute session.

### 13.3 Acceptance, per phase

| Phase | Done when |
|---|---|
| 1 | Narrator tier 1 plays in a real game on desktop and phone; Voices setting works; captions pass the e2e checks; total size ≤ 1.3 MB; budget test passes |
| 2 | Tier 2 plays; `Note.cue` is on all sites in 7.2; old saves and an older client still work (tested by loading a pre-change save and by feeding notes without cues) |
| 3 | Leaders play for AI rivals only; the full set is ≤ 3 MB; the lexicon passes review for all names |
| 4 | Recorded audio imported with the same game code and the same manifest; no change in tests |

### 13.4 Risks

| Risk | Likelihood | Effect | Mitigation |
|---|---|---|---|
| Voices feel repetitive | Medium | Annoying in long games | Cool-downs, 2 to 3 variants, the 45-line budget test, an easy Off |
| Synthetic voice sounds flat or odd for emotion | Medium | Cheapens the scenes | Review sheet; recorded narrator in phase 4; start with the narrator only |
| iOS audio quirks (silent switch, interruptions) | Medium | No sound on some phones | Real-device check in phase 1; captions always carry the line |
| Memory use on low-end phones | Low | Crashes | LRU of 24 clips (8 on low memory); decode on demand |
| Download weight on mobile data | Low | Slow first game | Groups loaded lazily; first minute ≈ 0.5 MB; nothing when Off |
| A reworded note silences a cue | Low | Silent bug | Structured `cue` field instead of regular expressions; engine test on every site |
| Licensing of generated voices | Low | Legal | Choose a service with commercial rights; keep terms in the repository; credits |
| Leader lines feel stereotyped | Medium | Offends or cheapens | Lines are about the leader's own imagery, not national clichés; reviewed for sensitivity; short |
| Added work breaks determinism or online play | Very low | Serious | Presentation only; one optional field on notes; no change to rules |

## 14. Phases, work breakdown, decisions needed

### 14.1 Phases

| Phase | Work | Files | Size |
|---|---|---|---|
| 0 Prototype | A throwaway player using the browser's own speech engine, reading 6 existing scene texts (found, wonder, era, war, capture, end). Purpose: judge pacing, ducking and caption placement before any audio exists. Not shipped | `voice.ts` stub | S |
| 1 Narrator, scenes | `lines.ts` (tier 1), `voice.ts`, `cues.ts`, manifest reader, voice bus and ducking in `audio.ts`, the setting, caption UI, `sw.js` rule, `scripts/voices.ts` with one adapter, `Note.cue` for the sites needed by tier 1, tests and e2e | `src/client/voice/*`, `audio.ts`, `gamescreen.ts`, `menus.ts`, `style.css`, `sw.js`, `types.ts`, `game.ts`, `combat.ts`, tests, scripts | M–L |
| 2 Fortune | Tier 2 lines, `event.*` and `event.q.*` cues, `city.starving`, `turnlimit` note, remaining `cue` sites, budget test | `lines.ts`, `events.ts`, `game.ts`, tests | M |
| 3 Leaders | Seven cues × 8; AI-only rule; per-wonder lines; lexicon review | `lines.ts`, `cues.ts` | M |
| 4 Human voices | Record or commission the narrator (and leaders); import route; re-run review | `scripts/voices.ts --import` | depends on the project |
| later | A second language: the catalog is keyed by id, so a language is a second set of lines and clips and a manifest per language | `lines.<lang>.ts` | not planned |

### 14.2 Decisions needed from the owner (with my default)

1. **Narrator only first, or leaders too?** Default: narrator in phases 1 and 2, leaders in phase 3.
2. **Generated or human voices?** Default: generated first for speed and cheap changes; commission a human narrator in phase 4 once the lines have settled.
3. **The narrator's feel.** Default: warm storyteller (section 4.1). Alternatives: solemn historian, or lighter and wry.
4. **Names in lines.** Default: none in the first release (section 9.5); wonder-specific lines later.
5. **On or off by default.** Default: on at Medium, with a one-tap Off. If the game is mostly played in public on a phone, off by default is the safer choice.
6. **Screen readers.** Default: ship the "Captions only" level together with the live region in phase 1, leave Medium as the default.
7. **Language.** Default: English only; the structure allows more.
8. **Speech service and account.** Needs an owner for the account and a decision on which service; I will write the adapter once chosen. Default: choose by trying the same five lines (a calm narrator line, a war line, an era line, a leader greeting, an ending) on two or three services and listening.
9. **Credits wording** for synthetic voices.
10. **The 45-lines-per-game budget.** Default: keep; tell me if you want more or fewer voices.

## 15. Appendices

### A. Pronunciation lexicon (for the speech service and for actors)

| Written | Say | Where it occurs |
|---|---|---|
| Puabi | poo-AH-bee | Sumer leader |
| Ur | oor | Sumer, Puabi's greeting |
| Hatshepsut | hat-SHEP-soot | Egypt leader |
| Pakal | pah-KAHL | Maya leader |
| Palenque | pah-LEN-keh | Pakal's greeting |
| Pericles | PEH-rih-kleez | Greece leader |
| Cyrus | SY-rus | Persia leader |
| Augustus | aw-GUS-tus | Rome leader |
| Jericho | JEH-rih-koh | Title, Spring Warden |
| Athens | ATH-enz | Pericles |
| Harappa, Mohenjo-daro | hah-RAP-ah, mo-HEN-jo DAR-oh | Not in lines; for any later Indus line |
| Enlightenment | en-LY-ten-ment | Narrator |
| Cacao | kah-KOW | Pakal's trade line |

### B. Direction notes for recording or generating

* **Narrator, good news:** slightly smiling, a half step up in pitch on the key word, a small lift at the end.
* **Narrator, bad news:** slower, lower, no lift at the end, a small pause before the last two words.
* **War lines (any speaker):** clipped, even, no shouting; menace through restraint.
* **Eras:** the first sentence announces, the second is a quiet afterthought.
* **Endings:** the slowest and warmest delivery, a longer final breath.
* **Leaders:** each line should be performable in one breath; keep each leader's manner from 4.2 and do not play accents as jokes.
* **Do not:** read captions as lists, add "uh" or filler, or ad-lib extra words.
* **Takes:** record three takes of each line; the reviewer picks one.

### C. Sample catalog entry

```ts
{
  id: 'war.on_you', speaker: 'narrator', priority: 4, cooldownTurns: 5, group: 'core',
  variants: [
    { id: 'war.on_you.1', text: 'War comes to your gates.' },
    { id: 'war.on_you.2', text: 'Someone has chosen war. Be ready.' },
    { id: 'war.on_you.3', text: 'The peace is over.' },
  ],
}
```

### D. Sample emission in the engine

```ts
// game.ts, in declareWar (note text unchanged)
this.note([], `${this.playerName(a)} declared war on ${this.playerName(b)}!`, 'war', undefined, false, { id: 'war', by: a, on: b });
```

`Game.note` gains one optional trailing parameter for the cue (the same way `luck` was added), so no existing call changes.

### E. Sample client wiring

```ts
// gamescreen.ts, in the notes loop that already builds `fresh`
voice.onNotes(fresh, v);

// showEraBanner(era)
voice.cue(`era.${ERA_KEYS[era]}`);

// where the event question opens
voice.cue(`event.q.${v.pending.id}`);

// where v.over first becomes true, next to moments.finale
voice.cue(endingCue(v));
```

### F. Excerpt of one game (illustrative)

| Turn | What happens | Voice |
|---|---|---|
| 1 | New game | "A river, a hill, and a people with somewhere to go." |
| 1 | Capital is founded for the player | "Your first city. Everything else grows from here." |
| 6 | First rival met (an AI Pericles) | Pericles: "Athens greets you, friend. Come, let us talk." |
| 18 | First trade agreement | "Caravans now walk between you, and both of you grow richer." |
| 33 | Drought | (silent: the event cool-down and the 20 s gap; toast only) |
| 41 | First own wonder | "A wonder is raised. Travellers will speak of it for centuries." |
| 52 | Rome declares war | Augustus: "Rome has spoken. The legions march." |
| 58 | A city is lost | "A city has fallen." |
| 66 | Peace | "The spears are lowered." |
| 84 | Medieval age | "The Medieval age begins. Walls rise, and so do ideas." |
| 120 | A rival reaches the Enlightenment | "A rival has reached the Enlightenment. Stop them." |
| 156 | The player wins by science | "The Enlightenment is yours. Reason lights the whole world." |

These are the highlights, not every line: a real game also has the other eras, a few events, a second wonder and the
rival countdowns, which brings it to the 20 to 40 range. Note the drought is left silent, because the 20-second gap
and the event cool-down apply.
