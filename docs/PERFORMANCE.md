# Performance and memory

All numbers come from the sandbox these were built in: a software-rendered browser (SwiftShader) and a shared CPU. They show **relative** cost and what the code allocates, not how smooth the game feels on a real GPU. Frame times from software GL are not reported for that reason; draw calls, triangle counts, heap and DOM growth are.

## Re-running the measurements

| What | Command |
|---|---|
| End-turn time per map size and player count | `npm run bench` |
| Server heap per room, bytes and CPU per broadcast command | `npm run bench:server` |
| "Did a refactor change the game?" (state hash after 70 turns, five seeded games) | `npm run fingerprint` before and after, compare the output |
| Invariants over many AI games | `npx tsx scripts/audit.ts 4 60` |

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
