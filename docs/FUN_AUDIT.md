# Enjoyability and AI-competition audit

Method: `scripts/fun.ts` plays AI-only games and records how they end and how they feel to compete in. 30 games per run, 2–8 players, easy / normal / hard, continents and islands, 200-turn limit (`npx tsx scripts/fun.ts 10 <0|10|20> 200 > out.jsonl`, then `npx tsx scripts/fun.ts --sum out*.jsonl`). `scripts/culture.ts` prints culture over time. All numbers are simulations of computer players; whether this is fun for a person is still a playtest question.

## What the baseline showed (30 games, before changes)

| Question | Finding |
|---|---|
| Who wins and how? | **29 of 30 ended by science, 1 by score, none by conquest.** Median end turn 159, earliest 136; Enlightenment reached on average at turn 152. Hard ended on average at turn 153. |
| Is there a contest? | Weak. The winner was already #1 on score at turn 50 in 17 of 30 games. Leader / runner-up score ratio grew from 1.15 (turn 60) to 1.64 (turn 150): the leader runs away. |
| Fighting | First contact turn 12, first war turn 59 (10 % of games never saw a war). About 670 attacks per game but only 1.4 city captures and 0 eliminations: lots of skirmishing, nothing decided. |
| Early pacing | First city on turn 3.4 on average, 3.2 cities per civ at turn 30, 5.2 at turn 60, 6.0 at turn 120. Expansion stalls around 6 cities. |
| Dead options | **Culture had no use after turn ~100**: all 12 policies are adopted by then and culture only adds a few score points. Cannon (0 built), Frigate (0), Crossbowman (1 built in 30 games): Metallurgy was researched by 3 players in 30 games,. Every policy is adopted at the same rate (141 ± 4): the choice never matters for the AI. |
| Feedback to the player | Wars, captures, wonders, eliminations and the Enlightenment countdown are announced to everyone. Missing: any warning that a rival is *about to* start the countdown, and nothing at all about culture. |

## Changes

1. **Cultural hegemony, a second peaceful victory.** A civilisation with at least 4000 culture (2680 on quick games) and 20 % more than every rival starts a 10-turn countdown (7 on quick); a rival climbing back to within 10 % breaks it. This gives culture a purpose after the policies run out, gives builders a path, and gives other civs a way to stop the leader that is not a war. A countdown chip in the top bar, a progress note every 10 turns once the leader has half of the goal, and a tooltip on the culture stat show the race. Computers join the race (extra temple and wonder weight when within reach, builders always) and treat a hegemony leader like an Enlightenment leader: they are more willing to declare war on them and target their capital.
2. **Tech leakage as catch-up.** Each civilisation you have met that already knows a technology makes it 8 % cheaper for you (up to 24 %). Followers close the gap, the leader gets no help, so the science race tightens.
3. **Tech-race warning.** Everyone is told when a civilisation becomes one discovery away from the Enlightenment.
4. **Late military technologies are researched.** Engineering, Chivalry and Metallurgy get higher AI weights (Metallurgy was almost never researched).

## Results (same 30 games, same seeds)

| | before | after (goal 4000) |
|---|---|---|
| Victory types | science 29, score 1 | **science 20, culture 8, conquest 2** |
| Median end turn | 159 | 153 |
| Games ended before turn 130 | 0 % | 7 % |
| Leader / runner-up score at turn 150 | 1.64 | 1.28 |
| Winner already #1 at turn 50 | 17 of 30 | 15 of 30 |
| Wars per game | 1.1 | 1.9 |
| Metallurgy researched (players, 30 games) | 3 | 8 |

Tuning notes: a culture goal of 3600 made culture win 15 of 30 and ended 17 % of games before turn 130; 4500 made it win only 4. 4000 is the compromise. On normal difficulty culture still wins only 1 of 12; it is the easy (4 of 6) and hard (3 of 12) AIs that take it.

## Still open

- **Conquest stays rare** (2 games of 30 end that way, 1.5 captures per game against ~580 attacks). The attackers cannot convert fights into captures; that is the next balance problem and needs a look at siege strength against walls and pikemen. Pikemen (3000+ built) and Musketmen (2300+) dominate unit production while Cannons and Frigates are nearly absent.
- **Winner predictability** is still about 50 % from turn 50. Tech leakage helped the late game, not the early one; stronger early catch-up (for example cheaper settlers when you have fewer cities than the average) is untested.
- **Barbarians and other mid-game pressure** were not built: units need an owner slot that every renderer and the view code assume exists.
- All 12 policies are adopted at the same rate by computers; the policy menu is not a real decision for them. Humans may feel differently.
- Nothing here measures fun directly. The first real 4-player game with friends will tell more than these tables.
