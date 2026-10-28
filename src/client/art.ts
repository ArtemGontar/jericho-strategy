/**
 * Procedural painting for the map: ground textures that blend into their neighbours, standing terrain features
 * (trees, hills, mountains, dunes), resources, improvements, cities and unit figures. Everything is drawn once
 * into small canvases (lit from the upper left) and then stamped onto the map.
 */
import { Rng } from '../shared/rng';
import type { ResourceId, TerrainId, UnitTypeId } from '../shared/types';
import { HEX, HEX_H, HEX_W } from './sprites';

export const ART_RES = 2;

export interface Sprite {
  c: HTMLCanvasElement;
  /** anchor in canvas pixels: the world point the sprite is positioned by */
  ax: number;
  ay: number;
}

function canvas(w: number, h: number): { c: HTMLCanvasElement; ctx: CanvasRenderingContext2D } {
  const c = document.createElement('canvas');
  c.width = Math.ceil(w);
  c.height = Math.ceil(h);
  const ctx = c.getContext('2d')!;
  return { c, ctx };
}

// ---------------------------------------------------------------- noise
function hash(ix: number, iy: number, seed: number): number {
  let h = Math.imul(ix, 374761393) ^ Math.imul(iy, 668265263) ^ Math.imul(seed, 2147483647);
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}
function smooth(t: number): number {
  return t * t * (3 - 2 * t);
}
function vnoise(x: number, y: number, seed: number): number {
  const ix = Math.floor(x);
  const iy = Math.floor(y);
  const fx = smooth(x - ix);
  const fy = smooth(y - iy);
  const a = hash(ix, iy, seed) * (1 - fx) + hash(ix + 1, iy, seed) * fx;
  const b = hash(ix, iy + 1, seed) * (1 - fx) + hash(ix + 1, iy + 1, seed) * fx;
  return a * (1 - fy) + b * fy;
}
function fbm(x: number, y: number, seed: number, oct = 4): number {
  let sum = 0;
  let amp = 0.5;
  let f = 1;
  let norm = 0;
  for (let i = 0; i < oct; i++) {
    sum += vnoise(x * f, y * f, seed + i * 31) * amp;
    norm += amp;
    amp *= 0.5;
    f *= 2.07;
  }
  return sum / norm;
}

type RGB = [number, number, number];
const hex3 = (h: string): RGB => {
  const n = parseInt(h.slice(1), 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
};
function mix(a: RGB, b: RGB, t: number): RGB {
  return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
}
function ramp(stops: Array<[number, RGB]>, t: number): RGB {
  if (t <= stops[0][0]) return stops[0][1];
  for (let i = 1; i < stops.length; i++) {
    if (t <= stops[i][0]) {
      const [t0, c0] = stops[i - 1];
      const [t1, c1] = stops[i];
      return mix(c0, c1, (t - t0) / (t1 - t0));
    }
  }
  return stops[stops.length - 1][1];
}

// ---------------------------------------------------------------- ground
const GROUND: Record<TerrainId, Array<[number, RGB]>> = {
  grass: [[0, hex3('#3f6b2b')], [0.35, hex3('#5a8a35')], [0.7, hex3('#7aab47')], [1, hex3('#9cc45a')]],
  plains: [[0, hex3('#9c8a45')], [0.4, hex3('#b8a55a')], [0.75, hex3('#cdb96b')], [1, hex3('#dfcd85')]],
  desert: [[0, hex3('#c9a868')], [0.4, hex3('#dcc283')], [0.75, hex3('#ead3a0')], [1, hex3('#f3e2b8')]],
  tundra: [[0, hex3('#8d9aa0')], [0.5, hex3('#b9c4c7')], [1, hex3('#e6ecee')]],
  forest: [[0, hex3('#2f5428')], [0.5, hex3('#42702f')], [1, hex3('#5a8a3d')]],
  hills: [[0, hex3('#7c7a3f')], [0.5, hex3('#9a9650')], [1, hex3('#b5ae68')]],
  mountain: [[0, hex3('#5d5a55')], [0.5, hex3('#7a766f')], [1, hex3('#98938b')]],
  coast: [[0, hex3('#2d8aa8')], [0.5, hex3('#4aa8bf')], [1, hex3('#7ccbd4')]],
  ocean: [[0, hex3('#13396b')], [0.5, hex3('#1a4a80')], [1, hex3('#235e97')]],
};

const GW = HEX_W * ART_RES + 60;
const GH = HEX_H * ART_RES + 60;

function hexMetric(dx: number, dy: number): number {
  return Math.max(Math.abs(dx), Math.abs(dx * 0.5 + dy * 0.8660254), Math.abs(dx * 0.5 - dy * 0.8660254));
}

const groundCache = new Map<string, Sprite>();
export function groundSprite(terrain: TerrainId, variant: number): Sprite {
  const key = `${terrain}:${variant}`;
  const hit = groundCache.get(key);
  if (hit) return hit;
  const { c, ctx } = canvas(GW, GH);
  const img = ctx.createImageData(c.width, c.height);
  const d = img.data;
  const cx = c.width / 2;
  const cy = c.height / 2;
  const apothem = (HEX * Math.cos(Math.PI / 6));
  const seed = variant * 977 + terrain.charCodeAt(0) * 13;
  const stops = GROUND[terrain];
  for (let py = 0; py < c.height; py++) {
    for (let px = 0; px < c.width; px++) {
      const dx = (px - cx) / ART_RES;
      const dy = (py - cy) / ART_RES;
      const m = hexMetric(dx, dy);
      let alpha: number;
      alpha = 1 - smooth(Math.max(0, Math.min(1, (m - apothem * 0.74) / (apothem * 0.52))));
      if (alpha <= 0) continue;
      const wx = dx + variant * 37;
      const wy = dy + variant * 19;
      let n = fbm(wx * 0.045, wy * 0.045, seed, 4);
      const fine = vnoise(wx * 0.35, wy * 0.35, seed + 7);
      let t = Math.max(0, Math.min(1, (n - 0.25) * 1.9));
      let col = ramp(stops, t);
      if (terrain === 'desert') {
        const dune = Math.sin((wx * 0.5 + wy * 0.9) * 0.16 + n * 7) * 0.5 + 0.5;
        col = mix(col, ramp(stops, 0.2), Math.pow(dune, 3) * 0.35);
      } else if (terrain === 'ocean' || terrain === 'coast') {
        const wave = Math.sin((wx * 0.7 - wy * 0.3) * 0.22 + n * 9) * 0.5 + 0.5;
        col = mix(col, ramp(stops, 1), Math.pow(wave, 6) * 0.22);
        if (terrain === 'coast') col = mix(col, hex3('#9ee0e0'), Math.pow(vnoise(wx * 0.09, wy * 0.09, seed + 3), 3) * 0.28);
      } else if (terrain === 'mountain') {
        col = mix(col, [54, 50, 46], Math.pow(fine, 2) * 0.3);
      } else if (terrain === 'grass' || terrain === 'forest') {
        col = mix(col, [30, 60, 20], Math.max(0, fine - 0.55) * 0.5);
      }
      // speckle
      const sp = (fine - 0.5) * 16;
      const i = (py * c.width + px) * 4;
      d[i] = Math.max(0, Math.min(255, col[0] + sp));
      d[i + 1] = Math.max(0, Math.min(255, col[1] + sp));
      d[i + 2] = Math.max(0, Math.min(255, col[2] + sp));
      d[i + 3] = alpha * 255;
    }
  }
  ctx.putImageData(img, 0, 0);
  const s = { c, ax: cx, ay: cy };
  groundCache.set(key, s);
  return s;
}

// ---------------------------------------------------------------- painting helpers
function shadow(ctx: CanvasRenderingContext2D, x: number, y: number, rx: number, ry: number, a = 0.28): void {
  ctx.save();
  ctx.fillStyle = `rgba(10,14,8,${a})`;
  ctx.beginPath();
  ctx.ellipse(x, y, rx, ry, -0.1, 0, Math.PI * 2);
  ctx.fill();
  ctx.restore();
}

function blob(ctx: CanvasRenderingContext2D, x: number, y: number, r: number, c0: string, c1: string, c2: string): void {
  const g = ctx.createRadialGradient(x - r * 0.35, y - r * 0.4, r * 0.1, x, y, r);
  g.addColorStop(0, c0);
  g.addColorStop(0.55, c1);
  g.addColorStop(1, c2);
  ctx.fillStyle = g;
  ctx.beginPath();
  ctx.arc(x, y, r, 0, Math.PI * 2);
  ctx.fill();
}

function tree(ctx: CanvasRenderingContext2D, x: number, y: number, s: number, kind: 'round' | 'pine', rng: Rng): void {
  shadow(ctx, x + s * 0.18, y + s * 0.02, s * 0.5, s * 0.16);
  if (kind === 'round') {
    ctx.fillStyle = '#4a3322';
    ctx.fillRect(x - s * 0.06, y - s * 0.34, s * 0.12, s * 0.36);
    const tone = rng.next();
    const mids = tone < 0.5 ? ['#7fb04a', '#4f8a33', '#2b5a22'] : ['#6fa045', '#3f7a2f', '#244f1f'];
    blob(ctx, x - s * 0.18, y - s * 0.55, s * 0.3, mids[0], mids[1], mids[2]);
    blob(ctx, x + s * 0.2, y - s * 0.52, s * 0.28, mids[0], mids[1], mids[2]);
    blob(ctx, x, y - s * 0.78, s * 0.34, mids[0], mids[1], mids[2]);
  } else {
    ctx.fillStyle = '#4a3322';
    ctx.fillRect(x - s * 0.05, y - s * 0.22, s * 0.1, s * 0.24);
    for (let i = 0; i < 3; i++) {
      const w = s * (0.42 - i * 0.1);
      const yy = y - s * 0.16 - i * s * 0.3;
      const g = ctx.createLinearGradient(x - w, yy, x + w, yy);
      g.addColorStop(0, '#5f9a4a');
      g.addColorStop(0.5, '#2f6a35');
      g.addColorStop(1, '#1c4426');
      ctx.fillStyle = g;
      ctx.beginPath();
      ctx.moveTo(x - w, yy);
      ctx.lineTo(x + w, yy);
      ctx.lineTo(x, yy - s * 0.42);
      ctx.closePath();
      ctx.fill();
    }
  }
}

function mound(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, light: string, mid: string, dark: string, rng: Rng): void {
  shadow(ctx, x + w * 0.15, y + h * 0.05, w * 1.0, h * 0.22, 0.3);
  const g = ctx.createRadialGradient(x - w * 0.4, y - h * 0.9, h * 0.1, x, y - h * 0.3, w * 1.2);
  g.addColorStop(0, light);
  g.addColorStop(0.5, mid);
  g.addColorStop(1, dark);
  ctx.fillStyle = g;
  ctx.beginPath();
  ctx.moveTo(x - w, y);
  ctx.bezierCurveTo(x - w * 0.85, y - h * 1.05, x - w * 0.25, y - h * 1.25, x, y - h);
  ctx.bezierCurveTo(x + w * 0.35, y - h * 1.15, x + w * 0.9, y - h * 0.8, x + w, y);
  ctx.quadraticCurveTo(x, y + h * 0.18, x - w, y);
  ctx.fill();
  // grass tufts on the crown
  ctx.strokeStyle = 'rgba(70,100,35,0.5)';
  ctx.lineWidth = 1;
  for (let i = 0; i < 9; i++) {
    const tx = x + (rng.next() - 0.5) * w * 1.3;
    const ty = y - h * (0.2 + rng.next() * 0.6);
    ctx.beginPath();
    ctx.moveTo(tx - 1.5, ty);
    ctx.lineTo(tx, ty - 3);
    ctx.lineTo(tx + 1.5, ty);
    ctx.stroke();
  }
}

function peak(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, rng: Rng): void {
  shadow(ctx, x + w * 0.35, y + 1, w * 1.1, h * 0.12, 0.3);
  const ridge = x + w * (0.05 + rng.next() * 0.15);
  // lit face
  let g = ctx.createLinearGradient(x - w, y - h, x, y);
  g.addColorStop(0, '#b5afa5');
  g.addColorStop(1, '#8a857c');
  ctx.fillStyle = g;
  ctx.beginPath();
  ctx.moveTo(x, y - h);
  ctx.lineTo(x - w, y);
  ctx.lineTo(ridge, y);
  ctx.closePath();
  ctx.fill();
  // shaded face
  g = ctx.createLinearGradient(x, y - h, x + w, y);
  g.addColorStop(0, '#6d685f');
  g.addColorStop(1, '#4a463f');
  ctx.fillStyle = g;
  ctx.beginPath();
  ctx.moveTo(x, y - h);
  ctx.lineTo(ridge, y);
  ctx.lineTo(x + w, y);
  ctx.closePath();
  ctx.fill();
  // crags
  ctx.strokeStyle = 'rgba(30,28,24,0.35)';
  ctx.lineWidth = 1;
  for (let i = 0; i < 4; i++) {
    const t = 0.2 + rng.next() * 0.6;
    ctx.beginPath();
    ctx.moveTo(x + (rng.next() - 0.5) * w * 0.2, y - h * t);
    ctx.lineTo(x + (rng.next() - 0.3) * w * 0.7, y - h * (t - 0.25));
    ctx.stroke();
  }
  // snow
  const sh = h * 0.38;
  ctx.fillStyle = '#f4f8fb';
  ctx.beginPath();
  ctx.moveTo(x, y - h);
  ctx.lineTo(x - w * 0.36, y - h + sh);
  ctx.lineTo(x - w * 0.2, y - h + sh * 0.72);
  ctx.lineTo(x - w * 0.05, y - h + sh * 1.05);
  ctx.lineTo(x + w * 0.12, y - h + sh * 0.7);
  ctx.lineTo(x + w * 0.38, y - h + sh);
  ctx.closePath();
  ctx.fill();
  ctx.fillStyle = 'rgba(150,175,205,0.55)';
  ctx.beginPath();
  ctx.moveTo(x, y - h);
  ctx.lineTo(x + w * 0.38, y - h + sh);
  ctx.lineTo(x + w * 0.12, y - h + sh * 0.7);
  ctx.lineTo(x + w * 0.02, y - h + sh * 0.3);
  ctx.closePath();
  ctx.fill();
}

// ---------------------------------------------------------------- standing terrain features
const DW = HEX_W * ART_RES + 40;
const DH = HEX_H * ART_RES + 90;
const decorCache = new Map<string, Sprite | null>();

export function decorSprite(terrain: TerrainId, variant: number): Sprite | null {
  const key = `${terrain}:${variant}`;
  if (decorCache.has(key)) return decorCache.get(key)!;
  let out: Sprite | null = null;
  const rng = new Rng(variant * 4099 + terrain.charCodeAt(0) * 31 + terrain.length);
  const make = (draw: (ctx: CanvasRenderingContext2D) => void): Sprite => {
    const { c, ctx } = canvas(DW, DH);
    const ax = c.width / 2;
    const ay = c.height - HEX_H * ART_RES / 2 - 20;
    ctx.translate(ax, ay);
    ctx.scale(ART_RES, ART_RES);
    draw(ctx);
    return { c, ax, ay };
  };
  const spot = (): { x: number; y: number } => ({ x: (rng.next() - 0.5) * HEX_W * 0.62, y: (rng.next() - 0.35) * HEX * 1.15 });
  switch (terrain) {
    case 'forest':
      out = make((ctx) => {
        const list: Array<{ x: number; y: number; s: number; k: 'round' | 'pine' }> = [];
        for (let i = 0; i < 8; i++) {
          const p = spot();
          list.push({ ...p, s: 22 + rng.next() * 12, k: rng.chance(0.35) ? 'pine' : 'round' });
        }
        list.sort((a, b) => a.y - b.y);
        for (const t of list) tree(ctx, t.x, t.y + 8, t.s, t.k, rng);
      });
      break;
    case 'hills':
      out = make((ctx) => {
        mound(ctx, -14, 12, 24, 22, '#c9c277', '#9a9650', '#615f32', rng);
        mound(ctx, 15, 14, 22, 18, '#c2bb70', '#928d49', '#5c5a2f', rng);
        mound(ctx, 0, 3, 26, 26, '#d4cd82', '#a5a05a', '#686537', rng);
      });
      break;
    case 'mountain':
      out = make((ctx) => {
        peak(ctx, -17, 14, 20, 34 + rng.next() * 6, rng);
        peak(ctx, 18, 15, 19, 30 + rng.next() * 6, rng);
        peak(ctx, 0, 8, 26, 50 + rng.next() * 8, rng);
      });
      break;
    case 'desert':
      out = make((ctx) => {
        for (let i = 0; i < 3; i++) {
          const p = spot();
          const w = 20 + rng.next() * 10;
          const g = ctx.createLinearGradient(p.x - w, p.y - 8, p.x + w, p.y + 4);
          g.addColorStop(0, '#f6e6b8');
          g.addColorStop(0.55, '#e2c88a');
          g.addColorStop(1, '#bf9f62');
          ctx.fillStyle = g;
          ctx.beginPath();
          ctx.moveTo(p.x - w, p.y + 4);
          ctx.quadraticCurveTo(p.x - w * 0.2, p.y - 12, p.x + w * 0.15, p.y - 7);
          ctx.quadraticCurveTo(p.x + w * 0.6, p.y - 2, p.x + w, p.y + 4);
          ctx.closePath();
          ctx.fill();
        }
        if (rng.chance(0.7)) {
          const p = spot();
          ctx.strokeStyle = '#4c7a3a';
          ctx.lineWidth = 3;
          ctx.lineCap = 'round';
          ctx.beginPath();
          ctx.moveTo(p.x, p.y + 6);
          ctx.lineTo(p.x, p.y - 8);
          ctx.moveTo(p.x, p.y - 1);
          ctx.lineTo(p.x - 5, p.y - 4);
          ctx.lineTo(p.x - 5, p.y - 9);
          ctx.moveTo(p.x, p.y - 3);
          ctx.lineTo(p.x + 5, p.y - 5);
          ctx.lineTo(p.x + 5, p.y - 10);
          ctx.stroke();
        }
      });
      break;
    case 'grass':
    case 'plains':
      out = make((ctx) => {
        const gold = terrain === 'plains';
        for (let i = 0; i < 18; i++) {
          const p = spot();
          ctx.strokeStyle = gold ? 'rgba(160,130,50,0.7)' : 'rgba(40,85,30,0.55)';
          ctx.lineWidth = 1.2;
          ctx.beginPath();
          ctx.moveTo(p.x - 2, p.y);
          ctx.quadraticCurveTo(p.x - 1, p.y - 4, p.x - 3, p.y - 7);
          ctx.moveTo(p.x, p.y);
          ctx.lineTo(p.x, p.y - 8);
          ctx.moveTo(p.x + 2, p.y);
          ctx.quadraticCurveTo(p.x + 1, p.y - 4, p.x + 3, p.y - 7);
          ctx.stroke();
        }
        if (rng.chance(gold ? 0.35 : 0.6)) {
          const p = spot();
          tree(ctx, p.x, p.y + 6, 17 + rng.next() * 6, 'round', rng);
        }
        if (!gold) {
          for (let i = 0; i < 5; i++) {
            const p = spot();
            ctx.fillStyle = ['#f4e07a', '#f2f2f0', '#e98bb0'][i % 3];
            ctx.beginPath();
            ctx.arc(p.x, p.y, 1.2, 0, Math.PI * 2);
            ctx.fill();
          }
        }
      });
      break;
    case 'tundra':
      out = make((ctx) => {
        for (let i = 0; i < 5; i++) {
          const p = spot();
          ctx.fillStyle = 'rgba(250,252,255,0.85)';
          ctx.beginPath();
          ctx.ellipse(p.x, p.y, 8 + rng.next() * 6, 3.5, 0, 0, Math.PI * 2);
          ctx.fill();
        }
        for (let i = 0; i < 3; i++) {
          const p = spot();
          tree(ctx, p.x, p.y + 6, 15 + rng.next() * 5, 'pine', rng);
        }
      });
      break;
    case 'ocean':
    case 'coast':
      out = make((ctx) => {
        ctx.strokeStyle = terrain === 'ocean' ? 'rgba(190,225,250,0.28)' : 'rgba(255,255,255,0.38)';
        ctx.lineWidth = 1.2;
        ctx.lineCap = 'round';
        for (let i = 0; i < 7; i++) {
          const p = spot();
          ctx.beginPath();
          ctx.moveTo(p.x - 7, p.y);
          ctx.quadraticCurveTo(p.x - 3.5, p.y - 3, p.x, p.y);
          ctx.quadraticCurveTo(p.x + 3.5, p.y + 3, p.x + 7, p.y);
          ctx.stroke();
        }
      });
      break;
  }
  decorCache.set(key, out);
  return out;
}

// ---------------------------------------------------------------- resources & improvements
const OBJ = 64;
const objCache = new Map<string, Sprite>();

function objSprite(key: string, draw: (ctx: CanvasRenderingContext2D) => void): Sprite {
  const hit = objCache.get(key);
  if (hit) return hit;
  const { c, ctx } = canvas(OBJ * ART_RES, OBJ * ART_RES);
  ctx.translate(c.width / 2, c.height * 0.72);
  ctx.scale(ART_RES, ART_RES);
  draw(ctx);
  const s = { c, ax: c.width / 2, ay: c.height * 0.72 };
  objCache.set(key, s);
  return s;
}

function cow(ctx: CanvasRenderingContext2D, x: number, y: number, s: number, dark: boolean): void {
  shadow(ctx, x, y + 1, s * 0.62, s * 0.16);
  ctx.fillStyle = dark ? '#6b4a32' : '#f1ece0';
  ctx.beginPath();
  ctx.ellipse(x, y - s * 0.38, s * 0.55, s * 0.32, 0, 0, Math.PI * 2);
  ctx.fill();
  ctx.fillStyle = dark ? '#8a6244' : '#3c3a36';
  ctx.beginPath();
  ctx.ellipse(x - s * 0.1, y - s * 0.42, s * 0.18, s * 0.12, 0, 0, Math.PI * 2);
  ctx.fill();
  ctx.fillStyle = dark ? '#6b4a32' : '#f1ece0';
  ctx.beginPath();
  ctx.ellipse(x + s * 0.55, y - s * 0.5, s * 0.2, s * 0.17, 0.3, 0, Math.PI * 2);
  ctx.fill();
  ctx.fillStyle = '#3a2c22';
  ctx.fillRect(x - s * 0.38, y - s * 0.12, s * 0.08, s * 0.18);
  ctx.fillRect(x + s * 0.28, y - s * 0.12, s * 0.08, s * 0.18);
}

function horse(ctx: CanvasRenderingContext2D, x: number, y: number, s: number, coat: string, mane: string): void {
  shadow(ctx, x, y + 1, s * 0.7, s * 0.14);
  ctx.fillStyle = coat;
  ctx.beginPath();
  ctx.ellipse(x, y - s * 0.55, s * 0.55, s * 0.26, 0, 0, Math.PI * 2);
  ctx.fill();
  ctx.beginPath();
  ctx.moveTo(x + s * 0.35, y - s * 0.7);
  ctx.lineTo(x + s * 0.62, y - s * 1.08);
  ctx.lineTo(x + s * 0.88, y - s * 1.02);
  ctx.lineTo(x + s * 0.84, y - s * 0.86);
  ctx.lineTo(x + s * 0.62, y - s * 0.84);
  ctx.lineTo(x + s * 0.5, y - s * 0.5);
  ctx.closePath();
  ctx.fill();
  ctx.fillStyle = mane;
  ctx.fillRect(x + s * 0.5, y - s * 1.1, s * 0.07, s * 0.34);
  ctx.fillRect(x - s * 0.62, y - s * 0.6, s * 0.1, s * 0.2);
  ctx.fillStyle = coat;
  for (const lx of [-0.4, -0.2, 0.25, 0.45]) ctx.fillRect(x + s * lx, y - s * 0.4, s * 0.08, s * 0.4);
}

export function resourceSprite(res: ResourceId): Sprite {
  return objSprite(`res:${res}`, (ctx) => {
    switch (res) {
      case 'wheat': {
        const rng = new Rng(11);
        shadow(ctx, 0, 6, 20, 6, 0.18);
        for (let row = 0; row < 3; row++) {
          for (let i = 0; i < 7; i++) {
            const x = -17 + i * 5.6 + row * 1.5;
            const y = -2 + row * 6;
            ctx.strokeStyle = '#a9902f';
            ctx.lineWidth = 1.2;
            ctx.beginPath();
            ctx.moveTo(x, y + 3);
            ctx.lineTo(x + (rng.next() - 0.5) * 2, y - 8);
            ctx.stroke();
            ctx.fillStyle = '#e8c44a';
            ctx.beginPath();
            ctx.ellipse(x, y - 9, 1.7, 4.2, 0, 0, Math.PI * 2);
            ctx.fill();
          }
        }
        break;
      }
      case 'cattle':
        cow(ctx, -9, 4, 16, false);
        cow(ctx, 11, 8, 14, true);
        break;
      case 'fish': {
        ctx.strokeStyle = 'rgba(255,255,255,0.7)';
        ctx.lineWidth = 1.3;
        for (const r of [11, 16]) {
          ctx.beginPath();
          ctx.ellipse(0, 4, r, r * 0.38, 0, 0, Math.PI * 2);
          ctx.stroke();
        }
        for (const [fx, fy, d] of [[-7, 2, 1], [6, -2, -1], [3, 8, 1]] as const) {
          ctx.fillStyle = '#e8efe8';
          ctx.beginPath();
          ctx.ellipse(fx, fy, 6, 2.6, 0, 0, Math.PI * 2);
          ctx.fill();
          ctx.beginPath();
          ctx.moveTo(fx - 5.5 * d, fy);
          ctx.lineTo(fx - 9 * d, fy - 3);
          ctx.lineTo(fx - 9 * d, fy + 3);
          ctx.closePath();
          ctx.fill();
          ctx.fillStyle = '#3a6a88';
          ctx.fillRect(fx + 2.5 * d, fy - 0.8, 1.2, 1.2);
        }
        break;
      }
      case 'deer': {
        shadow(ctx, 0, 6, 12, 3.5);
        ctx.fillStyle = '#9a6a3e';
        ctx.beginPath();
        ctx.ellipse(-2, -4, 11, 5.4, 0, 0, Math.PI * 2);
        ctx.fill();
        ctx.beginPath();
        ctx.moveTo(6, -6);
        ctx.lineTo(10, -17);
        ctx.lineTo(15, -16);
        ctx.lineTo(14, -12);
        ctx.lineTo(10, -11);
        ctx.lineTo(8, -2);
        ctx.closePath();
        ctx.fill();
        ctx.fillStyle = '#6b4526';
        for (const lx of [-10, -6, 3, 6]) ctx.fillRect(lx, -1, 1.6, 8);
        ctx.strokeStyle = '#d9c8a0';
        ctx.lineWidth = 1.2;
        ctx.beginPath();
        ctx.moveTo(11, -17);
        ctx.lineTo(9, -25);
        ctx.moveTo(10, -22);
        ctx.lineTo(6, -24);
        ctx.moveTo(13, -17);
        ctx.lineTo(15, -25);
        ctx.moveTo(14, -22);
        ctx.lineTo(18, -24);
        ctx.stroke();
        break;
      }
      case 'horses':
        horse(ctx, -9, 6, 16, '#7a4a2b', '#2c1d14');
        horse(ctx, 12, 10, 13, '#b98a55', '#4a3524');
        break;
      case 'iron':
      case 'gold': {
        const isGold = res === 'gold';
        shadow(ctx, 0, 7, 19, 5, 0.3);
        const rocks: Array<[number, number, number]> = [[-9, 3, 11], [8, 5, 9], [0, -2, 13]];
        for (const [rx, ry, rs] of rocks) {
          const g = ctx.createLinearGradient(rx - rs, ry - rs, rx + rs, ry + rs);
          g.addColorStop(0, '#8f8b84');
          g.addColorStop(1, '#4f4b46');
          ctx.fillStyle = g;
          ctx.beginPath();
          ctx.moveTo(rx - rs, ry + 3);
          ctx.lineTo(rx - rs * 0.6, ry - rs * 0.7);
          ctx.lineTo(rx + rs * 0.2, ry - rs);
          ctx.lineTo(rx + rs, ry - rs * 0.2);
          ctx.lineTo(rx + rs * 0.8, ry + 3);
          ctx.closePath();
          ctx.fill();
        }
        const rng = new Rng(isGold ? 5 : 9);
        for (let i = 0; i < 7; i++) {
          const x = (rng.next() - 0.5) * 24;
          const y = -8 + rng.next() * 12;
          ctx.fillStyle = isGold ? '#ffd23a' : '#d4743a';
          ctx.beginPath();
          ctx.moveTo(x, y - 3);
          ctx.lineTo(x + 2.4, y);
          ctx.lineTo(x, y + 2.4);
          ctx.lineTo(x - 2.4, y);
          ctx.closePath();
          ctx.fill();
          if (isGold) {
            ctx.fillStyle = 'rgba(255,255,255,0.8)';
            ctx.fillRect(x - 0.6, y - 1.6, 1.2, 1.2);
          }
        }
        break;
      }
    }
  });
}

export function farmSprite(variant: number): Sprite {
  return objSprite(`farm:${variant}`, (ctx) => {
    const rng = new Rng(variant * 5 + 3);
    const cols = ['#7fae3f', '#c9b04a', '#6f9a38', '#b89a46'];
    for (let r = 0; r < 3; r++) {
      for (let q = 0; q < 3; q++) {
        const x = -19 + q * 13 + r * 4;
        const y = -9 + r * 8;
        ctx.fillStyle = cols[(q + r + variant) % 4];
        ctx.beginPath();
        ctx.moveTo(x, y);
        ctx.lineTo(x + 12, y - 2);
        ctx.lineTo(x + 14, y + 6);
        ctx.lineTo(x + 2, y + 8);
        ctx.closePath();
        ctx.fill();
        ctx.strokeStyle = 'rgba(60,45,20,0.45)';
        ctx.lineWidth = 0.9;
        for (let k = 1; k < 4; k++) {
          ctx.beginPath();
          ctx.moveTo(x + k * 3, y - 0.5 * k);
          ctx.lineTo(x + 2 + k * 3, y + 7);
          ctx.stroke();
        }
      }
    }
    // little barn
    const bx = 16 + rng.next() * 2;
    const by = 10;
    ctx.fillStyle = '#9b4a32';
    ctx.fillRect(bx - 5, by - 6, 10, 7);
    ctx.fillStyle = '#6e2f20';
    ctx.beginPath();
    ctx.moveTo(bx - 6.5, by - 6);
    ctx.lineTo(bx, by - 12);
    ctx.lineTo(bx + 6.5, by - 6);
    ctx.closePath();
    ctx.fill();
  });
}

export function mineSprite(): Sprite {
  return objSprite('mine', (ctx) => {
    shadow(ctx, 0, 8, 22, 6, 0.3);
    const g = ctx.createLinearGradient(-20, -14, 20, 8);
    g.addColorStop(0, '#9b9690');
    g.addColorStop(1, '#55514b');
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.moveTo(-22, 8);
    ctx.lineTo(-14, -12);
    ctx.lineTo(2, -17);
    ctx.lineTo(18, -8);
    ctx.lineTo(23, 8);
    ctx.closePath();
    ctx.fill();
    ctx.fillStyle = '#16120f';
    ctx.fillRect(-6, -5, 13, 13);
    ctx.fillStyle = '#7a5530';
    ctx.fillRect(-8, -7, 3, 15);
    ctx.fillRect(5, -7, 3, 15);
    ctx.fillRect(-8, -8, 16, 3);
    ctx.fillStyle = '#6c6258';
    ctx.beginPath();
    ctx.arc(15, 7, 4, 0, Math.PI * 2);
    ctx.arc(-17, 8, 3, 0, Math.PI * 2);
    ctx.fill();
  });
}

export function hutSprite(): Sprite {
  return objSprite('hut', (ctx) => {
    shadow(ctx, 0, 9, 22, 6, 0.3);
    const hut = (x: number, y: number, s: number) => {
      ctx.fillStyle = '#b08b5a';
      ctx.fillRect(x - s * 0.7, y - s * 0.7, s * 1.4, s * 0.8);
      const g = ctx.createLinearGradient(x - s, y - s * 1.6, x + s, y - s * 0.6);
      g.addColorStop(0, '#d8b45a');
      g.addColorStop(1, '#8e6a2c');
      ctx.fillStyle = g;
      ctx.beginPath();
      ctx.moveTo(x - s * 1.05, y - s * 0.62);
      ctx.lineTo(x, y - s * 1.7);
      ctx.lineTo(x + s * 1.05, y - s * 0.62);
      ctx.closePath();
      ctx.fill();
      ctx.fillStyle = '#2a1c10';
      ctx.fillRect(x - s * 0.18, y - s * 0.5, s * 0.36, s * 0.6);
    };
    hut(-9, 4, 9);
    hut(10, 6, 8);
    ctx.strokeStyle = 'rgba(210,210,210,0.5)';
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(-9, -14);
    ctx.bezierCurveTo(-13, -20, -5, -22, -9, -29);
    ctx.stroke();
  });
}

// ---------------------------------------------------------------- cities
const cityCache = new Map<string, Sprite>();

function building(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, roof: number, wall: string, roofCol: string, accent: string): void {
  shadow(ctx, x + w * 0.4, y + 1, w * 0.85, w * 0.2, 0.3);
  // side
  ctx.fillStyle = shadeCss(wall, -0.28);
  ctx.beginPath();
  ctx.moveTo(x + w / 2, y);
  ctx.lineTo(x + w / 2 + w * 0.42, y - w * 0.14);
  ctx.lineTo(x + w / 2 + w * 0.42, y - h - w * 0.14);
  ctx.lineTo(x + w / 2, y - h);
  ctx.closePath();
  ctx.fill();
  // front
  const g = ctx.createLinearGradient(x - w / 2, y - h, x + w / 2, y);
  g.addColorStop(0, shadeCss(wall, 0.12));
  g.addColorStop(1, wall);
  ctx.fillStyle = g;
  ctx.fillRect(x - w / 2, y - h, w, h);
  // roof (gable, lit left / shaded right)
  ctx.fillStyle = roofCol;
  ctx.beginPath();
  ctx.moveTo(x - w / 2 - 1.5, y - h);
  ctx.lineTo(x, y - h - roof);
  ctx.lineTo(x + w / 2 + 1.5, y - h);
  ctx.closePath();
  ctx.fill();
  ctx.fillStyle = shadeCss(roofCol, -0.3);
  ctx.beginPath();
  ctx.moveTo(x, y - h - roof);
  ctx.lineTo(x + w / 2 + w * 0.42 + 1.5, y - h - w * 0.14);
  ctx.lineTo(x + w / 2 + 1.5, y - h);
  ctx.closePath();
  ctx.fill();
  // door and window
  ctx.fillStyle = 'rgba(30,20,12,0.7)';
  ctx.fillRect(x - w * 0.1, y - h * 0.45, w * 0.2, h * 0.45);
  ctx.fillStyle = accent;
  ctx.fillRect(x - w * 0.36, y - h * 0.78, w * 0.16, h * 0.2);
}

function shadeCss(hex: string, amount: number): string {
  const n = parseInt(hex.slice(1), 16);
  const f = (c: number) => Math.max(0, Math.min(255, Math.round(amount >= 0 ? c + (255 - c) * amount : c * (1 + amount))));
  return `rgb(${f((n >> 16) & 255)},${f((n >> 8) & 255)},${f(n & 255)})`;
}

export function citySprite(tier: number, walls: boolean, capital: boolean, color: string): Sprite {
  const key = `${tier}:${walls}:${capital}:${color}`;
  const hit = cityCache.get(key);
  if (hit) return hit;
  const W = 110;
  const H = 100;
  const { c, ctx } = canvas(W * ART_RES, H * ART_RES);
  const ax = c.width / 2;
  const ay = c.height * 0.7;
  ctx.translate(ax, ay);
  ctx.scale(ART_RES, ART_RES);
  const rng = new Rng(tier * 71 + (walls ? 5 : 0) + 17);
  // trampled ground
  const gg = ctx.createRadialGradient(0, 2, 4, 0, 4, 44);
  gg.addColorStop(0, 'rgba(176,150,104,0.95)');
  gg.addColorStop(0.7, 'rgba(150,126,86,0.85)');
  gg.addColorStop(1, 'rgba(120,100,70,0)');
  ctx.fillStyle = gg;
  ctx.beginPath();
  ctx.ellipse(0, 4, 44, 26, 0, 0, Math.PI * 2);
  ctx.fill();
  const stuccos = ['#e6d9bd', '#dccdae', '#eadfc6', '#d6c5a2'];
  const roofs = ['#b5543a', '#a64a34', '#c2603f', '#8e4333'];
  const count = 3 + tier * 2;
  const spots: Array<{ x: number; y: number; w: number; h: number }> = [];
  for (let i = 0; i < count; i++) {
    const a = (i / count) * Math.PI * 2 + rng.next() * 0.6;
    const r = i === 0 ? 0 : 12 + rng.next() * 18;
    spots.push({ x: Math.cos(a) * r * 1.25, y: Math.sin(a) * r * 0.62 + 4, w: 11 + rng.next() * 6 + tier, h: 9 + rng.next() * 6 + tier * 1.5 });
  }
  spots.sort((a, b) => a.y - b.y);
  if (walls) {
    // back half of the wall
    ctx.strokeStyle = '#b9b09b';
    ctx.lineWidth = 6;
    ctx.beginPath();
    ctx.ellipse(0, 4, 40, 23, 0, Math.PI, Math.PI * 2);
    ctx.stroke();
    ctx.strokeStyle = '#8d846f';
    ctx.lineWidth = 2;
    ctx.stroke();
  }
  for (const s of spots) building(ctx, s.x, s.y, s.w, s.h, 7 + tier * 0.8, stuccos[Math.floor(rng.next() * 4)], roofs[Math.floor(rng.next() * 4)], 'rgba(60,45,30,0.7)');
  // keep / temple at the centre
  const kx = 0;
  const ky = 6;
  const kh = 16 + tier * 3;
  shadow(ctx, 6, ky + 1, 15, 4, 0.3);
  ctx.fillStyle = '#d9cfb8';
  ctx.fillRect(kx - 10, ky - kh, 20, kh);
  ctx.fillStyle = shadeCss('#d9cfb8', -0.25);
  ctx.fillRect(kx + 10, ky - kh + 2, 7, kh - 2);
  ctx.fillStyle = color;
  ctx.beginPath();
  ctx.moveTo(kx - 12, ky - kh);
  ctx.lineTo(kx, ky - kh - 11);
  ctx.lineTo(kx + 12, ky - kh);
  ctx.closePath();
  ctx.fill();
  ctx.fillStyle = 'rgba(30,20,12,0.7)';
  ctx.fillRect(kx - 3, ky - 9, 6, 9);
  // banner
  ctx.strokeStyle = '#3a2c20';
  ctx.lineWidth = 1.2;
  ctx.beginPath();
  ctx.moveTo(kx, ky - kh - 11);
  ctx.lineTo(kx, ky - kh - 25);
  ctx.stroke();
  ctx.fillStyle = color;
  ctx.beginPath();
  ctx.moveTo(kx, ky - kh - 25);
  ctx.lineTo(kx + 12, ky - kh - 21);
  ctx.lineTo(kx, ky - kh - 17);
  ctx.closePath();
  ctx.fill();
  if (capital) {
    ctx.fillStyle = '#f2d98c';
    ctx.beginPath();
    ctx.arc(kx, ky - kh - 28, 2.4, 0, Math.PI * 2);
    ctx.fill();
  }
  if (walls) {
    // front half of the wall with towers
    ctx.strokeStyle = '#c8bfa9';
    ctx.lineWidth = 6;
    ctx.beginPath();
    ctx.ellipse(0, 4, 40, 23, 0, 0, Math.PI);
    ctx.stroke();
    ctx.strokeStyle = '#8d846f';
    ctx.lineWidth = 1.6;
    ctx.stroke();
    for (const a of [0, Math.PI / 2.4, Math.PI * 0.5, Math.PI * 0.6, Math.PI]) {
      const tx = Math.cos(a) * 40;
      const ty = 4 + Math.sin(a) * 23;
      ctx.fillStyle = '#b9b09b';
      ctx.fillRect(tx - 4, ty - 12, 8, 12);
      ctx.fillStyle = '#8d846f';
      ctx.fillRect(tx - 4, ty - 12, 8, 2);
      ctx.fillStyle = color;
      ctx.fillRect(tx - 1, ty - 17, 2, 6);
    }
  }
  const s = { c, ax, ay };
  cityCache.set(key, s);
  return s;
}

// ---------------------------------------------------------------- unit figures
const unitCache = new Map<string, Sprite>();
const SKIN = '#e0b58a';
const METAL = ['#e8ebef', '#a9b0ba', '#6c7480'];

function limb(ctx: CanvasRenderingContext2D, x1: number, y1: number, x2: number, y2: number, w: number, col: string): void {
  ctx.strokeStyle = col;
  ctx.lineWidth = w;
  ctx.lineCap = 'round';
  ctx.beginPath();
  ctx.moveTo(x1, y1);
  ctx.lineTo(x2, y2);
  ctx.stroke();
}

function head(ctx: CanvasRenderingContext2D, x: number, y: number, helm: 'none' | 'cap' | 'helm' | 'hat' | 'plume' | 'turban', color: string): void {
  blob(ctx, x, y, 4.2, '#f2cfa8', SKIN, '#b98760');
  if (helm === 'cap') {
    ctx.fillStyle = '#6b4a2e';
    ctx.beginPath();
    ctx.arc(x, y - 0.8, 4.5, Math.PI, Math.PI * 2);
    ctx.fill();
  } else if (helm === 'helm' || helm === 'plume') {
    const g = ctx.createLinearGradient(x - 5, y - 5, x + 5, y + 1);
    g.addColorStop(0, METAL[0]);
    g.addColorStop(1, METAL[2]);
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.arc(x, y - 0.6, 5, Math.PI, Math.PI * 2);
    ctx.lineTo(x + 5, y + 2);
    ctx.lineTo(x - 5, y + 2);
    ctx.closePath();
    ctx.fill();
    if (helm === 'plume') {
      ctx.fillStyle = color;
      ctx.beginPath();
      ctx.moveTo(x - 1, y - 5);
      ctx.quadraticCurveTo(x + 3, y - 11, x + 8, y - 7);
      ctx.quadraticCurveTo(x + 3, y - 7, x + 1, y - 4);
      ctx.fill();
    }
  } else if (helm === 'hat') {
    ctx.fillStyle = '#7a5a34';
    ctx.beginPath();
    ctx.ellipse(x, y - 2, 7.5, 2.2, 0, 0, Math.PI * 2);
    ctx.fill();
    ctx.beginPath();
    ctx.arc(x, y - 3, 3.8, Math.PI, Math.PI * 2);
    ctx.fill();
  } else if (helm === 'turban') {
    ctx.fillStyle = '#e9e3d2';
    ctx.beginPath();
    ctx.ellipse(x, y - 2.2, 5, 3.4, 0, 0, Math.PI * 2);
    ctx.fill();
  }
}

function shield(ctx: CanvasRenderingContext2D, x: number, y: number, r: number, color: string, round: boolean): void {
  ctx.save();
  ctx.translate(x, y);
  const g = ctx.createRadialGradient(-r * 0.3, -r * 0.3, 1, 0, 0, r * 1.3);
  g.addColorStop(0, shadeCss(color, 0.3));
  g.addColorStop(1, shadeCss(color, -0.25));
  ctx.fillStyle = g;
  ctx.beginPath();
  if (round) ctx.arc(0, 0, r, 0, Math.PI * 2);
  else {
    ctx.moveTo(-r, -r);
    ctx.lineTo(r, -r);
    ctx.lineTo(r, r * 0.2);
    ctx.lineTo(0, r * 1.3);
    ctx.lineTo(-r, r * 0.2);
    ctx.closePath();
  }
  ctx.fill();
  ctx.strokeStyle = 'rgba(40,30,20,0.8)';
  ctx.lineWidth = 1;
  ctx.stroke();
  ctx.fillStyle = METAL[1];
  ctx.beginPath();
  ctx.arc(0, round ? 0 : -r * 0.1, r * 0.28, 0, Math.PI * 2);
  ctx.fill();
  ctx.restore();
}

function sword(ctx: CanvasRenderingContext2D, x: number, y: number, len: number, ang: number): void {
  ctx.save();
  ctx.translate(x, y);
  ctx.rotate(ang);
  const g = ctx.createLinearGradient(-1.5, 0, 1.5, 0);
  g.addColorStop(0, METAL[0]);
  g.addColorStop(1, METAL[2]);
  ctx.fillStyle = g;
  ctx.fillRect(-1.3, -len, 2.6, len);
  ctx.beginPath();
  ctx.moveTo(-1.3, -len);
  ctx.lineTo(0, -len - 3);
  ctx.lineTo(1.3, -len);
  ctx.fill();
  ctx.fillStyle = '#8a6a30';
  ctx.fillRect(-3.2, -0.5, 6.4, 1.8);
  ctx.fillStyle = '#4a3322';
  ctx.fillRect(-0.9, 1, 1.8, 3.4);
  ctx.restore();
}

function spear(ctx: CanvasRenderingContext2D, x: number, y: number, len: number, ang: number): void {
  ctx.save();
  ctx.translate(x, y);
  ctx.rotate(ang);
  ctx.fillStyle = '#7a5a34';
  ctx.fillRect(-0.9, -len, 1.8, len + 8);
  ctx.fillStyle = METAL[0];
  ctx.beginPath();
  ctx.moveTo(0, -len - 7);
  ctx.lineTo(2.6, -len + 1);
  ctx.lineTo(-2.6, -len + 1);
  ctx.closePath();
  ctx.fill();
  ctx.restore();
}

function bow(ctx: CanvasRenderingContext2D, x: number, y: number, size: number): void {
  ctx.strokeStyle = '#6d4a28';
  ctx.lineWidth = 1.8;
  ctx.beginPath();
  ctx.arc(x, y, size, -Math.PI * 0.45, Math.PI * 0.45);
  ctx.stroke();
  ctx.strokeStyle = 'rgba(240,235,220,0.9)';
  ctx.lineWidth = 0.8;
  ctx.beginPath();
  ctx.moveTo(x + Math.cos(-Math.PI * 0.45) * size, y + Math.sin(-Math.PI * 0.45) * size);
  ctx.lineTo(x + Math.cos(Math.PI * 0.45) * size, y + Math.sin(Math.PI * 0.45) * size);
  ctx.stroke();
}

function walker(ctx: CanvasRenderingContext2D, color: string, o: { helm: 'none' | 'cap' | 'helm' | 'hat' | 'plume' | 'turban'; armor: string; cloak?: boolean; pants?: string }): void {
  // legs
  limb(ctx, -2.6, -12, -3.4, -1.5, 3.4, o.pants ?? '#4a3f33');
  limb(ctx, 2.6, -12, 3.6, -1.5, 3.4, o.pants ?? '#4a3f33');
  ctx.fillStyle = '#2c2218';
  ctx.fillRect(-5.2, -2, 4.4, 2.4);
  ctx.fillRect(1.6, -2, 4.4, 2.4);
  // torso
  const g = ctx.createLinearGradient(-6, -26, 6, -10);
  g.addColorStop(0, shadeCss(o.armor, 0.25));
  g.addColorStop(1, shadeCss(o.armor, -0.2));
  if (o.cloak) {
    ctx.fillStyle = shadeCss(color, -0.15);
    ctx.beginPath();
    ctx.moveTo(-6, -25);
    ctx.lineTo(6, -25);
    ctx.lineTo(8, -6);
    ctx.lineTo(-8, -6);
    ctx.closePath();
    ctx.fill();
  }
  ctx.fillStyle = g;
  ctx.beginPath();
  ctx.moveTo(-5.5, -25);
  ctx.lineTo(5.5, -25);
  ctx.lineTo(6.2, -10.5);
  ctx.lineTo(-6.2, -10.5);
  ctx.closePath();
  ctx.fill();
  ctx.fillStyle = color;
  ctx.fillRect(-6.2, -13, 12.4, 2.6);
  // arms (drawn by caller after weapon), head
  head(ctx, 0, -29.5, o.helm, color);
}

function mounted(ctx: CanvasRenderingContext2D, color: string, kind: 'horse' | 'knight'): void {
  // horse
  const coat = kind === 'knight' ? '#4a3a30' : '#9a6a3e';
  shadow(ctx, 0, 0, 22, 5, 0.3);
  ctx.fillStyle = coat;
  for (const lx of [-14, -9, 8, 13]) {
    ctx.beginPath();
    ctx.moveTo(lx - 1.8, -12);
    ctx.lineTo(lx + 1.8, -12);
    ctx.lineTo(lx + 1, -1);
    ctx.lineTo(lx - 1, -1);
    ctx.closePath();
    ctx.fill();
  }
  const bg = ctx.createLinearGradient(0, -26, 0, -8);
  bg.addColorStop(0, shadeCss(coat, 0.25));
  bg.addColorStop(1, shadeCss(coat, -0.2));
  ctx.fillStyle = bg;
  ctx.beginPath();
  ctx.ellipse(0, -16, 16, 7.5, 0, 0, Math.PI * 2);
  ctx.fill();
  ctx.beginPath();
  ctx.moveTo(10, -20);
  ctx.lineTo(16, -32);
  ctx.lineTo(23, -31);
  ctx.lineTo(24, -26);
  ctx.lineTo(18, -24);
  ctx.lineTo(15, -14);
  ctx.closePath();
  ctx.fill();
  ctx.fillStyle = '#2b2018';
  ctx.beginPath();
  ctx.moveTo(14, -33);
  ctx.lineTo(18, -31);
  ctx.lineTo(13, -22);
  ctx.closePath();
  ctx.fill();
  ctx.fillRect(-17, -20, 3, 9);
  if (kind === 'knight') {
    ctx.fillStyle = color;
    ctx.beginPath();
    ctx.moveTo(-14, -21);
    ctx.lineTo(14, -21);
    ctx.lineTo(16, -9);
    ctx.lineTo(-16, -9);
    ctx.closePath();
    ctx.fill();
  } else {
    ctx.fillStyle = color;
    ctx.fillRect(-4, -22, 9, 5);
  }
  // rider
  ctx.save();
  ctx.translate(0, -12);
  ctx.scale(0.78, 0.78);
  limb(ctx, -2, -12, -5, -3, 3.2, '#4a3f33');
  limb(ctx, 2, -12, 5, -3, 3.2, '#4a3f33');
  const tg = ctx.createLinearGradient(-6, -26, 6, -10);
  tg.addColorStop(0, shadeCss(kind === 'knight' ? '#b7bdc6' : color, 0.2));
  tg.addColorStop(1, shadeCss(kind === 'knight' ? '#6c7480' : color, -0.25));
  ctx.fillStyle = tg;
  ctx.fillRect(-5.5, -25, 11, 15);
  head(ctx, 0, -29.5, kind === 'knight' ? 'plume' : 'cap', color);
  ctx.restore();
}

export function unitSprite(type: UnitTypeId, color: string): Sprite {
  const key = `${type}:${color}`;
  const hit = unitCache.get(key);
  if (hit) return hit;
  const W = 64;
  const H = 72;
  const { c, ctx } = canvas(W * ART_RES, H * ART_RES);
  const ax = c.width / 2;
  const ay = c.height - 8 * ART_RES;
  ctx.translate(ax, ay);
  ctx.scale(ART_RES, ART_RES);
  const wide = type === 'horseman' || type === 'knight' || type === 'catapult' || type === 'cannon' || type === 'settler';
  if (!wide) shadow(ctx, 0, 0, 11, 3.2, 0.35);
  switch (type) {
    case 'warrior':
      walker(ctx, color, { helm: 'cap', armor: '#8a5a3a' });
      shield(ctx, -9, -17, 6, color, true);
      limb(ctx, 5, -22, 9, -17, 3, SKIN);
      sword(ctx, 10, -18, 14, 0.5);
      break;
    case 'swordsman':
      walker(ctx, color, { helm: 'helm', armor: '#9aa3ad' });
      shield(ctx, -9, -17, 7, color, false);
      limb(ctx, 5, -22, 10, -19, 3, SKIN);
      sword(ctx, 11, -19, 17, 0.25);
      break;
    case 'spearman':
      walker(ctx, color, { helm: 'helm', armor: '#a38a5a' });
      shield(ctx, -9, -16, 6.5, color, true);
      limb(ctx, 5, -22, 9, -15, 3, SKIN);
      spear(ctx, 10, -12, 36, -0.08);
      break;
    case 'pikeman':
      walker(ctx, color, { helm: 'helm', armor: '#8e97a2' });
      limb(ctx, 5, -22, 9, -15, 3, SKIN);
      limb(ctx, -5, -22, -1, -16, 3, SKIN);
      spear(ctx, 8, -8, 46, -0.12);
      break;
    case 'musketman':
      walker(ctx, color, { helm: 'hat', armor: color, pants: '#e6e0d0' });
      limb(ctx, 5, -22, 8, -17, 3, SKIN);
      ctx.save();
      ctx.translate(2, -20);
      ctx.rotate(-0.35);
      ctx.fillStyle = '#5a3d22';
      ctx.fillRect(-4, -1, 11, 3);
      ctx.fillStyle = '#444b55';
      ctx.fillRect(7, -0.8, 16, 1.8);
      ctx.restore();
      break;
    case 'archer':
      walker(ctx, color, { helm: 'cap', armor: '#6e8a4a', cloak: false });
      bow(ctx, 11, -19, 11);
      limb(ctx, 5, -22, 10, -19, 3, SKIN);
      break;
    case 'crossbowman':
      walker(ctx, color, { helm: 'helm', armor: '#7e6a4a' });
      limb(ctx, 5, -22, 9, -19, 3, SKIN);
      ctx.fillStyle = '#5a3d22';
      ctx.fillRect(2, -21, 15, 2.4);
      ctx.strokeStyle = '#3a2c20';
      ctx.lineWidth = 1.8;
      ctx.beginPath();
      ctx.moveTo(14, -26);
      ctx.quadraticCurveTo(19, -20, 14, -14);
      ctx.stroke();
      break;
    case 'scout':
      walker(ctx, color, { helm: 'hat', armor: '#6e5a3a', cloak: true });
      limb(ctx, 5, -22, 10, -25, 3, SKIN);
      ctx.fillStyle = '#c8a24a';
      ctx.save();
      ctx.translate(10, -25);
      ctx.rotate(-0.5);
      ctx.fillRect(-1.5, -7, 3, 9);
      ctx.restore();
      break;
    case 'worker':
      walker(ctx, color, { helm: 'hat', armor: '#8a7a56', pants: '#5b4a34' });
      limb(ctx, 5, -22, 9, -14, 3, SKIN);
      ctx.save();
      ctx.translate(10, -14);
      ctx.rotate(0.35);
      ctx.fillStyle = '#7a5a34';
      ctx.fillRect(-1, -24, 2, 30);
      ctx.fillStyle = '#7d848d';
      ctx.beginPath();
      ctx.moveTo(-8, -24);
      ctx.quadraticCurveTo(0, -30, 8, -24);
      ctx.lineTo(7, -22);
      ctx.quadraticCurveTo(0, -27, -7, -22);
      ctx.fill();
      ctx.restore();
      break;
    case 'settler': {
      shadow(ctx, 0, 0, 22, 4.5, 0.35);
      // ox
      ctx.fillStyle = '#7a5538';
      ctx.beginPath();
      ctx.ellipse(-20, -9, 9, 5, 0, 0, Math.PI * 2);
      ctx.fill();
      ctx.beginPath();
      ctx.arc(-29, -11, 3.6, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillRect(-26, -7, 1.8, 7);
      ctx.fillRect(-16, -7, 1.8, 7);
      ctx.strokeStyle = '#3a2c20';
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.moveTo(-12, -9);
      ctx.lineTo(-4, -10);
      ctx.stroke();
      // wagon body + canvas cover
      ctx.fillStyle = '#8a5f36';
      ctx.fillRect(-6, -14, 24, 8);
      const cg = ctx.createLinearGradient(0, -28, 0, -12);
      cg.addColorStop(0, '#f6f0e0');
      cg.addColorStop(1, '#cfc3a4');
      ctx.fillStyle = cg;
      ctx.beginPath();
      ctx.moveTo(-5, -14);
      ctx.quadraticCurveTo(6, -34, 17, -14);
      ctx.closePath();
      ctx.fill();
      ctx.fillStyle = color;
      ctx.fillRect(-5, -16, 22, 2.4);
      for (const wx of [-1, 13]) {
        ctx.strokeStyle = '#3a2c20';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.arc(wx, -4, 4.2, 0, Math.PI * 2);
        ctx.stroke();
        ctx.lineWidth = 0.9;
        ctx.beginPath();
        ctx.moveTo(wx - 4, -4);
        ctx.lineTo(wx + 4, -4);
        ctx.moveTo(wx, -8);
        ctx.lineTo(wx, 0);
        ctx.stroke();
      }
      break;
    }
    case 'horseman':
      mounted(ctx, color, 'horse');
      spear(ctx, 12, -17, 32, 0.15);
      break;
    case 'knight':
      mounted(ctx, color, 'knight');
      spear(ctx, 14, -16, 38, 0.2);
      break;
    case 'catapult':
    case 'cannon': {
      shadow(ctx, 0, 0, 22, 4.5, 0.35);
      ctx.fillStyle = '#7a5530';
      ctx.fillRect(-16, -9, 32, 5);
      for (const wx of [-11, 11]) {
        ctx.strokeStyle = '#3a2c20';
        ctx.lineWidth = 2.4;
        ctx.beginPath();
        ctx.arc(wx, -5, 5.5, 0, Math.PI * 2);
        ctx.stroke();
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(wx - 5.5, -5);
        ctx.lineTo(wx + 5.5, -5);
        ctx.moveTo(wx, -10.5);
        ctx.lineTo(wx, 0.5);
        ctx.stroke();
      }
      if (type === 'catapult') {
        ctx.fillStyle = '#8a5f36';
        ctx.fillRect(-9, -22, 3, 14);
        ctx.fillRect(6, -22, 3, 14);
        ctx.save();
        ctx.translate(0, -17);
        ctx.rotate(-0.6);
        ctx.fillStyle = '#6d4a28';
        ctx.fillRect(-2, -20, 4, 26);
        ctx.fillStyle = '#5b5750';
        ctx.beginPath();
        ctx.arc(0, -22, 4.5, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
      } else {
        const bg = ctx.createLinearGradient(0, -22, 0, -10);
        bg.addColorStop(0, '#7b828c');
        bg.addColorStop(1, '#2e3238');
        ctx.fillStyle = bg;
        ctx.save();
        ctx.translate(-2, -14);
        ctx.rotate(-0.25);
        ctx.fillRect(-12, -5, 30, 9);
        ctx.fillStyle = '#23262b';
        ctx.fillRect(15, -6, 5, 11);
        ctx.restore();
      }
      ctx.fillStyle = color;
      ctx.fillRect(-16, -11, 32, 2);
      break;
    }
  }
  const s = { c, ax, ay };
  unitCache.set(key, s);
  return s;
}

// ---------------------------------------------------------------- atmosphere
let cloudTile: HTMLCanvasElement | null = null;
/** A tileable layer of soft cloud shadows drifted across the map for atmosphere. */
export function cloudShadow(): HTMLCanvasElement {
  if (cloudTile) return cloudTile;
  const S = 256;
  const { c, ctx } = canvas(S, S);
  const img = ctx.createImageData(S, S);
  for (let y = 0; y < S; y++) {
    for (let x = 0; x < S; x++) {
      // tileable noise: blend four offset samples
      const u = x / S;
      const v = y / S;
      const f = (xx: number, yy: number) => fbm(xx * 4, yy * 4, 91, 4);
      const n = f(u, v) * (1 - u) * (1 - v) + f(u - 1, v) * u * (1 - v) + f(u, v - 1) * (1 - u) * v + f(u - 1, v - 1) * u * v;
      const a = Math.max(0, n - 0.5) * 2.4;
      const i = (y * S + x) * 4;
      img.data[i] = 8;
      img.data[i + 1] = 14;
      img.data[i + 2] = 28;
      img.data[i + 3] = Math.min(255, a * 255);
    }
  }
  ctx.putImageData(img, 0, 0);
  cloudTile = c;
  return c;
}

const dimCache = new WeakMap<HTMLCanvasElement, HTMLCanvasElement>();
/** A cooled, darkened copy of a sprite, used for tiles that are explored but currently out of sight. */
export function dimmed(s: Sprite): Sprite {
  let c = dimCache.get(s.c);
  if (!c) {
    const o = canvas(s.c.width, s.c.height);
    o.ctx.drawImage(s.c, 0, 0);
    o.ctx.globalCompositeOperation = 'source-atop';
    o.ctx.fillStyle = 'rgba(10,16,32,0.5)';
    o.ctx.fillRect(0, 0, o.c.width, o.c.height);
    c = o.c;
    dimCache.set(s.c, c);
  }
  return { c, ax: s.ax, ay: s.ay };
}

/** Stamp a sprite so its anchor lands on (x, y); `z` is the camera zoom, `k` an extra scale. */
export function stamp(ctx: CanvasRenderingContext2D, s: Sprite, x: number, y: number, z: number, k = 1): void {
  const f = (z * k) / ART_RES;
  ctx.drawImage(s.c, x - s.ax * f, y - s.ay * f, s.c.width * f, s.c.height * f);
}

// ---------------------------------------------------------------- leader portraits
interface Look {
  skin: [string, string, string];
  hair: string;
  beard: 'none' | 'short' | 'long' | 'curled';
  robe: string;
  trim: string;
  head: 'wrap' | 'leaf' | 'nemes' | 'fillet' | 'laurel' | 'crown' | 'jade' | 'helm';
  female?: boolean;
  bg: string;
}

const LOOKS: Record<string, Look> = {
  jericho: { skin: ['#d9a77a', '#b97f54', '#8a5a38'], hair: '#2a1d16', beard: 'short', robe: '#c9892a', trim: '#f0d58a', head: 'wrap', bg: '#e0a526' },
  sumer: { skin: ['#e1b186', '#c18a5c', '#8e5f3c'], hair: '#1c1512', beard: 'none', robe: '#2f6fc0', trim: '#f2c94c', head: 'leaf', female: true, bg: '#3b8fe0' },
  egypt: { skin: ['#d7a26f', '#b5784a', '#83532f'], hair: '#14110f', beard: 'none', robe: '#f1ead8', trim: '#d9a53a', head: 'nemes', female: true, bg: '#d9642e' },
  indus: { skin: ['#c98d5c', '#a56a3c', '#744626'], hair: '#171210', beard: 'long', robe: '#d8d0bd', trim: '#46b36b', head: 'fillet', bg: '#46b36b' },
  rome: { skin: ['#e6bb94', '#c79068', '#946446'], hair: '#3a2a1e', beard: 'none', robe: '#efe9dc', trim: '#8a1f3a', head: 'laurel', bg: '#b8323f' },
  persia: { skin: ['#d8a678', '#b67e52', '#85583a'], hair: '#18120f', beard: 'curled', robe: '#6f3fa8', trim: '#f0c860', head: 'crown', bg: '#9d5bd6' },
  maya: { skin: ['#c58a5a', '#a2693f', '#704425'], hair: '#0f0c0b', beard: 'none', robe: '#1e8c82', trim: '#e3c15a', head: 'jade', bg: '#1fa89b' },
  greece: { skin: ['#e3b58b', '#c48b62', '#8f5e3f'], hair: '#7d7468', beard: 'short', robe: '#f3f1ea', trim: '#3d6fb0', head: 'helm', bg: '#8da0c8' },
};

const portraitCache = new Map<string, string>();
/** A painted bust of a civilisation's leader, as a data URL (square, `px` pixels). */
export function portraitUrl(civ: string, px = 192): string {
  const key = `${civ}:${px}`;
  const hit = portraitCache.get(key);
  if (hit) return hit;
  const L = LOOKS[civ] ?? LOOKS.jericho;
  const { c, ctx } = canvas(px, px);
  ctx.scale(px / 100, px / 100);
  // backdrop
  let g = ctx.createRadialGradient(50, 36, 6, 50, 50, 78);
  g.addColorStop(0, shadeCss(L.bg, 0.35));
  g.addColorStop(0.55, shadeCss(L.bg, -0.3));
  g.addColorStop(1, shadeCss(L.bg, -0.78));
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, 100, 100);
  // shoulders / garment
  g = ctx.createLinearGradient(20, 70, 80, 100);
  g.addColorStop(0, shadeCss(L.robe, 0.12));
  g.addColorStop(1, shadeCss(L.robe, -0.35));
  ctx.fillStyle = g;
  ctx.beginPath();
  ctx.moveTo(6, 102);
  ctx.quadraticCurveTo(10, 74, 36, 70);
  ctx.lineTo(64, 70);
  ctx.quadraticCurveTo(90, 74, 94, 102);
  ctx.closePath();
  ctx.fill();
  // collar trim
  ctx.strokeStyle = L.trim;
  ctx.lineWidth = 3;
  ctx.beginPath();
  ctx.moveTo(34, 71);
  ctx.quadraticCurveTo(50, 90, 66, 71);
  ctx.stroke();
  ctx.fillStyle = L.trim;
  for (let i = 0; i < 5; i++) {
    const t = i / 4;
    ctx.beginPath();
    ctx.arc(37 + t * 26, 76 + Math.sin(t * Math.PI) * 9, 1.8, 0, Math.PI * 2);
    ctx.fill();
  }
  // neck
  g = ctx.createLinearGradient(40, 0, 60, 0);
  g.addColorStop(0, L.skin[1]);
  g.addColorStop(1, L.skin[2]);
  ctx.fillStyle = g;
  ctx.beginPath();
  ctx.moveTo(41, 58);
  ctx.lineTo(59, 58);
  ctx.lineTo(61, 74);
  ctx.quadraticCurveTo(50, 80, 39, 74);
  ctx.closePath();
  ctx.fill();
  // hair behind head
  if (L.female || L.head === 'nemes' || L.head === 'jade') {
    ctx.fillStyle = L.hair;
    ctx.beginPath();
    ctx.moveTo(29, 36);
    ctx.quadraticCurveTo(24, 70, 33, 78);
    ctx.lineTo(67, 78);
    ctx.quadraticCurveTo(76, 70, 71, 36);
    ctx.closePath();
    ctx.fill();
  }
  // head
  g = ctx.createRadialGradient(44, 38, 4, 50, 44, 28);
  g.addColorStop(0, L.skin[0]);
  g.addColorStop(0.6, L.skin[1]);
  g.addColorStop(1, L.skin[2]);
  ctx.fillStyle = g;
  ctx.beginPath();
  ctx.moveTo(50, 22);
  ctx.bezierCurveTo(67, 22, 69, 42, 63, 54);
  ctx.quadraticCurveTo(57, 65, 50, 65);
  ctx.quadraticCurveTo(43, 65, 37, 54);
  ctx.bezierCurveTo(31, 42, 33, 22, 50, 22);
  ctx.fill();
  // ears
  ctx.fillStyle = L.skin[1];
  ctx.beginPath();
  ctx.ellipse(32.5, 45, 2.6, 4.6, 0, 0, Math.PI * 2);
  ctx.ellipse(67.5, 45, 2.6, 4.6, 0, 0, Math.PI * 2);
  ctx.fill();
  // beard
  if (L.beard !== 'none') {
    ctx.fillStyle = L.head === 'helm' ? '#8d8579' : L.hair;
    ctx.beginPath();
    ctx.moveTo(35, 48);
    ctx.quadraticCurveTo(36, 62, 42, 66);
    const drop = L.beard === 'long' ? 82 : L.beard === 'curled' ? 76 : 70;
    ctx.quadraticCurveTo(50, drop, 58, 66);
    ctx.quadraticCurveTo(64, 62, 65, 48);
    ctx.quadraticCurveTo(60, 56, 50, 57);
    ctx.quadraticCurveTo(40, 56, 35, 48);
    ctx.fill();
    if (L.beard === 'curled') {
      ctx.strokeStyle = 'rgba(255,255,255,0.12)';
      ctx.lineWidth = 0.9;
      for (let i = 0; i < 4; i++) {
        ctx.beginPath();
        ctx.arc(42 + i * 5, 68, 2.2, 0, Math.PI * 1.6);
        ctx.stroke();
      }
    }
  }
  // face detail: brows, eyes, nose, mouth
  const kohl = L.head === 'nemes' || L.female;
  ctx.strokeStyle = L.hair;
  ctx.lineWidth = 1.7;
  ctx.lineCap = 'round';
  ctx.beginPath();
  ctx.moveTo(40, 38);
  ctx.quadraticCurveTo(44, 36, 47.5, 38);
  ctx.moveTo(52.5, 38);
  ctx.quadraticCurveTo(56, 36, 60, 38);
  ctx.stroke();
  for (const ex of [43.8, 56.2]) {
    ctx.fillStyle = '#f4efe6';
    ctx.beginPath();
    ctx.ellipse(ex, 42, 3.1, 1.9, 0, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = '#2b1d14';
    ctx.beginPath();
    ctx.arc(ex, 42, 1.5, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = 'rgba(255,255,255,0.85)';
    ctx.fillRect(ex - 0.2, 41.2, 0.8, 0.8);
    ctx.strokeStyle = kohl ? '#14100d' : 'rgba(40,25,15,0.55)';
    ctx.lineWidth = kohl ? 1.3 : 0.9;
    ctx.beginPath();
    ctx.ellipse(ex, 42, 3.4, 2.1, 0, Math.PI, Math.PI * 2);
    ctx.stroke();
    if (kohl) {
      ctx.beginPath();
      ctx.moveTo(ex + (ex < 50 ? -3.2 : 3.2), 42);
      ctx.lineTo(ex + (ex < 50 ? -6 : 6), 41);
      ctx.stroke();
    }
  }
  ctx.strokeStyle = 'rgba(70,40,20,0.5)';
  ctx.lineWidth = 1.1;
  ctx.beginPath();
  ctx.moveTo(50, 42);
  ctx.quadraticCurveTo(52.5, 49, 50.5, 51.5);
  ctx.quadraticCurveTo(48.5, 52.6, 47.5, 51.4);
  ctx.stroke();
  ctx.strokeStyle = L.female ? '#9b3a3a' : 'rgba(90,40,30,0.8)';
  ctx.lineWidth = L.female ? 2 : 1.4;
  ctx.beginPath();
  ctx.moveTo(45.5, 57.5);
  ctx.quadraticCurveTo(50, 59.4, 54.5, 57.5);
  ctx.stroke();
  // headgear
  const gold = (x0: number, y0: number, x1: number, y1: number) => {
    const gg = ctx.createLinearGradient(x0, y0, x1, y1);
    gg.addColorStop(0, '#fff0b0');
    gg.addColorStop(0.5, '#e2b64a');
    gg.addColorStop(1, '#8d6a1e');
    return gg;
  };
  switch (L.head) {
    case 'wrap':
      ctx.fillStyle = '#e8dfc9';
      ctx.beginPath();
      ctx.moveTo(30, 36);
      ctx.bezierCurveTo(30, 14, 70, 14, 70, 36);
      ctx.quadraticCurveTo(50, 28, 30, 36);
      ctx.fill();
      ctx.strokeStyle = 'rgba(120,100,70,0.45)';
      ctx.lineWidth = 0.8;
      for (let i = 0; i < 4; i++) {
        ctx.beginPath();
        ctx.moveTo(32 + i * 1.5, 31 - i * 3.6);
        ctx.quadraticCurveTo(50, 26 - i * 4, 68 - i * 1.5, 31 - i * 3.6);
        ctx.stroke();
      }
      ctx.fillStyle = '#c9892a';
      ctx.fillRect(30.5, 33, 39, 2.4);
      break;
    case 'leaf':
      ctx.fillStyle = L.hair;
      ctx.beginPath();
      ctx.moveTo(31, 40);
      ctx.bezierCurveTo(28, 12, 72, 12, 69, 40);
      ctx.quadraticCurveTo(50, 28, 31, 40);
      ctx.fill();
      ctx.fillStyle = gold(30, 20, 70, 34);
      for (let i = 0; i < 9; i++) {
        const t = i / 8;
        const px2 = 31 + t * 38;
        const py2 = 31 - Math.sin(t * Math.PI) * 5;
        ctx.save();
        ctx.translate(px2, py2);
        ctx.rotate((t - 0.5) * 1.2);
        ctx.beginPath();
        ctx.ellipse(0, -3.5, 1.8, 4.6, 0, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
      }
      ctx.fillStyle = gold(34, 28, 66, 28);
      ctx.fillRect(31, 33, 38, 2.3);
      ctx.fillStyle = gold(0, 0, 8, 8);
      ctx.beginPath();
      ctx.arc(32.5, 52, 2.4, 0, Math.PI * 2);
      ctx.arc(67.5, 52, 2.4, 0, Math.PI * 2);
      ctx.fill();
      break;
    case 'nemes':
      for (let i = 0; i < 9; i++) {
        ctx.fillStyle = i % 2 ? '#d9a53a' : '#2f63b8';
        ctx.beginPath();
        ctx.moveTo(30 + i * 0.4, 36 + i * 4.6);
        ctx.lineTo(30 + i * 0.4 + 6, 36 + i * 4.6);
        ctx.lineTo(30 + i * 0.4 + 6, 41 + i * 4.6);
        ctx.lineTo(30 + i * 0.4, 41 + i * 4.6);
        ctx.fill();
        ctx.beginPath();
        ctx.moveTo(70 - i * 0.4, 36 + i * 4.6);
        ctx.lineTo(70 - i * 0.4 - 6, 36 + i * 4.6);
        ctx.lineTo(70 - i * 0.4 - 6, 41 + i * 4.6);
        ctx.lineTo(70 - i * 0.4, 41 + i * 4.6);
        ctx.fill();
      }
      ctx.fillStyle = '#2f63b8';
      ctx.beginPath();
      ctx.moveTo(28, 40);
      ctx.bezierCurveTo(26, 8, 74, 8, 72, 40);
      ctx.lineTo(67, 38);
      ctx.quadraticCurveTo(50, 22, 33, 38);
      ctx.closePath();
      ctx.fill();
      ctx.strokeStyle = '#d9a53a';
      ctx.lineWidth = 1.6;
      for (let i = 0; i < 5; i++) {
        ctx.beginPath();
        ctx.moveTo(30 + i * 2, 33 - i * 3);
        ctx.quadraticCurveTo(50, 20 - i * 3, 70 - i * 2, 33 - i * 3);
        ctx.stroke();
      }
      ctx.fillStyle = gold(46, 14, 54, 30);
      ctx.beginPath();
      ctx.moveTo(50, 17);
      ctx.bezierCurveTo(46, 22, 52, 27, 50, 31);
      ctx.bezierCurveTo(54, 27, 54, 21, 50, 17);
      ctx.fill();
      break;
    case 'fillet':
      ctx.fillStyle = L.hair;
      ctx.beginPath();
      ctx.moveTo(30, 40);
      ctx.bezierCurveTo(26, 8, 74, 8, 70, 40);
      ctx.quadraticCurveTo(50, 26, 30, 40);
      ctx.fill();
      ctx.fillStyle = gold(30, 30, 70, 34);
      ctx.fillRect(30, 29, 40, 3);
      ctx.fillStyle = '#46b36b';
      ctx.beginPath();
      ctx.arc(50, 30.5, 3.2, 0, Math.PI * 2);
      ctx.fill();
      break;
    case 'laurel':
      ctx.fillStyle = L.hair;
      ctx.beginPath();
      ctx.moveTo(30, 40);
      ctx.bezierCurveTo(28, 14, 72, 14, 70, 40);
      ctx.quadraticCurveTo(50, 26, 30, 40);
      ctx.fill();
      for (const sd of [-1, 1]) {
        for (let i = 0; i < 7; i++) {
          const t = i / 6;
          const lx = 50 + sd * (8 + t * 17);
          const ly = 27 + Math.pow(t, 1.4) * 14;
          ctx.save();
          ctx.translate(lx, ly);
          ctx.rotate(sd * (0.9 - t * 0.9));
          ctx.fillStyle = i % 2 ? '#6c9a3a' : '#8bb848';
          ctx.beginPath();
          ctx.ellipse(0, -2.4, 1.9, 4.6, 0, 0, Math.PI * 2);
          ctx.fill();
          ctx.restore();
        }
      }
      break;
    case 'crown':
      ctx.fillStyle = L.hair;
      ctx.beginPath();
      ctx.moveTo(30, 40);
      ctx.bezierCurveTo(28, 18, 72, 18, 70, 40);
      ctx.quadraticCurveTo(50, 30, 30, 40);
      ctx.fill();
      ctx.fillStyle = gold(32, 6, 68, 32);
      ctx.beginPath();
      ctx.moveTo(32, 32);
      ctx.lineTo(31, 17);
      ctx.lineTo(39, 24);
      ctx.lineTo(44, 9);
      ctx.lineTo(50, 21);
      ctx.lineTo(56, 9);
      ctx.lineTo(61, 24);
      ctx.lineTo(69, 17);
      ctx.lineTo(68, 32);
      ctx.closePath();
      ctx.fill();
      ctx.fillStyle = '#b02a4a';
      for (const jx of [38, 50, 62]) {
        ctx.beginPath();
        ctx.arc(jx, 28, 1.9, 0, Math.PI * 2);
        ctx.fill();
      }
      break;
    case 'jade':
      ctx.fillStyle = '#26a37f';
      ctx.beginPath();
      ctx.moveTo(29, 38);
      ctx.bezierCurveTo(26, 6, 74, 6, 71, 38);
      ctx.lineTo(66, 34);
      ctx.quadraticCurveTo(50, 22, 34, 34);
      ctx.closePath();
      ctx.fill();
      for (let i = 0; i < 6; i++) {
        const t = i / 5;
        ctx.save();
        ctx.translate(50, 14);
        ctx.rotate((t - 0.5) * 1.8);
        const fg = ctx.createLinearGradient(0, 0, 0, -22);
        fg.addColorStop(0, '#1d8a5c');
        fg.addColorStop(1, '#8ad87a');
        ctx.fillStyle = fg;
        ctx.beginPath();
        ctx.ellipse(0, -10, 2.6, 11, 0, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
      }
      ctx.fillStyle = gold(36, 20, 64, 34);
      ctx.fillRect(31, 31, 38, 3);
      ctx.fillStyle = '#7ee0b0';
      ctx.beginPath();
      ctx.arc(50, 25, 3.4, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = '#7ee0b0';
      ctx.beginPath();
      ctx.arc(32.5, 55, 2.6, 0, Math.PI * 2);
      ctx.arc(67.5, 55, 2.6, 0, Math.PI * 2);
      ctx.fill();
      break;
    case 'helm': {
      ctx.fillStyle = L.hair;
      ctx.beginPath();
      ctx.moveTo(30, 40);
      ctx.bezierCurveTo(28, 18, 72, 18, 70, 40);
      ctx.quadraticCurveTo(50, 30, 30, 40);
      ctx.fill();
      const hg = ctx.createLinearGradient(30, 6, 70, 34);
      hg.addColorStop(0, '#f0c97a');
      hg.addColorStop(0.5, '#b8862e');
      hg.addColorStop(1, '#6e4e16');
      ctx.fillStyle = hg;
      ctx.beginPath();
      ctx.moveTo(29, 32);
      ctx.bezierCurveTo(26, 4, 74, 4, 71, 32);
      ctx.lineTo(66, 30);
      ctx.quadraticCurveTo(50, 20, 34, 30);
      ctx.closePath();
      ctx.fill();
      ctx.fillStyle = '#8b2a2a';
      ctx.beginPath();
      ctx.moveTo(44, 10);
      ctx.quadraticCurveTo(50, -2, 56, 10);
      ctx.quadraticCurveTo(58, 16, 50, 14);
      ctx.quadraticCurveTo(42, 16, 44, 10);
      ctx.fill();
      break;
    }
  }
  // soft lighting from the upper left
  g = ctx.createLinearGradient(0, 0, 100, 100);
  g.addColorStop(0, 'rgba(255,240,210,0.18)');
  g.addColorStop(0.5, 'rgba(0,0,0,0)');
  g.addColorStop(1, 'rgba(0,0,0,0.3)');
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, 100, 100);
  const url = c.toDataURL('image/png');
  portraitCache.set(key, url);
  return url;
}
