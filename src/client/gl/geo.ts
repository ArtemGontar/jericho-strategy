import {
  BoxGeometry, BufferGeometry, Color, CylinderGeometry, DoubleSide, Euler, Float32BufferAttribute, Matrix4, MeshStandardMaterial,
  Quaternion, SphereGeometry, Vector3, type Material,
} from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';

export interface Xf {
  x?: number;
  y?: number;
  z?: number;
  rx?: number;
  ry?: number;
  rz?: number;
  sx?: number;
  sy?: number;
  sz?: number;
}

const tmpM = new Matrix4();
const tmpQ = new Quaternion();
const tmpE = new Euler();
const tmpV = new Vector3();
const tmpS = new Vector3();
const tmpC = new Color();

function matrixOf(t: Xf): Matrix4 {
  tmpE.set(t.rx ?? 0, t.ry ?? 0, t.rz ?? 0, 'YXZ');
  tmpQ.setFromEuler(tmpE);
  tmpV.set(t.x ?? 0, t.y ?? 0, t.z ?? 0);
  tmpS.set(t.sx ?? 1, t.sy ?? 1, t.sz ?? 1);
  return tmpM.compose(tmpV, tmpQ, tmpS);
}

/** Accumulates coloured primitives into one merged geometry (vertex colours, flat parts and smooth parts mixed). */
export class Builder {
  private parts: BufferGeometry[] = [];

  add(g: BufferGeometry, color: string | number, t: Xf = {}): this {
    const geo = g.index ? g.toNonIndexed() : g.clone();
    geo.applyMatrix4(matrixOf(t));
    geo.deleteAttribute('uv');
    tmpC.set(color as string);
    const n = geo.attributes.position.count;
    const col = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) {
      col[i * 3] = tmpC.r;
      col[i * 3 + 1] = tmpC.g;
      col[i * 3 + 2] = tmpC.b;
    }
    geo.setAttribute('color', new Float32BufferAttribute(col, 3));
    this.parts.push(geo);
    return this;
  }
  box(w: number, h: number, d: number, color: string | number, t: Xf = {}): this {
    return this.add(new BoxGeometry(w, h, d), color, t);
  }
  /** cylinder or truncated cone, base at y=0 */
  cyl(rTop: number, rBot: number, h: number, color: string | number, t: Xf = {}, seg = 10): this {
    const g = new CylinderGeometry(rTop, rBot, h, seg, 1);
    g.translate(0, h / 2, 0);
    return this.add(g, color, t);
  }
  cone(r: number, h: number, color: string | number, t: Xf = {}, seg = 8): this {
    // a cone's apex "cap" has no area, so leave it out: one cap fewer of triangles per cone, same picture
    const g = new CylinderGeometry(0.0001, r, h, seg, 1);
    const src = g.index!.array;
    const keep: number[] = [];
    for (const grp of g.groups) if (grp.materialIndex !== 1) for (let i = grp.start; i < grp.start + grp.count; i++) keep.push(src[i]);
    g.setIndex(keep);
    g.clearGroups();
    g.translate(0, h / 2, 0);
    return this.add(g, color, t);
  }
  sphere(r: number, color: string | number, t: Xf = {}, seg = 10): this {
    return this.add(new SphereGeometry(r, seg, Math.max(5, Math.round(seg * 0.7))), color, t);
  }
  /** gable roof: a triangular prism with its base on y=0, ridge along x */
  gable(w: number, h: number, d: number, color: string | number, t: Xf = {}): this {
    const hw = w / 2;
    const hd = d / 2;
    const pos = [
      // front and back triangles
      -hw, 0, hd, hw, 0, hd, 0, h, hd,
      hw, 0, -hd, -hw, 0, -hd, 0, h, -hd,
      // slopes
      -hw, 0, hd, 0, h, hd, 0, h, -hd, -hw, 0, hd, 0, h, -hd, -hw, 0, -hd,
      hw, 0, hd, hw, 0, -hd, 0, h, -hd, hw, 0, hd, 0, h, -hd, 0, h, hd,
      // bottom
      -hw, 0, hd, -hw, 0, -hd, hw, 0, -hd, -hw, 0, hd, hw, 0, -hd, hw, 0, hd,
    ];
    const g = new BufferGeometry();
    g.setAttribute('position', new Float32BufferAttribute(pos, 3));
    g.computeVertexNormals();
    return this.add(g, color, t);
  }
  /** square pyramid, base on y=0 */
  pyramid(w: number, h: number, d: number, color: string | number, t: Xf = {}): this {
    return this.add(new CylinderGeometry(0.0001, Math.SQRT1_2, h, 4, 1).translate(0, h / 2, 0).rotateY(Math.PI / 4).scale(w, 1, d), color, t);
  }
  /** flat quad facing +y, useful for cloth and ground patches */
  plane(w: number, d: number, color: string | number, t: Xf = {}): this {
    const hw = w / 2;
    const hd = d / 2;
    const g = new BufferGeometry();
    g.setAttribute('position', new Float32BufferAttribute([-hw, 0, hd, hw, 0, hd, hw, 0, -hd, -hw, 0, hd, hw, 0, -hd, -hw, 0, -hd], 3));
    g.computeVertexNormals();
    return this.add(g, color, t);
  }
  get count(): number {
    return this.parts.length;
  }
  build(): BufferGeometry {
    const g = mergeGeometries(this.parts, false)!;
    for (const p of this.parts) p.dispose();
    this.parts = [];
    return g;
  }
}

const matCache = new Map<string, MeshStandardMaterial>();
/** See releaseSharedFigures: shared materials keep a dead renderer reachable through three's per-material records. */
export function releaseSharedMaterials(): void {
  for (const m of matCache.values()) m.dispose();
}
/** Shared vertex-coloured material; `team` variants take their colour from a uniform tint instead. */
export function litMaterial(opts: { roughness?: number; metalness?: number; emissive?: number; double?: boolean; flat?: boolean } = {}): MeshStandardMaterial {
  const key = JSON.stringify(opts);
  let m = matCache.get(key);
  if (!m) {
    m = new MeshStandardMaterial({
      vertexColors: true,
      roughness: opts.roughness ?? 0.82,
      metalness: opts.metalness ?? 0,
      emissive: opts.emissive ?? 0x000000,
      side: opts.double ? DoubleSide : undefined,
      flatShading: opts.flat ?? false,
    });
    matCache.set(key, m);
  }
  return m;
}
export type { Material };
