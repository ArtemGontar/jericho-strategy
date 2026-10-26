import { BUILDINGS, UNITS } from '../../shared/data';
import type { BuildingId, Focus, UnitTypeId } from '../../shared/types';
import type { BuildOption, ViewCity } from '../../shared/view';
import type { GameScreen } from '../gamescreen';
import { sfx } from '../audio';
import { btn, clear, fmtSigned, h, turnsLeft } from './dom';

let tab: 'units' | 'buildings' | 'wonders' = 'buildings';

function itemInfo(kind: 'unit' | 'building', id: string): { name: string; icon: string; desc: string } {
  if (kind === 'unit') {
    const d = UNITS[id as UnitTypeId];
    const stats = d.civilian ? '' : ` ⚔${d.strength}${d.ranged ? ` 🏹${d.ranged}` : ''} · ${d.moves} moves`;
    return { name: d.name, icon: d.icon, desc: d.desc + stats };
  }
  const b = BUILDINGS[id as BuildingId];
  return { name: b.name, icon: b.icon, desc: b.desc };
}

export function renderCityPanel(gs: GameScreen, panel: HTMLElement, c: ViewCity): void {
  const y = c.yield!;
  const growTurns = y.surplus > 0 ? turnsLeft((c.foodNeeded ?? 0) - (c.food ?? 0), y.surplus) : y.surplus < 0 ? '☠' : '∞';
  const atMax = (c.pop ?? 0) >= (c.maxPop ?? 8);
  const head = c.queue?.[0];
  const headCost = head?.cost ?? 0;

  const header = h('header', null,
    btn('◀', () => gs.cycleCity(-1), 'small ghost', 'Previous city'),
    h('h3', { style: { flex: '1' } }, (c.capital ? '★ ' : '') + c.name),
    h('span', { class: 'pill' }, `Pop ${c.pop}`),
    btn('▶', () => gs.cycleCity(1), 'small ghost', 'Next city'),
    btn('✕', () => gs.closeCity(), 'small ghost', 'Close (Esc)'),
  );

  const stat = (label: string, value: string, sub: string, color: string) =>
    h('div', null, h('span', { style: { color } }, value), h('small', null, label + (sub ? ` ${sub}` : '')));
  const yields = h('div', { class: 'yields' },
    stat('Food', `🌿${fmtSigned(y.surplus)}`, '', 'var(--food)'),
    stat('Prod', `⚙${y.prod}`, '', 'var(--prod)'),
    stat('Gold', `🪙${y.gold - y.upkeep >= 0 ? '+' : ''}${y.gold - y.upkeep}`, '', 'var(--coin)'),
    stat('Science', `🔬${y.science}`, '', 'var(--sci)'),
    stat('Culture', `🎭${y.culture}`, '', 'var(--cult)'),
  );

  const growth = h('div', null,
    h('div', { class: 'row' }, h('span', { class: 'muted' }, atMax ? 'Needs an Aqueduct to grow' : `Growth in ${growTurns} turns`), h('span', { class: 'spacer' }), h('span', { class: 'muted' }, `${Math.floor(c.food ?? 0)}/${c.foodNeeded}`)),
    h('div', { class: 'bar food', style: { width: '100%' } }, h('i', { style: { width: `${Math.min(100, ((c.food ?? 0) / (c.foodNeeded || 1)) * 100)}%` } })),
  );

  const focusRow = h('div', { class: 'seg' });
  (['balanced', 'food', 'production', 'gold'] as Focus[]).forEach((f) => {
    const icons: Record<Focus, string> = { balanced: '⚖ Balanced', food: '🌿 Food', production: '⚙ Production', gold: '🪙 Gold' };
    focusRow.append(btn(icons[f], () => void gs.cmd({ t: 'focus', city: c.id, focus: f }), `small ${c.focus === f ? 'active' : ''}`, 'How citizens choose which tiles to work'));
  });

  // production
  const prod = h('div', { style: { display: 'grid', gap: '6px' } });
  if (head) {
    const info = itemInfo(head.kind, head.id);
    const pct = Math.min(100, ((c.prod ?? 0) / headCost) * 100);
    prod.append(
      h('div', { class: 'qitem cur' },
        h('span', { class: 'ic' }, info.icon),
        h('div', null, h('b', null, info.name), h('div', { class: 'bar prod', style: { width: '100%' } }, h('i', { style: { width: `${pct}%` } }))),
        h('span', { class: 'muted' }, `${Math.floor(c.prod ?? 0)}/${headCost} · ${turnsLeft(headCost - (c.prod ?? 0), y.prod)}t`),
        btn('✕', () => void gs.cmd({ t: 'queue', city: c.id, op: 'remove', index: 0 }), 'small ghost', 'Cancel'),
      ),
    );
    if (c.buyPrice != null) {
      const canBuy = gs.view.gold >= c.buyPrice;
      prod.append(btn(`Buy for ${c.buyPrice} 🪙`, async () => {
        if (await gs.cmd({ t: 'buy', city: c.id })) sfx('build');
      }, 'small', canBuy ? 'Complete this item next turn' : `You need ${c.buyPrice} gold`));
      (prod.lastChild as HTMLButtonElement).disabled = !canBuy;
    }
  } else prod.append(h('div', { class: 'muted' }, 'Nothing is being built — pick something below.'));
  (c.queue ?? []).slice(1).forEach((q, i) => {
    const info = itemInfo(q.kind, q.id);
    prod.append(
      h('div', { class: 'qitem' }, h('span', { class: 'ic' }, info.icon), h('span', null, info.name), h('span', { class: 'muted' }, `${q.cost}`),
        h('span', null, btn('▲', () => void gs.cmd({ t: 'queue', city: c.id, op: 'up', index: i + 1 }), 'small ghost'), btn('✕', () => void gs.cmd({ t: 'queue', city: c.id, op: 'remove', index: i + 1 }), 'small ghost'))),
    );
  });

  // options
  const tabs = h('div', { class: 'tabs' });
  const opts = h('div', { style: { display: 'grid', gap: '2px' } });
  const all = c.options ?? [];
  const groups = {
    units: all.filter((o) => o.kind === 'unit'),
    buildings: all.filter((o) => o.kind === 'building' && !BUILDINGS[o.id as BuildingId].wonder),
    wonders: all.filter((o) => o.kind === 'building' && BUILDINGS[o.id as BuildingId].wonder),
  };
  if (!groups[tab].length) tab = groups.buildings.length ? 'buildings' : groups.units.length ? 'units' : groups.wonders.length ? 'wonders' : tab;
  (['buildings', 'units', 'wonders'] as const).forEach((k) => {
    tabs.append(btn(`${k === 'units' ? '⚔ Units' : k === 'buildings' ? '🏛 Buildings' : '🌟 Wonders'} (${groups[k].length})`, () => { tab = k; gs.refreshUI(); }, `small ${tab === k ? 'active' : ''}`));
  });
  const row = (o: BuildOption) => {
    const info = itemInfo(o.kind, o.id);
    const queued = c.queue?.some((q) => q.kind === o.kind && q.id === o.id);
    const r = h('div', { class: 'opt', title: info.desc },
      h('span', { class: 'ic' }, info.icon),
      h('div', null, h('div', { class: 'nm' }, info.name), h('div', { class: 'ds' }, info.desc)),
      h('span', { class: 'muted', style: { textAlign: 'right', fontSize: '12px' } }, `${o.cost}⚙`, h('br'), `${o.turns}t`),
      h('span', null,
        btn('▶', async () => { sfx('click'); await gs.cmd({ t: 'queue', city: c.id, op: 'set', kind: o.kind, id: o.id }); }, 'small', 'Build this now'),
        btn('+', async () => { sfx('click'); await gs.cmd({ t: 'queue', city: c.id, op: 'add', kind: o.kind, id: o.id }); }, 'small ghost', 'Add to the queue'),
      ),
    );
    if (queued) r.style.opacity = '0.55';
    r.addEventListener('dblclick', () => void gs.cmd({ t: 'queue', city: c.id, op: 'set', kind: o.kind, id: o.id }));
    return r;
  };
  for (const o of groups[tab]) opts.append(row(o));
  if (!groups[tab].length) opts.append(h('div', { class: 'muted' }, tab === 'wonders' ? 'No wonders available — research more technologies.' : 'Nothing available.'));

  const have = h('div', { class: 'row' });
  for (const b of c.buildings ?? []) have.append(h('span', { class: 'pill', title: BUILDINGS[b].desc }, BUILDINGS[b].icon, ' ', BUILDINGS[b].name));
  if (!c.buildings?.length) have.append(h('span', { class: 'muted' }, 'No buildings yet.'));

  const body = h('div', { class: 'body' },
    yields, growth,
    h('div', null, h('div', { class: 'sec-title' }, 'Citizens work…'), focusRow),
    h('div', null, h('div', { class: 'sec-title' }, 'Production'), prod),
    h('div', null, tabs, h('div', { style: { height: '6px' } }), opts),
    h('div', null, h('div', { class: 'sec-title' }, 'Buildings'), have),
    h('div', { class: 'muted', style: { fontSize: '12px' } }, `City strength ${c.strength} · HP ${c.hp}/${c.maxHp} · Territory radius ${c.radius} · Upkeep ${y.upkeep} 🪙`),
  );
  clear(panel);
  panel.append(header, body);
}
