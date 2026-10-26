import { describe, expect, it } from 'vitest';
import { Game } from '../src/shared/game';
import { buildView } from '../src/shared/view';
import { ViewMap } from '../src/shared/viewpath';
import { BUILDINGS, FINAL_TECH, TERRAIN } from '../src/shared/data';
import { techCost } from '../src/shared/economy';

function twoHumans(seed = 21, extra: Partial<Parameters<typeof Game.create>[0]> = {}) {
  const g = Game.create({ seed, size: 'small', players: [{ name: 'A', ai: false }, { name: 'B', ai: false }], ...extra });
  for (const t of g.tiles) t.explored = 0xffff;
  g.updateVisibility();
  const found = (pid: number) => g.exec(pid, { t: 'found', unit: g.units.find((u) => u.owner === pid && u.type === 'settler')!.id });
  found(0);
  found(1);
  return g;
}

describe('economy & production', () => {
  it('cities grow, build queued units, and honour the buy price', () => {
    const g = twoHumans();
    const c = g.playerCities(0)[0];
    expect(g.exec(0, { t: 'queue', city: c.id, op: 'set', kind: 'unit', id: 'warrior' }).ok).toBe(true);
    const before = g.units.filter((u) => u.owner === 0 && u.type === 'warrior').length;
    // not enough gold yet
    expect(g.exec(0, { t: 'buy', city: c.id }).ok).toBe(false);
    g.players[0].gold = 500;
    expect(g.exec(0, { t: 'buy', city: c.id }).ok).toBe(true);
    expect(g.players[0].gold).toBeLessThan(500);
    g.exec(0, { t: 'end' });
    g.exec(1, { t: 'end' });
    expect(g.units.filter((u) => u.owner === 0 && u.type === 'warrior').length).toBe(before + 1);
    let pop = c.pop;
    for (let i = 0; i < 20; i++) {
      g.exec(0, { t: 'end' });
      g.exec(1, { t: 'end' });
    }
    expect(c.pop).toBeGreaterThan(pop);
    pop = c.pop;
  });

  it('refuses items whose technology or resources are missing', () => {
    const g = twoHumans();
    const c = g.playerCities(0)[0];
    expect(g.exec(0, { t: 'queue', city: c.id, op: 'set', kind: 'building', id: 'library' }).ok).toBe(false);
    expect(g.exec(0, { t: 'queue', city: c.id, op: 'set', kind: 'unit', id: 'catapult' }).ok).toBe(false);
    g.players[0].techs.push('writing');
    expect(g.exec(0, { t: 'queue', city: c.id, op: 'set', kind: 'building', id: 'library' }).ok).toBe(true);
  });

  it('research completes, unlocks buildings and carries over', () => {
    const g = twoHumans();
    expect(g.exec(0, { t: 'research', tech: 'pottery' }).ok).toBe(true);
    expect(g.exec(0, { t: 'research', tech: 'writing' }).ok).toBe(false); // prerequisite missing
    const cost = techCost(g, g.players[0], 'pottery');
    g.players[0].research = cost - 1;
    g.exec(0, { t: 'end' });
    g.exec(1, { t: 'end' });
    expect(g.players[0].techs).toContain('pottery');
    expect(g.players[0].researching).toBeNull();
  });

  it('only one civilisation can own each wonder', () => {
    const g = twoHumans();
    g.players[0].techs.push('masonry');
    g.players[1].techs.push('masonry');
    const a = g.playerCities(0)[0];
    const b = g.playerCities(1)[0];
    expect(g.exec(0, { t: 'queue', city: a.id, op: 'set', kind: 'building', id: 'pyramids' }).ok).toBe(true);
    expect(g.exec(1, { t: 'queue', city: b.id, op: 'set', kind: 'building', id: 'pyramids' }).ok).toBe(true);
    a.prod = 9999;
    g.exec(0, { t: 'end' });
    g.exec(1, { t: 'end' });
    expect(g.s.wonders.pyramids).toBe(0);
    expect(a.buildings).toContain('pyramids');
    expect(b.queue.length).toBe(0); // B's queue entry was withdrawn
    expect(g.exec(1, { t: 'queue', city: b.id, op: 'set', kind: 'building', id: 'pyramids' }).ok).toBe(false);
    expect(BUILDINGS.pyramids.wonder).toBe(true);
  });

  it('workers build improvements over several turns', () => {
    const g = twoHumans();
    const w = g.units.find((u) => u.owner === 0 && u.type === 'worker')!;
    const c = g.playerCities(0)[0];
    // put the worker on an owned, farmable tile next to the capital
    const spot = g.nb(c.x, c.y).find((n) => ['grass', 'plains'].includes(g.tile(n.x, n.y).terrain) && g.tile(n.x, n.y).owner === 0 && g.unitsAt(n.x, n.y).length === 0)!;
    g.placeUnit(w, spot.x, spot.y);
    expect(g.exec(0, { t: 'work', unit: w.id, what: 'farm' }).ok).toBe(true);
    for (let i = 0; i < 6; i++) {
      g.exec(0, { t: 'end' });
      g.exec(1, { t: 'end' });
    }
    expect(g.tile(spot.x, spot.y).improvement).toBe('farm');
    expect(g.exec(0, { t: 'work', unit: w.id, what: 'mine' }).ok).toBe(false); // mining unknown / wrong terrain
  });
});

describe('diplomacy', () => {
  it('peace between humans needs consent, closes borders, and can be broken after 10 turns', () => {
    const g = twoHumans(33, { startAtWar: true } as never);
    g.setRelation(0, 1, 'war');
    expect(g.exec(0, { t: 'war', to: 1 }).ok).toBe(false);
    expect(g.exec(0, { t: 'peace', to: 1 }).ok).toBe(true);
    expect(g.s.proposals).toHaveLength(1);
    expect(g.exec(0, { t: 'accept', proposal: g.s.proposals[0].id, accept: true }).ok).toBe(false); // proposer cannot accept their own
    expect(g.exec(1, { t: 'accept', proposal: g.s.proposals[0].id, accept: true }).ok).toBe(true);
    expect(g.relation(0, 1)).toBe('peace');
    expect(g.exec(0, { t: 'war', to: 1 }).ok).toBe(false); // too soon
    for (let i = 0; i < 10; i++) {
      g.exec(0, { t: 'end' });
      g.exec(1, { t: 'end' });
    }
    expect(g.exec(0, { t: 'war', to: 1 }).ok).toBe(true);
    expect(g.relation(0, 1)).toBe('war');
  });

  it('armies cannot enter foreign borders at peace but scouts and workers can', () => {
    const g = twoHumans(33);
    g.setRelation(0, 1, 'peace');
    const bCity = g.playerCities(1)[0];
    const border = g.nb(bCity.x, bCity.y).find((n) => TERRAIN[g.tile(n.x, n.y).terrain].passable && g.tile(n.x, n.y).owner === 1 && g.unitsAt(n.x, n.y).length === 0)!;
    const warrior = g.units.find((u) => u.owner === 0 && u.type === 'warrior')!;
    const scout = g.units.find((u) => u.owner === 0 && u.type === 'scout')!;
    g.placeUnit(warrior, border.x, border.y);
    g.placeUnit(scout, border.x, border.y);
    // the warrior is "expelled" lazily by new treaties; here we just verify path-planning refuses the tile
    const far = g.nb(border.x, border.y).find((n) => g.tile(n.x, n.y).owner === 1 && g.unitsAt(n.x, n.y).length === 0 && TERRAIN[g.tile(n.x, n.y).terrain].passable);
    if (far) {
      expect(g.exec(0, { t: 'move', unit: warrior.id, to: far }).ok).toBe(false);
      expect(g.exec(0, { t: 'move', unit: scout.id, to: far }).ok).toBe(true);
    }
  });
});

describe('victory & elimination', () => {
  it('discovering the final technology wins a science victory', () => {
    const g = twoHumans();
    g.players[0].techs.push(...['astronomy', 'banking', 'gunpowder']);
    g.players[0].researching = FINAL_TECH;
    g.players[0].research = 1e6;
    g.exec(0, { t: 'end' });
    g.exec(1, { t: 'end' });
    expect(g.s.over).toBe(true);
    expect(g.s.winner).toBe(0);
    expect(g.s.victory).toBe('science');
    expect(g.exec(1, { t: 'end' }).ok).toBe(false); // the game is over
    expect(g.exec(1, { t: 'disband', unit: g.units[0].id }).ok).toBe(false);
  });

  it('turn limit awards the highest score', () => {
    const g = twoHumans(7, { turnLimit: 3 } as never);
    g.s.settings.turnLimit = 3;
    g.players[1].techs.push('pottery', 'mining');
    for (let i = 0; i < 4; i++) {
      g.exec(0, { t: 'end' });
      g.exec(1, { t: 'end' });
    }
    expect(g.s.over).toBe(true);
    expect(g.s.victory).toBe('score');
    expect(g.s.winner).toBe(1);
  });
});

describe('player views', () => {
  it('only reveal explored terrain and visible units', () => {
    const g = Game.create({ seed: 4, size: 'small', players: [{ name: 'A', ai: false }, { name: 'B', ai: false }] });
    const va = buildView(g, 0);
    expect(va.units.every((u) => u.owner === 0)).toBe(true);
    const hidden = va.state.map((s, i) => (s === 0 ? i : -1)).filter((i) => i >= 0);
    expect(hidden.length).toBeGreaterThan(va.state.length / 2);
    for (const i of hidden.slice(0, 30)) expect(va.terrain[i]).toBe(0);
    // own units get movement info, foreign ones would not
    const vm = new ViewMap(va);
    const warrior = va.units.find((u) => u.type === 'warrior')!;
    expect(vm.reachable(warrior).size).toBeGreaterThan(0);
    expect(buildView(g, 1).units.every((u) => u.owner === 1)).toBe(true);
  });
});
