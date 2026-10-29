import { CIVS, CIV_BY_ID, MAP_SIZES } from '../../shared/data';
import type { AiLevel } from '../../shared/types';
import type { RoomInfo, RoomSettings, RoomSlot } from '../../shared/protocol';
import { crestUrl, leaderUrl } from '../artwork';
import { icon } from '../icons';
import { isMuted, setMuted } from '../audio';
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

export type Scene = 'menu' | 'campaign' | 'settings' | 'archive';

/** A full-screen menu over one of the painted scenes (the picture itself is set in the stylesheet). */
export function screen(scene: Scene = 'menu'): { el: HTMLElement; content: HTMLElement } {
  const content = h('div', { class: 'content' });
  const el = h('div', { class: 'screen' }, h('div', { class: `bg scene-${scene}`, 'aria-hidden': 'true' }), content, soundToggle());
  return { el, content };
}

/** Round button in the corner of every menu screen that mutes or restores all sound. */
function soundToggle(): HTMLButtonElement {
  const b = h('button', { class: 'round sound-toggle', type: 'button' });
  const draw = () => {
    const on = !isMuted();
    b.title = on ? 'Sound on' : 'Sound off';
    b.setAttribute('aria-label', b.title);
    b.setAttribute('aria-pressed', String(on));
    clear(b);
    b.append(icon(on ? 'sound' : 'mute', 20));
  };
  b.addEventListener('click', () => {
    setMuted(!isMuted());
    draw();
  });
  draw();
  return b;
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
  el.classList.add('alive'); // only the title screen drifts and glints; the setup screens stay still
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
  // arrow keys walk the list, like a console menu
  list.addEventListener('keydown', (e) => {
    if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
    const items = Array.from(list.querySelectorAll('button'));
    const at = items.indexOf(document.activeElement as HTMLButtonElement);
    items[(at + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length]?.focus();
    e.preventDefault();
  });
  const ornament = () => h('div', { class: 'ornament', 'aria-hidden': 'true' }, h('i'), h('b'), h('i'));
  put(content, h('div', { class: 'title-wrap' },
    h('img', { class: 'crest-art', src: crestUrl, alt: '', draggable: false }),
    h('h1', { class: 'logo' }, 'JERICHO'),
    ornament(),
    h('p', { class: 'tagline' }, 'Found cities. Discover the ages. Outwit your friends.'),
    list,
    h('div', { class: 'civ-strip', 'aria-hidden': 'true', title: 'Eight civilisations' }, ...CIVS.map((c) => emblem(c.id, 38))),
    h('p', { class: 'muted fine' }, soloOnly ? 'Solo and hot-seat edition. Online rooms need the game server (see the repository README).' : 'A turn-based strategy game for 1–8 players, played in your browser.'),
  ));
  setTimeout(() => list.querySelector<HTMLButtonElement>('button.lead')?.focus({ preventScroll: true }), 60);
  return el;
}

// ------------------------------------------------------------------ setup (solo / hot-seat)
/** Horizontal, snap-scrolling picker of large civilisation cards; the first card is "Random". */
function civCarousel(onPick: (civ: string | null) => void): HTMLElement {
  const options: Array<{ id: string | null; card: HTMLButtonElement }> = [];
  const track = h('div', { class: 'civ-track', role: 'radiogroup', 'aria-label': 'Civilisation' });
  const add = (id: string | null, face: HTMLElement, name: string, leader: string, bonus: string, color: string) => {
    const card = h('button', { class: 'civ-card', type: 'button', role: 'radio', style: `--civ:${color}` }, face, h('b', null, name), h('em', null, leader), h('small', null, bonus));
    card.addEventListener('click', () => pick(options.findIndex((o) => o.id === id)));
    track.append(card);
    options.push({ id, card });
  };
  const stage = (...face: HTMLElement[]) => h('span', { class: 'civ-stage' }, ...face);
  add(null, stage(h('span', { class: 'emblem', style: { width: '96px', height: '96px', background: '#3a4556', color: '#e9e1cc', fontSize: '42px' } }, '?')), 'Random', 'Let fate decide', 'Any civilisation, drawn at the start', '#8f9aab');
  // a painted leader stands on the card; civilisations without one keep their drawn portrait
  for (const c of CIVS) add(c.id, stage(leaderUrl(c.id) ? h('img', { class: 'civ-leader', src: leaderUrl(c.id), alt: '', draggable: false }) : emblem(c.id, 104)), c.name, c.leader, c.bonus, c.color);

  const centre = (i: number, smooth: boolean) => {
    const c = options[i].card;
    track.scrollTo({ left: c.offsetLeft - (track.clientWidth - c.offsetWidth) / 2, behavior: smooth ? 'smooth' : 'auto' });
  };
  let at = 0;
  const pick = (i: number, smooth = true) => {
    at = Math.min(options.length - 1, Math.max(0, i));
    options.forEach((o, k) => {
      o.card.classList.toggle('on', k === at);
      o.card.setAttribute('aria-checked', String(k === at));
      o.card.tabIndex = k === at ? 0 : -1;
    });
    centre(at, smooth);
    onPick(options[at].id);
  };
  const step = (d: number) => {
    pick(at + d);
    options[at].card.focus({ preventScroll: true });
  };
  track.addEventListener('keydown', (e) => {
    if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft') return;
    step(e.key === 'ArrowRight' ? 1 : -1);
    e.preventDefault();
  });
  const arrow = (dir: -1 | 1) => {
    const label = dir < 0 ? 'Previous civilisation' : 'Next civilisation';
    const b = h('button', { class: `round civ-arrow ${dir < 0 ? 'prev' : 'next'}`, type: 'button', title: label, 'aria-label': label }, icon('chevron', 20));
    b.addEventListener('click', () => step(dir));
    return b;
  };
  pick(0, false);
  // the first centring needs the laid-out widths, so it repeats once the screen is in the page
  requestAnimationFrame(() => centre(at, false));
  return h('div', { class: 'civ-carousel' }, arrow(-1), track, arrow(1));
}

export function setupScreen(mode: 'solo' | 'hotseat', onStart: (c: SoloConfig) => void, onBack: () => void): HTMLElement {
  const { el, content } = screen('campaign');
  let civ: string | null = null;
  const nameIn = h('input', { value: savedName(), placeholder: 'Your name', maxLength: 20 });
  const opp = h('input', { type: 'range', min: '1', max: mode === 'solo' ? '7' : '6', value: mode === 'solo' ? '3' : '1' });
  const oppLabel = h('b', { class: 'num range-val' }, opp.value);
  const fillRange = () => {
    oppLabel.textContent = opp.value;
    opp.style.setProperty('--p', `${((Number(opp.value) - Number(opp.min)) / (Number(opp.max) - Number(opp.min))) * 100}%`);
  };
  opp.addEventListener('input', fillRange);
  fillRange();
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
  const friendFields = friendInputs.map((f, i) => field(`Player ${i + 2}`, f));

  const fields = [
    field(mode === 'solo' ? 'Name' : 'Player 1', nameIn),
    ...(mode === 'hotseat' ? [field('Humans', friendCount), ...friendFields] : []),
    field('Opponents', h('span', { class: 'range' }, opp, oppLabel)),
    field('Difficulty', level.el),
    field('Computers', aggr.el),
    field('Map', size),
    field('Land', land.el),
    field('Speed', speed.el),
    field('Diplomacy', stance.el),
    field('Victory', victories.el),
    field('Seed', seed),
  ];
  // two even columns: when the visible count is odd the last setting spans both, so no cell is left empty
  const layout = () => {
    friendFields.forEach((f, i) => f.classList.toggle('hidden', i >= Number(friendCount.value)));
    const shown = fields.filter((f) => !f.classList.contains('hidden'));
    fields.forEach((f) => f.classList.remove('wide'));
    if (shown.length % 2) shown[shown.length - 1].classList.add('wide');
  };
  friendCount.addEventListener('change', layout);
  layout();

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

  put(content, h('div', { class: 'sheet setup' },
    h('header', null, h('h2', null, mode === 'solo' ? 'New game' : 'Hot-seat game')),
    h('hr', { class: 'rule' }),
    h('div', { class: 'body' },
      h('section', null, h('div', { class: 'eyebrow' }, mode === 'solo' ? 'Choose your civilisation' : 'Player 1’s civilisation'), civCarousel((id) => { civ = id; })),
      h('section', null, h('div', { class: 'eyebrow' }, 'Settings'), h('div', { class: 'setup-grid' }, ...fields))),
    h('footer', null, btn('Back', onBack, 'big'), btn('Begin', start, 'primary big'))));
  return el;
}

// ------------------------------------------------------------------ online entry
export function onlineEntryScreen(initialCode: string, onCreate: (name: string) => void, onJoin: (code: string, name: string) => void, onBack: () => void, error: string | null): HTMLElement {
  const { el, content } = screen('settings');
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
  const { el, content } = screen('archive');
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
