import { CIV_BY_ID, ERA_NAMES, IMPROVEMENTS, POLICY_BY_ID, RESOURCES, TECH_BY_ID, TERRAIN, UNITS, policyCost } from '../../shared/data';
import { RESOURCE_LIST, TERRAIN_LIST } from '../../shared/view';
import type { WorkKind } from '../../shared/types';
import type { GameScreen } from '../gamescreen';
import { civColor, tileYieldFromView } from '../render';
import { isMuted, setMuted } from '../audio';
import { icon } from '../icons';
import { clear, fmtSigned, h, put } from './dom';

const NS = 'http://www.w3.org/2000/svg';

export function emblem(civId: string, size: number, label?: string): HTMLElement {
  const civ = CIV_BY_ID[civId];
  const e = h('span', { class: 'emblem', style: { width: `${size}px`, height: `${size}px`, background: civ.color, fontSize: `${Math.round(size * 0.46)}px` } }, label ?? civ.name[0]);
  return e;
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
  put(el,
    h('div', { class: 'pad-medals' }),
    h('div', { class: 'stat', title: 'Treasury and income per turn' }, icon('gold', 20, 'coin'), h('span', { class: 'num' }, String(v.gold)), h('small', { class: v.income.gold >= 0 ? 'pos' : 'neg' }, fmtSigned(v.income.gold))),
    h('span', { class: 'sep' }),
    h('div', { class: 'stat', title: 'Science per turn' }, icon('science', 20, 'sci'), h('span', { class: 'num' }, `+${v.income.science}`)),
    h('span', { class: 'sep' }),
    h('div', { class: 'stat', title: 'Culture per turn' }, icon('culture', 20, 'cult'), h('span', { class: 'num' }, `+${v.income.culture}`)),
    v.settings.turnTimer > 0 ? h('span', { class: 'sep' }) : null,
    v.settings.turnTimer > 0 ? h('div', { class: 'stat', title: 'Time left in this turn' }, icon('turn', 20), h('span', { class: 'num', id: 'timerlabel' }, '--')) : null,
    h('div', { class: 'grow' }),
    rankings,
    h('div', { class: 'era', title: civ.bonus }, `Turn ${v.turn} / ${v.settings.turnLimit}`, h('b', null, `${ERA_NAMES[currentEra(v.techs)]} Era`)),
    h('span', { class: 'sep' }),
    gs.opts.sendChat ? roundFlat('chat', 'Chat ( / )', () => gs.toggleChat()) : null,
    roundFlat('city', 'Your cities (L)', () => gs.openModal('cities'), 'hide-phone'),
    roundFlat('diplomacy', 'Diplomacy (D)', () => gs.openDiplomacy()),
    roundFlat('log', 'Message log', () => gs.openModal('log'), 'hide-phone'),
    roundFlat('menu', 'Menu (Esc)', () => gs.openModal('menu')),
  );
  gs.updateTimerLabel();
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
    n.remove();
  });
  host.append(n);
  while (host.children.length > 7) host.firstChild?.remove();
  setTimeout(() => n.remove(), kind === 'war' || kind === 'wonder' || kind === 'tech' ? 12000 : 7000);
}

// ------------------------------------------------------------------ unit bar
export function renderUnitBar(gs: GameScreen, el: HTMLElement): void {
  clear(el);
  const u = gs.selUnit;
  if (!u) {
    el.classList.add('hidden');
    return;
  }
  el.classList.remove('hidden');
  const v = gs.view;
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
  actions.append(h('span', { class: 'gap' }));
  act('disband', 'Disband (Del)', () => void gs.disband(u), { danger: true });

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
  );
  put(el,
    h('div', { class: 'head' },
      h('div', { class: 'flag' }, flag, icon(def.id, 24)),
      h('div', { style: { flex: '1', minWidth: '0' } }, h('h3', null, def.name), stats),
      hint),
    !def.civilian ? h('div', { class: 'bar hp' }, h('i', { style: { width: `${u.hp}%`, background: u.hp > 60 ? 'var(--good)' : u.hp > 30 ? '#e8c14a' : 'var(--bad)' } })) : null,
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
  const iconName = { end: 'skip', unit: 'warrior', city: 'city', research: 'science', policy: 'culture', wait: 'turn' }[st.kind];
  const btn = h('button', { class: `nextturn ${ready ? 'ready' : ''}`, type: 'button', 'aria-label': st.label }, rim, h('span', { class: 'face' }, icon(iconName, 40)));
  btn.disabled = gs.busy;
  btn.addEventListener('click', () => gs.primaryAction());
  put(el, btn, h('div', { class: 'turnlabel' }, st.label, st.sub ? h('small', null, st.sub) : null));
  if (st.kind !== 'end' && st.kind !== 'wait') el.append(h('button', { class: 'link', type: 'button', style: { fontSize: '12px' }, on: { click: () => void gs.endTurn() } }, 'End turn anyway'));
  if (gs.opts.hostActions && st.kind === 'wait') el.append(h('button', { class: 'link', type: 'button', style: { fontSize: '12px' }, on: { click: () => gs.opts.hostActions!.forceEnd() } }, 'Skip waiting (host)'));
}

// ------------------------------------------------------------------ map dock (lenses)
export function renderLenses(gs: GameScreen, el: HTMLElement): void {
  clear(el);
  const mk = (name: string, title: string, on: boolean, fn: () => void) => {
    const b = h('button', { class: `round ${on ? 'primary' : ''}`, type: 'button', title, 'aria-label': title }, icon(name, 18));
    b.addEventListener('click', () => {
      fn();
      renderLenses(gs, el);
    });
    return b;
  };
  put(el,
    mk('food', 'Show tile yields (Y)', gs.showYields, () => gs.togglePref('yields')),
    mk('grid', 'Grid lines (G)', gs.showGrid, () => gs.togglePref('grid')),
    mk('city', 'Centre on capital', false, () => { const c = gs.view.cities.find((x) => x.mine && x.capital) ?? gs.myCities()[0]; if (c) gs.focus(c.x, c.y); }),
    mk(isMuted() ? 'mute' : 'sound', 'Sound (M)', !isMuted(), () => setMuted(!isMuted())),
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
