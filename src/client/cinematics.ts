import type { Camera } from './render';
import type { Pos } from '../shared/hex';

export type CineMode = 'full' | 'short' | 'off';
export const CINE_LABEL: Record<CineMode, string> = { full: 'Full', short: 'Short', off: 'Off' };
export function cineMode(): CineMode {
  try {
    const v = localStorage.getItem('jericho.cine');
    if (v === 'short' || v === 'off') return v;
  } catch { /* ignore */ }
  return 'full';
}
export function setCineMode(m: CineMode): void {
  try { localStorage.setItem('jericho.cine', m); } catch { /* ignore */ }
}
export function reducedMotion(): boolean {
  return !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
}

export interface Clip {
  /** what the clip is, used to avoid queueing the same moment twice */
  id: string;
  /** higher plays first and can bump lower clips out of a full queue */
  priority: number;
  /** total length in ms at Full; Short plays at 60 % */
  dur: number;
  /** tile the camera moves to; omitted = the camera stays and only the card plays */
  focus?: Pos;
  /** zoom keyframes: [0..1 of the clip, multiple of the starting zoom], eased between */
  zoom?: Array<[number, number]>;
  /** slow drift while holding, in world pixels */
  drift?: [number, number];
  /** share of the clip spent travelling to the focus, and travelling back */
  travelIn?: number;
  travelOut?: number;
  /** keep the camera where the clip ends (finale) instead of returning */
  stay?: boolean;
  /** absolute zoom to end on when `stay` is set; 0 = the lowest the map allows */
  endZoom?: number;
  title?: string;
  sub?: string;
  tone?: 'gold' | 'red' | 'dark';
  /** whether the cinematic bars frame the shot (Full only) */
  bars?: boolean;
  onStart?: () => void;
}

export interface CineHost {
  cam: Camera;
  /** a card or bars to show/hide; implemented by the DOM layer */
  ui: { show(c: Clip, ms: number, mode: CineMode, still: boolean): void; hide(): void };
  /** true while the player is mid-action and should not be interrupted by a clip that is not urgent */
  busy(): boolean;
  /** invoked when the camera should be re-rendered even if nothing else changed */
  wake(): void;
}

const smooth = (k: number): number => (k <= 0 ? 0 : k >= 1 ? 1 : k * k * (3 - 2 * k));
const MAX_QUEUE = 3;
const STALE_MS = 7000;

interface Home { x: number; y: number; zoom: number }

/**
 * Short camera moments for the big events. They only move the camera and draw a card: the game state is already
 * final, input is never blocked, and anything that moves the map by hand (or any click / Escape) ends the clip.
 */
export class Cinematics {
  private queue: Array<{ clip: Clip; at: number }> = [];
  private cur: { clip: Clip; t0: number; ms: number; home: Home; start: Home; last: Home; from: Home; leaving: number; still: boolean; target: Pos | null } | null = null;
  private scratch: Home = { x: 0, y: 0, zoom: 1 };
  private skip = (e: Event): void => {
    if (!this.cur) return;
    if (e.type === 'keydown' && (e as KeyboardEvent).key !== 'Escape') return;
    this.end(false);
  };

  constructor(private host: CineHost) {
    window.addEventListener('pointerdown', this.skip, true);
    window.addEventListener('keydown', this.skip, true);
  }

  dispose(): void {
    window.removeEventListener('pointerdown', this.skip, true);
    window.removeEventListener('keydown', this.skip, true);
    this.queue.length = 0;
    this.cur = null;
    this.host.ui.hide();
  }

  get active(): boolean {
    return !!this.cur;
  }

  /** Called when something else wants the camera somewhere (a smooth focus): the clip's home moves with it. */
  retarget(p: { x: number; y: number }): void {
    if (this.cur) {
      this.cur.home.x = p.x;
      this.cur.home.y = p.y;
    }
  }

  play(clip: Clip, now: number): void {
    const mode = cineMode();
    if (mode === 'off') return;
    // Short keeps only the moments that matter
    if (mode === 'short' && clip.priority < 3 && !clip.title) return;
    if (this.queue.some((q) => q.clip.id === clip.id) || this.cur?.clip.id === clip.id) return;
    this.queue.push({ clip, at: now });
    this.queue.sort((a, b) => b.clip.priority - a.clip.priority || a.at - b.at);
    if (this.queue.length > MAX_QUEUE) this.queue.length = MAX_QUEUE;
  }

  clear(): void {
    this.queue.length = 0;
    if (this.cur) this.end(false);
  }

  /** Advance the active clip; returns true while it needs frames. */
  update(now: number): boolean {
    const cam = this.host.cam;
    if (!this.cur) {
      while (this.queue.length) {
        const q = this.queue[0];
        if (now - q.at > STALE_MS) { this.queue.shift(); continue; }
        // something the player is doing wins over a moment that can wait
        if (this.host.busy() && q.clip.priority < 4) return true;
        this.queue.shift();
        this.start(q.clip, now);
        break;
      }
      if (!this.cur) return this.queue.length > 0;
    }
    const c = this.cur!;
    // someone else moved the camera (drag, wheel, minimap, keys): the player has taken over
    if (Math.abs(cam.x - c.last.x) > 0.75 || Math.abs(cam.y - c.last.y) > 0.75 || Math.abs(cam.zoom - c.last.zoom) > 0.002) {
      this.end(true);
      return this.queue.length > 0;
    }
    const e = now - c.t0;
    if (c.leaving > 0) {
      const k = smooth((now - c.leaving) / 240);
      const out = this.scratch;
      out.x = c.from.x + (c.home.x - c.from.x) * k;
      out.y = c.from.y + (c.home.y - c.from.y) * k;
      out.zoom = c.from.zoom + (c.home.zoom - c.from.zoom) * k;
      this.commit(out, c);
      this.host.wake();
      if (k >= 1) this.cur = null;
      return true;
    }
    if (e >= c.ms) {
      this.end(false, true);
      return true;
    }
    this.pose(c, e / c.ms);
    this.host.wake();
    return true;
  }

  private start(clip: Clip, now: number): void {
    const cam = this.host.cam;
    const still = reducedMotion();
    const mode = cineMode();
    const ms = clip.dur * (mode === 'short' ? 0.6 : 1) * (still ? 0.6 : 1);
    const home: Home = { x: cam.x, y: cam.y, zoom: cam.zoom };
    const target = still ? null : clip.focus ? cam.worldOfTile(clip.focus.x, clip.focus.y) : { x: home.x, y: home.y };
    this.cur = { clip, t0: now, ms, home, start: { ...home }, last: { ...home }, from: { ...home }, leaving: 0, still, target: target ? { x: target.x, y: target.y } : null };
    clip.onStart?.();
    this.host.ui.show(clip, ms, mode, still);
  }

  /** Place the camera for progress k (0..1) of the clip. */
  private pose(c: NonNullable<Cinematics['cur']>, k: number): void {
    const clip = c.clip;
    const cam = this.host.cam;
    if (!c.target) {
      c.last.x = cam.x; c.last.y = cam.y; c.last.zoom = cam.zoom;
      return;
    }
    const tin = clip.travelIn ?? 0.3;
    const tout = clip.stay ? 0 : clip.travelOut ?? 0.25;
    const out = this.scratch;
    // position: home -> target -> home (or stays)
    const into = smooth(k / tin);
    const back = tout > 0 ? smooth((k - (1 - tout)) / tout) : 0;
    const hold = Math.max(0, Math.min(1, (k - tin) / Math.max(0.001, 1 - tin - tout)));
    const dx = clip.drift ? clip.drift[0] * hold : 0;
    const dy = clip.drift ? clip.drift[1] * hold : 0;
    const px = c.start.x + (c.target.x + dx - c.start.x) * into;
    const py = c.start.y + (c.target.y + dy - c.start.y) * into;
    out.x = px + (c.home.x - px) * back;
    out.y = py + (c.home.y - py) * back;
    // zoom keyframes, relative to the zoom the clip started on
    let mul = 1;
    const zk = clip.zoom;
    if (zk && zk.length) {
      let i = 0;
      while (i < zk.length - 1 && k > zk[i + 1][0]) i++;
      const a = i === 0 && k < zk[0][0] ? ([0, 1] as [number, number]) : zk[i];
      const b = i === 0 && k < zk[0][0] ? zk[0] : zk[Math.min(i + 1, zk.length - 1)];
      const span = b[0] - a[0];
      mul = span > 0 ? a[1] + (b[1] - a[1]) * smooth((k - a[0]) / span) : b[1];
    }
    let z = c.start.zoom * mul;
    if (clip.stay && clip.endZoom !== undefined && k > 0.5) {
      const end = clip.endZoom > 0 ? clip.endZoom : cam.minZoom();
      z = c.start.zoom * mul + (end - c.start.zoom * mul) * smooth((k - 0.35) / 0.65);
    }
    out.zoom = z;
    this.commit(out, c);
  }

  private commit(p: Home, c: NonNullable<Cinematics['cur']>): void {
    const cam = this.host.cam;
    cam.x = p.x;
    cam.y = p.y;
    cam.zoom = Math.max(cam.minZoom(), Math.min(2.4, p.zoom));
    cam.clamp();
    c.last.x = cam.x;
    c.last.y = cam.y;
    c.last.zoom = cam.zoom;
  }

  /** Finish the active clip: hand the camera back to the player (`taken`), or glide home. */
  private end(taken: boolean, natural = false): void {
    const c = this.cur;
    if (!c || c.leaving) return;
    this.host.ui.hide();
    if (taken || c.clip.stay || !c.target) {
      this.cur = null;
      return;
    }
    if (natural) {
      // the clip has already travelled home as part of its shape
      this.pose(c, 1);
      this.cur = null;
      this.host.wake();
      return;
    }
    // skipped part-way: glide back quickly
    c.leaving = performance.now();
    c.from = { ...c.last };
  }
}
