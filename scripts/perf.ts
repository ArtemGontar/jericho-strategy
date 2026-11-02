// @ts-nocheck
/**
 * Browser performance harness: loads a realistic mid-game save and measures what the page costs while the player does
 * nothing, while panning, and while ending turns. Needs `npm run build` and Playwright with Chromium.
 *
 *   npx tsx scripts/perf.ts                 both renderers, large map, 8 players, turn 60
 *   PERF_ONLY=flat|gl  PERF_SIZE=medium|large  PERF_TURNS=60  PERF_IDLE=12  npx tsx scripts/perf.ts
 *
 * The CPU numbers are main-thread seconds per wall-clock second from Chrome's own counters (script, layout, style,
 * other tasks). With a software rasteriser the GPU work happens elsewhere and is not counted, so the 3D numbers show
 * the JavaScript and driver-call cost only. Compare runs on the same machine; do not compare machines.
 */
import { createRequire } from 'node:module';
import { Game } from '../src/shared/game';
import { startServer } from '../src/server/server';

const require = createRequire(import.meta.url);
const pw = (() => {
  for (const c of [process.env.PLAYWRIGHT_MODULE, 'playwright', '/opt/node22/lib/node_modules/playwright'].filter(Boolean)) {
    try { return require(c); } catch { /* next */ }
  }
  throw new Error('Playwright is not installed.');
})();

const size = (process.env.PERF_SIZE ?? 'large') as 'small' | 'medium' | 'large';
const players = size === 'large' ? 8 : size === 'medium' ? 4 : 2;
const turns = Number(process.env.PERF_TURNS ?? 60);
const idleSec = Number(process.env.PERF_IDLE ?? 12);
const only = process.env.PERF_ONLY;

console.error = () => {};
const game = Game.create({ seed: 4242, size, players: Array.from({ length: players }, (_, i) => ({ name: i === 0 ? 'You' : `AI${i}`, ai: true, aiLevel: 'normal' })), turnLimit: 400 });
for (let t = 0; t < turns && !game.s.over; t++) game.processTurn();
game.players[0].ai = false;
const record = JSON.stringify({ format: 2, savedAt: Date.now(), humans: [0], state: JSON.parse(game.serialize()) });
console.log(`save: ${size}, ${players} players, turn ${game.turn}, ${(record.length / 1024).toFixed(0)} KB, ${game.cities.length} cities, ${game.units.length} units`);

const server = await startServer({ port: 0, ephemeral: true, dist: process.env.DIST ?? 'dist' });
const base = `http://localhost:${server.port}`;
const browser = await pw.chromium.launch({ args: ['--enable-precise-memory-info', '--js-flags=--expose-gc'] });

async function metrics(cdp) {
  const { metrics: m } = await cdp.send('Performance.getMetrics');
  const o = {};
  for (const x of m) o[x.name] = x.value;
  return o;
}
const fmt = (n, d = 1) => n.toFixed(d);

async function scenario(renderer) {
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, hasTouch: true, isMobile: true });
  await ctx.addInitScript(([rec, r]) => {
    localStorage.setItem('jericho.muted', '1');
    localStorage.setItem('jericho.save.auto', rec);
    localStorage.setItem('jericho.guide.v2', 'off');
    if (r === 'flat') localStorage.setItem('jericho.flat', '1');
    else localStorage.setItem('jericho.gl', '1');
  }, [record, renderer]);
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  const cdp = await ctx.newCDPSession(page);
  await cdp.send('Performance.enable');
  await page.goto(base);
  await page.click('button:has-text("Continue")');
  await page.waitForSelector('canvas.map');
  await page.waitForFunction(() => window.__jericho?.screen?.view);
  await page.waitForTimeout(2500); // let the renderer load and the first frames settle
  await page.evaluate(() => {
    const s = window.__jericho.screen;
    s.closeModal?.();
    // count real map draws
    window.__draws = 0;
    const r = s.renderer;
    const d = r.draw.bind(r);
    r.draw = (st) => { window.__draws++; return d(st); };
    window.__minimap = 0;
    const mm = s.drawMinimap.bind(s);
    s.drawMinimap = () => { window.__minimap++; return mm(); };
  });
  const row = { renderer };
  row.is3d = await page.evaluate(() => !!window.__jericho.screen.glCanvas);

  // 1. idle
  await cdp.send('HeapProfiler.collectGarbage');
  const m0 = await metrics(cdp);
  const t0 = Date.now();
  await page.evaluate(() => { window.__draws = 0; window.__minimap = 0; });
  await page.waitForTimeout(idleSec * 1000);
  const m1 = await metrics(cdp);
  const dt = (Date.now() - t0) / 1000;
  const draws = await page.evaluate(() => [window.__draws, window.__minimap]);
  row.idle = {
    cpuPct: ((m1.TaskDuration - m0.TaskDuration) / dt) * 100,
    scriptPct: ((m1.ScriptDuration - m0.ScriptDuration) / dt) * 100,
    layoutPct: (((m1.LayoutDuration - m0.LayoutDuration) + (m1.RecalcStyleDuration - m0.RecalcStyleDuration)) / dt) * 100,
    drawsPerSec: draws[0] / dt,
    minimapPerSec: draws[1] / dt,
  };

  // 2. pan for 4 seconds
  const m2 = await metrics(cdp);
  const p0 = Date.now();
  await page.evaluate(() => { window.__draws = 0; });
  for (let i = 0; i < 40; i++) {
    await page.evaluate((i) => { const s = window.__jericho.screen; s.cam.x += (i % 2 ? 40 : -40); s.cam.clamp(); s.dirty = true; }, i);
    await page.waitForTimeout(100);
  }
  const m3 = await metrics(cdp);
  const pdt = (Date.now() - p0) / 1000;
  row.pan = { cpuPct: ((m3.TaskDuration - m2.TaskDuration) / pdt) * 100, drawsPerSec: (await page.evaluate(() => window.__draws)) / pdt };

  // 3. end five turns
  const times = [];
  for (let i = 0; i < 5; i++) {
    const t = await page.evaluate(async () => { const s = window.__jericho.screen; const a = performance.now(); await s.endTurn(); return performance.now() - a; });
    times.push(t);
    await page.waitForTimeout(300);
  }
  row.endTurnMs = times.reduce((a, b) => a + b, 0) / times.length;
  row.endTurnMax = Math.max(...times);

  // 4. memory after all that
  await cdp.send('HeapProfiler.collectGarbage');
  const m4 = await metrics(cdp);
  row.heapMb = m4.JSHeapUsedSize / 1048576;
  row.nodes = m4.Nodes;
  row.listeners = m4.JSEventListeners;
  row.errors = errors.length;
  await ctx.close();
  return row;
}

try {
  const rows = [];
  for (const r of ['flat', 'gl']) {
    if (only && only !== r) continue;
    rows.push(await scenario(r));
  }
  console.log('');
  console.log('renderer  idle CPU%  script%  layout%  draws/s  minimap/s | pan CPU%  draws/s | end-turn avg / max ms | heap MB  DOM nodes  listeners');
  for (const r of rows) {
    console.log(`${(r.renderer + (r.is3d ? '' : '*')).padEnd(9)} ${fmt(r.idle.cpuPct).padStart(9)} ${fmt(r.idle.scriptPct).padStart(8)} ${fmt(r.idle.layoutPct).padStart(8)} ${fmt(r.idle.drawsPerSec).padStart(8)} ${fmt(r.idle.minimapPerSec).padStart(10)} | ${fmt(r.pan.cpuPct).padStart(8)} ${fmt(r.pan.drawsPerSec).padStart(8)} | ${fmt(r.endTurnMs, 0).padStart(10)} / ${fmt(r.endTurnMax, 0).padEnd(8)} | ${fmt(r.heapMb).padStart(7)} ${String(r.nodes).padStart(10)} ${String(r.listeners).padStart(10)}${r.errors ? `  (${r.errors} page errors)` : ''}`);
  }
  if (rows.some((r) => !r.is3d)) console.log('* the 3D renderer did not start in this browser; the row is the flat map');
  if (process.env.PERF_JSON) console.log(JSON.stringify(rows));
} finally {
  await browser.close();
  await server.close?.();
  process.exit(0);
}
