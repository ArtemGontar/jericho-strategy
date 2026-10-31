import { Game, type Command, type GameEvent } from '../shared/game';
import { buildView, filterEvents, type GameView } from '../shared/view';
import { previewAttack, type AttackPreview } from '../shared/combat';
import type { CmdResult } from '../shared/types';
import type { ClientMsg, RoomInfo, ServerMsg } from '../shared/protocol';
import { hasAnySave, makeRecord, newestReadable, readSave, removeSave, writeSave, type SaveId, type SaveRecord, type Store } from './saves';

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
/** The browser's storage, or a stand-in that refuses everything when the browser hides it (some private modes). */
export function browserStore(): Store {
  try {
    return window.localStorage;
  } catch {
    return { getItem: () => { throw new Error('no storage'); }, setItem: () => { throw new Error('no storage'); }, removeItem: () => { throw new Error('no storage'); } };
  }
}

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

  /** This game as a save record, for sending to another device. */
  exportRecord(): SaveRecord {
    return makeRecord(this.game.toJSON(), this.humans);
  }

  /** Called once when the browser refuses a save, so the player is told instead of believing the game is safe. */
  onSaveFailed: (message: string) => void = () => {};
  private saveWarned = false;

  /** Write the autosave, or a numbered slot when asked. Returns whether it worked. */
  save(id: SaveId = 'auto'): boolean {
    const res = writeSave(browserStore(), id, makeRecord(this.game.toJSON(), this.humans));
    if (res.ok) {
      this.saveWarned = false;
    } else if (id !== 'auto' || !this.saveWarned) {
      this.saveWarned = true;
      this.onSaveFailed(res.error);
    }
    return res.ok;
  }
  clearSave(): void {
    removeSave(browserStore(), 'auto');
  }
  close(): void {}
}

/** Open a saved game. A save that cannot be opened is reported and left in storage untouched. */
export function loadSavedGame(id?: SaveId): { ok: true; transport: LocalTransport } | { ok: false; error: string } {
  const store = browserStore();
  const target = id ?? newestReadable(store)?.id;
  if (!target) return { ok: false, error: 'There is no saved game to open.' };
  const res = readSave(store, target);
  if (!res.ok) return { ok: false, error: res.error };
  try {
    const game = Game.fromJSON(res.record.state);
    if (game.s.over) return { ok: false, error: 'That game is already over.' };
    return { ok: true, transport: new LocalTransport(game, res.record.humans) };
  } catch {
    return { ok: false, error: 'The save could not be opened. It is kept, so you can export it.' };
  }
}

export function hasSavedGame(): boolean {
  return hasAnySave(browserStore());
}

/** Send a save to the game server and get a short code for another device. Throws an Error with a message for the player. */
export async function sendSaveToServer(json: string): Promise<{ code: string; minutes: number }> {
  const url = RemoteTransport.httpUrl('/transfer');
  if (!url) throw new Error('There is no game server to send it through. Use Export instead.');
  let res: Response;
  try {
    res = await fetch(url, { method: 'POST', body: json, headers: { 'content-type': 'application/json' } });
  } catch {
    throw new Error('Could not reach the game server. Use Export instead, or try again later.');
  }
  const body = (await res.json().catch(() => ({}))) as { code?: string; minutes?: number; error?: string };
  if (!res.ok || !body.code) throw new Error(body.error ?? 'The server would not take it.');
  try { localStorage.setItem('jericho.backedup', String(Date.now())); } catch { /* ignore */ }
  return { code: body.code, minutes: body.minutes ?? 60 };
}

/** Fetch a save that another device sent. It can be fetched once. */
export async function fetchSaveByCode(code: string): Promise<string> {
  const clean = code.toUpperCase().replace(/[^A-Z0-9]/g, '');
  if (clean.length !== 8) throw new Error('A code has 8 letters and numbers.');
  const url = RemoteTransport.httpUrl(`/transfer/${clean}`);
  if (!url) throw new Error('There is no game server to fetch it from.');
  let res: Response;
  try {
    res = await fetch(url);
  } catch {
    throw new Error('Could not reach the game server.');
  }
  if (!res.ok) throw new Error(((await res.json().catch(() => ({}))) as { error?: string }).error ?? 'No saved game with that code.');
  return res.text();
}

/** Is a game server answering at the address this page would use for online play? */
export async function checkServer(timeoutMs = 6000): Promise<boolean> {
  const url = RemoteTransport.healthUrl();
  if (!url) return false;
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(), timeoutMs);
  try {
    const res = await fetch(url, { signal: ctl.signal, cache: 'no-store' });
    // a static host answers every path, so the body must be the server's own
    return res.ok && (await res.text()).trim() === 'ok';
  } catch {
    return false;
  } finally {
    clearTimeout(timer);
  }
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

  /** Address of the server's health check, derived from the socket address. */
  /** Address of a page on the game server, derived from the socket address (`/transfer` and the like). */
  static httpUrl(path: string, wsUrl = RemoteTransport.defaultUrl()): string | null {
    try {
      const u = new URL(wsUrl);
      u.protocol = u.protocol === 'wss:' ? 'https:' : 'http:';
      u.pathname = path;
      u.search = '';
      return u.toString();
    } catch {
      return null;
    }
  }

  static healthUrl(wsUrl = RemoteTransport.defaultUrl()): string | null {
    try {
      const u = new URL(wsUrl);
      u.protocol = u.protocol === 'wss:' ? 'https:' : 'http:';
      u.pathname = '/healthz';
      u.search = '';
      return u.toString();
    } catch {
      return null;
    }
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
