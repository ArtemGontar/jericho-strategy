import { rangeOffsets, hexDistance, neighborsOf, tilesWithin, type Pos } from './hex';
import { Rng } from './rng';
import { generateMap } from './mapgen';
import {
  BUILDINGS, CITY_BASE_HP, CIVS, CIV_BY_ID, FINAL_TECH, IMPROVEMENTS, MAP_SIZES, MAX_PLAYERS, MIN_CITY_DISTANCE,
  PEACE_MIN_TURNS, POLICIES, POLICY_BY_ID, RESOURCES, TECHS, TECH_BY_ID, TERRAIN, UNITS, UNIT_HP, UNIT_UPGRADES, XP_LEVELS, policyCost,
} from './data';
import {
  assignWorkedTiles, availableTechs, canBuildBuilding, canBuildUnit, cityYield, foodNeeded, hasBuilding, itemCost,
  maxPop, refreshPlayer, techCost,
} from './economy';
import { findPath, stepCost, tileBlockedFor } from './path';
import { resolveAttack, cityStrength, bombardTargets, captureCity } from './combat';
import { rollEvent } from './events';
import { SANCTION_TURNS, dealGold, dealPartners, dealSlots } from './civ';
import { aiTakeTurn, aiAutoExplore, aiAutoWorker, aiChooseResearch, aiPlanCity } from './ai';
import type {
  AiLevel, AiPersonality, BuildingId, City, CmdResult, Focus, GameSettings, GameState, Note, Player,
  QueueItem, Relation, ResourceId, Tile, Unit, UnitTypeId, WorkKind,
} from './types';

export type Command =
  | { t: 'move'; unit: number; to: Pos }
  | { t: 'attack'; unit: number; to: Pos }
  | { t: 'board'; unit: number; ship: number }
  | { t: 'found'; unit: number }
  | { t: 'work'; unit: number; what: WorkKind }
  | { t: 'order'; unit: number; order: 'fortify' | 'sleep' | 'explore' | 'auto' | 'cancel' | 'skip' }
  | { t: 'disband'; unit: number }
  | { t: 'queue'; city: number; op: 'set' | 'add' | 'remove' | 'up'; kind?: 'unit' | 'building'; id?: string; index?: number }
  | { t: 'buy'; city: number }
  | { t: 'focus'; city: number; focus: Focus }
  | { t: 'research'; tech: string }
  | { t: 'peace'; to: number }
  | { t: 'accept'; proposal: number; accept: boolean }
  | { t: 'war'; to: number }
  | { t: 'deal'; to: number }
  | { t: 'cancelDeal'; with: number }
  | { t: 'sanction'; on: number }
  | { t: 'sell'; city: number; building: string }
  | { t: 'governor'; city: number; on: boolean }
  | { t: 'autoResearch'; on: boolean }
  | { t: 'lift'; on: number }
  | { t: 'gift'; to: number; amount: number }
  | { t: 'policy'; id: string }
  | { t: 'tile'; city: number; x: number; y: number }
  | { t: 'upgrade'; unit: number }
  | { t: 'end' }
  | { t: 'unend' };

export type GameEvent =
  | { kind: 'move'; unit: number; owner: number; type: UnitTypeId; from: Pos; path: Pos[]; mask: number }
  | { kind: 'attack'; attacker: number; owner: number; type: UnitTypeId; from: Pos; to: Pos; ranged: boolean; dmgDef: number; dmgAtt: number; defOwner: number; defType: UnitTypeId | 'city'; killedDef: boolean; killedAtt: boolean; mask: number }
  | { kind: 'found'; city: number; owner: number; at: Pos; mask: number }
  | { kind: 'capture'; city: number; by: number; from: number; at: Pos; mask: number }
  | { kind: 'death'; type: UnitTypeId; owner: number; at: Pos; mask: number }
  | { kind: 'turn'; turn: number };

export interface PlayerSlot {
  name: string;
  civ?: string;
  ai: boolean;
  aiLevel?: AiLevel;
  personality?: AiPersonality;
}

export interface NewGameOptions {
  seed?: number;
  size?: keyof typeof MAP_SIZES;
  speed?: 'standard' | 'quick';
  turnLimit?: number;
  turnTimer?: number;
  players: PlayerSlot[];
  allowScience?: boolean;
  allowDomination?: boolean;
  events?: 'off' | 'gentle' | 'normal';
  /** found each human's capital where their settler stands, so the first turn is about what to build, not where to click */
  autoFound?: boolean;
  startAtWar?: boolean;
  aggression?: 'calm' | 'normal' | 'fierce';
  mapStyle?: 'continent' | 'islands';
}

export const STATE_VERSION = 1;

/** What each kind of computer player reaches for first when offered a policy (earlier is better). */
const AI_POLICY_PREF: Record<string, string[]> = {
  warlord: ['drill', 'veterans', 'fortification', 'conscription', 'craftsmen', 'pioneers', 'agrarian', 'merchants', 'exploration', 'hearth'],
  builder: ['patronage', 'scholarship', 'monuments', 'scribes', 'agrarian', 'coinage', 'merchants', 'hearth', 'bureaucracy', 'trade_routes', 'pioneers', 'seafaring', 'exploration'],
  balanced: ['craftsmen', 'agrarian', 'scholarship', 'patronage', 'pioneers', 'coinage', 'merchants', 'scribes', 'hearth', 'trade_routes', 'bureaucracy', 'seafaring', 'exploration'],
};

export class Game {
  s: GameState;
  rng: Rng;
  events: GameEvent[] = [];
  /** >0 while automation (AI helpers) is issuing orders, so it does not cancel the unit's own auto mode */
  autoDepth = 0;
  private unitIdx = new Map<number, Unit[]>();
  private unitById = new Map<number, Unit>();
  private cityById = new Map<number, City>();

  constructor(state: GameState) {
    this.s = state;
    this.rng = new Rng(state.rng);
    this.normalise();
    this.reindex();
  }

  // ------------------------------------------------------------------ construction
  static create(opts: NewGameOptions): Game {
    const n = Math.min(MAX_PLAYERS, Math.max(2, opts.players.length));
    const seed = (opts.seed ?? Math.floor(Math.random() * 2 ** 31)) >>> 0;
    const order = ['small', 'medium', 'large'] as const;
    const minimum = n <= 3 ? 0 : n <= 5 ? 1 : 2;
    const wanted = opts.size ? order.indexOf(opts.size) : minimum;
    const size = MAP_SIZES[order[Math.max(minimum, wanted)]];
    const speed = opts.speed ?? 'standard';
    const settings: GameSettings = {
      width: size.width,
      height: size.height,
      turnLimit: opts.turnLimit ?? (speed === 'quick' ? 120 : 200),
      speed,
      seed,
      turnTimer: opts.turnTimer ?? 0,
      allowScience: opts.allowScience ?? true,
      allowDomination: opts.allowDomination ?? true,
      events: opts.events ?? 'normal',
      startAtWar: opts.startAtWar ?? false,
      aggression: opts.aggression ?? 'normal',
      mapStyle: opts.mapStyle ?? 'continent',
    };
    const map = generateMap(size.width, size.height, n, seed, opts.mapStyle ?? 'continent');
    const rng = new Rng(seed ^ 0xa5a5a5a5);
    // assign civs: respect requests, fill the rest randomly without duplicates
    const taken = new Set<string>();
    for (const slot of opts.players.slice(0, n)) if (slot.civ && CIV_BY_ID[slot.civ]) taken.add(slot.civ);
    const free = rng.shuffle(CIVS.filter((c) => !taken.has(c.id)).map((c) => c.id));
    const players: Player[] = opts.players.slice(0, n).map((slot, i) => {
      let civ = slot.civ && CIV_BY_ID[slot.civ] ? slot.civ : free.pop()!;
      if (!civ) civ = CIVS[i % CIVS.length].id;
      const personalities: AiPersonality[] = ['builder', 'warlord', 'balanced'];
      return {
        id: i,
        name: slot.name,
        civ,
        color: i,
        ai: slot.ai,
        aiLevel: slot.aiLevel ?? 'normal',
        personality: slot.personality ?? rng.pick(personalities),
        alive: true,
        gold: 0,
        researching: null,
        research: 0,
        techs: [],
        culture: 0,
        policies: [],
        policyChoices: [],
        forgone: [],
        endedTurn: slot.ai,
        connected: !slot.ai,
        income: { gold: 0, science: 0, culture: 0 },
        score: 0,
        lastDefeat: -99,
        eliminatedTurn: -1,
        weariness: 0,
        content: 8,
        aggression: -99,
        restless: false,
        lastEvent: -99,
        autoResearch: false,
      };
    });
    const relations: Relation[][] = players.map((a) => players.map((b) => (a.id === b.id ? 'peace' : 'unmet')));
    const relationSince = players.map(() => players.map(() => 0));
    // randomise which start belongs to whom
    const starts = rng.shuffle(map.starts.slice());
    const state: GameState = {
      version: STATE_VERSION,
      settings,
      rng: rng.state,
      turn: 1,
      nextId: 1,
      tiles: map.tiles,
      players,
      cities: [],
      units: [],
      relations,
      relationSince,
      proposals: [],
      deals: [],
      sanctions: [],
      wonders: {},
      notes: [],
      winner: null,
      victory: null,
      over: false,
      startPositions: starts,
    };
    const game = new Game(state);
    players.forEach((p, i) => {
      const s = starts[i];
      game.addUnit(p.id, 'settler', s.x, s.y);
      game.addUnit(p.id, 'warrior', s.x, s.y);
      const spot = game.nb(s.x, s.y).find((q) => TERRAIN[game.tile(q.x, q.y).terrain].passable) ?? s;
      game.addUnit(p.id, 'worker', spot.x, spot.y);
      game.addUnit(p.id, 'scout', spot.x, spot.y);
    });
    game.updateVisibility();
    if (opts.autoFound) {
      // the start positions are chosen as good city sites, so this is never a bad capital
      for (const p of game.players) {
        const settler = !p.ai ? game.units.find((u) => u.owner === p.id && u.type === 'settler') : undefined;
        if (settler) game.exec(p.id, { t: 'found', unit: settler.id });
      }
      game.note([], 'The age of cities begins. Your capital is founded: choose what it builds, then explore!', 'info');
    } else game.note([], 'The age of cities begins. Found your capital and explore!', 'info');
    return game;
  }

  static fromJSON(json: string | GameState): Game {
    const s = typeof json === 'string' ? (JSON.parse(json) as GameState) : json;
    return new Game(s);
  }

  toJSON(): GameState {
    this.s.rng = this.rng.state;
    return this.s;
  }

  serialize(): string {
    return JSON.stringify(this.toJSON());
  }

  /** Fill in fields that older saves (or rooms persisted by an older server) do not have. */
  private normalise(): void {
    if (!this.s.settings.aggression) this.s.settings.aggression = 'normal';
    if (!this.s.settings.mapStyle) this.s.settings.mapStyle = 'continent';
    if (!this.s.settings.events) this.s.settings.events = 'normal';
    for (const t of this.s.tiles) if (t.hut === undefined) t.hut = false;
    for (const u of this.s.units) if (u.xp === undefined) u.xp = 0;
    for (const c of this.s.cities) { if (!c.locked) c.locked = []; if (c.governor === undefined) c.governor = false; }
    if (!this.s.deals) this.s.deals = [];
    if (!this.s.sanctions) this.s.sanctions = [];
    for (const p of this.s.players) {
      if (!p.policies) p.policies = [];
      if (!p.policyChoices) p.policyChoices = [];
      if (!p.forgone) p.forgone = [];
      if (p.weariness === undefined) p.weariness = 0;
      if (p.content === undefined) p.content = 8;
      if (p.aggression === undefined) p.aggression = -99;
      if (p.restless === undefined) p.restless = false;
      if (p.lastEvent === undefined) p.lastEvent = -99;
      if (p.autoResearch === undefined) p.autoResearch = false;
    }
  }

  private reindex(): void {
    this.unitIdx.clear();
    this.unitById.clear();
    this.cityById.clear();
    for (const u of this.s.units) {
      this.unitById.set(u.id, u);
      const k = u.y * this.width + u.x;
      const list = this.unitIdx.get(k);
      if (list) list.push(u);
      else this.unitIdx.set(k, [u]);
    }
    for (const c of this.s.cities) this.cityById.set(c.id, c);
  }

  // ------------------------------------------------------------------ accessors
  get width(): number { return this.s.settings.width; }
  get height(): number { return this.s.settings.height; }
  get turn(): number { return this.s.turn; }
  get tiles(): Tile[] { return this.s.tiles; }
  get players(): Player[] { return this.s.players; }
  get cities(): City[] { return this.s.cities; }
  get units(): Unit[] { return this.s.units; }
  get speedMult(): number { return this.s.settings.speed === 'quick' ? 0.67 : 1; }

  /** Culture a civilisation needs before it can claim cultural hegemony. */
  get cultureGoal(): number { return Math.round(6000 * this.speedMult); }

  tile(x: number, y: number): Tile { return this.s.tiles[y * this.s.settings.width + x]; }
  inBounds(x: number, y: number): boolean { return x >= 0 && y >= 0 && x < this.width && y < this.height; }
  dist(ax: number, ay: number, bx: number, by: number): number { return hexDistance(ax, ay, bx, by); }
  nb(x: number, y: number): Pos[] { return neighborsOf(x, y, this.width, this.height); }
  unit(id: number): Unit | undefined { return this.unitById.get(id); }
  city(id: number): City | undefined { return this.cityById.get(id); }
  cityAt(x: number, y: number): City | undefined {
    const c = this.tile(x, y).city;
    return c >= 0 ? this.cityById.get(c) : undefined;
  }
  unitsAt(x: number, y: number): Unit[] { return this.unitIdx.get(y * this.width + x) ?? EMPTY; }
  relation(a: number, b: number): Relation { return a === b ? 'peace' : this.s.relations[a][b]; }
  atWar(a: number, b: number): boolean { return a !== b && this.s.relations[a][b] === 'war'; }

  hostileUnitOn(playerId: number, x: number, y: number): boolean {
    for (const u of this.unitsAt(x, y)) if (u.owner !== playerId && this.atWar(playerId, u.owner) && !UNITS[u.type].civilian) return true;
    return false;
  }

  private landIds: Int32Array | null = null;
  /** Which walkable landmass a tile belongs to (-1 for water and mountains); computed once, terrain never changes. */
  landmass(x: number, y: number): number {
    if (!this.landIds) {
      const w = this.width;
      const ids = new Int32Array(w * this.height).fill(-1);
      let next = 0;
      for (let i = 0; i < ids.length; i++) {
        if (ids[i] !== -1 || !TERRAIN[this.s.tiles[i].terrain].passable) continue;
        const stack = [i];
        ids[i] = next;
        while (stack.length) {
          const cur = stack.pop()!;
          for (const n of this.nb(cur % w, Math.floor(cur / w))) {
            const ni = n.y * w + n.x;
            if (ids[ni] === -1 && TERRAIN[this.s.tiles[ni].terrain].passable) {
              ids[ni] = next;
              stack.push(ni);
            }
          }
        }
        next++;
      }
      this.landIds = ids;
    }
    return this.landIds[y * this.width + x];
  }

  isCoastal(x: number, y: number): boolean {
    for (const n of this.nb(x, y)) {
      const t = this.tile(n.x, n.y).terrain;
      if (t === 'ocean' || t === 'coast') return true;
    }
    return false;
  }

  hasResource(playerId: number, res: ResourceId): boolean {
    for (const t of this.s.tiles) if (t.owner === playerId && t.resource === res) return true;
    // a trade agreement shares the horses and iron of the partner
    if (RESOURCES[res].strategic) {
      for (const o of dealPartners(this, playerId)) for (const t of this.s.tiles) if (t.owner === o && t.resource === res) return true;
    }
    return false;
  }

  /** The era a technology belongs to. */
  techEra(id: string): number {
    return TECH_BY_ID[id]?.era ?? 0;
  }
  isCivilian(u: Unit): boolean {
    return !!UNITS[u.type].civilian;
  }

  hasDeal(a: number, b: number): boolean {
    return this.s.deals.some((d) => (d.a === a && d.b === b) || (d.a === b && d.b === a));
  }
  isSanctioned(by: number, on: number): boolean {
    return this.s.sanctions.some((x) => x.by === by && x.on === on);
  }
  addWeariness(pid: number, amount: number): void {
    const p = this.s.players[pid];
    if (p) p.weariness = Math.max(0, Math.min(12, p.weariness + amount));
  }

  playerCities(pid: number): City[] { return this.s.cities.filter((c) => c.owner === pid); }
  playerUnits(pid: number): Unit[] { return this.s.units.filter((u) => u.owner === pid); }
  humans(): Player[] { return this.s.players.filter((p) => !p.ai && p.alive); }

  note(players: number[], text: string, kind: Note['kind'] = 'info', at?: Pos): void {
    this.s.notes.push({ id: this.s.nextId++, turn: this.s.turn, players, text, kind, at });
    if (this.s.notes.length > 300) this.s.notes.splice(0, this.s.notes.length - 300);
  }

  playerName(id: number): string {
    return this.s.players[id]?.name ?? '?';
  }

  // ------------------------------------------------------------------ units
  addUnit(owner: number, type: UnitTypeId, x: number, y: number, level = 0): Unit {
    const def = UNITS[type];
    const u: Unit = {
      id: this.s.nextId++, owner, type, x, y, hp: UNIT_HP, mp: def.moves * 2, level, xp: 0, order: null, auto: null,
      active: false, attacked: false, fortified: false, skipped: false,
    };
    this.s.units.push(u);
    this.unitById.set(u.id, u);
    this.indexAdd(u);
    return u;
  }

  private indexAdd(u: Unit): void {
    const k = u.y * this.width + u.x;
    const list = this.unitIdx.get(k);
    if (list) list.push(u);
    else this.unitIdx.set(k, [u]);
  }
  private indexRemove(u: Unit): void {
    const k = u.y * this.width + u.x;
    const list = this.unitIdx.get(k);
    if (!list) return;
    const i = list.indexOf(u);
    if (i >= 0) list.splice(i, 1);
    if (list.length === 0) this.unitIdx.delete(k);
  }

  placeUnit(u: Unit, x: number, y: number): void {
    // a ship takes its passengers along
    const aboard = UNITS[u.type].naval ? this.cargoOf(u) : [];
    this.indexRemove(u);
    u.x = x;
    u.y = y;
    this.indexAdd(u);
    for (const c of aboard) {
      this.indexRemove(c);
      c.x = x;
      c.y = y;
      this.indexAdd(c);
    }
  }

  /** Units riding on this ship. */
  cargoOf(ship: Unit): Unit[] {
    return this.unitsAt(ship.x, ship.y).filter((c) => c.carriedBy === ship.id);
  }

  removeUnit(u: Unit): void {
    // a ship that goes down takes everyone aboard with it
    if (UNITS[u.type].naval) {
      for (const c of this.cargoOf(u).slice()) {
        this.events.push({ kind: 'death', type: c.type, owner: c.owner, at: { x: c.x, y: c.y }, mask: this.tile(c.x, c.y).visible | (1 << c.owner) });
        this.removeUnit(c);
      }
    }
    this.indexRemove(u);
    this.unitById.delete(u.id);
    const i = this.s.units.indexOf(u);
    if (i >= 0) this.s.units.splice(i, 1);
  }

  /** Mask of players who can currently see tile (x,y). */
  visMask(x: number, y: number): number { return this.tile(x, y).visible; }

  // ------------------------------------------------------------------ visibility & contact
  updateVisibility(): void {
    const w = this.width;
    const h = this.height;
    const tiles = this.s.tiles;
    for (const t of tiles) t.visible = 0;
    const reveal = (x: number, y: number, r: number, bit: number) => {
      const off = rangeOffsets(y, r);
      for (let i = 0; i < off.length; i += 2) {
        const nx = x + off[i];
        const ny = y + off[i + 1];
        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
        const t = tiles[ny * w + nx];
        t.visible |= bit;
        t.explored |= bit;
      }
    };
    for (const u of this.s.units) {
      const r = UNITS[u.type].vision + (this.s.players[u.owner].policies.includes('exploration') ? 1 : 0);
      reveal(u.x, u.y, r, 1 << u.owner);
    }
    for (const c of this.s.cities) reveal(c.x, c.y, c.radius + 1, 1 << c.owner);
    // first contact
    const check = (owner: number, x: number, y: number) => {
      const mask = this.tile(x, y).visible & ~(1 << owner);
      if (!mask) return;
      for (let p = 0; p < this.s.players.length; p++) {
        if (!(mask & (1 << p)) || !this.s.players[p].alive) continue;
        if (this.s.relations[p][owner] === 'unmet') this.meet(p, owner);
      }
    };
    for (const u of this.s.units) check(u.owner, u.x, u.y);
    for (const c of this.s.cities) check(c.owner, c.x, c.y);
  }

  private meet(a: number, b: number): void {
    this.setRelation(a, b, this.s.settings.startAtWar ? 'war' : 'peace');
    this.note([a], `You have met ${CIV_BY_ID[this.players[b].civ].adjective} civilisation (${this.playerName(b)}).`, 'info');
    this.note([b], `You have met ${CIV_BY_ID[this.players[a].civ].adjective} civilisation (${this.playerName(a)}).`, 'info');
  }

  setRelation(a: number, b: number, r: Relation): void {
    this.s.relations[a][b] = r;
    this.s.relations[b][a] = r;
    this.s.relationSince[a][b] = this.s.turn;
    this.s.relationSince[b][a] = this.s.turn;
  }

  // ------------------------------------------------------------------ cities
  nextCityName(owner: number): string {
    const civ = CIV_BY_ID[this.players[owner].civ];
    const used = new Set(this.s.cities.map((c) => c.name));
    for (const n of civ.cities) if (!used.has(n)) return n;
    let i = 2;
    while (used.has(`${civ.cities[0]} ${i}`)) i++;
    return `${civ.cities[0]} ${i}`;
  }

  claimTiles(city: City, radius: number): void {
    for (const p of tilesWithin(city.x, city.y, radius, this.width, this.height)) {
      const t = this.tile(p.x, p.y);
      if (t.owner === -1 && TERRAIN[t.terrain].workable) t.owner = city.owner;
      else if (t.owner === -1 && t.terrain === 'mountain') t.owner = city.owner;
    }
  }

  foundCity(owner: number, x: number, y: number): City {
    const t = this.tile(x, y);
    const capital = !this.s.cities.some((c) => c.owner === owner);
    const city: City = {
      id: this.s.nextId++, owner, name: this.nextCityName(owner), x, y, pop: 1, food: 0, prod: 0, prodKind: null,
      queue: [], buildings: [], hp: CITY_BASE_HP, focus: 'balanced', capital, founded: this.s.turn, radius: 2,
      worked: [], locked: [], governor: false, lastAttacked: -99, shot: false,
    };
    this.s.cities.push(city);
    this.cityById.set(city.id, city);
    t.city = city.id;
    t.owner = owner;
    this.claimTiles(city, 2);
    // fresh city centres are improved by the settlers
    if (t.terrain === 'forest') t.terrain = 'plains';
    return city;
  }

  cityMaxHp(city: City): number {
    return CITY_BASE_HP + (hasBuilding(city, 'walls') ? 100 : 0) + (this.s.players[city.owner].policies.includes('fortification') ? 60 : 0);
  }

  // ------------------------------------------------------------------ command dispatch
  exec(pid: number, cmd: Command): CmdResult {
    const p = this.s.players[pid];
    if (!p || !p.alive) return { ok: false, error: 'You are out of the game.' };
    if (this.s.over) return { ok: false, error: 'The game is over.' };
    let res: CmdResult;
    switch (cmd.t) {
      case 'move': res = this.cmdMove(p, cmd.unit, cmd.to); break;
      case 'attack': res = this.cmdAttack(p, cmd.unit, cmd.to); break;
      case 'board': res = this.cmdBoard(p, cmd.unit, cmd.ship); break;
      case 'found': res = this.cmdFound(p, cmd.unit); break;
      case 'work': res = this.cmdWork(p, cmd.unit, cmd.what); break;
      case 'order': res = this.cmdOrder(p, cmd.unit, cmd.order); break;
      case 'disband': res = this.cmdDisband(p, cmd.unit); break;
      case 'queue': res = this.cmdQueue(p, cmd); break;
      case 'buy': res = this.cmdBuy(p, cmd.city); break;
      case 'focus': res = this.cmdFocus(p, cmd.city, cmd.focus); break;
      case 'research': res = this.cmdResearch(p, cmd.tech); break;
      case 'peace': res = this.cmdPeace(p, cmd.to); break;
      case 'accept': res = this.cmdAccept(p, cmd.proposal, cmd.accept); break;
      case 'war': res = this.cmdWar(p, cmd.to); break;
      case 'deal': res = this.cmdDeal(p, cmd.to); break;
      case 'cancelDeal': res = this.cmdCancelDeal(p, cmd.with); break;
      case 'sanction': res = this.cmdSanction(p, cmd.on); break;
      case 'sell': res = this.cmdSell(p, cmd.city, cmd.building); break;
      case 'governor': res = this.cmdGovernor(p, cmd.city, cmd.on); break;
      case 'autoResearch': p.autoResearch = !!cmd.on; if (p.autoResearch && !p.researching && availableTechs(p).length) p.researching = aiChooseResearch(this, p); res = { ok: true }; break;
      case 'lift': res = this.cmdLift(p, cmd.on); break;
      case 'gift': res = this.cmdGift(p, cmd.to, cmd.amount); break;
      case 'policy': res = this.cmdPolicy(p, cmd.id); break;
      case 'tile': res = this.cmdTile(p, cmd.city, cmd.x, cmd.y); break;
      case 'upgrade': res = this.cmdUpgrade(p, cmd.unit); break;
      case 'end': res = this.cmdEnd(p); return res;
      case 'unend': p.endedTurn = false; return { ok: true };
      default: return { ok: false, error: 'Unknown command.' };
    }
    if (res.ok) {
      this.updateVisibility();
      refreshPlayer(this, pid);
      if (this.checkEliminations()) this.checkVictory();
    }
    return res;
  }

  private ownUnit(p: Player, id: number): Unit | string {
    const u = this.unit(id);
    if (!u) return 'That unit no longer exists.';
    if (u.owner !== p.id) return 'That is not your unit.';
    return u;
  }

  private cmdEnd(p: Player): CmdResult {
    if (p.endedTurn) return { ok: true };
    p.endedTurn = true;
    this.maybeProcessTurn();
    return { ok: true };
  }

  /** When every living human has ended their turn, resolve it. */
  maybeProcessTurn(): boolean {
    if (this.s.over) return false;
    const humans = this.s.players.filter((x) => !x.ai && x.alive);
    if (humans.length > 0 && humans.some((h) => !h.endedTurn)) return false;
    this.processTurn();
    return true;
  }

  // ------------------------------------------------------------------ unit commands
  private cmdMove(p: Player, id: number, to: Pos): CmdResult {
    const u = this.ownUnit(p, id);
    if (typeof u === 'string') return { ok: false, error: u };
    if (!this.inBounds(to.x, to.y)) return { ok: false, error: 'Out of bounds.' };
    if (u.x === to.x && u.y === to.y) return { ok: true };
    // walking onto a friendly ship next door means climbing aboard
    if (!UNITS[u.type].naval && u.carriedBy == null && this.dist(u.x, u.y, to.x, to.y) === 1 && !TERRAIN[this.tile(to.x, to.y).terrain].land) {
      const ship = this.unitsAt(to.x, to.y).find((s) => s.owner === p.id && UNITS[s.type].naval);
      if (ship) return this.cmdBoard(p, id, ship.id);
    }
    const tt = this.tile(to.x, to.y);
    if (!(tt.explored & (1 << p.id))) {
      // allow exploring toward unknown tiles only if adjacent
      if (this.dist(u.x, u.y, to.x, to.y) !== 1) return { ok: false, error: 'You have not explored that area.' };
    }
    const path = findPath(this, u, to);
    if (!path || path.length === 0) {
      // maybe an enemy stands there: try attacking if adjacent
      if (this.dist(u.x, u.y, to.x, to.y) === 1 && this.enemyAt(u, to.x, to.y)) return this.cmdAttack(p, id, to);
      return { ok: false, error: 'No route to that tile.' };
    }
    if (!this.autoDepth) u.auto = null;
    u.order = { kind: 'goto', to: { x: to.x, y: to.y } };
    u.fortified = false;
    u.skipped = false;
    this.followOrder(u);
    return { ok: true };
  }

  private cmdBoard(p: Player, id: number, shipId: number): CmdResult {
    const u = this.ownUnit(p, id);
    if (typeof u === 'string') return { ok: false, error: u };
    const ship = this.unit(shipId);
    if (!ship || ship.owner !== p.id || !UNITS[ship.type].naval) return { ok: false, error: 'That is not one of your ships.' };
    const def = UNITS[u.type];
    if (def.naval) return { ok: false, error: 'Ships cannot board ships.' };
    if (u.carriedBy != null) return { ok: false, error: 'Already aboard a ship.' };
    if (this.dist(u.x, u.y, ship.x, ship.y) > 1) return { ok: false, error: 'The ship is too far away.' };
    if (u.mp <= 0) return { ok: false, error: 'No movement left.' };
    if ((UNITS[ship.type].capacity ?? 0) <= 0) return { ok: false, error: 'That ship cannot carry units.' };
    if (this.cargoOf(ship).length >= (UNITS[ship.type].capacity ?? 0)) return { ok: false, error: 'The ship is full.' };
    const from = { x: u.x, y: u.y };
    u.carriedBy = ship.id;
    u.order = null;
    u.auto = null;
    u.fortified = false;
    u.skipped = false;
    u.mp = 0;
    u.active = true;
    this.placeUnit(u, ship.x, ship.y);
    this.events.push({ kind: 'move', unit: u.id, owner: u.owner, type: u.type, from, path: [{ x: ship.x, y: ship.y }], mask: this.trailMask([from, { x: ship.x, y: ship.y }], u) });
    return { ok: true };
  }

  enemyAt(u: Unit, x: number, y: number): boolean {
    const c = this.cityAt(x, y);
    if (c && c.owner !== u.owner && this.atWar(u.owner, c.owner)) return true;
    return this.unitsAt(x, y).some((o) => o.owner !== u.owner && this.atWar(u.owner, o.owner));
  }

  /** Take as many steps along the unit's goto order as movement allows. */
  followOrder(u: Unit): void {
    if (!u.order || u.order.kind !== 'goto') return;
    const target = u.order.to;
    const trail: Pos[] = [];
    const origin = { x: u.x, y: u.y };
    let replans = 0;
    const avoid = new Set<number>();
    while (u.mp > 0 && u.order && u.order.kind === 'goto') {
      if (u.x === target.x && u.y === target.y) {
        u.order = null;
        break;
      }
      const path = findPath(this, u, target, { avoid });
      if (!path || path.length === 0) {
        if (replans++ > 1) { u.order = null; break; }
        u.order = null;
        break;
      }
      const step = path[0];
      if (this.dist(u.x, u.y, step.x, step.y) !== 1) { u.order = null; break; }
      const isDest = step.x === target.x && step.y === target.y;
      if (!this.stepUnit(u, step.x, step.y, isDest)) {
        if (!isDest && u.mp > 0) {
          // a friendly unit holds the next tile and this step would use up our movement, so we could not stop there.
          // Part-way through a turn: wait, a fresh turn's movement lets us pass through it. From the start of a
          // turn (even full movement is not enough): go around it.
          if (u.mp < UNITS[u.type].moves * 2) break;
          if (avoid.size < 4) {
            avoid.add(step.y * this.width + step.x);
            continue;
          }
        }
        u.order = null;
        break;
      }
      trail.push(step);
    }
    if (u.order && u.order.kind === 'goto' && u.x === target.x && u.y === target.y) u.order = null;
    if (trail.length) {
      this.events.push({ kind: 'move', unit: u.id, owner: u.owner, type: u.type, from: origin, path: trail, mask: this.trailMask([origin, ...trail], u) });
    }
  }

  private trailMask(trail: Pos[], u: Unit): number {
    let mask = 1 << u.owner;
    for (const p of trail) mask |= this.tile(p.x, p.y).visible;
    return mask;
  }

  /** One step onto an adjacent tile. Returns false if the step is not possible. */
  stepUnit(u: Unit, x: number, y: number, isDest = true): boolean {
    if (u.mp <= 0) return false;
    const cost = stepCost(this, u, u.x, u.y, x, y);
    if (!isFinite(cost)) return false;
    // passing through a tile held by a friendly unit of the same class is fine as long as we keep moving
    const passThrough = !isDest && u.mp - cost > 0;
    if (tileBlockedFor(this, u, x, y, !passThrough)) return false;
    const landing = u.carriedBy != null;
    this.placeUnit(u, x, y);
    u.mp = landing ? 0 : Math.max(0, u.mp - cost);
    if (landing) u.carriedBy = null;
    u.active = true;
    u.fortified = false;
    // capturing lone civilians happens when a military unit walks in (handled by combat), so nothing here
    this.updateVisibility();
    if (this.tile(x, y).hut) this.exploreHut(u);
    return true;
  }

  private cmdAttack(p: Player, id: number, to: Pos): CmdResult {
    const u = this.ownUnit(p, id);
    if (typeof u === 'string') return { ok: false, error: u };
    if (!this.inBounds(to.x, to.y)) return { ok: false, error: 'Out of bounds.' };
    const res = resolveAttack(this, u, to.x, to.y);
    if (res.ok) u.order = null;
    return res;
  }

  private cmdFound(p: Player, id: number): CmdResult {
    const u = this.ownUnit(p, id);
    if (typeof u === 'string') return { ok: false, error: u };
    if (u.type !== 'settler') return { ok: false, error: 'Only settlers can found cities.' };
    if (u.mp <= 0) return { ok: false, error: 'The settler has no movement left this turn.' };
    const t = this.tile(u.x, u.y);
    if (!TERRAIN[t.terrain].land || t.terrain === 'mountain') return { ok: false, error: 'Cannot found a city here.' };
    if (t.owner >= 0 && t.owner !== p.id) return { ok: false, error: 'This land belongs to another civilisation.' };
    for (const c of this.s.cities)
      if (this.dist(c.x, c.y, u.x, u.y) < MIN_CITY_DISTANCE) return { ok: false, error: `Cities must be at least ${MIN_CITY_DISTANCE} tiles apart.` };
    const city = this.foundCity(p.id, u.x, u.y);
    this.removeUnit(u);
    this.updateVisibility();
    this.events.push({ kind: 'found', city: city.id, owner: p.id, at: { x: city.x, y: city.y }, mask: this.tile(city.x, city.y).visible });
    this.note([], `${p.name} founded ${city.name}.`, 'info', { x: city.x, y: city.y });
    return { ok: true };
  }

  workRate(playerId: number): number {
    const p = this.players[playerId];
    return (CIV_BY_ID[p.civ].id === 'persia' ? 1.5 : 1) * (p.policies.includes('pioneers') ? 1.25 : 1);
  }

  canWork(p: Player, u: Unit, what: WorkKind): string | null {
    if (u.type !== 'worker') return 'Only workers can build improvements.';
    const t = this.tile(u.x, u.y);
    const def = IMPROVEMENTS[what];
    if (def.tech && !p.techs.includes(def.tech)) return `Requires ${TECHS.find((x) => x.id === def.tech)!.name}.`;
    if (t.owner >= 0 && t.owner !== p.id) return 'You cannot build in foreign territory.';
    if (t.city >= 0) return 'Not inside a city.';
    if (!def.terrains.includes(t.terrain)) return `A ${def.name.toLowerCase()} cannot be built on ${TERRAIN[t.terrain].name.toLowerCase()}.`;
    if (what === 'road' && t.road) return 'There is already a road here.';
    if ((what === 'farm' || what === 'mine') && t.improvement === what) return `There is already a ${def.name.toLowerCase()} here.`;
    return null;
  }

  private cmdWork(p: Player, id: number, what: WorkKind): CmdResult {
    const u = this.ownUnit(p, id);
    if (typeof u === 'string') return { ok: false, error: u };
    const err = this.canWork(p, u, what);
    if (err) return { ok: false, error: err };
    const t = this.tile(u.x, u.y);
    if (!t.work || t.work.kind !== what) t.work = { kind: what, progress: 0 };
    if (!this.autoDepth) u.auto = null;
    u.order = { kind: 'build', what };
    u.fortified = false;
    return { ok: true };
  }

  private cmdOrder(p: Player, id: number, order: 'fortify' | 'sleep' | 'explore' | 'auto' | 'cancel' | 'skip'): CmdResult {
    const u = this.ownUnit(p, id);
    if (typeof u === 'string') return { ok: false, error: u };
    const def = UNITS[u.type];
    switch (order) {
      case 'fortify':
        if (def.civilian) return { ok: false, error: 'Civilians cannot fortify.' };
        u.auto = null;
        u.order = { kind: 'fortify' };
        u.fortified = true;
        break;
      case 'sleep':
        u.auto = null;
        u.order = { kind: 'sleep' };
        u.fortified = !def.civilian;
        break;
      case 'explore':
        if (def.civilian) return { ok: false, error: 'Only military units can explore.' };
        u.order = null;
        u.auto = 'explore';
        aiAutoExplore(this, u);
        break;
      case 'auto':
        if (u.type !== 'worker') return { ok: false, error: 'Only workers can work automatically.' };
        u.order = null;
        u.auto = 'work';
        aiAutoWorker(this, u);
        break;
      case 'cancel':
        u.order = null;
        u.auto = null;
        u.fortified = false;
        u.skipped = false;
        break;
      case 'skip':
        u.skipped = true;
        break;
    }
    return { ok: true };
  }

  private cmdDisband(p: Player, id: number): CmdResult {
    const u = this.ownUnit(p, id);
    if (typeof u === 'string') return { ok: false, error: u };
    this.removeUnit(u);
    return { ok: true };
  }

  // ------------------------------------------------------------------ city commands
  private ownCity(p: Player, id: number): City | string {
    const c = this.city(id);
    if (!c) return 'That city no longer exists.';
    if (c.owner !== p.id) return 'That is not your city.';
    return c;
  }

  allowedItem(p: Player, city: City, kind: 'unit' | 'building', id: string): boolean {
    return kind === 'unit' ? canBuildUnit(this, p.id, city, id) : canBuildBuilding(this, p.id, city, id);
  }

  private cmdQueue(p: Player, cmd: Extract<Command, { t: 'queue' }>): CmdResult {
    const c = this.ownCity(p, cmd.city);
    if (typeof c === 'string') return { ok: false, error: c };
    const before = c.queue[0];
    if (cmd.op === 'set' || cmd.op === 'add') {
      if (!cmd.kind || !cmd.id) return { ok: false, error: 'Nothing selected.' };
      if (!this.allowedItem(p, c, cmd.kind, cmd.id)) return { ok: false, error: 'You cannot build that here.' };
      const item: QueueItem = { kind: cmd.kind, id: cmd.id };
      if (cmd.op === 'set') c.queue = [item];
      else {
        if (item.kind === 'building' && c.queue.some((q) => q.kind === 'building' && q.id === item.id)) return { ok: false, error: 'Already queued.' };
        if (c.queue.length >= 8) return { ok: false, error: 'The queue is full.' };
        c.queue.push(item);
      }
    } else if (cmd.op === 'remove') {
      const i = cmd.index ?? 0;
      if (i < 0 || i >= c.queue.length) return { ok: false, error: 'No such queue entry.' };
      c.queue.splice(i, 1);
    } else if (cmd.op === 'up') {
      const i = cmd.index ?? 0;
      if (i <= 0 || i >= c.queue.length) return { ok: true };
      [c.queue[i - 1], c.queue[i]] = [c.queue[i], c.queue[i - 1]];
    }
    this.normaliseProduction(c, before);
    return { ok: true };
  }

  /** Switching to a different class of item costs half the stored production. */
  normaliseProduction(c: City, before?: QueueItem): void {
    const head = c.queue[0];
    if (!head) {
      return;
    }
    if (before && before.kind !== head.kind && c.prod > 0 && c.prodKind === before.kind) c.prod = Math.floor(c.prod / 2);
    c.prodKind = head.kind;
  }

  buyPrice(c: City): number | null {
    const head = c.queue[0];
    if (!head) return null;
    if (head.kind === 'building' && BUILDINGS[head.id as BuildingId].wonder) return null;
    const cost = itemCost(this, c.owner, head.kind, head.id);
    const rem = cost - c.prod;
    if (rem <= 0) return null;
    return Math.ceil(2 * rem + (rem * rem) / 20);
  }

  private cmdBuy(p: Player, id: number): CmdResult {
    const c = this.ownCity(p, id);
    if (typeof c === 'string') return { ok: false, error: c };
    const price = this.buyPrice(c);
    if (price === null) return { ok: false, error: 'Nothing to buy.' };
    if (p.gold < price) return { ok: false, error: `You need ${price} gold.` };
    p.gold -= price;
    c.prod = itemCost(this, c.owner, c.queue[0].kind, c.queue[0].id);
    return { ok: true };
  }

  private cmdFocus(p: Player, id: number, focus: Focus): CmdResult {
    const c = this.ownCity(p, id);
    if (typeof c === 'string') return { ok: false, error: c };
    c.focus = focus;
    return { ok: true };
  }

  private cmdResearch(p: Player, tech: string): CmdResult {
    if (!availableTechs(p).includes(tech)) return { ok: false, error: 'That technology is not available.' };
    p.researching = tech;
    return { ok: true };
  }

  // ------------------------------------------------------------------ diplomacy
  private cmdPeace(p: Player, to: number): CmdResult {
    const o = this.s.players[to];
    if (!o || !o.alive || to === p.id) return { ok: false, error: 'Invalid player.' };
    if (this.relation(p.id, to) !== 'war') return { ok: false, error: 'You are not at war with them.' };
    if (this.s.proposals.some((x) => x.from === p.id && x.to === to)) return { ok: false, error: 'You already proposed peace.' };
    if (o.ai) {
      const accepts = this.aiAcceptsPeace(o, p);
      if (accepts) {
        this.signPeace(p.id, to);
        return { ok: true };
      }
      this.note([p.id], `${o.name} rejected your offer of peace.`, 'bad');
      return { ok: true };
    }
    this.s.proposals.push({ id: this.s.nextId++, from: p.id, to, kind: 'peace', turn: this.s.turn });
    this.note([to], `${p.name} proposes peace. Open the diplomacy panel to answer.`, 'info');
    return { ok: true };
  }

  signPeace(a: number, b: number): void {
    this.setRelation(a, b, 'peace');
    this.s.proposals = this.s.proposals.filter((x) => !((x.from === a && x.to === b) || (x.from === b && x.to === a)));
    this.note([], `${this.playerName(a)} and ${this.playerName(b)} signed a peace treaty.`, 'good');
    // units standing in each other's territory are pushed home
    this.expelFromBorders(a, b);
    this.expelFromBorders(b, a);
  }

  private expelFromBorders(mover: number, landlord: number): void {
    for (const u of this.playerUnits(mover).slice()) {
      const t = this.tile(u.x, u.y);
      if (t.owner !== landlord) continue;
      if (UNITS[u.type].civilian || UNITS[u.type].scout) continue;
      const home = this.s.cities.filter((c) => c.owner === mover).sort((c1, c2) => this.dist(c1.x, c1.y, u.x, u.y) - this.dist(c2.x, c2.y, u.x, u.y))[0];
      if (home) this.placeUnit(u, home.x, home.y);
      else this.removeUnit(u);
      u.order = null;
    }
    this.updateVisibility();
  }

  private aiAcceptsPeace(ai: Player, from: Player): boolean {
    const mine = this.militaryPower(ai.id);
    const theirs = this.militaryPower(from.id);
    const sinceWar = this.s.turn - this.s.relationSince[ai.id][from.id];
    if (sinceWar < 4) return false;
    const base = ai.personality === 'warlord' ? 0.15 : ai.personality === 'builder' ? 0.75 : 0.45;
    const ratio = theirs / Math.max(1, mine);
    let p = base + (ratio - 1) * 0.25;
    if (this.s.turn - ai.lastDefeat < 10) p += 0.3;
    if (ai.weariness > 4) p += 0.25; // a tired people want peace
    p = Math.max(0.03, Math.min(0.95, p));
    return this.rng.chance(p);
  }

  militaryPower(pid: number): number {
    let s = 0;
    for (const u of this.s.units) {
      if (u.owner !== pid) continue;
      const d = UNITS[u.type];
      if (d.civilian) continue;
      s += Math.max(d.strength, d.ranged) * (u.hp / 100);
    }
    for (const c of this.s.cities) if (c.owner === pid) s += c.pop;
    return s;
  }

  private cmdAccept(p: Player, proposalId: number, accept: boolean): CmdResult {
    const i = this.s.proposals.findIndex((x) => x.id === proposalId && x.to === p.id);
    if (i < 0) return { ok: false, error: 'That proposal no longer exists.' };
    const pr = this.s.proposals[i];
    this.s.proposals.splice(i, 1);
    if (pr.kind === 'deal') {
      if (accept && this.dealBlocker(pr.from, pr.to) === null) this.signDeal(pr.from, pr.to);
      else this.note([pr.from], `${p.name} turned down your trade agreement.`, 'bad');
      return { ok: true };
    }
    if (accept && this.relation(pr.from, pr.to) === 'war') this.signPeace(pr.from, pr.to);
    else this.note([pr.from], `${p.name} rejected your peace proposal.`, 'bad');
    return { ok: true };
  }

  // ------------------------------------------------------------------ trade agreements and sanctions
  /** Why these two cannot sign a trade agreement right now, or null when they can. */
  dealBlocker(a: number, b: number): string | null {
    const pa = this.s.players[a];
    const pb = this.s.players[b];
    if (!pa || !pb || !pa.alive || !pb.alive || a === b) return 'Invalid player.';
    if (this.relation(a, b) !== 'peace') return 'You can only trade with a civilisation you are at peace with.';
    if (this.hasDeal(a, b)) return 'You already trade with them.';
    if (this.isSanctioned(a, b) || this.isSanctioned(b, a)) return 'Sanctions forbid it.';
    if (dealPartners(this, a).length >= dealSlots(this, a)) return `You already have ${dealSlots(this, a)} trade agreements. An embassy or Navigation allows more.`;
    if (dealPartners(this, b).length >= dealSlots(this, b)) return `${pb.name} already has all the trade agreements they can hold.`;
    return null;
  }

  private cmdDeal(p: Player, to: number): CmdResult {
    const o = this.s.players[to];
    const why = this.dealBlocker(p.id, to);
    if (why) return { ok: false, error: why };
    if (this.s.proposals.some((x) => x.kind === 'deal' && ((x.from === p.id && x.to === to) || (x.from === to && x.to === p.id)))) return { ok: false, error: 'A trade agreement is already on the table.' };
    if (o.ai) {
      if (this.aiAcceptsDeal(o, p)) this.signDeal(p.id, to);
      else this.note([p.id], `${o.name} is not interested in trading right now.`, 'bad');
      return { ok: true };
    }
    this.s.proposals.push({ id: this.s.nextId++, from: p.id, to, kind: 'deal', turn: this.s.turn });
    this.note([to], `${p.name} proposes a trade agreement. Open the diplomacy panel to answer.`, 'info');
    return { ok: true };
  }

  private aiAcceptsDeal(ai: Player, from: Player): boolean {
    let chance = ai.personality === 'builder' ? 0.92 : ai.personality === 'balanced' ? 0.78 : 0.5;
    // trading with someone who struck first is distasteful, and with someone we are already wary of
    if (this.s.turn - from.aggression < 20) chance -= 0.45;
    if (this.isSanctioned(from.id, ai.id)) chance = 0;
    return this.rng.chance(Math.max(0.05, chance));
  }

  signDeal(a: number, b: number): void {
    const [x, y] = a < b ? [a, b] : [b, a];
    this.s.deals.push({ a: x, b: y, since: this.s.turn });
    this.s.proposals = this.s.proposals.filter((q) => !(q.kind === 'deal' && ((q.from === a && q.to === b) || (q.from === b && q.to === a))));
    const g = dealGold(this, a, b);
    refreshPlayer(this, a);
    refreshPlayer(this, b);
    this.note([a, b], `Trade agreement signed: ${this.playerName(a)} and ${this.playerName(b)} each earn ${g} gold a turn and share their luxuries.`, 'good');
  }

  /** End a trade agreement. `why` is told to both sides. */
  endDeal(a: number, b: number, why: 'war' | 'sanctions' | 'chosen' | 'lost'): void {
    const i = this.s.deals.findIndex((d) => (d.a === a && d.b === b) || (d.a === b && d.b === a));
    if (i < 0) return;
    this.s.deals.splice(i, 1);
    refreshPlayer(this, a);
    refreshPlayer(this, b);
    const text = why === 'war' ? 'War ended' : why === 'sanctions' ? 'Sanctions ended' : why === 'lost' ? 'The partner is gone, so' : 'Ended';
    this.note([a, b], `${text}: the trade agreement between ${this.playerName(a)} and ${this.playerName(b)}.`, 'info');
  }

  private cmdCancelDeal(p: Player, other: number): CmdResult {
    if (!this.hasDeal(p.id, other)) return { ok: false, error: 'You have no trade agreement with them.' };
    this.endDeal(p.id, other, 'chosen');
    return { ok: true };
  }

  /** Hand a city to a governor, who picks what it builds whenever the queue is empty, or take it back. */
  private cmdGovernor(p: Player, cityId: number, on: boolean): CmdResult {
    const c = this.ownCity(p, cityId);
    if (typeof c === 'string') return { ok: false, error: c };
    c.governor = !!on;
    if (c.governor && !c.queue.length) aiPlanCity(this, p, c);
    return { ok: true };
  }

  /** Tear a building down to free its slot. It pays back a little. */
  private cmdSell(p: Player, cityId: number, building: string): CmdResult {
    const c = this.ownCity(p, cityId);
    if (typeof c === 'string') return { ok: false, error: c };
    const d = BUILDINGS[building as BuildingId];
    if (!d || !c.buildings.includes(d.id)) return { ok: false, error: 'There is no such building here.' };
    if (d.wonder) return { ok: false, error: 'A wonder cannot be torn down.' };
    // a building others rest on stays while they do
    const dependant = c.buildings.find((b) => BUILDINGS[b].needs === d.id);
    if (dependant) return { ok: false, error: `${BUILDINGS[dependant].name} needs it.` };
    c.buildings = c.buildings.filter((b) => b !== d.id);
    const refund = Math.floor(itemCost(this, p.id, 'building', d.id) * 0.3);
    p.gold += refund;
    this.note([p.id], `${c.name} tore down its ${d.name} and recovered ${refund} gold.`, 'info', { x: c.x, y: c.y });
    return { ok: true };
  }

  private cmdSanction(p: Player, on: number): CmdResult {
    const o = this.s.players[on];
    if (!o || !o.alive || on === p.id) return { ok: false, error: 'Invalid player.' };
    if (this.relation(p.id, on) === 'unmet') return { ok: false, error: 'You have not met them.' };
    if (this.isSanctioned(p.id, on)) return { ok: false, error: 'Your sanctions are already in force.' };
    this.s.sanctions.push({ by: p.id, on, since: this.s.turn });
    this.endDeal(p.id, on, 'sanctions');
    this.s.proposals = this.s.proposals.filter((q) => !(q.kind === 'deal' && ((q.from === p.id && q.to === on) || (q.from === on && q.to === p.id))));
    refreshPlayer(this, on);
    this.note([], `${p.name} imposed sanctions on ${o.name}.`, 'war');
    return { ok: true };
  }

  private cmdLift(p: Player, on: number): CmdResult {
    const i = this.s.sanctions.findIndex((x) => x.by === p.id && x.on === on);
    if (i < 0) return { ok: false, error: 'You have no sanctions on them.' };
    this.s.sanctions.splice(i, 1);
    refreshPlayer(this, on);
    this.note([], `${p.name} lifted its sanctions on ${this.playerName(on)}.`, 'good');
    return { ok: true };
  }

  private cmdWar(p: Player, to: number): CmdResult {
    const o = this.s.players[to];
    if (!o || !o.alive || to === p.id) return { ok: false, error: 'Invalid player.' };
    if (this.relation(p.id, to) !== 'peace') return { ok: false, error: 'You are already at war or have not met.' };
    if (this.s.turn - this.s.relationSince[p.id][to] < PEACE_MIN_TURNS) return { ok: false, error: `The treaty must hold for ${PEACE_MIN_TURNS} turns.` };
    this.declareWar(p.id, to);
    return { ok: true };
  }

  // ------------------------------------------------------------------ tribal villages, promotions, policies, upgrades
  private exploreHut(u: Unit): void {
    const t = this.tile(u.x, u.y);
    t.hut = false;
    const p = this.s.players[u.owner];
    const at = { x: u.x, y: u.y };
    const roll = this.rng.next();
    const mask = [u.owner];
    if (roll < 0.3) {
      const gold = this.rng.int(30, 71);
      p.gold += gold;
      this.note(mask, `The villagers gift you ${gold} gold.`, 'good', at);
    } else if (roll < 0.5) {
      const bonus = p.researching ? Math.round(techCost(this, p, p.researching) * 0.45) : 30;
      p.research += bonus;
      this.note(mask, `A village elder shares ancient knowledge (+${bonus} science).`, 'tech', at);
    } else if (roll < 0.7) {
      const type: UnitTypeId = UNITS[u.type].civilian ? 'warrior' : 'worker';
      const spot = this.nb(u.x, u.y).find((n) => TERRAIN[this.tile(n.x, n.y).terrain].passable && this.unitsAt(n.x, n.y).length === 0 && !this.tile(n.x, n.y).city);
      if (spot) {
        this.addUnit(u.owner, type, spot.x, spot.y);
        this.note(mask, `Villagers join you: a free ${UNITS[type].name}!`, 'good', at);
      } else {
        p.gold += 40;
        this.note(mask, 'The villagers gift you 40 gold.', 'good', at);
      }
    } else if (roll < 0.85) {
      const bit = 1 << u.owner;
      for (const q of tilesWithin(u.x, u.y, 6, this.width, this.height)) this.tile(q.x, q.y).explored |= bit;
      this.note(mask, 'A hermit draws you a map of the surrounding lands.', 'info', at);
    } else if (roll < 0.95) {
      p.culture += 25;
      this.note(mask, 'The village shares its songs and customs (+25 culture).', 'good', at);
    } else {
      u.hp = Math.max(15, u.hp - 30);
      this.note(mask, 'The villagers were hostile and drove your unit off!', 'bad', at);
    }
  }

  gainXp(u: Unit, amount: number): void {
    if (UNITS[u.type].civilian) return;
    u.xp += amount;
    while (u.level < XP_LEVELS.length && u.xp >= XP_LEVELS[u.level]) {
      u.level++;
      u.hp = Math.min(UNIT_HP, u.hp + 25);
      this.note([u.owner], `Your ${UNITS[u.type].name} was promoted (level ${u.level})!`, 'good', { x: u.x, y: u.y });
    }
  }

  nextPolicyCost(p: Player): number {
    return policyCost(p.policies.length, this.speedMult);
  }

  /** Policies that can still be offered: not adopted, not passed over. */
  openPolicies(p: Player): string[] {
    return POLICIES.filter((x) => !p.policies.includes(x.id) && !p.forgone.includes(x.id)).map((x) => x.id);
  }

  private offerPolicy(p: Player): void {
    if (p.policyChoices.length) return;
    const open = this.openPolicies(p);
    if (!open.length || p.culture < this.nextPolicyCost(p)) return;
    this.rng.shuffle(open);
    p.policyChoices = open.slice(0, 3);
    if (p.ai) {
      const pref = AI_POLICY_PREF[p.personality] ?? AI_POLICY_PREF.balanced;
      // the earliest on the personality's list; anything else only when nothing listed is on offer
      const rank = (id: string) => { const i = pref.indexOf(id); return i < 0 ? 99 : i; };
      const pick = p.policyChoices.slice().sort((a, b) => rank(a) - rank(b))[0];
      this.adoptPolicy(p, pick);
    } else this.note([p.id], 'Your culture has inspired a new policy. Choose one: the others are lost for this game.', 'tech');
  }

  private adoptPolicy(p: Player, id: string): void {
    // the ones passed over are gone for good: that is what makes the choice a choice
    for (const other of p.policyChoices) if (other !== id && !p.forgone.includes(other)) p.forgone.push(other);
    p.policies.push(id);
    p.policyChoices = [];
    if (!p.ai) this.note([p.id], `Adopted ${POLICY_BY_ID[id].name}: ${POLICY_BY_ID[id].desc}`, 'good');
  }

  private cmdPolicy(p: Player, id: string): CmdResult {
    if (!p.policyChoices.includes(id)) return { ok: false, error: 'That policy is not on offer.' };
    this.adoptPolicy(p, id);
    return { ok: true };
  }

  private cmdTile(p: Player, cityId: number, x: number, y: number): CmdResult {
    const c = this.ownCity(p, cityId);
    if (typeof c === 'string') return { ok: false, error: c };
    if (!this.inBounds(x, y)) return { ok: false, error: 'Out of bounds.' };
    const t = this.tile(x, y);
    const idx = y * this.width + x;
    if (this.dist(c.x, c.y, x, y) > c.radius || t.owner !== p.id || !TERRAIN[t.terrain].workable || t.city >= 0) return { ok: false, error: 'This city cannot work that tile.' };
    const i = c.locked.indexOf(idx);
    if (i >= 0) c.locked.splice(i, 1);
    else {
      if (c.locked.length >= c.pop) return { ok: false, error: `${c.name} only has ${c.pop} citizen${c.pop > 1 ? 's' : ''}.` };
      // another of our cities may be using it: steal it
      for (const o of this.s.cities) if (o.owner === p.id && o.id !== c.id) o.locked = o.locked.filter((k) => k !== idx);
      c.locked.push(idx);
    }
    return { ok: true };
  }

  upgradeInfo(u: Unit): { to: UnitTypeId; cost: number; error: string | null } | null {
    const to = UNIT_UPGRADES[u.type];
    if (!to) return null;
    const p = this.s.players[u.owner];
    const def = UNITS[to];
    const from = UNITS[u.type];
    const cost = Math.max(25, Math.round((def.cost - from.cost * 0.5) * 1.6));
    let error: string | null = null;
    if (def.tech && !p.techs.includes(def.tech)) error = `Requires ${TECHS.find((t) => t.id === def.tech)!.name}.`;
    else if (def.needs && !this.hasResource(u.owner, def.needs)) error = `Requires ${def.needs}.`;
    else if (this.tile(u.x, u.y).owner !== u.owner) error = 'Must be inside your borders.';
    else if (p.gold < cost) error = `Costs ${cost} gold.`;
    return { to, cost, error };
  }

  private cmdUpgrade(p: Player, id: number): CmdResult {
    const u = this.ownUnit(p, id);
    if (typeof u === 'string') return { ok: false, error: u };
    const info = this.upgradeInfo(u);
    if (!info) return { ok: false, error: 'This unit cannot be upgraded.' };
    if (info.error) return { ok: false, error: info.error };
    p.gold -= info.cost;
    u.type = info.to;
    u.mp = 0;
    u.order = null;
    this.note([p.id], `Your unit was upgraded to ${UNITS[info.to].name}.`, 'good', { x: u.x, y: u.y });
    return { ok: true };
  }

  private cmdGift(p: Player, to: number, amount: number): CmdResult {
    const o = this.s.players[to];
    if (!o || !o.alive || to === p.id) return { ok: false, error: 'Invalid player.' };
    if (this.relation(p.id, to) === 'unmet') return { ok: false, error: 'You have not met them yet.' };
    const n = Math.floor(amount);
    if (!(n >= 1)) return { ok: false, error: 'Nothing to give.' };
    if (p.gold < n) return { ok: false, error: 'You do not have that much gold.' };
    p.gold -= n;
    o.gold += n;
    this.note([to], `${p.name} gave you ${n} gold!`, 'good');
    this.note([p.id], `You gave ${n} gold to ${o.name}.`, 'info');
    return { ok: true };
  }

  declareWar(a: number, b: number): void {
    // striking someone we were at peace with is remembered, and the world may punish it
    if (this.relation(a, b) === 'peace') this.s.players[a].aggression = this.s.turn;
    this.setRelation(a, b, 'war');
    this.endDeal(a, b, 'war');
    this.s.proposals = this.s.proposals.filter((x) => !((x.from === a && x.to === b) || (x.from === b && x.to === a)));
    this.note([], `${this.playerName(a)} declared war on ${this.playerName(b)}!`, 'war');
  }

  // ------------------------------------------------------------------ turn processing
  processTurn(): void {
    // 1. AI players act
    // the starting seat rotates every turn so nobody gets first pick of contested sites and wonders
    const n = this.s.players.length;
    const order = this.s.players.map((_, i) => this.s.players[(i + this.s.turn) % n]);
    for (const p of order) {
      if (p.ai && p.alive) this.guard(`AI ${p.name}`, () => aiTakeTurn(this, p.id));
    }
    this.updateVisibility();
    // 2. economies
    for (const p of order) if (p.alive) this.guard(`economy ${p.name}`, () => this.economyTurn(p));
    // 3. cities shoot, units heal, workers work
    this.guard('city defence', () => this.cityDefense());
    this.guard('unit upkeep', () => this.unitUpkeep());
    // 4. new turn bookkeeping
    this.s.turn++;
    this.s.proposals = this.s.proposals.filter((x) => this.s.turn - x.turn < 6 && this.s.players[x.from].alive && this.s.players[x.to].alive);
    // sanctions lapse unless they are imposed again
    for (const x of this.s.sanctions.slice()) {
      if (this.s.turn - x.since >= SANCTION_TURNS) {
        this.s.sanctions = this.s.sanctions.filter((y) => y !== x);
        this.note([x.by, x.on], `The sanctions of ${this.playerName(x.by)} on ${this.playerName(x.on)} have run out.`, 'info');
      }
    }
    // war tires a people; peace rests them
    for (const p of this.s.players) {
      if (!p.alive) continue;
      const atWar = this.s.players.some((o) => o.alive && o.id !== p.id && this.relation(p.id, o.id) === 'war');
      if (atWar) this.addWeariness(p.id, 0.15);
      else if (p.weariness > 0) this.addWeariness(p.id, -1);
    }
    for (const u of this.s.units) {
      const def = UNITS[u.type];
      u.mp = def.moves * 2 + ((def.scout || def.naval) && this.s.players[u.owner].policies.includes('exploration') ? 2 : 0);
      u.active = false;
      u.attacked = false;
      u.skipped = false;
    }
    for (const c of this.s.cities) c.shot = false;
    this.updateVisibility();
    this.warnOfThreats();
    this.tip();
    for (const p of this.s.players) {
      p.endedTurn = p.ai || !p.alive;
    }
    // 5. standing orders (goto, explore, auto-work)
    for (const u of this.s.units.slice()) {
      if (!this.unit(u.id)) continue;
      this.guard('standing orders', () => {
        if (u.order && u.order.kind === 'goto') this.followOrder(u);
        if (!this.unit(u.id) || u.order) return;
        if (u.auto === 'work') aiAutoWorker(this, u);
        else if (u.auto === 'explore') aiAutoExplore(this, u);
      });
    }
    this.updateVisibility();
    for (const p of this.s.players) if (p.alive) refreshPlayer(this, p.id);
    this.checkEliminations();
    this.checkVictory();
    this.updateScores();
    this.events.push({ kind: 'turn', turn: this.s.turn });
  }

  /** A bug in one subsystem must never wedge a running game: log it and carry on with the turn. */
  private guard(what: string, fn: () => void): void {
    try {
      fn();
    } catch (e) {
      // eslint-disable-next-line no-console
      console.error(`[jericho] ${what} failed on turn ${this.s.turn}:`, e);
    }
  }

  private tip(): void {
    const tips: Record<number, string> = {
      2: 'Tip: the big button (bottom right) always shows what needs you next — production, research, a unit, or ending the turn.',
      5: 'Tip: set Workers to Auto (A) and Scouts to Explore (A) so you can focus on the big decisions.',
      9: 'Tip: you cannot attack a civilisation you are at peace with. Open Diplomacy (D) to declare war once the treaty is 10 turns old.',
      14: 'Tip: queue several items in a city with the + button, and spend spare gold on Buy to finish them early.',
      22: 'Tip: every city and citizen makes your people a little more restless. Luxuries (wine, spices, gems, pearls) keep them content: settle near them or trade for them. The heart at the top shows how you are doing.',
      38: 'Tip: in Diplomacy you can sign trade agreements (gold for both, and you share luxuries) or impose sanctions on a civilisation you want to hurt without going to war.',
    };
    const t = tips[this.s.turn];
    if (!t) return;
    const humans = this.s.players.filter((p) => !p.ai && p.alive).map((p) => p.id);
    if (humans.length) this.note(humans, t, 'info');
  }

  /** Tell human players when hostile armies stand near their cities. */
  private warnOfThreats(): void {
    for (const c of this.s.cities) {
      const owner = this.s.players[c.owner];
      if (owner.ai) continue;
      let count = 0;
      for (const q of tilesWithin(c.x, c.y, 3, this.width, this.height)) {
        for (const u of this.unitsAt(q.x, q.y)) {
          if (u.owner !== c.owner && this.atWar(c.owner, u.owner) && !UNITS[u.type].civilian && (this.tile(q.x, q.y).visible & (1 << c.owner))) count++;
        }
      }
      if (count > 0) this.note([c.owner], `${count} enemy unit${count > 1 ? 's' : ''} near ${c.name}!`, 'war', { x: c.x, y: c.y });
    }
  }

  private economyTurn(p: Player): void {
    refreshPlayer(this, p.id);
    // say so, once, when the people turn restless and when they recover
    if (!p.ai) {
      if (p.content < 0 && !p.restless) {
        p.restless = true;
        this.note([p.id], 'Your people are restless: cities grow and produce less. Luxuries, temples and trade agreements help (click the heart at the top).', 'bad');
      } else if (p.content >= 1 && p.restless) {
        p.restless = false;
        this.note([p.id], 'Your people are content again.', 'good');
      }
    }
    const aiMult = p.ai ? (p.aiLevel === 'easy' ? 0.8 : p.aiLevel === 'hard' ? 1.15 : 1) : 1;
    // gold
    p.gold += p.income.gold;
    if (p.gold < 0) {
      p.gold = 0;
      this.note([p.id], 'Your treasury is empty — buildings and units cost more than you earn!', 'bad');
    }
    // science
    let sci = p.income.science * aiMult;
    if (!p.researching) {
      if (p.ai || p.autoResearch) p.researching = aiChooseResearch(this, p);
      else if (availableTechs(p).length && this.s.turn % 6 === 1) this.note([p.id], 'Choose a technology to research.', 'info');
    }
    p.research += sci;
    if (p.researching) {
      const need = techCost(this, p, p.researching);
      if (p.research >= need) {
        p.research -= need;
        p.techs.push(p.researching);
        this.note([p.id], `Discovered ${TECHS.find((t) => t.id === p.researching)!.name}!`, 'tech');
        if (this.s.settings.allowScience && !this.s.enlightenment && availableTechs(p).includes(FINAL_TECH)) this.note([], `${p.name} is one discovery away from the Enlightenment.`, 'wonder');
        p.researching = p.ai || p.autoResearch ? aiChooseResearch(this, p) : null;
        if (!p.researching && !p.ai && availableTechs(p).length) this.note([p.id], 'Choose your next technology.', 'info');
      }
    }
    p.culture += p.income.culture;
    this.offerPolicy(p);
    // cities
    for (const c of this.s.cities.filter((x) => x.owner === p.id)) this.cityTurn(c, p, aiMult);
    // now and then something happens: a windfall, a plague, a find under your own land
    this.guard('events', () => { rollEvent(this, p); });
  }

  private cityTurn(c: City, p: Player, aiMult: number): void {
    const y = cityYield(this, c);
    // growth
    c.food += y.surplus;
    const need = foodNeeded(this, c.pop, c.owner);
    if (c.food >= need) {
      if (c.pop < maxPop(c)) {
        c.pop++;
        c.food = Math.max(0, c.food - need);
        if (hasBuilding(c, 'granary')) c.food += Math.floor(need * 0.15);
        this.note([p.id], `${c.name} grew to size ${c.pop}.`, 'good', { x: c.x, y: c.y });
      } else c.food = need;
    } else if (c.food < 0) {
      if (c.pop > 1) {
        c.pop--;
        this.note([p.id], `${c.name} is starving and lost population!`, 'bad', { x: c.x, y: c.y });
      }
      c.food = 0;
    }
    // borders grow with the city
    if (c.pop >= 6 && c.radius < 3) {
      c.radius = 3;
      this.claimTiles(c, 3);
    }
    // production
    c.prod += Math.max(0, y.prod * aiMult);
    // a governor fills an empty queue, so the player is not asked
    if (!c.queue.length && c.governor && !p.ai) aiPlanCity(this, p, c);
    if (!c.queue.length) {
      if (p.ai) return;
      if (this.s.turn % 5 === 0 || c.founded === this.s.turn - 1) this.note([p.id], `${c.name} has nothing to build.`, 'info', { x: c.x, y: c.y });
      return;
    }
    // drop items that became impossible
    while (c.queue.length && !this.allowedItem(p, c, c.queue[0].kind, c.queue[0].id)) {
      const bad = c.queue.shift()!;
      if (bad.kind === 'unit' && bad.id === 'settler' && c.pop < 2) {
        // wait for growth instead of dropping
        c.queue.unshift(bad);
        break;
      }
      this.note([p.id], `${c.name} can no longer build ${bad.kind === 'unit' ? UNITS[bad.id as UnitTypeId].name : BUILDINGS[bad.id as BuildingId].name}.`, 'info');
    }
    const head = c.queue[0];
    if (!head) return;
    const cost = itemCost(this, p.id, head.kind, head.id);
    if (c.prod < cost) return;
    if (head.kind === 'unit') {
      if (head.id === 'settler' && c.pop < 2) return;
      c.prod -= cost;
      c.queue.shift();
      const level = (hasBuilding(c, 'barracks') || p.policies.includes('veterans')) && !UNITS[head.id as UnitTypeId].civilian ? 1 : 0;
      this.addUnit(p.id, head.id as UnitTypeId, c.x, c.y, level);
      if (head.id === 'settler') c.pop = Math.max(1, c.pop - 1);
      this.note([p.id], `${c.name} built ${UNITS[head.id as UnitTypeId].name}.`, 'good', { x: c.x, y: c.y });
    } else {
      c.prod -= cost;
      c.queue.shift();
      const b = BUILDINGS[head.id as BuildingId];
      c.buildings.push(b.id);
      if (b.wonder) {
        this.s.wonders[b.id] = p.id;
        for (const other of this.s.cities) {
          if (other.id === c.id) continue;
          const had = other.queue.length;
          other.queue = other.queue.filter((q) => !(q.kind === 'building' && q.id === b.id));
          if (had !== other.queue.length && other.queue.length === 0) other.prodKind = null;
        }
        this.note([], `${p.name} completed the wonder ${b.name} in ${c.name}!`, 'wonder', { x: c.x, y: c.y });
      } else this.note([p.id], `${c.name} built ${b.name}.`, 'good', { x: c.x, y: c.y });
    }
    if (c.queue[0]) c.prodKind = c.queue[0].kind;
    else c.prodKind = null;
    if (c.prod > cost) c.prod = Math.min(c.prod, cost);
  }

  private cityDefense(): void {
    for (const c of this.s.cities) {
      // regen
      if (this.s.turn - c.lastAttacked >= 2) {
        const garrison = this.unitsAt(c.x, c.y).some((u) => u.owner === c.owner && !UNITS[u.type].civilian);
        c.hp = Math.min(this.cityMaxHp(c), c.hp + (garrison ? 18 : 10));
      }
      c.hp = Math.min(c.hp, this.cityMaxHp(c));
      if (c.shot) continue;
      const targets = bombardTargets(this, c);
      if (targets.length) {
        const t = targets[0];
        c.shot = true;
        const ctx = { dmg: 0 };
        const dmg = Math.max(4, Math.round(30 * Math.pow(1.04, cityStrength(this, c) * 0.8 - this.strengthOf(t)) * (0.9 + 0.2 * this.rng.next())));
        ctx.dmg = Math.min(t.hp, dmg);
        t.hp -= ctx.dmg;
        const killed = t.hp <= 0;
        this.events.push({
          kind: 'attack', attacker: -c.id, owner: c.owner, type: 'archer', from: { x: c.x, y: c.y }, to: { x: t.x, y: t.y }, ranged: true,
          dmgDef: ctx.dmg, dmgAtt: 0, defOwner: t.owner, defType: t.type, killedDef: killed, killedAtt: false,
          mask: this.tile(c.x, c.y).visible | this.tile(t.x, t.y).visible,
        });
        if (killed) {
          this.events.push({ kind: 'death', type: t.type, owner: t.owner, at: { x: t.x, y: t.y }, mask: this.tile(t.x, t.y).visible });
          this.removeUnit(t);
        }
      }
    }
  }

  private strengthOf(u: Unit): number {
    const d = UNITS[u.type];
    return Math.max(d.strength, 1) * (1 + 0.15 * u.level);
  }

  private unitUpkeep(): void {
    for (const u of this.s.units.slice()) {
      // healing
      if (!u.active && u.hp < UNIT_HP) {
        const t = this.tile(u.x, u.y);
        let heal = 8;
        if (t.city >= 0 && this.city(t.city)?.owner === u.owner) {
          heal = 25;
          if (hasBuilding(this.city(t.city)!, 'barracks')) heal = 100;
        } else if (t.owner === u.owner) heal = 15;
        if (u.fortified) heal += 5;
        if (this.players[u.owner].policies.includes('conscription')) heal += 10;
        u.hp = Math.min(UNIT_HP, u.hp + heal);
      }
      // workers
      const o = u.order;
      if (o && o.kind === 'build') {
        const t = this.tile(u.x, u.y);
        if (!t.work || t.work.kind !== o.what) {
          u.order = null;
          continue;
        }
        t.work.progress += this.workRate(u.owner);
        if (t.work.progress >= IMPROVEMENTS[o.what].turns) {
          if (o.what === 'road') t.road = true;
          else t.improvement = o.what;
          t.work = null;
          u.order = null;
          this.note([u.owner], `${UNITS[u.type].name} finished a ${IMPROVEMENTS[o.what].name.toLowerCase()}.`, 'info', { x: t.x, y: t.y });
        }
      }
    }
    // negative treasury is handled in economyTurn (income already deducts upkeep); extreme debt disbands units
    for (const p of this.s.players) {
      if (p.alive && p.gold <= 0 && p.income.gold < 0) {
        const victims = this.s.units.filter((u) => u.owner === p.id && !UNITS[u.type].civilian);
        if (victims.length > 2) {
          victims.sort((a, b) => UNITS[a.type].cost - UNITS[b.type].cost);
          const v = victims[0];
          this.note([p.id], `A ${UNITS[v.type].name} deserted because you cannot pay your army.`, 'bad');
          this.removeUnit(v);
        }
      }
    }
  }

  // ------------------------------------------------------------------ elimination, victory, score
  checkEliminations(): boolean {
    let changed = false;
    for (const p of this.s.players) {
      if (!p.alive) continue;
      const hasCity = this.s.cities.some((c) => c.owner === p.id);
      const hasSettler = this.s.units.some((u) => u.owner === p.id && u.type === 'settler');
      if (!hasCity && !hasSettler) {
        p.alive = false;
        p.eliminatedTurn = this.s.turn;
        p.endedTurn = true;
        for (const u of this.s.units.filter((x) => x.owner === p.id)) this.removeUnit(u);
        for (const t of this.s.tiles) if (t.owner === p.id) t.owner = -1;
        this.s.proposals = this.s.proposals.filter((x) => x.from !== p.id && x.to !== p.id);
        this.s.deals = this.s.deals.filter((d) => d.a !== p.id && d.b !== p.id);
        this.s.sanctions = this.s.sanctions.filter((x) => x.by !== p.id && x.on !== p.id);
        this.note([], `${p.name} has been eliminated!`, 'war');
        changed = true;
      }
    }
    if (changed) this.updateVisibility();
    return changed;
  }

  checkVictory(): void {
    if (this.s.over) return;
    const alive = this.s.players.filter((p) => p.alive);
    if (this.s.settings.allowScience && (this.tickEnlightenment(alive) || this.tickHegemony(alive))) return;
    if (this.s.settings.allowDomination && this.tickDomination(alive)) return;
    if (alive.length <= 1) {
      return this.finish(alive[0]?.id ?? null, 'last-standing');
    }
    const humansAlive = alive.filter((p) => !p.ai);
    const humansTotal = this.s.players.filter((p) => !p.ai).length;
    if (humansTotal > 0 && humansAlive.length === 0) {
      // all humans eliminated: highest-scoring survivor takes the game
      this.updateScores();
      const best = alive.slice().sort((a, b) => b.score - a.score)[0];
      return this.finish(best.id, 'domination');
    }
    if (this.s.settings.allowDomination) {
      // all remaining civs are at peace with each other and one has every capital? keep simple: only one team alive
      // (handled by alive.length <= 1 above)
    }
    if (this.s.turn > this.s.settings.turnLimit) {
      this.updateScores();
      const best = alive.slice().sort((a, b) => b.score - a.score)[0];
      return this.finish(best.id, 'score');
    }
  }

  /**
   * Learning the final technology does not win at once: the discoverer must hold their capital for a while, which gives
   * everyone else a chance to stop them. Returns true when the countdown ended the game.
   */
  private tickEnlightenment(alive: Player[]): boolean {
    const barred = (this.s.barred ??= []);
    let e = this.s.enlightenment ?? null;
    if (e) {
      const p = this.s.players[e.owner];
      const cap = this.cities.find((c) => c.id === e!.capital);
      if (!p.alive || !cap || cap.owner !== e.owner) {
        barred.push(e.owner);
        this.s.enlightenment = null;
        this.note([], `${p.name}'s capital has fallen: the Enlightenment is lost to them.`, 'war');
        e = null;
      } else if (this.s.turn - e.since >= e.duration) {
        this.finish(e.owner, 'science');
        return true;
      } else {
        const left = e.duration - (this.s.turn - e.since);
        if (left <= 3 || left % 4 === 0) this.note([], `${p.name}'s Enlightenment: ${left} turn${left === 1 ? '' : 's'} left. Take their capital to stop it!`, 'wonder');
      }
    }
    if (!e) {
      const cand = alive.find((p) => p.techs.includes(FINAL_TECH) && !barred.includes(p.id));
      if (cand) {
        const cap = this.cities.find((c) => c.owner === cand.id && c.capital) ?? this.cities.find((c) => c.owner === cand.id);
        if (cap) {
          const duration = this.s.settings.speed === 'quick' ? 8 : 12;
          this.s.enlightenment = { owner: cand.id, since: this.s.turn, capital: cap.id, duration };
          this.note([], `${cand.name} has reached the Enlightenment! They win in ${duration} turns unless their capital, ${cap.name}, falls.`, 'wonder');
        }
      }
    }
    return false;
  }

  /**
   * Cultural hegemony: a civilisation whose culture passes the goal and towers over every rival (+20%) starts a
   * countdown; it ends if a rival's culture climbs back to within 10% of theirs. Culture keeps its worth after the
   * last policy is adopted, and gives builders a race of their own. Returns true when the countdown ended the game.
   */
  private tickHegemony(alive: Player[]): boolean {
    if (alive.length < 2) return false;
    const goal = this.cultureGoal;
    const ranked = alive.slice().sort((a, b) => b.culture - a.culture);
    const top = ranked[0];
    const second = ranked[1];
    let h = this.s.hegemony ?? null;
    if (h) {
      const p = this.s.players[h.owner];
      if (!p.alive || p.culture < goal || p.culture < second.culture * 1.1 || (second.id === p.id)) {
        this.note([], `${p.name}'s cultural lead has slipped: the hegemony is broken.`, 'good');
        this.s.hegemony = null;
        h = null;
      } else if (this.s.turn - h.since >= h.duration) {
        this.finish(h.owner, 'culture');
        return true;
      } else {
        const left = h.duration - (this.s.turn - h.since);
        if (left <= 3 || left % 4 === 0) this.note([], `${p.name}'s cultural hegemony: ${left} turn${left === 1 ? '' : 's'} left. Out-build their culture or take their wonders!`, 'wonder');
      }
    }
    if (!h && top.culture >= goal && top.culture >= second.culture * 1.2) {
      const duration = this.s.settings.speed === 'quick' ? 7 : 10;
      this.s.hegemony = { owner: top.id, since: this.s.turn, duration };
      this.note([], `${top.name}'s culture dominates the world! They win in ${duration} turns unless a rival's culture catches up.`, 'wonder');
    } else if (!h && this.s.turn % 10 === 0) {
      // a visible race before the countdown: the leader's progress toward the goal
      const share = Math.min(1, top.culture / goal);
      if (share >= 0.5) this.note([], `Culture race: ${top.name} has ${Math.round(share * 100)}% of the culture needed for hegemony.`, 'info');
    }
    return false;
  }

  /**
   * Domination: a civilisation that holds at least 60% of every city in the world, at least six of them and at least
   * twice as many as anyone else, starts a countdown. Winning back enough cities breaks it. It gives a war a finish
   * line short of wiping everyone out. Returns true when the countdown ended the game.
   */
  private tickDomination(alive: Player[]): boolean {
    if (alive.length < 2) return false;
    const counts = alive.map((p) => ({ id: p.id, n: this.cities.filter((c) => c.owner === p.id).length })).sort((a, b) => b.n - a.n);
    const total = this.cities.length;
    const top = counts[0];
    const second = counts[1]?.n ?? 0;
    let d = this.s.domination ?? null;
    if (d) {
      const p = this.s.players[d.owner];
      const n = counts.find((c) => c.id === d!.owner)?.n ?? 0;
      // a little looser than the start, so a single lost city does not reset the clock
      if (!p.alive || n < total * 0.5 || n < second * 1.5) {
        this.note([], `${p.name}'s grip on the world has slipped: the domination is broken.`, 'good');
        this.s.domination = null;
        d = null;
      } else if (this.s.turn - d.since >= d.duration) {
        this.finish(d.owner, 'domination');
        return true;
      } else {
        const left = d.duration - (this.s.turn - d.since);
        if (left <= 3 || left % 4 === 0) this.note([], `${p.name} holds ${n} of ${total} cities: ${left} turn${left === 1 ? '' : 's'} to domination. Take cities back!`, 'wonder');
      }
    }
    if (!d && top.n >= 6 && top.n >= total * 0.6 && top.n >= second * 2) {
      const duration = this.s.settings.speed === 'quick' ? 7 : 10;
      this.s.domination = { owner: top.id, since: this.s.turn, duration };
      this.note([], `${this.playerName(top.id)} holds ${top.n} of ${total} cities! They win by domination in ${duration} turns unless the world takes cities back.`, 'wonder');
    }
    return false;
  }

  private finish(winner: number | null, victory: GameState['victory']): void {
    this.s.over = true;
    this.s.winner = winner;
    this.s.victory = victory;
    const name = winner === null ? 'Nobody' : this.playerName(winner);
    const how = victory === 'science' ? 'a scientific victory' : victory === 'culture' ? 'cultural hegemony' : victory === 'score' ? 'the highest score' : 'domination';
    this.note([], `Game over — ${name} wins by ${how}!`, 'wonder');
  }

  computeScore(p: Player): number {
    let s = 0;
    for (const c of this.s.cities) {
      if (c.owner !== p.id) continue;
      s += 8 + c.pop * 3 + c.buildings.filter((b) => !BUILDINGS[b].wonder).length * 2;
      s += c.buildings.filter((b) => BUILDINGS[b].wonder).length * 14;
    }
    s += p.techs.length * 6;
    s += Math.floor(p.culture / 8);
    let land = 0;
    for (const t of this.s.tiles) if (t.owner === p.id) land++;
    s += Math.floor(land * 0.5);
    return Math.round(s);
  }

  updateScores(): void {
    for (const p of this.s.players) p.score = p.alive ? this.computeScore(p) : p.score;
  }

  // ------------------------------------------------------------------ helpers used by combat / AI
  civOf(pid: number) {
    return CIV_BY_ID[this.players[pid].civ];
  }

  transferCity(city: City, newOwner: number): void {
    captureCity(this, city, newOwner);
  }

  assignTiles(pid: number): void {
    assignWorkedTiles(this, pid);
  }
}

const EMPTY: Unit[] = [];
