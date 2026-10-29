import type { Pos } from '../../shared/hex';
import { UNITS, TERRAIN } from '../../shared/data';
import { TERRAIN_LIST, type ViewCity, type ViewUnit } from '../../shared/view';
import { drawIcon } from '../icons';
import { DISPLAY_FONT, civColor, hexA, lum, roundRect, shade, tileYieldFromView, type RenderState } from '../render';
import { HEX, hexPath } from '../sprites';
import type { GLRenderer } from './glrenderer';
import { tileXZf } from './terrain';

/** Everything that stays flat on the glass: grid, reach, paths, selection, banners, flags and floating numbers. */
export class Overlay {
  /** where each city banner was drawn this frame, for click handling */
  rects = new Map<number, { x: number; y: number; w: number; h: number }>();
  constructor(private r: GLRenderer) {}

  private tileAt(x: number, y: number, lift = 0): { x: number; y: number } {
    const [X, Z] = tileXZf(x, y);
    return this.r.screenOf(X, Z, lift + Math.max(0, this.r.terrain.heightAt(X, Z)));
  }

  draw(ctx: CanvasRenderingContext2D, st: RenderState): void {
    const cam = this.r.cam;
    const v = st.view;
    const W = v.settings.width;
    const H = v.settings.height;
    const z = cam.zoom;
    const tl = cam.toWorld(0, 0);
    const br = cam.toWorld(cam.w, cam.h);
    const minRow = Math.max(0, Math.floor((tl.y - HEX * 3) / (HEX * 1.5)));
    const maxRow = Math.min(H - 1, Math.ceil((br.y + HEX * 3) / (HEX * 1.5)));
    const minCol = Math.max(0, Math.floor((tl.x - HEX * 2) / (HEX * 1.732)) - 1);
    const maxCol = Math.min(W - 1, Math.ceil((br.x + HEX * 2) / (HEX * 1.732)));
    ctx.lineJoin = 'round';
    ctx.lineCap = 'round';

    // worked tiles of an open city
    if (st.workedTiles) {
      for (const i of st.workedTiles) {
        const x = i % W;
        const y = (i / W) | 0;
        const s = this.tileAt(x, y);
        hexPath(ctx, s.x, s.y, HEX * z * 0.93);
        ctx.fillStyle = 'rgba(255,230,140,0.2)';
        ctx.fill();
        ctx.strokeStyle = 'rgba(255,230,140,0.55)';
        ctx.lineWidth = 1.5;
        ctx.stroke();
      }
    }

    // grid lines
    if (st.showGrid && z > 0.5) {
      ctx.strokeStyle = 'rgba(0,0,0,0.2)';
      ctx.lineWidth = 1;
      for (let y = minRow; y <= maxRow; y++) {
        for (let x = minCol; x <= maxCol; x++) {
          if (v.state[y * W + x] === 0) continue;
          const s = this.tileAt(x, y);
          hexPath(ctx, s.x, s.y, HEX * z);
          ctx.stroke();
        }
      }
    }

    // tile yields
    if (st.showYields && z > 0.7) {
      ctx.font = `700 ${Math.round(12 * Math.min(1.4, z))}px system-ui, sans-serif`;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      for (let y = minRow; y <= maxRow; y++) {
        for (let x = minCol; x <= maxCol; x++) {
          const i = y * W + x;
          if (v.state[i] === 0) continue;
          const t = TERRAIN[TERRAIN_LIST[v.terrain[i]]];
          if (!t.workable) continue;
          const s = this.tileAt(x, y);
          const yy = tileYieldFromView(v, i);
          const parts: Array<[number, string]> = [[yy.food, '#8be26b'], [yy.prod, '#f0a24a'], [yy.gold, '#ffd84a']];
          let ox = -((parts.filter((p) => p[0] > 0).length - 1) * 7.5 * z);
          for (const [n, col] of parts) {
            if (n <= 0) continue;
            const py = s.y + HEX * 0.5 * z * cam.tilt;
            ctx.lineWidth = 3.2;
            ctx.strokeStyle = 'rgba(0,0,0,0.75)';
            ctx.strokeText(String(n), s.x + ox, py);
            ctx.fillStyle = col;
            ctx.fillText(String(n), s.x + ox, py);
            ox += 15 * z;
          }
        }
      }
    }

    // move range, path and targets
    if (st.reach) {
      for (const [k] of st.reach) {
        const x = k % W;
        const y = (k / W) | 0;
        if (x < minCol || x > maxCol || y < minRow || y > maxRow) continue;
        const s = this.tileAt(x, y);
        hexPath(ctx, s.x, s.y, HEX * z * 0.9);
        ctx.fillStyle = 'rgba(255,255,255,0.14)';
        ctx.fill();
        ctx.strokeStyle = 'rgba(255,255,255,0.5)';
        ctx.lineWidth = 1.3;
        ctx.stroke();
      }
    }
    for (const k of st.attackTargets) {
      const x = k % W;
      const y = (k / W) | 0;
      const s = this.tileAt(x, y);
      const pulse = 0.22 + 0.14 * Math.sin(st.now / 180);
      hexPath(ctx, s.x, s.y, HEX * z * 0.92);
      ctx.fillStyle = `rgba(255,70,50,${pulse})`;
      ctx.fill();
      ctx.strokeStyle = 'rgba(255,100,80,0.95)';
      ctx.lineWidth = 2.4;
      ctx.stroke();
    }
    if (st.path && st.path.length) this.drawPath(ctx, st, st.path, z);

    // hover & selection
    if (st.hover) {
      const s = this.tileAt(st.hover.x, st.hover.y);
      hexPath(ctx, s.x, s.y, HEX * z * 0.96);
      ctx.strokeStyle = 'rgba(255,255,255,0.85)';
      ctx.lineWidth = 2;
      ctx.stroke();
    }
    const sel = st.selectedUnit ?? null;
    const selTile = sel ?? st.selectedCity ?? null;
    if (selTile && !sel) {
      const s = this.tileAt(selTile.x, selTile.y);
      hexPath(ctx, s.x, s.y, HEX * z * 0.98);
      ctx.strokeStyle = `rgba(255,214,90,${0.7 + 0.3 * Math.sin(st.now / 220)})`;
      ctx.lineWidth = 3.2;
      ctx.stroke();
    }

    // brief flashes, expanding rings
    for (const [k, until] of this.r.anim.flashes) {
      const x = k % 1000;
      const y = Math.floor(k / 1000);
      const s = this.tileAt(x, y);
      const left = (until - st.now) / 260;
      hexPath(ctx, s.x, s.y, HEX * z * 0.95);
      ctx.fillStyle = `rgba(255,255,255,${Math.max(0, Math.min(0.26, left * 0.26))})`;
      ctx.fill();
    }
    for (const rg of this.r.anim.rings) {
      const k = (st.now - rg.t0) / rg.dur;
      if (k < 0 || k > 1) continue;
      const s = this.tileAt(rg.x, rg.y);
      ctx.beginPath();
      ctx.ellipse(s.x, s.y, HEX * z * (0.4 + k * rg.max), HEX * z * (0.4 + k * rg.max) * cam.tilt, 0, 0, Math.PI * 2);
      ctx.strokeStyle = hexA(rg.color, 1 - k);
      ctx.lineWidth = 4 * z * (1 - k) + 1;
      ctx.stroke();
    }

    // flags over units, banners over cities
    this.rects.clear();
    const items: Array<{ y: number; draw: () => void }> = [];
    for (const u of v.units) {
      const a = this.r.units.anchors.get(u.id);
      if (!a) continue;
      items.push({ y: a.y, draw: () => this.drawUnitFlag(ctx, st, u, a) });
    }
    for (const c of v.cities) {
      if (v.state[c.y * W + c.x] === 0 || c.x < minCol - 1 || c.x > maxCol + 1) continue;
      items.push({ y: 1e6 + c.y, draw: () => this.drawBanner(ctx, st, c) });
    }
    items.sort((a, b) => a.y - b.y);
    for (const it of items) it.draw();

    // floating numbers
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    for (const f of this.r.anim.floaters) {
      const k = (st.now - f.t0) / f.dur;
      if (k < 0 || k > 1) continue;
      const s = this.tileAt(f.x, f.y, 0.8 + k * 0.9);
      ctx.font = `800 ${Math.round(18 * Math.max(0.85, z))}px system-ui, sans-serif`;
      ctx.globalAlpha = 1 - k * k;
      ctx.lineWidth = 4;
      ctx.strokeStyle = 'rgba(0,0,0,0.75)';
      ctx.strokeText(f.text, s.x, s.y);
      ctx.fillStyle = f.color;
      ctx.fillText(f.text, s.x, s.y);
      ctx.globalAlpha = 1;
    }
    for (const i of st.lockedTiles ?? []) {
      const s = this.tileAt(i % W, (i / W) | 0, 0.1);
      this.badge(ctx, 'tile', s.x, s.y - HEX * 0.3 * z, z);
    }
  }

  private badge(ctx: CanvasRenderingContext2D, name: string, x: number, y: number, z: number): void {
    const r = 10.5 * Math.max(0.8, z);
    ctx.beginPath();
    ctx.arc(x, y, r, 0, Math.PI * 2);
    ctx.fillStyle = 'rgba(8,11,17,0.85)';
    ctx.fill();
    ctx.lineWidth = 1.4;
    ctx.strokeStyle = '#f2d98c';
    ctx.stroke();
    drawIcon(ctx, name, x, y, r * 1.35, '#f2d98c', '#e9e1cc', 1.8);
  }

  /** A civilisation-coloured pennant carrying the unit's glyph, with health and status marks. */
  private drawUnitFlag(ctx: CanvasRenderingContext2D, st: RenderState, u: ViewUnit, a: { x: number; y: number; z: number; hgt: number }): void {
    const cam = this.r.cam;
    const v = st.view;
    const s = this.r.screenOf(a.x, a.z, a.y + a.hgt);
    const col = civColor(v, u.owner);
    const mine = u.owner === v.me;
    const selected = st.selectedUnit?.id === u.id;
    const k = Math.max(0.55, Math.min(0.95, cam.zoom * 0.8)) * (selected ? 1.12 : 1);
    const w = 22 * k;
    const h = 27 * k;
    const x = s.x;
    const y = s.y - h * 0.5;
    ctx.save();
    const flag = () => {
      ctx.beginPath();
      ctx.moveTo(x - w / 2, y - h / 2);
      ctx.lineTo(x + w / 2, y - h / 2);
      ctx.lineTo(x + w / 2, y + h * 0.16);
      ctx.lineTo(x, y + h / 2);
      ctx.lineTo(x - w / 2, y + h * 0.16);
      ctx.closePath();
    };
    ctx.shadowColor = 'rgba(0,0,0,0.5)';
    ctx.shadowBlur = 4;
    ctx.shadowOffsetY = 1.5;
    flag();
    const g = ctx.createLinearGradient(x - w / 2, y - h / 2, x + w / 2, y + h / 2);
    g.addColorStop(0, shade(col, 0.22));
    g.addColorStop(0.55, col);
    g.addColorStop(1, shade(col, -0.28));
    ctx.fillStyle = g;
    ctx.fill();
    ctx.shadowColor = 'transparent';
    ctx.lineWidth = selected ? 2.2 : 1.5;
    ctx.strokeStyle = selected ? '#f2d98c' : 'rgba(5,8,13,0.88)';
    ctx.stroke();
    const ink = lum(col) > 0.62 ? '#161b26' : '#ffffff';
    drawIcon(ctx, u.type, x, y - h * 0.05, 15 * k, ink, ink, 1.8);
    if (u.hp < 100) {
      const bw = w * 1.15;
      ctx.fillStyle = 'rgba(0,0,0,0.8)';
      ctx.fillRect(x - bw / 2, y + h / 2 + 2, bw, 4);
      ctx.fillStyle = u.hp > 60 ? '#82d48d' : u.hp > 30 ? '#e8c14a' : '#ec6c5d';
      ctx.fillRect(x - bw / 2 + 0.5, y + h / 2 + 2.5, ((bw - 1) * u.hp) / 100, 3);
    }
    if (mine) {
      let chip: string | null = null;
      if (u.auto === 'work') chip = 'auto';
      else if (u.auto === 'explore') chip = 'explore';
      else if (u.order?.kind === 'fortify' || u.fortified) chip = 'fortify';
      else if (u.order?.kind === 'sleep') chip = 'sleep';
      else if (u.order?.kind === 'build') chip = u.order.what;
      else if (u.order?.kind === 'goto') chip = 'chevron';
      if (chip) {
        const r = 7 * Math.max(0.85, k);
        const cx = x + w / 2 + 1;
        const cy = y - h / 2 + 2;
        ctx.beginPath();
        ctx.arc(cx, cy, r, 0, Math.PI * 2);
        ctx.fillStyle = '#0c1017';
        ctx.fill();
        ctx.lineWidth = 1.2;
        ctx.strokeStyle = '#d4af5a';
        ctx.stroke();
        drawIcon(ctx, chip, cx, cy, r * 1.35, '#f2d98c', '#f2d98c', 2);
      }
      for (let i = 0; i < u.level; i++) drawIcon(ctx, 'star', x - w / 2 + 4 * k + i * 6 * k, y - h / 2 - 4 * k, 8 * k, '#ffe08a', '#ffe08a', 1.2);
      if ((u.mp ?? 0) > 0 && !u.order && !u.auto && !u.fortified && !u.skipped && !selected) {
        flag();
        ctx.strokeStyle = `rgba(255,255,255,${0.4 + 0.35 * Math.sin(st.now / 280)})`;
        ctx.lineWidth = 2.4;
        ctx.stroke();
      }
    }
    ctx.restore();
  }

  /** City banner: population shield, name plate and the current build. */
  private drawBanner(ctx: CanvasRenderingContext2D, st: RenderState, c: ViewCity): void {
    const v = st.view;
    const cam = this.r.cam;
    const z = cam.zoom;
    const [X, Z] = tileXZf(c.x, c.y);
    const gy = Math.max(0, this.r.terrain.heightAt(X, Z));
    const s = this.r.screenOf(X, Z, gy + 0.98 + 0.12 * Math.min(3, Math.floor(c.pop / 4)));
    const col = civColor(v, c.owner);
    const fogged = v.state[c.y * v.settings.width + c.x] === 1;
    const fs = Math.max(11, Math.min(15, 12.5 * z + 1));
    ctx.font = `600 ${fs}px ${DISPLAY_FONT}`;
    const label = (c.capital ? '★ ' : '') + c.name;
    const tw = ctx.measureText(label).width;
    const head = c.mine ? c.queue?.[0] : null;
    const bh = fs + 12;
    const left = bh + 2;
    const right = head ? bh : 8;
    const bw = left + tw + right + 8;
    const bx = s.x - bw / 2;
    const by = s.y - bh - 2;
    this.rects.set(c.id, { x: bx, y: by, w: bw, h: bh });
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

  private drawPath(ctx: CanvasRenderingContext2D, st: RenderState, path: Array<Pos & { cost: number }>, z: number): void {
    const u = st.selectedUnit;
    if (!u) return;
    const full = UNITS[u.type].moves * 2;
    let mp = u.mp ?? full;
    ctx.save();
    ctx.strokeStyle = 'rgba(255,255,255,0.92)';
    ctx.lineWidth = Math.max(2, 3 * z);
    ctx.setLineDash([6 * z, 5 * z]);
    ctx.beginPath();
    const start = this.tileAt(u.x, u.y);
    ctx.moveTo(start.x, start.y);
    let turn = 1;
    let prev: Pos = { x: u.x, y: u.y };
    for (const p of path) {
      const c = st.vm.stepCost(u, prev.x, prev.y, p.x, p.y);
      if (mp <= 0) {
        turn++;
        mp = full;
      }
      mp -= c;
      const sc = this.tileAt(p.x, p.y);
      ctx.lineTo(sc.x, sc.y);
      prev = p;
    }
    ctx.stroke();
    ctx.setLineDash([]);
    const last = path[path.length - 1];
    const ls = this.tileAt(last.x, last.y);
    ctx.beginPath();
    ctx.arc(ls.x, ls.y, 6 * Math.max(0.8, z), 0, Math.PI * 2);
    ctx.fillStyle = '#fff';
    ctx.fill();
    if (turn > 1) {
      ctx.font = `800 ${Math.round(13 * Math.max(0.9, z))}px system-ui, sans-serif`;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      roundRect(ctx, ls.x - 11, ls.y - 34, 22, 20, 6);
      ctx.fillStyle = 'rgba(14,20,34,0.92)';
      ctx.fill();
      ctx.fillStyle = '#ffd84a';
      ctx.fillText(`${turn}`, ls.x, ls.y - 24);
    }
    ctx.restore();
  }
}
