/**
 * Determinism fingerprint: hashes the full game state after N turns of seeded AI-only games, so a refactor that is meant to
 * be behaviour-neutral can be checked by comparing the output before and after.
 *   npx tsx scripts/fingerprint.ts [turns] [srcRoot]
 */
import { createHash } from 'node:crypto';

const turns = Number(process.argv[2] ?? 70);
const root = process.argv[3] ?? '../src';
const { Game } = await import(`${root}/shared/game`);
console.error = () => {};
const cases = [
  { seed: 11, size: 'small', n: 2, style: 'continent' },
  { seed: 22, size: 'medium', n: 4, style: 'continent' },
  { seed: 33, size: 'large', n: 8, style: 'continent' },
  { seed: 44, size: 'medium', n: 5, style: 'islands' },
  { seed: 55, size: 'large', n: 6, style: 'continent' },
];
for (const c of cases) {
  const levels = ['easy', 'normal', 'hard'];
  const game = Game.create({ seed: c.seed, size: c.size, mapStyle: c.style, turnLimit: 300, players: Array.from({ length: c.n }, (_, i) => ({ name: `AI${i}`, ai: true, aiLevel: levels[(i + c.seed) % 3] })) });
  for (let t = 0; t < turns && !game.s.over; t++) game.processTurn();
  const h = createHash('sha1').update(game.serialize()).digest('hex').slice(0, 16);
  console.log(`${c.size} ${c.n}p ${c.style} seed ${c.seed} turn ${game.turn}: ${h}`);
}
