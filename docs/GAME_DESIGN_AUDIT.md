# Game design audit — how JERICHO plays

This is about the game itself: pacing, decisions, mechanics, units, the AI and how games end. It builds on `FUN_AUDIT.md` (which added cultural victory, tech catch-up and the Enlightenment warnings) and does not repeat it. Website, mobile layout and hosting are covered elsewhere.

**Method.** 24 AI-only games (`scripts/fun.ts`, seeds 7000–7253, 2–8 players, easy/normal/hard, continents and islands, 200-turn limit), a static read of every rule table (`src/shared/data.ts`, `economy.ts`, `combat.ts`) and of the AI's production and war code (`ai.ts`). No human has played these builds in a controlled test, so everything about *feel* is reasoning from the numbers, tagged **[measured]** when it comes from the simulations or the tables and **[reasoned]** when it is a judgement.

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
