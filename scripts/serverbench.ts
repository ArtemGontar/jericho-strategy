/**
 * Server cost: heap per started room, and CPU / bytes for one command that is broadcast to a room of humans.
 *   node --expose-gc --import tsx scripts/serverbench.ts [rooms] [humansPerRoom]
 */
import { WebSocket } from 'ws';
import type { ClientMsg, ServerMsg } from '../src/shared/protocol';
import { startServer } from '../src/server/server';

const ROOMS = Number(process.argv[2] ?? 20);
const HUMANS = Number(process.argv[3] ?? 4);
const gc = () => (globalThis as { gc?: () => void }).gc?.();
const heap = () => { gc(); gc(); return process.memoryUsage().heapUsed / 1048576; };

const server = await startServer({ port: 0, ephemeral: true, dist: 'dist', limits: { connections: 10000, roomsPerHour: 100000, badCodes: 100000 } });
const url = `ws://localhost:${server.port}/ws`;

interface Client { ws: WebSocket; bytes: number; views: number; room: string; token: string; slot: number; phase: string; errors: string[]; unit: number; wire: () => number }
function connect(): Promise<Client> {
  return new Promise((resolve) => {
    const ws = new WebSocket(url);
    const c: Client = { ws, bytes: 0, views: 0, room: '', token: '', slot: 0, phase: '', errors: [], unit: -1, wire: () => (ws as unknown as { _socket?: { bytesRead: number } })._socket?.bytesRead ?? 0 };
    ws.on('message', (raw) => {
      const s = raw.toString();
      c.bytes += s.length;
      const m = JSON.parse(s) as ServerMsg;
      if (m.t === 'view') { c.views++; const u = m.view.units.find((x) => x.owner === m.view.me); if (u) c.unit = u.id; }
      if (m.t === 'room') { c.room = m.room.code; c.token = m.room.you.token; c.slot = m.room.you.slot; c.phase = m.room.phase; }
      if (m.t === 'error') c.errors.push(m.message);
    });
    ws.on('open', () => resolve(c));
  });
}
const send = (c: Client, m: ClientMsg) => c.ws.send(JSON.stringify(m));
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

const base = heap();
const hosts: Client[] = [];
const all: Client[][] = [];
for (let r = 0; r < ROOMS; r++) {
  const host = await connect();
  send(host, { t: 'create', name: `H${r}` });
  await sleep(15);
  send(host, { t: 'settings', settings: { size: 'large' } });
  const group = [host];
  for (let h = 1; h < HUMANS; h++) {
    const f = await connect();
    send(f, { t: 'join', code: host.room, name: `F${r}_${h}` });
    group.push(f);
  }
  await sleep(15);
  // slots 1 and 2 start open: fill the rest up to 8 seats with computers
  for (let s = HUMANS; s < 8; s++) send(host, { t: 'slot', slot: s, kind: 'ai', aiLevel: 'normal' });
  await sleep(10);
  send(host, { t: 'start' });
  hosts.push(host);
  all.push(group);
  await sleep(40);
}
await sleep(300);
const after = heap();
const started = hosts.filter((h) => h.phase === 'playing').length;
const errs = hosts.flatMap((h) => h.errors).slice(0, 2);
console.log(`${started}/${ROOMS} started rooms ${errs.join('|')} (large map, up to ${Math.min(HUMANS, 3)} humans + AI): +${(after - base).toFixed(1)} MB heap, ${((after - base) / ROOMS).toFixed(2)} MB per room`);

// one broadcast command in the first room
const group = all[0];
for (const c of group) { c.bytes = 0; c.views = 0; }
const wire0 = group.map((c) => c.wire());
const cpu0 = process.cpuUsage();
const N = 50;
for (let i = 0; i < N; i++) send(group[0], { t: 'cmd', id: i, cmd: { t: 'order', unit: group[0].unit, order: i % 2 ? 'cancel' : 'sleep' } });
await sleep(1500);
const cpu = process.cpuUsage(cpu0);
const bytes = group.reduce((a, c) => a + c.bytes, 0);
const views = group.reduce((a, c) => a + c.views, 0);
const wire = group.reduce((a, c, i) => a + c.wire() - wire0[i], 0);
console.log(`${N} commands -> ${views} views sent, ${(bytes / 1024 / N).toFixed(0)} KB per command to the room (${(wire / 1024 / N).toFixed(0)} KB on the wire), server CPU ${((cpu.user + cpu.system) / 1000 / N).toFixed(2)} ms per command`);

// leave: everyone disconnects; are rooms kept (they are, until the sweep)?
for (const g of all) for (const c of g) c.ws.close();
await sleep(500);
const open = heap();
console.log(`after every player left: +${(open - base).toFixed(1)} MB (rooms stay until the idle sweep)`);
await server.close();
await sleep(200);
console.log(`after server.close(): +${(heap() - base).toFixed(1)} MB`);
process.exit(0);
