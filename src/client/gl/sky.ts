import { CanvasTexture, DoubleSide, Group, Mesh, MeshBasicMaterial, PlaneGeometry, SRGBColorSpace, type Scene } from 'three';
import { fbm } from './noise';
import { SQ3 } from './terrain';

function cloudTexture(): CanvasTexture {
  const S = 128;
  const c = document.createElement('canvas');
  c.width = S;
  c.height = S;
  const ctx = c.getContext('2d')!;
  const img = ctx.createImageData(S, S);
  for (let y = 0; y < S; y++) {
    for (let x = 0; x < S; x++) {
      const dx = (x / S - 0.5) * 2;
      const dy = (y / S - 0.5) * 2;
      const d = Math.min(1, Math.hypot(dx, dy * 1.4));
      const n = fbm(x * 0.045, y * 0.045, 7, 4);
      const a = Math.max(0, (1 - d * d) * (n * 1.6 - 0.35));
      const i = (y * S + x) * 4;
      img.data[i] = 255;
      img.data[i + 1] = 255;
      img.data[i + 2] = 255;
      img.data[i + 3] = Math.min(255, a * 360);
    }
  }
  ctx.putImageData(img, 0, 0);
  const t = new CanvasTexture(c);
  t.colorSpace = SRGBColorSpace;
  return t;
}

/** Slow clouds high above the map, and a few birds crossing it. */
export class Sky {
  private group = new Group();
  private clouds: Array<{ mesh: Mesh; speed: number; x: number; z: number; s: number }> = [];
  private birds: Array<{ g: Group; wingL: Mesh; wingR: Mesh; x: number; z: number; speed: number; phase: number; y: number }> = [];
  private extX = 40;
  private extZ = 30;
  private built = false;

  constructor(scene: Scene) {
    scene.add(this.group);
  }

  private build(extX: number, extZ: number): void {
    this.extX = extX;
    this.extZ = extZ;
    for (const c of this.clouds) this.group.remove(c.mesh);
    for (const b of this.birds) this.group.remove(b.g);
    this.clouds = [];
    this.birds = [];
    const tex = cloudTexture();
    const geo = new PlaneGeometry(1, 1).rotateX(-Math.PI / 2);
    const n = Math.max(6, Math.round((extX * extZ) / 140));
    for (let i = 0; i < n; i++) {
      const mat = new MeshBasicMaterial({ map: tex, transparent: true, opacity: 0.5, depthWrite: false, side: DoubleSide, color: 0xffffff });
      const m = new Mesh(geo, mat);
      const s = 5 + Math.random() * 8;
      m.scale.set(s, 1, s * (0.55 + Math.random() * 0.3));
      const x = Math.random() * (extX + 20) - 10;
      const z = Math.random() * (extZ + 20) - 10;
      m.position.set(x, 3.4 + Math.random() * 1.2, z);
      m.renderOrder = 9;
      m.rotation.y = Math.random() * 0.4;
      this.group.add(m);
      this.clouds.push({ mesh: m, speed: 0.18 + Math.random() * 0.15, x, z, s });
    }
    const wing = new PlaneGeometry(0.14, 0.06).rotateX(-Math.PI / 2).translate(0.07, 0, 0);
    const bmat = new MeshBasicMaterial({ color: 0x1a1a1f, side: DoubleSide });
    for (let i = 0; i < 9; i++) {
      const g = new Group();
      const wl = new Mesh(wing, bmat);
      const wr = new Mesh(wing, bmat);
      wr.scale.x = -1;
      g.add(wl, wr);
      this.group.add(g);
      this.birds.push({ g, wingL: wl, wingR: wr, x: Math.random() * extX, z: 2 + Math.random() * (extZ - 4), speed: 0.9 + Math.random() * 0.5, phase: Math.random() * 6, y: 1.6 + Math.random() * 0.7 });
    }
    this.built = true;
  }

  update(t: number, dt: number, extX: number, zoom: number, extZ: number, seen: (x: number, z: number) => number): void {
    if (!this.built || Math.abs(extX - this.extX) > 0.1) this.build(extX, extZ);
    // clouds thin out as the player zooms in so they never hide the action
    const fade = Math.max(0, Math.min(1, (1.35 - zoom) / 0.6));
    for (const c of this.clouds) {
      c.x += c.speed * dt;
      if (c.x > this.extX + 14) c.x = -14;
      c.mesh.position.x = c.x;
      // clouds belong over the world the player has uncovered, not over the black unknown
      const over = Math.min(1, seen(c.x, c.z) * 3);
      const mat = c.mesh.material as MeshBasicMaterial;
      mat.opacity += (0.42 * fade * over - mat.opacity) * Math.min(1, dt * 2);
      c.mesh.visible = mat.opacity > 0.01;
    }
    for (const b of this.birds) {
      b.x += b.speed * dt;
      if (b.x > this.extX + 3) {
        b.x = -3;
        b.z = 2 + Math.random() * (this.extZ - 4);
      }
      const flap = Math.sin(t * 9 + b.phase) * 0.7;
      b.wingL.rotation.z = flap;
      b.wingR.rotation.z = -flap;
      b.g.position.set(b.x, b.y + Math.sin(t * 0.9 + b.phase) * 0.08, b.z + Math.sin(t * 0.3 + b.phase) * 0.4);
      b.g.rotation.y = 0;
      b.g.visible = zoom < 2.0 && seen(b.x, b.z) > 0.2;
    }
  }
}
void SQ3;
