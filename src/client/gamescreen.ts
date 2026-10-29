import { hexDistance, type Pos } from '../shared/hex';
import type { GameEvent, Command } from '../shared/game';
import { IMPROVEMENTS, TECH_BY_ID, TERRAIN, UNITS } from '../shared/data';
import { TERRAIN_LIST, type GameView, type ViewCity, type ViewUnit } from '../shared/view';
import { ViewMap } from '../shared/viewpath';
import type { AttackPreview } from '../shared/combat';
import type { WorkKind } from '../shared/types';
import { Animator } from './anim';
import { Camera, MapRenderer, civColor, type MapDrawer, type RenderState } from './render';
import { GLRenderer } from './gl/glrenderer';
import { Moments } from './moments';
import { cineMode } from './cinematics';
import { renderGuide } from './ui/guide';
import { HEX, HEX_W } from './sprites';
import { sfx, isMuted, setMuted } from './audio';
import { askConfirm, btn, clear, h, put } from './ui/dom';
import type { GameTransport } from './transport';
import { renderCityPanel } from './ui/citypanel';
import { currentEra, renderLenses, renderMedallions, renderTileInfo, renderTopbar, renderTurnControl, renderUnitBar, pushNote } from './ui/hud';
import { ERA_NAMES } from '../shared/data';
import { showCities, showDiplomacy, showHelp, showLog, showMenu, showPolicies, showTechTree, showVictory } from './ui/screens';

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
  guide: HTMLElement;
  /** the player has sent a unit somewhere (for the first-game guide) */
  moved = false;
  cam = new Camera();
  anim = new Animator();
  renderer: MapDrawer;
  glCanvas: HTMLCanvasElement | null = null;
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
  showGrid = false;
  private lastAmbient = 0;
  private drawFailures = 0;
  busy = false;
  noteHistory: Array<{ id: number; turn: number; text: string; kind: string; at?: Pos }> = [];
  private lastNoteId = -1;
  private lastTurn = 0;
  private lastEra = -1;
  private moments!: Moments;
  private firstView = true;
  private raf = 0;
  private dirty = true;
  private modal: HTMLElement | null = null;
  private modalKind = '';
  private topbar: HTMLElement;
  private medallions: HTMLElement;
  private unitbar: HTMLElement;
  private turncontrol: HTMLElement;
  private notes: HTMLElement;
  private returnFocus: HTMLElement | null = null;
  private lenses: HTMLElement;
  private tileinfo: HTMLElement;
  private drawer: HTMLElement;
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
      this.showGrid = prefs.grid === true;
    } catch { /* ignore */ }
    this.root = h('div', { class: 'game' });
    this.canvas = h('canvas', { class: 'map', role: 'application', 'aria-label': 'Game map. Use the arrow keys to pan, plus and minus to zoom, N for the next unit, Enter to continue.', tabindex: 0 });
    this.mini = h('canvas', { class: 'minimap' });
    this.topbar = h('div', { class: 'topbar' });
    this.medallions = h('div', { class: 'medallions' });
    this.unitbar = h('div', { class: 'unitbar hidden' });
    this.turncontrol = h('div', { class: 'turncontrol' });
    this.notes = h('div', { class: 'notes' });
    this.lenses = h('div', { class: 'lenses' });
    this.tileinfo = h('div', { class: 'tileinfo' });
    this.guide = h('div', { class: 'guide hidden' });
    this.drawer = h('div', { class: 'drawer hidden' });
    this.tooltip = h('div', { class: 'tooltip hidden' });
    this.connBanner = h('div', { class: 'conn hidden' }, 'Reconnecting…');
    const self = this;
    const useGL = GLRenderer.supported();
    if (useGL) this.glCanvas = h('canvas', { class: 'mapgl' });
    this.root.append(
      ...(this.glCanvas ? [this.glCanvas] : []), this.canvas, this.topbar, this.medallions, this.notes, this.tileinfo,
      h('div', { class: 'mapdock' }, h('div', { class: 'minimap-wrap' }, this.mini), this.lenses),
      this.unitbar, this.turncontrol, this.guide, this.drawer, this.tooltip, this.connBanner);
    parent.append(this.root);
    this.renderer = this.glCanvas ? new GLRenderer(this.glCanvas, this.canvas, this.cam, this.anim) : new MapRenderer(this.canvas, this.cam, this.anim);

    transport.onView = (v, ev) => this.onView(v, ev);
    transport.onStatus = (ok) => this.connBanner.classList.toggle('hidden', ok);
    transport.onError = (m) => this.toast(m, 'bad');
    transport.onChat = (from, slot, text) => this.addChat(from, slot, text);
    transport.onCurtain = (name, done) => this.showCurtain(name, done);

    this.moments = new Moments({ cam: this.cam, root: this.root, anim: this.anim, get renderer() { return self.renderer; }, busy: () => this.anim.busy || !!this.modalKind, wake: () => { this.dirty = true; } });
    this.cleanup.push(() => this.moments.dispose());
    this.cleanup.push(() => this.renderer.dispose?.());
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
  /** An own ship with room aboard that a free land unit can climb onto from where it stands. */
  shipFor(u: ViewUnit): ViewUnit | null {
    if (UNITS[u.type].naval || u.carriedBy != null) return null;
    return this.myUnits().find((s) => {
      const d = UNITS[s.type];
      return d.naval && (d.capacity ?? 0) > (s.cargo?.length ?? 0) && hexDistance(u.x, u.y, s.x, s.y) <= 1;
    }) ?? null;
  }
  /** The ship on tile `t` that `u` could board, if any. */
  boardableShipAt(u: ViewUnit, t: Pos): ViewUnit | null {
    const ship = this.shipFor(u);
    return ship && ship.x === t.x && ship.y === t.y && (u.x !== t.x || u.y !== t.y) ? ship : null;
  }
  myCities(): ViewCity[] { return this.view.cities.filter((c) => c.mine); }
  needsOrders(u: ViewUnit): boolean {
    return u.owner === this.me && (u.mp ?? 0) > 0 && !u.order && !u.auto && !u.fortified && !u.skipped && u.carriedBy == null;
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
    pushNote(this, this.notes, text, kind, at);
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
    const fresh: typeof v.notes = [];
    for (const n of v.notes) {
      if (n.id <= this.lastNoteId) continue;
      fresh.push(n);
      this.toast(n.text, n.kind, n.at);
      this.noteHistory.push({ id: n.id, turn: n.turn, text: n.text, kind: n.kind, at: n.at });
      if (n.kind === 'tech') sfx('tech');
      else if (n.kind === 'wonder') sfx('wonder');
      else if (n.kind === 'war') sfx('notify');
    }
    this.lastNoteId = Math.max(this.lastNoteId, maxId);
    this.moments.onView(v, events, fresh, this.firstView);

    // a new era dawns
    const era = currentEra(v.techs);
    if (this.lastEra >= 0 && era > this.lastEra) this.showEraBanner(era);
    this.lastEra = era;
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
      this.moments.finale(v);
      setTimeout(() => this.openModal('victory'), cineMode() === 'off' ? 1400 : 4200);
    }
    if (this.modalKind && this.modalKind !== 'victory') this.rerenderModal();
    this.dirty = true;
  }

  private initCamera(): void {
    this.cam.zoom = Math.max(this.cam.minZoom(), 0.75, Math.min(1.0, this.cam.w / 1100));
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

  /** Select and centre the next (or, going back, the previous) unit that still needs orders. */
  selectNextIdle(backwards = false): void {
    const list = this.myUnits().filter((u) => this.needsOrders(u)).sort((a, b) => a.id - b.id);
    if (!list.length) return;
    const cur = this.selUnitId;
    const next = backwards
      ? [...list].reverse().find((u) => cur !== null && u.id < cur) ?? list[list.length - 1]
      : this.nextUnitNeedingOrders(cur);
    if (next) this.selectUnit(next.id, true);
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
    if (this.reach && u.carriedBy != null) {
      // a passenger can only step ashore; it cannot go further this turn
      for (const k of [...this.reach.keys()]) {
        const kx = k % this.view.settings.width;
        const ky = Math.floor(k / this.view.settings.width);
        if (hexDistance(u.x, u.y, kx, ky) > 1) this.reach.delete(k);
      }
    }
    const boat = (u.mp ?? 0) > 0 ? this.shipFor(u) : null;
    if (boat) {
      this.reach ??= new Map();
      this.reach.set(boat.y * this.view.settings.width + boat.x, 0);
    }
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
    // a clip owns the camera while it plays; a smooth focus requested meanwhile becomes where the clip hands it back
    if (this.camTarget && this.moments.cine.active) {
      this.moments.cine.retarget(this.camTarget);
      this.camTarget = null;
    }
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
    let animating = this.anim.update(now, sfx);
    if (this.moments.update(now)) {
      animating = true;
      this.dirty = true;
    }
    const selected = this.selUnit || this.attackTargets.size > 0;
    const ambient = (this.renderer.continuous || now - this.lastAmbient > 48) && !document.hidden;
    if (!this.dirty && !animating && !selected && !ambient) return;
    if (ambient) this.lastAmbient = now;
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
      lockedTiles: city?.locked ? new Set(city.locked) : null,
      now,
    };
    try {
      this.renderer.draw(st);
      this.drawFailures = 0;
    } catch (e) {
      console.error('map draw failed', e);
      // a 3D renderer that keeps failing (driver bug, lost context) must not leave the player with a dead map
      if (++this.drawFailures >= 3 && this.glCanvas) this.fallBackToFlat();
    }
    this.drawMinimap();
  }

  private fallBackToFlat(): void {
    try { this.renderer.dispose?.(); } catch { /* ignore */ }
    this.glCanvas?.remove();
    this.glCanvas = null;
    this.cam.setPitch(90);
    this.renderer = new MapRenderer(this.canvas, this.cam, this.anim);
    this.renderer.resize();
    this.drawFailures = 0;
    this.dirty = true;
    this.toast('The 3D map had a problem, so the flat map is shown instead. Pick Graphics in the menu to change it.', 'bad');
  }

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
          this.cam.y -= dy / (this.cam.zoom * this.cam.tilt);
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
    const W = this.view.settings.width;
    const H = this.view.settings.height;
    return this.renderer.pickTile?.(sx, sy, W, H) ?? this.cam.tileAt(sx, sy, W, H);
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
    // with a city open, clicking one of its tiles pins or releases a citizen
    const openCity = this.openCity;
    if (openCity && mode !== 'right') {
      const other = this.view.cities.find((c) => c.mine && c.id !== openCity.id && c.x === t.x && c.y === t.y);
      if (other) {
        this.openCityPanel(other.id);
        return;
      }
      if (!(t.x === openCity.x && t.y === openCity.y)) {
        void this.cmd({ t: 'tile', city: openCity.id, x: t.x, y: t.y });
        return;
      }
    }
    const own = this.myUnits().filter((u) => u.x === t.x && u.y === t.y);
    const city = this.view.cities.find((c) => c.x === t.x && c.y === t.y);
    const sel = this.selUnit;
    // banner click opens the city even when units are present
    if (city?.mine) {
      const wp = this.cam.worldOfTile(t.x, t.y);
      const s = this.cam.toScreen(wp.x, wp.y);
      const hit = this.renderer.bannerHit ? this.renderer.bannerHit(city.id, sx, sy) : sy < s.y - HEX * 0.45 * this.cam.zoom;
      if (hit) {
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
    const boat = this.boardableShipAt(u, t);
    if (boat) {
      if (mode === 'touch' && (!this.pending || this.pending.x !== t.x || this.pending.y !== t.y)) {
        this.pending = { x: t.x, y: t.y };
        this.toast('Tap again to board the ship');
        this.dirty = true;
        return;
      }
      this.pending = null;
      const ok = await this.cmd({ t: 'board', unit: u.id, ship: boat.id });
      if (ok) sfx('move');
      this.afterUnitAction();
      return;
    }
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
    if (ok) {
      sfx('move');
      this.moved = true;
    }
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
    if (this.modal && e.key === 'Tab') {
      this.trapFocus(e);
      return;
    }
    const target = e.target as HTMLElement;
    if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.tagName === 'SELECT')) {
      if (e.key === 'Escape') {
        target.blur();
        if (this.modal) this.closeModal();
      }
      return;
    }
    // Enter and Space on a focused button activate that button, not the global shortcut
    if ((e.key === 'Enter' || e.key === ' ') && target?.closest?.('button, a[href], [role="button"]')) return;
    if (this.modal && e.key !== 'Escape') return;
    const u = this.selUnit;
    const k = e.key.toLowerCase();
    const pan = 70 / this.cam.zoom;
    switch (k) {
      case 'escape':
        if (this.modal) this.closeModal();
        else if (this.openCityId !== null) this.closeCity();
        else if (this.selUnitId !== null) this.selectUnit(null);
        else this.openModal('menu');
        break;
      case 'enter': this.primaryAction(); break;
      case ' ':
        e.preventDefault();
        if (u) void this.cmd({ t: 'order', unit: u.id, order: 'skip' }).then(() => this.afterUnitAction());
        else this.primaryAction();
        break;
      case 'n': case '.': this.selectNextIdle(); break;
      case ',': this.selectNextIdle(true); break;
      case 'f': if (u) void this.cmd({ t: 'order', unit: u.id, order: 'fortify' }).then(() => this.afterUnitAction()); break;
      case 'z': if (u) void this.cmd({ t: 'order', unit: u.id, order: 'sleep' }).then(() => this.afterUnitAction()); break;
      case 'a': if (u) this.autoUnit(u); break;
      case 'b': if (u && u.type === 'settler') this.foundCity(u); break;
      case 'delete': if (u) this.disband(u); break;
      case 't': this.openModal('tech'); break;
      case 'd': this.openDiplomacy(); break;
      case 'l': this.openModal('cities'); break;
      case '/': e.preventDefault(); if (this.opts.sendChat) this.toggleChat(); break;
      case 'c': {
        const c = this.openCity ?? this.view.cities.find((cc) => cc.mine && cc.capital) ?? this.myCities()[0];
        if (c) this.openCityPanel(c.id);
        break;
      }
      case 'y': this.togglePref('yields'); break;
      case 'g': this.togglePref('grid'); break;
      case 'm': setMuted(!isMuted()); this.toast(isMuted() ? 'Sound off' : 'Sound on'); this.refreshUI(); break;
      case 'h': case '?': this.openModal('help'); break;
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
    if (!(await askConfirm(`Disband this ${UNITS[u.type].name}?`, 'Disband'))) return;
    await this.cmd({ t: 'order', unit: u.id, order: 'cancel' });
    await this.cmd({ t: 'disband', unit: u.id });
    this.selUnitId = null;
    this.afterUnitAction();
  }

  // ------------------------------------------------------------------ UI composition
  refreshUI(): void {
    if (!this.view) return;
    renderTopbar(this, this.topbar);
    renderMedallions(this, this.medallions);
    renderUnitBar(this, this.unitbar);
    renderTurnControl(this, this.turncontrol);
    renderLenses(this, this.lenses);
    renderGuide(this, this.guide);
    this.refreshTileInfo();
    this.renderSide();
  }

  updateTimerLabel(): void {
    const el = this.root.querySelector('#timerlabel');
    if (!el) return;
    if (!this.timerEnd) {
      el.textContent = '--';
      return;
    }
    const s = Math.max(0, Math.ceil((this.timerEnd - performance.now()) / 1000));
    el.textContent = `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
  }

  private refreshTileInfo(): void {
    if (!this.view) return;
    renderTileInfo(this, this.tileinfo, this.hover);
  }

  /** What the big round button currently does. */
  primaryState(): { label: string; sub?: string; kind: 'city' | 'research' | 'policy' | 'unit' | 'end' | 'wait'; action: () => void } {
    const v = this.view;
    const me = v.players[v.me];
    if (!me.alive && !v.over) return { label: 'Spectating', sub: 'You were eliminated', kind: 'wait', action: () => {} };
    if (v.over) return { label: 'Game over', kind: 'wait', action: () => this.openModal('victory') };
    if (me.endedTurn) {
      const waiting = v.players.filter((p) => p.alive && !p.ai && !p.endedTurn).map((p) => p.name);
      return {
        label: this.transport.kind === 'local' ? 'Resolving…' : `Waiting for ${waiting.length ? waiting.join(', ') : 'others'}`,
        sub: 'Click to take back your end of turn',
        kind: 'wait',
        action: () => void this.cmd({ t: 'unend' }),
      };
    }
    const idleCity = this.myCities().find((c) => this.cityNeedsProduction(c));
    if (idleCity) return { label: 'Choose production', sub: idleCity.name, kind: 'city', action: () => { this.focus(idleCity.x, idleCity.y); this.openCityPanel(idleCity.id); } };
    if (!v.researching && v.availableTechs.length) return { label: 'Choose research', kind: 'research', action: () => this.openModal('tech') };
    if (v.policyChoices.length) return { label: 'Choose policy', kind: 'policy', action: () => this.openModal('policies') };
    const next = this.nextUnitNeedingOrders(this.selUnitId);
    if (next) return { label: 'Next unit', sub: `${this.myUnits().filter((u) => this.needsOrders(u)).length} waiting`, kind: 'unit', action: () => this.selectUnit(next.id, true) };
    return { label: 'Next turn', kind: 'end', action: () => void this.endTurn() };
  }

  primaryAction(): void {
    if (this.busy) return;
    this.primaryState().action();
  }

  async endTurn(): Promise<void> {
    if (this.busy) return;
    this.busy = true;
    this.refreshUI();
    const delay = setTimeout(() => {
      this.thinking = h('div', { class: 'thinking' }, h('span', null, 'The world takes its turn'));
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

  // ------------------------------------------------------------------ city drawer
  private renderSide(): void {
    const c = this.openCity;
    this.root.classList.toggle('city-open', !!c);
    if (!c) {
      this.drawer.classList.add('hidden');
      return;
    }
    const scroller = this.drawer.querySelector('.body') as HTMLElement | null;
    const top = scroller?.scrollTop ?? 0;
    this.drawer.classList.remove('hidden');
    clear(this.drawer);
    renderCityPanel(this, this.drawer, c);
    const nb = this.drawer.querySelector('.body') as HTMLElement | null;
    if (nb) nb.scrollTop = top;
  }

  openCityPanel(id: number): void {
    this.openCityId = id;
    this.selUnitId = null;
    this.reach = null;
    this.path = null;
    this.attackTargets = new Set();
    this.closeModal();
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

  // ------------------------------------------------------------------ overlays
  private buildModal(kind: string): HTMLElement | null {
    switch (kind) {
      case 'tech': return showTechTree(this);
      case 'diplo': return showDiplomacy(this);
      case 'cities': return showCities(this);
      case 'log': return showLog(this);
      case 'menu': return showMenu(this);
      case 'help': return showHelp(this);
      case 'policies': return showPolicies(this);
      case 'victory': return showVictory(this);
      default: return null;
    }
  }

  openModal(kind: string): void {
    const content = this.buildModal(kind);
    if (!content) return;
    this.replaceModal();
    this.modalKind = kind;
    this.modal = h('div', { class: `overlay ${content.dataset.full ? 'full' : ''}`, role: 'dialog', 'aria-modal': 'true', 'aria-label': kind, tabindex: -1, on: { click: (e) => { if (e.target === this.modal) this.closeModal(); } } }, content);
    this.root.append(this.modal);
    this.modal.focus();
  }

  openDiplomacy(focus?: number): void {
    this.replaceModal();
    const content = showDiplomacy(this, focus);
    this.modalKind = 'diplo';
    this.modal = h('div', { class: 'overlay', role: 'dialog', 'aria-modal': 'true', 'aria-label': 'Diplomacy', tabindex: -1, on: { click: (e) => { if (e.target === this.modal) this.closeModal(); } } }, content);
    this.root.append(this.modal);
    this.modal.focus();
  }

  /** Called before a dialog opens: remembers what had focus (once, so swapping dialogs keeps the original) and clears the old one. */
  private replaceModal(): void {
    if (!this.modal) {
      const a = document.activeElement;
      this.returnFocus = a instanceof HTMLElement && a !== document.body ? a : null;
    }
    this.modal?.remove();
    this.modal = null;
    this.modalKind = '';
  }

  closeModal(): void {
    if (!this.modal) return;
    this.modal.remove();
    this.modal = null;
    this.modalKind = '';
    const back = this.returnFocus;
    this.returnFocus = null;
    // focus goes back to what opened the dialog, if that is still on the page
    if (back && back.isConnected) back.focus({ preventScroll: true });
  }

  /** Keeps Tab and Shift+Tab inside the open dialog. */
  private trapFocus(e: KeyboardEvent): void {
    const modal = this.modal;
    if (!modal) return;
    const items = Array.from(modal.querySelectorAll<HTMLElement>('button, [href], input, select, textarea, [tabindex]'))
      .filter((el) => el !== modal && el.tabIndex >= 0 && !(el as HTMLButtonElement).disabled && el.getClientRects().length > 0);
    if (!items.length) {
      e.preventDefault();
      modal.focus();
      return;
    }
    const first = items[0];
    const last = items[items.length - 1];
    const active = document.activeElement as HTMLElement | null;
    if (!active || !modal.contains(active) || active === modal) {
      e.preventDefault();
      (e.shiftKey ? last : first).focus();
    } else if (e.shiftKey && active === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && active === last) {
      e.preventDefault();
      first.focus();
    }
  }

  private rerenderModal(): void {
    if (!this.modal) return;
    const content = this.buildModal(this.modalKind);
    if (!content) return;
    const scrollers = Array.from(this.modal.querySelectorAll<HTMLElement>('.body, .treewrap')).map((b) => [b.scrollTop, b.scrollLeft] as const);
    clear(this.modal);
    this.modal.append(content);
    if (!this.modal.contains(document.activeElement)) this.modal.focus();
    Array.from(this.modal.querySelectorAll<HTMLElement>('.body, .treewrap')).forEach((b, i) => {
      if (scrollers[i]) {
        b.scrollTop = scrollers[i][0];
        b.scrollLeft = scrollers[i][1];
      }
    });
  }

  private showEraBanner(era: number): void {
    sfx('wonder');
    this.moments.era();
    const b = h('div', { class: 'erabanner' }, h('small', null, 'A new age begins'), h('b', null, `${ERA_NAMES[era]} Era`));
    this.root.append(b);
    setTimeout(() => b.remove(), 4200);
  }

  showCurtain(name: string, done: () => void): void {
    this.closeModal();
    this.closeCity(false);
    const c = h('div', { class: 'curtain' },
      h('div', null, h('h2', null, `Pass the device to ${name}`), h('p', { class: 'muted' }, 'Everything is hidden until you continue.'), btn(`I am ${name}`, () => { c.remove(); done(); }, 'primary big')));
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
    this.chatBox = h('div', { class: 'chatdock' }, this.chatLog, input);
    this.root.append(this.chatBox);
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

