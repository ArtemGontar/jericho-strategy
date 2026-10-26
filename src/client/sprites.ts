import { Rng } from '../shared/rng';
import type { TerrainId } from '../shared/types';

export const HEX = 48; // centre-to-corner in world units
export const HEX_W = Math.sqrt(3) * HEX;
export const HEX_H = 2 * HEX;
const RES = 2; // sprite resolution multiplier

export function hexPath(ctx: CanvasRenderingContext2D, cx: number, cy: number, r: number): void {
  ctx.beginPath();
  for (let i = 0; i < 6; i++) {
    const a = ((60 * i - 30) * Math.PI) / 180;
    const x = cx + r * Math.cos(a);
    const y = cy + r * Math.sin(a);
    if (i === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  }
  ctx.closePath();
}

const BASE: Record<TerrainId, [string, string]> = {
  ocean: ['#1d4a78', '#153a63'],
  coast: ['#3a8bb0', '#2c7398'],
  grass: ['#68a850', '#559a42'],
  plains: ['#bdb862', '#aaa552'],
  desert: ['#e0c987', '#d2b66d'],
  tundra: ['#c9d3d3', '#b3bfbf'],
  forest: ['#4a8745', '#3c7439'],
  hills: ['#9aa35a', '#838c47'],
  mountain: ['#8d8a86', '#74726f'],
};

function makeCanvas(): { c: HTMLCanvasElement; ctx: CanvasRenderingContext2D } {
  const c = document.createElement('canvas');
  c.width = Math.ceil(HEX_W * RES) + 4;
  c.height = Math.ceil(HEX_H * RES) + 4;
  const ctx = c.getContext('2d')!;
  ctx.scale(RES, RES);
  ctx.translate(2 / RES + HEX_W / 2, 2 / RES + HEX_H / 2);
  return { c, ctx };
}

function tree(ctx: CanvasRenderingContext2D, x: number, y: number, s: number, dark: string, light: string): void {
  ctx.fillStyle = '#4a3320';
  ctx.fillRect(x - s * 0.06, y + s * 0.2, s * 0.12, s * 0.22);
  for (let i = 0; i < 3; i++) {
    const w = s * (0.5 - i * 0.12);
    const yy = y + s * 0.25 - i * s * 0.3;
    ctx.fillStyle = i === 1 ? light : dark;
    ctx.beginPath();
    ctx.moveTo(x - w, yy);
    ctx.lineTo(x + w, yy);
    ctx.lineTo(x, yy - s * 0.42);
    ctx.closePath();
    ctx.fill();
  }
}

function mound(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, c1: string, c2: string): void {
  const g = ctx.createLinearGradient(x - w, y - h, x + w, y + h * 0.3);
  g.addColorStop(0, c1);
  g.addColorStop(1, c2);
  ctx.fillStyle = g;
  ctx.beginPath();
  ctx.moveTo(x - w, y);
  ctx.quadraticCurveTo(x - w * 0.5, y - h * 1.5, x, y - h);
  ctx.quadraticCurveTo(x + w * 0.5, y - h * 1.5, x + w, y);
  ctx.closePath();
  ctx.fill();
  ctx.strokeStyle = 'rgba(0,0,0,0.18)';
  ctx.lineWidth = 0.8;
  ctx.stroke();
}

function peak(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number): void {
  ctx.fillStyle = '#6b6864';
  ctx.beginPath();
  ctx.moveTo(x - w, y);
  ctx.lineTo(x, y - h);
  ctx.lineTo(x + w, y);
  ctx.closePath();
  ctx.fill();
  ctx.fillStyle = '#4f4c49';
  ctx.beginPath();
  ctx.moveTo(x, y - h);
  ctx.lineTo(x + w, y);
  ctx.lineTo(x + w * 0.1, y);
  ctx.closePath();
  ctx.fill();
  ctx.fillStyle = '#f4f6f8';
  ctx.beginPath();
  ctx.moveTo(x, y - h);
  ctx.lineTo(x - w * 0.34, y - h * 0.62);
  ctx.lineTo(x - w * 0.1, y - h * 0.68);
  ctx.lineTo(x + w * 0.08, y - h * 0.55);
  ctx.lineTo(x + w * 0.3, y - h * 0.62);
  ctx.closePath();
  ctx.fill();
}

function paintTerrain(terrain: TerrainId, variant: number): HTMLCanvasElement {
  const { c, ctx } = makeCanvas();
  const rng = new Rng(variant * 7919 + terrain.length * 131 + terrain.charCodeAt(0));
  const [c1, c2] = BASE[terrain];
  // clip to hex slightly oversized so neighbours overlap without seams
  hexPath(ctx, 0, 0, HEX * 1.015);
  ctx.save();
  ctx.clip();
  const g = ctx.createRadialGradient(-HEX * 0.2, -HEX * 0.3, 2, 0, 0, HEX * 1.1);
  g.addColorStop(0, c1);
  g.addColorStop(1, c2);
  ctx.fillStyle = g;
  ctx.fillRect(-HEX, -HEX, HEX * 2, HEX * 2);
  // grain
  for (let i = 0; i < 110; i++) {
    const x = (rng.next() - 0.5) * HEX_W;
    const y = (rng.next() - 0.5) * HEX_H;
    ctx.fillStyle = rng.chance(0.5) ? 'rgba(255,255,255,0.05)' : 'rgba(0,0,0,0.05)';
    ctx.fillRect(x, y, 1.4 + rng.next() * 1.6, 1.4 + rng.next() * 1.6);
  }
  const spot = () => ({ x: (rng.next() - 0.5) * HEX_W * 0.72, y: (rng.next() - 0.5) * HEX * 1.2 });
  switch (terrain) {
    case 'ocean':
    case 'coast': {
      ctx.strokeStyle = terrain === 'ocean' ? 'rgba(160,200,240,0.16)' : 'rgba(255,255,255,0.22)';
      ctx.lineWidth = 1;
      for (let i = 0; i < 6; i++) {
        const { x, y } = spot();
        ctx.beginPath();
        ctx.moveTo(x - 6, y);
        ctx.quadraticCurveTo(x - 3, y - 3, x, y);
        ctx.quadraticCurveTo(x + 3, y + 3, x + 6, y);
        ctx.stroke();
      }
      break;
    }
    case 'grass':
      ctx.strokeStyle = 'rgba(30,80,30,0.35)';
      ctx.lineWidth = 1;
      for (let i = 0; i < 14; i++) {
        const { x, y } = spot();
        ctx.beginPath();
        ctx.moveTo(x - 2, y);
        ctx.lineTo(x, y - 4);
        ctx.lineTo(x + 2, y);
        ctx.stroke();
      }
      break;
    case 'plains':
      ctx.strokeStyle = 'rgba(110,100,40,0.35)';
      ctx.lineWidth = 1;
      for (let i = 0; i < 16; i++) {
        const { x, y } = spot();
        ctx.beginPath();
        ctx.moveTo(x, y);
        ctx.lineTo(x + 1, y - 5);
        ctx.moveTo(x + 3, y);
        ctx.lineTo(x + 3, y - 4);
        ctx.stroke();
      }
      break;
    case 'desert':
      ctx.strokeStyle = 'rgba(150,110,50,0.35)';
      ctx.lineWidth = 1.2;
      for (let i = 0; i < 5; i++) {
        const { x, y } = spot();
        ctx.beginPath();
        ctx.arc(x, y + 6, 9 + rng.next() * 6, Math.PI * 1.15, Math.PI * 1.85);
        ctx.stroke();
      }
      break;
    case 'tundra':
      for (let i = 0; i < 30; i++) {
        const { x, y } = spot();
        ctx.fillStyle = 'rgba(255,255,255,0.5)';
        ctx.fillRect(x, y, 2, 1.5);
      }
      break;
    case 'forest': {
      const trees = [] as Array<{ x: number; y: number; s: number }>;
      for (let i = 0; i < 7; i++) {
        const { x, y } = spot();
        trees.push({ x, y, s: 11 + rng.next() * 7 });
      }
      trees.sort((a, b) => a.y - b.y);
      for (const t of trees) tree(ctx, t.x, t.y, t.s, '#2b5e2e', '#3c7f3c');
      break;
    }
    case 'hills': {
      const ms = [{ x: -10, y: 8 }, { x: 11, y: 10 }, { x: 0, y: 2 }].map((p) => ({ x: p.x + (rng.next() - 0.5) * 6, y: p.y + (rng.next() - 0.5) * 4 }));
      for (const m of ms) mound(ctx, m.x, m.y, 15 + rng.next() * 4, 11 + rng.next() * 4, '#b4b06a', '#7a7a42');
      break;
    }
    case 'mountain': {
      peak(ctx, -12, 10, 15, 24 + rng.next() * 4);
      peak(ctx, 12, 12, 14, 20 + rng.next() * 4);
      peak(ctx, 0, 6, 18, 31 + rng.next() * 5);
      break;
    }
  }
  ctx.restore();
  // soft edge
  hexPath(ctx, 0, 0, HEX);
  ctx.strokeStyle = 'rgba(0,0,0,0.16)';
  ctx.lineWidth = 1;
  ctx.stroke();
  return c;
}

const cache = new Map<string, HTMLCanvasElement>();
export function terrainSprite(terrain: TerrainId, variant: number): HTMLCanvasElement {
  const key = `${terrain}:${variant}`;
  let c = cache.get(key);
  if (!c) {
    c = paintTerrain(terrain, variant);
    cache.set(key, c);
  }
  return c;
}
export const SPRITE_RES = RES;
export const VARIANTS = 3;
