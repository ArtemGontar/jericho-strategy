# Product audit — JERICHO

Written from the product owner's seat. `AUDIT.md`, `FUN_AUDIT.md` and `PERFORMANCE.md` already cover engine correctness, server hardening, AI balance and renderer cost; this document does not repeat them. It looks at what a player, a friend being invited, and the person running the game would experience, and ranks what to do next.

**Evidence tags.** `[measured]` was run or counted in this repository. `[code]` was read in the source. `[inferred]` is reasoning from how browsers or players usually behave and needs a check on a real device or with real players. Nothing here comes from real players: the game has no analytics, so every claim about player behaviour is `[inferred]` by necessity (see direction 9).

## Status

Updated after the first round of work. The owner decided that **analytics and error reporting are not wanted**, so direction 9 and roadmap item 1 are dropped, not postponed. The recommendation about "measure before guessing" stands only as the playtest questions at the end.

| Area | Done | Not done |
|---|---|---|
| Saves (7) | Versioned records with migration from the first release, autosave plus three slots, export and import as a file, a saved-games screen, plain messages for a full or refused store and for a save that cannot be opened (the file is kept), tests with a committed first-release save | Cloud save |
| Quick games (2) | **Quick game** on the title screen (random civilisation, 2 computers, small map, 60 turns), a third game length, *Short · 60*, and phones default to *Quick · 120* in the setup form | Scaling costs for the 60-turn length (it plays at the quick pace) |
| Friends first (5) | Native share sheet and copy for invites (lobby and menu), link-preview tags and a 1200×630 picture (absolute addresses when `SITE_URL` or Vercel's production address is known), a server check on the Play online screen that disables create and join and says so when no backend answers, a 60-turn option in online rooms, a tab-title marker, buzz and optional notification when a new turn starts while the tab is in the background | Real push notifications (needs a push service and a server key), asynchronous long timers, room passwords, kicking, spectators |
| Phone basics (1, 6) | The first-game list became a one-line chip with touch wording, tile info is placed above the unit card with a background and clears itself, offline service worker, manifest `standalone` and `any` orientation, screen wake lock, vibration (own setting), smallest text sizes raised on phones | Reachability test with real hands; a real-device performance check of the 3D map |
| Smaller items (8, 10, 11, 12) | Fonts bundled instead of loaded from Google, colour-blind palette (Okabe-Ito civilisation colours, blue and orange for good and bad, a mark on warring rivals), high-contrast option, build number in the menu, a daily 3D end-to-end job, deploy docs point to `main`, stricter type checking (the `noUnusedLocals` option was listed twice with `false` winning) | Translations (strings are still inline), a linter (typescript-eslint does not support TypeScript 7 yet), screenshot comparisons, a privacy page, a record of where each picture came from |

Update, after the cross-device work: a solo game now moves between devices with a one-time code (and still by file), an online seat moves with a hand-over link, the title screen always offers *Saved games* so a new device can receive one, and a new game can start with the capital already founded.

Two corrections to the first version of this document:

- **Tile info on touch.** The first version said tapping a tile shows nothing. It does show a strip, but the strip had no background, sat under the notification area and collided with toasts. That is what was fixed.
- **Confirmation dialogs.** Found while testing: every `askConfirm` dialog (Leave game, declare war and attack, replace a save) had no styles at all. It rendered as a plain block at the top-left of the screen, underneath the open menu, so "Leave game" appeared to do nothing. Fixed; a browser test now checks the dialog is visible and centred.

## Summary

JERICHO is already a complete, unusually polished browser strategy game for its size: 8 civilisations, 24 technologies in 4 eras, 17 units, 17 buildings and wonders, 12 policies, 3 victory routes, sea play, solo, hot-seat and online with friends, a 3D map with a flat fallback, and a hardened server. The engine is deterministic and tested. The weak spots are not in the game rules. They are around the game: how a stranger learns it, whether a game fits in a phone session, whether progress survives, whether friends can actually get in, and whether anyone can tell how it is going.

The five bets with the best return, in order:

1. **Measure before guessing.** No analytics and no client error reporting exist. Add a privacy-light funnel (started, first city, turn 10, turn 30, finished, quit screen) and error capture. Everything else on this list is easier to rank afterwards. *(small)*
2. **Make a game fit a phone session.** The median game ends at turn 153 on standard speed. That is hours of play on a phone. Make *Quick* the default on phones, add a 60-turn "Skirmish" length, and make leaving and resuming effortless. *(small to medium)*
3. **Protect progress.** One localStorage slot, no versioning, a silent failure when a save cannot be read, and iOS Safari clears site storage after about a week of non-use. Add versioned saves, three slots, export/import, and a visible "saved" signal. *(medium)*
4. **Fix the invite path.** "Play with friends" is the headline promise. Add native share (Web Share API), a link preview image, and a check that the live site can actually reach a backend. *(small)*
5. **Make the first ten minutes work without a mouse.** Tap-to-inspect a tile, a phone-friendly first-game guide, and a short scripted tutorial game. *(medium)*

## 1. First-time experience and onboarding

| Finding | Evidence | Impact | Suggestion |
|---|---|---|---|
| The "Getting started" checklist is hidden on every phone-sized screen, so the player who most needs it never sees it. | `[code]` `.guide { display: none }` in the phone rules (it was already hidden at 760 px and below before the mobile pass; now also on short landscape) | High on mobile | Show the current step as a one-line banner above the turn button on phones; tap to expand the list. |
| ~~Tapping a tile shows nothing about it.~~ **Corrected:** it does show a strip, but it was unstyled, sat under the toasts and stayed forever. | `[measured]` screenshot after a tap | Medium on mobile | Done: the strip sits above the unit card on a background and clears after a few seconds. |
| New players choose among 8 civilisations, 3 difficulties, 3 AI temperaments, 3 map options, 2 speeds, 2 victory modes, a seed and an opponent count before they have seen the game. | `[code]` setup screen | Medium | Offer **Quick start** on the title screen (random civilisation, normal, quick speed, 2 opponents) and keep the full form behind "Custom game". |
| Only one tutorial surface exists, a text checklist. No scripted first game, no contextual hints when something unusual happens (first war, first policy, first wonder, first sea crossing). | `[code]` `ui/guide.ts` has 7 steps and nothing else | Medium | A guided 30-turn first scenario on a small fixed map (the seed field already makes this cheap), plus one-time "first time you…" hints. |
| The manifest asks for landscape, fullscreen. An installed copy is therefore locked to landscape even though portrait is now a supported layout. | `[code]` `public/manifest.webmanifest`: `"orientation": "landscape"` | Medium | Change to `"orientation": "any"`, `"display": "standalone"` (fullscreen hides the status bar and, on some Androids, the system gestures). |

## 2. Core loop and session design

| Finding | Evidence | Impact | Suggestion |
|---|---|---|---|
| Games run long. Median end turn is 153 (standard speed); the earliest simulated end was turn 136. | `[measured]` `FUN_AUDIT.md`, 30 games | High on mobile | Default phones to *Quick* (120 turns); add a "Short · 60" option with scaled costs. Measure turns per minute with real players before choosing the number. |
| A turn can take many taps: every idle unit, every city, research, policy. Only the single Next Turn button batches this. | `[code]` `primaryAction` walks one item at a time | Medium | "Auto-end if nothing needs me", a "fortify all idle military" shortcut, and city production queues that repeat. |
| There is no undo, not even for a mis-tapped move or disband (the disband button sits next to skip). | `[code]` none found | Medium on touch, where mis-taps are the norm | One-step undo for moves and orders made this turn (the engine is deterministic and snapshots are cheap), plus a confirm on disband for units that are not nearly dead. |
| Culture, science and gold are visible; food, production and happiness trade-offs are only inside the city panel. | `[code]` top bar | Low | A one-tap "empire overview" (all cities, food/production, idle, growing) sheet. The Cities list exists and is now in the phone menu, but it has no per-city action. |

## 3. Content depth and strategy variety

| Finding | Evidence | Impact | Suggestion |
|---|---|---|---|
| Mid-game has no pressure beyond rival civilisations: no barbarians, no events, no trade, no religion, no great people. | `[code]` + `AUDIT.md` item 4 | High for replay value | Order by cost-to-value: random events (drought, plague, raid) → barbarian camps (needs an extra "neutral" owner slot, see the note in `FUN_AUDIT.md`) → trade routes → great people. |
| Three tile improvements (farm, mine, road) and 7 resources. Terrain choice matters little after the first few cities. | `[measured]` counted from `data.ts` | Medium | Add plantations/camps/quarries tied to resources, and a luxury or happiness mechanic so expansion has a cost. |
| Conquest almost never ends a game (2 of 30 simulated). Pikemen and musketmen dominate production; cannons and frigates are almost unused. | `[measured]` `FUN_AUDIT.md` | High for players who want to fight | Retune siege (attack bonus against walls, a ramp for city strength), then re-measure with `scripts/fun.ts`. |
| The policy choice does not matter to the AI (all 12 adopted at the same rate), and may not matter to humans either. | `[measured]` `FUN_AUDIT.md` | Medium | Make policies mutually exclusive in branches so choosing one closes another. |
| The leader runs away: the winner is #1 on score by turn 50 in about half the games. | `[measured]` `FUN_AUDIT.md` | Medium | Catch-up for the early game (cheaper settlers for the player with fewer cities). Untested. |

## 4. Progression, retention and the meta game

Nothing carries from one game to the next. After a victory or defeat the player gets a final screen and a score table, and then starts from zero with no record that they played.

| Finding | Evidence | Suggestion |
|---|---|---|
| No statistics, achievements, game history or personal bests. | `[code]` none found | A local "Chronicle": games played, wins by victory type, fastest win, civilisations tried. Local only, no account needed. |
| The seed already makes a game reproducible, but nothing uses it. | `[code]` `SoloConfig.seed` | A **daily seed** (same map for everyone, local best score) is the cheapest retention feature available and works offline. |
| No replay or end-of-game story. The engine is deterministic from a seed plus commands, so a replay is possible. | `[inferred]` from the design | An end-of-game timeline (score per turn, wars, wonders, city captures) first; a full replay later. |
| Unlock-free by design (all civilisations available at once). | `[code]` | Keep. It suits a friends-first game. Do not add unlock grind. |

## 5. Multiplayer and the social promise

| Finding | Evidence | Impact | Suggestion |
|---|---|---|---|
| Turns are simultaneous and the session must stay open. There is no asynchronous mode and no way to be told it is your turn once you leave the tab. Phone browsers suspend background tabs within minutes. | `[code]` WebSocket transport; turn timer only | High: the main way friends actually play on phones | Web Push (installed PWA) for "your turn", "game started" and "a friend joined", plus a longer asynchronous turn timer (hours). This also needs the PWA work in direction 6. |
| Inviting is "copy the link". There is no native share sheet and no link preview, so the message in a chat app is a bare URL. | `[code]` menu offers "Copy invite link" only; `index.html` has no description or Open Graph tags | High | `navigator.share()` where available, plus `og:title`, `og:description` and an `og:image`. |
| The production frontend is a static Vercel site. The "Play online" button is always shown, but WebSockets need the separate backend. If `VITE_SERVER_URL` is not set for the Vercel build, the client falls back to the page's own `/ws`, which a static host does not serve, and the player sees a connection error on the online screen. | `[code]` `vercel.json` builds with the default config; the solo-only switch is a build variable; `docs/HOSTING.md` describes the AWS side | High if misconfigured | Verify on the live URL once (create a room and join it from a second device). Add a health check and a friendly "server unreachable, solo and hot-seat still work" message. |
| Rooms are joinable by anyone with the 5-letter code until the game starts. No password, no spectators, no kick other than handing a vanished player to the AI. | `[code]` + `AUDIT.md` items 5, 6 | Medium | Optional room password and a lobby "kick". Spectators are a later item. |
| Single backend instance, rooms in memory. About 12 USD a month, and a restart interrupts all games (snapshots are saved, so they resume). | `[code]` `docs/HOSTING.md` | Low at friends scale | Fine for now. Add an uptime check and an alert. |
| Hot-seat is the right fit for one phone passed around, but each handover curtain needs a tap and the "pass the device" step is easy to skip past. | `[code]` | Low | Fine. Consider hiding the previous player's units during the curtain on slow devices. |

## 6. Mobile and platform (beyond the layout pass)

The layout pass fixed portrait and landscape. What remains is the platform around it.

| Finding | Evidence | Impact | Suggestion |
|---|---|---|---|
| There is no service worker. Every visit re-downloads from the network (about 4.5 MB of build output, 65 asset files), and the game cannot open offline even though solo and hot-seat need no network. | `[measured]` `dist/` size; `[code]` no `serviceWorker` registration | High | Add a small service worker: cache the app shell and art on first load, update in the background. This is also what makes install and push reliable. |
| The 3D map has never been run on a real phone GPU. Software rendering shows correctness, not heat, battery or frame rate. This is the single biggest unknown for "comfortable on mobile". | `[measured]` `PERFORMANCE.md` states it | High | Profile on one low-end Android and one iPhone: frame time, battery per 30 minutes, thermal throttling. Pick the default quality for phones from that (the auto-quality drop exists but starts high). |
| No wake lock: the screen dims during a long AI turn or while the player thinks. | `[code]` none found | Low | `navigator.wakeLock` while a game is on screen. |
| No haptics. A short vibration on "found city", "attack" and "your turn" costs a few lines on Android. | `[code]` none found | Low | Optional setting. |
| Reachability: end turn is bottom-right (good); the menu and diplomacy buttons are top-right (hard for one-handed use on tall phones). | `[inferred]` | Low | Test with real hands. If it hurts, move Menu into the bottom row. |
| Text as small as 10–12 px appears in phone UI (labels, notes, unit stats). | `[code]` `style.css` | Medium | Raise the smallest sizes to 12–13 px on phones; add a "Larger text" setting. |

## 7. Persistence and data safety

| Finding | Evidence | Impact | Suggestion |
|---|---|---|---|
| **One save slot**, stored in localStorage under `jericho.save.v1`. Starting a new game overwrites it. | `[code]` `transport.ts` | High | Three named slots plus the autosave. |
| **A save that can no longer be read fails silently.** The title screen shows "Continue" when the key exists; if parsing or loading throws, the button does nothing. | `[code]` `main.ts` `onContinue`: `if (t) startGame(t)` with no else; `loadSavedGame` returns null on any error | High: the worst possible moment to say nothing | Say what happened, keep the raw save for export, and never delete it automatically. |
| **No save versioning or migration.** The key says `v1` but there is no version inside the data. Any engine change that alters the state shape breaks every existing save. | `[code]` | High as soon as the rules change after launch | Write `{ version, state }`, add a migration table, and add a test that loads a committed old save. |
| A failed write (storage full, private mode) is swallowed. The player believes the game is saved. | `[code]` `save()` catch block is empty | Medium | Surface "could not save" once, and offer "download save". |
| iOS Safari clears script-written storage for sites not used for about 7 days unless installed to the home screen. A player who returns the next weekend may lose the game. | `[inferred]` browser behaviour; verify on a device | Medium | Installed PWAs are exempt, so this rises in value with the service worker work. Also: export/import to a file, and optional cloud save later. |

## 8. Accessibility and inclusivity

| Finding | Evidence | Suggestion |
|---|---|---|
| Civilisations, relations (war red, peace green) and many states are told apart by colour alone. | `[code]` | A colour-blind-safe palette option, and shapes or patterns for war/peace on rank badges and unit flags. |
| English only. Every string is written inline in the TypeScript, so adding a language is a refactor, not a translation job. | `[measured]` no i18n code found | Decide first whether other languages matter. If yes, extract strings before the content grows further. The project already has a Russian legacy README, so there may be an audience. |
| The map is a canvas with a label; the HUD is mostly buttons. About 27 `aria-` attributes exist across the client. A full screen-reader experience of the map is unrealistic, but menus, dialogs, setup and the turn button can be made fully operable by keyboard and assistive tech. | `[measured]` count | Audit the menus and sheets with a screen reader once; fix the labels it reveals. |
| Reduced motion is respected in CSS. Cinematics and animation speed have settings. | `[code]` | Good. Keep. |
| No "larger text" or high-contrast setting. Muted text (`#7f8a9b`) is used for secondary information. | `[code]` | Check contrast numerically and offer a high-contrast toggle. |

## 9. Observability: knowing how the product is doing

**Dropped by the owner: no analytics and no error reporting.** The findings stay for the record. The build number now shows in Menu so a bug report can name a version.

| Finding | Evidence | Suggestion |
|---|---|---|
| **No analytics of any kind.** There is no way to tell how many people start a game, finish one, or quit on the setup screen. | `[measured]` searched for analytics, telemetry, Sentry, gtag, Plausible: none | A privacy-light event counter (a self-hosted or cookieless tool, no personal data, honour Do Not Track). Events: `new_game` (mode, size, difficulty), `first_city` (turn), `turn_10/30/100`, `game_over` (victory type, turn), `quit` (screen), `online_room_created/joined`. |
| **No client error reporting.** The only handler is for the server process. A crash on one phone model is invisible. | `[measured]` no `window.onerror` or `unhandledrejection` in the client | Add both handlers, rate-limited, sending a short message, build id and device class to the same endpoint. |
| Renderer fallbacks (3D to flat) happen silently except for a toast on the player's own screen. | `[code]` | Count them. The share of players on the flat map is the real graphics-support number. |
| No build identifier in the UI. When a player reports a bug there is no version to ask for. | `[code]` | Show the commit hash small in Menu → About. |

## 10. Quality, delivery and engineering health

| Finding | Evidence | Suggestion |
|---|---|---|
| CI runs typecheck, 57 unit tests and the browser tests on the flat map. The 3D path is not in CI. | `[measured]` `.github/workflows/ci.yml` | Add one scheduled job with the 3D section (`E2E_GL=1`) so a renderer break is found within a day. |
| The new phone-size checks run in CI (four viewports). Visual regressions (overlapping HUD, clipped text) are not caught, because the checks assert geometry, not appearance. | `[measured]` `scripts/e2e.ts` | Add screenshot comparisons for five key screens at two viewports. |
| No linter or formatter configuration. | `[measured]` none found | Add one. It is cheap now and expensive later. |
| `gamescreen.ts` is about 1,300 lines and owns input, camera, selection, modals and chat. Two renderers (3D, flat) must be kept in step. | `[measured]` line counts | Split input and camera out when next touching them. Consider retiring the flat renderer once 3D is proven on phones, or keep it deliberately as the low-power mode and test it as such. |
| Deploy documentation names a feature branch (`claude/determined-goodall-9vhnhg`) as the deploy branch. | `[code]` `docs/HOSTING.md` | Point the docs at `main`. |
| The deployed site depends on Google Fonts at runtime. | `[code]` `index.html` | See direction 11. |

## 11. Legal, privacy and brand

| Finding | Evidence | Suggestion |
|---|---|---|
| Fonts are loaded from Google's servers, which sends each visitor's address to Google. Some European regulators and courts treat that as a privacy problem, and it adds a failure point offline. | `[code]` `index.html` | Self-host Cinzel and Source Sans 3 (both are open-licence). It also helps the offline work. |
| Much of the art (scenes, leaders, sprites) is described in the code as generated artwork. | `[code]` `style.css` comment | Record where each asset came from and under what terms, before any public launch or store listing. |
| The README and manifest describe the game as "Civilization-style". Descriptive use is usually fine; using the word in a store title or logo is not. | `[code]` | Keep it to descriptive text, never in the name or icon. |
| No privacy statement, which is needed the moment analytics or push exist. | `[code]` | One short page: what is stored on the device, what is sent, and to whom. |
| Online rooms keep a player name and a session token on the server. | `[code]` | Document retention (rooms are swept after 20 minutes in the lobby and 6 hours after start). |

## 12. Discoverability and growth

| Finding | Evidence | Suggestion |
|---|---|---|
| The page has a title but no description, no Open Graph or Twitter card tags, and no `robots` or sitemap. Shared links and search results are plain. | `[measured]` `index.html` | Add them, with a good 1200×630 image (the existing scene art is a fit). |
| The title screen and README list features but there is no screenshot or short video to show. | `[code]` | Record a 20-second clip and four screenshots (the phone ones now look good). |
| No "install this game" prompt, and no ways to find the game besides the link. | `[code]` | Offer install once after the first finished game, not on the first visit. |
| A monetisation or sustainability decision has not been made, and it does not need to be yet. | — | Decide the intent: hobby for friends, open-source showcase, or product. Hosting is about 12 USD a month; that is the only recurring cost today. |

## Roadmap

Effort: S = a day or two, M = about a week, L = several weeks. Impact is for a phone-first, friends-first product.

### Now (next two weeks)
| # | Item | Direction | Effort | Why now |
|---|---|---|---|---|
| 1 | ~~Event counter and client error reporting~~ dropped; build id in Menu done | 9 | S | Decision: no analytics. |
| 2 | Fix the silent failure on "Continue"; add save version field; show a message when a write fails | 7 | S | It can destroy a game and says nothing. |
| 3 | Open Graph tags, native share for invites, check online works on the live site | 5, 12 | S | The core promise is playing with friends. |
| 4 | Manifest to `orientation: any`, `display: standalone`; self-host fonts | 1, 11 | S | One-line fixes with a visible effect. |
| 5 | Tap-to-inspect tile; phone version of the getting-started hint | 1 | M | New phone players currently see neither. |
| 6 | Real-device check: one low-end Android, one iPhone, 30 minutes each | 6 | S | The biggest unknown for "comfortable on mobile". |

### Next (a month or two)
| # | Item | Direction | Effort |
|---|---|---|---|
| 7 | Service worker (offline app shell), then install prompt | 6, 12 | M |
| 8 | Save slots, export/import, migration test | 7 | M |
| 9 | Quick start, shorter game length, phone default | 1, 2 | M |
| 10 | One-step undo, disband confirm, "fortify all idle" | 2 | M |
| 11 | Chronicle (local stats) and daily seed | 4 | M |
| 12 | Web Push for "your turn" and a long asynchronous timer | 5 | L |
| 13 | Colour-blind palette, larger-text option, contrast check | 8 | M |

### Later (needs a decision or playtests first)
| # | Item | Direction | Effort |
|---|---|---|---|
| 14 | Random events, then barbarians, then trade and great people | 3 | L each |
| 15 | Siege rebalance so conquest finishes games | 3 | M |
| 16 | Guided tutorial scenario | 1 | M |
| 17 | Strings extracted for translation | 8 | L |
| 18 | Spectators, room passwords | 5 | M |
| 19 | End-of-game timeline, then replay | 4 | M, L |

## KPIs to put on the dashboard once events exist

- **Activation:** share of new games where a first city is founded by turn 8.
- **Early retention:** share that reach turn 30; share that start a second game within 7 days.
- **Completion:** share of games that reach a victory screen; median turns played per session.
- **Social:** rooms created, rooms that start with two or more humans, friends per room.
- **Platform health:** share on the flat map, crash-free sessions, median load time on mobile.

## Questions only real players can answer

1. Does a 120-turn game fit how people actually play on a phone, or is 60 the real answer?
2. Do friends finish an online game in one sitting, or do they need to leave and return?
3. Where do first-time players stop: setup, first city, first research, or around turn 15?
4. Does the 3D map hold 30 fps on a three-year-old Android, and does it drain the battery?
5. Is conquest something players want (and so the siege retune matters), or is the peaceful path the real game?
6. Which of the setup options does anyone change from the defaults?

## How this audit was done

Read `docs/AUDIT.md`, `docs/FUN_AUDIT.md`, `docs/PERFORMANCE.md` and `docs/HOSTING.md`; counted game content from `src/shared/data.ts`; read the client entry, transport, menus, HUD and game screen code; searched for analytics, error handling, localisation, undo and service-worker support; read the manifest, `index.html` and CI workflows; and, in the same session, ran the game at four phone sizes in a desktop browser. No real devices and no real players were involved.
