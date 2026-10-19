using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Nasledie.Client;
using Nasledie.Core;

namespace Nasledie.EditorTools
{
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        public const string MainScenePath = "Assets/Scenes/Main.unity";

        static ProjectSetup()
        {
            EditorApplication.delayCall += EnsureProject;
        }

        [MenuItem("JERICHO/Создать главную сцену")]
        public static void EnsureProject()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists(MainScenePath))
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GameObject bootstrap = new GameObject("JerichoGame");
                bootstrap.AddComponent<GameBootstrap>();
                EditorSceneManager.SaveScene(scene, MainScenePath);
                AssetDatabase.Refresh();
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainScenePath, true)
            };
            PlayerSettings.productName = "JERICHO";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.jericho.strategy");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.jericho.strategy");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.iOS, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.iOS.targetOSVersionString = "13.0";
            Texture2D iosIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Runtime/Client/Resources/Art/AppIcon_iOS.png");
            if (iosIcon != null)
            {
                var iosIcons = new Texture2D[9];
                for (int i = 0; i < iosIcons.Length; i++) iosIcons[i] = iosIcon;
                PlayerSettings.SetIcons(NamedBuildTarget.iOS, iosIcons, IconKind.Application);
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("JERICHO/Собрать macOS player")]
        public static void BuildMacOSPlayer()
        {
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (scene.enabled) scenes.Add(scene.path);

            if (scenes.Count == 0) throw new BuildFailedException("Нет включённых сцен для сборки.");

            string outputPath = Environment.GetEnvironmentVariable("JERICHO_MAC_BUILD_PATH");
            if (string.IsNullOrWhiteSpace(outputPath)) outputPath = "Builds/Jericho-Mac.app";
            outputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            BuildProfile buildProfile = AssetDatabase.LoadAssetAtPath<BuildProfile>("Assets/BuildProfiles/macOS-Universal.asset");
            if (buildProfile == null)
                throw new BuildFailedException("macOS universal build profile is missing: Assets/BuildProfiles/macOS-Universal.asset.");

            var options = new BuildPlayerWithProfileOptions
            {
                buildProfile = buildProfile,
                locationPathName = outputPath,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("macOS player build failed: " + report.summary.result + ".");

            Debug.Log("JERICHO macOS player built at " + outputPath + ".");
        }
    }

    public sealed class BuildSceneValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return -1000; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            ProjectSetup.EnsureProject();
            if (!File.Exists(ProjectSetup.MainScenePath)) throw new BuildFailedException("Главная сцена игры не найдена.");
            string guid = AssetDatabase.AssetPathToGUID(ProjectSetup.MainScenePath);
            if (string.IsNullOrEmpty(guid)) throw new BuildFailedException("Главная сцена не импортирована в Unity AssetDatabase.");
            ArtResourceCheck.Run();
            CampaignSmokeCheck.Run();
        }
    }

    public static class ArtResourceCheck
    {
        [MenuItem("JERICHO/Проверить игровые материалы")]
        public static void RunFromMenu()
        {
            Run();
            Debug.Log("JERICHO: эпохальные атласы, культуры, фоновые иллюстрации и звуковые материалы доступны через Resources.");
        }

        public static void Run()
        {
            RequireSprites("Art/BuildingAtlas", 16);
            string[] eras = { "Communities", "Cities", "ConnectedLands", "Industry", "Synthesis" };
            for (int i = 0; i < eras.Length; i++) RequireSprites("Art/BuildingAtlas_" + eras[i], 16);
            RequireSprites("Art/WorldAtlas", 16);
            RequireSprites("Art/UnitAtlas", 4);
            RequireSprites("Art/UnitEraAtlas", 20);
            RequireSprites("Art/UnitRosterAtlas", 25);
            RequireSprites("Art/AchievementAtlas", 12);
            RequireSprites("Art/Animations/WorkerActivities_Rural", 16);
            RequireSprites("Art/Animations/WorkerActivities_Civic", 16);
            RequireSpriteFrames("Art/Animations/WorkerActivities_Rural", new[] { "Activity_Farmer", "Activity_Lumberjack", "Activity_Blacksmith", "Activity_Scholar" });
            RequireSpriteFrames("Art/Animations/WorkerActivities_Civic", new[] { "Activity_Merchant", "Activity_Artist", "Activity_Steward", "Activity_Builder" });
            for (int i = 0; i < NationCatalog.All.Length; i++)
            {
                NationDefinition nation = NationCatalog.All[i];
                RequireSprite("Art/" + nation.ArtKey, "архитектура нации " + nation.NameRu);
                RequireSprite("Art/" + nation.LeaderArtKey, "портрет персонажа " + nation.LeaderNameRu);
                RequireSprite("Art/" + nation.CampusArtKey, "культурная школа " + nation.NameRu);
                RequireSprite("Art/" + nation.UniqueDistrictArtKey, "уникальный район " + nation.NameRu);
                if (RulesCatalog.Building(nation.UniqueDistrictBuildingId) == null)
                    throw new BuildFailedException("Не найдено игровое правило уникального района " + nation.UniqueDistrictBuildingId + ".");
            }
            if (Resources.Load<Sprite>("Art/Jericho_Logo") == null) throw new BuildFailedException("Не найден логотип JERICHO.");
            if (Resources.Load<Sprite>("Art/AppIcon_iOS") == null) throw new BuildFailedException("Не найдена иконка iOS.");
            RequireSprite("Art/Building_Factory", "изображение индустриальной фабрики");
            string[] backgrounds = { "Menu_Background", "Settings_Background", "Tutorial_Background", "Campaign_Background",
                "City_Background", "Research_Background", "Diplomacy_Background", "Events_Background", "Archive_Background",
                "Victory_Background", "Defeat_Background" };
            string[] backgroundNames = { "главное меню", "настройки", "обучение", "настройка кампании", "город", "исследования",
                "дипломатия", "события", "архив", "победа", "итоги поражения" };
            for (int i = 0; i < backgrounds.Length; i++)
                RequireSprite("Art/Backgrounds/" + backgrounds[i], "фон экрана «" + backgroundNames[i] + "»");
            string[] musicTracks = { "Nasledie_Theme", "Theme_Cities", "Theme_ConnectedLands", "Theme_Industry", "Theme_Synthesis" };
            for (int i = 0; i < musicTracks.Length; i++)
                RequireAudioClip("Audio/" + musicTracks[i], "музыкальная тема эпохи " + eras[i]);
            string[] soundEffects = { "UI_Click", "UI_Confirm", "UI_Cancel", "World_Move", "Turn_Advance", "Build_Complete",
                "Research_Discovery", "Era_Transition", "Diplomacy_Seal", "Battle_Resolve", "Victory", "Defeat" };
            for (int i = 0; i < soundEffects.Length; i++)
                RequireAudioClip("Audio/Sfx/" + soundEffects[i], "звуковой эффект " + soundEffects[i]);
        }

        private static void RequireAudioClip(string path, string description)
        {
            if (Resources.Load<AudioClip>(path) == null)
                throw new BuildFailedException("Не найден " + description + " (" + path + ").");
        }

        private static void RequireSprites(string path, int expected)
        {
            Sprite[] sprites = Resources.LoadAll<Sprite>(path);
            if (sprites == null || sprites.Length != expected)
                throw new BuildFailedException("Атлас " + path + " должен содержать " + expected + " спрайтов, сейчас: " + (sprites == null ? 0 : sprites.Length) + ". Проверьте импорт ArtAssetImporter.");
        }

        private static void RequireSpriteFrames(string path, string[] activities)
        {
            Sprite[] sprites = Resources.LoadAll<Sprite>(path);
            for (int activity = 0; activity < activities.Length; activity++)
                for (int frame = 0; frame < 4; frame++)
                {
                    string expected = activities[activity] + "_" + frame;
                    bool found = false;
                    for (int i = 0; i < sprites.Length; i++)
                        if (sprites[i] != null && sprites[i].name == expected) { found = true; break; }
                    if (!found) throw new BuildFailedException("Не найден кадр " + expected + " в атласе " + path + ".");
                }
        }

        private static void RequireSprite(string path, string description)
        {
            if (Resources.Load<Sprite>(path) == null)
                throw new BuildFailedException("Не найден спрайт: " + description + " (" + path + ").");
        }
    }

    public static class CampaignSmokeCheck
    {
        [MenuItem("JERICHO/Проверить кампанию")]
        public static void RunFromMenu()
        {
            Run();
            Debug.Log("JERICHO: проверка каталога, сериализации и пошагового цикла завершилась без ошибок.");
        }

        public static void Run()
        {
            CheckUniqueIds();
            for (int difficulty = 0; difficulty < 4; difficulty++)
            {
                GameSimulation simulation = GameSimulation.NewCampaign(126831 + difficulty, (Difficulty)difficulty);
                if (simulation.State.Cells.Length != 169 || simulation.State.Civilizations.Length != 4 || simulation.State.Cities.Length != 4)
                    throw new BuildFailedException("Начальная карта кампании имеет неверный размер.");
                string json = JsonUtility.ToJson(simulation.State);
                GameState restored = JsonUtility.FromJson<GameState>(json);
                if (restored == null || restored.Seed != simulation.State.Seed || restored.Cities.Length != 4)
                    throw new BuildFailedException("Игровое состояние не проходит сериализацию JSON.");
                simulation = new GameSimulation(restored);
                while (!simulation.State.IsFinished && simulation.State.Turn <= simulation.State.MaximumTurn)
                {
                    int before = simulation.State.Turn;
                    simulation.EndTurn();
                    if (!simulation.State.IsFinished && simulation.State.Turn != before + 1)
                        throw new BuildFailedException("Пошаговый цикл остановился на сложности " + (Difficulty)difficulty + ": " + simulation.LastMessage);
                    if (simulation.LastMessage != null && simulation.LastMessage.StartsWith("Ход не сохранён", System.StringComparison.Ordinal))
                        throw new BuildFailedException("Внутренняя проверка кампании остановила ход на сложности " + (Difficulty)difficulty + ": " + simulation.LastMessage);
                }
                if (!simulation.State.IsFinished || simulation.State.WinnerCivilizationId < 0 || simulation.State.Turn > simulation.State.MaximumTurn)
                    throw new BuildFailedException("Кампания не дошла до корректного финала на сложности " + (Difficulty)difficulty + ".");
            }
        }

        private static void CheckUniqueIds()
        {
            var technologyIds = new HashSet<string>();
            for (int i = 0; i < RulesCatalog.Technologies.Length; i++)
            {
                TechnologyDefinition technology = RulesCatalog.Technologies[i];
                if (!technologyIds.Add(technology.Id)) throw new BuildFailedException("Повторяется технология " + technology.Id + ".");
            }
            for (int i = 0; i < RulesCatalog.Technologies.Length; i++)
                for (int p = 0; p < RulesCatalog.Technologies[i].Prerequisites.Length; p++)
                {
                    string prerequisite = RulesCatalog.Technologies[i].Prerequisites[p];
                    string[] alternatives = prerequisite.Split('|');
                    bool found = false;
                    for (int a = 0; a < alternatives.Length; a++) if (technologyIds.Contains(alternatives[a])) found = true;
                    if (!found) throw new BuildFailedException("Не найдена предварительная технология " + prerequisite + ".");
                }
            var buildingIds = new HashSet<string>();
            for (int i = 0; i < RulesCatalog.Buildings.Length; i++)
            {
                BuildingDefinition building = RulesCatalog.Buildings[i];
                if (!buildingIds.Add(building.Id)) throw new BuildFailedException("Повторяется здание " + building.Id + ".");
                if (!string.IsNullOrEmpty(building.RequiredTechnology) && !technologyIds.Contains(building.RequiredTechnology))
                    throw new BuildFailedException("Здание " + building.Id + " ссылается на отсутствующую технологию.");
            }
            for (int i = 0; i < RulesCatalog.Units.Length; i++)
            {
                UnitDefinition unit = RulesCatalog.Units[i];
                if (!string.IsNullOrEmpty(unit.RequiredTechnology) && !technologyIds.Contains(unit.RequiredTechnology))
                    throw new BuildFailedException("Отряд " + unit.Kind + " ссылается на отсутствующую технологию.");
                if (!string.IsNullOrEmpty(unit.RequiredBuilding) && !buildingIds.Contains(unit.RequiredBuilding))
                    throw new BuildFailedException("Отряд " + unit.Kind + " ссылается на отсутствующее здание.");
            }
            var improvements = new HashSet<ImprovementKind>();
            for (int i = 0; i < RulesCatalog.Improvements.Length; i++)
            {
                ImprovementDefinition improvement = RulesCatalog.Improvements[i];
                if (!improvements.Add(improvement.Kind)) throw new BuildFailedException("Повторяется улучшение " + improvement.Kind + ".");
                if (!technologyIds.Contains(improvement.RequiredTechnology)) throw new BuildFailedException("Улучшение " + improvement.Kind + " ссылается на отсутствующую технологию.");
            }
        }
    }
}
