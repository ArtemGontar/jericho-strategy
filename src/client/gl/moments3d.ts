import {
  AdditiveBlending, CylinderGeometry, DoubleSide, Group, Mesh, MeshBasicMaterial, RingGeometry, ShaderMaterial, type Scene,
} from 'three';
import type { Pos } from '../../shared/hex';
import type { Particles } from './fx';
import { tileXZ, type Terrain } from './terrain';

export type EffectKind = 'ring' | 'column' | 'fire' | 'confetti' | 'embers' | 'splash' | 'promote' | 'grow';
type RGB = [number, number, number];

interface Eff {
  kind: EffectKind;
  x: number;
  z: number;
  y: number;
  t0: number;
  dur: number;
  color: RGB;
  size: number;
  next: number;
  ring: Mesh | null;
  column: Mesh | null;
}

/** What the effects need to know about the view to scatter things across it. */
export interface Frame {
  cx: number;
  cz: number;
  hw: number;
  hh: number;
}

const MAX = 24;
const GOLD: RGB = [1, 0.82, 0.38];
const PALETTE: RGB[] = [[1, 0.82, 0.38], [1, 1, 1], [0.95, 0.45, 0.35], [0.45, 0.8, 1], [0.6, 0.95, 0.5]];

/**
 * One-shot staged effects for the big moments: expanding ground rings, a rising light column, city fires, confetti,
 * embers, splashes. Particles come from the shared pools; the rings and columns are a few pooled meshes. Nothing
 * here runs unless an effect is live, and `update` says whether more frames are needed.
 */
export class Effects3D {
  private group = new Group();
  private live: Eff[] = [];
  private ringGeo = new RingGeometry(0.86, 1, 40, 1).rotateX(-Math.PI / 2);
  private colGeo = new CylinderGeometry(0.14, 0.2, 7, 18, 1, true).translate(0, 3.5, 0);
  private ringFree: Mesh[] = [];
  private colFree: Mesh[] = [];
  private all: Mesh[] = [];

  constructor(private scene: Scene, private terrain: Terrain, private fx: Particles) {
    scene.add(this.group);
  }

  get active(): boolean {
    return this.live.length > 0;
  }

  private ringMesh(): Mesh {
    let m = this.ringFree.pop();
    if (!m) {
      m = new Mesh(this.ringGeo, new MeshBasicMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide }));
      m.renderOrder = 4;
      m.frustumCulled = false;
      this.group.add(m);
      this.all.push(m);
    }
    return m;
  }

  private columnMesh(): Mesh {
    let m = this.colFree.pop();
    if (!m) {
      m = new Mesh(this.colGeo, new ShaderMaterial({
        transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide,
        uniforms: { uK: { value: 0 }, uTime: { value: 0 }, uColor: { value: [1, 0.82, 0.38] } },
        vertexShader: /* glsl */ `
          varying vec3 vP; varying float vH;
          void main(){ vP = position; vH = position.y / 7.0; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }`,
        fragmentShader: /* glsl */ `
          varying vec3 vP; varying float vH; uniform float uK; uniform float uTime; uniform vec3 uColor;
          void main(){
            float up = pow(clamp(1.0 - vH, 0.0, 1.0), 1.5);
            float s = 0.62 + 0.38 * sin(vP.y * 5.0 - uTime * 6.0 + atan(vP.z, vP.x) * 3.0);
            vec3 c = mix(uColor, vec3(1.0), 0.18 * up);
            gl_FragColor = vec4(c, up * s * uK * 0.85);
          }`,
      }));
      m.renderOrder = 7;
      m.frustumCulled = false;
      this.group.add(m);
      this.all.push(m);
    }
    return m;
  }

  spawn(kind: EffectKind, at: Pos, now: number, opts: { dur?: number; color?: RGB; size?: number } = {}): void {
    if (this.live.length >= MAX) this.release(this.live.shift()!);
    const [X, Z] = tileXZ(at.x, at.y);
    const y = Math.max(0, this.terrain.heightAt(X, Z));
    const dur = opts.dur ?? DEFAULT_DUR[kind];
    const e: Eff = { kind, x: X, z: Z, y, t0: now, dur, color: opts.color ?? GOLD, size: opts.size ?? 1, next: 0, ring: null, column: null };
    if (kind === 'ring' || kind === 'grow' || kind === 'promote' || kind === 'splash') e.ring = this.ringMesh();
    if (kind === 'column') {
      e.column = this.columnMesh();
      (e.column.material as ShaderMaterial).uniforms.uColor.value = e.color;
      e.column.position.set(X, y, Z);
    }
    if (kind === 'splash') this.fx.burst(X, y + 0.05, Z, [0.8, 0.92, 1], 18, 0.55);
    if (kind === 'grow') this.fx.burst(X, y + 0.2, Z, [0.7, 1, 0.55], 14, 0.5);
    this.live.push(e);
  }

  private release(e: Eff): void {
    if (e.ring) {
      e.ring.visible = false;
      this.ringFree.push(e.ring);
    }
    if (e.column) {
      e.column.visible = false;
      this.colFree.push(e.column);
    }
  }

  /** How many emission steps are due: frame-rate independent, and never a burst after a stall. */
  private ticks(e: Eff, now: number, gap: number): number {
    if (e.next === 0 || e.next < now - 4 * gap) e.next = now;
    let n = 0;
    while (e.next <= now && n < 4) {
      e.next += gap;
      n++;
    }
    return n;
  }

  /** Advance every live effect; returns true while any needs frames. */
  update(now: number, f: Frame): boolean {
    for (let i = this.live.length - 1; i >= 0; i--) {
      const e = this.live[i];
      const k = (now - e.t0) / e.dur;
      if (k >= 1) {
        this.release(e);
        this.live.splice(i, 1);
        continue;
      }
      if (k < 0) {
        // queued behind an animation: nothing shows until its time
        if (e.ring) e.ring.visible = false;
        if (e.column) e.column.visible = false;
        continue;
      }
      if (e.ring) e.ring.visible = true;
      if (e.column) e.column.visible = true;
      switch (e.kind) {
        case 'ring':
        case 'grow':
        case 'splash':
        case 'promote': {
          const r = e.ring!;
          const base = e.kind === 'splash' ? 0.8 : e.kind === 'promote' ? 0.6 : e.kind === 'grow' ? 1.1 : 1.6;
          const sc = base * e.size * (0.15 + 0.85 * (1 - (1 - k) * (1 - k)));
          r.position.set(e.x, e.y + 0.03, e.z);
          r.scale.set(sc, 1, sc);
          const mat = r.material as MeshBasicMaterial;
          mat.color.setRGB(e.color[0], e.color[1], e.color[2]);
          mat.opacity = Math.pow(1 - k, 1.4) * 0.9;
          if (e.kind === 'promote' && now >= e.next) {
            e.next = now + 40;
            const a = k * 18;
            this.fx.emit({ x: e.x + Math.cos(a) * 0.22, y: e.y + 0.1 + k * 0.9, z: e.z + Math.sin(a) * 0.22, vy: 0.2, life: 0.5, size0: 0.07, size1: 0.01, c0: [1, 0.9, 0.5, 1], c1: [1, 0.6, 0.2, 0] }, true);
          } else if (e.kind === 'splash' && now >= e.next) {
            e.next = now + 90;
            this.fx.emit({ x: e.x + (Math.random() - 0.5) * 0.3, y: e.y + 0.02, z: e.z + (Math.random() - 0.5) * 0.3, vy: 0.18, life: 1.1, size0: 0.04, size1: 0.07, c0: [0.9, 0.97, 1, 0.55], c1: [0.9, 0.97, 1, 0], drag: 0.6 });
          }
          break;
        }
        case 'column': {
          const m = e.column!;
          const env = k < 0.18 ? k / 0.18 : k > 0.7 ? (1 - k) / 0.3 : 1;
          const u = (m.material as ShaderMaterial).uniforms;
          u.uK.value = env;
          u.uTime.value = now / 1000;
          const w = (1.3 + 0.7 * env) * e.size;
          m.scale.set(w, 0.25 + 0.75 * Math.min(1, k / 0.25), w);
          for (let st = this.ticks(e, now, 55); st > 0; st--) {
            const a = Math.random() * Math.PI * 2;
            this.fx.emit({ x: e.x + Math.cos(a) * 0.16, y: e.y + 0.1, z: e.z + Math.sin(a) * 0.16, vy: 0.9 + Math.random() * 0.8, life: 1.6, size0: 0.05, size1: 0.01, c0: [1, 0.9, 0.55, 0.9], c1: [1, 0.7, 0.3, 0] }, true);
          }
          break;
        }
        case 'fire': {
          for (let st = this.ticks(e, now, 45); st > 0; st--) {
            const heat = 1 - k * 0.75;
            for (let n = 0; n < 2; n++) {
              if (Math.random() > heat) continue;
              const ox = (Math.random() - 0.5) * 0.5 * e.size;
              const oz = (Math.random() - 0.5) * 0.5 * e.size;
              this.fx.emit({ x: e.x + ox, y: e.y + 0.18, z: e.z + oz, vy: 0.45 + Math.random() * 0.4, life: 0.5 + Math.random() * 0.4, size0: 0.15, size1: 0.02, c0: [1, 0.55, 0.15, 0.85], c1: [1, 0.2, 0.05, 0], drag: 0.8 }, true);
            }
            if (Math.random() < 0.5 * heat) this.fx.emit({ x: e.x + (Math.random() - 0.5) * 0.4, y: e.y + 0.4, z: e.z + (Math.random() - 0.5) * 0.4, vx: 0.05, vy: 0.3, life: 2.4, size0: 0.1, size1: 0.4, c0: [0.16, 0.15, 0.15, 0.55], c1: [0.1, 0.1, 0.1, 0], drag: 0.3 });
          }
          break;
        }
        case 'confetti':
        case 'embers': {
          for (let st = this.ticks(e, now, 40); st > 0; st--) {
            const fade = k > 0.75 ? (1 - k) / 0.25 : 1;
            for (let n = 0; n < 5; n++) {
              if (Math.random() > fade) continue;
              const x = f.cx + (Math.random() * 2 - 1) * f.hw;
              const z = f.cz + (Math.random() * 2 - 1) * f.hh;
              if (e.kind === 'confetti') {
                const c = PALETTE[(Math.random() * PALETTE.length) | 0];
                this.fx.emit({ x, y: 2.5 + Math.random() * 2.5, z, vx: (Math.random() - 0.5) * 0.3, vy: -0.1, vz: (Math.random() - 0.5) * 0.2, life: 3 + Math.random(), size0: 0.07, size1: 0.05, c0: [c[0], c[1], c[2], 1], c1: [c[0], c[1], c[2], 0.2], gravity: 0.35, drag: 0.4 }, true);
              } else {
                this.fx.emit({ x, y: 0.1 + Math.random() * 0.4, z, vx: 0.08 + Math.random() * 0.1, vy: 0.3 + Math.random() * 0.35, vz: (Math.random() - 0.5) * 0.1, life: 3 + Math.random() * 1.5, size0: 0.06, size1: 0.01, c0: [1, 0.5, 0.18, 1], c1: [0.6, 0.1, 0.05, 0], drag: 0.15 }, true);
              }
            }
          }
          break;
        }
      }
    }
    return this.live.length > 0;
  }

  dispose(): void {
    for (const e of this.live) this.release(e);
    this.live.length = 0;
    for (const m of this.all) {
      (m.material as { dispose(): void }).dispose();
    }
    this.ringGeo.dispose();
    this.colGeo.dispose();
    this.scene.remove(this.group);
    this.all.length = 0;
    this.ringFree.length = 0;
    this.colFree.length = 0;
  }
}

const DEFAULT_DUR: Record<EffectKind, number> = {
  ring: 1100, grow: 900, promote: 1100, splash: 1300, column: 3600, fire: 6000, confetti: 7000, embers: 7000,
};
