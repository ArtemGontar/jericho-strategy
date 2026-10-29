# Audit — JERICHO web game

What was checked: the engine over many AI-only games (`scripts/audit.ts`: 21 games, 2–8 players, three map sizes, three difficulties), command fuzzing (`scripts/chaos.ts`), the multiplayer server with hostile input, the 3D renderer for leaks and hitches, phone layout, and the browser flows (`scripts/e2e.ts`). Numbers below come from those runs; anything not measured is marked as such.

## Fixed in this pass

| # | Severity | Finding | Evidence | Fix |
|---|---|---|---|---|
| 1 | Critical | **One malformed URL took the whole server down** (and every running game with it). `decodeURIComponent` threw outside any handler. | `GET /%E0%A4%A` → uncaught `URIError` | Request handler wrapped; returns 400. Test added. |
| 2 | Critical | **Any client could crash the server** by sending a WebSocket frame over 16 KB: `ws` emits `error` and nothing listened. | 40 KB frame → uncaught `Max payload size exceeded` | `error` listener terminates only that socket; `clientError`/process-level guards added. Test added. |
| 3 | High | **An AI player could be stuck with no city for the whole game.** A move order whose route ran through a tile held by its own worker reported success but never moved; the AI repeated it every turn. | seed 1005: Greece had a settler and no city after 40 turns | Pathing now routes around (or waits behind) friendly units that block a step; a boxed-in first settler founds where it stands. Regression test. |
| 4 | High | The same rule made other units oscillate between two tiles, burning their movement. | audit reported 8 military units idle under a goto order | "Wait when part-way through a turn, go around from the start of a turn." Re-run of the same 21 games: **0 invariant violations (was 8)**; city captures rose from 2 to 10 as the stuck units started fighting. |
| 5 | Medium | 3D map hitched 35–90 ms on every newly explored tile (props rebuild + terrain rebuild). | measured 35 ms props, 89 ms terrain on a medium map | Rebuilds wait until exploration settles; terrain updates only the changed region. |
| 6 | Medium | A failing 3D renderer would leave a dead map. | — | Three failed frames in a row switch to the flat map with a notice. |
| 7 | Low | Clouds drifted over the black unexplored area; initial zoom on phones was tiny; icon-only buttons had no accessible name. | screenshots | Clouds only over explored ground; phone starts at a playable zoom; titles become `aria-label`s; the map has a role and label. |

Also checked and fine: no GPU resource growth over 20 turns (geometries/textures/programs flat), save size (442 KB for a 120-turn, 8-player, large map, well under the 5 MB localStorage limit), `npm audit` (0 vulnerabilities), command fuzzing (no invariant violations).

## Implemented since (second pass)

| Item | What changed | Measured result |
|---|---|---|
| Server hardening | 5-letter room codes, per-address connection / room-creation / wrong-code limits, 20-minute lobby TTL | `tests/limits.test.ts` |
| Join bug | The join screen still demanded a 4-letter code, so nobody could join a room after the code length change. Found by the online e2e run. | e2e "Online" passes again |
| AI aggression | Personalities, an "Aggressive computers" setup option, siege rush on battered cities, war planning on the same landmass, Enlightenment countdown with a world-wide response | `scripts/wars.ts`, 6 games each, 4 players, medium map: normal 2.3 captures/game, hard 1.2 captures/game, 0.0–0.2 eliminations/game |
| Ships and islands | Trireme, caravel, frigate, harbor, boarding and disembarking, island and scattered-island map styles, AIs build and explore by sea | `AUDIT_ISLANDS=1 scripts/audit.ts` |
| First-game guidance | A "Getting started" checklist that ticks itself off and can be dismissed; dialogs get `role="dialog"`, focus on open | e2e, screenshots |
| Volume | Separate music and effects levels (off / low / medium / high), remembered | — |

**Still not solved:** all 12 simulated games above ended by science (turn 123 on hard, 147 on normal). Wars and captures are up, but conquest never finishes a game, and the countdown did not change who wins. Whether that is a problem for humans needs a playtest.

## Open — ranked

### Gameplay and balance
1. **Games still end by research.** See above. Candidate levers: slower late-era techs, a conquest-friendlier victory threshold, stronger AI offence once a rival starts the Enlightenment.
2. Whether a good human can beat the hard AI's Enlightenment (around turn 107–123) is unmeasured.
3. Auto-explore can thrash for a few turns before settling on a target.
4. No barbarians, trade routes, religion or great people.

### Multiplayer and operations
5. Rooms are open to anyone with the code while in the lobby; there is no host password.
6. No spectators.
7. CI runs the browser tests on a software GL stack, so only the flat renderer is automated; the 3D path has manual runs and `tests/geometry.test.ts`.

### Client and UX
8. **3D performance on real hardware is unmeasured.** Software rendering proves correctness, not smoothness. Quality drops automatically if frames run long; Menu → Graphics overrides it.
9. No idle-unit list, no colour-blind palette, English only. Menus are not fully keyboard-navigable and dialogs have no focus trap.
10. The audio has not been listened to by anyone yet.
11. Notifications overlap the top of a phone screen for a few seconds.
12. Flat-renderer screenshots through Playwright time out on this machine (the page never goes idle); the e2e flows themselves pass. Not investigated.

### Engineering
13. Two map renderers (3D and flat 2D) must be kept in step.
14. One commit on the branch (`d0317eb`) is missing the attribution trailers; it was pushed before it could be amended.

## How to re-run the checks

```
npx tsx scripts/audit.ts 21 150     # engine invariants over many AI games (~20 minutes)
npm run chaos                        # random invalid commands
npm test                             # unit + server tests, including hostile-input tests
npm run build && E2E_FLAT=1 npm run e2e      # browser flows on the 2D map
npm run build && E2E_GL=1 E2E_ONLY=solo npm run e2e   # the 3D map, one section (slow without a GPU)
```
