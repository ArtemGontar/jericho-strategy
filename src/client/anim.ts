import type { Pos } from '../shared/hex';
import type { GameEvent } from '../shared/game';
import type { UnitTypeId } from '../shared/types';

export interface Floater {
  x: number; // tile coordinates (fractional allowed)
  y: number;
  text: string;
  color: string;
  t0: number;
  dur: number;
}
export interface Projectile {
  from: Pos;
  to: Pos;
  t0: number;
  dur: number;
  kind: 'arrow' | 'rock' | 'bolt';
}
export interface Ring {
  x: number;
  y: number;
  t0: number;
  dur: number;
  color: string;
  max: number;
}
export interface Ghost {
  type: UnitTypeId;
  owner: number;
  x: number;
  y: number;
  /** visible from now until fadeStart, then fades over 400 ms */
  fadeStart: number;
  until: number;
}
interface MoveAnim {
  from: Pos;
  path: Pos[];
  t0: number;
  stepMs: number;
}
interface Lunge {
  unit: number;
  from: Pos;
  to: Pos;
  t0: number;
  dur: number;
}

/** Time-based presentation layer; the game state is already final when these play. */
export class Animator {
  moves = new Map<number, MoveAnim>();
  lunges: Lunge[] = [];
  floaters: Floater[] = [];
  projectiles: Projectile[] = [];
  rings: Ring[] = [];
  ghosts: Ghost[] = [];
  /** tiles being flashed (key -> end time) */
  flashes = new Map<number, number>();
  private cursor = 0;
  sounds: Array<{ at: number; name: string; done: boolean }> = [];

  get busy(): boolean {
    return this.cursor > performance.now();
  }

  clear(): void {
    this.moves.clear();
    this.lunges = [];
    this.floaters = [];
    this.projectiles = [];
    this.rings = [];
    this.ghosts = [];
    this.flashes.clear();
    this.cursor = 0;
    this.sounds = [];
  }

  /** Queue the visible consequences of a batch of events. `me` marks own actions so they play immediately. */
  schedule(events: GameEvent[], me: number): void {
    const now = performance.now();
    if (this.cursor < now) this.cursor = now;
    // compress long AI sequences so a whole round never takes more than ~4 seconds
    const budget = 4000;
    let count = 0;
    for (const e of events) if (e.kind === 'move' || e.kind === 'attack') count++;
    const speed = count > 12 ? Math.max(0.25, Math.min(1, budget / (count * 420))) : 1;
    for (const e of events) {
      if (e.kind === 'move') {
        const steps = e.path.length;
        const stepMs = 130 * speed;
        const own = e.owner === me;
        const t0 = own ? now : this.cursor;
        this.moves.set(e.unit, { from: e.from, path: e.path, t0, stepMs });
        if (!own) this.cursor = t0 + steps * stepMs * 0.7;
        else this.cursor = Math.max(this.cursor, t0 + steps * stepMs);
      } else if (e.kind === 'attack') {
        const t0 = this.cursor;
        const dur = (e.ranged ? 380 : 320) * Math.max(0.5, speed);
        if (e.attacker > 0) this.lunges.push({ unit: e.attacker, from: e.from, to: e.to, t0, dur });
        if (e.ranged) this.projectiles.push({ from: e.from, to: e.to, t0, dur: dur * 0.9, kind: e.type === 'catapult' ? 'rock' : e.type === 'cannon' ? 'bolt' : 'arrow' });
        const hitAt = t0 + (e.ranged ? dur * 0.85 : dur * 0.5);
        this.floaters.push({ x: e.to.x, y: e.to.y, text: `-${e.dmgDef}`, color: '#ff6b5e', t0: hitAt, dur: 1100 });
        if (e.dmgAtt > 0) this.floaters.push({ x: e.from.x, y: e.from.y, text: `-${e.dmgAtt}`, color: '#ffb067', t0: hitAt + 60, dur: 1100 });
        this.flashes.set(e.to.y * 1000 + e.to.x, hitAt + 260);
        this.sounds.push({ at: hitAt, name: e.ranged ? (e.type === 'cannon' ? 'cannon' : e.type === 'catapult' ? 'catapult' : 'shoot') : e.type === 'swordsman' || e.type === 'knight' ? 'clash' : 'hit', done: false });
        this.cursor = t0 + dur + 120;
      } else if (e.kind === 'death') {
        const at = this.cursor;
        this.ghosts.push({ type: e.type, owner: e.owner, x: e.at.x, y: e.at.y, fadeStart: at, until: at + 500 });
        this.sounds.push({ at, name: 'death', done: false });
      } else if (e.kind === 'found') {
        this.rings.push({ x: e.at.x, y: e.at.y, t0: this.cursor, dur: 900, color: '#ffd24a', max: 1.6 });
        this.sounds.push({ at: this.cursor, name: 'found', done: false });
      } else if (e.kind === 'capture') {
        this.rings.push({ x: e.at.x, y: e.at.y, t0: this.cursor, dur: 1200, color: '#ff5d4a', max: 2.2 });
        this.sounds.push({ at: this.cursor, name: 'capture', done: false });
        this.cursor += 500;
      }
    }
  }

  /** Fractional tile position of an animated unit, or null when it should be drawn where the state says. */
  unitPos(id: number, now: number): { x: number; y: number } | null {
    const m = this.moves.get(id);
    const l = this.lunges.find((q) => q.unit === id && now >= q.t0 && now < q.t0 + q.dur);
    if (l) {
      const k = (now - l.t0) / l.dur;
      const f = k < 0.5 ? k * 2 : (1 - k) * 2;
      const dx = l.to.x - l.from.x;
      const dy = l.to.y - l.from.y;
      const len = Math.hypot(dx, dy) || 1;
      const reach = 0.45 * f;
      return { x: l.from.x + (dx / len) * reach, y: l.from.y + (dy / len) * reach };
    }
    if (!m) return null;
    if (now < m.t0) return { x: m.from.x, y: m.from.y };
    const total = m.path.length * m.stepMs;
    if (now >= m.t0 + total) {
      this.moves.delete(id);
      return null;
    }
    const k = (now - m.t0) / m.stepMs;
    const i = Math.floor(k);
    const f = k - i;
    const a = i === 0 ? m.from : m.path[i - 1];
    const b = m.path[i];
    return { x: a.x + (b.x - a.x) * f, y: a.y + (b.y - a.y) * f };
  }

  /** hides a unit's new tile until its attack lunge is over; used for attackers positioned at `from` */
  update(now: number, play: (name: string) => void): boolean {
    this.floaters = this.floaters.filter((f) => now < f.t0 + f.dur);
    this.projectiles = this.projectiles.filter((p) => now < p.t0 + p.dur);
    this.rings = this.rings.filter((r) => now < r.t0 + r.dur);
    this.lunges = this.lunges.filter((l) => now < l.t0 + l.dur);
    this.ghosts = this.ghosts.filter((g) => now < g.until + 400);
    for (const [k, v] of this.flashes) if (now > v) this.flashes.delete(k);
    for (const s of this.sounds) {
      if (!s.done && now >= s.at) {
        s.done = true;
        play(s.name);
      }
    }
    this.sounds = this.sounds.filter((s) => !s.done);
    return (
      this.moves.size > 0 || this.lunges.length > 0 || this.floaters.length > 0 || this.projectiles.length > 0 ||
      this.rings.length > 0 || this.ghosts.length > 0 || this.flashes.size > 0 || this.sounds.length > 0 || this.cursor > now
    );
  }
}
