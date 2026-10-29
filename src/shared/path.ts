import { neighborsOf, type Pos } from './hex';
import { TERRAIN, UNITS } from './data';
import type { Unit } from './types';
import type { Game } from './game';

/** Movement-point cost for `unit` to step onto tile (x,y) from (fx,fy). */
export function stepCost(game: Game, unit: Unit, fx: number, fy: number, x: number, y: number): number {
  const t = game.tile(x, y);
  const def = UNITS[unit.type];
  if (!TERRAIN[t.terrain].passable) return Infinity;
  if (def.scout) return 2;
  const from = game.tile(fx, fy);
  if (t.road && from.road) return 1;
  return TERRAIN[t.terrain].move;
}

/** Can this unit ever stand on this tile (ignoring who is there)? */
export function canEnterTerrain(game: Game, unit: Unit, x: number, y: number): boolean {
  return TERRAIN[game.tile(x, y).terrain].passable;
}

/**
 * Whether `unit` may legally move onto tile for the purposes of path planning:
 * foreign borders are closed under peace, enemy-occupied tiles block (the path ends before them).
 */
export function tileBlockedFor(game: Game, unit: Unit, x: number, y: number, finalStep: boolean): boolean {
  const t = game.tile(x, y);
  if (!TERRAIN[t.terrain].passable) return true;
  const def0 = UNITS[unit.type];
  if (t.owner >= 0 && t.owner !== unit.owner && game.relation(unit.owner, t.owner) === 'peace' && !def0.civilian && !def0.scout) return true;
  const occupants = game.unitsAt(x, y);
  const def = UNITS[unit.type];
  const seen = (t.visible & (1 << unit.owner)) !== 0;
  for (const o of occupants) {
    if (o.owner !== unit.owner) {
      if (!seen) continue; // hidden units do not influence planning
      return true;
    }
    // same-owner stacking rules (outside cities)
    if (finalStep && t.city < 0) {
      const odef = UNITS[o.type];
      if (!!odef.civilian === !!def.civilian) return true;
    }
  }
  if (t.city >= 0) {
    const c = game.cities.find((cc) => cc.id === t.city)!;
    if (c.owner !== unit.owner) return true;
  }
  return false;
}

interface Node {
  x: number;
  y: number;
  g: number;
  f: number;
  parent: Node | null;
}

/**
 * A* over move-point costs. Returns the list of steps (excluding the start) or null.
 * Only tiles the player has explored may be planned through (stops path-finding from leaking fog).
 */
/** `avoid` lists tile indices (y * width + x) the route must not step on, e.g. a friendly unit that blocks the way. */
export function findPath(game: Game, unit: Unit, to: Pos, opts: { maxCost?: number; avoid?: Set<number> } = {}): Pos[] | null {
  const w = game.width;
  const h = game.height;
  if (unit.x === to.x && unit.y === to.y) return [];
  const goalTile = game.tile(to.x, to.y);
  if (!TERRAIN[goalTile.terrain].passable) return null;
  const bit = 1 << unit.owner;
  const open: Node[] = [{ x: unit.x, y: unit.y, g: 0, f: 0, parent: null }];
  const best = new Map<number, number>();
  best.set(unit.y * w + unit.x, 0);
  const maxCost = opts.maxCost ?? 600;
  const heur = (x: number, y: number) => game.dist(x, y, to.x, to.y) * 1;
  let guard = 0;
  while (open.length && guard++ < 12000) {
    let bi = 0;
    for (let i = 1; i < open.length; i++) if (open[i].f < open[bi].f) bi = i;
    const cur = open[bi];
    open[bi] = open[open.length - 1];
    open.pop();
    if (cur.x === to.x && cur.y === to.y) {
      const out: Pos[] = [];
      let n: Node | null = cur;
      while (n && n.parent) {
        out.push({ x: n.x, y: n.y });
        n = n.parent;
      }
      return out.reverse();
    }
    if ((best.get(cur.y * w + cur.x) ?? Infinity) < cur.g) continue;
    for (const nb of neighborsOf(cur.x, cur.y, w, h)) {
      const t = game.tile(nb.x, nb.y);
      if (!(t.explored & bit)) continue;
      const isGoal = nb.x === to.x && nb.y === to.y;
      if (opts.avoid?.has(nb.y * w + nb.x) && !isGoal) continue;
      if (tileBlockedFor(game, unit, nb.x, nb.y, isGoal)) continue;
      // cost: use real movement cost, but add a small constant so shorter step counts win ties
      let c = stepCost(game, unit, cur.x, cur.y, nb.x, nb.y);
      if (!isFinite(c)) continue;
      c += 0.01;
      const g = cur.g + c;
      if (g > maxCost) continue;
      const key = nb.y * w + nb.x;
      if (g < (best.get(key) ?? Infinity)) {
        best.set(key, g);
        open.push({ x: nb.x, y: nb.y, g, f: g + heur(nb.x, nb.y) * 1, parent: cur });
      }
    }
  }
  return null;
}

/** Dijkstra flood: reachable tiles within this turn's remaining movement (for UI highlights). */
export function reachableTiles(game: Game, unit: Unit): Map<number, number> {
  const w = game.width;
  const h = game.height;
  const out = new Map<number, number>();
  const start = unit.y * w + unit.x;
  out.set(start, 0);
  const queue: Array<{ x: number; y: number; spent: number }> = [{ x: unit.x, y: unit.y, spent: 0 }];
  while (queue.length) {
    let bi = 0;
    for (let i = 1; i < queue.length; i++) if (queue[i].spent < queue[bi].spent) bi = i;
    const cur = queue[bi];
    queue[bi] = queue[queue.length - 1];
    queue.pop();
    if ((out.get(cur.y * w + cur.x) ?? Infinity) < cur.spent) continue;
    if (cur.spent >= unit.mp) continue;
    for (const nb of neighborsOf(cur.x, cur.y, w, h)) {
      if (tileBlockedFor(game, unit, nb.x, nb.y, true)) continue;
      const c = stepCost(game, unit, cur.x, cur.y, nb.x, nb.y);
      if (!isFinite(c)) continue;
      const spent = cur.spent + Math.min(c, unit.mp - cur.spent);
      const key = nb.y * w + nb.x;
      if (spent < (out.get(key) ?? Infinity)) {
        out.set(key, spent);
        queue.push({ x: nb.x, y: nb.y, spent });
      }
    }
  }
  out.delete(start);
  return out;
}
