import { tilesWithin } from './hex';
import { BUILDINGS, TERRAIN, UNITS, UNIT_HP } from './data';
import { hasBuilding, refreshPlayer } from './economy';
import type { City, CmdResult, Unit } from './types';
import type { Game } from './game';

const BASE_DAMAGE = 30;

function hpFactor(hp: number, max: number): number {
  return 0.7 + 0.3 * (hp / max);
}

export function damageFor(att: number, def: number, roll = 1): number {
  const dmg = BASE_DAMAGE * Math.pow(1.04, att - def) * roll;
  return Math.max(3, Math.min(100, dmg));
}

export function cityStrength(game: Game, city: City): number {
  const owner = game.players[city.owner];
  let s = 8 + city.pop * 0.8 + owner.techs.length * 0.5;
  if (hasBuilding(city, 'walls')) s *= 1.4;
  if (game.tile(city.x, city.y).terrain === 'hills') s *= 1.25;
  let best = 0;
  for (const u of game.unitsAt(city.x, city.y)) {
    if (u.owner !== city.owner || UNITS[u.type].civilian) continue;
    best = Math.max(best, defenceStrength(game, u, false));
  }
  s += best * 0.35;
  return s * hpFactor(city.hp, game.cityMaxHp(city));
}

/** Strength of a unit when it is the one being hit. */
export function defenceStrength(game: Game, u: Unit, attackerMounted: boolean): number {
  const d = UNITS[u.type];
  let m = 1 + 0.15 * u.level;
  const t = game.tile(u.x, u.y);
  m += TERRAIN[t.terrain].defense / 100;
  if (u.fortified) m += 0.25;
  if (attackerMounted && d.antiMounted) m += d.antiMounted / 100;
  const base = d.civilian ? 1 : Math.max(d.strength, 1);
  return base * m * hpFactor(u.hp, UNIT_HP);
}

/** Strength of a unit when it attacks. */
export function attackStrength(game: Game, u: Unit, ranged: boolean, defMounted: boolean, vsCity: boolean): number {
  const d = UNITS[u.type];
  let m = 1 + 0.15 * u.level;
  if (d.antiMounted && defMounted) m += d.antiMounted / 100;
  if (d.siege && vsCity) m += 0.6;
  const base = ranged ? d.ranged : d.strength;
  return base * m * hpFactor(u.hp, UNIT_HP);
}

export function bombardTargets(game: Game, city: City): Unit[] {
  const out: Unit[] = [];
  for (const p of tilesWithin(city.x, city.y, 2, game.width, game.height)) {
    for (const u of game.unitsAt(p.x, p.y)) {
      if (u.owner !== city.owner && game.atWar(city.owner, u.owner)) out.push(u);
    }
  }
  out.sort((a, b) => a.hp - b.hp);
  return out;
}

export interface AttackPreview {
  ok: boolean;
  error?: string;
  /** expected damage to the defender / to the attacker */
  dmgDef: number;
  dmgAtt: number;
  defHp: number;
  attHp: number;
  target: string;
  /** true when the defending side is a city */
  city: boolean;
  capture?: boolean;
}

function bestDefender(game: Game, att: Unit, x: number, y: number): Unit | null {
  let best: Unit | null = null;
  let bestS = -1;
  const mounted = !!UNITS[att.type].mounted;
  for (const o of game.unitsAt(x, y)) {
    if (o.owner === att.owner || !game.atWar(att.owner, o.owner)) continue;
    if (UNITS[o.type].civilian) continue;
    const s = defenceStrength(game, o, mounted);
    if (s > bestS) {
      bestS = s;
      best = o;
    }
  }
  return best;
}

export function previewAttack(game: Game, att: Unit, x: number, y: number): AttackPreview {
  const fail = (error: string): AttackPreview => ({ ok: false, error, dmgDef: 0, dmgAtt: 0, defHp: 0, attHp: att.hp, target: '', city: false });
  const d = UNITS[att.type];
  if (d.civilian) return fail('Civilians cannot attack.');
  if (att.attacked) return fail('Already attacked this turn.');
  if (att.mp <= 0) return fail('No movement left.');
  const ranged = d.ranged > 0;
  const dist = game.dist(att.x, att.y, x, y);
  if (ranged ? dist < 1 || dist > d.range : dist !== 1) return fail('Out of range.');
  const city = game.cityAt(x, y);
  const units = game.unitsAt(x, y).filter((o) => o.owner !== att.owner);
  const foeOwner = city && city.owner !== att.owner ? city.owner : units[0]?.owner;
  if (foeOwner === undefined || foeOwner === att.owner) return fail('Nothing to attack.');
  if (!game.atWar(att.owner, foeOwner)) return fail(game.relation(att.owner, foeOwner) === 'peace' ? 'You are at peace.' : 'You have not met them.');
  if (!ranged && !TERRAIN[game.tile(x, y).terrain].passable) return fail('Impassable.');
  if (city && city.owner !== att.owner) {
    if (city.hp <= 0 && !ranged) {
      return { ok: true, dmgDef: 0, dmgAtt: 0, defHp: 0, attHp: att.hp, target: city.name, city: true, capture: true };
    }
    const A = attackStrength(game, att, ranged, false, true);
    const D = cityStrength(game, city);
    return {
      ok: true,
      dmgDef: Math.round(damageFor(A, D)),
      dmgAtt: ranged ? 0 : Math.round(damageFor(D, A) * 0.6),
      defHp: city.hp,
      attHp: att.hp,
      target: city.name,
      city: true,
    };
  }
  const def = bestDefender(game, att, x, y);
  if (!def) {
    const civ = units.find((o) => game.atWar(att.owner, o.owner));
    if (!civ) return fail('Nothing to attack.');
    return { ok: true, dmgDef: civ.hp, dmgAtt: 0, defHp: civ.hp, attHp: att.hp, target: UNITS[civ.type].name, city: false, capture: !ranged };
  }
  const A = attackStrength(game, att, ranged, !!UNITS[def.type].mounted, false);
  const D = defenceStrength(game, def, !!d.mounted);
  return {
    ok: true,
    dmgDef: Math.round(Math.min(def.hp, damageFor(A, D))),
    dmgAtt: ranged ? 0 : Math.round(Math.min(att.hp, damageFor(D, A))),
    defHp: def.hp,
    attHp: att.hp,
    target: UNITS[def.type].name,
    city: false,
  };
}

export function resolveAttack(game: Game, att: Unit, x: number, y: number): CmdResult {
  const pv = previewAttack(game, att, x, y);
  if (!pv.ok) return { ok: false, error: pv.error };
  const d = UNITS[att.type];
  const ranged = d.ranged > 0;
  const rng = game.rng;
  const roll = () => 0.9 + 0.2 * rng.next();
  const from = { x: att.x, y: att.y };
  const to = { x, y };
  const mask = () => game.tile(from.x, from.y).visible | game.tile(x, y).visible;
  const city = game.cityAt(x, y);
  let dmgDef = 0;
  let dmgAtt = 0;
  let killedDef = false;
  let killedAtt = false;
  let defOwner = -1;
  let defType: Unit['type'] | 'city' = 'warrior';
  const attackerOwner = att.owner;

  if (city && city.owner !== att.owner) {
    defOwner = city.owner;
    defType = 'city';
    if (pv.capture) {
      doCapture(game, att, city);
      return { ok: true };
    }
    const A = attackStrength(game, att, ranged, false, true);
    const D = cityStrength(game, city);
    dmgDef = Math.round(damageFor(A, D, roll()));
    dmgAtt = ranged ? 0 : Math.round(damageFor(D, A, roll()) * 0.6);
    city.hp -= dmgDef;
    city.lastAttacked = game.turn;
    if (ranged && city.hp < 1) city.hp = 1;
    if (city.hp < 0) city.hp = 0;
    att.hp -= dmgAtt;
    killedAtt = att.hp <= 0;
    game.players[defOwner].lastDefeat = game.turn;
    game.note([defOwner], `${city.name} is under attack!`, 'war', { x: city.x, y: city.y });
    if (!ranged && city.hp <= 0 && !killedAtt) {
      finishAttack(game, att, from, to, ranged, dmgDef, dmgAtt, defOwner, defType, false, false, mask());
      doCapture(game, att, city);
      return { ok: true };
    }
  } else {
    const def = bestDefender(game, att, x, y);
    if (!def) {
      // only civilians: capture or destroy
      const victims = game.unitsAt(x, y).filter((o) => o.owner !== att.owner && game.atWar(att.owner, o.owner)).slice();
      defOwner = victims[0].owner;
      defType = victims[0].type;
      for (const v of victims) {
        if (ranged) {
          game.events.push({ kind: 'death', type: v.type, owner: v.owner, at: { x, y }, mask: game.tile(x, y).visible });
          game.removeUnit(v);
        } else {
          v.owner = attackerOwner;
          v.order = null;
          v.mp = 0;
        }
      }
      game.note([defOwner], `Your civilian units were ${ranged ? 'killed' : 'captured'}!`, 'bad', { x, y });
      killedDef = true;
      dmgDef = 100;
      if (!ranged) game.placeUnit(att, x, y);
      finishAttack(game, att, from, to, ranged, dmgDef, 0, defOwner, defType, killedDef, false, mask());
      return { ok: true };
    }
    defOwner = def.owner;
    defType = def.type;
    const A = attackStrength(game, att, ranged, !!UNITS[def.type].mounted, false);
    const D = defenceStrength(game, def, !!d.mounted);
    dmgDef = Math.min(def.hp, Math.round(damageFor(A, D, roll())));
    dmgAtt = ranged ? 0 : Math.min(att.hp, Math.round(damageFor(D, A, roll())));
    def.hp -= dmgDef;
    att.hp -= dmgAtt;
    killedDef = def.hp <= 0;
    killedAtt = att.hp <= 0;
    game.players[defOwner].lastDefeat = game.turn;
    if (!killedDef) game.note([defOwner], `Your ${UNITS[def.type].name} was attacked!`, 'war', { x, y });
    if (killedDef) {
      game.events.push({ kind: 'death', type: def.type, owner: def.owner, at: { x, y }, mask: game.tile(x, y).visible });
      game.note([defOwner], `Your ${UNITS[def.type].name} was killed!`, 'bad', { x, y });
      game.removeUnit(def);
    }
  }
  if (killedAtt) {
    game.events.push({ kind: 'death', type: att.type, owner: att.owner, at: from, mask: game.tile(from.x, from.y).visible });
    game.removeUnit(att);
  }
  finishAttack(game, att, from, to, ranged, dmgDef, dmgAtt, defOwner, defType, killedDef, killedAtt, mask());
  // melee winners advance into an emptied tile
  if (!killedAtt && killedDef && !ranged && !city) {
    const rest = game.unitsAt(x, y);
    const enemiesLeft = rest.some((o) => o.owner !== att.owner && game.atWar(att.owner, o.owner));
    if (!enemiesLeft) {
      // leftover enemy civilians are captured
      for (const v of rest.slice()) if (v.owner !== att.owner) { v.owner = att.owner; v.order = null; v.mp = 0; }
      game.placeUnit(att, x, y);
    }
  }
  return { ok: true };
}

function finishAttack(
  game: Game, att: Unit, from: { x: number; y: number }, to: { x: number; y: number }, ranged: boolean, dmgDef: number,
  dmgAtt: number, defOwner: number, defType: Unit['type'] | 'city', killedDef: boolean, killedAtt: boolean, mask: number,
): void {
  att.attacked = true;
  att.mp = 0;
  att.active = true;
  att.fortified = false;
  game.events.push({
    kind: 'attack', attacker: att.id, owner: att.owner, type: att.type, from, to, ranged, dmgDef, dmgAtt, defOwner, defType,
    killedDef, killedAtt, mask: mask | (1 << att.owner),
  });
}

function doCapture(game: Game, att: Unit, city: City): void {
  const from = { x: att.x, y: att.y };
  const previous = city.owner;
  captureCity(game, city, att.owner);
  game.placeUnit(att, city.x, city.y);
  att.mp = 0;
  att.attacked = true;
  att.active = true;
  game.events.push({
    kind: 'capture', city: city.id, by: att.owner, from: previous, at: { x: city.x, y: city.y },
    mask: game.tile(city.x, city.y).visible | game.tile(from.x, from.y).visible | (1 << att.owner) | (1 << previous),
  });
}

export function captureCity(game: Game, city: City, newOwner: number): void {
  const old = city.owner;
  // defenders inside die, civilians are taken
  for (const u of game.unitsAt(city.x, city.y).slice()) {
    if (u.owner === old) {
      if (UNITS[u.type].civilian) {
        u.owner = newOwner;
        u.order = null;
      } else {
        game.events.push({ kind: 'death', type: u.type, owner: u.owner, at: { x: city.x, y: city.y }, mask: 0xffff });
        game.removeUnit(u);
      }
    }
  }
  const wasCapital = city.capital;
  city.owner = newOwner;
  city.hp = Math.floor(game.cityMaxHp(city) * 0.3);
  city.pop = Math.max(1, city.pop - 1);
  city.food = 0;
  city.prod = 0;
  city.queue = [];
  city.prodKind = null;
  city.capital = false;
  city.focus = 'balanced';
  city.lastAttacked = game.turn;
  city.buildings = city.buildings.filter((b) => BUILDINGS[b].wonder || game.rng.next() > 0.35);
  for (const b of city.buildings) if (BUILDINGS[b].wonder) game.s.wonders[b] = newOwner;
  // territory
  const keep = game.cities.filter((c) => c.owner === old && c.id !== city.id);
  for (const p of tilesWithin(city.x, city.y, city.radius, game.width, game.height)) {
    const t = game.tile(p.x, p.y);
    if (t.owner !== old && t.owner !== -1) continue;
    const protectedByOld = keep.some((c) => game.dist(c.x, c.y, p.x, p.y) <= 1);
    if (!protectedByOld) t.owner = newOwner;
  }
  game.tile(city.x, city.y).owner = newOwner;
  if (wasCapital) {
    const next = keep.sort((a, b) => b.pop - a.pop)[0];
    if (next) next.capital = true;
  }
  if (!game.cities.some((c) => c.owner === newOwner && c.capital)) city.capital = true;
  game.players[old].lastDefeat = game.turn;
  game.note([], `${game.playerName(newOwner)} captured ${city.name} from ${game.playerName(old)}!`, 'war', { x: city.x, y: city.y });
  refreshPlayer(game, old);
  refreshPlayer(game, newOwner);
}
