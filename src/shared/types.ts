import type { Pos } from './hex';

export type TerrainId = 'ocean' | 'coast' | 'grass' | 'plains' | 'desert' | 'tundra' | 'forest' | 'hills' | 'mountain';
export type ResourceId = 'wheat' | 'cattle' | 'fish' | 'deer' | 'horses' | 'iron' | 'gold';
export type ImprovementId = 'farm' | 'mine';
export type WorkKind = 'farm' | 'mine' | 'road';
export type UnitTypeId =
  | 'settler' | 'worker' | 'scout' | 'warrior' | 'archer' | 'spearman' | 'horseman' | 'swordsman'
  | 'catapult' | 'crossbowman' | 'pikeman' | 'knight' | 'musketman' | 'cannon';
export type BuildingId =
  | 'granary' | 'walls' | 'barracks' | 'library' | 'workshop' | 'market' | 'temple' | 'aqueduct'
  | 'university' | 'bank' | 'observatory'
  | 'pyramids' | 'great_library' | 'hanging_gardens' | 'oracle' | 'colossus';
export type TechId = string;
export type Focus = 'balanced' | 'food' | 'production' | 'gold';
export type Relation = 'unmet' | 'war' | 'peace';

export interface Tile {
  x: number;
  y: number;
  terrain: TerrainId;
  resource: ResourceId | null;
  improvement: ImprovementId | null;
  road: boolean;
  /** player id or -1 */
  owner: number;
  /** id of the city standing here, or -1 */
  city: number;
  /** bitmask of players who have ever seen this tile */
  explored: number;
  /** bitmask of players who can see this tile right now */
  visible: number;
  /** in-progress worker project */
  work: { kind: WorkKind; progress: number } | null;
}

export type UnitOrder =
  | { kind: 'goto'; to: Pos }
  | { kind: 'build'; what: WorkKind }
  | { kind: 'fortify' }
  | { kind: 'sleep' };

export interface Unit {
  id: number;
  owner: number;
  type: UnitTypeId;
  x: number;
  y: number;
  hp: number;
  /** movement points left this turn (2 per full tile of flat ground) */
  mp: number;
  level: number;
  order: UnitOrder | null;
  /** standing automation: workers improve land, military units explore */
  auto: 'work' | 'explore' | null;
  /** moved or fought this turn (no healing) */
  active: boolean;
  /** already used its attack this turn */
  attacked: boolean;
  fortified: boolean;
  /** player pressed "skip" for this turn */
  skipped: boolean;
}

export interface QueueItem {
  kind: 'unit' | 'building';
  id: string;
}

export interface City {
  id: number;
  owner: number;
  name: string;
  x: number;
  y: number;
  pop: number;
  food: number;
  prod: number;
  prodKind: 'unit' | 'building' | null;
  queue: QueueItem[];
  buildings: BuildingId[];
  hp: number;
  focus: Focus;
  capital: boolean;
  founded: number;
  radius: number;
  /** tile indices currently worked */
  worked: number[];
  lastAttacked: number;
  /** has this city already bombarded this turn */
  shot: boolean;
}

export type AiPersonality = 'builder' | 'warlord' | 'balanced';
export type AiLevel = 'easy' | 'normal' | 'hard';

export interface Player {
  id: number;
  name: string;
  civ: string;
  color: number;
  ai: boolean;
  aiLevel: AiLevel;
  personality: AiPersonality;
  alive: boolean;
  gold: number;
  researching: TechId | null;
  research: number;
  techs: TechId[];
  culture: number;
  endedTurn: boolean;
  /** whether the account behind this slot is connected (online games) */
  connected: boolean;
  /** last computed per-turn values for display */
  income: { gold: number; science: number; culture: number };
  score: number;
  /** last turn the player lost a city / was attacked, used by AI */
  lastDefeat: number;
  eliminatedTurn: number;
}

export interface Proposal {
  id: number;
  from: number;
  to: number;
  kind: 'peace';
  turn: number;
}

export interface Note {
  id: number;
  turn: number;
  /** player ids that should see this note, empty = everyone */
  players: number[];
  text: string;
  kind: 'info' | 'war' | 'good' | 'bad' | 'wonder' | 'tech';
  at?: Pos;
}

export interface GameSettings {
  width: number;
  height: number;
  turnLimit: number;
  speed: 'standard' | 'quick';
  seed: number;
  /** Seconds per turn in online games, 0 = no timer */
  turnTimer: number;
  allowScience: boolean;
  allowDomination: boolean;
}

export interface GameState {
  version: number;
  settings: GameSettings;
  rng: number;
  turn: number;
  nextId: number;
  tiles: Tile[];
  players: Player[];
  cities: City[];
  units: Unit[];
  /** relations[a][b] */
  relations: Relation[][];
  /** turn on which the current relation between a and b started */
  relationSince: number[][];
  proposals: Proposal[];
  wonders: Partial<Record<BuildingId, number>>;
  notes: Note[];
  winner: number | null;
  victory: 'domination' | 'science' | 'score' | 'last-standing' | null;
  over: boolean;
  /** Turn counter of last processTurn (used for UI) */
  startPositions: Pos[];
}

export interface CmdResult {
  ok: boolean;
  error?: string;
}
