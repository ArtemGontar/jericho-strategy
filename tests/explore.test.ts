import { describe, expect, it } from 'vitest';
import { runScout } from '../scripts/explore-thrash';

describe('auto-explore', () => {
  it('uncovers the land without dithering and then stands down', () => {
    for (const seed of [902, 903, 905]) {
      const r = runScout(seed, 60);
      // dead ends force the odd retreat, but a scout must not swing back and forth turn after turn
      expect(r.oscillations, `seed ${seed}`).toBeLessThanOrEqual(3);
      expect(r.explored, `seed ${seed}`).toBeGreaterThan(500);
      expect(r.auto, `seed ${seed} still exploring after 60 turns`).toBe(false);
    }
  }, 60000);
});
