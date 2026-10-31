import { hexDistance, neighborsOf, tilesWithin, type Pos } from './hex';
import { Rng } from './rng';
import { LUXURIES, RESOURCES, TERRAIN } from './data';
import type { ResourceId, TerrainId, Tile } from './types';

function makeNoise(rng: Rng): (x: number, y: number) => number {
  const N = 256;
  const lattice = new Float32Array(N * N);
  for (let i = 0; i < lattice.length; i++) lattice[i] = rng.next();
  const at = (ix: number, iy: number) => lattice[((iy & 255) << 8) | (ix & 255)];
  const smooth = (t: number) => t * t * (3 - 2 * t);
  return (x, y) => {
    const ix = Math.floor(x);
    const iy = Math.floor(y);
    const fx = smooth(x - ix);
    const fy = smooth(y - iy);
    const a = at(ix, iy) * (1 - fx) + at(ix + 1, iy) * fx;
    const b = at(ix, iy + 1) * (1 - fx) + at(ix + 1, iy + 1) * fx;
    return a * (1 - fy) + b * fy;
  };
}

function fbm(noise: (x: number, y: number) => number, x: number, y: number, octaves: number): number {
  let sum = 0;
  let amp = 1;
  let norm = 0;
  let f = 1;
  for (let i = 0; i < octaves; i++) {
    sum += noise(x * f, y * f) * amp;
    norm += amp;
    amp *= 0.5;
    f *= 2;
  }
  return sum / norm;
}

export interface GeneratedMap {
  tiles: Tile[];
  starts: Pos[];
}

function blankTile(x: number, y: number, terrain: TerrainId): Tile {
  return {
    x, y, terrain, resource: null, improvement: null, road: false, owner: -1, city: -1,
    explored: 0, visible: 0, hut: false, work: null,
  };
}

export type MapStyle = 'continent' | 'islands';

/** Try several seed variants until a map with enough room for everyone comes out. */
export function generateMap(width: number, height: number, players: number, seed: number, style: MapStyle = 'continent'): GeneratedMap {
  for (let attempt = 0; attempt < 60; attempt++) {
    const result = tryGenerate(width, height, players, seed + attempt * 7919, style);
    if (result) return result;
  }
  if (style === 'islands') return generateMap(width, height, players, seed, 'continent');
  throw new Error('Could not generate a suitable map');
}

function tryGenerate(w: number, h: number, players: number, seed: number, style: MapStyle): GeneratedMap | null {
  const islands = style === 'islands';
  const rng = new Rng(seed);
  const hNoise = makeNoise(rng);
  const hillNoise = makeNoise(rng);
  const moistNoise = makeNoise(rng);
  const detail = makeNoise(rng);
  const total = w * h;

  // --- height field with a continental falloff so land clumps into one big mass
  const heights = new Float32Array(total);
  const cx = (w - 1) / 2;
  const cy = (h - 1) / 2;
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const nx = (x + 0.5 * (y & 1)) / w;
      const ny = y / h;
      const base = fbm(hNoise, nx * 4.2 + 3, ny * 4.2 + 7, 5);
      const dx = (x - cx) / (w * 0.5);
      const dy = (y - cy) / (h * 0.5);
      const d = Math.sqrt(dx * dx * 0.9 + dy * dy * 1.15);
      const mask = 1 - Math.min(1, Math.pow(d, 1.6));
      heights[y * w + x] = islands ? base * 0.9 + mask * 0.16 : base * 0.62 + mask * 0.62;
    }
  }
  const sorted = Array.from(heights).sort((a, b) => a - b);
  const landFraction = islands ? 0.36 : 0.46;
  const seaLevel = sorted[Math.floor(total * (1 - landFraction))];
  const landSorted = sorted.filter((v) => v > seaLevel);
  const rank = (v: number) => {
    // fraction of land tiles that are lower than v
    let lo = 0;
    let hi = landSorted.length;
    while (lo < hi) {
      const mid = (lo + hi) >> 1;
      if (landSorted[mid] < v) lo = mid + 1;
      else hi = mid;
    }
    return lo / Math.max(1, landSorted.length);
  };

  const tiles: Tile[] = [];
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const v = heights[y * w + x];
      const lat = Math.abs(y / (h - 1) - 0.5) * 2; // 0 equator .. 1 pole
      let terrain: TerrainId;
      if (v <= seaLevel) terrain = 'ocean';
      else {
        const r = rank(v);
        const nx = (x + 0.5 * (y & 1)) / w;
        const ny = y / h;
        const moist = fbm(moistNoise, nx * 3.4 + 11, ny * 3.4 + 5, 4) + (detail(nx * 17, ny * 17) - 0.5) * 0.12;
        const hill = fbm(hillNoise, nx * 6 + 2, ny * 6 + 9, 3);
        if (lat > 0.9) terrain = 'tundra';
        else if (r > 0.95 || (hill > 0.76 && r > 0.85)) terrain = 'mountain';
        else if (r > 0.88 || hill > 0.72) terrain = 'hills';
        else if (lat > 0.82) terrain = moist > 0.5 ? 'forest' : 'tundra';
        else if (moist < 0.33 && lat < 0.8) terrain = 'desert';
        else if (moist > 0.67) terrain = 'forest';
        else if (moist < 0.47) terrain = 'plains';
        else terrain = 'grass';
      }
      tiles.push(blankTile(x, y, terrain));
    }
  }
  const idx = (x: number, y: number) => y * w + x;

  // --- keep only the largest connected landmass (nobody can sail)
  const comp = new Int32Array(total).fill(-1);
  const sizes: number[] = [];
  for (let i = 0; i < total; i++) {
    if (comp[i] !== -1 || !TERRAIN[tiles[i].terrain].land) continue;
    const id = sizes.length;
    let count = 0;
    const stack = [i];
    comp[i] = id;
    while (stack.length) {
      const cur = stack.pop()!;
      count++;
      const t = tiles[cur];
      for (const n of neighborsOf(t.x, t.y, w, h)) {
        const ni = idx(n.x, n.y);
        if (comp[ni] === -1 && TERRAIN[tiles[ni].terrain].land && tiles[ni].terrain !== 'mountain') {
          comp[ni] = id;
          stack.push(ni);
        }
      }
    }
    sizes.push(count);
  }
  if (sizes.length === 0) return null;
  let main = 0;
  for (let i = 1; i < sizes.length; i++) if (sizes[i] > sizes[main]) main = i;
  const mainSize = sizes[main];
  // islands: every player needs a home island of their own; continents: one big landmass with room for all
  const homeSize = Math.max(26, Math.floor((w * h) / (players * 14)));
  const bigIslands = sizes.map((s, i) => ({ s, i })).filter((o) => o.s >= homeSize).sort((a, b) => b.s - a.s);
  if (islands ? bigIslands.length < players : mainSize < players * 55) return null;
  // small islands stay (ships can reach them); specks of one or two tiles do not
  const minKeep = 4;
  for (let i = 0; i < total; i++) {
    const t = tiles[i];
    if (!TERRAIN[t.terrain].land) continue;
    if (t.terrain === 'mountain') {
      // keep mountains only if they touch land that is kept
      let touches = false;
      for (const n of neighborsOf(t.x, t.y, w, h)) {
        const c = comp[idx(n.x, n.y)];
        if (c >= 0 && sizes[c] >= minKeep) touches = true;
      }
      if (!touches) t.terrain = 'ocean';
    } else if (sizes[comp[i]] < minKeep) t.terrain = 'ocean';
  }
  // a scatter of small islands across the sea gives ships somewhere to go (islands maps already have plenty)
  if (!islands) {
    const wanted = Math.round((w * h) / 150);
    const distLand = (x: number, y: number, r: number) => tilesWithin(x, y, r, w, h).some((q) => TERRAIN[tiles[idx(q.x, q.y)].terrain].land);
    const palette: TerrainId[] = ['grass', 'grass', 'plains', 'plains', 'forest', 'hills'];
    let made = 0;
    for (let tries = 0; tries < 400 && made < wanted; tries++) {
      const sx = 2 + Math.floor(rng.next() * (w - 4));
      const sy = 2 + Math.floor(rng.next() * (h - 4));
      if (tiles[idx(sx, sy)].terrain !== 'ocean' || distLand(sx, sy, 3)) continue;
      const target = 5 + Math.floor(rng.next() * 8);
      const blob: Tile[] = [tiles[idx(sx, sy)]];
      const seen = new Set<number>([idx(sx, sy)]);
      for (let k = 0; k < blob.length && blob.length < target; k++) {
        for (const n of neighborsOf(blob[k].x, blob[k].y, w, h)) {
          const ni = idx(n.x, n.y);
          if (seen.has(ni) || blob.length >= target || !rng.chance(0.62)) continue;
          seen.add(ni);
          if (tiles[ni].terrain === 'ocean' && !distLand(n.x, n.y, 2)) blob.push(tiles[ni]);
        }
      }
      if (blob.length < 4) continue;
      for (const t of blob) t.terrain = palette[Math.floor(rng.next() * palette.length)];
      made++;
    }
  }
  // coasts: ocean next to land
  for (const t of tiles) {
    if (t.terrain !== 'ocean') continue;
    for (const n of neighborsOf(t.x, t.y, w, h)) {
      if (TERRAIN[tiles[idx(n.x, n.y)].terrain].land) {
        t.terrain = 'coast';
        break;
      }
    }
  }

  // --- resources
  const placeResource = (res: ResourceId, chance: number) => {
    const def = RESOURCES[res];
    for (const t of tiles) {
      if (t.resource || !def.terrains.includes(t.terrain)) continue;
      if (rng.chance(chance)) t.resource = res;
    }
  };
  placeResource('wheat', 0.07);
  placeResource('cattle', 0.07);
  placeResource('fish', 0.12);
  placeResource('deer', 0.1);
  placeResource('horses', 0.05);
  placeResource('iron', 0.05);
  placeResource('gold', 0.04);
  // luxuries are scarcer: every civilisation has a reason to trade
  placeResource('wine', 0.035);
  placeResource('spices', 0.03);
  placeResource('gems', 0.03);
  placeResource('pearls', 0.05);

  // --- start positions: spread over the main landmass, each with decent surroundings
  const quality = (t: Tile): number => {
    let food = 0;
    let prod = 0;
    let land = 0;
    for (const n of tilesWithin(t.x, t.y, 2, w, h)) {
      const nt = tiles[idx(n.x, n.y)];
      const def = TERRAIN[nt.terrain];
      if (def.land && nt.terrain !== 'mountain') land++;
      food += def.food + (nt.resource ? RESOURCES[nt.resource].food : 0);
      prod += def.prod + (nt.resource ? RESOURCES[nt.resource].prod : 0);
    }
    return land >= 12 && food >= 17 && prod >= 6 ? food + prod * 1.2 + land * 0.5 : -1;
  };
  const homeIslands = new Set(bigIslands.slice(0, islands ? players : 0).map((o) => o.i));
  const candidates: Tile[] = tiles.filter(
    (t) => (islands ? homeIslands.has(comp[idx(t.x, t.y)]) : comp[idx(t.x, t.y)] === main) && ['grass', 'plains', 'hills', 'forest', 'desert'].includes(t.terrain) && quality(t) > 0,
  );
  if (candidates.length < players) return null;
  const starts: Tile[] = [];
  if (islands) {
    // one start per home island, on its best-looking tile
    for (const isle of homeIslands) {
      let best: Tile | null = null;
      let bq = -1;
      for (const c of candidates) {
        if (comp[idx(c.x, c.y)] !== isle) continue;
        const q = quality(c) + rng.next();
        if (q > bq) {
          bq = q;
          best = c;
        }
      }
      if (!best) return null;
      starts.push(best);
    }
    // seats are dealt in random order so no one is always on the biggest island
    for (let i = starts.length - 1; i > 0; i--) {
      const j = Math.floor(rng.next() * (i + 1));
      [starts[i], starts[j]] = [starts[j], starts[i]];
    }
  } else {
    const first = candidates[Math.floor(rng.next() * candidates.length)];
    starts.push(first);
  }
  while (starts.length < players) {
    let best: Tile | null = null;
    let bestScore = -1;
    for (const c of candidates) {
      let md = 999;
      for (const s of starts) md = Math.min(md, hexDistance(c.x, c.y, s.x, s.y));
      const score = md * 10 + quality(c) * 0.15 + rng.next();
      if (score > bestScore) {
        bestScore = score;
        best = c;
      }
    }
    if (!best) return null;
    starts.push(best);
  }
  let minDist = 999;
  for (let i = 0; i < starts.length; i++)
    for (let j = i + 1; j < starts.length; j++) minDist = Math.min(minDist, hexDistance(starts[i].x, starts[i].y, starts[j].x, starts[j].y));
  if (!islands && players > 1 && minDist < Math.max(7, Math.floor(Math.min(w, h) / (players > 3 ? 4 : 3)))) return null;

  // --- fairness pass: every start gets food, production, and a few tiles of room
  for (const s of starts) {
    if (s.terrain === 'desert' || s.terrain === 'tundra') s.terrain = 'plains';
    s.resource = null;
    const ring = tilesWithin(s.x, s.y, 2, w, h).filter((p) => !(p.x === s.x && p.y === s.y));
    const near = ring.map((p) => tiles[idx(p.x, p.y)]);
    const count = (pred: (t: Tile) => boolean) => near.filter(pred).length;
    const wantFood = 2 - count((t) => t.resource === 'wheat' || t.resource === 'cattle' || t.resource === 'fish');
    for (let k = 0; k < wantFood; k++) {
      const spot = rng.pick(near.filter((t) => !t.resource && (t.terrain === 'grass' || t.terrain === 'plains')));
      if (spot) spot.resource = rng.chance(0.5) ? 'wheat' : 'cattle';
    }
    if (count((t) => t.terrain === 'hills' || t.terrain === 'forest') < 2) {
      const spot = rng.pick(near.filter((t) => !t.resource && (t.terrain === 'plains' || t.terrain === 'grass')));
      if (spot) spot.terrain = 'hills';
    }
    if (count((t) => t.resource === 'iron' || t.resource === 'horses') < 1) {
      const spot = rng.pick(near.filter((t) => !t.resource && (t.terrain === 'plains' || t.terrain === 'hills')));
      if (spot) spot.resource = spot.terrain === 'hills' ? 'iron' : 'horses';
    }
    if (count((t) => t.terrain === 'mountain') > 1) {
      for (const t of near) if (t.terrain === 'mountain') t.terrain = 'hills';
    }
  }
  // luxuries: each start gets one kind within reach, and neighbours get different kinds, so trading is how you get the rest
  const lux = LUXURIES.slice();
  starts.forEach((s, i) => {
    const want = lux[i % lux.length];
    const reach = tilesWithin(s.x, s.y, 4, w, h).map((p) => tiles[idx(p.x, p.y)]).filter((t) => !(t.x === s.x && t.y === s.y));
    if (reach.some((t) => t.resource === want)) return;
    const def = RESOURCES[want];
    const spot = rng.pick(reach.filter((t) => !t.resource && def.terrains.includes(t.terrain)));
    if (spot) spot.resource = want;
  });
  // tribal villages: scattered over the land, never near a start
  const hutCount = Math.round((w * h) / 70);
  const land = tiles.filter((t) => ['grass', 'plains', 'desert', 'forest', 'hills', 'tundra'].includes(t.terrain) && !t.resource);
  let placed = 0;
  for (let tries = 0; tries < 600 && placed < hutCount; tries++) {
    const t = land[Math.floor(rng.next() * land.length)];
    if (!t || t.hut) continue;
    if (starts.some((s0) => hexDistance(s0.x, s0.y, t.x, t.y) < 6)) continue;
    if (tiles.some((o) => o.hut && hexDistance(o.x, o.y, t.x, t.y) < 5)) continue;
    t.hut = true;
    placed++;
  }
  return { tiles, starts: starts.map((s) => ({ x: s.x, y: s.y })) };
}
