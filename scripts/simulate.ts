import { Game } from '../src/shared/game';
import { UNITS } from '../src/shared/data';

const games = Number(process.argv[2] ?? 3);
const players = Number(process.argv[3] ?? 4);
const maxTurns = Number(process.argv[4] ?? 200);
const verbose = process.argv.includes('-v');

for (let g = 0; g < games; g++) {
  const t0 = Date.now();
  const game = Game.create({
    seed: 1000 + g,
    players: Array.from({ length: players }, (_, i) => ({ name: `AI${i}`, ai: true, aiLevel: 'normal' })),
    turnLimit: maxTurns,
  });
  let turns = 0;
  while (!game.s.over && turns < maxTurns + 5) {
    game.processTurn();
    turns++;
    if (verbose && turns % 20 === 0) {
      console.log(`  T${game.turn}`, game.players.map((p) => `${p.name}:${game.playerCities(p.id).length}c/${game.playerUnits(p.id).length}u/${p.techs.length}t/${p.score}`).join('  '));
    }
  }
  game.updateScores();
  const wars = game.s.notes.filter((n) => n.kind === 'war');
  console.log(`  war notes: ${wars.length} (declared: ${wars.filter((n) => n.text.includes('declared')).length}, captured: ${wars.filter((n) => n.text.includes('captured')).length}, eliminated: ${wars.filter((n) => n.text.includes('eliminated')).length})`);
  const summary = game.players.map((p) => {
    const cities = game.playerCities(p.id).length;
    const army = game.playerUnits(p.id).filter((u) => !UNITS[u.type].civilian).length;
    return `${p.name}[${p.civ},${p.personality}] alive=${p.alive} cities=${cities} army=${army} techs=${p.techs.length} score=${p.score}`;
  });
  console.log(`game ${g}: turn ${game.turn} over=${game.s.over} winner=${game.s.winner} via ${game.s.victory} (${Date.now() - t0}ms)`);
  for (const s of summary) console.log('   ' + s);
}
