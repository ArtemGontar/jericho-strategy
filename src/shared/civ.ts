/**
 * Prosperity and trade: how content a civilisation's people are, and the agreements and sanctions between civilisations.
 *
 * Contentment is one number for the whole empire. Every city you found and every citizen you grow costs some of it;
 * luxuries (each kind counts once, whether you hold it or trade for it), temples, wonders and a few policies give it back,
 * and war takes it away. Below zero the people are restless: cities grow slower and produce less. Far above zero they
 * thrive a little. This is what turns "build everything, settle everywhere" into a set of choices, and what gives trade
 * its worth.
 *
 * Trade agreements pay both sides gold every turn and let each use the other's luxuries and strategic resources.
 * Sanctions cancel the agreement, forbid a new one, and cost the target gold and contentment.
 */
import { BUILDINGS, LUXURIES, RESOURCES } from './data';
import type { ResourceId } from './types';
import type { Game } from './game';

export const MAX_DEALS = 4;

/** Trade agreements a civilisation may hold: four, one more for Navigation, and one for each of up to two embassies. */
export function dealSlots(game: Game, pid: number): number {
  const p = game.players[pid];
  let embassies = 0;
  for (const c of game.s.cities) if (c.owner === pid && c.buildings.includes('embassy')) embassies++;
  return MAX_DEALS + (p.techs.includes('navigation') ? 1 : 0) + Math.min(2, embassies) + (p.policies.includes('open_society') ? 1 : 0);
}
/** sanctions lapse after this many turns unless they are imposed again */
export const SANCTION_TURNS = 30;
/** contentment at which the people thrive */
export const THRIVING = 12;

export interface ContentPart {
  label: string;
  value: number;
}
export interface Contentment {
  value: number;
  parts: ContentPart[];
}

/** Everyone this civilisation has a trade agreement with. */
export function dealPartners(game: Game, pid: number): number[] {
  const out: number[] = [];
  for (const d of game.s.deals) {
    if (d.a === pid) out.push(d.b);
    else if (d.b === pid) out.push(d.a);
  }
  return out;
}

/** How many civilisations have sanctions on this one. */
export function sanctionersOf(game: Game, pid: number): number[] {
  return game.s.sanctions.filter((s) => s.on === pid && game.players[s.by].alive).map((s) => s.by);
}

/** Luxury kinds on tiles inside this civilisation's borders. */
export function ownLuxuries(game: Game, pid: number): ResourceId[] {
  const seen = new Set<ResourceId>();
  for (const t of game.s.tiles) if (t.owner === pid && t.resource && RESOURCES[t.resource].luxury) seen.add(t.resource);
  return LUXURIES.filter((r) => seen.has(r));
}

/** Luxuries a civilisation enjoys: its own, and the kinds its trading partners hold that it lacks. */
export function luxuryAccess(game: Game, pid: number): { own: ResourceId[]; traded: ResourceId[] } {
  const own = ownLuxuries(game, pid);
  const traded = new Set<ResourceId>();
  for (const o of dealPartners(game, pid)) {
    if (!game.players[o].alive) continue;
    for (const r of ownLuxuries(game, o)) if (!own.includes(r)) traded.add(r);
  }
  return { own, traded: LUXURIES.filter((r) => traded.has(r)) };
}

/** Gold a trade agreement pays each side every turn: 2 for two small civilisations, up to 5 for two large ones. */
export function dealGold(game: Game, a: number, b: number): number {
  const ca = game.s.cities.filter((c) => c.owner === a).length;
  const cb = game.s.cities.filter((c) => c.owner === b).length;
  return Math.min(5, 1 + Math.ceil(Math.min(ca, cb) / 2));
}

export function dealIncome(game: Game, pid: number): number {
  let g = 0;
  const open = game.players[pid].policies.includes('open_society') ? 1 : 0;
  for (const o of dealPartners(game, pid)) if (game.players[o].alive) g += dealGold(game, pid, o) + open;
  return g;
}

export function contentment(game: Game, pid: number): Contentment {
  const p = game.players[pid];
  const parts: ContentPart[] = [];
  const add = (label: string, value: number) => { if (Math.abs(value) >= 0.05) parts.push({ label, value: Math.round(value * 10) / 10 }); };
  const mine = game.s.cities.filter((c) => c.owner === pid);
  const pop = mine.reduce((s, c) => s + c.pop, 0);
  add('Base', 8);
  add(`${mine.length} ${mine.length === 1 ? 'city' : 'cities'}`, -1.3 * Math.max(0, mine.length - 1));
  // medicine: healthier cities are easier to keep calm
  add(`${pop} ${pop === 1 ? 'citizen' : 'citizens'}`, -(p.techs.includes('medicine') ? 0.24 : 0.3) * pop);
  const lux = luxuryAccess(game, pid);
  add(`Luxuries (${lux.own.length + lux.traded.length})`, 3 * (lux.own.length + lux.traded.length));
  let civic = 0;
  let wonders = 0;
  let civicCount = 0;
  for (const c of mine) {
    for (const b of c.buildings) {
      if (BUILDINGS[b].wonder) wonders++;
      else if (BUILDINGS[b].content) {
        civic += BUILDINGS[b].content!;
        civicCount++;
      }
    }
  }
  add(`Temples, shrines and the like (${civicCount})`, civic);
  add(`Wonders (${wonders})`, wonders);
  if (p.policies.includes('patronage')) add('Patronage of the Arts', 2);
  if (p.policies.includes('hearth')) add('Hearth and Home', 2);
  if (p.policies.includes('bread_circuses')) add('Bread and Circuses', 3);
  if (p.policies.includes('public_health')) add('Public Health', 2);
  if (p.policies.includes('industrial_policy')) add('Industrial Policy', -2);
  if (p.policies.includes('state_religion')) add('State Religion', mine.reduce((n, c) => n + c.buildings.filter((b) => b === 'temple' || b === 'shrine' || b === 'cathedral').length, 0));
  add('War weariness', -Math.min(8, p.weariness));
  add('Sanctions', -Math.min(4, sanctionersOf(game, pid).length));
  const value = Math.round(parts.reduce((s, x) => s + x.value, 0) * 10) / 10;
  return { value, parts };
}

/** Restless people produce less. The people of a thriving civilisation produce a little more. */
export function outputFactor(content: number): number {
  if (content < 0) return Math.max(0.55, 1 + 0.05 * content);
  return content >= THRIVING ? 1.05 : 1;
}

/** Restless cities grow slowly; content ones a little faster. */
export function growthFactor(content: number): number {
  if (content < 0) return Math.max(0.4, 1 + 0.07 * content);
  return content >= THRIVING ? 1.1 : 1;
}
