using System;
using System.Collections.Generic;

namespace Nasledie.Core
{
    [Serializable]
    public sealed class TechnologyDefinition
    {
        public string Id;
        public string NameRu;
        public string NameEn;
        public Era Era;
        public int Cost;
        public string[] Prerequisites;
        public string Unlock;
        public bool KeyTechnology;
    }

    [Serializable]
    public sealed class BuildingDefinition
    {
        public string Id;
        public string NameRu;
        public string NameEn;
        public int ProductionCost;
        public int UpkeepTenths;
        public string RequiredTechnology;
        public int HousingTenths;
        public int FoodStorageTurns;
        public int KnowledgePerScholar;
        public int TreasuryPerMerchant;
        public int CulturePerArtist;
        public int Health;
        public int Education;
        public int Safety;
        public int AdminCapacity;
        public int WorkerPlaceRole = -1;
        public int WorkerPlaces;
        public int TradeSlots;
        public int Defense;
        public bool UniqueEmpireBuilding;
        public bool CulturalObject;
    }

    [Serializable]
    public sealed class ImprovementDefinition
    {
        public ImprovementKind Kind;
        public string NameRu;
        public string NameEn;
        public int ProductionCost;
        public string RequiredTechnology;
        public TerrainKind[] AllowedTerrain;
    }

    [Serializable]
    public sealed class UnitDefinition
    {
        public UnitKind Kind;
        public string NameRu;
        public string NameEn;
        public int ProductionCost;
        public int TreasuryCostTenths;
        public int PopulationTenths;
        public int Strength;
        public int Movement;
        public int UpkeepTenths;
        public string RequiredTechnology;
        public string RequiredBuilding;
        public int RecruitTurns = 2;
    }

    [Serializable]
    public sealed class LawDefinition
    {
        public string Id;
        public string NameRu;
        public string NameEn;
        public string Category;
        public Era Era;
        public int UpkeepPerCityTenths;
        public string DescriptionRu;
        public string DescriptionEn;
    }

    [Serializable]
    public sealed class EventDefinition
    {
        public EventKind Kind;
        public string NameRu;
        public string NameEn;
        public string TriggerRu;
        public string[] ChoiceIds;
    }

    public static class RulesCatalog
    {
        public static readonly TechnologyDefinition[] Technologies =
        {
            Tech("agriculture", "Земледелие", "Agriculture", Era.Communities, 8, null, "farms, stable growth"),
            Tech("pottery", "Гончарное дело", "Pottery", Era.Communities, 8, null, "granary"),
            Tech("stone", "Каменное строительство", "Stonework", Era.Communities, 8, null, "stone buildings, basic fortifications"),
            Tech("writing", "Письменность", "Writing", Era.Communities, 8, null, "council, treaties, saved knowledge"),
            Tech("coastal_craft", "Прибрежное ремесло", "Coastal Craft", Era.Communities, 8, new[] { "agriculture|pottery" }, "fishery, coastal trade"),
            Tech("irrigation", "Ирригация", "Irrigation", Era.Cities, 12, new[] { "agriculture" }, "arid land, aqueduct"),
            Tech("roads", "Дороги", "Roads", Era.Cities, 12, new[] { "stone" }, "roads and supply network"),
            Tech("coin_markets", "Монета и рынки", "Coin and Markets", Era.Cities, 12, new[] { "writing" }, "merchants, markets, resource trade"),
            Tech("medicine", "Медицина", "Medicine", Era.Cities, 12, new[] { "agriculture", "writing" }, "clinic, epidemic resilience"),
            Tech("civic_schools", "Гражданские школы", "Civic Schools", Era.Cities, 12, new[] { "writing" }, "schools, education"),
            Tech("engineering", "Инженерия", "Engineering", Era.ConnectedLands, 18, new[] { "stone", "roads" }, "bridges, improved fortification"),
            Tech("trade_league", "Торговая лига", "Trade League", Era.ConnectedLands, 18, new[] { "coin_markets" }, "international routes, joint projects"),
            Tech("printing", "Печать", "Printing", Era.ConnectedLands, 18, new[] { "civic_schools" }, "cultural influence"),
            Tech("public_health", "Общественное здоровье", "Public Health", Era.ConnectedLands, 18, new[] { "medicine", "irrigation" }, "hospital, sanitation"),
            Tech("university", "Университет", "University", Era.ConnectedLands, 18, new[] { "civic_schools" }, "university, science project"),
            Tech("mechanization", "Механизация", "Mechanization", Era.Industry, 25, new[] { "engineering" }, "factory, production"),
            Tech("electricity", "Электричество", "Electricity", Era.Industry, 25, new[] { "mechanization" }, "power grid"),
            Tech("railways", "Железные дороги", "Railways", Era.Industry, 25, new[] { "roads", "mechanization" }, "long distance trade and supply"),
            Tech("mass_education", "Массовое образование", "Mass Education", Era.Industry, 25, new[] { "university" }, "education growth"),
            Tech("free_press", "Свободная пресса", "Free Press", Era.Industry, 25, new[] { "printing" }, "cultural exchange"),
            Tech("clean_energy", "Чистая энергия", "Clean Energy", Era.Synthesis, 34, new[] { "electricity" }, "late production without pollution"),
            Tech("computing_networks", "Вычислительные сети", "Computing Networks", Era.Synthesis, 42, new[] { "electricity", "university" }, "final science project", true),
            Tech("global_health", "Глобальное здоровье", "Global Health", Era.Synthesis, 34, new[] { "public_health", "computing_networks" }, "health cooperation"),
            Tech("open_science", "Открытая наука", "Open Science", Era.Synthesis, 42, new[] { "mass_education", "computing_networks" }, "final science project", true),
            Tech("international_assembly", "Международная ассамблея", "International Assembly", Era.Synthesis, 42, new[] { "trade_league", "free_press" }, "diplomatic victory", true)
        };

        public static readonly BuildingDefinition[] Buildings =
        {
            Building("granary", "Амбар", "Granary", 10, 10, "pottery", foodStorageTurns: 2),
            Building("house", "Дом", "House", 8, 5, null, housingTenths: 40),
            Building("residential_quarter", "Жилой квартал", "Residential Quarter", 18, 20, "engineering", housingTenths: 80, workerPlaceRole: (int)WorkerRole.CivilServant, workerPlaces: 1),
            Building("workshop", "Мастерская", "Workshop", 12, 10, "stone", workerPlaceRole: (int)WorkerRole.Artisan, workerPlaces: 1),
            Building("school", "Школа", "School", 12, 10, "civic_schools", knowledgePerScholar: 2, education: 5, workerPlaceRole: (int)WorkerRole.Scholar, workerPlaces: 1),
            Building("market", "Рынок", "Market", 12, 10, "coin_markets", treasuryPerMerchant: 2, workerPlaceRole: (int)WorkerRole.Merchant, workerPlaces: 1, tradeSlots: 1),
            Building("clinic", "Клиника", "Clinic", 14, 20, "medicine", health: 10, workerPlaceRole: (int)WorkerRole.CivilServant, workerPlaces: 1),
            Building("cultural_house", "Культурный дом", "Cultural House", 12, 10, "writing", culturePerArtist: 3, workerPlaceRole: (int)WorkerRole.Artist, workerPlaces: 1, culturalObject: true),
            Building("council", "Совет", "Council", 10, 10, "writing", adminCapacity: 1, workerPlaceRole: (int)WorkerRole.CivilServant, workerPlaces: 1, unique: true),
            Building("administration", "Администрация", "Administration", 16, 20, "engineering", adminCapacity: 2, unique: true),
            Building("road_services", "Дорожная служба", "Road Services", 14, 20, "roads", safety: 5),
            Building("university", "Университет", "University", 20, 30, "university", knowledgePerScholar: 4, education: 10, workerPlaceRole: (int)WorkerRole.Scholar, workerPlaces: 2),
            Building("hospital", "Больница", "Hospital", 20, 30, "public_health", health: 15, workerPlaceRole: (int)WorkerRole.CivilServant, workerPlaces: 2),
            Building("archive", "Архив и музей", "Archive and Museum", 18, 20, "printing", culturePerArtist: 5, culturalObject: true),
            Building("exchange_house", "Дом обмена", "House of Exchange", 20, 20, "trade_league", culturePerArtist: 4, culturalObject: true),
            Building("academy", "Академия", "Academy", 16, 30, "computing_networks", knowledgePerScholar: 5, education: 15, workerPlaceRole: (int)WorkerRole.Scholar, workerPlaces: 2),
            Building("barracks", "Казармы", "Barracks", 14, 15, "stone", defense: 10),
            Building("assembly", "Ассамблея", "Assembly", 26, 25, "international_assembly", adminCapacity: 1, unique: true)
        };

        public static readonly ImprovementDefinition[] Improvements =
        {
            Improvement(ImprovementKind.Farm, "Ферма", "Farm", 6, "agriculture", TerrainKind.Meadow, TerrainKind.Plains, TerrainKind.Arid),
            Improvement(ImprovementKind.Forestry, "Лесное хозяйство", "Forestry", 6, "agriculture", TerrainKind.Forest),
            Improvement(ImprovementKind.Workshop, "Мастерская", "Workshop", 8, "stone", TerrainKind.Hills, TerrainKind.Plains),
            Improvement(ImprovementKind.Road, "Дорога", "Road", 4, "roads", TerrainKind.Plains, TerrainKind.Meadow, TerrainKind.Forest, TerrainKind.Hills, TerrainKind.Arid),
            Improvement(ImprovementKind.Bridge, "Мост", "Bridge", 8, "engineering", TerrainKind.River),
            Improvement(ImprovementKind.Market, "Рынок", "Market", 8, "coin_markets", TerrainKind.Plains, TerrainKind.Meadow),
            Improvement(ImprovementKind.Fishery, "Рыбный причал", "Fishery", 8, "coastal_craft", TerrainKind.Coast),
            Improvement(ImprovementKind.IrrigatedFarm, "Ирригационная ферма", "Irrigated Farm", 10, "irrigation", TerrainKind.Arid)
        };

        public static readonly UnitDefinition[] Units =
        {
            new UnitDefinition { Kind = UnitKind.Militia, NameRu = "Ополчение", NameEn = "Militia", ProductionCost = 8, TreasuryCostTenths = 30, PopulationTenths = 5, Strength = 70, Movement = 1, UpkeepTenths = 10, RecruitTurns = 2 },
            new UnitDefinition { Kind = UnitKind.FieldForce, NameRu = "Полевые силы", NameEn = "Field Force", ProductionCost = 14, TreasuryCostTenths = 60, PopulationTenths = 10, Strength = 100, Movement = 2, UpkeepTenths = 20, RequiredTechnology = "roads", RequiredBuilding = "barracks", RecruitTurns = 2 },
            new UnitDefinition { Kind = UnitKind.SiegeTeam, NameRu = "Осадная команда", NameEn = "Siege Team", ProductionCost = 16, TreasuryCostTenths = 50, PopulationTenths = 5, Strength = 55, Movement = 1, UpkeepTenths = 20, RequiredTechnology = "engineering", RequiredBuilding = "barracks", RecruitTurns = 2 },
            new UnitDefinition { Kind = UnitKind.SettlerWagon, NameRu = "Поселенец", NameEn = "Settler", ProductionCost = 4, TreasuryCostTenths = 0, PopulationTenths = 20, Strength = 0, Movement = 2, UpkeepTenths = 0, RecruitTurns = 1 }
        };

        public static readonly LawDefinition[] Laws =
        {
            Law("open_markets", "Открытые рынки", "Open Markets", "Economy", Era.Cities, 0, "+15% торгового дохода, +5 привлекательности; блокада снижает экспорт.", "+15% trade income, +5 appeal; blockade reduces exports."),
            Law("reserve_market", "Резервный рынок", "Reserve Market", "Economy", Era.Cities, 0, "+1 еда городу со складом, −10% торгового дохода.", "+1 food to cities with storage, −10% trade income."),
            Law("universal_education", "Всеобщее обучение", "Universal Education", "Society", Era.ConnectedLands, 10, "+10% знаний, +5 образования за 3 хода.", "+10% knowledge, +5 education over 3 turns."),
            Law("local_councils", "Местные советы", "Local Councils", "Society", Era.Cities, 0, "+10 доверия вне столицы, +1 административная ёмкость.", "+10 trust outside the capital, +1 admin capacity."),
            Law("centralized_service", "Централизованная служба", "Centralized Service", "Government", Era.Cities, 0, "+2 административной ёмкости; −5 привлекательности автономных городов.", "+2 admin capacity; −5 appeal in autonomous cities."),
            Law("city_autonomy", "Городские автономии", "City Autonomy", "Government", Era.ConnectedLands, 20, "+10 привлекательности, быстрее восстановление; −2 казны на удалённый город.", "+10 appeal, faster recovery; −2 treasury per remote city."),
            Law("volunteer_service", "Добровольная служба", "Volunteer Service", "Defense", Era.Cities, 0, "Дешевле содержание войск в мирное время, медленное пополнение.", "Lower peacetime upkeep, slower recruitment."),
            Law("limited_conscription", "Ограниченный призыв", "Limited Conscription", "Defense", Era.ConnectedLands, 0, "Быстрое пополнение, −10 доверия при мобилизации.", "Faster recruitment, −10 trust during mobilization.")
        };

        public static readonly EventDefinition[] Events =
        {
            Event(EventKind.Drought, "Засуха", "Drought", "Два хода сухой погоды без ирригации.", "BuyFood", "ReassignWorkers", "Rationing"),
            Event(EventKind.Epidemic, "Эпидемия", "Epidemic", "Плотность высока, клиники нет.", "OpenClinic", "Quarantine", "RequestAid"),
            Event(EventKind.RoadCongestion, "Перегруженные дороги", "Road Congestion", "Три маршрута сходятся в городе.", "BuildHub", "RestrictTraffic", "AcceptDelay"),
            Event(EventKind.RefugeeFlow, "Поток беженцев", "Refugee Flow", "Война или осада у соседа.", "Accept", "OpenCorridor", "Refuse"),
            Event(EventKind.ProcurementCorruption, "Коррупция закупок", "Procurement Corruption", "Низкое доверие и высокие налоги.", "Investigate", "Audit", "Ignore"),
            Event(EventKind.Smog, "Промышленный смог", "Industrial Smog", "Высокое производство без чистой энергии.", "Filters", "ReduceOutput", "Ignore"),
            Event(EventKind.EducationReform, "Образовательная реформа", "Education Reform", "Открыты школы, выросли знания.", "FundSchools", "Experiment", "KeepBudget"),
            Event(EventKind.BorderDispute, "Спорная граница", "Border Dispute", "Город или отряд вошёл в спорную зону.", "Negotiate", "ExchangeLand", "Ultimatum"),
            Event(EventKind.TradeShortage, "Торговый дефицит", "Trade Shortage", "Потерян ключевой маршрут.", "EmergencyStock", "NewRoute", "DomesticSupply"),
            Event(EventKind.CulturalExchange, "Культурный обмен", "Cultural Exchange", "Открытые границы и два центра культуры.", "Festival", "JointMuseum", "CloseExchange"),
            Event(EventKind.MobilizationFatigue, "Усталость от мобилизации", "Mobilization Fatigue", "Военная нагрузка достигла 75.", "PaySupport", "LocalTruce", "Continue"),
            Event(EventKind.SharedResearch, "Общее исследование", "Shared Research", "Научный обмен минимум двух сторон.", "JointProject", "NationalProject", "ShareKnowledge")
        };

        private static TechnologyDefinition Tech(string id, string ru, string en, Era era, int cost, string[] prerequisites, string unlock, bool key = false)
        {
            return new TechnologyDefinition { Id = id, NameRu = ru, NameEn = en, Era = era, Cost = cost, Prerequisites = prerequisites ?? Array.Empty<string>(), Unlock = unlock, KeyTechnology = key };
        }

        private static BuildingDefinition Building(string id, string ru, string en, int cost, int upkeep, string tech,
            int housingTenths = 0, int foodStorageTurns = 0, int knowledgePerScholar = 0, int treasuryPerMerchant = 0,
            int culturePerArtist = 0, int health = 0, int education = 0, int safety = 0, int adminCapacity = 0,
            int workerPlaceRole = -1, int workerPlaces = 0, int tradeSlots = 0, int defense = 0,
            bool unique = false, bool culturalObject = false)
        {
            return new BuildingDefinition
            {
                Id = id, NameRu = ru, NameEn = en, ProductionCost = cost, UpkeepTenths = upkeep,
                RequiredTechnology = tech, HousingTenths = housingTenths, FoodStorageTurns = foodStorageTurns,
                KnowledgePerScholar = knowledgePerScholar, TreasuryPerMerchant = treasuryPerMerchant,
                CulturePerArtist = culturePerArtist, Health = health, Education = education, Safety = safety,
                AdminCapacity = adminCapacity, WorkerPlaceRole = workerPlaceRole, WorkerPlaces = workerPlaces,
                TradeSlots = tradeSlots, Defense = defense, UniqueEmpireBuilding = unique, CulturalObject = culturalObject
            };
        }

        private static ImprovementDefinition Improvement(ImprovementKind kind, string ru, string en, int cost, string tech, params TerrainKind[] terrain)
        {
            return new ImprovementDefinition { Kind = kind, NameRu = ru, NameEn = en, ProductionCost = cost, RequiredTechnology = tech, AllowedTerrain = terrain };
        }

        private static LawDefinition Law(string id, string ru, string en, string category, Era era, int upkeep, string descriptionRu, string descriptionEn)
        {
            return new LawDefinition { Id = id, NameRu = ru, NameEn = en, Category = category, Era = era, UpkeepPerCityTenths = upkeep, DescriptionRu = descriptionRu, DescriptionEn = descriptionEn };
        }

        private static EventDefinition Event(EventKind kind, string ru, string en, string trigger, params string[] choices)
        {
            return new EventDefinition { Kind = kind, NameRu = ru, NameEn = en, TriggerRu = trigger, ChoiceIds = choices };
        }

        public static TechnologyDefinition Technology(string id)
        {
            for (int i = 0; i < Technologies.Length; i++) if (Technologies[i].Id == id) return Technologies[i];
            return null;
        }

        public static BuildingDefinition Building(string id)
        {
            for (int i = 0; i < Buildings.Length; i++) if (Buildings[i].Id == id) return Buildings[i];
            return null;
        }

        public static UnitDefinition Unit(UnitKind kind)
        {
            for (int i = 0; i < Units.Length; i++) if (Units[i].Kind == kind) return Units[i];
            return null;
        }

        public static int TechnologyIndex(string id)
        {
            for (int i = 0; i < Technologies.Length; i++) if (Technologies[i].Id == id) return i;
            return -1;
        }

        public static bool IsUnlockKnown(CivilizationState civilization, string unlock)
        {
            for (int i = 0; i < civilization.KnownTechnologies.Length; i++)
            {
                TechnologyDefinition tech = Technology(civilization.KnownTechnologies[i]);
                if (tech != null && tech.Unlock.IndexOf(unlock, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}
