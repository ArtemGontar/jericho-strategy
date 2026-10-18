using System;

namespace Nasledie.Core
{
    [Serializable]
    public sealed class NationDefinition
    {
        public readonly string Id;
        public readonly string NameRu;
        public readonly string Archetype;
        public readonly string StrategicGoal;
        public readonly string CapitalName;
        public readonly string[] AdditionalCityNames;
        public readonly string ArtKey;
        public readonly string LeaderNameRu;
        public readonly string LeaderTitleRu;
        public readonly string LeaderBiographyRu;
        public readonly string LeaderHistoricalNoteRu;
        public readonly string LeaderArtKey;
        public readonly string CampusNameRu;
        public readonly string CampusDescriptionRu;
        public readonly string CampusBonusDescriptionRu;
        public readonly string CampusArtKey;
        public readonly int CampusKnowledgeTenthsPerLevel;
        public readonly int CampusTreasuryTenthsPerLevel;
        public readonly int CampusCulturePrestigePerLevel;
        public readonly int CampusEducationPerLevel;
        public readonly string UniqueDistrictBuildingId;
        public readonly string UniqueDistrictArtKey;
        public readonly string BonusTitle;
        public readonly string BonusDescription;
        public readonly string IdentityDescription;

        public NationDefinition(string id, string nameRu, string archetype, string strategicGoal, string capitalName,
            string[] additionalCityNames, string artKey, string leaderNameRu, string leaderTitleRu, string leaderBiographyRu,
            string leaderHistoricalNoteRu, string leaderArtKey, string campusNameRu, string campusDescriptionRu,
            string campusBonusDescriptionRu, string campusArtKey, int campusKnowledgeTenthsPerLevel,
            int campusTreasuryTenthsPerLevel, int campusCulturePrestigePerLevel, int campusEducationPerLevel,
            string uniqueDistrictBuildingId, string uniqueDistrictArtKey, string bonusTitle, string bonusDescription,
            string identityDescription)
        {
            Id = id;
            NameRu = nameRu;
            Archetype = archetype;
            StrategicGoal = strategicGoal;
            CapitalName = capitalName;
            AdditionalCityNames = additionalCityNames ?? Array.Empty<string>();
            ArtKey = artKey;
            LeaderNameRu = leaderNameRu;
            LeaderTitleRu = leaderTitleRu;
            LeaderBiographyRu = leaderBiographyRu;
            LeaderHistoricalNoteRu = leaderHistoricalNoteRu;
            LeaderArtKey = leaderArtKey;
            CampusNameRu = campusNameRu;
            CampusDescriptionRu = campusDescriptionRu;
            CampusBonusDescriptionRu = campusBonusDescriptionRu;
            CampusArtKey = campusArtKey;
            CampusKnowledgeTenthsPerLevel = campusKnowledgeTenthsPerLevel;
            CampusTreasuryTenthsPerLevel = campusTreasuryTenthsPerLevel;
            CampusCulturePrestigePerLevel = campusCulturePrestigePerLevel;
            CampusEducationPerLevel = campusEducationPerLevel;
            UniqueDistrictBuildingId = uniqueDistrictBuildingId;
            UniqueDistrictArtKey = uniqueDistrictArtKey;
            BonusTitle = bonusTitle;
            BonusDescription = bonusDescription;
            IdentityDescription = identityDescription;
        }
    }

    public static class NationCatalog
    {
        public static readonly NationDefinition[] All =
        {
            new NationDefinition(
                "jericho", "Поселение Иерихон", "Попечитель", "quality", "Иерихон",
                new[] { "Айн-эс-Султан", "Вади-Кельт", "Эйн-Геди", "Бейт-Шеан" },
                "UniqueDistricts/District_Jericho_SpringTower", "Хранительница источника", "Голос общины",
                "Собирательный образ старейшины раннего Иерихона. Она ведёт общину через переговоры, совместные запасы и решения о защите источника.",
                "У раннего поселения не сохранилось имени правителя. Персонаж художественный и не представлен как исторически засвидетельствованная личность.",
                "Leaders/Leader_Jericho", "Круг памяти",
                "Игровая интерпретация устной передачи опыта до распространения письменности; археологически подтверждённой школы Иерихона мы не заявляем.",
                "Каждый учебный район даёт +1 культурный престиж за ход.", "Campuses/Campus_Jericho", 0, 0, 1, 0,
                "jericho_spring_tower", "UniqueDistricts/District_Jericho_SpringTower",
                "Источник общины", "+10% к выпуску еды",
                "Неолитическое поселение у источника Айн-эс-Султан. Его общая жизнь строится вокруг воды, запасов и раннего земледелия."),

            new NationDefinition(
                "sumer", "Шумер", "Учёный", "science", "Урук",
                new[] { "Ур", "Лагаш", "Эриду", "Ниппур" },
                "UniqueDistricts/District_Sumer_Ziggurat", "Энхедуанна", "Жрица и поэтесса Ура",
                "Энхедуанна была верховной жрицей и автором гимнов, чьё имя сохранилось в письменных источниках. В игре она выступает культурным лидером, а не утверждается как царица Шумера.",
                "Историческая личность XXIII века до н. э.; дочь Саргона Аккадского и верховная жрица Нанны в Уре.",
                "Leaders/Leader_Sumer", "Эдубба — Дом табличек",
                "Собирательный образ шумерской школы писцов, где обучали письму, счёту и работе с глиняными табличками.",
                "Каждый учебный район даёт +1 знание за ход.", "Campuses/Campus_Sumer", 10, 0, 0, 0,
                "sumer_ziggurat_archive", "UniqueDistricts/District_Sumer_Ziggurat",
                "Память табличек", "+10% к выпуску знаний",
                "Города южной Месопотамии связывают каналы, храмы, ремесло и ранние письменные администрации."),

            new NationDefinition(
                "egypt", "Древний Египет", "Строитель наследия", "culture", "Фивы",
                new[] { "Мемфис", "Абидос", "Элефантина", "Ахетатон" },
                "UniqueDistricts/District_Egypt_Nilometer", "Хатшепсут", "Фараон XVIII династии",
                "Хатшепсут укрепляла торговые связи, поддерживала крупные строительные проекты и правила как фараон. Её экспедиция в Пунт стала заметной частью египетской истории.",
                "Историческая правительница Египта XV века до н. э.; изображение использует царский головной убор XVIII династии.",
                "Leaders/Leader_Egypt", "Пер-Анх — Дом жизни",
                "При храме могли действовать собрания писцов, архивы и медицинские знания. Это культурная модель учебного района, а не утверждение о современном университете.",
                "Каждый учебный район даёт +1 образование за ход.", "Campuses/Campus_Egypt", 0, 0, 0, 1,
                "egypt_nilometer", "UniqueDistricts/District_Egypt_Nilometer",
                "Монументальные проекты", "+10% к культурному престижу",
                "Цивилизация долины Нила опирается на управляемые разливы, дальнюю торговлю, архивы и монументальные проекты."),

            new NationDefinition(
                "indus", "Индская цивилизация", "Торговец", "trade", "Хараппа",
                new[] { "Мохенджо-Даро", "Дхолавира", "Лотхал", "Ракхигархи" },
                "UniqueDistricts/District_Indus_GreatBath", "Хранительница печати", "Представительница городского совета",
                "Собирательный персонаж для игры о городской общине долины Инда. Она представляет координацию водоснабжения, ремесла и городских правил.",
                "Личные имена правителей цивилизации пока неизвестны: письменность Инда не расшифрована. Персонаж вымышленный, не исторический портрет.",
                "Leaders/Leader_Indus", "Дом печати",
                "Игровая интерпретация обмена знаниями через городские печати и ремесло. Это не заявление о доказанной школе или университете.",
                "Каждый учебный район даёт +1 казну за ход.", "Campuses/Campus_Indus", 0, 10, 0, 0,
                "indus_great_bath", "UniqueDistricts/District_Indus_GreatBath",
                "Стандарты долины Инда", "+10% к торговому доходу",
                "Сеть крупных городов с кирпичной застройкой, водоотводом, общественными сооружениями и стандартизованными мерами.")
        };

        public static NationDefinition Get(string id)
        {
            string normalized = NormalizeLegacyId(id);
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].Id, normalized, StringComparison.Ordinal)) return All[i];
            return null;
        }

        public static NationDefinition GetAt(int index)
        {
            if (All.Length == 0) return null;
            index = (index % All.Length + All.Length) % All.Length;
            return All[index];
        }

        public static int IndexOf(string id)
        {
            string normalized = NormalizeLegacyId(id);
            for (int i = 0; i < All.Length; i++) if (All[i].Id == normalized) return i;
            return -1;
        }

        public static NationDefinition ForCivilizationIndex(int civilizationIndex, string playerNationId)
        {
            int playerIndex = IndexOf(playerNationId);
            if (playerIndex < 0) playerIndex = 0;
            return GetAt(playerIndex + civilizationIndex);
        }

        private static string NormalizeLegacyId(string id)
        {
            switch (id)
            {
                case "valley": return "jericho";
                case "archivary": return "sumer";
                case "river_league": return "egypt";
                case "garden_union": return "indus";
                default: return id;
            }
        }
    }
}
