using System;
using System.Collections.Generic;
using UnityEngine;
using Nasledie.Core;

namespace Nasledie.Client
{
    public sealed class AchievementDefinition
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string Description;
        public readonly int RenownReward;
        public readonly string TitleReward;
        public readonly string Artwork;

        public AchievementDefinition(string id, string name, string description, int renown, string title, string artwork)
        {
            Id = id;
            Name = name;
            Description = description;
            RenownReward = renown;
            TitleReward = title;
            Artwork = artwork;
        }
    }

    [Serializable]
    internal sealed class PlayerMetaProfile
    {
        public int Version = 1;
        public int Renown;
        public string SelectedTitleId = "";
        public string[] UnlockedAchievementIds = Array.Empty<string>();
    }

    /// <summary>Device-level progression. Rewards are cosmetic, persistent, and never alter campaign balance.</summary>
    public static class AchievementService
    {
        private const string SaveKey = "nasledie.meta_profile.v1";
        private static PlayerMetaProfile profile;

        public static readonly AchievementDefinition[] Definitions =
        {
            new AchievementDefinition("first_turn", "Первый летописный лист", "Завершите первый ход кампании.", 20, "Хранитель хроник", "Badge_Scroll"),
            new AchievementDefinition("builder", "Преобразить землю", "Создайте пять улучшений на своей территории.", 30, "Мастер земли", "Badge_Wheat"),
            new AchievementDefinition("second_city", "Новые основания", "Основайте второй город.", 35, "Основатель", "Badge_City"),
            new AchievementDefinition("researcher", "Круг знаний", "Изучите восемь технологий.", 40, "Учёный", "Badge_Book"),
            new AchievementDefinition("diplomat", "Сеть доверия", "Удерживайте одновременно два мирных договора.", 40, "Посредник", "Badge_Handshake"),
            new AchievementDefinition("caretaker", "Города для людей", "Добейтесь здоровья и доверия не ниже 70 в двух городах.", 35, "Попечитель", "Badge_Heart"),
            new AchievementDefinition("crisis_helper", "Опора мира", "Внесите вклад в решение глобального кризиса.", 30, "Опора мира", "Badge_Wreath"),
            new AchievementDefinition("reconciler", "Сложить мечи", "Завершите конфликт и восстановитесь до военной нагрузки не выше 40.", 45, "Примиритель", "Badge_WhiteFlag"),
            new AchievementDefinition("victor", "Создатель наследия", "Победите в кампании любым путём.", 100, "Создатель наследия", "Badge_Crown"),
            new AchievementDefinition("peaceful_victory", "Без меча", "Победите через благополучие или дипломатию, ни разу не объявляя войну.", 80, "Миротворец", "Badge_OliveSun"),
            new AchievementDefinition("cultural_victory", "Голос культуры", "Завершите кампанию культурной победой.", 60, "Меценат", "Badge_Lyre")
        };

        public static int Renown { get { EnsureLoaded(); return profile.Renown; } }
        public static int UnlockedCount { get { EnsureLoaded(); return profile.UnlockedAchievementIds.Length; } }
        public static string SelectedTitleId { get { EnsureLoaded(); return profile.SelectedTitleId; } }
        public static string SelectedTitle
        {
            get
            {
                EnsureLoaded();
                if (string.IsNullOrEmpty(profile.SelectedTitleId)) return "Без звания";
                AchievementDefinition definition = Find(profile.SelectedTitleId);
                return definition == null ? "Без звания" : definition.TitleReward;
            }
        }
        public static string SelectedTitleArtwork
        {
            get
            {
                EnsureLoaded();
                AchievementDefinition definition = Find(profile.SelectedTitleId);
                return definition == null ? "Badge_Star" : definition.Artwork;
            }
        }

        public static AchievementDefinition[] Evaluate(GameState state)
        {
            EnsureLoaded();
            if (state == null) return Array.Empty<AchievementDefinition>();
            var earned = new List<AchievementDefinition>();
            for (int i = 0; i < Definitions.Length; i++)
            {
                AchievementDefinition definition = Definitions[i];
                if (IsUnlocked(definition.Id) || !IsComplete(definition.Id, state)) continue;
                Array.Resize(ref profile.UnlockedAchievementIds, profile.UnlockedAchievementIds.Length + 1);
                profile.UnlockedAchievementIds[profile.UnlockedAchievementIds.Length - 1] = definition.Id;
                profile.Renown += definition.RenownReward;
                earned.Add(definition);
            }
            if (earned.Count > 0) Save();
            return earned.ToArray();
        }

        public static bool IsUnlocked(string id)
        {
            EnsureLoaded();
            return Array.IndexOf(profile.UnlockedAchievementIds, id) >= 0;
        }

        public static bool SelectTitle(string achievementId)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(achievementId))
            {
                profile.SelectedTitleId = "";
                Save();
                return true;
            }
            if (!IsUnlocked(achievementId)) return false;
            AchievementDefinition definition = Find(achievementId);
            if (definition == null) return false;
            profile.SelectedTitleId = achievementId;
            Save();
            return true;
        }

        public static string ProgressText(AchievementDefinition definition, GameState state)
        {
            if (definition == null) return "";
            if (IsUnlocked(definition.Id)) return "Получено · награда уже в профиле";
            if (state == null) return "Начните кампанию, чтобы увидеть прогресс";
            CivilizationState player = state.Civilization(state.HumanCivilizationId);
            if (player == null) return "Нет данных кампании";
            switch (definition.Id)
            {
                case "first_turn": return Math.Min(1, Math.Max(0, state.Turn - 1)) + "/1 ход";
                case "builder": return CountImprovements(state, player.Id) + "/5 улучшений";
                case "second_city": return CountCities(state, player.Id) + "/2 города";
                case "researcher": return Math.Min(8, player.CompletedTechnologies) + "/8 технологий";
                case "diplomat": return CountPeaceTreaties(state, player.Id) + "/2 активных договора";
                case "caretaker": return QualifyingCities(state, player.Id) + "/2 города с хорошим здоровьем и доверием";
                case "crisis_helper": return HasHelpedWithCrisis(state, player.Id) ? "Вклад внесён" : "Вклад пока не внесён";
                case "reconciler": return player.WarsStarted == 0 ? "Конфликтов игрока ещё не было" : HasActiveConflict(state, player.Id) ? "Идёт восстановление после конфликта" : "Военная нагрузка " + player.WarStrain + "/40";
                case "victor": return state.IsFinished && state.WinnerCivilizationId == player.Id ? "Победа достигнута" : "Победите любым путём";
                case "peaceful_victory": return player.WarsStarted == 0 ? "Выиграйте путь благополучия или дипломатии" : "Объявленная война закрыла это достижение в текущей кампании";
                case "cultural_victory": return state.IsFinished && state.WinnerCivilizationId == player.Id && state.WinningPath == VictoryPath.Cultural ? "Культурная победа достигнута" : "Завершите культурную победу";
                default: return "В процессе";
            }
        }

        private static bool IsComplete(string id, GameState state)
        {
            CivilizationState player = state.Civilization(state.HumanCivilizationId);
            if (player == null) return false;
            switch (id)
            {
                case "first_turn": return state.Turn > 1;
                case "builder": return CountImprovements(state, player.Id) >= 5;
                case "second_city": return CountCities(state, player.Id) >= 2;
                case "researcher": return player.CompletedTechnologies >= 8;
                case "diplomat": return CountPeaceTreaties(state, player.Id) >= 2;
                case "caretaker": return QualifyingCities(state, player.Id) >= 2;
                case "crisis_helper": return HasHelpedWithCrisis(state, player.Id);
                case "reconciler": return player.WarsStarted > 0 && !HasActiveConflict(state, player.Id) && player.WarStrain <= 40;
                case "victor": return state.IsFinished && state.WinnerCivilizationId == player.Id;
                case "peaceful_victory": return state.IsFinished && state.WinnerCivilizationId == player.Id && player.WarsStarted == 0 &&
                    (state.WinningPath == VictoryPath.QualityOfLife || state.WinningPath == VictoryPath.Diplomatic);
                case "cultural_victory": return state.IsFinished && state.WinnerCivilizationId == player.Id && state.WinningPath == VictoryPath.Cultural;
                default: return false;
            }
        }

        private static int CountImprovements(GameState state, int ownerId)
        {
            int count = 0;
            for (int i = 0; i < state.Cells.Length; i++)
                if (state.Cells[i].OwnerId == ownerId && state.Cells[i].Improvement != ImprovementKind.None && state.Cells[i].Damage < 75) count++;
            return count;
        }

        private static int CountCities(GameState state, int ownerId)
        {
            int count = 0;
            for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].OwnerId == ownerId) count++;
            return count;
        }

        private static int CountPeaceTreaties(GameState state, int ownerId)
        {
            int count = 0;
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (!treaty.Active || (treaty.FromCivilizationId != ownerId && treaty.ToCivilizationId != ownerId)) continue;
                if (treaty.Kind == TreatyKind.TradeRoute || treaty.Kind == TreatyKind.NonAggression || treaty.Kind == TreatyKind.OpenBorders ||
                    treaty.Kind == TreatyKind.KnowledgeExchange || treaty.Kind == TreatyKind.Guarantee) count++;
            }
            return count;
        }

        private static int QualifyingCities(GameState state, int ownerId)
        {
            int count = 0;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId == ownerId && city.Health >= 70 && city.Trust >= 70) count++;
            }
            return count;
        }

        private static bool HasHelpedWithCrisis(GameState state, int ownerId)
        {
            int mask = 1 << ownerId;
            return state.HasResolvedGlobalCrisis && (state.ResolvedGlobalCrisisContributorMask & mask) != 0;
        }

        private static bool HasActiveConflict(GameState state, int ownerId)
        {
            for (int i = 0; i < state.Conflicts.Length; i++)
            {
                ConflictState conflict = state.Conflicts[i];
                if (conflict.Active && (conflict.AggressorId == ownerId || conflict.DefenderId == ownerId)) return true;
            }
            return false;
        }

        private static AchievementDefinition Find(string id)
        {
            for (int i = 0; i < Definitions.Length; i++) if (Definitions[i].Id == id) return Definitions[i];
            return null;
        }

        private static void EnsureLoaded()
        {
            if (profile != null) return;
            try
            {
                string json = PlayerPrefs.GetString(SaveKey, "");
                profile = string.IsNullOrEmpty(json) ? new PlayerMetaProfile() : JsonUtility.FromJson<PlayerMetaProfile>(json);
            }
            catch { profile = null; }
            if (profile == null || profile.Version != 1) profile = new PlayerMetaProfile();
            profile.UnlockedAchievementIds = profile.UnlockedAchievementIds ?? Array.Empty<string>();
        }

        private static void Save()
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(profile));
            PlayerPrefs.Save();
        }
    }
}
