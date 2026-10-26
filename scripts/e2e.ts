// @ts-nocheck
/**
 * Browser end-to-end checks. Needs a production build (`npm run build`) and Playwright with Chromium installed
 * (`npx playwright install chromium`, or set PLAYWRIGHT_MODULE to its install path).
 *
 *   npm run e2e
 */
import { createRequire } from 'node:module';
import { startServer } from '../src/server/server';

const require = createRequire(import.meta.url);
function loadPlaywright(): typeof import('playwright') {
  const candidates = [process.env.PLAYWRIGHT_MODULE, 'playwright', '/opt/node22/lib/node_modules/playwright'].filter(Boolean) as string[];
  for (const c of candidates) {
    try {
      return require(c);
    } catch { /* try next */ }
  }
  throw new Error('Playwright is not installed. Run: npm i -D playwright && npx playwright install chromium');
}

const { chromium } = loadPlaywright();
let failures = 0;
function check(name: string, ok: boolean, detail = ''): void {
  console.log(`${ok ? '  ✓' : '  ✗'} ${name}${detail ? ` — ${detail}` : ''}`);
  if (!ok) failures++;
}

const server = await startServer({ port: 0, ephemeral: true, dist: process.env.DIST ?? 'dist' });
const base = `http://localhost:${server.port}`;
const browser = await chromium.launch();

async function newPage(name: string, opts: Parameters<typeof browser.newContext>[0] = { viewport: { width: 1280, height: 780 } }) {
  const ctx = await browser.newContext(opts);
  const page = await ctx.newPage();
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(`${name}: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(`${name}: ${m.text()}`); });
  return { page, errors, ctx };
}
const screenState = (page: import('playwright').Page) =>
  page.evaluate(() => {
    const s = (window as unknown as { __jericho: { screen: any } }).__jericho.screen;
    const v = s.view;
    return { turn: v.turn, me: v.me, cities: v.cities.filter((c: any) => c.mine).length, units: v.units.filter((u: any) => u.owner === v.me).length, techs: v.techs.length, over: v.over };
  });

try {
  // ------------------------------------------------------------------ solo
  console.log('Solo game');
  {
    const { page, errors } = await newPage('solo');
    await page.goto(base);
    await page.click('button:has-text("New game vs computer")');
    await page.click('button:has-text("Start game")');
    await page.waitForSelector('canvas.map');
    await page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    await page.keyboard.press('b');
    await page.waitForTimeout(400);
    let s = await screenState(page);
    check('founded a capital with the B key', s.cities === 1);
    const label = () => page.locator('.endturn').first().innerText().then((t) => t.split('\n')[0]);
    for (let step = 0; step < 300; step++) {
      const turn = (await screenState(page)).turn;
      if (turn >= 15) break;
      const l = await label();
      if (l.startsWith('Choose production')) {
        if (await page.locator('.sidepanel.hidden').count()) await page.locator('.endturn').first().click();
        const names = await page.locator('.opt .nm').allInnerTexts();
        const pick = ['Settler', 'Worker', 'Warrior', 'Library'].map((n) => names.indexOf(n)).find((i) => i >= 0) ?? 0;
        await page.locator('.opt').nth(pick).locator('button:has-text("▶")').click();
      } else if (l.startsWith('Choose research')) {
        await page.locator('.endturn').first().click();
        await page.locator('.tech.avail').first().click();
      } else if (l.startsWith('Next unit')) {
        await page.locator('.endturn').first().click();
        const type = await page.evaluate(() => (window as any).__jericho.screen.selUnit?.type);
        if (type === 'settler') {
          const can = await page.evaluate(() => { const sc = (window as any).__jericho.screen; return sc.canFound(sc.selUnit) === null; });
          await page.keyboard.press(can ? 'b' : 'z');
        } else if (type === 'worker' || type === 'scout') await page.keyboard.press('a');
        else await page.keyboard.press('f');
      } else if (l.startsWith('End turn')) {
        await page.locator('.endturn').first().click();
        await page.waitForTimeout(150);
      } else await page.waitForTimeout(100);
    }
    s = await screenState(page);
    check('played 15 turns through the UI', s.turn >= 15, `turn ${s.turn}`);
    check('researched technologies', s.techs >= 2, `${s.techs} techs`);
    check('has built something', s.units >= 3);
    // autosave + continue
    await page.reload();
    await page.waitForSelector('button:has-text("Continue game")');
    await page.click('button:has-text("Continue game")');
    await page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    const after = await screenState(page);
    check('autosave resumes the game', after.turn >= 14, `turn ${after.turn}`);
    check('no browser errors (solo)', errors.length === 0, errors.slice(0, 2).join(' | '));
  }

  // ------------------------------------------------------------------ hot-seat
  console.log('Hot-seat');
  {
    const { page, errors } = await newPage('hotseat');
    await page.goto(base);
    await page.click('button:has-text("Hot-seat")');
    await page.fill('input[placeholder="Your name"]', 'Ann');
    await page.fill('input[placeholder="Player 2 name"]', 'Ben');
    await page.click('button:has-text("Start game")');
    await page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    check('first human is Ann', (await page.evaluate(() => (window as any).__jericho.screen.view.players[0].name)) === 'Ann');
    await page.keyboard.press('b');
    await page.waitForTimeout(300);
    await page.evaluate(() => { void (window as any).__jericho.screen.endTurn(); });
    await page.waitForSelector('.curtain');
    check('pass-the-device curtain appears', (await page.locator('.curtain h2').innerText()).includes('Ben'));
    await page.click('.curtain button');
    await page.waitForTimeout(300);
    check('second human now plays', (await screenState(page)).me === 1);
    await page.keyboard.press('b');
    await page.evaluate(() => { void (window as any).__jericho.screen.endTurn(); });
    await page.waitForSelector('.curtain');
    await page.click('.curtain button');
    await page.waitForTimeout(500);
    const st = await screenState(page);
    check('turn advanced after both humans ended', st.turn === 2 && st.me === 0, `turn ${st.turn}, me ${st.me}`);
    check('no browser errors (hot-seat)', errors.length === 0, errors.slice(0, 2).join(' | '));
  }

  // ------------------------------------------------------------------ online
  console.log('Online (2 players)');
  {
    const A = await newPage('A');
    const B = await newPage('B');
    await A.page.goto(base);
    await A.page.click('button:has-text("Play online")');
    await A.page.fill('input[placeholder="Your name"]', 'Alice');
    await A.page.click('button:has-text("Create a room")');
    await A.page.waitForSelector('.code');
    const code = (await A.page.locator('.code').first().innerText()).trim();
    check('room code is 4 letters', /^[A-Z]{4}$/.test(code), code);
    await B.page.goto(`${base}/?room=${code}`);
    await B.page.fill('input[placeholder="Your name"]', 'Bob');
    await B.page.click('button:has-text("Join")');
    await B.page.waitForSelector('.lobby');
    await A.page.waitForFunction(() => document.body.innerText.includes('Bob'));
    check('host sees the friend in the lobby', true);
    await B.page.fill('input[placeholder^="Chat"]', 'ready!');
    await B.page.press('input[placeholder^="Chat"]', 'Enter');
    await A.page.waitForFunction(() => document.querySelector('.chat-log')?.textContent?.includes('ready!'));
    check('lobby chat works', true);
    await A.page.click('button:has-text("Start game")');
    await A.page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    await B.page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    await A.page.keyboard.press('b');
    await B.page.keyboard.press('b');
    await A.page.waitForTimeout(400);
    check('both founded capitals', (await screenState(A.page)).cities === 1 && (await screenState(B.page)).cities === 1);
    await A.page.evaluate(() => { void (window as any).__jericho.screen.endTurn(); });
    await A.page.waitForTimeout(300);
    check('host waits for the friend', (await A.page.locator('.endturn').first().innerText()).includes('Waiting for Bob'));
    // Bob drops and reconnects
    await B.page.reload();
    // the invite URL still carries ?room=CODE, so a refresh drops Bob straight back into the game
    await B.page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    check('reconnect restores Bob into the same game', (await screenState(B.page)).me === 1);
    await B.page.evaluate(() => { void (window as any).__jericho.screen.endTurn(); });
    await A.page.waitForFunction(() => (window as any).__jericho.screen.view.turn === 2, null, { timeout: 5000 });
    check('turn resolves once everyone is ready', (await screenState(A.page)).turn === 2);
    check('no browser errors (online)', A.errors.length + B.errors.length === 0, [...A.errors, ...B.errors].slice(0, 2).join(' | '));
  }

  // ------------------------------------------------------------------ phone layout
  console.log('Phone layout');
  {
    const { page, errors } = await newPage('phone', { viewport: { width: 390, height: 780 }, hasTouch: true, isMobile: true, deviceScaleFactor: 2 });
    await page.goto(base);
    await page.tap('button:has-text("New game vs computer")');
    await page.tap('button:has-text("Start game")');
    await page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1);
    check('no horizontal overflow on a phone', !overflow);
    await page.tap('button:has-text("Found city")');
    await page.waitForSelector('.sidepanel:not(.hidden)');
    check('city panel opens as a bottom sheet', true);
    check('no browser errors (phone)', errors.length === 0, errors.slice(0, 2).join(' | '));
  }
} finally {
  await browser.close();
  await server.close();
}
console.log(failures ? `\n${failures} check(s) failed` : '\nAll end-to-end checks passed');
process.exit(failures ? 1 : 0);
