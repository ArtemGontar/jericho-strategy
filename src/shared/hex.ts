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

function toCube(x: number, y: number): [number, number, number] {
  const q = x - (y - (y & 1)) / 2;
  const r = y;
  return [q, r, -q - r];
}

export function hexDistance(ax: number, ay: number, bx: number, by: number): number {
  const [aq, ar, as] = toCube(ax, ay);
  const [bq, br, bs] = toCube(bx, by);
  return Math.max(Math.abs(aq - bq), Math.abs(ar - br), Math.abs(as - bs));
}

/** All tiles within `radius` of (x,y), nearest first, clipped to the map. */
export function tilesWithin(x: number, y: number, radius: number, w: number, h: number): Pos[] {
  const out: Pos[] = [];
  for (let dy = -radius; dy <= radius; dy++) {
    for (let dx = -radius - 1; dx <= radius + 1; dx++) {
      const nx = x + dx;
      const ny = y + dy;
      if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
      if (hexDistance(x, y, nx, ny) <= radius) out.push({ x: nx, y: ny });
    }
  }
  out.sort((a, b) => hexDistance(x, y, a.x, a.y) - hexDistance(x, y, b.x, b.y));
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
