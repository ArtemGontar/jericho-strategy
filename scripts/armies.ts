/**
 * Army makeup of computer players partway through a game (balance helper).
 *   npx tsx scripts/armies.ts [games] [turn]
 * Prints, per game and then overall, the share of each soldier kind: front line, shooters, cavalry and siege.
 */
import { Game } from '../src/shared/game';
import { UNITS } from '../src/shared/data';

console.error = () => {};
const games = Number(process.argv[2] ?? 6);
const at = Number(process.argv[3] ?? 100);
const total = { melee: 0, ranged: 0, mounted: 0, siege: 0, n: 0 };
const kinds: Record<string, number> = {};
for (let g = 0; g < games; g++) {
  const n = 2 + ((g * 3) % 7);
  const game = Game.create({ seed: 7000 + g * 11, size: n >= 6 ? 'large' : n >= 4 ? 'medium' : 'small', players: Array.from({ length: n }, (_, i) => ({ name: `AI${i}`, ai: true, aiLevel: 'normal' as const })), turnLimit: 200 });
  while (!game.s.over && game.turn < at) game.processTurn();
  const c = { melee: 0, ranged: 0, mounted: 0, siege: 0 };
  for (const u of game.units) {
    const d = UNITS[u.type];
    if (d.civilian || d.scout || d.naval) continue;
    const k = d.siege ? 'siege' : d.mounted ? 'mounted' : d.ranged > 0 ? 'ranged' : 'melee';
    c[k]++;
    kinds[u.type] = (kinds[u.type] ?? 0) + 1;
  }
  const sum = c.melee + c.ranged + c.mounted + c.siege || 1;
  console.log(`game ${g} (${n}p) turn ${game.turn}: ${sum} soldiers  melee ${Math.round((c.melee / sum) * 100)}%  shooters ${Math.round((c.ranged / sum) * 100)}%  cavalry ${Math.round((c.mounted / sum) * 100)}%  siege ${Math.round((c.siege / sum) * 100)}%`);
  for (const k of Object.keys(c) as Array<keyof typeof c>) total[k] += c[k];
  total.n += sum;
}
const pct = (x: number) => `${Math.round((x / (total.n || 1)) * 100)}%`;
console.log(`all: melee ${pct(total.melee)}  shooters ${pct(total.ranged)}  cavalry ${pct(total.mounted)}  siege ${pct(total.siege)}`);
console.log('by kind:', JSON.stringify(kinds));
