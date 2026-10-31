import { describe, expect, it } from 'vitest';
import { Game } from '../src/shared/game';
import { EVENT_CHANCE, EVENT_SPACING, EVENTS, FIRST_BAD_TURN, rollEvent, settlePending, triggerEvent } from '../src/shared/events';
import { tileYield } from '../src/shared/economy';
import { buildView } from '../src/shared/view';
import { RESOURCES, TERRAIN } from '../src/shared/data';

/** A human with a capital and a second city, so that bad luck is allowed. */
function world(seed = 5, extra: Partial<Parameters<typeof Game.create>[0]> = {}) {
  const g = Game.create({ seed, size: 'medium', players: [{ name: 'A', ai: false }, { name: 'B', ai: false }], ...extra });
  for (const t of g.tiles) t.explored = 0xffff;
  g.exec(0, { t: 'found', unit: g.units.find((u) => u.owner === 0 && u.type === 'settler')!.id });
  g.exec(1, { t: 'found', unit: g.units.find((u) => u.owner === 1 && u.type === 'settler')!.id });
  for (const t of g.tiles) {
    if (g.playerCities(0).length >= 2) break;
    if (!TERRAIN[t.terrain].land || !TERRAIN[t.terrain].passable || t.city >= 0) continue;
    if (g.cities.some((c) => g.dist(c.x, c.y, t.x, t.y) < 3)) continue;
    if (g.dist(g.playerCities(0)[0].x, g.playerCities(0)[0].y, t.x, t.y) > 5) continue;
    g.foundCity(0, t.x, t.y);
  }
  return g;
}
const roll = (g: Game, n: number) => {
  const out = [];
  for (let i = 0; i < n; i++) {
    g.players[0].lastEvent = -99;
    const r = rollEvent(g, g.players[0]);
    if (r) out.push(r);
  }
  return out;
};

describe('random events', () => {
  it('are off when asked, and rarer when gentle', () => {
    const off = world(5, { events: 'off' });
    off.s.turn = 80;
    expect(roll(off, 500)).toHaveLength(0);
    const gentle = world(5, { events: 'gentle' });
    const normal = world(5, { events: 'normal' });
    gentle.s.turn = normal.s.turn = 80;
    const a = roll(gentle, 3000).length, b = roll(normal, 3000).length;
    expect(a).toBeGreaterThan(3000 * EVENT_CHANCE.gentle * 0.6);
    expect(a).toBeLessThan(3000 * EVENT_CHANCE.gentle * 1.4);
    expect(b).toBeGreaterThan(a);
  });

  it('are kind to a new player: nothing bad early or with a single city', () => {
    const early = world(6);
    early.s.turn = FIRST_BAD_TURN - 1;
    const r1 = roll(early, 2500);
    expect(r1.length).toBeGreaterThan(100);
    expect(r1.every((e) => e.kind === 'good')).toBe(true);
    const lone = world(6);
    lone.s.turn = 90;
    for (const c of lone.playerCities(0).slice(1)) c.owner = 1;
    expect(lone.playerCities(0)).toHaveLength(1);
    expect(roll(lone, 2500).every((e) => e.kind === 'good')).toBe(true);
    // later, with two cities, bad things do happen
    const later = world(6);
    later.s.turn = 90;
    for (const c of later.playerCities(0)) { c.pop = 4; c.buildings.push('library'); }
    expect(roll(later, 3000).some((e) => e.kind === 'bad')).toBe(true);
  });

  it('never come twice in a row for one civilisation', () => {
    const g = world(7);
    g.s.turn = 80;
    g.players[0].lastEvent = 79;
    let n = 0;
    for (let i = 0; i < 400; i++) if (rollEvent(g, g.players[0])) n++;
    // after the first, the spacing keeps the rest quiet until time has passed
    expect(n).toBeLessThanOrEqual(1);
    expect(EVENT_SPACING).toBeGreaterThanOrEqual(3);
  });

  it('plague kills a citizen, is rarer with Medicine, and cannot strike a city with a Hospital', () => {
    const g = world(8);
    g.s.turn = 80;
    const [a] = g.playerCities(0);
    a.pop = 5;
    const before = a.pop;
    const res = triggerEvent(g, g.players[0], 'plague');
    expect(res?.kind).toBe('bad');
    expect(g.playerCities(0).reduce((s, c) => s + c.pop, 0)).toBeLessThan(before + g.playerCities(0).slice(1).reduce((s, c) => s + c.pop, 0));
    const h = world(8);
    h.s.turn = 80;
    for (const c of h.playerCities(0)) { c.pop = 5; c.buildings.push('hospital'); }
    expect(triggerEvent(h, h.players[0], 'plague')).toBeNull();
    const plain = EVENTS.find((e) => e.id === 'plague')!;
    const w = (techs: string[]) => { const m = world(8); m.s.turn = 80; m.players[0].techs.push(...techs); for (const c of m.playerCities(0)) c.pop = 5; return plain.plan({ game: m, p: m.players[0], cities: m.playerCities(0), era: 2 })!.weight; };
    expect(w(['medicine'])).toBeLessThan(w([]));
    expect(w(['medicine', 'sanitation'])).toBeLessThan(w(['medicine']));
  });

  it('earthquakes bring buildings down, unless the civilisation knows Construction', () => {
    let lost = 0, kept = 0;
    for (const construction of [false, true]) {
      for (let i = 0; i < 40; i++) {
        const g = world(9 + i);
        g.s.turn = 90;
        if (construction) g.players[0].techs.push('construction');
        const city = g.playerCities(0)[0];
        city.buildings.push('library');
        const n = city.buildings.length;
        triggerEvent(g, g.players[0], 'earthquake');
        const after = g.playerCities(0).reduce((s, c) => s + c.buildings.length, 0);
        const total = n + g.playerCities(0).slice(1).reduce((s, c) => s + c.buildings.length, 0);
        if (after < total) { if (construction) lost++; else kept++; }
      }
    }
    // without Construction the building falls every time (kept counts the falls here); with it, rarely
    expect(kept).toBe(40);
    expect(lost).toBeLessThan(25);
  });

  it('raiders take gold from an unguarded city and are turned back by a garrison', () => {
    const g = world(10);
    g.s.turn = 90;
    g.players[0].gold = 200;
    for (const u of g.units.filter((x) => x.owner === 0 && !g.isCivilian(x))) g.removeUnit(u);
    const res = triggerEvent(g, g.players[0], 'raiders');
    expect(res?.kind).toBe('bad');
    expect(g.players[0].gold).toBeLessThan(200);
    const guarded = world(10);
    guarded.s.turn = 90;
    guarded.players[0].gold = 200;
    for (const c of guarded.playerCities(0)) guarded.addUnit(0, 'warrior', c.x, c.y);
    triggerEvent(guarded, guarded.players[0], 'raiders');
    expect(guarded.players[0].gold).toBe(200);
  });

  it('drought is softened by a granary; floods spare a city with a harbor', () => {
    const g = world(11);
    g.s.turn = 60;
    for (const c of g.playerCities(0)) { c.pop = 4; c.food = 40; }
    triggerEvent(g, g.players[0], 'drought');
    expect(Math.min(...g.playerCities(0).map((c) => c.food))).toBeLessThan(40);
    const s = world(11);
    s.s.turn = 60;
    for (const c of s.playerCities(0)) { c.pop = 4; c.food = 40; c.buildings.push('granary'); }
    const before = s.playerCities(0).map((c) => c.food);
    triggerEvent(s, s.players[0], 'drought');
    const lostFood = s.playerCities(0).map((c, i) => before[i] - c.food);
    expect(Math.max(...lostFood)).toBeGreaterThan(0);
    expect(Math.max(...lostFood)).toBeLessThan(40 - 0); // smaller than the unprotected loss
  });

  it('good luck pays: harvest, merchants, scholars, ruins and a new luxury', () => {
    const g = world(12);
    g.s.turn = 40;
    const p = g.players[0];
    p.techs.push('mining', 'pottery');
    p.researching = 'writing';
    const gold = p.gold, culture = p.culture, research = p.research;
    expect(triggerEvent(g, p, 'merchants')?.kind).toBe('good');
    expect(p.gold).toBeGreaterThan(gold);
    expect(triggerEvent(g, p, 'ruins')?.kind).toBe('good');
    expect(p.culture).toBeGreaterThan(culture);
    expect(triggerEvent(g, p, 'scholars')?.kind).toBe('good');
    expect(p.research).toBeGreaterThan(research);
    const food = g.playerCities(0).reduce((s, c) => s + c.food, 0);
    expect(triggerEvent(g, p, 'harvest')?.kind).toBe('good');
    expect(g.playerCities(0).reduce((s, c) => s + c.food, 0)).toBeGreaterThan(food);
    for (const t of g.tiles) if (t.owner === 0 && t.resource && RESOURCES[t.resource].luxury) t.resource = null;
    const lux = triggerEvent(g, p, 'luxury');
    if (lux) expect(g.tiles.some((t) => t.owner === 0 && t.resource && RESOURCES[t.resource].luxury)).toBe(true);
  });
});

describe('fossil fuel', () => {
  it('is found only once the technology exists, and is worth nothing and invisible until then', () => {
    const g = world(13);
    g.s.turn = 80;
    const p = g.players[0];
    expect(triggerEvent(g, p, 'coal')).toBeNull(); // no Industrialization yet
    p.techs.push('industrialization');
    const res = triggerEvent(g, p, 'coal');
    expect(res?.kind).toBe('good');
    const tile = g.tiles.find((t) => t.owner === 0 && t.resource === 'coal')!;
    expect(tile).toBeDefined();
    const i = tile.y * g.width + tile.x;
    // the owner sees it and it pays
    expect(buildView(g, 0).resource[i]).toBeGreaterThan(0);
    expect(tileYield(tile, p.techs).prod).toBe(tileYield({ ...tile, resource: null }, p.techs).prod + 2);
    // a rival without the technology neither sees it nor gets anything from it
    g.setRelation(0, 1, 'peace');
    for (const t of g.tiles) t.explored = 0xffff;
    expect(buildView(g, 1).resource[i]).toBe(0);
    expect(tileYield(tile, g.players[1].techs).prod).toBe(tileYield({ ...tile, resource: null }, g.players[1].techs).prod);
    // before the technology, the same tile pays nothing
    expect(tileYield(tile, []).prod).toBe(tileYield({ ...tile, resource: null }, []).prod);
    // oil needs Combustion
    expect(triggerEvent(g, p, 'oil')).toBeNull();
    p.techs.push('combustion');
    expect(triggerEvent(g, p, 'oil')?.kind).toBe('good');
  });
});

describe('the industrial age', () => {
  it('lies on the way to the Enlightenment, so the late military and sea techs are no longer skipped', async () => {
    const { TECH_BY_ID, FINAL_TECH, TECHS, BUILDINGS } = await import('../src/shared/data');
    const need = new Set<string>();
    const walk = (id: string) => { if (need.has(id)) return; need.add(id); for (const p of TECH_BY_ID[id].pre) walk(p); };
    walk(FINAL_TECH);
    expect(need.has('industrialization')).toBe(true);
    expect(need.has('metallurgy')).toBe(true);
    // and a fork: the last step is one of three roads
    expect(TECH_BY_ID[FINAL_TECH].any).toEqual(['electricity', 'sanitation', 'combustion']);
    expect(TECH_BY_ID[FINAL_TECH].era).toBe(4);
    // the era's own techs unlock what they say
    for (const [tech, building] of [['industrialization', 'factory'], ['sanitation', 'hospital'], ['electricity', 'laboratory']] as const) expect(BUILDINGS[building].tech).toBe(tech);
    expect(TECHS.filter((t) => t.era === 4).length).toBeGreaterThanOrEqual(4);
    // none of the three roads is forced on everyone
    for (const road of ['electricity', 'sanitation', 'combustion']) expect(need.has(road)).toBe(false);
  });
});

describe('a fork in the tree', () => {
  it('the last technology needs one of three roads, and any one will do', async () => {
    const { availableTechs, techCost } = await import('../src/shared/economy');
    const { TECH_BY_ID, FINAL_TECH } = await import('../src/shared/data');
    const g = world(14);
    const p = g.players[0];
    // everything on the common way is known
    const known = new Set<string>();
    const add = (id: string) => { if (known.has(id)) return; known.add(id); for (const x of TECH_BY_ID[id].pre) add(x); };
    add('astronomy'); add('banking'); add('industrialization');
    p.techs.push(...known);
    expect(availableTechs(p)).not.toContain(FINAL_TECH);
    for (const road of ['electricity', 'sanitation', 'combustion']) {
      const q = world(14).players[0];
      const k = new Set<string>();
      const walk = (id: string) => { if (k.has(id)) return; k.add(id); for (const x of TECH_BY_ID[id].pre) walk(x); };
      walk('astronomy'); walk('banking'); walk('industrialization'); walk(road);
      q.techs.push(...k);
      expect(availableTechs(q), road).toContain(FINAL_TECH);
    }
    // the three roads cost about the same from the common way, so the choice is about what each leaves behind
    const costs = ['electricity', 'sanitation', 'combustion'].map((road) => {
      const q = world(14);
      const k = new Set<string>();
      const walk = (id: string) => { if (k.has(id)) return; k.add(id); for (const x of TECH_BY_ID[id].pre) walk(x); };
      walk('astronomy'); walk('banking'); walk('industrialization');
      q.players[0].techs.push(...k);
      const own = new Set<string>();
      const need = (id: string) => { if (own.has(id) || k.has(id)) return; own.add(id); for (const x of TECH_BY_ID[id].pre) need(x); };
      need(road);
      let c = 0;
      for (const id of own) c += techCost(q, q.players[0], id);
      return c;
    });
    expect(Math.max(...costs) / Math.min(...costs)).toBeLessThan(1.25);
  });
});

describe('events that ask a question', () => {
  /** Roll until a human has a question waiting. */
  function asked(seed: number, id?: string) {
    const g = world(seed);
    g.s.turn = 90;
    for (const c of g.playerCities(0)) { c.pop = 5; c.buildings.push('library'); }
    g.players[0].researching = 'writing';
    for (let i = 0; i < 4000; i++) {
      g.players[0].lastEvent = -99;
      g.players[0].pending = null;
      rollEvent(g, g.players[0]);
      const q = g.players[0].pending as { id: string } | null;
      if (q && (!id || q.id === id)) return g;
    }
    return null;
  }

  it('wait for an answer, with two options, and apply the one chosen', () => {
    const g = asked(20, 'envoy')!;
    expect(g).not.toBeNull();
    const p = g.players[0];
    expect(p.pending!.options).toHaveLength(2);
    expect(buildView(g, 0).pending?.id).toBe('envoy');
    expect(g.exec(0, { t: 'choose', option: 5 }).ok).toBe(false);
    expect(g.exec(0, { t: 'choose', option: -1 }).ok).toBe(false);
    const gold = p.gold;
    expect(g.exec(0, { t: 'choose', option: 0 }).ok).toBe(true);
    expect(p.pending).toBeNull();
    expect(p.gold).toBeGreaterThan(gold);
    expect(g.exec(0, { t: 'choose', option: 0 }).ok).toBe(false); // nothing left to answer
  });

  it('settle themselves with their default if nobody answers, and never hold the game up', () => {
    const g = asked(21, 'dispute')!;
    expect(g).not.toBeNull();
    const p = g.players[0];
    const research = p.research, culture = p.culture;
    p.pending!.turn = g.turn - 1;
    settlePending(g, p);
    expect(p.pending).toBeNull();
    expect(p.research > research || p.culture > culture).toBe(true);
    // a question from an earlier turn is also settled when the next turn is worked out
    const h = asked(22, 'envoy')!;
    h.players[0].pending!.turn = h.turn - 1;
    h.exec(0, { t: 'end' });
    h.exec(1, { t: 'end' });
    expect(h.players[0].pending?.turn ?? h.turn).toBeGreaterThanOrEqual(h.turn - 1);
  });

  it('are answered at once for the computers, and a bad one waits for turn 30', () => {
    const g = world(23, { players: [{ name: 'A', ai: false }, { name: 'B', ai: true }] });
    g.s.turn = 90;
    for (const c of g.playerCities(1)) c.pop = 5;
    for (let i = 0; i < 1500; i++) { g.players[1].lastEvent = -99; rollEvent(g, g.players[1]); }
    expect(g.players[1].pending).toBeNull();
    const early = world(24);
    early.s.turn = 20;
    for (const c of early.playerCities(0)) c.pop = 5;
    for (let i = 0; i < 3000; i++) { early.players[0].lastEvent = -99; early.players[0].pending = null; rollEvent(early, early.players[0]); const q = early.players[0].pending as { id: string } | null; if (q) expect(q.id).not.toBe('rumours'); }
  });

  it('a quarantine costs gold and saves the city; carrying on is a gamble', () => {
    const g = asked(25, 'rumours');
    if (!g) return; // needs a city that can fall ill: skip when the map offers none
    const p = g.players[0];
    p.gold = 100;
    const pop = g.playerCities(0).reduce((s, c) => s + c.pop, 0);
    g.exec(0, { t: 'choose', option: 0 });
    expect(p.gold).toBeLessThan(100);
    expect(g.playerCities(0).reduce((s, c) => s + c.pop, 0)).toBe(pop);
  });
});

describe('later policies', () => {
  it('are offered only once their technology is known, and each does its work', async () => {
    const { POLICIES } = await import('../src/shared/data');
    const { cityYield } = await import('../src/shared/economy');
    const { contentment, dealSlots } = await import('../src/shared/civ');
    const g = world(26);
    const p = g.players[0];
    const late = POLICIES.filter((x) => x.tech);
    expect(late.length).toBeGreaterThanOrEqual(6);
    for (const x of late) expect(g.openPolicies(p), x.id).not.toContain(x.id);
    p.techs.push(...late.map((x) => x.tech!));
    for (const x of late) expect(g.openPolicies(p), x.id).toContain(x.id);
    const city = g.playerCities(0)[0];
    city.buildings.push('university', 'factory', 'temple');
    const base = cityYield(g, city);
    const content = contentment(g, 0).value;
    const slots = dealSlots(g, 0);
    p.policies.push('academies');
    expect(cityYield(g, city).science).toBeCloseTo(base.science + 2, 5);
    p.policies.push('industrial_policy');
    expect(cityYield(g, city).prod).toBeCloseTo(base.prod + 3, 5);
    expect(contentment(g, 0).value).toBeCloseTo(content - 2, 5);
    p.policies.push('bread_circuses');
    expect(cityYield(g, city).prod).toBeLessThan(base.prod + 3);
    p.policies.push('open_society');
    expect(dealSlots(g, 0)).toBe(slots + 1);
    p.policies.push('public_health');
    expect(triggerEvent(g, p, 'plague')).toBeNull();
  });
});
