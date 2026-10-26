using System;

namespace Nasledie.Core
{
    public sealed class CityYield
    {
        public int FoodTenths;
        public int Production;
        public int KnowledgeTenths;
        public int TreasuryTenths;
        public int CulturePrestige;
        public int ServiceCoverage;
        public int HealthChange;
        public int SafetyChange;
        public int EducationChange;
        public int EnvironmentChange;
        public int AttractionChange;
        public int OccupiedWorkers;
        public int UnemployedWorkers;
        public int[] RoleOutputTenths = new int[6];
    }

    public static class EconomyCalculator
    {
        private const int FlexibleCityWorkplaces = 3;

        public static CityYield Calculate(GameState state, CityState city)
        {
            var result = new CityYield();
            CivilizationState civilization = state.Civilization(city.OwnerId);
            if (civilization == null) return result;

            int totalWorkers = city.WorkerCount;
            int assigned = 0;
            for (int i = 0; i < city.Workers.Length; i++) assigned += city.Workers[i];
            result.OccupiedWorkers = assigned;
            result.UnemployedWorkers = Math.Max(0, totalWorkers - assigned);

            int efficiency = Math.Max(5000, Math.Min(11500, city.EfficiencyBasisPoints));
            int farmerCount = At(city.Workers, WorkerRole.Farmer);
            int artisanCount = At(city.Workers, WorkerRole.Artisan);
            int scholarCount = At(city.Workers, WorkerRole.Scholar);
            int merchantCount = At(city.Workers, WorkerRole.Merchant);
            int artistCount = At(city.Workers, WorkerRole.Artist);
            int servantCount = At(city.Workers, WorkerRole.CivilServant);

            result.FoodTenths += Scale(farmerCount * 20, efficiency);
            result.Production += Scale(artisanCount * 2, efficiency);
            result.KnowledgeTenths += Scale(scholarCount * 30, efficiency);
            result.TreasuryTenths += Scale(merchantCount * 20, efficiency);
            result.CulturePrestige += Scale(artistCount, efficiency);
            result.ServiceCoverage += servantCount * 20;
            result.HealthChange += city.ServiceFunding.Length > 0 ? city.ServiceFunding[0] : 0;
            result.SafetyChange += city.ServiceFunding.Length > 1 ? city.ServiceFunding[1] : 0;
            result.EducationChange += city.ServiceFunding.Length > 2 ? city.ServiceFunding[2] : 0;

            int marketTileBonus = 0;
            int fisheryFoodBonus = 0;
            int fisheryTradeBonusTenths = 0;
            int parkCount = 0;
            int fortificationCount = 0;
            int resourceIndustryBonusPercent = 0;
            int resourceHealthBonus = 0;
            int[] farmerCellModifiers = new int[state.Cells.Length + 3];
            int[] artisanCellModifiers = new int[state.Cells.Length + 3];
            int farmerCellCount = 3; // the three flexible city-center workplaces
            int artisanCellCount = 3;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell cell = state.Cells[i];
                if (!IsCellWithinWorkArea(state, city, cell)) continue;
                if (cell.Damage < 75 && cell.Improvement == ImprovementKind.Market) marketTileBonus += 10;
                if (cell.Damage < 75 && cell.Improvement == ImprovementKind.Park) parkCount++;
                if (cell.Damage < 75 && cell.Improvement == ImprovementKind.Fortification) fortificationCount++;
                if (cell.Damage < 75 && cell.Improvement == ImprovementKind.Fishery)
                {
                    fisheryFoodBonus += 20;
                    fisheryTradeBonusTenths += 10;
                }
                if (cell.Damage < 75 && cell.Resource == ResourceKind.Iron && cell.Improvement != ImprovementKind.None) resourceIndustryBonusPercent += 10;
                if (cell.Resource == ResourceKind.Herbs) resourceHealthBonus += 5;

                bool farmed = cell.Damage < 75 && cell.ImprovementLevel > 0 &&
                    (cell.Improvement == ImprovementKind.Farm || cell.Improvement == ImprovementKind.IrrigatedFarm);
                int farmerModifier = cell.Terrain == TerrainKind.Meadow || cell.Terrain == TerrainKind.River ? 10 :
                    cell.Terrain == TerrainKind.Arid || cell.Terrain == TerrainKind.Swamp ? -10 : 0;
                if (farmed)
                {
                    if (cell.Improvement == ImprovementKind.IrrigatedFarm && civilization.Knows("irrigation"))
                        farmerModifier = 20;
                    else farmerModifier += 10;
                }
                if (farmed && cell.Resource == ResourceKind.FertileSoil) farmerModifier += 10;
                farmerCellModifiers[farmerCellCount++] = farmerModifier;

                int artisanModifier = cell.Terrain == TerrainKind.Forest || cell.Terrain == TerrainKind.Hills ? 10 : 0;
                if (cell.Damage < 75 && (cell.Improvement == ImprovementKind.Forestry || cell.Improvement == ImprovementKind.Workshop)) artisanModifier += 10;
                artisanCellModifiers[artisanCellCount++] = artisanModifier;
            }
            result.FoodTenths += Scale(BestWorkerCellBonus(farmerCellModifiers, farmerCellCount, farmerCount), efficiency);
            result.Production += Scale(BestWorkerCellBonus(artisanCellModifiers, artisanCellCount, artisanCount), efficiency) / 10;
            result.FoodTenths += Scale(fisheryFoodBonus, efficiency);
            result.Production = result.Production * (100 + resourceIndustryBonusPercent) / 100;
            result.HealthChange += resourceHealthBonus;
            result.EnvironmentChange += parkCount * 2;
            result.AttractionChange += parkCount * 2;
            result.TreasuryTenths -= fortificationCount * 5;

            int schoolLevel = city.BuildingLevel("school");
            int universityLevel = city.BuildingLevel("university");
            int academyLevel = city.BuildingLevel("academy");
            int marketLevel = city.BuildingLevel("market");
            int workshopLevel = city.BuildingLevel("workshop");
            int factoryLevel = city.BuildingLevel("factory");
            int houseLevel = city.BuildingLevel("house") + city.BuildingLevel("residential_quarter");
            int culturalHouse = city.BuildingLevel("cultural_house");
            int archive = city.BuildingLevel("archive");
            int exchangeHouse = city.BuildingLevel("exchange_house");
            int hospital = city.BuildingLevel("hospital");
            int clinic = city.BuildingLevel("clinic");

            result.Production += artisanCount > 0 ? workshopLevel * 2 : 0;
            BuildingDefinition factory = RulesCatalog.Building("factory");
            if (factory != null && artisanCount > 0 && factoryLevel > 0)
                result.Production += Scale(artisanCount * factoryLevel * factory.ProductionPerArtisan, efficiency);
            result.KnowledgeTenths += scholarCount > 0 ? schoolLevel * 20 + universityLevel * 40 + academyLevel * 50 : 0;
            result.TreasuryTenths += merchantCount > 0 ? marketLevel * 20 + marketTileBonus + fisheryTradeBonusTenths : 0;
            if (artistCount > 0)
            {
                BuildingDefinition culturalHouseDefinition = RulesCatalog.Building("cultural_house");
                BuildingDefinition archiveDefinition = RulesCatalog.Building("archive");
                BuildingDefinition exchangeHouseDefinition = RulesCatalog.Building("exchange_house");
                result.CulturePrestige += culturalHouse * (culturalHouseDefinition == null ? 3 : culturalHouseDefinition.CulturePerArtist) +
                    archive * (archiveDefinition == null ? 5 : archiveDefinition.CulturePerArtist) +
                    exchangeHouse * (exchangeHouseDefinition == null ? 4 : exchangeHouseDefinition.CulturePerArtist);
            }
            result.ServiceCoverage += servantCount > 0 ? (clinic + hospital) * 20 : 0;
            result.HealthChange += clinic + hospital * 2;
            result.SafetyChange += city.BuildingLevel("barracks") + city.BuildingLevel("road_services");
            result.EducationChange += (schoolLevel > 0 ? 1 : 0) + (universityLevel > 0 ? 2 : 0) + (academyLevel > 0 ? 2 : 0);

            for (int i = 0; i < RulesCatalog.Buildings.Length; i++)
            {
                BuildingDefinition district = RulesCatalog.Buildings[i];
                if (string.IsNullOrEmpty(district.RequiredNationId) || district.RequiredNationId != civilization.NationId) continue;
                int level = city.BuildingLevel(district.Id);
                if (level <= 0) continue;
                result.FoodTenths += Scale(district.FlatFoodTenths * level, efficiency);
                result.KnowledgeTenths += Scale(district.FlatKnowledgeTenths * level, efficiency);
                result.TreasuryTenths += Scale(district.FlatTreasuryTenths * level, efficiency);
                result.CulturePrestige += Scale(district.FlatCulturePrestige * level, efficiency);
                result.HealthChange += district.HealthPerTurn * level;
                result.EducationChange += district.EducationPerTurn * level;
                result.SafetyChange += district.SafetyPerTurn * level;
            }
            NationDefinition nationDefinition = NationCatalog.Get(civilization.NationId);
            int campusLevels = schoolLevel + universityLevel + academyLevel;
            if (nationDefinition != null && campusLevels > 0)
            {
                result.KnowledgeTenths += Scale(nationDefinition.CampusKnowledgeTenthsPerLevel * campusLevels, efficiency);
                result.TreasuryTenths += Scale(nationDefinition.CampusTreasuryTenthsPerLevel * campusLevels, efficiency);
                result.CulturePrestige += Scale(nationDefinition.CampusCulturePrestigePerLevel * campusLevels, efficiency);
                result.EducationChange += nationDefinition.CampusEducationPerLevel * campusLevels;
            }

            if (civilization.HasLaw("open_markets")) result.TreasuryTenths = result.TreasuryTenths * 115 / 100;
            if (civilization.HasLaw("reserve_market") && city.FoodStoreCapacityTenths > city.PopulationTenths * 2)
                result.FoodTenths += 10;
            if (civilization.HasLaw("universal_education")) result.KnowledgeTenths = result.KnowledgeTenths * 110 / 100;
            if (civilization.HasLaw("volunteer_service")) result.TreasuryTenths += 2;

            if (civilization.Knows("mechanization")) result.Production += artisanCount > 0 ? 1 : 0;
            if (factoryLevel > 0 && artisanCount > 0 && !civilization.Knows("clean_energy")) result.EnvironmentChange -= factoryLevel * 2;

            int taxMultiplier = civilization.TaxRate == TaxRate.Low ? 7000 : civilization.TaxRate == TaxRate.High ? 13500 : 10000;
            result.TreasuryTenths = result.TreasuryTenths * taxMultiplier / 10000;
            switch (civilization.NationId)
            {
                case "jericho": result.FoodTenths = result.FoodTenths * 110 / 100; break;
                case "sumer": result.KnowledgeTenths = result.KnowledgeTenths * 110 / 100; break;
                case "egypt": result.CulturePrestige = result.CulturePrestige * 110 / 100; break;
                case "indus": result.TreasuryTenths = result.TreasuryTenths * 110 / 100; break;
            }
            if (!civilization.IsHuman)
            {
                int difficultyRate = civilization.Difficulty == Difficulty.Calm ? 9500 : civilization.Difficulty == Difficulty.Strong ? 11250 : civilization.Difficulty == Difficulty.Trial ? 13000 : 10000;
                result.FoodTenths = result.FoodTenths * difficultyRate / 10000;
                result.Production = result.Production * difficultyRate / 10000;
                result.KnowledgeTenths = result.KnowledgeTenths * difficultyRate / 10000;
                result.CulturePrestige = result.CulturePrestige * difficultyRate / 10000;
                if (result.TreasuryTenths > 0) result.TreasuryTenths = result.TreasuryTenths * difficultyRate / 10000;
            }
            result.TreasuryTenths += CityUpkeep(state, city) * -1;

            int warPenalty = city.WarDamageTurns > 0 || city.SiegeTurns > 0 ? 1500 : 0;
            if (warPenalty > 0)
            {
                result.FoodTenths = result.FoodTenths * (10000 - warPenalty) / 10000;
                result.Production = result.Production * (10000 - warPenalty) / 10000;
                result.KnowledgeTenths = result.KnowledgeTenths * (10000 - warPenalty) / 10000;
                result.TreasuryTenths = result.TreasuryTenths * (10000 - warPenalty) / 10000;
            }
            if (city.OccupiedById >= 0)
            {
                int occupation = city.IntegrationTurns <= 0 ? 5000 : city.IntegrationTurns >= 5 ? 8500 : 7000;
                result.FoodTenths = result.FoodTenths * occupation / 10000;
                result.Production = result.Production * occupation / 10000;
                result.KnowledgeTenths = result.KnowledgeTenths * occupation / 10000;
                result.TreasuryTenths = result.TreasuryTenths * occupation / 10000;
            }
            else if (city.IntegrationTurns > 0)
            {
                result.FoodTenths = result.FoodTenths * 7000 / 10000;
                result.Production = result.Production * 7000 / 10000;
                result.KnowledgeTenths = result.KnowledgeTenths * 7000 / 10000;
                result.TreasuryTenths = result.TreasuryTenths * 7000 / 10000;
            }
            return result;
        }

        public static bool HasNearbyResource(GameState state, CityState city, ResourceKind resource, int radius = 2)
        {
            if (state == null || city == null) return false;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell cell = state.Cells[i];
                if (cell.OwnerId == city.OwnerId && cell.Resource == resource &&
                    HexGrid.Distance(cell.Q, cell.R, city.Q, city.R) <= radius) return true;
            }
            return false;
        }

        public static int WorkerCapacity(GameState state, CityState city)
        {
            if (state == null || city == null) return 0;
            return FlexibleCityWorkplaces + CountWorkableCells(state, city) + city.BuildingLevel("residential_quarter");
        }

        public static int WorkerRoleCapacity(GameState state, CityState city, WorkerRole role)
        {
            if (state == null || city == null) return 0;
            int capacity = role == WorkerRole.Farmer ? FlexibleCityWorkplaces : 1;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell cell = state.Cells[i];
                if (!IsCellWithinWorkArea(state, city, cell)) continue;
                if (role == WorkerRole.Farmer && IsFarmerWorkCell(cell)) capacity++;
            }
            for (int i = 0; i < RulesCatalog.Buildings.Length; i++)
            {
                BuildingDefinition building = RulesCatalog.Buildings[i];
                if (building.WorkerPlaceRole == (int)role)
                    capacity += city.BuildingLevel(building.Id) * Math.Max(0, building.WorkerPlaces);
            }
            if (role == WorkerRole.Merchant) capacity += Math.Max(0, CountTradeRoutes(state, city.OwnerId));
            return capacity;
        }

        public static int AssignedWorkers(CityState city)
        {
            if (city == null || city.Workers == null) return 0;
            int assigned = 0;
            for (int i = 0; i < city.Workers.Length; i++) assigned += Math.Max(0, city.Workers[i]);
            return assigned;
        }

        public static void NormalizeWorkerAssignments(GameState state, CityState city)
        {
            if (state == null || city == null) return;
            if (city.Workers == null) city.Workers = new int[6];
            else if (city.Workers.Length != 6) Array.Resize(ref city.Workers, 6);

            for (int role = 0; role < city.Workers.Length; role++)
            {
                city.Workers[role] = Math.Max(0, Math.Min(city.Workers[role], WorkerRoleCapacity(state, city, (WorkerRole)role)));
            }

            int limit = Math.Min(city.WorkerCount, WorkerCapacity(state, city));
            int remove = Math.Max(0, AssignedWorkers(city) - limit);
            // Keep food and production labor first when population or a district is lost.
            for (int role = city.Workers.Length - 1; role >= 0 && remove > 0; role--)
            {
                int amount = Math.Min(remove, city.Workers[role]);
                city.Workers[role] -= amount;
                remove -= amount;
            }
        }

        private static int CountWorkableCells(GameState state, CityState city)
        {
            int count = 0;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell cell = state.Cells[i];
                if (cell == null || cell.OwnerId != city.OwnerId ||
                    cell.Terrain == TerrainKind.Mountain || cell.Terrain == TerrainKind.Water) continue;
                int distance = HexGrid.Distance(cell.Q, cell.R, city.Q, city.R);
                if (distance >= 1 && distance <= 4) count++;
            }
            return count;
        }

        private static bool IsCellWithinWorkArea(GameState state, CityState city, HexCell cell)
        {
            if (state == null || city == null || cell == null || cell.OwnerId != city.OwnerId ||
                cell.Terrain == TerrainKind.Mountain || cell.Terrain == TerrainKind.Water) return false;
            int distance = HexGrid.Distance(cell.Q, cell.R, city.Q, city.R);
            return distance >= 1 && distance <= 2;
        }

        private static bool IsFarmerWorkCell(HexCell cell)
        {
            return cell.Terrain == TerrainKind.Meadow || cell.Terrain == TerrainKind.Plains ||
                   cell.Terrain == TerrainKind.Arid || cell.Terrain == TerrainKind.River ||
                   cell.Terrain == TerrainKind.Coast;
        }

        public static int CityUpkeep(GameState state, CityState city)
        {
            int upkeep = 0;
            for (int i = 0; i < RulesCatalog.Buildings.Length; i++)
            {
                BuildingDefinition definition = RulesCatalog.Buildings[i];
                upkeep += city.BuildingLevel(definition.Id) * definition.UpkeepTenths;
            }
            CivilizationState owner = state.Civilization(city.OwnerId);
            if (owner != null)
            {
                for (int i = 0; i < owner.Laws.Length; i++)
                    for (int j = 0; j < RulesCatalog.Laws.Length; j++)
                        if (RulesCatalog.Laws[j].Id == owner.Laws[i]) upkeep += RulesCatalog.Laws[j].UpkeepPerCityTenths;
            }
            for (int i = 0; i < city.ServiceFunding.Length; i++)
            {
                int funding = city.ServiceFunding[i];
                if (funding == 2) upkeep += 30;
                else if (funding == 0) upkeep += 0;
                else upkeep += 10;
            }
            CivilizationState civ = owner;
            if (civ != null && civ.HasLaw("city_autonomy") && city.AdministrativeLoad <= 0) upkeep += 20;
            int cityCount = 0;
            for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].OwnerId == city.OwnerId) cityCount++;
            if (civ != null && cityCount > civ.AdministrativeCapacity) upkeep += (cityCount - civ.AdministrativeCapacity) * 20;
            bool atWar = false;
            for (int i = 0; i < state.Conflicts.Length; i++)
                if (state.Conflicts[i].Active && state.Conflicts[i].Phase == 2 &&
                    (state.Conflicts[i].AggressorId == city.OwnerId || state.Conflicts[i].DefenderId == city.OwnerId)) atWar = true;
            for (int i = 0; i < state.Units.Length; i++)
            {
                if (state.Units[i].OwnerId == city.OwnerId && state.Units[i].CitySourceId == city.Id)
                {
                    UnitDefinition unit = RulesCatalog.Unit(state.Units[i].Kind);
                    if (unit != null) upkeep += civ != null && civ.HasLaw("volunteer_service") && !atWar ? unit.UpkeepTenths / 2 : unit.UpkeepTenths;
                }
            }
            return upkeep;
        }

        public static bool AutoAssign(GameState state, CityState city, string focus)
        {
            int workersAvailable = city.WorkerCount;
            var next = new int[6];
            int[] roleSlots = new int[6];
            for (int role = 0; role < roleSlots.Length; role++)
                roleSlots[role] = WorkerRoleCapacity(state, city, (WorkerRole)role);
            int available = Math.Min(workersAvailable, WorkerCapacity(state, city));

            int farmers = Math.Min(Math.Min(3, roleSlots[(int)WorkerRole.Farmer]), available);
            next[(int)WorkerRole.Farmer] = farmers;
            available -= farmers;
            if (focus == "trade" && available > 0)
            {
                int merchants = Math.Min(roleSlots[(int)WorkerRole.Merchant], available);
                next[(int)WorkerRole.Merchant] += merchants;
                available -= merchants;
            }
            if (focus == "food" && available > 0)
            {
                int extra = Math.Min(roleSlots[(int)WorkerRole.Farmer] - farmers, available);
                next[(int)WorkerRole.Farmer] += extra;
                available -= extra;
            }
            if (focus == "industry" && available > 0)
            {
                int extra = Math.Min(roleSlots[(int)WorkerRole.Artisan], Math.Min(2, available));
                next[(int)WorkerRole.Artisan] += extra;
                available -= extra;
            }
            if (focus == "science")
            {
                AssignPreferred(next, WorkerRole.Scholar, roleSlots, ref available, 2);
                AssignPreferred(next, WorkerRole.Artisan, roleSlots, ref available, 1);
            }
            else if (focus == "culture")
            {
                AssignPreferred(next, WorkerRole.Artist, roleSlots, ref available, 2);
                AssignPreferred(next, WorkerRole.Artisan, roleSlots, ref available, 1);
            }
            else
            {
                AssignPreferred(next, WorkerRole.Artisan, roleSlots, ref available, 1);
                AssignPreferred(next, WorkerRole.Scholar, roleSlots, ref available, 1);
            }
            AssignPreferred(next, WorkerRole.Merchant, roleSlots, ref available, focus == "trade" ? 2 : focus == "culture" ? 0 : 1);
            AssignPreferred(next, WorkerRole.Artist, roleSlots, ref available, focus == "culture" ? 2 : 0);
            AssignPreferred(next, WorkerRole.CivilServant, roleSlots, ref available, 0);
            if (available > 0)
            {
                int role = focus == "food" ? (int)WorkerRole.Farmer : focus == "industry" ? (int)WorkerRole.Artisan : focus == "science" ? (int)WorkerRole.Scholar : focus == "trade" ? (int)WorkerRole.Merchant : (int)WorkerRole.Artist;
                int spare = Math.Max(0, roleSlots[role] - next[role]);
                int added = Math.Min(spare, available);
                next[role] += added;
            }
            Array.Copy(next, city.Workers, Math.Min(next.Length, city.Workers.Length));
            return true;
        }

        private static void AssignPreferred(int[] workers, WorkerRole role, int[] slots, ref int remaining, int preferred)
        {
            int index = (int)role;
            int amount = Math.Min(Math.Min(preferred, Math.Max(0, slots[index] - workers[index])), remaining);
            workers[index] += amount;
            remaining -= amount;
        }

        private static int CountTradeRoutes(GameState state, int ownerId)
        {
            int count = 0;
            for (int i = 0; i < state.Treaties.Length; i++)
                if (state.Treaties[i].Active && state.Treaties[i].Kind == TreatyKind.TradeRoute &&
                    (state.Treaties[i].FromCivilizationId == ownerId || state.Treaties[i].ToCivilizationId == ownerId)) count++;
            return count;
        }

        private static int At(int[] values, WorkerRole role)
        {
            int index = (int)role;
            return index >= 0 && index < values.Length ? values[index] : 0;
        }

        private static int BestWorkerCellBonus(int[] modifiers, int modifierCount, int workerCount)
        {
            int total = 0;
            int count = Math.Min(Math.Max(0, workerCount), modifierCount);
            for (int worker = 0; worker < count; worker++)
            {
                int bestIndex = -1;
                int best = int.MinValue;
                for (int i = 0; i < modifierCount; i++)
                    if (modifiers[i] > best) { best = modifiers[i]; bestIndex = i; }
                if (bestIndex < 0) break;
                total += best;
                modifiers[bestIndex] = int.MinValue;
            }
            return total;
        }

        private static int Scale(int value, int basisPoints)
        {
            return value * basisPoints / 10000;
        }
    }
}
