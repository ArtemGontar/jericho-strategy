import { describe, expect, it } from 'vitest';
import { Game } from '../src/shared/game';
import { MAX_DEALS, SANCTION_TURNS, contentment, dealGold, luxuryAccess, outputFactor, growthFactor } from '../src/shared/civ';
import { cityYield, refreshPlayer } from '../src/shared/economy';
import { buildView } from '../src/shared/view';
import { TERRAIN } from '../src/shared/data';

/** n humans, each with a capital, all met and at peace. */
function world(n = 3, seed = 21) {
  const g = Game.create({ seed, size: 'large', players: Array.from({ length: n }, (_, i) => ({ name: `P${i}`, ai: false })) });
  for (const t of g.tiles) t.explored = 0xffff;
  for (let i = 0; i < n; i++) g.exec(i, { t: 'found', unit: g.units.find((u) => u.owner === i && u.type === 'settler')!.id });
  for (let a = 0; a < n; a++) for (let b = a + 1; b < n; b++) g.setRelation(a, b, 'peace');
  return g;
}
/** Put a luxury on a tile inside a player's borders. */
function giveLuxury(g: Game, pid: number, res: 'wine' | 'spices' | 'gems' | 'pearls') {
  const t = g.tiles.find((x) => x.owner === pid && !x.resource && TERRAIN[x.terrain].land && x.city < 0)!;
  t.resource = res;
}
const stripLuxuries = (g: Game) => { for (const t of g.tiles) if (t.resource && ['wine', 'spices', 'gems', 'pearls'].includes(t.resource)) t.resource = null; };

describe('contentment', () => {
  it('breaks down into parts that add up, and luxuries, temples and wonders raise it', () => {
    const g = world(2);
    stripLuxuries(g);
    const base = contentment(g, 0);
    expect(Math.round(base.parts.reduce((s, p) => s + p.value, 0) * 10) / 10).toBe(base.value);
    giveLuxury(g, 0, 'wine');
    expect(contentment(g, 0).value).toBe(base.value + 3);
    giveLuxury(g, 0, 'gems');
    expect(contentment(g, 0).value).toBe(base.value + 6);
    g.playerCities(0)[0].buildings.push('temple');
    expect(contentment(g, 0).value).toBe(base.value + 7);
    g.playerCities(0)[0].buildings.push('pyramids');
    expect(contentment(g, 0).value).toBe(base.value + 8);
  });

  it('falls with every city and citizen, and with war weariness', () => {
    const g = world(2);
    stripLuxuries(g);
    const one = contentment(g, 0).value;
    let made = 0;
    for (const t of g.tiles) {
      if (made >= 3) break;
      if (!TERRAIN[t.terrain].land || !TERRAIN[t.terrain].passable || t.city >= 0) continue;
      if (g.cities.some((c) => g.dist(c.x, c.y, t.x, t.y) < 3)) continue;
      g.foundCity(0, t.x, t.y);
      made++;
    }
    expect(made).toBe(3);
    const four = contentment(g, 0).value;
    expect(four).toBeLessThan(one - 3);
    g.players[0].weariness = 5;
    expect(contentment(g, 0).value).toBe(Math.round((four - 5) * 10) / 10);
    g.players[0].weariness = 50; // only so much is felt
    expect(contentment(g, 0).value).toBe(Math.round((four - 8) * 10) / 10);
  });

  it('restless people produce and grow less, thriving ones a little more', () => {
    expect(outputFactor(-4)).toBeCloseTo(0.8);
    expect(outputFactor(-40)).toBe(0.55);
    expect(growthFactor(-4)).toBeCloseTo(0.72);
    expect(growthFactor(-40)).toBe(0.4);
    expect(outputFactor(3)).toBe(1);
    expect(outputFactor(12)).toBeGreaterThan(1);
    const g = world(2);
    const city = g.playerCities(0)[0];
    const p = g.players[0];
    p.content = 3;
    const calm = cityYield(g, city);
    p.content = -6;
    const restless = cityYield(g, city);
    expect(restless.prod).toBeLessThan(calm.prod);
    expect(restless.gold).toBeLessThan(calm.gold);
    expect(restless.science).toBeLessThan(calm.science);
    p.content = 12;
    expect(cityYield(g, city).prod).toBeGreaterThan(calm.prod);
  });

  it('reaches the player through the view', () => {
    const g = world(2);
    const v = buildView(g, 0);
    expect(v.content.value).toBe(contentment(g, 0).value);
    expect(v.dealsLeft).toBe(MAX_DEALS);
    expect(v.players[1].deal).toBe(false);
  });
});

describe('trade agreements', () => {
  it('are proposed, answered, and pay both sides every turn', () => {
    const g = world(2);
    refreshPlayer(g, 0); refreshPlayer(g, 1);
    const before = [g.players[0].income.gold, g.players[1].income.gold];
    expect(g.exec(0, { t: 'deal', to: 1 }).ok).toBe(true);
    const pr = g.s.proposals.find((x) => x.kind === 'deal')!;
    expect(pr.from).toBe(0);
    expect(g.exec(0, { t: 'accept', proposal: pr.id, accept: true }).ok).toBe(false); // only the one asked can answer
    expect(g.exec(1, { t: 'accept', proposal: pr.id, accept: true }).ok).toBe(true);
    expect(g.hasDeal(0, 1)).toBe(true);
    const gold = dealGold(g, 0, 1);
    expect(gold).toBe(2);
    expect(g.players[0].income.gold).toBe(before[0] + gold);
    expect(g.players[1].income.gold).toBe(before[1] + gold);
    expect(g.exec(0, { t: 'deal', to: 1 }).ok).toBe(false); // already trading
  });

  it('need peace, and end when war is declared', () => {
    const g = world(2);
    g.setRelation(0, 1, 'war');
    expect(g.exec(0, { t: 'deal', to: 1 }).ok).toBe(false);
    g.setRelation(0, 1, 'peace');
    g.signDeal(0, 1);
    expect(g.hasDeal(0, 1)).toBe(true);
    g.declareWar(0, 1);
    expect(g.hasDeal(0, 1)).toBe(false);
    expect(g.players[0].aggression).toBe(g.turn); // striking a neighbour at peace is remembered
  });

  it('share luxuries (contentment) and strategic resources', () => {
    const g = world(2);
    stripLuxuries(g);
    giveLuxury(g, 1, 'spices');
    for (const t of g.tiles) if (t.owner !== 1 && (t.resource === 'iron')) t.resource = null;
    const ironSpot = g.tiles.find((x) => x.owner === 1 && !x.resource && x.terrain !== 'mountain' && TERRAIN[x.terrain].land && x.city < 0 && x.terrain !== 'coast')!;
    ironSpot.resource = 'iron';
    const before = contentment(g, 0).value;
    expect(luxuryAccess(g, 0).traded).toEqual([]);
    expect(g.hasResource(0, 'iron')).toBe(false);
    g.signDeal(0, 1);
    expect(luxuryAccess(g, 0).traded).toEqual(['spices']);
    expect(contentment(g, 0).value).toBe(before + 3);
    expect(g.hasResource(0, 'iron')).toBe(true);
    g.endDeal(0, 1, 'chosen');
    expect(g.hasResource(0, 'iron')).toBe(false);
  });

  it('are limited in number', () => {
    const g = world(MAX_DEALS + 2);
    for (let i = 1; i <= MAX_DEALS; i++) g.signDeal(0, i);
    expect(g.exec(0, { t: 'deal', to: MAX_DEALS + 1 }).ok).toBe(false);
    expect(buildView(g, 0).dealsLeft).toBe(0);
  });
});

describe('sanctions', () => {
  it('cancel the agreement, forbid a new one, and cost the target gold and contentment', () => {
    const g = world(3);
    stripLuxuries(g);
    g.signDeal(0, 1);
    refreshPlayer(g, 1);
    const income = g.players[1].income.gold;
    const content = contentment(g, 1).value;
    expect(g.exec(0, { t: 'sanction', on: 1 }).ok).toBe(true);
    expect(g.hasDeal(0, 1)).toBe(false);
    expect(g.isSanctioned(0, 1)).toBe(true);
    expect(g.exec(1, { t: 'deal', to: 0 }).ok).toBe(false);
    expect(g.exec(0, { t: 'deal', to: 1 }).ok).toBe(false);
    refreshPlayer(g, 1);
    expect(g.players[1].income.gold).toBeLessThan(income);
    expect(contentment(g, 1).value).toBeCloseTo(content - 1, 5);
    expect(g.exec(0, { t: 'sanction', on: 1 }).ok).toBe(false); // already in force
    expect(g.exec(2, { t: 'lift', on: 1 }).ok).toBe(false); // only the one who imposed them can lift them
    expect(g.exec(0, { t: 'lift', on: 1 }).ok).toBe(true);
    expect(g.exec(1, { t: 'deal', to: 0 }).ok).toBe(true);
  });

  it('lapse after a while, and cannot be put on someone you have not met', () => {
    const g = world(3);
    g.setRelation(0, 2, 'unmet');
    expect(g.exec(0, { t: 'sanction', on: 2 }).ok).toBe(false);
    g.exec(0, { t: 'sanction', on: 1 });
    expect(g.s.sanctions).toHaveLength(1);
    g.s.turn += SANCTION_TURNS;
    g.processTurn();
    expect(g.s.sanctions).toHaveLength(0);
  });

  it('war weariness grows with war and fades in peace', () => {
    const g = world(2);
    g.setRelation(0, 1, 'war');
    const w0 = g.players[0].weariness;
    g.processTurn();
    expect(g.players[0].weariness).toBeGreaterThan(w0);
    g.addWeariness(0, 5);
    g.setRelation(0, 1, 'peace');
    const w1 = g.players[0].weariness;
    g.processTurn();
    expect(g.players[0].weariness).toBeLessThan(w1);
  });
});

describe('hostile input', () => {
  it('never throws or changes anything for nonsense targets', () => {
    const g = world(2);
    const junk = [-1, 99, NaN, 1.5, 0, undefined as unknown as number, 'x' as unknown as number, null as unknown as number];
    for (const j of junk) {
      for (const cmd of [{ t: 'deal', to: j }, { t: 'cancelDeal', with: j }, { t: 'sanction', on: j }, { t: 'lift', on: j }] as const) {
        expect(() => g.exec(0, cmd as never)).not.toThrow();
      }
    }
    expect(g.s.deals).toHaveLength(0);
    expect(g.s.sanctions).toHaveLength(0);
    expect(g.exec(0, { t: 'sanction', on: 0 }).ok).toBe(false); // not yourself
    expect(g.exec(0, { t: 'deal', to: 0 }).ok).toBe(false);
  });
});

