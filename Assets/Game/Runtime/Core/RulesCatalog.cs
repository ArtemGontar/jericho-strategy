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
        public string RequiredNationId;
        public string ArtKey;
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
        public int FlatFoodTenths;
        public int FlatKnowledgeTenths;
        public int FlatTreasuryTenths;
        public int FlatCulturePrestige;
        public int HealthPerTurn;
        public int EducationPerTurn;
        public int SafetyPerTurn;
        public int ProductionPerArtisan;
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
        public UnitClass Class;
        public Era Era;
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
        public int AttackBonusPercent;
        public int DefenseBonusPercent;
        public bool LegacyOnly;
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
        private static readonly string[] SaveV10TechnologyOrder =
        {
            "agriculture", "pottery", "stone", "writing", "coastal_craft",
            "irrigation", "roads", "coin_markets", "medicine", "civic_schools",
            "engineering", "trade_league", "printing", "public_health", "university",
            "mechanization", "electricity", "railways", "mass_education", "free_press",
            "clean_energy", "computing_networks", "global_health", "open_science", "international_assembly"
        };

        public static readonly TechnologyDefinition[] Technologies =
        {
            Tech("agriculture", "Земледелие", "Agriculture", Era.Communities, 8, null, "farms, stable growth"),
            Tech("pottery", "Гончарное дело", "Pottery", Era.Communities, 8, null, "granary"),
            Tech("stone", "Каменное строительство", "Stonework", Era.Communities, 8, null, "stone buildings, basic fortifications"),
            Tech("writing", "Письменность", "Writing", Era.Communities, 8, null, "council, treaties, saved knowledge"),
            Tech("coastal_craft", "Прибрежное ремесло", "Coastal Craft", Era.Communities, 8, new[] { "agriculture|pottery" }, "fishery, coastal trade"),
            Tech("slingcraft", "Искусство пращи", "Slingcraft", Era.Communities, 8, new[] { "agriculture" }, "slingers and ranged units"),
            Tech("animal_husbandry", "Скотоводство", "Animal Husbandry", Era.Communities, 8, new[] { "agriculture" }, "chariots and mounted units"),
            Tech("fieldcraft", "Знание местности", "Fieldcraft", Era.Communities, 8, new[] { "writing" }, "scouts and reconnaissance"),
            Tech("irrigation", "Ирригация", "Irrigation", Era.Cities, 12, new[] { "agriculture" }, "arid land, aqueduct"),
            Tech("roads", "Дороги", "Roads", Era.Cities, 12, new[] { "stone" }, "roads and supply network"),
            Tech("coin_markets", "Монета и рынки", "Coin and Markets", Era.Cities, 12, new[] { "writing" }, "merchants, markets, resource trade"),
            Tech("medicine", "Медицина", "Medicine", Era.Cities, 12, new[] { "agriculture", "writing" }, "clinic, epidemic resilience"),
            Tech("civic_schools", "Гражданские школы", "Civic Schools", Era.Cities, 12, new[] { "writing" }, "schools, education"),
            Tech("military_drill", "Военный строй", "Military Drill", Era.Cities, 12, new[] { "stone", "writing" }, "professional infantry formations"),
            Tech("composite_bow", "Составной лук", "Composite Bow", Era.Cities, 12, new[] { "slingcraft" }, "composite bows and ranged units"),
            Tech("horse_breeding", "Коневодство", "Horse Breeding", Era.Cities, 12, new[] { "animal_husbandry" }, "mounted cavalry"),
            Tech("torsion_siege", "Торсионные машины", "Torsion Engines", Era.Cities, 12, new[] { "roads", "stone" }, "catapults and siege engines"),
            Tech("surveying", "Землемерие", "Surveying", Era.Cities, 12, new[] { "roads" }, "scout routes and border maps"),
            Tech("engineering", "Инженерия", "Engineering", Era.ConnectedLands, 18, new[] { "stone", "roads" }, "bridges, improved fortification"),
            Tech("trade_league", "Торговая лига", "Trade League", Era.ConnectedLands, 18, new[] { "coin_markets" }, "international routes, joint projects"),
            Tech("printing", "Печать", "Printing", Era.ConnectedLands, 18, new[] { "civic_schools" }, "cultural influence"),
            Tech("public_health", "Общественное здоровье", "Public Health", Era.ConnectedLands, 18, new[] { "medicine", "irrigation" }, "hospital, sanitation"),
            Tech("university", "Университет", "University", Era.ConnectedLands, 18, new[] { "civic_schools" }, "university, science project"),
            Tech("steelworking", "Обработка стали", "Steelworking", Era.ConnectedLands, 18, new[] { "engineering" }, "armored infantry"),
            Tech("crossbow", "Арбалет", "Crossbow", Era.ConnectedLands, 18, new[] { "composite_bow" }, "crossbow formations"),
            Tech("stirrups", "Стремена", "Stirrups", Era.ConnectedLands, 18, new[] { "horse_breeding", "engineering" }, "heavy cavalry"),
            Tech("counterweight_siege", "Противовесные машины", "Counterweight Siege", Era.ConnectedLands, 18, new[] { "engineering" }, "trebuchets and siege crews"),
            Tech("cartography", "Картография", "Cartography", Era.ConnectedLands, 18, new[] { "trade_league" }, "long-range reconnaissance"),
            Tech("mechanization", "Механизация", "Mechanization", Era.Industry, 25, new[] { "engineering" }, "factory, production"),
            Tech("electricity", "Электричество", "Electricity", Era.Industry, 25, new[] { "mechanization" }, "power grid"),
            Tech("railways", "Железные дороги", "Railways", Era.Industry, 25, new[] { "roads", "mechanization" }, "long distance trade and supply"),
            Tech("mass_education", "Массовое образование", "Mass Education", Era.Industry, 25, new[] { "university" }, "education growth"),
            Tech("free_press", "Свободная пресса", "Free Press", Era.Industry, 25, new[] { "printing" }, "cultural exchange"),
            Tech("firearms", "Огнестрельное оружие", "Firearms", Era.Industry, 25, new[] { "mechanization" }, "industrial line infantry"),
            Tech("rifling", "Нарезное оружие", "Rifling", Era.Industry, 25, new[] { "firearms" }, "rifle skirmishers"),
            Tech("motorization", "Моторизация", "Motorization", Era.Industry, 25, new[] { "railways" }, "mobile cavalry and vehicles"),
            Tech("field_artillery", "Полевая артиллерия", "Field Artillery", Era.Industry, 25, new[] { "mechanization" }, "modern artillery"),
            Tech("reconnaissance", "Военная разведка", "Military Intelligence", Era.Industry, 25, new[] { "mass_education" }, "reconnaissance units"),
            Tech("clean_energy", "Чистая энергия", "Clean Energy", Era.Synthesis, 34, new[] { "electricity" }, "late production without pollution"),
            Tech("computing_networks", "Вычислительные сети", "Computing Networks", Era.Synthesis, 42, new[] { "electricity", "university" }, "final science project", true),
            Tech("global_health", "Глобальное здоровье", "Global Health", Era.Synthesis, 34, new[] { "public_health", "computing_networks" }, "health cooperation"),
            Tech("open_science", "Открытая наука", "Open Science", Era.Synthesis, 42, new[] { "mass_education", "computing_networks" }, "final science project", true),
            Tech("international_assembly", "Международная ассамблея", "International Assembly", Era.Synthesis, 42, new[] { "trade_league", "free_press" }, "diplomatic victory", true),
            Tech("networked_infantry", "Сетевая пехота", "Networked Infantry", Era.Synthesis, 34, new[] { "computing_networks" }, "networked infantry"),
            Tech("precision_systems", "Высокоточные системы", "Precision Systems", Era.Synthesis, 34, new[] { "computing_networks" }, "precision ranged units"),
            Tech("armored_mobility", "Бронированная мобильность", "Armored Mobility", Era.Synthesis, 34, new[] { "clean_energy" }, "armored cavalry"),
            Tech("smart_artillery", "Умная артиллерия", "Smart Artillery", Era.Synthesis, 42, new[] { "precision_systems" }, "precision siege units"),
            Tech("autonomous_recon", "Автономная разведка", "Autonomous Reconnaissance", Era.Synthesis, 34, new[] { "computing_networks" }, "autonomous scouts")
        };

        public static readonly BuildingDefinition[] Buildings =
        {
            Building("granary", "Амбар", "Granary", 10, 10, "pottery", foodStorageTurns: 2),
            Building("house", "Дом", "House", 8, 5, null, housingTenths: 40),
            Building("residential_quarter", "Жилой квартал", "Residential Quarter", 18, 20, "engineering", housingTenths: 80),
            Building("workshop", "Мастерская", "Workshop", 12, 10, "stone", workerPlaceRole: (int)WorkerRole.Artisan, workerPlaces: 1),
            Building("factory", "Фабрика", "Factory", 32, 40, "mechanization", workerPlaceRole: (int)WorkerRole.Artisan,
                workerPlaces: 3, productionPerArtisan: 1, artKey: "Building_Factory"),
            Building("school", "Школа", "School", 12, 10, "civic_schools", knowledgePerScholar: 2, education: 5, workerPlaceRole: (int)WorkerRole.Scholar, workerPlaces: 1),
            Building("market", "Рынок", "Market", 12, 10, "coin_markets", treasuryPerMerchant: 2, workerPlaceRole: (int)WorkerRole.Merchant, workerPlaces: 1, tradeSlots: 1),
            Building("clinic", "Клиника", "Clinic", 14, 20, "medicine", health: 10, workerPlaceRole: (int)WorkerRole.CivilServant, workerPlaces: 1),
            Building("cultural_house", "Культурный дом", "Cultural House", 12, 10, "writing", culturePerArtist: 3, workerPlaceRole: (int)WorkerRole.Artist, workerPlaces: 1, culturalObject: true),
            Building("council", "Совет", "Council", 10, 10, "writing", adminCapacity: 1, unique: true),
            Building("administration", "Администрация", "Administration", 16, 20, "engineering", adminCapacity: 2, unique: true),
            Building("road_services", "Дорожная служба", "Road Services", 14, 20, "roads", safety: 5,
                workerPlaceRole: (int)WorkerRole.CivilServant, workerPlaces: 1),
            Building("university", "Университет", "University", 20, 30, "university", knowledgePerScholar: 4, education: 10, workerPlaceRole: (int)WorkerRole.Scholar, workerPlaces: 2),
            Building("hospital", "Больница", "Hospital", 20, 30, "public_health", health: 15, workerPlaceRole: (int)WorkerRole.CivilServant, workerPlaces: 1),
            Building("archive", "Архив и музей", "Archive and Museum", 18, 20, "printing", culturePerArtist: 5,
                workerPlaceRole: (int)WorkerRole.Artist, workerPlaces: 1, culturalObject: true),
            Building("exchange_house", "Дом обмена", "House of Exchange", 20, 20, "trade_league", culturePerArtist: 4,
                workerPlaceRole: (int)WorkerRole.Artist, workerPlaces: 1, culturalObject: true),
            Building("academy", "Академия", "Academy", 16, 30, "computing_networks", knowledgePerScholar: 5, education: 15, workerPlaceRole: (int)WorkerRole.Scholar, workerPlaces: 2),
            Building("barracks", "Казармы", "Barracks", 14, 15, "stone", defense: 10),
            Building("assembly", "Ассамблея", "Assembly", 26, 25, "international_assembly", adminCapacity: 1, unique: true),
            Building("jericho_spring_tower", "Башня источника", "Spring Tower", 12, 5, "stone", unique: true,
                requiredNationId: "jericho", artKey: "UniqueDistricts/District_Jericho_SpringTower", flatFoodTenths: 10, safetyPerTurn: 1),
            Building("sumer_ziggurat_archive", "Зиккуратовый архив", "Ziggurat Archive", 16, 10, "writing", unique: true,
                requiredNationId: "sumer", artKey: "UniqueDistricts/District_Sumer_Ziggurat", flatKnowledgeTenths: 10, flatCulturePrestige: 1),
            Building("egypt_nilometer", "Двор нилометра", "Nilometer Court", 18, 10, "irrigation", unique: true,
                requiredNationId: "egypt", artKey: "UniqueDistricts/District_Egypt_Nilometer", flatFoodTenths: 20),
            Building("indus_great_bath", "Большая купальня", "Great Bath", 20, 10, "public_health", unique: true,
                requiredNationId: "indus", artKey: "UniqueDistricts/District_Indus_GreatBath", healthPerTurn: 2)
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
            Improvement(ImprovementKind.IrrigatedFarm, "Ирригационная ферма", "Irrigated Farm", 10, "irrigation", TerrainKind.Arid),
            Improvement(ImprovementKind.Park, "Парк и заповедник", "Park and Reserve", 7, "writing", TerrainKind.Meadow, TerrainKind.Plains, TerrainKind.Forest),
            Improvement(ImprovementKind.Fortification, "Укрепление", "Fortification", 10, "stone", TerrainKind.Plains, TerrainKind.Meadow, TerrainKind.Hills)
        };

        public static readonly UnitDefinition[] Units =
        {
            MilitaryUnit(UnitKind.Militia, UnitClass.Infantry, Era.Communities, "Ополчение", "Militia", 78, 8, 30, 50, 1, 10, null),
            new UnitDefinition { Kind = UnitKind.FieldForce, Class = UnitClass.Infantry, Era = Era.Communities,
                NameRu = "Полевые силы", NameEn = "Field Force", ProductionCost = 14, TreasuryCostTenths = 60,
                PopulationTenths = 10, Strength = 100, Movement = 2, UpkeepTenths = 20, RequiredTechnology = "roads",
                RequiredBuilding = "barracks", RecruitTurns = 2, LegacyOnly = true },
            MilitaryUnit(UnitKind.SiegeTeam, UnitClass.Siege, Era.Communities, "Осадная команда", "Siege Team", 58, 16, 50, 50, 1, 20, "stone"),
            MilitaryUnit(UnitKind.CommunitiesRanged, UnitClass.Ranged, Era.Communities, "Пращники", "Slingers", 68, 10, 35, 40, 1, 12, "slingcraft"),
            MilitaryUnit(UnitKind.CommunitiesCavalry, UnitClass.Cavalry, Era.Communities, "Колесничие", "Chariots", 92, 18, 70, 80, 3, 28, "animal_husbandry"),
            MilitaryUnit(UnitKind.CommunitiesScout, UnitClass.Scout, Era.Communities, "Следопыты", "Pathfinders", 52, 8, 25, 40, 3, 8, "fieldcraft"),

            MilitaryUnit(UnitKind.CitiesInfantry, UnitClass.Infantry, Era.Cities, "Гоплиты", "Hoplites", 100, 13, 50, 70, 1, 18, "military_drill"),
            MilitaryUnit(UnitKind.CitiesRanged, UnitClass.Ranged, Era.Cities, "Лучники", "Composite Archers", 86, 13, 45, 60, 1, 16, "composite_bow"),
            MilitaryUnit(UnitKind.CitiesCavalry, UnitClass.Cavalry, Era.Cities, "Конница", "Cavalry", 112, 18, 75, 90, 3, 26, "horse_breeding"),
            MilitaryUnit(UnitKind.CitiesSiege, UnitClass.Siege, Era.Cities, "Катапульты", "Catapults", 78, 18, 65, 50, 1, 25, "torsion_siege"),
            MilitaryUnit(UnitKind.CitiesScout, UnitClass.Scout, Era.Cities, "Пограничные разведчики", "Border Scouts", 68, 12, 40, 40, 3, 12, "surveying"),

            MilitaryUnit(UnitKind.ConnectedInfantry, UnitClass.Infantry, Era.ConnectedLands, "Профессиональная пехота", "Professional Infantry", 124, 20, 80, 80, 1, 24, "steelworking"),
            MilitaryUnit(UnitKind.ConnectedRanged, UnitClass.Ranged, Era.ConnectedLands, "Арбалетчики", "Crossbowmen", 108, 20, 70, 60, 1, 21, "crossbow"),
            MilitaryUnit(UnitKind.ConnectedCavalry, UnitClass.Cavalry, Era.ConnectedLands, "Рыцари", "Knights", 142, 25, 100, 100, 3, 34, "stirrups"),
            MilitaryUnit(UnitKind.ConnectedSiege, UnitClass.Siege, Era.ConnectedLands, "Требушеты", "Trebuchets", 98, 24, 90, 60, 1, 32, "counterweight_siege"),
            MilitaryUnit(UnitKind.ConnectedScout, UnitClass.Scout, Era.ConnectedLands, "Дальняя разведка", "Long-Range Scouts", 82, 16, 55, 50, 3, 15, "cartography"),

            MilitaryUnit(UnitKind.IndustryInfantry, UnitClass.Infantry, Era.Industry, "Линейная пехота", "Line Infantry", 150, 28, 110, 80, 1, 34, "firearms"),
            MilitaryUnit(UnitKind.IndustryRanged, UnitClass.Ranged, Era.Industry, "Стрелки", "Rifle Skirmishers", 136, 28, 100, 70, 1, 31, "rifling"),
            MilitaryUnit(UnitKind.IndustryCavalry, UnitClass.Cavalry, Era.Industry, "Драгуны", "Dragoons", 168, 32, 130, 90, 3, 40, "motorization"),
            MilitaryUnit(UnitKind.IndustrySiege, UnitClass.Siege, Era.Industry, "Полевые пушки", "Field Artillery", 116, 32, 125, 70, 1, 42, "field_artillery"),
            MilitaryUnit(UnitKind.IndustryScout, UnitClass.Scout, Era.Industry, "Разведывательные отряды", "Reconnaissance Teams", 104, 24, 85, 50, 3, 24, "reconnaissance"),

            MilitaryUnit(UnitKind.SynthesisInfantry, UnitClass.Infantry, Era.Synthesis, "Сетевая пехота", "Networked Infantry", 178, 38, 150, 80, 1, 42, "networked_infantry"),
            MilitaryUnit(UnitKind.SynthesisRanged, UnitClass.Ranged, Era.Synthesis, "Точные стрелки", "Precision Marksmen", 160, 38, 140, 70, 1, 39, "precision_systems"),
            MilitaryUnit(UnitKind.SynthesisCavalry, UnitClass.Cavalry, Era.Synthesis, "Бронегруппа", "Armored Cavalry", 196, 42, 170, 100, 3, 50, "armored_mobility"),
            MilitaryUnit(UnitKind.SynthesisSiege, UnitClass.Siege, Era.Synthesis, "Самоходная артиллерия", "Self-Propelled Artillery", 142, 42, 160, 70, 2, 52, "smart_artillery"),
            MilitaryUnit(UnitKind.SynthesisScout, UnitClass.Scout, Era.Synthesis, "Дроновая разведка", "Drone Reconnaissance", 122, 32, 120, 50, 4, 30, "autonomous_recon"),

            new UnitDefinition { Kind = UnitKind.SettlerWagon, Class = UnitClass.Civilian, Era = Era.Communities,
                NameRu = "Поселенец", NameEn = "Settler", ProductionCost = 4, TreasuryCostTenths = 0,
                PopulationTenths = 20, Strength = 0, Movement = 2, UpkeepTenths = 0, RecruitTurns = 1 }
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
            Event(EventKind.SharedResearch, "Общее исследование", "Shared Research", "Научный обмен минимум двух сторон.", "JointProject", "NationalProject", "ShareKnowledge"),
            Event(EventKind.SettlerInterception, "Перехват гражданского обоза", "Civilian Convoy Intercepted", "Военный отряд угрожает обозу. Выберите путь для жителей: безопасное отступление, интернирование на один ход или рискованный прорыв с потерями.", "Retreat", "Internment", "ContinueJourney"),
            Event(EventKind.NeutralMediation, "Предложение нейтрального посредника", "Neutral Mediation Proposal", "После восьми ходов без контроля над городом нейтральная сторона предлагает переговоры. Перемирие остановит боевые потери; отказ сохранит войну, но повысит напряжение.", "AcceptMediation", "RejectMediation")
        };

        private static TechnologyDefinition Tech(string id, string ru, string en, Era era, int cost, string[] prerequisites, string unlock, bool key = false)
        {
            return new TechnologyDefinition { Id = id, NameRu = ru, NameEn = en, Era = era, Cost = cost, Prerequisites = prerequisites ?? Array.Empty<string>(), Unlock = unlock, KeyTechnology = key };
        }

        private static BuildingDefinition Building(string id, string ru, string en, int cost, int upkeep, string tech,
            int housingTenths = 0, int foodStorageTurns = 0, int knowledgePerScholar = 0, int treasuryPerMerchant = 0,
            int culturePerArtist = 0, int health = 0, int education = 0, int safety = 0, int adminCapacity = 0,
            int workerPlaceRole = -1, int workerPlaces = 0, int tradeSlots = 0, int defense = 0,
            bool unique = false, bool culturalObject = false, string requiredNationId = null, string artKey = null,
            int flatFoodTenths = 0, int flatKnowledgeTenths = 0, int flatTreasuryTenths = 0,
            int flatCulturePrestige = 0, int healthPerTurn = 0, int educationPerTurn = 0, int safetyPerTurn = 0,
            int productionPerArtisan = 0)
        {
            return new BuildingDefinition
            {
                Id = id, NameRu = ru, NameEn = en, ProductionCost = cost, UpkeepTenths = upkeep,
                RequiredTechnology = tech, RequiredNationId = requiredNationId, ArtKey = artKey,
                HousingTenths = housingTenths, FoodStorageTurns = foodStorageTurns,
                KnowledgePerScholar = knowledgePerScholar, TreasuryPerMerchant = treasuryPerMerchant,
                CulturePerArtist = culturePerArtist, Health = health, Education = education, Safety = safety,
                AdminCapacity = adminCapacity, WorkerPlaceRole = workerPlaceRole, WorkerPlaces = workerPlaces,
                TradeSlots = tradeSlots, Defense = defense, FlatFoodTenths = flatFoodTenths,
                FlatKnowledgeTenths = flatKnowledgeTenths, FlatTreasuryTenths = flatTreasuryTenths,
                FlatCulturePrestige = flatCulturePrestige, HealthPerTurn = healthPerTurn,
                EducationPerTurn = educationPerTurn, SafetyPerTurn = safetyPerTurn,
                ProductionPerArtisan = productionPerArtisan,
                UniqueEmpireBuilding = unique, CulturalObject = culturalObject
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

        private static UnitDefinition MilitaryUnit(UnitKind kind, UnitClass unitClass, Era era, string ru, string en,
            int strength, int production, int treasury, int population, int movement, int upkeep, string technology)
        {
            int attackBonus = unitClass == UnitClass.Ranged ? 18 : unitClass == UnitClass.Cavalry ? 15 :
                unitClass == UnitClass.Siege ? 25 : unitClass == UnitClass.Scout ? 8 : 0;
            int defenseBonus = unitClass == UnitClass.Infantry ? 15 : unitClass == UnitClass.Ranged ? -5 :
                unitClass == UnitClass.Cavalry ? -10 : unitClass == UnitClass.Siege ? -20 : -10;
            return new UnitDefinition
            {
                Kind = kind, Class = unitClass, Era = era, NameRu = ru, NameEn = en,
                ProductionCost = production, TreasuryCostTenths = treasury, PopulationTenths = population,
                Strength = strength, Movement = movement, UpkeepTenths = upkeep,
                RequiredTechnology = technology, RequiredBuilding = unitClass == UnitClass.Infantry && era == Era.Communities ? null : "barracks",
                RecruitTurns = unitClass == UnitClass.Siege || unitClass == UnitClass.Cavalry ? 3 : 2,
                AttackBonusPercent = attackBonus, DefenseBonusPercent = defenseBonus
            };
        }

        public static int TechnologyIndex(string id)
        {
            for (int i = 0; i < Technologies.Length; i++) if (Technologies[i].Id == id) return i;
            return -1;
        }

        public static void MigrateTechnologyProgressFromSaveV10(CivilizationState civilization)
        {
            if (civilization == null) return;
            int[] previousProgress = civilization.TechnologyProgressTenths ?? Array.Empty<int>();
            int previousResearchIndex = civilization.ActiveResearchIndex;
            string activeTechnologyId = previousResearchIndex >= 0 && previousResearchIndex < SaveV10TechnologyOrder.Length
                ? SaveV10TechnologyOrder[previousResearchIndex] : null;
            int[] migratedProgress = new int[Technologies.Length];
            for (int oldIndex = 0; oldIndex < previousProgress.Length && oldIndex < SaveV10TechnologyOrder.Length; oldIndex++)
            {
                int currentIndex = TechnologyIndex(SaveV10TechnologyOrder[oldIndex]);
                if (currentIndex >= 0) migratedProgress[currentIndex] = previousProgress[oldIndex];
            }
            civilization.TechnologyProgressTenths = migratedProgress;
            civilization.ActiveResearchIndex = activeTechnologyId == null ? -1 : TechnologyIndex(activeTechnologyId);
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
