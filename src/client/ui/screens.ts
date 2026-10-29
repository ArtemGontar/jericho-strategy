import { BUILDINGS, CIV_BY_ID, ERA_NAMES, POLICIES, TECHS, TECH_BY_ID, UNITS, policyCost } from '../../shared/data';
import type { TechDef } from '../../shared/data';
import type { GameScreen } from '../gamescreen';
import { isMuted, setMuted, sfx } from '../audio';
import { animSpeed, setAnimSpeed, type AnimSpeed } from '../anim';
import { GRAPHICS_LABEL, graphicsPref, setGraphicsPref, type Graphics } from '../gl/glrenderer';
import { icon } from '../icons';
import { askConfirm, btn, clear, h, put, turnsLeft } from './dom';
import { currentEra, emblem } from './hud';

const NS = 'http://www.w3.org/2000/svg';

export function sheet(title: string, body: HTMLElement | Array<HTMLElement | null>, onClose: () => void, opts: { narrow?: boolean; full?: boolean; right?: Array<HTMLElement | null> } = {}): HTMLElement {
  const close = h('button', { class: 'round flat', type: 'button', title: 'Close (Esc)', 'aria-label': 'Close' }, icon('close', 20));
  close.addEventListener('click', onClose);
  const s = h('div', { class: `sheet ${opts.narrow ? 'narrow' : ''}` },
    h('header', null, h('h2', null, title), ...(opts.right ?? []), close),
    h('hr', { class: 'rule' }),
    h('div', { class: 'body' }, body));
  if (opts.full) s.dataset.full = '1';
  return s;
}

// ------------------------------------------------------------------ technology tree
const TECH_ICON: Record<string, string> = {
  pottery: 'granary', mining: 'mine', archery: 'archer', bronze: 'spearman', husbandry: 'horses', masonry: 'walls', writing: 'library', wheel: 'road',
  ironworking: 'swordsman', mathematics: 'catapult', currency: 'market', philosophy: 'temple', construction: 'aqueduct',
  education: 'university', engineering: 'crossbowman', feudalism: 'pikeman', chivalry: 'knight', banking: 'bank',
  gunpowder: 'musketman', metallurgy: 'cannon', astronomy: 'observatory', enlightenment: 'science',
};

const NODE_W = 200;
const NODE_H = 58;
const STEP_X = 232;
const STEP_Y = 72;
const TOP = 52;

interface Placed {
  t: TechDef;
  x: number;
  y: number;
}

function layoutTree(): { nodes: Map<string, Placed>; width: number; height: number; eraX: number[] } {
  // sub-column inside an era = length of the longest chain of same-era prerequisites
  const sub = new Map<string, number>();
  const subOf = (t: TechDef): number => {
    const known = sub.get(t.id);
    if (known !== undefined) return known;
    let d = 0;
    for (const p of t.pre) {
      const pt = TECH_BY_ID[p];
      if (pt.era === t.era) d = Math.max(d, subOf(pt) + 1);
    }
    sub.set(t.id, d);
    return d;
  };
  TECHS.forEach(subOf);
  const nodes = new Map<string, Placed>();
  const eraX: number[] = [];
  let cursor = 24;
  let maxY = 0;
  for (let era = 0; era < 4; era++) {
    const inEra = TECHS.filter((t) => t.era === era);
    const cols = Math.max(...inEra.map((t) => sub.get(t.id)!)) + 1;
    eraX.push(cursor);
    for (let c = 0; c < cols; c++) {
      const list = inEra.filter((t) => sub.get(t.id) === c);
      const bary = (t: TechDef) => {
        const ys = t.pre.map((p) => nodes.get(p)?.y).filter((v): v is number => v !== undefined);
        return ys.length ? ys.reduce((a, b) => a + b, 0) / ys.length : Infinity;
      };
      list.sort((a, b) => bary(a) - bary(b) || TECHS.indexOf(a) - TECHS.indexOf(b));
      // place each node at its barycentre, pushed down so rows never overlap
      let floor = TOP;
      for (const t of list) {
        const want = Number.isFinite(bary(t)) ? bary(t) : floor;
        const y = Math.max(floor, want);
        nodes.set(t.id, { t, x: cursor + c * STEP_X, y });
        floor = y + STEP_Y;
        maxY = Math.max(maxY, y);
      }
    }
    cursor += cols * STEP_X + 36;
  }
  return { nodes, width: cursor, height: maxY + NODE_H + 40, eraX };
}

const layoutCache = layoutTree();

export function showTechTree(gs: GameScreen): HTMLElement {
  const v = gs.view;
  const { nodes, width, height, eraX } = layoutCache;
  const tree = h('div', { class: 'tree', style: { width: `${width}px`, height: `${height}px` } });
  const svg = document.createElementNS(NS, 'svg');
  svg.setAttribute('class', 'links');
  svg.setAttribute('width', String(width));
  svg.setAttribute('height', String(height));
  tree.append(svg);
  for (let era = 0; era < 4; era++) tree.append(h('div', { class: 'era-label', style: { left: `${eraX[era]}px` } }, ERA_NAMES[era]));
  const stateOf = (t: TechDef) => (v.techs.includes(t.id) ? 'known' : v.researching === t.id ? 'cur' : v.availableTechs.includes(t.id) ? 'avail' : 'locked');
  for (const { t, x, y } of nodes.values()) {
    for (const pre of t.pre) {
      const a = nodes.get(pre)!;
      const x1 = a.x + NODE_W;
      const y1 = a.y + NODE_H / 2;
      const x2 = x;
      const y2 = y + NODE_H / 2;
      const p = document.createElementNS(NS, 'path');
      const mid = Math.max(24, (x2 - x1) / 2);
      p.setAttribute('d', `M${x1} ${y1} C${x1 + mid} ${y1} ${x2 - mid} ${y2} ${x2} ${y2}`);
      const st = stateOf(t);
      p.setAttribute('class', `link ${v.techs.includes(pre) && st === 'known' ? 'lit' : v.techs.includes(pre) && (st === 'avail' || st === 'cur') ? 'next' : ''}`);
      svg.append(p);
    }
  }
  for (const { t, x, y } of nodes.values()) {
    const st = stateOf(t);
    const cost = v.techCosts[t.id];
    const missing = t.pre.filter((p) => !v.techs.includes(p)).map((p) => TECH_BY_ID[p].name);
    const sub = st === 'known' ? 'Discovered' : st === 'cur' ? `${turnsLeft(v.researchCost - v.research, v.income.science)} turns left` : st === 'avail' ? `${cost} · ${turnsLeft(cost, v.income.science)} turns` : `Needs ${missing.join(', ')}`;
    const node = h('div', { class: `node ${st}`, style: { left: `${x}px`, top: `${y}px` }, title: `Unlocks: ${t.desc}` },
      h('span', { class: 'badge' }, icon(TECH_ICON[t.id] ?? 'science', 22)),
      h('div', null, h('b', null, t.name), h('span', null, st === 'locked' ? sub : `${t.desc} · ${sub}`)));
    if (st === 'cur' && v.researchCost) node.append(h('i', { class: 'prog', style: { width: `${Math.min(100, (v.research / v.researchCost) * 100)}%` } }));
    if (st === 'avail') {
      node.addEventListener('click', async () => {
        if (await gs.cmd({ t: 'research', tech: t.id })) {
          sfx('click');
          gs.closeModal();
        }
      });
    }
    tree.append(node);
  }
  const wrap = h('div', { class: 'treewrap' }, tree);
  const researching = v.researching ? TECH_BY_ID[v.researching] : null;
  const s = h('div', { class: 'sheet', style: { display: 'flex', flexDirection: 'column', height: '100%' } },
    h('header', null,
      h('h2', null, 'Technology'),
      h('span', { class: 'muted' }, researching ? `Researching ${researching.name}` : 'Choose a technology to research'),
      h('span', { class: 'sci', style: { display: 'inline-flex', alignItems: 'center', gap: '6px', fontWeight: '600' } }, icon('science', 18), `+${v.income.science} per turn`),
      (() => {
        const c = h('button', { class: 'round flat', type: 'button', title: 'Close (Esc)' }, icon('close', 20));
        c.addEventListener('click', () => gs.closeModal());
        return c;
      })()),
    h('hr', { class: 'rule' }), wrap);
  s.dataset.full = '1';
  // bring the current frontier into view
  setTimeout(() => {
    const focus = researching ? nodes.get(researching.id) : [...nodes.values()].filter((n) => stateOf(n.t) === 'avail').sort((a, b) => a.x - b.x)[0];
    if (focus) wrap.scrollLeft = Math.max(0, focus.x - 120);
  }, 0);
  return s;
}

// ------------------------------------------------------------------ policies
export function showPolicies(gs: GameScreen): HTMLElement {
  const v = gs.view;
  const body: HTMLElement[] = [];
  if (v.policyChoices.length) {
    const choices = h('div', { class: 'choices' });
    for (const id of v.policyChoices) {
      const p = POLICIES.find((x) => x.id === id)!;
      choices.append(h('div', { class: 'choice' },
        icon(p.icon, 44), h('h3', null, p.name), h('p', null, p.desc),
        btn('Adopt', async () => {
          if (await gs.cmd({ t: 'policy', id })) {
            sfx('tech');
            gs.openModal('policies');
          }
        }, 'primary')));
    }
    body.push(h('div', { class: 'eyebrow' }, 'Your culture has inspired a new policy'), choices);
  } else {
    const speed = v.settings.speed === 'quick' ? 0.67 : 1;
    const prev = v.policies.length ? policyCost(v.policies.length - 1, speed) : 0;
    const pct = Math.max(0, Math.min(100, ((v.culture - prev) / Math.max(1, v.policyCost - prev)) * 100));
    body.push(
      v.policies.length >= POLICIES.length
        ? h('p', { class: 'muted' }, 'You have adopted every policy.')
        : h('div', { class: 'meter', style: { display: 'grid', gap: '6px' } },
          h('div', { class: 'row', style: { display: 'flex', justifyContent: 'space-between' } }, h('span', null, 'Next policy'), h('span', { class: 'num cult' }, `${v.culture} / ${v.policyCost} culture`)),
          h('div', { class: 'bar' }, h('i', { style: { width: `${pct}%`, background: 'var(--cult)' } })),
          h('div', { class: 'hint' }, `You earn +${v.income.culture} culture per turn from buildings, wonders and cities.`)));
  }
  const adopted = h('div');
  for (const id of v.policies) {
    const p = POLICIES.find((x) => x.id === id)!;
    adopted.append(h('div', { class: 'item' }, icon(p.icon, 26, 'big'), h('div', null, h('div', { class: 'nm' }, p.name), h('div', { class: 'ds' }, p.desc)), h('span'), h('span')));
  }
  if (!v.policies.length) adopted.append(h('div', { class: 'hint' }, 'No policies adopted yet.'));
  body.push(h('div', { class: 'eyebrow' }, 'Adopted'), adopted);
  return sheet('Policies', body, () => gs.closeModal(), { narrow: !v.policyChoices.length });
}

// ------------------------------------------------------------------ diplomacy
let diploFocus = -1;
export function showDiplomacy(gs: GameScreen, focusId?: number): HTMLElement {
  const v = gs.view;
  const me = v.players[v.me];
  if (focusId !== undefined) diploFocus = focusId;
  const others = v.players.filter((p) => p.id !== v.me);
  if (!others.some((p) => p.id === diploFocus)) diploFocus = (others.find((p) => p.relation !== 'unmet' && p.alive) ?? others[0])?.id ?? -1;
  const sel = v.players[diploFocus];

  const list = h('div', { class: 'list' });
  for (const p of others) {
    const pending = v.proposals.some((q) => q.to === v.me && q.from === p.id);
    const rel = p.relation === 'war' ? 'War' : p.relation === 'peace' ? 'Peace' : 'Unmet';
    const row = h('button', { class: `who ${p.id === diploFocus ? 'on' : ''}`, type: 'button' },
      emblem(p.civ, 34),
      h('div', null, h('b', null, CIV_BY_ID[p.civ].leader), h('small', { class: p.alive ? `rel-${p.relation}` : '' }, p.alive ? rel : 'Eliminated')),
      pending ? h('span', { class: 'pill gold' }, icon('peace', 16)) : h('span', { class: 'num muted' }, String(p.score)));
    row.addEventListener('click', () => gs.openDiplomacy(p.id));
    list.append(row);
  }

  const detail = h('div', { class: 'detail' });
  if (sel) {
    const civ = CIV_BY_ID[sel.civ];
    const strength = sel.relation === 'unmet' ? '—' : sel.power > me.power * 1.3 ? 'Stronger' : sel.power < me.power * 0.7 ? 'Weaker' : 'Even';
    put(detail,
      h('div', { style: { display: 'flex', gap: '18px', alignItems: 'center' } },
        emblem(sel.civ, 76),
        h('div', null, h('h2', { style: { fontSize: '24px' } }, civ.leader), h('div', { class: 'muted' }, `${sel.name} · ${civ.name}${sel.ai ? ' · computer' : ''}`), h('div', { style: { color: 'var(--gold)', fontSize: '13px', marginTop: '4px' } }, civ.bonus))),
      h('hr', { class: 'rule soft' }),
      h('div', { class: 'facts' },
        h('div', null, h('small', null, 'Relation'), h('b', { class: `rel-${sel.relation}` }, sel.relation === 'war' ? 'At war' : sel.relation === 'peace' ? `Peace (${sel.relationAge}t)` : 'Not met')),
        h('div', null, h('small', null, 'Score'), h('b', { class: 'num' }, String(sel.score))),
        h('div', null, h('small', null, 'Cities'), h('b', { class: 'num' }, String(sel.cityCount))),
        h('div', null, h('small', null, 'Army'), h('b', null, strength))));
    for (const p of v.proposals.filter((q) => q.to === v.me && q.from === sel.id)) {
      detail.append(h('div', { class: 'row', style: { display: 'flex', gap: '12px', alignItems: 'center', flexWrap: 'wrap' } },
        icon('peace', 20, 'pos'), h('span', null, `${sel.name} proposes peace.`),
        btn('Accept', async () => { await gs.cmd({ t: 'accept', proposal: p.id, accept: true }); }, 'small primary'),
        btn('Decline', async () => { await gs.cmd({ t: 'accept', proposal: p.id, accept: false }); }, 'small')));
    }
    const actions = h('div', { style: { display: 'flex', gap: '10px', flexWrap: 'wrap' } });
    if (sel.alive) {
      if (sel.relation === 'war') {
        const pending = v.proposals.some((q) => q.from === v.me && q.to === sel.id);
        const b = btn(pending ? 'Peace offered…' : 'Propose peace', async () => { await gs.cmd({ t: 'peace', to: sel.id }); }, '');
        b.disabled = pending;
        actions.append(b);
      } else if (sel.relation === 'peace') {
        const can = sel.relationAge >= 10;
        const b = btn('Declare war', async () => { if (await askConfirm(`Declare war on ${sel.name}?`, 'Declare war')) await gs.cmd({ t: 'war', to: sel.id }); }, 'danger', can ? '' : `The treaty must hold for 10 turns (${sel.relationAge} so far)`);
        b.disabled = !can;
        actions.append(b);
      }
      if (sel.relation !== 'unmet') {
        for (const amt of [25, 100]) {
          const g = btn([icon('gift', 16), `Gift ${amt}`], async () => { await gs.cmd({ t: 'gift', to: sel.id, amount: amt }); }, '', `Give ${amt} gold to ${sel.name}`);
          g.disabled = v.gold < amt;
          actions.append(g);
        }
      }
      if (!sel.ai && !sel.connected && gs.opts.hostActions) actions.append(btn('Give to computer', () => gs.opts.hostActions!.takeover(sel.id), '', 'Replace this disconnected player with the computer'));
    }
    detail.append(actions);
    detail.append(h('div', { class: 'hint' }, sel.relation === 'unmet' ? 'Send a unit near their borders to meet them.' : 'Peace keeps armies out of each other’s borders and must hold for 10 turns before you can break it.'));
  } else detail.append(h('p', { class: 'muted' }, 'There is nobody else in this game.'));

  return sheet('Diplomacy', h('div', { class: 'diplo' }, list, detail), () => gs.closeModal());
}

// ------------------------------------------------------------------ menu
export function showMenu(gs: GameScreen): HTMLElement {
  const item = (label: string, fn: () => void, lead = false) => {
    const b = h('button', { type: 'button', class: lead ? 'lead' : '' }, label);
    b.addEventListener('click', fn);
    return b;
  };
  const list = h('div', { class: 'menu-list' });
  list.append(
    item('Resume', () => gs.closeModal(), true),
    item('Policies', () => gs.openModal('policies')),
    item('How to play', () => gs.openModal('help')),
    item(`Sound · ${isMuted() ? 'off' : 'on'}`, () => { setMuted(!isMuted()); gs.openModal('menu'); }),
    item(`Graphics · ${GRAPHICS_LABEL[graphicsPref()]}`, async () => {
      const order: Graphics[] = ['auto', 'high', 'medium', 'low', 'flat'];
      const next = order[(order.indexOf(graphicsPref()) + 1) % order.length];
      setGraphicsPref(next);
      if (await askConfirm(`Graphics set to ${GRAPHICS_LABEL[next]}. Reload the page to apply it? Your game is saved.`, 'Reload')) location.reload();
      else gs.openModal('menu');
    }),
    item(`Animations · ${animSpeed()}`, () => { const o: AnimSpeed[] = ['normal', 'fast', 'instant']; setAnimSpeed(o[(o.indexOf(animSpeed()) + 1) % 3]); gs.openModal('menu'); }),
    item(`Tile yields · ${gs.showYields ? 'on' : 'off'}`, () => { gs.togglePref('yields'); gs.openModal('menu'); }),
    item(`Grid lines · ${gs.showGrid ? 'on' : 'off'}`, () => { gs.togglePref('grid'); gs.openModal('menu'); }),
  );
  if (gs.opts.sendChat) list.append(item('Chat', () => { gs.closeModal(); gs.toggleChat(); }));
  const body: HTMLElement[] = [list];
  const invite = gs.opts.invite;
  if (invite) {
    const link = `${location.origin}${location.pathname}?room=${invite.code}`;
    body.push(h('div', { style: { display: 'grid', gap: '6px', justifyItems: 'center', textAlign: 'center' } },
      h('div', { class: 'eyebrow' }, 'Room code'), h('div', { class: 'code', style: { fontSize: '34px' } }, invite.code),
      btn('Copy invite link', () => { void navigator.clipboard?.writeText(link).catch(() => undefined); gs.toast('Invite link copied'); }, 'small')));
  }
  body.push(item('Leave game', async () => {
    if (await askConfirm(gs.transport.kind === 'local' ? 'Leave to the main menu? Your game is saved automatically each turn.' : 'Leave the game? You can rejoin with the room code.', 'Leave')) gs.opts.onExit();
  }));
  return sheet('Menu', body, () => gs.closeModal(), { narrow: true });
}

// ------------------------------------------------------------------ help
export function showHelp(gs: GameScreen): HTMLElement {
  const limit = gs.view?.settings.turnLimit ?? 200;
  const grid = h('div', { class: 'helpgrid' },
    h('div', null, h('h4', null, 'Getting started'),
      h('p', null, 'Select your Settler and press ', h('kbd', null, 'B'), ' to found your capital. Seek food and a few hills, ideally by the coast. Your Warrior guards it, the Scout explores, the Worker improves land.'),
      h('p', null, 'Walk onto a ', h('b', null, 'tribal village'), ' for gold, knowledge, a free unit or a map.')),
    h('div', null, h('h4', null, 'Cities'),
      h('p', null, 'Click a city (or press ', h('kbd', null, 'C'), ') to choose what it builds. Spend gold to buy items. Click a highlighted tile on the map to pin a citizen to it.'),
      h('p', null, 'Cities must be 3 tiles apart and grow beyond 8 only with an Aqueduct.')),
    h('div', null, h('h4', null, 'Research and culture'),
      h('p', null, 'Press ', h('kbd', null, 'T'), ' for the technology tree. Culture fills the ring under the crest at the top left; each time it completes you adopt a policy with a lasting bonus.'),
      h('p', null, 'Discover ', h('b', null, 'Enlightenment'), ' and hold your capital for 12 turns to win by science. The whole world gets that long to take it from you.')),
    h('div', null, h('h4', null, 'War and peace'),
      h('p', null, 'Hover an enemy to preview a fight, click to attack. Ranged units strike two tiles away without retaliation. Fighting earns experience: veterans hit harder. Old units can be upgraded for gold inside your borders.'),
      h('p', null, 'Reduce a city to 0 HP and walk a melee unit in to capture it. You can only attack civilisations you are at war with; use Diplomacy (', h('kbd', null, 'D'), ').')),
    h('div', null, h('h4', null, 'Winning'),
      h('p', null, h('b', null, 'Conquest:'), ' be the last civilisation standing. ', h('b', null, 'Science:'), ' discover Enlightenment and keep your capital for 12 turns. ', h('b', null, 'Score:'), ` hold the highest score when turn ${limit} ends.`)),
    h('div', null, h('h4', null, 'Controls'),
      h('ul', null,
        h('li', null, h('kbd', null, 'Click'), ' select, move, attack · right-click always commands'),
        h('li', null, 'Drag or arrow keys to pan · wheel or pinch to zoom'),
        h('li', null, h('kbd', null, 'Enter'), ' next action · ', h('kbd', null, 'N'), ' next unit · ', h('kbd', null, 'Space'), ' skip'),
        h('li', null, h('kbd', null, 'F'), ' fortify · ', h('kbd', null, 'Z'), ' sleep · ', h('kbd', null, 'A'), ' automate'),
        h('li', null, h('kbd', null, 'T'), ' tech · ', h('kbd', null, 'D'), ' diplomacy · ', h('kbd', null, 'L'), ' cities · ', h('kbd', null, 'Y'), ' yields'),
        h('li', null, 'On touch, tap a destination, then tap it again.'))));
  void BUILDINGS;
  void UNITS;
  return sheet('How to play', grid, () => gs.closeModal());
}

// ------------------------------------------------------------------ end of game
export function showVictory(gs: GameScreen): HTMLElement {
  const v = gs.view;
  const winner = v.winner !== null ? v.players[v.winner] : null;
  const won = !!winner && winner.id === v.me;
  const how: Record<string, string> = { science: 'a scientific victory', score: 'the highest score', domination: 'domination', 'last-standing': 'conquest' };
  const rows = v.players.slice().sort((a, b) => b.score - a.score).map((p, i) =>
    h('tr', null, h('td', { class: 'num' }, String(i + 1)), h('td', null, emblem(p.civ, 22), ' ', p.name, p.id === v.winner ? ' ★' : ''), h('td', { class: 'num' }, String(p.score)), h('td', { class: 'num' }, `${p.cityCount} cities`), h('td', { class: 'num' }, `${p.techCount} techs`)));
  const footer = h('footer', null,
    btn('Main menu', () => gs.opts.onExit(), 'primary'),
    gs.opts.hostActions ? btn('Play again', () => gs.opts.hostActions!.rematch()) : null,
    btn('Keep looking at the map', () => gs.closeModal(), 'quiet'));
  const s = sheet(won ? 'Victory' : 'Game over', [
    h('div', { class: 'winner' }, icon(won ? 'crown' : 'skull', 54), h('h2', null, won ? 'You triumph' : winner ? `${winner.name} wins` : 'No winner'),
      h('p', { class: 'muted', style: { margin: '0' } }, winner ? `${CIV_BY_ID[winner.civ].name} achieves ${how[v.victory ?? ''] ?? v.victory}.` : '')),
    h('table', { class: 'table' }, h('thead', null, h('tr', null, ...['#', 'Civilisation', 'Score', '', ''].map((t) => h('th', null, t)))), h('tbody', null, ...rows)),
  ], () => gs.closeModal(), { narrow: true });
  s.append(footer);
  return s;
}

// ------------------------------------------------------------------ cities and log
export function showCities(gs: GameScreen): HTMLElement {
  const rows = gs.myCities().map((c) => {
    const head = c.queue?.[0];
    const info = head ? (head.kind === 'unit' ? UNITS[head.id as keyof typeof UNITS] : BUILDINGS[head.id as keyof typeof BUILDINGS]) : null;
    const tr = h('tr', { class: 'go' },
      h('td', null, (c.capital ? '★ ' : '') + c.name), h('td', { class: 'num' }, String(c.pop)),
      h('td', { class: `num ${c.yield!.surplus < 0 ? 'neg' : ''}` }, `${c.yield!.surplus >= 0 ? '+' : ''}${c.yield!.surplus}`), h('td', { class: 'num' }, String(c.yield!.prod)), h('td', { class: 'num' }, String(c.yield!.science)),
      h('td', null, info ? h('span', { style: { display: 'inline-flex', gap: '8px', alignItems: 'center' } }, icon(info.id, 18), `${info.name} · ${turnsLeft(head!.cost - (c.prod ?? 0), c.yield!.prod)}t`) : h('span', { class: 'neg' }, 'Idle')));
    tr.addEventListener('click', () => { gs.closeModal(); gs.focus(c.x, c.y); gs.openCityPanel(c.id); });
    return tr;
  });
  const body = rows.length
    ? h('table', { class: 'table' }, h('thead', null, h('tr', null, ...['City', 'Pop', 'Food', 'Prod', 'Sci', 'Building'].map((t) => h('th', null, t)))), h('tbody', null, ...rows))
    : h('p', { class: 'muted' }, 'You have no cities yet. Found one with your Settler (B).');
  return sheet('Your cities', body, () => gs.closeModal(), { narrow: false });
}

export function showLog(gs: GameScreen): HTMLElement {
  const icons: Record<string, string> = { info: 'log', good: 'star', bad: 'shield', war: 'swords', tech: 'science', wonder: 'wonder' };
  const items = gs.noteHistory.slice(-120).reverse().map((n) => {
    const row = h('div', { class: 'logline', style: { cursor: n.at ? 'pointer' : 'default' } }, h('span', { class: 'muted num' }, `T${n.turn}`), icon(icons[n.kind] ?? 'log', 18), h('span', null, n.text));
    if (n.at) row.addEventListener('click', () => { gs.closeModal(); gs.focus(n.at!.x, n.at!.y); });
    return row;
  });
  return sheet('Message log', h('div', null, ...(items.length ? items : [h('p', { class: 'muted' }, 'Nothing yet.')])), () => gs.closeModal(), { narrow: true });
}

void clear;
void currentEra;
