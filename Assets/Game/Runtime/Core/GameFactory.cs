using System;
using System.Collections.Generic;

namespace Nasledie.Core
{
    public sealed class DeterministicRandom
    {
        private uint state;
        public uint State { get { return state; } }

        public DeterministicRandom(uint seed) { state = seed == 0 ? 0x9E3779B9u : seed; }

        public uint NextUInt()
        {
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }

        public int Range(int minimumInclusive, int maximumExclusive)
        {
            if (maximumExclusive <= minimumInclusive) return minimumInclusive;
            return minimumInclusive + (int)(NextUInt() % (uint)(maximumExclusive - minimumInclusive));
        }

        public int SignedRange(int magnitude)
        {
            return Range(-magnitude, magnitude + 1);
        }
    }

    public static class HexGrid
    {
        public static int Distance(int aq, int ar, int bq, int br)
        {
            int dq = aq - bq;
            int dr = ar - br;
            return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
        }

        public static readonly int[,] Directions =
        {
            { 1, 0 }, { 1, -1 }, { 0, -1 }, { -1, 0 }, { -1, 1 }, { 0, 1 }
        };
    }

    public static class GameFactory
    {
        private static readonly int[,] StartPositions =
        {
            { 2, 2 }, { 10, 2 }, { 2, 10 }, { 10, 10 }
        };

        public static GameState NewGame(int seed, Difficulty difficulty)
        {
            return NewGame(seed, difficulty, GamePace.Standard, "jericho");
        }

        public static GameState NewGame(int seed, Difficulty difficulty, GamePace pace)
        {
            return NewGame(seed, difficulty, pace, "jericho");
        }

        public static GameState NewGame(int seed, Difficulty difficulty, GamePace pace, string playerNationId)
        {
            NationDefinition selectedNation = NationCatalog.Get(playerNationId);
            if (selectedNation == null) selectedNation = NationCatalog.GetAt(0);
            playerNationId = selectedNation.Id;
            var state = new GameState
            {
                Seed = seed,
                Turn = 1,
                Pace = pace,
                MaximumTurn = GamePacing.MaximumTurn(pace),
                HumanCivilizationId = 0,
                SelectedCityId = 1,
                Crisis = new CrisisState { Kind = CrisisKind.None }
            };

            var rng = new DeterministicRandom((uint)seed);
            state.Cells = GenerateMap(rng);
            state.Civilizations = CreateCivilizations(difficulty, playerNationId);
            state.Cities = CreateCapitals(state);
            state.NextEntityId = state.Cities.Length + 1;
            state.Units = Array.Empty<UnitState>();
            state.Treaties = Array.Empty<TreatyState>();
            state.Conflicts = Array.Empty<ConflictState>();
            state.Events = Array.Empty<ActiveEventState>();
            state.Reports = Array.Empty<TurnReport>();
            state.EventLog = new[] { "Ход 1 · Столица готова к развитию. Выберите приоритет и завершите ход.", "Ход 1 · Люди, здоровье, еда и доверие важнее площади завоёванной земли." };
            state.LastPlayerBalanceTenths = 0;
            RebuildVisibility(state);
            return state;
        }

        private static CivilizationState[] CreateCivilizations(Difficulty difficulty, string playerNationId)
        {
            var civilizations = new CivilizationState[4];
            for (int i = 0; i < civilizations.Length; i++)
            {
                NationDefinition nation = NationCatalog.ForCivilizationIndex(i, playerNationId);
                civilizations[i] = new CivilizationState
                {
                    Id = i,
                    Name = nation.NameRu,
                    NationId = nation.Id,
                    Archetype = nation.Archetype,
                    IsHuman = i == 0,
                    Difficulty = difficulty,
                    TreasuryTenths = i == 0 ? 120 : 120,
                    KnowledgeTenths = 0,
                    InfluenceTenths = 50,
                    CulturePrestige = 0,
                    Reputation = 50,
                    AdministrativeCapacity = 2,
                    Era = Era.Communities,
                    Relations = new[] { 0, 0, 0, 0 },
                    Trusts = new[] { 50, 50, 50, 50 },
                    Threats = new[] { 10, 10, 10, 10 },
                    CulturalInfluence = new[] { 0, 0, 0, 0 },
                    TradeDependency = new[] { 0, 0, 0, 0 },
                    TechnologyProgressTenths = new int[RulesCatalog.Technologies.Length],
                    KnownTechnologies = i == 0 ? new[] { "agriculture" } : new[] { "agriculture" },
                    ActiveResearchIndex = RulesCatalog.TechnologyIndex(i == 0 ? "writing" : "agriculture"),
                    StrategicGoal = i == 0 ? "player" : nation.StrategicGoal,
                    StrategyHoldTurns = 0
                };
                civilizations[i].CompletedTechnologies = 1;
            }
            civilizations[0].Reputation = 55;
            return civilizations;
        }

        private static CityState[] CreateCapitals(GameState state)
        {
            var cities = new CityState[4];
            for (int i = 0; i < cities.Length; i++)
            {
                int q = StartPositions[i, 0];
                int r = StartPositions[i, 1];
                var city = new CityState
                {
                    Id = i + 1,
                    OwnerId = i,
                    Name = NationCatalog.Get(state.Civilizations[i].NationId).CapitalName,
                    Q = q,
                    R = r,
                    PopulationTenths = 60,
                    FoodTenths = 120,
                    FoodStoreCapacityTenths = 240,
                    GrowthTenths = 0,
                    HousingTenths = 100,
                    Health = i == 0 ? 60 : 65,
                    Safety = 75,
                    Education = 25,
                    Trust = i == 0 ? 65 : 62,
                    Environment = 100,
                    Attraction = 50,
                    EfficiencyBasisPoints = 10000,
                    AdministrativeLoad = 1,
                    Workers = new[] { 3, 1, 1, 1, 0, 0 },
                    ServiceFunding = new[] { 1, 1, 1 },
                    Project = new CityProject { Kind = ProjectKind.None, TargetId = "", Cost = 0 },
                    BuildingIds = Array.Empty<string>(),
                    BuildingLevels = Array.Empty<int>(),
                    CompletedCulturalIds = Array.Empty<string>()
                };
                cities[i] = city;
                int startingCapacity = 2;
                if (i > 0 && state.Civilizations[i].Difficulty == Difficulty.Strong) startingCapacity = 3;
                else if (i > 0 && state.Civilizations[i].Difficulty == Difficulty.Trial) startingCapacity = 4;
                state.Civilizations[i].AdministrativeCapacity = startingCapacity;
            }

            foreach (HexCell cell in state.Cells)
            {
                int nearestCity = -1;
                int nearestDistance = int.MaxValue;
                for (int i = 0; i < cities.Length; i++)
                {
                    int distance = HexGrid.Distance(cell.Q, cell.R, cities[i].Q, cities[i].R);
                    if (distance < nearestDistance) { nearestDistance = distance; nearestCity = i; }
                }
                if (nearestDistance <= 1 && cell.Terrain != TerrainKind.Mountain && cell.Terrain != TerrainKind.Water)
                    cell.OwnerId = nearestCity;
            }
            for (int civilizationId = 0; civilizationId < cities.Length; civilizationId++)
            {
                int starterFarms = 0;
                for (int terrainPass = 0; terrainPass < 3 && starterFarms < 2; terrainPass++)
                {
                    for (int i = 0; i < state.Cells.Length && starterFarms < 2; i++)
                    {
                        HexCell cell = state.Cells[i];
                        int distance = HexGrid.Distance(cell.Q, cell.R, cities[civilizationId].Q, cities[civilizationId].R);
                        if (distance != 2 || (cell.OwnerId >= 0 && cell.OwnerId != civilizationId) ||
                            cell.Improvement != ImprovementKind.None) continue;
                        bool preferredTerrain = terrainPass == 0 ? cell.Terrain == TerrainKind.Meadow : terrainPass == 1 ? cell.Terrain == TerrainKind.Plains : cell.Terrain == TerrainKind.Arid;
                        if (!preferredTerrain) continue;
                        cell.OwnerId = civilizationId;
                        cell.Improvement = ImprovementKind.Farm;
                        cell.ImprovementLevel = 1;
                        starterFarms++;
                    }
                }
                for (int terrainPass = 0; terrainPass < 3 && starterFarms < 2; terrainPass++)
                {
                    for (int i = 0; i < state.Cells.Length && starterFarms < 2; i++)
                    {
                        HexCell cell = state.Cells[i];
                        int distance = HexGrid.Distance(cell.Q, cell.R, cities[civilizationId].Q, cities[civilizationId].R);
                        if (distance != 1 || (cell.OwnerId >= 0 && cell.OwnerId != civilizationId) || cell.Improvement != ImprovementKind.None) continue;
                        bool preferredTerrain = terrainPass == 0 ? cell.Terrain == TerrainKind.Meadow : terrainPass == 1 ? cell.Terrain == TerrainKind.Plains : cell.Terrain == TerrainKind.Arid;
                        if (!preferredTerrain) continue;
                        cell.OwnerId = civilizationId;
                        cell.Improvement = ImprovementKind.Farm;
                        cell.ImprovementLevel = 1;
                        starterFarms++;
                    }
                }
            }
            return cities;
        }

        private static HexCell[] GenerateMap(DeterministicRandom rng)
        {
            var cells = new HexCell[169];
            int index = 0;
            for (int q = 0; q < 13; q++)
            {
                for (int r = 0; r < 13; r++)
                {
                    int edge = Math.Min(Math.Min(q, 12 - q), Math.Min(r, 12 - r));
                    int noise = rng.Range(0, 100);
                    TerrainKind terrain;
                    if (edge == 0 && noise < 48) terrain = TerrainKind.Water;
                    else if (edge == 0) terrain = TerrainKind.Coast;
                    else if (noise < 8) terrain = TerrainKind.Mountain;
                    else if (noise < 23) terrain = TerrainKind.Hills;
                    else if (noise < 39) terrain = TerrainKind.Forest;
                    else if (noise < 53) terrain = TerrainKind.Meadow;
                    else if (noise < 61) terrain = TerrainKind.Arid;
                    else if (noise < 67) terrain = TerrainKind.Swamp;
                    else terrain = TerrainKind.Plains;

                    if (NearAnyStart(q, r, 3))
                    {
                        terrain = noise < 22 ? TerrainKind.Forest : noise < 38 ? TerrainKind.Hills : noise < 62 ? TerrainKind.Meadow : TerrainKind.Plains;
                    }
                    if (InCentralCorridor(q, r))
                    {
                        if (terrain == TerrainKind.Water || terrain == TerrainKind.Mountain) terrain = TerrainKind.Plains;
                        if ((q + r) % 7 == 0) terrain = TerrainKind.River;
                    }

                    ResourceKind resource = ResourceKind.None;
                    if (terrain == TerrainKind.Forest && rng.Range(0, 11) == 0) resource = ResourceKind.Iron;
                    else if (terrain == TerrainKind.Swamp && rng.Range(0, 6) == 0) resource = ResourceKind.Herbs;
                    else if (terrain == TerrainKind.Coast && rng.Range(0, 4) == 0) resource = ResourceKind.Salt;
                    else if ((terrain == TerrainKind.Meadow || terrain == TerrainKind.Plains) && rng.Range(0, 9) == 0) resource = ResourceKind.FertileSoil;

                    cells[index++] = new HexCell { Q = q, R = r, Terrain = terrain, Resource = resource, OwnerId = -1, VisibleToMask = 0, ExploredByMask = 0 };
                }
            }
            EnsureFairStarts(cells);
            return cells;
        }

        private static bool InCentralCorridor(int q, int r)
        {
            return q >= 2 && q <= 10 && r >= 2 && r <= 10 && Math.Abs(q - r) <= 4;
        }

        private static bool NearAnyStart(int q, int r, int radius)
        {
            for (int i = 0; i < StartPositions.GetLength(0); i++)
                if (HexGrid.Distance(q, r, StartPositions[i, 0], StartPositions[i, 1]) <= radius) return true;
            return false;
        }

        private static void EnsureFairStarts(HexCell[] cells)
        {
            for (int startIndex = 0; startIndex < StartPositions.GetLength(0); startIndex++)
            {
                int q0 = StartPositions[startIndex, 0];
                int r0 = StartPositions[startIndex, 1];
                int foodPotential = 0;
                int rawMaterial = 0;
                for (int i = 0; i < cells.Length; i++)
                {
                    HexCell cell = cells[i];
                    if (HexGrid.Distance(cell.Q, cell.R, q0, r0) > 3) continue;
                    if (cell.Terrain == TerrainKind.Meadow || cell.Terrain == TerrainKind.Plains || cell.Terrain == TerrainKind.River || cell.Terrain == TerrainKind.Coast)
                        foodPotential++;
                    if (cell.Terrain == TerrainKind.Forest || cell.Terrain == TerrainKind.Hills || cell.Resource == ResourceKind.Iron)
                        rawMaterial++;
                }
                if (foodPotential < 4 || rawMaterial < 1)
                {
                    for (int i = 0; i < cells.Length; i++)
                    {
                        HexCell cell = cells[i];
                        if (HexGrid.Distance(cell.Q, cell.R, q0, r0) > 3) continue;
                        if (foodPotential < 4 && (cell.Terrain == TerrainKind.Water || cell.Terrain == TerrainKind.Mountain || cell.Terrain == TerrainKind.Swamp || cell.Terrain == TerrainKind.Arid))
                        {
                            cell.Terrain = TerrainKind.Meadow;
                            foodPotential++;
                        }
                        if (rawMaterial < 1 && cell.Terrain == TerrainKind.Plains)
                        {
                            cell.Terrain = TerrainKind.Forest;
                            rawMaterial++;
                        }
                    }
                }
            }
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].Terrain == TerrainKind.River && cells[i].Resource == ResourceKind.None)
                {
                    cells[i].Resource = ResourceKind.FertileSoil;
                    break;
                }
            }
        }

        public static void RebuildVisibility(GameState state)
        {
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell cell = state.Cells[i];
                cell.VisibleToMask = 0;
                for (int c = 0; c < state.Civilizations.Length; c++)
                {
                    bool visible = false;
                    for (int j = 0; j < state.Cities.Length && !visible; j++)
                        if (state.Cities[j].OwnerId == c && HexGrid.Distance(cell.Q, cell.R, state.Cities[j].Q, state.Cities[j].R) <= 2) visible = true;
                    for (int j = 0; j < state.Units.Length && !visible; j++)
                        if (state.Units[j].OwnerId == c && HexGrid.Distance(cell.Q, cell.R, state.Units[j].Q, state.Units[j].R) <= 1) visible = true;
                    if (visible)
                    {
                        int mask = 1 << c;
                        cell.VisibleToMask |= mask;
                        cell.ExploredByMask |= mask;
                        cell.LastSeenTurn = state.Turn;
                    }
                }
            }
        }
    }
}
