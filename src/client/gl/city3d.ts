import { Color, Group, Mesh, MeshStandardMaterial, Object3D, type Scene } from 'three';
import type { GameView, ViewCity } from '../../shared/view';
import { civColor } from '../render';
import type { Particles } from './fx';
import { Builder } from './geo';
import { patchMaterial } from './fog';
import { rngFor } from './noise';
import { addInk } from './models';
import { tileXZ, type Terrain } from './terrain';

/** thickness of the ink outline around buildings, in building space */
const INK = 0.007;

const WALLS = ['#f1debb', '#e4cfa6', '#eed9b2', '#dcc194', '#e8d2a8'];
const ROOFS = ['#c0502f', '#b0452c', '#cc6238', '#9c3f2c', '#c8723a'];
const STONE = '#b8af9a';
/** whole-town scale: layouts below are drawn in a compact unit space and enlarged to fill the tile */
const S = 1.4;

interface Slot {
  group: Group;
  sig: string;
  cloths: Object3D[];
  chimneys: Array<[number, number, number]>;
  seed: number;
}

function house(b: Builder, t: Builder, x: number, z: number, rng: () => number, tier: number): [number, number, number] {
  const w = 0.13 + rng() * 0.07;
  const d = 0.12 + rng() * 0.05;
  const h = 0.09 + rng() * 0.05 + tier * 0.012;
  const ry = Math.round(rng() * 4) * (Math.PI / 2) + (rng() - 0.5) * 0.25;
  const wall = WALLS[Math.floor(rng() * WALLS.length)];
  const roof = ROOFS[Math.floor(rng() * ROOFS.length)];
  b.box(w * 1.05, 0.022, d * 1.05, '#b3a68a', { x, y: 0.011, z, ry });
  b.box(w, h, d, wall, { x, y: h / 2, z, ry });
  const two = rng() < 0.3 + tier * 0.12;
  let top = h;
  if (two) {
    b.box(w * 0.9, h * 0.7, d * 0.9, wall, { x, y: h + h * 0.35, z, ry });
    top = h * 1.7;
  }
  b.gable(w * 1.18, 0.07 + rng() * 0.03, d * 1.12, roof, { x, y: top, z, ry });
  // door and windows
  const cs = Math.cos(ry);
  const sn = Math.sin(ry);
  b.box(0.03, 0.05, 0.006, '#3a2a1c', { x: x + sn * (d / 2 + 0.002), y: 0.025, z: z + cs * (d / 2 + 0.002), ry });
  b.box(0.03, 0.03, 0.006, '#3f6f7a', { x: x + cs * -w * 0.3 + sn * (d / 2 + 0.002), y: h * 0.62, z: z - sn * -w * 0.3 + cs * (d / 2 + 0.002), ry });
  b.box(0.026, 0.026, 0.006, '#f0cf78', { x: x + cs * w * 0.3 + sn * (d / 2 + 0.002), y: h * 0.62, z: z - sn * w * 0.3 + cs * (d / 2 + 0.002), ry });
  if (rng() < 0.4) t.box(w * 0.12, 0.012, d * 1.14, 'white', { x, y: top + 0.058, z, ry });
  // chimney
  const chx = x + cs * w * 0.25;
  const chz = z - sn * w * 0.25;
  b.box(0.026, 0.07, 0.026, '#8a7a68', { x: chx, y: top + 0.05, z: chz });
  return [chx, top + 0.1, chz];
}

function tower(b: Builder, t: Builder, x: number, z: number, r: number, h: number, roof = true): void {
  b.cyl(r, r * 1.08, h, STONE, { x, z }, 10);
  b.cyl(r * 1.18, r * 1.18, 0.025, '#9b927e', { x, y: h, z }, 10);
  for (let i = 0; i < 8; i++) {
    const a = (i / 8) * Math.PI * 2;
    b.box(r * 0.4, 0.035, r * 0.4, STONE, { x: x + Math.cos(a) * r * 1.0, y: h + 0.03, z: z + Math.sin(a) * r * 1.0, ry: -a });
  }
  if (roof) t.cone(r * 1.2, r * 2.2, 'white', { x, y: h + 0.02, z }, 10);
}

function wallRing(b: Builder, t: Builder, radius: number, gate: boolean): void {
  const pts: Array<[number, number]> = [];
  for (let i = 0; i < 6; i++) {
    const a = ((60 * i - 30) * Math.PI) / 180;
    pts.push([Math.cos(a) * radius, Math.sin(a) * radius]);
  }
  for (let i = 0; i < 6; i++) {
    const [ax, az] = pts[i];
    const [bx, bz] = pts[(i + 1) % 6];
    const len = Math.hypot(bx - ax, bz - az);
    const mx = (ax + bx) / 2;
    const mz = (az + bz) / 2;
    const ry = -Math.atan2(bz - az, bx - ax);
    const isGate = gate && i === 1;
    if (isGate) {
      for (const s of [-1, 1]) {
        b.box(len * 0.32, 0.15, 0.05, STONE, { x: mx + Math.cos(-ry) * s * len * 0.34, y: 0.075, z: mz + Math.sin(-ry) * s * len * 0.34, ry });
      }
      b.box(len * 0.4, 0.04, 0.055, STONE, { x: mx, y: 0.16, z: mz, ry });
      t.box(len * 0.2, 0.02, 0.058, 'white', { x: mx, y: 0.185, z: mz, ry });
      continue;
    }
    b.box(len, 0.15, 0.05, STONE, { x: mx, y: 0.075, z: mz, ry });
    b.box(len, 0.02, 0.07, '#a39a86', { x: mx, y: 0.16, z: mz, ry });
    const n = Math.floor(len / 0.09);
    for (let k = 0; k < n; k++) {
      const f = (k + 0.5) / n - 0.5;
      b.box(0.05, 0.04, 0.07, STONE, { x: mx + Math.cos(-ry) * f * len, y: 0.19, z: mz + Math.sin(-ry) * f * len, ry });
    }
  }
  for (const [x, z] of pts) tower(b, t, x, z, 0.062, 0.24);
}

function keep(b: Builder, t: Builder, capital: boolean, tier: number): { flag: [number, number, number] } {
  const h = 0.26 + tier * 0.07;
  b.box(0.34, 0.14, 0.3, '#d9cfb8', { y: 0.07 });
  b.box(0.2 + tier * 0.02, h, 0.2 + tier * 0.02, '#e2d8c0', { y: 0.14 + h / 2 });
  b.box(0.22 + tier * 0.02, 0.025, 0.22 + tier * 0.02, '#a49b86', { y: 0.14 + h });
  t.cone(0.17 + tier * 0.015, 0.2, 'white', { y: 0.14 + h + 0.025 }, 4);
  b.box(0.05, 0.075, 0.006, '#3a2a1c', { y: 0.04, z: 0.152 });
  b.box(0.034, 0.05, 0.006, '#e8c872', { x: 0.05, y: 0.14 + h * 0.5, z: 0.1 + tier * 0.01 });
  b.box(0.034, 0.05, 0.006, '#e8c872', { x: -0.05, y: 0.14 + h * 0.5, z: 0.1 + tier * 0.01 });
  // corner turrets
  for (const sx of [-1, 1]) {
    for (const sz of [-1, 1]) {
      b.cyl(0.04, 0.045, 0.2, '#cfc5ac', { x: sx * 0.17, y: 0.0, z: sz * 0.15 }, 8);
      t.cone(0.058, 0.1, 'white', { x: sx * 0.17, y: 0.2, z: sz * 0.15 }, 8);
    }
  }
  const top = 0.14 + h + 0.025 + 0.2;
  b.cyl(0.006, 0.006, 0.17, '#3a2c20', { y: top }, 4);
  if (capital) b.sphere(0.018, '#f2d98c', { y: top + 0.18 }, 6);
  return { flag: [0, top + (capital ? 0.12 : 0.1), 0] };
}

function wonder(b: Builder, t: Builder, id: string): void {
  const x = 0.4;
  const z = -0.33;
  switch (id) {
    case 'pyramids':
      b.pyramid(0.46, 0.34, 0.46, '#dcc080', { x, z, ry: 0.2 });
      b.pyramid(0.26, 0.2, 0.26, '#cfb372', { x: x - 0.3, z: z + 0.2, ry: 0.5 });
      break;
    case 'great_library':
      b.box(0.34, 0.04, 0.24, '#e6dfcf', { x, z, y: 0.02 });
      for (let i = 0; i < 6; i++) b.cyl(0.02, 0.022, 0.18, '#f0ead9', { x: x - 0.14 + i * 0.056, y: 0.04, z: z + 0.1 }, 8);
      b.box(0.34, 0.025, 0.24, '#d8d1bd', { x, y: 0.22, z });
      b.gable(0.36, 0.1, 0.27, '#b5543a', { x, y: 0.245, z });
      break;
    case 'hanging_gardens':
      for (let i = 0; i < 3; i++) {
        b.box(0.4 - i * 0.1, 0.07, 0.34 - i * 0.09, '#d9cfb8', { x, y: i * 0.075 + 0.035, z });
        b.box(0.37 - i * 0.1, 0.012, 0.31 - i * 0.09, '#5fa043', { x, y: i * 0.075 + 0.075, z });
        for (let k = 0; k < 4; k++) b.sphere(0.04, '#4d8f3a', { x: x - 0.12 + k * 0.08 - i * 0.02, y: i * 0.075 + 0.1, z: z + 0.1 - i * 0.03 }, 6);
      }
      break;
    case 'oracle':
      b.cyl(0.19, 0.2, 0.04, '#e6dfcf', { x, z }, 14);
      for (let i = 0; i < 8; i++) {
        const a = (i / 8) * Math.PI * 2;
        b.cyl(0.018, 0.02, 0.17, '#f0ead9', { x: x + Math.cos(a) * 0.15, y: 0.04, z: z + Math.sin(a) * 0.15 }, 8);
      }
      b.cyl(0.18, 0.18, 0.02, '#d8d1bd', { x, y: 0.21, z }, 14);
      b.sphere(0.12, '#c8a24a', { x, y: 0.23, z, sy: 0.7 }, 12);
      break;
    case 'colossus':
      b.box(0.24, 0.1, 0.2, '#cfc5ac', { x, y: 0.05, z });
      b.cyl(0.035, 0.05, 0.26, '#c8a45a', { x: x - 0.04, y: 0.1, z }, 7);
      b.cyl(0.035, 0.05, 0.26, '#c8a45a', { x: x + 0.04, y: 0.1, z }, 7);
      b.cyl(0.07, 0.06, 0.2, '#d4b062', { x, y: 0.34, z }, 8);
      b.sphere(0.055, '#d9b868', { x, y: 0.58, z }, 8);
      b.cyl(0.018, 0.018, 0.18, '#d4b062', { x: x + 0.1, y: 0.42, z, rz: -0.4 }, 5);
      b.sphere(0.03, '#ffcf6a', { x: x + 0.15, y: 0.6, z }, 6);
      break;
    default:
  }
  void t;
}

/** One small town per city: houses that multiply with population, a keep, optional walls and wonders. */
export class CityLayer {
  private slots = new Map<number, Slot>();
  private unitMat: MeshStandardMaterial;
  private teamMats = new Map<string, MeshStandardMaterial>();

  constructor(private scene: Scene, private terrain: Terrain, private fx: Particles) {
    this.unitMat = new MeshStandardMaterial({ vertexColors: true, roughness: 0.85 });
    patchMaterial(this.unitMat, terrain.uniforms, { paint: 15 });
  }

  private team(color: string): MeshStandardMaterial {
    let m = this.teamMats.get(color);
    if (!m) {
      m = new MeshStandardMaterial({ color: new Color(color), roughness: 0.7 });
      patchMaterial(m, this.terrain.uniforms, { paint: 15 });
      this.teamMats.set(color, m);
    }
    return m;
  }

  private build(c: ViewCity, color: string): Slot {
    const rng = rngFor(c.x, c.y, 91);
    const b = new Builder();
    const t = new Builder();
    const tier = Math.min(3, Math.floor(c.pop / 4));
    const walls = !!c.buildings?.includes('walls');
    const chimneys: Array<[number, number, number]> = [];
    // dusty courtyard is painted on the ground; here come the buildings
    const n = Math.min(18, 3 + c.pop);
    const radiusMax = walls ? 0.42 : 0.52;
    const spots: Array<[number, number]> = [];
    for (let i = 0; i < n * 8 && spots.length < n; i++) {
      const a = rng() * Math.PI * 2;
      const r = 0.27 + Math.sqrt(rng()) * (radiusMax - 0.27);
      const x = Math.cos(a) * r;
      const z = Math.sin(a) * r * 0.95;
      
      if (spots.some(([sx, sz]) => Math.hypot(sx - x, sz - z) < 0.19)) continue;
      spots.push([x, z]);
    }
    spots.sort((p, q) => p[1] - q[1]);
    for (const [x, z] of spots) chimneys.push(house(b, t, x, z, rng, tier));
    const k = keep(b, t, c.capital, tier);
    if (tier >= 2) {
      tower(b, t, -0.3, -0.24, 0.05, 0.3);
    }
    if (c.buildings?.includes('market') || tier >= 1) {
      // market stalls
      for (let i = 0; i < 2; i++) {
        b.box(0.1, 0.05, 0.07, '#a67c4a', { x: -0.26 + i * 0.13, y: 0.025, z: 0.3 });
        t.box(0.12, 0.012, 0.09, 'white', { x: -0.26 + i * 0.13, y: 0.09, z: 0.3, rx: 0.15 });
        b.cyl(0.004, 0.004, 0.07, '#5a4630', { x: -0.31 + i * 0.13, y: 0.025, z: 0.33 }, 4);
        b.cyl(0.004, 0.004, 0.07, '#5a4630', { x: -0.21 + i * 0.13, y: 0.025, z: 0.33 }, 4);
      }
    }
    if (c.buildings?.includes('temple')) {
      b.box(0.17, 0.03, 0.13, '#e6dfcf', { x: -0.34, y: 0.015, z: -0.11 });
      for (let i = 0; i < 4; i++) b.cyl(0.012, 0.014, 0.1, '#f0ead9', { x: -0.41 + i * 0.047, y: 0.03, z: -0.05 }, 6);
      b.gable(0.19, 0.06, 0.15, '#d9cfb8', { x: -0.34, y: 0.13, z: -0.11 });
    }
    if (c.buildings?.includes('granary')) {
      b.cyl(0.05, 0.055, 0.12, '#c9a96a', { x: 0.3, z: 0.18 }, 9);
      b.cone(0.065, 0.07, '#b5543a', { x: 0.3, y: 0.12, z: 0.18 }, 9);
    }
    if (c.buildings?.includes('barracks')) {
      b.box(0.17, 0.08, 0.1, '#8a7a68', { x: 0.32, y: 0.04, z: -0.26 });
      b.gable(0.19, 0.05, 0.12, '#6e3326', { x: 0.32, y: 0.08, z: -0.26 });
      t.box(0.004, 0.1, 0.1, 'white', { x: 0.42, y: 0.14, z: -0.26 });
    }
    if (c.buildings?.includes('library') || c.buildings?.includes('university')) {
      b.box(0.15, 0.1, 0.12, '#e0d7c0', { x: -0.08, y: 0.05, z: -0.34 });
      b.gable(0.17, 0.05, 0.14, '#4a6a8a', { x: -0.08, y: 0.1, z: -0.34 });
    }
    if (walls) wallRing(b, t, 0.6, true);
    for (const w of c.wonders ?? []) wonder(b, t, w);
    const group = new Group();
    const main = new Mesh(b.build(), this.unitMat);
    main.castShadow = true;
    main.receiveShadow = true;
    group.add(main);
    addInk(main, this.terrain.uniforms, INK);
    if (t.count) {
      const tm = new Mesh(t.build(), this.team(color));
      tm.castShadow = true;
      group.add(tm);
      addInk(tm, this.terrain.uniforms, INK);
    }
    // banner on the keep
    const flagPivot = new Object3D();
    flagPivot.position.set(k.flag[0], k.flag[1], k.flag[2]);
    const cloth = new Builder();
    cloth.box(0.17, 0.1, 0.005, 'white', { x: 0.09, y: 0 });
    cloth.box(0.17, 0.014, 0.007, 'white', { x: 0.09, y: 0.036 });
    const clothMesh = new Mesh(cloth.build(), this.team(color));
    clothMesh.castShadow = true;
    flagPivot.add(clothMesh);
    addInk(clothMesh, this.terrain.uniforms, INK);
    group.add(flagPivot);
    const [X, Z] = tileXZ(c.x, c.y);
    group.position.set(X, Math.max(0, this.terrain.heightAt(X, Z)) - 0.005, Z);
    group.scale.setScalar(S);
    this.scene.add(group);
    return { group, sig: '', cloths: [flagPivot], chimneys: chimneys.map(([cx, cy, cz]) => [cx * S + X, cy * S + group.position.y, cz * S + Z] as [number, number, number]), seed: rng() * 50 };
  }

  update(view: GameView, now: number): void {
    const t = now / 1000;
    const alive = new Set<number>();
    const W = view.settings.width;
    for (const c of view.cities) {
      if (view.state[c.y * W + c.x] === 0) continue;
      alive.add(c.id);
      const color = civColor(view, c.owner);
      const sig = `${c.pop}:${c.capital}:${color}:${(c.buildings ?? []).join(',')}:${(c.wonders ?? []).join(',')}:${this.terrain.gx}`;
      let s = this.slots.get(c.id);
      if (!s || s.sig !== sig) {
        if (s) {
          this.scene.remove(s.group);
          s.group.traverse((o) => ((o as Mesh).geometry as { dispose?: () => void } | undefined)?.dispose?.());
        }
        s = this.build(c, color);
        s.sig = sig;
        this.slots.set(c.id, s);
      }
      for (const f of s.cloths) {
        f.rotation.y = Math.sin(t * 3 + s.seed) * 0.4;
        f.rotation.z = Math.sin(t * 2.3 + s.seed) * 0.06;
      }
      // hearth smoke
      if (view.state[c.y * W + c.x] === 2 && Math.random() < 0.05 + Math.min(c.pop, 12) * 0.004) {
        const ch = s.chimneys[Math.floor(Math.random() * s.chimneys.length)];
        if (ch) this.fx.smoke(ch[0], ch[1], ch[2], 0.9);
      }
    }
    for (const [id, s] of this.slots) {
      if (!alive.has(id)) {
        this.scene.remove(s.group);
        this.slots.delete(id);
      }
    }
  }
}
