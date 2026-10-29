import { beforeAll, describe, expect, it } from 'vitest';
import type { Cinematics as CinematicsT, Clip } from '../src/client/cinematics';

let Cinematics: typeof CinematicsT;
beforeAll(async () => {
  (globalThis as unknown as { window: unknown }).window = { addEventListener() {}, removeEventListener() {}, matchMedia: () => ({ matches: false }) };
  ({ Cinematics } = await import('../src/client/cinematics'));
});

function rig(busy = false) {
  const cam = {
    x: 100, y: 100, zoom: 1, w: 800, h: 600,
    worldOfTile: (x: number, y: number) => ({ x: x * 10, y: y * 10 }),
    minZoom: () => 0.3,
    clamp() {},
  };
  const log: string[] = [];
  const ui = { show: (c: Clip) => log.push(`show ${c.id}`), hide: () => log.push('hide') };
  const host = { cam, ui, busy: () => busy, wake() {} };
  return { cam, log, cine: new Cinematics(host as never) };
}

const clip = (id: string, over: Partial<Clip> = {}): Clip => ({ id, priority: 3, dur: 1000, focus: { x: 50, y: 40 }, zoom: [[0, 1], [0.4, 1.5], [0.8, 1.5], [1, 1]], ...over });

describe('Cinematics', () => {
  it('moves to the focus and hands the camera back exactly where it was', () => {
    const { cam, cine } = rig();
    cine.play(clip('a'), 0);
    let moved = false;
    for (let t = 0; t <= 1200; t += 16) {
      cine.update(t);
      if (Math.abs(cam.x - 500) < 60 && cam.zoom > 1.3) moved = true;
    }
    expect(moved).toBe(true);
    expect(cine.active).toBe(false);
    expect(cam.x).toBeCloseTo(100, 3);
    expect(cam.y).toBeCloseTo(100, 3);
    expect(cam.zoom).toBeCloseTo(1, 3);
  });

  it('is cancelled by the player moving the camera, without snapping it back', () => {
    const { cam, cine } = rig();
    cine.play(clip('a'), 0);
    for (let t = 0; t <= 400; t += 16) cine.update(t);
    cam.x += 80; // a drag
    cine.update(420);
    expect(cine.active).toBe(false);
    expect(cam.x).toBeGreaterThan(200);
  });

  it('plays higher priority first, never the same clip twice, and drops stale ones', () => {
    const { cine, log } = rig();
    cine.play(clip('low', { priority: 2 }), 0);
    cine.play(clip('high', { priority: 4 }), 0);
    cine.play(clip('high', { priority: 4 }), 0);
    cine.update(1);
    expect(log.filter((l) => l.startsWith('show'))).toEqual(['show high']);
    const b = rig(true);
    b.cine.play(clip('waiting', { priority: 3 }), 0);
    b.cine.update(100);
    expect(b.cine.active).toBe(false); // the player is busy: it waits
    b.cine.update(20000);
    expect(b.log.some((l) => l.startsWith('show'))).toBe(false); // and goes stale
  });

  it('keeps the camera where a finale leaves it', () => {
    const { cam, cine } = rig();
    cine.play(clip('end', { priority: 5, stay: true, endZoom: 0, zoom: [[0, 1], [1, 1]] }), 0);
    for (let t = 0; t <= 1100; t += 16) cine.update(t);
    expect(cine.active).toBe(false);
    expect(cam.zoom).toBeCloseTo(0.3, 2);
  });
});
