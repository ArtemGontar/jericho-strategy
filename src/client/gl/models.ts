import { BackSide, Color, Group, Mesh, MeshBasicMaterial, MeshStandardMaterial, Object3D, TorusGeometry, CylinderGeometry, type BufferGeometry } from 'three';
import type { UnitTypeId } from '../../shared/types';
import { UNITS } from '../../shared/data';
import { Builder } from './geo';
import { patchMaterial, type SharedUniforms } from './fog';

/** What the animation code needs to know about a unit this frame. */
export interface Pose {
  t: number;
  seed: number;
  /** accumulated walk phase in radians */
  phase: number;
  /** 0 standing, 1 walking */
  walk: number;
  /** -1 when not attacking, otherwise 0..1 progress of the strike */
  attack: number;
  fort: number;
  sleep: number;
  work: boolean;
}

interface Part {
  pivot: Object3D;
  n: Builder;
  t: Builder;
}

export interface Rig {
  type: UnitTypeId;
  kind: 'biped' | 'mounted' | 'cart' | 'siege' | 'cannon' | 'ship';
  root: Group;
  /** upright height in scene units, used to place health bars and banners */
  height: number;
  mats: MeshStandardMaterial[];
  body?: Object3D;
  head?: Object3D;
  armL?: Object3D;
  armR?: Object3D;
  legL?: Object3D;
  legR?: Object3D;
  weapon?: Object3D;
  rider?: Object3D;
  horseLegs: Object3D[];
  oxLegs: Object3D[];
  wheels: Object3D[];
  tail?: Object3D;
  neck?: Object3D;
  throwArm?: Object3D;
  barrel?: Object3D;
  flag?: Object3D;
  oars: Object3D[];
  sails: Object3D[];
  /** set when a shot is released so effects can spawn once */
  fired?: boolean;
}

const SKIN = '#e3ae7c';
const HAIR = '#3a2616';
const DARK = '#2c2219';

class RigBuild {
  root = new Group();
  parts: Part[] = [];
  pivot(parent: Object3D | null, x = 0, y = 0, z = 0): Part {
    const o = new Object3D();
    o.position.set(x, y, z);
    (parent ?? this.root).add(o);
    const p = { pivot: o, n: new Builder(), t: new Builder() };
    this.parts.push(p);
    return p;
  }
  finish(unitMat: MeshStandardMaterial, teamMat: MeshStandardMaterial): void {
    for (const p of this.parts) {
      for (const [b, m] of [[p.n, unitMat], [p.t, teamMat]] as const) {
        if (!b.count) continue;
        const mesh = new Mesh(b.build(), m);
        mesh.castShadow = true;
        mesh.receiveShadow = false;
        mesh.userData.team = m === teamMat;
        p.pivot.add(mesh);
      }
    }
  }
}

// ---------------------------------------------------------------- human parts
interface Costume {
  tunic: string;
  pants?: string;
  hat?: 'none' | 'cap' | 'helm' | 'hood' | 'straw' | 'wide' | 'tricorn' | 'kettle' | 'headband' | 'crest';
  /** tunic panels use the team colour */
  teamTunic?: boolean;
  cloak?: boolean;
}

function addHuman(rb: RigBuild, parent: Object3D | null, c: Costume, rig: Rig, baseY = 0): { body: Part; armR: Part; armL: Part; head: Part } {
  const y0 = baseY;
  const body = rb.pivot(parent, 0, y0 + 0.18, 0);
  // torso
  body.n.box(0.16, 0.2, 0.1, c.tunic, { y: 0.1 });
  if (c.teamTunic) body.t.box(0.165, 0.2, 0.105, 'white', { y: 0.1 });
  body.n.box(0.17, 0.035, 0.11, '#5a4128', { y: 0.01 });
  body.n.box(0.03, 0.03, 0.012, '#d9b25a', { y: 0.01, z: 0.058 });
  body.t.box(0.175, 0.02, 0.115, 'white', { y: 0.032 });
  // tunic skirt with a team-coloured hem, and a team-coloured collar
  body.n.cyl(0.092, 0.112, 0.09, c.tunic, { y: -0.075 }, 8);
  body.t.cyl(0.113, 0.113, 0.014, 'white', { y: -0.078 }, 8);
  body.t.cyl(0.056, 0.07, 0.03, 'white', { y: 0.19 }, 8);
  if (c.cloak) body.t.box(0.19, 0.27, 0.025, 'white', { y: 0.1, z: -0.07, rx: 0.12 });
  // head
  const head = rb.pivot(body.pivot, 0, 0.255, 0);
  head.n.sphere(0.058, SKIN, { y: 0.045, sy: 1.05 }, 10);
  head.n.sphere(0.011, DARK, { x: 0.02, y: 0.05, z: 0.05 }, 4);
  head.n.sphere(0.011, DARK, { x: -0.02, y: 0.05, z: 0.05 }, 4);
  head.n.sphere(0.058, HAIR, { y: 0.07, z: -0.012, sy: 0.75, sz: 1.0 }, 9);
  switch (c.hat) {
    case 'cap':
      head.n.sphere(0.063, '#6a4a2e', { y: 0.072, sy: 0.62 }, 9);
      break;
    case 'helm':
      head.n.sphere(0.066, '#aeb6c0', { y: 0.075, sy: 0.85 }, 10);
      head.n.box(0.012, 0.05, 0.012, '#aeb6c0', { y: 0.05, z: 0.062 });
      head.n.cyl(0.066, 0.068, 0.014, '#8f98a3', { y: 0.052 }, 10);
      break;
    case 'crest':
      head.n.sphere(0.067, '#b9c1cb', { y: 0.075, sy: 0.85 }, 10);
      head.n.cyl(0.067, 0.07, 0.014, '#8f98a3', { y: 0.052 }, 10);
      head.t.box(0.014, 0.05, 0.1, 'white', { y: 0.135 });
      break;
    case 'hood':
      head.n.cone(0.075, 0.13, '#4e6a38', { y: 0.04, sz: 1.05 }, 8);
      break;
    case 'straw':
      head.n.cyl(0.1, 0.1, 0.008, '#d9bf6a', { y: 0.085 }, 14);
      head.n.cyl(0.045, 0.055, 0.045, '#d2b45a', { y: 0.085 }, 10);
      break;
    case 'wide':
      head.n.cyl(0.095, 0.095, 0.008, '#6a4f33', { y: 0.088 }, 14);
      head.n.cyl(0.05, 0.058, 0.05, '#6a4f33', { y: 0.088 }, 10);
      head.t.cyl(0.052, 0.06, 0.012, 'white', { y: 0.092 }, 10);
      break;
    case 'tricorn':
      head.n.box(0.15, 0.015, 0.11, '#1f1c1c', { y: 0.085, ry: 0.0 });
      head.n.cyl(0.05, 0.058, 0.04, '#25211f', { y: 0.088 }, 8);
      head.t.box(0.152, 0.008, 0.112, 'white', { y: 0.098 });
      break;
    case 'kettle':
      head.n.cyl(0.095, 0.095, 0.01, '#9aa2ac', { y: 0.082 }, 12);
      head.n.sphere(0.058, '#aeb6c0', { y: 0.085, sy: 0.9 }, 9);
      break;
    case 'headband':
      head.t.cyl(0.062, 0.062, 0.022, 'white', { y: 0.062 }, 10);
      break;
    default:
  }
  head.pivot.scale.setScalar(1.22);
  // legs
  const mkLeg = (side: number): Part => {
    const l = rb.pivot(body.pivot, side * 0.045, 0.0, 0);
    l.n.cyl(0.036, 0.03, 0.17, c.pants ?? '#4d3e2f', { y: -0.17 }, 7);
    l.n.cyl(0.034, 0.036, 0.05, '#dccaa2', { y: -0.17 }, 7);
    l.n.box(0.06, 0.03, 0.1, '#6a4a2c', { y: -0.17, z: 0.015 });
    return l;
  };
  const legL = mkLeg(-1);
  const legR = mkLeg(1);
  // arms
  const mkArm = (side: number): Part => {
    const a = rb.pivot(body.pivot, side * 0.1, 0.18, 0);
    a.n.cyl(0.03, 0.024, 0.14, c.teamTunic ? SKIN : c.tunic, { y: -0.14 }, 7);
    a.n.cyl(0.027, 0.027, 0.03, '#dccaa2', { y: -0.135 }, 6);
    a.n.sphere(0.028, SKIN, { y: -0.145 }, 6);
    return a;
  };
  const armL = mkArm(-1);
  const armR = mkArm(1);
  rig.body = body.pivot;
  rig.head = head.pivot;
  rig.armL = armL.pivot;
  rig.armR = armR.pivot;
  rig.legL = legL.pivot;
  rig.legR = legR.pivot;
  return { body, armR, armL, head };
}

function roundShield(p: Part, r: number, face: 'team' | string): void {
  const rot = { rz: Math.PI / 2, x: -0.03 };
  const g = new CylinderGeometry(r, r, 0.018, 14);
  (face === 'team' ? p.t : p.n).add(g, face === 'team' ? 'white' : face, { ...rot, y: -0.1 });
  p.n.add(new CylinderGeometry(r * 0.28, r * 0.28, 0.04, 8), '#b8bec8', { ...rot, y: -0.1, x: -0.045 });
  p.n.add(new CylinderGeometry(r * 1.05, r * 1.05, 0.012, 14), '#5a4a38', { ...rot, y: -0.1, x: -0.025 });
}

function kiteShield(p: Part): void {
  p.t.box(0.015, 0.2, 0.13, 'white', { x: -0.035, y: -0.11 });
  p.n.box(0.012, 0.16, 0.014, '#e8e0c8', { x: -0.045, y: -0.11 });
  p.n.box(0.012, 0.012, 0.1, '#e8e0c8', { x: -0.045, y: -0.1 });
  p.n.sphere(0.02, '#c0c6cf', { x: -0.045, y: -0.1 }, 6);
}

function sword(p: Part, len: number): void {
  p.n.box(0.012, len, 0.034, '#d8dde4', { x: 0.02, y: -0.14 - len / 2 + 0.28 - 0.1, z: 0.02, rx: 0 });
  p.n.box(0.05, 0.012, 0.012, '#b08a3a', { x: 0.02, y: -0.14, z: 0.02 });
}

function staff(p: Part, len: number, tip: 'spear' | 'pike' | 'staff' | 'pick'): void {
  p.n.cyl(0.011, 0.012, len, '#7a5a38', { x: 0.03, y: -0.17, z: 0.05 }, 5);
  if (tip === 'spear' || tip === 'pike') {
    p.n.cone(0.024, tip === 'pike' ? 0.1 : 0.07, '#d8dde4', { x: 0.03, y: -0.17 + len, z: 0.05 }, 4);
  } else if (tip === 'pick') {
    p.n.box(0.2, 0.025, 0.025, '#6f7680', { x: 0.03, y: -0.17 + len - 0.02, z: 0.05 });
    p.n.cone(0.02, 0.05, '#6f7680', { x: 0.13, y: -0.17 + len - 0.02, z: 0.05, rz: -Math.PI / 2 }, 4);
    p.n.cone(0.02, 0.05, '#6f7680', { x: -0.07, y: -0.17 + len - 0.02, z: 0.05, rz: Math.PI / 2 }, 4);
  }
}

function bow(p: Part): void {
  const g = new TorusGeometry(0.15, 0.007, 4, 14, Math.PI * 0.9);
  p.n.add(g, '#6b4a2a', { x: -0.015, y: -0.16, z: 0.1, rz: -Math.PI * 0.45 + Math.PI / 2, ry: Math.PI / 2 });
  p.n.box(0.003, 0.3, 0.003, '#efe8d0', { x: -0.015, y: -0.16, z: 0.07 });
}

function musket(p: Part): void {
  p.n.box(0.026, 0.05, 0.16, '#5a3d22', { x: 0.025, y: -0.145, z: 0.0 });
  p.n.box(0.014, 0.014, 0.34, '#4b525c', { x: 0.025, y: -0.14, z: 0.24 });
}

function crossbow(p: Part): void {
  p.n.box(0.02, 0.03, 0.22, '#5a3d22', { x: 0.02, y: -0.15, z: 0.08 });
  p.n.box(0.2, 0.012, 0.012, '#3a2c20', { x: 0.02, y: -0.15, z: 0.18 });
}

function quiver(body: Part): void {
  body.n.cyl(0.03, 0.026, 0.14, '#6a4a2e', { x: 0.03, y: 0.04, z: -0.075, rx: 0.2 }, 6);
  for (const dx of [-0.01, 0.01, 0.0]) body.n.cone(0.008, 0.03, '#efe8d0', { x: 0.03 + dx, y: 0.185, z: -0.095, rx: 0.2 }, 4);
}

// ---------------------------------------------------------------- mounts and vehicles
function addHorse(rb: RigBuild, rig: Rig, coat: string, barding: boolean): Part {
  const body = rb.pivot(null, 0, 0.3, 0);
  body.n.sphere(0.13, coat, { sx: 1.5, sy: 0.82, sz: 0.78 }, 12);
  body.n.sphere(0.115, coat, { x: 0.15, y: 0.03, sx: 1.0, sy: 0.9, sz: 0.85 }, 10);
  body.n.sphere(0.11, coat, { x: -0.16, y: 0.0, sx: 1.0, sy: 0.9, sz: 0.85 }, 10);
  // neck and head
  const neck = rb.pivot(body.pivot, 0.2, 0.06, 0);
  neck.n.cyl(0.05, 0.075, 0.2, coat, { rz: -0.75 }, 8);
  neck.n.box(0.02, 0.17, 0.02, '#221810', { x: -0.02, y: 0.02, rz: -0.75 });
  const head = rb.pivot(neck.pivot, 0.13, 0.15, 0);
  head.n.sphere(0.055, coat, { sx: 1.0 }, 8);
  head.n.sphere(0.04, coat, { x: 0.075, y: -0.025, sx: 1.4, sy: 0.85 }, 8);
  head.n.cone(0.014, 0.04, coat, { x: 0.0, y: 0.04, z: 0.03 }, 4);
  head.n.cone(0.014, 0.04, coat, { x: 0.0, y: 0.04, z: -0.03 }, 4);
  head.n.sphere(0.01, '#120d0a', { x: 0.03, y: 0.015, z: 0.047 }, 4);
  head.n.sphere(0.01, '#120d0a', { x: 0.03, y: 0.015, z: -0.047 }, 4);
  // legs: pivot at the shoulder/hip
  const mkLeg = (x: number, z: number): Part => {
    const l = rb.pivot(body.pivot, x, -0.04, z);
    l.n.cyl(0.026, 0.02, 0.17, coat, { y: -0.17 }, 6);
    l.n.cyl(0.02, 0.016, 0.1, '#2a1f18', { y: -0.28 }, 6);
    return l;
  };
  rig.horseLegs = [mkLeg(0.18, 0.06), mkLeg(0.18, -0.06), mkLeg(-0.17, 0.06), mkLeg(-0.17, -0.06)].map((p) => p.pivot);
  const tail = rb.pivot(body.pivot, -0.27, 0.05, 0);
  tail.n.cone(0.03, 0.2, '#221810', { rz: Math.PI * 0.6 }, 5);
  rig.tail = tail.pivot;
  rig.neck = neck.pivot;
  if (barding) {
    body.t.box(0.34, 0.14, 0.22, 'white', { y: -0.01, x: 0.0 });
    body.t.box(0.1, 0.02, 0.24, 'white', { y: -0.04, x: 0.13 });
    body.n.box(0.09, 0.012, 0.11, '#d4af5a', { x: 0.02, y: 0.075 });
  }
  // saddle
  body.n.box(0.12, 0.025, 0.14, '#6a3f26', { y: 0.095 });
  return body;
}

function wheel(rb: RigBuild, parent: Object3D | null, x: number, y: number, z: number, r: number): Part {
  const w = rb.pivot(parent, x, y, z);
  const rim = new TorusGeometry(r, r * 0.12, 5, 14);
  w.n.add(rim, '#4a3622', {});
  w.n.box(r * 2, r * 0.18, r * 0.18, '#5a4630');
  w.n.box(r * 0.18, r * 2, r * 0.18, '#5a4630');
  w.n.box(r * 0.14, r * 2, r * 0.14, '#5a4630', { rz: Math.PI / 4 });
  w.n.box(r * 0.14, r * 2, r * 0.14, '#5a4630', { rz: -Math.PI / 4 });
  return w;
}

function flag(rb: RigBuild, parent: Object3D | null, x: number, y: number, z: number, h: number, rig?: Rig): void {
  const pole = rb.pivot(parent, x, y, z);
  pole.n.cyl(0.006, 0.007, h, '#3a2c20', {}, 4);
  const cloth = rb.pivot(pole.pivot, 0, h - 0.03, 0);
  cloth.t.box(0.13, 0.08, 0.004, 'white', { x: 0.07, y: -0.04 });
  cloth.n.sphere(0.01, '#d4af5a', { y: 0.03 }, 4);
  if (rig) rig.flag = cloth.pivot;
}

// ---------------------------------------------------------------- the roster
const BUILDERS: Record<UnitTypeId, (rb: RigBuild, rig: Rig) => void> = {
  warrior(rb, rig) {
    const h = addHuman(rb, null, { tunic: '#b8582f', pants: '#7a5a38', hat: 'headband' }, rig);
    roundShield(h.armL, 0.095, 'team');
    h.armR.n.cyl(0.02, 0.03, 0.2, '#6b4a2e', { x: 0.02, y: -0.2, z: 0.03, rx: 0 }, 6);
    h.armR.n.sphere(0.04, '#7a5a38', { x: 0.02, y: 0.0, z: 0.03 }, 6);
    rig.weapon = h.armR.pivot;
  },
  swordsman(rb, rig) {
    const h = addHuman(rb, null, { tunic: '#7f93ad', pants: '#5f574c', hat: 'crest' }, rig);
    kiteShield(h.armL);
    sword(h.armR, 0.26);
    h.body.t.box(0.16, 0.06, 0.105, 'white', { y: 0.16 });
    rig.weapon = h.armR.pivot;
  },
  spearman(rb, rig) {
    const h = addHuman(rb, null, { tunic: '#c9a35a', pants: '#6a5238', hat: 'helm' }, rig);
    roundShield(h.armL, 0.095, 'team');
    staff(h.armR, 0.7, 'spear');
    rig.weapon = h.armR.pivot;
  },
  pikeman(rb, rig) {
    const h = addHuman(rb, null, { tunic: '#7189a8', pants: '#54483a', hat: 'helm', teamTunic: true }, rig);
    staff(h.armR, 0.95, 'pike');
    rig.weapon = h.armR.pivot;
  },
  musketman(rb, rig) {
    const h = addHuman(rb, null, { tunic: '#2f4272', pants: '#efe8d4', hat: 'tricorn', teamTunic: true }, rig);
    musket(h.armR);
    h.body.n.box(0.04, 0.2, 0.108, '#efe8d4', { y: 0.1 });
    rig.weapon = h.armR.pivot;
  },
  archer(rb, rig) {
    const h = addHuman(rb, null, { tunic: '#5f9a42', pants: '#6a5236', hat: 'hood' }, rig);
    bow(h.armL);
    quiver(h.body);
    rig.weapon = h.armR.pivot;
  },
  crossbowman(rb, rig) {
    const h = addHuman(rb, null, { tunic: '#a47c46', pants: '#54402e', hat: 'kettle', teamTunic: false }, rig);
    crossbow(h.armR);
    h.body.t.box(0.17, 0.02, 0.112, 'white', { y: 0.195 });
    rig.weapon = h.armR.pivot;
  },
  scout(rb, rig) {
    const h = addHuman(rb, null, { tunic: '#8a7a46', pants: '#54402c', hat: 'wide', cloak: true }, rig);
    h.body.n.box(0.12, 0.14, 0.06, '#5f4a30', { y: 0.1, z: -0.1 });
    staff(h.armR, 0.4, 'staff');
    h.armL.n.cyl(0.02, 0.016, 0.1, '#b8903a', { x: -0.03, y: -0.2, z: 0.05, rx: 1.1 }, 6);
    rig.weapon = h.armR.pivot;
  },
  worker(rb, rig) {
    const h = addHuman(rb, null, { tunic: '#d0b062', pants: '#6a5236', hat: 'straw' }, rig);
    staff(h.armR, 0.34, 'pick');
    h.body.n.box(0.07, 0.08, 0.045, '#7a5a38', { x: -0.08, y: 0.03, z: 0.0 });
    rig.weapon = h.armR.pivot;
  },
  settler(rb, rig) {
    // ox-drawn covered wagon with the settlers on the bench
    const cart = rb.pivot(null, -0.06, 0.0, 0);
    cart.n.box(0.4, 0.05, 0.22, '#8a5f36', { y: 0.14 });
    cart.n.box(0.4, 0.07, 0.012, '#7a5230', { y: 0.2, z: 0.11 });
    cart.n.box(0.4, 0.07, 0.012, '#7a5230', { y: 0.2, z: -0.11 });
    const cover = new CylinderGeometry(0.115, 0.115, 0.36, 12, 1, false, 0, Math.PI);
    cart.n.add(cover, '#f1e9d2', { y: 0.2, rz: Math.PI / 2, rx: 0 });
    cart.t.box(0.38, 0.03, 0.235, 'white', { y: 0.21 });
    cart.n.box(0.06, 0.07, 0.19, '#7a5230', { x: 0.2, y: 0.19 });
    // crates
    cart.n.box(0.09, 0.07, 0.08, '#a67c4a', { x: -0.12, y: 0.2, z: 0.0 });
    const mkWheel = (x: number, z: number) => wheel(rb, cart.pivot, x, 0.1, z, 0.1);
    rig.wheels = [mkWheel(-0.13, 0.125), mkWheel(0.13, 0.125), mkWheel(-0.13, -0.125), mkWheel(0.13, -0.125)].map((w) => {
      w.pivot.rotation.order = 'ZYX';
      return w.pivot;
    });
    // oxen
    const mkOx = (z: number): Part => {
      const ox = rb.pivot(null, 0.32, 0.18, z);
      ox.n.sphere(0.1, '#7a5538', { sx: 1.3, sy: 0.85, sz: 0.8 }, 9);
      ox.n.sphere(0.056, '#7a5538', { x: 0.14, y: 0.04, sx: 1.2 }, 8);
      ox.n.cone(0.012, 0.06, '#e4dcc4', { x: 0.14, y: 0.08, z: 0.05, rx: 0.8 }, 4);
      ox.n.cone(0.012, 0.06, '#e4dcc4', { x: 0.14, y: 0.08, z: -0.05, rx: -0.8 }, 4);
      for (const [lx, lz] of [[-0.08, -0.04], [-0.08, 0.04], [0.08, -0.04], [0.08, 0.04]]) {
        const leg = rb.pivot(ox.pivot, lx, -0.05, lz);
        leg.n.cyl(0.014, 0.016, 0.13, '#5f4128', { y: -0.13 }, 5);
        rig.oxLegs.push(leg.pivot);
      }
      return ox;
    };
    mkOx(0.07);
    mkOx(-0.07);
    cart.n.box(0.16, 0.012, 0.012, '#4a3622', { x: 0.27, y: 0.2 });
    // driver
    const d = addHuman(rb, cart.pivot, { tunic: '#a8845a', pants: '#4d3e2f', hat: 'wide' }, rig, 0.0);
    d.body.pivot.position.set(0.14, 0.2, 0);
    d.body.pivot.rotation.y = Math.PI / 2;
    d.body.pivot.scale.setScalar(0.85);
    rig.legL!.rotation.x = -1.3;
    rig.legR!.rotation.x = -1.3;
    flag(rb, cart.pivot, -0.19, 0.18, -0.1, 0.34, rig);
  },
  horseman(rb, rig) {
    const horse = addHorse(rb, rig, '#8a5a35', false);
    const r = addHuman(rb, horse.pivot, { tunic: '#a8723c', pants: '#54402e', hat: 'cap', teamTunic: true }, rig, 0.1);
    r.body.pivot.position.set(0.0, 0.1, 0);
    r.body.pivot.rotation.y = Math.PI / 2;
    rig.legL!.visible = false;
    rig.legR!.visible = false;
    roundShield(r.armL, 0.085, 'team');
    staff(r.armR, 0.6, 'spear');
    rig.rider = r.body.pivot;
    rig.weapon = r.armR.pivot;
  },
  knight(rb, rig) {
    const horse = addHorse(rb, rig, '#4a3a30', true);
    const r = addHuman(rb, horse.pivot, { tunic: '#aeb6c0', pants: '#7d848d', hat: 'crest', teamTunic: false }, rig, 0.1);
    r.body.pivot.position.set(0.0, 0.1, 0);
    r.body.pivot.rotation.y = Math.PI / 2;
    rig.legL!.visible = false;
    rig.legR!.visible = false;
    kiteShield(r.armL);
    r.body.t.box(0.17, 0.12, 0.108, 'white', { y: 0.12 });
    staff(r.armR, 0.7, 'spear');
    r.armR.t.box(0.1, 0.05, 0.004, 'white', { x: 0.03, y: 0.4, z: 0.05 });
    rig.rider = r.body.pivot;
    rig.weapon = r.armR.pivot;
  },
  trireme(rb, rig) {
    const b = rb.pivot(null, 0, 0.0, 0);
    const len = 0.95;
    const beam = 0.26;
    hull(rb, b, len, beam, '#8a5a35', '#6e4426');
    // row of shields along the rail
    for (const side of [-1, 1]) for (let i = 0; i < 6; i++) b.t.cyl(0.045, 0.045, 0.012, 'white', { x: (i - 2.5) * 0.11, y: 0.14, z: side * beam * 0.34, rx: Math.PI / 2 }, 8);
    mast(b, 0.05, 0.5);
    squareSail(rb, rig, b, 0.05, 0.28, 0.3, 0.26);
    oarRow(rb, rig, b, 6, len, beam);
    b.n.cone(0.03, 0.12, '#b9a05a', { x: len * 0.54, y: 0.0, rz: -Math.PI / 2 }, 5);
    rig.height = 0.55;
  },
  caravel(rb, rig) {
    const b = rb.pivot(null, 0, 0.0, 0);
    const len = 1.0;
    const beam = 0.3;
    hull(rb, b, len, beam, '#7a4c2a', '#5d3a20');
    b.n.box(0.3, 0.1, beam * 0.8, '#6e4426', { x: len * 0.3, y: 0.17 });
    mast(b, -0.12, 0.7);
    mast(b, 0.2, 0.55);
    squareSail(rb, rig, b, -0.12, 0.3, 0.34, 0.38);
    squareSail(rb, rig, b, 0.2, 0.3, 0.28, 0.3);
    b.t.box(0.012, 0.012, 0.3, 'white', { x: -0.12, y: 0.52 });
    const fl = rb.pivot(b.pivot, -0.12, 0.82, 0);
    fl.t.box(0.14, 0.07, 0.004, 'white', { x: 0.07, y: -0.03 });
    rig.flag = fl.pivot;
    rig.height = 0.75;
  },
  frigate(rb, rig) {
    const b = rb.pivot(null, 0, 0.0, 0);
    const len = 1.15;
    const beam = 0.34;
    hull(rb, b, len, beam, '#4a3a30', '#33271f');
    // gun ports and cannon muzzles
    for (const side of [-1, 1]) {
      for (let i = 0; i < 5; i++) {
        b.n.box(0.05, 0.035, 0.012, '#15100c', { x: (i - 2) * 0.15, y: 0.08, z: side * beam * 0.46 });
        b.n.cyl(0.014, 0.016, 0.06, '#2a2c30', { x: (i - 2) * 0.15, y: 0.075, z: side * beam * 0.52, rx: Math.PI / 2 }, 6);
      }
    }
    b.t.box(len * 0.8, 0.03, beam * 0.9, 'white', { y: 0.104 });
    for (const x of [-0.28, 0.05, 0.36]) {
      mast(b, x, x === 0.05 ? 0.85 : 0.65);
      squareSail(rb, rig, b, x, 0.3, 0.3, x === 0.05 ? 0.46 : 0.36, false);
    }
    const fl = rb.pivot(b.pivot, 0.05, 0.98, 0);
    fl.t.box(0.16, 0.08, 0.004, 'white', { x: 0.08, y: -0.04 });
    rig.flag = fl.pivot;
    rig.height = 0.95;
  },
  catapult(rb, rig) {
    const frame = rb.pivot(null, 0, 0.12, 0);
    frame.n.box(0.4, 0.04, 0.22, '#7a5530', { y: 0.0 });
    frame.n.box(0.04, 0.04, 0.3, '#6a4828', { x: -0.14, y: 0.04 });
    frame.n.box(0.04, 0.04, 0.3, '#6a4828', { x: 0.14, y: 0.04 });
    frame.n.box(0.035, 0.22, 0.035, '#8a5f36', { x: -0.08, y: 0.13, z: 0.1 });
    frame.n.box(0.035, 0.22, 0.035, '#8a5f36', { x: -0.08, y: 0.13, z: -0.1 });
    frame.t.box(0.18, 0.05, 0.215, 'white', { x: 0.08, y: 0.045 });
    rig.wheels = [wheel(rb, frame.pivot, -0.14, -0.02, 0.15, 0.09), wheel(rb, frame.pivot, 0.14, -0.02, 0.15, 0.09), wheel(rb, frame.pivot, -0.14, -0.02, -0.15, 0.09), wheel(rb, frame.pivot, 0.14, -0.02, -0.15, 0.09)].map((w) => w.pivot);
    const arm = rb.pivot(frame.pivot, -0.08, 0.2, 0);
    arm.n.box(0.04, 0.34, 0.04, '#7a5530', { x: 0.08, y: 0.06, rz: -0.35 });
    arm.n.cyl(0.07, 0.04, 0.05, '#6a4828', { x: 0.18, y: 0.2, z: 0 }, 8);
    arm.n.sphere(0.055, '#6b665e', { x: 0.18, y: 0.27 }, 8);
    arm.n.cyl(0.02, 0.02, 0.26, '#5a4630', { rx: Math.PI / 2, y: 0.0 }, 6);
    rig.throwArm = arm.pivot;
    flag(rb, frame.pivot, -0.18, 0.02, 0.1, 0.34, rig);
  },
  cannon(rb, rig) {
    const car = rb.pivot(null, 0, 0.12, 0);
    car.n.box(0.34, 0.04, 0.16, '#6a4828', { y: 0.0 });
    car.n.box(0.2, 0.06, 0.03, '#7a5530', { x: -0.1, y: 0.05, z: 0.08 });
    car.n.box(0.2, 0.06, 0.03, '#7a5530', { x: -0.1, y: 0.05, z: -0.08 });
    car.t.box(0.14, 0.03, 0.17, 'white', { x: -0.12, y: 0.012 });
    rig.wheels = [wheel(rb, car.pivot, 0.0, -0.01, 0.12, 0.1), wheel(rb, car.pivot, 0.0, -0.01, -0.12, 0.1)].map((w) => w.pivot);
    const barrel = rb.pivot(car.pivot, -0.02, 0.1, 0);
    barrel.n.cyl(0.05, 0.065, 0.3, '#8c6a2e', { rz: -Math.PI / 2, x: 0.28 }, 12);
    barrel.n.cyl(0.07, 0.07, 0.04, '#6f5222', { rz: -Math.PI / 2, x: 0.17 }, 10);
    barrel.n.sphere(0.07, '#6f5222', { x: -0.0, sx: 1.0 }, 9);
    barrel.n.cyl(0.058, 0.058, 0.012, '#2a2218', { rz: -Math.PI / 2, x: 0.285 }, 10);
    rig.barrel = barrel.pivot;
    // cannonballs
    for (let i = 0; i < 3; i++) car.n.sphere(0.03, '#2a2c30', { x: -0.17 + (i % 2) * 0.05, y: 0.05 + (i === 2 ? 0.05 : 0), z: 0.0 + (i === 1 ? 0.05 : -0.02) }, 6);
    flag(rb, car.pivot, -0.2, 0.0, 0.0, 0.32, rig);
  },
};

function hull(rb: RigBuild, b: Part, len: number, beam: number, wood: string, trim: string): void {
  // a tapered hull: a long ellipsoid for the bottom, a flatter one for the deck, a pointed bow and a raised stern
  b.n.sphere(0.5, wood, { x: 0, y: 0.03, sx: len, sy: 0.2, sz: beam }, 14);
  b.n.sphere(0.5, shadeW(wood, -0.18), { x: 0, y: -0.01, sx: len * 0.96, sy: 0.14, sz: beam * 0.8 }, 12);
  b.n.box(len * 0.82, 0.015, beam * 0.62, '#b08a5a', { x: 0, y: 0.115 });
  b.n.cone(beam * 0.34, len * 0.2, wood, { x: len * 0.5, y: 0.06, rz: -Math.PI / 2 }, 6);
  b.n.box(len * 0.2, 0.09, beam * 0.7, trim, { x: -len * 0.42, y: 0.15 });
  b.n.cyl(0.03, 0.05, 0.1, trim, { x: -len * 0.52, y: 0.1, rz: 0.5 }, 6);
  void rb;
}
function shadeW(hex: string, k: number): string {
  const n = parseInt(hex.slice(1), 16);
  const f = (c: number) => Math.max(0, Math.min(255, Math.round(k >= 0 ? c + (255 - c) * k : c * (1 + k))));
  return `rgb(${f((n >> 16) & 255)},${f((n >> 8) & 255)},${f(n & 255)})`;
}
function oarRow(rb: RigBuild, rig: Rig, hullPart: Part, count: number, len: number, beam: number): void {
  for (const side of [-1, 1]) {
    for (let i = 0; i < count; i++) {
      const x = (i - (count - 1) / 2) * (len * 0.78 / count);
      const o = rb.pivot(hullPart.pivot, x, 0.1, side * beam * 0.3);
      o.n.cyl(0.009, 0.009, 0.34, '#caa46a', { rx: side * 1.15, z: side * 0.04 }, 4);
      o.n.box(0.04, 0.006, 0.05, '#caa46a', { y: 0.0, z: side * 0.33, x: 0, rx: 0 });
      o.pivot.userData.side = side;
      rig.oars.push(o.pivot);
    }
  }
}
function squareSail(rb: RigBuild, rig: Rig, parent: Part, x: number, y: number, w: number, h: number, stripe = true): void {
  const s = rb.pivot(parent.pivot, x, y, 0);
  s.n.box(0.012, h, w, '#efe6cc', { y: h / 2 });
  if (stripe) s.t.box(0.016, h * 0.22, w * 1.0, 'white', { y: h * 0.55 });
  s.n.cyl(0.012, 0.012, w * 1.1, '#5a4630', { y: h, rx: Math.PI / 2 }, 5);
  rig.sails.push(s.pivot);
}
function mast(b: Part, x: number, h: number): void {
  b.n.cyl(0.014, 0.02, h, '#6a4a2a', { x, y: 0.12 }, 6);
}

const mounts: UnitTypeId[] = ['horseman', 'knight'];
const heights: Partial<Record<UnitTypeId, number>> = { pikeman: 0.82, spearman: 0.78, horseman: 0.8, knight: 0.82, catapult: 0.5, cannon: 0.4, settler: 0.55 };

const unitMatCache = new Map<string, MeshStandardMaterial>();

/** Dark ink shell drawn behind figures and buildings; one shared material per outline width. */
const inkMats = new Map<number, MeshBasicMaterial>();
export function inkMaterial(u: SharedUniforms, width = 0.011): MeshBasicMaterial {
  let m = inkMats.get(width);
  if (!m) {
    m = new MeshBasicMaterial({ color: new Color('#2b1d17'), side: BackSide });
    patchMaterial(m, u, { outline: width });
    inkMats.set(width, m);
  }
  return m;
}

/** A shell of the mesh's geometry in ink, added next to it so it follows the same transform. */
export function addInk(mesh: Mesh, u: SharedUniforms, width?: number): Mesh {
  const shell = new Mesh(mesh.geometry, inkMaterial(u, width));
  shell.userData.ink = true;
  mesh.parent?.add(shell);
  return shell;
}

const protos = new Map<UnitTypeId, Rig>();

/**
 * Release what the shared figures hold on the GPU. Their geometry outlives any one renderer, and three keeps each
 * buffer in a WeakMap keyed by that geometry, so without this the first renderer's canvas, context and everything its
 * listeners reach would stay in memory for as long as the page lives.
 */
export function releaseSharedFigures(): void {
  for (const r of protos.values()) r.root.traverse((o) => { (o as Mesh).geometry?.dispose(); });
  for (const m of unitMatCache.values()) m.dispose();
  for (const m of inkMats.values()) m.dispose();
  inkMats.clear();
}

function protoRig(type: UnitTypeId): Rig {
  let proto = protos.get(type);
  if (proto) return proto;
  const rig: Rig = {
    type,
    kind: UNITS[type].naval ? 'ship' : mounts.includes(type) ? 'mounted' : type === 'settler' ? 'cart' : type === 'catapult' ? 'siege' : type === 'cannon' ? 'cannon' : 'biped',
    root: new Group(),
    height: heights[type] ?? 0.62,
    mats: [],
    horseLegs: [],
    oxLegs: [],
    wheels: [],
    oars: [],
    sails: [],
  };
  const rb = new RigBuild();
  rb.root = rig.root;
  // humans are modelled facing +z; every rig is turned to face +x so one heading drives them all
  if (rig.kind === 'biped') rig.root.rotation.y = Math.PI / 2;
  BUILDERS[type](rb, rig);
  // placeholder materials: every unit swaps in its own when cloned
  rb.finish(new MeshStandardMaterial(), new MeshStandardMaterial());
  protos.set(type, rig);
  return rig;
}

/** A fresh figure for one unit: geometry is shared with every other unit of the type, materials are its own. */
export function buildRig(type: UnitTypeId, team: string, u: SharedUniforms): Rig {
  const proto = protoRig(type);
  const before: Object3D[] = [];
  proto.root.traverse((o) => before.push(o));
  const root = proto.root.clone(true);
  const after: Object3D[] = [];
  root.traverse((o) => after.push(o));
  const map = (o?: Object3D): Object3D | undefined => (o ? after[before.indexOf(o)] : undefined);
  const um = new MeshStandardMaterial({ vertexColors: true, roughness: 0.78, metalness: 0.05 });
  patchMaterial(um, u, { paint: 26 });
  const tm = new MeshStandardMaterial({ color: new Color(team), roughness: 0.7, metalness: 0.0 });
  patchMaterial(tm, u, { paint: 26 });
  const meshes: Mesh[] = [];
  root.traverse((o) => {
    const m = o as Mesh;
    if (m.isMesh) meshes.push(m);
  });
  for (const m of meshes) {
    m.material = m.userData.team ? tm : um;
    addInk(m, u);
  }
  return {
    ...proto,
    root,
    mats: [um, tm],
    body: map(proto.body),
    head: map(proto.head),
    armL: map(proto.armL),
    armR: map(proto.armR),
    legL: map(proto.legL),
    legR: map(proto.legR),
    weapon: map(proto.weapon),
    rider: map(proto.rider),
    horseLegs: proto.horseLegs.map((o) => map(o)!),
    oxLegs: proto.oxLegs.map((o) => map(o)!),
    wheels: proto.wheels.map((o) => map(o)!),
    tail: map(proto.tail),
    neck: map(proto.neck),
    throwArm: map(proto.throwArm),
    barrel: map(proto.barrel),
    flag: map(proto.flag),
    oars: proto.oars.map((o) => map(o)!),
    sails: proto.sails.map((o) => map(o)!),
  };
}

// ---------------------------------------------------------------- animation
const lerp = (a: number, b: number, k: number) => a + (b - a) * k;
const ease = (k: number) => k * k * (3 - 2 * k);

/** Drive a rig's limbs for this frame; positions and facing are handled by the caller. */
export function animateRig(r: Rig, p: Pose): void {
  const idle = Math.sin(p.t * 2.1 + p.seed) * 0.5 + 0.5;
  const w = p.walk;
  const sw = Math.sin(p.phase);
  const atk = p.attack;
  const swing = atk >= 0 ? (atk < 0.35 ? ease(atk / 0.35) * -1 : atk < 0.55 ? lerp(-1, 1, ease((atk - 0.35) / 0.2)) : lerp(1, 0, ease((atk - 0.55) / 0.45))) : 0;
  switch (r.kind) {
    case 'biped': {
      const b = r.body!;
      b.position.y = 0.18 - p.fort * 0.035 - p.sleep * 0.06 + Math.abs(sw) * 0.022 * w + idle * 0.004;
      b.rotation.z = sw * 0.045 * w;
      b.rotation.x = p.sleep * 0.5 + (atk >= 0 ? swing * 0.1 : 0);
      b.rotation.y = atk >= 0 ? -swing * 0.35 : 0;
      r.legL!.rotation.x = sw * 0.75 * w - p.fort * 0.5;
      r.legR!.rotation.x = -sw * 0.75 * w - p.fort * 0.5;
      r.legL!.rotation.z = 0;
      const ranged = r.type === 'archer' || r.type === 'crossbowman' || r.type === 'musketman';
      const aim = r.type === 'archer' ? 1.45 : r.type === 'crossbowman' ? 1.35 : 1.25;
      const armLx = -sw * 0.5 * w + Math.sin(p.t * 1.4 + p.seed) * 0.03;
      const armRx = sw * 0.5 * w - Math.sin(p.t * 1.4 + p.seed) * 0.03;
      r.armL!.rotation.set(armLx, 0, 0.1);
      r.armR!.rotation.set(armRx, 0, -0.1);
      if (r.type === 'worker') {
        if (p.work) {
          const k = (p.t * 2.2 + p.seed) % 1;
          r.armR!.rotation.x = k < 0.5 ? lerp(-2.7, -2.0, k * 2) : lerp(-2.0, 0.4, ease((k - 0.5) * 2));
          r.body!.rotation.x = 0.12 + (k > 0.5 ? 0.18 : 0);
        } else r.armR!.rotation.x = -0.3 + sw * 0.3 * w;
      } else if (ranged) {
        if (atk >= 0) {
          // aim, hold, release
          const k = atk < 0.6 ? ease(atk / 0.6) : 1 - ease((atk - 0.6) / 0.4) * 0.0;
          r.armL!.rotation.set(-aim * k, 0, 0.0);
          r.armR!.rotation.set(-aim * k + (r.type === 'archer' ? Math.sin(Math.min(1, atk / 0.55) * Math.PI) * -0.05 : 0), r.type === 'archer' ? 0.5 * (1 - (atk > 0.62 ? 1 : 0)) : 0, 0.0);
          if (r.type === 'musketman' || r.type === 'crossbowman') r.armL!.rotation.set(-aim * k * 0.9, -0.35 * k, 0);
          r.body!.rotation.y = -0.4 * k;
        } else if (w < 0.1 && r.type !== 'archer') r.armR!.rotation.x = -0.5;
      } else if (atk >= 0) {
        if (r.type === 'spearman' || r.type === 'pikeman') {
          const thrust = atk < 0.4 ? -ease(atk / 0.4) * 0.35 : atk < 0.6 ? lerp(-0.35, 1.0, ease((atk - 0.4) / 0.2)) : lerp(1.0, 0, ease((atk - 0.6) / 0.4));
          r.armR!.rotation.set(-1.15, 0, -0.2);
          r.armR!.position.z = thrust * 0.1;
          r.armR!.position.y = 0.18;
        } else {
          r.armR!.rotation.set(-1.4 + swing * 1.9 - 0.9 * Math.max(0, -swing), 0, -0.2);
        }
      } else if (r.type === 'spearman' || r.type === 'pikeman') {
        r.armR!.rotation.set(-0.15 + sw * 0.3 * w, 0, -0.12);
        r.armR!.position.z = 0;
      }
      if (p.fort > 0 && atk < 0) {
        if (r.type !== 'archer') r.armL!.rotation.set(lerp(armLx, -1.15, p.fort), 0, lerp(0.1, 0.45, p.fort));
      }
      r.head!.rotation.y = Math.sin(p.t * 0.7 + p.seed * 2) * 0.35 * (1 - w);
      r.head!.rotation.x = p.sleep * 0.6;
      break;
    }
    case 'mounted': {
      const gallop = p.phase * 1.35;
      const g = Math.sin(gallop);
      const legs = r.horseLegs;
      // front pair leads, hind pair follows
      legs[0].rotation.z = g * 0.8 * w;
      legs[1].rotation.z = Math.sin(gallop + 0.5) * 0.8 * w;
      legs[2].rotation.z = Math.sin(gallop + Math.PI) * 0.8 * w;
      legs[3].rotation.z = Math.sin(gallop + Math.PI + 0.5) * 0.8 * w;
      const parent = legs[0].parent!;
      parent.position.y = 0.3 + Math.abs(g) * 0.03 * w + idle * 0.003 - p.fort * 0.02;
      parent.rotation.z = g * 0.05 * w;
      r.neck!.rotation.z = Math.sin(p.t * 0.9 + p.seed) * 0.05 + g * 0.08 * w;
      r.tail!.rotation.z = Math.sin(p.t * 2 + p.seed) * 0.15 + g * 0.2 * w;
      const rd = r.rider!;
      rd.rotation.z = -g * 0.04 * w;
      r.armL!.rotation.set(-0.5, 0, 0.2);
      if (atk >= 0) r.armR!.rotation.set(-1.4 + swing * 0.9, 0, -0.2);
      else r.armR!.rotation.set(-0.35, 0, -0.1);
      break;
    }
    case 'cart': {
      const g = Math.sin(p.phase * 1.2);
      for (const wh of r.wheels) wh.rotation.z = -p.phase * 0.9;
      const [a, b, c, d] = r.oxLegs;
      if (a) {
        a.rotation.z = g * 0.6 * w;
        b.rotation.z = -g * 0.6 * w;
        c.rotation.z = -g * 0.6 * w;
        d.rotation.z = g * 0.6 * w;
      }
      r.flag!.rotation.y = Math.sin(p.t * 3 + p.seed) * 0.35;
      r.root.rotation.z = Math.sin(p.phase * 1.2) * 0.012 * w;
      break;
    }
    case 'siege': {
      for (const wh of r.wheels) wh.rotation.z = -p.phase * 0.9;
      const arm = r.throwArm!;
      if (atk >= 0) {
        // winch back, then fling forward
        arm.rotation.z = atk < 0.45 ? lerp(0, 0.9, ease(atk / 0.45)) : atk < 0.55 ? lerp(0.9, -1.4, ease((atk - 0.45) / 0.1)) : lerp(-1.4, 0, ease((atk - 0.55) / 0.45));
      } else arm.rotation.z = Math.sin(p.t * 1.2 + p.seed) * 0.02;
      r.flag!.rotation.y = Math.sin(p.t * 3 + p.seed) * 0.35;
      break;
    }
    case 'ship': {
      // gentle heave and roll; oars and sails work while under way
      const swell = Math.sin(p.t * 1.5 + p.seed);
      r.root.position.y = swell * 0.012 + Math.abs(Math.sin(p.phase * 0.6)) * 0.01 * w;
      r.root.rotation.z = Math.sin(p.t * 1.1 + p.seed * 2) * 0.03 + Math.sin(p.phase * 0.6) * 0.02 * w;
      r.root.rotation.x = Math.sin(p.t * 1.3 + p.seed) * 0.025;
      r.oars.forEach((o, i) => {
        const side = (o.userData.side as number) ?? 1;
        o.rotation.z = Math.sin(p.phase * 1.5 + i * 0.5) * 0.55 * w + (w < 0.1 ? 0.15 : 0);
        o.rotation.y = side * (0.25 + Math.sin(p.phase * 1.5 + i * 0.5) * 0.1 * w);
      });
      r.sails.forEach((sl, i) => {
        sl.rotation.y = Math.sin(p.t * 1.2 + p.seed + i) * 0.06 + w * 0.05;
        sl.scale.z = 1 + Math.sin(p.t * 2 + i) * 0.03 + w * 0.05;
      });
      if (r.flag) r.flag.rotation.y = Math.sin(p.t * 3 + p.seed) * 0.4;
      // an attack makes the guns kick the whole hull
      if (atk >= 0) r.root.rotation.z += (atk > 0.5 ? Math.max(0, 1 - (atk - 0.5) * 4) : 0) * 0.07;
      break;
    }
    case 'cannon': {
      for (const wh of r.wheels) wh.rotation.z = -p.phase * 0.9;
      const bar = r.barrel!;
      if (atk >= 0) {
        const kick = atk < 0.5 ? 0 : Math.max(0, 1 - (atk - 0.5) / 0.35);
        bar.position.x = -0.02 - kick * 0.09;
        bar.rotation.z = kick * 0.08;
      } else {
        bar.position.x = -0.02;
        bar.rotation.z = 0;
      }
      r.flag!.rotation.y = Math.sin(p.t * 3 + p.seed) * 0.35;
      break;
    }
  }
}

/** The glowing ring that marks a unit's owner and selection, built once and recoloured. */
export function ringGeometry(): BufferGeometry {
  return new TorusGeometry(0.34, 0.018, 6, 36).rotateX(Math.PI / 2);
}
