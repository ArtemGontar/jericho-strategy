import { tilesWithin, type Pos } from './hex';
import { BUILDINGS, FINAL_TECH, IMPROVEMENTS, MIN_CITY_DISTANCE, PEACE_MIN_TURNS, RESOURCES, TECHS, TECH_BY_ID, TERRAIN, UNITS } from './data';
import { availableTechs, canBuildBuilding, canBuildUnit, itemCost, tileYield } from './economy';
import { findPath, tileBlockedFor } from './path';
import { previewAttack } from './combat';
import type { BuildingId, City, Player, Tile, Unit, UnitTypeId, WorkKind } from './types';
import type { Game } from './game';

// ---------------------------------------------------------------------------------------------
// Research

let finalClosure: Set<string> | null = null;
function closureOfFinal(): Set<string> {
  if (finalClosure) return finalClosure;
  const set = new Set<string>();
  const visit = (id: string) => {
    if (set.has(id)) return;
    set.add(id);
    for (const p of TECH_BY_ID[id].pre) visit(p);
  };
  visit(FINAL_TECH);
  finalClosure = set;
  return set;
}

const TECH_WEIGHT: Record<string, number> = {
  pottery: 3, mining: 4, archery: 2, bronze: 4, husbandry: 2, masonry: 3, writing: 7, wheel: 1,
  ironworking: 3, mathematics: 2, currency: 5, philosophy: 5, construction: 4,
  education: 6, engineering: 3, feudalism: 3, chivalry: 2, banking: 5,
  gunpowder: 4, metallurgy: 2, astronomy: 5, enlightenment: 10,
};

export function aiChooseResearch(game: Game, p: Player): string | null {
  const options = availableTechs(p);
  if (!options.length) return null;
  const closure = closureOfFinal();
  let best = options[0];
  let bestScore = -Infinity;
  for (const id of options) {
    const t = TECH_BY_ID[id];
    let s = (TECH_WEIGHT[id] ?? 2) - t.cost / 60 + (closure.has(id) ? 2.5 : 0);
    if (p.personality === 'warlord' && ['bronze', 'archery', 'husbandry', 'ironworking', 'mathematics', 'feudalism', 'chivalry', 'gunpowder', 'metallurgy'].includes(id)) s += 4;
    if (p.personality === 'builder' && ['pottery', 'writing', 'currency', 'philosophy', 'education', 'construction'].includes(id)) s += 2;
    if (game.rng.chance(0.15)) s += 1.5;
    if (s > bestScore) {
      bestScore = s;
      best = id;
    }
  }
  return best;
}

// ---------------------------------------------------------------------------------------------
// Shared helpers

const claimedSites = new Map<number, Set<number>>();

function exec(game: Game, pid: number, cmd: Parameters<Game['exec']>[1]) {
  game.autoDepth++;
  try {
    return game.exec(pid, cmd);
  } finally {
    game.autoDepth--;
  }
}

function military(game: Game, pid: number): Unit[] {
  return game.units.filter((u) => u.owner === pid && !UNITS[u.type].civilian);
}

function enemiesOf(game: Game, pid: number): number[] {
  return game.players.filter((o) => o.alive && o.id !== pid && game.atWar(pid, o.id)).map((o) => o.id);
}

function knownEnemyCities(game: Game, pid: number): City[] {
  const bit = 1 << pid;
  return game.cities.filter((c) => c.owner !== pid && game.atWar(pid, c.owner) && (game.tile(c.x, c.y).explored & bit) !== 0);
}

function nearest<T extends { x: number; y: number }>(from: Pos, list: T[]): T | null {
  let best: T | null = null;
  let bd = Infinity;
  for (const o of list) {
    const d = Math.abs(o.x - from.x) + Math.abs(o.y - from.y);
    if (d < bd) {
      bd = d;
      best = o;
    }
  }
  return best;
}

function hexNear(game: Game, from: Pos, list: Array<{ x: number; y: number }>) {
  let best: { x: number; y: number } | null = null;
  let bd = Infinity;
  for (const o of list) {
    const d = game.dist(from.x, from.y, o.x, o.y);
    if (d < bd) {
      bd = d;
      best = o;
    }
  }
  return best;
}

function gotoTile(game: Game, u: Unit, to: Pos): boolean {
  if (u.x === to.x && u.y === to.y) return true;
  const r = exec(game, u.owner, { t: 'move', unit: u.id, to });
  return r.ok;
}

// ---------------------------------------------------------------------------------------------
// Exploration

export function aiAutoExplore(game: Game, u: Unit): void {
  const bit = 1 << u.owner;
  const w = game.width;
  const h = game.height;
  // BFS outward over explored, passable tiles until one touches the unknown
  const seen = new Set<number>([u.y * w + u.x]);
  let frontier: Pos[] = [{ x: u.x, y: u.y }];
  let found: Pos | null = null;
  let depth = 0;
  while (frontier.length && !found && depth < 40) {
    const next: Pos[] = [];
    for (const cur of frontier) {
      for (const nb of game.nb(cur.x, cur.y)) {
        const k = nb.y * w + nb.x;
        if (seen.has(k)) continue;
        seen.add(k);
        const t = game.tile(nb.x, nb.y);
        if (!(t.explored & bit)) continue;
        if (!TERRAIN[t.terrain].passable) continue;
        if (tileBlockedFor(game, u, nb.x, nb.y, false)) continue;
        // wary of enemy cities
        let touchesUnknown = false;
        for (const nn of game.nb(nb.x, nb.y)) if (!(game.tile(nn.x, nn.y).explored & bit)) { touchesUnknown = true; break; }
        if (touchesUnknown) {
          found = nb;
          break;
        }
        next.push(nb);
      }
      if (found) break;
    }
    frontier = next;
    depth++;
  }
  if (!found) {
    // everything reachable is explored
    u.auto = null;
    const home = nearest(u, game.cities.filter((c) => c.owner === u.owner));
    if (home && (u.x !== home.x || u.y !== home.y)) exec(game, u.owner, { t: 'move', unit: u.id, to: { x: home.x, y: home.y } });
    exec(game, u.owner, { t: 'order', unit: u.id, order: 'fortify' });
    return;
  }
  exec(game, u.owner, { t: 'move', unit: u.id, to: found });
  // keep going while movement remains (new tiles may have been revealed)
  let guard = 0;
  while (u.mp > 0 && !u.order && guard++ < 4 && game.unit(u.id)) {
    const before = u.x * 1000 + u.y;
    aiExploreStep(game, u);
    if (u.x * 1000 + u.y === before) break;
  }
}

function aiExploreStep(game: Game, u: Unit): void {
  const bit = 1 << u.owner;
  const w = game.width;
  const seen = new Set<number>([u.y * w + u.x]);
  let frontier: Pos[] = [{ x: u.x, y: u.y }];
  let depth = 0;
  while (frontier.length && depth < 14) {
    const next: Pos[] = [];
    for (const cur of frontier) {
      for (const nb of game.nb(cur.x, cur.y)) {
        const k = nb.y * w + nb.x;
        if (seen.has(k)) continue;
        seen.add(k);
        const t = game.tile(nb.x, nb.y);
        if (!(t.explored & bit) || !TERRAIN[t.terrain].passable || tileBlockedFor(game, u, nb.x, nb.y, false)) continue;
        for (const nn of game.nb(nb.x, nb.y)) {
          if (!(game.tile(nn.x, nn.y).explored & bit)) {
            exec(game, u.owner, { t: 'move', unit: u.id, to: nb });
            return;
          }
        }
        next.push(nb);
      }
    }
    frontier = next;
    depth++;
  }
}

// ---------------------------------------------------------------------------------------------
// Workers

function tileWorkValue(game: Game, p: Player, t: Tile, kind: WorkKind): number {
  if (t.city >= 0 || t.owner !== p.id) return 0;
  const def = IMPROVEMENTS[kind];
  if (def.tech && !p.techs.includes(def.tech)) return 0;
  if (!def.terrains.includes(t.terrain)) return 0;
  const cityNear = game.cities.some((c) => c.owner === p.id && game.dist(c.x, c.y, t.x, t.y) <= c.radius);
  if (!cityNear) return 0;
  const worked = game.cities.some((c) => c.owner === p.id && c.worked.includes(t.y * game.width + t.x));
  const wMul = worked ? 1.7 : 0.8;
  if (kind === 'farm') {
    if (t.improvement) return 0;
    const base = tileYield(t);
    if (t.terrain === 'desert') return base.food + 1 < 1 ? 0 : 0.5 * wMul;
    return (t.resource === 'wheat' ? 2.4 : 1.6) * wMul + (base.prod === 0 ? 0.2 : 0);
  }
  if (kind === 'mine') {
    if (t.improvement) return 0;
    return (t.resource === 'iron' || t.resource === 'gold' ? 3.2 : 2.2) * wMul;
  }
  if (t.road) return 0;
  // roads: only where they connect something
  const nearCity = game.cities.some((c) => c.owner === p.id && game.dist(c.x, c.y, t.x, t.y) <= 1);
  return nearCity ? 0.55 : 0.25;
}

export function aiAutoWorker(game: Game, u: Unit): void {
  const p = game.players[u.owner];
  if (u.type !== 'worker') return;
  // if already on a useful tile, build there
  const kinds: WorkKind[] = ['farm', 'mine', 'road'];
  let bestScore = 0;
  let bestTile: Tile | null = null;
  let bestKind: WorkKind = 'farm';
  for (const c of game.cities) {
    if (c.owner !== p.id) continue;
    for (const pos of tilesWithin(c.x, c.y, c.radius, game.width, game.height)) {
      const t = game.tile(pos.x, pos.y);
      if (t.owner !== p.id) continue;
      if (game.unitsAt(t.x, t.y).some((o) => o.owner !== u.owner)) continue;
      // another worker already on it?
      if (game.unitsAt(t.x, t.y).some((o) => o.id !== u.id && o.type === 'worker')) continue;
      for (const k of kinds) {
        let v = tileWorkValue(game, p, t, k);
        if (v <= 0) continue;
        const d = game.dist(u.x, u.y, t.x, t.y);
        v -= d * 0.12;
        // work already started here is worth finishing
        if (t.work && t.work.kind === k) v += 0.6;
        if (v > bestScore) {
          bestScore = v;
          bestTile = t;
          bestKind = k;
        }
      }
    }
  }
  if (!bestTile) {
    // nothing to do: stay put
    return;
  }
  if (u.x === bestTile.x && u.y === bestTile.y) {
    exec(game, u.owner, { t: 'work', unit: u.id, what: bestKind });
    return;
  }
  const r = exec(game, u.owner, { t: 'move', unit: u.id, to: { x: bestTile.x, y: bestTile.y } });
  if (r.ok && u.x === bestTile.x && u.y === bestTile.y && u.mp >= 0) exec(game, u.owner, { t: 'work', unit: u.id, what: bestKind });
}

// ---------------------------------------------------------------------------------------------
// Settling

function siteScore(game: Game, p: Player, x: number, y: number, from: Pos): number {
  const t = game.tile(x, y);
  if (!TERRAIN[t.terrain].land || t.terrain === 'mountain') return -1;
  if (t.owner >= 0 && t.owner !== p.id) return -1;
  if (t.city >= 0) return -1;
  let nearestOwn = 99;
  for (const c of game.cities) {
    const d = game.dist(c.x, c.y, x, y);
    if (d < MIN_CITY_DISTANCE) return -1;
    if (c.owner === p.id) nearestOwn = Math.min(nearestOwn, d);
    else if (d < 5) return -1;
  }
  let s = 0;
  for (const q of tilesWithin(x, y, 2, game.width, game.height)) {
    const qt = game.tile(q.x, q.y);
    if (qt.owner >= 0 && qt.owner !== p.id) continue;
    const y2 = tileYield(qt);
    s += y2.food * 1.3 + y2.prod * 1.0 + y2.gold * 0.5;
    if (qt.resource && RESOURCES[qt.resource].strategic) s += 1.5;
    if (qt.city >= 0) s -= 2;
    if (qt.owner === p.id) s -= 0.3;
  }
  if (t.terrain === 'hills') s += 3;
  if (game.isCoastal(x, y)) s += 2;
  if (t.terrain === 'desert' || t.terrain === 'tundra') s -= 4;
  if (nearestOwn < 99) s += nearestOwn <= 5 ? 3 : -(nearestOwn - 5) * 1.2;
  s -= game.dist(from.x, from.y, x, y) * 0.55;
  return s;
}

function bestSite(game: Game, p: Player, u: Unit | null, from: Pos): { x: number; y: number; score: number } | null {
  const bit = 1 << p.id;
  const taken = claimedSites.get(p.id) ?? new Set();
  let best: { x: number; y: number; score: number } | null = null;
  const candidates: Array<{ x: number; y: number; score: number }> = [];
  for (const t of game.tiles) {
    if (!(t.explored & bit)) continue;
    if (game.dist(from.x, from.y, t.x, t.y) > 14) continue;
    const key = t.y * game.width + t.x;
    let blocked = false;
    for (const k of taken) {
      const kx = k % game.width;
      const ky = Math.floor(k / game.width);
      if (game.dist(kx, ky, t.x, t.y) < MIN_CITY_DISTANCE) { blocked = true; break; }
    }
    if (blocked && !(u && u.x === t.x && u.y === t.y && taken.has(key))) continue;
    const s = siteScore(game, p, t.x, t.y, from);
    if (s > 6) candidates.push({ x: t.x, y: t.y, score: s });
  }
  candidates.sort((a, b) => b.score - a.score);
  for (const c of candidates.slice(0, 6)) {
    if (u) {
      if (u.x === c.x && u.y === c.y) return c;
      if (!findPath(game, u, c)) continue;
    }
    best = c;
    break;
  }
  return best;
}

function runSettler(game: Game, u: Unit): void {
  const p = game.players[u.owner];
  const site = bestSite(game, p, u, u);
  if (!site) {
    // nowhere to go: found right here if legal, else wait
    const r = exec(game, u.owner, { t: 'found', unit: u.id });
    if (!r.ok) exec(game, u.owner, { t: 'order', unit: u.id, order: 'sleep' });
    return;
  }
  const set = claimedSites.get(p.id) ?? new Set<number>();
  set.add(site.y * game.width + site.x);
  claimedSites.set(p.id, set);
  if (u.x === site.x && u.y === site.y) {
    exec(game, u.owner, { t: 'found', unit: u.id });
    return;
  }
  exec(game, u.owner, { t: 'move', unit: u.id, to: site });
  if (game.unit(u.id) && u.x === site.x && u.y === site.y && u.mp > 0) exec(game, u.owner, { t: 'found', unit: u.id });
}

// ---------------------------------------------------------------------------------------------
// Military

function garrison(game: Game, c: City): Unit[] {
  return game.unitsAt(c.x, c.y).filter((u) => u.owner === c.owner && !UNITS[u.type].civilian);
}

function threatNear(game: Game, c: City, radius = 4): number {
  let s = 0;
  for (const q of tilesWithin(c.x, c.y, radius, game.width, game.height)) {
    for (const o of game.unitsAt(q.x, q.y)) {
      if (o.owner !== c.owner && game.atWar(c.owner, o.owner) && !UNITS[o.type].civilian && (game.tile(q.x, q.y).visible & (1 << c.owner)) !== 0) {
        s += Math.max(UNITS[o.type].strength, UNITS[o.type].ranged) / 8;
      }
    }
  }
  return s;
}

function tryAttack(game: Game, u: Unit): boolean {
  const d = UNITS[u.type];
  if (u.attacked || u.mp <= 0) return false;
  const range = d.ranged > 0 ? d.range : 1;
  let best: { x: number; y: number; score: number } | null = null;
  for (const q of tilesWithin(u.x, u.y, range, game.width, game.height)) {
    if (q.x === u.x && q.y === u.y) continue;
    if (!(game.tile(q.x, q.y).visible & (1 << u.owner))) continue;
    const pv = previewAttack(game, u, q.x, q.y);
    if (!pv.ok) continue;
    let score: number;
    if (pv.capture) score = 100;
    else if (pv.city) {
      const city = game.cityAt(q.x, q.y)!;
      score = 3 + (1 - city.hp / 300) * 4 + (pv.dmgDef - pv.dmgAtt) / 20;
      if (d.ranged === 0 && pv.dmgAtt > u.hp * 0.7) score = -1;
    } else {
      const killsIt = pv.dmgDef >= pv.defHp;
      score = (pv.dmgDef - pv.dmgAtt * 1.2) / 15 + (killsIt ? 4 : 0);
      if (d.ranged === 0 && pv.dmgAtt >= u.hp) score = -1;
    }
    if (score > 0.2 && (!best || score > best.score)) best = { x: q.x, y: q.y, score };
  }
  if (!best) return false;
  const r = exec(game, u.owner, { t: 'attack', unit: u.id, to: best });
  return r.ok;
}

function freeAdjacentTo(game: Game, u: Unit, target: Pos, wantDist: number): Pos | null {
  let best: Pos | null = null;
  let bd = Infinity;
  for (const q of tilesWithin(target.x, target.y, wantDist, game.width, game.height)) {
    if (game.dist(q.x, q.y, target.x, target.y) !== wantDist) continue;
    if (tileBlockedFor(game, u, q.x, q.y, true)) continue;
    const d = game.dist(u.x, u.y, q.x, q.y);
    if (d < bd) {
      bd = d;
      best = q;
    }
  }
  return best;
}

interface WarPlan {
  target: City | null;
  rally: Pos | null;
}

function planWar(game: Game, p: Player): WarPlan {
  const enemyCities = knownEnemyCities(game, p.id);
  if (!enemyCities.length) return { target: null, rally: null };
  const mine = game.cities.filter((c) => c.owner === p.id);
  let best: City | null = null;
  let bs = Infinity;
  for (const e of enemyCities) {
    const near = hexNear(game, e, mine);
    const d = near ? game.dist(e.x, e.y, near.x, near.y) : 30;
    const s = d + e.hp / 40 - (e.capital ? 0 : 0);
    if (s < bs) {
      bs = s;
      best = e;
    }
  }
  if (!best) return { target: null, rally: null };
  return { target: best, rally: null };
}

function runMilitary(game: Game, p: Player, units: Unit[]): void {
  const cities = game.cities.filter((c) => c.owner === p.id);
  if (!cities.length) return;
  const plan = planWar(game, p);
  const need = new Map<number, number>();
  for (const c of cities) {
    const th = threatNear(game, c);
    need.set(c.id, th > 0.5 ? 2 : p.personality === 'warlord' ? 1 : 1);
    if (th > 2) need.set(c.id, 3);
  }
  const assigned = new Set<number>();
  // 1. wounded units rest
  for (const u of units) {
    if (u.hp < 45 && !game.cityAt(u.x, u.y)) {
      const home = hexNear(game, u, cities);
      if (home) {
        gotoTile(game, u, home);
        assigned.add(u.id);
      }
    }
  }
  // 2. garrisons: units already standing in a city stay; shortfalls are filled by the nearest free unit
  const garrisonCount = new Map<number, number>();
  for (const c of cities) {
    let n = 0;
    for (const u of garrison(game, c)) {
      if (units.includes(u) && !UNITS[u.type].scout) {
        n++;
        // defenders stay
      }
    }
    garrisonCount.set(c.id, n);
  }
  const idle = units.filter((u) => !assigned.has(u.id) && !UNITS[u.type].scout && game.unit(u.id));
  for (const c of cities) {
    while ((garrisonCount.get(c.id) ?? 0) < (need.get(c.id) ?? 1)) {
      // unit not currently garrisoning anywhere else with the closest distance
      let pick: Unit | null = null;
      let pd = Infinity;
      for (const u of idle) {
        if (assigned.has(u.id)) continue;
        const inCity = game.cityAt(u.x, u.y);
        if (inCity && inCity.owner === p.id && (garrisonCount.get(inCity.id) ?? 0) <= (need.get(inCity.id) ?? 1) && inCity.id !== c.id) continue;
        const d = game.dist(u.x, u.y, c.x, c.y);
        if (d < pd) {
          pd = d;
          pick = u;
        }
      }
      if (!pick || pd > 12) break;
      assigned.add(pick.id);
      garrisonCount.set(c.id, (garrisonCount.get(c.id) ?? 0) + 1);
      if (pick.x !== c.x || pick.y !== c.y) gotoTile(game, pick, c);
      if (game.unit(pick.id) && pick.x === c.x && pick.y === c.y) exec(game, p.id, { t: 'order', unit: pick.id, order: 'fortify' });
    }
  }
  // garrison units that are home: shoot things in range, then fortify
  for (const u of units) {
    if (!game.unit(u.id)) continue;
    const c = game.cityAt(u.x, u.y);
    if (c && c.owner === p.id && assigned.has(u.id) && (u.x === c.x && u.y === c.y)) {
      if (UNITS[u.type].ranged > 0) tryAttack(game, u);
      else if (threatNear(game, c, 1) > 0) tryAttack(game, u);
      if (game.unit(u.id) && !u.fortified) exec(game, p.id, { t: 'order', unit: u.id, order: 'fortify' });
    }
  }
  // 3. the rest form the field army
  const army = units.filter((u) => game.unit(u.id) && !assigned.has(u.id) && !UNITS[u.type].scout);
  const atWar = enemiesOf(game, p.id).length > 0;
  const target = plan.target;
  const wantsAttack = atWar && target && army.length >= (p.personality === 'warlord' ? 3 : 4);
  if (wantsAttack && target) {
    // gather within 4 tiles of the target, then go in together
    const near = army.filter((u) => game.dist(u.x, u.y, target.x, target.y) <= 4).length;
    const ready = near >= Math.max(3, Math.ceil(army.length * 0.6)) || game.turn - p.lastDefeat < 0;
    for (const u of army) {
      if (!game.unit(u.id)) continue;
      if (tryAttack(game, u)) continue;
      const d = UNITS[u.type];
      const ranged = d.ranged > 0;
      const dist = game.dist(u.x, u.y, target.x, target.y);
      if (dist <= 4 && !ready && !ranged) continue; // wait for friends
      if (ready || dist > 4) {
        const wantDist = ranged ? Math.min(2, d.range) : 1;
        if (dist <= wantDist && ranged) continue;
        const spot = freeAdjacentTo(game, u, target, wantDist) ?? freeAdjacentTo(game, u, target, 2) ?? freeAdjacentTo(game, u, target, 1);
        if (spot) gotoTile(game, u, spot);
        if (game.unit(u.id)) tryAttack(game, u);
      }
    }
  } else {
    // at war but blind: send a couple of soldiers to find the enemy
    if (atWar && !target) {
      const scouting = army.filter((u) => u.auto === 'explore').length;
      for (const u of army) {
        if (scouting >= 2) break;
        if (!u.auto && UNITS[u.type].ranged === 0 && UNITS[u.type].moves >= 2) {
          u.auto = 'explore';
          aiAutoExplore(game, u);
          break;
        }
      }
    }
    // peacetime or too small: hunt trespassers near cities, otherwise fortify on the border
    for (const u of army) {
      if (u.auto === 'explore') continue;
      if (!game.unit(u.id)) continue;
      if (tryAttack(game, u)) continue;
      if (!u.order && !u.fortified) {
        const home = hexNear(game, u, cities);
        if (home && (u.x !== home.x || u.y !== home.y) && game.dist(u.x, u.y, home.x, home.y) > 2) gotoTile(game, u, home);
        else if (game.unit(u.id)) exec(game, p.id, { t: 'order', unit: u.id, order: 'fortify' });
      }
    }
  }
}

// ---------------------------------------------------------------------------------------------
// Production

function countUnits(game: Game, pid: number, type: UnitTypeId): number {
  return game.units.filter((u) => u.owner === pid && u.type === type).length;
}

function queuedIn(game: Game, pid: number, kind: 'unit' | 'building', id: string): number {
  return game.cities.filter((c) => c.owner === pid && c.queue.some((q) => q.kind === kind && q.id === id)).length;
}

function bestMilitary(game: Game, p: Player, city: City, role: 'melee' | 'ranged' | 'siege' | 'any'): UnitTypeId | null {
  let best: UnitTypeId | null = null;
  let bs = -1;
  for (const id of Object.keys(UNITS) as UnitTypeId[]) {
    const d = UNITS[id];
    if (d.civilian || d.scout) continue;
    if (!canBuildUnit(game, p.id, city, id)) continue;
    const power = Math.max(d.strength, d.ranged * 0.85);
    if (role === 'melee' && d.ranged > 0) continue;
    if (role === 'ranged' && !(d.ranged > 0 && !d.siege)) continue;
    if (role === 'siege' && !d.siege) continue;
    const s = power / Math.sqrt(d.cost);
    if (power > bs * 0 && s > bs) {
      bs = s;
      best = id;
    }
  }
  return best;
}

export function aiPlanCity(game: Game, p: Player, city: City): void {
  if (city.queue.length) return;
  const myCities = game.cities.filter((c) => c.owner === p.id);
  const options: Array<{ kind: 'unit' | 'building'; id: string; score: number }> = [];
  const add = (kind: 'unit' | 'building', id: string, score: number) => {
    if (kind === 'unit' ? !canBuildUnit(game, p.id, city, id) : !canBuildBuilding(game, p.id, city, id)) return;
    options.push({ kind, id, score: score + game.rng.next() * 0.8 });
  };
  const gar = garrison(game, city).filter((u) => !UNITS[u.type].scout).length;
  const th = threatNear(game, city);
  const warNow = enemiesOf(game, p.id).length > 0;
  const workers = countUnits(game, p.id, 'worker');
  const settlers = countUnits(game, p.id, 'settler') + queuedIn(game, p.id, 'unit', 'settler');
  const armySize = military(game, p.id).filter((u) => !UNITS[u.type].scout).length;
  const desiredCities = p.personality === 'builder' ? 7 : p.personality === 'warlord' ? 4 : 5;
  const site = myCities.length + settlers < desiredCities + (game.turn > 120 ? 0 : 0) ? bestSite(game, p, null, city) : null;
  const sciWeight = p.personality === 'builder' ? 1.2 : 1;

  if (gar === 0 || (th > 0.5 && gar < 2)) {
    const d = bestMilitary(game, p, city, 'melee') ?? 'warrior';
    add('unit', d, 10 + th * 3);
  }
  if (workers < Math.max(1, Math.floor(myCities.length * 0.8)) && queuedIn(game, p.id, 'unit', 'worker') === 0) add('unit', 'worker', 8);
  if (site && city.pop >= 2 && settlers < 2) add('unit', 'settler', 9.5 - myCities.length * 0.4);
  if (site && city.pop === 1 && settlers < 1 && myCities.length < 3) add('unit', 'settler', 6);
  // buildings
  add('building', 'granary', 3.2);
  add('building', 'library', 6 * sciWeight);
  add('building', 'workshop', city.pop >= 3 ? 4.6 : 2.5);
  add('building', 'market', 5.2);
  add('building', 'temple', 4.1);
  add('building', 'university', 6.4 * sciWeight);
  add('building', 'bank', 5);
  add('building', 'observatory', 5);
  if (city.pop >= 7) add('building', 'aqueduct', 7);
  if (th > 0.8 || (warNow && city.hp < 150)) add('building', 'walls', 6.5);
  if (p.personality === 'warlord' || warNow) add('building', 'barracks', 4.8);
  // wonders only in the strongest production city, one at a time per player
  const topProd = myCities.slice().sort((a, b) => b.pop - a.pop)[0];
  if (topProd && topProd.id === city.id) {
    for (const b of Object.values(BUILDINGS)) {
      if (!b.wonder) continue;
      if (queuedIn(game, p.id, 'building', b.id) > 0) continue;
      add('building', b.id, 5.6);
    }
  }
  // military
  const wantArmy = p.personality === 'warlord' ? 3 + myCities.length * 2 : 2 + myCities.length * 1.2;
  if (armySize < wantArmy) {
    const role = game.rng.chance(0.3) ? 'ranged' : game.rng.chance(0.2) ? 'siege' : 'melee';
    const u = bestMilitary(game, p, city, role) ?? bestMilitary(game, p, city, 'any');
    if (u) add('unit', u, (warNow ? 8.2 : p.personality === 'warlord' ? 6.2 : 3.3) + (armySize < myCities.length ? 2 : 0));
  }
  if (warNow && armySize >= 4) {
    const siege = bestMilitary(game, p, city, 'siege');
    if (siege && countUnits(game, p.id, siege) < 3) add('unit', siege, 7);
  }
  if (!options.length) {
    const d = bestMilitary(game, p, city, 'any');
    if (d) add('unit', d, 1);
    else add('unit', 'warrior', 1);
  }
  options.sort((a, b) => b.score - a.score);
  const pick = options[0];
  if (pick) exec(game, p.id, { t: 'queue', city: city.id, op: 'set', kind: pick.kind, id: pick.id });
}

function maybeBuy(game: Game, p: Player, city: City): void {
  const head = city.queue[0];
  if (!head) return;
  const price = game.buyPrice(city);
  if (price === null) return;
  const th = threatNear(game, city);
  const reserve = 40;
  if (p.gold - price < reserve) return;
  const urgent = th > 0.8 && head.kind === 'unit';
  const useful = head.kind === 'unit' ? head.id === 'settler' || head.id === 'worker' : true;
  if (urgent || (useful && price < p.gold * 0.5 && game.rng.chance(0.6))) exec(game, p.id, { t: 'buy', city: city.id });
}

// ---------------------------------------------------------------------------------------------
// Diplomacy

function runDiplomacy(game: Game, p: Player): void {
  const others = game.players.filter((o) => o.alive && o.id !== p.id && game.relation(p.id, o.id) !== 'unmet');
  const myPower = game.militaryPower(p.id);
  const wars = others.filter((o) => game.atWar(p.id, o.id));
  for (const o of others) {
    const rel = game.relation(p.id, o.id);
    const since = game.turn - game.s.relationSince[p.id][o.id];
    if (rel === 'war') {
      const theirPower = game.militaryPower(o.id);
      const wantsPeace =
        p.personality === 'builder' ? since >= 3 && game.rng.chance(0.35)
        : p.personality === 'balanced' ? since >= 6 && (theirPower > myPower * 0.9 || game.rng.chance(0.08))
        : theirPower > myPower * 1.25 && since >= 6 && game.rng.chance(0.3);
      const alreadyAsked = game.s.proposals.some((x) => x.from === p.id && x.to === o.id);
      if (wantsPeace && !alreadyAsked) exec(game, p.id, { t: 'peace', to: o.id });
    } else if (rel === 'peace' && since >= PEACE_MIN_TURNS && wars.length === 0 && game.turn > 25) {
      const theirPower = game.militaryPower(o.id);
      const ratio = myPower / Math.max(1, theirPower);
      const neighbours = game.cities.some((c) => c.owner === p.id && game.cities.some((e) => e.owner === o.id && game.dist(c.x, c.y, e.x, e.y) < 14));
      if (!neighbours) continue;
      const prob = p.personality === 'warlord' ? (ratio > 1.15 ? 0.06 : 0.01) : p.personality === 'balanced' ? (ratio > 1.5 ? 0.025 : 0) : ratio > 2.2 ? 0.01 : 0;
      if (game.rng.chance(prob)) exec(game, p.id, { t: 'war', to: o.id });
    }
  }
}

// ---------------------------------------------------------------------------------------------
// Entry point

export function aiTakeTurn(game: Game, pid: number): void {
  const p = game.players[pid];
  claimedSites.set(pid, new Set());
  if (!p.researching) p.researching = aiChooseResearch(game, p);
  runDiplomacy(game, p);
  // settlers first so cities are founded before planning production
  for (const u of game.units.filter((x) => x.owner === pid && x.type === 'settler')) {
    if (game.unit(u.id)) runSettler(game, u);
  }
  for (const u of game.units.filter((x) => x.owner === pid && x.type === 'worker')) {
    if (!game.unit(u.id)) continue;
    if (u.auto !== 'work') u.auto = 'work';
    if (!u.order) aiAutoWorker(game, u);
  }
  for (const u of game.units.filter((x) => x.owner === pid && x.type === 'scout')) {
    if (!game.unit(u.id)) continue;
    if (u.auto !== 'explore') u.auto = 'explore';
    if (!u.order && u.auto === 'explore') aiAutoExplore(game, u);
  }
  runMilitary(game, p, military(game, pid).filter((u) => !UNITS[u.type].scout));
  for (const c of game.cities.filter((x) => x.owner === pid)) {
    aiPlanCity(game, p, c);
    maybeBuy(game, p, c);
  }
}

void itemCost;
void TECHS;
