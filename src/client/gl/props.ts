import {
  AdditiveBlending, BackSide, Color, DoubleSide, Group, InstancedMesh, Matrix4, Mesh, MeshBasicMaterial, MeshStandardMaterial, Object3D, Quaternion,
  RingGeometry, Vector3, type BufferGeometry, type Scene,
} from 'three';
import { RESOURCE_LIST, TERRAIN_LIST, type GameView } from '../../shared/view';
import { Prefab } from './prefabs';
import { patchMaterial } from './fog';
import { rngFor } from './noise';
import { SQ3, tileXZ, type Terrain } from './terrain';

type Rng = () => number;

const INK_COLOR = new Color('#2b1d17');

/** A random point inside a pointy-top hex of the given radius, relative to its centre. */
function spotIn(rng: Rng, r: number): [number, number] {
  for (let i = 0; i < 20; i++) {
    const dx = (rng() * 2 - 1) * r * (SQ3 / 2);
    const dz = (rng() * 2 - 1) * r;
    if (Math.abs(dz) <= r - Math.abs(dx) / SQ3) return [dx, dz];
  }
  return [0, 0];
}

const M = new Matrix4();
const Q = new Quaternion();
const V = new Vector3();
const S = new Vector3();
const AXIS_Y = new Vector3(0, 1, 0);
function compose(x: number, y: number, z: number, yaw: number, scale: number, sy = scale): Matrix4 {
  Q.setFromAxisAngle(AXIS_Y, yaw);
  V.set(x, y, z);
  S.set(scale, sy, scale);
  return M.clone().compose(V, Q, S);
}

interface InstSet {
  geo: BufferGeometry;
  mats: Matrix4[];
  colors: Color[];
}

interface Animal {
  root: Group;
  head: Object3D;
  seed: number;
  home: [number, number];
  kind: 'cow' | 'horse' | 'deer';
  baseY: number;
}
interface Fish {
  mesh: Mesh;
  ring: Mesh;
  seed: number;
  home: [number, number];
}

/** Everything standing on the ground that is not a city or a unit: trees, rocks, crops, animals, mines, huts. */
export class Props {
  group = new Group();
  private sig = '';
  private animals: Animal[] = [];
  private fishes: Fish[] = [];
  private foliageMat: MeshStandardMaterial;
  private hardMat: MeshStandardMaterial;
  private glowMat: MeshStandardMaterial;
  private animalMat: MeshStandardMaterial;
  private ringMat: MeshBasicMaterial;
  private fishMat: MeshStandardMaterial;
  private inkFoliageMat: MeshBasicMaterial;
  private inkHardMat: MeshBasicMaterial;
  private geos = new Map<string, BufferGeometry>();

  constructor(scene: Scene, private terrain: Terrain) {
    scene.add(this.group);
    const u = terrain.uniforms;
    this.foliageMat = new MeshStandardMaterial({ vertexColors: true, roughness: 0.85 });
    patchMaterial(this.foliageMat, u, { sway: 0.5, paint: 34 });
    this.hardMat = new MeshStandardMaterial({ vertexColors: true, roughness: 0.9 });
    patchMaterial(this.hardMat, u, { paint: 30 });
    this.glowMat = new MeshStandardMaterial({ vertexColors: true, roughness: 0.35, metalness: 0.3, emissive: 0xffffff, emissiveIntensity: 0.5 });
    patchMaterial(this.glowMat, u);
    this.animalMat = new MeshStandardMaterial({ vertexColors: true, roughness: 0.9 });
    patchMaterial(this.animalMat, u, { paint: 34 });
    this.fishMat = new MeshStandardMaterial({ vertexColors: true, roughness: 0.4, metalness: 0.1 });
    patchMaterial(this.fishMat, u);
    this.ringMat = new MeshBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.5, depthWrite: false, side: DoubleSide, blending: AdditiveBlending });
    // ink shells share the sway of the foliage they outline
    this.inkFoliageMat = new MeshBasicMaterial({ color: INK_COLOR, side: BackSide });
    patchMaterial(this.inkFoliageMat, u, { sway: 0.5, outline: 0.009 });
    this.inkHardMat = new MeshBasicMaterial({ color: INK_COLOR, side: BackSide });
    patchMaterial(this.inkHardMat, u, { outline: 0.008 });
  }

  dispose(): void {
    this.inkFoliageMat.dispose();
    this.inkHardMat.dispose();
  }

  private geo(key: string, make: () => BufferGeometry): BufferGeometry {
    let g = this.geos.get(key);
    if (!g) {
      g = make();
      this.geos.set(key, g);
    }
    return g;
  }

  private clear(): void {
    for (const c of [...this.group.children]) {
      this.group.remove(c);
      if (c instanceof InstancedMesh) c.dispose();
    }
    this.animals = [];
    this.fishes = [];
  }

  /** A cheap fingerprint of everything the props are placed from. */
  signature(view: GameView, cityKeys: Set<number>): string {
    const W = view.settings.width;
    const H = view.settings.height;
    let acc = 0;
    for (let i = 0; i < W * H; i++) {
      acc = (Math.imul(acc, 31) + (view.state[i] > 0 ? 1 : 0) + view.terrain[i] * 5 + view.resource[i] * 17 + view.improvement[i] * 29 + view.hut[i] * 41) | 0;
    }
    for (const k of cityKeys) acc = (Math.imul(acc, 19) + k) | 0;
    return `${W}:${acc}:${this.terrain.gx}`;
  }

  get currentSig(): string {
    return this.sig;
  }

  update(view: GameView, cityKeys: Set<number>, sig = this.signature(view, cityKeys)): void {
    const W = view.settings.width;
    const H = view.settings.height;
    if (sig === this.sig) return;
    this.sig = sig;
    this.clear();

    const sets = new Map<string, InstSet & { mat: MeshStandardMaterial; shadow: boolean }>();
    const put = (key: string, make: () => BufferGeometry, mat: MeshStandardMaterial, m: Matrix4, color?: Color, shadow = true) => {
      let s = sets.get(key);
      if (!s) {
        s = { geo: this.geo(key, make), mats: [], colors: [], mat, shadow };
        sets.set(key, s);
      }
      s.mats.push(m);
      s.colors.push(color ?? new Color(1, 1, 1));
    };
    const tint = (rng: Rng, base = 1, spread = 0.18): Color => {
      const k = base * (1 - spread / 2 + rng() * spread);
      return new Color(k, k * (0.94 + rng() * 0.12), k * (0.92 + rng() * 0.1));
    };
    const ground = (X: number, Z: number) => Math.max(0, this.terrain.heightAt(X, Z));

    for (let y = 0; y < H; y++) {
      for (let x = 0; x < W; x++) {
        const i = y * W + x;
        if (view.state[i] === 0) continue;
        if (cityKeys.has(i)) continue;
        const t = TERRAIN_LIST[view.terrain[i]];
        const [cx, cz] = tileXZ(x, y);
        const rng = rngFor(x, y, 11);
        const imp = view.improvement[i];
        const res = view.resource[i] > 0 ? RESOURCE_LIST[view.resource[i] - 1] : null;
        // resources keep to one corner of their tile so trees leave them room
        const rsx = cx + 0.24;
        const rsz = cz + 0.1;
        const free = (px: number, pz: number) => !res || Math.hypot(px - rsx, pz - rsz) > 0.34;
        const tree = (kind: 'broadleaf' | 'pine', r: number, scale: [number, number]) => {
          for (let n = 0; n < 6; n++) {
            const [dx, dz] = spotIn(rng, r);
            if (!free(cx + dx, cz + dz)) continue;
            const s = scale[0] + rng() * (scale[1] - scale[0]);
            const px = cx + dx;
            const pz = cz + dz;
            const snowy = t === 'tundra';
            const col = snowy ? new Color(0.95, 1.0, 1.05) : tint(rng, 1, 0.3);
            put(kind, kind === 'broadleaf' ? Prefab.broadleaf : Prefab.pine, this.foliageMat, compose(px, ground(px, pz) - 0.01, pz, rng() * 6.28, s, s * (0.9 + rng() * 0.3)), col);
            return;
          }
        };
        const rock = (r: number, scale: [number, number]) => {
          const [dx, dz] = spotIn(rng, r);
          if (!free(cx + dx, cz + dz)) return;
          const s = scale[0] + rng() * (scale[1] - scale[0]);
          put('rock', Prefab.rock, this.hardMat, compose(cx + dx, ground(cx + dx, cz + dz) - 0.02, cz + dz, rng() * 6.28, s), tint(rng, 1, 0.3));
        };
        if (!imp && (t === 'grass' || t === 'plains' || t === 'hills' || t === 'forest')) {
          const n = t === 'grass' ? 9 : t === 'plains' ? 7 : t === 'forest' ? 3 : 4;
          for (let k = 0; k < n; k++) {
            const [dx, dz] = spotIn(rng, 0.82);
            if (!free(cx + dx, cz + dz)) continue;
            const s = 0.8 + rng() * 0.9;
            put('tuft', Prefab.tuft, this.foliageMat, compose(cx + dx, ground(cx + dx, cz + dz) - 0.005, cz + dz, rng() * 6.28, s), tint(rng, t === 'plains' ? 1.15 : 1, 0.3), false);
          }
          if (t === 'grass' && rng() < 0.45) {
            const hues = ['#f4e06a', '#f4f0e8', '#e98bb0', '#b48be0'];
            const [bx, bz] = spotIn(rng, 0.7);
            for (let k = 0; k < 4; k++) {
              const fx = cx + bx + (rng() - 0.5) * 0.18;
              const fz = cz + bz + (rng() - 0.5) * 0.18;
              const col = hues[Math.floor(rng() * hues.length)];
              put(`flower:${col}`, () => Prefab.flower(col), this.foliageMat, compose(fx, ground(fx, fz) - 0.005, fz, rng() * 6, 0.9 + rng() * 0.5), undefined, false);
            }
          }
        }
        if (!imp) {
          switch (t) {
            case 'forest': {
              const n = 10;
              for (let k = 0; k < n; k++) tree(rng() < 0.32 ? 'pine' : 'broadleaf', 0.78, [0.85, 1.3]);
              break;
            }
            case 'grass':
              for (let k = Math.floor(rng() * 3); k > 0; k--) tree(rng() < 0.2 ? 'pine' : 'broadleaf', 0.7, [0.7, 1.0]);
              if (rng() < 0.2) rock(0.7, [0.6, 0.9]);
              break;
            case 'plains':
              if (rng() < 0.55) tree('broadleaf', 0.7, [0.6, 0.85]);
              if (rng() < 0.3) rock(0.7, [0.6, 0.9]);
              break;
            case 'tundra':
              for (let k = 0; k < 4; k++) tree('pine', 0.75, [0.6, 0.95]);
              if (rng() < 0.5) rock(0.7, [0.7, 1.1]);
              break;
            case 'desert':
              if (rng() < 0.7) {
                const [dx, dz] = spotIn(rng, 0.65);
                if (free(cx + dx, cz + dz)) put('cactus', Prefab.cactus, this.foliageMat, compose(cx + dx, ground(cx + dx, cz + dz) - 0.01, cz + dz, rng() * 6.28, 0.85 + rng() * 0.6), tint(rng));
              }
              if (rng() < 0.5) rock(0.7, [0.7, 1.2]);
              break;
            case 'hills':
              for (let k = 0; k < 2; k++) if (rng() < 0.55) tree(rng() < 0.7 ? 'pine' : 'broadleaf', 0.62, [0.7, 1.0]);
              for (let k = 0; k < 2; k++) rock(0.65, [0.8, 1.4]);
              break;
            case 'mountain':
              for (let k = 0; k < 3; k++) rock(0.6, [0.5, 1.2]);
              break;
            default:
          }
        } else if (imp === 1) {
          const bx = cx - 0.32;
          const bz = cz - 0.34;
          put('barn', Prefab.barn, this.hardMat, compose(bx, ground(bx, bz), bz, (rng() - 0.5) * 0.9, 1.0), undefined);
          for (let k = 0; k < 2; k++) {
            const hx = cx + 0.1 + k * 0.17;
            const hz = cz - 0.46 + rng() * 0.1;
            put('haystack', Prefab.haystack, this.hardMat, compose(hx, ground(hx, hz), hz, rng() * 6, 0.9 + rng() * 0.3), tint(rng));
          }
        } else if (imp === 2) {
          put('mine', Prefab.mine, this.hardMat, compose(cx, ground(cx, cz) - 0.02, cz - 0.05, 0.4, 1.0));
        }
        if (view.hut[i] === 1) put('hut', Prefab.hut, this.hardMat, compose(cx, ground(cx, cz), cz, rng() * 6, 1.15));

        // resources
        if (res) {
          const gy = Math.max(ground(rsx, rsz), 0);
          switch (res) {
            case 'wheat':
              for (let k = 0; k < 34; k++) {
                const a = rng() * 6.28;
                const rr = Math.sqrt(rng()) * 0.3;
                const px = rsx + Math.cos(a) * rr;
                const pz = rsz + Math.sin(a) * rr * 0.8;
                put('stalk', Prefab.stalk, this.foliageMat, compose(px, ground(px, pz), pz, rng() * 6, 0.9 + rng() * 0.4, 0.9 + rng() * 0.5), tint(rng, 1, 0.2));
              }
              break;
            case 'iron':
            case 'gold':
            case 'gems': {
              put('oreRocks', Prefab.oreRocks, this.hardMat, compose(rsx, gy - 0.01, rsz, rng() * 6, 1.5));
              const col = res === 'gold' ? '#ffcf3a' : res === 'gems' ? '#58d0ff' : '#d9692c';
              put(`crystal:${res}`, () => Prefab.crystal(col), this.glowMat, compose(rsx, gy + 0.03, rsz, rng() * 6, 1.6));
              break;
            }
            // wine, spices and pearls: a small cluster of glowing goods in their own colour
            case 'wine':
            case 'spices':
            case 'pearls': {
              const col = res === 'wine' ? '#a8325f' : res === 'spices' ? '#e8822a' : '#f3efe6';
              for (let k = 0; k < 4; k++) {
                const a = k * 1.7 + rng();
                const rr = 0.12 + rng() * 0.2;
                const px = rsx + Math.cos(a) * rr;
                const pz = rsz + Math.sin(a) * rr * 0.8;
                put(`crystal:${res}`, () => Prefab.crystal(col), this.glowMat, compose(px, Math.max(ground(px, pz), 0) + 0.02, pz, rng() * 6, 1.2 + rng() * 0.6));
              }
              break;
            }
            case 'cattle':
            case 'horses':
            case 'deer': {
              const kind = res === 'cattle' ? 'cow' : res === 'horses' ? 'horse' : 'deer';
              for (let k = 0; k < 2; k++) {
                const a = k * 2.4 + rng();
                const ax = rsx + Math.cos(a) * 0.14;
                const az = rsz + Math.sin(a) * 0.12;
                this.addAnimal(kind, ax, ground(ax, az), az, rng, k);
              }
              break;
            }
            case 'fish': {
              const fm = new Mesh(this.geo('fish', Prefab.fish), this.fishMat);
              fm.visible = false;
              fm.castShadow = false;
              const ring = new Mesh(new RingGeometry(0.6, 1, 24).rotateX(-Math.PI / 2), this.ringMat.clone());
              ring.visible = false;
              this.group.add(fm, ring);
              this.fishes.push({ mesh: fm, ring, seed: rng() * 100, home: [rsx, rsz] });
              break;
            }
          }
        }
      }
    }
    // one instanced mesh per kind of prop per block of ground, so blocks that are off screen are not drawn or shadowed
    const BLOCK = 20;
    for (const [, s] of sets) {
      const blocks = new Map<number, number[]>();
      for (let k = 0; k < s.mats.length; k++) {
        const e = s.mats[k].elements;
        const key = Math.floor(e[12] / BLOCK) * 1024 + Math.floor(e[14] / BLOCK);
        const list = blocks.get(key);
        if (list) list.push(k);
        else blocks.set(key, [k]);
      }
      for (const [, list] of blocks) {
        const mesh = new InstancedMesh(s.geo, s.mat, list.length);
        list.forEach((k, n) => {
          mesh.setMatrixAt(n, s.mats[k]);
          mesh.setColorAt(n, s.colors[k]);
        });
        mesh.castShadow = s.shadow;
        mesh.receiveShadow = true;
        // swaying foliage and tall props reach beyond the instance origins, so pad the culling sphere
        mesh.computeBoundingSphere();
        if (mesh.boundingSphere) mesh.boundingSphere.radius += 2.5;
        this.group.add(mesh);
        // solid props get an ink outline; grass, flowers and stalks are too fine for one
        if (s.shadow && s.mat !== this.glowMat) {
          const ink = new InstancedMesh(s.geo, s.mat === this.foliageMat ? this.inkFoliageMat : this.inkHardMat, list.length);
          list.forEach((k, n) => ink.setMatrixAt(n, s.mats[k]));
          ink.castShadow = false;
          ink.receiveShadow = false;
          ink.boundingSphere = mesh.boundingSphere;
          this.group.add(ink);
        }
      }
    }
  }

  private addAnimal(kind: 'cow' | 'horse' | 'deer', x: number, y: number, z: number, rng: Rng, n: number): void {
    const root = new Group();
    root.position.set(x, y, z);
    root.rotation.y = rng() * 6.28;
    const palettes = {
      cow: [['#f1ece0', '#3a3834'], ['#7a5538', '#d9cfc0']],
      horse: [['#7a4a2b', null], ['#cfc3a6', null]],
      deer: [['#a77a4a', '#e8dcc4'], ['#8f6a40', null]],
    } as const;
    const [coat, patch] = palettes[kind][n % 2];
    const size = kind === 'horse' ? 1.5 : kind === 'cow' ? 1.25 : 1.15;
    const body = new Mesh(this.geo(`ab:${kind}:${n % 2}`, () => Prefab.animalBody(coat, patch, size, kind === 'horse' ? 1.15 : 1)), this.animalMat);
    const headGeo = this.geo(`ah:${kind}:${n % 2}`, () => Prefab.animalHead(coat, size, kind === 'cow' ? 'horns' : kind === 'deer' && n % 2 === 1 ? 'antlers' : kind === 'horse' ? 'mane' : 'none'));
    const head = new Object3D();
    const hm = new Mesh(headGeo, this.animalMat);
    head.add(hm);
    const hmInk = new Mesh(headGeo, this.inkHardMat);
    head.add(hmInk);
    const baseY = (kind === 'horse' ? 0.34 : 0.29) * size;
    head.position.set(0.19 * size, baseY, 0);
    // neck
    const neck = new Mesh(this.geo(`an:${kind}:${n % 2}`, () => {
      return neckGeo(coat, size, kind === 'horse' ? 1.5 : 1);
    }), this.animalMat);
    neck.position.set(0.09 * size, 0.2 * size, 0);
    body.castShadow = hm.castShadow = neck.castShadow = true;
    root.add(body, neck, head, new Mesh(body.geometry, this.inkHardMat), neckInk(neck, this.inkHardMat));
    this.group.add(root);
    this.animals.push({ root, head, seed: rng() * 50, home: [x, z], kind, baseY });
  }

  tick(t: number): void {
    for (const a of this.animals) {
      const k = t * (a.kind === 'horse' ? 0.7 : 0.5) + a.seed;
      // graze: head dips, then lifts and looks around
      const dip = Math.max(0, Math.sin(k * 1.3));
      a.head.rotation.z = -0.15 - dip * 0.9;
      a.head.position.y = a.baseY - dip * 0.07;
      const look = Math.sin(k * 0.7) * 0.4;
      a.head.rotation.y = look;
      a.root.rotation.y += Math.sin(k * 0.31) * 0.0015;
      a.root.position.y = Math.max(0, this.terrain.heightAt(a.root.position.x, a.root.position.z));
    }
    for (const f of this.fishes) {
      const period = 4.2;
      const k = ((t + f.seed) % period) / period;
      const leap = k < 0.22 ? k / 0.22 : -1;
      if (leap >= 0) {
        const ang = leap * Math.PI;
        f.mesh.visible = true;
        f.mesh.position.set(f.home[0] + (leap - 0.5) * 0.28, 0.02 + Math.sin(ang) * 0.17, f.home[1] + Math.sin(f.seed) * 0.1);
        f.mesh.rotation.set(0, Math.sin(f.seed) * 2, -Math.cos(ang) * 0.9);
        f.mesh.scale.setScalar(1.3);
      } else f.mesh.visible = false;
      // ripples where it entered and left the water
      const rk = k < 0.5 ? k / 0.5 : 2;
      if (rk <= 1) {
        f.ring.visible = true;
        const rad = 0.04 + rk * 0.22;
        f.ring.scale.set(rad, 1, rad);
        f.ring.position.set(f.home[0] - 0.14, 0.012, f.home[1] + Math.sin(f.seed) * 0.1);
        (f.ring.material as MeshBasicMaterial).opacity = (1 - rk) * 0.5;
      } else f.ring.visible = false;
    }
  }
}

/** An ink shell for the neck, which sits at the neck's own offset. */
function neckInk(neck: Mesh, mat: MeshBasicMaterial): Mesh {
  const shell = new Mesh(neck.geometry, mat);
  shell.position.copy(neck.position);
  return shell;
}

function neckGeo(coat: string, size: number, len: number): BufferGeometry {
  return Prefab.animalNeck(coat, size, len);
}
