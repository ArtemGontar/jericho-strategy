import { describe, expect, it } from 'vitest';
import { Game } from '../src/shared/game';
import { generateMap } from '../src/shared/mapgen';
import { hexDistance, neighborsOf, pixelToHex, hexToPixel } from '../src/shared/hex';
import { findPath } from '../src/shared/path';
import { previewAttack } from '../src/shared/combat';
import { TERRAIN, UNITS } from '../src/shared/data';

const ais = (n: number) => Array.from({ length: n }, (_, i) => ({ name: `AI${i}`, ai: true }));

describe('hex math', () => {
  it('neighbours are at distance 1 and symmetric', () => {
    for (let y = 0; y < 8; y++)
      for (let x = 0; x < 8; x++) {
        const nbs = neighborsOf(x, y, 8, 8);
        for (const n of nbs) {
          expect(hexDistance(x, y, n.x, n.y)).toBe(1);
          expect(neighborsOf(n.x, n.y, 8, 8).some((m) => m.x === x && m.y === y)).toBe(true);
        }
      }
  });
  it('pixel <-> hex roundtrip', () => {
    for (const [x, y] of [[0, 0], [3, 2], [5, 5], [7, 3]]) {
      const { px, py } = hexToPixel(x, y, 30);
      expect(pixelToHex(px, py, 30, 10, 10)).toEqual({ x, y });
    }
  });
});

describe('map generation', () => {
  it('creates reachable, fair starts for several seeds and sizes', () => {
    for (const [w, h, n] of [[30, 20, 2], [40, 26, 4], [52, 34, 8]] as const) {
      for (let seed = 1; seed <= 4; seed++) {
        const map = generateMap(w, h, n, seed);
        expect(map.starts).toHaveLength(n);
        const game = Game.create({ seed, size: w === 30 ? 'small' : w === 40 ? 'medium' : 'large', players: ais(n) });
        // every settler can path to every other player's start (no sailing needed)
        const units = game.units.filter((u) => u.type === 'warrior');
        for (const t of game.tiles) t.explored = 0xffff;
        for (const b of units.slice(1)) {
          const path = findPath(game, units[0], { x: b.x, y: b.y });
          expect(path, `seed ${seed} ${w}x${h}`).not.toBeNull();
        }
      }
    }
  });
});

describe('basic play', () => {
  it('founds a city, claims land, and cannot found too close', () => {
    const game = Game.create({ seed: 5, players: [{ name: 'Me', ai: false }, { name: 'Bot', ai: true }] });
    const settler = game.units.find((u) => u.owner === 0 && u.type === 'settler')!;
    expect(game.exec(0, { t: 'found', unit: settler.id }).ok).toBe(true);
    const city = game.playerCities(0)[0];
    expect(city.capital).toBe(true);
    expect(game.tiles.filter((t) => t.owner === 0).length).toBeGreaterThanOrEqual(7);
    // a second settler right next door must be refused
    const s2 = game.addUnit(0, 'settler', city.x, city.y);
    expect(game.exec(0, { t: 'found', unit: s2.id }).ok).toBe(false);
  });

  it('moves along a path over multiple turns', () => {
    const game = Game.create({ seed: 10, players: [{ name: 'Me', ai: false }, { name: 'Bot', ai: true }] });
    const warrior = game.units.find((u) => u.owner === 0 && u.type === 'warrior')!;
    const start = { x: warrior.x, y: warrior.y };
    // find a passable tile ~6 away that we can see
    let target = null as null | { x: number; y: number };
    for (const t of game.tiles) {
      if (TERRAIN[t.terrain].passable && game.dist(start.x, start.y, t.x, t.y) === 4) { target = { x: t.x, y: t.y }; break; }
    }
    expect(target).not.toBeNull();
    for (const t of game.tiles) t.explored |= 1;
    const res = game.exec(0, { t: 'move', unit: warrior.id, to: target! });
    expect(res.ok).toBe(true);
    expect(game.dist(start.x, start.y, warrior.x, warrior.y)).toBeGreaterThan(0);
    // end turn: standing orders carry on
    game.exec(0, { t: 'end' });
    for (let i = 0; i < 6 && warrior.order; i++) game.exec(0, { t: 'end' });
    expect(warrior.x === target!.x && warrior.y === target!.y).toBe(true);
  });

  it('turn waits for every human and then advances', () => {
    const game = Game.create({ seed: 3, players: [{ name: 'A', ai: false }, { name: 'B', ai: false }, { name: 'C', ai: true }] });
    expect(game.turn).toBe(1);
    game.exec(0, { t: 'end' });
    expect(game.turn).toBe(1);
    game.exec(1, { t: 'end' });
    expect(game.turn).toBe(2);
    expect(game.players[0].endedTurn).toBe(false);
  });
});

describe('combat', () => {
  it('requires war, resolves damage, and captures an empty city', () => {
    const game = Game.create({ seed: 12, players: [{ name: 'A', ai: false }, { name: 'B', ai: false }] });
    for (const t of game.tiles) t.explored = 0xffff;
    const sA = game.units.find((u) => u.owner === 0 && u.type === 'settler')!;
    const sB = game.units.find((u) => u.owner === 1 && u.type === 'settler')!;
    game.exec(0, { t: 'found', unit: sA.id });
    game.exec(1, { t: 'found', unit: sB.id });
    const cityB = game.playerCities(1)[0];
    // put an attacker next to B's city
    const free = game.nb(cityB.x, cityB.y).find((n) => TERRAIN[game.tile(n.x, n.y).terrain].passable && game.unitsAt(n.x, n.y).length === 0)!;
    const att = game.addUnit(0, 'swordsman', free.x, free.y);
    game.setRelation(0, 1, 'peace');
    expect(previewAttack(game, att, cityB.x, cityB.y).ok).toBe(false);
    game.setRelation(0, 1, 'war');
    for (const u of game.units.filter((u) => u.owner === 1 && u.x === cityB.x && u.y === cityB.y)) game.removeUnit(u);
    const pv = previewAttack(game, att, cityB.x, cityB.y);
    expect(pv.ok).toBe(true);
    expect(pv.dmgDef).toBeGreaterThan(0);
    cityB.hp = 5;
    const r = game.exec(0, { t: 'attack', unit: att.id, to: { x: cityB.x, y: cityB.y } });
    expect(r.ok).toBe(true);
    expect(cityB.owner).toBe(0);
    expect(game.players[1].alive).toBe(false);
    expect(game.s.over).toBe(true);
  });

  it('ranged units cannot kill a city and take no counter damage', () => {
    const game = Game.create({ seed: 12, players: [{ name: 'A', ai: false }, { name: 'B', ai: false }] });
    for (const t of game.tiles) t.explored = 0xffff;
    game.exec(0, { t: 'found', unit: game.units.find((u) => u.owner === 0 && u.type === 'settler')!.id });
    game.exec(1, { t: 'found', unit: game.units.find((u) => u.owner === 1 && u.type === 'settler')!.id });
    const cityB = game.playerCities(1)[0];
    const spot = game.nb(cityB.x, cityB.y).find((n) => TERRAIN[game.tile(n.x, n.y).terrain].passable)!;
    const archer = game.addUnit(0, 'archer', spot.x, spot.y);
    game.setRelation(0, 1, 'war');
    cityB.hp = 3;
    game.exec(0, { t: 'attack', unit: archer.id, to: { x: cityB.x, y: cityB.y } });
    expect(cityB.hp).toBe(1);
    expect(cityB.owner).toBe(1);
    expect(archer.hp).toBe(100);
  });
});

describe('determinism & persistence', () => {
  it('same seed gives identical games; save/load round-trips', () => {
    const run = () => {
      const g = Game.create({ seed: 42, players: ais(3) });
      for (let i = 0; i < 40; i++) g.processTurn();
      return g;
    };
    const a = run();
    const b = run();
    expect(a.serialize()).toBe(b.serialize());
    const c = Game.fromJSON(a.serialize());
    c.processTurn();
    a.processTurn();
    expect(c.serialize()).toBe(a.serialize());
  });

  it('a full AI-only game ends with a winner and sane stats', () => {
    const g = Game.create({ seed: 2024, players: ais(4), turnLimit: 160 });
    let guard = 0;
    while (!g.s.over && guard++ < 200) g.processTurn();
    expect(g.s.over).toBe(true);
    expect(g.s.winner).not.toBeNull();
    const alive = g.players.filter((p) => p.alive);
    expect(alive.length).toBeGreaterThan(0);
    expect(g.cities.length).toBeGreaterThanOrEqual(8);
    // nobody should be stuck without ever founding a city
    for (const p of alive) expect(g.playerCities(p.id).length).toBeGreaterThan(0);
    for (const u of g.units) expect(UNITS[u.type]).toBeDefined();
  });
});

describe('island maps', () => {
  it('give every player a home island of their own and keep islands for ships to find', () => {
    for (const [size, n] of [['small', 2], ['medium', 3], ['large', 5]] as const) {
      for (const seed of [3, 17, 91]) {
        const g = Game.create({ seed, size, mapStyle: 'islands', players: Array.from({ length: n }, (_, i) => ({ name: `P${i}`, ai: true })) });
        const homes = new Set(g.units.filter((u) => u.type === 'settler').map((u) => g.landmass(u.x, u.y)));
        expect(homes.size, `${size} ${n}p seed ${seed}`).toBe(n);
      }
    }
  });

  it('continent maps keep small islands too', () => {
    const g = Game.create({ seed: 5, size: 'large', players: Array.from({ length: 4 }, (_, i) => ({ name: `P${i}`, ai: true })) });
    const ids = new Set<number>();
    for (const t of g.tiles) if (TERRAIN[t.terrain].passable) ids.add(g.landmass(t.x, t.y));
    expect(ids.size).toBeGreaterThan(1);
  });
});

describe('unit roles', () => {
  /** Two capitals at war, with a free tile beside B's city for an attacker. */
  function war(seed = 12) {
    const game = Game.create({ seed, players: [{ name: 'A', ai: false }, { name: 'B', ai: false }] });
    for (const t of game.tiles) t.explored = 0xffff;
    game.exec(0, { t: 'found', unit: game.units.find((u) => u.owner === 0 && u.type === 'settler')!.id });
    game.exec(1, { t: 'found', unit: game.units.find((u) => u.owner === 1 && u.type === 'settler')!.id });
    const cityB = game.playerCities(1)[0];
    for (const u of game.units.filter((u) => u.owner === 1 && u.x === cityB.x && u.y === cityB.y)) game.removeUnit(u);
    game.setRelation(0, 1, 'war');
    const spot = game.nb(cityB.x, cityB.y).find((n) => TERRAIN[game.tile(n.x, n.y).terrain].passable && game.unitsAt(n.x, n.y).length === 0)!;
    return { game, cityB, spot };
  }

  it('walls turn back ordinary soldiers and archers, but not siege engines', () => {
    const { game, cityB, spot } = war();
    const sword = game.addUnit(0, 'swordsman', spot.x, spot.y);
    const archer = game.addUnit(0, 'archer', spot.x, spot.y);
    const cat = game.addUnit(0, 'catapult', spot.x, spot.y);
    const open = [sword, archer, cat].map((u) => previewAttack(game, u, cityB.x, cityB.y).dmgDef);
    cityB.buildings.push('walls');
    const walled = [sword, archer, cat].map((u) => previewAttack(game, u, cityB.x, cityB.y).dmgDef);
    // walls also make the city stronger, so a catapult's damage drops a little, but soldiers lose far more
    expect(walled[0]).toBeLessThan(open[0] * 0.65);
    expect(walled[1]).toBeLessThan(open[1] * 0.65);
    expect(walled[2]).toBeGreaterThan(open[2] * 0.7);
    expect(walled[2]).toBeGreaterThan(walled[0]);
  });

  it('cavalry ride down archers and siege crews, and not spearmen', () => {
    const { game, spot } = war();
    const at = game.nb(spot.x, spot.y).find((n) => TERRAIN[game.tile(n.x, n.y).terrain].passable && game.unitsAt(n.x, n.y).length === 0)!;
    const horse = game.addUnit(0, 'horseman', spot.x, spot.y);
    const archer = game.addUnit(1, 'archer', at.x, at.y);
    const vsArcher = previewAttack(game, horse, at.x, at.y).dmgDef;
    game.removeUnit(archer);
    const spear = game.addUnit(1, 'spearman', at.x, at.y);
    const vsSpear = previewAttack(game, horse, at.x, at.y).dmgDef;
    expect(vsArcher).toBeGreaterThan(vsSpear * 2);
    game.removeUnit(spear);
    const crew = game.addUnit(1, 'catapult', at.x, at.y);
    expect(previewAttack(game, horse, at.x, at.y).dmgDef).toBeGreaterThan(vsSpear * 2);
    void crew;
  });
});

describe('domination', () => {
  it('starts a countdown when one civilisation holds most of the cities, and ends the game if it holds', () => {
    const game = Game.create({ seed: 31, size: 'large', players: [{ name: 'A', ai: false }, { name: 'B', ai: false }] });
    for (const t of game.tiles) t.explored = 0xffff;
    game.exec(0, { t: 'found', unit: game.units.find((u) => u.owner === 0 && u.type === 'settler')!.id });
    game.exec(1, { t: 'found', unit: game.units.find((u) => u.owner === 1 && u.type === 'settler')!.id });
    // hand A seven more cities, so A holds 8 of 9
    let made = 0;
    for (const t of game.tiles) {
      if (made >= 7) break;
      if (!TERRAIN[t.terrain].land || !TERRAIN[t.terrain].passable || t.city >= 0) continue;
      if (game.cities.some((c) => game.dist(c.x, c.y, t.x, t.y) < 3)) continue;
      game.foundCity(0, t.x, t.y);
      made++;
    }
    expect(game.cities.filter((c) => c.owner === 0).length).toBeGreaterThanOrEqual(6);
    game.checkVictory();
    const d = game.s.domination;
    expect(d?.owner).toBe(0);
    // winning back cities breaks it
    const mine = game.cities.filter((c) => c.owner === 0);
    for (const c of mine.slice(0, Math.ceil(mine.length / 2))) c.owner = 1;
    game.checkVictory();
    expect(game.s.domination).toBeNull();
  });

  it('is won when the countdown runs out', () => {
    const game = Game.create({ seed: 31, size: 'large', players: [{ name: 'A', ai: false }, { name: 'B', ai: false }] });
    for (const t of game.tiles) t.explored = 0xffff;
    game.exec(0, { t: 'found', unit: game.units.find((u) => u.owner === 0 && u.type === 'settler')!.id });
    game.exec(1, { t: 'found', unit: game.units.find((u) => u.owner === 1 && u.type === 'settler')!.id });
    let made = 0;
    for (const t of game.tiles) {
      if (made >= 7) break;
      if (!TERRAIN[t.terrain].land || !TERRAIN[t.terrain].passable || t.city >= 0) continue;
      if (game.cities.some((c) => game.dist(c.x, c.y, t.x, t.y) < 3)) continue;
      game.foundCity(0, t.x, t.y);
      made++;
    }
    game.checkVictory();
    expect(game.s.domination).not.toBeNull();
    game.s.turn += game.s.domination!.duration;
    game.checkVictory();
    expect(game.s.over).toBe(true);
    expect(game.s.winner).toBe(0);
    expect(game.s.victory).toBe('domination');
  });
});

