import { generateMap } from '../src/shared/mapgen';
const [w, h, p, seed] = [Number(process.argv[2] ?? 40), Number(process.argv[3] ?? 26), Number(process.argv[4] ?? 4), Number(process.argv[5] ?? 1)];
const m = generateMap(w, h, p, seed);
const ch: Record<string, string> = { ocean: ' ', coast: '.', grass: '"', plains: '_', desert: 'd', tundra: 't', forest: 'f', hills: 'n', mountain: 'M' };
for (let y = 0; y < h; y++) {
  let line = y & 1 ? ' ' : '';
  for (let x = 0; x < w; x++) {
    const t = m.tiles[y * w + x];
    const s = m.starts.findIndex((q) => q.x === x && q.y === y);
    line += (s >= 0 ? String(s) : ch[t.terrain]) + ' ';
  }
  console.log(line);
}
console.log('starts', JSON.stringify(m.starts));
const hist: Record<string, number> = {};
for (const t of m.tiles) hist[t.terrain] = (hist[t.terrain] ?? 0) + 1;
console.log(hist);
