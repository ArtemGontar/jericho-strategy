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
};

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
  horseman:    U({ id: 'horseman', name: 'Horseman', icon: '🏇', cost: 55, moves: 4, strength: 12, ranged: 0, range: 0, tech: 'husbandry', needs: 'horses', mounted: true, vision: 2, desc: 'Fast raider. Requires horses.', obsoleteBy: 'chivalry' }),
  swordsman:   U({ id: 'swordsman', name: 'Swordsman', icon: '⚔️', cost: 60, moves: 2, strength: 15, ranged: 0, range: 0, tech: 'ironworking', needs: 'iron', vision: 2, desc: 'Powerful melee infantry. Requires iron.', obsoleteBy: 'gunpowder' }),
  catapult:    U({ id: 'catapult', name: 'Catapult', icon: '🪨', cost: 75, moves: 2, strength: 5, ranged: 14, range: 2, tech: 'mathematics', siege: true, vision: 2, desc: 'Siege engine, devastating against cities.', obsoleteBy: 'metallurgy' }),
  crossbowman: U({ id: 'crossbowman', name: 'Crossbowman', icon: '🎯', cost: 80, moves: 2, strength: 8, ranged: 16, range: 2, tech: 'engineering', vision: 2, desc: 'Accurate ranged infantry.' }),
  pikeman:     U({ id: 'pikeman', name: 'Pikeman', icon: '🛡️', cost: 70, moves: 2, strength: 18, ranged: 0, range: 0, tech: 'feudalism', antiMounted: 100, vision: 2, desc: 'Tough defender, strong against mounted units.' }),
  knight:      U({ id: 'knight', name: 'Knight', icon: '🐴', cost: 95, moves: 4, strength: 24, ranged: 0, range: 0, tech: 'chivalry', needs: 'horses', mounted: true, vision: 2, desc: 'Heavy cavalry. Requires horses.' }),
  musketman:   U({ id: 'musketman', name: 'Musketman', icon: '🔫', cost: 110, moves: 2, strength: 30, ranged: 0, range: 0, tech: 'gunpowder', vision: 2, desc: 'Gunpowder infantry.' }),
  cannon:      U({ id: 'cannon', name: 'Cannon', icon: '💣', cost: 130, moves: 2, strength: 10, ranged: 34, range: 2, tech: 'metallurgy', siege: true, vision: 2, desc: 'Heavy artillery.' }),
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
  desc: string;
}

const B = (d: BuildingDef): BuildingDef => d;
export const BUILDINGS: Record<BuildingId, BuildingDef> = {
  granary:      B({ id: 'granary', name: 'Granary', icon: '🏺', cost: 40, upkeep: 1, tech: 'pottery', food: 2, desc: '+2 food.' }),
  walls:        B({ id: 'walls', name: 'Walls', icon: '🧱', cost: 60, upkeep: 1, tech: 'masonry', desc: '+100 city HP and +50% city defence.' }),
  barracks:     B({ id: 'barracks', name: 'Barracks', icon: '⚔️', cost: 60, upkeep: 1, tech: 'bronze', desc: 'New military units start veteran (+15% strength) and heal fully in the city.' }),
  library:      B({ id: 'library', name: 'Library', icon: '📚', cost: 70, upkeep: 1, tech: 'writing', science: 3, desc: '+3 science.' }),
  workshop:     B({ id: 'workshop', name: 'Workshop', icon: '🔨', cost: 70, upkeep: 1, tech: 'mining', prod: 2, desc: '+2 production.' }),
  market:       B({ id: 'market', name: 'Market', icon: '⚖️', cost: 80, upkeep: 0, tech: 'currency', gold: 3, desc: '+3 gold.' }),
  temple:       B({ id: 'temple', name: 'Temple', icon: '🛕', cost: 60, upkeep: 1, tech: 'philosophy', culture: 2, science: 1, desc: '+2 culture, +1 science.' }),
  aqueduct:     B({ id: 'aqueduct', name: 'Aqueduct', icon: '🌉', cost: 80, upkeep: 1, tech: 'construction', growth: 1, desc: 'Allows the city to grow beyond 8 population.' }),
  university:   B({ id: 'university', name: 'University', icon: '🎓', cost: 120, upkeep: 2, tech: 'education', needs: 'library', science: 5, desc: '+5 science. Requires a Library.' }),
  bank:         B({ id: 'bank', name: 'Bank', icon: '🏦', cost: 120, upkeep: 2, tech: 'banking', needs: 'market', gold: 5, desc: '+5 gold. Requires a Market.' }),
  observatory:  B({ id: 'observatory', name: 'Observatory', icon: '🔭', cost: 120, upkeep: 2, tech: 'astronomy', science: 4, culture: 1, desc: '+4 science.' }),
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
  era: 0 | 1 | 2 | 3;
  cost: number;
  pre: TechId[];
  desc: string;
}
const T = (id: TechId, name: string, era: 0 | 1 | 2 | 3, cost: number, pre: TechId[], desc: string): TechDef => ({ id, name, era, cost, pre, desc });
export const ERA_NAMES = ['Ancient', 'Classical', 'Medieval', 'Renaissance'];
export const TECHS: TechDef[] = [
  T('pottery', 'Pottery', 0, 25, [], 'Granary'),
  T('mining', 'Mining', 0, 25, [], 'Mines, Workshop'),
  T('archery', 'Archery', 0, 30, [], 'Archer'),
  T('bronze', 'Bronze Working', 0, 35, ['mining'], 'Spearman, Barracks, Colossus'),
  T('husbandry', 'Animal Husbandry', 0, 30, [], 'Horseman'),
  T('masonry', 'Masonry', 0, 30, [], 'Walls, Pyramids'),
  T('writing', 'Writing', 0, 35, ['pottery'], 'Library, Great Library'),
  T('wheel', 'The Wheel', 0, 35, ['husbandry'], 'Roads'),
  T('ironworking', 'Iron Working', 1, 70, ['bronze'], 'Swordsman'),
  T('mathematics', 'Mathematics', 1, 70, ['archery', 'masonry'], 'Catapult'),
  T('currency', 'Currency', 1, 70, ['bronze'], 'Market'),
  T('philosophy', 'Philosophy', 1, 80, ['writing'], 'Temple, Oracle'),
  T('construction', 'Construction', 1, 80, ['masonry', 'wheel'], 'Aqueduct, Hanging Gardens'),
  T('education', 'Education', 2, 130, ['philosophy'], 'University'),
  T('engineering', 'Engineering', 2, 130, ['mathematics', 'construction'], 'Crossbowman'),
  T('feudalism', 'Feudalism', 2, 130, ['ironworking'], 'Pikeman'),
  T('chivalry', 'Chivalry', 2, 150, ['feudalism', 'husbandry'], 'Knight'),
  T('banking', 'Banking', 2, 150, ['currency', 'education'], 'Bank'),
  T('gunpowder', 'Gunpowder', 3, 230, ['feudalism', 'engineering'], 'Musketman'),
  T('metallurgy', 'Metallurgy', 3, 260, ['gunpowder', 'mathematics'], 'Cannon'),
  T('astronomy', 'Astronomy', 3, 230, ['education'], 'Observatory'),
  T('enlightenment', 'Enlightenment', 3, 400, ['astronomy', 'banking', 'gunpowder'], 'Science victory'),
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
}
export const POLICIES: PolicyDef[] = [
  { id: 'agrarian', name: 'Agrarian Reform', icon: 'farm', desc: '+1 food in every city.' },
  { id: 'craftsmen', name: 'Guild Craftsmen', icon: 'production', desc: '+1 production in every city.' },
  { id: 'coinage', name: 'Royal Coinage', icon: 'gold', desc: '+10% gold.' },
  { id: 'scholarship', name: 'Scholarship', icon: 'science', desc: '+10% science.' },
  { id: 'patronage', name: 'Patronage of the Arts', icon: 'culture', desc: '+2 culture in every city.' },
  { id: 'drill', name: 'Standing Army', icon: 'swords', desc: 'Military units cost 20% less to build.' },
  { id: 'fortification', name: 'Fortification Doctrine', icon: 'walls', desc: 'Cities are 25% stronger and have +60 HP.' },
  { id: 'exploration', name: 'Age of Exploration', icon: 'explore', desc: 'All units see one tile further.' },
  { id: 'bureaucracy', name: 'Civil Service', icon: 'policy', desc: 'Each city pays 1 less building upkeep.' },
  { id: 'conscription', name: 'Field Medicine', icon: 'health', desc: 'Units heal 10 more HP per turn.' },
  { id: 'trade_routes', name: 'Caravan Routes', icon: 'trade', desc: '+4 gold in the capital and +1 gold in every other city.' },
  { id: 'pioneers', name: 'Pioneer Spirit', icon: 'settler', desc: 'Settlers cost 30% less and workers build 25% faster.' },
];
export const POLICY_BY_ID: Record<string, PolicyDef> = Object.fromEntries(POLICIES.map((p) => [p.id, p]));
export function policyCost(adopted: number, speedMult: number): number {
  return Math.round(28 * Math.pow(adopted + 1, 1.5) * speedMult);
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
