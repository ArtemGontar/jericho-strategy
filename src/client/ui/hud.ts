import { portraitUrl } from '../art';
import { CIV_BY_ID, ERA_NAMES, IMPROVEMENTS, POLICY_BY_ID, RESOURCES, TECH_BY_ID, TERRAIN, UNITS, policyCost } from '../../shared/data';
import { RESOURCE_LIST, TERRAIN_LIST } from '../../shared/view';
import type { WorkKind } from '../../shared/types';
import type { GameScreen } from '../gamescreen';
import { civColor, tileYieldFromView } from '../render';
import { isMuted, setMuted } from '../audio';
import { artIcon, leaderUrl } from '../artwork';
import { icon } from '../icons';
import { THRIVING } from '../../shared/civ';
import { clear, floatingOrders, fmtSigned, h, isCompact, put, tickNumber } from './dom';

const NS = 'http://www.w3.org/2000/svg';

export function emblem(civId: string, size: number, label?: string): HTMLElement {
  const civ = CIV_BY_ID[civId];
  void label;
  const leader = leaderUrl(civId);
  // a painted leader sits on a glow of the civilisation's colour; the others keep the drawn bust
  if (leader) return h('span', { class: 'emblem portrait leader', style: `width:${size}px;height:${size}px;border-color:${civ.color};border-width:${size > 48 ? 3 : 2}px;--civ:${civ.color};background-image:url(${leader})` });
  return h('span', { class: 'emblem portrait', style: { width: `${size}px`, height: `${size}px`, borderColor: civ.color, borderWidth: size > 48 ? '3px' : '2px', backgroundImage: `url(${portraitUrl(civId, size > 48 ? 256 : 112)})` } });
}

/** A circular progress ring around a medallion face. */
function ring(progress: number, color: string): SVGSVGElement {
  const svg = document.createElementNS(NS, 'svg');
  svg.setAttribute('viewBox', '0 0 62 62');
  svg.setAttribute('class', 'ring');
  const mk = (r: number, stroke: string, width: number, extra: Record<string, string> = {}) => {
    const c = document.createElementNS(NS, 'circle');
    c.setAttribute('cx', '31');
    c.setAttribute('cy', '31');
    c.setAttribute('r', String(r));
    c.setAttribute('fill', 'none');
    c.setAttribute('stroke', stroke);
    c.setAttribute('stroke-width', String(width));
    for (const [k, v] of Object.entries(extra)) c.setAttribute(k, v);
    return c;
  };
  svg.append(mk(29.5, '#8a6d2f', 1.5), mk(26, 'rgba(255,255,255,0.1)', 4));
  const circ = 2 * Math.PI * 26;
  const arc = mk(26, color, 4, { 'stroke-dasharray': `${Math.max(0.001, Math.min(1, progress)) * circ} ${circ}`, 'stroke-linecap': 'butt', transform: 'rotate(-90 31 31)' });
  svg.append(arc);
  return svg;
}

export function currentEra(techs: string[]): number {
  let era = 0;
  for (const t of techs) era = Math.max(era, TECH_BY_ID[t]?.era ?? 0);
  return era;
}

// ------------------------------------------------------------------ top bar and medallions
export function renderTopbar(gs: GameScreen, el: HTMLElement): void {
  const v = gs.view;
  clear(el);
  const me = v.players[v.me];
  const civ = CIV_BY_ID[me.civ];
  const rankings = h('div', { class: 'rankings' });
  for (const p of v.players) {
    const rel = p.id === v.me ? '' : p.relation;
    const item = h('div', {
      class: `rank ${p.alive ? '' : 'dead'} ${rel === 'war' ? 'war' : ''}`,
      title: `${p.name} — ${CIV_BY_ID[p.civ].name}\nScore ${p.score} · ${p.cityCount} cities · ${p.techCount} techs${p.id === v.me ? '' : `\n${p.relation === 'war' ? 'At war' : p.relation === 'peace' ? 'At peace' : 'Not met yet'}`}`,
      on: { click: () => gs.openDiplomacy(p.id) },
    }, emblem(p.civ, 26), h('small', { class: 'num' }, String(p.score)));
    if (!p.ai && p.endedTurn && p.alive) item.append(h('span', { class: 'tick', title: 'Ready' }, icon('plus', 8)));
    rankings.append(item);
  }
  const goldNum = h('span', { class: 'num' }, String(v.gold));
  tickNumber(goldNum, 'gold', v.gold);
  const era = currentEra(v.techs);
  put(el,
    h('div', { class: 'pad-medals' }),
    h('div', { class: 'stats' },
      h('div', { class: 'stat', title: 'Treasury and income per turn' }, icon('gold', 20, 'coin'), goldNum, h('small', { class: v.income.gold >= 0 ? 'pos' : 'neg' }, fmtSigned(v.income.gold))),
      h('span', { class: 'sep' }),
      h('div', { class: 'stat', title: 'Science per turn' }, icon('science', 20, 'sci'), h('span', { class: 'num' }, `+${v.income.science}`)),
      h('span', { class: 'sep' }),
      h('div', { class: 'stat', title: v.cultureGoal ? `Culture per turn. Total ${v.culture} of ${v.cultureGoal} needed for cultural hegemony (and a 20% lead over every rival).` : 'Culture per turn' }, icon('culture', 20, 'cult'), h('span', { class: 'num' }, `+${v.income.culture}`)),
      contentChip(gs),
      idleUnits(gs),
      v.settings.turnTimer > 0 ? h('span', { class: 'sep' }) : null,
      v.settings.turnTimer > 0 ? h('div', { class: 'stat', title: 'Time left in this turn' }, icon('turn', 20), h('span', { class: 'num', id: 'timerlabel' }, '--')) : null,
      v.enlightenment ? h('span', { class: 'sep' }) : null,
      v.enlightenment ? countdown(gs) : null,
      v.hegemony ? h('span', { class: 'sep' }) : null,
      v.hegemony ? hegemonyChip(gs) : null,
      v.domination ? h('span', { class: 'sep' }) : null,
      v.domination ? dominationChip(gs) : null,
    ),
    rankings,
    h('div', { class: 'era', 'data-era': String(era), title: civ.bonus }, `Turn ${v.turn} / ${v.settings.turnLimit}`, h('b', null, `${ERA_NAMES[era]} Era`)),
    h('span', { class: 'sep' }),
    gs.opts.sendChat ? roundFlat('chat', 'Chat ( / )', () => gs.toggleChat()) : null,
    roundFlat('city', 'Your cities (L)', () => gs.openModal('cities'), 'hide-phone'),
    roundFlat('diplomacy', 'Diplomacy (D)', () => gs.openDiplomacy()),
    roundFlat('log', 'Message log', () => gs.openModal('log'), 'hide-phone'),
    roundFlat('menu', 'Menu (Esc)', () => gs.openModal('menu')),
    h('div', { class: 'turnline', 'aria-hidden': 'true' }, h('i', { style: { transform: `scaleX(${Math.min(1, v.turn / Math.max(1, v.settings.turnLimit))})` } })),
  );
  gs.updateTimerLabel();
}

/** How content the people are: a heart and a number, red when they are restless. Click for the breakdown. */
function contentChip(gs: GameScreen): HTMLElement {
  const v = gs.view;
  const c = v.content.value;
  const state = c < 0 ? 'restless' : c >= THRIVING ? 'thriving' : 'content';
  const b = h('button', {
    class: `stat click content-chip ${state}`,
    type: 'button',
    title: c < 0 ? `Contentment ${fmtSigned(Math.round(c))}: your people are restless, so cities grow and produce less. Click to see why.` : c >= THRIVING ? `Contentment ${Math.round(c)}: your people thrive. Click for details.` : `Contentment ${Math.round(c)}: your people are content. Click for details.`,
  }, icon('health', 20), h('span', { class: 'num' }, String(Math.round(c))));
  b.addEventListener('click', (e) => {
    e.stopPropagation();
    gs.openModal('content');
  });
  return b;
}

/** Units still waiting for orders; clicking (or pressing . / N) jumps to the next one. */
function idleUnits(gs: GameScreen): HTMLElement | null {
  const n = gs.myUnits().filter((u) => gs.needsOrders(u)).length;
  if (!n) return null;
  const b = h('button', { class: 'idle-units', type: 'button', title: 'Units waiting for orders. Click, or press . (N) for the next one.' }, h('b', { class: 'num' }, String(n)), h('span', null, n === 1 ? 'unit idle' : 'units idle'));
  b.addEventListener('click', (ev) => {
    ev.stopPropagation();
    gs.selectNextIdle();
  });
  return h('span', { class: 'idle-wrap' }, h('span', { class: 'sep' }), b);
}

/** The Enlightenment race: who is about to win, and how long is left to take their capital. */
function countdown(gs: GameScreen): HTMLElement {
  const v = gs.view;
  const e = v.enlightenment!;
  const who = v.players[e.owner];
  const mine = e.owner === v.me;
  const cap = v.cities.find((c) => c.id === e.capital);
  const b = h('button', {
    class: `countdown ${mine ? 'mine' : 'foe'}`,
    type: 'button',
    title: mine ? `You win in ${e.turnsLeft} turns if you keep ${cap?.name ?? 'your capital'}.` : `${who.name} wins in ${e.turnsLeft} turns unless their capital${cap ? `, ${cap.name},` : ''} falls. Click to look.`,
  }, icon('science', 16), h('span', null, mine ? 'Hold your capital' : `${CIV_BY_ID[who.civ].name} wins`), h('b', { class: 'num' }, String(e.turnsLeft)));
  b.addEventListener('click', (ev) => {
    ev.stopPropagation();
    if (cap) gs.focus(cap.x, cap.y);
    if (isCompact()) gs.toast(b.title); // the chip shrinks to an icon and a number on a phone
  });
  return b;
}

/** The culture race: a civilisation's culture towers over everyone's, and rivals have a few turns to catch up. */
function hegemonyChip(gs: GameScreen): HTMLElement {
  const v = gs.view;
  const e = v.hegemony!;
  const who = v.players[e.owner];
  const mine = e.owner === v.me;
  const chip = h('div', {
    class: `countdown ${mine ? 'mine' : 'foe'}`,
    title: mine ? `Your culture dominates. You win in ${e.turnsLeft} turns if the lead holds.` : `${who.name}'s culture dominates: they win in ${e.turnsLeft} turns unless a rival's culture catches up to within 10%, or their cities fall.`,
  }, icon('culture', 16), h('span', null, mine ? 'Culture lead' : `${CIV_BY_ID[who.civ].name} culture`), h('b', { class: 'num' }, String(e.turnsLeft)));
  chip.addEventListener('click', (ev) => {
    ev.stopPropagation();
    if (isCompact()) gs.toast(chip.title);
  });
  return chip;
}

/** The conquest race: one civilisation holds most of the world's cities. Click to look at its capital. */
function dominationChip(gs: GameScreen): HTMLElement {
  const v = gs.view;
  const e = v.domination!;
  const who = v.players[e.owner];
  const mine = e.owner === v.me;
  const cap = v.cities.find((c) => c.owner === e.owner && c.capital) ?? v.cities.find((c) => c.owner === e.owner);
  const chip = h('button', {
    class: `countdown ${mine ? 'mine' : 'foe'}`,
    type: 'button',
    title: mine ? `You hold most of the world's cities. You win in ${e.turnsLeft} turns unless enough are taken back.` : `${who.name} holds most of the world's cities and wins in ${e.turnsLeft} turns unless you and the others take cities back. Click to look.`,
  }, icon('swords', 16), h('span', null, mine ? 'Hold your conquests' : `${CIV_BY_ID[who.civ].name} dominates`), h('b', { class: 'num' }, String(e.turnsLeft)));
  chip.addEventListener('click', (ev) => {
    ev.stopPropagation();
    if (cap) gs.focus(cap.x, cap.y);
    if (isCompact()) gs.toast(chip.title);
  });
  return chip;
}

function roundFlat(name: string, title: string, fn: () => void, extra = ''): HTMLButtonElement {
  const b = h('button', { class: `round flat ${extra}`, type: 'button', title, 'aria-label': title }, icon(name, 20));
  b.addEventListener('click', (e) => {
    e.stopPropagation();
    fn();
  });
  return b;
}

export function renderMedallions(gs: GameScreen, el: HTMLElement): void {
  const v = gs.view;
  clear(el);
  // research
  const tech = v.researching ? TECH_BY_ID[v.researching] : null;
  const sciProgress = tech && v.researchCost ? v.research / v.researchCost : 0;
  const sciTurns = tech && v.income.science > 0 ? Math.max(1, Math.ceil((v.researchCost - v.research) / v.income.science)) : 0;
  const researchAlert = !tech && v.availableTechs.length > 0;
  el.append(
    h('div', { class: `medal ${researchAlert ? 'alert' : ''}`, title: 'Open the technology tree (T)', on: { click: () => gs.openModal('tech') } },
      h('div', { class: 'disc' }, ring(sciProgress, 'var(--sci)'), h('div', { class: 'face sci' }, icon('science', 24))),
      h('div', { class: 'txt' }, h('b', null, tech ? tech.name : 'Choose research'), h('span', null, tech ? `${sciTurns || '∞'} turn${sciTurns === 1 ? '' : 's'}` : `${v.availableTechs.length} available`))),
  );
  // culture / policies
  const speed = v.settings.speed === 'quick' ? 0.67 : 1;
  const next = v.policyCost;
  const prev = v.policies.length ? policyCost(v.policies.length - 1, speed) : 0;
  const cultProgress = v.policyChoices.length ? 1 : Math.max(0, Math.min(1, (v.culture - prev) / Math.max(1, next - prev)));
  const policyAlert = v.policyChoices.length > 0;
  el.append(
    h('div', { class: `medal ${policyAlert ? 'alert' : ''}`, title: 'Policies', on: { click: () => gs.openModal('policies') } },
      h('div', { class: 'disc' }, ring(cultProgress, 'var(--cult)'), h('div', { class: 'face cult' }, icon('culture', 24))),
      h('div', { class: 'txt' }, h('b', null, policyAlert ? 'Choose a policy' : 'Culture'), h('span', null, policyAlert ? 'New policy ready' : `${v.culture} / ${next}`))),
  );
}

// ------------------------------------------------------------------ notifications
const NOTE_ICON: Record<string, string> = { info: 'log', good: 'star', bad: 'shield', war: 'swords', tech: 'science', wonder: 'wonder' };

export function pushNote(gs: GameScreen, host: HTMLElement, text: string, kind: string, at?: { x: number; y: number }): void {
  const n = h('div', { class: `note ${kind}` }, icon(NOTE_ICON[kind] ?? 'log', 20), h('div', null, text));
  n.addEventListener('click', () => {
    if (at) gs.focus(at.x, at.y);
    n.classList.add('out');
    setTimeout(() => n.remove(), 240);
  });
  host.append(n);
  // a phone has no room to spare: fewer notes, and they leave sooner
  const phone = isCompact();
  while (host.children.length > (phone ? 3 : 7)) host.firstChild?.remove();
  const long = kind === 'war' || kind === 'wonder' || kind === 'tech';
  setTimeout(() => {
    n.classList.add('out');
    setTimeout(() => n.remove(), 240);
  }, phone ? (long ? 8000 : 4500) : (long ? 12000 : 7000));
}

// ------------------------------------------------------------------ unit bar
export function renderUnitBar(gs: GameScreen, el: HTMLElement): void {
  clear(el);
  const u = gs.selUnit;
  if (!u) {
    el.classList.add('hidden');
    gs.unitActions.classList.add('hidden');
    return;
  }
  el.classList.remove('hidden');
  const v = gs.view;
  // on a big screen the order buttons float beside the unit; on a phone they stay in the bar
  const floating = floatingOrders();
  const def = UNITS[u.type];
  const col = civColor(v, u.owner);
  const hint = h('span', { class: 'hint' }, ' ');
  const actions = h('div', { class: 'actions' });
  const act = (name: string, label: string, fn: () => void, opts: { disabled?: string; primary?: boolean; danger?: boolean; extra?: string } = {}) => {
    const b = h('button', { class: `round ${opts.primary ? 'primary' : ''} ${opts.danger ? 'danger' : ''}`, type: 'button', 'aria-label': label, title: opts.disabled ? `${label} — ${opts.disabled}` : label }, icon(name, 20));
    if (opts.disabled) b.disabled = true;
    b.addEventListener('click', (e) => {
      e.stopPropagation();
      fn();
    });
    b.addEventListener('mouseenter', () => { hint.textContent = opts.disabled ? `${label} — ${opts.disabled}` : label + (opts.extra ? ` — ${opts.extra}` : ''); });
    b.addEventListener('mouseleave', () => { hint.textContent = ' '; });
    actions.append(b);
  };
  if (u.type === 'settler') {
    const err = gs.canFound(u);
    act('found', 'Found city (B)', () => void gs.foundCity(u), { disabled: err ?? '', primary: true });
  }
  if (u.type === 'worker') {
    (['farm', 'mine', 'road'] as WorkKind[]).forEach((k) => {
      const err = gs.canWork(u, k);
      const d = IMPROVEMENTS[k];
      act(k, `Build ${d.name.toLowerCase()}`, () => void gs.cmd({ t: 'work', unit: u.id, what: k }).then(() => gs.afterUnitAction()), { disabled: err ?? '', extra: `${d.turns} turns` });
    });
    act('auto', 'Automate (A)', () => void gs.autoUnit(u), { extra: 'the worker improves land on its own' });
  }
  const boat = gs.shipFor(u);
  if (boat && (u.mp ?? 0) > 0) act('trireme', `Board the ${UNITS[boat.type].name}`, () => void gs.cmd({ t: 'board', unit: u.id, ship: boat.id }).then(() => gs.afterUnitAction()), { primary: true, extra: 'the unit sails with the ship' });
  if (!def.civilian) {
    act('fortify', 'Fortify (F)', () => void gs.cmd({ t: 'order', unit: u.id, order: 'fortify' }).then(() => gs.afterUnitAction()), { extra: '+25% defence and faster healing' });
    act('explore', 'Explore (A)', () => void gs.autoUnit(u), { extra: 'move automatically to reveal the map' });
    if (u.upgrade) {
      const up = u.upgrade;
      act('upgrade', `Upgrade to ${UNITS[up.to].name} (${up.cost} gold)`, () => void gs.cmd({ t: 'upgrade', unit: u.id }), { disabled: up.error ?? '' });
    }
  }
  act('sleep', 'Sleep (Z)', () => void gs.cmd({ t: 'order', unit: u.id, order: 'sleep' }).then(() => gs.afterUnitAction()), { extra: 'stop asking for orders' });
  act('skip', 'Skip turn (Space)', () => void gs.cmd({ t: 'order', unit: u.id, order: 'skip' }).then(() => gs.afterUnitAction()));
  if (u.order || u.auto || u.fortified) act('cancel', 'Cancel orders', () => void gs.cmd({ t: 'order', unit: u.id, order: 'cancel' }).then(() => gs.recomputeSelection()));
  const inCity = v.cities.find((c) => c.mine && c.x === u.x && c.y === u.y);
  if (inCity) act('city', 'Open city (C)', () => gs.openCityPanel(inCity.id));
  const orders = [...actions.children];
  actions.replaceChildren();
  actions.append(...(floating ? [] : orders), h('span', { class: 'gap' }));
  act('disband', 'Disband (Del)', () => void gs.disband(u), { danger: true });
  const fl = gs.unitActions;
  clear(fl);
  fl.classList.toggle('hidden', !floating);
  if (floating) fl.append(...orders);

  const flag = document.createElementNS(NS, 'svg');
  flag.setAttribute('viewBox', '0 0 44 54');
  const shape = document.createElementNS(NS, 'path');
  shape.setAttribute('d', 'M3 3h38v34L22 51 3 37z');
  shape.setAttribute('fill', col);
  shape.setAttribute('stroke', 'rgba(0,0,0,0.55)');
  shape.setAttribute('stroke-width', '2');
  flag.append(shape);
  const full = def.moves;
  const stats = h('div', { class: 'stats' },
    h('span', null, 'Moves ', h('b', { class: 'num' }, `${((u.mp ?? 0) / 2).toFixed((u.mp ?? 0) % 2 ? 1 : 0)}/${full}`)),
    def.strength > 0 ? h('span', null, icon('swords', 14), h('b', { class: 'num' }, String(def.strength))) : null,
    def.ranged > 0 ? h('span', null, 'Range ', h('b', { class: 'num' }, `${def.ranged}/${def.range}`)) : null,
    !def.civilian ? h('span', null, icon('level', 14), h('b', null, u.level ? `Level ${u.level}` : 'Recruit'), u.nextXp ? h('span', { class: 'muted' }, ` ${u.xp}/${u.nextXp} xp`) : null) : null,
    u.order?.kind === 'goto' ? h('span', null, icon('chevron', 14), 'On the way') : null,
    u.order?.kind === 'build' && u.workProgress ? h('span', null, icon(u.workProgress.kind, 14), `${Math.floor(u.workProgress.progress)}/${IMPROVEMENTS[u.workProgress.kind].turns}`) : null,
    u.auto ? h('span', null, icon('auto', 14), u.auto === 'work' ? 'Automated' : 'Exploring') : null,
    def.naval && (def.capacity ?? 0) > 0 ? h('span', null, 'Aboard ', h('b', { class: 'num' }, `${u.cargo?.length ?? 0}/${def.capacity}`)) : null,
    u.carriedBy != null ? h('span', null, icon('trireme', 14), 'Aboard a ship: click the shore to land') : null,
  );
  // passengers: a click picks one to give it orders
  const passengers = (u.cargo ?? []).map((id) => v.units.find((x) => x.id === id)).filter((x): x is NonNullable<typeof x> => !!x);
  const roster = passengers.length ? h('div', { class: 'passengers' }, ...passengers.map((p) => {
    const b = h('button', { class: 'round', type: 'button', title: `${UNITS[p.type].name} — give orders`, 'aria-label': UNITS[p.type].name }, icon(p.type, 18));
    b.addEventListener('click', (e) => { e.stopPropagation(); gs.selectUnit(p.id); });
    return b;
  })) : null;
  put(el,
    h('div', { class: 'head' },
      h('div', { class: 'flag' }, flag, artIcon(def.id, 28)),
      h('div', { style: { flex: '1', minWidth: '0' } }, h('h3', null, def.name), stats),
      hint,
      !def.civilian ? h('div', { class: 'hpnum', title: 'Health' }, icon('health', 15), h('b', { class: 'num' }, String(u.hp))) : null),
    !def.civilian ? h('div', { class: 'bar hp' }, h('i', { style: { width: `${u.hp}%`, background: u.hp > 60 ? 'var(--good)' : u.hp > 30 ? '#e8c14a' : 'var(--bad)' } })) : null,
    roster,
    actions);
}

// ------------------------------------------------------------------ turn control
export function renderTurnControl(gs: GameScreen, el: HTMLElement): void {
  clear(el);
  const st = gs.primaryState();
  const ready = st.kind === 'end';
  const rim = document.createElementNS(NS, 'svg');
  rim.setAttribute('viewBox', '0 0 104 104');
  rim.setAttribute('class', 'rim');
  const circle = (r: number, stroke: string, w: number) => {
    const c = document.createElementNS(NS, 'circle');
    c.setAttribute('cx', '52');
    c.setAttribute('cy', '52');
    c.setAttribute('r', String(r));
    c.setAttribute('fill', 'none');
    c.setAttribute('stroke', stroke);
    c.setAttribute('stroke-width', String(w));
    return c;
  };
  rim.append(circle(50, ready ? '#f2d98c' : '#8a6d2f', 2), circle(46.5, 'rgba(0,0,0,0.6)', 3), circle(44, ready ? '#d4af5a' : '#5a4820', 1));
  // 24 tick marks around the bezel, like a dial
  for (let i = 0; i < 24; i++) {
    const a = (i / 24) * Math.PI * 2;
    const long = i % 6 === 0;
    const t = document.createElementNS(NS, 'line');
    t.setAttribute('x1', String(52 + Math.cos(a) * (long ? 41 : 42.5)));
    t.setAttribute('y1', String(52 + Math.sin(a) * (long ? 41 : 42.5)));
    t.setAttribute('x2', String(52 + Math.cos(a) * 44));
    t.setAttribute('y2', String(52 + Math.sin(a) * 44));
    t.setAttribute('stroke', ready ? '#f2d98c' : '#8a6d2f');
    t.setAttribute('stroke-width', long ? '1.6' : '1');
    rim.append(t);
  }
  if (st.kind === 'wait') {
    const orbit = circle(48, '#f2d98c', 2);
    orbit.setAttribute('class', 'spin');
    orbit.setAttribute('stroke-dasharray', '22 280');
    orbit.setAttribute('stroke-linecap', 'round');
    rim.append(orbit);
  }
  const iconName = { end: 'skip', unit: 'warrior', city: 'city', research: 'science', policy: 'culture', wait: 'turn' }[st.kind];
  const btn = h('button', { class: `nextturn ${ready ? 'ready' : ''}`, type: 'button', 'aria-label': st.label }, h('span', { class: 'halo' }), rim, h('span', { class: 'face' }, icon(iconName, 40)));
  btn.disabled = gs.busy;
  btn.addEventListener('click', () => gs.primaryAction());
  put(el, h('div', { class: 'turnno' }, `Turn ${gs.view.turn}`), btn, h('div', { class: 'turnlabel' }, st.label, st.sub ? h('small', null, st.sub) : null));
  if (st.kind !== 'end' && st.kind !== 'wait') el.append(h('button', { class: 'link', type: 'button', style: { fontSize: '12px' }, on: { click: () => void gs.endTurn() } }, 'End turn anyway'));
  if (gs.opts.hostActions && st.kind === 'wait') el.append(h('button', { class: 'link', type: 'button', style: { fontSize: '12px' }, on: { click: () => gs.opts.hostActions!.forceEnd() } }, 'Skip waiting (host)'));
}

// ------------------------------------------------------------------ map dock (lenses)
export function renderLenses(gs: GameScreen, el: HTMLElement): void {
  clear(el);
  const mk = (name: string, title: string, on: boolean, fn: () => void, extra = '') => {
    const b = h('button', { class: `round ${on ? 'primary' : ''} ${extra}`, type: 'button', title, 'aria-label': title }, icon(name, 18));
    b.addEventListener('click', () => {
      fn();
      renderLenses(gs, el);
    });
    return b;
  };
  put(el,
    mk('food', 'Show tile yields (Y)', gs.showYields, () => gs.togglePref('yields')),
    mk('city', 'Centre on capital', false, () => { const c = gs.view.cities.find((x) => x.mine && x.capital) ?? gs.myCities()[0]; if (c) gs.focus(c.x, c.y); }),
    mk('grid', 'Grid lines (G)', gs.showGrid, () => gs.togglePref('grid'), 'hide-phone'),
    mk(isMuted() ? 'mute' : 'sound', 'Sound (M)', !isMuted(), () => setMuted(!isMuted()), 'hide-phone'),
  );
}

// ------------------------------------------------------------------ hovered tile
export function renderTileInfo(gs: GameScreen, el: HTMLElement, tile: { x: number; y: number } | null): void {
  clear(el);
  const v = gs.view;
  if (!tile) return;
  const W = v.settings.width;
  const i = tile.y * W + tile.x;
  if (v.state[i] === 0) {
    put(el, h('b', null, 'Unexplored'));
    return;
  }
  const terr = TERRAIN[TERRAIN_LIST[v.terrain[i]]];
  const y = tileYieldFromView(v, i);
  const owner = v.owner[i] - 1;
  const res = v.resource[i] > 0 ? RESOURCES[RESOURCE_LIST[v.resource[i] - 1]] : null;
  put(el,
    h('b', null, terr.name),
    h('span', { class: 'food' }, icon('food', 15), String(y.food)),
    h('span', { class: 'prod' }, icon('production', 15), String(y.prod)),
    h('span', { class: 'coin' }, icon('gold', 15), String(y.gold)),
    res ? h('span', null, icon(RESOURCE_LIST[v.resource[i] - 1], 15), res.name) : null,
    v.improvement[i] ? h('span', null, icon(v.improvement[i] === 1 ? 'farm' : 'mine', 15), v.improvement[i] === 1 ? 'Farm' : 'Mine') : null,
    v.road[i] ? h('span', null, icon('road', 15), 'Road') : null,
    v.hut[i] ? h('span', null, icon('hut', 15), 'Tribal village') : null,
    terr.defense ? h('span', null, icon('shield', 15), `+${terr.defense}%`) : null,
    owner >= 0 ? h('span', null, h('span', { class: 'dot', style: { background: civColor(v, owner) } }), v.players[owner].name) : null,
  );
  for (const q of v.units.filter((o) => o.x === tile.x && o.y === tile.y && o.owner !== v.me)) {
    el.append(h('span', null, icon(q.type, 15), `${UNITS[q.type].name} · ${v.players[q.owner].name} · ${q.hp} HP`));
  }
  const city = v.cities.find((c) => c.x === tile.x && c.y === tile.y && !c.mine);
  if (city) el.append(h('span', null, icon('city', 15), `${city.name} · pop ${city.pop} · ${city.hp}/${city.maxHp} HP`));
  void POLICY_BY_ID;
}
