import { Game } from '../src/shared/game';
import { buildView } from '../src/shared/view';

const n = Number(process.argv[2] ?? 20);
let worst = 0;
let failures = 0;
for (let g = 0; g < n; g++) {
  const players = 2 + (g % 7);
  const size = (['small', 'medium', 'large'] as const)[g % 3];
  const seed = 5000 + g * 31;
  try {
    const game = Game.create({
      seed, size, players: Array.from({ length: players }, (_, i) => ({ name: `AI${i}`, ai: true, aiLevel: (['easy', 'normal', 'hard'] as const)[i % 3] })),
      turnLimit: 160, speed: g % 4 === 0 ? 'quick' : 'standard',
    });
    const t0 = Date.now();
    let turns = 0;
    while (!game.s.over && turns < 180) {
      const t1 = Date.now();
      game.processTurn();
      game.events.length = 0;
      worst = Math.max(worst, Date.now() - t1);
      turns++;
      for (let p = 0; p < players; p++) buildView(game, p); // views must never throw
    }
    // invariants
    for (const u of game.units) {
      if (!game.tile(u.x, u.y)) throw new Error('unit out of map');
      if (u.hp <= 0) throw new Error('dead unit lingering');
    }
    const seen = new Set<string>();
    for (const u of game.units) {
      const k = `${u.x},${u.y},${u.owner},${u.type === 'settler' || u.type === 'worker' ? 'c' : 'm'}`;
      void seen.add(k);
    }
    console.log(`game ${g} ${players}p ${size}: turn ${game.turn} winner=${game.s.winner} via ${game.s.victory} ${Date.now() - t0}ms cities=${game.cities.length} units=${game.units.length}`);
  } catch (e) {
    failures++;
    console.log(`game ${g} FAILED (${players}p ${size} seed ${seed}):`, (e as Error).stack);
  }
}
console.log('worst turn ms', worst, 'failures', failures);
process.exit(failures ? 1 : 0);
