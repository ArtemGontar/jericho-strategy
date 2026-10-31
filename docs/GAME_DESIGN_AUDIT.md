# Game design audit — how JERICHO plays

This is about the game itself: pacing, decisions, mechanics, units, the AI and how games end. It builds on `FUN_AUDIT.md` (which added cultural victory, tech catch-up and the Enlightenment warnings) and does not repeat it. Website, mobile layout and hosting are covered elsewhere.

**Method.** 24 AI-only games (`scripts/fun.ts`, seeds 7000–7253, 2–8 players, easy/normal/hard, continents and islands, 200-turn limit), a static read of every rule table (`src/shared/data.ts`, `economy.ts`, `combat.ts`) and of the AI's production and war code (`ai.ts`). No human has played these builds in a controlled test, so everything about *feel* is reasoning from the numbers, tagged **[measured]** when it comes from the simulations or the tables and **[reasoned]** when it is a judgement.

## Status

**Done: item 1, policies you lose by not choosing** (owner's choice: a relaxed builder, so the contest-with-wars items wait).

- Each offer is three policies; taking one **loses the other two for the rest of the game**. The player's screen says so and lists what was passed over.
- Six new policies (Merchant Guilds, Scribes and Copyists, Monument Builders, Veteran Corps, Seafaring, Hearth and Home), so the pool is 18 and a game adopts about six.
- The culture curve is steeper (`45 × n^2.2`) so the picks arrive across the whole game instead of all by turn 100.
- The computers choose by personality (builder, warlord, balanced), earliest on their list first, instead of taking whatever was offered.

Same 24 games, before and after [measured]:

| | Before | After |
|---|---|---|
| Times each policy was adopted | 110–120 for every one of 12 | 5 to 70 across 18 (agrarian 69, scholarship 70, scribes 52, merchants 40; exploration 5, seafaring 5) |
| Victories | science 16, culture 7, last standing 1 | science 19, culture 4, last standing 1 |
| Median end turn | 153 | 158 |
| Captures per game | 1.6 | 2.4 |
| Leader ÷ second at turn 120 | 1.36 | 1.18 |

What to watch: **culture wins fell from 7 to 4**, because Patronage of the Arts (+2 culture per city) is now a choice that few computers make. If the second route to victory should stay as common as before, raise culture elsewhere or give the culture policies to the builder personality earlier. **Exploration and Seafaring are rarely chosen**, even by a computer with no reason to avoid them: they are weak next to the economy policies, so they need a stronger effect. The other leader-gap and capture changes are within the noise of 24 games and should not be read as effects.

Saves from before this change keep working: players get an empty "passed over" list, and the next offer comes at the new, higher threshold.

**Done: items 3 and 6, unit roles and a conquest win** (second pass), plus buffs for the two weak policies.

- **Walls now matter.** Only siege engines (catapult, cannon) do full damage to a walled city; soldiers and archers do 65%. Walls also keep their +100 HP and +40% defence.
- **Cavalry ride down archers and siege crews** (+50% attack against any unit that shoots). Horseman and Knight descriptions say so.
- **The computers build a mix.** An army is short of shooters, cavalry or siege when they are below about 22%, 10–20% and 12–30% of it (siege more when the enemy has walls), and then the cities build what is missing even if the army is already big enough. Before, they picked the strongest melee unit per cost every time.
- **Domination victory.** A civilisation that holds at least 60% of all cities, at least six, and at least twice as many as anyone else starts a 10-turn countdown (7 on quick). The world declares war on it, its cities become the computers' preferred targets, and winning cities back breaks it. The HUD shows the countdown.
- **Exploration** now also gives scouts and ships one more move; **Seafaring** now gives +1 gold as well as +1 food from every worked water tile. The builder personality takes Patronage of the Arts first.

Same 24 games, three snapshots [measured]:

| | First audit | After policies | After roles and domination |
|---|---|---|---|
| Victories | science 16, culture 7, last standing 1 | science 19, culture 4, last standing 1 | **science 15, culture 5, domination 4** |
| Captures per game | 1.6 | 2.4 | **2.7** |
| Lead changes per game | 3.3 | 3.3 | 4.0 |
| Median end turn | 153 | 158 | 152 |
| Archers built | 76 | 75 | **980** |
| Crossbowmen built | 4 | 19 | **703** |
| Horsemen built | 51 | 34 | **562** |
| Catapults built | 626 | 582 | **1023** |

Army makeup at turn 100 (`scripts/armies.ts`, 6 games): 84% front line, 9% shooters, 6% siege, 1% cavalry. Old front-line units stay until they are lost, so the mix moves slowly; the units built from now on follow the mix.

Still open from this pass:
- **Frigates and cannons are still almost never built** (Metallurgy is an optional tech that most games finish without), so the sea has no military role. That needs the tech-tree work (item 4) or a naval AI.
- **Cavalry is rare late** because it needs horses and Chivalry.
- Domination ended 4 of 16 continent games and none of the island games, which is what the rules say: you cannot walk to cities across water. That is a reason to make ships matter.
- 24 games is a small sample; the share of games each route wins moves by a few games between runs.

**Done: a civilisation-building pass (third pass), after the owner asked for less war and more economy and diplomacy.** The military changes above stay, but war is now costly instead of the point.

- **Luxuries.** Four new resources (wine, spices, gems, pearls). Each kind a civilisation holds, or trades for, adds +3 contentment. Every start gets one kind within reach and neighbours get different kinds, so trading is how you get the rest.
- **Contentment.** One number for the empire, shown as a heart in the top bar with a breakdown sheet. Base 9, minus 1.2 per city beyond the first and 0.25 per citizen; plus luxuries, +1 per temple, +1 per wonder, +2 each for Patronage of the Arts and Hearth and Home; minus war weariness (up to 8) and sanctions (up to 4). Below 0 cities grow up to 60% slower and produce, earn and research up to 45% less; at 12 and above they grow 10% faster and produce 5% more. This is what makes expansion a trade-off.
- **Trade agreements.** Up to four per civilisation, signed by proposal. Each pays both sides 2–5 gold a turn (more when both are large), shares luxuries (+contentment) and shares horses and iron. War, sanctions or the partner's death end them.
- **Sanctions.** Cancel the agreement, forbid a new one, cost the target 6% of its gold and 1 contentment per sanctioner (capped), and lapse after 30 turns unless renewed.
- **War costs.** War weariness rises 0.15 a turn at war, 0.35 per soldier lost and 4 per city lost, falls 1 a turn in peace, and lowers contentment. Striking a neighbour at peace is remembered for 20 turns: the computers refuse its trade offers more often, and the civilisations that trade with the victim may sanction it.
- **The computers** propose and accept trade, sanction aggressors and front-runners (at most one sanction each, and they lift them), build temples when their people are restless, stop founding cities when they are very restless, settle next to luxuries they lack, and are less keen on war when weary or restless.

Same 24 games, before the first pass and now [measured]:

| | Baseline | Now |
|---|---|---|
| Contentment median at turn 30 / 90 / 150 | not tracked | 14.0 / 7.8 / 5.8 |
| Restless civilisations at turn 90 / 150 | not tracked | 13% / 18% |
| Trade agreements in force | 0 | about 6 per game through turn 120 |
| Sanctions in force | 0 | about 2 per game |
| Victories | science 16, culture 7, last standing 1 | science 16, culture 7, domination 1 |
| Median end turn | 153 | 145 |
| Enlightenment reached (average) | 142 | 136 |

What to watch:
- **Games end about 8 turns earlier** and 21% end before turn 130 (4% at baseline). Trade income and the early "thriving" bonus speed everyone up. If you want longer games, lower the deal gold (`dealGold` in `src/shared/civ.ts`), raise the thriving threshold, or raise the late tech costs.
- **Contentment is high early** (median 14 at turn 30) because few cities cost little; the pressure arrives from about turn 60.
- **Temples arrive late** (Philosophy), so for the first stretch luxuries are the only way to stay content.
- **Luxury art in the 3D renderer is unverified on a real screen**: the flat map shows them clearly; the 3D map draws them like gold and iron.
- The seeds in two older tests were changed because new resource placement changes the generated maps.

**Done: building slots, civic techs and civilisation identity (fourth pass).**

- **Building slots.** A city holds three buildings, plus one for every two citizens, plus one in the capital, up to ten. Walls, barracks, wonders and each civilisation's own building take no slot. A building can be torn down (recovering 30% of its cost) to make room, unless another building needs it. The computers give each city a role (the capital studies, then trade, production, civic) and build what suits it.
- **Civic techs.** Ceremony (Shrine, +1 contentment, cheap and early), Drama and Poetry (Amphitheater, +2), Diplomacy (Embassy: one more trade agreement, two count), Medicine (citizens weigh a fifth less on contentment), Theology (Cathedral, +3, needs a Temple). Navigation also allows one more trade agreement. None is needed to win by science.
- **One building per civilisation,** free of slots and gated by an early tech: Spring Gardens, Ziggurat, Temple of the Sun, Great Bath, Forum, Satrapy, Stepped Pyramid, Agora.
- **Retuned** because the new buildings add culture and contentment: the new civic buildings give little or no culture, the culture goal is 4300 (was 4000), the contentment base is 8 and a citizen weighs 0.3.

Same 24 games, baseline against now [measured]:

| | Baseline | Now |
|---|---|---|
| Victories | science 16, culture 7, last standing 1 | science 14, culture 8, domination 2 |
| Median end turn | 153 | 151 |
| Games ended before turn 130 | 4% | 4% |
| Enlightenment reached (average) | 142 | 143 |
| Contentment median at turn 60 / 120 / 150 | not tracked | 5.8 / 8.7 / 7.9 |
| Restless civilisations at turn 60 / 150 | not tracked | 11% / 7% |
| Cities of one civilisation sharing the same buildings | not tracked | 50% (1 = identical) |
| Cities at full slots at the end | not applicable | 36% |
| Built across 24 games | granary 202, harbor 11 | granary 96, shrine 340, each civilisation's own building 43 to 92 |

What to watch:
- **Contentment is comfortable late** (7–9 median, a quarter of civilisations thriving) because the computers build shrines and their own building everywhere. It is a little less tight than the earlier pass; slots, not contentment, are now the main constraint on a city.
- **The tech tree is wider, not forked.** The sea and late-military techs (Sailing, Chivalry, Metallurgy) are still optional for a science victory, and Theology and the Cathedral are rarely reached by the computers. True alternative routes to the Enlightenment need an either-or prerequisite and a balance pass on their cost.
- **The computers never tear buildings down,** so an early choice stays chosen.
- Harbors are rarely built (10 across 24 games): the slot is better spent elsewhere for most coastal cities. Worth a stronger Harbor.

**Done: events, an Industrial era and less work for the player (fifth pass),** after the owner asked for more mid- and end-game content, some randomness with technology in it, and a game that does not lose people early or wear them out.

- **Random events** (`src/shared/events.ts`), Off, Gentle or Normal. Good: bountiful harvest, travelling merchants, visiting scholars, ancient ruins, a new luxury in the hills, and coal and oil once the technology exists. Bad: drought, plague, earthquake, floods, wildfire, raiders. Technology decides how hard they bite (Medicine and Sanitation, Hospital, Construction, Granary, Harbor, a garrison). Nothing bad before turn 30 or with one city; events are at least four turns apart; opponents set to easy make the player's luck gentler. All from the game's seeded random numbers.
- **Industrial era.** Industrialization, Sanitation, Electricity, Combustion; Enlightenment moved into the era and now needs Industrialization, which needs Metallurgy, so the late military line is no longer skipped. Factory, Hospital, Laboratory; four new wonders (Great Wall, Great Lighthouse, Notre Dame, Crystal Palace); coal and oil that are invisible and worthless until the technology is known.
- **Less work.** A governor per city (picks what to build when the queue empties), a suggested build marked in the city panel and offered in one tap, a suggested technology in the tree, and Auto-research. Suggestions use no random numbers, so asking never changes a game.
- **Retuned:** the culture goal is 6000 (it was 4300) so that culture does not end games before the new era is reached.

Same 24 games, baseline against now [measured]:

| | Baseline | Now |
|---|---|---|
| Victories | science 16, culture 7, last standing 1 | science 12, culture 9, domination 3 |
| Median end turn | 153 | 166 |
| Civilisations reaching the Industrial era | not possible | 53 of 100 |
| Enlightenment reached (average turn) | 142 | 155 |
| Events per civilisation per game | none | about 11 (all good before turn 30: about 2 per civilisation) |

What to watch:
- **Games are about 13 turns longer.** On the standard 200-turn limit a few reach it; Quick (120) and Short (60) still end on score.
- **Hospitals and laboratories are rarely built by the computers** (their techs are optional); they are for human players and long games.
- **Events fire about once every 14 turns per civilisation.** If that feels busy, lower `EVENT_CHANCE` in `events.ts`.
- **Culture still wins about a third of games;** the goal was raised once and may need another look after play-testing.

**Done: a guided start, a fork in the tree, later choices and a back-up nudge (sixth pass),** after the owner asked for a first game that teaches by pointing, not by text, and for the remaining next steps.

- **The coach** (`src/client/ui/guide.ts`) replaces the text checklist. A ring pulses on the actual control and a tip of two or three words sits beside it, with the key word in bold, a picture, and progress dots: *Pick what to **build***, *Choose **research***, *Send the scout to **explore***, *End the turn*, *Found a **second city***, *Sign a **trade** deal*. Each step ends by itself when the thing is done; the cross skips it; it finishes on its own after the basics (or turn 60). It scrolls a hidden control into view and never intercepts a tap.
- **A fork in the tree.** The Enlightenment needs one of three roads: Electricity (learning, Laboratory), Sanitation (health, Hospital) or Combustion (the sea and industry, oil). They cost about the same from the common way; which is cheapest depends on what a civilisation already knows, and the computers lean by temperament.
- **Later policies** join the offers as their technology is learned (Bread and Circuses, Open Society, Public Health, Academies, State Religion, Industrial Policy), each with a price.
- **Three events ask a question**: scholars and priests disagree, an envoy arrives, sickness is rumoured. Two big buttons, a picture and a word or two each; ignored, they settle themselves next turn, so nobody waits, online or off.
- **A back-up nudge**: once or twice in a long game, a note that opens the save sheet in one tap, never more than every three days and not if a copy was made within the week.
- **Drawn icons** for the civic, industrial, wonder and unique buildings and for coal and oil, in place of borrowed ones.

Same 24 games, baseline against now [measured]:

| | Baseline | Now |
|---|---|---|
| Victories | science 16, culture 7, last standing 1 | science 13, culture 7, domination 2, score 2 |
| Median end turn | 153 | 175 |
| Civilisations reaching the Industrial era | impossible | 75 of 100 |
| Road taken to the Enlightenment | one | electricity 16, sanitation 23, combustion 34 |
| Events per civilisation per game | none | about 12 (all kind before turn 30) |

What to watch:
- **Games are now about 20 turns longer than the first baseline.** That is the added era and its choices. On the standard 200-turn limit most still end by a victory; a few reach the limit and end on score. Quick and Short are unchanged in feel.
- **State Religion is rarely adopted** because it needs Theology, which few computers research.
- **The coach is checked in a browser at four sizes** but has had no new-player test; its wording and order are the first thing to adjust after one.

## The one-paragraph verdict

The first thirty turns are good: a first city on turn 3, three cities by turn 30, a clear checklist, and a lot to do. After that the game turns into a **checklist**. Every building, every technology (bar one), every policy and every wonder ends up built, learned or adopted in every game, because nothing costs you anything you would miss. Land runs out around turn 60, wars start late (turn 55 on average) and rarely finish, so the next 90 turns are a queue of build orders until someone learns the Enlightenment and wins by science. The rules are sound and the engine is solid. What is missing is **scarcity**: reasons to choose one thing over another.

## What the numbers say

### Pacing [measured]

| | Turn 30 | Turn 60 | Turn 90 | Turn 120 | Turn 150 |
|---|---|---|---|---|---|
| Cities per civilisation | 3.2 | 5.4 | 5.7 | 6.0 | 6.2 |
| Leader score ÷ second | 1.14 | 1.15 | 1.21 | 1.36 | 1.31 |

- First city turn 3.4, first contact turn 3.3, **first war turn 55**, and 8% of games never see a war.
- Expansion is finished by turn 60. From turn 60 to turn 150 the average civilisation gains less than one city.
- The games end at a median of **turn 153** (earliest 126). The Enlightenment is reached at turn 142 on average.
- 16 of 24 games end by science, 7 by culture, 1 by last civilisation standing. **None end by conquest.**

So the stretch from about turn 60 to turn 140 has no growth axis (the land is taken), no early war and no event. It is where a player is most likely to feel nothing is happening. [reasoned]

### Decisions [measured]

| What | Evidence | What it means |
|---|---|---|
| Policies | All 12 are adopted at the same rate (110–120 times each across 24 games). Each is a flat bonus (+1 food, +10% gold, and so on). None changes a rule, and none excludes another. | The choice is only an order. At the end everyone has all twelve. |
| Buildings | Library 695, University 674, Market 618, Temple 548, Bank 528, Observatory 494, Aqueduct 509 built across the same games. Payback is 20–53 turns for every building that gives a yield; upkeep is at most 2. | Nothing is ever worth skipping, so a city's build order is a to-do list, not a plan. |
| Technologies | None is left unresearched. 20 of 24 are required to win by science; the four optional ones are **Sailing, Chivalry, Navigation, Metallurgy** (the whole sea and late-military lines). The longest chain is 6 techs and no technology excludes another. | Research order barely matters, and the optional lines are almost never used (Metallurgy was learned by 7 players in 24 games). |
| Wonders | Each non-coastal wonder was built in every game (24/24, 23/24). | Every wonder is available to somebody, so no wonder race is ever lost. |
| Improvements | Three (farm, mine, road). Seven resources, each a fixed yield, two of them "strategic" (horses, iron). | Terrain is a score to read once at the start, not a puzzle you return to. |

[reasoned] A Civilization-style game gets most of its depth from tension between things you want and cannot all have. At the moment you can have all of them.

### Military and conquest [measured]

Units built across 24 games: pikeman 2565, musketman 2033, swordsman 1341, spearman 1300, warrior 929, catapult 626, knight 606, archer 76, horseman 51, crossbowman 4, cannon 3, caravel 7, **frigate 0**.

- Every melee unit costs about the same per point of strength (24–27 strength per 100 production). Ranged and siege units are worse on that measure (16–17) and exist for their special rules only.
- The AI buys by strength divided by the square root of cost, so it always picks the strongest melee unit it can: pikeman, then musketman. Whole branches of the roster (archers, crossbowmen, cannons, frigates, caravels) are almost dead content. [measured]
- Captures: 1.6 per game and 0.1 eliminations, against 1.8 wars per game. A war usually ends where it began. The attack counter in `fun.ts` (600 per game) also counts city bombardments, so it is not a measure of fighting.
- Units upgrade along a single ladder (warrior → swordsman → musketman, and so on) and older units become impossible to build (`obsoleteBy`). There are almost no counters beyond "spears and pikes beat horses". [measured]

### Victory

- Science is the default result. Culture works (7 of 24, mostly easy and hard computers), which was the point of the last pass.
- Domination exists only as "last civilisation standing". There is no softer conquest goal (hold every capital, hold most cities), so a player who prefers war has no target short of eliminating everyone, which almost never happens.

### The computer opponents [measured and reasoned]

- Difficulty is a multiplier: hard computers get +15% science and production, easy ones −20%. They do not play differently, so "hard" means a slightly faster tech race.
- Personalities (builder, warlord, and so on) change a few weights but the build lists are the same.
- Every civilisation's special bonus is a small flat percentage (+10% science, +10% gold, +1 food). There are no unique units or buildings, so playing Rome and playing Sumer feel alike. [reasoned]

## What to do, ranked

Each item names the problem, the change, the risk and how it would be checked. All of it lives in `src/shared/`, so it is covered by the existing simulations (`scripts/fun.ts`, `scripts/audit.ts`, `npm test`) and is identical for solo, hot-seat and online.

| # | Change | Why | Effort | Risk |
|---|---|---|---|---|
| 1 | **Policies you lose by not choosing.** Each offer of three: take one, the other two are gone for the game. Raise the cost so the four or five policies arrive across the whole game. Keep culture useful through hegemony. | Turns a to-do list into an identity: a game is "the fortress one" or "the scholar one". | S | Low. One rule, one UI line ("forgone"). |
| 2 | **Building slots.** A city holds a limited number of buildings (for example 3 + one per 3 population, wonders and walls excluded). | Forces the real city decisions: library or market, temple or workshop. It also makes more, smaller cities worth founding. | M | Medium: slows science, which is the goal, but the whole curve needs retuning with `fun.ts`. |
| 3 | **Give the dead lines a job.** Walls halve damage from units that are not siege; cavalry are strong against archers and crossbows; ranged units are at their best behind a fortified pike. Teach the AI to assemble a mix. | Makes archers, crossbows, catapults and cannons the answer to something, so army composition becomes a decision. | M | Medium: combat balance. Check with `scripts/wars.ts`. |
| 4 | **Branch the tech tree.** Make the science victory need one of two routes (for example Navigation *or* Chivalry, in addition to the core), and add a few techs so the sea and the late military lines matter. | Research order becomes a plan. The sea and late-army content gets used. | M–L | Medium. |
| 5 | **Pressure in the middle.** Raiders and barbarian camps, and a few events (drought, plague, a rich harvest). | Fills turn 60–140. It also gives peaceful players something to defend against. This needs an owner slot for a "neutral" side that every renderer and the view code assume exists, which is why it was left out before. | L | High: touches the state shape, views and both renderers. |
| 6 | **A war goal short of elimination.** Domination victory by holding most of the cities or every capital for some turns. | Gives conquest a finish line, so a fighter has a way to win. | S–M | Low. |
| 7 | **Civilisation identity.** One unique unit or building per civilisation instead of a flat percentage. | Makes eight civilisations feel like eight games. | M | Low. |
| 8 | **Harder computers that think harder.** Hard: war earlier, mixed armies, targets the leader. Easy: forgiving. Drop the output multipliers. | The difficulty setting should change how the game is played, not how fast. | M | Medium. |
| 9 | **Catch-up for the early lead.** Cheaper settlers for the civilisation with fewer cities, a small bonus to the one far behind. Untested. | Leader ÷ second goes from 1.14 to 1.36 by turn 120. | S | Low. |

**Suggested first slice: 1, 3 and 6.** They are independent, small to medium, change how games feel without changing the state shape, and each can be checked against the same 24 seeds before and after. Then 2 and 4 (which fix the checklist problem from two sides), then 5 and 7.

## Questions for you, the owner

1. **What is the game meant to feel like?** A relaxed builder you finish in a sitting (the current design, plus the checklist fixes), or a contest with real wars? This decides whether 5 and 8 are worth their cost.
2. **How long should a game last on a phone?** The Quick and Short lengths exist, but the mid-game emptiness gets *worse* as the game shortens, so I would want to tune the pacing for the length you care about.
3. **Is it acceptable that a game changes after a save?** Items 1, 2 and 4 change rules. A saved game from before keeps its rules only if the rule set is stored in the save (I would add a `rules` version to the state). It is cheap now and expensive later.

## How to re-run the numbers

```
npx tsx scripts/fun.ts 12 0 200 > a.jsonl; npx tsx scripts/fun.ts 12 12 200 > b.jsonl     # about 4 minutes, two in parallel
npx tsx scripts/fun.ts --sum a.jsonl b.jsonl
```
