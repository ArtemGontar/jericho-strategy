import { BUILDINGS, CIV_BY_ID, ERA_NAMES, IMPROVEMENT_YIELD, TECHS, UNITS, XP_LEVELS } from './data';
import {
  availableTechs, canBuildBuilding, canBuildUnit, cityYield, foodNeeded, itemCost, maxPop, techCost, type CityYield,
} from './economy';
import { cityStrength } from './combat';
import type { Game, GameEvent } from './game';
import type {
  BuildingId, Focus, GameSettings, Note, Proposal, Relation, ResourceId, TerrainId, UnitOrder, UnitTypeId, WorkKind,
} from './types';

export const TERRAIN_LIST: TerrainId[] = ['ocean', 'coast', 'grass', 'plains', 'desert', 'tundra', 'forest', 'hills', 'mountain'];
export const RESOURCE_LIST: ResourceId[] = ['wheat', 'cattle', 'fish', 'deer', 'horses', 'iron', 'gold'];

export interface ViewPlayer {
  id: number;
  name: string;
  civ: string;
  ai: boolean;
  alive: boolean;
  connected: boolean;
  endedTurn: boolean;
  score: number;
  techCount: number;
  cityCount: number;
  relation: Relation;
  /** turns the current relation has lasted */
  relationAge: number;
  power: number;
}

export interface ViewUnit {
  id: number;
  owner: number;
  type: UnitTypeId;
  x: number;
  y: number;
  hp: number;
  level: number;
  /** ship this unit is aboard (own units only) */
  carriedBy?: number;
  /** ids of the units aboard this ship (own ships only) */
  cargo?: number[];
  xp?: number;
  nextXp?: number | null;
  upgrade?: { to: UnitTypeId; cost: number; error: string | null } | null;
  mp?: number;
  order?: UnitOrder | null;
  auto?: 'work' | 'explore' | null;
  fortified: boolean;
  attacked?: boolean;
  skipped?: boolean;
  /** work being done on the tile under this worker */
  workProgress?: { kind: WorkKind; progress: number; total: number } | null;
}

export interface BuildOption {
  kind: 'unit' | 'building';
  id: string;
  cost: number;
  turns: number;
}

export interface ViewCity {
  id: number;
  owner: number;
  name: string;
  x: number;
  y: number;
  pop: number;
  capital: boolean;
  hp: number;
  maxHp: number;
  visible: boolean;
  strength: number;
  wonders: BuildingId[];
  // own cities only:
  mine?: boolean;
  food?: number;
  foodNeeded?: number;
  prod?: number;
  queue?: Array<{ kind: 'unit' | 'building'; id: string; cost: number }>;
  buildings?: BuildingId[];
  focus?: Focus;
  yield?: CityYield;
  worked?: number[];
  locked?: number[];
  options?: BuildOption[];
  buyPrice?: number | null;
  maxPop?: number;
  radius?: number;
}

export interface GameView {
  me: number;
  turn: number;
  settings: GameSettings;
  over: boolean;
  winner: number | null;
  victory: string | null;
  players: ViewPlayer[];
  // private economy
  gold: number;
  income: { gold: number; science: number; culture: number };
  researching: string | null;
  research: number;
  researchCost: number;
  techs: string[];
  availableTechs: string[];
  techCosts: Record<string, number>;
  culture: number;
  policies: string[];
  policyChoices: string[];
  /** culture needed for the next policy */
  policyCost: number;
  // map: parallel arrays, index = y * width + x
  terrain: number[];
  resource: number[];
  improvement: number[];
  road: number[];
  hut: number[];
  owner: number[];
  /** 0 unexplored, 1 fogged, 2 visible */
  state: number[];
  /** per-tile work progress for own tiles: kind code*100+progress, 0 none */
  cities: ViewCity[];
  units: ViewUnit[];
  proposals: Proposal[];
  notes: Note[];
  wonders: Partial<Record<BuildingId, number>>;
  /** milliseconds left on the turn timer when this view was sent (online), or 0 */
  timerEnds: number;
  /** the Enlightenment countdown, when one is running */
  enlightenment?: { owner: number; capital: number; turnsLeft: number } | null;
  /** the cultural hegemony countdown, when one is running */
  hegemony?: { owner: number; turnsLeft: number } | null;
  /** culture a civilisation needs before it can claim cultural hegemony */
  cultureGoal?: number;
}

const IMPROVEMENT_CODE: Record<string, number> = { farm: 1, mine: 2 };
export const IMPROVEMENT_NAMES: Array<'farm' | 'mine' | null> = [null, 'farm', 'mine'];
void IMPROVEMENT_YIELD;
void ERA_NAMES;
void TECHS;

export function buildView(game: Game, me: number, timerEnds = 0): GameView {
  const s = game.s;
  const p = game.players[me];
  const bit = 1 << me;
  const n = game.width * game.height;
  const terrain = new Array<number>(n);
  const resource = new Array<number>(n);
  const improvement = new Array<number>(n);
  const road = new Array<number>(n);
  const hut = new Array<number>(n);
  const owner = new Array<number>(n);
  const state = new Array<number>(n);
  for (let i = 0; i < n; i++) {
    const t = game.tiles[i];
    if (!(t.explored & bit)) {
      terrain[i] = 0; resource[i] = 0; improvement[i] = 0; road[i] = 0; hut[i] = 0; owner[i] = 0; state[i] = 0;
      continue;
    }
    terrain[i] = TERRAIN_LIST.indexOf(t.terrain);
    resource[i] = t.resource ? RESOURCE_LIST.indexOf(t.resource) + 1 : 0;
    improvement[i] = t.improvement ? IMPROVEMENT_CODE[t.improvement] : 0;
    road[i] = t.road ? 1 : 0;
    hut[i] = t.hut ? 1 : 0;
    owner[i] = t.owner + 1;
    state[i] = t.visible & bit ? 2 : 1;
  }

  const players: ViewPlayer[] = s.players.map((o) => ({
    id: o.id,
    name: o.name,
    civ: o.civ,
    ai: o.ai,
    alive: o.alive,
    connected: o.connected,
    endedTurn: o.endedTurn,
    score: o.score,
    techCount: o.techs.length,
    cityCount: game.cities.filter((c) => c.owner === o.id).length,
    relation: game.relation(me, o.id),
    relationAge: s.turn - s.relationSince[me][o.id],
    power: Math.round(game.militaryPower(o.id)),
  }));

  const units: ViewUnit[] = [];
  for (const u of game.units) {
    const own = u.owner === me;
    if (!own && !(game.tile(u.x, u.y).visible & bit)) continue;
    if (!own && u.carriedBy != null) continue; // passengers are not visible from outside
    const v: ViewUnit = { id: u.id, owner: u.owner, type: u.type, x: u.x, y: u.y, hp: u.hp, level: u.level, fortified: u.fortified };
    if (u.carriedBy != null) v.carriedBy = u.carriedBy;
    if (UNITS[u.type].naval && (UNITS[u.type].capacity ?? 0) > 0 && own) v.cargo = game.cargoOf(u).map((c) => c.id);
    if (own) {
      v.mp = u.mp;
      v.order = u.order;
      v.auto = u.auto;
      v.attacked = u.attacked;
      v.xp = u.xp;
      v.nextXp = u.level < XP_LEVELS.length ? XP_LEVELS[u.level] : null;
      v.upgrade = game.upgradeInfo(u);
      v.skipped = u.skipped;
      const t = game.tile(u.x, u.y);
      if (t.work && u.order && u.order.kind === 'build') {
        v.workProgress = { kind: t.work.kind, progress: t.work.progress, total: 0 };
      }
    }
    units.push(v);
  }

  const cities: ViewCity[] = [];
  for (const c of game.cities) {
    const t = game.tile(c.x, c.y);
    const mine = c.owner === me;
    if (!mine && !(t.explored & bit)) continue;
    const vc: ViewCity = {
      id: c.id, owner: c.owner, name: c.name, x: c.x, y: c.y, pop: c.pop, capital: c.capital, hp: c.hp, maxHp: game.cityMaxHp(c),
      visible: !!(t.visible & bit), strength: Math.round(cityStrength(game, c) * 10) / 10,
      wonders: c.buildings.filter((b) => BUILDINGS[b].wonder),
    };
    if (mine) {
      vc.mine = true;
      vc.food = c.food;
      vc.foodNeeded = foodNeeded(game, c.pop);
      vc.prod = c.prod;
      vc.queue = c.queue.map((q) => ({ kind: q.kind, id: q.id, cost: itemCost(game, me, q.kind, q.id) }));
      vc.buildings = c.buildings.slice();
      vc.focus = c.focus;
      vc.yield = cityYield(game, c);
      vc.worked = c.worked.slice();
      vc.locked = c.locked.slice();
      vc.buyPrice = game.buyPrice(c);
      vc.maxPop = maxPop(c);
      vc.radius = c.radius;
      const opts: BuildOption[] = [];
      const prodRate = Math.max(1, vc.yield.prod);
      for (const id of Object.keys(UNITS) as UnitTypeId[]) {
        if (!canBuildUnit(game, me, c, id)) continue;
        const cost = itemCost(game, me, 'unit', id);
        opts.push({ kind: 'unit', id, cost, turns: Math.max(1, Math.ceil(cost / prodRate)) });
      }
      for (const id of Object.keys(BUILDINGS) as BuildingId[]) {
        if (!canBuildBuilding(game, me, c, id)) continue;
        const cost = itemCost(game, me, 'building', id);
        opts.push({ kind: 'building', id, cost, turns: Math.max(1, Math.ceil(cost / prodRate)) });
      }
      vc.options = opts;
    }
    cities.push(vc);
  }

  const notes = s.notes.filter((x) => (x.players.length === 0 || x.players.includes(me)) && x.text).slice(-60);
  const researchCost = p.researching ? techCost(game, p, p.researching) : 0;
  const techCosts: Record<string, number> = {};
  for (const t of availableTechs(p)) techCosts[t] = techCost(game, p, t);
  return {
    me,
    turn: s.turn,
    settings: s.settings,
    over: s.over,
    winner: s.winner,
    victory: s.victory,
    players,
    gold: Math.floor(p.gold),
    income: { gold: p.income.gold, science: p.income.science, culture: p.income.culture },
    researching: p.researching,
    research: Math.floor(p.research),
    researchCost,
    techs: p.techs.slice(),
    availableTechs: availableTechs(p),
    techCosts,
    culture: Math.floor(p.culture),
    policies: p.policies.slice(),
    policyChoices: p.policyChoices.slice(),
    policyCost: game.nextPolicyCost(p),
    terrain, resource, improvement, road, hut, owner, state,
    cities, units,
    proposals: s.proposals.filter((x) => x.from === me || x.to === me),
    notes,
    wonders: { ...s.wonders },
    timerEnds,
    enlightenment: s.enlightenment ? { owner: s.enlightenment.owner, capital: s.enlightenment.capital, turnsLeft: s.enlightenment.duration - (s.turn - s.enlightenment.since) } : null,
    hegemony: s.hegemony ? { owner: s.hegemony.owner, turnsLeft: s.hegemony.duration - (s.turn - s.hegemony.since) } : null,
    cultureGoal: game.cultureGoal,
  };
}

export function filterEvents(events: GameEvent[], me: number): GameEvent[] {
  const bit = 1 << me;
  return events.filter((e) => e.kind === 'turn' || (e.mask & bit) !== 0);
}

void CIV_BY_ID;
