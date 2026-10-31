/**
 * Random events: now and then something good or bad happens to a civilisation, and what it knows decides how hard.
 *
 * - Good luck: a bountiful harvest, travelling merchants, visiting scholars, ancient ruins, a new luxury found in the
 *   hills, and, once the technology exists to use them, coal and oil under your own land.
 * - Bad luck: drought, plague, an earthquake, floods, wildfire, raiders. Medicine and Sanitation make plague rare,
 *   a Hospital stops it, Construction lets buildings stand through an earthquake, a garrison turns raiders back.
 *
 * Nothing bad happens before turn 30 or to a civilisation with a single city, so a new player gets a few kind surprises and
 * time to learn first. Easy opponents mean a gentler fate for the player as well. Events never pick a choice for you and never
 * stack: one at most every few turns. Everything is drawn from the game's own seeded random numbers, so a game replays the same.
 */
import { BUILDINGS, LUXURIES, RESOURCES } from './data';
import { foodNeeded, techCost } from './economy';
import { luxuryAccess } from './civ';
import type { City, CmdResult, PendingEvent, Player, ResourceId, Tile } from './types';
import type { Game } from './game';

export type EventsSetting = 'off' | 'gentle' | 'normal';
/** chance per turn that a civilisation is touched by something */
export const EVENT_CHANCE: Record<EventsSetting, number> = { off: 0, gentle: 0.05, normal: 0.09 };
/** fewest turns between two events for one civilisation */
export const EVENT_SPACING = 4;
/** nothing bad before this turn */
export const FIRST_BAD_TURN = 30;

interface Ctx {
  game: Game;
  p: Player;
  cities: City[];
  era: number;
}
interface Plan {
  weight: number;
  run: () => { text: string; at?: { x: number; y: number }; kind?: 'good' | 'bad' | 'info' };
}
interface EventDef {
  id: string;
  kind: 'good' | 'bad';
  minTurn: number;
  /** the civilisation must know this technology */
  tech?: string;
  plan(c: Ctx): Plan | null;
}

const pick = <T>(game: Game, list: T[]): T | null => (list.length ? list[Math.floor(game.rng.next() * list.length)] : null);
const has = (p: Player, tech: string): boolean => p.techs.includes(tech);

function ownedTiles(game: Game, pid: number, pred: (t: Tile) => boolean): Tile[] {
  const out: Tile[] = [];
  for (const t of game.tiles) if (t.owner === pid && t.city < 0 && pred(t)) out.push(t);
  return out;
}
const nearestCity = (c: Ctx, t: Tile): City | undefined => c.cities.slice().sort((a, b) => c.game.dist(a.x, a.y, t.x, t.y) - c.game.dist(b.x, b.y, t.x, t.y))[0];

/** The civilisation's era: that of the newest technology it knows. */
function eraOf(game: Game, p: Player): number {
  let era = 0;
  for (const t of game.players[p.id].techs) era = Math.max(era, game.techEra(t));
  return era;
}

function discovery(c: Ctx, res: ResourceId, terrains: string[], limit: number): Plan | null {
  const { game, p } = c;
  if (game.tiles.filter((t) => t.owner === p.id && t.resource === res).length >= limit) return null;
  const spot = pick(game, ownedTiles(game, p.id, (t) => !t.resource && terrains.includes(t.terrain)));
  if (!spot) return null;
  const city = nearestCity(c, spot);
  return {
    weight: 8,
    run: () => {
      spot.resource = res;
      return { text: `${RESOURCES[res].name} found near ${city?.name ?? 'your cities'}! ${res === 'coal' ? 'Mines there will feed your industry.' : 'Pumps there will pay for themselves.'}`, at: { x: spot.x, y: spot.y }, kind: 'good' };
    },
  };
}

export const EVENTS: EventDef[] = [
  // ------------------------------------------------------------------ good luck
  {
    id: 'harvest', kind: 'good', minTurn: 4,
    plan: (c) => {
      const city = pick(c.game, c.cities);
      if (!city) return null;
      return { weight: 10, run: () => {
        city.food += Math.round(foodNeeded(c.game, city.pop, c.p.id) * 0.6);
        return { text: `A bountiful harvest in ${city.name}: the granaries overflow and its people thrive.`, at: { x: city.x, y: city.y }, kind: 'good' };
      } };
    },
  },
  {
    id: 'merchants', kind: 'good', minTurn: 6,
    plan: (c) => {
      const city = pick(c.game, c.cities);
      if (!city) return null;
      return { weight: 8, run: () => {
        const gold = 12 + 12 * c.era + 3 * c.cities.length;
        c.p.gold += gold;
        return { text: `Travelling merchants stop at ${city.name} and spend ${gold} gold in its markets.`, at: { x: city.x, y: city.y }, kind: 'good' };
      } };
    },
  },
  {
    id: 'scholars', kind: 'good', minTurn: 10,
    plan: (c) => {
      const { game, p } = c;
      if (!p.researching) return null;
      return { weight: 6, run: () => {
        const bonus = Math.max(5, Math.round(techCost(game, p, p.researching!) * 0.12));
        p.research += bonus;
        return { text: `Wandering scholars share what they know: +${bonus} research.`, kind: 'good' };
      } };
    },
  },
  {
    id: 'ruins', kind: 'good', minTurn: 8,
    plan: (c) => {
      const city = pick(c.game, c.cities);
      if (!city) return null;
      return { weight: 5, run: () => {
        const culture = 10 + Math.round(c.game.turn / 3);
        c.p.culture += culture;
        return { text: `Ancient ruins near ${city.name} reveal a forgotten art: +${culture} culture.`, at: { x: city.x, y: city.y }, kind: 'good' };
      } };
    },
  },
  {
    id: 'luxury', kind: 'good', minTurn: 15, tech: 'mining',
    plan: (c) => {
      const { game, p } = c;
      const have = new Set<ResourceId>([...luxuryAccess(game, p.id).own, ...luxuryAccess(game, p.id).traded]);
      const lacking = LUXURIES.filter((r) => !have.has(r) && r !== 'pearls');
      const res = pick(game, lacking);
      if (!res) return null;
      const spot = pick(game, ownedTiles(game, p.id, (t) => !t.resource && RESOURCES[res].terrains.includes(t.terrain)));
      if (!spot) return null;
      const city = nearestCity(c, spot);
      return { weight: 6, run: () => {
        spot.resource = res;
        return { text: `Prospectors find ${RESOURCES[res].name.toLowerCase()} near ${city?.name ?? 'your cities'}: a new luxury for your people (+3 contentment).`, at: { x: spot.x, y: spot.y }, kind: 'good' };
      } };
    },
  },
  { id: 'coal', kind: 'good', minTurn: 1, tech: 'industrialization', plan: (c) => discovery(c, 'coal', ['hills', 'plains', 'desert', 'tundra'], 3) },
  { id: 'oil', kind: 'good', minTurn: 1, tech: 'combustion', plan: (c) => discovery(c, 'oil', ['desert', 'plains', 'tundra', 'coast'], 2) },

  // ------------------------------------------------------------------ bad luck
  {
    id: 'drought', kind: 'bad', minTurn: FIRST_BAD_TURN,
    plan: (c) => {
      const city = pick(c.game, c.cities.filter((x) => x.pop >= 2));
      if (!city) return null;
      const granary = city.buildings.includes('granary');
      return { weight: 6, run: () => {
        const loss = Math.round(foodNeeded(c.game, city.pop, c.p.id) * (granary ? 0.25 : 0.55));
        city.food = Math.max(0, city.food - loss);
        return { text: granary ? `Drought hits ${city.name}, but its granary keeps the people fed.` : `Drought in ${city.name}: the crops fail and its stores run low.`, at: { x: city.x, y: city.y }, kind: 'bad' };
      } };
    },
  },
  {
    id: 'plague', kind: 'bad', minTurn: 35,
    plan: (c) => {
      const city = pick(c.game, c.cities.filter((x) => x.pop >= 3 && !x.buildings.includes('hospital')));
      if (!city || c.p.policies.includes('public_health')) return null;
      // medicine and sanitation make it rare
      const weight = 6 * (has(c.p, 'medicine') ? 0.4 : 1) * (has(c.p, 'sanitation') ? 0.3 : 1);
      return { weight, run: () => {
        city.pop -= 1;
        city.food = 0;
        return { text: `Plague strikes ${city.name}: many have died. ${has(c.p, 'medicine') ? 'A Hospital would have saved them.' : 'Medicine would help against it.'}`, at: { x: city.x, y: city.y }, kind: 'bad' };
      } };
    },
  },
  {
    id: 'earthquake', kind: 'bad', minTurn: 50,
    plan: (c) => {
      const { game, p } = c;
      const city = pick(game, c.cities.filter((x) => x.buildings.some((b) => !BUILDINGS[b].wonder)));
      if (!city) return null;
      return { weight: 3, run: () => {
        const b = pick(game, city.buildings.filter((x) => !BUILDINGS[x].wonder))!;
        // sound construction lets most buildings ride it out
        if (has(p, 'construction') && game.rng.chance(0.7)) {
          return { text: `An earthquake shakes ${city.name}, but its ${BUILDINGS[b].name} stands firm thanks to sound construction.`, at: { x: city.x, y: city.y }, kind: 'info' };
        }
        city.buildings = city.buildings.filter((x) => x !== b);
        return { text: `An earthquake in ${city.name} brings down its ${BUILDINGS[b].name}. ${has(p, 'construction') ? '' : 'Construction would help buildings stand.'}`.trim(), at: { x: city.x, y: city.y }, kind: 'bad' };
      } };
    },
  },
  {
    id: 'flood', kind: 'bad', minTurn: FIRST_BAD_TURN,
    plan: (c) => {
      const city = pick(c.game, c.cities.filter((x) => c.game.isCoastal(x.x, x.y) && !x.buildings.includes('harbor')));
      if (!city) return null;
      return { weight: 4, run: () => {
        city.prod = Math.floor(city.prod * 0.3);
        return { text: `Floods hit ${city.name}: much of its work is washed away. A Harbor protects the shore.`, at: { x: city.x, y: city.y }, kind: 'bad' };
      } };
    },
  },
  {
    id: 'wildfire', kind: 'bad', minTurn: FIRST_BAD_TURN,
    plan: (c) => {
      const spot = pick(c.game, ownedTiles(c.game, c.p.id, (t) => t.improvement === 'farm' || t.improvement === 'mine'));
      if (!spot) return null;
      const city = nearestCity(c, spot);
      return { weight: 4, run: () => {
        const what = spot.improvement === 'farm' ? 'farm' : 'mine';
        spot.improvement = null;
        return { text: `Wildfire destroys a ${what} near ${city?.name ?? 'your cities'}. Your workers can rebuild it.`, at: { x: spot.x, y: spot.y }, kind: 'bad' };
      } };
    },
  },
  {
    id: 'raiders', kind: 'bad', minTurn: 40,
    plan: (c) => {
      const { game, p } = c;
      if (p.gold < 30) return null;
      const city = pick(game, c.cities);
      if (!city) return null;
      return { weight: 4, run: () => {
        const guarded = game.unitsAt(city.x, city.y).some((u) => u.owner === p.id && !game.isCivilian(u));
        if (guarded) return { text: `Raiders approach ${city.name}, but its garrison turns them back.`, at: { x: city.x, y: city.y }, kind: 'info' };
        const stolen = Math.min(60, Math.round(p.gold * 0.15));
        p.gold -= stolen;
        return { text: `Raiders plunder ${city.name} and carry off ${stolen} gold. A garrison would have stopped them.`, at: { x: city.x, y: city.y }, kind: 'bad' };
      } };
    },
  },
];

// ---------------------------------------------------------------------------------------------------- questions
/**
 * A few events ask something. The player sees two options with a picture and two words each; if the question is ignored it
 * is answered the way `fallback` says when the next turn begins, so no one is ever held up. The computers just choose.
 */
interface Choice {
  id: string;
  kind: 'good' | 'bad';
  minTurn: number;
  /** the option taken when nobody answers */
  fallback: number;
  plan(c: Ctx): { weight: number; text: string; city?: City; options: PendingEvent['options'] } | null;
  resolve(c: Ctx, option: number, city: City | null): { text: string; kind: 'good' | 'bad' | 'info' };
}

export const CHOICES: Choice[] = [
  {
    id: 'dispute', kind: 'good', minTurn: 20, fallback: 0,
    plan: (c) => (c.p.researching ? { weight: 4, text: 'Scholars and priests disagree about what to fund.', options: [{ label: 'Libraries', art: 'library', hint: 'research' }, { label: 'Temples', art: 'temple', hint: 'culture' }] } : null),
    resolve: (c, option) => {
      const { game, p } = c;
      if (option === 0 && p.researching) {
        const bonus = Math.max(8, Math.round(techCost(game, p, p.researching) * 0.18));
        p.research += bonus;
        return { text: `The libraries win: +${bonus} research.`, kind: 'good' };
      }
      const culture = 20 + Math.round(game.turn / 3);
      p.culture += culture;
      return { text: `The temples win: +${culture} culture.`, kind: 'good' };
    },
  },
  {
    id: 'envoy', kind: 'good', minTurn: 12, fallback: 0,
    plan: () => ({ weight: 4, text: 'An envoy from a distant land arrives.', options: [{ label: 'Take gifts', art: 'gold', hint: 'gold' }, { label: 'Learn from them', art: 'science', hint: 'research' }] }),
    resolve: (c, option) => {
      const { game, p } = c;
      if (option === 0 || !p.researching) {
        const gold = 25 + 15 * c.era;
        p.gold += gold;
        return { text: `The envoy's gifts: +${gold} gold.`, kind: 'good' };
      }
      const bonus = Math.max(8, Math.round(techCost(game, p, p.researching) * 0.2));
      p.research += bonus;
      return { text: `The envoy teaches your scholars: +${bonus} research.`, kind: 'good' };
    },
  },
  {
    id: 'rumours', kind: 'bad', minTurn: FIRST_BAD_TURN, fallback: 1,
    plan: (c) => {
      if (c.p.policies.includes('public_health')) return null;
      const city = pick(c.game, c.cities.filter((x) => x.pop >= 3 && !x.buildings.includes('hospital')));
      if (!city) return null;
      const cost = 20 + 10 * c.era;
      return { weight: 4, city, text: `Sickness is rumoured in ${city.name}.`, options: [{ label: 'Quarantine', art: 'health', hint: `${cost} gold` }, { label: 'Carry on', art: 'skip', hint: 'a risk' }] };
    },
    resolve: (c, option, city) => {
      const { game, p } = c;
      if (!city) return { text: 'The rumours fade.', kind: 'info' };
      if (option === 0) {
        const cost = Math.min(p.gold, 20 + 10 * c.era);
        p.gold -= cost;
        return { text: `${city.name} is quarantined for ${cost} gold. The sickness goes no further.`, kind: 'good' };
      }
      if (game.rng.chance(0.5) && city.pop >= 2) {
        city.pop -= 1;
        city.food = 0;
        return { text: `The sickness spreads in ${city.name}: some of its people die.`, kind: 'bad' };
      }
      return { text: `The rumours in ${city.name} come to nothing.`, kind: 'info' };
    },
  },
];
const CHOICE_BY_ID: Record<string, Choice> = Object.fromEntries(CHOICES.map((x) => [x.id, x]));

function ctxOf(game: Game, p: Player): Ctx {
  return { game, p, cities: game.s.cities.filter((c) => c.owner === p.id), era: eraOf(game, p) };
}

/** Answer the waiting question. */
export function chooseEvent(game: Game, p: Player, option: number): CmdResult {
  const pending = p.pending;
  if (!pending) return { ok: false, error: 'Nothing is waiting for an answer.' };
  if (!Number.isInteger(option) || option < 0 || option >= pending.options.length) return { ok: false, error: 'That is not one of the options.' };
  const def = CHOICE_BY_ID[pending.id];
  p.pending = null;
  if (!def) return { ok: true };
  const city = game.s.cities.find((c) => c.id === pending.city && c.owner === p.id) ?? null;
  const r = def.resolve(ctxOf(game, p), option, city);
  if (!p.ai) game.note([p.id], r.text, r.kind, pending.at);
  return { ok: true };
}

/** A question nobody answered gets its default. */
export function settlePending(game: Game, p: Player): void {
  const pending = p.pending;
  if (!pending) return;
  chooseEvent(game, p, CHOICE_BY_ID[pending.id]?.fallback ?? 0);
}

export const EVENT_BY_ID: Record<string, EventDef> = Object.fromEntries(EVENTS.map((e) => [e.id, e]));

/** How hard fate is on the player: gentler when the opponents are set to easy, a little harsher when they are hard. */
function badLuck(game: Game): number {
  const ai = game.s.players.filter((p) => p.ai);
  if (!ai.length) return 1;
  const hard = ai.filter((p) => p.aiLevel === 'hard').length, easy = ai.filter((p) => p.aiLevel === 'easy').length;
  return easy > hard ? 0.5 : hard > easy ? 1.15 : 1;
}

function announce(game: Game, p: Player, r: ReturnType<Plan['run']>): void {
  // the computers take what comes silently; the player is told, and the world hears of the big ones
  if (!p.ai) game.note([p.id], r.text, r.kind ?? 'info', r.at);
}

export interface EventResult {
  id: string;
  kind: 'good' | 'bad';
  text: string;
}

/** Apply one named event to a civilisation if it can happen (used by tests and the debug tools). */
export function triggerEvent(game: Game, p: Player, id: string): EventResult | null {
  const def = EVENT_BY_ID[id];
  if (!def) return null;
  const cities = game.s.cities.filter((c) => c.owner === p.id);
  if (!cities.length) return null;
  if (def.tech && !has(p, def.tech)) return null;
  const plan = def.plan({ game, p, cities, era: eraOf(game, p) });
  if (!plan) return null;
  const r = plan.run();
  p.lastEvent = game.turn;
  announce(game, p, r);
  return { id, kind: def.kind, text: r.text };
}

/** Once a turn, for each civilisation: maybe something happens. */
export function rollEvent(game: Game, p: Player): EventResult | null {
  const setting = game.s.settings.events ?? 'normal';
  const chance = EVENT_CHANCE[setting];
  if (!chance) return null;
  // one draw per turn keeps the random stream the same whether or not anything can happen
  const roll = game.rng.next();
  if (roll >= chance) return null;
  if (game.turn - p.lastEvent < EVENT_SPACING) return null;
  const cities = game.s.cities.filter((c) => c.owner === p.id);
  if (!cities.length) return null;
  const ctx: Ctx = { game, p, cities, era: eraOf(game, p) };
  const fate = badLuck(game);
  const options: Array<{ weight: number; plan: Plan; def: EventDef }> = [];
  for (const def of EVENTS) {
    if (game.turn < def.minTurn) continue;
    if (def.tech && !has(p, def.tech)) continue;
    // a new player's first cities are left alone to grow
    if (def.kind === 'bad' && (game.turn < FIRST_BAD_TURN || cities.length < 2)) continue;
    const plan = def.plan(ctx);
    if (!plan) continue;
    options.push({ weight: plan.weight * (def.kind === 'bad' ? fate * (setting === 'gentle' ? 0.6 : 1) : 1), plan, def });
  }
  // a few events ask something instead of just happening (never while a question is already waiting)
  if (!p.pending) {
    for (const def of CHOICES) {
      if (game.turn < def.minTurn) continue;
      if (def.kind === 'bad' && (game.turn < FIRST_BAD_TURN || cities.length < 2)) continue;
      const q = def.plan(ctx);
      if (!q) continue;
      const wt = q.weight * (def.kind === 'bad' ? fate * (setting === 'gentle' ? 0.6 : 1) : 1);
      const plan: Plan = {
        weight: wt,
        run: () => {
          if (p.ai) {
            const r = def.resolve(ctx, game.rng.chance(0.5) ? 0 : 1, q.city ?? null);
            return { text: r.text, kind: r.kind };
          }
          p.pending = { id: def.id, text: q.text, city: q.city?.id ?? -1, at: q.city ? { x: q.city.x, y: q.city.y } : undefined, turn: game.turn, options: q.options };
          return { text: q.text, at: q.city ? { x: q.city.x, y: q.city.y } : undefined, kind: 'info' };
        },
      };
      options.push({ weight: plan.weight, plan, def: { id: def.id, kind: def.kind, minTurn: def.minTurn, plan: () => null } });
    }
  }
  const total = options.reduce((s, o) => s + o.weight, 0);
  if (!total) return null;
  let r = game.rng.next() * total;
  let chosen = options[options.length - 1];
  for (const o of options) {
    r -= o.weight;
    if (r <= 0) { chosen = o; break; }
  }
  const result = chosen.plan.run();
  p.lastEvent = game.turn;
  announce(game, p, result);
  return { id: chosen.def.id, kind: chosen.def.kind, text: result.text };
}
