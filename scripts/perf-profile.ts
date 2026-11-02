// @ts-nocheck
/**
 * Where does the page spend its JavaScript time? Loads a mid-game save and samples the CPU profiler while idle or while
 * ending turns. Use an unminified build so function names are readable:
 *   npx vite build --minify false --outDir /tmp/dist-dev --emptyOutDir
 *   DIST=/tmp/dist-dev npx tsx scripts/perf-profile.ts idle|turns [flat|gl]
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
console.error = () => {};
const mode = process.argv[2] ?? 'idle';
const renderer = process.argv[3] ?? 'flat';
const game = Game.create({ seed: 4242, size: 'large', players: Array.from({ length: 8 }, (_, i) => ({ name: i === 0 ? 'You' : `AI${i}`, ai: true, aiLevel: 'normal' })), turnLimit: 400 });
for (let t = 0; t < 60; t++) game.processTurn();
game.players[0].ai = false;
const record = JSON.stringify({ format: 2, savedAt: Date.now(), humans: [0], state: JSON.parse(game.serialize()) });
const server = await startServer({ port: 0, ephemeral: true, dist: process.env.DIST ?? 'dist' });
const browser = await pw.chromium.launch();
const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, hasTouch: true, isMobile: true });
await ctx.addInitScript(([rec, r]) => {
  localStorage.setItem('jericho.muted', '1'); localStorage.setItem('jericho.save.auto', rec); localStorage.setItem('jericho.guide.v2', 'off');
  localStorage.setItem(r === 'flat' ? 'jericho.flat' : 'jericho.gl', '1');
}, [record, renderer]);
const page = await ctx.newPage();
const cdp = await ctx.newCDPSession(page);
await page.goto(`http://localhost:${server.port}`);
await page.click('button:has-text("Continue")');
await page.waitForSelector('canvas.map');
await page.waitForTimeout(3000);
await cdp.send('Profiler.enable');
await cdp.send('Profiler.setSamplingInterval', { interval: 200 });
await cdp.send('Profiler.start');
if (mode === 'turns') for (let i = 0; i < 6; i++) { await page.evaluate(() => window.__jericho.screen.endTurn()); await page.waitForTimeout(150); }
else await page.waitForTimeout(8000);
const { profile } = await cdp.send('Profiler.stop');
const byId = new Map(profile.nodes.map((n) => [n.id, n]));
const self = new Map();
profile.samples.forEach((id, i) => self.set(id, (self.get(id) ?? 0) + (profile.timeDeltas[i] ?? 0)));
const agg = new Map();
for (const [id, t] of self) {
  const cf = byId.get(id).callFrame;
  const k = `${cf.functionName || '(anon)'} ${cf.url.split('/').slice(-1)[0]}:${cf.lineNumber + 1}`;
  agg.set(k, (agg.get(k) ?? 0) + t);
}
const total = [...agg.values()].reduce((a, b) => a + b, 0);
const idle = (agg.get('(idle) :0') ?? 0) + (agg.get('(program) :0') ?? 0);
console.log(`${mode}/${renderer}: sampled ${(total / 1000).toFixed(0)} ms, of which idle or native ${(idle / 1000).toFixed(0)} ms`);
[...agg.entries()].filter(([k]) => !k.startsWith('(idle)') && !k.startsWith('(program)')).sort((a, b) => b[1] - a[1]).slice(0, 22).forEach(([k, t]) => console.log((t / 1000).toFixed(0).padStart(6) + ' ms  ' + k));
await browser.close();
await server.close?.();
process.exit(0);
