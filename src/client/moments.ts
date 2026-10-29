import type { Animator } from './anim';
import { Cinematics, cineMode, type Clip } from './cinematics';
import { civColor, type Camera } from './render';
import type { GameEvent } from '../shared/game';
import type { GameView } from '../shared/view';
import { UNITS } from '../shared/data';
import { sfx } from './audio';
import { MomentsUI } from './ui/moments';
import { Effects3D, type EffectKind, type Frame } from './gl/moments3d';
import type { Particles } from './gl/fx';
import type { Terrain } from './gl/terrain';
import type { Scene } from 'three';
import type { Pos } from '../shared/hex';

type FreshNote = GameView['notes'][number];

/** The bits of the 3D renderer the staged effects need; the flat renderer has none of them. */
interface Stage { scene: Scene; terrain: Terrain; fx: Particles }

export interface MomentsHost {
  cam: Camera;
  root: HTMLElement;
  anim: Animator;
  renderer: object;
  /** true while the player is busy (animations playing, a dialog open) and a clip that can wait should wait */
  busy(): boolean;
  wake(): void;
}

const HEX = 48;

function rgb(v: GameView, owner: number): [number, number, number] {
  const hex = civColor(v, owner).replace('#', '');
  const n = parseInt(hex.length === 3 ? hex.replace(/./g, '$&$&') : hex, 16);
  return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
}

/**
 * Decides which game events deserve a moment (a short camera clip, a title card, a staged effect) and plays them.
 * Everything here is presentation: the game state is final before any of it starts, and nothing blocks input.
 */
export class Moments {
  readonly cine: Cinematics;
  private ui: MomentsUI;
  private fx: Effects3D | null = null;
  private fxScene: Scene | null = null;

  constructor(private host: MomentsHost) {
    this.ui = new MomentsUI(host.root);
    this.cine = new Cinematics({ cam: host.cam, ui: this.ui, busy: () => host.busy(), wake: () => host.wake() });
  }

  private stage(): Effects3D | null {
    const r = this.host.renderer as Partial<Stage>;
    if (!r.scene || !r.terrain || !r.fx) {
      this.fx?.dispose();
      this.fx = null;
      this.fxScene = null;
      return null;
    }
    if (this.fxScene !== r.scene) {
      this.fx?.dispose();
      this.fx = new Effects3D(r.scene, r.terrain, r.fx);
      this.fxScene = r.scene;
    }
    return this.fx;
  }

  private effect(kind: EffectKind, at: Pos, opts: { dur?: number; color?: [number, number, number]; size?: number; delay?: number } = {}): void {
    if (cineMode() === 'off' && kind !== 'splash' && kind !== 'promote' && kind !== 'grow') return;
    if (kind === 'splash' || kind === 'promote' || kind === 'grow') setTimeout(() => sfx(kind), opts.delay ?? 0);
    this.stage()?.spawn(kind, at, performance.now() + (opts.delay ?? 0), opts);
  }

  /** When the animations already queued will be over, so a consequence appears after the fight that caused it. */
  private after(): number {
    return Math.max(0, this.host.anim.until() - performance.now());
  }

  private play(c: Clip, sound?: string): void {
    this.cine.play({ ...c, onStart: () => { if (sound) sfx(sound); } }, performance.now());
  }

  onView(v: GameView, events: GameEvent[], fresh: FreshNote[], first: boolean): void {
    if (first) return;
    const W = v.settings.width;
    const known = (p?: Pos): p is Pos => !!p && v.state[p.y * W + p.x] >= 1;
    const mine = v.cities.filter((c) => c.mine);
    const delay = this.after();

    for (const e of events) {
      if (e.kind === 'found') {
        const color = rgb(v, e.owner);
        this.effect('ring', e.at, { color, delay });
        if (e.owner === v.me && mine.length === 1) {
          const city = mine[0];
          this.effect('column', e.at, { color: [1, 0.85, 0.5], dur: 2200, size: 0.6, delay });
          this.play({ id: 'found', priority: 2, dur: 3200, focus: e.at, zoom: [[0, 1], [0.3, 0.84], [0.62, 1.28], [0.8, 1.28], [1, 1]], title: city.name, sub: 'Your capital is founded', travelIn: 0.2, travelOut: 0.2 }, 'whoosh');
        }
      } else if (e.kind === 'capture') {
        const color = rgb(v, e.by);
        this.effect('fire', e.at, { dur: 7000, delay });
        this.effect('ring', e.at, { color, size: 1.5, delay });
        const city = v.cities.find((c) => c.x === e.at.x && c.y === e.at.y);
        if (city && (e.by === v.me || e.from === v.me)) {
          const won = e.by === v.me;
          this.play({ id: `cap${e.city}`, priority: 3, dur: 3600, focus: e.at, zoom: [[0, 1], [0.25, 1.35], [0.82, 1.42], [1, 1]], drift: [0, -10], title: city.name, sub: won ? 'City captured' : 'City lost', tone: won ? 'gold' : 'red' }, won ? 'whoosh' : 'drums');
        }
      } else if (e.kind === 'death' && UNITS[e.type].naval) {
        this.effect('splash', e.at, { delay });
      }
    }

    for (const n of fresh) {
      if (n.kind === 'wonder') {
        const m = /completed the wonder (.+?) in (.+?)!$/.exec(n.text);
        if (known(n.at)) {
          this.effect('column', n.at, { dur: 4200 });
          if (m) this.play({ id: `wonder${n.id}`, priority: 3, dur: 4200, focus: n.at, zoom: [[0, 1], [0.3, 1.2], [0.85, 1.45], [1, 1]], drift: [28, -8], title: m[1], sub: `Wonder completed in ${m[2]}` }, 'column');
        }
      } else if (n.kind === 'tech' && n.text.startsWith('Discovered ')) {
        if (cineMode() !== 'off') this.ui.tech(n.text.slice(11).replace(/!$/, ''));
      } else if (n.kind === 'war') {
        const m = /^(.+?) declared war on (.+?)!$/.exec(n.text);
        if (m && m[2] === v.players[v.me]?.name) {
          const them = v.players.find((p) => p.name === m[1]);
          const mineCity = mine[0];
          const target = them ? nearest(v.cities.filter((c) => c.owner === them.id && known(c)), mineCity) : null;
          this.play({ id: `war${n.id}`, priority: 3, dur: 3400, focus: target ?? undefined, zoom: [[0, 1], [0.35, 1.3], [0.85, 1.42], [1, 1]], title: m[1].replace(/ \(AI\)$/, ''), sub: 'Declares war on you', tone: 'red' }, 'drums');
        }
      } else if (n.kind === 'good' && n.at) {
        if (/was promoted/.test(n.text)) this.effect('promote', n.at, { delay });
        else if (/grew to size/.test(n.text)) this.effect('grow', n.at, { delay });
      }
    }
  }

  /** A new era: pull back to show the lands; the DOM banner carries the title. */
  era(): void {
    this.play({ id: 'era', priority: 2, dur: 3200, zoom: [[0, 1], [0.35, 0.8], [0.72, 0.8], [1, 1]], bars: false }, 'whoosh');
  }

  /** The finale: pull the camera back over the world while confetti (win) or embers (loss) fall. */
  finale(v: GameView): void {
    const win = v.winner === v.me;
    const cap = v.cities.find((c) => c.mine && c.capital) ?? v.cities.find((c) => c.mine) ?? v.cities[0];
    this.cine.clear();
    const at = cap ? { x: cap.x, y: cap.y } : undefined;
    this.cine.play({ id: 'finale', priority: 5, dur: 4200, focus: at, zoom: [[0, 1], [1, 1]], stay: true, endZoom: 0, title: win ? 'Victory' : 'Defeat', sub: win ? 'Your name will be remembered' : 'Your story ends here', tone: win ? 'gold' : 'dark' }, performance.now());
    if (cineMode() !== 'off') this.stage()?.spawn(win ? 'confetti' : 'embers', at ?? { x: 0, y: 0 }, performance.now(), { dur: 8000 });
  }

  /** Keeps the frame loop running while a clip or effect is live. */
  update(now: number): boolean {
    const c = this.cine.update(now);
    let e = false;
    if (this.fx) {
      const cam = this.host.cam;
      const frame: Frame = { cx: cam.x / HEX, cz: cam.y / HEX, hw: cam.w / 2 / (cam.zoom * HEX), hh: cam.h / 2 / (cam.zoom * HEX * cam.tilt) };
      e = this.fx.update(now, frame);
    }
    return c || e;
  }

  dispose(): void {
    this.cine.dispose();
    this.ui.hide();
    this.fx?.dispose();
    this.fx = null;
  }
}

function nearest(cities: Array<{ x: number; y: number }>, from?: { x: number; y: number }): Pos | null {
  if (!cities.length) return null;
  if (!from) return { x: cities[0].x, y: cities[0].y };
  let best = cities[0];
  let bd = Infinity;
  for (const c of cities) {
    const d = (c.x - from.x) ** 2 + (c.y - from.y) ** 2;
    if (d < bd) { bd = d; best = c; }
  }
  return { x: best.x, y: best.y };
}
