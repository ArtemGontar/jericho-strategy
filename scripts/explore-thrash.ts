// Measures how much an auto-exploring scout doubles back: positions A -> B -> A over consecutive turns,
// and how much of the map it uncovers. Usage: npx tsx scripts/explore-thrash.ts [games] [turns]
import { Game } from '../src/shared/game';

export function runScout(seed: number, turns: number): { backtracks: number; oscillations: number; explored: number; moves: number; auto: boolean } {
  const g = Game.create({ seed, size: 'medium', players: [{ name: 'A', ai: false }, { name: 'B', ai: true }] });
  const settler = g.units.find((u) => u.owner === 0 && u.type === 'settler')!;
  g.exec(0, { t: 'found', unit: settler.id });
  const city = g.playerCities(0)[0];
  const scout = g.addUnit(0, 'scout', city.x, city.y);
  g.exec(0, { t: 'order', unit: scout.id, order: 'explore' });
  // every tile the scout stepped on, in order, across all legs of all turns
  const steps: string[] = [`${scout.x},${scout.y}`];
  const ends: string[] = [`${scout.x},${scout.y}`];
  let moves = 0;
  for (let i = 0; i < turns; i++) {
    g.events.length = 0;
    g.exec(0, { t: 'end' });
    ends.push(`${scout.x},${scout.y}`);
    for (const ev of g.events) {
      if (ev.kind !== 'move' || ev.unit !== scout.id) continue;
      moves++;
      const last = steps[steps.length - 1];
      const from = `${ev.from.x},${ev.from.y}`;
      if (from !== last) steps.push(from);
      for (const q of ev.path) steps.push(`${q.x},${q.y}`);
    }
  }
  // doubling back: stepping onto the tile we were on two steps ago, or a tile visited in the last 12 steps
  let backtracks = 0;
  for (let i = 12; i < steps.length; i++) if (steps.slice(i - 12, i).includes(steps[i])) backtracks++;
  // oscillation: ending a turn where we ended two turns ago, having moved in between (A -> B -> A)
  let oscillations = 0;
  for (let i = 2; i < ends.length; i++) if (ends[i] === ends[i - 2] && ends[i] !== ends[i - 1]) oscillations++;
  const bit = 1;
  const explored = g.tiles.filter((t) => t.explored & bit).length;
  return { backtracks, oscillations, explored, moves, auto: !!g.unit(scout.id)?.auto };
}

if (process.argv[1]?.endsWith('explore-thrash.ts')) {
  const games = Number(process.argv[2] ?? 8);
  const turns = Number(process.argv[3] ?? 40);
  let b = 0, e = 0, m = 0, o = 0;
  for (let s = 0; s < games; s++) {
    const r = runScout(900 + s, turns);
    console.log(`seed ${900 + s}: backtracks ${r.backtracks}, oscillations ${r.oscillations}, moves ${r.moves}, explored ${r.explored}, still auto ${r.auto}`);
    b += r.backtracks; o += r.oscillations; e += r.explored; m += r.moves;
  }
  console.log(`total backtracks ${b}, oscillations ${o}, moves ${m}, avg explored ${(e / games).toFixed(0)}`);
}
