import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
import { existsSync, mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { extname, join, normalize, resolve } from 'node:path';
import { randomBytes } from 'node:crypto';
import { WebSocketServer, WebSocket } from 'ws';
import { Game, type PlayerSlot } from '../shared/game';
import { buildView, filterEvents } from '../shared/view';
import { previewAttack } from '../shared/combat';
import { CIVS, MAX_PLAYERS } from '../shared/data';
import {
  DEFAULT_ROOM_SETTINGS, type ClientMsg, type RoomInfo, type RoomSettings, type RoomSlot, type ServerMsg,
} from '../shared/protocol';
import type { GameState } from '../shared/types';

export interface ServerOptions {
  port?: number;
  dist?: string;
  dataDir?: string;
  /** do not persist rooms to disk (tests) */
  ephemeral?: boolean;
}

let DIST = '';
let DATA = '';
let EPHEMERAL = false;
const MIME: Record<string, string> = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8',
  '.svg': 'image/svg+xml', '.png': 'image/png', '.json': 'application/json', '.ico': 'image/x-icon', '.woff2': 'font/woff2', '.map': 'application/json',
};

interface Member {
  slot: number;
  token: string;
  ws: WebSocket | null;
}

class Room {
  code: string;
  phase: 'lobby' | 'playing' | 'over' = 'lobby';
  slots: RoomSlot[];
  settings: RoomSettings = { ...DEFAULT_ROOM_SETTINGS };
  members: Member[] = [];
  game: Game | null = null;
  timer: NodeJS.Timeout | null = null;
  timerEnds = 0;
  lastActive = Date.now();

  constructor(code: string) {
    this.code = code;
    this.slots = Array.from({ length: MAX_PLAYERS }, (_, i) => ({
      slot: i, kind: i === 0 ? 'human' : i < 3 ? 'open' : 'closed', name: '', civ: null, connected: false, host: i === 0, aiLevel: 'normal',
    }));
  }

  info(member: Member): RoomInfo {
    return {
      code: this.code, phase: this.phase, slots: this.slots, settings: this.settings,
      you: { slot: member.slot, token: member.token, host: this.slots[member.slot].host },
    };
  }

  send(ws: WebSocket | null, msg: ServerMsg): void {
    if (ws && ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(msg));
  }

  broadcastRoom(): void {
    for (const m of this.members) this.send(m.ws, { t: 'room', room: this.info(m) });
  }

  playerIndexOf(slot: number): number {
    // game player ids follow the order of active slots at start
    return this.activeSlots().findIndex((s) => s.slot === slot);
  }

  activeSlots(): RoomSlot[] {
    return this.slots.filter((s) => s.kind === 'human' || s.kind === 'ai');
  }

  sendViews(events: ReturnType<Game['events']['slice']> = []): void {
    if (!this.game) return;
    for (const m of this.members) {
      const pid = this.playerIndexOf(m.slot);
      if (pid < 0) continue;
      this.send(m.ws, { t: 'view', view: buildView(this.game, pid, this.timerEnds ? Math.max(0, this.timerEnds - Date.now()) : 0), events: filterEvents(events, pid) });
    }
  }

  start(): string | null {
    const active = this.activeSlots();
    if (active.length < 2) return 'You need at least two players (add an AI or invite a friend).';
    const used = new Set<string>();
    for (const s of active) if (s.civ) used.add(s.civ);
    const players: PlayerSlot[] = active.map((s, i) => ({
      name: s.name || (s.kind === 'ai' ? `${CIVS.find((c) => c.id === s.civ)?.leader ?? 'Computer'} (AI)` : `Player ${i + 1}`),
      civ: s.civ ?? undefined, ai: s.kind === 'ai', aiLevel: s.aiLevel,
    }));
    const game = Game.create({
      seed: this.settings.seed ?? undefined,
      size: this.settings.size,
      speed: this.settings.speed,
      turnLimit: this.settings.turnLimit,
      turnTimer: this.settings.turnTimer,
      startAtWar: this.settings.startAtWar,
      allowScience: this.settings.scienceVictory,
      players,
    });
    active.forEach((s, i) => {
      if (s.kind === 'ai') {
        game.players[i].name = `${game.civOf(i).leader} (AI)`;
      } else game.players[i].connected = this.members.some((m) => m.slot === s.slot && m.ws !== null);
    });
    this.game = game;
    this.phase = 'playing';
    this.armTimer();
    this.persist();
    this.broadcastRoom();
    this.sendViews([]);
    return null;
  }

  armTimer(): void {
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
    this.timerEnds = 0;
    if (!this.game || this.game.s.over || this.settings.turnTimer <= 0) return;
    this.timerEnds = Date.now() + this.settings.turnTimer * 1000;
    this.timer = setTimeout(() => this.forceEndTurn(), this.settings.turnTimer * 1000 + 50);
  }

  forceEndTurn(): void {
    const g = this.game;
    if (!g || g.s.over) return;
    for (const p of g.players) if (p.alive && !p.ai) p.endedTurn = true;
    this.afterEnd(g.turn);
  }

  /** Called after any command; resolves the turn if everyone is done and pushes views. */
  afterEnd(beforeTurn: number): void {
    const g = this.game!;
    g.maybeProcessTurn();
    const events = g.events.splice(0);
    if (g.turn !== beforeTurn || g.s.over) {
      this.armTimer();
      this.persist();
    }
    if (g.s.over && this.phase !== 'over') {
      this.phase = 'over';
      this.broadcastRoom();
    }
    this.sendViews(events);
  }

  persist(): void {
    if (!this.game || EPHEMERAL) return;
    try {
      mkdirSync(DATA, { recursive: true });
      const snapshot = { code: this.code, phase: this.phase, slots: this.slots, settings: this.settings, tokens: this.members.map((m) => ({ slot: m.slot, token: m.token })), state: this.game.toJSON() };
      writeFileSync(join(DATA, `${this.code}.json`), JSON.stringify(snapshot));
    } catch (e) {
      console.error('persist failed', e);
    }
  }
}


let rooms = new Map<string, Room>();

function newCode(): string {
  const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ';
  for (;;) {
    let c = '';
    for (let i = 0; i < 4; i++) c += alphabet[Math.floor(Math.random() * alphabet.length)];
    if (!rooms.has(c)) return c;
  }
}

function loadRooms(): void {
  if (!existsSync(DATA)) return;
  for (const f of readdirSync(DATA)) {
    if (!f.endsWith('.json')) continue;
    try {
      const path = join(DATA, f);
      if (Date.now() - statSync(path).mtimeMs > 3 * 24 * 3600 * 1000) continue;
      const snap = JSON.parse(readFileSync(path, 'utf8')) as { code: string; phase: Room['phase']; slots: RoomSlot[]; settings: RoomSettings; tokens: Array<{ slot: number; token: string }>; state: GameState };
      const room = new Room(snap.code);
      room.phase = snap.phase;
      room.slots = snap.slots.map((s) => ({ ...s, connected: false }));
      room.settings = snap.settings;
      room.members = snap.tokens.map((t) => ({ slot: t.slot, token: t.token, ws: null }));
      room.game = Game.fromJSON(snap.state);
      for (const p of room.game.players) if (!p.ai) p.connected = false;
      rooms.set(room.code, room);
      room.armTimer();
    } catch (e) {
      console.error('could not restore', f, e);
    }
  }
  if (rooms.size) console.log(`Restored ${rooms.size} room(s)`);
}

// ----------------------------------------------------------------------------- http
function serveStatic(req: IncomingMessage, res: ServerResponse): void {
  const url = new URL(req.url ?? '/', 'http://x');
  if (url.pathname === '/healthz') {
    res.writeHead(200, { 'content-type': 'text/plain' });
    res.end('ok');
    return;
  }
  let rel = normalize(decodeURIComponent(url.pathname)).replace(/^([/\\])+/, '');
  if (rel.includes('..')) rel = '';
  let file = join(DIST, rel);
  if (!existsSync(file) || statSync(file).isDirectory()) file = join(DIST, 'index.html');
  if (!existsSync(file)) {
    res.writeHead(404, { 'content-type': 'text/plain' });
    res.end('Build the client first: npm run build');
    return;
  }
  const ext = extname(file);
  const immutable = file.includes(`${join(DIST, 'assets')}`);
  res.writeHead(200, { 'content-type': MIME[ext] ?? 'application/octet-stream', 'cache-control': immutable ? 'public, max-age=31536000, immutable' : 'no-cache' });
  res.end(readFileSync(file));
}

function attachSockets(wss: WebSocketServer): void {

wss.on('connection', (ws) => {
  let room: Room | null = null;
  let member: Member | null = null;
  let budget = 40;
  const refill = setInterval(() => { budget = Math.min(60, budget + 20); }, 1000);
  const err = (message: string) => ws.send(JSON.stringify({ t: 'error', message } satisfies ServerMsg));

  const attach = (r: Room, m: Member) => {
    room = r;
    member = m;
    if (m.ws && m.ws !== ws) try { m.ws.close(4000, 'replaced'); } catch { /* ignore */ }
    m.ws = ws;
    r.slots[m.slot].connected = true;
    if (r.game) {
      const pid = r.playerIndexOf(m.slot);
      if (pid >= 0) r.game.players[pid].connected = true;
    }
    r.lastActive = Date.now();
    r.broadcastRoom();
    if (r.game) r.sendViews();
  };

  ws.on('message', (raw) => {
    if (--budget < 0) {
      err('Slow down a little — too many actions at once.');
      return;
    }
    let msg: ClientMsg;
    try {
      msg = JSON.parse(raw.toString()) as ClientMsg;
    } catch {
      return;
    }
    try {
      handle(msg);
    } catch (e) {
      console.error('handler error', e);
      err('Something went wrong on the server.');
    }
  });

  function handle(msg: ClientMsg): void {
    if (msg.t === 'ping') return void ws.send(JSON.stringify({ t: 'pong' } satisfies ServerMsg));
    if (msg.t === 'create') {
      if (rooms.size >= 300) return err('The server is full right now. Try again in a few minutes.');
      const r = new Room(newCode());
      rooms.set(r.code, r);
      const m: Member = { slot: 0, token: randomBytes(12).toString('hex'), ws: null };
      r.members.push(m);
      r.slots[0].name = (msg.name || 'Host').slice(0, 20);
      attach(r, m);
      return;
    }
    if (msg.t === 'join') {
      const r = rooms.get((msg.code || '').toUpperCase().trim());
      if (!r) return err('No game with that code.');
      const returning = msg.token ? r.members.find((m) => m.token === msg.token) : undefined;
      if (returning) {
        attach(r, returning);
        return;
      }
      if (r.phase !== 'lobby') return err('That game has already started.');
      const free = r.slots.find((s) => s.kind === 'open');
      if (!free) return err('The game is full.');
      free.kind = 'human';
      free.name = (msg.name || `Player ${free.slot + 1}`).slice(0, 20);
      const m: Member = { slot: free.slot, token: randomBytes(12).toString('hex'), ws: null };
      r.members.push(m);
      attach(r, m);
      return;
    }
    if (!room || !member) return err('Join a game first.');
    const r = room;
    const m = member;
    r.lastActive = Date.now();
    const isHost = r.slots[m.slot].host;
    switch (msg.t) {
      case 'leave': {
        if (r.phase === 'lobby') {
          const s = r.slots[m.slot];
          if (!s.host) {
            s.kind = 'open'; s.name = ''; s.civ = null; s.connected = false;
            r.members = r.members.filter((x) => x !== m);
          }
        } else {
          r.slots[m.slot].connected = false;
          m.ws = null;
        }
        room = null;
        member = null;
        r.broadcastRoom();
        return;
      }
      case 'rename':
        r.slots[m.slot].name = (msg.name || '').slice(0, 20);
        return r.broadcastRoom();
      case 'civ': {
        if (r.phase !== 'lobby') return;
        if (msg.civ && !CIVS.some((c) => c.id === msg.civ)) return;
        const target = msg.slot !== undefined && isHost && r.slots[msg.slot]?.kind === 'ai' ? msg.slot : m.slot;
        if (msg.civ && r.slots.some((s) => s.slot !== target && s.civ === msg.civ && (s.kind === 'human' || s.kind === 'ai'))) return err('That civilisation is taken.');
        r.slots[target].civ = msg.civ;
        return r.broadcastRoom();
      }
      case 'settings': {
        if (!isHost || r.phase !== 'lobby') return;
        const s = msg.settings;
        if (s.size && ['small', 'medium', 'large'].includes(s.size)) r.settings.size = s.size;
        if (s.speed && ['standard', 'quick'].includes(s.speed)) r.settings.speed = s.speed;
        if (typeof s.turnLimit === 'number') r.settings.turnLimit = Math.max(40, Math.min(400, Math.floor(s.turnLimit)));
        if (typeof s.turnTimer === 'number') r.settings.turnTimer = Math.max(0, Math.min(600, Math.floor(s.turnTimer)));
        if (s.seed === null || typeof s.seed === 'number') r.settings.seed = s.seed;
        if (typeof s.startAtWar === 'boolean') r.settings.startAtWar = s.startAtWar;
        if (typeof s.scienceVictory === 'boolean') r.settings.scienceVictory = s.scienceVictory;
        return r.broadcastRoom();
      }
      case 'slot': {
        if (!isHost || r.phase !== 'lobby') return;
        const s = r.slots[msg.slot];
        if (!s || s.host) return;
        if (s.kind === 'human' && s.connected) {
          // kick the human
          const mem = r.members.find((x) => x.slot === s.slot);
          if (mem?.ws) r.send(mem.ws, { t: 'error', message: 'The host removed you from the game.' });
          if (mem?.ws) try { mem.ws.close(4001, 'kicked'); } catch { /* ignore */ }
          r.members = r.members.filter((x) => x !== mem);
        }
        s.kind = msg.kind;
        s.name = msg.kind === 'ai' ? 'Computer' : '';
        s.connected = false;
        if (msg.kind !== 'ai') s.civ = null;
        if (msg.aiLevel) s.aiLevel = msg.aiLevel;
        return r.broadcastRoom();
      }
      case 'start': {
        if (!isHost || r.phase !== 'lobby') return;
        const e = r.start();
        if (e) err(e);
        return;
      }
      case 'chat': {
        const text = (msg.text || '').trim().slice(0, 240);
        if (!text) return;
        const name = r.slots[m.slot].name || `Player ${m.slot + 1}`;
        for (const x of r.members) r.send(x.ws, { t: 'chat', from: name, slot: r.playerIndexOf(m.slot), text });
        return;
      }
      case 'cmd': {
        if (!r.game || r.phase === 'lobby') return;
        const pid = r.playerIndexOf(m.slot);
        if (pid < 0) return;
        const before = r.game.turn;
        const res = r.game.exec(pid, msg.cmd);
        r.send(ws, { t: 'result', id: msg.id, res });
        if (msg.cmd.t === 'end' || msg.cmd.t === 'unend') {
          r.afterEnd(before);
        } else {
          const events = r.game.events.splice(0);
          if (res.ok) r.sendViews(events);
          if (r.game.s.over && r.phase !== 'over') { r.phase = 'over'; r.persist(); r.broadcastRoom(); }
        }
        return;
      }
      case 'preview': {
        if (!r.game) return;
        const pid = r.playerIndexOf(m.slot);
        const u = r.game.unit(msg.unit);
        if (!u || u.owner !== pid) return;
        r.send(ws, { t: 'result', id: msg.id, res: { ok: true }, preview: previewAttack(r.game, u, msg.to.x, msg.to.y) });
        return;
      }
      case 'rematch': {
        if (!isHost || r.phase !== 'over') return;
        if (r.timer) clearTimeout(r.timer);
        r.timer = null;
        r.game = null;
        r.phase = 'lobby';
        // drop seats of people who left, keep everyone still connected plus computer opponents
        for (const s of r.slots) {
          if (s.kind === 'human' && !s.connected && !s.host) {
            s.kind = 'open'; s.name = ''; s.civ = null;
            r.members = r.members.filter((x) => x.slot !== s.slot);
          }
        }
        r.broadcastRoom();
        return;
      }
      case 'forceEnd': {
        if (!isHost || !r.game) return;
        r.forceEndTurn();
        return;
      }
      case 'takeover': {
        // host hands a disconnected player's civilisation to the computer
        if (!isHost || !r.game) return;
        const pid = r.playerIndexOf(msg.slot);
        if (pid < 0) return;
        const gp = r.game.players[pid];
        if (gp.ai || r.slots[msg.slot].connected) return;
        gp.ai = true;
        gp.name = `${gp.name} (AI)`;
        r.slots[msg.slot].kind = 'ai';
        r.broadcastRoom();
        r.afterEnd(r.game.turn);
        return;
      }
    }
  }

  ws.on('close', () => {
    clearInterval(refill);
    if (room && member) {
      const r = room;
      const m = member;
      if (m.ws === ws) {
        m.ws = null;
        r.slots[m.slot].connected = false;
        if (r.game) {
          const pid = r.playerIndexOf(m.slot);
          if (pid >= 0) r.game.players[pid].connected = false;
        }
        if (r.phase === 'lobby' && !r.slots[m.slot].host) {
          const s = r.slots[m.slot];
          s.kind = 'open'; s.name = ''; s.civ = null;
          r.members = r.members.filter((x) => x !== m);
        }
        r.broadcastRoom();
        if (r.game) r.sendViews();
      }
    }
  });
});


}

export function startServer(opts: ServerOptions = {}): Promise<{ port: number; close: () => Promise<void> }> {
  DIST = resolve(opts.dist ?? process.env.DIST ?? join(process.cwd(), 'dist'));
  DATA = resolve(opts.dataDir ?? process.env.DATA_DIR ?? join(process.cwd(), 'server-data'));
  EPHEMERAL = !!opts.ephemeral;
  rooms = new Map();
  const server = createServer(serveStatic);
  const wss = new WebSocketServer({ server, path: '/ws', maxPayload: 16 * 1024 });
  attachSockets(wss);
  const sweep = setInterval(() => {
    const now = Date.now();
    for (const [code, r] of rooms) {
      const anyone = r.members.some((m) => m.ws);
      if (!anyone && now - r.lastActive > 6 * 3600 * 1000) {
        if (r.timer) clearTimeout(r.timer);
        rooms.delete(code);
      }
    }
  }, 10 * 60 * 1000);
  sweep.unref();
  if (!EPHEMERAL) loadRooms();
  return new Promise((resolveStart) => {
    server.listen(opts.port ?? Number(process.env.PORT ?? 8080), () => {
      const addr = server.address();
      const port = typeof addr === 'object' && addr ? addr.port : 0;
      resolveStart({
        port,
        close: () => new Promise((res) => {
          clearInterval(sweep);
          for (const r of rooms.values()) if (r.timer) clearTimeout(r.timer);
          for (const c of wss.clients) c.terminate();
          wss.close(() => server.close(() => res()));
        }),
      });
    });
  });
}
