import { hexToPixel, pixelToHex, type Pos } from '../shared/hex';
import { BUILDINGS, CIV_BY_ID, IMPROVEMENT_YIELD, RESOURCES, TERRAIN, UNITS } from '../shared/data';
import { RESOURCE_LIST, TERRAIN_LIST, type GameView, type ViewCity, type ViewUnit } from '../shared/view';
import type { ViewMap } from '../shared/viewpath';
import type { Animator } from './anim';
import { HEX, HEX_H, HEX_W, SPRITE_RES, VARIANTS, hexPath, terrainSprite } from './sprites';
import { drawIcon } from './icons';

export const DISPLAY_FONT = "'Cinzel','Palatino Linotype','Book Antiqua',Georgia,serif";
export const EMOJI_FONT = '"Apple Color Emoji","Segoe UI Emoji","Noto Color Emoji","Twemoji Mozilla",sans-serif';

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

  setWorld(cols: number, rows: number): void {
    this.worldW = HEX_W * (cols + 0.5);
    this.worldH = HEX * 1.5 * rows + HEX * 0.5;
  }
  worldOfTile(x: number, y: number): { x: number; y: number } {
    const p = hexToPixel(x, y, HEX);
    return { x: p.px + HEX_W / 2, y: p.py + HEX };
  }
  toScreen(wx: number, wy: number): { x: number; y: number } {
    return { x: (wx - this.x) * this.zoom + this.w / 2, y: (wy - this.y) * this.zoom + this.h / 2 };
  }
  toWorld(sx: number, sy: number): { x: number; y: number } {
    return { x: (sx - this.w / 2) / this.zoom + this.x, y: (sy - this.h / 2) / this.zoom + this.y };
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
    return Math.max(0.28, Math.min(this.w / (this.worldW + 100), this.h / (this.worldH + 100)) * 0.9);
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

function corner(cx: number, cy: number, r: number, i: number): [number, number] {
  const a = ((60 * i - 30) * Math.PI) / 180;
  return [cx + r * Math.cos(a), cy + r * Math.sin(a)];
}

export class MapRenderer {
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
    const sw = (HEX_W * SPRITE_RES + 4) / SPRITE_RES;
    const sh = (HEX_H * SPRITE_RES + 4) / SPRITE_RES;

    // 1. terrain
    for (let y = minRow; y <= maxRow; y++) {
      for (let x = minCol; x <= maxCol; x++) {
        const i = y * W + x;
        const state = v.state[i];
        const wp = cam.worldOfTile(x, y);
        const s = cam.toScreen(wp.x, wp.y);
        if (state === 0) {
          hexPath(ctx, s.x, s.y, HEX * z * 1.01);
          ctx.fillStyle = '#0e1626';
          ctx.fill();
          continue;
        }
        const terr = TERRAIN_LIST[v.terrain[i]];
        const spr = terrainSprite(terr, (x * 7 + y * 13) % VARIANTS);
        ctx.drawImage(spr, s.x - (sw * z) / 2, s.y - (sh * z) / 2, sw * z, sh * z);
        if (st.workedTiles?.has(i)) {
          hexPath(ctx, s.x, s.y, HEX * z * 0.92);
          ctx.fillStyle = 'rgba(255,230,140,0.18)';
          ctx.fill();
        }
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
        hexPath(ctx, s.x, s.y, r);
        ctx.fillStyle = hexA(col, 0.13);
        ctx.fill();
        const nbs = y & 1 ? EDGE_NEIGHBOR_ODD : EDGE_NEIGHBOR_EVEN;
        for (let e = 0; e < 6; e++) {
          const nx = x + nbs[e][0];
          const ny = y + nbs[e][1];
          const nOwner = nx < 0 || ny < 0 || nx >= W || ny >= H ? -2 : v.owner[ny * W + nx] - 1;
          if (nOwner === owner) continue;
          const [ax, ay] = corner(s.x, s.y, r * 0.97, e);
          const [bx, by] = corner(s.x, s.y, r * 0.97, e + 1);
          ctx.strokeStyle = hexA(col, 0.95);
          ctx.lineWidth = Math.max(2, 3.2 * z);
          ctx.beginPath();
          ctx.moveTo(ax, ay);
          ctx.lineTo(bx, by);
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

    // 4. roads, improvements, resources
    for (let y = minRow; y <= maxRow; y++) {
      for (let x = minCol; x <= maxCol; x++) {
        const i = y * W + x;
        if (v.state[i] === 0) continue;
        const wp = cam.worldOfTile(x, y);
        const s = cam.toScreen(wp.x, wp.y);
        if (v.road[i] === 1) this.drawRoads(ctx, v, x, y, s.x, s.y, z);
      }
    }
    for (let y = minRow; y <= maxRow; y++) {
      for (let x = minCol; x <= maxCol; x++) {
        const i = y * W + x;
        if (v.state[i] === 0) continue;
        const wp = cam.worldOfTile(x, y);
        const s = cam.toScreen(wp.x, wp.y);
        const imp = v.improvement[i];
        if (imp === 1) this.drawFarm(ctx, s.x, s.y, z);
        else if (imp === 2) this.drawMine(ctx, s.x, s.y, z);
        const res = v.resource[i];
        if (res > 0 && z > 0.45) this.drawBadge(ctx, RESOURCE_LIST[res - 1], s.x + HEX * 0.5 * z, s.y + HEX * 0.42 * z, z, RESOURCES[RESOURCE_LIST[res - 1]].strategic);
        if (v.hut[i] === 1 && z > 0.4) this.drawBadge(ctx, 'hut', s.x - HEX * 0.1 * z, s.y + HEX * 0.1 * z, z * 1.25, false, true);
        if (st.lockedTiles?.has(i)) this.drawBadge(ctx, 'tile', s.x, s.y - HEX * 0.46 * z, z, false, true);
      }
    }

    // 5. fog
    for (let y = minRow; y <= maxRow; y++) {
      for (let x = minCol; x <= maxCol; x++) {
        if (v.state[y * W + x] !== 1) continue;
        const wp = cam.worldOfTile(x, y);
        const s = cam.toScreen(wp.x, wp.y);
        hexPath(ctx, s.x, s.y, HEX * z * 1.01);
        ctx.fillStyle = 'rgba(8,12,22,0.52)';
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

    // 8. cities
    const sorted: Array<{ y: number; draw: () => void }> = [];
    for (const c of v.cities) {
      if (c.x < minCol - 1 || c.x > maxCol + 1 || c.y < minRow - 1 || c.y > maxRow + 1) continue;
      sorted.push({ y: c.y * 10, draw: () => this.drawCity(ctx, v, c, z, st) });
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

  private emoji(ctx: CanvasRenderingContext2D, e: string, x: number, y: number, size: number, shadow = false): void {
    ctx.font = `${Math.max(6, Math.round(size))}px ${EMOJI_FONT}`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    if (shadow) {
      ctx.save();
      ctx.shadowColor = 'rgba(0,0,0,0.6)';
      ctx.shadowBlur = 3;
      ctx.fillText(e, x, y);
      ctx.restore();
    } else ctx.fillText(e, x, y);
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
    ctx.strokeStyle = 'rgba(92,66,40,0.9)';
    ctx.lineWidth = Math.max(2, 4 * z);
    ctx.lineCap = 'round';
    let any = false;
    for (let e = 0; e < 6; e++) {
      const nx = x + nbs[e][0];
      const ny = y + nbs[e][1];
      if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
      const ni = ny * W + nx;
      const isCity = v.cities.some((c) => c.x === nx && c.y === ny);
      if (v.road[ni] !== 1 && !isCity) continue;
      if (v.state[ni] === 0) continue;
      // neighbour direction vector (edge normal)
      const [ax, ay] = corner(0, 0, HEX * z, e);
      const [bx, by] = corner(0, 0, HEX * z, e + 1);
      const mx = (ax + bx) / 2;
      const my = (ay + by) / 2;
      ctx.beginPath();
      ctx.moveTo(sx, sy);
      ctx.lineTo(sx + mx, sy + my);
      ctx.stroke();
      any = true;
    }
    if (!any) {
      ctx.beginPath();
      ctx.arc(sx, sy, 2.5 * z, 0, Math.PI * 2);
      ctx.fillStyle = 'rgba(92,66,40,0.9)';
      ctx.fill();
    }
  }

  private drawFarm(ctx: CanvasRenderingContext2D, sx: number, sy: number, z: number): void {
    ctx.save();
    ctx.strokeStyle = 'rgba(240,226,120,0.85)';
    ctx.lineWidth = Math.max(1, 1.6 * z);
    for (let i = -2; i <= 2; i++) {
      ctx.beginPath();
      ctx.moveTo(sx - HEX * 0.5 * z + i * 6 * z, sy + HEX * 0.1 * z + i * 3 * z);
      ctx.lineTo(sx - HEX * 0.1 * z + i * 6 * z, sy + HEX * 0.42 * z + i * 3 * z);
      ctx.stroke();
    }
    ctx.restore();
  }

  private drawMine(ctx: CanvasRenderingContext2D, sx: number, sy: number, z: number): void {
    ctx.save();
    const x = sx - HEX * 0.4 * z;
    const y = sy + HEX * 0.4 * z;
    ctx.fillStyle = '#3a2d22';
    ctx.beginPath();
    ctx.moveTo(x - 9 * z, y + 6 * z);
    ctx.lineTo(x - 7 * z, y - 6 * z);
    ctx.lineTo(x + 7 * z, y - 6 * z);
    ctx.lineTo(x + 9 * z, y + 6 * z);
    ctx.closePath();
    ctx.fill();
    ctx.fillStyle = '#120d08';
    ctx.fillRect(x - 4 * z, y - 1 * z, 8 * z, 7 * z);
    ctx.restore();
  }

  private drawCity(ctx: CanvasRenderingContext2D, v: GameView, c: ViewCity, z: number, st: RenderState): void {
    const wp = this.cam.worldOfTile(c.x, c.y);
    const s = this.cam.toScreen(wp.x, wp.y);
    const col = civColor(v, c.owner);
    const fogged = v.state[c.y * v.settings.width + c.x] === 1;
    ctx.save();
    if (fogged) ctx.globalAlpha = 0.8;
    // platform
    ctx.beginPath();
    ctx.ellipse(s.x, s.y + HEX * 0.2 * z, HEX * 0.66 * z, HEX * 0.4 * z, 0, 0, Math.PI * 2);
    ctx.fillStyle = hexA(col, 0.38);
    ctx.fill();
    ctx.strokeStyle = hexA(col, 0.95);
    ctx.lineWidth = Math.max(1.5, 2.2 * z);
    ctx.stroke();
    // walls
    if (c.mine && c.buildings?.includes('walls')) {
      ctx.strokeStyle = '#c9c2b0';
      ctx.lineWidth = Math.max(2, 3.4 * z);
      ctx.setLineDash([5 * z, 2.5 * z]);
      ctx.beginPath();
      ctx.ellipse(s.x, s.y + HEX * 0.2 * z, HEX * 0.74 * z, HEX * 0.47 * z, 0, 0, Math.PI * 2);
      ctx.stroke();
      ctx.setLineDash([]);
    }
    // houses
    const n = Math.min(7, 2 + Math.floor(c.pop / 2));
    const spots = [[0, -4], [-13, 2], [13, 3], [-7, 11], [8, 12], [-20, 9], [21, 10]];
    for (let i = n - 1; i >= 0; i--) {
      const [hx, hy] = spots[i];
      const x = s.x + hx * z * 0.9;
      const y = s.y + hy * z * 0.9;
      const big = i === 0;
      const w = (big ? 15 : 10) * z;
      const h = (big ? 13 : 9) * z;
      ctx.fillStyle = '#e8dcc0';
      ctx.fillRect(x - w / 2, y - h / 2, w, h);
      ctx.fillStyle = hexA(col, 1);
      ctx.beginPath();
      ctx.moveTo(x - w / 2 - 1.5 * z, y - h / 2);
      ctx.lineTo(x, y - h / 2 - (big ? 9 : 6) * z);
      ctx.lineTo(x + w / 2 + 1.5 * z, y - h / 2);
      ctx.closePath();
      ctx.fill();
      ctx.fillStyle = 'rgba(40,30,20,0.55)';
      ctx.fillRect(x - 1.4 * z, y + h / 2 - 4.5 * z, 2.8 * z, 4.5 * z);
    }
    if (c.wonders.length) drawIcon(ctx, c.wonders[0], s.x + 25 * z, s.y - 12 * z, 17 * z, '#f2d98c', '#f2d98c', 1.9);
    ctx.restore();

    // banner: pop shield, name, and what the city is building
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
    const by = s.y - HEX * 1.02 * z - bh / 2 + 8 * z;
    ctx.save();
    if (fogged) ctx.globalAlpha = 0.85;
    // body
    ctx.beginPath();
    ctx.moveTo(bx + 6, by);
    ctx.lineTo(bx + bw - 6, by);
    ctx.lineTo(bx + bw, by + bh / 2);
    ctx.lineTo(bx + bw - 6, by + bh);
    ctx.lineTo(bx + 6, by + bh);
    ctx.lineTo(bx, by + bh / 2);
    ctx.closePath();
    const g = ctx.createLinearGradient(0, by, 0, by + bh);
    g.addColorStop(0, 'rgba(24,31,43,0.96)');
    g.addColorStop(1, 'rgba(8,11,17,0.96)');
    ctx.fillStyle = g;
    ctx.fill();
    ctx.lineWidth = 1.6;
    ctx.strokeStyle = hexA(col, 1);
    ctx.stroke();
    // population shield
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
    ctx.fillStyle = '#0c1017';
    ctx.font = `700 ${fs}px ${DISPLAY_FONT}`;
    ctx.fillText(String(c.pop), bx + pr + 1, by + bh / 2 + 0.5);
    ctx.textAlign = 'left';
    ctx.fillStyle = '#efe7d0';
    ctx.font = `600 ${fs}px ${DISPLAY_FONT}`;
    ctx.fillText(label, bx + left + 2, by + bh / 2 + 0.5);
    if (head) {
      drawIcon(ctx, head.id, bx + bw - bh * 0.62, by + bh / 2, bh * 0.62, '#e5974a', '#e5974a', 1.8);
    }
    // damage bar
    if (c.hp < c.maxHp && c.visible) {
      ctx.fillStyle = 'rgba(0,0,0,0.65)';
      ctx.fillRect(bx + 4, by + bh + 2, bw - 8, 4);
      ctx.fillStyle = c.hp / c.maxHp > 0.5 ? '#82d48d' : c.hp / c.maxHp > 0.25 ? '#e8c14a' : '#ec6c5d';
      ctx.fillRect(bx + 4, by + bh + 2, (bw - 8) * Math.max(0, c.hp / c.maxHp), 4);
    }
    ctx.restore();
    void st;
  }

  /** Unit flag: a shield-shaped banner in the civilisation colour carrying the unit's glyph. */
  private drawUnit(ctx: CanvasRenderingContext2D, v: GameView, u: ViewUnit, tx: number, ty: number, z: number, st: RenderState, ghost = false): void {
    const def = UNITS[u.type];
    const wp = this.cam.worldOfTile(tx, ty);
    const s = this.cam.toScreen(wp.x, wp.y);
    const col = civColor(v, u.owner);
    const civ = !!def.civilian;
    const onCity = v.cities.some((c) => c.x === Math.round(tx) && c.y === Math.round(ty));
    const ox = (civ ? -14 : onCity ? 16 : 0) * z;
    const oy = (civ ? 10 : onCity ? 12 : -4) * z;
    const x = s.x + ox;
    const y = s.y + oy;
    const mine = u.owner === v.me;
    const selected = st.selectedUnit?.id === u.id;
    const k = Math.max(0.75, z) * (selected ? 1.1 : 1);
    const w = 28 * k;
    const h = 34 * k;
    ctx.save();
    // drop shadow
    ctx.beginPath();
    ctx.ellipse(x, y + h * 0.58, w * 0.5, h * 0.12, 0, 0, Math.PI * 2);
    ctx.fillStyle = 'rgba(0,0,0,0.35)';
    ctx.fill();
    const flag = () => {
      ctx.beginPath();
      ctx.moveTo(x - w / 2, y - h / 2);
      ctx.lineTo(x + w / 2, y - h / 2);
      ctx.lineTo(x + w / 2, y + h * 0.14);
      ctx.lineTo(x, y + h / 2);
      ctx.lineTo(x - w / 2, y + h * 0.14);
      ctx.closePath();
    };
    flag();
    const g = ctx.createLinearGradient(x - w / 2, y - h / 2, x + w / 2, y + h / 2);
    g.addColorStop(0, shade(col, 0.22));
    g.addColorStop(0.55, col);
    g.addColorStop(1, shade(col, -0.28));
    ctx.fillStyle = g;
    ctx.fill();
    ctx.lineWidth = Math.max(1.6, 2 * k);
    ctx.strokeStyle = selected ? '#f2d98c' : 'rgba(5,8,13,0.85)';
    ctx.stroke();
    // inner rule
    ctx.save();
    flag();
    ctx.clip();
    ctx.lineWidth = 1;
    ctx.strokeStyle = 'rgba(255,255,255,0.22)';
    ctx.strokeRect(x - w / 2 + 3 * k, y - h / 2 + 3 * k, w - 6 * k, h);
    ctx.restore();
    const ink = lum(col) > 0.62 ? '#161b26' : '#ffffff';
    drawIcon(ctx, def.id, x, y - h * 0.06, 19 * k, ink, ink, 1.9);
    // hit points
    if (u.hp < 100 && !ghost) {
      const bw = w * 0.9;
      ctx.fillStyle = 'rgba(0,0,0,0.75)';
      ctx.fillRect(x - bw / 2, y + h * 0.58 + 1, bw, 4);
      ctx.fillStyle = u.hp > 60 ? '#82d48d' : u.hp > 30 ? '#e8c14a' : '#ec6c5d';
      ctx.fillRect(x - bw / 2, y + h * 0.58 + 1, (bw * u.hp) / 100, 4);
    }
    if (!ghost && mine) {
      // status chip
      let chip: string | null = null;
      if (u.auto === 'work') chip = 'auto';
      else if (u.auto === 'explore') chip = 'explore';
      else if (u.order?.kind === 'fortify' || u.fortified) chip = 'fortify';
      else if (u.order?.kind === 'sleep') chip = 'sleep';
      else if (u.order?.kind === 'build') chip = u.order.what;
      else if (u.order?.kind === 'goto') chip = 'chevron';
      if (chip) {
        const r = 7.5 * Math.max(0.85, z);
        const cx = x + w / 2 - 1;
        const cy = y - h / 2 + 1;
        ctx.beginPath();
        ctx.arc(cx, cy, r, 0, Math.PI * 2);
        ctx.fillStyle = '#0c1017';
        ctx.fill();
        ctx.lineWidth = 1.3;
        ctx.strokeStyle = '#d4af5a';
        ctx.stroke();
        drawIcon(ctx, chip, cx, cy, r * 1.35, '#f2d98c', '#f2d98c', 2.1);
      }
      if (u.level > 0) {
        for (let i = 0; i < u.level; i++) drawIcon(ctx, 'star', x - w / 2 + 5 * k + i * 6 * k, y - h / 2 - 3 * k, 8 * k, '#ffe08a', '#ffe08a', 1.2);
      }
      if ((u.mp ?? 0) > 0 && !u.order && !u.auto && !u.fortified && !u.skipped && !selected) {
        ctx.save();
        flag();
        ctx.strokeStyle = `rgba(255,255,255,${0.4 + 0.35 * Math.sin(st.now / 280)})`;
        ctx.lineWidth = 2.6;
        ctx.stroke();
        ctx.restore();
      }
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
