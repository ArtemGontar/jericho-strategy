import {
  CircleGeometry, Color, DoubleSide, Group, Mesh, MeshBasicMaterial, MeshStandardMaterial, TorusGeometry, type Scene,
} from 'three';
import { UNITS } from '../../shared/data';
import type { ViewUnit } from '../../shared/view';
import type { Animator } from '../anim';
import type { RenderState } from '../render';
import { civColor } from '../render';
import type { Particles } from './fx';
import { animateRig, buildRig, type Pose, type Rig } from './models';
import { tileXZ, tileXZf, type Terrain } from './terrain';
import { EDGE_NEIGHBOR_EVEN, EDGE_NEIGHBOR_ODD } from './hexmath';
import { TERRAIN_LIST } from '../../shared/view';

const SCALE: Record<string, number> = { biped: 1.6, mounted: 1.45, cart: 1.25, siege: 1.4, cannon: 1.5, ship: 1.35 };
const RANGED = new Set(['archer', 'crossbowman', 'musketman', 'catapult', 'cannon', 'frigate']);

interface Slot {
  id: number;
  type: string;
  owner: number;
  holder: Group;
  rig: Rig;
  ring: Mesh;
  base: Mesh;
  selRing: Mesh;
  heading: number;
  x: number;
  z: number;
  phase: number;
  walk: number;
  fort: number;
  sleep: number;
  hp: number;
  hurtUntil: number;
  seed: number;
  lastDust: number;
  fired: boolean;
  tick: number;
  color: string;
  ghostAt: number;
  ox: number;
  oz: number;
}

const MIL_SLOTS: Array<[number, number]> = [[0, 0.1], [0.5, 0.2], [-0.5, 0.2], [0, -0.4], [0.45, -0.35], [-0.45, -0.35]];
const CIV_SLOTS: Array<[number, number]> = [[-0.45, -0.22], [0.45, -0.25], [0, -0.5]];
const CITY_SLOTS: Array<[number, number]> = [[0.62, 0.5], [-0.62, 0.5], [0.0, 0.78], [0.88, 0.0], [-0.88, 0.0]];

/** Owns the 3D figure for every unit: placement, facing, gait and combat poses. */
export class UnitLayer {
  private slots = new Map<number, Slot>();
  private ghosts = new Map<string, Slot>();
  private ringGeo = new TorusGeometry(0.33, 0.022, 6, 40).rotateX(Math.PI / 2);
  private discGeo = new CircleGeometry(0.31, 28).rotateX(-Math.PI / 2);
  private selGeo = new TorusGeometry(0.43, 0.026, 6, 48).rotateX(Math.PI / 2);
  private lastTime = 0;
  private teamCache = new Map<string, string>();
  /** screen-space anchors for the 2D overlay, rebuilt every frame */
  anchors = new Map<number, { x: number; y: number; z: number; hgt: number }>();
  /** True when any figure is travelling or fighting this frame (shadows must keep up with them). */
  motion = false;

  constructor(private scene: Scene, private terrain: Terrain, private anim: Animator, private fx: Particles) {}

  private makeSlot(u: { id: number; type: string; owner: number; hp: number }, color: string): Slot {
    const rig = buildRig(u.type as ViewUnit['type'], color, this.terrain.uniforms);
    const holder = new Group();
    rig.root.scale.setScalar(SCALE[rig.kind]);
    holder.add(rig.root);
    const ringMat = new MeshBasicMaterial({ color: new Color(color), transparent: true, opacity: 0.95, depthWrite: false });
    const ring = new Mesh(this.ringGeo, ringMat);
    ring.position.y = 0.012;
    const baseMat = new MeshBasicMaterial({ color: new Color(color), transparent: true, opacity: 0.28, depthWrite: false, side: DoubleSide });
    const base = new Mesh(this.discGeo, baseMat);
    base.position.y = 0.008;
    const selRing = new Mesh(this.selGeo, new MeshBasicMaterial({ color: 0xffdf80, transparent: true, opacity: 0.95, depthWrite: false }));
    selRing.position.y = 0.016;
    selRing.visible = false;
    if (UNITS[u.type as ViewUnit['type']].naval) {
      for (const m of [ring, base, selRing]) m.scale.setScalar(1.6);
    }
    holder.add(ring, base, selRing);
    this.scene.add(holder);
    return {
      id: u.id, type: u.type, owner: u.owner, holder, rig, ring, base, selRing, heading: -1.05, x: 0, z: 0, phase: Math.random() * 6, walk: 0, fort: 0, sleep: 0,
      hp: u.hp, hurtUntil: 0, seed: Math.random() * 100, lastDust: 0, fired: false, tick: 0, color, ghostAt: 0, ox: 0, oz: 0,
    };
  }

  private dispose(s: Slot): void {
    this.scene.remove(s.holder);
    for (const m of s.rig.mats) m.dispose();
    (s.ring.material as MeshBasicMaterial).dispose();
    (s.base.material as MeshBasicMaterial).dispose();
    (s.selRing.material as MeshBasicMaterial).dispose();
  }

  update(st: RenderState, now: number, cityAt: Set<number>): void {
    const v = st.view;
    const W = v.settings.width;
    const dt = this.lastTime ? Math.min(0.1, (now - this.lastTime) / 1000) : 0.016;
    this.lastTime = now;
    const t = now / 1000;
    this.anchors.clear();
    this.motion = false;

    // stack offsets: how many units share each tile
    const stacks = new Map<number, { mil: number; civ: number }>();
    const alive = new Set<number>();
    const order = v.units.slice().sort((a, b) => a.id - b.id);
    for (const u of order) {
      alive.add(u.id);
      const color = civColor(v, u.owner);
      let s = this.slots.get(u.id);
      if (s && (s.type !== u.type || s.color !== color)) {
        this.dispose(s);
        this.slots.delete(u.id);
        s = undefined;
      }
      if (!s) {
        s = this.makeSlot(u, color);
        const [px, pz] = tileXZ(u.x, u.y);
        s.x = px;
        s.z = pz;
        this.slots.set(u.id, s);
      }
      if (u.carriedBy != null) {
        // passengers ride below deck: the ship's flag shows how many are aboard
        s.holder.visible = false;
        this.anchors.delete(u.id);
        continue;
      }
      const def = UNITS[u.type];
      const civ = !!def.civilian;
      const ranged = RANGED.has(u.type);
      const key = u.y * W + u.x;
      const stack = stacks.get(key) ?? { mil: 0, civ: 0 };
      stacks.set(key, stack);
      const onCity = cityAt.has(key);
      let off: [number, number];
      if (onCity && def.naval) {
        // a ship in port lies in the water beside the city
        const nbs = u.y & 1 ? EDGE_NEIGHBOR_ODD : EDGE_NEIGHBOR_EVEN;
        off = [0.0, 0.7];
        for (const [dx, dy] of nbs) {
          const nx = u.x + dx;
          const ny = u.y + dy;
          if (nx < 0 || ny < 0 || nx >= W || ny >= v.settings.height) continue;
          const t = TERRAIN_LIST[v.terrain[ny * W + nx]];
          if (t === 'coast' || t === 'ocean') {
            const [cx, cz] = tileXZ(u.x, u.y);
            const [wx, wz] = tileXZ(nx, ny);
            off = [(wx - cx) * 0.62, (wz - cz) * 0.62];
            break;
          }
        }
      } else if (onCity) off = CITY_SLOTS[(stack.mil + stack.civ) % CITY_SLOTS.length];
      else if (civ) off = CIV_SLOTS[stack.civ % CIV_SLOTS.length];
      else off = MIL_SLOTS[stack.mil % MIL_SLOTS.length];
      if (civ) stack.civ++;
      else stack.mil++;

      // animated position in tile space: moves, and melee lunges (ranged units stay put while shooting)
      const lunge = this.anim.lunges.find((l) => l.unit === u.id && now >= l.t0 && now < l.t0 + l.dur);
      const ap = this.anim.unitPos(u.id, now);
      let tx = u.x;
      let ty = u.y;
      if (ap && !(lunge && ranged)) {
        tx = ap.x;
        ty = ap.y;
      }
      let [X, Z] = tileXZf(tx, ty);
      const moving = !!ap && !lunge;
      if (ap || lunge) this.motion = true;
      // ease toward the unit's slot on the tile; while travelling it walks the tile centres
      const wantX = moving ? 0 : off[0];
      const wantZ = moving ? 0 : off[1];
      const ek = Math.min(1, dt * 9);
      s.ox += (wantX - s.ox) * ek;
      s.oz += (wantZ - s.oz) * ek;
      X += s.ox;
      Z += s.oz;

      const dx = X - s.x;
      const dz = Z - s.z;
      const dist = Math.hypot(dx, dz);
      const speed = dist / Math.max(dt, 0.001);
      s.x = X;
      s.z = Z;
      if (moving && dist > 0.0005) {
        const want = Math.atan2(-dz, dx);
        s.heading = turn(s.heading, want, Math.min(1, dt * 11));
      }
      if (lunge) {
        const tx2 = tileXZ(lunge.to.x, lunge.to.y);
        const fx2 = tileXZ(lunge.from.x, lunge.from.y);
        s.heading = turn(s.heading, Math.atan2(-(tx2[1] - fx2[1]), tx2[0] - fx2[0]), Math.min(1, dt * 14));
      }
      const wantWalk = Math.min(1, speed / 0.9);
      s.walk += (wantWalk - s.walk) * Math.min(1, dt * 12);
      s.phase += dt * (2.5 + speed * 3.8) * (s.walk > 0.05 ? 1 : 0.2);
      const fortWant = u.fortified || u.order?.kind === 'fortify' ? 1 : 0;
      s.fort += (fortWant - s.fort) * Math.min(1, dt * 6);
      const sleepWant = u.order?.kind === 'sleep' ? 1 : 0;
      s.sleep += (sleepWant - s.sleep) * Math.min(1, dt * 5);

      let attack = -1;
      if (lunge) attack = (now - lunge.t0) / lunge.dur;
      // ranged shot release: smoke and flash once at the right moment
      if (attack >= 0.55 && !s.fired) {
        s.fired = true;
        this.onRelease(s, u.type);
      }
      if (attack < 0) s.fired = false;

      if (u.hp < s.hp) s.hurtUntil = now + 380;
      s.hp = u.hp;

      const pose: Pose = {
        t, seed: s.seed, phase: s.phase, walk: s.walk, attack, fort: s.fort, sleep: s.sleep,
        work: u.order?.kind === 'build' && s.walk < 0.1,
      };
      animateRig(s.rig, pose);
      const flash = now < s.hurtUntil ? (s.hurtUntil - now) / 380 : 0;
      this.tint(s, flash, st.selectedUnit?.id === u.id);

      const gy0 = Math.max(0, this.terrain.heightAt(X, Z));
      s.holder.position.set(X, gy0, Z);
      s.holder.rotation.y = s.heading;
      // keep the ring flat on the ground regardless of facing
      s.ring.rotation.y = -s.heading;
      s.base.rotation.y = -s.heading;
      s.selRing.rotation.y = -s.heading;
      const sel = st.selectedUnit?.id === u.id;
      s.selRing.visible = sel;
      if (sel) {
        const k = 1 + Math.sin(t * 4) * 0.05;
        s.selRing.scale.set(k, 1, k);
      }
      const mine = u.owner === v.me;
      (s.ring.material as MeshBasicMaterial).opacity = mine ? 0.95 : 0.8;
      // footsteps
      if (s.walk > 0.5 && now - s.lastDust > (def.naval ? 70 : 150)) {
        s.lastDust = now;
        if (def.naval) {
          // foam in the wake, a little behind the stern
          const bx = X - Math.cos(s.heading) * 0.5;
          const bz = Z + Math.sin(s.heading) * 0.5;
          this.fx.dust(bx, 0.03, bz, [0.92, 0.98, 1.0]);
        } else this.fx.dust(X, gy0 + 0.02, Z);
      }
      if (u.workProgress && pose.work && now - s.lastDust > 420) {
        s.lastDust = now;
        this.fx.dust(X + 0.1, gy0 + 0.03, Z + 0.05, [0.7, 0.6, 0.45]);
      }
      this.anchors.set(u.id, { x: X, y: gy0, z: Z, hgt: s.rig.height * SCALE[s.rig.kind] + 0.06 });
      s.holder.visible = true;
    }
    for (const [id, s] of this.slots) {
      if (!alive.has(id)) {
        this.dispose(s);
        this.slots.delete(id);
      }
    }

    // units that just died tip over and sink
    const keep = new Set<string>();
    for (const g of this.anim.ghosts) {
      const key = `${g.type}:${g.owner}:${g.x}:${g.y}:${g.fadeStart}`;
      keep.add(key);
      let s = this.ghosts.get(key);
      if (!s) {
        s = this.makeSlot({ id: -1, type: g.type, owner: g.owner, hp: 0 }, civColor(v, g.owner));
        const [gx, gz] = tileXZ(g.x, g.y);
        s.x = gx;
        s.z = gz;
        s.ghostAt = now;
        s.ring.visible = false;
        s.base.visible = false;
        this.ghosts.set(key, s);
        this.fx.sparks(gx, 0.2, gz, 8, [0.9, 0.5, 0.3]);
        this.fx.dust(gx, 0.05, gz);
      }
      const k = Math.max(0, Math.min(1, (now - Math.max(s.ghostAt, g.fadeStart)) / 600));
      s.holder.position.set(s.x, Math.max(0, this.terrain.heightAt(s.x, s.z)) - k * 0.2, s.z);
      s.holder.rotation.z = k * 1.35;
      s.holder.scale.setScalar(Math.max(0.01, 1 - Math.max(0, k - 0.6) * 2.4));
      animateRig(s.rig, { t, seed: s.seed, phase: 0, walk: 0, attack: -1, fort: 0, sleep: 0, work: false });
    }
    for (const [key, s] of this.ghosts) {
      if (!keep.has(key)) {
        this.dispose(s);
        this.ghosts.delete(key);
      }
    }
  }

  private onRelease(s: Slot, type: string): void {
    const gy = this.terrain.heightAt(s.x, s.z);
    const dirx = Math.cos(s.heading);
    const dirz = -Math.sin(s.heading);
    if (type === 'cannon' || type === 'frigate') {
      const mx = s.x + dirx * 0.35;
      const mz = s.z + dirz * 0.35;
      this.fx.flash(mx, gy + 0.3, mz, 0.7);
      this.fx.sparks(mx, gy + 0.3, mz, 14);
      for (let i = 0; i < 10; i++) this.fx.smoke(mx + dirx * i * 0.03, gy + 0.3, mz + dirz * i * 0.03, 1.6);
    } else if (type === 'musketman') {
      const mx = s.x + dirx * 0.3;
      const mz = s.z + dirz * 0.3;
      this.fx.flash(mx, gy + 0.3, mz, 0.35);
      for (let i = 0; i < 4; i++) this.fx.smoke(mx, gy + 0.3, mz, 0.8);
    } else if (type === 'catapult') {
      this.fx.dust(s.x + dirx * 0.2, gy + 0.1, s.z + dirz * 0.2);
    } else if (type !== 'archer' && type !== 'crossbowman') {
      this.fx.sparks(s.x + dirx * 0.42, gy + 0.28, s.z + dirz * 0.42, 9, [0.95, 0.9, 0.8]);
    }
  }

  private tint(s: Slot, flash: number, selected: boolean): void {
    const [um] = s.rig.mats;
    const e = flash > 0 ? new Color(0.9 * flash, 0.12 * flash, 0.08 * flash) : selected ? new Color(0.05, 0.045, 0.02) : new Color(0, 0, 0);
    for (const m of s.rig.mats as MeshStandardMaterial[]) m.emissive.copy(e);
    void um;
  }

  /** Scene-space head position of a unit for floating labels; null when it is not drawn. */
  anchorOf(id: number): { x: number; y: number; z: number; hgt: number } | null {
    return this.anchors.get(id) ?? null;
  }

  /** The tile under a screen ray is resolved by the camera; this lets callers know which unit stands where. */
  dispose_all(): void {
    for (const s of this.slots.values()) this.dispose(s);
    for (const s of this.ghosts.values()) this.dispose(s);
    this.slots.clear();
    this.ghosts.clear();
  }
}

function turn(from: number, to: number, k: number): number {
  let d = to - from;
  while (d > Math.PI) d -= Math.PI * 2;
  while (d < -Math.PI) d += Math.PI * 2;
  return from + d * k;
}
