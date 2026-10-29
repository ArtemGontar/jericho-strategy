/** Prints culture totals and income over time for a few AI-only games (balance helper). npx tsx scripts/culture.ts [seed...] */
import { Game } from '../src/shared/game';

console.error = () => {};
const seeds = process.argv.slice(2).map(Number);
for (const seed of seeds.length ? seeds : [7000, 7033]) {
  const game = Game.create({ seed, size: 'medium', players: Array.from({ length: 4 }, (_, i) => ({ name: `AI${i}`, ai: true, aiLevel: 'normal' as const })), turnLimit: 200 });
  while (!game.s.over && game.turn < 205) {
    game.processTurn();
    if ([50, 100, 150].includes(game.turn)) console.log(seed, game.turn, game.players.map((p) => `${Math.round(p.culture)}(+${p.income.culture.toFixed(0)}, ${p.policies.length} policies)`).join('  '));
  }
  console.log(seed, 'ended', game.turn, game.s.victory);
}
