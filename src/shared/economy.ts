import { hexDistance, tilesWithin } from './hex';
import { BUILDINGS, CIV_BY_ID, FOCUS_WEIGHTS, IMPROVEMENT_YIELD, RESOURCES, TECHS, TECH_BY_ID, TERRAIN, UNITS } from './data';
import type { BuildingId, City, Player, Tile } from './types';
import type { Game } from './game';

/** Global knobs: how expensive technology (per era) and production items are relative to the base tables. */
export const TECH_ERA_FACTOR = [1.0, 2.4, 3.9, 5.6];
export const PROD_COST_FACTOR = 0.75;

export interface Yield {
  food: number;
  prod: number;
  gold: number;
}

export function tileYield(t: Tile): Yield {
  const base = TERRAIN[t.terrain];
  const y = { food: base.food, prod: base.prod, gold: base.gold };
  if (t.resource) {
    const r = RESOURCES[t.resource];
    y.food += r.food;
    y.prod += r.prod;
    y.gold += r.gold;
  }
  if (t.improvement) {
    const i = IMPROVEMENT_YIELD[t.improvement];
    y.food += i.food;
    y.prod += i.prod;
    y.gold += i.gold;
  }
  return y;
}

export interface CityYield {
  food: number;
  prod: number;
  gold: number;
  science: number;
  culture: number;
  upkeep: number;
  eat: number;
  surplus: number;
}

function civOf(game: Game, owner: number) {
  return CIV_BY_ID[game.players[owner].civ];
}

export function hasBuilding(city: City, b: BuildingId): boolean {
  return city.buildings.includes(b);
}

export function maxPop(city: City): number {
  return hasBuilding(city, 'aqueduct') ? 18 : 8;
}

export function foodNeeded(game: Game, pop: number): number {
  const base = 15 + 6 * (pop - 1) + Math.pow(pop - 1, 1.7);
  return Math.max(8, Math.round(base * game.speedMult));
}

export function cityCenterYield(game: Game, city: City): Yield {
  const t = game.tile(city.x, city.y);
  const y = tileYield(t);
  y.food = Math.max(2, y.food);
  y.prod = Math.max(3, y.prod);
  y.gold += 1;
  if (civOf(game, city.owner).id === 'jericho') y.food += 1;
  if (civOf(game, city.owner).id === 'egypt' && city.capital) y.prod += 1;
  return y;
}

/** Pick which tiles each citizen works, honouring each city's focus. Cities of one player never share a tile. */
export function assignWorkedTiles(game: Game, playerId: number): void {
  const used = new Set<number>();
  for (const c of game.cities) used.add(c.y * game.width + c.x);
  const mine = game.cities.filter((c) => c.owner === playerId).sort((a, b) => a.id - b.id);
  for (const city of mine) {
    const candidates: Array<{ idx: number; y: Yield }> = [];
    for (const p of tilesWithin(city.x, city.y, city.radius, game.width, game.height)) {
      const idx = p.y * game.width + p.x;
      if (used.has(idx)) continue;
      const t = game.tiles[idx];
      if (t.owner !== playerId || !TERRAIN[t.terrain].workable) continue;
      if (game.hostileUnitOn(playerId, t.x, t.y)) continue;
      candidates.push({ idx, y: tileYield(t) });
    }
    // pinned tiles first
    const pinned = candidates.filter((c) => city.locked.includes(c.idx)).slice(0, city.pop);
    city.locked = city.locked.filter((k) => candidates.some((c) => c.idx === k));
    const free = candidates.filter((c) => !pinned.includes(c));
    const room = Math.max(0, city.pop - pinned.length);
    const pick = (wf: number) => {
      const w = FOCUS_WEIGHTS[city.focus];
      const scored = free
        .map((c) => ({ c, s: c.y.food * w.food * wf + c.y.prod * w.prod + c.y.gold * w.gold + (c.y.food + c.y.prod + c.y.gold) * 0.05 }))
        .sort((a, b) => b.s - a.s || a.c.idx - b.c.idx);
      return [...pinned, ...scored.slice(0, room).map((s) => s.c)];
    };
    let chosen = pick(1);
    const center = cityCenterYield(game, city);
    const surplus = (list: typeof chosen) => center.food + list.reduce((s, c) => s + c.y.food, 0) - city.pop * 2;
    if (surplus(chosen) < 0 && city.focus !== 'food') {
      const alt = pick(2.6);
      if (surplus(alt) > surplus(chosen)) chosen = alt;
    }
    city.worked = chosen.map((c) => c.idx);
    for (const c of chosen) used.add(c.idx);
  }
}

export function cityYield(game: Game, city: City): CityYield {
  const civ = civOf(game, city.owner);
  const y = cityCenterYield(game, city);
  let food = y.food;
  let prod = y.prod;
  let gold = y.gold;
  for (const idx of city.worked) {
    const t = tileYield(game.tiles[idx]);
    food += t.food;
    prod += t.prod;
    gold += t.gold;
  }
  let science = 3 + Math.floor(city.pop * 0.7) + (city.capital ? 2 : 0);
  let culture = 1;
  let upkeep = 0;
  for (const b of city.buildings) {
    const d = BUILDINGS[b];
    food += d.food ?? 0;
    prod += d.prod ?? 0;
    gold += d.gold ?? 0;
    science += d.science ?? 0;
    culture += d.culture ?? 0;
    upkeep += d.upkeep;
  }
  const pol = game.players[city.owner].policies;
  if (pol.includes('agrarian')) food += 1;
  if (pol.includes('craftsmen')) prod += 1;
  if (pol.includes('patronage')) culture += 2;
  if (pol.includes('bureaucracy')) upkeep = Math.max(0, upkeep - 1);
  if (pol.includes('trade_routes')) gold += city.capital ? 4 : 1;
  if (civ.id === 'maya') culture += 2;
  if (civ.id === 'greece') {
    gold += 2;
    if (hasBuilding(city, 'temple')) science += 1;
    if (hasBuilding(city, 'library')) science += 1;
  }
  if (city.capital) gold += 3;
  const eat = city.pop * 2;
  return { food, prod, gold, science, culture, upkeep, eat, surplus: food - eat };
}

export function unitUpkeep(game: Game, playerId: number): number {
  let military = 0;
  for (const u of game.units) {
    if (u.owner !== playerId) continue;
    const d = UNITS[u.type];
    if (!d.civilian) military++;
  }
  const cities = game.cities.filter((c) => c.owner === playerId).length;
  const free = 2 + cities * 2;
  return Math.max(0, military - free);
}

export function techCost(game: Game, player: Player, techId: string): number {
  const def = TECH_BY_ID[techId];
  const cities = game.cities.filter((c) => c.owner === player.id).length;
  const scale = 1 + 0.07 * Math.max(0, cities - 1);
  return Math.max(5, Math.round(def.cost * TECH_ERA_FACTOR[def.era] * game.speedMult * scale));
}

export function itemCost(game: Game, playerId: number, kind: 'unit' | 'building', id: string): number {
  const civ = civOf(game, playerId);
  let base: number;
  if (kind === 'unit') {
    base = UNITS[id as keyof typeof UNITS].cost;
    if (civ.id === 'rome' && !UNITS[id as keyof typeof UNITS].civilian) base *= 0.87;
    const pol = game.players[playerId].policies;
    if (pol.includes('drill') && !UNITS[id as keyof typeof UNITS].civilian) base *= 0.8;
    if (pol.includes('pioneers') && id === 'settler') base *= 0.7;
  } else {
    const b = BUILDINGS[id as BuildingId];
    base = b.cost;
    if (civ.id === 'egypt' && b.wonder) base *= 0.85;
  }
  return Math.max(5, Math.round(base * PROD_COST_FACTOR * game.speedMult));
}

/** Recompute worked tiles + per-turn totals shown in the UI. */
export function refreshPlayer(game: Game, playerId: number): void {
  const p = game.players[playerId];
  if (!p.alive) return;
  assignWorkedTiles(game, playerId);
  let gold = 0;
  let science = 0;
  let culture = 0;
  let upkeep = 0;
  for (const c of game.cities) {
    if (c.owner !== playerId) continue;
    const y = cityYield(game, c);
    gold += y.gold;
    science += y.science;
    culture += y.culture;
    upkeep += y.upkeep;
  }
  const civ = civOf(game, playerId);
  if (civ.id === 'sumer') science = Math.round(science * 1.1);
  if (civ.id === 'indus') gold = Math.round(gold * 1.1);
  if (p.policies.includes('scholarship')) science = Math.round(science * 1.1);
  if (p.policies.includes('coinage')) gold = Math.round(gold * 1.1);
  const net = gold - upkeep - unitUpkeep(game, playerId);
  p.income = { gold: net, science, culture };
}

export function availableTechs(player: Player): string[] {
  return TECHS.filter((t) => !player.techs.includes(t.id) && t.pre.every((p) => player.techs.includes(p))).map((t) => t.id);
}

export function canBuildUnit(game: Game, playerId: number, city: City, unitId: string): boolean {
  const d = UNITS[unitId as keyof typeof UNITS];
  if (!d) return false;
  const p = game.players[playerId];
  if (d.tech && !p.techs.includes(d.tech)) return false;
  if (d.obsoleteBy && p.techs.includes(d.obsoleteBy)) return false;
  if (d.needs && !game.hasResource(playerId, d.needs)) return false;
  if (d.id === 'settler' && city.pop < 2) return false;
  return true;
}

export function canBuildBuilding(game: Game, playerId: number, city: City, bid: string): boolean {
  const d = BUILDINGS[bid as BuildingId];
  if (!d) return false;
  const p = game.players[playerId];
  if (d.tech && !p.techs.includes(d.tech)) return false;
  if (city.buildings.includes(d.id)) return false;
  if (d.needs && !city.buildings.includes(d.needs)) return false;
  if (d.wonder) {
    if (game.s.wonders[d.id] !== undefined) return false;
    if (d.coastal && !game.isCoastal(city.x, city.y)) return false;
  }
  return true;
}

export function distanceToNearestCity(game: Game, x: number, y: number): number {
  let best = 99;
  for (const c of game.cities) best = Math.min(best, hexDistance(x, y, c.x, c.y));
  return best;
}
