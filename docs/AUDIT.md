# Audit — JERICHO web game

What was checked: the engine over many AI-only games (`scripts/audit.ts`: 21 games, 2–8 players, three map sizes, three difficulties), command fuzzing (`scripts/chaos.ts`), the multiplayer server with hostile input, the 3D renderer for leaks and hitches, phone layout, and the browser flows (`scripts/e2e.ts`). Numbers below come from those runs; anything not measured is marked as such.

## Fixed in this pass

| # | Severity | Finding | Evidence | Fix |
|---|---|---|---|---|
| 1 | Critical | **One malformed URL took the whole server down** (and every running game with it). `decodeURIComponent` threw outside any handler. | `GET /%E0%A4%A` → uncaught `URIError` | Request handler wrapped; returns 400. Test added. |
| 2 | Critical | **Any client could crash the server** by sending a WebSocket frame over 16 KB: `ws` emits `error` and nothing listened. | 40 KB frame → uncaught `Max payload size exceeded` | `error` listener terminates only that socket; `clientError`/process-level guards added. Test added. |
| 3 | High | **An AI player could be stuck with no city for the whole game.** A move order whose route ran through a tile held by its own worker reported success but never moved; the AI repeated it every turn. | seed 1005: Greece had a settler and no city after 40 turns | Pathing now routes around (or waits behind) friendly units that block a step; a boxed-in first settler founds where it stands. Regression test. |
| 4 | High | The same rule made other units oscillate between two tiles, burning their movement. | audit reported 8 military units idle under a goto order | "Wait when part-way through a turn, go around from the start of a turn." Re-measured below. |
| 5 | Medium | 3D map hitched 35–90 ms on every newly explored tile (props rebuild + terrain rebuild). | measured 35 ms props, 89 ms terrain on a medium map | Rebuilds wait until exploration settles; terrain updates only the changed region. |
| 6 | Medium | A failing 3D renderer would leave a dead map. | — | Three failed frames in a row switch to the flat map with a notice. |
| 7 | Low | Clouds drifted over the black unexplored area; initial zoom on phones was tiny; icon-only buttons had no accessible name. | screenshots | Clouds only over explored ground; phone starts at a playable zoom; titles become `aria-label`s; the map has a role and label. |

Also checked and fine: no GPU resource growth over 20 turns (geometries/textures/programs flat), save size (442 KB for a 120-turn, 8-player, large map, well under the 5 MB localStorage limit), `npm audit` (0 vulnerabilities), command fuzzing (no invariant violations).

## Open — ranked

### Gameplay and balance
1. **The AI almost never fights.** Over 21 games: 17 ended by science, 4 by score, **0 by conquest, 2 city captures in total**. War declarations are gated (grace period, neighbour distance, low probabilities) so that beginners are not rushed; the side effect is that the game is a research race. *Suggestion:* make `warlord` and `hard` AIs commit to a target (stage an army, then declare), scale the grace period down with difficulty, and add an "Aggressive computers" setup option.
2. **Hard AI finishes Enlightenment around turn 107–118.** Whether a good human can beat that is unmeasured — it needs a playtest, not a simulation. If it is too fast, add 10–15 % to `TECH_ERA_FACTOR` for the hard level only.
3. **No ships.** Water is a wall: no island maps, no naval exploration, no coastal defence. This is the largest missing piece of the Civilization feel.
4. Auto-explore can thrash (a scout walks back and forth for several turns before settling on a target). Cosmetic for AIs, annoying for a human who set "auto".
5. No barbarians, trade routes, religion or great people — fine for a first release, but the mid-game has few decisions beyond expand/research/war.

### Multiplayer and operations
6. Room codes are four letters and rooms are open to anyone who guesses one while in the lobby. Fine among friends, weak on a public server. Longer codes or a host-set password would close it.
7. The server refuses new rooms at 300, so a script could fill it. Needs a per-IP limit on room creation and connections.
8. No spectators; a player who leaves a lobby is not shown why.
9. CI runs the browser tests on a software GL stack, so it exercises only the flat 2D renderer. The 3D path has had one manual 15-turn run and the unit tests in `tests/geometry.test.ts`, nothing automated in CI.

### Client and UX
10. **3D performance on real hardware is unmeasured.** Software rendering is far slower than any GPU, so the numbers here prove correctness, not smoothness. Quality drops automatically if frames run long; Menu → Graphics overrides it.
11. No tutorial or first-game guidance beyond the tips; no idle-unit list; no colour-blind palette; English only. Accessibility is partial (labels added now, but menus are not fully keyboard-navigable and there is no focus trap in dialogs).
12. Sound has one mute toggle: no separate volume for music and effects. The audio itself has not been listened to by anyone yet.
13. Notifications overlap the top of a phone screen for a few seconds.

### Engineering
14. Two map renderers (3D and flat 2D) now have to be kept in step. The flat one could be dropped once the 3D one has proved itself on real devices — or kept as the accessibility/low-power option.
15. One commit on the branch (`d0317eb`) is missing the attribution trailers; it was pushed before I could amend it.

## How to re-run the checks

```
npx tsx scripts/audit.ts 21 150     # engine invariants over many AI games (~20 minutes)
npm run chaos                        # random invalid commands
npm test                             # unit + server tests, including hostile-input tests
npm run build && E2E_FLAT=1 npm run e2e      # browser flows on the 2D map
npm run build && E2E_GL=1 E2E_ONLY=solo npm run e2e   # the 3D map, one section (slow without a GPU)
```
