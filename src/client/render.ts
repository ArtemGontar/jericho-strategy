import { hexToPixel, pixelToHex, type Pos } from '../shared/hex';
import { CIV_BY_ID, IMPROVEMENT_YIELD, RESOURCES, TERRAIN, UNITS } from '../shared/data';
import { RESOURCE_LIST, TERRAIN_LIST, type GameView, type ViewCity, type ViewUnit } from '../shared/view';
import type { ViewMap } from '../shared/viewpath';
import type { Animator } from './anim';
import { HEX, HEX_W, hexPath, proj } from './sprites';
import { cloudShadow, citySprite, decorSprite, dimmed, farmSprite, groundSprite, hutSprite, mineSprite, resourceSprite, stamp, unitSprite, type Sprite } from './art';
import { drawIcon } from './icons';

export const DISPLAY_FONT = "'Cinzel','Palatino Linotype','Book Antiqua',Georgia,serif";

export function civColor(view: GameView, owner: number): string {
  const p = view.players[owner];
  return p ? CIV_BY_ID[p.civ].color : '#888';
}

export class Camera {
  x = 0;
  y = 0;
  zoom = 1;
  w = 800;
  h = 600;
  worldW = 0;
  worldH = 0;
  /** ground foreshortening (sin of the pitch) and how far a unit of height lifts a point up the screen (cos of the pitch) */
  tilt = 1;
  lift = 0;
  setPitch(deg: number): void {
    const r = (deg * Math.PI) / 180;
    this.tilt = Math.sin(r);
    this.lift = Math.cos(r);
    proj.tilt = this.tilt;
  }

  setWorld(cols: number, rows: number): void {
    this.worldW = HEX_W * (cols + 0.5);
    this.worldH = HEX * 1.5 * rows + HEX * 0.5;
  }
  worldOfTile(x: number, y: number): { x: number; y: number } {
    const p = hexToPixel(x, y, HEX);
    return { x: p.px + HEX_W / 2, y: p.py + HEX };
  }
  /** Screen position of a world point; `hgt` is its height above the ground in world units. */
  toScreen(wx: number, wy: number, hgt = 0): { x: number; y: number } {
    return { x: (wx - this.x) * this.zoom + this.w / 2, y: (wy - this.y) * this.zoom * this.tilt - hgt * this.zoom * this.lift + this.h / 2 };
  }
  /** Ground point under a screen position. */
  toWorld(sx: number, sy: number): { x: number; y: number } {
    return { x: (sx - this.w / 2) / this.zoom + this.x, y: (sy - this.h / 2) / (this.zoom * this.tilt) + this.y };
  }
  tileAt(sx: number, sy: number, cols: number, rows: number): Pos | null {
    const w = this.toWorld(sx, sy);
    return pixelToHex(w.x - HEX_W / 2, w.y - HEX, HEX, cols, rows);
  }
  clamp(): void {
    const mx = this.worldW + 120;
    const my = this.worldH + 120;
    this.x = Math.max(-60, Math.min(mx - 60, this.x));
    this.y = Math.max(-60, Math.min(my - 60, this.y));
  }
  minZoom(): number {
    return Math.max(0.28, Math.min(this.w / (this.worldW + 100), this.h / ((this.worldH + 100) * this.tilt)) * 0.9);
  }
  focusTile(x: number, y: number): void {
    const p = this.worldOfTile(x, y);
    this.x = p.x;
    this.y = p.y;
    this.clamp();
  }
}

export interface RenderState {
  view: GameView;
  vm: ViewMap;
  selectedUnit: ViewUnit | null;
  selectedCity: ViewCity | null;
  hover: Pos | null;
  reach: Map<number, number> | null;
  path: Array<Pos & { cost: number }> | null;
  pathTurns: number;
  attackTargets: Set<number>;
  showYields: boolean;
  showGrid: boolean;
  /** tiles currently worked by the open city */
  workedTiles: Set<number> | null;
  /** tiles pinned by the player in the open city */
  lockedTiles?: Set<number> | null;
  now: number;
}

export function tileYieldFromView(view: GameView, i: number): { food: number; prod: number; gold: number } {
  const t = TERRAIN[TERRAIN_LIST[view.terrain[i]]];
  const y = { food: t.food, prod: t.prod, gold: t.gold };
  const r = view.resource[i];
  if (r > 0) {
    const rd = RESOURCES[RESOURCE_LIST[r - 1]];
    y.food += rd.food;
    y.prod += rd.prod;
    y.gold += rd.gold;
  }
  const imp = view.improvement[i];
  if (imp === 1) y.food += IMPROVEMENT_YIELD.farm.food;
  if (imp === 2) y.prod += IMPROVEMENT_YIELD.mine.prod;
  return y;
}

// edge i of a pointy-top hex lies between corner i and i+1; neighbour in that direction (odd-r):
const EDGE_NEIGHBOR_EVEN: Array<[number, number]> = [[1, 0], [0, 1], [-1, 1], [-1, 0], [-1, -1], [0, -1]];
const EDGE_NEIGHBOR_ODD: Array<[number, number]> = [[1, 0], [1, 1], [0, 1], [-1, 0], [0, -1], [1, -1]];

const LAND_BASE: Record<string, string> = {
  grass: '#5f8f38',
  plains: '#b8a55a',
  desert: '#dcc283',
  tundra: '#b9c4c7',
  forest: '#42702f',
  hills: '#9a9650',
  mountain: '#7a766f',
};

function corner(cx: number, cy: number, r: number, i: number): [number, number] {
  const a = ((60 * i - 30) * Math.PI) / 180;
  return [cx + r * Math.cos(a), cy + r * Math.sin(a) * proj.tilt];
}

/** What the game screen needs from a map renderer. */
export interface MapDrawer {
  /** true when the picture is always in motion and wants a frame every tick */
  readonly continuous?: boolean;
  /** Minimum gap between frames that only repaint ambient motion (water, sway), in ms. */
  readonly idleMs?: number;
  resize(): void;
  draw(st: RenderState): void;
  dispose?(): void;
  /** tile under a screen point, when the renderer knows better than the flat ground plane */
  pickTile?(sx: number, sy: number, W: number, H: number): { x: number; y: number } | null;
  /** whether a screen point lies on a city's banner */
  bannerHit?(cityId: number, sx: number, sy: number): boolean;
  /** screen position of a unit's feet and of the top of its figure, for panels that follow it */
  unitScreen?(id: number): { x: number; y: number; top: number } | null;
}

export class MapRenderer implements MapDrawer {
  ctx: CanvasRenderingContext2D;
  constructor(public canvas: HTMLCanvasElement, public cam: Camera, public anim: Animator) {
    this.ctx = canvas.getContext('2d')!;
  }

  resize(): void {
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    const r = this.canvas.getBoundingClientRect();
    this.canvas.width = Math.max(1, Math.floor(r.width * dpr));
    this.canvas.height = Math.max(1, Math.floor(r.height * dpr));
    this.cam.w = r.width;
    this.cam.h = r.height;
  }

  draw(st: RenderState): void {
    const ctx = this.ctx;
    const cam = this.cam;
    proj.tilt = cam.tilt;
    const dpr = this.canvas.width / cam.w;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.fillStyle = '#0a111d';
    ctx.fillRect(0, 0, cam.w, cam.h);
    const v = st.view;
    const W = v.settings.width;
    const H = v.settings.height;
    const z = cam.zoom;
    // visible tile range
    const tl = cam.toWorld(0, 0);
    const br = cam.toWorld(cam.w, cam.h);
    const minRow = Math.max(0, Math.floor((tl.y - HEX * 2) / (HEX * 1.5)));
    const maxRow = Math.min(H - 1, Math.ceil((br.y + HEX) / (HEX * 1.5)));
    const minCol = Math.max(0, Math.floor((tl.x - HEX_W) / HEX_W) - 1);
    const maxCol = Math.min(W - 1, Math.ceil((br.x + HEX_W) / HEX_W));
    const idx = (x: number, y: number) => y * W + x;
    const variant = (x: number, y: number) => (x * 7 + y * 13) % 4;
    const putGround = (x: number, y: number, i: number) => {
      const wp = cam.worldOfTile(x, y);
      const s = cam.toScreen(wp.x, wp.y);
      const spr = groundSprite(TERRAIN_LIST[v.terrain[i]], variant(x, y));
      stamp(ctx, v.state[i] === 1 ? dimmed(spr) : spr, s.x, s.y, z);
    };

    // 1. terrain: water first, then a flat base under every land tile, then the feathered land textures on top
    for (let y = minRow; y <= maxRow; y++) {
      for (let x = minCol; x <= maxCol; x++) {
        const i = idx(x, y);
        if (v.state[i] === 0) continue;
        const t = TERRAIN_LIST[v.terrain[i]];
        if (TERRAIN[t].land) continue;
        const wp = cam.worldOfTile(x, y);
        const s = cam.toScreen(wp.x, wp.y);
        hexPath(ctx, s.x, s.y, HEX * z * 1.02);
        ctx.fillStyle = t === 'ocean' ? '#1a4a80' : '#46a2ba';
        ctx.fill();
      }
    }
    for (let y = minRow; y <= maxRow; y++) {
      for (let x = minCol; x <= maxCol; x++) {
        const i = idx(x, y);
        if (v.state[i] === 0 || TERRAIN[TERRAIN_LIST[v.terrain[i]]].land) continue;
        putGround(x, y, i);
      }
    }
    for (let y = minRow; y <= maxRow; y++) {
      for (let x = minCol; x <= maxCol; x++) {
        const i = idx(x, y);
        if (v.state[i] === 0 || !TERRAIN[TERRAIN_LIST[v.terrain[i]]].land) continue;
        const wp = cam.worldOfTile(x, y);
        const s = cam.toScreen(wp.x, wp.y);
        hexPath(ctx, s.x, s.y, HEX * z * 0.99);
        ctx.fillStyle = LAND_BASE[TERRAIN_LIST[v.terrain[i]]];
        ctx.fill();
      }
    }
    for (let y = minRow; y <= maxRow; y++) {
      for (let x = minCol; x <= maxCol; x++) {
        const i = idx(x, y);
        if (v.state[i] === 0 || !TERRAIN[TERRAIN_LIST[v.terrain[i]]].land) continue;
        putGround(x, y, i);
        if (st.workedTiles?.has(i)) {
          const wp = cam.worldOfTile(x, y);
          const s = cam.toScreen(wp.x, wp.y);
          hexPath(ctx, s.x, s.y, HEX * z * 0.92);
          ctx.fillStyle = 'rgba(255,230,140,0.18)';
          ctx.fill();
        }
      }
    }
    // water glints
    if (z > 0.5) {
      ctx.lineCap = 'round';
      for (let y = minRow; y <= maxRow; y++) {
        for (let x = minCol; x <= maxCol; x++) {
          const i = idx(x, y);
          if (v.state[i] !== 2) continue;
          const t = TERRAIN_LIST[v.terrain[i]];
          if (t !== 'ocean' && t !== 'coast') continue;
          const wp = cam.worldOfTile(x, y);
          const s = cam.toScreen(wp.x, wp.y);
          for (let k = 0; k < 2; k++) {
            const ph = (x * 31 + y * 17 + k * 53) % 100;
            const a = Math.sin(st.now / 1100 + ph) * 0.5 + 0.5;
            if (a < 0.35) continue;
            const gx = s.x + (((ph * 7) % 50) - 25) * z * 0.9;
            const gy = s.y + (((ph * 13) % 40) - 20) * z * 0.9;
            ctx.strokeStyle = `rgba(235,248,255,${(a - 0.3) * 0.5})`;
            ctx.lineWidth = Math.max(1, 1.5 * z);
            ctx.beginPath();
            ctx.moveTo(gx - 6 * z, gy);
            ctx.quadraticCurveTo(gx - 3 * z, gy - 2.4 * z, gx, gy);
            ctx.quadraticCurveTo(gx + 3 * z, gy + 2.4 * z, gx + 6 * z, gy);
            ctx.stroke();
          }
        }
      }
    }

    // unexplored tiles: dark shroud, with a soft fade creeping onto explored neighbours
    for (let y = minRow; y <= maxRow; y++) {
      for (let x = minCol; x <= maxCol; x++) {
        const i = idx(x, y);
        const wp = cam.worldOfTile(x, y);
        const s = cam.toScreen(wp.x, wp.y);
        if (v.state[i] === 0) {
          hexPath(ctx, s.x, s.y, HEX * z * 1.02);
          ctx.fillStyle = '#0a1120';
          ctx.fill();
          continue;
        }
        const nbs = y & 1 ? EDGE_NEIGHBOR_ODD : EDGE_NEIGHBOR_EVEN;
        for (let e = 0; e < 6; e++) {
          const nx = x + nbs[e][0];
          const ny = y + nbs[e][1];
          if (nx < 0 || ny < 0 || nx >= W || ny >= H || v.state[idx(nx, ny)] !== 0) continue;
          const [ax, ay] = corner(s.x, s.y, HEX * z, e);
          const [bx, by] = corner(s.x, s.y, HEX * z, e + 1);
          const mx = (ax + bx) / 2;
          const my = (ay + by) / 2;
          const ix = s.x + (mx - s.x) * 0.25;
          const iy = s.y + (my - s.y) * 0.25;
          const g = ctx.createLinearGradient(mx, my, ix, iy);
          g.addColorStop(0, 'rgba(10,17,32,0.95)');
          g.addColorStop(1, 'rgba(10,17,32,0)');
          ctx.save();
          hexPath(ctx, s.x, s.y, HEX * z * 1.0);
          ctx.clip();
          ctx.fillStyle = g;
          ctx.fillRect(s.x - HEX * z, s.y - HEX * z, HEX * z * 2, HEX * z * 2);
          ctx.restore();
        }
      }
    }

    // drifting cloud shadows
    if (z > 0.35) {
      const pat = ctx.createPattern(cloudShadow(), 'repeat');
      if (pat) {
        const sc = z * 2.2;
        pat.setTransform(new DOMMatrix().translate(cam.w / 2 - cam.x * z + st.now * 0.006 * z, cam.h / 2 - cam.y * z + st.now * 0.002 * z).scale(sc, sc));
        ctx.globalAlpha = 0.16;
        ctx.fillStyle = pat;
        ctx.fillRect(0, 0, cam.w, cam.h);
        ctx.globalAlpha = 1;
      }
    }

    // 2. ownership tint + borders
    ctx.lineJoin = 'round';
    ctx.lineCap = 'round';
    for (let y = minRow; y <= maxRow; y++) {
      for (let x = minCol; x <= maxCol; x++) {
        const i = y * W + x;
        if (v.state[i] === 0) continue;
        const owner = v.owner[i] - 1;
        if (owner < 0) continue;
        const wp = cam.worldOfTile(x, y);
        const s = cam.toScreen(wp.x, wp.y);
        const col = civColor(v, owner);
        const r = HEX * z;
        const nbs = y & 1 ? EDGE_NEIGHBOR_ODD : EDGE_NEIGHBOR_EVEN;
        const edges: number[] = [];
        for (let e = 0; e < 6; e++) {
          const nx = x + nbs[e][0];
          const ny = y + nbs[e][1];
          const nOwner = nx < 0 || ny < 0 || nx >= W || ny >= H ? -2 : v.owner[ny * W + nx] - 1;
          if (nOwner !== owner) edges.push(e);
        }
        if (!edges.length) continue;
        ctx.save();
        hexPath(ctx, s.x, s.y, r);
        ctx.clip();
        for (const [lw, a] of [[16 * z, 0.14], [9 * z, 0.2]] as const) {
          ctx.strokeStyle = hexA(col, a);
          ctx.lineWidth = lw;
          for (const e of edges) {
            const [ax, ay] = corner(s.x, s.y, r, e);
            const [bx, by] = corner(s.x, s.y, r, e + 1);
            ctx.beginPath();
            ctx.moveTo(ax, ay);
            ctx.lineTo(bx, by);
            ctx.stroke();
          }
        }
        ctx.restore();
        ctx.lineWidth = Math.max(2, 3.2 * z);
        for (const e of edges) {
          const [ax, ay] = corner(s.x, s.y, r * 0.97, e);
          const [bx, by] = corner(s.x, s.y, r * 0.97, e + 1);
          ctx.strokeStyle = 'rgba(8,10,16,0.45)';
          ctx.lineWidth = Math.max(3, 5 * z);
          ctx.beginPath();
          ctx.moveTo(ax, ay);
          ctx.lineTo(bx, by);
          ctx.stroke();
          ctx.strokeStyle = hexA(col, 0.98);
          ctx.lineWidth = Math.max(2, 3 * z);
          ctx.stroke();
        }
      }
    }

    // shorelines: a soft foam line on every land edge that touches water
    for (let y = minRow; y <= maxRow; y++) {
      for (let x = minCol; x <= maxCol; x++) {
        const i = y * W + x;
        if (v.state[i] === 0) continue;
        const terr = TERRAIN_LIST[v.terrain[i]];
        if (TERRAIN[terr].land) continue;
        const wp = cam.worldOfTile(x, y);
        const sp = cam.toScreen(wp.x, wp.y);
        const nbs = y & 1 ? EDGE_NEIGHBOR_ODD : EDGE_NEIGHBOR_EVEN;
        for (let e = 0; e < 6; e++) {
          const nx = x + nbs[e][0];
          const ny = y + nbs[e][1];
          if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
          const ni = ny * W + nx;
          if (v.state[ni] === 0 || !TERRAIN[TERRAIN_LIST[v.terrain[ni]]].land) continue;
          const [ax, ay] = corner(sp.x, sp.y, HEX * z * 0.985, e);
          const [bx, by] = corner(sp.x, sp.y, HEX * z * 0.985, e + 1);
          ctx.strokeStyle = 'rgba(214,238,248,0.55)';
          ctx.lineWidth = Math.max(1.5, 3 * z);
          ctx.beginPath();
          ctx.moveTo(ax, ay);
          ctx.lineTo(bx, by);
          ctx.stroke();
          ctx.strokeStyle = 'rgba(214,238,248,0.22)';
          ctx.lineWidth = Math.max(3, 7 * z);
          ctx.stroke();
        }
      }
    }

    // 3. grid
    if (st.showGrid && z > 0.5) {
      ctx.strokeStyle = 'rgba(0,0,0,0.13)';
      ctx.lineWidth = 1;
      for (let y = minRow; y <= maxRow; y++) {
        for (let x = minCol; x <= maxCol; x++) {
          if (v.state[y * W + x] === 0) continue;
          const wp = cam.worldOfTile(x, y);
          const s = cam.toScreen(wp.x, wp.y);
          hexPath(ctx, s.x, s.y, HEX * z);
          ctx.stroke();
        }
      }
    }

    // 4. roads
    for (let y = minRow; y <= maxRow; y++) {
      for (let x = minCol; x <= maxCol; x++) {
        const i = y * W + x;
        if (v.state[i] === 0) continue;
        const wp = cam.worldOfTile(x, y);
        const s = cam.toScreen(wp.x, wp.y);
        if (v.road[i] === 1) this.drawRoads(ctx, v, x, y, s.x, s.y, z);
      }
    }

    // 5. fog of war over explored-but-unseen ground
    for (let y = minRow; y <= maxRow; y++) {
      for (let x = minCol; x <= maxCol; x++) {
        if (v.state[y * W + x] !== 1) continue;
        const wp = cam.worldOfTile(x, y);
        const s = cam.toScreen(wp.x, wp.y);
        hexPath(ctx, s.x, s.y, HEX * z * 1.01);
        ctx.fillStyle = 'rgba(8,14,30,0.32)';
        ctx.fill();
      }
    }

    // 6. yields
    if (st.showYields && z > 0.75) {
      ctx.font = `600 ${Math.round(11 * z)}px system-ui, sans-serif`;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      for (let y = minRow; y <= maxRow; y++) {
        for (let x = minCol; x <= maxCol; x++) {
          const i = y * W + x;
          if (v.state[i] === 0) continue;
          const t = TERRAIN[TERRAIN_LIST[v.terrain[i]]];
          if (!t.workable) continue;
          const wp = cam.worldOfTile(x, y);
          const s = cam.toScreen(wp.x, wp.y);
          const yy = tileYieldFromView(v, i);
          const parts: Array<[number, string]> = [[yy.food, '#8be26b'], [yy.prod, '#f0a24a'], [yy.gold, '#ffd84a']];
          let ox = -((parts.filter((p) => p[0] > 0).length - 1) * 7 * z);
          for (const [n, col] of parts) {
            if (n <= 0) continue;
            ctx.fillStyle = 'rgba(0,0,0,0.65)';
            ctx.fillText(String(n), s.x + ox + 0.8, s.y + HEX * 0.62 * z + 0.8);
            ctx.fillStyle = col;
            ctx.fillText(String(n), s.x + ox, s.y + HEX * 0.62 * z);
            ox += 14 * z;
          }
        }
      }
    }

    // 7. move range / path / attack targets
    if (st.reach) {
      for (const [k] of st.reach) {
        const x = k % W;
        const y = Math.floor(k / W);
        if (x < minCol || x > maxCol || y < minRow || y > maxRow) continue;
        const wp = cam.worldOfTile(x, y);
        const s = cam.toScreen(wp.x, wp.y);
        hexPath(ctx, s.x, s.y, HEX * z * 0.9);
        ctx.fillStyle = 'rgba(255,255,255,0.16)';
        ctx.fill();
        ctx.strokeStyle = 'rgba(255,255,255,0.45)';
        ctx.lineWidth = 1.2;
        ctx.stroke();
      }
    }
    for (const k of st.attackTargets) {
      const x = k % W;
      const y = Math.floor(k / W);
      const wp = cam.worldOfTile(x, y);
      const s = cam.toScreen(wp.x, wp.y);
      const pulse = 0.25 + 0.15 * Math.sin(st.now / 180);
      hexPath(ctx, s.x, s.y, HEX * z * 0.92);
      ctx.fillStyle = `rgba(255,70,50,${pulse})`;
      ctx.fill();
      ctx.strokeStyle = 'rgba(255,90,70,0.95)';
      ctx.lineWidth = 2.2;
      ctx.stroke();
    }
    if (st.path && st.path.length) this.drawPath(ctx, st, st.path, z);

    // 8. standing terrain, resources, improvements, cities (all depth-sorted with the units)
    const sorted: Array<{ y: number; draw: () => void }> = [];
    const cityAt = new Set<number>();
    for (const c of v.cities) cityAt.add(c.y * W + c.x);
    for (let y = minRow; y <= maxRow; y++) {
      for (let x = minCol; x <= maxCol; x++) {
        const i = y * W + x;
        const state = v.state[i];
        if (state === 0) continue;
        const wp = cam.worldOfTile(x, y);
        const sp = cam.toScreen(wp.x, wp.y);
        const fog = state === 1;
        const put = (spr: Sprite, ox: number, oy: number, k = 1) => stamp(ctx, fog ? dimmed(spr) : spr, sp.x + ox * z, sp.y + oy * z, z, k);
        const terr = TERRAIN_LIST[v.terrain[i]];
        const imp = v.improvement[i];
        const base = y * 10;
        if (!cityAt.has(i)) {
          const flat = (terr === 'grass' || terr === 'plains') && imp === 1;
          if (!flat) {
            const dec = decorSprite(terr, variant(x, y));
            if (dec) sorted.push({ y: base, draw: () => put(dec, 0, 0) });
          }
          if (imp === 1) sorted.push({ y: base + 1, draw: () => put(farmSprite(variant(x, y)), 0, 6, 1.05) });
          else if (imp === 2) sorted.push({ y: base + 1, draw: () => put(mineSprite(), -4, 16, 1.05) });
          if (v.hut[i] === 1) sorted.push({ y: base + 1, draw: () => put(hutSprite(), 0, 6, 1.05) });
          const res = v.resource[i];
          if (res > 0) {
            const rid = RESOURCE_LIST[res - 1];
            const onWater = terr === 'ocean' || terr === 'coast';
            sorted.push({ y: base + 2, draw: () => put(resourceSprite(rid), onWater ? 0 : 14, onWater ? 4 : 18, 0.95) });
          }
        }
        if (st.lockedTiles?.has(i)) sorted.push({ y: base + 3, draw: () => this.drawBadge(ctx, 'tile', sp.x, sp.y - HEX * 0.46 * z, z, false, true) });
      }
    }
    for (const c of v.cities) {
      if (c.x < minCol - 1 || c.x > maxCol + 1 || c.y < minRow - 1 || c.y > maxRow + 1) continue;
      sorted.push({ y: c.y * 10 + 4, draw: () => this.drawCity(ctx, v, c, z) });
    }
    // 9. units (sorted by row, civilians behind military)
    const unitList = v.units.slice();
    const ghostList = this.anim.ghosts;
    for (const u of unitList) {
      const p = this.anim.unitPos(u.id, st.now);
      const tx = p ? p.x : u.x;
      const ty = p ? p.y : u.y;
      if (tx < minCol - 2 || tx > maxCol + 2 || ty < minRow - 2 || ty > maxRow + 2) continue;
      const civ = UNITS[u.type].civilian;
      sorted.push({ y: ty * 10 + 5 + (civ ? -1 : 1) + (p ? 3 : 0), draw: () => this.drawUnit(ctx, v, u, tx, ty, z, st) });
    }
    for (const g of ghostList) {
      sorted.push({
        y: g.y * 10 + 6,
        draw: () => {
          const fade = st.now < g.fadeStart ? 1 : Math.max(0, 1 - (st.now - g.fadeStart) / 500);
          if (fade <= 0) return;
          const gu: ViewUnit = { id: -1, owner: g.owner, type: g.type, x: g.x, y: g.y, hp: 100, level: 0, fortified: false };
          ctx.globalAlpha = fade;
          this.drawUnit(ctx, v, gu, g.x, g.y, z, st, true);
          ctx.globalAlpha = 1;
        },
      });
    }
    sorted.sort((a, b) => a.y - b.y);
    for (const s of sorted) s.draw();
    for (const c of v.cities) {
      if (c.x < minCol - 1 || c.x > maxCol + 1 || c.y < minRow - 1 || c.y > maxRow + 1) continue;
      this.drawBanner(ctx, v, c, z);
    }

    // soft vignette so the map sits in its frame
    const vg = ctx.createRadialGradient(cam.w / 2, cam.h / 2, Math.min(cam.w, cam.h) * 0.35, cam.w / 2, cam.h / 2, Math.hypot(cam.w, cam.h) * 0.62);
    vg.addColorStop(0, 'rgba(6,10,20,0)');
    vg.addColorStop(1, 'rgba(6,10,20,0.42)');
    ctx.fillStyle = vg;
    ctx.fillRect(0, 0, cam.w, cam.h);

    // 10. hover & selection
    if (st.hover) {
      const wp = cam.worldOfTile(st.hover.x, st.hover.y);
      const s = cam.toScreen(wp.x, wp.y);
      hexPath(ctx, s.x, s.y, HEX * z * 0.96);
      ctx.strokeStyle = 'rgba(255,255,255,0.8)';
      ctx.lineWidth = 2;
      ctx.stroke();
    }
    const sel = st.selectedUnit ?? null;
    const selTile = sel ? { x: sel.x, y: sel.y } : st.selectedCity ? { x: st.selectedCity.x, y: st.selectedCity.y } : null;
    if (selTile) {
      const p = st.selectedUnit ? this.anim.unitPos(st.selectedUnit.id, st.now) : null;
      const wp = cam.worldOfTile(p ? p.x : selTile.x, p ? p.y : selTile.y);
      const s = cam.toScreen(wp.x, wp.y);
      hexPath(ctx, s.x, s.y, HEX * z * 0.98);
      ctx.strokeStyle = `rgba(255,214,90,${0.7 + 0.3 * Math.sin(st.now / 220)})`;
      ctx.lineWidth = 3.2;
      ctx.stroke();
    }

    // 11. effects
    for (const f of this.anim.flashes) {
      const x = f[0] % 1000;
      const y = Math.floor(f[0] / 1000);
      const wp = cam.worldOfTile(x, y);
      const s = cam.toScreen(wp.x, wp.y);
      const left = (f[1] - st.now) / 260;
      hexPath(ctx, s.x, s.y, HEX * z * 0.95);
      ctx.fillStyle = `rgba(255,255,255,${Math.max(0, Math.min(0.6, left * 0.6))})`;
      ctx.fill();
    }
    for (const r of this.anim.rings) {
      const k = (st.now - r.t0) / r.dur;
      if (k < 0 || k > 1) continue;
      const wp = cam.worldOfTile(r.x, r.y);
      const s = cam.toScreen(wp.x, wp.y);
      ctx.beginPath();
      ctx.arc(s.x, s.y, HEX * z * (0.4 + k * r.max), 0, Math.PI * 2);
      ctx.strokeStyle = hexA(r.color, 1 - k);
      ctx.lineWidth = 4 * z * (1 - k) + 1;
      ctx.stroke();
    }
    for (const p of this.anim.projectiles) {
      const k = (st.now - p.t0) / p.dur;
      if (k < 0 || k > 1) continue;
      const a = cam.worldOfTile(p.from.x, p.from.y);
      const b = cam.worldOfTile(p.to.x, p.to.y);
      const wx = a.x + (b.x - a.x) * k;
      const wy = a.y + (b.y - a.y) * k - Math.sin(k * Math.PI) * 26;
      const s = cam.toScreen(wx, wy);
      ctx.save();
      ctx.fillStyle = p.kind === 'arrow' ? '#f2e6c4' : p.kind === 'rock' ? '#8a8378' : '#2a2a2a';
      ctx.beginPath();
      ctx.arc(s.x, s.y, (p.kind === 'arrow' ? 3 : 5.5) * z, 0, Math.PI * 2);
      ctx.fill();
      ctx.restore();
    }
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    for (const f of this.anim.floaters) {
      const k = (st.now - f.t0) / f.dur;
      if (k < 0 || k > 1) continue;
      const wp = cam.worldOfTile(f.x, f.y);
      const s = cam.toScreen(wp.x, wp.y - 18 - k * 30);
      ctx.font = `800 ${Math.round(18 * Math.max(0.8, z))}px system-ui, sans-serif`;
      ctx.globalAlpha = 1 - k * k;
      ctx.lineWidth = 4;
      ctx.strokeStyle = 'rgba(0,0,0,0.75)';
      ctx.strokeText(f.text, s.x, s.y);
      ctx.fillStyle = f.color;
      ctx.fillText(f.text, s.x, s.y);
      ctx.globalAlpha = 1;
    }
  }

  /** Round resource / feature badge in the Civilization style: dark disc, thin ring, glyph. */
  private drawBadge(ctx: CanvasRenderingContext2D, name: string, x: number, y: number, z: number, strategic: boolean, gold = false): void {
    const r = 10.5 * Math.max(0.8, z);
    ctx.beginPath();
    ctx.arc(x, y, r, 0, Math.PI * 2);
    ctx.fillStyle = 'rgba(8,11,17,0.82)';
    ctx.fill();
    ctx.lineWidth = 1.4;
    ctx.strokeStyle = gold ? '#f2d98c' : strategic ? '#e5974a' : 'rgba(233,225,204,0.55)';
    ctx.stroke();
    drawIcon(ctx, name, x, y, r * 1.35, gold ? '#f2d98c' : strategic ? '#f0a860' : '#e9e1cc', '#e9e1cc', 1.8);
  }

  private drawRoads(ctx: CanvasRenderingContext2D, v: GameView, x: number, y: number, sx: number, sy: number, z: number): void {
    const W = v.settings.width;
    const H = v.settings.height;
    const nbs = y & 1 ? EDGE_NEIGHBOR_ODD : EDGE_NEIGHBOR_EVEN;
    const arms: Array<[number, number]> = [];
    for (let e = 0; e < 6; e++) {
      const nx = x + nbs[e][0];
      const ny = y + nbs[e][1];
      if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
      const ni = ny * W + nx;
      const isCity = v.cities.some((c) => c.x === nx && c.y === ny);
      if (v.road[ni] !== 1 && !isCity) continue;
      if (v.state[ni] === 0) continue;
      const [ax, ay] = corner(0, 0, HEX * z, e);
      const [bx, by] = corner(0, 0, HEX * z, e + 1);
      arms.push([(ax + bx) / 2, (ay + by) / 2]);
    }
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    const trace = () => {
      for (const [mx, my] of arms) {
        ctx.beginPath();
        ctx.moveTo(sx, sy);
        ctx.quadraticCurveTo(sx + mx * 0.5 + my * 0.06, sy + my * 0.5 - mx * 0.06, sx + mx, sy + my);
        ctx.stroke();
      }
    };
    if (!arms.length) {
      ctx.beginPath();
      ctx.arc(sx, sy, 3 * z, 0, Math.PI * 2);
      ctx.fillStyle = 'rgba(120,92,58,0.9)';
      ctx.fill();
      return;
    }
    ctx.strokeStyle = 'rgba(60,42,24,0.55)';
    ctx.lineWidth = Math.max(3, 7.5 * z);
    trace();
    ctx.strokeStyle = '#b79a68';
    ctx.lineWidth = Math.max(2, 4.6 * z);
    trace();
    ctx.strokeStyle = 'rgba(232,212,160,0.55)';
    ctx.lineWidth = Math.max(1, 1.6 * z);
    trace();
  }

  private drawCity(ctx: CanvasRenderingContext2D, v: GameView, c: ViewCity, z: number): void {
    const wp = this.cam.worldOfTile(c.x, c.y);
    const s = this.cam.toScreen(wp.x, wp.y);
    const col = civColor(v, c.owner);
    const fogged = v.state[c.y * v.settings.width + c.x] === 1;
    const walls = !!c.mine && !!c.buildings?.includes('walls');
    const tier = Math.min(3, Math.floor(c.pop / 4));
    const spr = citySprite(tier, walls, c.capital, col);
    stamp(ctx, fogged ? dimmed(spr) : spr, s.x, s.y + 6 * z, z, 1.4);
    if (c.wonders.length) drawIcon(ctx, c.wonders[0], s.x + 30 * z, s.y - 14 * z, 17 * z, '#f2d98c', '#f2d98c', 1.9);
  }

  /** City banner in the Civilization manner: population shield, name plate and the current build. */
  private drawBanner(ctx: CanvasRenderingContext2D, v: GameView, c: ViewCity, z: number): void {
    const wp = this.cam.worldOfTile(c.x, c.y);
    const s = this.cam.toScreen(wp.x, wp.y);
    const col = civColor(v, c.owner);
    const fogged = v.state[c.y * v.settings.width + c.x] === 1;
    const fs = Math.max(11, Math.min(15, 12.5 * z + 1));
    ctx.font = `600 ${fs}px ${DISPLAY_FONT}`;
    const label = (c.capital ? '\u2605 ' : '') + c.name;
    const tw = ctx.measureText(label).width;
    const head = c.mine ? c.queue?.[0] : null;
    const bh = fs + 12;
    const left = bh + 2;
    const right = head ? bh : 8;
    const bw = left + tw + right + 8;
    const bx = s.x - bw / 2;
    const by = s.y + HEX * 0.5 * z + 6;
    ctx.save();
    if (fogged) ctx.globalAlpha = 0.85;
    ctx.shadowColor = 'rgba(0,0,0,0.55)';
    ctx.shadowBlur = 6;
    ctx.shadowOffsetY = 2;
    ctx.beginPath();
    ctx.moveTo(bx + 6, by);
    ctx.lineTo(bx + bw - 6, by);
    ctx.lineTo(bx + bw, by + bh / 2);
    ctx.lineTo(bx + bw - 6, by + bh);
    ctx.lineTo(bx + 6, by + bh);
    ctx.lineTo(bx, by + bh / 2);
    ctx.closePath();
    const g = ctx.createLinearGradient(0, by, 0, by + bh);
    g.addColorStop(0, 'rgba(30,38,52,0.97)');
    g.addColorStop(1, 'rgba(9,12,19,0.97)');
    ctx.fillStyle = g;
    ctx.fill();
    ctx.shadowColor = 'transparent';
    ctx.lineWidth = 1.6;
    ctx.strokeStyle = hexA(col, 1);
    ctx.stroke();
    const pr = bh * 0.52;
    ctx.beginPath();
    ctx.arc(bx + pr + 1, by + bh / 2, pr, 0, Math.PI * 2);
    ctx.fillStyle = hexA(col, 1);
    ctx.fill();
    ctx.lineWidth = 1.5;
    ctx.strokeStyle = 'rgba(0,0,0,0.5)';
    ctx.stroke();
    ctx.textBaseline = 'middle';
    ctx.textAlign = 'center';
    ctx.fillStyle = lum(col) > 0.62 ? '#10151d' : '#ffffff';
    ctx.font = `700 ${fs}px ${DISPLAY_FONT}`;
    ctx.fillText(String(c.pop), bx + pr + 1, by + bh / 2 + 0.5);
    ctx.textAlign = 'left';
    ctx.fillStyle = '#efe7d0';
    ctx.font = `600 ${fs}px ${DISPLAY_FONT}`;
    ctx.fillText(label, bx + left + 2, by + bh / 2 + 0.5);
    if (head) drawIcon(ctx, head.id, bx + bw - bh * 0.62, by + bh / 2, bh * 0.62, '#e5974a', '#e5974a', 1.8);
    if (c.hp < c.maxHp && c.visible) {
      ctx.fillStyle = 'rgba(0,0,0,0.65)';
      ctx.fillRect(bx + 4, by + bh + 2, bw - 8, 4);
      ctx.fillStyle = c.hp / c.maxHp > 0.5 ? '#82d48d' : c.hp / c.maxHp > 0.25 ? '#e8c14a' : '#ec6c5d';
      ctx.fillRect(bx + 4, by + bh + 2, (bw - 8) * Math.max(0, c.hp / c.maxHp), 4);
    }
    ctx.restore();
  }

  /** A painted figure standing on a civilisation-coloured base, with health and status marks above it. */
  private drawUnit(ctx: CanvasRenderingContext2D, v: GameView, u: ViewUnit, tx: number, ty: number, z: number, st: RenderState, ghost = false): void {
    const def = UNITS[u.type];
    const wp = this.cam.worldOfTile(tx, ty);
    const s = this.cam.toScreen(wp.x, wp.y);
    const col = civColor(v, u.owner);
    const civ = !!def.civilian;
    const onCity = v.cities.some((c) => c.x === Math.round(tx) && c.y === Math.round(ty));
    const x = s.x + (civ ? -16 : onCity ? 26 : 0) * z;
    const y = s.y + (civ ? 20 : onCity ? 14 : 16) * z;
    const mine = u.owner === v.me;
    const selected = st.selectedUnit?.id === u.id;
    const k = Math.max(0.8, z) * 1.12 * (selected ? 1.06 : 1);
    ctx.save();
    // base disc in the owner's colour
    const rx = 17 * k;
    const ry = 6.6 * k;
    ctx.beginPath();
    ctx.ellipse(x, y + 1.5 * k, rx, ry, 0, 0, Math.PI * 2);
    const bg = ctx.createRadialGradient(x - rx * 0.3, y, 1, x, y + 1.5 * k, rx);
    bg.addColorStop(0, shade(col, 0.25));
    bg.addColorStop(1, shade(col, -0.2));
    ctx.fillStyle = bg;
    ctx.fill();
    ctx.lineWidth = selected ? 2.4 : 1.5;
    ctx.strokeStyle = selected ? `rgba(255,222,120,${0.75 + 0.25 * Math.sin(st.now / 200)})` : 'rgba(8,10,16,0.85)';
    ctx.stroke();
    if (!ghost && mine && (u.mp ?? 0) > 0 && !u.order && !u.auto && !u.fortified && !u.skipped && !selected) {
      ctx.strokeStyle = `rgba(255,255,255,${0.35 + 0.4 * Math.sin(st.now / 280)})`;
      ctx.lineWidth = 2.4;
      ctx.stroke();
    }
    // the figure
    const spr = unitSprite(u.type, col);
    stamp(ctx, spr, x, y, k, 1);
    // a small banner on a pole so ownership reads at a glance
    const top = y - 50 * k;
    ctx.strokeStyle = 'rgba(20,16,12,0.9)';
    ctx.lineWidth = 1.4;
    ctx.beginPath();
    ctx.moveTo(x - 13 * k, y - 4 * k);
    ctx.lineTo(x - 13 * k, y - 36 * k);
    ctx.stroke();
    ctx.beginPath();
    ctx.moveTo(x - 13 * k, y - 36 * k);
    ctx.lineTo(x - 2 * k, y - 33 * k);
    ctx.lineTo(x - 13 * k, y - 29 * k);
    ctx.closePath();
    ctx.fillStyle = col;
    ctx.fill();
    ctx.lineWidth = 0.9;
    ctx.stroke();
    if (u.hp < 100 && !ghost) {
      const bw = 28 * k;
      ctx.fillStyle = 'rgba(0,0,0,0.8)';
      ctx.fillRect(x - bw / 2, top, bw, 5);
      ctx.fillStyle = u.hp > 60 ? '#82d48d' : u.hp > 30 ? '#e8c14a' : '#ec6c5d';
      ctx.fillRect(x - bw / 2 + 0.5, top + 0.5, ((bw - 1) * u.hp) / 100, 4);
    }
    if (!ghost && mine) {
      let chip: string | null = null;
      if (u.auto === 'work') chip = 'auto';
      else if (u.auto === 'explore') chip = 'explore';
      else if (u.order?.kind === 'fortify' || u.fortified) chip = 'fortify';
      else if (u.order?.kind === 'sleep') chip = 'sleep';
      else if (u.order?.kind === 'build') chip = u.order.what;
      else if (u.order?.kind === 'goto') chip = 'chevron';
      if (chip) {
        const r = 7.5 * Math.max(0.85, z);
        const cx = x + 16 * k;
        const cy = y - 38 * k;
        ctx.beginPath();
        ctx.arc(cx, cy, r, 0, Math.PI * 2);
        ctx.fillStyle = '#0c1017';
        ctx.fill();
        ctx.lineWidth = 1.3;
        ctx.strokeStyle = '#d4af5a';
        ctx.stroke();
        drawIcon(ctx, chip, cx, cy, r * 1.35, '#f2d98c', '#f2d98c', 2.1);
      }
      for (let i = 0; i < u.level; i++) drawIcon(ctx, 'star', x - 10 * k + i * 7 * k, y - 43 * k, 8 * k, '#ffe08a', '#ffe08a', 1.2);
    }
    ctx.restore();
  }

  private drawPath(ctx: CanvasRenderingContext2D, st: RenderState, path: Array<Pos & { cost: number }>, z: number): void {
    const u = st.selectedUnit;
    if (!u) return;
    const full = UNITS[u.type].moves * 2;
    let mp = u.mp ?? full;
    ctx.save();
    ctx.strokeStyle = 'rgba(255,255,255,0.9)';
    ctx.lineWidth = Math.max(2, 3 * z);
    ctx.setLineDash([6 * z, 5 * z]);
    ctx.beginPath();
    const start = this.cam.toScreen(...(Object.values(this.cam.worldOfTile(u.x, u.y)) as [number, number]));
    ctx.moveTo(start.x, start.y);
    let turn = 1;
    let prev: Pos = { x: u.x, y: u.y };
    const marks: Array<{ x: number; y: number; n: number }> = [];
    for (const p of path) {
      const c = st.vm.stepCost(u, prev.x, prev.y, p.x, p.y);
      if (mp <= 0) {
        turn++;
        mp = full;
        const w = this.cam.worldOfTile(prev.x, prev.y);
        const sc = this.cam.toScreen(w.x, w.y);
        marks.push({ x: sc.x, y: sc.y, n: turn - 1 });
      }
      mp -= c;
      const w = this.cam.worldOfTile(p.x, p.y);
      const sc = this.cam.toScreen(w.x, w.y);
      ctx.lineTo(sc.x, sc.y);
      prev = p;
    }
    ctx.stroke();
    ctx.setLineDash([]);
    const last = path[path.length - 1];
    const lw = this.cam.worldOfTile(last.x, last.y);
    const ls = this.cam.toScreen(lw.x, lw.y);
    ctx.beginPath();
    ctx.arc(ls.x, ls.y, 6 * Math.max(0.8, z), 0, Math.PI * 2);
    ctx.fillStyle = '#fff';
    ctx.fill();
    if (turn > 1) {
      ctx.font = `800 ${Math.round(13 * Math.max(0.9, z))}px system-ui, sans-serif`;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      const label = `${turn}`;
      roundRect(ctx, ls.x - 11, ls.y - 34, 22, 20, 6);
      ctx.fillStyle = 'rgba(14,20,34,0.92)';
      ctx.fill();
      ctx.fillStyle = '#ffd84a';
      ctx.fillText(label, ls.x, ls.y - 24);
    }
    ctx.restore();
  }
}

export function roundRect(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number): void {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}

export function hexA(hex: string, a: number): string {
  const n = parseInt(hex.slice(1), 16);
  return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${a})`;
}

/** Lighten (amount > 0) or darken (amount < 0) a #rrggbb colour. */
export function shade(hex: string, amount: number): string {
  const n = parseInt(hex.slice(1), 16);
  const f = (c: number) => Math.max(0, Math.min(255, Math.round(amount >= 0 ? c + (255 - c) * amount : c * (1 + amount))));
  return `rgb(${f((n >> 16) & 255)},${f((n >> 8) & 255)},${f(n & 255)})`;
}

/** Relative luminance (0..1) of a #rrggbb colour. */
export function lum(hex: string): number {
  const n = parseInt(hex.slice(1), 16);
  return (0.2126 * ((n >> 16) & 255) + 0.7152 * ((n >> 8) & 255) + 0.0722 * (n & 255)) / 255;
}
