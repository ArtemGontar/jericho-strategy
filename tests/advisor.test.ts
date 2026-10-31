import { describe, expect, it } from 'vitest';
import { Game } from '../src/shared/game';
import { aiChooseResearch, suggestBuild } from '../src/shared/ai';
import { buildView } from '../src/shared/view';

function world(seed = 5) {
  const g = Game.create({ seed, size: 'small', players: [{ name: 'A', ai: false }, { name: 'B', ai: true }] });
  for (const t of g.tiles) t.explored = 0xffff;
  g.exec(0, { t: 'found', unit: g.units.find((u) => u.owner === 0 && u.type === 'settler')!.id });
  return g;
}

describe('suggestions', () => {
  it('never touch the random numbers or the state, so asking is always safe', () => {
    const g = world();
    const city = g.playerCities(0)[0];
    const rngBefore = g.rng.state;
    const stateBefore = JSON.stringify(g.s);
    for (let i = 0; i < 5; i++) {
      const a = suggestBuild(g, g.players[0], city);
      const b = suggestBuild(g, g.players[0], city);
      expect(b).toEqual(a);
      buildView(g, 0);
      aiChooseResearch(g, g.players[0], true);
    }
    expect(g.rng.state).toBe(rngBefore);
    expect(JSON.stringify(g.s)).toBe(stateBefore);
  });

  it('offer something sensible: a defender before anything else in a bare new city', () => {
    const g = world();
    for (const u of g.units.filter((x) => x.owner === 0 && x.type !== 'settler')) g.removeUnit(u);
    const s = suggestBuild(g, g.players[0], g.playerCities(0)[0]);
    expect(s).not.toBeNull();
    expect(s!.kind).toBe('unit');
    const v = buildView(g, 0);
    expect(v.cities.find((c) => c.mine)!.suggest).toEqual(s);
    expect(v.cities.find((c) => !c.mine)?.suggest).toBeUndefined();
  });

  it('suggest a technology once there is a choice and nothing is being researched', () => {
    const g = world();
    g.players[0].researching = null;
    expect(buildView(g, 0).suggestedTech).not.toBeNull();
    g.exec(0, { t: 'research', tech: buildView(g, 0).suggestedTech! });
    expect(buildView(g, 0).suggestedTech).toBeNull();
  });
});

describe('governors', () => {
  it('fill an empty queue on their own, and leave a human’s other cities alone', () => {
    const g = world();
    const city = g.playerCities(0)[0];
    expect(city.queue).toHaveLength(0);
    expect(g.exec(0, { t: 'governor', city: city.id, on: true }).ok).toBe(true);
    expect(city.governor).toBe(true);
    expect(city.queue.length).toBeGreaterThan(0); // chosen at once
    city.queue = [];
    g.players[0].endedTurn = true;
    g.maybeProcessTurn();
    expect(city.queue.length).toBeGreaterThan(0); // and again when it runs dry
    expect(g.exec(0, { t: 'governor', city: city.id, on: false }).ok).toBe(true);
    city.queue = [];
    g.players[0].endedTurn = true;
    g.maybeProcessTurn();
    expect(city.queue).toHaveLength(0);
    // only your own cities
    const other = g.playerCities(1)[0];
    if (other) expect(g.exec(0, { t: 'governor', city: other.id, on: true }).ok).toBe(false);
  });

  it('a governed city no longer asks for the player’s attention', () => {
    const g = world();
    const city = g.playerCities(0)[0];
    const v0 = buildView(g, 0).cities.find((c) => c.mine)!;
    expect(v0.governor).toBe(false);
    g.exec(0, { t: 'governor', city: city.id, on: true });
    expect(buildView(g, 0).cities.find((c) => c.mine)!.governor).toBe(true);
  });
});

describe('auto-research', () => {
  it('picks the next technology when one is finished or none is set', () => {
    const g = world();
    const p = g.players[0];
    p.researching = null;
    expect(g.exec(0, { t: 'autoResearch', on: true }).ok).toBe(true);
    expect(p.autoResearch).toBe(true);
    expect(p.researching).not.toBeNull(); // chosen at once
    const first = p.researching!;
    p.research = 1e9;
    p.endedTurn = true;
    g.maybeProcessTurn();
    expect(p.techs).toContain(first);
    expect(p.researching).not.toBeNull();
    expect(p.researching).not.toBe(first);
    g.exec(0, { t: 'autoResearch', on: false });
    expect(p.autoResearch).toBe(false);
  });
});
