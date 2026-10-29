import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import WebSocket from 'ws';
import { startServer } from '../src/server/server';
import type { ServerMsg } from '../src/shared/protocol';

let port = 0;
let close: () => Promise<void>;
beforeAll(async () => {
  const s = await startServer({ port: 0, ephemeral: true, dist: '/nonexistent', limits: { connections: 3, roomsPerHour: 2, badCodes: 3 } });
  port = s.port;
  close = s.close;
});
afterAll(async () => {
  await close();
});

function open(): Promise<{ ws: WebSocket; msgs: ServerMsg[] }> {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(`ws://localhost:${port}/ws`);
    const msgs: ServerMsg[] = [];
    ws.on('message', (raw) => msgs.push(JSON.parse(raw.toString())));
    ws.on('open', () => resolve({ ws, msgs }));
    ws.on('error', reject);
  });
}
const wait = (ms = 120) => new Promise((r) => setTimeout(r, ms));
const errors = (msgs: ServerMsg[]) => msgs.filter((m) => m.t === 'error').map((m) => (m as { message: string }).message);

describe('abuse limits', () => {
  it('uses five-letter room codes', async () => {
    const a = await open();
    a.ws.send(JSON.stringify({ t: 'create', name: 'A' }));
    await wait();
    const room = a.msgs.find((m) => m.t === 'room') as { t: 'room'; room: { code: string } } | undefined;
    expect(room?.room.code).toMatch(/^[A-Z]{5}$/);
    a.ws.close();
  });

  it('limits how many rooms one address can create', async () => {
    const c = await open();
    c.ws.send(JSON.stringify({ t: 'create', name: 'B' }));
    c.ws.send(JSON.stringify({ t: 'create', name: 'B' }));
    c.ws.send(JSON.stringify({ t: 'create', name: 'B' }));
    await wait(200);
    expect(errors(c.msgs).some((e) => /lot of rooms/.test(e))).toBe(true);
    c.ws.close();
  });

  it('pauses someone who keeps guessing room codes', async () => {
    const c = await open();
    for (let i = 0; i < 5; i++) c.ws.send(JSON.stringify({ t: 'join', code: 'ZZZZ' + i, name: 'G' }));
    await wait(200);
    const e = errors(c.msgs);
    expect(e.filter((x) => /No game with that code/.test(x)).length).toBe(3);
    expect(e.some((x) => /Too many wrong codes/.test(x))).toBe(true);
    c.ws.close();
  });

  it('caps simultaneous connections per address', async () => {
    const open3 = await Promise.all([open(), open(), open()]);
    const extra = new WebSocket(`ws://localhost:${port}/ws`);
    const code = await new Promise<number>((res) => { extra.on('close', (c) => res(c)); extra.on('error', () => undefined); });
    expect(code).toBe(1013);
    for (const o of open3) o.ws.close();
  });
});

describe('allowed origins', () => {
  it('refuses a browser page from another site and accepts the listed one', async () => {
    const s = await startServer({ port: 0, ephemeral: true, dist: '/nonexistent', allowedOrigins: ['https://play.example.com'] });
    const tryOpen = (origin?: string) => new Promise<boolean>((resolve) => {
      const ws = new WebSocket(`ws://localhost:${s.port}/ws`, origin ? { headers: { Origin: origin } } : {});
      ws.on('open', () => { ws.close(); resolve(true); });
      ws.on('error', () => resolve(false));
    });
    expect(await tryOpen('https://evil.example.net')).toBe(false);
    expect(await tryOpen('https://play.example.com')).toBe(true);
    expect(await tryOpen()).toBe(true);
    await s.close();
  });
});
