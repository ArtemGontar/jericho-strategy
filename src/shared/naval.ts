import { TERRAIN, UNITS } from './data';
import type { TerrainId, UnitTypeId } from './types';

/**
 * Where a unit may stand, by kind: walkers on passable land; ships on coastal water, on the open ocean when they are
 * ocean-going, and in a friendly coastal city (a port).
 */
export function canStandOn(type: UnitTypeId, terrain: TerrainId, friendlyPort: boolean): boolean {
  const def = UNITS[type];
  if (!def.naval) return TERRAIN[terrain].passable;
  if (terrain === 'coast') return true;
  if (terrain === 'ocean') return !!def.deepSea;
  return friendlyPort;
}

/** Movement-point cost of stepping onto this terrain: ships pay one tile (2 points) per step. */
export function navalStepCost(): number {
  return 2;
}

export function isWater(terrain: TerrainId): boolean {
  return !TERRAIN[terrain].land;
}
