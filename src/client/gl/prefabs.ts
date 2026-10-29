import { BufferGeometry, CylinderGeometry, IcosahedronGeometry, OctahedronGeometry } from 'three';
import { Builder } from './geo';

/** Low-poly props: everything is built once and instanced or cloned per tile. */
export const Prefab = {
  broadleaf(): BufferGeometry {
    const b = new Builder();
    b.cyl(0.022, 0.04, 0.16, '#5b4129', {}, 6);
    b.sphere(0.17, '#4c9a3c', { y: 0.27, sy: 0.92 }, 9);
    b.sphere(0.13, '#62ad44', { x: -0.1, y: 0.2, z: 0.05 }, 8);
    b.sphere(0.125, '#3b8a36', { x: 0.1, y: 0.22, z: -0.04 }, 8);
    b.sphere(0.1, '#82bd4c', { x: 0.01, y: 0.4, z: 0.02 }, 8);
    return b.build();
  },
  pine(): BufferGeometry {
    const b = new Builder();
    b.cyl(0.018, 0.034, 0.12, '#55402b', {}, 6);
    b.cone(0.17, 0.22, '#2a6b48', { y: 0.08 }, 8);
    b.cone(0.135, 0.2, '#308050', { y: 0.2 }, 8);
    b.cone(0.1, 0.18, '#3a9058', { y: 0.31 }, 8);
    b.cone(0.06, 0.13, '#52a362', { y: 0.41 }, 8);
    return b.build();
  },
  rock(): BufferGeometry {
    const g = new IcosahedronGeometry(0.1, 0);
    const b = new Builder();
    b.add(g, '#9d9183', { y: 0.05, sy: 0.7, sx: 1.2 });
    b.add(new IcosahedronGeometry(0.06, 0), '#85796c', { x: 0.1, y: 0.03, z: 0.04, sy: 0.75 });
    return b.build();
  },
  cactus(): BufferGeometry {
    const b = new Builder();
    b.cyl(0.035, 0.04, 0.26, '#4f8a45', {}, 7);
    b.sphere(0.036, '#5a9a4c', { y: 0.26 }, 7);
    b.cyl(0.024, 0.026, 0.1, '#4f8a45', { x: 0.075, y: 0.09, rz: -1.2 }, 6);
    b.cyl(0.022, 0.024, 0.09, '#4f8a45', { x: 0.115, y: 0.15 }, 6);
    b.cyl(0.024, 0.026, 0.1, '#4f8a45', { x: -0.075, y: 0.12, rz: 1.2 }, 6);
    b.cyl(0.022, 0.024, 0.08, '#4f8a45', { x: -0.115, y: 0.17 }, 6);
    return b.build();
  },
  stalk(): BufferGeometry {
    const b = new Builder();
    b.add(new CylinderGeometry(0.004, 0.006, 0.17, 4).translate(0, 0.085, 0), '#a8963a');
    b.add(new OctahedronGeometry(0.014, 0).scale(1, 3.4, 1).translate(0, 0.19, 0), '#e8c648');
    return b.build();
  },
  tuft(): BufferGeometry {
    const b = new Builder();
    b.cone(0.014, 0.12, '#6fa845', { x: 0, z: 0, rz: 0.12 }, 3);
    b.cone(0.014, 0.15, '#79b44c', { x: 0.02, z: 0.01, rz: -0.18 }, 3);
    b.cone(0.014, 0.1, '#5c9439', { x: -0.018, z: -0.012, rz: 0.3 }, 3);
    b.cone(0.012, 0.11, '#82bd55', { x: 0.005, z: 0.02, rx: 0.25 }, 3);
    return b.build();
  },
  flower(color: string): BufferGeometry {
    const b = new Builder();
    b.cyl(0.004, 0.004, 0.07, '#5e9a3e', {}, 3);
    b.sphere(0.017, color, { y: 0.075, sy: 0.6 }, 5);
    b.sphere(0.008, '#f4d35a', { y: 0.082 }, 4);
    return b.build();
  },
  haystack(): BufferGeometry {
    const b = new Builder();
    b.cyl(0.07, 0.075, 0.07, '#d4b552', {}, 8);
    b.sphere(0.07, '#dcbf5a', { y: 0.07, sy: 0.75 }, 8);
    return b.build();
  },
  barn(): BufferGeometry {
    const b = new Builder();
    b.box(0.26, 0.15, 0.2, '#a84a35', { y: 0.075 });
    b.gable(0.3, 0.1, 0.24, '#6e3326', { y: 0.15 });
    b.box(0.06, 0.09, 0.005, '#4a2a20', { y: 0.045, z: 0.103 });
    // fence
    for (let i = 0; i < 5; i++) b.cyl(0.006, 0.006, 0.07, '#8a6b45', { x: -0.2 + i * 0.1, z: 0.22 }, 4);
    b.box(0.4, 0.008, 0.008, '#8a6b45', { y: 0.05, z: 0.22 });
    return b.build();
  },
  mine(): BufferGeometry {
    const b = new Builder();
    b.add(new IcosahedronGeometry(0.26, 0), '#8a857c', { y: 0.06, sy: 0.7, sx: 1.25 });
    b.add(new IcosahedronGeometry(0.15, 0), '#756f66', { x: 0.2, y: 0.03, z: 0.08, sy: 0.8 });
    b.box(0.17, 0.15, 0.1, '#17110d', { y: 0.08, z: 0.22 });
    b.box(0.03, 0.17, 0.03, '#8a6038', { x: -0.095, y: 0.085, z: 0.25 });
    b.box(0.03, 0.17, 0.03, '#8a6038', { x: 0.095, y: 0.085, z: 0.25 });
    b.box(0.23, 0.03, 0.04, '#8a6038', { y: 0.17, z: 0.25 });
    // ore cart and spoil
    b.box(0.1, 0.05, 0.07, '#5a4630', { x: 0.2, y: 0.03, z: 0.22 });
    b.add(new IcosahedronGeometry(0.05, 0), '#554d44', { x: 0.2, y: 0.07, z: 0.22 });
    b.add(new IcosahedronGeometry(0.06, 0), '#6a6359', { x: -0.2, y: 0.03, z: 0.2 });
    return b.build();
  },
  hut(): BufferGeometry {
    const b = new Builder();
    const one = (x: number, z: number, s: number) => {
      b.cyl(0.1 * s, 0.11 * s, 0.1 * s, '#b4905f', { x, z }, 9);
      b.cone(0.15 * s, 0.17 * s, '#d2ab55', { x, y: 0.1 * s, z }, 9);
      b.box(0.04 * s, 0.07 * s, 0.01, '#2a1c10', { x, y: 0.035 * s, z: z + 0.105 * s });
    };
    one(-0.1, -0.02, 1);
    one(0.13, 0.08, 0.85);
    b.cyl(0.03, 0.04, 0.012, '#3a3028', { x: 0.0, z: 0.22 }, 7);
    b.cone(0.02, 0.05, '#e8842a', { x: 0.0, y: 0.012, z: 0.22 }, 5);
    return b.build();
  },
  crystal(color: string): BufferGeometry {
    const b = new Builder();
    b.add(new OctahedronGeometry(0.045, 0), color, { y: 0.07, sy: 1.8 });
    b.add(new OctahedronGeometry(0.03, 0), color, { x: 0.06, y: 0.05, z: 0.03, sy: 1.6, rz: 0.4 });
    b.add(new OctahedronGeometry(0.03, 0), color, { x: -0.05, y: 0.04, z: -0.03, sy: 1.5, rz: -0.4 });
    return b.build();
  },
  oreRocks(): BufferGeometry {
    const b = new Builder();
    b.add(new IcosahedronGeometry(0.12, 0), '#7c776f', { x: -0.07, y: 0.05, sy: 0.75 });
    b.add(new IcosahedronGeometry(0.1, 0), '#69645c', { x: 0.1, y: 0.04, z: 0.04, sy: 0.8 });
    b.add(new IcosahedronGeometry(0.08, 0), '#85807a', { x: 0.0, y: 0.04, z: -0.1, sy: 0.8 });
    return b.build();
  },
  /** quadruped body without its head; the head is separate so it can nod */
  animalBody(coat: string, patch: string | null, size: number, long = 1): BufferGeometry {
    const b = new Builder();
    b.sphere(0.1 * size, coat, { y: 0.17 * size, sx: 1.35 * long, sy: 0.82, sz: 0.8 }, 9);
    if (patch) b.sphere(0.06 * size, patch, { x: -0.02, y: 0.2 * size, sx: 1.4, sy: 0.7, sz: 0.9 }, 7);
    for (const [lx, lz] of [[-0.09, -0.04], [-0.09, 0.04], [0.09, -0.04], [0.09, 0.04]]) {
      b.cyl(0.014 * size, 0.018 * size, 0.12 * size, patch ? '#3a2e26' : coat, { x: lx * size * long, z: lz * size }, 5);
    }
    b.cyl(0.006, 0.01, 0.1 * size, coat, { x: -0.13 * size * long, y: 0.12 * size, rz: 0.5 }, 4);
    return b.build();
  },
  animalHead(coat: string, size: number, horns: 'none' | 'horns' | 'antlers' | 'mane'): BufferGeometry {
    const b = new Builder();
    b.sphere(0.052 * size, coat, { x: 0.0, y: 0.0, sx: 1.2 }, 8);
    b.sphere(0.036 * size, coat, { x: 0.05 * size, y: -0.014 * size, sx: 1.3, sy: 0.9 }, 7);
    b.sphere(0.01, '#16110d', { x: 0.015 * size, y: 0.02 * size, z: 0.04 * size }, 4);
    b.sphere(0.01, '#16110d', { x: 0.015 * size, y: 0.02 * size, z: -0.04 * size }, 4);
    if (horns === 'horns') {
      b.cone(0.012, 0.06, '#e4dcc4', { x: -0.01, y: 0.04, z: 0.05, rx: 0.9 }, 5);
      b.cone(0.012, 0.06, '#e4dcc4', { x: -0.01, y: 0.04, z: -0.05, rx: -0.9 }, 5);
    } else if (horns === 'antlers') {
      for (const s of [-1, 1]) {
        b.cyl(0.006, 0.008, 0.11, '#d8c9a2', { x: -0.015, y: 0.03, z: 0.03 * s, rx: 0.35 * s }, 4);
        b.cyl(0.005, 0.006, 0.06, '#d8c9a2', { x: 0.0, y: 0.1, z: 0.07 * s, rx: 0.9 * s }, 4);
        b.cyl(0.005, 0.006, 0.05, '#d8c9a2', { x: -0.03, y: 0.1, z: 0.06 * s, rx: 0.6 * s }, 4);
      }
    } else if (horns === 'mane') {
      b.box(0.02, 0.11, 0.035, '#241913', { x: -0.045, y: 0.0, rz: 0.4 });
    }
    return b.build();
  },
  animalNeck(coat: string, size: number, len: number): BufferGeometry {
    const b = new Builder();
    b.cyl(0.03 * size, 0.046 * size, 0.15 * size * len, coat, { rz: -0.75 }, 7);
    return b.build();
  },
  fish(): BufferGeometry {
    const b = new Builder();
    b.sphere(0.045, '#dfe9ee', { sx: 2.2, sy: 0.8, sz: 0.7 }, 7);
    b.cone(0.03, 0.06, '#c8d6dc', { x: -0.1, rz: -Math.PI / 2 }, 4);
    b.cone(0.02, 0.04, '#91b4c4', { y: 0.03, rz: 0.0 }, 4);
    return b.build();
  },
  dust(): BufferGeometry {
    return new Builder().sphere(0.01, '#fff').build();
  },
};
