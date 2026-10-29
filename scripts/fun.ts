/**
 * Enjoyability audit: AI-only games across player counts, difficulties and map styles; records who wins and how, pacing,
 * competition (city counts, score gaps, lead changes) and option usage. Prints one JSON line per game.
 *   npx tsx scripts/fun.ts [games] [startIndex] [maxTurns] > out.jsonl
 *   npx tsx scripts/fun.ts --sum out.jsonl [more.jsonl ...]
 */
import { readFileSync } from 'node:fs';
import { Game } from '../src/shared/game';
import { BUILDINGS, UNITS, TECHS, POLICIES, FINAL_TECH } from '../src/shared/data';

if (process.argv[2] === '--sum') {
  const rows = process.argv.slice(3).flatMap((f) => readFileSync(f, 'utf8').trim().split('\n').filter((l) => l.startsWith('{')).map((l) => JSON.parse(l)));
  summarise(rows);
  process.exit(0);
}
const games = Number(process.argv[2] ?? 12);
const start = Number(process.argv[3] ?? 0);
const maxTurns = Number(process.argv[4] ?? 200);
const levels = ['normal', 'hard', 'normal', 'hard', 'easy'] as const;
const sizes = ['small', 'medium', 'large'] as const;
console.error = () => {};

for (let g = start; g < start + games; g++) {
  const n = 2 + ((g * 3) % 7);
  const level = levels[g % levels.length];
  const style = g % 3 === 2 ? 'islands' : 'continent';
  const size = n >= 6 ? 'large' : n >= 4 ? sizes[1 + (g % 2)] : sizes[g % 3];
  const seed = 7000 + g * 11;
  const game = Game.create({ seed, size, players: Array.from({ length: n }, (_, i) => ({ name: `AI${i}`, ai: true, aiLevel: level })), turnLimit: maxTurns, mapStyle: style });
  const firstCity: number[] = Array(n).fill(-1);
  let contact = -1, firstWar = -1, leader = -1, leadChanges = 0, enl = -1, attacks = 0;
  const timeline: Array<{ t: number; cities: number[]; score: number[] }> = [];
  const seenUnits = new Set<number>();
  const built: Record<string, number> = {};
  while (!game.s.over && game.turn < maxTurns + 3) {
    game.processTurn();
    for (const e of game.events) if (e.kind === 'attack') attacks++;
    game.events.length = 0;
    const t = game.turn;
    for (const p of game.players) if (firstCity[p.id] < 0 && game.playerCities(p.id).length) firstCity[p.id] = t;
    if (contact < 0 || firstWar < 0) {
      for (let a = 0; a < n; a++) for (let b = a + 1; b < n; b++) {
        const r = game.s.relations[a][b];
        if (contact < 0 && r !== 'unmet') contact = t;
        if (firstWar < 0 && r === 'war') firstWar = t;
      }
    }
    for (const u of game.units) if (!seenUnits.has(u.id)) { seenUnits.add(u.id); built[u.type] = (built[u.type] ?? 0) + 1; }
    if (enl < 0 && game.players.some((p) => p.techs.includes(FINAL_TECH))) enl = t;
    if (t % 10 === 0) {
      game.updateScores();
      const sc = game.players.map((p) => (p.alive ? p.score : 0));
      const best = sc.indexOf(Math.max(...sc));
      if (leader >= 0 && best !== leader) leadChanges++;
      leader = best;
      timeline.push({ t, cities: game.players.map((p) => game.playerCities(p.id).length), score: sc });
    }
  }
  game.updateScores();
  const bld: Record<string, number> = {};
  for (const c of game.cities) for (const b of c.buildings) bld[b] = (bld[b] ?? 0) + 1;
  const techUse: Record<string, number> = {};
  const polUse: Record<string, number> = {};
  for (const p of game.players) {
    for (const x of p.techs) techUse[x] = (techUse[x] ?? 0) + 1;
    for (const x of p.policies) polUse[x] = (polUse[x] ?? 0) + 1;
  }
  const wars = game.s.notes.filter((x) => x.kind === 'war');
  const fc = firstCity.filter((x) => x >= 0);
  console.log(JSON.stringify({
    g, seed, n, size, level, style, end: game.turn, victory: game.s.victory, winner: game.s.winner,
    firstCityAvg: fc.reduce((a, b) => a + b, 0) / Math.max(1, fc.length),
    contact, firstWar, enl, leadChanges, attacks,
    declared: wars.filter((x) => x.text.includes('declared')).length, captured: wars.filter((x) => x.text.includes('captured')).length,
    eliminated: wars.filter((x) => x.text.includes('eliminated')).length,
    finalCities: game.players.map((p) => game.playerCities(p.id).length), finalScore: game.players.map((p) => p.score),
    timeline, built, bld, techUse, polUse,
  }));
}

function summarise(rows: any[]): void {
  const pct = (x: number, d: number) => `${((100 * x) / Math.max(1, d)).toFixed(0)}%`;
  const N = rows.length;
  const count = (rs: any[], k: string) => JSON.stringify(rs.reduce((m: any, r) => ((m[String(r[k])] = (m[String(r[k])] ?? 0) + 1), m), {}));
  const mean = (rs: any[], f: (r: any) => number) => (rs.reduce((a, r) => a + f(r), 0) / Math.max(1, rs.length)).toFixed(1);
  console.log(`games: ${N}; victory: ${count(rows, 'victory')}`);
  for (const lv of ['easy', 'normal', 'hard']) {
    const rs = rows.filter((r) => r.level === lv);
    if (rs.length) console.log(`  ${lv}: n=${rs.length} avg end turn ${mean(rs, (r) => r.end)} victory ${count(rs, 'victory')}`);
  }
  for (const st of ['continent', 'islands']) {
    const rs = rows.filter((r) => r.style === st);
    if (rs.length) console.log(`  ${st}: n=${rs.length} avg end turn ${mean(rs, (r) => r.end)} victory ${count(rs, 'victory')}`);
  }
  const ends = rows.map((r) => r.end).sort((a, b) => a - b);
  console.log(`end turn: min ${ends[0]} median ${ends[N >> 1]} max ${ends[N - 1]}; ended before turn 130: ${pct(ends.filter((x) => x < 130).length, N)}`);
  console.log(`first city avg turn ${mean(rows, (r) => r.firstCityAvg)}, first contact ${mean(rows, (r) => r.contact)}, first war ${mean(rows, (r) => (r.firstWar < 0 ? r.end : r.firstWar))} (${pct(rows.filter((r) => r.firstWar < 0).length, N)} never), Enlightenment reached ${mean(rows, (r) => (r.enl < 0 ? r.end : r.enl))}`);
  console.log(`wars ${mean(rows, (r) => r.declared)}/game, captures ${mean(rows, (r) => r.captured)}/game, eliminations ${mean(rows, (r) => r.eliminated)}/game, attacks ${mean(rows, (r) => r.attacks)}/game, lead changes (10-turn samples) ${mean(rows, (r) => r.leadChanges)}/game`);
  for (const T of [30, 60, 90, 120, 150]) {
    const cs: number[] = [], gaps: number[] = [], cmax: number[] = [];
    for (const r of rows) {
      const f = r.timeline.find((x: any) => x.t === T);
      if (!f) continue;
      const c = f.cities.filter((x: number) => x > 0);
      if (c.length) { cs.push(c.reduce((a: number, b: number) => a + b, 0) / c.length); cmax.push(Math.max(...c)); }
      const s = [...f.score].sort((a: number, b: number) => b - a);
      if (s[1] > 0) gaps.push(s[0] / s[1]);
    }
    const m = (a: number[]) => (a.reduce((x, y) => x + y, 0) / Math.max(1, a.length)).toFixed(2);
    if (cs.length) console.log(`turn ${T}: games still running ${cs.length}, mean cities/civ ${m(cs)}, biggest civ ${m(cmax)} cities, leader/2nd score ${m(gaps)}`);
  }
  const ranks = rows.filter((r) => r.winner != null).map((r) => {
    const f = r.timeline.find((x: any) => x.t === 50);
    return f ? [...f.score].sort((a: number, b: number) => b - a).indexOf(f.score[r.winner]) : -1;
  }).filter((x) => x >= 0) as number[];
  console.log(`winner's score rank at turn 50: ${[0, 1, 2, 3].map((k) => `#${k + 1}:${ranks.filter((x) => x === k).length}`).join(' ')} (of ${ranks.length})`);
  const sum = (k: string) => rows.reduce((m: Record<string, number>, r) => { for (const [a, b] of Object.entries(r[k] as Record<string, number>)) m[a] = (m[a] ?? 0) + b; return m; }, {});
  const show = (title: string, m: Record<string, number>, all: string[]) => console.log(`${title}: ${all.map((x) => `${x}=${m[x] ?? 0}`).join(' ')}`);
  show('units built', sum('built'), Object.keys(UNITS));
  show('buildings standing at end', sum('bld'), Object.keys(BUILDINGS));
  show('policies adopted', sum('polUse'), POLICIES.map((p) => p.id));
  const tu = sum('techUse');
  console.log(`techs never researched: ${TECHS.filter((t) => !tu[t.id]).map((t) => t.id).join(', ') || 'none'}; rarest: ${TECHS.map((t) => [t.id, tu[t.id] ?? 0] as const).sort((a, b) => a[1] - b[1]).slice(0, 6).map((x) => x.join('=')).join(' ')}`);
}
