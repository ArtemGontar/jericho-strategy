/** Throws random (often invalid) commands at games to prove the engine rejects bad input without corrupting state. */
import { Game, type Command } from '../src/shared/game';
import { Rng } from '../src/shared/rng';
import { BUILDINGS, TECHS, UNITS } from '../src/shared/data';

const games = Number(process.argv[2] ?? 6);
let failures = 0;
const unitIds = Object.keys(UNITS);
const buildingIds = Object.keys(BUILDINGS);

function invariants(g: Game): string | null {
  for (const u of g.units) {
    if (!g.inBounds(u.x, u.y)) return `unit ${u.id} out of bounds`;
    if (u.hp <= 0 || u.hp > 100) return `unit ${u.id} hp ${u.hp}`;
    if (!g.players[u.owner]) return `unit ${u.id} bad owner`;
    if (!g.unitsAt(u.x, u.y).includes(u)) return `unit ${u.id} missing from index`;
  }
  const seen = new Map<string, number>();
  for (const c of g.cities) {
    if (g.tile(c.x, c.y).city !== c.id) return `city ${c.id} tile link broken`;
    if (c.pop < 1) return `city ${c.id} pop ${c.pop}`;
    const k = `${c.x},${c.y}`;
    if (seen.has(k)) return `two cities at ${k}`;
    seen.set(k, c.id);
    for (const w of c.worked) if (g.tiles[w].owner !== c.owner && g.tiles[w].owner !== -1) return `city ${c.id} works foreign tile`;
  }
  for (const t of g.tiles) {
    if (t.city >= 0 && !g.city(t.city)) return `tile points at missing city ${t.city}`;
  }
  for (const p of g.players) if (!Number.isFinite(p.gold) || !Number.isFinite(p.research)) return `NaN economy for ${p.name}`;
  return null;
}

for (let n = 0; n < games; n++) {
  const rng = new Rng(900 + n);
  const humans = 1 + (n % 3);
  const players = Array.from({ length: 3 + (n % 2) }, (_, i) => ({ name: `P${i}`, ai: i >= humans }));
  const g = Game.create({ seed: 300 + n, size: 'medium', players });
  for (const t of g.tiles) t.explored |= (1 << humans) - 1;
  let cmds = 0;
  try {
    for (let turn = 0; turn < 90 && !g.s.over; turn++) {
      for (let k = 0; k < 40; k++) {
        const pid = rng.int(0, humans);
        const mine = g.playerUnits(pid);
        const cities = g.playerCities(pid);
        const pickUnit = () => (mine.length && rng.chance(0.9) ? rng.pick(mine).id : rng.int(0, 400));
        const pickCity = () => (cities.length && rng.chance(0.9) ? rng.pick(cities).id : rng.int(0, 400));
        const pos = () => ({ x: rng.int(-2, g.width + 2), y: rng.int(-2, g.height + 2) });
        const near = () => {
          const u = mine.length ? rng.pick(mine) : null;
          return u ? { x: u.x + rng.int(-3, 4), y: u.y + rng.int(-3, 4) } : pos();
        };
        const cmd = ((): Command => {
          switch (rng.int(0, 16)) {
            case 0: return { t: 'move', unit: pickUnit(), to: near() };
            case 1: return { t: 'move', unit: pickUnit(), to: pos() };
            case 2: return { t: 'attack', unit: pickUnit(), to: near() };
            case 3: return { t: 'found', unit: pickUnit() };
            case 4: return { t: 'work', unit: pickUnit(), what: rng.pick(['farm', 'mine', 'road'] as const) };
            case 5: return { t: 'order', unit: pickUnit(), order: rng.pick(['fortify', 'sleep', 'explore', 'auto', 'cancel', 'skip'] as const) };
            case 6: return { t: 'disband', unit: pickUnit() };
            case 7: return { t: 'queue', city: pickCity(), op: rng.pick(['set', 'add', 'remove', 'up'] as const), kind: rng.pick(['unit', 'building'] as const), id: rng.chance(0.5) ? rng.pick(unitIds) : rng.pick(buildingIds), index: rng.int(-1, 5) };
            case 8: return { t: 'buy', city: pickCity() };
            case 9: return { t: 'focus', city: pickCity(), focus: rng.pick(['balanced', 'food', 'production', 'gold'] as const) };
            case 10: return { t: 'research', tech: rng.pick(TECHS).id };
            case 11: return { t: 'peace', to: rng.int(0, players.length + 1) };
            case 12: return { t: 'war', to: rng.int(0, players.length + 1) };
            case 13: return { t: 'gift', to: rng.int(0, players.length + 1), amount: rng.int(-5, 80) };
            case 14: return { t: 'accept', proposal: rng.int(0, 400), accept: rng.chance(0.5) };
            default: return { t: 'found', unit: pickUnit() };
          }
        })();
        cmds++;
        const res = g.exec(pid, cmd);
        if (typeof res.ok !== 'boolean') throw new Error('exec did not return a result');
        const bad = invariants(g);
        if (bad) throw new Error(`invariant broken after ${JSON.stringify(cmd)}: ${bad}`);
      }
      for (let pid = 0; pid < humans; pid++) g.exec(pid, { t: 'end' });
      g.events.length = 0;
      const bad = invariants(g);
      if (bad) throw new Error(`invariant broken after turn: ${bad}`);
    }
    console.log(`chaos ${n}: ${cmds} commands, turn ${g.turn}, ok`);
  } catch (e) {
    failures++;
    console.log(`chaos ${n} FAILED:`, (e as Error).stack);
  }
}
process.exit(failures ? 1 : 0);
