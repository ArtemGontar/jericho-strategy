/**
 * Engine benchmark: time per end-turn on AI-only games, plus the per-player view build, save serialisation and heap.
 *   npx tsx scripts/bench.ts [turns]      (BENCH_JSON=1 prints a machine-readable line per case)
 * Numbers are CPU milliseconds on whatever machine runs it: compare runs on the same machine, not across machines.
 */
import { Game } from '../src/shared/game';
import { buildView } from '../src/shared/view';

const turns = Number(process.argv[2] ?? 80);
const cases: Array<{ size: 'small' | 'medium' | 'large'; n: number; level: 'normal' | 'hard' }> = [
  { size: 'small', n: 2, level: 'hard' },
  { size: 'medium', n: 4, level: 'hard' },
  { size: 'large', n: 8, level: 'hard' },
];
const errors: string[] = [];
console.error = (...a: unknown[]) => { errors.push(a.map(String).join(' ').slice(0, 160)); };

const pct = (xs: number[], p: number) => { const s = xs.slice().sort((a, b) => a - b); return s[Math.min(s.length - 1, Math.floor(s.length * p))] ?? 0; };
const heapMb = () => { (globalThis as { gc?: () => void }).gc?.(); return process.memoryUsage().heapUsed / 1048576; };

console.log(`${'case'.padEnd(22)} ${'turn avg'.padStart(9)} ${'p95'.padStart(7)} ${'max'.padStart(7)} ${'view/pl'.padStart(8)} ${'view KB'.padStart(8)} ${'save ms'.padStart(8)} ${'save KB'.padStart(8)} ${'heap MB'.padStart(8)}`);
for (const c of cases) {
  const game = Game.create({ seed: 4242, size: c.size, players: Array.from({ length: c.n }, (_, i) => ({ name: `AI${i}`, ai: true, aiLevel: c.level })), turnLimit: 400 });
  const times: number[] = [];
  const views: number[] = [];
  let viewKb = 0;
  const h0 = heapMb();
  for (let t = 0; t < turns && !game.s.over; t++) {
    const t0 = performance.now();
    game.processTurn();
    times.push(performance.now() - t0);
    if (t % 10 === 9) {
      const v0 = performance.now();
      let bytes = 0;
      for (let p = 0; p < c.n; p++) bytes += JSON.stringify(buildView(game, p)).length;
      views.push((performance.now() - v0) / c.n);
      viewKb = bytes / c.n / 1024;
    }
  }
  const s0 = performance.now();
  const save = game.serialize();
  const saveMs = performance.now() - s0;
  const avg = times.reduce((a, b) => a + b, 0) / times.length;
  const viewMs = views.reduce((a, b) => a + b, 0) / Math.max(1, views.length);
  const label = `${c.size} ${c.n}p ${c.level} t${game.turn}`;
  console.log(`${label.padEnd(22)} ${avg.toFixed(1).padStart(9)} ${pct(times, 0.95).toFixed(1).padStart(7)} ${Math.max(...times).toFixed(1).padStart(7)} ${viewMs.toFixed(1).padStart(8)} ${viewKb.toFixed(0).padStart(8)} ${saveMs.toFixed(1).padStart(8)} ${(save.length / 1024).toFixed(0).padStart(8)} ${(heapMb() - h0).toFixed(0).padStart(8)}`);
  if (process.env.BENCH_JSON) console.log(JSON.stringify({ label, avg, p95: pct(times, 0.95), max: Math.max(...times), viewMs, viewKb, saveMs, saveKb: save.length / 1024 }));
}
if (errors.length) console.log(`engine errors logged: ${errors.length} (first: ${errors[0]})`);
