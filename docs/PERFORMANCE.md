# Performance and memory

All numbers come from the sandbox these were built in: a software-rendered browser (SwiftShader) and a shared CPU. They show **relative** cost and what the code allocates, not how smooth the game feels on a real GPU. Frame times from software GL are not reported for that reason; draw calls, triangle counts, heap and DOM growth are.

## Re-running the measurements

| What | Command |
|---|---|
| End-turn time per map size and player count | `npm run bench` |
| Server heap per room, bytes and CPU per broadcast command | `npm run bench:server` |
| "Did a refactor change the game?" (state hash after 70 turns, five seeded games) | `npm run fingerprint` before and after, compare the output |
| Invariants over many AI games | `npx tsx scripts/audit.ts 4 60` |

| What | Command |
|---|---|
| Browser: idle and pan CPU, end-turn time, heap and DOM on a large mid-game save | `npm run build && npx tsx scripts/perf.ts` (`PERF_ONLY=flat\|gl`, `PERF_IDLE=40`) |
| Browser: which functions the page spends its time in | `npx vite build --minify false --outDir /tmp/dist-dev --emptyOutDir && DIST=/tmp/dist-dev npx tsx scripts/perf-profile.ts idle\|turns flat` |

## Second pass (November): engine and idle cost

Measured on the same machine before and after; the browser has no GPU here, so 3D frame costs are reasoned, not measured.

| Change | Why | Effect |
|---|---|---|
| Luxury tiles are a short index (`Game.luxuryTiles()`), not a scan of the whole map | Contentment is recomputed after every command, and each call scanned every tile once for the player and once per trading partner. It had grown to about 10 % of an AI turn | Large map, 8 players, 80 turns: end-of-turn **49 ms → about 32 ms** on average, p95 98 → about 78 ms |
| Path search: allocation-free neighbour loop, a reused best-cost table, and an immediate "no route" for a walker whose target is on another landmass | 15 % of searches fail and each failure used to flood the whole explored map | Paths and games are unchanged: the state hash after 70 turns matches on all five seeded games |
| Minimap is drawn once per new view into a hidden canvas; each frame only copies it and draws the viewport frame, and does nothing when neither world nor camera moved | It redrew every tile up to 3 times, about 20 times a second | Flat map idle script time 6.7 % → 4.3 % of one core |
| A map that only shimmers or pulses a selection ring is redrawn at 30 frames a second at most (it was every screen refresh while a unit was selected, which is most of the time), at half after 20 s without input, at a quarter after 90 s, and not at all behind a dialog | An idle phone was rendering the 3D scene at 60 frames a second | 3D: half the frames while a unit is selected, a quarter to an eighth when left alone. Any touch restores the full rate at once. Moving units, paths and camera moves still draw every frame |
| Shadow map 1024² instead of 2048² on phones (shorter screen side 520 px or less) | A phone shows a small patch of ground | A quarter of the shadow memory and fill. Screenshots at 390×844 differ from the old ones by less than two captures of the same build differ from each other |
| The autosave is written 300 ms after the new turn is on screen (and at once when the page is hidden, closed or left), not before | Serialising a 350 KB state took tens of milliseconds on this machine, several times that on a phone, in the middle of the end-of-turn wait | The new turn appears sooner. Nothing is lost: a pending save is flushed on `visibilitychange`, `pagehide` and when the game screen closes |
| Unit-bar height and the unit action buttons' size are no longer measured on every redraw (a `ResizeObserver` and a cache keyed by unit state) | Reading `offsetHeight` right after rebuilding the bar forced a layout; it was the largest single item in an end-of-turn profile | Removed from the profile |
| The minimap and map buttons now sit above the turn button however tall its labels are (`--turn-h`) | On a 390 px phone "Choose production" wrapped to two lines and the button overlapped the map buttons | Fixed layout bug |

Not changed, on purpose: the 30-frame idle cap itself (water and leaves still move smoothly while you play), the 2048 shadow map on larger screens, pixel ratio, and any rule or AI decision.

What is still large: every command rebuilds the whole HUD (`refreshUI`), the AI and the engine run on the main thread (a worker would remove the end-of-turn pause on slow phones, and is the next big step), updateVisibility runs after every move (about 16 % of an AI turn), and path searches still scan their open list linearly (a heap was tried: it gave the same routes but no speed-up, because the cost is in the neighbour checks).

## Engine

Hot spot: visibility recomputation and hex-range queries. Every unit step recomputed who can see what, and each range query rebuilt and sorted a list of positions. Range offsets are now computed once per row parity and radius, and the distance function no longer allocates.

| End-turn, AI only, hard, 80 turns | Before | After |
|---|---|---|
| small, 2 players | 7.5 ms | 4 ms |
| medium, 4 players | 62.6 ms (p95 179) | 18 ms (p95 47) |
| large, 8 players | 165 ms (p95 598, max 754) | 29 ms (p95 77, max 102) |

The game state after 70 turns is byte-identical to the previous code on five seeded games (`npm run fingerprint`). What is left on the profile: A* path search (about 17 %), visibility (about 15 %), garbage collection (about 5 %).

## 3D renderer

* The ground is cut into blocks and every kind of prop is instanced per block, so blocks off screen are skipped (frustum culling) in both the main pass and the shadow pass. Before, the whole map was drawn every frame.
* The shadow map is refreshed when the view or a figure moves, and otherwise 15 times a second. With only ambient animation running, frames are limited to 30 per second, and to about 70 on high-refresh displays.
* Cones no longer carry an apex cap (zero area), 20–25 % fewer triangles on pines and grass tufts, with identical pixels.
* High-DPI screens (device pixel ratio 1.75 and up) no longer allocate a multisampled back buffer; at that density edges are already smooth.
* Terrain buffer uploads send only the rows that changed.

Large map, 8 players, turn 61, 1280×760, per frame (main pass + shadow pass), software-rendered:

| View | Draw calls before → after | Triangles before → after |
|---|---|---|
| default zoom | 1379 → 1535 | 1.47 M → 1.15 M |
| zoomed in | 955 → 1059 | 1.40 M → 0.92 M |
| zoomed out (whole map in view) | 1740 → 1958 | 1.53 M → 1.41 M |

Draw calls went **up** a little because blocks multiply instanced meshes; triangles went down by a quarter at default zoom and by more when zoomed in. In steady state the shadow pass runs on every second frame and frames are capped at 30 per second, so per second, at default zoom, the renderer submits about 2.4 times fewer draw calls (34 k instead of 83 k) and about 3.4 times fewer triangles (26 M instead of 88 M) than the old 60 frames a second with a full shadow pass each. While the camera pans or figures move, every frame still renders shadows, and the saving is the triangle count only. Pixel comparison against the old build with sky and time frozen: mean difference under 0.3 of 765 on the default view, no missing blocks when zoomed out or at the map edge.

What I could not measure: GPU time and real frame rate. Draw calls are dominated by unit and city figures (about 700 meshes in view on a busy map); merging those would be the next step, and needs a real GPU to judge.

## Memory

* **Fixed: leaving a game leaked the whole game.** Shared figure geometry and materials outlive a renderer, and three.js keeps GPU buffers in a WeakMap keyed by them, which kept the old canvas, GL context and the entire previous game screen (and its `Game`) reachable. Each start and leave added about 500 DOM nodes, 65 listeners and 0.9 MB; after the fix 12 start/leave cycles stay flat at 76 nodes and 32 listeners.
* JS heap in a running game, large map, 8 players, 60 turns: 9 MB to 12 MB (game state, views, figures); geometry and texture counts level off with the content on the map (no growth from one turn to the next); allocation while idle is about 10 KB per second, about 10 KB per frame while panning.
* Server: a started large-map room holds about 0.4 MB, so the 300-room cap bounds it near 120 MB. Rooms with nobody connected are dropped by the idle sweep (20 minutes in the lobby, 6 hours once started).

## Network

* A command that changes nothing in a player's picture of the world now sends that player nothing (previously every command sent every player a full view).
* WebSocket frames are deflated (no context takeover, so no per-connection zlib window): a large-map view is about 27 KB of JSON and about 1 KB on the wire.

## Loading

| First load (gzipped) | Before | After |
|---|---|---|
| JavaScript needed for the menus and the flat map | 237 KB | 82 KB |
| three.js and the 3D renderer | in the main file | 154 KB, loaded when needed |
| CSS | 6.6 KB | 6.6 KB |

The web-font stylesheet no longer blocks first paint.
