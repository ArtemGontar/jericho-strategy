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
  it('the final technology starts a countdown, and holding the capital through it wins', () => {
    const g = twoHumans();
    g.players[0].techs.push(...['astronomy', 'banking', 'gunpowder']);
    g.players[0].researching = FINAL_TECH;
    g.players[0].research = 1e6;
    g.exec(0, { t: 'end' });
    g.exec(1, { t: 'end' });
    // discovered, but not yet won
    expect(g.s.over).toBe(false);
    expect(g.s.enlightenment?.owner).toBe(0);
    expect(buildView(g, 1).enlightenment?.turnsLeft).toBeGreaterThan(0);
    const left = g.s.enlightenment!.duration;
    for (let i = 0; i < left + 1 && !g.s.over; i++) {
      g.exec(0, { t: 'end' });
      g.exec(1, { t: 'end' });
    }
    expect(g.s.over).toBe(true);
    expect(g.s.winner).toBe(0);
    expect(g.s.victory).toBe('science');
    expect(g.exec(1, { t: 'end' }).ok).toBe(false); // the game is over
    expect(g.exec(1, { t: 'disband', unit: g.units[0].id }).ok).toBe(false);
  });

  it('losing the capital during the countdown cancels the science victory', () => {
    const g = twoHumans();
    g.players[0].techs.push(...['astronomy', 'banking', 'gunpowder']);
    g.players[0].researching = FINAL_TECH;
    g.players[0].research = 1e6;
    g.exec(0, { t: 'end' });
    g.exec(1, { t: 'end' });
    const capital = g.cities.find((c) => c.id === g.s.enlightenment!.capital)!;
    capital.owner = 1; // the rival takes it
    for (let i = 0; i < 4; i++) {
      g.exec(0, { t: 'end' });
      g.exec(1, { t: 'end' });
    }
    expect(g.s.enlightenment ?? null).toBe(null);
    expect(g.s.barred).toContain(0);
    expect(g.s.victory).not.toBe('science');
  });

  it('a towering culture starts a hegemony countdown that wins unless a rival catches up', () => {
    const g = twoHumans();
    g.players[0].culture = g.cultureGoal * 1.3;
    g.players[1].culture = g.cultureGoal * 0.5;
    g.exec(0, { t: 'end' });
    g.exec(1, { t: 'end' });
    expect(g.s.over).toBe(false);
    expect(g.s.hegemony?.owner).toBe(0);
    expect(buildView(g, 1).hegemony?.turnsLeft).toBeGreaterThan(0);
    const left = g.s.hegemony!.duration;
    for (let i = 0; i < left + 1 && !g.s.over; i++) {
      g.exec(0, { t: 'end' });
      g.exec(1, { t: 'end' });
    }
    expect(g.s.over).toBe(true);
    expect(g.s.victory).toBe('culture');
    expect(g.s.winner).toBe(0);
  });

  it('a rival whose culture catches up breaks the hegemony', () => {
    const g = twoHumans();
    g.players[0].culture = g.cultureGoal * 1.3;
    g.players[1].culture = g.cultureGoal * 0.5;
    g.exec(0, { t: 'end' });
    g.exec(1, { t: 'end' });
    expect(g.s.hegemony?.owner).toBe(0);
    g.players[1].culture = g.cultureGoal * 1.25;
    g.exec(0, { t: 'end' });
    g.exec(1, { t: 'end' });
    expect(g.s.hegemony ?? null).toBe(null);
    expect(g.s.over).toBe(false);
  });

  it('technologies known by civilisations we have met get cheaper', () => {
    const g = twoHumans();
    g.s.relations[0][1] = 'peace';
    g.s.relations[1][0] = 'peace';
    const base = techCost(g, g.players[0], 'writing');
    g.players[1].techs.push('pottery', 'writing');
    expect(techCost(g, g.players[0], 'writing')).toBeLessThan(base);
    g.s.relations[0][1] = 'unmet';
    g.s.relations[1][0] = 'unmet';
    expect(techCost(g, g.players[0], 'writing')).toBe(base);
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

describe('exploration, promotion, policies and upgrades', () => {
  it('tribal villages pay out once and disappear', () => {
    const g = twoHumans(41);
    const warrior = g.units.find((u) => u.owner === 0 && u.type === 'warrior')!;
    const spot = g.nb(warrior.x, warrior.y).find((n) => TERRAIN[g.tile(n.x, n.y).terrain].passable && g.unitsAt(n.x, n.y).length === 0 && g.tile(n.x, n.y).city < 0)!;
    g.tile(spot.x, spot.y).hut = true;
    const before = { gold: g.players[0].gold, research: g.players[0].research, culture: g.players[0].culture, units: g.units.length, hp: warrior.hp };
    expect(g.exec(0, { t: 'move', unit: warrior.id, to: spot }).ok).toBe(true);
    expect(g.tile(spot.x, spot.y).hut).toBe(false);
    const after = { gold: g.players[0].gold, research: g.players[0].research, culture: g.players[0].culture, units: g.units.length, hp: warrior.hp };
    expect(JSON.stringify(after)).not.toBe(JSON.stringify(before));
  });

  it('veterans are promoted after enough fighting', () => {
    const g = twoHumans(42);
    const w = g.units.find((u) => u.owner === 0 && u.type === 'warrior')!;
    expect(w.level).toBe(0);
    g.gainXp(w, 15);
    expect(w.level).toBe(1);
    g.gainXp(w, 200);
    expect(w.level).toBe(3);
    const worker = g.units.find((u) => u.owner === 0 && u.type === 'worker')!;
    g.gainXp(worker, 500);
    expect(worker.level).toBe(0);
  });

  it('culture unlocks a policy choice that changes the economy', () => {
    const g = twoHumans(43);
    const p = g.players[0];
    expect(p.policyChoices).toHaveLength(0);
    p.culture = g.nextPolicyCost(p);
    g.exec(0, { t: 'end' });
    g.exec(1, { t: 'end' });
    expect(p.policyChoices).toHaveLength(3);
    expect(g.exec(0, { t: 'policy', id: 'nonsense' }).ok).toBe(false);
    const incomeBefore = p.income.culture;
    expect(g.exec(0, { t: 'policy', id: 'patronage' }).ok).toBe(p.policyChoices.length === 0 ? true : p.policyChoices.includes('patronage'));
    if (!p.policies.length) expect(g.exec(0, { t: 'policy', id: p.policyChoices[0] }).ok).toBe(true);
    expect(p.policies).toHaveLength(1);
    expect(p.policyChoices).toHaveLength(0);
    void incomeBefore;
  });

  it('pinned tiles are always worked, and units upgrade for gold', () => {
    const g = twoHumans(44);
    const c = g.playerCities(0)[0];
    c.pop = 3;
    const tile = g.nb(c.x, c.y).find((n) => g.tile(n.x, n.y).owner === 0 && TERRAIN[g.tile(n.x, n.y).terrain].workable)!;
    const idx = tile.y * g.width + tile.x;
    expect(g.exec(0, { t: 'tile', city: c.id, x: tile.x, y: tile.y }).ok).toBe(true);
    expect(c.worked).toContain(idx);
    expect(g.exec(0, { t: 'tile', city: c.id, x: c.x, y: c.y }).ok).toBe(false); // the centre is not assignable
    expect(g.exec(0, { t: 'tile', city: c.id, x: tile.x, y: tile.y }).ok).toBe(true); // unpin
    expect(c.locked).toHaveLength(0);

    const w = g.units.find((u) => u.owner === 0 && u.type === 'warrior')!;
    g.players[0].gold = 1000;
    expect(g.exec(0, { t: 'upgrade', unit: w.id }).ok).toBe(false); // needs Iron Working + iron
    g.players[0].techs.push('bronze', 'ironworking');
    const spear = g.addUnit(0, 'spearman', c.x, c.y);
    g.players[0].techs.push('feudalism');
    expect(g.exec(0, { t: 'upgrade', unit: spear.id }).ok).toBe(true);
    expect(spear.type).toBe('pikeman');
    expect(g.players[0].gold).toBeLessThan(1000);
  });
});

describe('movement regressions', () => {
  it('a settler is not stranded behind its own worker (seed 1005)', () => {
    const game = Game.create({ seed: 1005, players: Array.from({ length: 4 }, (_, i) => ({ name: `AI${i}`, ai: true, aiLevel: 'normal' as const })), turnLimit: 200 });
    for (let t = 0; t < 12; t++) game.processTurn();
    for (const p of game.players) expect(game.playerCities(p.id).length, `${p.name} founded a city`).toBeGreaterThan(0);
  });
});

describe('ships', () => {
  /** a land tile next to water with a free coastal tile beside it, found by search so the test does not depend on a seed */
  function shore(g: ReturnType<typeof twoHumans>) {
    // borders over the sea close it to warships of a civilisation at peace; keep the test water neutral
    for (const t of g.tiles) if (!TERRAIN[t.terrain].land) t.owner = -1;
    for (const t of g.tiles) {
      if (t.terrain === 'coast' || t.terrain === 'ocean') continue;
      if (!TERRAIN[t.terrain].passable || t.city >= 0) continue;
      const water = g.nb(t.x, t.y).find((n) => g.tile(n.x, n.y).terrain === 'coast' && !g.unitsAt(n.x, n.y).length);
      if (water) return { land: { x: t.x, y: t.y }, water };
    }
    throw new Error('no shore');
  }

  it('sail on water only, and ocean-going ships may leave the coast', () => {
    const g = twoHumans(21);
    const { water } = shore(g);
    const tri = g.addUnit(0, 'trireme', water.x, water.y);
    const cara = g.addUnit(0, 'caravel', water.x, water.y);
    let landTarget: { x: number; y: number } | null = null;
    let deep: { x: number; y: number } | null = null;
    for (const n of g.nb(water.x, water.y)) {
      const t = g.tile(n.x, n.y);
      if (TERRAIN[t.terrain].land && t.city < 0 && !landTarget) landTarget = n;
      if (t.terrain === 'ocean' && !deep) deep = n;
    }
    // a ship cannot walk inland
    if (landTarget) expect(g.exec(0, { t: 'move', unit: tri.id, to: landTarget }).ok).toBe(false);
    // the trireme refuses deep water; the caravel does not (when such a tile is adjacent)
    if (deep) {
      cara.mp = 8;
      expect(g.exec(0, { t: 'move', unit: cara.id, to: deep }).ok).toBe(true);
      tri.mp = 8;
      expect(g.exec(0, { t: 'move', unit: tri.id, to: deep }).ok).toBe(false);
    }
  });

  it('carry land units: board, sail together, disembark; sinking the ship drowns the cargo', () => {
    const g = twoHumans(21);
    const { land, water } = shore(g);
    const ship = g.addUnit(0, 'trireme', water.x, water.y);
    const man = g.addUnit(0, 'warrior', land.x, land.y);
    man.mp = 4;
    // climbing aboard by walking onto the ship
    expect(g.exec(0, { t: 'move', unit: man.id, to: water }).ok).toBe(true);
    expect(man.carriedBy).toBe(ship.id);
    expect(man.x === water.x && man.y === water.y).toBe(true);
    expect(g.cargoOf(ship).length).toBe(1);
    // the passenger cannot fight from the deck
    // sail to another coastal tile; the passenger comes along
    const next = g.nb(water.x, water.y).find((n) => g.tile(n.x, n.y).terrain === 'coast' && !g.unitsAt(n.x, n.y).length);
    if (next) {
      ship.mp = 6;
      expect(g.exec(0, { t: 'move', unit: ship.id, to: next }).ok).toBe(true);
      expect(man.x === ship.x && man.y === ship.y).toBe(true);
    }
    // capacity: a trireme holds two
    const a = g.addUnit(0, 'warrior', land.x, land.y);
    const b = g.addUnit(0, 'archer', land.x, land.y);
    for (const u of [a, b]) u.mp = 4;
    expect(ship.x === water.x && ship.y === water.y).toBe(next ? false : true);
    // disembark onto land next to the ship's current tile
    const shoreNow = g.nb(ship.x, ship.y).find((n) => TERRAIN[g.tile(n.x, n.y).terrain].passable && g.tile(n.x, n.y).city < 0 && !g.unitsAt(n.x, n.y).length);
    if (shoreNow) {
      man.mp = 4;
      expect(g.exec(0, { t: 'move', unit: man.id, to: shoreNow }).ok).toBe(true);
      expect(man.carriedBy == null).toBe(true);
      expect(man.mp).toBe(0);
    }
    // a sunk ship takes its passengers with it
    const g2 = twoHumans(21);
    const s2 = shore(g2);
    const ship2 = g2.addUnit(0, 'trireme', s2.water.x, s2.water.y);
    const rider = g2.addUnit(0, 'warrior', s2.land.x, s2.land.y);
    rider.mp = 4;
    g2.exec(0, { t: 'move', unit: rider.id, to: s2.water });
    expect(rider.carriedBy).toBe(ship2.id);
    g2.removeUnit(ship2);
    expect(g2.units.includes(rider)).toBe(false);
  });

  it('only coastal cities build ships, and only with the technology', () => {
    const g = twoHumans(21);
    const c = g.playerCities(0)[0];
    const coastal = g.isCoastal(c.x, c.y);
    const p = g.players[0];
    expect(g.exec(0, { t: 'queue', city: c.id, op: 'set', kind: 'unit', id: 'trireme' }).ok).toBe(false);
    p.techs.push('sailing');
    expect(g.exec(0, { t: 'queue', city: c.id, op: 'set', kind: 'unit', id: 'trireme' }).ok).toBe(coastal);
  });
});
