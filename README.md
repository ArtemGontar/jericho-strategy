# JERICHO

A Civilization-style turn-based strategy game that runs in your browser — play solo against computer rivals, share one screen in **hot-seat**, or **play online with friends** (up to 8 players, any mix of humans and AI).

Found cities, work the land, research 24 technologies across four eras, adopt policies, race for wonders, explore tribal villages, make and break treaties, and conquer your neighbours — sail to distant islands, or out-research them to win by Enlightenment — then hold your capital while the whole world tries to take it.

The map is a real 3D scene drawn with WebGL (three.js) from a tilted camera: terrain with relief, rock and snow, a water shader with surf, shadows, wind in the trees, clouds and birds, towns that grow house by house, and articulated figures for every unit that walk, aim, strike, fortify and fall. The sun moves across the sky as turns pass. Menus sit on a 3D render of a finished world, and the game has a quiet generative soundtrack with wind, surf and birdsong.

The interface follows the classic strategy-table look: a hanging research and culture medallion pair, a round *Next Turn* button that always shows what needs you next, unit flags on the map, a city drawer with pinnable tiles, a full tech tree with prerequisite lines, and a master/detail diplomacy screen. It is drawn with one small hand-made icon set (`src/client/icons.ts`) and a flat, hairline-separated visual language — no emoji, no nested cards.

> The original Unity prototype is archived in [`legacy/`](legacy/README_unity_ru.md). This web version is a ground-up rewrite built around a small, deterministic, fully tested game engine.

## Play

```sh
npm install
npm start          # builds the client and serves game + multiplayer on http://localhost:8080
```

Open <http://localhost:8080>. From the title screen:

| Mode | What it is |
| --- | --- |
| **Quick game** | One tap: a random civilisation against two computers on a small map, 60 turns. |
| **New game vs computer** | 1–7 AI rivals, three difficulties, how warlike they are (Calm / Normal / Fierce), map sizes, continent or islands, quick/standard speed. Autosaves every turn, plus three save slots, export and import (**Saved games**). Setup offers three game lengths: 200, 120 and 60 turns. |
| **Play online with friends** | Create a room, send the 5-letter code or invite link. Simultaneous turns, optional turn timer, lobby chat, reconnect after a refresh, host can hand a vanished player to the AI, and **Play again** after the game. |
| **Hot-seat** | 2–4 humans on one device with a "pass the device" curtain between turns, plus optional AI. |

### Playing with friends who are not on your network

The server is one Node process that serves both the game and the WebSocket lobby. Any of these works:

* **Quick tunnel** (no account): run `npm start`, then in another terminal `npx cloudflared tunnel --url http://localhost:8080` (or `npx localtunnel --port 8080`). Send the printed `https://…` link to your friends.
* **Docker**: `docker build -t jericho . && docker run -p 8080:8080 -v jericho-data:/data jericho`
* **Any Node host** (Fly.io, Render, Railway, a VPS…): build command `npm ci && npm run build`, start command `npm run server`. Set `PORT` if the platform asks; set `DATA_DIR` to a persistent volume to keep rooms across restarts. Health check: `/healthz`.

Room state (the whole game) is saved on the server after every turn, so a restart does not end a game — players just rejoin with the same link.

## How to play

1. **Found your capital** — select the Settler and press `B`. Pick food-rich land near hills and the coast.
2. **Choose production** in the city panel (`C`): warriors for safety, workers and settlers to grow, then libraries, markets, walls… Queue several items, or spend gold to **buy** the current one. **A city holds only a few buildings** (three, plus one for every two citizens, plus one in the capital, up to ten), so each city needs a role: the capital studies, another trades, another builds, another keeps the peace. Walls, barracks, wonders and your civilisation's own building take no slot, and you can tear a building down to make room.
3. **Pick research** (`T`). Technologies unlock units, buildings, wonders and tile improvements.
4. **Explore and expand.** Cities must be 3 tiles apart. Workers build farms, mines and roads (or set them to **Auto**). Scouts can auto-explore (`A`).
5. **Meet the neighbours.** By default civilisations start at *peace* (armies cannot enter each other's borders). You can only attack civilisations you are at war with: declare war from **Diplomacy** (`D`) once a treaty has held for 10 turns. The *Start at war* option makes everyone hostile on contact. Once you are at peace you can sign a **trade agreement** (gold for both of you every turn, and you share each other's luxuries and horses and iron) or impose **sanctions** on a civilisation you want to hurt without fighting it: trade stops and they lose gold and contentment. Declaring war on someone at peace is remembered, and computer players may shun you for it.
6. **Grow your people.** Culture fills the ring beside the research medallion; every time it completes you choose one of three **policies** (lasting bonuses) and the two you pass over are lost for the rest of the game, so each game ends up with a different set. Click tiles in the city drawer to **pin** citizens where you want them. Walk onto **tribal villages** for rewards. **Keep them content:** every city you found and every citizen you grow makes your people a little more restless, and restless people grow and produce less. Luxuries (wine, spices, gems, pearls — each kind counts once, whether you hold it or trade for it), temples, wonders and a few policies bring contentment back, and war takes it away. The heart at the top shows how you are doing.
   **Surprises.** Now and then something happens to you: a bountiful harvest, travelling merchants, visiting scholars, a new luxury or (once you have the technology to use them) coal and oil under your own land; or a drought, a plague, an earthquake, floods, wildfire or raiders. What you know decides how hard it bites: Medicine and Sanitation make plague rare and a Hospital stops it, Construction lets buildings stand through an earthquake, a Granary softens drought, a Harbor turns floods away, a garrison turns raiders back. Nothing bad happens before turn 30 or while you have a single city. Choose **Off**, **Gentle** or **Normal** when you start a game.
   **Less to do.** Hand any city to a **Governor** and it chooses what to build whenever it runs out, or tap the suggested build; turn on **Auto-research** and the next technology picks itself. Both can be switched off at any time.
7. **Fight.** Click an enemy to attack — hover first to preview the damage. Ranged units (archers, catapults, crossbowmen, cannons) shoot two tiles away without retaliation. Walls turn back everything but siege engines, and cavalry ride down archers. Fighting earns experience and promotions; outdated units can be **upgraded** for gold inside your borders. Reduce a city to 0 HP and walk a melee unit in to capture it; capturing a lone worker or settler takes it prisoner.
8. **Cross the sea.** Learn *Sailing* and build a Trireme (or, later, a Caravel or Frigate) in a coastal city. Walk a unit onto an adjacent friendly ship to board it, sail, then step ashore on the far coast — settle islands, scout, or raid. Choose *Islands* in the setup to give every player a home island of their own. Ships can only attack other ships, except the Frigate, which bombards coasts.
9. **Win** by being the last civilisation standing, by discovering **Enlightenment** (the last technology of the Industrial era) first and holding your capital for 12 turns while everyone else gets a chance to take it (science), by **domination** (hold at least 60% of all the cities, six or more, and twice as many as anyone else, for 10 turns), or by holding the highest score when the turn limit ends.

### Saving, and playing on more than one device

JERICHO runs in any modern browser, so a phone, a tablet and a computer all play the same game, and an online room can mix them. There are no accounts.

| What | Where it lives | Moving it to another device |
| --- | --- | --- |
| **Solo and hot-seat games** | In the browser on the device: an autosave at the end of every turn, plus three slots (Menu → Save game). Each save carries a version, so a newer build still opens an older save. | **Send** (Saved games, or Menu → Save game → *Send this game to another device*) gives an 8-character code that works once, for an hour, through the game server. On the other device open *Saved games*, type the code and press *Receive*. Or **Export** a file and **Import** it. |
| **Online games** | On the game server, which saves the room after every turn and brings it back after a restart (rooms nobody has touched for three days are not restored, and files older than a week are deleted). Your seat is a secret kept in your browser. | *Menu → Continue on another device* gives a link that takes over your seat from the new device. Anyone with the link can play your seat, so send it only to yourself. A refresh or a closed tab needs nothing: the same browser slips back into its seat. |
| **Settings** (sound, graphics, events, accessibility) | In the browser, per device. | Set them again on the new device. |

If a browser clears its site data (some do after a week without a visit, unless the page is installed to the home screen), its saves go with it, which is a reason to keep a copy with Send or Export. Solo and hot-seat also work with no network once the page has been opened once.

### The eight civilisations

Each has a bonus and one building of its own that nobody else can build and that takes no slot:

| Civilisation | Bonus | Own building |
| --- | --- | --- |
| Jericho | +1 food in every city centre | Spring Gardens: +3 food, +1 contentment |
| Sumer | +10% science | Ziggurat: +3 science, +1 culture |
| Egypt | +1 production in the capital, cheaper wonders | Temple of the Sun: +1 production, +2 culture, +1 contentment |
| Indus | +10% gold | Great Bath: +2 contentment, +1 food |
| Rome | military units built 15% faster | Forum: +4 gold, +1 contentment |
| Persia | workers build 50% faster | Satrapy: +2 gold, +2 production |
| Maya | +2 culture per city | Stepped Pyramid: +2 science, +2 culture |
| Greece | +2 gold per city, +1 science per temple and library | Agora: +2 contentment, +2 science, +1 gold |

Five civic technologies sit beside the main tree: **Ceremony** (Shrine), **Drama and Poetry** (Amphitheater), **Diplomacy** (Embassy: one more trade agreement), **Medicine** (citizens weigh less on contentment) and **Theology** (Cathedral). **Navigation** also allows one more trade agreement. None is needed to win by science.

### Graphics

The 3D renderer needs WebGL 2. On a software renderer (no GPU) the game falls back to a flat 2D map by itself, and when frames run slow it lowers shadows and resolution on its own. **Menu → Graphics** forces *High / Medium / Low / Flat 2D*. For testing, add `?flat` to the address for the 2D map or `?gl` to force 3D.

### Phones and tablets

Solo and hot-seat work offline once the page has been opened (a small service worker stores what the game needs). Display options in **Menu**: colour-blind palette, high contrast, vibration, turn alerts (a marker in the tab title, a buzz and an optional notification when a new turn is ready while the tab is in the background). The interface adapts to touch screens in both orientations. Held upright, the unit card sits bottom-left beside the turn button and the city panel is a bottom sheet. On its side, the order buttons float beside the unit, the city panel becomes a side panel and the menu screens use two columns. Safe areas (notches, home bar) are respected. Cities, diplomacy and the message log are reachable from **Menu** when the top bar has no room for them. `npm run e2e:flat` checks four phone sizes.

### Controls

| Input | Action |
| --- | --- |
| Click / tap | Select · move · attack (on touch: tap a destination, tap again to confirm) |
| Right-click | Always issues a command to the selected unit |
| Drag, arrow keys | Pan the map · wheel / pinch to zoom |
| `Enter` | Do the next thing (choose production → research → next unit → end turn) |
| `N` · `Space` | Next unit · skip this unit |
| `B` `F` `Z` `A` | Found city · fortify · sleep · auto-work / auto-explore |
| `C` `T` `D` `L` | Capital/city panel · technology tree · diplomacy · city list |
| `Y` `G` `M` `/` | Tile yields · grid · mute · chat (online) |
| `Esc` | Close panel / deselect / menu |

## Project layout

```
src/shared/   Pure TypeScript game engine – no DOM, no Node APIs, deterministic from a seed
  game.ts       state + command handling + turn processing
  combat.ts     damage model, previews, city capture
  economy.ts    tile/city yields, growth, research, costs
  ai.ts         computer players (settling, workers, scouting, defence, offence, diplomacy, research)
  mapgen.ts     continents, resources, fair start positions
  view.ts       per-player fog-of-war snapshot sent to clients
  data.ts       every rule number: terrain, units, buildings, wonders, techs, civilisations
src/server/   Node + ws: static files, rooms, lobby, authoritative game, per-player views, turn timer, persistence
src/client/   HUD and screens (vanilla TypeScript, no framework), icon set, audio, animation timeline, cinematic moments (`cinematics.ts`, `moments.ts`; Menu → Cinematics: Full / Short / Off)
  gl/           three.js map: terrain, water, props, cities, unit models, particles, fog of war, 2D overlay
  render.ts     the flat 2D map used as the fallback renderer
tests/        Vitest: engine rules, determinism, full AI games, multiplayer server integration
scripts/      simulate.ts (AI-vs-AI games) · fuzz.ts (stability) · idle.ts (AI aggression) · e2e.ts (browser tests)
legacy/       The archived Unity prototype and its design documents
```

The same engine runs in the browser for solo/hot-seat games and on the server for online games; clients only ever receive what their player can see.

## Development

```sh
npm run dev         # Vite dev server (proxies /ws to a server on :8080 – run `npm run server` too for online play)
npm test            # engine + server tests
npm run typecheck
npm run sim -- 4 4 200      # 4 AI-only games, 4 players, 200 turn limit
npm run fuzz -- 30          # stress many random games
npm run build && npm run e2e   # full browser tests (needs Playwright + Chromium)
npm run build && npm run e2e:flat   # the same on the flat 2D map (lighter on machines without a GPU)
npm run build && npm run e2e:gl     # 3D smoke test: WebGL2 context, four turns, no failed frames, a drawn map, JS heap
npx tsx scripts/explore-thrash.ts 24 60   # how much an auto-exploring scout doubles back
```

The 3D smoke test is not part of the default CI job, because it needs a WebGL2-capable Chromium. It passes here on a GPU-less machine (software rendering, about 20 s). To run it in CI, add a step after the normal end-to-end one; it has not been tried on GitHub's runners:

```yaml
      - name: 3D smoke test
        run: npm run e2e:gl
```

Balance knobs live in `src/shared/data.ts` (unit/building/tech tables) and the top of `src/shared/economy.ts` (`TECH_ERA_FACTOR`, `PROD_COST_FACTOR`).
