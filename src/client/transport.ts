import { Game, type Command, type GameEvent } from '../shared/game';
import { buildView, filterEvents, type GameView } from '../shared/view';
import { previewAttack, type AttackPreview } from '../shared/combat';
import type { CmdResult } from '../shared/types';
import type { ClientMsg, RoomInfo, ServerMsg } from '../shared/protocol';

export interface GameTransport {
  readonly kind: 'local' | 'remote';
  /** called by the transport whenever the player's view changes */
  onView: (view: GameView, events: GameEvent[]) => void;
  onChat: (from: string, slot: number, text: string) => void;
  onError: (message: string) => void;
  /** online only */
  onStatus: (connected: boolean) => void;
  onCurtain: (nextName: string, done: () => void) => void;
  send(cmd: Command): Promise<CmdResult>;
  preview(unit: number, to: { x: number; y: number }): Promise<AttackPreview | null>;
  sendChat(text: string): void;
  /** push the current view again (after the UI mounts) */
  refresh(): void;
  close(): void;
}

// --------------------------------------------------------------------------- local (solo + hot-seat)
export const SAVE_KEY = 'jericho.save.v1';

export class LocalTransport implements GameTransport {
  readonly kind = 'local' as const;
  onView: GameTransport['onView'] = () => {};
  onChat: GameTransport['onChat'] = () => {};
  onError: GameTransport['onError'] = () => {};
  onStatus: GameTransport['onStatus'] = () => {};
  onCurtain: GameTransport['onCurtain'] = (_n, done) => done();
  private current = 0;
  private busy = false;

  constructor(public game: Game, private humans: number[], private autosave = true) {
    this.current = humans[0] ?? 0;
  }

  get me(): number {
    return this.current;
  }

  refresh(): void {
    this.onView(buildView(this.game, this.current), []);
  }

  async send(cmd: Command): Promise<CmdResult> {
    if (this.busy) return { ok: false, error: 'Please wait…' };
    const g = this.game;
    const before = g.turn;
    const mover = this.current;
    if (cmd.t === 'end') {
      this.busy = true;
      g.players[mover].endedTurn = true;
      const pending = this.humans.filter((h) => g.players[h].alive && !g.players[h].endedTurn);
      if (pending.length === 0) {
        // let the "waiting" state paint before the AI thinks
        this.onView(buildView(g, mover), []);
        await new Promise((r) => setTimeout(r, 40));
        g.maybeProcessTurn();
        const events = g.events.splice(0);
        if (g.turn !== before && this.autosave) this.save();
        const next = this.humans.find((h) => g.players[h].alive) ?? this.humans[0];
        await this.passTo(next, g.turn !== before);
        this.busy = false;
        this.onView(buildView(g, this.current), filterEvents(events, this.current));
      } else {
        await this.passTo(pending[0], false);
        this.busy = false;
        this.onView(buildView(g, this.current), []);
      }
      return { ok: true };
    }
    const res = g.exec(mover, cmd);
    const events = g.events.splice(0);
    if (cmd.t === 'unend') g.players[mover].endedTurn = false;
    this.onView(buildView(g, this.current), filterEvents(events, this.current));
    if (g.s.over && this.autosave) this.clearSave();
    return res;
  }

  private passTo(pid: number, _newTurn: boolean): Promise<void> {
    if (pid === this.current || this.humans.length < 2) {
      this.current = pid;
      return Promise.resolve();
    }
    return new Promise((resolve) => {
      this.onCurtain(this.game.players[pid].name, () => {
        this.current = pid;
        resolve();
      });
    });
  }

  async preview(unit: number, to: { x: number; y: number }): Promise<AttackPreview | null> {
    const u = this.game.unit(unit);
    if (!u) return null;
    return previewAttack(this.game, u, to.x, to.y);
  }

  sendChat(): void {}

  save(): void {
    try {
      localStorage.setItem(SAVE_KEY, JSON.stringify({ humans: this.humans, state: this.game.toJSON() }));
    } catch {
      /* storage full or unavailable */
    }
  }
  clearSave(): void {
    try { localStorage.removeItem(SAVE_KEY); } catch { /* ignore */ }
  }
  close(): void {}
}

export function loadSavedGame(): LocalTransport | null {
  try {
    const raw = localStorage.getItem(SAVE_KEY);
    if (!raw) return null;
    const data = JSON.parse(raw) as { humans: number[]; state: ReturnType<Game['toJSON']> };
    const game = Game.fromJSON(data.state);
    if (game.s.over) return null;
    return new LocalTransport(game, data.humans);
  } catch {
    return null;
  }
}

export function hasSavedGame(): boolean {
  try { return !!localStorage.getItem(SAVE_KEY); } catch { return false; }
}

// --------------------------------------------------------------------------- remote (WebSocket)
export class RemoteTransport implements GameTransport {
  readonly kind = 'remote' as const;
  onView: GameTransport['onView'] = () => {};
  onChat: GameTransport['onChat'] = () => {};
  onError: GameTransport['onError'] = () => {};
  onStatus: GameTransport['onStatus'] = () => {};
  onCurtain: GameTransport['onCurtain'] = (_n, done) => done();
  onRoom: (room: RoomInfo) => void = () => {};
  private ws: WebSocket | null = null;
  private nextId = 1;
  private pending = new Map<number, (m: Extract<ServerMsg, { t: 'result' }>) => void>();
  private closed = false;
  private retry = 0;
  private lastView: GameView | null = null;
  private pingTimer: ReturnType<typeof setInterval> | null = null;
  room: RoomInfo | null = null;

  constructor(private url: string, private opening: () => ClientMsg) {}

  static defaultUrl(): string {
    const loc = window.location;
    const params = new URLSearchParams(loc.search);
    const override = params.get('server');
    if (override) return override;
    // a build for a separately hosted frontend (e.g. Vercel) bakes in the backend address: wss://api.example.com/ws
    const baked = import.meta.env.VITE_SERVER_URL as string | undefined;
    if (baked) return baked;
    const proto = loc.protocol === 'https:' ? 'wss:' : 'ws:';
    return `${proto}//${loc.host}/ws`;
  }

  connect(): Promise<void> {
    return new Promise((resolve, reject) => {
      let settled = false;
      const open = () => {
        const ws = new WebSocket(this.url);
        this.ws = ws;
        ws.onopen = () => {
          this.retry = 0;
          this.onStatus(true);
          ws.send(JSON.stringify(this.opening()));
          if (!settled) {
            settled = true;
            resolve();
          }
          if (this.pingTimer) clearInterval(this.pingTimer);
          this.pingTimer = setInterval(() => this.raw({ t: 'ping' }), 25000);
        };
        ws.onmessage = (ev) => this.handle(JSON.parse(String(ev.data)) as ServerMsg);
        ws.onclose = () => {
          this.onStatus(false);
          if (this.pingTimer) clearInterval(this.pingTimer);
          if (this.closed) return;
          if (!settled) {
            settled = true;
            reject(new Error('Could not reach the game server.'));
            return;
          }
          this.retry++;
          setTimeout(open, Math.min(8000, 600 * this.retry));
        };
        ws.onerror = () => { /* handled by onclose */ };
      };
      open();
    });
  }

  private handle(msg: ServerMsg): void {
    switch (msg.t) {
      case 'room':
        this.room = msg.room;
        this.onRoom(msg.room);
        break;
      case 'view':
        this.lastView = msg.view;
        this.onView(msg.view, msg.events);
        break;
      case 'result': {
        const cb = this.pending.get(msg.id);
        if (cb) {
          this.pending.delete(msg.id);
          cb(msg);
        }
        break;
      }
      case 'chat':
        this.onChat(msg.from, msg.slot, msg.text);
        break;
      case 'error':
        this.onError(msg.message);
        break;
    }
  }

  raw(msg: ClientMsg): void {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) this.ws.send(JSON.stringify(msg));
  }

  send(cmd: Command): Promise<CmdResult> {
    const id = this.nextId++;
    return new Promise((resolve) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        resolve({ ok: false, error: 'No response from the server.' });
      }, 8000);
      this.pending.set(id, (m) => {
        clearTimeout(timeout);
        resolve(m.res);
      });
      this.raw({ t: 'cmd', id, cmd });
    });
  }

  preview(unit: number, to: { x: number; y: number }): Promise<AttackPreview | null> {
    const id = this.nextId++;
    return new Promise((resolve) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        resolve(null);
      }, 3000);
      this.pending.set(id, (m) => {
        clearTimeout(timeout);
        resolve(m.preview ?? null);
      });
      this.raw({ t: 'preview', id, unit, to });
    });
  }

  sendChat(text: string): void {
    this.raw({ t: 'chat', text });
  }

  refresh(): void {
    if (this.lastView) this.onView(this.lastView, []);
  }

  close(): void {
    this.closed = true;
    if (this.pingTimer) clearInterval(this.pingTimer);
    this.ws?.close();
  }
}
