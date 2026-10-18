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
        public int OccupiedWorkers;
        public int UnemployedWorkers;
        public int[] RoleOutputTenths = new int[6];
    }

    public static class EconomyCalculator
    {
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

            int farmTileBonus = 0;
            int marketTileBonus = 0;
            int forestBonus = 0;
            int resourceFoodBonus = 0;
            int resourceIndustryBonusPercent = 0;
            int serviceBonus = 0;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell cell = state.Cells[i];
                if (cell.OwnerId != city.OwnerId || HexGrid.Distance(cell.Q, cell.R, city.Q, city.R) > 2) continue;
                if (cell.Damage < 75 && (cell.Improvement == ImprovementKind.Farm || cell.Improvement == ImprovementKind.IrrigatedFarm) && cell.ImprovementLevel > 0) farmTileBonus += 10 * cell.ImprovementLevel;
                if (cell.Damage < 75 && cell.Improvement == ImprovementKind.Market) marketTileBonus += 10;
                if (cell.Damage < 75 && cell.Improvement == ImprovementKind.Forestry) forestBonus += 1;
                if (cell.Damage < 75 && cell.Resource == ResourceKind.FertileSoil && (cell.Improvement == ImprovementKind.Farm || cell.Improvement == ImprovementKind.IrrigatedFarm)) resourceFoodBonus += 10;
                if (cell.Damage < 75 && cell.Resource == ResourceKind.Iron && cell.Improvement != ImprovementKind.None) resourceIndustryBonusPercent += 10;
                if (cell.Resource == ResourceKind.Herbs) serviceBonus += 5;
            }
            result.FoodTenths += Scale(farmTileBonus, efficiency);
            result.FoodTenths += Scale(resourceFoodBonus, efficiency);
            result.Production += forestBonus;
            result.Production = result.Production * (100 + resourceIndustryBonusPercent) / 100;
            result.ServiceCoverage += serviceBonus;

            int schoolLevel = city.BuildingLevel("school");
            int universityLevel = city.BuildingLevel("university");
            int academyLevel = city.BuildingLevel("academy");
            int marketLevel = city.BuildingLevel("market");
            int workshopLevel = city.BuildingLevel("workshop");
            int houseLevel = city.BuildingLevel("house") + city.BuildingLevel("residential_quarter");
            int culturalHouse = city.BuildingLevel("cultural_house");
            int archive = city.BuildingLevel("archive");
            int exchangeHouse = city.BuildingLevel("exchange_house");
            int hospital = city.BuildingLevel("hospital");
            int clinic = city.BuildingLevel("clinic");

            result.Production += artisanCount > 0 ? workshopLevel * 2 : 0;
            result.KnowledgeTenths += scholarCount > 0 ? schoolLevel * 20 + universityLevel * 40 + academyLevel * 50 : 0;
            result.TreasuryTenths += merchantCount > 0 ? marketLevel * 20 + marketTileBonus : 0;
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

            if (civilization.Knows("irrigation"))
            {
                for (int i = 0; i < state.Cells.Length; i++)
                {
                    HexCell cell = state.Cells[i];
                    if (cell.OwnerId == city.OwnerId && cell.Terrain == TerrainKind.Arid && cell.Improvement == ImprovementKind.IrrigatedFarm)
                        result.FoodTenths += 20;
                }
            }
            if (civilization.Knows("mechanization")) result.Production += artisanCount > 0 ? 1 : 0;

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
            return result;
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
            int flexibleSlots = 3;
            int[] roleSlots = new int[6];
            int workCells = 0;
            int farmCells = 0;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell cell = state.Cells[i];
                if (cell.OwnerId != city.OwnerId || HexGrid.Distance(cell.Q, cell.R, city.Q, city.R) == 0) continue;
                if (cell.Terrain == TerrainKind.Mountain || cell.Terrain == TerrainKind.Water) continue;
                workCells++;
                if (cell.Terrain == TerrainKind.Meadow || cell.Terrain == TerrainKind.Plains || cell.Terrain == TerrainKind.Arid || cell.Terrain == TerrainKind.River || cell.Terrain == TerrainKind.Coast) farmCells++;
            }
            roleSlots[(int)WorkerRole.Farmer] = farmCells;
            roleSlots[(int)WorkerRole.Artisan] = 1 + city.BuildingLevel("workshop");
            roleSlots[(int)WorkerRole.Scholar] = 1 + city.BuildingLevel("school") + city.BuildingLevel("university") * 2 + city.BuildingLevel("academy") * 2;
            roleSlots[(int)WorkerRole.Merchant] = 1 + city.BuildingLevel("market") + Math.Max(0, CountTradeRoutes(state, city.OwnerId));
            roleSlots[(int)WorkerRole.Artist] = 1 + city.BuildingLevel("cultural_house") + city.BuildingLevel("archive") + city.BuildingLevel("exchange_house");
            roleSlots[(int)WorkerRole.CivilServant] = 1 + city.BuildingLevel("clinic") + city.BuildingLevel("hospital") + city.BuildingLevel("road_services");
            int totalSlots = flexibleSlots + workCells + city.BuildingLevel("residential_quarter");
            int available = Math.Min(workersAvailable, totalSlots);

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

        private static int Scale(int value, int basisPoints)
        {
            return value * basisPoints / 10000;
        }
    }
}
