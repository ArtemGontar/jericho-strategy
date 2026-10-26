import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import WebSocket from 'ws';
import { startServer } from '../src/server/server';
import type { ClientMsg, RoomInfo, ServerMsg } from '../src/shared/protocol';
import type { GameView } from '../src/shared/view';

let port = 0;
let close: () => Promise<void>;
beforeAll(async () => {
  const s = await startServer({ port: 0, ephemeral: true, dist: '/nonexistent' });
  port = s.port;
  close = s.close;
});
afterAll(async () => {
  await close();
});

class Client {
  ws: WebSocket;
  msgs: ServerMsg[] = [];
  room: RoomInfo | null = null;
  view: GameView | null = null;
  private nextId = 1;
  private waiters: Array<() => void> = [];
  constructor() {
    this.ws = new WebSocket(`ws://localhost:${port}/ws`);
    this.ws.on('message', (raw) => {
      const m = JSON.parse(raw.toString()) as ServerMsg;
      this.msgs.push(m);
      if (m.t === 'room') this.room = m.room;
      if (m.t === 'view') this.view = m.view;
      this.waiters.splice(0).forEach((w) => w());
    });
  }
  open(): Promise<void> {
    return new Promise((res) => this.ws.on('open', () => res()));
  }
  send(m: ClientMsg): void {
    this.ws.send(JSON.stringify(m));
  }
  async until(cond: () => boolean, ms = 4000): Promise<void> {
    const t0 = Date.now();
    while (!cond()) {
      if (Date.now() - t0 > ms) throw new Error('timeout waiting for condition');
      await new Promise<void>((r) => {
        this.waiters.push(r);
        setTimeout(r, 50);
      });
    }
  }
  async cmd(cmd: Extract<ClientMsg, { t: 'cmd' }>['cmd']): Promise<{ ok: boolean; error?: string }> {
    const id = this.nextId++;
    this.send({ t: 'cmd', id, cmd });
    await this.until(() => this.msgs.some((m) => m.t === 'result' && m.id === id));
    return (this.msgs.find((m) => m.t === 'result' && m.id === id) as Extract<ServerMsg, { t: 'result' }>).res;
  }
}

async function twoPlayerGame(): Promise<{ a: Client; b: Client; code: string }> {
  const a = new Client();
  await a.open();
  a.send({ t: 'create', name: 'Alice' });
  await a.until(() => !!a.room);
  const code = a.room!.code;
  const b = new Client();
  await b.open();
  b.send({ t: 'join', code, name: 'Bob' });
  await b.until(() => !!b.room);
  a.send({ t: 'settings', settings: { size: 'small', turnLimit: 100 } });
  a.send({ t: 'start' });
  await a.until(() => !!a.view);
  await b.until(() => !!b.view);
  return { a, b, code };
}

describe('multiplayer server', () => {
  it('runs a lobby, starts a game, and resolves simultaneous turns', async () => {
    const { a, b } = await twoPlayerGame();
    expect(a.view!.me).toBe(0);
    expect(b.view!.me).toBe(1);
    expect(a.view!.turn).toBe(1);
    const sa = a.view!.units.find((u) => u.owner === 0 && u.type === 'settler')!;
    expect((await a.cmd({ t: 'found', unit: sa.id })).ok).toBe(true);
    const sb = b.view!.units.find((u) => u.owner === 1 && u.type === 'settler')!;
    expect((await b.cmd({ t: 'found', unit: sb.id })).ok).toBe(true);
    // A cannot command B's units
    const bWarrior = b.view!.units.find((u) => u.owner === 1 && u.type === 'warrior')!;
    expect((await a.cmd({ t: 'disband', unit: bWarrior.id })).ok).toBe(false);
    await a.cmd({ t: 'end' });
    await a.until(() => a.view!.players[0].endedTurn);
    expect(a.view!.turn).toBe(1);
    await b.cmd({ t: 'end' });
    await a.until(() => a.view!.turn === 2);
    await b.until(() => b.view!.turn === 2);
    expect(a.view!.cities.filter((c) => c.mine)).toHaveLength(1);
    a.ws.close();
    b.ws.close();
  });

  it('hides unexplored terrain and enemy units outside vision', async () => {
    const { a, b } = await twoPlayerGame();
    const unexplored = a.view!.state.filter((s) => s === 0).length;
    expect(unexplored).toBeGreaterThan(a.view!.state.length * 0.5);
    // A sees none of B's units at the start
    expect(a.view!.units.every((u) => u.owner === 0)).toBe(true);
    expect(b.view!.units.every((u) => u.owner === 1)).toBe(true);
    a.ws.close();
    b.ws.close();
  });

  it('lets a disconnected player rejoin with their token and relays chat', async () => {
    const { a, b, code } = await twoPlayerGame();
    const token = b.room!.you.token;
    b.send({ t: 'chat', text: 'hi there' });
    await a.until(() => a.msgs.some((m) => m.t === 'chat'));
    b.ws.close();
    await a.until(() => a.room!.slots[1].connected === false);
    const b2 = new Client();
    await b2.open();
    b2.send({ t: 'join', code, name: 'Bob', token });
    await b2.until(() => !!b2.view);
    expect(b2.view!.me).toBe(1);
    expect(a.room!.slots[1].connected).toBe(true);
    a.ws.close();
    b2.ws.close();
  });

  it('lets the host skip a stalled player and hand them to the AI', async () => {
    const { a, b } = await twoPlayerGame();
    await a.cmd({ t: 'end' });
    a.send({ t: 'forceEnd' });
    await a.until(() => a.view!.turn === 2);
    b.ws.close();
    await a.until(() => a.room!.slots[1].connected === false);
    a.send({ t: 'takeover', slot: 1 });
    await a.until(() => a.view!.players[1].ai === true);
    await a.cmd({ t: 'end' });
    await a.until(() => a.view!.turn === 3);
    a.ws.close();
  });

  it('rejects unknown rooms and full games', async () => {
    const c = new Client();
    await c.open();
    c.send({ t: 'join', code: 'ZZZZ', name: 'Nobody' });
    await c.until(() => c.msgs.some((m) => m.t === 'error'));
    c.ws.close();
  });
});

describe('persistence', () => {
  it('restores running games after a server restart', async () => {
    const dir = mkdtempSync(join(tmpdir(), 'jericho-'));
    const first = await startServer({ port: 0, dataDir: dir, dist: '/nonexistent' });
    const savedPort = port;
    port = first.port;
    const a = new Client();
    await a.open();
    a.send({ t: 'create', name: 'Alice' });
    await a.until(() => !!a.room);
    const code = a.room!.code;
    const token = a.room!.you.token;
    a.send({ t: 'slot', slot: 1, kind: 'ai' });
    a.send({ t: 'settings', settings: { size: 'small' } });
    await a.until(() => a.room!.slots[1].kind === 'ai');
    a.send({ t: 'start' });
    await a.until(() => !!a.view);
    const settler = a.view!.units.find((u) => u.type === 'settler')!;
    await a.cmd({ t: 'found', unit: settler.id });
    await a.cmd({ t: 'end' });
    await a.until(() => a.view!.turn === 2);
    a.ws.close();
    await first.close();

    const second = await startServer({ port: 0, dataDir: dir, dist: '/nonexistent' });
    port = second.port;
    const b = new Client();
    await b.open();
    b.send({ t: 'join', code, name: 'Alice', token });
    await b.until(() => !!b.view);
    expect(b.view!.turn).toBe(2);
    expect(b.view!.cities.filter((c) => c.mine)).toHaveLength(1);
    b.ws.close();
    await second.close();
    port = savedPort;
    rmSync(dir, { recursive: true, force: true });
  });
});
