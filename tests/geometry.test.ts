import { describe, expect, it } from 'vitest';
import { tileXZ, tileXZf, SQ3 } from '../src/client/gl/terrain';
import { Camera } from '../src/client/render';

describe('map geometry', () => {
  it('3D tile centres agree with the camera that drives the 2D overlay', () => {
    const cam = new Camera();
    for (const [x, y] of [[0, 0], [3, 4], [7, 5], [12, 11], [39, 25]]) {
      const w = cam.worldOfTile(x, y);
      const [X, Z] = tileXZ(x, y);
      expect(X * 48).toBeCloseTo(w.x, 6);
      expect(Z * 48).toBeCloseTo(w.y, 6);
    }
  });

  it('fractional positions interpolate between neighbouring centres, across odd and even rows', () => {
    expect(tileXZf(5, 6)).toEqual(tileXZ(5, 6));
    const [ax, az] = tileXZ(5, 6);
    const [bx, bz] = tileXZ(6, 6);
    const [mx, mz] = tileXZf(5.5, 6);
    expect(mx).toBeCloseTo((ax + bx) / 2, 9);
    expect(mz).toBeCloseTo((az + bz) / 2, 9);
    // stepping down a row moves half a tile sideways on odd rows
    const [cx, cz] = tileXZ(5, 7);
    const [dx, dz] = tileXZf(5, 6.5);
    expect(dx).toBeCloseTo((ax + cx) / 2, 9);
    expect(dz).toBeCloseTo((az + cz) / 2, 9);
    expect(Math.abs(cx - ax)).toBeCloseTo(SQ3 / 2, 9);
  });

  it('the tilted camera maps ground points to the screen and back', () => {
    const cam = new Camera();
    cam.setPitch(52);
    cam.w = 1000;
    cam.h = 700;
    cam.zoom = 1.3;
    cam.x = 800;
    cam.y = 500;
    for (const [wx, wy] of [[0, 0], [900, 450], [1500, 900]]) {
      const s = cam.toScreen(wx, wy);
      const w = cam.toWorld(s.x, s.y);
      expect(w.x).toBeCloseTo(wx, 6);
      expect(w.y).toBeCloseTo(wy, 6);
    }
    // height lifts a point straight up the screen
    const a = cam.toScreen(900, 450, 0);
    const b = cam.toScreen(900, 450, 48);
    expect(b.x).toBeCloseTo(a.x, 9);
    expect(b.y).toBeLessThan(a.y);
  });
});
