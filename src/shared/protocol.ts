import type { Command, GameEvent } from './game';
import type { GameView } from './view';
import type { AttackPreview } from './combat';
import type { AiLevel, CmdResult } from './types';

export type SlotKind = 'human' | 'ai' | 'open' | 'closed';

export interface RoomSlot {
  slot: number;
  kind: SlotKind;
  name: string;
  civ: string | null;
  connected: boolean;
  host: boolean;
  aiLevel: AiLevel;
}

export interface RoomSettings {
  size: 'small' | 'medium' | 'large';
  speed: 'standard' | 'quick';
  turnLimit: number;
  /** seconds, 0 = off */
  turnTimer: number;
  seed: number | null;
  startAtWar: boolean;
}

export interface RoomInfo {
  code: string;
  phase: 'lobby' | 'playing' | 'over';
  slots: RoomSlot[];
  settings: RoomSettings;
  you: { slot: number; token: string; host: boolean };
}

export type ClientMsg =
  | { t: 'create'; name: string }
  | { t: 'join'; code: string; name: string; token?: string }
  | { t: 'settings'; settings: Partial<RoomSettings> }
  | { t: 'slot'; slot: number; kind: 'ai' | 'open' | 'closed'; aiLevel?: AiLevel }
  | { t: 'civ'; civ: string | null; slot?: number }
  | { t: 'rename'; name: string }
  | { t: 'start' }
  | { t: 'cmd'; id: number; cmd: Command }
  | { t: 'preview'; id: number; unit: number; to: { x: number; y: number } }
  | { t: 'chat'; text: string }
  | { t: 'forceEnd' }
  | { t: 'rematch' }
  | { t: 'takeover'; slot: number }
  | { t: 'leave' }
  | { t: 'ping' };

export type ServerMsg =
  | { t: 'room'; room: RoomInfo }
  | { t: 'view'; view: GameView; events: GameEvent[] }
  | { t: 'result'; id: number; res: CmdResult; preview?: AttackPreview }
  | { t: 'chat'; from: string; slot: number; text: string }
  | { t: 'error'; message: string }
  | { t: 'pong' };

export const DEFAULT_ROOM_SETTINGS: RoomSettings = { size: 'medium', speed: 'standard', turnLimit: 200, turnTimer: 0, seed: null, startAtWar: false };
