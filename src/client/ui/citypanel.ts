import { BUILDINGS, UNITS } from '../../shared/data';
import type { BuildingId, Focus, UnitTypeId } from '../../shared/types';
import type { BuildOption, ViewCity } from '../../shared/view';
import type { GameScreen } from '../gamescreen';
import { sfx } from '../audio';
import { artIcon } from '../artwork';
import { icon } from '../icons';
import { civColor } from '../render';
import { clear, fmtSigned, h, put, turnsLeft } from './dom';

let tab: 'buildings' | 'units' | 'wonders' = 'buildings';

function itemInfo(kind: 'unit' | 'building', id: string): { name: string; icon: string; desc: string } {
  if (kind === 'unit') {
    const d = UNITS[id as UnitTypeId];
    const stats = d.civilian ? '' : ` Strength ${d.strength}${d.ranged ? `, ranged ${d.ranged}` : ''}, ${d.moves} moves.`;
    return { name: d.name, icon: d.id, desc: d.desc + stats };
  }
  const b = BUILDINGS[id as BuildingId];
  return { name: b.name, icon: b.id, desc: b.desc };
}

const SVG_NS = 'http://www.w3.org/2000/svg';

/** An item icon inside a progress ring, used for what the city is building right now. */
function ringIcon(pct: number, name: string): HTMLElement {
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('viewBox', '0 0 52 52');
  svg.setAttribute('class', 'pring');
  const circ = 2 * Math.PI * 22;
  const mk = (stroke: string, w: number, extra: Record<string, string> = {}) => {
    const c = document.createElementNS(SVG_NS, 'circle');
    for (const [k, v] of Object.entries({ cx: '26', cy: '26', r: '22', fill: 'none', stroke, 'stroke-width': String(w), ...extra })) c.setAttribute(k, v);
    return c;
  };
  svg.append(mk('rgba(255,255,255,0.1)', 4), mk('var(--prod)', 4, { 'stroke-dasharray': `${Math.max(0.001, Math.min(100, pct) / 100) * circ} ${circ}`, transform: 'rotate(-90 26 26)', 'stroke-linecap': 'round' }));
  return h('div', { class: 'pdisc' }, svg, h('span', { class: 'pface' }, artIcon(name, 24, 'big')));
}

const FOCUS: Array<[Focus, string]> = [['balanced', 'Balanced'], ['food', 'Food'], ['production', 'Prod'], ['gold', 'Gold']];

export function renderCityPanel(gs: GameScreen, panel: HTMLElement, c: ViewCity): void {
  const y = c.yield!;
  const growTurns = y.surplus > 0 ? turnsLeft((c.foodNeeded ?? 0) - (c.food ?? 0), y.surplus) : y.surplus < 0 ? 'starving' : '∞';
  const atMax = (c.pop ?? 0) >= (c.maxPop ?? 8);
  const head = c.queue?.[0];
  const headCost = head?.cost ?? 0;

  const nav = (name: string, title: string, fn: () => void) => {
    const b = h('button', { class: 'round flat', type: 'button', title, 'aria-label': title }, icon(name, 18));
    b.addEventListener('click', fn);
    return b;
  };
  const header = h('header', { class: 'scene-city', style: `--civ: ${civColor(gs.view, gs.view.me)}` },
    h('span', { class: 'watermark', 'aria-hidden': 'true' }, icon('city', 168)),
    h('div', { class: 'titlerow' },
      nav('chevron', 'Previous city', () => gs.cycleCity(-1)),
      h('h2', null, (c.capital ? '★ ' : '') + c.name),
      nav('chevron', 'Next city', () => gs.cycleCity(1)),
      nav('close', 'Close (Esc)', () => gs.closeCity())),
    h('div', { class: 'chips' },
      h('span', null, icon('population', 15), `Population ${c.pop}`),
      h('span', null, icon('shield', 15), `Strength ${c.strength}`),
      h('span', null, icon('health', 15), `${c.hp}/${c.maxHp}`)));
  // mirror the chevron on the "previous" button
  (header.querySelector('.titlerow button svg') as SVGElement | null)?.setAttribute('style', 'transform: scaleX(-1)');

  const y1 = (label: string, value: string, cls: string, ic: string) =>
    h('div', { class: cls }, icon(ic, 20), h('span', { class: 'num' }, value), h('small', null, label));
  const yields = h('section', null,
    h('div', { class: 'yieldrow' },
      y1('Food', fmtSigned(y.surplus), 'food', 'food'),
      y1('Prod', String(y.prod), 'prod', 'production'),
      y1('Gold', fmtSigned(y.gold - y.upkeep), 'coin', 'gold'),
      y1('Science', String(y.science), 'sci', 'science'),
      y1('Culture', String(y.culture), 'cult', 'culture')),
    h('div', { class: 'meter' },
      h('div', { class: 'line' }, h('span', null, atMax ? 'Needs an Aqueduct to grow' : y.surplus < 0 ? 'Starving!' : `Grows in ${growTurns} turns`), h('span', { class: 'num' }, `${Math.floor(c.food ?? 0)} / ${c.foodNeeded}`)),
      h('div', { class: 'bar' }, h('i', { style: { width: `${Math.min(100, ((c.food ?? 0) / (c.foodNeeded || 1)) * 100)}%`, background: 'var(--food)' } }))));

  const seg = h('div', { class: 'seg' });
  for (const [f, label] of FOCUS) {
    const b = h('button', { class: c.focus === f ? 'on' : '', type: 'button' }, label);
    b.addEventListener('click', () => void gs.cmd({ t: 'focus', city: c.id, focus: f }));
    seg.append(b);
  }
  const citizens = h('section', null,
    h('div', { class: 'eyebrow' }, 'Citizens work'),
    seg,
    h('div', { class: 'hint' }, `Click a highlighted tile on the map to pin a citizen to it (${c.locked?.length ?? 0}/${c.pop} pinned).`));

  // production
  const prod = h('section', null, h('div', { class: 'eyebrow' }, 'Production'));
  if (head) {
    const info = itemInfo(head.kind, head.id);
    const pct = Math.min(100, ((c.prod ?? 0) / headCost) * 100);
    const remove = h('button', { class: 'round flat', type: 'button', title: 'Cancel' }, icon('close', 16));
    remove.addEventListener('click', () => void gs.cmd({ t: 'queue', city: c.id, op: 'remove', index: 0 }));
    prod.append(
      h('div', { class: 'item current' },
        ringIcon(pct, info.icon),
        h('div', null, h('div', { class: 'nm' }, info.name), h('div', { class: 'ds' }, info.desc)),
        h('div', { class: 'cost num' }, `${Math.floor(c.prod ?? 0)}/${headCost}`, h('br'), `${turnsLeft(headCost - (c.prod ?? 0), y.prod)} turns`),
        remove));
    if (c.buyPrice != null) {
      const canBuy = gs.view.gold >= c.buyPrice;
      const b = h('button', { class: 'btn small', type: 'button', title: canBuy ? 'Complete this item next turn' : `You need ${c.buyPrice} gold` }, icon('gold', 15), `Buy for ${c.buyPrice}`);
      b.disabled = !canBuy;
      b.addEventListener('click', async () => {
        if (await gs.cmd({ t: 'buy', city: c.id })) sfx('build');
      });
      prod.append(h('div', null, b));
    }
  } else prod.append(h('div', { class: 'hint' }, 'Nothing is being built. Pick something below.'));
  (c.queue ?? []).slice(1).forEach((q, i) => {
    const info = itemInfo(q.kind, q.id);
    const up = h('button', { class: 'round flat', type: 'button', title: 'Move up' }, icon('up', 16));
    up.addEventListener('click', () => void gs.cmd({ t: 'queue', city: c.id, op: 'up', index: i + 1 }));
    const rm = h('button', { class: 'round flat', type: 'button', title: 'Remove' }, icon('close', 16));
    rm.addEventListener('click', () => void gs.cmd({ t: 'queue', city: c.id, op: 'remove', index: i + 1 }));
    prod.append(h('div', { class: 'item' }, artIcon(info.icon, 22, 'big'), h('div', { class: 'nm' }, info.name), h('div', { class: 'cost num' }, String(q.cost)), h('span', null, up, rm)));
  });

  // options
  const all = c.options ?? [];
  const groups = {
    buildings: all.filter((o) => o.kind === 'building' && !BUILDINGS[o.id as BuildingId].wonder),
    units: all.filter((o) => o.kind === 'unit'),
    wonders: all.filter((o) => o.kind === 'building' && BUILDINGS[o.id as BuildingId].wonder),
  };
  if (!groups[tab].length) tab = groups.buildings.length ? 'buildings' : groups.units.length ? 'units' : groups.wonders.length ? 'wonders' : tab;
  const row = (o: BuildOption) => {
    const info = itemInfo(o.kind, o.id);
    const queued = c.queue?.some((q) => q.kind === o.kind && q.id === o.id);
    const add = h('button', { class: 'round flat', type: 'button', title: 'Add to queue' }, icon('plus', 16));
    add.addEventListener('click', async (e) => {
      e.stopPropagation();
      sfx('click');
      await gs.cmd({ t: 'queue', city: c.id, op: 'add', kind: o.kind, id: o.id });
    });
    const r = h('div', { class: `item pick ${queued ? 'dim' : ''}`, title: info.desc },
      artIcon(info.icon, 26, 'big'),
      h('div', null, h('div', { class: 'nm' }, info.name), h('div', { class: 'ds' }, info.desc)),
      h('div', { class: 'cost num' }, `${o.cost}`, h('br'), `${o.turns}t`),
      add);
    r.addEventListener('click', async () => {
      sfx('click');
      await gs.cmd({ t: 'queue', city: c.id, op: 'set', kind: o.kind, id: o.id });
    });
    return r;
  };
  // switching tab swaps only this section, so the rest of the drawer stays untouched
  const buildOptions = (): HTMLElement => {
    const kinds = ['buildings', 'units', 'wonders'] as const;
    const tabs = h('div', { class: 'tabrow' });
    for (const k of kinds) {
      const b = h('button', { class: tab === k ? 'on' : '', type: 'button' }, `${k} · ${groups[k].length}`);
      b.addEventListener('click', () => {
        tab = k;
        const next = buildOptions();
        options.replaceWith(next);
        options = next;
        next.querySelectorAll('button')[kinds.indexOf(k)]?.focus({ preventScroll: true });
      });
      tabs.append(b);
    }
    const list = h('div');
    for (const o of groups[tab]) list.append(row(o));
    if (!groups[tab].length) list.append(h('div', { class: 'hint', style: { padding: '10px 0' } }, tab === 'wonders' ? 'No wonders available yet. Research more technologies.' : 'Nothing available.'));
    return h('section', null, tabs, list);
  };
  let options = buildOptions();

  const have = h('section', null, h('div', { class: 'eyebrow' }, 'Built'), h('div', { class: 'chips' }));
  const chips = have.querySelector('.chips')!;
  for (const b of c.buildings ?? []) chips.append(h('span', { title: BUILDINGS[b].desc }, icon(b, 16), BUILDINGS[b].name));
  if (!c.buildings?.length) chips.append(h('span', { class: 'muted' }, 'No buildings yet.'));
  have.append(h('div', { class: 'hint' }, `Territory radius ${c.radius} · building upkeep ${y.upkeep} gold per turn`));

  clear(panel);
  panel.append(header, h('div', { class: 'body' }, yields, citizens, prod, options, have));
}
void put;
