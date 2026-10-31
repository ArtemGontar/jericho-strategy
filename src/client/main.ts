import { applyAccessibility } from './access';
import { startAmbience } from './audio';
import './style.css';
import { Game, type PlayerSlot } from '../shared/game';
import type { ClientMsg, RoomInfo } from '../shared/protocol';
import { GameScreen } from './gamescreen';
import { LocalTransport, RemoteTransport, hasSavedGame, loadSavedGame } from './transport';
import { clear, h, installMotion } from './ui/dom';
import { resetGuide } from './ui/guide';
import { showHelp } from './ui/screens';
import { lobbyScreen, onlineEntryScreen, savedGamesScreen, savedName, setupScreen, titleScreen, type SoloConfig } from './ui/menus';

const app = document.getElementById('app')!;
// browsers only allow sound after a gesture: start the ambience on the first click or key press
const wake = () => {
  startAmbience();
  window.removeEventListener('pointerdown', wake);
  window.removeEventListener('keydown', wake);
};
window.addEventListener('pointerdown', wake);
window.addEventListener('keydown', wake);
const ONLINE_KEY = 'jericho.online';

let current: GameScreen | null = null;
let currentTransport: RemoteTransport | null = null;

declare global {
  interface Window {
    __jericho?: { screen: GameScreen | null; transport: RemoteTransport | null };
  }
}
function publish(): void {
  window.__jericho = { screen: current, transport: currentTransport };
}

function mount(el: HTMLElement): void {
  clear(app);
  app.append(el);
}

function leaveGame(): void {
  if (current) {
    if (current.transport.kind === 'remote') (current.transport as RemoteTransport).raw({ t: 'leave' });
    current.destroy();
    current = null;
  }
  currentTransport = null;
  publish();
  showTitle();
}

function rememberOnline(room: RoomInfo, name: string): void {
  try { localStorage.setItem(ONLINE_KEY, JSON.stringify({ code: room.code, token: room.you.token, name })); } catch { /* ignore */ }
}
function forgetOnline(): void {
  try { localStorage.removeItem(ONLINE_KEY); } catch { /* ignore */ }
}
function savedOnline(): { code: string; token: string; name: string } | null {
  try {
    const raw = localStorage.getItem(ONLINE_KEY);
    return raw ? (JSON.parse(raw) as { code: string; token: string; name: string }) : null;
  } catch {
    return null;
  }
}

// ----------------------------------------------------------------------------- screens
function showTitle(): void {
  const online = savedOnline();
  mount(titleScreen({
    continueGame: hasSavedGame(),
    savedGames: true, // always: it is also where a game from another device is received
    onSaved: () => showSavedGames(),
    // one tap: a random civilisation against two computers on a small map, stopping at turn 60
    onQuick: () => startLocal({ name: savedName() || 'Player', civ: null, opponents: 2, aiLevel: 'normal', size: 'small', speed: 'quick', seed: null, turnLimit: 60, startAtWar: false, scienceVictory: true, aggression: 'normal', mapStyle: 'continent', events: 'gentle', autoFound: true, friends: [] }, 'solo'),
    rejoin: online ? online.code : null,
    onNew: () => showSetup('solo'),
    onHotseat: () => showSetup('hotseat'),
    onOnline: () => showOnlineEntry(''),
    onContinue: () => {
      const r = loadSavedGame();
      if (r.ok) startGame(r.transport);
      else showSavedGames(r.error);
    },
    onRejoin: () => {
      if (online) void connectOnline(() => ({ t: 'join', code: online.code, name: online.name, token: online.token }), online.name);
    },
    onHelp: () => {
      // lightweight help without a running game
      const fake = { view: { settings: { turnLimit: 200 } }, closeModal: () => back.remove() } as unknown as GameScreen;
      const back = h('div', { class: 'overlay', style: { position: 'fixed' }, on: { click: (e) => { if (e.target === back) back.remove(); } } }, showHelp(fake));
      app.append(back);
    },
  }));
}

function showSavedGames(message: string | null = null): void {
  mount(savedGamesScreen((id) => {
    const r = loadSavedGame(id);
    if (r.ok) startGame(r.transport);
    else showSavedGames(r.error);
  }, showTitle, message));
}

function showSetup(mode: 'solo' | 'hotseat'): void {
  mount(setupScreen(mode, (cfg) => startLocal(cfg, mode), showTitle));
}

function showOnlineEntry(code: string, error: string | null = null): void {
  mount(onlineEntryScreen(
    code,
    (name) => void connectOnline(() => ({ t: 'create', name }), name),
    (c, name) => {
      if (c.length !== 5) return showOnlineEntry(c, 'Enter the 5-letter room code.');
      void connectOnline(() => ({ t: 'join', code: c, name }), name);
    },
    showTitle,
    error,
  ));
}

// ----------------------------------------------------------------------------- local games
function startLocal(cfg: SoloConfig, mode: 'solo' | 'hotseat'): void {
  const humans = [cfg.name, ...(mode === 'hotseat' ? cfg.friends : [])];
  const players: PlayerSlot[] = humans.map((name, i) => ({ name, civ: i === 0 ? cfg.civ ?? undefined : undefined, ai: false }));
  for (let i = 0; i < cfg.opponents; i++) players.push({ name: 'Computer', ai: true, aiLevel: cfg.aiLevel });
  const n = players.length;
  const size = cfg.size === 'auto' ? undefined : cfg.size;
  const game = Game.create({ seed: cfg.seed ?? undefined, size: size ?? (n <= 3 ? 'small' : n <= 5 ? 'medium' : 'large'), speed: cfg.speed, turnLimit: cfg.turnLimit, startAtWar: cfg.startAtWar, allowScience: cfg.scienceVictory, aggression: cfg.aggression, mapStyle: cfg.mapStyle, events: cfg.events, autoFound: cfg.autoFound, players });
  game.players.forEach((p) => {
    if (p.ai) p.name = `${game.civOf(p.id).leader} (AI)`;
  });
  const t = new LocalTransport(game, humans.map((_, i) => i));
  t.save();
  resetGuide();
  startGame(t);
}

function startGame(transport: LocalTransport | RemoteTransport, extra: Partial<ConstructorParameters<typeof GameScreen>[2]> = {}): void {
  if (current) current.destroy();
  const gs = new GameScreen(app, transport, { onExit: leaveGame, ...extra });
  current = gs;
  if (transport.kind === 'remote') currentTransport = transport as RemoteTransport;
  publish();
  // GameScreen appended itself to #app; remove menu screens
  Array.from(app.children).forEach((c) => { if (c !== gs.root) c.remove(); });
}

// ----------------------------------------------------------------------------- online
function hostActions(rt: RemoteTransport) {
  return { forceEnd: () => rt.raw({ t: 'forceEnd' }), takeover: (slot: number) => rt.raw({ t: 'takeover', slot }), rematch: () => rt.raw({ t: 'rematch' }) };
}

async function connectOnline(initial: () => ClientMsg, name: string): Promise<void> {
  let joined: RoomInfo | null = null;
  const rt = new RemoteTransport(RemoteTransport.defaultUrl(), () => (joined ? { t: 'join', code: joined.code, name, token: joined.you.token } : initial()));
  let lobby: ReturnType<typeof lobbyScreen> | null = null;
  let playing = false;
  const earlyChat: Array<{ from: string; text: string }> = [];

  rt.onError = (msg) => {
    if (!joined) {
      rt.close();
      showOnlineEntry('', msg);
      return;
    }
    if (current) current.toast(msg, 'bad');
    else if (lobby) alert(msg);
  };
  rt.onChat = (from, _slot, text) => {
    if (current && playing) current.addChat(from, _slot, text);
    else if (lobby) lobby.chat(from, text);
    else earlyChat.push({ from, text });
  };
  rt.onRoom = (room) => {
    joined = room;
    rememberOnline(room, name);
    if (room.phase === 'lobby') {
      if (playing) {
        // host started a rematch: back to the lobby with the same connection
        playing = false;
        current?.destroy(true);
        current = null;
        lobby = null;
        publish();
      }
      if (!lobby) {
        lobby = lobbyScreen(rt, () => {
          rt.raw({ t: 'leave' });
          rt.close();
          forgetOnline();
          showTitle();
        });
        mount(lobby.el);
        for (const l of earlyChat) lobby.chat(l.from, l.text);
      }
      lobby.update(room);
    } else if (!playing) {
      playing = true;
      startGame(rt, {
        invite: { code: room.code },
        handoff: () => {
          const o = savedOnline();
          return o ? `${location.origin}${location.pathname}?room=${o.code}&seat=${o.token}&name=${encodeURIComponent(o.name)}` : null;
        },
        sendChat: (text) => rt.sendChat(text),
        hostActions: room.you.host ? hostActions(rt) : null,
      });
    } else if (current && room.you.host) {
      current.opts.hostActions = hostActions(rt);
    }
  };
  try {
    await rt.connect();
  } catch (e) {
    showOnlineEntry('', (e as Error).message);
  }
}

// ----------------------------------------------------------------------------- boot
applyAccessibility();
installMotion();
const params = new URLSearchParams(location.search);
const roomParam = params.get('room');
const seatParam = params.get('seat');
const previous = savedOnline();
if (roomParam && seatParam && /^[a-f0-9]{8,64}$/i.test(seatParam)) {
  // a link from our own other device: take the seat over, and take the secret out of the address bar
  const code = roomParam.toUpperCase().slice(0, 6);
  const who = (params.get('name') ?? '').slice(0, 20) || 'Player';
  history.replaceState(null, '', location.pathname);
  void connectOnline(() => ({ t: 'join', code, name: who, token: seatParam }), who);
} else if (roomParam && previous && previous.code === roomParam.toUpperCase().slice(0, 6)) {
  // opened our own invite link again (e.g. after a refresh): slip straight back into the game
  void connectOnline(() => ({ t: 'join', code: previous.code, name: previous.name, token: previous.token }), previous.name);
} else if (roomParam) showOnlineEntry(roomParam.toUpperCase().slice(0, 6));
else showTitle();
void savedName;

// offline support: the first visit stores what the game needs, so later visits open at once and solo play needs no network
if ('serviceWorker' in navigator && import.meta.env.PROD) {
  window.addEventListener('load', () => { void navigator.serviceWorker.register('sw.js').catch(() => undefined); });
}
