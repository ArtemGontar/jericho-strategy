import {
  Vector3, Box3, Sphere, BufferAttribute, BufferGeometry, CanvasTexture, Color, DataTexture, Group, LinearFilter, LinearMipmapLinearFilter, Mesh, MeshStandardMaterial,
  PlaneGeometry, RedFormat, SRGBColorSpace, ShaderMaterial, UnsignedByteType, Vector2, type Scene, type WebGLProgramParametersWithUniforms,
} from 'three';
import { TERRAIN_LIST, RESOURCE_LIST, type GameView } from '../../shared/view';
import type { TerrainId } from '../../shared/types';
import { EDGE_NEIGHBOR_EVEN, EDGE_NEIGHBOR_ODD } from './hexmath';
import { GLSL_NOISE, fbm, hash2 } from './noise';
import { FOG_FRAGMENT, type SharedUniforms } from './fog';

export const SQ3 = Math.sqrt(3);
export const tileXZ = (x: number, y: number): [number, number] => [SQ3 * (x + 0.5 * (y & 1)) + SQ3 / 2, 1.5 * y + 1];

/** Scene position of a possibly fractional tile coordinate (animated units sit between tile centres). */
export function tileXZf(tx: number, ty: number): [number, number] {
  const fx = Math.floor(tx);
  const fy = Math.floor(ty);
  const [ax, az] = tileXZ(fx, fy);
  const [bx, bz] = tileXZ(fx + 1, fy);
  const [cx, cz] = tileXZ(fx, fy + 1);
  const gx = tx - fx;
  const gy = ty - fy;
  return [ax + (bx - ax) * gx + (cx - ax) * gy, az + (bz - az) * gx + (cz - az) * gy];
}

const TERRAIN_H: Record<TerrainId, number> = {
  ocean: -0.5, coast: -0.17, grass: 0.06, plains: 0.045, desert: 0.03, tundra: 0.06, forest: 0.08, hills: 0.34, mountain: 0.78,
};
const TERRAIN_COLOR: Record<TerrainId, string> = {
  ocean: '#1f4d6e', coast: '#d3c18d', grass: '#5f9a35', plains: '#bba85a', desert: '#dcc185', tundra: '#c4cfd1',
  forest: '#3f7030', hills: '#8c9a48', mountain: '#7b7469',
};

const RES = 5; // vertices per scene unit
const FOG_RES = 2.5;
const MARGIN = 2.2;
const SIG_H = 0.42;
const SIG_C = 0.3;

interface TileData {
  h: number;
  r: number;
  g: number;
  b: number;
  forest: number;
  desert: number;
  tundra: number;
  rock: number;
  mount: number;
  hill: number;
  flat: number;
  known: boolean;
}

const VOID_TILE: TileData = { h: -0.45, r: 0.02, g: 0.06, b: 0.1, forest: 0, desert: 0, tundra: 0, rock: 0, mount: 0, hill: 0, flat: 0, known: false };

export class Terrain {
  W = 0;
  H = 0;
  extX = 0;
  extZ = 0;
  x0 = 0;
  z0 = 0;
  gx = 0;
  gz = 0;
  heights = new Float32Array(0);
  water: Mesh | null = null;
  /** The ground is cut into square blocks of the vertex grid that share one set of vertex buffers, so the ones off screen are skipped. */
  private ground = new Group();
  private chunks: Array<{ mesh: Mesh; i0: number; i1: number; j0: number; j1: number }> = [];
  private attrs: BufferAttribute[] = [];
  private built = false;
  get mesh(): Group | null {
    return this.built ? this.ground : null;
  }
  private tiles: TileData[] = [];
  private sig = '';
  private fogSig = '';
  private decalSig = '';
  private fogTex: DataTexture;
  private fogData = new Uint8Array(4);
  private fogW = 1;
  private fogH = 1;
  private depthTex: DataTexture;
  private decalCanvas = document.createElement('canvas');
  private decalTex: CanvasTexture;
  uniforms: SharedUniforms;
  private waterMat: ShaderMaterial;
  private terrainMat: MeshStandardMaterial;
  private scene: Scene;
  private decalOrg = new Vector2(0, 0);
  private decalExt = new Vector2(1, 1);
  private depthOrg = new Vector2(0, 0);
  private depthExt = new Vector2(1, 1);

  constructor(scene: Scene) {
    this.scene = scene;
    this.fogTex = new DataTexture(this.fogData, 1, 1, RedFormat, UnsignedByteType);
    this.fogTex.minFilter = LinearFilter;
    this.fogTex.magFilter = LinearFilter;
    this.fogTex.needsUpdate = true;
    this.depthTex = new DataTexture(new Uint8Array(4), 1, 1, RedFormat, UnsignedByteType);
    this.depthTex.minFilter = LinearFilter;
    this.depthTex.magFilter = LinearFilter;
    this.depthTex.needsUpdate = true;
    this.decalCanvas.width = 4;
    this.decalCanvas.height = 4;
    this.decalTex = new CanvasTexture(this.decalCanvas);
    this.decalTex.colorSpace = SRGBColorSpace;
    // the shader reads v straight from the world z, so the canvas must not be flipped on upload
    this.decalTex.flipY = false;
    this.uniforms = {
      uFog: { value: this.fogTex },
      uFogOrg: { value: new Vector2() },
      uFogExt: { value: new Vector2(1, 1) },
      uVoid: { value: new Color(0x0a1120) },
      uTime: { value: 0 },
    };
    // the void colour is applied after tone mapping, so it is a plain display-space value
    const v = this.uniforms.uVoid.value as Color;
    v.r = 0.039;
    v.g = 0.067;
    v.b = 0.125;
    this.terrainMat = this.makeTerrainMaterial();
    this.waterMat = this.makeWaterMaterial();
  }

  // ------------------------------------------------------------------ materials
  private makeTerrainMaterial(): MeshStandardMaterial {
    const mat = new MeshStandardMaterial({ vertexColors: true, roughness: 0.96, metalness: 0 });
    const u = this.uniforms;
    const decal = { value: this.decalTex };
    const decalOrg = { value: this.decalOrg };
    const decalExt = { value: this.decalExt };
    mat.onBeforeCompile = (shader: WebGLProgramParametersWithUniforms) => {
      Object.assign(shader.uniforms, u, { uDecal: decal, uDecalOrg: decalOrg, uDecalExt: decalExt });
      shader.vertexShader = shader.vertexShader
        .replace('#include <common>', '#include <common>\nattribute vec4 aMask;\nvarying vec4 vMask;\nvarying vec3 vWorldF;')
        .replace('#include <begin_vertex>', '#include <begin_vertex>\nvMask = aMask;\nvWorldF = (modelMatrix * vec4(transformed, 1.0)).xyz;');
      shader.fragmentShader = shader.fragmentShader
        .replace(
          '#include <common>',
          `#include <common>
          varying vec4 vMask;
          varying vec3 vWorldF;
          uniform sampler2D uFog; uniform vec2 uFogOrg; uniform vec2 uFogExt; uniform vec3 uVoid; uniform float uTime;
          uniform sampler2D uDecal; uniform vec2 uDecalOrg; uniform vec2 uDecalExt;
          ${GLSL_NOISE}`,
        )
        .replace(
          '#include <color_fragment>',
          `#include <color_fragment>
          {
            vec3 wp = vWorldF;
            float n1 = fbm3(wp.xz * 2.4);
            float n2 = fbm3(wp.xz * 9.0);
            float n3 = vnoise(wp.xz * 46.0);
            vec3 c = diffuseColor.rgb;
            c *= 0.84 + 0.32 * n1;
            c *= 0.93 + 0.14 * n2;
            vec3 fn = normalize(cross(dFdx(wp), dFdy(wp)));
            float slope = 1.0 - abs(fn.y);
            // forest floor: darker, mossy
            c = mix(c, c * vec3(0.66, 0.78, 0.58), vMask.x * 0.85);
            // grass blades read as fine streaks
            float blades = vnoise(vec2(wp.x * 60.0, wp.z * 22.0));
            c *= 1.0 + (blades - 0.5) * 0.16 * (1.0 - vMask.y) * (1.0 - vMask.w);
            // desert ripples
            float dune = sin((wp.x * 1.4 + wp.z * 2.2) * 3.2 + n1 * 7.0) * 0.5 + 0.5;
            c = mix(c, c * vec3(1.1, 1.06, 0.94), vMask.y * dune * 0.55);
            c = mix(c, c * vec3(0.86, 0.78, 0.64), vMask.y * (1.0 - dune) * 0.35);
            // rock on steep ground
            float rock = smoothstep(0.1, 0.34, slope + (n2 - 0.5) * 0.3 + vMask.w * 0.12);
            vec3 rockCol = mix(vec3(0.15, 0.135, 0.12), vec3(0.36, 0.32, 0.27), n2 * 0.7 + n3 * 0.3);
            float strata = sin(wp.y * 38.0 + n1 * 5.0) * 0.5 + 0.5;
            rockCol *= 0.88 + 0.22 * strata;
            c = mix(c, rockCol, rock * clamp(0.25 + vMask.w * 1.1, 0.0, 1.0));
            // snow high up and on the tundra
            float snowLine = 1.3 + (n1 - 0.5) * 0.3 - slope * 0.5;
            float snow = smoothstep(snowLine, snowLine + 0.09, wp.y);
            c = mix(c, vec3(0.88, 0.92, 0.98) * (0.94 + 0.08 * n2), snow);
            c = mix(c, vec3(0.62, 0.7, 0.74) * (0.9 + 0.2 * n2), vMask.z * 0.7 * (1.0 - rock));
            // wet sand along the shore and under the water
            float shore = (1.0 - smoothstep(0.01, 0.085, wp.y)) * step(-0.3, wp.y);
            c = mix(c, vec3(0.62, 0.5, 0.31) * (0.9 + 0.2 * n2), shore * (1.0 - vMask.w) * 0.8);
            c *= mix(1.0, 0.72, 1.0 - smoothstep(-0.04, 0.02, wp.y));
            // roads, fields and other things painted on the ground
            vec2 duv = (wp.xz - uDecalOrg) / uDecalExt;
            if (duv.x > 0.0 && duv.y > 0.0 && duv.x < 1.0 && duv.y < 1.0) {
              vec4 d = texture2D(uDecal, duv);
              c = mix(c, d.rgb, d.a);
            }
            diffuseColor.rgb = c;
          }`,
        )
        .replace(
          '#include <dithering_fragment>',
          `#include <dithering_fragment>
          {
            float cl = smoothstep(0.48, 0.78, fbm3(vWorldF.xz * 0.16 + vec2(uTime * 0.035, uTime * 0.012)));
            gl_FragColor.rgb *= 1.0 - cl * 0.26;
          }
          ${FOG_FRAGMENT}`,
        );
    };
    mat.customProgramCacheKey = () => 'terrain';
    return mat;
  }

  private makeWaterMaterial(): ShaderMaterial {
    const u = this.uniforms;
    return new ShaderMaterial({
      transparent: true,
      depthWrite: false,
      uniforms: {
        ...u,
        uDepth: { value: this.depthTex },
        uDepthOrg: { value: this.depthOrg },
        uDepthExt: { value: this.depthExt },
        uSunDir: { value: new Vector3(-0.45, 0.8, 0.5) },
        uSunTint: { value: new Vector3(1, 0.94, 0.82) },
      },
      vertexShader: /* glsl */ `
        varying vec3 vWorldF;
        void main(){
          vec4 wp = modelMatrix * vec4(position, 1.0);
          vWorldF = wp.xyz;
          gl_Position = projectionMatrix * viewMatrix * wp;
        }`,
      fragmentShader: /* glsl */ `
        varying vec3 vWorldF;
        uniform sampler2D uFog; uniform vec2 uFogOrg; uniform vec2 uFogExt; uniform vec3 uVoid; uniform float uTime;
        uniform sampler2D uDepth; uniform vec2 uDepthOrg; uniform vec2 uDepthExt; uniform vec3 uSunDir; uniform vec3 uSunTint;
        ${GLSL_NOISE}
        void main(){
          vec2 p = vWorldF.xz;
          float depth = texture2D(uDepth, (p - uDepthOrg) / uDepthExt).r * 0.55;
          float t = uTime;
          float w1 = fbm3(p * 3.2 + vec2(t * 0.07, t * 0.04));
          float w2 = fbm3(p * 3.2 * 1.7 + vec2(-t * 0.05, t * 0.08) + 17.0);
          float w3 = fbm3(p * 9.0 + vec2(t * 0.12, -t * 0.09) + 5.0);
          vec3 n = normalize(vec3((w1 - 0.5) * 0.55 + (w3 - 0.5) * 0.35, 1.0, (w2 - 0.5) * 0.55 + (w3 - 0.5) * 0.3));
          vec3 V = normalize(cameraPosition - vWorldF);
          vec3 L = normalize(uSunDir);
          vec3 H = normalize(L + V);
          float spec = pow(max(dot(n, H), 0.0), 120.0);
          float spec2 = pow(max(dot(n, H), 0.0), 18.0) * 0.12;
          float fres = pow(1.0 - max(dot(n, V), 0.0), 3.0);
          float dd = smoothstep(0.0, 0.46, depth);
          vec3 shallow = vec3(0.07, 0.5, 0.52);
          vec3 mid = vec3(0.012, 0.13, 0.3);
          vec3 deep = vec3(0.004, 0.034, 0.14);
          vec3 col = mix(shallow, mid, smoothstep(0.0, 0.2, depth));
          col = mix(col, deep, smoothstep(0.18, 0.5, depth));
          col += (w1 - 0.5) * 0.07;
          // sun caustics in the shallows
          float caus = pow(1.0 - abs(fbm3(p * 14.0 + t * 0.25) * 2.0 - 1.0), 6.0);
          col += vec3(0.5, 0.9, 0.8) * caus * 0.07 * (1.0 - dd);
          col = mix(col, vec3(0.2, 0.42, 0.62), fres * 0.45);
          col += uSunTint * (spec * 0.9 + spec2);
          // surf where the water meets land
          float edge = depth + (vnoise(p * 15.0 + t * 0.35) - 0.5) * 0.04;
          float foam = 1.0 - smoothstep(0.004, 0.05, edge);
          foam *= 0.55 + 0.45 * sin(t * 1.9 + depth * 70.0 + w1 * 4.0);
          float cap = 0.0;
          col = mix(col, vec3(0.96, 0.99, 1.0), clamp(foam * 0.85 + cap, 0.0, 1.0));
          float alpha = smoothstep(0.0, 0.034, depth) * mix(0.72, 0.985, smoothstep(0.0, 0.3, depth));
          alpha = max(alpha, foam * 0.55);
          gl_FragColor = vec4(col, alpha);
          #include <tonemapping_fragment>
          #include <colorspace_fragment>
          ${FOG_FRAGMENT}
        }`,
    });
  }

  // ------------------------------------------------------------------ data
  /** Sample the smoothed ground height at a scene position. */
  heightAt(X: number, Z: number): number {
    if (!this.gx) return 0;
    const fx = (X - this.x0) * RES;
    const fz = (Z - this.z0) * RES;
    const ix = Math.max(0, Math.min(this.gx - 2, Math.floor(fx)));
    const iz = Math.max(0, Math.min(this.gz - 2, Math.floor(fz)));
    const tx = Math.max(0, Math.min(1, fx - ix));
    const tz = Math.max(0, Math.min(1, fz - iz));
    const h = this.heights;
    const a = h[iz * this.gx + ix] * (1 - tx) + h[iz * this.gx + ix + 1] * tx;
    const b = h[(iz + 1) * this.gx + ix] * (1 - tx) + h[(iz + 1) * this.gx + ix + 1] * tx;
    return a * (1 - tz) + b * tz;
  }
  heightAtTile(x: number, y: number): number {
    const [X, Z] = tileXZ(x, y);
    return Math.max(0, this.heightAt(X, Z));
  }

  private tileData(_view: GameView, x: number, y: number): TileData {
    if (x < 0 || y < 0 || x >= this.W || y >= this.H) return OCEAN_TILE;
    return this.tiles[y * this.W + x] ?? VOID_TILE;
  }

  /** Re-derive the ground mesh when the known world changed. */
  /** A cheap fingerprint of everything the ground mesh is built from. */
  signature(view: GameView, cityKeys: Set<number>): string {
    const W = view.settings.width;
    const H = view.settings.height;
    let acc = 0;
    for (let i = 0; i < W * H; i++) {
      acc = (Math.imul(acc, 31) + view.terrain[i] * 3 + (view.state[i] > 0 ? 1 : 0) + (view.improvement[i] === 1 ? 7 : 0)) | 0;
    }
    for (const k of cityKeys) acc = (Math.imul(acc, 17) + k) | 0;
    return `${W}x${H}:${acc}`;
  }

  get currentSig(): string {
    return this.sig;
  }

  setView(view: GameView, cityKeys: Set<number>, sig = this.signature(view, cityKeys)): boolean {
    if (sig === this.sig) return false;
    this.sig = sig;
    this.build(view, cityKeys);
    return true;
  }

  private pos = new Float32Array(0);
  private nor = new Float32Array(0);
  private col = new Float32Array(0);
  private msk = new Float32Array(0);
  private depthData = new Uint8Array(0);
  private tileKeys = new Int32Array(0);

  private build(view: GameView, cityKeys: Set<number>): void {
    const W = view.settings.width;
    const H = view.settings.height;
    const fresh = !this.built || this.W !== W || this.H !== H;
    this.W = W;
    this.H = H;
    this.extX = SQ3 * (W + 0.5);
    this.extZ = 1.5 * H + 0.5;
    this.x0 = -MARGIN;
    this.z0 = -MARGIN;
    const gx = Math.ceil((this.extX + MARGIN * 2) * RES) + 1;
    const gz = Math.ceil((this.extZ + MARGIN * 2) * RES) + 1;
    this.gx = gx;
    this.gz = gz;
    // per-tile attributes
    const c = new Color();
    const tiles: TileData[] = new Array(W * H);
    const keys = new Int32Array(W * H);
    for (let i = 0; i < W * H; i++) {
      const known = view.state[i] > 0;
      if (!known) {
        tiles[i] = VOID_TILE;
        keys[i] = -1;
        continue;
      }
      const t = TERRAIN_LIST[view.terrain[i]];
      const x = i % W;
      const y = (i / W) | 0;
      c.set(TERRAIN_COLOR[t]);
      const jit = 0.93 + hash2(x, y, 5) * 0.14;
      const isCity = cityKeys.has(i);
      const flat = isCity ? 1 : view.improvement[i] === 1 ? 0.7 : view.improvement[i] === 2 ? 0.3 : 0;
      keys[i] = view.terrain[i] * 8 + Math.round(flat * 4);
      tiles[i] = {
        h: TERRAIN_H[t],
        r: c.r * jit,
        g: c.g * jit,
        b: c.b * jit,
        forest: t === 'forest' ? 1 : 0,
        desert: t === 'desert' ? 1 : 0,
        tundra: t === 'tundra' ? 1 : 0,
        rock: t === 'mountain' ? 1 : t === 'hills' ? 0.4 : 0,
        mount: t === 'mountain' ? 1 : 0,
        hill: t === 'hills' ? 1 : 0,
        flat,
        known: true,
      };
    }
    this.tiles = tiles;

    // which part of the mesh needs recomputing? Newly seen tiles only touch their own neighbourhood.
    let i0 = 0;
    let i1 = gx - 1;
    let j0 = 0;
    let j1 = gz - 1;
    if (!fresh && this.tileKeys.length === keys.length) {
      let minX = 1e9, maxX = -1e9, minZ = 1e9, maxZ = -1e9, n = 0;
      for (let i = 0; i < keys.length; i++) {
        if (keys[i] === this.tileKeys[i]) continue;
        n++;
        const [X, Z] = tileXZ(i % W, (i / W) | 0);
        minX = Math.min(minX, X);
        maxX = Math.max(maxX, X);
        minZ = Math.min(minZ, Z);
        maxZ = Math.max(maxZ, Z);
      }
      if (n === 0) {
        this.tileKeys = keys;
        return;
      }
      if (n < keys.length * 0.3) {
        const pad = 2.1;
        i0 = Math.max(0, Math.floor((minX - pad - this.x0) * RES));
        i1 = Math.min(gx - 1, Math.ceil((maxX + pad - this.x0) * RES));
        j0 = Math.max(0, Math.floor((minZ - pad - this.z0) * RES));
        j1 = Math.min(gz - 1, Math.ceil((maxZ + pad - this.z0) * RES));
      }
    }
    this.tileKeys = keys;
    if (fresh) {
      this.pos = new Float32Array(gx * gz * 3);
      this.nor = new Float32Array(gx * gz * 3);
      this.col = new Float32Array(gx * gz * 3);
      this.msk = new Float32Array(gx * gz * 4);
      this.heights = new Float32Array(gx * gz);
      this.depthData = new Uint8Array(gx * gz);
    }
    this.fillVertices(view, i0, i1, j0, j1);
    this.fillNormals(Math.max(0, i0 - 1), Math.min(gx - 1, i1 + 1), Math.max(0, j0 - 1), Math.min(gz - 1, j1 + 1));

    if (fresh) {
      for (const c of this.chunks) {
        this.ground.remove(c.mesh);
        c.mesh.geometry.dispose();
      }
      this.chunks = [];
      this.attrs = [
        new BufferAttribute(this.pos, 3),
        new BufferAttribute(this.nor, 3),
        new BufferAttribute(this.col, 3),
        new BufferAttribute(this.msk, 4),
      ];
      const names = ['position', 'normal', 'color', 'aMask'];
      const CH = 96;
      for (let cj = 0; cj < gz - 1; cj += CH) {
        for (let ci = 0; ci < gx - 1; ci += CH) {
          const ci1 = Math.min(gx - 1, ci + CH);
          const cj1 = Math.min(gz - 1, cj + CH);
          const index = new Uint32Array((ci1 - ci) * (cj1 - cj) * 6);
          let n = 0;
          for (let j = cj; j < cj1; j++) {
            for (let i = ci; i < ci1; i++) {
              const a = j * gx + i;
              const b = a + 1;
              const cc = a + gx;
              const d = cc + 1;
              index[n++] = a;
              index[n++] = cc;
              index[n++] = b;
              index[n++] = b;
              index[n++] = cc;
              index[n++] = d;
            }
          }
          const g = new BufferGeometry();
          g.setIndex(new BufferAttribute(index, 1));
          names.forEach((k, q) => g.setAttribute(k, this.attrs[q]));
          const mesh = new Mesh(g, this.terrainMat);
          mesh.receiveShadow = true;
          mesh.castShadow = true;
          this.ground.add(mesh);
          this.chunks.push({ mesh, i0: ci, i1: ci1, j0: cj, j1: cj1 });
        }
      }
      if (!this.ground.parent) this.scene.add(this.ground);
      this.built = true;
    } else {
      // only the rows that were recomputed need to travel to the GPU
      const rows = Math.min(gz - 1, j1 + 1) - Math.max(0, j0 - 1) + 1;
      const first = Math.max(0, j0 - 1);
      for (const at of this.attrs) at.addUpdateRange(first * gx * at.itemSize, rows * gx * at.itemSize);
      for (const at of this.attrs) at.needsUpdate = true;
    }
    this.updateChunkBounds();

    // water depth map on the same grid
    if (fresh) {
      this.depthTex.dispose();
      this.depthTex = new DataTexture(this.depthData, gx, gz, RedFormat, UnsignedByteType);
      this.depthTex.minFilter = LinearFilter;
      this.depthTex.magFilter = LinearFilter;
      this.waterMat.uniforms.uDepth.value = this.depthTex;
    }
    this.depthTex.needsUpdate = true;
    this.depthOrg.set(this.x0 - 0.5 / RES, this.z0 - 0.5 / RES);
    this.depthExt.set(gx / RES, gz / RES);
    if (!this.water) {
      this.water = new Mesh(new PlaneGeometry(1, 1), this.waterMat);
      this.water.rotation.x = -Math.PI / 2;
      this.water.renderOrder = 2;
      this.water.frustumCulled = false;
      this.scene.add(this.water);
    }
    this.water.scale.set(this.extX + MARGIN * 2, this.extZ + MARGIN * 2, 1);
    this.water.position.set(this.x0 + (this.extX + MARGIN * 2) / 2, 0.004, this.z0 + (this.extZ + MARGIN * 2) / 2);
    this.decalSig = '';
    this.fogSig = '';
  }

  private box = new Box3();
  private updateChunkBounds(): void {
    const gx = this.gx;
    const pos = this.pos;
    for (const c of this.chunks) {
      let mnx = 1e9, mny = 1e9, mnz = 1e9, mxx = -1e9, mxy = -1e9, mxz = -1e9;
      for (let j = c.j0; j <= c.j1; j++) {
        for (let i = c.i0; i <= c.i1; i++) {
          const k = (j * gx + i) * 3;
          const x = pos[k], y = pos[k + 1], z = pos[k + 2];
          if (x < mnx) mnx = x;
          if (x > mxx) mxx = x;
          if (y < mny) mny = y;
          if (y > mxy) mxy = y;
          if (z < mnz) mnz = z;
          if (z > mxz) mxz = z;
        }
      }
      const g = c.mesh.geometry;
      this.box.min.set(mnx, mny, mnz);
      this.box.max.set(mxx, mxy, mxz);
      g.boundingBox = (g.boundingBox ?? new Box3()).copy(this.box);
      g.boundingSphere = this.box.getBoundingSphere(g.boundingSphere ?? new Sphere());
    }
  }

  private fillVertices(view: GameView, i0: number, i1: number, j0: number, j1: number): void {
    const gx = this.gx;
    const { pos, col, msk, heights, depthData } = this;
    const cand: Array<[TileData, number]> = [];
    const twoSh = 2 * SIG_H * SIG_H;
    const twoSc = 2 * SIG_C * SIG_C;
    for (let j = j0; j <= j1; j++) {
      const Z = this.z0 + j / RES;
      const r0 = Math.round((Z - 1) / 1.5);
      for (let i = i0; i <= i1; i++) {
        const X = this.x0 + i / RES;
        cand.length = 0;
        for (let rr = r0 - 1; rr <= r0 + 1; rr++) {
          const c0 = Math.round((X - SQ3 / 2) / SQ3 - 0.5 * (rr & 1));
          for (let cc = c0 - 1; cc <= c0 + 1; cc++) {
            const tcx = SQ3 * (cc + 0.5 * (rr & 1)) + SQ3 / 2;
            const tcz = 1.5 * rr + 1;
            const d2 = (X - tcx) * (X - tcx) + (Z - tcz) * (Z - tcz);
            if (d2 > 2.4) continue;
            cand.push([this.tileData(view, cc, rr), d2]);
          }
        }
        let sh = 0, sc = 0, hh = 0, cr = 0, cg = 0, cb = 0, mf = 0, md = 0, mt = 0, mr = 0, mm = 0, mh = 0, fl = 0;
        for (const [t, d2] of cand) {
          const wh = Math.exp(-d2 / twoSh);
          const wc = Math.exp(-d2 / twoSc);
          sh += wh;
          sc += wc;
          hh += t.h * wh;
          mm += t.mount * wh;
          mh += t.hill * wh;
          fl += t.flat * wh;
          cr += t.r * wc;
          cg += t.g * wc;
          cb += t.b * wc;
          mf += t.forest * wc;
          md += t.desert * wc;
          mt += t.tundra * wc;
          mr += t.rock * wc;
        }
        if (sh < 1e-4) {
          sh = 1;
          hh = -0.45;
        }
        if (sc < 1e-4) sc = 1;
        let h = hh / sh;
        const mount = mm / sh;
        const hill = mh / sh;
        const flat = Math.min(1, fl / sh);
        const rough = 1 - flat * 0.85;
        const nA = fbm(X * 1.9, Z * 1.9, 3, 3);
        const ridge = 1 - Math.abs(2 * fbm(X * 3.1 + 11, Z * 3.1 + 7, 9, 4) - 1);
        h += (nA - 0.5) * 0.3 * hill * rough;
        h += (ridge * ridge * 0.85 + (nA - 0.5) * 0.25) * mount * rough;
        h += mount * mount * 0.14;
        if (h > -0.02) h += (fbm(X * 5, Z * 5, 21, 2) - 0.5) * 0.035 * rough;
        const k = j * gx + i;
        heights[k] = h;
        depthData[k] = Math.max(0, Math.min(255, Math.round((-h / 0.55) * 255)));
        pos[k * 3] = X;
        pos[k * 3 + 1] = h;
        pos[k * 3 + 2] = Z;
        col[k * 3] = cr / sc;
        col[k * 3 + 1] = cg / sc;
        col[k * 3 + 2] = cb / sc;
        msk[k * 4] = mf / sc;
        msk[k * 4 + 1] = md / sc;
        msk[k * 4 + 2] = mt / sc;
        msk[k * 4 + 3] = Math.min(1, mr / sc);
      }
    }
  }

  private fillNormals(i0: number, i1: number, j0: number, j1: number): void {
    const { gx, gz, heights, nor } = this;
    const step = 1 / RES;
    for (let j = j0; j <= j1; j++) {
      for (let i = i0; i <= i1; i++) {
        const il = Math.max(0, i - 1);
        const ir = Math.min(gx - 1, i + 1);
        const jl = Math.max(0, j - 1);
        const jr = Math.min(gz - 1, j + 1);
        const dx = (heights[j * gx + ir] - heights[j * gx + il]) / ((ir - il) * step);
        const dz = (heights[jr * gx + i] - heights[jl * gx + i]) / ((jr - jl) * step);
        const inv = 1 / Math.sqrt(dx * dx + 1 + dz * dz);
        const k = j * gx + i;
        nor[k * 3] = -dx * inv;
        nor[k * 3 + 1] = inv;
        nor[k * 3 + 2] = -dz * inv;
      }
    }
  }

  // ------------------------------------------------------------------ fog of war
  updateFog(view: GameView): void {
    const W = view.settings.width;
    const H = view.settings.height;
    let acc = 0;
    for (let i = 0; i < W * H; i++) acc = (Math.imul(acc, 33) + view.state[i] + 1) | 0;
    const sig = `${W}:${acc}`;
    if (sig === this.fogSig) return;
    this.fogSig = sig;
    const fw = Math.ceil((this.extX + MARGIN * 2) * FOG_RES);
    const fh = Math.ceil((this.extZ + MARGIN * 2) * FOG_RES);
    if (fw !== this.fogW || fh !== this.fogH) {
      this.fogW = fw;
      this.fogH = fh;
      this.fogData = new Uint8Array(fw * fh);
      this.fogTex.dispose();
      this.fogTex = new DataTexture(this.fogData, fw, fh, RedFormat, UnsignedByteType);
      this.fogTex.minFilter = LinearFilter;
      this.fogTex.magFilter = LinearFilter;
      this.uniforms.uFog.value = this.fogTex;
    }
    const sigma = 0.36;
    const two = 2 * sigma * sigma;
    const out = this.fogData;
    for (let j = 0; j < fh; j++) {
      const Z = this.z0 + (j + 0.5) / FOG_RES;
      const r0 = Math.round((Z - 1) / 1.5);
      for (let i = 0; i < fw; i++) {
        const X = this.x0 + (i + 0.5) / FOG_RES;
        let s = 0;
        let v = 0;
        for (let rr = r0 - 1; rr <= r0 + 1; rr++) {
          const c0 = Math.round((X - SQ3 / 2) / SQ3 - 0.5 * (rr & 1));
          for (let cc = c0 - 1; cc <= c0 + 1; cc++) {
            const tcx = SQ3 * (cc + 0.5 * (rr & 1)) + SQ3 / 2;
            const tcz = 1.5 * rr + 1;
            const d2 = (X - tcx) * (X - tcx) + (Z - tcz) * (Z - tcz);
            if (d2 > 1.6) continue;
            const w = Math.exp(-d2 / two);
            s += w;
            if (cc >= 0 && rr >= 0 && cc < W && rr < H) {
              const st = view.state[rr * W + cc];
              v += w * (st === 2 ? 1 : st === 1 ? 0.5 : 0);
            }
          }
        }
        out[j * fw + i] = s > 1e-5 ? Math.round((v / s) * 255) : 0;
      }
    }
    this.fogTex.needsUpdate = true;
    this.uniforms.uFogOrg.value.set(this.x0, this.z0);
    this.uniforms.uFogExt.value.set(fw / FOG_RES, fh / FOG_RES);
  }

  // ------------------------------------------------------------------ painted ground: roads, fields, borders
  updateDecal(view: GameView, colorOf: (owner: number) => string, cityKeys: Set<number>): void {
    const W = view.settings.width;
    const H = view.settings.height;
    let acc = 0;
    for (let i = 0; i < W * H; i++) acc = (Math.imul(acc, 29) + view.road[i] * 3 + view.improvement[i] * 5 + view.owner[i] * 11 + view.state[i]) | 0;
    for (const k of cityKeys) acc = (Math.imul(acc, 13) + k) | 0;
    const sig = `${W}:${acc}`;
    if (sig === this.decalSig) return;
    this.decalSig = sig;
    const maxPx = 3072;
    const scale = Math.min(26, maxPx / Math.max(this.extX, this.extZ));
    const cw = Math.ceil(this.extX * scale);
    const ch = Math.ceil(this.extZ * scale);
    const cv = this.decalCanvas;
    if (cv.width !== cw || cv.height !== ch) {
      cv.width = cw;
      cv.height = ch;
    }
    const ctx = cv.getContext('2d')!;
    ctx.clearRect(0, 0, cw, ch);
    ctx.setTransform(scale, 0, 0, scale, 0, 0);
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    const nbOf = (y: number) => (y & 1 ? EDGE_NEIGHBOR_ODD : EDGE_NEIGHBOR_EVEN);
    const corner = (cx: number, cz: number, r: number, i: number): [number, number] => {
      const a = ((60 * i - 30) * Math.PI) / 180;
      return [cx + r * Math.cos(a), cz + r * Math.sin(a)];
    };
    // city plazas
    for (const k of cityKeys) {
      const x = k % W;
      const y = (k / W) | 0;
      if (view.state[k] === 0) continue;
      const [X, Z] = tileXZ(x, y);
      const g = ctx.createRadialGradient(X, Z, 0.1, X, Z, 0.95);
      g.addColorStop(0, 'rgba(170,146,104,0.95)');
      g.addColorStop(0.65, 'rgba(150,128,90,0.8)');
      g.addColorStop(1, 'rgba(130,110,80,0)');
      ctx.fillStyle = g;
      ctx.beginPath();
      ctx.arc(X, Z, 0.95, 0, Math.PI * 2);
      ctx.fill();
    }
    // fields and mine scars
    for (let y = 0; y < H; y++) {
      for (let x = 0; x < W; x++) {
        const i = y * W + x;
        if (view.state[i] === 0) continue;
        const imp = view.improvement[i];
        if (!imp) continue;
        const [X, Z] = tileXZ(x, y);
        if (imp === 1) {
          ctx.save();
          ctx.beginPath();
          for (let e = 0; e < 6; e++) {
            const [px, pz] = corner(X, Z, 0.8, e);
            if (e === 0) ctx.moveTo(px, pz);
            else ctx.lineTo(px, pz);
          }
          ctx.closePath();
          ctx.clip();
          const rot = hash2(x, y, 3) * Math.PI;
          ctx.translate(X, Z);
          ctx.rotate(rot);
          const palette = ['#7fae3f', '#c9b04a', '#6a9a38', '#b49a46', '#8bb84a'];
          for (let a = -1; a <= 1; a++) {
            for (let b = -1; b <= 1; b++) {
              ctx.fillStyle = palette[Math.floor(hash2(x * 3 + a, y * 5 + b, 7) * palette.length)];
              ctx.fillRect(a * 0.55 - 0.26, b * 0.55 - 0.26, 0.52, 0.52);
              ctx.strokeStyle = 'rgba(50,40,15,0.28)';
              ctx.lineWidth = 0.012;
              for (let r = 0; r < 6; r++) {
                ctx.beginPath();
                ctx.moveTo(a * 0.55 - 0.26 + r * 0.09, b * 0.55 - 0.26);
                ctx.lineTo(a * 0.55 - 0.26 + r * 0.09, b * 0.55 + 0.26);
                ctx.stroke();
              }
            }
          }
          ctx.restore();
          ctx.strokeStyle = 'rgba(46,84,34,0.7)';
          ctx.lineWidth = 0.035;
          ctx.beginPath();
          for (let e = 0; e < 6; e++) {
            const [px, pz] = corner(X, Z, 0.8, e);
            if (e === 0) ctx.moveTo(px, pz);
            else ctx.lineTo(px, pz);
          }
          ctx.closePath();
          ctx.stroke();
        } else {
          const g = ctx.createRadialGradient(X - 0.05, Z + 0.2, 0.05, X - 0.05, Z + 0.2, 0.55);
          g.addColorStop(0, 'rgba(80,66,52,0.9)');
          g.addColorStop(1, 'rgba(80,66,52,0)');
          ctx.fillStyle = g;
          ctx.beginPath();
          ctx.arc(X - 0.05, Z + 0.2, 0.55, 0, Math.PI * 2);
          ctx.fill();
        }
      }
    }
    // roads
    const roadAt = (x: number, y: number) => x >= 0 && y >= 0 && x < W && y < H && (view.road[y * W + x] === 1 || cityKeys.has(y * W + x)) && view.state[y * W + x] > 0;
    const strokeRoads = (width: number, style: string) => {
      ctx.strokeStyle = style;
      ctx.lineWidth = width;
      for (let y = 0; y < H; y++) {
        for (let x = 0; x < W; x++) {
          if (view.road[y * W + x] !== 1 || view.state[y * W + x] === 0) continue;
          const [X, Z] = tileXZ(x, y);
          const nbs = nbOf(y);
          let any = false;
          for (let e = 0; e < 6; e++) {
            const nx = x + nbs[e][0];
            const ny = y + nbs[e][1];
            if (!roadAt(nx, ny)) continue;
            any = true;
            const [ax, az] = corner(X, Z, 1, e);
            const [bx, bz] = corner(X, Z, 1, e + 1);
            const mx = (ax + bx) / 2;
            const mz = (az + bz) / 2;
            const wob = (hash2(x * 7 + e, y * 11, 2) - 0.5) * 0.18;
            ctx.beginPath();
            ctx.moveTo(X, Z);
            ctx.quadraticCurveTo((X + mx) / 2 - (mz - Z) * wob, (Z + mz) / 2 + (mx - X) * wob, mx, mz);
            ctx.stroke();
          }
          if (!any) {
            ctx.beginPath();
            ctx.arc(X, Z, 0.05, 0, Math.PI * 2);
            ctx.fillStyle = style;
            ctx.fill();
          }
        }
      }
    };
    strokeRoads(0.2, 'rgba(70,50,30,0.55)');
    strokeRoads(0.14, 'rgba(158,128,86,0.98)');
    strokeRoads(0.05, 'rgba(206,182,134,0.6)');
    // borders: a coloured glow hugging the inside of each frontier
    for (let y = 0; y < H; y++) {
      for (let x = 0; x < W; x++) {
        const i = y * W + x;
        if (view.state[i] === 0) continue;
        const owner = view.owner[i] - 1;
        if (owner < 0) continue;
        const [X, Z] = tileXZ(x, y);
        const nbs = nbOf(y);
        const col = colorOf(owner);
        const edges: number[] = [];
        for (let e = 0; e < 6; e++) {
          const nx = x + nbs[e][0];
          const ny = y + nbs[e][1];
          const no = nx < 0 || ny < 0 || nx >= W || ny >= H ? -2 : view.owner[ny * W + nx] - 1;
          if (no !== owner) edges.push(e);
        }
        if (!edges.length) continue;
        const path = (r: number) => {
          for (const e of edges) {
            const [ax, az] = corner(X, Z, r, e);
            const [bx, bz] = corner(X, Z, r, e + 1);
            ctx.moveTo(ax, az);
            ctx.lineTo(bx, bz);
          }
        };
        ctx.save();
        ctx.beginPath();
        for (let e = 0; e < 6; e++) {
          const [px, pz] = corner(X, Z, 1.0, e);
          if (e === 0) ctx.moveTo(px, pz);
          else ctx.lineTo(px, pz);
        }
        ctx.closePath();
        ctx.clip();
        ctx.strokeStyle = col;
        ctx.globalAlpha = 0.13;
        ctx.lineWidth = 0.55;
        ctx.beginPath();
        path(1.0);
        ctx.stroke();
        ctx.globalAlpha = 0.22;
        ctx.lineWidth = 0.3;
        ctx.beginPath();
        path(1.0);
        ctx.stroke();
        ctx.restore();
        ctx.globalAlpha = 1;
        ctx.strokeStyle = 'rgba(8,10,16,0.5)';
        ctx.lineWidth = 0.095;
        ctx.beginPath();
        path(0.955);
        ctx.stroke();
        ctx.strokeStyle = col;
        ctx.lineWidth = 0.062;
        ctx.beginPath();
        path(0.955);
        ctx.stroke();
      }
    }
    this.decalTex.needsUpdate = true;
    this.decalTex.generateMipmaps = true;
    this.decalTex.minFilter = LinearMipmapLinearFilter;
    this.decalTex.anisotropy = 4;
    this.decalOrg.set(0, 0);
    this.decalExt.set(this.extX, this.extZ);
  }

  /** 0 unexplored .. 1 in sight, sampled from the fog texture's CPU copy. */
  fogAt(X: number, Z: number): number {
    const i = Math.floor((X - this.x0) * FOG_RES);
    const j = Math.floor((Z - this.z0) * FOG_RES);
    if (i < 0 || j < 0 || i >= this.fogW || j >= this.fogH) return 0;
    return this.fogData[j * this.fogW + i] / 255;
  }

  setTime(t: number): void {
    this.uniforms.uTime.value = t;
  }

  setSun(dir: Vector3, tint: [number, number, number]): void {
    (this.waterMat.uniforms.uSunDir.value as Vector3).copy(dir);
    (this.waterMat.uniforms.uSunTint.value as Vector3).set(tint[0], tint[1], tint[2]);
  }

  /** known-resource helper so callers need not import the list */
  static resourceName(i: number): string {
    return RESOURCE_LIST[i - 1];
  }
}

const OCEAN_TILE: TileData = { h: -0.5, r: 0.012, g: 0.07, b: 0.14, forest: 0, desert: 0, tundra: 0, rock: 0, mount: 0, hill: 0, flat: 0, known: true };
