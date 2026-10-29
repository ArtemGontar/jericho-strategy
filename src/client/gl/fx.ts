import {
  AdditiveBlending, BufferAttribute, BufferGeometry, CylinderGeometry, Group, Mesh, MeshStandardMaterial, NormalBlending, Points, ShaderMaterial,
  SphereGeometry, Vector3, type Scene,
} from 'three';
import type { Animator, Projectile } from '../anim';
import { tileXZ, type Terrain } from './terrain';

export interface ParticleSpec {
  x: number;
  y: number;
  z: number;
  vx?: number;
  vy?: number;
  vz?: number;
  life: number;
  size0: number;
  size1: number;
  /** start and end colour as r,g,b,a (0..1, display space) */
  c0: [number, number, number, number];
  c1?: [number, number, number, number];
  gravity?: number;
  drag?: number;
}

class Pool {
  readonly points: Points;
  private pos: Float32Array;
  private col: Float32Array;
  private siz: Float32Array;
  private vel: Float32Array;
  private age: Float32Array;
  private life: Float32Array;
  private s0: Float32Array;
  private s1: Float32Array;
  private c0: Float32Array;
  private c1: Float32Array;
  private grav: Float32Array;
  private drag: Float32Array;
  private head = 0;
  private live = 0;
  readonly material: ShaderMaterial;

  constructor(private cap: number, additive: boolean) {
    this.pos = new Float32Array(cap * 3);
    this.col = new Float32Array(cap * 4);
    this.siz = new Float32Array(cap);
    this.vel = new Float32Array(cap * 3);
    this.age = new Float32Array(cap).fill(1e9);
    this.life = new Float32Array(cap).fill(1);
    this.s0 = new Float32Array(cap);
    this.s1 = new Float32Array(cap);
    this.c0 = new Float32Array(cap * 4);
    this.c1 = new Float32Array(cap * 4);
    this.grav = new Float32Array(cap);
    this.drag = new Float32Array(cap);
    const g = new BufferGeometry();
    g.setAttribute('position', new BufferAttribute(this.pos, 3));
    g.setAttribute('aColor', new BufferAttribute(this.col, 4));
    g.setAttribute('aSize', new BufferAttribute(this.siz, 1));
    this.material = new ShaderMaterial({
      transparent: true,
      depthWrite: false,
      blending: additive ? AdditiveBlending : NormalBlending,
      uniforms: { uScale: { value: 48 } },
      vertexShader: /* glsl */ `
        attribute vec4 aColor; attribute float aSize; varying vec4 vColor; uniform float uScale;
        void main(){
          vColor = aColor;
          vec4 mv = modelViewMatrix * vec4(position, 1.0);
          gl_Position = projectionMatrix * mv;
          gl_PointSize = max(0.0, aSize * uScale);
        }`,
      fragmentShader: /* glsl */ `
        varying vec4 vColor;
        void main(){
          vec2 p = gl_PointCoord * 2.0 - 1.0;
          float d = dot(p, p);
          if (d > 1.0) discard;
          float a = (1.0 - d) * (1.0 - d);
          gl_FragColor = vec4(vColor.rgb, vColor.a * a);
        }`,
    });
    this.points = new Points(g, this.material);
    this.points.frustumCulled = false;
    this.points.renderOrder = additive ? 6 : 5;
  }

  emit(s: ParticleSpec): void {
    const i = this.head;
    this.head = (this.head + 1) % this.cap;
    this.pos[i * 3] = s.x;
    this.pos[i * 3 + 1] = s.y;
    this.pos[i * 3 + 2] = s.z;
    this.vel[i * 3] = s.vx ?? 0;
    this.vel[i * 3 + 1] = s.vy ?? 0;
    this.vel[i * 3 + 2] = s.vz ?? 0;
    this.age[i] = 0;
    this.life[i] = s.life;
    this.s0[i] = s.size0;
    this.s1[i] = s.size1;
    const c1 = s.c1 ?? [s.c0[0], s.c0[1], s.c0[2], 0];
    for (let k = 0; k < 4; k++) {
      this.c0[i * 4 + k] = s.c0[k];
      this.c1[i * 4 + k] = c1[k];
    }
    this.grav[i] = s.gravity ?? 0;
    this.drag[i] = s.drag ?? 0;
  }

  update(dt: number): void {
    let any = false;
    for (let i = 0; i < this.cap; i++) {
      const a = this.age[i];
      if (a > this.life[i]) {
        this.siz[i] = 0;
        continue;
      }
      any = true;
      this.age[i] = a + dt;
      const k = Math.min(1, this.age[i] / this.life[i]);
      const d = Math.max(0, 1 - this.drag[i] * dt);
      this.vel[i * 3] *= d;
      this.vel[i * 3 + 1] = this.vel[i * 3 + 1] * d - this.grav[i] * dt;
      this.vel[i * 3 + 2] *= d;
      this.pos[i * 3] += this.vel[i * 3] * dt;
      this.pos[i * 3 + 1] += this.vel[i * 3 + 1] * dt;
      this.pos[i * 3 + 2] += this.vel[i * 3 + 2] * dt;
      this.siz[i] = this.s0[i] + (this.s1[i] - this.s0[i]) * k;
      for (let c = 0; c < 4; c++) this.col[i * 4 + c] = this.c0[i * 4 + c] + (this.c1[i * 4 + c] - this.c0[i * 4 + c]) * k;
    }
    this.live = any ? 1 : 0;
    const g = this.points.geometry;
    g.attributes.position.needsUpdate = true;
    g.attributes.aColor.needsUpdate = true;
    g.attributes.aSize.needsUpdate = true;
    this.points.visible = this.live > 0;
  }
}

/** Smoke, dust, sparks and glows. Sizes are in scene units; the pixel scale follows the camera zoom. */
export class Particles {
  private soft = new Pool(1800, false);
  private glow = new Pool(900, true);
  private last = 0;

  constructor(scene: Scene) {
    scene.add(this.soft.points, this.glow.points);
  }

  emit(s: ParticleSpec, glow = false): void {
    (glow ? this.glow : this.soft).emit(s);
  }

  smoke(x: number, y: number, z: number, strength = 1): void {
    this.soft.emit({
      x, y, z, vx: 0.03 + Math.random() * 0.03, vy: 0.12 + Math.random() * 0.06, vz: (Math.random() - 0.5) * 0.04,
      life: 2.6 + Math.random() * 1.2, size0: 0.05 * strength, size1: 0.26 * strength,
      c0: [0.82, 0.82, 0.84, 0.5], c1: [0.6, 0.6, 0.64, 0], drag: 0.2,
    });
  }

  dust(x: number, y: number, z: number, color: [number, number, number] = [0.78, 0.68, 0.5]): void {
    this.soft.emit({
      x: x + (Math.random() - 0.5) * 0.08, y, z: z + (Math.random() - 0.5) * 0.08,
      vx: (Math.random() - 0.5) * 0.14, vy: 0.08 + Math.random() * 0.08, vz: (Math.random() - 0.5) * 0.14,
      life: 0.7 + Math.random() * 0.4, size0: 0.05, size1: 0.17,
      c0: [color[0], color[1], color[2], 0.5], c1: [color[0], color[1], color[2], 0], drag: 2.2,
    });
  }

  sparks(x: number, y: number, z: number, n = 10, hot: [number, number, number] = [1, 0.78, 0.35]): void {
    for (let i = 0; i < n; i++) {
      const a = Math.random() * Math.PI * 2;
      const sp = 0.4 + Math.random() * 0.8;
      this.glow.emit({
        x, y, z, vx: Math.cos(a) * sp, vy: 0.5 + Math.random() * 0.9, vz: Math.sin(a) * sp,
        life: 0.35 + Math.random() * 0.35, size0: 0.045, size1: 0.01,
        c0: [hot[0], hot[1], hot[2], 1], c1: [1, 0.3, 0.1, 0], gravity: 2.4, drag: 1.2,
      });
    }
  }

  flash(x: number, y: number, z: number, size = 0.4): void {
    this.glow.emit({ x, y, z, life: 0.16, size0: size, size1: size * 0.3, c0: [1, 0.85, 0.5, 0.9], c1: [1, 0.5, 0.1, 0] });
  }

  burst(x: number, y: number, z: number, color: [number, number, number], n = 36, radius = 1): void {
    for (let i = 0; i < n; i++) {
      const a = (i / n) * Math.PI * 2 + Math.random() * 0.3;
      const sp = (0.5 + Math.random() * 0.9) * radius;
      this.glow.emit({
        x, y, z, vx: Math.cos(a) * sp, vy: 0.3 + Math.random() * 0.8, vz: Math.sin(a) * sp,
        life: 0.9 + Math.random() * 0.5, size0: 0.09, size1: 0.02,
        c0: [color[0], color[1], color[2], 1], c1: [color[0], color[1], color[2], 0], gravity: 1.2, drag: 0.8,
      });
    }
  }

  update(now: number, zoom: number, pixelRatio: number): void {
    const dt = this.last ? Math.min(0.05, (now - this.last) / 1000) : 0.016;
    this.last = now;
    const scale = zoom * 48 * pixelRatio;
    this.soft.material.uniforms.uScale.value = scale;
    this.glow.material.uniforms.uScale.value = scale;
    this.soft.update(dt);
    this.glow.update(dt);
  }
}


/** Arrows, boulders and cannonballs in flight, with their impacts. */
export class Projectiles {
  private group = new Group();
  private live = new Map<Projectile, Mesh>();
  private landed = new WeakSet<Projectile>();
  private arrowGeo = new CylinderGeometry(0.008, 0.008, 0.26, 4).rotateZ(Math.PI / 2);
  private rockGeo = new SphereGeometry(0.075, 7, 6);
  private ballGeo = new SphereGeometry(0.055, 8, 6);
  private arrowMat = new MeshStandardMaterial({ color: 0xe9dcb8, roughness: 0.7 });
  private rockMat = new MeshStandardMaterial({ color: 0x77726a, roughness: 0.95, flatShading: true });
  private ballMat = new MeshStandardMaterial({ color: 0x25272b, roughness: 0.5, metalness: 0.5 });
  private tmp = new Vector3();

  get active(): boolean {
    return this.live.size > 0;
  }

  constructor(scene: Scene, private terrain: Terrain, private anim: Animator, private fx: Particles) {
    scene.add(this.group);
  }

  update(now: number): void {
    const seen = new Set<Projectile>();
    for (const p of this.anim.projectiles) {
      const k = (now - p.t0) / p.dur;
      if (k < 0) continue;
      seen.add(p);
      const [ax, az] = tileXZ(p.from.x, p.from.y);
      const [bx, bz] = tileXZ(p.to.x, p.to.y);
      const gy = (X: number, Z: number) => Math.max(0, this.terrain.heightAt(X, Z));
      if (k >= 1) {
        if (!this.landed.has(p)) {
          this.landed.add(p);
          const h = gy(bx, bz) + 0.18;
          if (p.kind === 'bolt') {
            this.fx.flash(bx, h, bz, 0.8);
            this.fx.sparks(bx, h, bz, 16);
            for (let i = 0; i < 8; i++) this.fx.smoke(bx, h, bz, 1.5);
          } else if (p.kind === 'rock') {
            this.fx.sparks(bx, h, bz, 12, [0.9, 0.85, 0.75]);
            for (let i = 0; i < 6; i++) this.fx.dust(bx, h - 0.1, bz);
          } else this.fx.sparks(bx, h, bz, 5, [1, 0.95, 0.8]);
        }
        continue;
      }
      let m = this.live.get(p);
      if (!m) {
        m = new Mesh(p.kind === 'arrow' ? this.arrowGeo : p.kind === 'rock' ? this.rockGeo : this.ballGeo, p.kind === 'arrow' ? this.arrowMat : p.kind === 'rock' ? this.rockMat : this.ballMat);
        m.castShadow = true;
        this.group.add(m);
        this.live.set(p, m);
      }
      const x = ax + (bx - ax) * k;
      const z = az + (bz - az) * k;
      const lift = p.kind === 'arrow' ? 0.5 : p.kind === 'rock' ? 0.9 : 0.35;
      const y0 = gy(ax, az) + 0.32;
      const y1 = gy(bx, bz) + 0.2;
      const y = y0 + (y1 - y0) * k + Math.sin(k * Math.PI) * lift;
      m.position.set(x, y, z);
      if (p.kind === 'arrow') {
        const k2 = Math.min(1, k + 0.03);
        const nx = ax + (bx - ax) * k2;
        const nz = az + (bz - az) * k2;
        const ny = y0 + (y1 - y0) * k2 + Math.sin(k2 * Math.PI) * lift;
        this.tmp.set(nx - x, ny - y, nz - z);
        m.rotation.set(0, 0, 0);
        m.rotation.y = Math.atan2(-this.tmp.z, this.tmp.x);
        m.rotation.z = Math.atan2(this.tmp.y, Math.hypot(this.tmp.x, this.tmp.z));
      } else if (p.kind === 'rock') {
        m.rotation.x += 0.2;
        m.rotation.z += 0.13;
      } else if (Math.random() < 0.6) this.fx.smoke(x, y, z, 0.5);
    }
    for (const [p, m] of this.live) {
      if (!seen.has(p) || now - p.t0 >= p.dur) {
        this.group.remove(m);
        this.live.delete(p);
      }
    }
  }
}
