import { CIVS, CIV_BY_ID, MAP_SIZES } from '../../shared/data';
import { Game } from '../../shared/game';
import { buildView } from '../../shared/view';
import { ViewMap } from '../../shared/viewpath';
import type { AiLevel } from '../../shared/types';
import type { RoomInfo, RoomSettings, RoomSlot } from '../../shared/protocol';
import { Animator } from '../anim';
import { Camera, MapRenderer } from '../render';
import { btn, clear, h, put } from './dom';
import type { RemoteTransport } from '../transport';

export interface SoloConfig {
  name: string;
  civ: string | null;
  opponents: number;
  aiLevel: AiLevel;
  size: keyof typeof MAP_SIZES | 'auto';
  speed: 'standard' | 'quick';
  seed: number | null;
  turnLimit: number;
  startAtWar: boolean;
  /** extra local humans (hot-seat), names */
  friends: string[];
}

export function savedName(): string {
  try { return localStorage.getItem('jericho.name') || ''; } catch { return ''; }
}
export function saveName(n: string): void {
  try { localStorage.setItem('jericho.name', n); } catch { /* ignore */ }
}

/** Paint a generated world behind the menus. */
export function paintBackdrop(canvas: HTMLCanvasElement): void {
  try {
    const game = Game.create({ seed: Math.floor(Math.random() * 1e6), size: 'medium', players: [{ name: 'a', ai: true }, { name: 'b', ai: true }, { name: 'c', ai: true }] });
    for (let i = 0; i < 18; i++) game.processTurn();
    for (const t of game.tiles) t.explored = 0xff;
    for (const t of game.tiles) t.visible = 0xff;
    const view = buildView(game, 0);
    const cam = new Camera();
    cam.setWorld(view.settings.width, view.settings.height);
    const r = new MapRenderer(canvas, cam, new Animator());
    r.resize();
    cam.zoom = Math.max(cam.w / (cam.worldW + 60), cam.h / (cam.worldH + 60)) * 1.02;
    cam.x = cam.worldW / 2;
    cam.y = cam.worldH / 2;
    view.units = [];
    r.draw({
      view, vm: new ViewMap(view), selectedUnit: null, selectedCity: null, hover: null, reach: null, path: null, pathTurns: 0,
      attackTargets: new Set(), showYields: false, showGrid: false, workedTiles: null, now: 0,
    });
  } catch (e) {
    console.warn('backdrop failed', e);
  }
}

export function screen(withBackdrop = true): { el: HTMLElement; content: HTMLElement } {
  const content = h('div', { class: 'content' });
  const el = h('div', { class: `screen ${withBackdrop ? 'bg-map' : ''}` });
  if (withBackdrop) {
    const c = h('canvas', { class: 'bg' });
    el.append(c);
    setTimeout(() => paintBackdrop(c), 30);
  }
  el.append(content);
  return { el, content };
}

// ------------------------------------------------------------------ title
export interface TitleHandlers {
  continueGame: boolean;
  rejoin: string | null;
  onNew: () => void;
  onHotseat: () => void;
  onOnline: () => void;
  onContinue: () => void;
  onRejoin: () => void;
  onHelp: () => void;
}
export function titleScreen(hd: TitleHandlers): HTMLElement {
  const { el, content } = screen();
  const list = h('div', { class: 'menu-list' });
  if (hd.continueGame) list.append(btn(h('span', null, '▶ Continue game ', h('small', null, '(autosave)')), hd.onContinue, 'primary'));
  if (hd.rejoin) list.append(btn(h('span', null, `🔗 Rejoin online game `, h('small', null, hd.rejoin)), hd.onRejoin, 'primary'));
  list.append(
    btn('⚔ New game vs computer', hd.onNew, hd.continueGame || hd.rejoin ? '' : 'primary'),
    btn('🌍 Play online with friends', hd.onOnline),
    btn('🪑 Hot-seat (same device)', hd.onHotseat),
    btn('📖 How to play', hd.onHelp, 'ghost'),
  );
  put(content, h('div', { class: 'title-wrap' },
    h('h1', { class: 'logo' }, 'JERICHO'),
    h('p', { class: 'tagline' }, 'Found cities. Discover the ages. Outwit your friends.'),
    list,
    h('p', { class: 'muted', style: { fontSize: '12px' } }, 'A turn-based strategy game for 1–8 players · runs in your browser'),
  ));
  return el;
}

// ------------------------------------------------------------------ setup (solo / hot-seat)
export function setupScreen(mode: 'solo' | 'hotseat', onStart: (c: SoloConfig) => void, onBack: () => void): HTMLElement {
  const { el, content } = screen();
  let civ: string | null = null;
  const nameIn = h('input', { value: savedName(), placeholder: 'Your name', maxLength: 20 });
  const opp = h('input', { type: 'range', min: '1', max: mode === 'solo' ? '7' : '6', value: mode === 'solo' ? '3' : '1' });
  const oppLabel = h('b', null, opp.value);
  opp.addEventListener('input', () => { oppLabel.textContent = opp.value; });
  const level = h('select', null, h('option', { value: 'easy' }, 'Easy — relaxed neighbours'), h('option', { value: 'normal', selected: true }, 'Normal'), h('option', { value: 'hard' }, 'Hard — they get a production bonus'));
  const size = h('select', null, h('option', { value: 'auto' }, 'Automatic'), ...Object.entries(MAP_SIZES).map(([k, v]) => h('option', { value: k }, v.label)));
  const speed = h('select', null, h('option', { value: 'standard' }, 'Standard (200 turns)'), h('option', { value: 'quick' }, 'Quick (120 turns, cheaper everything)'));
  const seed = h('input', { placeholder: 'Random', inputMode: 'numeric' });
  const stance = h('select', null, h('option', { value: 'peace' }, 'Start at peace (declare war when ready)'), h('option', { value: 'war' }, 'Start at war (everyone is hostile)'));
  const friendInputs = [1, 2, 3].map((i) => h('input', { placeholder: `Player ${i + 1} name`, maxLength: 20, class: mode === 'hotseat' ? '' : 'hidden' }));
  const grid = h('div', { class: 'civ-grid' });
  const draw = () => {
    clear(grid);
    grid.append(h('button', { class: `civ-card ${civ === null ? 'sel' : ''}`, type: 'button', on: { click: () => { civ = null; draw(); } } }, h('b', null, '🎲 Random'), h('span', null, 'Let fate decide')));
    for (const c of CIVS) {
      grid.append(h('button', { class: `civ-card ${civ === c.id ? 'sel' : ''}`, type: 'button', on: { click: () => { civ = c.id; draw(); } } },
        h('b', null, h('span', { class: 'dot', style: { background: c.color } }), c.name), h('span', null, c.leader), h('span', { style: { color: 'var(--gold2)' } }, c.bonus)));
    }
  };
  draw();
  const start = () => {
    const name = nameIn.value.trim() || 'Player';
    saveName(name);
    const friends = mode === 'hotseat' ? friendInputs.map((f, i) => f.value.trim() || `Player ${i + 2}`).slice(0, 3) : [];
    const humanCount = 1 + (mode === 'hotseat' ? Number(friendCount.value) : 0);
    onStart({
      name, civ, opponents: Number(opp.value), aiLevel: level.value as AiLevel, size: size.value as SoloConfig['size'], speed: speed.value as SoloConfig['speed'],
      seed: seed.value.trim() ? Number(seed.value) || null : null, turnLimit: speed.value === 'quick' ? 120 : 200, startAtWar: stance.value === 'war', friends: friends.slice(0, humanCount - 1),
    });
  };
  const friendCount = h('select', { class: mode === 'hotseat' ? '' : 'hidden' }, h('option', { value: '1' }, '2 humans'), h('option', { value: '2' }, '3 humans'), h('option', { value: '3' }, '4 humans'));
  friendCount.addEventListener('change', () => friendInputs.forEach((f, i) => f.classList.toggle('hidden', i >= Number(friendCount.value))));
  friendInputs.forEach((f, i) => f.classList.toggle('hidden', mode !== 'hotseat' || i >= 1));
  put(content, h('div', { class: 'card setup' },
    h('h2', null, mode === 'solo' ? 'New game' : 'Hot-seat game'),
    h('div', { class: 'grid2' },
      h('label', { class: 'field' }, mode === 'solo' ? 'Your name' : 'Player 1', nameIn),
      mode === 'hotseat' ? h('label', { class: 'field' }, 'Humans', friendCount) : null,
    ),
    mode === 'hotseat' ? h('div', { class: 'grid2' }, ...friendInputs) : null,
    h('div', null, h('div', { class: 'sec-title' }, mode === 'solo' ? 'Your civilisation' : 'Player 1’s civilisation (others are random)'), grid),
    h('div', { class: 'grid2' },
      h('label', { class: 'field' }, h('span', null, 'Computer opponents: ', oppLabel), opp),
      h('label', { class: 'field' }, 'Difficulty', level),
      h('label', { class: 'field' }, 'Map size', size),
      h('label', { class: 'field' }, 'Game speed', speed),
      h('label', { class: 'field' }, 'Diplomacy', stance),
      h('label', { class: 'field' }, 'Map seed (optional)', seed),
    ),
    h('div', { class: 'row' }, btn('← Back', onBack), h('span', { class: 'spacer' }), btn('Start game →', start, 'primary big')),
  ));
  return el;
}

// ------------------------------------------------------------------ online entry
export function onlineEntryScreen(initialCode: string, onCreate: (name: string) => void, onJoin: (code: string, name: string) => void, onBack: () => void, error: string | null): HTMLElement {
  const { el, content } = screen();
  const nameIn = h('input', { value: savedName(), placeholder: 'Your name', maxLength: 20 });
  const codeIn = h('input', { value: initialCode, placeholder: 'ABCD', maxLength: 4, style: { textTransform: 'uppercase', letterSpacing: '0.4em', fontSize: '22px', textAlign: 'center', width: '140px' } });
  const name = () => {
    const n = nameIn.value.trim() || 'Player';
    saveName(n);
    return n;
  };
  put(content, h('div', { class: 'card setup', style: { width: 'min(520px,100%)' } },
    h('h2', null, 'Play online'),
    error ? h('div', { class: 'error-banner' }, error) : null,
    h('label', { class: 'field' }, 'Your name', nameIn),
    h('div', { class: 'card', style: { background: 'var(--panel2)' } },
      h('div', { class: 'sec-title' }, 'Host a game'),
      h('p', { class: 'muted' }, 'Create a room, then send the invite link or 4-letter code to your friends.'),
      btn('Create a room', () => onCreate(name()), 'primary')),
    h('div', { class: 'card', style: { background: 'var(--panel2)' } },
      h('div', { class: 'sec-title' }, 'Join a friend'),
      h('div', { class: 'row' }, codeIn, btn('Join', () => onJoin(codeIn.value.toUpperCase().trim(), name()), 'primary'))),
    h('div', { class: 'row' }, btn('← Back', onBack)),
  ));
  codeIn.addEventListener('keydown', (e) => { if (e.key === 'Enter') onJoin(codeIn.value.toUpperCase().trim(), name()); });
  return el;
}

// ------------------------------------------------------------------ lobby
export function lobbyScreen(rt: RemoteTransport, onLeave: () => void): { el: HTMLElement; update: (room: RoomInfo) => void; chat: (from: string, text: string) => void } {
  const { el, content } = screen();
  const wrap = h('div', { class: 'lobby' });
  content.append(wrap);
  const chatLines: Array<{ from: string; text: string }> = [];
  const chatLog = h('div', { class: 'chat-log' });
  const chatIn = h('input', { placeholder: 'Chat with your friends…', maxLength: 240, style: { width: '100%' } });
  chatIn.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && chatIn.value.trim()) {
      rt.sendChat(chatIn.value.trim());
      chatIn.value = '';
    }
  });
  const renderChat = () => {
    clear(chatLog);
    for (const l of chatLines.slice(-40)) chatLog.append(h('div', null, h('b', null, l.from + ': '), l.text));
    chatLog.scrollTop = chatLog.scrollHeight;
  };
  const update = (room: RoomInfo) => {
    const host = room.you.host;
    clear(wrap);
    const link = `${location.origin}${location.pathname}?room=${room.code}`;
    const send = (m: Parameters<RemoteTransport['raw']>[0]) => rt.raw(m);

    // players
    const players = h('div', { style: { display: 'grid', gap: '6px' } });
    for (const s of room.slots) {
      if (s.kind === 'closed' && !host) continue;
      players.append(slotRow(room, s, host, send));
    }
    const left = h('div', { class: 'card', style: { display: 'grid', gap: '10px', alignContent: 'start' } },
      h('div', { class: 'row' }, h('h2', { style: { margin: '0', fontFamily: 'var(--serif)' } }, 'Game lobby'), h('span', { class: 'spacer' }), btn('Leave', onLeave, 'small')),
      players,
      h('div', { class: 'muted', style: { fontSize: '12px' } }, host ? 'Open a seat for a friend, or fill it with a computer opponent. At least two seats are needed.' : 'Waiting for the host to start the game…'),
    );

    // settings
    const s = room.settings;
    const sel = <K extends keyof RoomSettings>(label: string, key: K, options: Array<[string, string]>) => {
      const select = h('select', { disabled: !host }, ...options.map(([v, l]) => h('option', { value: v, selected: String(s[key]) === v }, l)));
      select.addEventListener('change', () => send({ t: 'settings', settings: { [key]: key === 'turnTimer' || key === 'turnLimit' ? Number(select.value) : key === 'startAtWar' ? select.value === 'true' : select.value } as Partial<RoomSettings> }));
      return h('label', { class: 'field' }, label, select);
    };
    const right = h('div', { style: { display: 'grid', gap: '12px', alignContent: 'start' } },
      h('div', { class: 'card', style: { textAlign: 'center' } },
        h('div', { class: 'muted' }, 'Room code'), h('div', { class: 'code' }, room.code),
        btn('📋 Copy invite link', () => { void navigator.clipboard?.writeText(link); }, 'small'),
        h('div', { class: 'muted', style: { fontSize: '11px', marginTop: '6px', wordBreak: 'break-all' } }, link)),
      h('div', { class: 'card', style: { display: 'grid', gap: '10px' } },
        h('div', { class: 'sec-title' }, 'Game settings'),
        sel('Map size', 'size', Object.entries(MAP_SIZES).map(([k, v]) => [k, v.label])),
        sel('Speed', 'speed', [['standard', 'Standard'], ['quick', 'Quick (cheaper)']]),
        sel('Turn limit', 'turnLimit', [['100', '100'], ['120', '120'], ['150', '150'], ['200', '200'], ['300', '300']]),
        sel('Diplomacy', 'startAtWar', [['false', 'Start at peace'], ['true', 'Start at war']]),
        sel('Turn timer', 'turnTimer', [['0', 'Off — wait for everyone'], ['60', '1 minute'], ['120', '2 minutes'], ['180', '3 minutes'], ['300', '5 minutes']]),
        host ? btn('Start game ▶', () => send({ t: 'start' }), 'primary big') : null),
      h('div', { class: 'card' }, h('div', { class: 'sec-title' }, 'Chat'), chatLog, h('div', { style: { height: '6px' } }), chatIn),
    );
    wrap.append(left, right);
    renderChat();
  };
  return { el, update, chat: (from, text) => { chatLines.push({ from, text }); renderChat(); } };
}

function slotRow(room: RoomInfo, s: RoomSlot, host: boolean, send: (m: Parameters<RemoteTransport['raw']>[0]) => void): HTMLElement {
  const me = room.you.slot === s.slot;
  const civ = s.civ ? CIV_BY_ID[s.civ] : null;
  const dot = h('span', { class: 'dot', style: { background: civ ? civ.color : '#556' } });
  const nameEl = h('div', null,
    h('b', null, s.kind === 'open' ? 'Open seat' : s.kind === 'closed' ? 'Closed' : s.name || 'Player'),
    s.kind === 'human' ? h('span', { class: 'muted', style: { marginLeft: '6px', fontSize: '12px' } }, `${s.host ? 'host · ' : ''}${me ? 'you' : s.connected ? 'online' : 'away'}`) : null,
    s.kind === 'ai' ? h('span', { class: 'muted', style: { marginLeft: '6px', fontSize: '12px' } }, 'computer') : null);
  const civSelect = h('select', { disabled: !(me || (host && s.kind === 'ai')), title: 'Civilisation' },
    h('option', { value: '' }, '🎲 Random'),
    ...CIVS.map((c) => h('option', { value: c.id, selected: s.civ === c.id, disabled: room.slots.some((o) => o.slot !== s.slot && o.civ === c.id && (o.kind === 'human' || o.kind === 'ai')) }, c.name)));
  civSelect.addEventListener('change', () => send({ t: 'civ', civ: civSelect.value || null, slot: s.slot }));
  if (s.kind === 'open' || s.kind === 'closed') civSelect.classList.add('hidden');
  const controls = h('div', { class: 'row' });
  if (host && !s.host) {
    const kind = h('select', null,
      h('option', { value: 'open', selected: s.kind === 'open' }, 'Open'),
      h('option', { value: 'ai', selected: s.kind === 'ai' }, 'Computer'),
      h('option', { value: 'closed', selected: s.kind === 'closed' }, 'Closed'),
      s.kind === 'human' ? h('option', { value: 'human', selected: true }, 'Friend') : null);
    kind.addEventListener('change', () => { if (kind.value !== 'human') send({ t: 'slot', slot: s.slot, kind: kind.value as 'ai' | 'open' | 'closed' }); });
    controls.append(kind);
    if (s.kind === 'ai') {
      const lvl = h('select', null, ...(['easy', 'normal', 'hard'] as AiLevel[]).map((l) => h('option', { value: l, selected: s.aiLevel === l }, l)));
      lvl.addEventListener('change', () => send({ t: 'slot', slot: s.slot, kind: 'ai', aiLevel: lvl.value as AiLevel }));
      controls.append(lvl);
    }
  }
  return h('div', { class: `slot ${me ? 'me' : ''} ${s.kind === 'open' || s.kind === 'closed' ? 'empty' : ''}` }, dot, nameEl, civSelect, controls);
}
