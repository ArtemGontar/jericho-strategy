/** Small deterministic noise helpers shared by the terrain, props and effects. */
export function hash2(ix: number, iy: number, seed = 0): number {
  let h = Math.imul(ix | 0, 374761393) ^ Math.imul(iy | 0, 668265263) ^ Math.imul(seed | 0, 2147483647);
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}
const fade = (t: number) => t * t * (3 - 2 * t);
export function vnoise(x: number, y: number, seed = 0): number {
  const ix = Math.floor(x);
  const iy = Math.floor(y);
  const fx = fade(x - ix);
  const fy = fade(y - iy);
  const a = hash2(ix, iy, seed) * (1 - fx) + hash2(ix + 1, iy, seed) * fx;
  const b = hash2(ix, iy + 1, seed) * (1 - fx) + hash2(ix + 1, iy + 1, seed) * fx;
  return a * (1 - fy) + b * fy;
}
export function fbm(x: number, y: number, seed = 0, oct = 4): number {
  let sum = 0;
  let amp = 0.5;
  let f = 1;
  let norm = 0;
  for (let i = 0; i < oct; i++) {
    sum += vnoise(x * f, y * f, seed + i * 31) * amp;
    norm += amp;
    amp *= 0.5;
    f *= 2.03;
  }
  return sum / norm;
}
/** mulberry32 seeded from a tile so props are stable between rebuilds */
export function rngFor(x: number, y: number, salt = 0): () => number {
  let a = (Math.imul(x + 1, 73856093) ^ Math.imul(y + 1, 19349663) ^ Math.imul(salt + 7, 83492791)) >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
/** GLSL noise used by the terrain, water and cloud shaders */
export const GLSL_NOISE = /* glsl */ `
float hash21(vec2 p){ p = fract(p*vec2(123.34, 456.21)); p += dot(p, p+45.32); return fract(p.x*p.y); }
float vnoise(vec2 p){
  vec2 i = floor(p); vec2 f = fract(p);
  f = f*f*(3.0-2.0*f);
  float a = hash21(i), b = hash21(i+vec2(1.,0.)), c = hash21(i+vec2(0.,1.)), d = hash21(i+vec2(1.,1.));
  return mix(mix(a,b,f.x), mix(c,d,f.x), f.y);
}
float fbm3(vec2 p){
  float s = 0.0, a = 0.5;
  for(int i=0;i<3;i++){ s += a*vnoise(p); p = p*2.03 + 17.1; a *= 0.5; }
  return s/0.875;
}
`;
