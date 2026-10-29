/** The generated illustrations (see scripts/optimize-art.mjs) and where each one is used. */
import { icon } from './icons';

const files = import.meta.glob('./assets/art/**/*.webp', { eager: true, query: '?url', import: 'default' }) as Record<string, string>;
const url = (path: string): string | undefined => files[`./assets/art/${path}.webp`];

/** The civilisations that have a painted leader; the others keep the drawn portrait. */
export function leaderUrl(civ: string): string | undefined {
  return url(`leaders/${civ}`);
}

export const crestUrl = url('crest') ?? '';

/** Unit ids mapped onto the era and role of the painted roster. Ships and workers have no painting. */
const UNIT_SPRITE: Record<string, string> = {
  settler: 'settler', worker: 'worker', scout: 'communities_scout', warrior: 'communities_ranged', archer: 'cities_ranged', spearman: 'communities_infantry',
  horseman: 'communities_cavalry', swordsman: 'cities_infantry', catapult: 'cities_siege', crossbowman: 'connectedlands_ranged',
  pikeman: 'connectedlands_infantry', knight: 'connectedlands_cavalry', musketman: 'industry_infantry', cannon: 'industry_siege',
};

/** Building ids mapped onto the painted set; walls, harbours and wonders have none. */
const BUILDING_SPRITE: Record<string, string> = {
  granary: 'granary', workshop: 'workshop', market: 'market', library: 'archivemuseum', temple: 'culturalhall',
  university: 'university', bank: 'exchangehouse', barracks: 'barracks',
};

export function spriteUrl(id: string): string | undefined {
  const unit = UNIT_SPRITE[id];
  if (unit) return url(`sprites/units/${unit}`);
  const building = BUILDING_SPRITE[id];
  return building ? url(`sprites/buildings/${building}`) : undefined;
}

/** Smallest icon size that is big enough to read a painted sprite. */
const SPRITE_MIN = 20;

/** The painted sprite of a unit or building, or the line icon when it has none or is drawn small. */
export function artIcon(name: string, size = 18, cls = ''): HTMLElement | SVGSVGElement {
  const src = size >= SPRITE_MIN ? spriteUrl(name) : undefined;
  if (!src) return icon(name, size, cls);
  const img = document.createElement('img');
  const px = Math.round(size * 1.4);
  img.className = `sprite ${cls}`.trim();
  img.src = src;
  img.alt = '';
  img.width = px;
  img.height = px;
  img.draggable = false;
  return img;
}
