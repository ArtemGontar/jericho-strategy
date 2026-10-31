/**
 * Saved games: one autosave and three manual slots, each a small versioned record.
 *
 * Nothing here ever deletes a save it cannot read: an unreadable save stays in storage so it can be exported or
 * kept for a newer version of the game. Storage is passed in so the logic runs (and is tested) without a browser.
 */
import { CIV_BY_ID } from '../shared/data';
import { STATE_VERSION } from '../shared/game';
import type { GameState } from '../shared/types';

/** Shape of the record written by this build. Version 1 (before slots) stored `{ humans, state }` under `jericho.save.v1`. */
export const SAVE_FORMAT = 2;
export const SLOT_COUNT = 3;
export const LEGACY_KEY = 'jericho.save.v1';

export type SaveId = 'auto' | 'slot1' | 'slot2' | 'slot3';
export const SAVE_IDS: SaveId[] = ['auto', 'slot1', 'slot2', 'slot3'];
export const keyOf = (id: SaveId): string => (id === 'auto' ? 'jericho.save.auto' : `jericho.save.${id}`);
export const slotName = (id: SaveId): string => (id === 'auto' ? 'Autosave' : `Slot ${id.slice(4)}`);

export interface SaveRecord {
  format: number;
  savedAt: number;
  /** which player seats are people on this device */
  humans: number[];
  state: GameState;
}

export interface SaveSummary {
  id: SaveId;
  /** false when something is stored but cannot be opened by this build */
  ok: boolean;
  /** human-readable reason when `ok` is false */
  problem?: string;
  savedAt: number;
  turn: number;
  label: string;
  players: number;
}

export type Store = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;

/** Turn whatever was stored into a current record, or throw an Error that says why it cannot be used. */
export function migrate(raw: unknown): SaveRecord {
  if (!raw || typeof raw !== 'object') throw new Error('The file is not a saved game.');
  const r = raw as Record<string, unknown>;
  // version 1: no format field, `{ humans, state }`
  const format = typeof r.format === 'number' ? r.format : r.state && r.humans ? 1 : 0;
  if (format === 0) throw new Error('The file is not a saved game.');
  if (format > SAVE_FORMAT) throw new Error('This save was made by a newer version of the game. Reload the page to update, then try again.');
  const state = r.state as GameState | undefined;
  if (!state || typeof state !== 'object' || !Array.isArray(state.players) || !Array.isArray(state.tiles) || !state.settings) throw new Error('The save is damaged.');
  if (typeof state.version === 'number' && state.version > STATE_VERSION) throw new Error('This save was made by a newer version of the game. Reload the page to update, then try again.');
  const humans = Array.isArray(r.humans) ? (r.humans as unknown[]).filter((n): n is number => typeof n === 'number') : [0];
  if (!humans.length || humans.some((h) => !state.players[h])) throw new Error('The save is damaged.');
  return { format: SAVE_FORMAT, savedAt: typeof r.savedAt === 'number' ? r.savedAt : 0, humans, state };
}

export function makeRecord(state: GameState, humans: number[], now = Date.now()): SaveRecord {
  return { format: SAVE_FORMAT, savedAt: now, humans, state };
}

export type WriteResult = { ok: true } | { ok: false; error: string };

export function writeSave(store: Store, id: SaveId, rec: SaveRecord): WriteResult {
  try {
    store.setItem(keyOf(id), JSON.stringify(rec));
    if (id === 'auto') store.removeItem(LEGACY_KEY); // the old single-slot copy is no longer needed
    return { ok: true };
  } catch (e) {
    const full = e instanceof Error && /quota|full|exceed/i.test(`${e.name} ${e.message}`);
    return { ok: false, error: full ? 'Not enough storage space on this device to save the game.' : 'This browser would not let the game save (private browsing, or storage turned off).' };
  }
}

export type ReadResult = { ok: true; record: SaveRecord } | { ok: false; error: string; raw: string | null };

export function readSave(store: Store, id: SaveId): ReadResult {
  let raw: string | null = null;
  try {
    raw = store.getItem(keyOf(id));
    // an older build kept its single save under another key: it becomes the autosave the first time it is read
    if (raw === null && id === 'auto') {
      const old = store.getItem(LEGACY_KEY);
      if (old !== null) raw = old;
    }
  } catch {
    return { ok: false, error: 'This browser would not let the game read its saves.', raw: null };
  }
  if (raw === null) return { ok: false, error: 'Nothing is saved here.', raw: null };
  try {
    return { ok: true, record: migrate(JSON.parse(raw)) };
  } catch (e) {
    return { ok: false, error: e instanceof SyntaxError ? 'The save is damaged.' : (e as Error).message, raw };
  }
}

export function hasAnySave(store: Store): boolean {
  try {
    return SAVE_IDS.some((id) => store.getItem(keyOf(id)) !== null) || store.getItem(LEGACY_KEY) !== null;
  } catch {
    return false;
  }
}

export function removeSave(store: Store, id: SaveId): void {
  try {
    store.removeItem(keyOf(id));
    if (id === 'auto') store.removeItem(LEGACY_KEY);
  } catch { /* ignore */ }
}

function describe(rec: SaveRecord): { label: string; turn: number; players: number } {
  const me = rec.state.players[rec.humans[0]];
  const civ = me ? CIV_BY_ID[me.civ]?.name ?? me.civ : 'Unknown';
  const what = rec.humans.length > 1 ? `${rec.humans.length} players` : civ;
  return { label: `${what} · turn ${rec.state.turn}`, turn: rec.state.turn, players: rec.state.players.length };
}

export function listSaves(store: Store): SaveSummary[] {
  const out: SaveSummary[] = [];
  for (const id of SAVE_IDS) {
    const res = readSave(store, id);
    if (res.ok) out.push({ id, ok: true, savedAt: res.record.savedAt, ...describe(res.record) });
    else if (res.raw !== null) out.push({ id, ok: false, problem: res.error, savedAt: 0, turn: 0, label: 'Cannot be opened', players: 0 });
  }
  return out;
}

/** The save "Continue" should open: the newest one that can be read. */
export function newestReadable(store: Store): SaveSummary | null {
  return listSaves(store).filter((s) => s.ok).sort((a, b) => b.savedAt - a.savedAt)[0] ?? null;
}

/** Text for a downloadable file. A save that cannot be read is exported exactly as it is stored. */
export function exportText(store: Store, id: SaveId): string | null {
  const res = readSave(store, id);
  if (res.ok) return JSON.stringify(res.record);
  return res.raw;
}

export function importText(text: string): SaveRecord {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    throw new Error('The file is not a saved game.');
  }
  return migrate(parsed);
}

export function fileNameFor(id: SaveId, rec: SaveRecord | null, now = new Date()): string {
  const day = now.toISOString().slice(0, 10);
  return `jericho-${id}${rec ? `-turn${rec.state.turn}` : ''}-${day}.json`;
}
