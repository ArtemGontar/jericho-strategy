import { hexDistance, type Pos } from '../shared/hex';
import type { GameEvent, Command } from '../shared/game';
import { BUILDINGS, CIV_BY_ID, IMPROVEMENTS, TECH_BY_ID, TERRAIN, UNITS, RESOURCES } from '../shared/data';
import { RESOURCE_LIST, TERRAIN_LIST, type GameView, type ViewCity, type ViewUnit } from '../shared/view';
import { ViewMap } from '../shared/viewpath';
import type { AttackPreview } from '../shared/combat';
import type { WorkKind } from '../shared/types';
import { Animator } from './anim';
import { Camera, MapRenderer, civColor, tileYieldFromView, type RenderState } from './render';
import { HEX, HEX_W } from './sprites';
import { sfx, isMuted, setMuted } from './audio';
import { btn, clear, fmtSigned, h, put } from './ui/dom';
import type { GameTransport } from './transport';
import { renderCityPanel } from './ui/citypanel';
import { showHelp, showMenu, showTechTree, showDiplomacy, showVictory, showCities, showLog } from './ui/modals';

export interface GameScreenOptions {
  /** called when the user leaves to the main menu */
  onExit: () => void;
  /** online only */
  invite?: { code: string };
  hostActions?: { forceEnd: () => void; takeover: (slot: number) => void; rematch: () => void } | null;
  sendChat?: (text: string) => void;
}

const SAVE_TOGGLES = 'jericho.prefs';

export class GameScreen {
  root: HTMLElement;
  canvas: HTMLCanvasElement;
  mini: HTMLCanvasElement;
  cam = new Camera();
  anim = new Animator();
  renderer: MapRenderer;
  view!: GameView;
  vm!: ViewMap;
  selUnitId: number | null = null;
  openCityId: number | null = null;
  hover: Pos | null = null;
  reach: Map<number, number> | null = null;
  path: Array<Pos & { cost: number }> | null = null;
  attackTargets = new Set<number>();
  pending: Pos | null = null;
  showYields = false;
  showGrid = true;
  busy = false;
  noteHistory: Array<{ id: number; turn: number; text: string; kind: string; at?: Pos }> = [];
  private lastNoteId = -1;
  private lastTurn = 0;
  private firstView = true;
  private raf = 0;
  private dirty = true;
  private modal: HTMLElement | null = null;
  private modalKind = '';
  private topbar: HTMLElement;
  private bottomLeft: HTMLElement;
  private endturn: HTMLElement;
  private toasts: HTMLElement;
  private sidepanel: HTMLElement;
  private tooltip: HTMLElement;
  private connBanner: HTMLElement;
  private thinking: HTMLElement | null = null;
  private previewCache = new Map<string, AttackPreview | null>();
  private previewKey = '';
  private pendingCityOpen: Pos | null = null;
  private camTarget: { x: number; y: number } | null = null;
  private timerEnd = 0;
  private timerTick: ReturnType<typeof setInterval> | null = null;
  private cleanup: Array<() => void> = [];
  private overShown = false;
  private chatBox: HTMLElement | null = null;
  private chatLog: HTMLElement | null = null;
  chatLines: Array<{ from: string; text: string; color: string }> = [];

  constructor(parent: HTMLElement, public transport: GameTransport, public opts: GameScreenOptions) {
    try {
      const prefs = JSON.parse(localStorage.getItem(SAVE_TOGGLES) ?? '{}') as { yields?: boolean; grid?: boolean };
      this.showYields = !!prefs.yields;
      this.showGrid = prefs.grid !== false;
    } catch { /* ignore */ }
    this.root = h('div', { class: 'game' });
    this.canvas = h('canvas', { class: 'map' });
    this.mini = h('canvas', { class: 'minimap' });
    this.topbar = h('div', { class: 'topbar' });
    this.bottomLeft = h('div', { class: 'bottom-left' });
    this.endturn = h('div', { class: 'bottom-right' });
    this.toasts = h('div', { class: 'toasts' });
    this.sidepanel = h('div', { class: 'sidepanel hidden' });
    this.tooltip = h('div', { class: 'tooltip hidden' });
    this.connBanner = h('div', { class: 'conn hidden' }, 'Reconnecting…');
    this.root.append(this.canvas, this.topbar, this.toasts, this.bottomLeft, this.endturn, this.sidepanel, this.tooltip, this.connBanner);
    parent.append(this.root);
    this.renderer = new MapRenderer(this.canvas, this.cam, this.anim);

    transport.onView = (v, ev) => this.onView(v, ev);
    transport.onStatus = (ok) => this.connBanner.classList.toggle('hidden', ok);
    transport.onError = (m) => this.toast(m, 'bad');
    transport.onChat = (from, slot, text) => this.addChat(from, slot, text);
    transport.onCurtain = (name, done) => this.showCurtain(name, done);

    this.bindInput();
    const onResize = () => {
      this.renderer.resize();
      this.dirty = true;
    };
    window.addEventListener('resize', onResize);
    this.cleanup.push(() => window.removeEventListener('resize', onResize));
    this.renderer.resize();
    this.loop = this.loop.bind(this);
    this.raf = requestAnimationFrame(this.loop);
    transport.refresh();
  }

  destroy(keepTransport = false): void {
    cancelAnimationFrame(this.raf);
    if (this.timerTick) clearInterval(this.timerTick);
    for (const c of this.cleanup) c();
    if (!keepTransport) this.transport.close();
    this.root.remove();
  }

  // ------------------------------------------------------------------ helpers
  get me(): number { return this.view.me; }
  get selUnit(): ViewUnit | null {
    return this.selUnitId === null ? null : this.view.units.find((u) => u.id === this.selUnitId && u.owner === this.me) ?? null;
  }
  get openCity(): ViewCity | null {
    return this.openCityId === null ? null : this.view.cities.find((c) => c.id === this.openCityId && c.mine) ?? null;
  }
  myUnits(): ViewUnit[] { return this.view.units.filter((u) => u.owner === this.me); }
  myCities(): ViewCity[] { return this.view.cities.filter((c) => c.mine); }
  needsOrders(u: ViewUnit): boolean {
    return u.owner === this.me && (u.mp ?? 0) > 0 && !u.order && !u.auto && !u.fortified && !u.skipped;
  }
  cityNeedsProduction(c: ViewCity): boolean { return !!c.mine && (c.queue?.length ?? 0) === 0; }

  async cmd(c: Command): Promise<boolean> {
    const res = await this.transport.send(c);
    if (!res.ok) {
      sfx('error');
      this.toast(res.error ?? 'Not possible.', 'bad');
    }
    return res.ok;
  }

  toast(text: string, kind = 'info', at?: Pos): void {
    if (!text) return;
    const t = h('div', { class: `toast ${kind}` }, text);
    if (at) t.addEventListener('click', () => this.focus(at.x, at.y));
    this.toasts.append(t);
    while (this.toasts.children.length > 6) this.toasts.firstChild?.remove();
    setTimeout(() => t.remove(), kind === 'war' || kind === 'wonder' ? 9000 : 6000);
  }

  focus(x: number, y: number, smooth = true): void {
    const p = this.cam.worldOfTile(x, y);
    if (smooth) this.camTarget = p;
    else {
      this.cam.x = p.x;
      this.cam.y = p.y;
      this.cam.clamp();
    }
    this.dirty = true;
  }

  // ------------------------------------------------------------------ view updates
  private onView(v: GameView, events: GameEvent[]): void {
    const prev = this.firstView ? null : this.view;
    this.view = v;
    this.vm = new ViewMap(v);
    this.cam.setWorld(v.settings.width, v.settings.height);
    if (events.length) this.anim.schedule(events, v.me);
    // timer
    this.timerEnd = v.timerEnds > 0 ? performance.now() + v.timerEnds : 0;
    if (this.timerEnd && !this.timerTick) this.timerTick = setInterval(() => this.updateTimerLabel(), 500);
    // notes
    const maxId = v.notes.reduce((m, n) => Math.max(m, n.id), -1);
    if (this.firstView) {
      this.lastNoteId = maxId - 2;
    }
    for (const n of v.notes) {
      if (n.id <= this.lastNoteId) continue;
      this.toast(n.text, n.kind, n.at);
      this.noteHistory.push({ id: n.id, turn: n.turn, text: n.text, kind: n.kind, at: n.at });
      if (n.kind === 'tech') sfx('tech');
      else if (n.kind === 'wonder') sfx('wonder');
      else if (n.kind === 'war') sfx('notify');
    }
    this.lastNoteId = Math.max(this.lastNoteId, maxId);

    // selection upkeep
    if (this.selUnitId !== null && !this.selUnit) this.selUnitId = null;
    if (this.openCityId !== null && !this.openCity) this.closeCity();
    const newTurn = v.turn !== this.lastTurn;
    if (this.firstView) {
      this.firstView = false;
      this.lastTurn = v.turn;
      this.initCamera();
      this.autoSelect();
    } else if (newTurn) {
      this.lastTurn = v.turn;
      this.pending = null;
      sfx('turn');
      this.toast(`Turn ${v.turn}`, 'info');
      this.autoSelect();
    }
    void prev;
    if (this.pendingCityOpen) {
      const p = this.pendingCityOpen;
      const c = v.cities.find((cc) => cc.mine && cc.x === p.x && cc.y === p.y);
      if (c) {
        this.pendingCityOpen = null;
        this.openCityId = c.id;
        this.selUnitId = null;
        this.focus(c.x, c.y);
      }
    }
    this.recomputeSelection();
    this.refreshUI();
    if (v.over && !this.overShown) {
      this.overShown = true;
      sfx(v.winner === v.me ? 'victory' : 'defeat');
      setTimeout(() => this.openModal('victory', showVictory(this)), 1400);
    }
    if (this.modalKind && this.modalKind !== 'victory' && this.modalKind !== 'curtain') this.rerenderModal();
    this.dirty = true;
  }

  private initCamera(): void {
    this.cam.zoom = Math.max(this.cam.minZoom(), Math.min(1.0, this.cam.w / 1100));
    const focus = this.myUnits().find((u) => u.type === 'settler') ?? this.myCities()[0] ?? this.myUnits()[0];
    if (focus) this.focus(focus.x, focus.y, false);
  }

  private autoSelect(): void {
    const next = this.nextUnitNeedingOrders(null);
    if (next) {
      this.selectUnit(next.id, false);
      this.focus(next.x, next.y, !this.firstView);
    }
  }

  nextUnitNeedingOrders(after: number | null): ViewUnit | null {
    const list = this.myUnits().filter((u) => this.needsOrders(u)).sort((a, b) => a.id - b.id);
    if (!list.length) return null;
    if (after === null) return list[0];
    return list.find((u) => u.id > after) ?? list[0];
  }

  selectUnit(id: number | null, center = false): void {
    this.selUnitId = id;
    this.pending = null;
    if (id !== null) {
      this.closeCity(false);
      const u = this.selUnit;
      if (u && center) this.focus(u.x, u.y);
    }
    this.recomputeSelection();
    this.refreshUI();
    this.dirty = true;
  }

  recomputeSelection(): void {
    this.reach = null;
    this.path = null;
    this.attackTargets = new Set();
    const u = this.selUnit;
    if (!u) return;
    const def = UNITS[u.type];
    if ((u.mp ?? 0) > 0 && !u.attacked) this.reach = this.vm.reachable(u);
    // attack targets
    if (!def.civilian && !u.attacked && (u.mp ?? 0) > 0) {
      const range = def.ranged > 0 ? def.range : 1;
      const W = this.view.settings.width;
      const targets = new Set<number>();
      const consider = (x: number, y: number, owner: number) => {
        if (owner === this.me) return;
        if (this.view.players[owner]?.relation !== 'war') return;
        const d = hexDistance(u.x, u.y, x, y);
        if (d >= 1 && d <= range) targets.add(y * W + x);
      };
      for (const o of this.view.units) if (this.view.state[o.y * W + o.x] === 2) consider(o.x, o.y, o.owner);
      for (const c of this.view.cities) if (this.view.state[c.y * W + c.x] === 2) consider(c.x, c.y, c.owner);
      this.attackTargets = targets;
    }
  }

  // ------------------------------------------------------------------ main loop
  private loop(): void {
    this.raf = requestAnimationFrame(this.loop);
    if (!this.view) return;
    const now = performance.now();
    if (this.camTarget) {
      const dx = this.camTarget.x - this.cam.x;
      const dy = this.camTarget.y - this.cam.y;
      if (Math.abs(dx) + Math.abs(dy) < 1.5) {
        this.cam.x = this.camTarget.x;
        this.cam.y = this.camTarget.y;
        this.camTarget = null;
      } else {
        this.cam.x += dx * 0.18;
        this.cam.y += dy * 0.18;
      }
      this.cam.clamp();
      this.dirty = true;
    }
    const animating = this.anim.update(now, sfx);
    const selected = this.selUnit || this.attackTargets.size > 0;
    if (!this.dirty && !animating && !selected && !this.showAlways) return;
    this.dirty = false;
    const city = this.openCity;
    const st: RenderState = {
      view: this.view,
      vm: this.vm,
      selectedUnit: this.selUnit,
      selectedCity: city,
      hover: this.hover,
      reach: this.reach,
      path: this.pathToDraw(),
      pathTurns: 0,
      attackTargets: this.attackTargets,
      showYields: this.showYields || !!city,
      showGrid: this.showGrid,
      workedTiles: city?.worked ? new Set(city.worked) : null,
      now,
    };
    this.renderer.draw(st);
    this.drawMinimap();
  }
  private get showAlways(): boolean { return false; }

  private pathToDraw(): Array<Pos & { cost: number }> | null {
    return this.path;
  }

  // ------------------------------------------------------------------ minimap
  private drawMinimap(): void {
    const v = this.view;
    const W = v.settings.width;
    const H = v.settings.height;
    const target = this.mini;
    const cw = target.clientWidth || 200;
    const unit = cw / (W + 0.5);
    const ch = Math.ceil(unit * (H * 0.866 + 0.2887));
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    if (target.width !== Math.round(cw * dpr) || target.height !== Math.round(ch * dpr)) {
      target.width = Math.round(cw * dpr);
      target.height = Math.round(ch * dpr);
      target.style.height = ch + 'px';
    }
    const ctx = target.getContext('2d')!;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.fillStyle = '#05080f';
    ctx.fillRect(0, 0, cw, ch);
    const sx = unit;
    const sy = unit / 0.866;
    const colors: Record<string, string> = {
      ocean: '#1b3f68', coast: '#2f7ba0', grass: '#5c9c48', plains: '#b0ab58', desert: '#d9c07d', tundra: '#c4cdcd', forest: '#3f7a3c', hills: '#8d9650', mountain: '#85827f',
    };
    for (let y = 0; y < H; y++) {
      for (let x = 0; x < W; x++) {
        const i = y * W + x;
        const s = v.state[i];
        if (s === 0) continue;
        const terr = TERRAIN_LIST[v.terrain[i]];
        let col = colors[terr];
        const owner = v.owner[i] - 1;
        const px = (x + 0.5 * (y & 1)) * sx;
        const py = y * unit * 0.866;
        ctx.fillStyle = col;
        ctx.fillRect(px, py, sx + 0.5, sy + 0.5);
        if (owner >= 0) {
          ctx.fillStyle = civColor(v, owner) + '99';
          ctx.fillRect(px, py, sx + 0.5, sy + 0.5);
        }
        if (s === 1) {
          ctx.fillStyle = 'rgba(0,0,0,0.45)';
          ctx.fillRect(px, py, sx + 0.5, sy + 0.5);
        }
      }
    }
    for (const c of v.cities) {
      const px = (c.x + 0.5 * (c.y & 1) + 0.5) * sx;
      const py = (c.y * 0.866 + 0.5) * unit;
      ctx.fillStyle = '#fff';
      ctx.fillRect(px - 2.5, py - 2.5, 5, 5);
      ctx.fillStyle = civColor(v, c.owner);
      ctx.fillRect(px - 1.5, py - 1.5, 3, 3);
    }
    for (const u of v.units) {
      if (u.owner !== this.me && UNITS[u.type].civilian) continue;
      const px = (u.x + 0.5 * (u.y & 1) + 0.5) * sx;
      const py = (u.y * 0.866 + 0.5) * unit;
      ctx.fillStyle = civColor(v, u.owner);
      ctx.fillRect(px - 1, py - 1, 2, 2);
    }
    // viewport
    const cam = this.cam;
    const tl = cam.toWorld(0, 0);
    const br = cam.toWorld(cam.w, cam.h);
    const ww = HEX_W * (W + 0.5);
    const wh = HEX * 1.5 * H + HEX * 0.5;
    ctx.strokeStyle = 'rgba(255,255,255,0.9)';
    ctx.lineWidth = 1.2;
    ctx.strokeRect((tl.x / ww) * cw, (tl.y / wh) * ch, ((br.x - tl.x) / ww) * cw, ((br.y - tl.y) / wh) * ch);
  }

  private minimapJump(ev: PointerEvent): void {
    const r = this.mini.getBoundingClientRect();
    const v = this.view;
    const ww = HEX_W * (v.settings.width + 0.5);
    const wh = HEX * 1.5 * v.settings.height + HEX * 0.5;
    this.cam.x = ((ev.clientX - r.left) / r.width) * ww;
    this.cam.y = ((ev.clientY - r.top) / r.height) * wh;
    this.camTarget = null;
    this.cam.clamp();
    this.dirty = true;
  }

  // ------------------------------------------------------------------ input
  private bindInput(): void {
    const cv = this.canvas;
    const pointers = new Map<number, { x: number; y: number }>();
    let dragging = false;
    let moved = 0;
    let startX = 0;
    let startY = 0;
    let pinchDist = 0;
    let downButton = 0;
    let minimapDrag = false;

    cv.addEventListener('contextmenu', (e) => e.preventDefault());
    cv.addEventListener('pointerdown', (e) => {
      cv.setPointerCapture(e.pointerId);
      pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
      downButton = e.button;
      moved = 0;
      startX = e.clientX;
      startY = e.clientY;
      dragging = false;
      if (pointers.size === 2) {
        const [a, b] = [...pointers.values()];
        pinchDist = Math.hypot(a.x - b.x, a.y - b.y);
      }
      this.camTarget = null;
    });
    cv.addEventListener('pointermove', (e) => {
      const rect = cv.getBoundingClientRect();
      const sx = e.clientX - rect.left;
      const sy = e.clientY - rect.top;
      const p = pointers.get(e.pointerId);
      if (p) {
        const dx = e.clientX - p.x;
        const dy = e.clientY - p.y;
        p.x = e.clientX;
        p.y = e.clientY;
        moved += Math.abs(dx) + Math.abs(dy);
        if (pointers.size === 1 && (downButton === 0 || downButton === 1 || downButton === 2) && Math.hypot(e.clientX - startX, e.clientY - startY) > 6) {
          dragging = true;
          this.cam.x -= dx / this.cam.zoom;
          this.cam.y -= dy / this.cam.zoom;
          this.cam.clamp();
          cv.classList.add('grabbing');
          this.dirty = true;
        } else if (pointers.size === 2) {
          const [a, b] = [...pointers.values()];
          const d = Math.hypot(a.x - b.x, a.y - b.y);
          if (pinchDist > 0) this.zoomAt((a.x + b.x) / 2 - rect.left, (a.y + b.y) / 2 - rect.top, d / pinchDist);
          pinchDist = d;
          dragging = true;
        }
      } else if (e.pointerType === 'mouse') {
        this.updateHover(sx, sy);
      }
    });
    const up = (e: PointerEvent) => {
      const had = pointers.delete(e.pointerId);
      cv.classList.remove('grabbing');
      if (!had) return;
      if (pointers.size === 0 && !dragging && moved < 8) {
        const rect = cv.getBoundingClientRect();
        this.onTap(e.clientX - rect.left, e.clientY - rect.top, e.button === 2 || downButton === 2 ? 'right' : e.pointerType === 'touch' ? 'touch' : 'left');
      }
      if (pointers.size < 2) pinchDist = 0;
      if (pointers.size === 0) dragging = false;
    };
    cv.addEventListener('pointerup', up);
    cv.addEventListener('pointercancel', (e) => {
      pointers.delete(e.pointerId);
      cv.classList.remove('grabbing');
    });
    cv.addEventListener('pointerleave', () => {
      if (!pointers.size) {
        this.hover = null;
        this.tooltip.classList.add('hidden');
        this.dirty = true;
      }
    });
    cv.addEventListener('wheel', (e) => {
      e.preventDefault();
      const rect = cv.getBoundingClientRect();
      this.zoomAt(e.clientX - rect.left, e.clientY - rect.top, Math.exp(-e.deltaY * 0.0015));
    }, { passive: false });

    // minimap
    this.mini.addEventListener('pointerdown', (e) => {
      minimapDrag = true;
      this.mini.setPointerCapture(e.pointerId);
      this.minimapJump(e);
    });
    this.mini.addEventListener('pointermove', (e) => { if (minimapDrag) this.minimapJump(e); });
    this.mini.addEventListener('pointerup', () => { minimapDrag = false; });

    const key = (e: KeyboardEvent) => this.onKey(e);
    window.addEventListener('keydown', key);
    this.cleanup.push(() => window.removeEventListener('keydown', key));
  }

  private zoomAt(sx: number, sy: number, factor: number): void {
    const before = this.cam.toWorld(sx, sy);
    this.cam.zoom = Math.max(this.cam.minZoom(), Math.min(2.4, this.cam.zoom * factor));
    const after = this.cam.toWorld(sx, sy);
    this.cam.x += before.x - after.x;
    this.cam.y += before.y - after.y;
    this.cam.clamp();
    this.dirty = true;
  }

  private tileAtScreen(sx: number, sy: number): Pos | null {
    return this.cam.tileAt(sx, sy, this.view.settings.width, this.view.settings.height);
  }

  private updateHover(sx: number, sy: number): void {
    const t = this.tileAtScreen(sx, sy);
    const changed = !t !== !this.hover || (t && this.hover && (t.x !== this.hover.x || t.y !== this.hover.y));
    if (!changed) {
      this.moveTooltip(sx, sy);
      return;
    }
    this.hover = t;
    this.dirty = true;
    this.path = null;
    this.tooltip.classList.add('hidden');
    const u = this.selUnit;
    if (t && u) {
      const key = t.y * this.view.settings.width + t.x;
      if (this.attackTargets.has(key)) {
        this.showAttackPreview(u, t, sx, sy);
      } else if (this.view.state[key] > 0 && (u.mp ?? 0) > 0 && !(t.x === u.x && t.y === u.y)) {
        const p = this.vm.path(u, t);
        if (p && p.length) this.path = p;
      }
    }
    this.refreshTileInfo();
    this.canvas.classList.toggle('pointer', !!t && (this.attackTargets.has(t.y * this.view.settings.width + t.x) || this.hasOwnCity(t)));
  }

  private hasOwnCity(t: Pos): boolean {
    return this.view.cities.some((c) => c.x === t.x && c.y === t.y && c.mine);
  }

  private moveTooltip(sx: number, sy: number): void {
    this.tooltip.style.left = `${sx + 18}px`;
    this.tooltip.style.top = `${sy + 18}px`;
  }

  private async showAttackPreview(u: ViewUnit, t: Pos, sx: number, sy: number): Promise<void> {
    const key = `${u.id}:${t.x}:${t.y}:${u.hp}:${this.view.turn}`;
    this.previewKey = key;
    let pv = this.previewCache.get(key);
    if (pv === undefined) {
      pv = await this.transport.preview(u.id, t);
      this.previewCache.set(key, pv);
      if (this.previewCache.size > 80) this.previewCache.clear();
    }
    if (this.previewKey !== key || !pv || !pv.ok) return;
    clear(this.tooltip);
    if (pv.capture) this.tooltip.append(h('div', null, h('b', null, 'Capture '), pv.target));
    else
      put(this.tooltip,
        h('div', null, h('b', null, UNITS[u.type].ranged > 0 ? 'Shoot ' : 'Attack '), pv.target),
        h('div', null, `Deals ~`, h('b', { class: 'pos' }, `${pv.dmgDef}`), ` of ${pv.defHp} HP`),
        pv.dmgAtt > 0 ? h('div', null, `Takes ~`, h('b', { class: 'neg' }, `${pv.dmgAtt}`)) : null,
      );
    this.tooltip.classList.remove('hidden');
    this.moveTooltip(sx, sy);
  }

  private onTap(sx: number, sy: number, mode: 'left' | 'right' | 'touch'): void {
    const t = this.tileAtScreen(sx, sy);
    if (!t) return;
    sfx('click');
    if (mode !== 'touch') this.hover = t;
    else {
      this.hover = t;
      this.refreshTileInfo();
    }
    const W = this.view.settings.width;
    const key = t.y * W + t.x;
    const own = this.myUnits().filter((u) => u.x === t.x && u.y === t.y);
    const city = this.view.cities.find((c) => c.x === t.x && c.y === t.y);
    const sel = this.selUnit;
    // banner click opens the city even when units are present
    if (city?.mine) {
      const wp = this.cam.worldOfTile(t.x, t.y);
      const s = this.cam.toScreen(wp.x, wp.y);
      if (sy < s.y - HEX * 0.45 * this.cam.zoom) {
        this.openCityPanel(city.id);
        return;
      }
    }
    if (mode === 'right') {
      if (sel) this.commandUnit(sel, t, 'left');
      return;
    }
    if (sel) {
      if (t.x === sel.x && t.y === sel.y) {
        if (own.length > 1) {
          const i = own.findIndex((u) => u.id === sel.id);
          this.selectUnit(own[(i + 1) % own.length].id);
        } else if (city?.mine) this.openCityPanel(city.id);
        else this.selectUnit(null);
        return;
      }
      if (own.length && !this.attackTargets.has(key)) {
        this.selectUnit((own.find((u) => this.needsOrders(u)) ?? own[0]).id);
        return;
      }
      this.commandUnit(sel, t, mode);
      return;
    }
    if (own.length) {
      this.selectUnit((own.find((u) => this.needsOrders(u)) ?? own[0]).id);
      return;
    }
    if (city?.mine) {
      this.openCityPanel(city.id);
      return;
    }
    this.refreshUI();
  }

  private async commandUnit(u: ViewUnit, t: Pos, mode: 'left' | 'touch'): Promise<void> {
    const W = this.view.settings.width;
    const key = t.y * W + t.x;
    const isAttack = this.attackTargets.has(key);
    if (mode === 'touch') {
      if (!this.pending || this.pending.x !== t.x || this.pending.y !== t.y) {
        this.pending = { x: t.x, y: t.y };
        if (isAttack) this.showAttackPreview(u, t, 40, 120);
        else {
          const p = this.vm.path(u, t);
          this.path = p && p.length ? p : null;
          if (p && p.length) this.toast(`Tap again to go there (${this.vm.turnsFor(u, p)} turn${this.vm.turnsFor(u, p) > 1 ? 's' : ''})`);
          else this.toast('No route there.', 'bad');
        }
        this.dirty = true;
        return;
      }
      this.pending = null;
    }
    this.tooltip.classList.add('hidden');
    if (isAttack) {
      const ok = await this.cmd({ t: 'attack', unit: u.id, to: t });
      if (ok) {
        const still = this.selUnit;
        if (!still) this.selUnitId = null;
      }
      this.afterUnitAction();
      return;
    }
    const ok = await this.cmd({ t: 'move', unit: u.id, to: t });
    if (ok) sfx('move');
    this.afterUnitAction();
  }

  /** After an order: stay on the unit while it still has movement, otherwise hop to the next unit. */
  afterUnitAction(): void {
    const u = this.selUnit;
    if (u && this.needsOrders(u) && (u.mp ?? 0) > 0 && !u.order) {
      this.recomputeSelection();
      this.refreshUI();
      return;
    }
    const next = this.nextUnitNeedingOrders(this.selUnitId ?? 0);
    if (next && !this.anim.busy) this.selectUnit(next.id, true);
    else if (!next) this.selectUnit(null);
    else {
      this.recomputeSelection();
      this.refreshUI();
    }
  }

  private onKey(e: KeyboardEvent): void {
    if (!this.view) return;
    const target = e.target as HTMLElement;
    if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.tagName === 'SELECT')) {
      if (e.key === 'Escape') target.blur();
      return;
    }
    if (this.modal && e.key !== 'Escape') return;
    const u = this.selUnit;
    const k = e.key.toLowerCase();
    const pan = 70 / this.cam.zoom;
    switch (k) {
      case 'escape':
        if (this.modal) this.closeModal();
        else if (this.openCityId !== null) this.closeCity();
        else if (this.selUnitId !== null) this.selectUnit(null);
        else this.openModal('menu', showMenu(this));
        break;
      case 'enter': this.primaryAction(); break;
      case ' ':
        e.preventDefault();
        if (u) void this.cmd({ t: 'order', unit: u.id, order: 'skip' }).then(() => this.afterUnitAction());
        else this.primaryAction();
        break;
      case 'n': {
        const next = this.nextUnitNeedingOrders(this.selUnitId);
        if (next) this.selectUnit(next.id, true);
        break;
      }
      case 'f': if (u) void this.cmd({ t: 'order', unit: u.id, order: 'fortify' }).then(() => this.afterUnitAction()); break;
      case 'z': if (u) void this.cmd({ t: 'order', unit: u.id, order: 'sleep' }).then(() => this.afterUnitAction()); break;
      case 'a': if (u) this.autoUnit(u); break;
      case 'b': if (u && u.type === 'settler') this.foundCity(u); break;
      case 'delete': if (u) this.disband(u); break;
      case 't': this.openModal('tech', showTechTree(this)); break;
      case 'd': this.openModal('diplo', showDiplomacy(this)); break;
      case 'l': this.openModal('cities', showCities(this)); break;
      case '/': e.preventDefault(); if (this.opts.sendChat) this.toggleChat(); break;
      case 'c': {
        const c = this.openCity ?? this.view.cities.find((cc) => cc.mine && cc.capital) ?? this.myCities()[0];
        if (c) this.openCityPanel(c.id);
        break;
      }
      case 'y': this.togglePref('yields'); break;
      case 'g': this.togglePref('grid'); break;
      case 'm': setMuted(!isMuted()); this.toast(isMuted() ? 'Sound off' : 'Sound on'); break;
      case 'h': case '?': this.openModal('help', showHelp(this)); break;
      case 'arrowleft': this.cam.x -= pan; this.cam.clamp(); this.dirty = true; break;
      case 'arrowright': this.cam.x += pan; this.cam.clamp(); this.dirty = true; break;
      case 'arrowup': this.cam.y -= pan; this.cam.clamp(); this.dirty = true; break;
      case 'arrowdown': this.cam.y += pan; this.cam.clamp(); this.dirty = true; break;
      case '+': case '=': this.zoomAt(this.cam.w / 2, this.cam.h / 2, 1.2); break;
      case '-': this.zoomAt(this.cam.w / 2, this.cam.h / 2, 1 / 1.2); break;
    }
  }

  togglePref(which: 'yields' | 'grid'): void {
    if (which === 'yields') this.showYields = !this.showYields;
    else this.showGrid = !this.showGrid;
    try { localStorage.setItem(SAVE_TOGGLES, JSON.stringify({ yields: this.showYields, grid: this.showGrid })); } catch { /* ignore */ }
    this.dirty = true;
  }

  // ------------------------------------------------------------------ unit actions
  canFound(u: ViewUnit): string | null {
    const i = u.y * this.view.settings.width + u.x;
    const terr = TERRAIN_LIST[this.view.terrain[i]];
    if ((u.mp ?? 0) <= 0) return 'No movement left this turn.';
    if (terr === 'mountain') return 'Cannot found a city on mountains.';
    const owner = this.view.owner[i] - 1;
    if (owner >= 0 && owner !== this.me) return 'This land belongs to someone else.';
    for (const c of this.view.cities) if (hexDistance(c.x, c.y, u.x, u.y) < 3) return 'Too close to another city (3 tiles apart).';
    return null;
  }

  canWork(u: ViewUnit, kind: WorkKind): string | null {
    const i = u.y * this.view.settings.width + u.x;
    const terr = TERRAIN_LIST[this.view.terrain[i]];
    const def = IMPROVEMENTS[kind];
    if (def.tech && !this.view.techs.includes(def.tech)) return `Requires ${TECH_BY_ID[def.tech].name}.`;
    const owner = this.view.owner[i] - 1;
    if (owner >= 0 && owner !== this.me) return 'Foreign territory.';
    if (this.view.cities.some((c) => c.x === u.x && c.y === u.y)) return 'Not inside a city.';
    if (!def.terrains.includes(terr)) return `Cannot build a ${def.name.toLowerCase()} on ${TERRAIN[terr].name.toLowerCase()}.`;
    if (kind === 'road' && this.view.road[i] === 1) return 'Already has a road.';
    if (kind === 'farm' && this.view.improvement[i] === 1) return 'Already a farm.';
    if (kind === 'mine' && this.view.improvement[i] === 2) return 'Already a mine.';
    return null;
  }

  async foundCity(u: ViewUnit): Promise<void> {
    const err = this.canFound(u);
    if (err) {
      sfx('error');
      this.toast(err, 'bad');
      return;
    }
    const ok = await this.cmd({ t: 'found', unit: u.id });
    if (ok) {
      this.selUnitId = null;
      const c = this.view.cities.find((cc) => cc.x === u.x && cc.y === u.y);
      if (c) this.openCityPanel(c.id);
      else this.pendingCityOpen = { x: u.x, y: u.y };
    }
  }

  async autoUnit(u: ViewUnit): Promise<void> {
    if (u.type === 'worker') await this.cmd({ t: 'order', unit: u.id, order: 'auto' });
    else if (!UNITS[u.type].civilian) await this.cmd({ t: 'order', unit: u.id, order: 'explore' });
    this.afterUnitAction();
  }

  async disband(u: ViewUnit): Promise<void> {
    if (!confirm(`Disband this ${UNITS[u.type].name}?`)) return;
    await this.cmd({ t: 'order', unit: u.id, order: 'cancel' });
    await this.cmd({ t: 'disband', unit: u.id });
    this.selUnitId = null;
    this.afterUnitAction();
  }

  // ------------------------------------------------------------------ turn flow
  /** What the big button currently does. */
  primaryState(): { label: string; sub?: string; kind: 'city' | 'research' | 'unit' | 'end' | 'wait'; action: () => void } {
    const v = this.view;
    const me = v.players[v.me];
    if (!me.alive && !v.over) return { label: 'Spectating', sub: 'You were eliminated', kind: 'wait', action: () => {} };
    if (v.over) return { label: 'Game over', kind: 'wait', action: () => this.openModal('victory', showVictory(this)) };
    if (me.endedTurn) {
      const waiting = v.players.filter((p) => p.alive && !p.ai && !p.endedTurn).map((p) => p.name);
      return {
        label: this.transport.kind === 'local' ? 'Resolving…' : `Waiting for ${waiting.length ? waiting.join(', ') : 'others'}…`,
        sub: 'Click to take back your end of turn',
        kind: 'wait',
        action: () => void this.cmd({ t: 'unend' }),
      };
    }
    const idleCity = this.myCities().find((c) => this.cityNeedsProduction(c));
    if (idleCity) return { label: `Choose production`, sub: idleCity.name, kind: 'city', action: () => { this.focus(idleCity.x, idleCity.y); this.openCityPanel(idleCity.id); } };
    if (!v.researching && v.availableTechs.length) return { label: 'Choose research', kind: 'research', action: () => this.openModal('tech', showTechTree(this)) };
    const next = this.nextUnitNeedingOrders(this.selUnitId);
    if (next) return { label: 'Next unit', sub: `${this.myUnits().filter((u) => this.needsOrders(u)).length} waiting`, kind: 'unit', action: () => this.selectUnit(next.id, true) };
    return { label: 'End turn', kind: 'end', action: () => void this.endTurn() };
  }

  primaryAction(): void {
    if (this.busy) return;
    this.primaryState().action();
  }

  async endTurn(): Promise<void> {
    if (this.busy) return;
    this.busy = true;
    const delay = setTimeout(() => {
      this.thinking = h('div', { class: 'thinking' }, h('span', null, 'The other civilisations are taking their turns…'));
      this.root.append(this.thinking);
    }, 350);
    try {
      this.selUnitId = null;
      await this.cmd({ t: 'end' });
    } finally {
      clearTimeout(delay);
      this.thinking?.remove();
      this.thinking = null;
      this.busy = false;
      this.refreshUI();
    }
  }

  // ------------------------------------------------------------------ UI composition
  refreshUI(): void {
    if (!this.view) return;
    this.renderTopbar();
    this.renderBottomLeft();
    this.renderEndTurn();
    this.renderSide();
  }

  private renderTopbar(): void {
    const v = this.view;
    const t = this.topbar;
    clear(t);
    const me = v.players[v.me];
    const civ = CIV_BY_ID[me.civ];
    const researching = v.researching ? TECH_BY_ID[v.researching] : null;
    const perTurn = v.income.science;
    const pct = researching && v.researchCost ? Math.min(100, (v.research / v.researchCost) * 100) : 0;
    t.append(
      btn('☰', () => this.openModal('menu', showMenu(this)), 'ghost', 'Menu (Esc)'),
      h('div', { class: 'stat', title: civ.bonus }, h('span', { class: 'dot', style: { background: civ.color } }), h('div', null, civ.name, h('small', null, `Turn ${v.turn} / ${v.settings.turnLimit}`))),
      h('div', { class: 'stat', title: 'Gold in the treasury (and per turn)' }, h('span', { class: 'ico' }, '🪙'), h('div', null, `${v.gold}`, h('small', { class: v.income.gold >= 0 ? 'pos' : 'neg' }, `${fmtSigned(v.income.gold)} / turn`))),
      h('div', { class: 'stat click', title: 'Open the technology tree (T)', on: { click: () => this.openModal('tech', showTechTree(this)) } },
        h('span', { class: 'ico' }, '🔬'),
        h('div', null,
          researching ? researching.name : h('span', { class: 'neg' }, 'Choose research!'),
          h('small', null, researching ? `+${perTurn} / turn · ${perTurn > 0 ? Math.ceil((v.researchCost - v.research) / perTurn) : '∞'} turns` : `+${perTurn} science / turn`),
          researching ? h('div', { class: 'bar' }, h('i', { style: { width: `${pct}%` } })) : null,
        )),
      h('div', { class: 'stat', title: 'Culture counts toward your score' }, h('span', { class: 'ico' }, '🎭'), h('div', null, `${v.culture}`, h('small', null, `+${v.income.culture} / turn`))),
    );
    if (v.settings.turnTimer > 0) t.append(h('div', { class: 'stat', id: 'timer', title: 'Time left in this turn' }, h('span', { class: 'ico' }, '⏱'), h('div', { id: 'timerlabel' }, '--')));
    // scoreboard
    const sb = h('div', { class: 'scoreboard' });
    for (const p of v.players) {
      const col = CIV_BY_ID[p.civ].color;
      const rel = p.id === v.me ? '' : p.relation === 'war' ? '⚔' : p.relation === 'peace' ? '🕊' : '?';
      sb.append(
        h('div', { class: `sb-item ${p.alive ? '' : 'dead'}`, title: `${p.name} — ${CIV_BY_ID[p.civ].name}\nScore ${p.score} · ${p.cityCount} cities · ${p.techCount} techs`, on: { click: () => this.openModal('diplo', showDiplomacy(this)) } },
          h('span', { class: 'dot', style: { background: col } }), p.name.length > 11 ? p.name.slice(0, 10) + '…' : p.name,
          h('b', null, p.score), rel ? h('span', { class: 'rel' }, rel) : null,
          !p.ai ? h('span', { class: p.endedTurn ? 'ready' : 'muted', title: p.endedTurn ? 'Ready' : 'Still playing' }, p.endedTurn ? '✔' : p.connected ? '…' : '⚠') : null,
        ),
      );
    }
    t.append(sb);
    put(t,
      this.opts.sendChat ? btn('💬', () => this.toggleChat(), 'ghost', 'Chat (/)') : null,
      btn('🏙', () => this.openModal('cities', showCities(this)), 'ghost', 'Your cities (L)'),
      btn('📜', () => this.openModal('log', showLog(this)), 'ghost', 'Message log'),
      btn('🏛', () => this.openModal('diplo', showDiplomacy(this)), 'ghost', 'Diplomacy (D)'),
      btn('?', () => this.openModal('help', showHelp(this)), 'ghost', 'Help (H)'),
    );
    this.updateTimerLabel();
  }

  private updateTimerLabel(): void {
    const el = this.root.querySelector('#timerlabel');
    if (!el) return;
    if (!this.timerEnd) {
      el.textContent = '--';
      return;
    }
    const s = Math.max(0, Math.ceil((this.timerEnd - performance.now()) / 1000));
    el.textContent = `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
  }

  private tileInfoEl: HTMLElement | null = null;
  private renderBottomLeft(): void {
    const bl = this.bottomLeft;
    clear(bl);
    const u = this.selUnit;
    if (u) bl.append(this.unitPanel(u));
    this.tileInfoEl = h('div', { class: 'panel tile-info' });
    bl.append(this.tileInfoEl);
    this.refreshTileInfo();
    if (this.chatBox) bl.append(this.chatBox);
  }

  private refreshTileInfo(): void {
    const el = this.tileInfoEl;
    if (!el || !this.view) return;
    clear(el);
    const t = this.hover;
    if (!t) {
      el.classList.add('hidden');
      return;
    }
    const W = this.view.settings.width;
    const i = t.y * W + t.x;
    if (this.view.state[i] === 0) {
      el.classList.remove('hidden');
      el.append(h('span', null, 'Unexplored'));
      return;
    }
    el.classList.remove('hidden');
    const terr = TERRAIN[TERRAIN_LIST[this.view.terrain[i]]];
    const y = tileYieldFromView(this.view, i);
    const owner = this.view.owner[i] - 1;
    const res = this.view.resource[i] > 0 ? RESOURCES[RESOURCE_LIST[this.view.resource[i] - 1]] : null;
    put(el,
      h('b', null, terr.name),
      h('span', { style: { color: 'var(--food)' } }, `🌿${y.food}`),
      h('span', { style: { color: 'var(--prod)' } }, `⚙${y.prod}`),
      h('span', { style: { color: 'var(--coin)' } }, `🪙${y.gold}`),
      res ? h('span', null, `${res.icon} ${res.name}`) : null,
      this.view.improvement[i] ? h('span', null, this.view.improvement[i] === 1 ? '🌱 Farm' : '⛏ Mine') : null,
      this.view.road[i] ? h('span', null, '🛤 Road') : null,
      terr.defense ? h('span', null, `🛡 +${terr.defense}%`) : null,
      owner >= 0 ? h('span', null, h('span', { class: 'dot', style: { background: civColor(this.view, owner) } }), ' ', this.view.players[owner].name) : null,
    );
    const units = this.view.units.filter((q) => q.x === t.x && q.y === t.y && q.owner !== this.me);
    for (const q of units) el.append(h('span', { class: 'pill' }, UNITS[q.type].icon, ' ', UNITS[q.type].name, ` (${this.view.players[q.owner].name}) ${q.hp}hp`));
    const city = this.view.cities.find((c) => c.x === t.x && c.y === t.y && !c.mine);
    if (city) el.append(h('span', { class: 'pill' }, `🏙 ${city.name} · pop ${city.pop} · ${city.hp}/${city.maxHp} HP`));
  }

  private unitPanel(u: ViewUnit): HTMLElement {
    const def = UNITS[u.type];
    const col = civColor(this.view, u.owner);
    const full = def.moves * 2;
    const actions = h('div', { class: 'actions' });
    const add = (label: string, fn: () => void, title = '', disabled = '', cls = '') => {
      const b = btn(label, fn, `small ${cls}`, disabled || title);
      if (disabled) b.disabled = true;
      actions.append(b);
    };
    if (u.type === 'settler') {
      const err = this.canFound(u);
      add('🏙 Found city (B)', () => void this.foundCity(u), 'Found a new city here', err ?? '', 'primary');
    }
    if (u.type === 'worker') {
      (['farm', 'mine', 'road'] as WorkKind[]).forEach((k) => {
        const err = this.canWork(u, k);
        const d = IMPROVEMENTS[k];
        add(`${d.icon} ${d.name}`, () => void this.cmd({ t: 'work', unit: u.id, what: k }).then(() => this.afterUnitAction()), `${d.name}: ${d.turns} turns`, err ?? '');
      });
      add('🤖 Auto (A)', () => void this.autoUnit(u), 'Let the worker improve land by itself');
    }
    if (!def.civilian) {
      add('🛡 Fortify (F)', () => void this.cmd({ t: 'order', unit: u.id, order: 'fortify' }).then(() => this.afterUnitAction()), '+25% defence and healing');
      add('🧭 Explore (A)', () => void this.autoUnit(u), 'Explore automatically');
    }
    add('💤 Sleep (Z)', () => void this.cmd({ t: 'order', unit: u.id, order: 'sleep' }).then(() => this.afterUnitAction()), 'Stop asking for orders');
    add('⏭ Skip (Space)', () => void this.cmd({ t: 'order', unit: u.id, order: 'skip' }).then(() => this.afterUnitAction()), 'Skip this unit for the turn');
    if (u.order || u.auto || u.fortified) add('✖ Cancel orders', () => void this.cmd({ t: 'order', unit: u.id, order: 'cancel' }).then(() => this.recomputeSelection()), 'Give new orders');
    const inCity = this.view.cities.find((c) => c.mine && c.x === u.x && c.y === u.y);
    if (inCity) add('🏙 City (C)', () => this.openCityPanel(inCity.id));
    add('🗑', () => void this.disband(u), 'Disband (Del)', '', 'danger');
    const stats = h('div', { class: 'unit-stats' },
      h('span', null, 'Moves ', h('b', null, `${((u.mp ?? 0) / 2).toFixed(u.mp! % 2 ? 1 : 0)}/${full / 2}`)),
      def.strength > 0 ? h('span', null, '⚔ ', h('b', null, `${def.strength}`)) : null,
      def.ranged > 0 ? h('span', null, '🏹 ', h('b', null, `${def.ranged}`), ` range ${def.range}`) : null,
      u.level > 0 ? h('span', null, '★ ', h('b', null, `veteran`)) : null,
      u.order?.kind === 'goto' ? h('span', null, '→ travelling') : null,
      u.order?.kind === 'build' && u.workProgress ? h('span', null, `⚒ ${u.workProgress.kind} ${Math.floor(u.workProgress.progress)}/${IMPROVEMENTS[u.workProgress.kind].turns}`) : null,
      u.auto ? h('span', null, u.auto === 'work' ? '🤖 auto-working' : '🧭 exploring') : null,
    );
    return h('div', { class: 'panel unit-panel' },
      h('div', { class: 'unit-head' },
        h('div', { class: 'unit-ico', style: { borderColor: col } }, def.icon),
        h('div', { style: { flex: '1' } }, h('h4', null, def.name), stats, def.civilian ? null : h('div', { class: 'hpbar' }, h('i', { style: { width: `${u.hp}%`, background: u.hp > 60 ? 'var(--good)' : u.hp > 30 ? '#e8c14a' : 'var(--bad)' } }))),
      ),
      actions,
    );
  }

  private renderEndTurn(): void {
    const e = this.endturn;
    clear(e);
    const st = this.primaryState();
    const b = btn(st.label, () => this.primaryAction(), `big endturn ${st.kind === 'wait' ? 'waiting' : 'primary'}`);
    if (this.busy) b.disabled = true;
    e.append(this.mini, b);
    if (st.sub) e.append(h('div', { class: 'endturn-sub' }, st.sub));
    if (st.kind !== 'end' && st.kind !== 'wait') {
      e.append(h('div', { class: 'endturn-sub' }, h('a', { on: { click: () => void this.endTurn() } }, 'End turn anyway')));
    }
    if (this.opts.hostActions && st.kind === 'wait') {
      e.append(h('div', { class: 'endturn-sub' }, h('a', { on: { click: () => this.opts.hostActions!.forceEnd() } }, 'Skip waiting (host)')));
    }
  }

  private renderSide(): void {
    const c = this.openCity;
    this.root.classList.toggle('city-open', !!c);
    if (!c) {
      this.sidepanel.classList.add('hidden');
      return;
    }
    const scroller = this.sidepanel.querySelector('.body') as HTMLElement | null;
    const top = scroller?.scrollTop ?? 0;
    this.sidepanel.classList.remove('hidden');
    clear(this.sidepanel);
    renderCityPanel(this, this.sidepanel, c);
    const nb = this.sidepanel.querySelector('.body') as HTMLElement | null;
    if (nb) nb.scrollTop = top;
  }

  openCityPanel(id: number): void {
    this.openCityId = id;
    this.selUnitId = null;
    this.reach = null;
    this.path = null;
    this.attackTargets = new Set();
    const c = this.openCity;
    if (c) this.focus(c.x, c.y);
    this.refreshUI();
    this.dirty = true;
  }

  closeCity(refresh = true): void {
    this.openCityId = null;
    if (refresh) {
      this.refreshUI();
      this.dirty = true;
    }
  }

  cycleCity(dir: number): void {
    const list = this.myCities();
    if (!list.length) return;
    const i = list.findIndex((c) => c.id === this.openCityId);
    this.openCityPanel(list[(i + dir + list.length) % list.length].id);
  }

  // ------------------------------------------------------------------ modals
  openModal(kind: string, content: HTMLElement): void {
    this.closeModal();
    this.modalKind = kind;
    this.modal = h('div', { class: 'modal-back', on: { click: (e) => { if (e.target === this.modal) this.closeModal(); } } }, content);
    this.root.append(this.modal);
  }

  closeModal(): void {
    this.modal?.remove();
    this.modal = null;
    this.modalKind = '';
  }

  private rerenderModal(): void {
    if (!this.modal) return;
    const kind = this.modalKind;
    const content = kind === 'tech' ? showTechTree(this) : kind === 'diplo' ? showDiplomacy(this) : kind === 'cities' ? showCities(this) : kind === 'log' ? showLog(this) : kind === 'menu' ? showMenu(this) : kind === 'help' ? showHelp(this) : null;
    if (!content) return;
    const scrollers = Array.from(this.modal.querySelectorAll<HTMLElement>('.body')).map((b) => b.scrollTop);
    clear(this.modal);
    this.modal.append(content);
    Array.from(this.modal.querySelectorAll<HTMLElement>('.body')).forEach((b, i) => { b.scrollTop = scrollers[i] ?? 0; });
  }

  showCurtain(name: string, done: () => void): void {
    this.closeModal();
    this.closeCity(false);
    const c = h('div', { class: 'curtain' },
      h('div', null, h('h2', null, `Pass the device to ${name}`), h('p', { class: 'muted' }, 'Everything is hidden until you continue.'), btn(`I am ${name} — continue`, () => { c.remove(); done(); }, 'primary big')));
    this.root.append(c);
  }

  // ------------------------------------------------------------------ chat (online)
  addChat(from: string, slot: number, text: string): void {
    const col = slot >= 0 && this.view?.players[slot] ? civColor(this.view, slot) : '#ccc';
    this.chatLines.push({ from, text, color: col });
    if (this.chatLines.length > 80) this.chatLines.shift();
    this.toast(`${from}: ${text}`, 'info');
    if (this.chatLog) this.renderChatLog();
  }

  toggleChat(): void {
    if (this.chatBox) {
      this.chatBox.remove();
      this.chatBox = null;
      this.chatLog = null;
      return;
    }
    this.chatLog = h('div', { class: 'chat-log' });
    const input = h('input', { placeholder: 'Say something… (Enter to send)', maxLength: 240, style: { width: '100%' } });
    input.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && input.value.trim()) {
        this.opts.sendChat?.(input.value.trim());
        input.value = '';
      }
      e.stopPropagation();
    });
    this.chatBox = h('div', { class: 'panel' }, this.chatLog, input);
    this.bottomLeft.append(this.chatBox);
    this.renderChatLog();
    input.focus();
  }

  private renderChatLog(): void {
    if (!this.chatLog) return;
    clear(this.chatLog);
    for (const l of this.chatLines.slice(-30)) this.chatLog.append(h('div', null, h('b', { style: { color: l.color } }, l.from + ': '), l.text));
    this.chatLog.scrollTop = this.chatLog.scrollHeight;
  }
}

void BUILDINGS;
