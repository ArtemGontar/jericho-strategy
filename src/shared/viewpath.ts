import { neighborsOf, type Pos } from './hex';
import { TERRAIN, UNITS } from './data';
import { canStandOn, navalStepCost } from './naval';
import { TERRAIN_LIST, type GameView, type ViewUnit } from './view';

/** Client-side movement planning from a player's view (mirrors the server rules closely enough for previews). */
export class ViewMap {
  w: number;
  h: number;
  constructor(public view: GameView) {
    this.w = view.settings.width;
    this.h = view.settings.height;
  }

  terrain(x: number, y: number) {
    return TERRAIN[TERRAIN_LIST[this.view.terrain[y * this.w + x]]];
  }
  terrainId(x: number, y: number) {
    return TERRAIN_LIST[this.view.terrain[y * this.w + x]];
  }
  explored(x: number, y: number): boolean { return this.view.state[y * this.w + x] > 0; }
  visible(x: number, y: number): boolean { return this.view.state[y * this.w + x] === 2; }
  road(x: number, y: number): boolean { return this.view.road[y * this.w + x] === 1; }
  owner(x: number, y: number): number { return this.view.owner[y * this.w + x] - 1; }

  unitsAt(x: number, y: number): ViewUnit[] {
    return this.view.units.filter((u) => u.x === x && u.y === y);
  }
  cityAt(x: number, y: number) {
    return this.view.cities.find((c) => c.x === x && c.y === y);
  }

  isCoastal(x: number, y: number): boolean {
    for (const n of neighborsOf(x, y, this.w, this.h)) {
      const t = this.terrainId(n.x, n.y);
      if (t === 'ocean' || t === 'coast') return true;
    }
    return false;
  }

  /** May this unit stand here at all? Ships need water, or a friendly coastal city. */
  standable(u: ViewUnit, x: number, y: number): boolean {
    const def = UNITS[u.type];
    const city = def.naval ? this.cityAt(x, y) : undefined;
    const port = !!city && city.owner === u.owner && this.isCoastal(x, y);
    return canStandOn(u.type, this.terrainId(x, y), port);
  }

  stepCost(u: ViewUnit, fx: number, fy: number, x: number, y: number): number {
    const def = UNITS[u.type];
    const t = this.terrain(x, y);
    if (!this.standable(u, x, y)) return Infinity;
    if (def.naval) return navalStepCost();
    if (def.scout) return 2;
    if (this.road(x, y) && this.road(fx, fy)) return 1;
    return t.move;
  }

  blocked(u: ViewUnit, x: number, y: number, finalStep: boolean): boolean {
    if (!this.explored(x, y)) return true;
    if (!this.standable(u, x, y)) return true;
    const owner = this.owner(x, y);
    const d0 = UNITS[u.type];
    if (owner >= 0 && owner !== u.owner && this.view.players[owner]?.relation === 'peace' && !d0.civilian && !d0.scout) return true;
    const city = this.cityAt(x, y);
    if (city && city.owner !== u.owner) return true;
    const def = UNITS[u.type];
    for (const o of this.unitsAt(x, y)) {
      if (o.carriedBy != null) continue;
      if (o.owner !== u.owner) return true;
      const od = UNITS[o.type];
      const sameClass = !!od.naval === !!def.naval && (def.naval || !!od.civilian === !!def.civilian);
      if (finalStep && !city && sameClass && o.id !== u.id) return true;
    }
    return false;
  }

  /** Cheapest route to `to` as steps (excluding start) with cumulative cost; null when unreachable. */
  path(u: ViewUnit, to: Pos): Array<Pos & { cost: number }> | null {
    if (u.x === to.x && u.y === to.y) return [];
    const key = (x: number, y: number) => y * this.w + x;
    const best = new Map<number, number>([[key(u.x, u.y), 0]]);
    const prev = new Map<number, number>();
    const open: Array<{ x: number; y: number; g: number; f: number }> = [{ x: u.x, y: u.y, g: 0, f: 0 }];
    const dist = (x: number, y: number) => hexDist(x, y, to.x, to.y);
    let guard = 0;
    while (open.length && guard++ < 8000) {
      let bi = 0;
      for (let i = 1; i < open.length; i++) if (open[i].f < open[bi].f) bi = i;
      const cur = open[bi];
      open[bi] = open[open.length - 1];
      open.pop();
      if (cur.x === to.x && cur.y === to.y) {
        const out: Array<Pos & { cost: number }> = [];
        let k = key(cur.x, cur.y);
        while (k !== key(u.x, u.y)) {
          out.push({ x: k % this.w, y: Math.floor(k / this.w), cost: best.get(k)! });
          k = prev.get(k)!;
        }
        return out.reverse();
      }
      if ((best.get(key(cur.x, cur.y)) ?? Infinity) < cur.g) continue;
      for (const nb of neighborsOf(cur.x, cur.y, this.w, this.h)) {
        const isGoal = nb.x === to.x && nb.y === to.y;
        if (this.blocked(u, nb.x, nb.y, isGoal)) continue;
        const c = this.stepCost(u, cur.x, cur.y, nb.x, nb.y);
        if (!isFinite(c)) continue;
        const g = cur.g + c + 0.01;
        const k = key(nb.x, nb.y);
        if (g < (best.get(k) ?? Infinity)) {
          best.set(k, g);
          prev.set(k, key(cur.x, cur.y));
          open.push({ x: nb.x, y: nb.y, g, f: g + dist(nb.x, nb.y) });
        }
      }
    }
    return null;
  }

  /** Tiles reachable this turn with remaining movement (key -> cost). */
  reachable(u: ViewUnit): Map<number, number> {
    const mp = u.mp ?? 0;
    const out = new Map<number, number>();
    const start = u.y * this.w + u.x;
    out.set(start, 0);
    const open = [{ x: u.x, y: u.y, spent: 0 }];
    while (open.length) {
      let bi = 0;
      for (let i = 1; i < open.length; i++) if (open[i].spent < open[bi].spent) bi = i;
      const cur = open[bi];
      open[bi] = open[open.length - 1];
      open.pop();
      if ((out.get(cur.y * this.w + cur.x) ?? Infinity) < cur.spent) continue;
      if (cur.spent >= mp) continue;
      for (const nb of neighborsOf(cur.x, cur.y, this.w, this.h)) {
        if (this.blocked(u, nb.x, nb.y, true)) continue;
        const c = this.stepCost(u, cur.x, cur.y, nb.x, nb.y);
        if (!isFinite(c)) continue;
        const spent = cur.spent + Math.min(c, mp - cur.spent);
        const k = nb.y * this.w + nb.x;
        if (spent < (out.get(k) ?? Infinity)) {
          out.set(k, spent);
          open.push({ x: nb.x, y: nb.y, spent });
        }
      }
    }
    out.delete(start);
    return out;
  }

  /** Number of turns needed to walk `path` starting with `mp` movement left and `full` movement per turn. */
  turnsFor(u: ViewUnit, path: Array<Pos & { cost: number }>): number {
    const full = UNITS[u.type].moves * 2;
    let mp = u.mp ?? full;
    let turns = 1;
    let from: Pos = { x: u.x, y: u.y };
    for (const step of path) {
      const c = this.stepCost(u, from.x, from.y, step.x, step.y);
      if (mp <= 0) {
        turns++;
        mp = full;
      }
      mp -= c;
      from = step;
    }
    return turns;
  }
}

function hexDist(ax: number, ay: number, bx: number, by: number): number {
  const aq = ax - (ay - (ay & 1)) / 2;
  const bq = bx - (by - (by & 1)) / 2;
  const dq = aq - bq;
  const dr = ay - by;
  return Math.max(Math.abs(dq), Math.abs(dr), Math.abs(dq + dr));
}
