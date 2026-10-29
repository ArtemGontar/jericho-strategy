import {
  ACESFilmicToneMapping, AmbientLight, Color, DirectionalLight, HemisphereLight, OrthographicCamera, PCFSoftShadowMap, Scene, Vector3, WebGLRenderer,
} from 'three';
import type { Animator } from '../anim';
import { Camera, civColor, type MapDrawer, type RenderState } from '../render';
import { Terrain } from './terrain';
import { Props } from './props';
import { CityLayer } from './city3d';
import { UnitLayer } from './units3d';
import { Particles, Projectiles } from './fx';
import { Overlay } from './overlay';
import { tileXZ } from './terrain';
import { Sky } from './sky';

const HEX = 48;
export const PITCH = 52;

export type Graphics = 'auto' | 'high' | 'medium' | 'low' | 'flat';
export const GRAPHICS_LABEL: Record<Graphics, string> = { auto: 'Automatic', high: 'High', medium: 'Medium', low: 'Low', flat: 'Flat 2D' };
export function graphicsPref(): Graphics {
  try {
    const v = localStorage.getItem('jericho.gfx');
    if (v === 'high' || v === 'medium' || v === 'low' || v === 'flat') return v;
  } catch { /* ignore */ }
  return 'auto';
}
export function setGraphicsPref(g: Graphics): void {
  try { localStorage.setItem('jericho.gfx', g); } catch { /* ignore */ }
}

/** The 3D map: a tilted orthographic view of lit, shadowed terrain, with the 2D overlay drawn on top. */
export class GLRenderer implements MapDrawer {
  readonly continuous = true;
  readonly gl: WebGLRenderer;
  readonly scene = new Scene();
  readonly camera = new OrthographicCamera(-10, 10, 10, -10, 1, 600);
  readonly terrain: Terrain;
  readonly props: Props;
  readonly cities: CityLayer;
  readonly units: UnitLayer;
  readonly fx: Particles;
  private projectiles: Projectiles;
  private overlay: Overlay;
  private sky: Sky;
  private sun: DirectionalLight;
  private hemi!: HemisphereLight;
  private sunDir = new Vector3(-0.55, 0.78, 0.5).normalize();
  private target = new Vector3();
  private lastNow = 0;

  /** WebGL2 present, not switched off, and not a software rasteriser (which would crawl). `?gl` forces it on. */
  static supported(): boolean {
    try {
      const q = new URLSearchParams(location.search);
      if (q.has('flat') || localStorage.getItem('jericho.flat') === '1' || graphicsPref() === 'flat') return false;
      const c = document.createElement('canvas');
      const gl = c.getContext('webgl2') as WebGL2RenderingContext | null;
      if (!gl) return false;
      if (!q.has('gl') && localStorage.getItem('jericho.gl') !== '1' && graphicsPref() === 'auto') {
        const ext = gl.getExtension('WEBGL_debug_renderer_info');
        const name = ext ? String(gl.getParameter(ext.UNMASKED_RENDERER_WEBGL)) : '';
        if (/swiftshader|llvmpipe|software|softpipe/i.test(name)) return false;
      }
      gl.getExtension('WEBGL_lose_context')?.loseContext();
      return true;
    } catch {
      return false;
    }
  }

  /** 2 = full effects, 1 = no shadows, 0 = also lower resolution. Drops by itself when frames take too long. */
  quality = 2;
  private adaptive = true;
  private slowFrames = 0;
  private frames = 0;

  private applyQuality(): void {
    this.gl.shadowMap.enabled = this.quality >= 2;
    this.sun.castShadow = this.quality >= 2;
    this.gl.shadowMap.needsUpdate = true;
    const dpr = Math.min(this.quality >= 2 ? 2 : this.quality === 1 ? 1.25 : 1, window.devicePixelRatio || 1);
    this.gl.setPixelRatio(dpr);
    this.gl.setSize(Math.max(1, this.cam.w), Math.max(1, this.cam.h), false);
    for (const m of this.scene.children) m.traverse?.((o) => {
      const mat = (o as { material?: { needsUpdate: boolean } }).material;
      if (mat && !Array.isArray(mat)) mat.needsUpdate = true;
    });
  }

  private pending = new Map<string, { sig: string; since: number; changed: number }>();

  /** True when `sig` differs from what is built and has stopped changing for a moment (or the first build is due). */
  private settled(key: string, sig: string, built: string, now: number): boolean {
    if (sig === built) {
      this.pending.delete(key);
      return false;
    }
    if (!built) return true;
    const p = this.pending.get(key);
    if (!p) {
      this.pending.set(key, { sig, since: now, changed: now });
      return false;
    }
    if (p.sig !== sig) {
      p.sig = sig;
      p.changed = now;
    }
    const ready = now - p.changed > 220 || now - p.since > 1200;
    if (ready) this.pending.delete(key);
    return ready;
  }

  private watchFrameRate(now: number): void {
    if (this.lastNow) {
      const dt = now - this.lastNow;
      this.frames++;
      if (dt > 50 && dt < 1500) this.slowFrames++;
      else if (dt <= 30 && this.slowFrames > 0) this.slowFrames--;
      if (this.adaptive && this.slowFrames > 40 && this.quality > 0) {
        this.quality--;
        this.slowFrames = 0;
        this.applyQuality();
      }
    }
  }

  constructor(public glCanvas: HTMLCanvasElement, public canvas: HTMLCanvasElement, public cam: Camera, public anim: Animator) {
    this.gl = new WebGLRenderer({ canvas: glCanvas, antialias: true, alpha: false, powerPreference: 'high-performance' });
    this.gl.setClearColor(new Color(0x0a1120));
    this.gl.toneMapping = ACESFilmicToneMapping;
    this.gl.toneMappingExposure = 1.05;
    this.gl.shadowMap.enabled = true;
    this.gl.shadowMap.type = PCFSoftShadowMap;
    cam.setPitch(PITCH);
    const pref = graphicsPref();
    if (pref !== 'auto') {
      this.adaptive = false;
      this.quality = pref === 'high' ? 2 : pref === 'medium' ? 1 : 0;
      this.gl.shadowMap.enabled = this.quality >= 2;
    }

    this.hemi = new HemisphereLight(0xcfe2ff, 0x6b5a3e, 1.05);
    this.scene.add(this.hemi);
    this.scene.add(new AmbientLight(0xffffff, 0.12));
    this.sun = new DirectionalLight(0xfff0d6, 2.9);
    this.sun.castShadow = true;
    this.sun.shadow.mapSize.set(2048, 2048);
    this.sun.shadow.bias = -0.0006;
    this.sun.shadow.normalBias = 0.03;
    this.scene.add(this.sun, this.sun.target);
    this.terrain = new Terrain(this.scene);
    this.props = new Props(this.scene, this.terrain);
    this.fx = new Particles(this.scene);
    this.cities = new CityLayer(this.scene, this.terrain, this.fx);
    this.units = new UnitLayer(this.scene, this.terrain, anim, this.fx);
    this.projectiles = new Projectiles(this.scene, this.terrain, anim, this.fx);
    this.overlay = new Overlay(this);
    this.sky = new Sky(this.scene);
  }

  resize(): void {
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    const r = this.canvas.getBoundingClientRect();
    this.canvas.width = Math.max(1, Math.floor(r.width * dpr));
    this.canvas.height = Math.max(1, Math.floor(r.height * dpr));
    this.gl.setPixelRatio(Math.min(dpr, this.quality >= 2 ? 2 : this.quality === 1 ? 1.25 : 1));
    this.gl.setSize(Math.max(1, r.width), Math.max(1, r.height), false);
    this.cam.w = r.width;
    this.cam.h = r.height;
  }

  /** Size the renderer without a laid-out canvas (for off-screen snapshots). */
  resizeTo(w: number, h: number, dpr = 1): void {
    this.canvas.width = Math.floor(w * dpr);
    this.canvas.height = Math.floor(h * dpr);
    this.gl.setPixelRatio(dpr);
    this.gl.setSize(w, h, false);
    this.cam.w = w;
    this.cam.h = h;
  }

  dispose(): void {
    this.units.dispose_all();
    this.gl.dispose();
    this.gl.forceContextLoss();
  }

  private syncCamera(): void {
    const cam = this.cam;
    const per = cam.zoom * HEX;
    const hw = cam.w / 2 / per;
    const hh = cam.h / 2 / per;
    const c = this.camera;
    c.left = -hw;
    c.right = hw;
    c.top = hh;
    c.bottom = -hh;
    c.near = 1;
    c.far = 600;
    const p = (PITCH * Math.PI) / 180;
    this.target.set(cam.x / HEX, 0, cam.y / HEX);
    c.position.set(this.target.x, Math.sin(p) * 200, this.target.z + Math.cos(p) * 200);
    c.up.set(0, 1, 0);
    c.lookAt(this.target);
    c.updateProjectionMatrix();
    c.updateMatrixWorld();
    // sun follows the view so the shadow map is spent where the player is looking
    const radius = Math.min(46, Math.hypot(hw, hh / cam.tilt) + 4);
    const dir = this.sunDir;
    this.sun.position.copy(this.target).addScaledVector(dir, 80);
    this.sun.target.position.copy(this.target);
    const sc = this.sun.shadow.camera;
    sc.left = -radius;
    sc.right = radius;
    sc.top = radius;
    sc.bottom = -radius;
    sc.near = 1;
    sc.far = 200;
    sc.updateProjectionMatrix();
    this.sun.target.updateMatrixWorld();
  }

  draw(st: RenderState): void {
    const v = st.view;
    const W = v.settings.width;
    const t = st.now / 1000;
    const cityKeys = new Set<number>();
    for (const c of v.cities) cityKeys.add(c.y * W + c.x);
    // rebuilding the ground and its props takes tens of milliseconds, so while the player is still uncovering map
    // (every step of a scout) wait until the changes settle instead of hitching on each one
    const tSig = this.terrain.signature(v, cityKeys);
    const pSig = this.props.signature(v, cityKeys);
    if (this.settled('terrain', tSig, this.terrain.currentSig, st.now)) this.terrain.setView(v, cityKeys, tSig);
    if (this.terrain.mesh) {
      this.terrain.updateFog(v);
      this.terrain.updateDecal(v, (o) => civColor(v, o), cityKeys);
      if (this.settled('props', pSig, this.props.currentSig, st.now)) this.props.update(v, cityKeys, pSig);
    }
    this.props.tick(t);
    this.cities.update(v, st.now);
    this.units.update(st, st.now, cityKeys);
    this.projectiles.update(st.now);
    this.fx.update(st.now, this.cam.zoom, this.gl.getPixelRatio());
    this.setDaylight(v.turn);
    this.terrain.setTime(t);
    this.sky.update(t, Math.min(0.1, (st.now - (this.lastNow || st.now)) / 1000), this.terrain.extX, this.cam.zoom, this.terrain.extZ);
    this.syncCamera();
    this.gl.render(this.scene, this.camera);
    const ctx = this.canvas.getContext('2d')!;
    const dpr = this.canvas.width / this.cam.w;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, this.cam.w, this.cam.h);
    this.overlay.draw(ctx, st);
    this.watchFrameRate(st.now);
    this.lastNow = st.now;
  }

  bannerHit(cityId: number, sx: number, sy: number): boolean {
    const r = this.overlay.rects.get(cityId);
    return !!r && sx >= r.x - 4 && sx <= r.x + r.w + 4 && sy >= r.y - 4 && sy <= r.y + r.h + 4;
  }

  /**
   * The sun drifts across the sky as turns pass: warm mornings, bright noons, red dusks and a short blue night.
   * A full day lasts 36 turns, and a game starts in the morning.
   */
  private setDaylight(turn: number): void {
    const p = (((turn - 1) / 36 + 0.14) % 1 + 1) % 1;
    // keyframes: [phase, sun rgb, sun intensity, sky rgb, ground rgb, hemi intensity, elevation]
    const K: Array<[number, number[], number, number[], number[], number, number]> = [
      [0.0, [1.0, 0.62, 0.38], 1.6, [0.75, 0.62, 0.62], [0.35, 0.28, 0.25], 0.7, 0.28],
      [0.12, [1.0, 0.86, 0.68], 2.5, [0.78, 0.84, 0.95], [0.42, 0.35, 0.26], 0.95, 0.55],
      [0.4, [1.0, 0.97, 0.9], 3.0, [0.82, 0.9, 1.0], [0.43, 0.36, 0.25], 1.05, 0.95],
      [0.66, [1.0, 0.88, 0.7], 2.6, [0.8, 0.85, 0.95], [0.42, 0.34, 0.24], 0.95, 0.6],
      [0.8, [1.0, 0.5, 0.3], 1.7, [0.78, 0.55, 0.55], [0.34, 0.25, 0.22], 0.72, 0.26],
      [0.9, [0.45, 0.55, 0.9], 0.55, [0.32, 0.4, 0.62], [0.16, 0.16, 0.22], 0.62, 0.45],
      [1.0, [1.0, 0.62, 0.38], 1.6, [0.75, 0.62, 0.62], [0.35, 0.28, 0.25], 0.7, 0.28],
    ];
    let i = 0;
    while (i < K.length - 2 && p > K[i + 1][0]) i++;
    const a = K[i];
    const b = K[i + 1];
    const k = (p - a[0]) / (b[0] - a[0]);
    const mixv = (x: number[], y: number[]) => x.map((v, n) => v + (y[n] - v) * k);
    const sunC = mixv(a[1], b[1]);
    const sky = mixv(a[3], b[3]);
    const gnd = mixv(a[4], b[4]);
    const lerp = (x: number, y: number) => x + (y - x) * k;
    this.sun.color.setRGB(sunC[0], sunC[1], sunC[2]);
    this.sun.intensity = lerp(a[2], b[2]);
    this.hemi.color.setRGB(sky[0], sky[1], sky[2]);
    this.hemi.groundColor.setRGB(gnd[0], gnd[1], gnd[2]);
    this.hemi.intensity = lerp(a[5], b[5]);
    const el = lerp(a[6], b[6]);
    const az = (p < 0.85 ? p / 0.85 : 0.5) * Math.PI;
    // the sun crosses from the east side to the west side, always lit from the camera's side
    this.sunDir.set(-Math.cos(az) * Math.cos(el) * 0.9, Math.sin(el), 0.45 + Math.cos(el) * 0.3).normalize();
    this.terrain.setSun(this.sunDir, [sunC[0], sunC[1], sunC[2]]);
  }

  /** Screen position of a scene point (X, Z on the ground, Y up). */
  screenOf(X: number, Z: number, Y = 0): { x: number; y: number } {
    return this.cam.toScreen(X * HEX, Z * HEX, Y * HEX);
  }

  /** Which tile is under a screen position, accounting for the height of the ground. */
  pickTile(sx: number, sy: number, W: number, H: number): { x: number; y: number } | null {
    const base = this.cam.tileAt(sx, sy, W, H);
    if (!base) return null;
    let best: { x: number; y: number } | null = null;
    let bestD = 1e9;
    for (let dy = -1; dy <= 3; dy++) {
      for (let dx = -1; dx <= 1; dx++) {
        const x = base.x + dx;
        const y = base.y + dy;
        if (x < 0 || y < 0 || x >= W || y >= H) continue;
        const [X, Z] = tileXZ(x, y);
        const p = this.screenOf(X, Z, Math.max(0, this.terrain.heightAt(X, Z)));
        const ux = (sx - p.x) / (HEX * this.cam.zoom);
        const uy = (sy - p.y) / (HEX * this.cam.zoom * this.cam.tilt);
        const m = Math.max(Math.abs(ux) / 0.866, Math.abs(ux * 0.5 + uy * 0.866) / 0.866 * 1.0, Math.abs(ux * 0.5 - uy * 0.866) / 0.866);
        const d = Math.hypot(ux, uy);
        if (m <= 1.02 && d < bestD) {
          bestD = d;
          best = { x, y };
        }
      }
    }
    return best ?? base;
  }
}
