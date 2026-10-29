/** Pointy-top hexes in "odd-r" offset coordinates (odd rows are shoved half a hex to the right). */
export interface Pos {
  x: number;
  y: number;
}

const EVEN_DIRS: ReadonlyArray<readonly [number, number]> = [
  [1, 0], [0, -1], [-1, -1], [-1, 0], [-1, 1], [0, 1],
];
const ODD_DIRS: ReadonlyArray<readonly [number, number]> = [
  [1, 0], [1, -1], [0, -1], [-1, 0], [0, 1], [1, 1],
];

export function neighborsOf(x: number, y: number, w: number, h: number): Pos[] {
  const dirs = (y & 1) === 1 ? ODD_DIRS : EVEN_DIRS;
  const out: Pos[] = [];
  for (const [dx, dy] of dirs) {
    const nx = x + dx;
    const ny = y + dy;
    if (nx >= 0 && ny >= 0 && nx < w && ny < h) out.push({ x: nx, y: ny });
  }
  return out;
}

export function hexDistance(ax: number, ay: number, bx: number, by: number): number {
  // cube coordinates: q = x - (y - (y&1)) / 2, r = y, s = -q - r
  const dq = ax - (ay - (ay & 1)) / 2 - (bx - (by - (by & 1)) / 2);
  const dr = ay - by;
  const ds = dq + dr;
  const aq = dq < 0 ? -dq : dq;
  const ar = dr < 0 ? -dr : dr;
  const as = ds < 0 ? -ds : ds;
  return aq > ar ? (aq > as ? aq : as) : (ar > as ? ar : as);
}

/** Offsets within `radius`, nearest first, for a row parity. The distance only depends on the parity of the row, so one table serves every tile. */
const OFFSETS: Array<Int16Array | undefined> = [];
function offsetsFor(parity: number, radius: number): Int16Array {
  const key = radius * 2 + parity;
  let t = OFFSETS[key];
  if (t) return t;
  const list: Array<[number, number, number]> = [];
  const y = parity;
  for (let dy = -radius; dy <= radius; dy++) {
    for (let dx = -radius - 1; dx <= radius + 1; dx++) {
      const d = hexDistance(0, y, dx, y + dy);
      if (d <= radius) list.push([dx, dy, d]);
    }
  }
  list.sort((a, b) => a[2] - b[2]);
  t = new Int16Array(list.length * 2);
  list.forEach((o, i) => { t![i * 2] = o[0]; t![i * 2 + 1] = o[1]; });
  OFFSETS[key] = t;
  return t;
}

/** Calls `fn(x, y)` for every tile within `radius` of (x,y), nearest first, clipped to the map. Allocation-free. */
export function forTilesWithin(x: number, y: number, radius: number, w: number, h: number, fn: (x: number, y: number) => void): void {
  const t = offsetsFor(y & 1, radius);
  for (let i = 0; i < t.length; i += 2) {
    const nx = x + t[i];
    const ny = y + t[i + 1];
    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
    fn(nx, ny);
  }
}

/** All tiles within `radius` of (x,y), nearest first, clipped to the map. */
export function tilesWithin(x: number, y: number, radius: number, w: number, h: number): Pos[] {
  const out: Pos[] = [];
  const t = offsetsFor(y & 1, radius);
  for (let i = 0; i < t.length; i += 2) {
    const nx = x + t[i];
    const ny = y + t[i + 1];
    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
    out.push({ x: nx, y: ny });
  }
  return out;
}

export const HEX_SQRT3 = Math.sqrt(3);

/** Pixel centre of a hex for a given hex "size" (centre-to-corner). */
export function hexToPixel(x: number, y: number, size: number): { px: number; py: number } {
  return { px: size * HEX_SQRT3 * (x + 0.5 * (y & 1)), py: size * 1.5 * y };
}

export function pixelToHex(px: number, py: number, size: number, w: number, h: number): Pos | null {
  // Convert to fractional axial then round in cube space, then back to odd-r offset.
  const qf = ((HEX_SQRT3 / 3) * px - (1 / 3) * py) / size;
  const rf = ((2 / 3) * py) / size;
  let q = Math.round(qf);
  let r = Math.round(rf);
  const s = Math.round(-qf - rf);
  const dq = Math.abs(q - qf);
  const dr = Math.abs(r - rf);
  const ds = Math.abs(s - (-qf - rf));
  if (dq > dr && dq > ds) q = -r - s;
  else if (dr > ds) r = -q - s;
  const x = q + (r - (r & 1)) / 2;
  const y = r;
  if (x < 0 || y < 0 || x >= w || y >= h) return null;
  return { x, y };
}
