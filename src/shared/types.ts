import type { Pos } from './hex';

export type TerrainId = 'ocean' | 'coast' | 'grass' | 'plains' | 'desert' | 'tundra' | 'forest' | 'hills' | 'mountain';
export type ResourceId = 'wheat' | 'cattle' | 'fish' | 'deer' | 'horses' | 'iron' | 'gold' | 'wine' | 'spices' | 'gems' | 'pearls' | 'coal' | 'oil';
export type ImprovementId = 'farm' | 'mine';
export type WorkKind = 'farm' | 'mine' | 'road';
export type UnitTypeId =
  | 'settler' | 'worker' | 'scout' | 'warrior' | 'archer' | 'spearman' | 'horseman' | 'swordsman'
  | 'catapult' | 'crossbowman' | 'pikeman' | 'knight' | 'musketman' | 'cannon'
  | 'trireme' | 'caravel' | 'frigate';
export type BuildingId =
  | 'granary' | 'walls' | 'barracks' | 'library' | 'workshop' | 'market' | 'temple' | 'aqueduct'
  | 'university' | 'bank' | 'observatory' | 'harbor'
  | 'shrine' | 'amphitheater' | 'embassy' | 'cathedral' | 'factory' | 'hospital' | 'laboratory'
  | 'spring_gardens' | 'ziggurat' | 'sun_temple' | 'great_bath' | 'forum' | 'satrapy' | 'stepped_pyramid' | 'agora'
  | 'pyramids' | 'great_library' | 'hanging_gardens' | 'oracle' | 'colossus' | 'great_wall' | 'lighthouse' | 'notre_dame' | 'crystal_palace';
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
  /** tribal village waiting to be explored */
  hut: boolean;
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
  xp: number;
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
  /** id of the ship carrying this unit, if it is aboard one */
  carriedBy?: number | null;
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
  /** tiles the player pinned for citizens to work */
  locked: number[];
  /** a governor chooses what this city builds whenever its queue runs empty */
  governor: boolean;
  lastAttacked: number;
  /** has this city already bombarded this turn */
  shot: boolean;
}

export type AiPersonality = 'builder' | 'warlord' | 'balanced';
export type AiLevel = 'easy' | 'normal' | 'hard';

export interface PendingEvent {
  id: string;
  /** one short line */
  text: string;
  /** the city it concerns, or -1 */
  city: number;
  at?: Pos;
  turn: number;
  options: Array<{ label: string; art: string; hint: string }>;
}

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
  /** adopted policy ids */
  policies: string[];
  /** policies on offer right now (empty when none is pending) */
  policyChoices: string[];
  /** policies passed over in an earlier offer: lost for the rest of the game */
  forgone: string[];
  endedTurn: boolean;
  /** whether the account behind this slot is connected (online games) */
  connected: boolean;
  /** last computed per-turn values for display */
  income: { gold: number; science: number; culture: number };
  score: number;
  /** last turn the player lost a city / was attacked, used by AI */
  lastDefeat: number;
  eliminatedTurn: number;
  /** tiredness of war: grows with fighting and losses, fades in peace, and lowers contentment */
  weariness: number;
  /** contentment as last computed (see civ.ts) */
  content: number;
  /** last turn this civilisation declared war on someone it was at peace with */
  aggression: number;
  /** the people were restless at the last check (so the player is told once when that changes) */
  restless: boolean;
  /** the turn something last happened to this civilisation (see events.ts) */
  lastEvent: number;
  /** research chooses itself whenever a technology is finished */
  autoResearch: boolean;
  /** a question waiting for an answer (see events.ts); left alone, it answers itself next turn */
  pending: PendingEvent | null;
}

export interface Proposal {
  id: number;
  from: number;
  to: number;
  kind: 'peace' | 'deal';
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
  /** civilisations are at war when they first meet (otherwise at peace) */
  startAtWar: boolean;
  /** how readily the computers start wars: calm, normal or fierce */
  aggression: Aggression;
  /** one big continent, or a separate island for every player */
  mapStyle: 'continent' | 'islands';
  /** random events: off, gentle or normal (see events.ts) */
  events: 'off' | 'gentle' | 'normal';
}

export type Aggression = 'calm' | 'normal' | 'fierce';

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
  /** trade agreements: a < b */
  deals: Array<{ a: number; b: number; since: number }>;
  /** sanctions: `by` refuses to trade with `on` and hurts it */
  sanctions: Array<{ by: number; on: number; since: number }>;
  wonders: Partial<Record<BuildingId, number>>;
  notes: Note[];
  /** the Enlightenment countdown: set when a civilisation learns the final technology */
  enlightenment?: { owner: number; since: number; capital: number; duration: number } | null;
  /** the cultural hegemony countdown: set when one civilisation's culture towers over everyone else's */
  hegemony?: { owner: number; since: number; duration: number } | null;
  /** the domination countdown: set when one civilisation holds most of the world's cities */
  domination?: { owner: number; since: number; duration: number } | null;
  /** civilisations whose academy fell during a countdown and can no longer win by science */
  barred?: number[];
  winner: number | null;
  victory: 'domination' | 'science' | 'culture' | 'score' | 'last-standing' | null;
  over: boolean;
  /** Turn counter of last processTurn (used for UI) */
  startPositions: Pos[];
}

export interface CmdResult {
  ok: boolean;
  error?: string;
}
