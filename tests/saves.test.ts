import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { Game } from '../src/shared/game';
import { LEGACY_KEY, SAVE_FORMAT, exportText, importText, keyOf, listSaves, makeRecord, migrate, newestReadable, readSave, removeSave, writeSave, type Store } from '../src/client/saves';

class Mem implements Store {
  data = new Map<string, string>();
  limit = Infinity;
  getItem(k: string) { return this.data.get(k) ?? null; }
  setItem(k: string, v: string) {
    if (v.length > this.limit) { const e = new Error('The quota has been exceeded.'); e.name = 'QuotaExceededError'; throw e; }
    this.data.set(k, v);
  }
  removeItem(k: string) { this.data.delete(k); }
}

const v1 = readFileSync(new URL('./fixtures/save-v1.json', import.meta.url), 'utf8');

describe('saved games', () => {
  it('opens a save written by the first release (no format field, single-slot key)', () => {
    const store = new Mem();
    store.setItem(LEGACY_KEY, v1);
    const res = readSave(store, 'auto');
    expect(res.ok).toBe(true);
    if (!res.ok) return;
    expect(res.record.format).toBe(SAVE_FORMAT);
    expect(res.record.humans).toEqual([0]);
    const game = Game.fromJSON(res.record.state);
    const turn = game.turn;
    expect(turn).toBeGreaterThan(1);
    // the game still plays on from it
    game.players[0].endedTurn = true;
    game.maybeProcessTurn();
    expect(game.turn).toBe(turn + 1);
  });

  it('moves the old save to the autosave key on the next write and drops the legacy copy', () => {
    const store = new Mem();
    store.setItem(LEGACY_KEY, v1);
    const rec = (readSave(store, 'auto') as { ok: true; record: ReturnType<typeof makeRecord> }).record;
    expect(writeSave(store, 'auto', rec).ok).toBe(true);
    expect(store.getItem(LEGACY_KEY)).toBeNull();
    expect(store.getItem(keyOf('auto'))).not.toBeNull();
  });

  it('round-trips a record through export and import', () => {
    const store = new Mem();
    const rec = migrate(JSON.parse(v1));
    writeSave(store, 'slot2', rec);
    const text = exportText(store, 'slot2')!;
    const back = importText(text);
    expect(back.state.turn).toBe(rec.state.turn);
    expect(back.humans).toEqual(rec.humans);
  });

  it('lists slots with a readable summary and the newest readable save wins', () => {
    const store = new Mem();
    const rec = migrate(JSON.parse(v1));
    writeSave(store, 'slot1', { ...rec, savedAt: 1000 });
    writeSave(store, 'auto', { ...rec, savedAt: 5000 });
    const list = listSaves(store);
    expect(list.map((s) => s.id)).toEqual(['auto', 'slot1']);
    expect(list[0].label).toMatch(/turn \d+/);
    expect(newestReadable(store)?.id).toBe('auto');
  });

  it('keeps a save it cannot read, says why, and still exports it unchanged', () => {
    const store = new Mem();
    store.setItem(keyOf('slot1'), '{"format":99,"state":{}}');
    store.setItem(keyOf('slot2'), '{not json');
    const list = listSaves(store);
    expect(list.every((s) => !s.ok)).toBe(true);
    expect(list.find((s) => s.id === 'slot1')?.problem).toMatch(/newer version/);
    expect(list.find((s) => s.id === 'slot2')?.problem).toMatch(/damaged/);
    expect(newestReadable(store)).toBeNull();
    expect(exportText(store, 'slot2')).toBe('{not json');
    // nothing was deleted by looking
    expect(store.getItem(keyOf('slot1'))).not.toBeNull();
    removeSave(store, 'slot1');
    expect(store.getItem(keyOf('slot1'))).toBeNull();
  });

  it('reports a full or refused store instead of pretending the game is saved', () => {
    const rec = migrate(JSON.parse(v1));
    const full = new Mem();
    full.limit = 10;
    const a = writeSave(full, 'auto', rec);
    expect(a.ok).toBe(false);
    if (!a.ok) expect(a.error).toMatch(/storage space/);
    const refusing: Store = { getItem: () => null, setItem: () => { throw new Error('denied'); }, removeItem: () => {} };
    const b = writeSave(refusing, 'auto', rec);
    expect(b.ok).toBe(false);
    if (!b.ok) expect(b.error).toMatch(/private browsing/);
  });

  it('rejects files that are not saves', () => {
    expect(() => importText('hello')).toThrow(/not a saved game/);
    expect(() => importText('{"a":1}')).toThrow(/not a saved game/);
    expect(() => importText(JSON.stringify({ humans: [5], state: { players: [], tiles: [], settings: {} } }))).toThrow(/damaged/);
  });
});
