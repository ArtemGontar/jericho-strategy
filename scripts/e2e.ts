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
/** E2E_ONLY=solo|hotseat|online|phone|gl runs a single section. The 3D section (gl) runs only when asked for: E2E_ONLY=gl, or E2E_GL=1. */
const only = process.env.E2E_ONLY;
function check(name: string, ok: boolean, detail = ''): void {
  console.log(`${ok ? '  ✓' : '  ✗'} ${name}${detail ? ` — ${detail}` : ''}`);
  if (!ok) failures++;
}

const server = await startServer({ port: 0, ephemeral: true, dist: process.env.DIST ?? 'dist' });
const base = `http://localhost:${server.port}`;
const browser = await chromium.launch();

async function newPage(name: string, opts: Parameters<typeof browser.newContext>[0] = { viewport: { width: 1280, height: 780 } }) {
  const ctx = await browser.newContext(opts);
  // E2E_FLAT=1 runs the browser tests on the 2D map renderer, which is far lighter for software-rendered CI machines
  if (process.env.E2E_FLAT) await ctx.addInitScript(() => localStorage.setItem('jericho.flat', '1'));
  // E2E_GL=1 forces the 3D renderer even on a software rasteriser
  if (process.env.E2E_GL) await ctx.addInitScript(() => localStorage.setItem('jericho.gl', '1'));
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
  if (!only || only === 'solo') {
    const { page, errors } = await newPage('solo');
    await page.goto(base);
    await page.click('button:has-text("New game")');
    await page.click('button:has-text("Begin")');
    await page.waitForSelector('canvas.map');
    await page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    await page.keyboard.press('b');
    await page.waitForTimeout(400);
    let s = await screenState(page);
    check('founded a capital with the B key', s.cities === 1);
    // idle units: the top bar counts them and "." jumps to the next one
    const idle = await page.evaluate(() => { const sc = (window as any).__jericho.screen; return { n: sc.myUnits().filter((u: any) => sc.needsOrders(u)).length, shown: document.querySelector('.idle-units b')?.textContent ?? null }; });
    check('top bar counts units waiting for orders', idle.n > 0 && idle.shown === String(idle.n), `${idle.shown} shown, ${idle.n} idle`);
    const unitBefore = await page.evaluate(() => (window as any).__jericho.screen.selUnitId);
    await page.keyboard.press('.');
    const unitAfter = await page.evaluate(() => (window as any).__jericho.screen.selUnitId);
    check('"." selects the next idle unit', unitAfter !== null && (idle.n === 1 || unitAfter !== unitBefore), `${unitBefore} -> ${unitAfter}`);
    // dialogs trap focus, close on Escape and give focus back
    await page.focus('button[aria-label^="Menu"]');
    await page.keyboard.press('Enter');
    await page.waitForSelector('.overlay');
    let escaped = false;
    for (let i = 0; i < 30 && !escaped; i++) {
      await page.keyboard.press(i % 2 ? 'Tab' : 'Shift+Tab');
      escaped = !(await page.evaluate(() => !!document.activeElement?.closest('.overlay')));
    }
    check('Tab stays inside an open dialog', !escaped);
    await page.keyboard.press('Escape');
    await page.waitForFunction(() => !document.querySelector('.overlay'));
    const back = await page.evaluate(() => document.activeElement?.getAttribute('aria-label') ?? '');
    check('Escape closes it and focus returns to the menu button', back.startsWith('Menu'), back);
    const label = () => page.locator('.turnlabel').first().innerText().then((t) => t.split('\n')[0].trim());
    let lastKey = '';
    let repeats = 0;
    for (let step = 0; step < 400; step++) {
      const turn = (await screenState(page)).turn;
      if (turn >= 15) break;
      const l = await label();
      const diag = await page.evaluate(() => { const sc = (window as any).__jericho.screen; const u = sc.selUnit; return `${sc.view.turn}|${u ? u.type : '-'}|city=${sc.openCityId}|modal=${sc.modalKind}|busy=${sc.busy}`; });
      repeats = `${l}${diag}` === lastKey ? repeats + 1 : 0;
      lastKey = `${l}${diag}`;
      if (repeats > 8) {
        console.log(`  (stuck at "${l}" ${diag}; nudging)`);
        await page.keyboard.press('Escape');
        await page.waitForTimeout(150);
        repeats = 0;
      }
      if (l.toLowerCase().startsWith('choose production')) {
        if (await page.locator('.drawer.hidden').count()) await page.locator('.nextturn').first().click();
        const names = await page.locator('.item.pick .nm').allInnerTexts();
        const pick = ['Settler', 'Worker', 'Warrior', 'Library'].map((n) => names.indexOf(n)).find((i) => i >= 0) ?? 0;
        await page.locator('.item.pick').nth(pick).click();
      } else if (l.toLowerCase().startsWith('choose research')) {
        await page.locator('.nextturn').first().click();
        await page.locator('.node.avail').first().click();
      } else if (l.toLowerCase().startsWith('choose policy')) {
        await page.locator('.nextturn').first().click();
        await page.locator('.choice button').first().click();
        await page.keyboard.press('Escape');
      } else if (l.toLowerCase().startsWith('next unit')) {
        await page.locator('.nextturn').first().click();
        const type = await page.evaluate(() => (window as any).__jericho.screen.selUnit?.type);
        if (type === 'settler') {
          const can = await page.evaluate(() => { const sc = (window as any).__jericho.screen; return sc.canFound(sc.selUnit) === null; });
          await page.keyboard.press(can ? 'b' : 'z');
        } else if (type === 'worker' || type === 'scout') await page.keyboard.press('a');
        else await page.keyboard.press('f');
      } else if (l.toLowerCase().startsWith('next turn')) {
        await page.locator('.nextturn').first().click();
        await page.waitForTimeout(150);
      } else await page.waitForTimeout(100);
    }
    s = await screenState(page);
    check('played 15 turns through the UI', s.turn >= 15, `turn ${s.turn}`);
    check('researched technologies', s.techs >= 2, `${s.techs} techs`);
    check('has built something', s.units >= 3);
    // autosave + continue
    await page.reload();
    await page.waitForSelector('button:has-text("Continue")');
    await page.click('button:has-text("Continue")');
    await page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    const after = await screenState(page);
    check('autosave resumes the game', after.turn >= 14, `turn ${after.turn}`);
    check('no browser errors (solo)', errors.length === 0, errors.slice(0, 2).join(' | '));
  }

  // ------------------------------------------------------------------ hot-seat
  console.log('Hot-seat');
  if (!only || only === 'hotseat') {
    const { page, errors } = await newPage('hotseat');
    await page.goto(base);
    await page.click('button:has-text("Hot-seat")');
    await page.fill('input[placeholder="Your name"]', 'Ann');
    await page.fill('input[placeholder="Player 2 name"]', 'Ben');
    await page.click('button:has-text("Begin")');
    await page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    check('first human is Ann', (await page.evaluate(() => (window as any).__jericho.screen.view.players[0].name)) === 'Ann');
    await page.keyboard.press('b');
    await page.waitForTimeout(300);
    await page.evaluate(() => { void (window as any).__jericho.screen.endTurn(); });
    await page.waitForSelector('.curtain');
    check('pass-the-device curtain appears', (await page.locator('.curtain h2').innerText()).toLowerCase().includes('ben'));
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
  if (!only || only === 'online') {
    const A = await newPage('A');
    const B = await newPage('B');
    await A.page.goto(base);
    await A.page.click('button:has-text("Play online")');
    await A.page.fill('input[placeholder="Your name"]', 'Alice');
    await A.page.click('button:has-text("Create a room")');
    await A.page.waitForSelector('.code');
    const code = (await A.page.locator('.code').first().innerText()).trim();
    check('room code is 5 letters', /^[A-Z]{5}$/.test(code), code);
    await B.page.goto(`${base}/?room=${code}`);
    await B.page.fill('input[placeholder="Your name"]', 'Bob');
    await B.page.click('button:has-text("Join")');
    await B.page.waitForSelector('.code');
    await A.page.waitForFunction(() => document.body.innerText.toLowerCase().includes('bob'));
    check('host sees the friend in the lobby', true);
    await B.page.fill('input[placeholder^="Say something"]', 'ready!');
    await B.page.press('input[placeholder^="Say something"]', 'Enter');
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
    const waitLabel = (await A.page.locator('.turnlabel').first().innerText()).replace(/\s+/g, ' ');
    check('host waits for the friend', waitLabel.toLowerCase().includes('waiting for bob'), waitLabel);
    // Bob drops and reconnects
    await B.page.reload();
    // the invite URL still carries ?room=CODE, so a refresh drops Bob straight back into the game
    await B.page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    check('reconnect restores Bob into the same game', (await screenState(B.page)).me === 1);
    await B.page.evaluate(() => { void (window as any).__jericho.screen.endTurn(); });
    await A.page.waitForFunction(() => (window as any).__jericho.screen.view.turn === 2, null, { timeout: 5000 });
    check('turn resolves once everyone is ready', (await screenState(A.page)).turn === 2);
    // Bob carries his seat to another browser with the hand-over link, and the first browser is replaced
    const link = await B.page.evaluate(() => { const o = JSON.parse(localStorage.getItem('jericho.online') || 'null'); return o ? `${location.origin}${location.pathname}?room=${o.code}&seat=${o.token}&name=${encodeURIComponent(o.name)}` : null; });
    check('a hand-over link exists for the online seat', !!link);
    const C = await newPage('bob on another device');
    await C.page.goto(link!);
    await C.page.waitForFunction(() => (window as any).__jericho?.screen?.view, null, { timeout: 8000 }).catch(() => undefined);
    check('the seat moves to the other browser', (await screenState(C.page).catch(() => ({ me: -1 }))).me === 1);
    check('the secret is taken out of the address bar', !(await C.page.url()).includes('seat='));
    check('no browser errors (online)', A.errors.length + B.errors.length === 0, [...A.errors, ...B.errors].slice(0, 2).join(' | '));
  }

  // ------------------------------------------------------------------ everyday flows
  console.log('Quick game, saves, dialogs, server check, offline');
  if (!only || only === 'solo') {
    const { page, errors } = await newPage('flows', { viewport: { width: 390, height: 780 }, hasTouch: true, isMobile: true, deviceScaleFactor: 1 });
    await page.goto(base);
    await page.waitForSelector('button:has-text("Quick game")');
    // the page is installable offline: a service worker takes control
    const sw = await page.evaluate(async () => { const r = await navigator.serviceWorker?.ready; return !!r?.active; }).catch(() => false);
    check('a service worker is active for offline play', sw);
    await page.tap('button:has-text("Quick game")');
    await page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    const lim = await page.evaluate(() => (window as any).__jericho.screen.view.settings.turnLimit);
    check('Quick game starts a 60-turn game', lim === 60, `turn limit ${lim}`);
    // contentment: the heart is in the top bar and opens its breakdown
    const heart = await page.$('.content-chip');
    check('the contentment heart is in the top bar', !!heart);
    await page.tap('.content-chip');
    await page.waitForSelector('.overlay .contentlist');
    const parts = await page.$$eval('.overlay .contentlist .line', (l) => l.length);
    check('tapping it shows what the people feel and why', parts >= 2, `${parts} lines`);
    await page.evaluate(() => (window as any).__jericho.screen.closeModal());
    // the leave confirmation is a visible dialog in the middle of the screen
    await page.evaluate(() => (window as any).__jericho.screen.openModal('menu'));
    await page.tap('button:has-text("Leave game")');
    await page.waitForSelector('.modal');
    const box = await page.evaluate(() => { const r = document.querySelector('.modal')!.getBoundingClientRect(); const b = document.querySelector('.modal-back')!; return { top: r.top, bottom: r.bottom, w: r.width, pos: getComputedStyle(b).position }; });
    check('the leave confirmation is a visible, centred dialog', box.pos === 'fixed' && box.top > 40 && box.bottom < 780 && box.w > 250, JSON.stringify(box));
    await page.tap('.modal button:has-text("Leave")');
    await page.waitForSelector('button:has-text("Saved games")');
    // saved games: the autosave is listed and can be opened
    await page.tap('button:has-text("Saved games")');
    await page.waitForSelector('.save-row');
    const rows = await page.$$eval('.save-row', (r) => r.length);
    check('the autosave is listed under Saved games', rows >= 1, `${rows} saves`);
    await page.tap('.save-row button:has-text("Open")');
    await page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    check('a saved game opens from the list', true);
    // a manual slot
    await page.evaluate(() => (window as any).__jericho.screen.openModal('save'));
    await page.tap('.overlay .menu-list button:has-text("Slot 1")');
    await page.waitForTimeout(300);
    const slot = await page.evaluate(() => localStorage.getItem('jericho.save.slot1'));
    check('Save game writes a slot', !!slot && JSON.parse(slot).format === 2);
    // an unreadable save is reported, kept, and Continue does not fail silently
    await page.evaluate(() => { localStorage.setItem('jericho.save.auto', '{broken'); (window as any).__jericho.screen.opts.onExit(); });
    await page.waitForSelector('button:has-text("Continue")');
    check('a damaged autosave is kept in storage, and the title screen still offers Continue', await page.evaluate(() => localStorage.getItem('jericho.save.auto') === '{broken'));
    await page.tap('button:has-text("Play online")');
    await page.waitForSelector('.server-status.up, .server-status.down');
    const up = await page.$('.server-status.up');
    check('Play online confirms the game server is reachable', !!up);
    // a solo game travels to another device by a short code
    await page.evaluate(() => { localStorage.removeItem('jericho.save.auto'); });
    await page.reload();
    await page.waitForSelector('button:has-text("Quick game")');
    await page.tap('button:has-text("Quick game")');
    await page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    await page.evaluate(() => (window as any).__jericho.screen.openModal('save'));
    await page.tap('.overlay button:has-text("Send this game")');
    await page.waitForSelector('.overlay .verdict.good');
    const sent = await page.$eval('.overlay .verdict.good', (e) => e.textContent ?? '');
    const code = /code is ([A-Z0-9]{8})/.exec(sent)?.[1] ?? '';
    check('a game can be sent to another device with a code', code.length === 8, sent.slice(0, 40));
    const D = await newPage('other device', { viewport: { width: 1000, height: 700 } });
    await D.page.goto(base);
    // a device with no saves of its own can still receive one
    await D.page.waitForSelector('button:has-text("Saved games")');
    await D.page.click('button:has-text("Saved games")');
    await D.page.fill('input[aria-label="Code from your other device"]', code.toLowerCase());
    await D.page.click('button:has-text("Receive")');
    await D.page.waitForSelector('.ok-line');
    check('the other device receives it', /Received/.test(await D.page.$eval('.ok-line', (e) => e.textContent ?? '')));
    await D.page.click('.save-row button:has-text("Open")');
    await D.page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    check('and opens the same game', (await screenState(D.page)).turn >= 1);
    // the code worked once
    await D.page.evaluate(() => (window as any).__jericho.screen.opts.onExit());
    await D.page.click('button:has-text("Saved games")');
    await D.page.fill('input[aria-label="Code from your other device"]', code);
    await D.page.click('button:has-text("Receive")');
    await D.page.waitForSelector('.error-line');
    check('a code works only once', /No saved game with that code/.test(await D.page.$eval('.error-line', (e) => e.textContent ?? '')));
    await D.ctx.close();
    check('no browser errors (flows)', errors.length === 0, errors.slice(0, 2).join(' | '));
  }

  // ------------------------------------------------------------------ phone layout
  console.log('Phone layout');
  if (!only || only === 'phone') {
    const { page, errors } = await newPage('phone', { viewport: { width: 390, height: 780 }, hasTouch: true, isMobile: true, deviceScaleFactor: 2 });
    await page.goto(base);
    await page.tap('button:has-text("New game")');
    await page.tap('button:has-text("Begin")');
    await page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1);
    check('no horizontal overflow on a phone', !overflow);
    // the capital is founded for the player, and its panel is already open
    await page.waitForSelector('.drawer:not(.hidden)');
    check('city panel opens as a bottom sheet', true);
    check('no browser errors (phone)', errors.length === 0, errors.slice(0, 2).join(' | '));
  }

  // phones of every shape: the top-bar buttons stay on screen, and a phone on its side keeps the map beside the city panel
  console.log('Phone sizes');
  if (!only || only === 'phone') {
    for (const [w, hgt] of [[360, 640], [390, 844], [667, 375], [932, 430]] as const) {
      const { page, errors } = await newPage(`phone ${w}x${hgt}`, { viewport: { width: w, height: hgt }, hasTouch: true, isMobile: true, deviceScaleFactor: 2 });
      await page.goto(base);
      await page.tap('button:has-text("New game")');
      await page.tap('button:has-text("Begin")');
      await page.waitForFunction(() => (window as any).__jericho?.screen?.view);
      await page.waitForTimeout(500);
      const bar = await page.evaluate(() => {
        const vw = window.innerWidth;
        const btns = [...document.querySelectorAll('.topbar .round')] as HTMLElement[];
        const menu = document.querySelector('.topbar button[aria-label^="Menu"]')?.getBoundingClientRect();
        return { overflow: document.documentElement.scrollWidth > vw + 1, offscreen: btns.filter((b) => { const r = b.getBoundingClientRect(); return r.width > 0 && (r.right > vw + 0.5 || r.left < 0); }).length, menu: !!menu && menu.right <= vw && menu.width >= 36 };
      });
      check(`${w}x${hgt}: no horizontal overflow, top-bar buttons on screen`, !bar.overflow && bar.offscreen === 0 && bar.menu);
      // the capital's panel opens first; look at the unit card with it closed and a unit picked
      await page.evaluate(() => { const sc = (window as any).__jericho.screen; sc.closeCity(); sc.selectNextIdle(); });
      await page.waitForTimeout(400);
      const unit = await page.evaluate(() => { const r = document.querySelector('.unitbar')?.getBoundingClientRect(); return r ? Math.round(r.height) : 0; });
      if (hgt < 520) check(`${w}x${hgt}: unit card leaves most of the map free`, unit > 0 && unit < hgt * 0.34, `${unit}px of ${hgt}px`);
      await page.evaluate(() => { const sc = (window as any).__jericho.screen; sc.openCityPanel(sc.view.cities.find((c: any) => c.mine).id); });
      await page.waitForSelector('.drawer:not(.hidden)');
      await page.waitForTimeout(400);
      const d = await page.evaluate(() => { const r = document.querySelector('.drawer')!.getBoundingClientRect(); return { l: r.left, t: r.top, r: r.right, b: r.bottom, w: innerWidth, h: innerHeight }; });
      if (hgt < 520) check(`${w}x${hgt}: city panel is a side panel and the map stays visible`, d.t < hgt * 0.2 && d.r < d.w * 0.55, `drawer ${Math.round(d.r)}px of ${d.w}px`);
      else check(`${w}x${hgt}: city panel is a bottom sheet`, d.t > hgt * 0.3 && d.b >= hgt - 1);
      const slotText = await page.$eval('.drawer .slots', (e) => e.textContent ?? '').catch(() => '');
      check(`${w}x${hgt}: the city panel shows its building slots`, /Slots \d+ \/ \d+/.test(slotText), slotText);
      const advisor = await page.evaluate(() => ({ governor: !!document.querySelector('.drawer .slotline .btn'), suggested: /Build /.test(document.querySelector('.drawer .btn.primary')?.textContent ?? '') }));
      check(`${w}x${hgt}: the city panel offers a governor and a suggested build`, advisor.governor && advisor.suggested, JSON.stringify(advisor));
      const menuHas = await (async () => { await page.evaluate(() => (window as any).__jericho.screen.openModal('menu')); return page.evaluate(() => ['Your cities', 'Diplomacy', 'Message log'].every((t) => [...document.querySelectorAll('.overlay .menu-list button')].some((b) => b.textContent?.includes(t)))); })();
      check(`${w}x${hgt}: the menu reaches cities, diplomacy and the log`, menuHas);
      check(`${w}x${hgt}: no browser errors`, errors.length === 0, errors.slice(0, 2).join(' | '));
    }
  }

  // ------------------------------------------------------------------ 3D renderer
  console.log('3D renderer');
  if (only === 'gl' || (!only && process.env.E2E_GL)) {
    const { page, errors } = await newPage('gl', { viewport: { width: 1100, height: 700 } });
    await page.goto(`${base}/?gl`);
    await page.click('button:has-text("New game")');
    await page.click('button:has-text("Begin")');
    await page.waitForSelector('canvas.map');
    await page.waitForFunction(() => (window as any).__jericho?.screen?.view);
    await page.keyboard.press('b');
    await page.waitForTimeout(800);
    const gl = () => page.evaluate(() => {
      const sc = (window as any).__jericho.screen;
      const c = document.createElement('canvas');
      return { is3d: !!sc.glCanvas && sc.glCanvas.isConnected, failures: sc.drawFailures, turn: sc.view.turn, webgl2: !!c.getContext('webgl2') };
    });
    let g = await gl();
    check('the 3D renderer is active', g.is3d && g.webgl2, JSON.stringify(g));
    for (let i = 0; i < 4; i++) {
      await page.evaluate(() => (window as any).__jericho.screen.endTurn());
      await page.waitForTimeout(500);
    }
    g = await gl();
    check('four turns later it is still 3D with no failed frames', g.is3d && g.failures === 0 && g.turn >= 4, JSON.stringify(g));
    const shot = await page.screenshot();
    check('the map actually draws something', shot.length > 30000, `${shot.length} bytes`);
    const heap = await page.evaluate(() => (performance as any).memory?.usedJSHeapSize ?? 0);
    check('JS heap stays modest', heap === 0 || heap < 400 * 1024 * 1024, `${(heap / 1048576).toFixed(0)} MB`);
    check('no browser errors (3D)', errors.length === 0, errors.slice(0, 2).join(' | '));
  }
} finally {
  await browser.close();
  await server.close();
}
console.log(failures ? `\n${failures} check(s) failed` : '\nAll end-to-end checks passed');
process.exit(failures ? 1 : 0);
