import type { BuildingId, Focus, ImprovementId, ResourceId, TechId, TerrainId, UnitTypeId, WorkKind } from './types';

// ---------------------------------------------------------------- terrain
export interface TerrainDef {
  name: string;
  food: number;
  prod: number;
  gold: number;
  /** movement points to enter (flat = 2) */
  move: number;
  /** % defence bonus for a unit standing here */
  defense: number;
  passable: boolean;
  land: boolean;
  workable: boolean;
}

export const TERRAIN: Record<TerrainId, TerrainDef> = {
  ocean:    { name: 'Ocean',    food: 1, prod: 0, gold: 0, move: 99, defense: 0,  passable: false, land: false, workable: true },
  coast:    { name: 'Coast',    food: 1, prod: 0, gold: 1, move: 99, defense: 0,  passable: false, land: false, workable: true },
  grass:    { name: 'Grassland',food: 2, prod: 0, gold: 0, move: 2,  defense: 0,  passable: true,  land: true,  workable: true },
  plains:   { name: 'Plains',   food: 1, prod: 1, gold: 0, move: 2,  defense: 0,  passable: true,  land: true,  workable: true },
  desert:   { name: 'Desert',   food: 0, prod: 0, gold: 0, move: 2,  defense: 0,  passable: true,  land: true,  workable: true },
  tundra:   { name: 'Tundra',   food: 1, prod: 0, gold: 0, move: 2,  defense: 0,  passable: true,  land: true,  workable: true },
  forest:   { name: 'Forest',   food: 1, prod: 1, gold: 0, move: 4,  defense: 25, passable: true,  land: true,  workable: true },
  hills:    { name: 'Hills',    food: 0, prod: 2, gold: 0, move: 4,  defense: 25, passable: true,  land: true,  workable: true },
  mountain: { name: 'Mountains',food: 0, prod: 0, gold: 0, move: 99, defense: 0,  passable: false, land: true,  workable: false },
};

export interface ResourceDef {
  name: string;
  icon: string;
  food: number;
  prod: number;
  gold: number;
  strategic: boolean;
  /** invisible, and worth nothing, until its owner knows this technology (coal and oil: fossil fuel waits to be found and used) */
  reveal?: string;
  /** a luxury: each kind a civilisation holds (or trades for) makes its people more content */
  luxury?: boolean;
  terrains: TerrainId[];
}

export const RESOURCES: Record<ResourceId, ResourceDef> = {
  wheat:  { name: 'Wheat',  icon: '🌾', food: 2, prod: 0, gold: 0, strategic: false, terrains: ['grass', 'plains'] },
  cattle: { name: 'Cattle', icon: '🐄', food: 1, prod: 1, gold: 0, strategic: false, terrains: ['grass'] },
  fish:   { name: 'Fish',   icon: '🐟', food: 2, prod: 0, gold: 0, strategic: false, terrains: ['coast'] },
  deer:   { name: 'Deer',   icon: '🦌', food: 1, prod: 1, gold: 0, strategic: false, terrains: ['forest'] },
  horses: { name: 'Horses', icon: '🐎', food: 0, prod: 1, gold: 0, strategic: true,  terrains: ['grass', 'plains'] },
  iron:   { name: 'Iron',   icon: '⛏️', food: 0, prod: 1, gold: 0, strategic: true,  terrains: ['hills', 'plains', 'desert'] },
  gold:   { name: 'Gold',   icon: '💰', food: 0, prod: 0, gold: 3, strategic: false, terrains: ['hills', 'desert', 'plains'] },
  wine:   { name: 'Wine',   icon: '🍇', food: 0, prod: 0, gold: 1, strategic: false, luxury: true, terrains: ['grass', 'plains'] },
  spices: { name: 'Spices', icon: '🌶️', food: 0, prod: 0, gold: 1, strategic: false, luxury: true, terrains: ['forest', 'desert', 'plains'] },
  gems:   { name: 'Gems',   icon: '💎', food: 0, prod: 0, gold: 2, strategic: false, luxury: true, terrains: ['hills', 'desert'] },
  coal:   { name: 'Coal',   icon: '⚫', food: 0, prod: 2, gold: 0, strategic: false, reveal: 'industrialization', terrains: ['hills', 'plains', 'desert', 'tundra'] },
  oil:    { name: 'Oil',    icon: '🛢️', food: 0, prod: 1, gold: 3, strategic: false, reveal: 'combustion', terrains: ['desert', 'plains', 'tundra', 'coast'] },
  pearls: { name: 'Pearls', icon: '🦪', food: 0, prod: 0, gold: 1, strategic: false, luxury: true, terrains: ['coast'] },
};
export const LUXURIES = (Object.keys(RESOURCES) as ResourceId[]).filter((r) => RESOURCES[r].luxury);

export const IMPROVEMENTS: Record<WorkKind, { name: string; icon: string; turns: number; tech: TechId | null; terrains: TerrainId[] }> = {
  farm: { name: 'Farm', icon: '🌱', turns: 5, tech: null, terrains: ['grass', 'plains', 'desert', 'tundra'] },
  mine: { name: 'Mine', icon: '⛏', turns: 6, tech: 'mining', terrains: ['hills'] },
  road: { name: 'Road', icon: '🛤', turns: 3, tech: 'wheel', terrains: ['grass', 'plains', 'desert', 'tundra', 'forest', 'hills'] },
};
export const IMPROVEMENT_YIELD: Record<ImprovementId, { food: number; prod: number; gold: number }> = {
  farm: { food: 1, prod: 0, gold: 0 },
  mine: { food: 0, prod: 2, gold: 0 },
};

// ---------------------------------------------------------------- units
export interface UnitDef {
  id: UnitTypeId;
  name: string;
  icon: string;
  cost: number;
  /** full movement points; internally x2 */
  moves: number;
  strength: number;
  ranged: number;
  range: number;
  tech: TechId | null;
  needs?: ResourceId;
  civilian?: boolean;
  /** bonus vs mounted units (percent) */
  antiMounted?: number;
  mounted?: boolean;
  siege?: boolean;
  /** ignores terrain movement costs */
  scout?: boolean;
  vision: number;
  desc: string;
  /** a ship: sails on water instead of walking on land */
  naval?: boolean;
  /** how many land units a ship carries (0 = none) */
  capacity?: number;
  /** a ship that may leave the coast for the open ocean */
  deepSea?: boolean;
  /** obsolete when this tech is known (can no longer be built) */
  obsoleteBy?: TechId;
}

const U = (d: UnitDef): UnitDef => d;
export const UNITS: Record<UnitTypeId, UnitDef> = {
  settler:     U({ id: 'settler', name: 'Settler', icon: '🏕️', cost: 60, moves: 2, strength: 0, ranged: 0, range: 0, tech: null, civilian: true, vision: 2, desc: 'Founds a new city. Costs 1 population.' }),
  worker:      U({ id: 'worker', name: 'Worker', icon: '🧑‍🌾', cost: 40, moves: 2, strength: 0, ranged: 0, range: 0, tech: null, civilian: true, vision: 2, desc: 'Builds farms, mines and roads.' }),
  scout:       U({ id: 'scout', name: 'Scout', icon: '🧭', cost: 25, moves: 3, strength: 4, ranged: 0, range: 0, tech: null, scout: true, vision: 3, desc: 'Fast explorer that ignores terrain costs.' }),
  warrior:     U({ id: 'warrior', name: 'Warrior', icon: '🗡️', cost: 30, moves: 2, strength: 8, ranged: 0, range: 0, tech: null, vision: 2, desc: 'Basic melee fighter.', obsoleteBy: 'ironworking' }),
  archer:      U({ id: 'archer', name: 'Archer', icon: '🏹', cost: 40, moves: 2, strength: 5, ranged: 8, range: 2, tech: 'archery', vision: 2, desc: 'Shoots enemies two tiles away without retaliation from melee units.', obsoleteBy: 'engineering' }),
  spearman:    U({ id: 'spearman', name: 'Spearman', icon: '🔱', cost: 45, moves: 2, strength: 11, ranged: 0, range: 0, tech: 'bronze', antiMounted: 100, vision: 2, desc: 'Strong against mounted units.', obsoleteBy: 'feudalism' }),
  horseman:    U({ id: 'horseman', name: 'Horseman', icon: '🏇', cost: 55, moves: 4, strength: 12, ranged: 0, range: 0, tech: 'husbandry', needs: 'horses', mounted: true, vision: 2, desc: 'Fast raider that rides down archers and siege crews. Requires horses.', obsoleteBy: 'chivalry' }),
  swordsman:   U({ id: 'swordsman', name: 'Swordsman', icon: '⚔️', cost: 60, moves: 2, strength: 15, ranged: 0, range: 0, tech: 'ironworking', needs: 'iron', vision: 2, desc: 'Powerful melee infantry. Requires iron.', obsoleteBy: 'gunpowder' }),
  catapult:    U({ id: 'catapult', name: 'Catapult', icon: '🪨', cost: 75, moves: 2, strength: 5, ranged: 14, range: 2, tech: 'mathematics', siege: true, vision: 2, desc: 'Siege engine. Only siege does full damage to a walled city.', obsoleteBy: 'metallurgy' }),
  crossbowman: U({ id: 'crossbowman', name: 'Crossbowman', icon: '🎯', cost: 80, moves: 2, strength: 8, ranged: 16, range: 2, tech: 'engineering', vision: 2, desc: 'Accurate ranged infantry.' }),
  pikeman:     U({ id: 'pikeman', name: 'Pikeman', icon: '🛡️', cost: 70, moves: 2, strength: 18, ranged: 0, range: 0, tech: 'feudalism', antiMounted: 100, vision: 2, desc: 'Tough defender, strong against mounted units.' }),
  knight:      U({ id: 'knight', name: 'Knight', icon: '🐴', cost: 95, moves: 4, strength: 24, ranged: 0, range: 0, tech: 'chivalry', needs: 'horses', mounted: true, vision: 2, desc: 'Heavy cavalry that rides down archers and siege crews. Requires horses.' }),
  musketman:   U({ id: 'musketman', name: 'Musketman', icon: '🔫', cost: 110, moves: 2, strength: 30, ranged: 0, range: 0, tech: 'gunpowder', vision: 2, desc: 'Gunpowder infantry.' }),
  trireme:     U({ id: 'trireme', name: 'Trireme', icon: '⛵', cost: 45, moves: 3, strength: 6, ranged: 0, range: 0, tech: 'sailing', naval: true, capacity: 2, vision: 2, desc: 'Coastal ship that carries two land units. Cannot enter the deep ocean.', obsoleteBy: 'navigation' }),
  caravel:     U({ id: 'caravel', name: 'Caravel', icon: '⛵', cost: 80, moves: 4, strength: 12, ranged: 0, range: 0, tech: 'navigation', naval: true, capacity: 3, deepSea: true, vision: 3, desc: 'Ocean-going ship that carries three land units.' }),
  frigate:     U({ id: 'frigate', name: 'Frigate', icon: '🚢', cost: 120, moves: 4, strength: 18, ranged: 22, range: 2, tech: 'metallurgy', naval: true, capacity: 0, deepSea: true, vision: 3, desc: 'Warship that bombards ships and the coast from two tiles away.' }),
  cannon:      U({ id: 'cannon', name: 'Cannon', icon: '💣', cost: 130, moves: 2, strength: 10, ranged: 34, range: 2, tech: 'metallurgy', siege: true, vision: 2, desc: 'Heavy artillery. Only siege does full damage to a walled city.' }),
};

// ---------------------------------------------------------------- buildings
export interface BuildingDef {
  id: BuildingId;
  name: string;
  icon: string;
  cost: number;
  upkeep: number;
  tech: TechId | null;
  needs?: BuildingId;
  wonder?: boolean;
  coastal?: boolean;
  food?: number;
  prod?: number;
  gold?: number;
  science?: number;
  culture?: number;
  growth?: number;
  /** contentment this building gives the whole empire */
  content?: number;
  /** takes no slot in the city (walls, barracks and every civilisation's own building) */
  free?: boolean;
  /** only this civilisation can build it */
  civ?: string;
  desc: string;
}

const B = (d: BuildingDef): BuildingDef => d;
export const BUILDINGS: Record<BuildingId, BuildingDef> = {
  granary:      B({ id: 'granary', name: 'Granary', icon: '🏺', cost: 40, upkeep: 1, tech: 'pottery', food: 2, desc: '+2 food.' }),
  walls:        B({ id: 'walls', name: 'Walls', icon: '🧱', cost: 60, upkeep: 1, tech: 'masonry', free: true, desc: '+100 city HP and +40% city defence. Only siege engines do full damage to a walled city.' }),
  barracks:     B({ id: 'barracks', name: 'Barracks', icon: '⚔️', cost: 60, upkeep: 1, tech: 'bronze', free: true, desc: 'New military units start veteran (+15% strength) and heal fully in the city.' }),
  library:      B({ id: 'library', name: 'Library', icon: '📚', cost: 70, upkeep: 1, tech: 'writing', science: 3, desc: '+3 science.' }),
  workshop:     B({ id: 'workshop', name: 'Workshop', icon: '🔨', cost: 70, upkeep: 1, tech: 'mining', prod: 2, desc: '+2 production.' }),
  market:       B({ id: 'market', name: 'Market', icon: '⚖️', cost: 80, upkeep: 0, tech: 'currency', gold: 3, desc: '+3 gold.' }),
  temple:       B({ id: 'temple', name: 'Temple', icon: '🛕', cost: 60, upkeep: 1, tech: 'philosophy', culture: 2, science: 1, content: 1, desc: '+2 culture, +1 science, +1 contentment.' }),
  aqueduct:     B({ id: 'aqueduct', name: 'Aqueduct', icon: '🌉', cost: 80, upkeep: 1, tech: 'construction', growth: 1, desc: 'Allows the city to grow beyond 8 population.' }),
  university:   B({ id: 'university', name: 'University', icon: '🎓', cost: 120, upkeep: 2, tech: 'education', needs: 'library', science: 5, desc: '+5 science. Requires a Library.' }),
  bank:         B({ id: 'bank', name: 'Bank', icon: '🏦', cost: 120, upkeep: 2, tech: 'banking', needs: 'market', gold: 5, desc: '+5 gold. Requires a Market.' }),
  observatory:  B({ id: 'observatory', name: 'Observatory', icon: '🔭', cost: 120, upkeep: 2, tech: 'astronomy', science: 4, culture: 1, desc: '+4 science.' }),
  harbor:       B({ id: 'harbor', name: 'Harbor', icon: '⚓', cost: 60, upkeep: 1, tech: 'sailing', coastal: true, food: 1, desc: 'Coastal city: +1 food, and +1 food from every worked water tile.' }),
  // civic buildings: they keep the people content and culture growing
  shrine:       B({ id: 'shrine', name: 'Shrine', icon: '⛩️', cost: 30, upkeep: 0, tech: 'ceremony', content: 1, desc: '+1 contentment. Cheap, and early.' }),
  amphitheater: B({ id: 'amphitheater', name: 'Amphitheater', icon: '🎭', cost: 70, upkeep: 1, tech: 'drama', content: 2, desc: '+2 contentment.' }),
  embassy:      B({ id: 'embassy', name: 'Embassy', icon: '🕊️', cost: 60, upkeep: 1, tech: 'diplomacy', gold: 1, desc: '+1 gold, and one more trade agreement. Two embassies count.' }),
  cathedral:    B({ id: 'cathedral', name: 'Cathedral', icon: '⛪', cost: 120, upkeep: 2, tech: 'theology', needs: 'temple', culture: 1, content: 3, desc: '+3 contentment, +1 culture. Requires a Temple.' }),
  // the industrial age: more output and healthier cities, at a price
  factory:      B({ id: 'factory', name: 'Factory', icon: '🏭', cost: 150, upkeep: 3, tech: 'industrialization', needs: 'workshop', prod: 5, desc: '+5 production. Requires a Workshop.' }),
  hospital:     B({ id: 'hospital', name: 'Hospital', icon: '🏥', cost: 110, upkeep: 2, tech: 'sanitation', food: 1, content: 1, desc: '+1 food, +1 contentment, and plague cannot strike this city.' }),
  laboratory:   B({ id: 'laboratory', name: 'Laboratory', icon: '🔬', cost: 150, upkeep: 3, tech: 'electricity', needs: 'university', science: 6, desc: '+6 science. Requires a University.' }),
  // one building only a single civilisation can raise; it takes no slot, so it is always worth having
  spring_gardens:  B({ id: 'spring_gardens', name: 'Spring Gardens', icon: '🌿', cost: 50, upkeep: 0, tech: 'pottery', food: 3, content: 1, free: true, civ: 'jericho', desc: 'Jericho only. +3 food, +1 contentment. Takes no slot.' }),
  ziggurat:        B({ id: 'ziggurat', name: 'Ziggurat', icon: '🛕', cost: 70, upkeep: 1, tech: 'writing', science: 3, culture: 1, free: true, civ: 'sumer', desc: 'Sumer only. +3 science, +1 culture. Takes no slot.' }),
  sun_temple:      B({ id: 'sun_temple', name: 'Temple of the Sun', icon: '☀️', cost: 60, upkeep: 0, tech: 'masonry', prod: 1, culture: 2, content: 1, free: true, civ: 'egypt', desc: 'Egypt only. +1 production, +2 culture, +1 contentment. Takes no slot.' }),
  great_bath:      B({ id: 'great_bath', name: 'Great Bath', icon: '🛁', cost: 70, upkeep: 1, tech: 'construction', food: 1, content: 2, free: true, civ: 'indus', desc: 'Indus only. +2 contentment, +1 food. Takes no slot.' }),
  forum:           B({ id: 'forum', name: 'Forum', icon: '🏛️', cost: 80, upkeep: 0, tech: 'currency', gold: 4, content: 1, free: true, civ: 'rome', desc: 'Rome only. +4 gold, +1 contentment. Takes no slot.' }),
  satrapy:         B({ id: 'satrapy', name: 'Satrapy', icon: '🏺', cost: 80, upkeep: 1, tech: 'currency', gold: 2, prod: 2, free: true, civ: 'persia', desc: 'Persia only. +2 gold, +2 production. Takes no slot.' }),
  stepped_pyramid: B({ id: 'stepped_pyramid', name: 'Stepped Pyramid', icon: '🔺', cost: 90, upkeep: 1, tech: 'mathematics', science: 2, culture: 2, free: true, civ: 'maya', desc: 'Maya only. +2 science, +2 culture. Takes no slot.' }),
  agora:           B({ id: 'agora', name: 'Agora', icon: '🏛️', cost: 80, upkeep: 0, tech: 'philosophy', science: 2, gold: 1, content: 2, free: true, civ: 'greece', desc: 'Greece only. +2 contentment, +2 science, +1 gold. Takes no slot.' }),
  great_wall:      B({ id: 'great_wall', name: 'Great Wall', icon: '🧱', cost: 190, upkeep: 0, tech: 'construction', wonder: true, culture: 2, content: 2, desc: 'Wonder: +2 culture, +2 contentment.' }),
  lighthouse:      B({ id: 'lighthouse', name: 'Great Lighthouse', icon: '🗼', cost: 170, upkeep: 0, tech: 'sailing', wonder: true, coastal: true, food: 2, gold: 3, culture: 2, desc: 'Wonder (coastal city): +2 food, +3 gold, +2 culture.' }),
  notre_dame:      B({ id: 'notre_dame', name: 'Notre Dame', icon: '⛪', cost: 230, upkeep: 0, tech: 'theology', wonder: true, culture: 6, content: 3, desc: 'Wonder: +6 culture, +3 contentment.' }),
  crystal_palace:  B({ id: 'crystal_palace', name: 'Crystal Palace', icon: '🏛️', cost: 260, upkeep: 0, tech: 'industrialization', wonder: true, prod: 6, science: 3, culture: 3, desc: 'Wonder: +6 production, +3 science, +3 culture.' }),
  pyramids:        B({ id: 'pyramids', name: 'Pyramids', icon: '🔺', cost: 170, upkeep: 0, tech: 'masonry', wonder: true, prod: 4, culture: 3, desc: 'Wonder: +4 production, +3 culture.' }),
  great_library:   B({ id: 'great_library', name: 'Great Library', icon: '🏛️', cost: 190, upkeep: 0, tech: 'writing', wonder: true, science: 8, culture: 3, desc: 'Wonder: +8 science, +3 culture.' }),
  hanging_gardens: B({ id: 'hanging_gardens', name: 'Hanging Gardens', icon: '🌳', cost: 190, upkeep: 0, tech: 'construction', wonder: true, food: 4, culture: 3, desc: 'Wonder: +4 food, +3 culture.' }),
  oracle:          B({ id: 'oracle', name: 'Oracle', icon: '🔮', cost: 210, upkeep: 0, tech: 'philosophy', wonder: true, culture: 8, gold: 2, desc: 'Wonder: +8 culture, +2 gold.' }),
  colossus:        B({ id: 'colossus', name: 'Colossus', icon: '🗿', cost: 200, upkeep: 0, tech: 'bronze', wonder: true, coastal: true, gold: 7, culture: 3, desc: 'Wonder (coastal city): +7 gold, +3 culture.' }),
};
export const WONDER_IDS = (Object.keys(BUILDINGS) as BuildingId[]).filter((b) => BUILDINGS[b].wonder);

// ---------------------------------------------------------------- technology
export interface TechDef {
  id: TechId;
  name: string;
  era: 0 | 1 | 2 | 3 | 4;
  cost: number;
  pre: TechId[];
  /** when given, one of these is needed as well: a fork in the tree */
  any?: TechId[];
  desc: string;
}
const T = (id: TechId, name: string, era: 0 | 1 | 2 | 3 | 4, cost: number, pre: TechId[], desc: string, any?: TechId[]): TechDef => ({ id, name, era, cost, pre, desc, ...(any ? { any } : {}) });
export const ERA_NAMES = ['Ancient', 'Classical', 'Medieval', 'Renaissance', 'Industrial'];
export const TECHS: TechDef[] = [
  T('pottery', 'Pottery', 0, 25, [], 'Granary'),
  T('mining', 'Mining', 0, 25, [], 'Mines, Workshop'),
  T('archery', 'Archery', 0, 30, [], 'Archer'),
  T('bronze', 'Bronze Working', 0, 35, ['mining'], 'Spearman, Barracks, Colossus'),
  T('husbandry', 'Animal Husbandry', 0, 30, [], 'Horseman'),
  T('masonry', 'Masonry', 0, 30, [], 'Walls, Pyramids'),
  T('writing', 'Writing', 0, 35, ['pottery'], 'Library, Great Library'),
  T('wheel', 'The Wheel', 0, 35, ['husbandry'], 'Roads'),
  T('sailing', 'Sailing', 0, 35, ['pottery'], 'Trireme, Harbor, Lighthouse'),
  T('ceremony', 'Ceremony', 0, 30, [], 'Shrine: an early way to keep people content'),
  T('ironworking', 'Iron Working', 1, 70, ['bronze'], 'Swordsman'),
  T('mathematics', 'Mathematics', 1, 70, ['archery', 'masonry'], 'Catapult'),
  T('currency', 'Currency', 1, 70, ['bronze'], 'Market'),
  T('philosophy', 'Philosophy', 1, 80, ['writing'], 'Temple, Oracle'),
  T('construction', 'Construction', 1, 80, ['masonry', 'wheel'], 'Aqueduct, Hanging Gardens, Great Wall: buildings survive earthquakes'),
  T('drama', 'Drama and Poetry', 1, 75, ['writing'], 'Amphitheater'),
  T('diplomacy', 'Diplomacy', 1, 80, ['writing', 'currency'], 'Embassy: one more trade agreement'),
  T('education', 'Education', 2, 130, ['philosophy'], 'University'),
  T('engineering', 'Engineering', 2, 130, ['mathematics', 'construction'], 'Crossbowman'),
  T('feudalism', 'Feudalism', 2, 130, ['ironworking'], 'Pikeman'),
  T('chivalry', 'Chivalry', 2, 150, ['feudalism', 'husbandry'], 'Knight'),
  T('navigation', 'Navigation', 2, 140, ['sailing', 'mathematics'], 'Caravel, ocean voyages, one more trade agreement'),
  T('medicine', 'Medicine', 2, 140, ['philosophy', 'construction'], 'Citizens weigh less on contentment'),
  T('theology', 'Theology', 2, 150, ['philosophy', 'drama'], 'Cathedral, Notre Dame'),
  T('banking', 'Banking', 2, 150, ['currency', 'education'], 'Bank'),
  T('gunpowder', 'Gunpowder', 3, 230, ['feudalism', 'engineering'], 'Musketman'),
  T('metallurgy', 'Metallurgy', 3, 260, ['gunpowder', 'mathematics'], 'Cannon, Frigate'),
  T('astronomy', 'Astronomy', 3, 230, ['education'], 'Observatory'),
  T('industrialization', 'Industrialization', 4, 170, ['metallurgy', 'banking'], 'Factory, Crystal Palace, and coal that can be dug'),
  T('sanitation', 'Sanitation', 4, 190, ['medicine', 'industrialization'], 'Hospital: no more plague. The road of health'),
  T('electricity', 'Electricity', 4, 230, ['industrialization', 'astronomy'], 'Laboratory: the road of learning'),
  T('combustion', 'Combustion', 4, 170, ['industrialization', 'navigation'], 'Oil that can be pumped. The road of the sea and of industry'),
  // three roads to the end, each costing about the same and each leaving something different behind
  T('enlightenment', 'Enlightenment', 4, 200, ['astronomy', 'banking', 'industrialization'], 'Science victory. Reached by one of three roads: Electricity, Sanitation or Combustion', ['electricity', 'sanitation', 'combustion']),
];
export const TECH_BY_ID: Record<string, TechDef> = Object.fromEntries(TECHS.map((t) => [t.id, t]));
export const FINAL_TECH = 'enlightenment';

// ---------------------------------------------------------------- civilisations
export interface CivDef {
  id: string;
  name: string;
  leader: string;
  adjective: string;
  color: string;
  bonus: string;
  cities: string[];
}
export const CIVS: CivDef[] = [
  { id: 'jericho', name: 'Jericho', leader: 'The Spring Warden', adjective: 'Jerichan', color: '#e0a526', bonus: '+1 food in every city centre', cities: ['Jericho', 'Ain es-Sultan', 'Gilgal', 'Bethel', 'Shechem', 'Hebron', 'Gezer', 'Lachish', 'Megiddo', 'Hazor'] },
  { id: 'sumer', name: 'Sumer', leader: 'Queen Puabi', adjective: 'Sumerian', color: '#3b8fe0', bonus: '+10% science', cities: ['Uruk', 'Ur', 'Eridu', 'Lagash', 'Nippur', 'Kish', 'Umma', 'Larsa', 'Isin', 'Adab'] },
  { id: 'egypt', name: 'Egypt', leader: 'Hatshepsut', adjective: 'Egyptian', color: '#d9642e', bonus: '+1 production in the capital, cheaper wonders', cities: ['Thebes', 'Memphis', 'Giza', 'Heliopolis', 'Abydos', 'Karnak', 'Aswan', 'Amarna', 'Edfu', 'Luxor'] },
  { id: 'indus', name: 'Indus', leader: 'The Priest-King', adjective: 'Harappan', color: '#46b36b', bonus: '+10% gold', cities: ['Harappa', 'Mohenjo-daro', 'Dholavira', 'Lothal', 'Rakhigarhi', 'Kalibangan', 'Ganweriwala', 'Chanhudaro', 'Sutkagen Dor', 'Mehrgarh'] },
  { id: 'rome', name: 'Rome', leader: 'Augustus', adjective: 'Roman', color: '#b8323f', bonus: 'Military units are built 15% faster', cities: ['Roma', 'Antium', 'Cumae', 'Neapolis', 'Ravenna', 'Mediolanum', 'Capua', 'Arretium', 'Ostia', 'Tarentum'] },
  { id: 'persia', name: 'Persia', leader: 'Cyrus', adjective: 'Persian', color: '#9d5bd6', bonus: 'Workers build 50% faster', cities: ['Persepolis', 'Susa', 'Pasargadae', 'Ecbatana', 'Babylon', 'Sardis', 'Bactra', 'Tarsus', 'Hamadan', 'Shiraz'] },
  { id: 'maya', name: 'Maya', leader: 'Pakal the Great', adjective: 'Mayan', color: '#1fa89b', bonus: '+2 culture per city', cities: ['Tikal', 'Calakmul', 'Palenque', 'Copan', 'Caracol', 'Uxmal', 'Chichen Itza', 'Mayapan', 'Yaxchilan', 'Coba'] },
  { id: 'greece', name: 'Greece', leader: 'Pericles', adjective: 'Greek', color: '#e6e6ee', bonus: '+2 gold per city, +1 science per temple/library', cities: ['Athens', 'Sparta', 'Corinth', 'Thebes', 'Argos', 'Delphi', 'Olympia', 'Megara', 'Rhodes', 'Syracuse'] },
];
export const CIV_BY_ID: Record<string, CivDef> = Object.fromEntries(CIVS.map((c) => [c.id, c]));

// ---------------------------------------------------------------- tuning
export const FOCUS_WEIGHTS: Record<Focus, { food: number; prod: number; gold: number }> = {
  balanced: { food: 1.2, prod: 1.1, gold: 0.8 },
  food: { food: 2.2, prod: 0.8, gold: 0.5 },
  production: { food: 0.9, prod: 2.2, gold: 0.6 },
  gold: { food: 0.9, prod: 0.8, gold: 2.4 },
};

export const MAP_SIZES = {
  small: { width: 30, height: 20, label: 'Small (2–3 players)' },
  medium: { width: 40, height: 26, label: 'Medium (3–5 players)' },
  large: { width: 52, height: 34, label: 'Large (5–8 players)' },
} as const;

export const MAX_PLAYERS = 8;
export const CITY_BASE_HP = 160;
export const UNIT_HP = 100;
export const MIN_CITY_DISTANCE = 3;
export const PEACE_MIN_TURNS = 10;

// ---------------------------------------------------------------- policies, promotions, upgrades
export interface PolicyDef {
  id: string;
  name: string;
  icon: string;
  desc: string;
  /** only offered to a civilisation that knows this technology: the later policies */
  tech?: string;
}
export const POLICIES: PolicyDef[] = [
  { id: 'agrarian', name: 'Agrarian Reform', icon: 'farm', desc: '+1 food in every city.' },
  { id: 'craftsmen', name: 'Guild Craftsmen', icon: 'production', desc: '+1 production in every city.' },
  { id: 'coinage', name: 'Royal Coinage', icon: 'gold', desc: '+10% gold.' },
  { id: 'scholarship', name: 'Scholarship', icon: 'science', desc: '+10% science.' },
  { id: 'patronage', name: 'Patronage of the Arts', icon: 'culture', desc: '+2 culture in every city.' },
  { id: 'drill', name: 'Standing Army', icon: 'swords', desc: 'Military units cost 20% less to build.' },
  { id: 'fortification', name: 'Fortification Doctrine', icon: 'walls', desc: 'Cities are 25% stronger and have +60 HP.' },
  { id: 'exploration', name: 'Age of Exploration', icon: 'explore', desc: 'All units see one tile further. Scouts and ships move one tile more.' },
  { id: 'bureaucracy', name: 'Civil Service', icon: 'policy', desc: 'Each city pays 1 less building upkeep.' },
  { id: 'conscription', name: 'Field Medicine', icon: 'health', desc: 'Units heal 10 more HP per turn.' },
  { id: 'trade_routes', name: 'Caravan Routes', icon: 'trade', desc: '+4 gold in the capital and +1 gold in every other city.' },
  { id: 'pioneers', name: 'Pioneer Spirit', icon: 'settler', desc: 'Settlers cost 30% less and workers build 25% faster.' },
  { id: 'merchants', name: 'Merchant Guilds', icon: 'market', desc: '+1 gold for every Market and +2 for every Bank.' },
  { id: 'scribes', name: 'Scribes and Copyists', icon: 'library', desc: '+1 science for every Library and +2 for every University.' },
  { id: 'monuments', name: 'Monument Builders', icon: 'wonder', desc: 'Wonders cost 25% less and give +2 culture more.' },
  { id: 'veterans', name: 'Veteran Corps', icon: 'level', desc: 'Every new soldier starts as a veteran, with or without Barracks.' },
  { id: 'seafaring', name: 'Seafaring', icon: 'trireme', desc: '+1 food and +1 gold from every worked water tile. Ships cost 25% less.' },
  { id: 'hearth', name: 'Hearth and Home', icon: 'food', desc: 'Cities need 15% less food to grow.' },
  // later policies: each is offered once its technology is known, and each costs something
  { id: 'bread_circuses', name: 'Bread and Circuses', icon: 'culture', tech: 'drama', desc: '+3 contentment, but 5% less production.' },
  { id: 'open_society', name: 'Open Society', icon: 'trade', tech: 'diplomacy', desc: 'One more trade agreement, and +1 gold from each.' },
  { id: 'public_health', name: 'Public Health', icon: 'health', tech: 'medicine', desc: '+2 contentment, and plague never strikes.' },
  { id: 'academies', name: 'Academies', icon: 'science', tech: 'education', desc: '+2 science for every University and Laboratory.' },
  { id: 'state_religion', name: 'State Religion', icon: 'temple', tech: 'theology', desc: '+1 contentment and +1 culture for every Temple, Shrine and Cathedral.' },
  { id: 'industrial_policy', name: 'Industrial Policy', icon: 'production', tech: 'industrialization', desc: '+3 production for every Factory, but 2 less contentment.' },
];
export const POLICY_BY_ID: Record<string, PolicyDef> = Object.fromEntries(POLICIES.map((p) => [p.id, p]));
/**
 * Culture needed for the next policy, as a running total. Each offer is a choice of three and the two you pass over are
 * lost for the rest of the game, so about six policies are adopted in a game: this curve spreads them over its length
 * (roughly turns 25, 45, 65, 90, 120 and 150).
 */
export function policyCost(adopted: number, speedMult: number): number {
  return Math.round(45 * Math.pow(adopted + 1, 2.2) * speedMult);
}

/** Experience needed for each promotion level (level 1, 2, 3). */
export const XP_LEVELS = [14, 40, 80];

export const UNIT_UPGRADES: Partial<Record<UnitTypeId, UnitTypeId>> = {
  warrior: 'swordsman',
  spearman: 'pikeman',
  archer: 'crossbowman',
  horseman: 'knight',
  swordsman: 'musketman',
  pikeman: 'musketman',
  catapult: 'cannon',
};
