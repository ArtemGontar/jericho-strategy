/**
 * Wide-net engine audit: many AI-only games across player counts, map sizes and difficulties, checking invariants.
 *   npx tsx scripts/audit.ts [games] [maxTurns]
 */
import { Game } from '../src/shared/game';
import { UNITS } from '../src/shared/data';

const games = Number(process.argv[2] ?? 24);
const maxTurns = Number(process.argv[3] ?? 160);
const sizes = ['small', 'medium', 'large'] as const;
const levels = ['easy', 'normal', 'hard'] as const;
const problems: string[] = [];
const stats = { science: 0, score: 0, conquest: 0, other: 0, turns: [] as number[], maxTurnMs: 0, captures: 0 };
const errors: string[] = [];
const origErr = console.error;
console.error = (...a: unknown[]) => { errors.push(a.map(String).join(' ').slice(0, 160)); };

for (let g = 0; g < games; g++) {
  const n = 2 + (g % 7);
  const size = n >= 6 ? 'large' : n >= 4 ? sizes[1 + (g % 2)] : sizes[g % 3];
  const level = levels[g % 3];
  const seed = 5000 + g * 7;
  const label = `seed ${seed} ${n}p ${size} ${level}`;
  let game: Game;
  try {
    game = Game.create({ seed, size, players: Array.from({ length: n }, (_, i) => ({ name: `AI${i}`, ai: true, aiLevel: level })), turnLimit: maxTurns, mapStyle: process.env.AUDIT_ISLANDS ? 'islands' : 'continent' });
  } catch (e) {
    problems.push(`${label}: map generation failed: ${(e as Error).message}`);
    continue;
  }
  const idle = new Map<number, number>();
  let turns = 0;
  while (!game.s.over && turns < maxTurns + 2) {
    const t0 = Date.now();
    try { game.processTurn(); } catch (e) { problems.push(`${label}: processTurn threw at turn ${game.turn}: ${(e as Error).message}`); break; }
    stats.maxTurnMs = Math.max(stats.maxTurnMs, Date.now() - t0);
    turns++;
    if (game.turn === 30) {
      for (const p of game.players) if (p.alive && game.playerCities(p.id).length === 0) problems.push(`${label}: ${p.name} still has no city at turn 30`);
    }
    // units that stand still under a goto order for 12+ turns are stuck
    for (const u of game.units) {
      const key = u.id * 1000 + u.x * 40 + u.y;
      const k = u.order?.kind === 'goto' ? (idle.get(key) ?? 0) + 1 : 0;
      if (u.order?.kind === 'goto') idle.set(key, k);
      if (k === 12) problems.push(`${label}: unit ${u.type}#${u.id} of ${game.players[u.owner].name} stuck at ${u.x},${u.y} under a goto order`);
    }
    if (game.turn % 10 === 0) {
      for (const u of game.units) {
        if (!game.players[u.owner].alive) problems.push(`${label}: unit of a dead player remains`);
        if (game.tile(u.x, u.y).city >= 0 && game.cities.find((c) => c.id === game.tile(u.x, u.y).city)!.owner !== u.owner && !UNITS[u.type].civilian) problems.push(`${label}: unit inside an enemy city`);
      }
    }
  }
  stats.turns.push(game.turn);
  const v = game.s.victory as string | null;
  if (v === 'science') stats.science++;
  else if (v === 'score') stats.score++;
  else if (v === 'conquest' || v === 'last') stats.conquest++;
  else stats.other++;
  stats.captures += game.s.notes.filter((x) => x.kind === 'war' && x.text.includes('captured')).length;
  console.log(`${label}: turn ${game.turn} ${v ?? 'unfinished'}`);
}
console.error = origErr;
const avg = stats.turns.reduce((a, b) => a + b, 0) / Math.max(1, stats.turns.length);
console.log(`\nvictories: science ${stats.science}, score ${stats.score}, conquest ${stats.conquest}, other ${stats.other}; average end turn ${avg.toFixed(0)}; city captures ${stats.captures}; slowest turn ${stats.maxTurnMs}ms`);
console.log(problems.length ? `\n${problems.length} problem(s):\n` + [...new Set(problems)].join('\n') : '\nno invariant violations');
if (errors.length) console.log(`\n${errors.length} guarded errors logged:\n` + [...new Set(errors)].slice(0, 8).join('\n'));
