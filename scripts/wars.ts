/**
 * How warlike are the computers? Runs AI-only games and reports wars, captures, eliminations and how games end.
 *   npx tsx scripts/wars.ts [games] [level] [players] [size]
 */
import { Game } from '../src/shared/game';

const games = Number(process.argv[2] ?? 8);
const level = (process.argv[3] ?? 'normal') as 'easy' | 'normal' | 'hard';
const n = Number(process.argv[4] ?? 4);
const size = (process.argv[5] ?? 'medium') as 'small' | 'medium' | 'large';
const tot = { declared: 0, captured: 0, eliminated: 0, science: 0, score: 0, conquest: 0, turns: 0, attacks: 0 };
for (let g = 0; g < games; g++) {
  const game = Game.create({ seed: 9000 + g * 13, size, players: Array.from({ length: n }, (_, i) => ({ name: `AI${i}`, ai: true, aiLevel: level })), turnLimit: 200 });
  let attacks = 0;
  while (!game.s.over && game.turn < 205) {
    game.processTurn();
    for (const e of game.events) if (e.kind === 'attack') attacks++;
    game.events.length = 0;
  }
  const wars = game.s.notes.filter((x) => x.kind === 'war');
  const declared = wars.filter((x) => x.text.includes('declared')).length;
  const captured = wars.filter((x) => x.text.includes('captured')).length;
  const eliminated = wars.filter((x) => x.text.includes('eliminated')).length;
  const v = String(game.s.victory);
  tot.declared += declared; tot.captured += captured; tot.eliminated += eliminated; tot.turns += game.turn; tot.attacks += attacks;
  if (v === 'science') tot.science++; else if (v === 'score') tot.score++; else tot.conquest++;
  console.log(`game ${g}: turn ${game.turn} ${v} wars ${declared} captures ${captured} eliminated ${eliminated} attacks ${attacks}`);
}
console.log(`\n${level} ${n}p ${size}: avg end turn ${(tot.turns / games).toFixed(0)}, wars ${(tot.declared / games).toFixed(1)}/game, captures ${(tot.captured / games).toFixed(1)}/game, eliminations ${(tot.eliminated / games).toFixed(1)}/game, attacks ${(tot.attacks / games).toFixed(0)}/game; science ${tot.science}, score ${tot.score}, conquest/last ${tot.conquest}`);
