import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { startServer } from '../src/server/server';
import { TRANSFER_MAX_BYTES, resetTransfers } from '../src/server/transfer';
import { Game } from '../src/shared/game';
import { makeRecord } from '../src/client/saves';

let base = '';
let close: () => Promise<void>;
let restricted = '';
let closeRestricted: () => Promise<void>;
beforeAll(async () => {
  const s = await startServer({ port: 0, ephemeral: true, dist: '/nonexistent' });
  base = `http://localhost:${s.port}`;
  close = s.close;
  const r = await startServer({ port: 0, ephemeral: true, dist: '/nonexistent', allowedOrigins: ['https://play.example.com'] });
  restricted = `http://localhost:${r.port}`;
  closeRestricted = r.close;
});
beforeEach(() => resetTransfers());
afterAll(async () => {
  await close();
  await closeRestricted();
});

const save = () => JSON.stringify(makeRecord(Game.create({ seed: 3, size: 'small', players: [{ name: 'A', ai: false }, { name: 'B', ai: true }] }).toJSON(), [0]));
const post = (url: string, body: string, headers: Record<string, string> = {}) => fetch(`${url}/transfer`, { method: 'POST', body, headers: { 'content-type': 'application/json', ...headers } });

describe('sending a saved game to another device', () => {
  it('hands the save over once, by a short code', async () => {
    const body = save();
    const r = await post(base, body);
    expect(r.status).toBe(200);
    const { code } = (await r.json()) as { code: string };
    expect(code).toMatch(/^[A-HJ-NP-Z2-9]{8}$/);
    const got = await fetch(`${base}/transfer/${code}`);
    expect(got.status).toBe(200);
    expect(await got.text()).toBe(body);
    // it works once
    expect((await fetch(`${base}/transfer/${code}`)).status).toBe(404);
  });

  it('accepts the code in lower case and with spaces or dashes, as people type it', async () => {
    const { code } = (await (await post(base, save())).json()) as { code: string };
    const typed = `${code.slice(0, 4)}-${code.slice(4)}`.toLowerCase();
    expect((await fetch(`${base}/transfer/${encodeURIComponent(typed)}`)).status).toBe(200);
  });

  it('refuses anything that is not a save, and anything too large', async () => {
    expect((await post(base, 'hello')).status).toBe(400);
    expect((await post(base, '{"a":1}')).status).toBe(400);
    expect((await post(base, JSON.stringify({ state: { players: 1, tiles: [] } }))).status).toBe(400);
    const big = JSON.stringify({ state: { players: [], tiles: [], pad: 'x'.repeat(TRANSFER_MAX_BYTES + 10) } });
    const r = await post(base, big).catch(() => null);
    // the server may cut the connection once it has said no
    if (r) expect(r.status).toBe(413);
  });

  it('knows nothing of a wrong code, and slows down someone who keeps guessing', async () => {
    const own = await startServer({ port: 0, ephemeral: true, dist: '/nonexistent' });
    try {
      const url = `http://localhost:${own.port}`;
      let last = 0;
      for (let i = 0; i < 20; i++) last = (await fetch(`${url}/transfer/ABCDEFGH`)).status;
      expect(last).toBe(429);
    } finally {
      await own.close();
    }
  });

  it('limits how many saves one address may send in an hour', async () => {
    const own = await startServer({ port: 0, ephemeral: true, dist: '/nonexistent' });
    try {
      const url = `http://localhost:${own.port}`;
      const body = save();
      const statuses: number[] = [];
      for (let i = 0; i < 14; i++) statuses.push((await post(url, body)).status);
      expect(statuses.slice(0, 12).every((s) => s === 200)).toBe(true);
      expect(statuses[13]).toBe(429);
    } finally {
      await own.close();
    }
  });

  it('answers a page from another site only when the server trusts it', async () => {
    const ok = await post(restricted, save(), { origin: 'https://play.example.com' });
    expect(ok.status).toBe(200);
    expect(ok.headers.get('access-control-allow-origin')).toBe('https://play.example.com');
    const no = await post(restricted, save(), { origin: 'https://evil.example.net' });
    expect(no.status).toBe(403);
    expect(no.headers.get('access-control-allow-origin')).toBeNull();
    const pre = await fetch(`${restricted}/transfer`, { method: 'OPTIONS', headers: { origin: 'https://play.example.com' } });
    expect(pre.status).toBe(204);
  });

  it('leaves the rest of the server alone', async () => {
    expect((await fetch(`${base}/healthz`)).status).toBe(200);
    expect((await fetch(`${base}/transfer/`)).status).toBe(404);
  });
});
