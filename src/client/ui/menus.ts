import { CIVS, CIV_BY_ID, MAP_SIZES } from '../../shared/data';
import { Game } from '../../shared/game';
import { buildView } from '../../shared/view';
import { ViewMap } from '../../shared/viewpath';
import type { AiLevel } from '../../shared/types';
import type { RoomInfo, RoomSettings, RoomSlot } from '../../shared/protocol';
import { Animator } from '../anim';
import { Camera, MapRenderer } from '../render';
import { GLRenderer } from '../gl/glrenderer';
import { btn, clear, h, put } from './dom';
import { emblem } from './hud';
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
  scienceVictory: boolean;
  aggression: 'calm' | 'normal' | 'fierce';
  mapStyle: 'continent' | 'islands';
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
let backdropCache: HTMLCanvasElement | null = null;

/** One 3D render of a finished world, kept as a picture and reused behind every menu. */
function snapshotBackdrop(view: ReturnType<typeof buildView>, w: number, h: number): HTMLCanvasElement | null {
  if (backdropCache) return backdropCache;
  if (!GLRenderer.supported() || new URLSearchParams(location.search).has('flat')) return null;
  try {
    const dpr = Math.min(1.5, window.devicePixelRatio || 1);
    const glCanvas = document.createElement('canvas');
    const overlay = document.createElement('canvas');
    const cam = new Camera();
    const gr = new GLRenderer(glCanvas, overlay, cam, new Animator());
    gr.resizeTo(w, h, dpr);
    cam.setWorld(view.settings.width, view.settings.height);
    cam.zoom = Math.max(w / (cam.worldW + 60), h / ((cam.worldH + 60) * cam.tilt)) * 1.05;
    cam.x = cam.worldW / 2;
    cam.y = cam.worldH / 2;
    const st = {
      view, vm: new ViewMap(view), selectedUnit: null, selectedCity: null, hover: null, reach: null, path: null, pathTurns: 0,
      attackTargets: new Set<number>(), showYields: false, showGrid: false, workedTiles: null, now: 4000,
    };
    gr.draw(st);
    gr.draw({ ...st, now: 4016 });
    const out = document.createElement('canvas');
    out.width = Math.floor(w * dpr);
    out.height = Math.floor(h * dpr);
    const ctx = out.getContext('2d')!;
    ctx.drawImage(glCanvas, 0, 0, out.width, out.height);
    ctx.drawImage(overlay, 0, 0, out.width, out.height);
    gr.dispose();
    backdropCache = out;
    return out;
  } catch (e) {
    console.warn('3D backdrop failed, using the flat one', e);
    return null;
  }
}

export function paintBackdrop(canvas: HTMLCanvasElement): void {
  try {
    if (backdropCache) {
      canvas.width = backdropCache.width;
      canvas.height = backdropCache.height;
      canvas.getContext('2d')!.drawImage(backdropCache, 0, 0);
      return;
    }
    const game = Game.create({ seed: Math.floor(Math.random() * 1e6), size: 'medium', players: [{ name: 'a', ai: true }, { name: 'b', ai: true }, { name: 'c', ai: true }] });
    for (let i = 0; i < 18; i++) game.processTurn();
    for (const t of game.tiles) t.explored = 0xff;
    for (const t of game.tiles) t.visible = 0xff;
    const view = buildView(game, 0);
    view.units = [];
    view.hut = view.hut.map(() => 0);
    const w = Math.max(640, window.innerWidth);
    const h = Math.max(400, window.innerHeight);
    const snap = snapshotBackdrop(view, w, h);
    if (snap) {
      canvas.width = snap.width;
      canvas.height = snap.height;
      canvas.getContext('2d')!.drawImage(snap, 0, 0);
      return;
    }
    const cam = new Camera();
    cam.setWorld(view.settings.width, view.settings.height);
    const r = new MapRenderer(canvas, cam, new Animator());
    r.resize();
    cam.zoom = Math.max(cam.w / (cam.worldW + 60), cam.h / (cam.worldH + 60)) * 1.02;
    cam.x = cam.worldW / 2;
    cam.y = cam.worldH / 2;
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
  const el = h('div', { class: 'screen' });
  if (withBackdrop) {
    const c = h('canvas', { class: 'bg' });
    el.append(c);
    setTimeout(() => paintBackdrop(c), 30);
  }
  el.append(content);
  return { el, content };
}

/** Joined button group where exactly one option is active. */
function segmented<T extends string>(options: Array<[T, string]>, initial: T, onChange?: (v: T) => void): { el: HTMLElement; get: () => T } {
  let value = initial;
  const el = h('div', { class: 'seg' });
  const draw = () => {
    clear(el);
    for (const [v, label] of options) {
      const b = h('button', { type: 'button', class: v === value ? 'on' : '' }, label);
      b.addEventListener('click', () => {
        value = v;
        onChange?.(v);
        draw();
      });
      el.append(b);
    }
  };
  draw();
  return { el, get: () => value };
}

const field = (label: string, control: HTMLElement) => h('label', { class: 'field' }, h('span', null, label), control);

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
  const item = (label: string, fn: () => void, sub = '', lead = false) => {
    const b = h('button', { type: 'button', class: lead ? 'lead' : '' }, h('span', null, label), sub ? h('small', null, sub) : null);
    b.addEventListener('click', fn);
    return b;
  };
  const soloOnly = !!import.meta.env.VITE_SOLO_ONLY;
  put(list,
    hd.continueGame ? item('Continue', hd.onContinue, 'autosave', true) : null,
    hd.rejoin ? item('Rejoin online game', hd.onRejoin, hd.rejoin, true) : null,
    item('New game', hd.onNew, 'vs computer', !hd.continueGame && !hd.rejoin),
    soloOnly ? null : item('Play online', hd.onOnline, 'with friends'),
    item('Hot-seat', hd.onHotseat, 'one screen'),
    item('How to play', hd.onHelp),
  );
  put(content, h('div', { class: 'title-wrap' },
    h('h1', { class: 'logo' }, 'JERICHO'),
    h('p', { class: 'tagline' }, 'Found cities. Discover the ages. Outwit your friends.'),
    list,
    h('p', { class: 'muted', style: { fontSize: '12px', margin: '0' } }, soloOnly ? 'Solo and hot-seat edition. Online rooms need the game server (see the repository README).' : 'A turn-based strategy game for 1–8 players, played in your browser.'),
  ));
  return el;
}

// ------------------------------------------------------------------ setup (solo / hot-seat)
export function setupScreen(mode: 'solo' | 'hotseat', onStart: (c: SoloConfig) => void, onBack: () => void): HTMLElement {
  const { el, content } = screen();
  let civ: string | null = null;
  const nameIn = h('input', { value: savedName(), placeholder: 'Your name', maxLength: 20 });
  const opp = h('input', { type: 'range', min: '1', max: mode === 'solo' ? '7' : '6', value: mode === 'solo' ? '3' : '1' });
  const oppLabel = h('b', { class: 'num' }, opp.value);
  opp.addEventListener('input', () => { oppLabel.textContent = opp.value; });
  const level = segmented<AiLevel>([['easy', 'Easy'], ['normal', 'Normal'], ['hard', 'Hard']], 'normal');
  const speed = segmented<'standard' | 'quick'>([['standard', 'Standard · 200'], ['quick', 'Quick · 120']], 'standard');
  const stance = segmented<'peace' | 'war'>([['peace', 'Peace'], ['war', 'War']], 'peace');
  const land = segmented<'continent' | 'islands'>([['continent', 'Continent'], ['islands', 'Islands']], 'continent');
  const aggr = segmented<'calm' | 'normal' | 'fierce'>([['calm', 'Calm'], ['normal', 'Normal'], ['fierce', 'Fierce']], 'normal');
  const victories = segmented<'all' | 'noscience'>([['all', 'All'], ['noscience', 'No science']], 'all');
  const size = h('select', null, h('option', { value: 'auto' }, 'Automatic'), ...Object.entries(MAP_SIZES).map(([k, v]) => h('option', { value: k }, v.label)));
  const seed = h('input', { placeholder: 'Random', inputMode: 'numeric' });
  const friendCount = h('select', null, h('option', { value: '1' }, '2 humans'), h('option', { value: '2' }, '3 humans'), h('option', { value: '3' }, '4 humans'));
  const friendInputs = [1, 2, 3].map((i) => h('input', { placeholder: `Player ${i + 1} name`, maxLength: 20 }));
  const friendsBox = h('div', { style: { display: 'grid', gap: '2px' } }, ...friendInputs);
  const syncFriends = () => friendInputs.forEach((f, i) => f.classList.toggle('hidden', i >= Number(friendCount.value)));
  friendCount.addEventListener('change', syncFriends);
  syncFriends();

  const civList = h('div', { class: 'civ-list' });
  const draw = () => {
    clear(civList);
    const rand = h('button', { class: `civ-row ${civ === null ? 'on' : ''}`, type: 'button' }, h('span', { class: 'emblem', style: { width: '34px', height: '34px', background: '#3a4556', color: '#e9e1cc' } }, '?'), h('div', null, h('b', null, 'Random'), h('small', null, 'Let fate decide')));
    rand.addEventListener('click', () => { civ = null; draw(); });
    civList.append(rand);
    for (const c of CIVS) {
      const row = h('button', { class: `civ-row ${civ === c.id ? 'on' : ''}`, type: 'button' }, emblem(c.id, 34), h('div', null, h('b', null, c.name), h('small', null, `${c.leader} · ${c.bonus}`)));
      row.addEventListener('click', () => { civ = c.id; draw(); });
      civList.append(row);
    }
  };
  draw();

  const start = () => {
    const name = nameIn.value.trim() || 'Player';
    saveName(name);
    const humans = 1 + (mode === 'hotseat' ? Number(friendCount.value) : 0);
    const friends = mode === 'hotseat' ? friendInputs.map((f, i) => f.value.trim() || `Player ${i + 2}`).slice(0, humans - 1) : [];
    onStart({
      name, civ, opponents: Number(opp.value), aiLevel: level.get(), size: size.value as SoloConfig['size'], speed: speed.get(),
      seed: seed.value.trim() ? Number(seed.value) || null : null, turnLimit: speed.get() === 'quick' ? 120 : 200,
      startAtWar: stance.get() === 'war', scienceVictory: victories.get() === 'all', aggression: aggr.get(), mapStyle: land.get(), friends,
    });
  };

  const sheetEl = h('div', { class: 'sheet' },
    h('header', null, h('h2', null, mode === 'solo' ? 'New game' : 'Hot-seat game')),
    h('hr', { class: 'rule' }),
    h('div', { class: 'body' },
      h('div', { class: 'cols' },
        h('div', null, h('div', { class: 'eyebrow', style: { padding: '4px 0 8px' } }, mode === 'solo' ? 'Choose your civilisation' : 'Player 1’s civilisation'), civList),
        h('div', null,
          h('div', { class: 'eyebrow', style: { padding: '4px 0 2px' } }, 'Players'),
          field(mode === 'solo' ? 'Name' : 'Player 1', nameIn),
          mode === 'hotseat' ? field('Humans', friendCount) : null,
          mode === 'hotseat' ? field('Names', friendsBox) : null,
          field('Opponents', h('span', { style: { display: 'flex', gap: '12px', alignItems: 'center' } }, opp, oppLabel)),
          field('Difficulty', level.el),
          h('div', { class: 'eyebrow', style: { padding: '18px 0 2px' } }, 'World'),
          field('Map', size),
          field('Land', land.el),
          field('Speed', speed.el),
          field('Diplomacy', stance.el),
          field('Computers', aggr.el),
          field('Victory', victories.el),
          field('Seed', seed)))),
    h('footer', null, btn('Back', onBack), h('span', { style: { flex: '1' } }), btn('Begin', start, 'primary big')));
  put(content, sheetEl);
  return el;
}

// ------------------------------------------------------------------ online entry
export function onlineEntryScreen(initialCode: string, onCreate: (name: string) => void, onJoin: (code: string, name: string) => void, onBack: () => void, error: string | null): HTMLElement {
  const { el, content } = screen();
  const nameIn = h('input', { value: savedName(), placeholder: 'Your name', maxLength: 20 });
  const codeIn = h('input', { value: initialCode, placeholder: 'ABCDE', maxLength: 6, style: { textTransform: 'uppercase', letterSpacing: '0.4em', fontSize: '24px', textAlign: 'center', width: '150px', fontFamily: 'var(--display)' } });
  const name = () => {
    const n = nameIn.value.trim() || 'Player';
    saveName(n);
    return n;
  };
  put(content, h('div', { class: 'sheet narrow' },
    h('header', null, h('h2', null, 'Play online')),
    h('hr', { class: 'rule' }),
    h('div', { class: 'body' },
      error ? h('div', { class: 'error-line' }, error) : null,
      field('Your name', nameIn),
      h('div', { style: { display: 'grid', gap: '8px', paddingTop: '8px' } }, h('div', { class: 'eyebrow' }, 'Host a game'), h('p', { class: 'muted', style: { margin: '0', fontSize: '14px' } }, 'Create a room, then send the invite link or the five-letter code to your friends.'), h('div', null, btn('Create a room', () => onCreate(name()), 'primary'))),
      h('hr', { class: 'rule soft' }),
      h('div', { style: { display: 'grid', gap: '10px' } }, h('div', { class: 'eyebrow' }, 'Join a friend'), h('div', { style: { display: 'flex', gap: '14px', alignItems: 'center' } }, codeIn, btn('Join', () => onJoin(codeIn.value.toUpperCase().trim(), name()), 'primary')))),
    h('footer', null, btn('Back', onBack))));
  codeIn.addEventListener('keydown', (e) => { if (e.key === 'Enter') onJoin(codeIn.value.toUpperCase().trim(), name()); });
  return el;
}

// ------------------------------------------------------------------ lobby
export function lobbyScreen(rt: RemoteTransport, onLeave: () => void): { el: HTMLElement; update: (room: RoomInfo) => void; chat: (from: string, text: string) => void } {
  const { el, content } = screen();
  const wrap = h('div', { class: 'sheet' });
  content.append(wrap);
  const chatLines: Array<{ from: string; text: string }> = [];
  const chatLog = h('div', { class: 'chat-log' });
  const chatIn = h('input', { placeholder: 'Say something to your friends…', maxLength: 240, style: { width: '100%' } });
  chatIn.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && chatIn.value.trim()) {
      rt.sendChat(chatIn.value.trim());
      chatIn.value = '';
    }
  });
  const renderChat = () => {
    clear(chatLog);
    for (const l of chatLines.slice(-40)) chatLog.append(h('div', null, h('b', null, l.from + ' '), l.text));
    chatLog.scrollTop = chatLog.scrollHeight;
  };
  const update = (room: RoomInfo) => {
    const host = room.you.host;
    clear(wrap);
    const link = `${location.origin}${location.pathname}?room=${room.code}`;
    const send = (m: Parameters<RemoteTransport['raw']>[0]) => rt.raw(m);

    const players = h('div');
    for (const s of room.slots) {
      if (s.kind === 'closed' && !host) continue;
      players.append(slotRow(room, s, host, send));
    }

    const s = room.settings;
    const sel = <K extends keyof RoomSettings>(label: string, key: K, options: Array<[string, string]>) => {
      const select = h('select', { disabled: !host }, ...options.map(([v, l]) => h('option', { value: v, selected: String(s[key]) === v }, l)));
      select.addEventListener('change', () => send({ t: 'settings', settings: { [key]: key === 'turnTimer' || key === 'turnLimit' ? Number(select.value) : key === 'startAtWar' || key === 'scienceVictory' ? select.value === 'true' : select.value } as Partial<RoomSettings> }));
      return field(label, select);
    };
    put(wrap,
      h('header', null, h('h2', null, 'Game lobby'), btn('Leave', onLeave, 'small')),
      h('hr', { class: 'rule' }),
      h('div', { class: 'body' },
        h('div', { class: 'cols' },
          h('div', null, h('div', { class: 'eyebrow', style: { paddingBottom: '6px' } }, 'Seats'), players,
            h('p', { class: 'hint', style: { margin: '10px 0 0', color: 'var(--muted)', fontSize: '13px' } }, host ? 'Open a seat for a friend or fill it with a computer opponent. At least two seats are needed.' : 'Waiting for the host to start the game.')),
          h('div', null,
            h('div', { style: { textAlign: 'center', paddingBottom: '14px' } }, h('div', { class: 'eyebrow' }, 'Room code'), h('div', { class: 'code' }, room.code),
              btn('Copy invite link', () => { void navigator.clipboard?.writeText(link).catch(() => undefined); }, 'small'),
              h('div', { class: 'muted', style: { fontSize: '11px', marginTop: '8px', wordBreak: 'break-all' } }, link)),
            h('div', { class: 'eyebrow', style: { padding: '6px 0 2px' } }, 'Settings'),
            sel('Map', 'size', Object.entries(MAP_SIZES).map(([k, v]) => [k, v.label])),
            sel('Land', 'mapStyle', [['continent', 'One continent'], ['islands', 'Islands (ships needed)']]),
            sel('Speed', 'speed', [['standard', 'Standard'], ['quick', 'Quick']]),
            sel('Turn limit', 'turnLimit', [['100', '100'], ['120', '120'], ['150', '150'], ['200', '200'], ['300', '300']]),
            sel('Diplomacy', 'startAtWar', [['false', 'Start at peace'], ['true', 'Start at war']]),
            sel('Computers', 'aggression', [['calm', 'Calm'], ['normal', 'Normal'], ['fierce', 'Fierce']]),
            sel('Science win', 'scienceVictory', [['true', 'Enabled'], ['false', 'Disabled']]),
            sel('Turn timer', 'turnTimer', [['0', 'Off'], ['60', '1 minute'], ['120', '2 minutes'], ['180', '3 minutes'], ['300', '5 minutes']]),
            h('div', { class: 'eyebrow', style: { padding: '18px 0 6px' } }, 'Chat'), chatLog, h('div', { style: { height: '6px' } }), chatIn))),
      h('footer', null, h('span', { style: { flex: '1' } }), host ? btn('Start game', () => send({ t: 'start' }), 'primary big') : null));
    renderChat();
  };
  return { el, update, chat: (from, text) => { chatLines.push({ from, text }); renderChat(); } };
}

function slotRow(room: RoomInfo, s: RoomSlot, host: boolean, send: (m: Parameters<RemoteTransport['raw']>[0]) => void): HTMLElement {
  const me = room.you.slot === s.slot;
  const civ = s.civ ? CIV_BY_ID[s.civ] : null;
  const badge = civ ? emblem(civ.id, 30) : h('span', { class: 'emblem', style: { width: '30px', height: '30px', background: s.kind === 'open' || s.kind === 'closed' ? '#252e3b' : '#3a4556', color: '#8f9aab' } }, s.kind === 'open' ? '+' : s.kind === 'closed' ? '–' : '?');
  const nameEl = h('div', { class: 'name' },
    h('b', null, s.kind === 'open' ? 'Open seat' : s.kind === 'closed' ? 'Closed' : s.kind === 'ai' ? 'Computer' : s.name || 'Player'),
    s.kind === 'human' ? h('span', { class: 'muted', style: { marginLeft: '8px', fontSize: '12px' } }, `${s.host ? 'host · ' : ''}${me ? 'you' : s.connected ? 'online' : 'away'}`) : null,
    null);
  const civSelect = h('select', { disabled: !(me || (host && s.kind === 'ai')), title: 'Civilisation' },
    h('option', { value: '' }, 'Random'),
    ...CIVS.map((c) => h('option', { value: c.id, selected: s.civ === c.id, disabled: room.slots.some((o) => o.slot !== s.slot && o.civ === c.id && (o.kind === 'human' || o.kind === 'ai')) }, c.name)));
  civSelect.addEventListener('change', () => send({ t: 'civ', civ: civSelect.value || null, slot: s.slot }));
  if (s.kind === 'open' || s.kind === 'closed') civSelect.classList.add('hidden');
  const controls = h('div', { style: { display: 'flex', gap: '8px' } });
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
  return h('div', { class: `slot ${s.kind === 'open' || s.kind === 'closed' ? 'empty' : ''}` }, badge, nameEl, civSelect, controls);
}
