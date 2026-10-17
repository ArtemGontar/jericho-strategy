using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using Nasledie.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Nasledie.Client
{
    public sealed class GameBootstrap : MonoBehaviour
    {
        private enum ScreenPage { Overview, City, Research, Diplomacy, Laws, Events, Victory, Chronicle }

        private GameSimulation simulation;
        private Canvas canvas;
        private RectTransform safeRoot;
        private RectTransform header;
        private RectTransform pageHost;
        private RectTransform navHost;
        private RectTransform actionHost;
        private RectTransform menuHost;
        private Text titleLabel;
        private Text resourceLabel;
        private Text noticeLabel;
        private string activeUtilityScreen = "menu";
        private ScreenPage currentPage = ScreenPage.Overview;
        private int selectedCityId = -1;
        private int selectedUnitId = -1;
        private int selectedQ = -1;
        private int selectedR = -1;
        private AudioSource soundtrack;
        private bool musicEnabled = true;
        private int musicVolumeStep = 1;
        private static readonly float[] MusicVolumeSteps = { 0.12f, 0.24f, 0.38f, 0.55f };
        private static readonly Dictionary<string, Sprite> Artwork = new Dictionary<string, Sprite>();
        private static bool artworkAtlasesLoaded;
        private Difficulty selectedDifficulty = Difficulty.Standard;
        private string notice = "";
        private Rect lastSafeArea;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private int animationMode = 1; // 0: off, 1: quick, 2: full
        private bool presentationBusy;
        private bool dismissPresentationRequested;
        private int movingUnitId = -1;
        private Vector2 mapScrollPosition = new Vector2(0f, 1f);
        private GameObject presentationLayer;

        private void Start()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            musicEnabled = PlayerPrefs.GetInt("nasledie.music.enabled", 1) != 0;
            musicVolumeStep = Mathf.Clamp(PlayerPrefs.GetInt("nasledie.music.volume", 1), 0, MusicVolumeSteps.Length - 1);
            animationMode = Mathf.Clamp(PlayerPrefs.GetInt("jericho.animation.mode", 1), 0, 2);
            EnsureEventSystem();
            BuildCanvas();
            InitializeSoundtrack();
            ShowMenu();
        }

        private void Update()
        {
            if (lastScreenWidth == Screen.width && lastScreenHeight == Screen.height && lastSafeArea == Screen.safeArea) return;
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            lastSafeArea = Screen.safeArea;
            ApplySafeArea();
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystem);
        }

        private void BuildCanvas()
        {
            GameObject canvasObject = new GameObject("NasledieCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            safeRoot = UiKit.Rect(UiKit.CreateObject("SafeArea", canvasObject.transform));
            ApplySafeArea();

            GameObject background = UiKit.CreateObject("Background", safeRoot);
            Image backgroundImage = background.AddComponent<Image>();
            backgroundImage.color = GamePalette.Ink;
            UiKit.SetAnchors(UiKit.Rect(background), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            background.transform.SetAsFirstSibling();

            GameObject headerObject = UiKit.Panel(safeRoot, "Header", GamePalette.InkSoft);
            header = UiKit.Rect(headerObject);
            UiKit.SetAnchors(header, new Vector2(0, 0.88f), Vector2.one, new Vector2(0, 0), Vector2.zero);
            UiKit.Vertical(headerObject, 8, 0);
            titleLabel = UiKit.Label(headerObject.transform, "JERICHO", 30, GamePalette.Gold, TextAnchor.MiddleLeft, true);
            LayoutElement titleLayout = titleLabel.GetComponent<LayoutElement>();
            titleLayout.minHeight = 32;
            titleLayout.preferredHeight = 34;
            resourceLabel = UiKit.Label(headerObject.transform, "", 16, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            LayoutElement resourceLayout = resourceLabel.GetComponent<LayoutElement>();
            resourceLayout.minHeight = 25;
            resourceLayout.preferredHeight = 26;
            noticeLabel = UiKit.Label(headerObject.transform, "", 15, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            LayoutElement noticeLayout = noticeLabel.GetComponent<LayoutElement>();
            noticeLayout.minHeight = 20;
            noticeLayout.preferredHeight = 22;

            GameObject pageObject = UiKit.CreateObject("PageHost", safeRoot);
            pageHost = UiKit.Rect(pageObject);
            UiKit.SetAnchors(pageHost, new Vector2(0, 0.365f), new Vector2(1, 0.88f), Vector2.zero, Vector2.zero);
            pageObject.SetActive(false);

            GameObject navObject = UiKit.Panel(safeRoot, "Navigation", GamePalette.Ink);
            navHost = UiKit.Rect(navObject);
            UiKit.SetAnchors(navHost, new Vector2(0, 0.125f), new Vector2(1, 0.365f), Vector2.zero, Vector2.zero);
            UiKit.Vertical(navObject, 4, 3);
            GameObject navRow1 = UiKit.CreateObject("NavigationRow1", navObject.transform);
            UiKit.Horizontal(navRow1, 4, 8);
            GameObject navRow2 = UiKit.CreateObject("NavigationRow2", navObject.transform);
            UiKit.Horizontal(navRow2, 4, 8);
            AddNavButton(navRow1.transform, "МИР", ScreenPage.Overview);
            AddNavButton(navRow1.transform, "ГОРОД", ScreenPage.City);
            AddNavButton(navRow1.transform, "НАУКА", ScreenPage.Research);
            AddNavButton(navRow1.transform, "ДИПЛОМАТИЯ", ScreenPage.Diplomacy);
            AddNavButton(navRow2.transform, "ЗАКОНЫ", ScreenPage.Laws);
            AddNavButton(navRow2.transform, "СОБЫТИЯ", ScreenPage.Events);
            AddNavButton(navRow2.transform, "ПОБЕДА", ScreenPage.Victory);
            AddNavButton(navRow2.transform, "ЛЕТОПИСЬ", ScreenPage.Chronicle);

            GameObject actionObject = UiKit.Panel(safeRoot, "TurnActions", GamePalette.InkSoft);
            actionHost = UiKit.Rect(actionObject);
            UiKit.SetAnchors(actionHost, Vector2.zero, new Vector2(1, 0.125f), Vector2.zero, Vector2.zero);
            UiKit.Horizontal(actionObject, 4, 4);
            Button menuButton = UiKit.Button(actionObject.transform, "МЕНЮ", ShowMenu, GamePalette.Ink, 110, 16);
            Button saveButton = UiKit.Button(actionObject.transform, "СОХРАНИТЬ", SaveManual, GamePalette.Ink, 110, 16);
            Button achievementsButton = UiKit.Button(actionObject.transform, "НАГРАДЫ", ShowAchievements, GamePalette.Ink, 110, 15);
            Button endButton = UiKit.Button(actionObject.transform, "ЗАВЕРШИТЬ ХОД", EndTurn, GamePalette.Teal, 110, 17);
            menuButton.GetComponent<LayoutElement>().flexibleWidth = 0.48f;
            saveButton.GetComponent<LayoutElement>().flexibleWidth = 0.72f;
            achievementsButton.GetComponent<LayoutElement>().flexibleWidth = 0.66f;
            endButton.GetComponent<LayoutElement>().flexibleWidth = 1.15f;
            actionObject.SetActive(false);
            navObject.SetActive(false);

            GameObject menuObject = UiKit.Panel(safeRoot, "MenuOverlay", new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.98f));
            menuHost = UiKit.Rect(menuObject);
            UiKit.SetAnchors(menuHost, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        private void ApplySafeArea()
        {
            if (safeRoot == null) return;
            Rect safe = Screen.safeArea;
            safeRoot.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            safeRoot.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            safeRoot.offsetMin = Vector2.zero;
            safeRoot.offsetMax = Vector2.zero;
        }

        private void AddNavButton(Transform parent, string label, ScreenPage page)
        {
            Button button = UiKit.Button(parent, label, delegate { currentPage = page; notice = ""; RenderGame(); }, GamePalette.InkSoft, 110, 17);
            button.GetComponent<LayoutElement>().flexibleWidth = 1f;
        }

        private void ShowMenu()
        {
            activeUtilityScreen = "menu";
            DestroyChildren(menuHost);
            menuHost.gameObject.SetActive(true);
            if (pageHost != null) pageHost.gameObject.SetActive(false);
            if (navHost != null) navHost.gameObject.SetActive(false);
            if (actionHost != null) actionHost.gameObject.SetActive(false);
            if (header != null) header.gameObject.SetActive(false);

            GameObject menu = UiKit.ScrollView(menuHost, "MenuScroll");
            UiKit.Vertical(menu, 24, 18);
            AddArtwork(menu.transform, "Jericho_Logo", 178);
            UiKit.Label(menu.transform, "JERICHO", 64, GamePalette.Gold, TextAnchor.MiddleCenter, true);
            UiKit.Label(menu.transform, "От первого поселения — к цивилизации, которую выбирают строить.", 26, GamePalette.Paper, TextAnchor.MiddleCenter, false);
            UiKit.Label(menu.transform, "Экономика · люди · культура · договоры · решения с последствиями", 22, GamePalette.Muted, TextAnchor.MiddleCenter, false);
            UiKit.Label(menu.transform, "Профиль · " + AchievementService.SelectedTitle + " · слава " + AchievementService.Renown + " · достижений " + AchievementService.UnlockedCount + "/" + AchievementService.Definitions.Length,
                18, GamePalette.Gold, TextAnchor.MiddleCenter, true);
            AddArtwork(menu.transform, AchievementService.SelectedTitleArtwork, 82);
            UiKit.Button(menu.transform, musicEnabled ? "МУЗЫКА: ВКЛЮЧЕНА" : "МУЗЫКА: ВЫКЛЮЧЕНА", ToggleMusic, GamePalette.InkSoft, 54, 18);
            UiKit.Button(menu.transform, "ДОСТИЖЕНИЯ И НАГРАДЫ", ShowAchievements, GamePalette.InkSoft, 66, 21);
            UiKit.Button(menu.transform, "НАСТРОЙКИ", ShowSettings, GamePalette.InkSoft, 62, 21);
            Button savesButton = UiKit.Button(menu.transform, "УПРАВЛЕНИЕ СОХРАНЕНИЯМИ", ShowSaves, GamePalette.InkSoft, 62, 19);
            savesButton.interactable = SaveService.HasSave;

            if (simulation != null)
            {
                UiKit.Button(menu.transform, "ВЕРНУТЬСЯ В КАМПАНИЮ", delegate { menuHost.gameObject.SetActive(false); RenderGame(); }, GamePalette.Teal, 92, 27);
                UiKit.Button(menu.transform, "СОХРАНИТЬ И ПРОДОЛЖИТЬ", delegate { SaveManual(); menuHost.gameObject.SetActive(false); RenderGame(); }, GamePalette.InkSoft, 76, 23);
                UiKit.Button(menu.transform, "В ГЛАВНОЕ МЕНЮ", delegate { simulation = null; ShowMenu(); }, GamePalette.InkSoft, 70, 22);
                return;
            }

            UiKit.Button(menu.transform, "НОВАЯ КАМПАНИЯ", StartNewCampaign, GamePalette.Teal, 96, 30);
            Button continueButton = UiKit.Button(menu.transform, "ПРОДОЛЖИТЬ", LoadCampaign, GamePalette.InkSoft, 80, 26);
            continueButton.interactable = SaveService.HasSave;
            UiKit.Button(menu.transform, "СЛОЖНОСТЬ: " + DifficultyName(selectedDifficulty), CycleDifficulty, GamePalette.InkSoft, 70, 22);
            UiKit.Button(menu.transform, "КАК ИГРАТЬ", ShowTutorial, GamePalette.InkSoft, 70, 22);
            UiKit.Label(menu.transform, "Демо-кампания · 4 цивилизации · 5 путей к победе · одиночная игра без сети", 20, GamePalette.Muted, TextAnchor.MiddleCenter, false);
        }

        private void InitializeSoundtrack()
        {
            GameObject sourceObject = new GameObject("NasledieSoundtrack");
            sourceObject.transform.SetParent(transform, false);
            if (FindObjectOfType<AudioListener>() == null) sourceObject.AddComponent<AudioListener>();
            soundtrack = sourceObject.AddComponent<AudioSource>();
            soundtrack.clip = Resources.Load<AudioClip>("Audio/Nasledie_Theme");
            soundtrack.loop = true;
            soundtrack.playOnAwake = false;
            soundtrack.spatialBlend = 0f;
            soundtrack.volume = MusicVolumeSteps[musicVolumeStep];
            if (soundtrack.clip != null && musicEnabled) soundtrack.Play();
        }

        private void ToggleMusic()
        {
            musicEnabled = !musicEnabled;
            PlayerPrefs.SetInt("nasledie.music.enabled", musicEnabled ? 1 : 0);
            PlayerPrefs.Save();
            if (soundtrack != null)
            {
                if (musicEnabled && soundtrack.clip != null) soundtrack.Play();
                else soundtrack.Pause();
            }
            if (simulation != null && !menuHost.gameObject.activeSelf) RenderGame();
            else if (activeUtilityScreen == "settings") ShowSettings();
            else if (activeUtilityScreen == "achievements") ShowAchievements();
            else if (activeUtilityScreen == "saves") ShowSaves();
            else ShowMenu();
        }

        private string MusicVolumeLabel()
        {
            return "ГРОМКОСТЬ МУЗЫКИ · " + Mathf.RoundToInt(MusicVolumeSteps[musicVolumeStep] * 100f) + "%";
        }

        private void CycleMusicVolume()
        {
            musicVolumeStep = (musicVolumeStep + 1) % MusicVolumeSteps.Length;
            if (soundtrack != null) soundtrack.volume = MusicVolumeSteps[musicVolumeStep];
            PlayerPrefs.SetInt("nasledie.music.volume", musicVolumeStep);
            PlayerPrefs.Save();
            ShowSettings();
        }

        private void ShowSettings()
        {
            PrepareUtilityOverlay();
            activeUtilityScreen = "settings";
            GameObject content = UiKit.ScrollView(menuHost, "SettingsScroll");
            UiKit.Label(content.transform, "НАСТРОЙКИ", 44, GamePalette.Gold, TextAnchor.MiddleCenter, true);
            UiKit.Label(content.transform, "Выбор сохраняется на этом устройстве.", 20, GamePalette.Muted, TextAnchor.MiddleCenter, false);
            UiKit.Button(content.transform, musicEnabled ? "МУЗЫКА · ВКЛЮЧЕНА" : "МУЗЫКА · ВЫКЛЮЧЕНА", ToggleMusic, GamePalette.Teal, 68, 22);
            UiKit.Button(content.transform, MusicVolumeLabel() + " · ИЗМЕНИТЬ", CycleMusicVolume, GamePalette.InkSoft, 68, 21);
            UiKit.Button(content.transform, AnimationModeLabel() + " · ИЗМЕНИТЬ", CycleAnimationMode, GamePalette.InkSoft, 68, 21);
            UiKit.Label(content.transform, "Экран: альбомная ориентация · safe area для вырезов iPhone включена.\nАвтосохранение: " +
                (SaveService.HasSave ? "кампания найдена" : "появится после первого хода"), 21, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Label(content.transform, "Сложность: " + DifficultyName(selectedDifficulty) + " · меняется перед новой кампанией.", 20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            UiKit.Button(content.transform, "НАЗАД", ShowMenu, GamePalette.Teal, 70, 22);
        }

        private string AnimationModeLabel()
        {
            return animationMode == 0 ? "АНИМАЦИИ · ВЫКЛЮЧЕНЫ" : animationMode == 1 ? "АНИМАЦИИ · БЫСТРЫЕ" : "АНИМАЦИИ · ПОЛНЫЕ";
        }

        private void CycleAnimationMode()
        {
            animationMode = (animationMode + 1) % 3;
            PlayerPrefs.SetInt("jericho.animation.mode", animationMode);
            PlayerPrefs.Save();
            ShowSettings();
        }

        private float AnimationScale { get { return animationMode == 2 ? 1f : animationMode == 1 ? 0.45f : 0f; } }

        private void ShowAchievements()
        {
            PrepareUtilityOverlay();
            activeUtilityScreen = "achievements";
            GameObject content = UiKit.ScrollView(menuHost, "AchievementsScroll");
            UiKit.Label(content.transform, "ДОСТИЖЕНИЯ И НАГРАДЫ", 42, GamePalette.Gold, TextAnchor.MiddleCenter, true);
            UiKit.Label(content.transform, "Титул: " + AchievementService.SelectedTitle + " · слава: " + AchievementService.Renown +
                " · открыто: " + AchievementService.UnlockedCount + "/" + AchievementService.Definitions.Length +
                "\nСлава и титулы сохраняются между кампаниями. Они не дают боевых или экономических бонусов.",
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            GameState state = simulation == null ? null : simulation.State;
            for (int i = 0; i < AchievementService.Definitions.Length; i++)
            {
                AchievementDefinition achievement = AchievementService.Definitions[i];
                bool unlocked = AchievementService.IsUnlocked(achievement.Id);
                bool equipped = AchievementService.SelectedTitleId == achievement.Id;
                GameObject card = UiKit.Card(content.transform, (unlocked ? "✓  " : "○  ") + achievement.Name);
                AddArtwork(card.transform, achievement.Artwork, 88);
                UiKit.Label(card.transform, achievement.Description, 20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                UiKit.Label(card.transform, AchievementService.ProgressText(achievement, state), 19,
                    unlocked ? GamePalette.Success : GamePalette.Muted, TextAnchor.MiddleLeft, true);
                UiKit.Label(card.transform, "Награда: титул «" + achievement.TitleReward + "» · " + achievement.RenownReward + " славы",
                    19, GamePalette.Gold, TextAnchor.MiddleLeft, false);
                Button equip = UiKit.Button(card.transform, equipped ? "ЭТОТ ТИТУЛ НАДЕТ" : "НАДЕТЬ ТИТУЛ «" + achievement.TitleReward + "»",
                    delegate { AchievementService.SelectTitle(achievement.Id); notice = "Титул изменён: «" + achievement.TitleReward + "»."; ShowAchievements(); },
                    unlocked ? GamePalette.Teal : GamePalette.Ink, 56, 17);
                equip.interactable = unlocked && !equipped;
            }
            UiKit.Button(content.transform, "СНЯТЬ ТИТУЛ", delegate { AchievementService.SelectTitle(""); ShowAchievements(); }, GamePalette.InkSoft, 58, 19);
            UiKit.Button(content.transform, "НАЗАД", ShowMenu, GamePalette.Teal, 70, 22);
        }

        private void ShowSaves()
        {
            PrepareUtilityOverlay();
            activeUtilityScreen = "saves";
            GameObject content = UiKit.ScrollView(menuHost, "SaveSlotsScroll");
            UiKit.Label(content.transform, "СОХРАНЕНИЯ", 44, GamePalette.Gold, TextAnchor.MiddleCenter, true);
            UiKit.Label(content.transform, "Автосохранения создаются после каждого хода; вручную сохранённая кампания занимает отдельный слот.",
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);

            GameState manual = SaveService.LoadManual();
            AddSaveSlot(content.transform, "РУЧНОЕ СОХРАНЕНИЕ", manual, delegate { OpenSavedCampaign(SaveService.LoadManual(), "Ручное сохранение"); });
            for (int slot = 0; slot < 3; slot++)
            {
                int capturedSlot = slot;
                GameState auto = SaveService.LoadAuto(slot);
                string label = slot == 0 ? "АВТОСОХРАНЕНИЕ · ПОСЛЕДНЕЕ" : slot == 1 ? "АВТОСОХРАНЕНИЕ · ПРЕДЫДУЩЕЕ" : "АВТОСОХРАНЕНИЕ · РАННЕЕ";
                string capturedLabel = label;
                AddSaveSlot(content.transform, capturedLabel, auto, delegate { OpenSavedCampaign(SaveService.LoadAuto(capturedSlot), capturedLabel); });
            }
            UiKit.Button(content.transform, "НАЗАД", ShowMenu, GamePalette.Teal, 70, 22);
        }

        private void AddSaveSlot(Transform parent, string title, GameState state, Action load)
        {
            GameObject card = UiKit.Card(parent, title);
            if (state == null)
            {
                UiKit.Label(card.transform, "Слот пуст или файл повреждён.", 20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
                return;
            }
            CivilizationState player = state.Civilization(state.HumanCivilizationId);
            UiKit.Label(card.transform, "Ход " + Math.Min(state.Turn, state.MaximumTurn) + "/" + state.MaximumTurn + " · " +
                (player == null ? "цивилизация не найдена" : player.Name) + " · мир " + state.Seed +
                (state.IsFinished ? " · кампания завершена" : ""), 20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Button(card.transform, "ЗАГРУЗИТЬ ЭТУ КАМПАНИЮ", load, GamePalette.Teal, 58, 19);
        }

        private void OpenSavedCampaign(GameState saved, string source)
        {
            if (saved == null)
            {
                notice = "Сохранение пусто или повреждено.";
                ShowSaves();
                return;
            }
            simulation = new GameSimulation(saved);
            selectedCityId = simulation.FindCapital(saved.HumanCivilizationId);
            selectedUnitId = -1;
            currentPage = saved.IsFinished ? ScreenPage.Victory : ScreenPage.Overview;
            notice = source + " загружено · ход " + saved.Turn + ".";
            menuHost.gameObject.SetActive(false);
            RenderGame();
        }

        private void PrepareUtilityOverlay()
        {
            DestroyChildren(menuHost);
            menuHost.gameObject.SetActive(true);
            if (pageHost != null) pageHost.gameObject.SetActive(false);
            if (navHost != null) navHost.gameObject.SetActive(false);
            if (actionHost != null) actionHost.gameObject.SetActive(false);
            if (header != null) header.gameObject.SetActive(false);
        }

        private static Sprite LoadArtwork(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Sprite sprite;
            if (Artwork.TryGetValue(name, out sprite)) return sprite;
            sprite = Resources.Load<Sprite>("Art/" + name);
            if (sprite == null)
            {
                LoadArtworkAtlases();
                Artwork.TryGetValue(name, out sprite);
            }
            Artwork[name] = sprite;
            return sprite;
        }

        private static void LoadArtworkAtlases()
        {
            if (artworkAtlasesLoaded) return;
            artworkAtlasesLoaded = true;
            string[] atlases =
            {
                "Art/BuildingAtlas", "Art/BuildingAtlas_Communities", "Art/BuildingAtlas_Cities",
                "Art/BuildingAtlas_ConnectedLands", "Art/BuildingAtlas_Industry", "Art/BuildingAtlas_Synthesis",
                "Art/WorldAtlas", "Art/UnitAtlas", "Art/UnitEraAtlas", "Art/AchievementAtlas"
            };
            for (int a = 0; a < atlases.Length; a++)
            {
                Sprite[] sprites = Resources.LoadAll<Sprite>(atlases[a]);
                for (int i = 0; i < sprites.Length; i++)
                    if (sprites[i] != null && !Artwork.ContainsKey(sprites[i].name)) Artwork[sprites[i].name] = sprites[i];
            }
        }

        private static void AddArtwork(Transform parent, string name, float height)
        {
            Sprite sprite = LoadArtwork(name);
            if (sprite == null) return;
            GameObject art = UiKit.CreateObject("Artwork_" + name, parent);
            Image image = art.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            LayoutElement layout = art.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleWidth = 1f;
        }

        private static string BuildingArtwork(string id)
        {
            switch (id)
            {
                case "granary": return "Building_Granary";
                case "house": return "Building_House";
                case "residential_quarter": return "Building_ResidentialQuarter";
                case "workshop": return "Building_Workshop";
                case "school": return "Building_School";
                case "market": return "Building_Market";
                case "clinic": return "Building_Clinic";
                case "cultural_house": return "Building_CulturalHall";
                case "council": return "Building_Council";
                case "administration": return "Building_Administration";
                case "road_services": return "Building_RoadServices";
                case "university": case "academy": return "Building_University";
                case "hospital": return "Building_Hospital";
                case "archive": return "Building_ArchiveMuseum";
                case "exchange_house": return "Building_ExchangeHouse";
                case "barracks": return "Building_Barracks";
                case "assembly": return "Building_Council";
                default: return null;
            }
        }

        private static string BuildingArtwork(string id, Era era)
        {
            string artwork = BuildingArtwork(id);
            return string.IsNullOrEmpty(artwork) ? null : "Building_" + era + "_" + artwork.Substring("Building_".Length);
        }

        private static string ImprovementArtwork(ImprovementKind kind, Era era)
        {
            switch (kind)
            {
                case ImprovementKind.Farm: case ImprovementKind.IrrigatedFarm: return "Farm";
                case ImprovementKind.Forestry: return "Forestry";
                case ImprovementKind.Workshop: return BuildingArtwork("workshop", era);
                case ImprovementKind.Road: return "Improvement_Road";
                case ImprovementKind.Bridge: return "Improvement_Bridge";
                case ImprovementKind.Market: return BuildingArtwork("market", era);
                case ImprovementKind.Fishery: return "Terrain_Coast";
                default: return null;
            }
        }

        private static string TerrainArtwork(TerrainKind terrain)
        {
            switch (terrain)
            {
                case TerrainKind.Plains: return "Terrain_Plains";
                case TerrainKind.Meadow: return "Terrain_Meadow";
                case TerrainKind.Forest: return "Terrain_Forest";
                case TerrainKind.Hills: return "Terrain_Hills";
                case TerrainKind.Mountain: return "Terrain_Mountain";
                case TerrainKind.River: return "Terrain_River";
                case TerrainKind.Coast: return "Terrain_Coast";
                case TerrainKind.Arid: return "Terrain_Arid";
                case TerrainKind.Swamp: return "Terrain_Swamp";
                case TerrainKind.Water: return "Terrain_Water";
                default: return null;
            }
        }

        private static string ResourceArtwork(ResourceKind resource)
        {
            switch (resource)
            {
                case ResourceKind.Iron: return "Resource_Iron";
                case ResourceKind.Herbs: return "Resource_Herbs";
                case ResourceKind.Salt: return "Resource_Salt";
                case ResourceKind.FertileSoil: return "Resource_FertileSoil";
                default: return null;
            }
        }

        private static string UnitArtwork(UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Militia: return "Unit_Militia";
                case UnitKind.FieldForce: return "Unit_FieldForce";
                case UnitKind.SiegeTeam: return "Unit_SiegeTeam";
                case UnitKind.SettlerWagon: return "Unit_Settler";
                default: return null;
            }
        }

        private static string UnitArtwork(UnitKind kind, Era era)
        {
            string artwork = UnitArtwork(kind);
            return string.IsNullOrEmpty(artwork) ? null : "Unit_" + era + "_" + artwork.Substring("Unit_".Length);
        }

        private static void AddButtonArtwork(Button button, string name)
        {
            Sprite sprite = LoadArtwork(name);
            if (button == null || sprite == null) return;
            GameObject art = UiKit.CreateObject("ButtonArtwork_" + name, button.transform);
            Image image = art.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            RectTransform rect = UiKit.Rect(art);
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(43, 43);
            rect.anchoredPosition = new Vector2(28, 0);
            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.alignment = TextAnchor.MiddleLeft;
                label.raycastTarget = false;
                label.rectTransform.offsetMin = new Vector2(60, 5);
                label.rectTransform.offsetMax = new Vector2(-10, -5);
            }
        }

        private void ShowTutorial()
        {
            PrepareUtilityOverlay();
            activeUtilityScreen = "tutorial";
            GameObject tutorial = UiKit.ScrollView(menuHost, "TutorialScroll");
            UiKit.Vertical(tutorial, 24, 16);
            UiKit.Label(tutorial.transform, "ПЕРВЫЕ ШАГИ", 46, GamePalette.Gold, TextAnchor.MiddleCenter, true);
            UiKit.Label(tutorial.transform,
                "1. В городе распределите работников: фермеры дают еду, ремесленники — производство, учёные — знания, торговцы — казну.\n\n" +
                "2. Выберите исследование, затем стройте открытые здания и улучшайте клетки вокруг города.\n\n" +
                "3. Растите без голода: запас еды кормит жителей, переполненный запас начинает рост.\n\n" +
                "4. Законы и договоры дают преимущества. Перейдите в дипломатию, чтобы торговать и договариваться.\n\n" +
                "5. Война проходит через кризис мобилизации. Она отнимает репутацию, доверие, торговлю и население. Захват не приносит отдельного пути к победе.\n\n" +
                "6. Откройте вкладку «Победа»: развивайтесь через качество жизни, науку, культуру, дипломатию или итоговое наследие к 70-му ходу.",
                25, GamePalette.Paper, TextAnchor.UpperLeft, false);
            UiKit.Button(tutorial.transform, "НАЗАД", ShowMenu, GamePalette.Teal, 82, 25);
        }

        private void CycleDifficulty()
        {
            selectedDifficulty = (Difficulty)(((int)selectedDifficulty + 1) % 4);
            ShowMenu();
        }

        private static string DifficultyName(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Calm: return "Спокойная";
                case Difficulty.Strong: return "Сильная";
                case Difficulty.Trial: return "Испытание";
                default: return "Стандартная";
            }
        }

        private void StartNewCampaign()
        {
            if (SaveService.HasSave)
            {
                ShowNewCampaignConfirmation();
                return;
            }
            CreateNewCampaign();
        }

        private void ShowNewCampaignConfirmation()
        {
            PrepareUtilityOverlay();
            activeUtilityScreen = "new_game";
            GameObject content = UiKit.ScrollView(menuHost, "NewCampaignConfirmation");
            UiKit.Label(content.transform, "НОВАЯ КАМПАНИЯ", 44, GamePalette.Gold, TextAnchor.MiddleCenter, true);
            UiKit.Label(content.transform, "Начать новую партию на сложности «" + DifficultyName(selectedDifficulty) + "»?\n\n" +
                "Текущие автосохранения останутся доступны в управлении слотами, но последующие ходы новой партии начнут вытеснять их по очереди. Ручное сохранение хранится отдельно.",
                23, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Button(content.transform, "НАЧАТЬ НОВУЮ КАМПАНИЮ", CreateNewCampaign, GamePalette.Teal, 78, 22);
            UiKit.Button(content.transform, "ОТКРЫТЬ СОХРАНЕНИЯ", ShowSaves, GamePalette.InkSoft, 66, 21);
            UiKit.Button(content.transform, "ОТМЕНА", ShowMenu, GamePalette.Rust, 66, 21);
        }

        private void CreateNewCampaign()
        {
            int seed = UnityEngine.Random.Range(100000, 999999);
            simulation = GameSimulation.NewCampaign(seed, selectedDifficulty);
            selectedCityId = simulation.FindCapital(simulation.State.HumanCivilizationId);
            selectedUnitId = -1;
            currentPage = ScreenPage.Overview;
            notice = "Новая кампания создана · мир " + seed + ".";
            SaveService.SaveAuto(simulation.State);
            menuHost.gameObject.SetActive(false);
            RenderGame();
        }

        private void LoadCampaign()
        {
            GameState saved = SaveService.LoadLatest();
            OpenSavedCampaign(saved, "Кампания");
        }

        private void RenderGame()
        {
            if (simulation == null) { ShowMenu(); return; }
            CaptureMapScrollPosition();
            AchievementDefinition[] earned = AchievementService.Evaluate(simulation.State);
            if (earned.Length > 0)
                notice = "Достижение «" + earned[0].Name + "» · получен титул «" + earned[0].TitleReward + "» и " + earned[0].RenownReward + " славы" +
                    (earned.Length > 1 ? " · ещё наград: " + (earned.Length - 1) : "") + ".";
            if (simulation.State.IsFinished) currentPage = ScreenPage.Victory;
            ApplySafeArea();
            menuHost.gameObject.SetActive(false);
            header.gameObject.SetActive(true);
            pageHost.gameObject.SetActive(true);
            navHost.gameObject.SetActive(true);
            actionHost.gameObject.SetActive(!simulation.State.IsFinished);
            UpdateHeader();
            DestroyChildren(pageHost);
            GameObject content = UiKit.ScrollView(pageHost, "PageScroll");
            switch (currentPage)
            {
                case ScreenPage.Overview: RenderOverview(content.transform); break;
                case ScreenPage.City: RenderCity(content.transform); break;
                case ScreenPage.Research: RenderResearch(content.transform); break;
                case ScreenPage.Diplomacy: RenderDiplomacy(content.transform); break;
                case ScreenPage.Laws: RenderLaws(content.transform); break;
                case ScreenPage.Events: RenderEvents(content.transform); break;
                case ScreenPage.Victory: RenderVictory(content.transform); break;
                case ScreenPage.Chronicle: RenderChronicle(content.transform); break;
            }
        }

        private void UpdateHeader()
        {
            GameState state = simulation.State;
            CivilizationState player = state.Civilization(state.HumanCivilizationId);
            titleLabel.text = "JERICHO     ·     ХОД " + state.Turn + "/" + state.MaximumTurn + "     ·     " + EraName(player.Era);
            resourceLabel.text = player.Name + " · " + AchievementService.SelectedTitle + "     |     Казна " + Format(player.TreasuryTenths) +
                "     ·     Знания " + Format(player.KnowledgeTenths) +
                "     ·     Влияние " + Format(player.InfluenceTenths) +
                "     ·     Культура " + player.CulturePrestige;
            noticeLabel.text = notice;
            noticeLabel.color = notice.IndexOf("не", StringComparison.OrdinalIgnoreCase) >= 0 || notice.IndexOf("нехват", StringComparison.OrdinalIgnoreCase) >= 0
                ? GamePalette.Danger : GamePalette.Muted;
        }

        private void RenderOverview(Transform parent)
        {
            GameState state = simulation.State;
            UiKit.Label(parent, "Карта государства", 32, GamePalette.Gold, TextAnchor.MiddleLeft, true);
            AddMap(parent);
            CityState selected = SelectedCity();
            if (selected != null)
            {
                GameObject card = UiKit.Card(parent, selected.Name + " · город " + selected.Id);
                UiKit.Label(card.transform, "Население " + Format(selected.PopulationTenths) + " / " + Format(selected.HousingTenths) +
                    "     Еда " + Format(selected.FoodTenths) + "     Здоровье " + selected.Health + "     Доверие " + selected.Trust,
                    22, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                CityYield yield = simulation.PreviewCity(selected.Id);
                UiKit.Label(card.transform, "За ход: еда " + Format(yield.FoodTenths) + " · производство " + yield.Production +
                    " · знания " + Format(yield.KnowledgeTenths) + " · казна " + Format(yield.TreasuryTenths), 21, GamePalette.Muted, TextAnchor.MiddleLeft, false);
                UiKit.Button(card.transform, "УПРАВЛЯТЬ ГОРОДОМ", delegate { currentPage = ScreenPage.City; RenderGame(); }, GamePalette.Ink, 62, 22);
            }
            RenderPendingAlerts(parent);
            RenderGlobalCrisis(parent);
            RenderUnits(parent);
            GameObject civCard = UiKit.Card(parent, "Соседи и развитие мира");
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState civ = state.Civilizations[i];
                if (civ.Id == state.HumanCivilizationId) continue;
                UiKit.Label(civCard.transform, civ.Name + " · " + civ.Archetype + " · репутация " + civ.Reputation +
                    " · отношение к вам " + civ.Relations[playerId(state)] + " · городов " + CountCities(state, civ.Id),
                    20, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);
            }
            UiKit.Button(civCard.transform, "ОТКРЫТЬ ДИПЛОМАТИЮ", delegate { currentPage = ScreenPage.Diplomacy; RenderGame(); }, GamePalette.Ink, 60, 22);
        }

        private static int playerId(GameState state) { return state.HumanCivilizationId; }

        private void AddMap(Transform parent)
        {
            GameObject card = UiKit.Card(parent, "Территории · нажмите на гекс, чтобы выбрать город, клетку или отряд", 688);
            GameObject scrollObject = UiKit.CreateObject("MapScroll", card.transform);
            LayoutElement mapHeight = scrollObject.AddComponent<LayoutElement>();
            mapHeight.minHeight = 610;
            mapHeight.preferredHeight = 610;
            mapHeight.flexibleWidth = 1f;
            ScrollRect scroll = scrollObject.AddComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48f;
            GameObject viewport = UiKit.CreateObject("MapViewport", scrollObject.transform);
            RectTransform viewportRect = UiKit.Rect(viewport);
            UiKit.SetAnchors(viewportRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image background = viewport.AddComponent<Image>();
            background.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.8f);
            viewport.AddComponent<RectMask2D>();
            GameObject content = UiKit.CreateObject("HexContent", viewport.transform);
            RectTransform contentRect = UiKit.Rect(content);
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(0, 1);
            contentRect.pivot = new Vector2(0, 1);
            contentRect.anchoredPosition = Vector2.zero;
            float safeWidth = Screen.width / Mathf.Max(0.1f, canvas.scaleFactor);
            float contentWidth = Mathf.Max(1060f, safeWidth - 96f);
            contentRect.sizeDelta = new Vector2(contentWidth, 650);
            contentRect.anchoredPosition = new Vector2((contentWidth - 1060f) * 0.5f, 0f);
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.normalizedPosition = mapScrollPosition;
            GameState state = simulation.State;
            int humanMask = 1 << state.HumanCivilizationId;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell cell = state.Cells[i];
                CreateHex(content.transform, cell, humanMask);
            }
            UiKit.Label(card.transform, "⌂ город · ● войско · ◇ обоз · F ферма · T лес · = дорога · буквы отмечают ресурс. Цвет границ показывает владельца, тёмные клетки скрыты туманом.",
                18, GamePalette.Muted, TextAnchor.MiddleLeft, false);
        }

        private void CreateHex(Transform parent, HexCell cell, int humanMask)
        {
            GameObject tile = UiKit.CreateObject("Hex_" + cell.Q + "_" + cell.R, parent);
            RectTransform rect = UiKit.Rect(tile);
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(62, 56);
            rect.anchoredPosition = new Vector2(43 + cell.Q * 53 + cell.R * 26.5f, -34 - cell.R * 46);
            HexTileGraphic graphic = tile.AddComponent<HexTileGraphic>();
            bool explored = (cell.ExploredByMask & humanMask) != 0;
            bool visible = (cell.VisibleToMask & humanMask) != 0;
            graphic.color = explored ? TerrainColor(cell.Terrain) : new Color(0.15f, 0.22f, 0.22f, 1f);
            if (!visible && explored) graphic.color = Color.Lerp(graphic.color, GamePalette.Ink, 0.42f);
            Color ownerColor = OwnerColor(cell.OwnerId);
            if (cell.OwnerId >= 0 && explored) graphic.color = Color.Lerp(graphic.color, ownerColor, 0.24f);
            bool selected = (cell.Q == selectedQ && cell.R == selectedR);
            CityState city = CityAt(cell.Q, cell.R);
            if (selected || (city != null && city.Id == selectedCityId)) graphic.EdgeColor = GamePalette.Gold;
            else if (cell.OwnerId == simulation.State.HumanCivilizationId) graphic.EdgeColor = new Color(0.69f, 0.8f, 0.57f, 1f);
            else graphic.EdgeColor = new Color(0.12f, 0.18f, 0.17f, 1f);
            Button button = tile.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.transition = Selectable.Transition.None;
            int q = cell.Q, r = cell.R;
            button.onClick.AddListener(delegate { SelectHex(q, r); });
            if (explored) AddMapArtwork(tile.transform, TerrainArtwork(cell.Terrain), new Vector2(45, 39), new Vector2(0, -1), visible ? Color.white : new Color(0.55f, 0.62f, 0.58f, 0.9f));
            string marker = "";
            if (!explored) marker = "?";
            else if (city != null) marker = "⌂";
            else
            {
                UnitState unit = visible ? UnitAt(cell.Q, cell.R) : null;
                if (unit != null && unit.Id == movingUnitId) unit = null;
                if (unit != null) marker = unit.Kind == UnitKind.SettlerWagon ? "◇" : "●";
                else if (cell.Improvement != ImprovementKind.None && cell.Damage < 75) marker = ImprovementMark(cell.Improvement);
                else if (cell.Resource != ResourceKind.None) marker = ResourceMark(cell.Resource);
            }
            if (!string.IsNullOrEmpty(marker))
            {
                string artwork = null;
                if (explored && city != null) artwork = BuildingArtwork("council", CivilizationEra(city.OwnerId));
                else if (visible)
                {
                    UnitState unit = UnitAt(cell.Q, cell.R);
                    if (unit != null && unit.Id == movingUnitId) unit = null;
                    if (unit != null) artwork = UnitArtwork(unit.Kind, CivilizationEra(unit.OwnerId));
                    else if (cell.Improvement != ImprovementKind.None && cell.Damage < 75) artwork = ImprovementArtwork(cell.Improvement, CivilizationEra(cell.OwnerId));
                    else artwork = ResourceArtwork(cell.Resource);
                }
                Sprite sprite = LoadArtwork(artwork);
                if (sprite != null)
                {
                    AddMapArtwork(tile.transform, artwork, city != null ? new Vector2(38, 34) : new Vector2(27, 25),
                        city != null ? new Vector2(0, 3) : new Vector2(12, 8), Color.white);
                }
                else
                {
                    Text icon = UiKit.OverlayText(tile.transform, marker, city == null ? 22 : 25, city == null ? GamePalette.Paper : GamePalette.Gold, TextAnchor.MiddleCenter);
                    RectTransform iconRect = icon.rectTransform;
                    iconRect.anchorMin = Vector2.zero;
                    iconRect.anchorMax = Vector2.one;
                    iconRect.offsetMin = Vector2.zero;
                    iconRect.offsetMax = Vector2.zero;
                }
            }
            if (city != null && explored)
            {
                string cityTag = city.Name.Length <= 3 ? city.Name : city.Name.Substring(0, 3);
                Text cityLabel = UiKit.OverlayText(tile.transform, cityTag, 11, GamePalette.Paper, TextAnchor.LowerCenter);
                RectTransform labelRect = cityLabel.rectTransform;
                labelRect.anchorMin = new Vector2(0, 0);
                labelRect.anchorMax = new Vector2(1, 0.34f);
                labelRect.offsetMin = new Vector2(-5, 0);
                labelRect.offsetMax = new Vector2(5, 0);
            }
        }

        private static void AddMapArtwork(Transform parent, string name, Vector2 size, Vector2 position, Color tint)
        {
            Sprite sprite = LoadArtwork(name);
            if (sprite == null) return;
            GameObject art = UiKit.CreateObject("MapArtwork_" + name, parent);
            Image image = art.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = tint;
            image.raycastTarget = false;
            RectTransform rect = UiKit.Rect(art);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private Era CivilizationEra(int civilizationId)
        {
            CivilizationState civilization = simulation.State.Civilization(civilizationId);
            return civilization == null ? Era.Communities : civilization.Era;
        }

        private void SelectHex(int q, int r)
        {
            if (presentationBusy) return;
            selectedQ = q;
            selectedR = r;
            CityState city = CityAt(q, r);
            if (city != null && city.OwnerId == simulation.State.HumanCivilizationId)
            {
                selectedCityId = city.Id;
                selectedUnitId = -1;
                notice = "Выбран город «" + city.Name + "».";
                RenderGame();
                return;
            }
            UnitState ownUnit = UnitAt(q, r);
            if (ownUnit != null && ownUnit.OwnerId == simulation.State.HumanCivilizationId)
            {
                selectedUnitId = ownUnit.Id;
                notice = "Выбран отряд «" + RulesCatalog.Unit(ownUnit.Kind).NameRu + "».";
                RenderGame();
                return;
            }
            if (selectedUnitId >= 0)
            {
                UnitState moving = UnitById(selectedUnitId);
                Vector2 startScreen = moving == null ? Vector2.zero : HexScreenPosition(moving.Q, moving.R);
                CommandResult movement = simulation.Issue(new GameCommand { Kind = CommandKind.MoveUnit, ActorId = simulation.State.HumanCivilizationId, EntityId = selectedUnitId, Q = q, R = r });
                notice = movement.Message;
                if (movement.Accepted && moving != null && animationMode > 0 && currentPage == ScreenPage.Overview && startScreen != Vector2.zero)
                {
                    int movedId = moving.Id;
                    UnitKind movedKind = moving.Kind;
                    Era movedEra = CivilizationEra(moving.OwnerId);
                    movingUnitId = movedId;
                    selectedUnitId = -1;
                    RenderGame();
                    StartCoroutine(AnimateUnitMovement(movedId, movedKind, movedEra, startScreen, q, r));
                    return;
                }
                if (movement.Accepted) selectedUnitId = -1;
                RenderGame();
                return;
            }
            HexCell cell = simulation.State.Cell(q, r);
            notice = cell == null ? "Клетки здесь нет." : cell.OwnerId == simulation.State.HumanCivilizationId ? "Выбрана клетка. Откройте управление городом, чтобы назначить улучшение." : "Клетка выбрана.";
            RenderGame();
        }

        private Color TerrainColor(TerrainKind terrain)
        {
            switch (terrain)
            {
                case TerrainKind.Plains: return GamePalette.Hex("839A6B");
                case TerrainKind.Meadow: return GamePalette.Hex("A4B47A");
                case TerrainKind.Forest: return GamePalette.Hex("53775E");
                case TerrainKind.Hills: return GamePalette.Hex("A28B60");
                case TerrainKind.Mountain: return GamePalette.Hex("8D918B");
                case TerrainKind.River: return GamePalette.Hex("5D9AA0");
                case TerrainKind.Coast: return GamePalette.Hex("75AEB0");
                case TerrainKind.Arid: return GamePalette.Hex("C0A06E");
                case TerrainKind.Swamp: return GamePalette.Hex("6F8174");
                case TerrainKind.Water: return GamePalette.Hex("467C91");
                default: return GamePalette.Ink;
            }
        }

        private Color OwnerColor(int ownerId)
        {
            switch (ownerId)
            {
                case 0: return GamePalette.Gold;
                case 1: return GamePalette.Teal;
                case 2: return GamePalette.Rust;
                case 3: return GamePalette.Sage;
                default: return Color.white;
            }
        }

        private static string ImprovementMark(ImprovementKind improvement)
        {
            switch (improvement)
            {
                case ImprovementKind.Farm: return "F";
                case ImprovementKind.IrrigatedFarm: return "F+";
                case ImprovementKind.Forestry: return "T";
                case ImprovementKind.Workshop: return "W";
                case ImprovementKind.Road: return "=";
                case ImprovementKind.Bridge: return "╪";
                case ImprovementKind.Market: return "$";
                case ImprovementKind.Fishery: return "~";
                default: return "·";
            }
        }

        private static string ResourceMark(ResourceKind resource)
        {
            switch (resource)
            {
                case ResourceKind.Iron: return "I";
                case ResourceKind.Herbs: return "H";
                case ResourceKind.Salt: return "S";
                case ResourceKind.FertileSoil: return "+";
                default: return "";
            }
        }

        private CityState CityAt(int q, int r)
        {
            for (int i = 0; i < simulation.State.Cities.Length; i++)
                if (simulation.State.Cities[i].Q == q && simulation.State.Cities[i].R == r) return simulation.State.Cities[i];
            return null;
        }

        private UnitState UnitAt(int q, int r)
        {
            for (int i = 0; i < simulation.State.Units.Length; i++)
                if (simulation.State.Units[i].Q == q && simulation.State.Units[i].R == r) return simulation.State.Units[i];
            return null;
        }

        private CityState SelectedCity()
        {
            CityState city = simulation.State.City(selectedCityId);
            if (city != null && city.OwnerId == simulation.State.HumanCivilizationId) return city;
            int capital = simulation.FindCapital(simulation.State.HumanCivilizationId);
            selectedCityId = capital;
            return simulation.State.City(capital);
        }

        private void RenderPendingAlerts(Transform parent)
        {
            int pending = 0;
            for (int i = 0; i < simulation.State.Events.Length; i++)
                if (!simulation.State.Events[i].IsResolved && simulation.State.Events[i].TargetCivilizationId == simulation.State.HumanCivilizationId) pending++;
            if (pending == 0) return;
            GameObject card = UiKit.Card(parent, "Нужно решение");
            UiKit.Label(card.transform, "Есть событие, которое требует вашего ответа. Некоторые последствия усиливаются, пока вы откладываете решение.", 22, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Button(card.transform, "ОТКРЫТЬ СОБЫТИЯ", delegate { currentPage = ScreenPage.Events; RenderGame(); }, GamePalette.Rust, 62, 22);
        }

        private void RenderGlobalCrisis(Transform parent)
        {
            CrisisState crisis = simulation.State.Crisis;
            if (crisis == null || crisis.Kind == CrisisKind.None || crisis.Resolved) return;
            GameObject card = UiKit.Card(parent, "Мировой кризис · угроза " + crisis.Threat);
            UiKit.Label(card.transform, crisis.PublicText + " Ход кризиса: " + crisis.TurnsActive + ". Любой вклад улучшает репутацию и снижает угрозу.", 22, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            GameObject row = UiKit.CreateObject("CrisisContributions", card.transform);
            UiKit.Horizontal(row, 0, 8);
            AddActionButton(row.transform, "ЕДА · 5", delegate { Issue(CommandKind.ContributeToCrisis, text: "food", value: 5); }, GamePalette.Teal, 62);
            AddActionButton(row.transform, "ЗНАНИЯ · 5", delegate { Issue(CommandKind.ContributeToCrisis, text: "knowledge", value: 5); }, GamePalette.Ink, 62);
            AddActionButton(row.transform, "ВЛИЯНИЕ · 5", delegate { Issue(CommandKind.ContributeToCrisis, text: "influence", value: 5); }, GamePalette.Rust, 62);
        }

        private void RenderUnits(Transform parent)
        {
            GameState state = simulation.State;
            int owner = state.HumanCivilizationId;
            UnitState selected = null;
            for (int i = 0; i < state.Units.Length; i++) if (state.Units[i].Id == selectedUnitId && state.Units[i].OwnerId == owner) selected = state.Units[i];
            if (selected != null)
            {
                UnitDefinition definition = RulesCatalog.Unit(selected.Kind);
                GameObject card = UiKit.Card(parent, "Выбран отряд · " + definition.NameRu);
                UiKit.Label(card.transform, "Боеспособность " + selected.Strength + " · готовность " + selected.Readiness + " · снабжение " + (selected.SupplyBasisPoints / 100) + "% · очки движения " + selected.MovementLeft,
                    22, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                if (selected.Kind == UnitKind.SettlerWagon)
                    UiKit.Button(card.transform, "ОСНОВАТЬ ГОРОД ЗДЕСЬ", delegate { Issue(CommandKind.FoundCity, entityId: selected.Id); selectedUnitId = -1; }, GamePalette.Gold, 66, 23);
            }
            GameObject unitsCard = UiKit.Card(parent, "Служба и экспедиции");
            int count = 0;
            for (int i = 0; i < state.Units.Length; i++)
            {
                UnitState unit = state.Units[i];
                if (unit.OwnerId != owner) continue;
                count++;
                UnitDefinition definition = RulesCatalog.Unit(unit.Kind);
                UiKit.Label(unitsCard.transform, definition.NameRu + " · " + unit.Strength + " силы · " + unit.MovementLeft + " движения · клетка " + unit.Q + "," + unit.R,
                    20, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);
                int id = unit.Id;
                UiKit.Button(unitsCard.transform, "ВЫБРАТЬ ОТРЯД", delegate { selectedUnitId = id; selectedQ = unit.Q; selectedR = unit.R; RenderGame(); }, GamePalette.Ink, 52, 18);
            }
            if (count == 0) UiKit.Label(unitsCard.transform, "Отрядов пока нет. Набор ополчения доступен в городе с казармами; обоз появляется при населении не менее 8.", 20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
        }

        private void AddActionButton(Transform parent, string label, Action action, Color color, float height = 58)
        {
            Button button = UiKit.Button(parent, label, action, color, height, 18);
            LayoutElement layout = button.GetComponent<LayoutElement>();
            layout.flexibleWidth = 1;
        }

        private void RenderCity(Transform parent)
        {
            CivilizationState owner = simulation.State.Civilization(simulation.State.HumanCivilizationId);
            CityState city = SelectedCity();
            if (city == null) return;
            GameObject selector = UiKit.Card(parent, "Ваши города");
            for (int i = 0; i < simulation.State.Cities.Length; i++)
            {
                CityState owned = simulation.State.Cities[i];
                if (owned.OwnerId != owner.Id) continue;
                int id = owned.Id;
                string label = owned.Name + " · население " + Format(owned.PopulationTenths) + (id == city.Id ? " · выбран" : "");
                UiKit.Button(selector.transform, label, delegate { selectedCityId = id; selectedQ = owned.Q; selectedR = owned.R; RenderGame(); }, id == city.Id ? GamePalette.Teal : GamePalette.Ink, 58, 20);
            }
            GameObject overview = UiKit.Card(parent, city.Name + " · управление");
            UiKit.Label(overview.transform, "Население " + Format(city.PopulationTenths) + "/" + Format(city.HousingTenths) +
                " · резерв работников " + Format(city.ReservedPopulationTenths) + " · еда " + Format(city.FoodTenths) +
                " · рост " + Format(city.GrowthTenths) + " · эффективность " + (city.EfficiencyBasisPoints / 100) + "%",
                22, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Label(overview.transform, "Здоровье " + city.Health + " · безопасность " + city.Safety + " · образование " + city.Education +
                " · доверие " + city.Trust + " · среда " + city.Environment + " · привлекательность " + city.Attraction,
                22, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Label(overview.transform, "Город развивается, когда запас еды покрывает текущее потребление. Дефицит снижает здоровье и доверие, затем вызывает отток и смерти.",
                20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            CityYield forecast = simulation.PreviewCity(city.Id);
            int foodBalance = forecast.FoodTenths - city.PopulationTenths;
            UiKit.Label(overview.transform, "Прогноз хода: еда " + Signed(foodBalance) + " (выпуск " + Format(forecast.FoodTenths) + " − потребление " + Format(city.PopulationTenths) +
                ") · казна города " + Signed(forecast.TreasuryTenths) + " после содержания " + Format(EconomyCalculator.CityUpkeep(simulation.State, city)) +
                " · производство " + forecast.Production + " · знания " + Format(forecast.KnowledgeTenths) + " · культура " + forecast.CulturePrestige,
                20, foodBalance < 0 || forecast.TreasuryTenths < 0 ? GamePalette.Danger : GamePalette.Sage, TextAnchor.MiddleLeft, true);
            RenderCityArchitecture(parent, city, owner);
            RenderWorkers(parent, city);
            RenderServiceControls(parent, city);
            RenderCityProject(parent, city);
            RenderBuildingChoices(parent, city, owner);
            RenderRecruitChoices(parent, city, owner);
            RenderImprovementChoices(parent, city, owner);
        }

        private void RenderCityArchitecture(Transform parent, CityState city, CivilizationState owner)
        {
            GameObject card = UiKit.Card(parent, "Архитектура · эпоха «" + EraName(owner.Era) + "»");
            GameObject center = UiKit.CreateObject("CityCenterArtwork", card.transform);
            UiKit.Horizontal(center, 8, 14);
            AddCityBuildingArtwork(center.transform, BuildingArtwork("council", owner.Era), 138);
            Text eraDescription = UiKit.Label(center.transform,
                "Центральное здание и новые постройки используют архитектуру текущей эпохи. При переходе эпохи ансамбль обновится на карте и в городе.",
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            eraDescription.GetComponent<LayoutElement>().flexibleWidth = 1f;

            int shown = 0;
            for (int i = 0; i < city.BuildingIds.Length; i++)
            {
                int level = i < city.BuildingLevels.Length ? city.BuildingLevels[i] : 0;
                if (level <= 0) continue;
                BuildingDefinition definition = RulesCatalog.Building(city.BuildingIds[i]);
                if (definition == null) continue;
                GameObject row = UiKit.CreateObject("BuiltBuilding_" + city.BuildingIds[i], card.transform);
                UiKit.Horizontal(row, 4, 14);
                AddCityBuildingArtwork(row.transform, BuildingArtwork(definition.Id, owner.Era), 82);
                Text buildingLabel = UiKit.Label(row.transform, definition.NameRu + " · уровень " + level, 19,
                    GamePalette.PaperDim, TextAnchor.MiddleLeft, true);
                buildingLabel.GetComponent<LayoutElement>().flexibleWidth = 1f;
                shown++;
            }
            if (shown == 0)
                UiKit.Label(card.transform, "Городской центр пока единственное завершённое здание.", 19, GamePalette.Muted, TextAnchor.MiddleLeft, false);
        }

        private static void AddCityBuildingArtwork(Transform parent, string name, float size)
        {
            Sprite sprite = LoadArtwork(name);
            if (sprite == null) return;
            GameObject art = UiKit.CreateObject("CityBuildingArtwork_" + name, parent);
            Image image = art.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            LayoutElement layout = art.AddComponent<LayoutElement>();
            layout.minWidth = size;
            layout.preferredWidth = size;
            layout.minHeight = size;
            layout.preferredHeight = size;
            layout.flexibleWidth = 0;
        }

        private void RenderWorkers(Transform parent, CityState city)
        {
            GameObject card = UiKit.Card(parent, "Работники · " + city.WorkerCount + " доступно");
            WorkerRole[] roles = { WorkerRole.Farmer, WorkerRole.Artisan, WorkerRole.Scholar, WorkerRole.Merchant, WorkerRole.Artist, WorkerRole.CivilServant };
            string[] names = { "Земледельцы · еда", "Ремесленники · производство", "Учёные · знания", "Торговцы · казна", "Художники · культура", "Служащие · услуги" };
            for (int i = 0; i < roles.Length; i++)
            {
                int roleIndex = i;
                GameObject row = UiKit.CreateObject("Worker_" + i, card.transform);
                UiKit.Horizontal(row, 0, 8);
                Text value = UiKit.Label(row.transform, names[i] + " · " + city.Workers[i], 20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                value.GetComponent<LayoutElement>().flexibleWidth = 1;
                int next = Math.Max(0, city.Workers[i] - 1);
                AddActionButton(row.transform, "−", delegate { Issue(CommandKind.SetWorkers, city.Id, value: roleIndex, targetId: next); }, GamePalette.Ink, 52);
                int countAssigned = 0;
                for (int w = 0; w < city.Workers.Length; w++) countAssigned += city.Workers[w];
                int increase = countAssigned < city.WorkerCount ? city.Workers[i] + 1 : city.Workers[i];
                Button plus = UiKit.Button(row.transform, "+", delegate { Issue(CommandKind.SetWorkers, city.Id, value: roleIndex, targetId: increase); }, GamePalette.Ink, 52, 22);
                plus.GetComponent<LayoutElement>().flexibleWidth = 0;
                plus.GetComponent<LayoutElement>().preferredWidth = 60;
                plus.interactable = countAssigned < city.WorkerCount;
            }
            UiKit.Label(card.transform, "Перераспределение вручную сохраняет число работников в остальных ролях. Автораспределение учитывает доступные рабочие места.", 19, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            GameObject focusRow = UiKit.CreateObject("WorkerFocus", card.transform);
            UiKit.Horizontal(focusRow, 0, 8);
            AddActionButton(focusRow.transform, "БАЛАНС", delegate { AutoAssign(city.Id, "balanced"); }, GamePalette.Teal);
            AddActionButton(focusRow.transform, "ЕДА", delegate { AutoAssign(city.Id, "food"); }, GamePalette.Ink);
            AddActionButton(focusRow.transform, "НАУКА", delegate { AutoAssign(city.Id, "science"); }, GamePalette.Ink);
            AddActionButton(focusRow.transform, "ТОРГОВЛЯ", delegate { AutoAssign(city.Id, "trade"); }, GamePalette.Ink);
        }

        private void AutoAssign(int cityId, string focus) { Issue(CommandKind.AutoAssignWorkers, cityId, text: focus); }

        private void RenderServiceControls(Transform parent, CityState city)
        {
            GameObject card = UiKit.Card(parent, "Городские службы · текущее содержание " + Format(EconomyCalculator.CityUpkeep(simulation.State, city)) + " казны за ход");
            string[] names = { "Здоровье", "Безопасность", "Образование" };
            for (int i = 0; i < 3; i++)
            {
                int service = i;
                GameObject row = UiKit.CreateObject("Service_" + i, card.transform);
                UiKit.Horizontal(row, 0, 8);
                Text name = UiKit.Label(row.transform, names[i] + " · " + SpendingName(city.ServiceFunding[i]), 20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                name.GetComponent<LayoutElement>().flexibleWidth = 1;
                AddActionButton(row.transform, "МЕНЬШЕ", delegate { SetSpending(city.Id, service, Math.Max(0, city.ServiceFunding[service] - 1)); }, GamePalette.Ink, 50);
                AddActionButton(row.transform, "БОЛЬШЕ", delegate { SetSpending(city.Id, service, Math.Min(2, city.ServiceFunding[service] + 1)); }, GamePalette.Teal, 50);
            }
            UiKit.Label(card.transform, "Низкий уровень бесплатен; стандартный и высокий стоят больше. Финансирование постепенно меняет городские показатели.", 19, GamePalette.Muted, TextAnchor.MiddleLeft, false);
        }

        private static string SpendingName(int funding)
        {
            return funding <= 0 ? "низкое" : funding == 1 ? "стандартное" : "высокое";
        }

        private void SetSpending(int cityId, int service, int funding) { Issue(CommandKind.SetSpending, cityId, value: service, targetId: funding); }

        private void RenderCityProject(Transform parent, CityState city)
        {
            CityProject project = city.Project ?? new CityProject();
            GameObject card = UiKit.Card(parent, "Строительство и активный проект");
            if (project.Kind == ProjectKind.None) UiKit.Label(card.transform, "Очередь пуста. Проект получает производство города в конце каждого хода.", 21, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            else
            {
                int percent = project.Cost <= 0 ? 0 : Math.Min(100, project.Progress * 100 / project.Cost);
                int production = Math.Max(1, simulation.PreviewCity(city.Id).Production);
                int turnsLeft = Math.Max(0, project.Cost - project.Progress + production - 1) / production;
                UiKit.Label(card.transform, ProjectName(project) + " · " + project.Progress + "/" + project.Cost + " ПП · " + percent + "% · примерно " + turnsLeft + " ход(а) до завершения",
                    22, GamePalette.Paper, TextAnchor.MiddleLeft, true);
                UiKit.Button(card.transform, "ОТМЕНИТЬ ПРОЕКТ · ВОЗВРАТ ПОЛОВИНЫ ВЛОЖЕННОЙ КАЗНЫ", delegate { Issue(CommandKind.SetProject, city.Id, text: "cancel"); }, GamePalette.Rust, 62, 18);
            }
        }

        private static string ProjectName(CityProject project)
        {
            if (project.Kind == ProjectKind.Building) { BuildingDefinition b = RulesCatalog.Building(project.TargetId); return b == null ? project.TargetId : b.NameRu; }
            if (project.Kind == ProjectKind.Recruitment) return "Набор: " + project.TargetId;
            if (project.Kind == ProjectKind.Improvement) return "Улучшение: " + project.TargetId;
            if (project.Kind == ProjectKind.EraTransition) return "Переход эпохи";
            if (project.Kind == ProjectKind.ScientificProject) return "Научная программа";
            return "Нет проекта";
        }

        private void RenderBuildingChoices(Transform parent, CityState city, CivilizationState owner)
        {
            GameObject card = UiKit.Card(parent, "Здания · производство " + simulation.PreviewCity(city.Id).Production + " ПП за ход");
            for (int i = 0; i < RulesCatalog.Buildings.Length; i++)
            {
                BuildingDefinition building = RulesCatalog.Buildings[i];
                if (building.Id == "legacy_quarter" || building.ProductionCost <= 0) continue;
                bool unlocked = string.IsNullOrEmpty(building.RequiredTechnology) || owner.Knows(building.RequiredTechnology);
                bool already = city.BuildingLevel(building.Id) > 0;
                string label = building.NameRu + " · " + building.ProductionCost + " ПП · содержание " + Format(building.UpkeepTenths) +
                    (unlocked ? "" : " · нужна технология «" + building.RequiredTechnology + "»") +
                    (already ? " · уже построено" : "");
                Button button = UiKit.Button(card.transform, label,
                    delegate { Issue(CommandKind.SetProject, city.Id, text: building.Id); }, already ? GamePalette.Ink : unlocked ? GamePalette.Teal : GamePalette.Ink, 60, 18);
                AddButtonArtwork(button, BuildingArtwork(building.Id, owner.Era));
                button.interactable = unlocked && (!already || building.Id == "house") &&
                    (!building.UniqueEmpireBuilding || CountEmpireBuilding(owner.Id, building.Id) == 0) &&
                    (city.Project == null || city.Project.Kind == ProjectKind.None);
            }
        }

        private void RenderRecruitChoices(Transform parent, CityState city, CivilizationState owner)
        {
            GameObject card = UiKit.Card(parent, "Служба и расширение территории");
            for (int i = 0; i < RulesCatalog.Units.Length; i++)
            {
                UnitDefinition unit = RulesCatalog.Units[i];
                bool tech = string.IsNullOrEmpty(unit.RequiredTechnology) || owner.Knows(unit.RequiredTechnology);
                bool building = string.IsNullOrEmpty(unit.RequiredBuilding) || city.BuildingLevel(unit.RequiredBuilding) > 0;
                string label = unit.NameRu + " · " + unit.ProductionCost + " ПП · " + Format(unit.TreasuryCostTenths) + " казны · " + Format(unit.PopulationTenths) + " населения";
                if (!tech) label += " · нужна технология " + unit.RequiredTechnology;
                if (!building) label += " · нужны " + unit.RequiredBuilding;
                UnitKind kind = unit.Kind;
                Button button = UiKit.Button(card.transform, label, delegate { Issue(CommandKind.Recruit, city.Id, value: (int)kind); }, unit.Kind == UnitKind.SettlerWagon ? GamePalette.Teal : GamePalette.Ink, 60, 18);
                AddButtonArtwork(button, UnitArtwork(unit.Kind, owner.Era));
                button.interactable = tech && building && (city.Project == null || city.Project.Kind == ProjectKind.None) &&
                    (unit.Kind != UnitKind.SettlerWagon || (city.PopulationTenths >= 80 && city.FoodTenths >= 60)) &&
                    (unit.Kind == UnitKind.SettlerWagon || (city.PopulationTenths >= unit.PopulationTenths + 40 && owner.TreasuryTenths >= unit.TreasuryCostTenths));
            }
            UiKit.Label(card.transform, "Набор переводит жителей из экономики в войско, резервирует население на время подготовки и добавляет содержание. Обоз стоит 6 еды и 2 населения.", 19, GamePalette.Muted, TextAnchor.MiddleLeft, false);
        }

        private void RenderImprovementChoices(Transform parent, CityState city, CivilizationState owner)
        {
            GameObject card = UiKit.Card(parent, "Улучшение клеток");
            HexCell selected = simulation.State.Cell(selectedQ, selectedR);
            if (selected == null || selected.OwnerId != owner.Id || HexGrid.Distance(selected.Q, selected.R, city.Q, city.R) > 2)
            {
                UiKit.Label(card.transform, "Выберите на карте принадлежащую городу клетку в пределах двух гексов.", 20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
                return;
            }
            UiKit.Label(card.transform, "Клетка " + selected.Q + "," + selected.R + " · " + TerrainName(selected.Terrain) + " · ресурс " + selected.Resource +
                " · улучшение " + selected.Improvement + " · повреждение " + selected.Damage + "%", 21, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            if (selected.Damage > 0)
            {
                UiKit.Button(card.transform, "ВОССТАНОВИТЬ ИНФРАСТРУКТУРУ · 4 ПП", delegate { Issue(CommandKind.Repair, city.Id, q: selected.Q, r: selected.R); }, GamePalette.Rust, 58, 20);
            }
            bool hasChoice = false;
            for (int i = 0; i < RulesCatalog.Improvements.Length; i++)
            {
                ImprovementDefinition improvement = RulesCatalog.Improvements[i];
                bool tech = owner.Knows(improvement.RequiredTechnology);
                bool terrain = Array.IndexOf(improvement.AllowedTerrain, selected.Terrain) >= 0;
                if (!tech || !terrain || selected.Improvement == improvement.Kind) continue;
                hasChoice = true;
                ImprovementKind kind = improvement.Kind;
                Button button = UiKit.Button(card.transform, improvement.NameRu + " · " + improvement.ProductionCost + " ПП · технология " + improvement.RequiredTechnology,
                    delegate { Issue(CommandKind.BuildImprovement, city.Id, value: (int)kind, q: selected.Q, r: selected.R); }, GamePalette.Ink, 56, 18);
                AddButtonArtwork(button, ImprovementArtwork(kind, owner.Era));
                button.interactable = city.Project == null || city.Project.Kind == ProjectKind.None;
            }
            if (!hasChoice) UiKit.Label(card.transform, "Нет доступных улучшений для этой клетки. Развивайте земледелие, дороги, инженерное дело или торговлю.", 19, GamePalette.Muted, TextAnchor.MiddleLeft, false);
        }

        private static string TerrainName(TerrainKind terrain)
        {
            switch (terrain)
            {
                case TerrainKind.Plains: return "равнина";
                case TerrainKind.Meadow: return "луг";
                case TerrainKind.Forest: return "лес";
                case TerrainKind.Hills: return "холмы";
                case TerrainKind.Mountain: return "горы";
                case TerrainKind.River: return "река";
                case TerrainKind.Coast: return "побережье";
                case TerrainKind.Arid: return "засушливая земля";
                case TerrainKind.Swamp: return "болото";
                case TerrainKind.Water: return "вода";
                default: return terrain.ToString();
            }
        }

        private int CountEmpireBuilding(int ownerId, string buildingId)
        {
            int count = 0;
            for (int i = 0; i < simulation.State.Cities.Length; i++)
                if (simulation.State.Cities[i].OwnerId == ownerId) count += simulation.State.Cities[i].BuildingLevel(buildingId);
            return count;
        }

        private void Issue(CommandKind kind, int entityId = 0, int targetId = 0, int value = 0, string text = "", int q = 0, int r = 0, int sequence = 0)
        {
            if (simulation == null || presentationBusy) return;
            CommandResult result = simulation.Issue(new GameCommand
            {
                Kind = kind, ActorId = simulation.State.HumanCivilizationId, EntityId = entityId,
                TargetId = targetId, Value = value, Text = text, Q = q, R = r, Sequence = sequence
            });
            notice = result.Message;
            RenderGame();
        }

        private void SetTax(TaxRate rate) { Issue(CommandKind.SetTax, value: (int)rate); }

        private void SaveManual()
        {
            if (simulation == null) return;
            SaveService.SaveManual(simulation.State);
            notice = "Кампания сохранена на этом устройстве.";
            if (menuHost.gameObject.activeSelf) ShowMenu(); else RenderGame();
        }

        private void EndTurn()
        {
            if (simulation == null || simulation.State.IsFinished || presentationBusy) return;
            if (animationMode == 0) { ResolveTurnNow(); return; }
            StartCoroutine(ResolveTurnWithPresentation());
        }

        private void ResolveTurnNow()
        {
            int previousTurn = simulation.State.Turn;
            simulation.EndTurn();
            if (simulation.State.Turn != previousTurn)
            {
                SaveService.SaveAuto(simulation.State);
                notice = "Ход " + previousTurn + " завершён. " + simulation.LastMessage;
            }
            else notice = simulation.LastMessage;
            if (simulation.State.IsFinished) currentPage = ScreenPage.Victory;
            else if (HasPendingEvent()) currentPage = ScreenPage.Events;
            else currentPage = ScreenPage.Overview;
            RenderGame();
        }

        private IEnumerator ResolveTurnWithPresentation()
        {
            presentationBusy = true;
            Era previousEra = simulation.State.Civilization(simulation.State.HumanCivilizationId).Era;
            int previousTurn = simulation.State.Turn;
            Dictionary<string, int> buildingLevels = CapturePlayerBuildingLevels();
            GameObject layer = CreatePresentationLayer();
            Text status;
            Image progress;
            RectTransform panel = CreatePresentationPanel(layer.transform, "ХОД " + previousTurn + " · РАЗРЕШЕНИЕ", out status, out progress);
            CanvasGroup transitionGroup = panel.gameObject.AddComponent<CanvasGroup>();
            yield return ScaleIn(panel, transitionGroup, 0.16f * AnimationScale);
            float duration = 0.45f * AnimationScale;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                if (progress != null) progress.fillAmount = Mathf.Clamp01(elapsed / duration);
                if (status != null) status.text = elapsed < duration * 0.34f ? "Сверяем приказы и перемещения…" : elapsed < duration * 0.68f ? "Считаем развитие и последствия…" : "Готовим летопись нового хода…";
                yield return null;
            }

            simulation.EndTurn();
            bool advanced = simulation.State.Turn != previousTurn;
            if (advanced) SaveService.SaveAuto(simulation.State);
            if (simulation.State.IsFinished) currentPage = ScreenPage.Victory;
            else if (HasPendingEvent()) currentPage = ScreenPage.Events;
            else currentPage = ScreenPage.Overview;
            notice = advanced ? "Ход " + previousTurn + " завершён. " + simulation.LastMessage : simulation.LastMessage;
            RenderGame();

            if (advanced)
            {
                Era nextEra = simulation.State.Civilization(simulation.State.HumanCivilizationId).Era;
                List<BuildingDefinition> completed = FindNewlyCompletedBuildings(buildingLevels);
                bool hasEraTransition = nextEra != previousEra;
                if (hasEraTransition || completed.Count > 0)
                {
                    dismissPresentationRequested = false;
                    ShowTurnMilestones(layer.transform, previousTurn, previousEra, nextEra, completed);
                    float remaining = animationMode == 2 ? 2.4f : 1.3f;
                    while (remaining > 0f && !dismissPresentationRequested)
                    {
                        remaining -= Time.unscaledDeltaTime;
                        yield return null;
                    }
                }
            }
            if (presentationLayer != null) Destroy(presentationLayer);
            presentationLayer = null;
            presentationBusy = false;
        }

        private Dictionary<string, int> CapturePlayerBuildingLevels()
        {
            Dictionary<string, int> result = new Dictionary<string, int>();
            int playerId = simulation.State.HumanCivilizationId;
            for (int i = 0; i < simulation.State.Cities.Length; i++)
            {
                CityState city = simulation.State.Cities[i];
                if (city.OwnerId != playerId) continue;
                for (int b = 0; b < city.BuildingIds.Length; b++)
                    result[city.Id + ":" + city.BuildingIds[b]] = b < city.BuildingLevels.Length ? city.BuildingLevels[b] : 0;
            }
            return result;
        }

        private List<BuildingDefinition> FindNewlyCompletedBuildings(Dictionary<string, int> previous)
        {
            List<BuildingDefinition> completed = new List<BuildingDefinition>();
            int playerId = simulation.State.HumanCivilizationId;
            for (int i = 0; i < simulation.State.Cities.Length; i++)
            {
                CityState city = simulation.State.Cities[i];
                if (city.OwnerId != playerId) continue;
                for (int b = 0; b < city.BuildingIds.Length; b++)
                {
                    string id = city.BuildingIds[b];
                    int current = b < city.BuildingLevels.Length ? city.BuildingLevels[b] : 0;
                    int old;
                    if (!previous.TryGetValue(city.Id + ":" + id, out old)) old = 0;
                    if (current > old)
                    {
                        BuildingDefinition definition = RulesCatalog.Building(id);
                        if (definition != null) completed.Add(definition);
                    }
                }
            }
            return completed;
        }

        private void ShowTurnMilestones(Transform layer, int completedTurn, Era oldEra, Era newEra, List<BuildingDefinition> buildings)
        {
            DestroyChildren(layer);
            Image blocker = layer.GetComponent<Image>();
            if (blocker != null) blocker.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.86f);
            Button backdrop = layer.GetComponent<Button>();
            if (backdrop == null) backdrop = layer.gameObject.AddComponent<Button>();
            backdrop.targetGraphic = blocker;
            backdrop.transition = Selectable.Transition.None;
            backdrop.onClick.AddListener(delegate { dismissPresentationRequested = true; });
            GameObject card = UiKit.Panel(layer, "TurnMilestones", GamePalette.InkSoft);
            RectTransform rect = UiKit.Rect(card);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(850, 780);
            UiKit.Vertical(card, 26, 18);
            CanvasGroup group = card.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            UiKit.Label(card.transform, "ЛЕТОПИСЬ · ХОД " + completedTurn, 34, GamePalette.Gold, TextAnchor.MiddleCenter, true);
            if (oldEra != newEra)
            {
                UiKit.Label(card.transform, "Новая эпоха: " + EraName(oldEra) + " → " + EraName(newEra), 27, GamePalette.Paper, TextAnchor.MiddleCenter, true);
                GameObject eras = UiKit.CreateObject("EraTransitionArt", card.transform);
                UiKit.Horizontal(eras, 8, 12);
                AddArtwork(eras.transform, BuildingArtwork("council", oldEra), 190);
                UiKit.Label(eras.transform, "→", 40, GamePalette.Gold, TextAnchor.MiddleCenter, true);
                AddArtwork(eras.transform, BuildingArtwork("council", newEra), 190);
            }
            if (buildings.Count > 0)
            {
                UiKit.Label(card.transform, buildings.Count == 1 ? "Завершено новое здание" : "Завершены новые здания", 25, GamePalette.Paper, TextAnchor.MiddleCenter, true);
                int count = Math.Min(buildings.Count, 4);
                for (int i = 0; i < count; i++)
                {
                    GameObject row = UiKit.CreateObject("CompletedBuilding_" + i, card.transform);
                    UiKit.Horizontal(row, 8, 12);
                    AddArtwork(row.transform, BuildingArtwork(buildings[i].Id, newEra), 98);
                    UiKit.Label(row.transform, buildings[i].NameRu, 23, GamePalette.Paper, TextAnchor.MiddleLeft, true);
                }
                if (buildings.Count > count) UiKit.Label(card.transform, "И ещё завершено зданий: " + (buildings.Count - count), 18, GamePalette.Muted, TextAnchor.MiddleCenter, false);
            }
            Text continueText = UiKit.Label(card.transform, "Коснитесь экрана, чтобы продолжить", 18, GamePalette.Muted, TextAnchor.MiddleCenter, false);
            Button dismiss = card.AddComponent<Button>();
            dismiss.targetGraphic = card.GetComponent<Image>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(delegate { dismissPresentationRequested = true; });
            continueText.raycastTarget = false;
            StartCoroutine(ScaleIn(card.transform as RectTransform, group, 0.2f * AnimationScale));
        }

        private GameObject CreatePresentationLayer()
        {
            GameObject layer = UiKit.CreateObject("PresentationLayer", safeRoot);
            UiKit.SetAnchors(UiKit.Rect(layer), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image blocker = layer.AddComponent<Image>();
            blocker.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.72f);
            blocker.raycastTarget = true;
            presentationLayer = layer;
            return layer;
        }

        private RectTransform CreatePresentationPanel(Transform layer, string title, out Text status, out Image progress)
        {
            GameObject panel = UiKit.Panel(layer, "TurnTransition", GamePalette.InkSoft);
            RectTransform rect = UiKit.Rect(panel);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(820, 400);
            UiKit.Vertical(panel, 28, 22);
            UiKit.Label(panel.transform, "JERICHO", 29, GamePalette.Gold, TextAnchor.MiddleCenter, true);
            UiKit.Label(panel.transform, title, 24, GamePalette.Paper, TextAnchor.MiddleCenter, true);
            status = UiKit.Label(panel.transform, "Сверяем приказы и перемещения…", 23, GamePalette.Muted, TextAnchor.MiddleCenter, false);
            GameObject trackObject = UiKit.CreateObject("ProgressTrack", panel.transform);
            LayoutElement trackLayout = trackObject.AddComponent<LayoutElement>();
            trackLayout.minHeight = 18; trackLayout.preferredHeight = 18;
            Image track = trackObject.AddComponent<Image>();
            track.color = GamePalette.Ink;
            GameObject fillObject = UiKit.CreateObject("ProgressFill", trackObject.transform);
            RectTransform fillRect = UiKit.Rect(fillObject);
            fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero; fillRect.offsetMax = Vector2.zero;
            progress = fillObject.AddComponent<Image>();
            progress.color = GamePalette.Gold;
            progress.type = Image.Type.Filled;
            progress.fillMethod = Image.FillMethod.Horizontal;
            progress.fillOrigin = (int)Image.OriginHorizontal.Left;
            progress.fillAmount = 0f;
            return rect;
        }

        private IEnumerator ScaleIn(RectTransform rect, CanvasGroup group, float duration)
        {
            if (rect == null || group == null) yield break;
            Vector3 from = Vector3.one * 0.9f;
            rect.localScale = from;
            float elapsed = 0f;
            while (elapsed < duration && rect != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                rect.localScale = Vector3.Lerp(from, Vector3.one, t);
                group.alpha = t;
                yield return null;
            }
            if (rect != null) rect.localScale = Vector3.one;
            if (group != null) group.alpha = 1f;
        }

        private IEnumerator AnimateUnitMovement(int unitId, UnitKind kind, Era era, Vector2 startScreen, int targetQ, int targetR)
        {
            presentationBusy = true;
            GameObject layer = CreatePresentationLayer();
            yield return new WaitForEndOfFrame();
            Canvas.ForceUpdateCanvases();
            Vector2 startLocal, targetLocal;
            RectTransform target = FindHex(targetQ, targetR);
            Vector2 targetScreen = target == null ? startScreen : RectScreenCenter(target);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(safeRoot, startScreen, null, out startLocal);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(safeRoot, targetScreen, null, out targetLocal);
            string artwork = UnitArtwork(kind, era);
            Sprite sprite = LoadArtwork(artwork);
            if (sprite != null)
            {
                GameObject mover = UiKit.CreateObject("MovingUnit_" + unitId, layer.transform);
                Image image = mover.AddComponent<Image>();
                image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false;
                RectTransform rect = UiKit.Rect(mover);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(kind == UnitKind.SettlerWagon ? 58 : 48, kind == UnitKind.SettlerWagon ? 50 : 46);
                float duration = (kind == UnitKind.SettlerWagon ? 0.8f : 0.58f) * AnimationScale;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    float eased = t * t * (3f - 2f * t);
                    rect.anchoredPosition = Vector2.Lerp(startLocal, targetLocal, eased) + Vector2.up * Mathf.Sin(t * Mathf.PI) * 28f;
                    rect.localScale = Vector3.one * (0.88f + 0.12f * Mathf.Sin(t * Mathf.PI));
                    yield return null;
                }
            }
            movingUnitId = -1;
            if (presentationLayer != null) Destroy(presentationLayer);
            presentationLayer = null;
            presentationBusy = false;
            RenderGame();
        }

        private RectTransform FindHex(int q, int r)
        {
            if (pageHost == null) return null;
            RectTransform[] rects = pageHost.GetComponentsInChildren<RectTransform>(true);
            string id = "Hex_" + q + "_" + r;
            for (int i = 0; i < rects.Length; i++) if (rects[i].name == id) return rects[i];
            return null;
        }

        private Vector2 HexScreenPosition(int q, int r)
        {
            RectTransform hex = FindHex(q, r);
            return hex == null ? Vector2.zero : RectScreenCenter(hex);
        }

        private static Vector2 RectScreenCenter(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector3 center = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
            return RectTransformUtility.WorldToScreenPoint(null, center);
        }

        private UnitState UnitById(int id)
        {
            for (int i = 0; i < simulation.State.Units.Length; i++) if (simulation.State.Units[i].Id == id) return simulation.State.Units[i];
            return null;
        }

        private void CaptureMapScrollPosition()
        {
            if (pageHost == null) return;
            RectTransform[] rects = pageHost.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < rects.Length; i++)
            {
                if (rects[i].name != "MapScroll") continue;
                ScrollRect scroll = rects[i].GetComponent<ScrollRect>();
                if (scroll != null) mapScrollPosition = scroll.normalizedPosition;
                return;
            }
        }

        private bool HasPendingEvent()
        {
            for (int i = 0; i < simulation.State.Events.Length; i++)
                if (!simulation.State.Events[i].IsResolved && simulation.State.Events[i].TargetCivilizationId == simulation.State.HumanCivilizationId) return true;
            return false;
        }

        private void RenderResearch(Transform parent)
        {
            CivilizationState owner = simulation.State.Civilization(simulation.State.HumanCivilizationId);
            GameObject summary = UiKit.Card(parent, "Наука · эпоха «" + EraName(owner.Era) + "»");
            TechnologyDefinition active = owner.ActiveResearchIndex >= 0 && owner.ActiveResearchIndex < RulesCatalog.Technologies.Length
                ? RulesCatalog.Technologies[owner.ActiveResearchIndex] : null;
            if (active == null) UiKit.Label(summary.transform, "Исследование не выбрано. Выберите доступную технологию ниже.", 21, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            else
            {
                int cost = active.Cost * 10 * (active.KeyTechnology ? 125 : 100) / 100;
                int progress = owner.TechnologyProgressTenths[owner.ActiveResearchIndex];
                UiKit.Label(summary.transform, "Сейчас: «" + active.NameRu + "» · " + Format(progress) + "/" + Format(cost) + " знаний · " +
                    (cost <= 0 ? 0 : progress * 100 / cost) + "%", 23, GamePalette.Paper, TextAnchor.MiddleLeft, true);
            }
            UiKit.Label(summary.transform, "Знания поступают от учёных и школ. Передовые эпохи открываются через общественный переход: минимум четыре изученные технологии текущей эпохи и 10 влияния.",
                20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            int eraKnowledge = CountTechnologiesInEra(owner, owner.Era);
            Button eraButton = UiKit.Button(summary.transform, "НАЧАТЬ ПЕРЕХОД ЭПОХИ · " + eraKnowledge + "/4 ТЕХНОЛОГИЙ · 10 ВЛИЯНИЯ",
                delegate { Issue(CommandKind.SetProject, SelectedCity().Id, text: "era_transition"); }, GamePalette.Gold, 70, 19);
            eraButton.interactable = (int)owner.Era < 4 && eraKnowledge >= 4 && owner.InfluenceTenths >= 100 &&
                (SelectedCity().Project == null || SelectedCity().Project.Kind == ProjectKind.None);

            for (int i = 0; i < RulesCatalog.Technologies.Length; i++)
            {
                TechnologyDefinition tech = RulesCatalog.Technologies[i];
                bool known = owner.Knows(tech.Id);
                string lockedReason = TechnologyBlockReason(owner, tech);
                bool available = !known && string.IsNullOrEmpty(lockedReason);
                string status = known ? "изучено" : available ? "доступно" : lockedReason;
                int cost = tech.Cost * 10 * (tech.KeyTechnology ? 125 : 100) / 100;
                int progress = owner.TechnologyProgressTenths[i];
                GameObject card = UiKit.Card(parent, tech.NameRu + (tech.KeyTechnology ? " · ключевая" : ""));
                UiKit.Label(card.transform, "Эпоха: " + EraName(tech.Era) + " · цена: " + Format(cost) + " знаний · " + status +
                    (progress > 0 ? " · накоплено " + Format(progress) : ""), 20, known ? GamePalette.Sage : GamePalette.Paper, TextAnchor.MiddleLeft, false);
                UiKit.Label(card.transform, "Открывает: " + tech.Unlock +
                    (tech.Prerequisites.Length > 0 ? "\nНужно: " + string.Join(" · ", tech.Prerequisites) : ""), 19, GamePalette.Muted, TextAnchor.MiddleLeft, false);
                Button choose = UiKit.Button(card.transform, known ? "УЖЕ ИЗУЧЕНО" : available ? "ВЫБРАТЬ ИССЛЕДОВАНИЕ" : "ЗАБЛОКИРОВАНО",
                    delegate { Issue(CommandKind.ChooseTechnology, text: tech.Id); }, available ? GamePalette.Teal : GamePalette.Ink, 56, 18);
                choose.interactable = available;
            }
        }

        private static string TechnologyBlockReason(CivilizationState civ, TechnologyDefinition tech)
        {
            if ((int)tech.Era > (int)civ.Era) return "требуется более поздняя эпоха";
            for (int i = 0; i < tech.Prerequisites.Length; i++)
            {
                string prerequisite = tech.Prerequisites[i];
                if (prerequisite.Contains("|"))
                {
                    string[] choices = prerequisite.Split('|');
                    if (!civ.Knows(choices[0]) && !civ.Knows(choices[1])) return "нет предварительной технологии";
                }
                else if (!civ.Knows(prerequisite)) return "нет предварительной технологии";
            }
            return "";
        }

        private static int CountTechnologiesInEra(CivilizationState civ, Era era)
        {
            int count = 0;
            for (int i = 0; i < civ.KnownTechnologies.Length; i++)
            {
                TechnologyDefinition tech = RulesCatalog.Technology(civ.KnownTechnologies[i]);
                if (tech != null && tech.Era == era) count++;
            }
            return count;
        }

        private void RenderDiplomacy(Transform parent)
        {
            GameState state = simulation.State;
            CivilizationState player = state.Civilization(state.HumanCivilizationId);
            GameObject fiscal = UiKit.Card(parent, "Доходы и налоги");
            UiKit.Label(fiscal.transform, "Ставка сейчас: " + TaxName(player.TaxRate) + " · каждый город получает больше казны от торговцев, но высокий налог снижает доверие и привлекательность.",
                21, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            GameObject taxRow = UiKit.CreateObject("TaxChoices", fiscal.transform);
            UiKit.Horizontal(taxRow, 0, 8);
            AddActionButton(taxRow.transform, "НИЗКИЙ", delegate { SetTax(TaxRate.Low); }, GamePalette.Teal, 58);
            AddActionButton(taxRow.transform, "ОБЫЧНЫЙ", delegate { SetTax(TaxRate.Normal); }, GamePalette.Ink, 58);
            AddActionButton(taxRow.transform, "ВЫСОКИЙ", delegate { SetTax(TaxRate.High); }, GamePalette.Rust, 58);

            GameObject agreements = UiKit.Card(parent, "Предложения и договоры");
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (!treaty.Pending || treaty.ToCivilizationId != player.Id) continue;
                CivilizationState from = state.Civilization(treaty.FromCivilizationId);
                GameObject offer = UiKit.Card(agreements.transform, (from == null ? "Сосед" : from.Name) + " предлагает: " + TreatyName(treaty.Kind), 0);
                UiKit.Button(offer.transform, "ПРИНЯТЬ", delegate { Issue(CommandKind.AcceptTreaty, treaty.Id); }, GamePalette.Teal, 54, 20);
            }
            if (agreements.transform.childCount == 1) UiKit.Label(agreements.transform, "Входящих предложений нет.", 20, GamePalette.Muted, TextAnchor.MiddleLeft, false);

            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState other = state.Civilizations[i];
                if (other.Id == player.Id) continue;
                CityState partnerCity = FirstCity(other.Id);
                ConflictState conflict = FindConflict(state, player.Id, other.Id);
                string status = conflict == null ? "мир" : ConflictPhaseName(conflict.Phase) + " · ход войны " + conflict.WarTurn;
                GameObject card = UiKit.Card(parent, other.Name + " · " + other.Archetype);
                UiKit.Label(card.transform, "Отношение " + player.Relations[other.Id] + " · доверие " + player.Trusts[other.Id] +
                    " · угроза " + player.Threats[other.Id] + " · репутация соседа " + other.Reputation + " · статус: " + status,
                    21, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                UiKit.Label(card.transform, "Договоры требуют влияния. Торговля безопасна, но разрыв маршрута теряет накопленные доходы и отношения.",
                    18, GamePalette.Muted, TextAnchor.MiddleLeft, false);
                GameObject row1 = UiKit.CreateObject("TreatyRow1", card.transform);
                UiKit.Horizontal(row1, 0, 8);
                AddActionButton(row1.transform, "ТОРГОВЛЯ", delegate { OfferTreaty(other.Id, TreatyKind.TradeRoute); }, GamePalette.Teal, 56);
                AddActionButton(row1.transform, "ОТКРЫТЫЕ ГРАНИЦЫ", delegate { OfferTreaty(other.Id, TreatyKind.OpenBorders); }, GamePalette.Ink, 56);
                GameObject row2 = UiKit.CreateObject("TreatyRow2", card.transform);
                UiKit.Horizontal(row2, 0, 8);
                AddActionButton(row2.transform, "НЕНАПАДЕНИЕ", delegate { OfferTreaty(other.Id, TreatyKind.NonAggression); }, GamePalette.Ink, 56);
                AddActionButton(row2.transform, "ОБМЕН ЗНАНИЯМИ", delegate { OfferTreaty(other.Id, TreatyKind.KnowledgeExchange); }, GamePalette.Ink, 56);
                if (conflict != null && conflict.Phase >= 0 && conflict.Phase <= 2)
                    UiKit.Button(card.transform, "ПРЕДЛОЖИТЬ ПЕРЕМИРИЕ · 5 КАЗНЫ ИЛИ 8 РЕПУТАЦИИ", delegate { Issue(CommandKind.MakePeace, targetId: other.Id); }, GamePalette.Rust, 58, 18);
                if (partnerCity != null && !other.IsDefeated)
                {
                    UiKit.Label(card.transform, "Торговый маршрут ведёт в город «" + partnerCity.Name + "».", 18, GamePalette.Muted, TextAnchor.MiddleLeft, false);
                }
            }
            GameObject war = UiKit.Card(parent, "Кризис мобилизации и война");
            UiKit.Label(war.transform, "Война не даёт обычного пути к победе. Она начинается через дипломатический кризис и мобилизацию, разрывает торговлю, снижает доверие, вызывает эвакуацию и портит инфраструктуру.",
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState target = state.Civilizations[i];
                if (target.Id == player.Id || target.IsDefeated || FindConflict(state, player.Id, target.Id) != null) continue;
                CityState capital = FirstCity(target.Id);
                if (capital == null) continue;
                int id = target.Id, q = capital.Q, r = capital.R;
                Button declare = UiKit.Button(war.transform, "КРИЗИС МОБИЛИЗАЦИИ ПРОТИВ " + target.Name.ToUpperInvariant() + " · 8 КАЗНЫ",
                    delegate { Issue(CommandKind.DeclareWar, targetId: id, q: q, r: r, text: "международный пограничный спор"); }, GamePalette.Danger, 64, 17);
                declare.interactable = player.TreasuryTenths >= 80;
            }
        }

        private void OfferTreaty(int targetId, TreatyKind kind)
        {
            CityState own = SelectedCity();
            CityState target = FirstCity(targetId);
            if (own == null || target == null) { notice = "Нужны города обеих сторон."; RenderGame(); return; }
            Issue(CommandKind.ProposeTreaty, own.Id, targetId, (int)kind, sequence: target.Id);
        }

        private CityState FirstCity(int ownerId)
        {
            for (int i = 0; i < simulation.State.Cities.Length; i++) if (simulation.State.Cities[i].OwnerId == ownerId) return simulation.State.Cities[i];
            return null;
        }

        private static string TreatyName(TreatyKind kind)
        {
            switch (kind)
            {
                case TreatyKind.TradeRoute: return "торговый маршрут";
                case TreatyKind.NonAggression: return "договор о ненападении";
                case TreatyKind.OpenBorders: return "открытые границы";
                case TreatyKind.KnowledgeExchange: return "обмен знаниями";
                case TreatyKind.Guarantee: return "гарантия безопасности";
                case TreatyKind.Embargo: return "эмбарго";
                case TreatyKind.Peace: return "перемирие";
                default: return "соглашение";
            }
        }

        private static string ConflictPhaseName(int phase)
        {
            switch (phase)
            {
                case 0: return "кризис";
                case 1: return "мобилизация";
                case 2: return "война";
                case 3: return "перемирие";
                case 4: return "восстановление";
                default: return "завершено";
            }
        }

        private static ConflictState FindConflict(GameState state, int first, int second)
        {
            for (int i = 0; i < state.Conflicts.Length; i++)
            {
                ConflictState conflict = state.Conflicts[i];
                if (conflict.Active && ((conflict.AggressorId == first && conflict.DefenderId == second) ||
                    (conflict.AggressorId == second && conflict.DefenderId == first))) return conflict;
            }
            return null;
        }

        private static string TaxName(TaxRate rate)
        {
            return rate == TaxRate.Low ? "низкая" : rate == TaxRate.High ? "высокая" : "обычная";
        }

        private void RenderLaws(Transform parent)
        {
            CivilizationState owner = simulation.State.Civilization(simulation.State.HumanCivilizationId);
            GameObject summary = UiKit.Card(parent, "Институты · влияние " + Format(owner.InfluenceTenths) + " · кулдаун " + owner.LawCooldownTurns + " ходов");
            UiKit.Label(summary.transform, "Один активный закон на категорию. Новая реформа заменяет старую и требует 5 влияния. Некоторые законы добавляют содержание городов или административную ёмкость.",
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            for (int i = 0; i < RulesCatalog.Laws.Length; i++)
            {
                LawDefinition law = RulesCatalog.Laws[i];
                bool era = (int)law.Era <= (int)owner.Era;
                bool active = owner.HasLaw(law.Id);
                GameObject card = UiKit.Card(parent, law.NameRu + " · " + law.Category);
                UiKit.Label(card.transform, law.DescriptionRu + " · эпоха " + EraName(law.Era) + " · содержание " + Format(law.UpkeepPerCityTenths) + " на город за ход.",
                    20, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);
                Button enact = UiKit.Button(card.transform, active ? "УЖЕ ДЕЙСТВУЕТ" : era ? "ПРИНЯТЬ РЕФОРМУ · 5 ВЛИЯНИЯ" : "ЗАБЛОКИРОВАНО ДО НОВОЙ ЭПОХИ",
                    delegate { Issue(CommandKind.EnactLaw, text: law.Id); }, active ? GamePalette.Ink : GamePalette.Teal, 58, 18);
                enact.interactable = era && !active && owner.InfluenceTenths >= 50 && owner.LawCooldownTurns == 0;
            }
        }

        private void RenderEvents(Transform parent)
        {
            GameState state = simulation.State;
            CivilizationState player = state.Civilization(state.HumanCivilizationId);
            GameObject crisis = UiKit.Card(parent, "Общие кризисы");
            if (state.Crisis != null && state.Crisis.Kind != CrisisKind.None && !state.Crisis.Resolved)
            {
                UiKit.Label(crisis.transform, state.Crisis.PublicText + " · угроза " + state.Crisis.Threat + " · ход " + state.Crisis.TurnsActive,
                    21, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                GameObject row = UiKit.CreateObject("CrisisResources", crisis.transform);
                UiKit.Horizontal(row, 0, 8);
                AddActionButton(row.transform, "ВНЕСТИ 5 ЕДЫ", delegate { Issue(CommandKind.ContributeToCrisis, text: "food", value: 5); }, GamePalette.Teal, 58);
                AddActionButton(row.transform, "ВНЕСТИ 5 ЗНАНИЙ", delegate { Issue(CommandKind.ContributeToCrisis, text: "knowledge", value: 5); }, GamePalette.Ink, 58);
                AddActionButton(row.transform, "ВНЕСТИ 5 ВЛИЯНИЯ", delegate { Issue(CommandKind.ContributeToCrisis, text: "influence", value: 5); }, GamePalette.Rust, 58);
            }
            else UiKit.Label(crisis.transform, state.HasResolvedGlobalCrisis ? "Последний мировой кризис преодолён. Совместная помощь укрепила репутацию участвовавших цивилизаций." : "Мирового кризиса сейчас нет. Новые кризисы могут затронуть еду, здоровье и торговые пути.",
                20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            UiKit.Label(crisis.transform, "Доступно ресурсов: казна " + Format(player.TreasuryTenths) + " · знания " + Format(player.KnowledgeTenths) + " · влияние " + Format(player.InfluenceTenths),
                18, GamePalette.Muted, TextAnchor.MiddleLeft, false);

            int pending = 0;
            for (int i = 0; i < state.Events.Length; i++)
            {
                ActiveEventState active = state.Events[i];
                if (active.IsResolved || active.TargetCivilizationId != player.Id) continue;
                pending++;
                EventDefinition definition = EventDefinitionFor(active.Kind);
                CityState city = state.City(active.TargetCityId);
                CivilizationState other = active.OtherCivilizationId >= 0 ? state.Civilization(active.OtherCivilizationId) : null;
                GameObject card = UiKit.Card(parent, EventName(active.Kind) + " · " + (city == null ? "ваша цивилизация" : city.Name) + (other == null ? "" : " · спор с " + other.Name));
                UiKit.Label(card.transform, definition == null ? "Событие требует решения." : definition.TriggerRu, 21, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                UiKit.Label(card.transform, "Выберите один ответ. Каждый вариант меняет ресурсы или состояние города.", 18, GamePalette.Muted, TextAnchor.MiddleLeft, false);
                for (int c = 0; c < active.Choices.Length; c++)
                {
                    string choice = active.Choices[c];
                    Button button = UiKit.Button(card.transform, ChoiceName(choice), delegate { Issue(CommandKind.EventChoice, active.Id, text: choice); }, GamePalette.Ink, 58, 18);
                    button.interactable = ChoiceAvailable(active, choice, player, city);
                }
            }
            if (pending == 0)
            {
                GameObject quiet = UiKit.Card(parent, "Городские события");
                UiKit.Label(quiet.transform, "Новых событий, требующих вашего ответа, нет. Проблемы еды и здоровья повышают вероятность засухи или эпидемии; торговля, образование и конфликты открывают другие события.",
                    20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            }
        }

        private EventDefinition EventDefinitionFor(EventKind kind)
        {
            for (int i = 0; i < RulesCatalog.Events.Length; i++) if (RulesCatalog.Events[i].Kind == kind) return RulesCatalog.Events[i];
            return null;
        }

        private static string EventName(EventKind kind)
        {
            for (int i = 0; i < RulesCatalog.Events.Length; i++) if (RulesCatalog.Events[i].Kind == kind) return RulesCatalog.Events[i].NameRu;
            return kind.ToString();
        }

        private static string ChoiceName(string choice)
        {
            switch (choice)
            {
                case "BuyFood": return "Закупить еду · 5 казны";
                case "ReassignWorkers": return "Перевести работников на поля";
                case "Rationing": return "Ввести нормирование · снизить доверие";
                case "OpenClinic": return "Открыть временную клинику · 10 казны";
                case "Quarantine": return "Карантин · здоровье вверх, торговля вниз";
                case "RequestAid": return "Запросить соседскую помощь";
                case "BuildHub": return "Построить дорожный узел · 6 казны";
                case "RestrictTraffic": return "Ограничить движение · снизить привлекательность";
                case "AcceptDelay": return "Принять задержку · снизить выпуск";
                case "Accept": return "Принять семьи · 4 казны";
                case "OpenCorridor": return "Открыть коридор · 2 влияния";
                case "Refuse": return "Отказать";
                case "Investigate": return "Провести расследование · 5 казны";
                case "Audit": return "Аудит · 2 влияния";
                case "Ignore": return "Не вмешиваться";
                case "Filters": return "Установить фильтры · 6 казны";
                case "ReduceOutput": return "Сократить выпуск";
                case "FundSchools": return "Финансировать школы · 8 казны";
                case "Experiment": return "Проверить новую программу · 2 влияния";
                case "KeepBudget": return "Оставить бюджет без изменений";
                case "Negotiate": return "Начать переговоры";
                case "ExchangeLand": return "Обменяться землями · 2 влияния";
                case "Ultimatum": return "Выдвинуть ультиматум соседу";
                case "EmergencyStock": return "Открыть резервы · 6 казны";
                case "NewRoute": return "Искать новые торговые связи";
                case "DomesticSupply": return "Начать строительство амбара";
                case "Festival": return "Провести фестиваль · 4 казны";
                case "JointMuseum": return "Поддержать общий музей · 2 влияния";
                case "CloseExchange": return "Ограничить обмен";
                case "PaySupport": return "Выплатить поддержку · 8 казны";
                case "LocalTruce": return "Попросить о местном перемирии";
                case "Continue": return "Продолжить мобилизацию";
                case "JointProject": return "Общий научный проект · 10 знаний";
                case "NationalProject": return "Развивать собственную программу";
                case "ShareKnowledge": return "Передать знания соседям · 5 знаний";
                default: return choice;
            }
        }

        private static bool ChoiceAvailable(ActiveEventState active, string choice, CivilizationState player, CityState city)
        {
            if (choice == "BuyFood") return player.TreasuryTenths >= 50;
            if (choice == "OpenClinic") return player.TreasuryTenths >= 100;
            if (choice == "BuildHub" || choice == "Filters") return player.TreasuryTenths >= 60;
            if (choice == "Accept") return player.TreasuryTenths >= 40 && city != null && city.PopulationTenths + 10 <= city.HousingTenths;
            if (choice == "OpenCorridor" || choice == "Audit" || choice == "Experiment" || choice == "JointMuseum") return player.InfluenceTenths >= 20;
            if (choice == "Investigate") return player.TreasuryTenths >= 50;
            if (choice == "FundSchools") return player.TreasuryTenths >= 80;
            if (choice == "ExchangeLand") return player.InfluenceTenths >= 20;
            if (choice == "EmergencyStock" || choice == "Festival") return player.TreasuryTenths >= (choice == "Festival" ? 40 : 60);
            if (choice == "PaySupport") return player.TreasuryTenths >= 80;
            if (choice == "JointProject") return player.KnowledgeTenths >= 100;
            if (choice == "ShareKnowledge") return player.KnowledgeTenths >= 50;
            if (choice == "DomesticSupply") return city != null && city.Project != null && city.Project.Kind == ProjectKind.None;
            return true;
        }

        private void RenderVictory(Transform parent)
        {
            GameState state = simulation.State;
            CivilizationState player = state.Civilization(state.HumanCivilizationId);
            if (state.IsFinished)
            {
                CivilizationState winner = state.Civilization(state.WinnerCivilizationId);
                GameObject final = UiKit.Card(parent, winner == null ? "Кампания завершена" : winner.Name + " победила");
                UiKit.Label(final.transform, "Путь: " + VictoryEvaluator.PathName(state.WinningPath) + "\n" +
                    (winner != null && winner.Id == player.Id ? "Вы завершили кампанию победой." : "Победила другая цивилизация. Вы можете изучить итоги в летописи."),
                    28, GamePalette.Gold, TextAnchor.MiddleLeft, true);
                UiKit.Label(final.transform, "Итоговый счёт наследия: " + VictoryEvaluator.HeritageScore(state, player.Id) + " · население " + Format(simulation.CivilizationPopulationTenths(player.Id)) +
                    " · технологии " + player.CompletedTechnologies + " · культура " + player.CulturePrestige + " · репутация " + player.Reputation,
                    22, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                UiKit.Button(final.transform, "ПОСМОТРЕТЬ ДОСТИЖЕНИЯ И НАГРАДЫ", ShowAchievements, GamePalette.InkSoft, 68, 20);
                UiKit.Button(final.transform, "В ГЛАВНОЕ МЕНЮ", delegate { simulation = null; ShowMenu(); }, GamePalette.Teal, 78, 24);
                return;
            }

            GameObject scoreCard = UiKit.Card(parent, "Пути к победе · ход " + state.Turn + " из " + state.MaximumTurn);
            UiKit.Label(scoreCard.transform, "Захват территории не является условием победы. Пять путей оценивают устойчивость, знания, культуру, сотрудничество и долгосрочное развитие.",
                21, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Label(scoreCard.transform, "Ваш счёт наследия: " + VictoryEvaluator.HeritageScore(state, player.Id) + " · мировой кризис преодолён: " + (state.HasResolvedGlobalCrisis ? "да" : "нет"),
                21, GamePalette.Gold, TextAnchor.MiddleLeft, true);

            int qualityCities = 0;
            int health = 0, safety = 0, education = 0, trust = 0, environment = 0;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != player.Id) continue;
                qualityCities++;
                health += city.Health;
                safety += city.Safety;
                education += city.Education;
                trust += city.Trust;
                environment += city.Environment;
            }
            GameObject quality = UiKit.Card(parent, "Благополучие · пассивный путь");
            UiKit.Label(quality.transform, "Удерживать 5 ходов: не менее 3 городов, средние здоровье ≥72, безопасность ≥70, образование ≥55, доверие ≥70 и среда ≥75. Нельзя воевать или иметь дефицит казны.",
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Label(quality.transform, "Города " + qualityCities + " · здоровье " + Average(health, qualityCities) + " · безопасность " + Average(safety, qualityCities) +
                " · образование " + Average(education, qualityCities) + " · доверие " + Average(trust, qualityCities) + " · среда " + Average(environment, qualityCities) +
                " · спокойные ходы " + player.QualityVictoryTurns + "/5", 21, GamePalette.Sage, TextAnchor.MiddleLeft, true);

            int universities = CountEmpireBuilding(player.Id, "university");
            int academies = CountEmpireBuilding(player.Id, "academy");
            bool scienceReady = player.Knows("computing_networks") && player.Knows("open_science") && universities >= 2 && academies >= 1 && player.KnowledgeTenths >= 300 && player.InfluenceTenths >= 80;
            GameObject science = UiKit.Card(parent, "Научный прорыв · активный путь");
            UiKit.Label(science.transform, "Изучите вычислительные сети и открытую науку; постройте 2 университета и академию; внесите 30 знаний и 8 влияния. После накопления 20 ПП программа проходит четыре этапа международного подтверждения.",
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Label(science.transform, "Университеты " + universities + "/2 · академии " + academies + "/1 · прогресс программы " + player.ScienceProjectTurns + "/4", 20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            Button scienceButton = UiKit.Button(science.transform, "НАЧАТЬ ПРОГРАММУ В ГОРОДЕ", delegate { Issue(CommandKind.BeginVictoryProject, SelectedCity().Id, value: (int)VictoryPath.Scientific); }, GamePalette.Teal, 62, 19);
            scienceButton.interactable = scienceReady && (SelectedCity().Project == null || SelectedCity().Project.Kind == ProjectKind.None);

            int culturalObjects = CountCulturalObjects(player.Id);
            GameObject culture = UiKit.Card(parent, "Культурное наследие · активный путь");
            UiKit.Label(culture.transform, "Нужно 100 культурного престижа и культурные центры в двух городах. Во время фестиваля не менее двух соседей должны иметь культурное влияние от 25.",
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Label(culture.transform, "Престиж " + player.CulturePrestige + "/100 · культурных объектов " + culturalObjects + " · влияние соседей " + CulturalPartners(player) + "/2 · фестиваль " + player.CultureFestivalTurns + "/2", 20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            Button cultureButton = UiKit.Button(culture.transform, "ПРОВЕСТИ ФЕСТИВАЛЬ · 20 ВЛИЯНИЯ И 10 КАЗНЫ",
                delegate { Issue(CommandKind.BeginVictoryProject, SelectedCity().Id, value: (int)VictoryPath.Cultural); }, GamePalette.Rust, 62, 18);
            cultureButton.interactable = player.CulturePrestige >= 100 && player.InfluenceTenths >= 200 && player.TreasuryTenths >= 100 && player.CultureFestivalTurns == 0;

            int routes = CountRoutes(player.Id);
            GameObject diplomatic = UiKit.Card(parent, "Международное сотрудничество · активный путь");
            UiKit.Label(diplomatic.transform, "Изучите международную ассамблею и постройте её. Преодолейте глобальный кризис, заключите два торговых маршрута, удержите репутацию 70 и доверие минимум двух партнёров.",
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Label(diplomatic.transform, "Торговые маршруты " + routes + "/2 · репутация " + player.Reputation + "/70 · кризис " + (state.HasResolvedGlobalCrisis ? "преодолён" : "не преодолён") +
                " · коалиция " + player.DiplomaticCoalitionTurns + "/2", 20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            bool diplomacyReady = player.Knows("international_assembly") && CountEmpireBuilding(player.Id, "assembly") > 0 && state.HasResolvedGlobalCrisis && routes >= 2 && player.Reputation >= 70;
            Button diplomaticButton = UiKit.Button(diplomatic.transform, "СОЗВАТЬ КОАЛИЦИЮ В АССАМБЛЕЕ", delegate { Issue(CommandKind.BeginVictoryProject, SelectedCity().Id, value: (int)VictoryPath.Diplomatic); }, GamePalette.Ink, 62, 18);
            diplomaticButton.interactable = diplomacyReady && player.DiplomaticCoalitionTurns == 0 && (SelectedCity().Project == null || SelectedCity().Project.Kind == ProjectKind.None);

            GameObject heritage = UiKit.Card(parent, "Наследие эпох · итоговый путь");
            UiKit.Label(heritage.transform, "Если никто не завершил другой путь, на 70-м ходу побеждает цивилизация с наибольшим счётом развития. Он учитывает население, качество городов, технологии, культуру, связи, репутацию и цену войны.",
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Label(heritage.transform, "Ваш счёт: " + VictoryEvaluator.HeritageScore(state, player.Id) + " · прогресс города: " + player.CompletedTechnologies + " технологий · " + CountCities(state, player.Id) + " городов · " + Format(simulation.CivilizationPopulationTenths(player.Id)) + " жителей.",
                21, GamePalette.Gold, TextAnchor.MiddleLeft, true);
            GameObject comparison = UiKit.Card(parent, "Счёт соседей");
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState civ = state.Civilizations[i];
                UiKit.Label(comparison.transform, civ.Name + " · " + VictoryEvaluator.HeritageScore(state, civ.Id) + " очков · репутация " + civ.Reputation +
                    " · технологии " + civ.CompletedTechnologies + " · военная нагрузка " + civ.WarStrain,
                    20, civ.Id == player.Id ? GamePalette.Gold : GamePalette.PaperDim, TextAnchor.MiddleLeft, civ.Id == player.Id);
            }
        }

        private int CountCulturalObjects(int ownerId)
        {
            int count = 0;
            for (int i = 0; i < simulation.State.Cities.Length; i++)
                if (simulation.State.Cities[i].OwnerId == ownerId) count += simulation.State.Cities[i].CompletedCulturalIds.Length;
            return count;
        }

        private static int CulturalPartners(CivilizationState civ)
        {
            int count = 0;
            for (int i = 0; i < civ.CulturalInfluence.Length; i++) if (i != civ.Id && civ.CulturalInfluence[i] >= 25) count++;
            return count;
        }

        private int CountRoutes(int ownerId)
        {
            int count = 0;
            for (int i = 0; i < simulation.State.Treaties.Length; i++)
            {
                TreatyState treaty = simulation.State.Treaties[i];
                if (treaty.Active && treaty.Kind == TreatyKind.TradeRoute && (treaty.FromCivilizationId == ownerId || treaty.ToCivilizationId == ownerId)) count++;
            }
            return count;
        }

        private void RenderChronicle(Transform parent)
        {
            GameState state = simulation.State;
            GameObject reportCard = UiKit.Card(parent, "Последние отчёты");
            if (state.Reports.Length == 0) UiKit.Label(reportCard.transform, "После первого завершённого хода появится отчёт: изменения еды, населения, казны, знаний, влияния и культуры.", 20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            for (int i = state.Reports.Length - 1; i >= 0; i--)
            {
                TurnReport report = state.Reports[i];
                GameObject card = UiKit.Card(reportCard.transform, "Ход " + report.Turn + " · итог");
                UiKit.Label(card.transform, "Еда " + Signed(report.FoodDeltaTenths) + " · население " + Signed(report.PopulationDeltaTenths) +
                    " · казна " + Signed(report.TreasuryDeltaTenths) + " · знания " + Signed(report.KnowledgeDeltaTenths) +
                    " · влияние " + Signed(report.InfluenceDeltaTenths) + " · культура " + Signed(report.CultureDelta),
                    20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                UiKit.Label(card.transform, "Смерти " + Format(report.DeathsTenths) + " · миграция " + Format(report.EmigrationTenths) + " · сражения " + report.BattleCount + " · договоры " + report.TreatyCount,
                    18, GamePalette.Muted, TextAnchor.MiddleLeft, false);
                for (int e = 0; e < report.Entries.Length; e++) UiKit.Label(card.transform, "• " + report.Entries[e], 18, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);
            }
            GameObject log = UiKit.Card(parent, "Летопись кампании · последние события");
            for (int i = state.EventLog.Length - 1; i >= 0; i--) UiKit.Label(log.transform, state.EventLog[i], 18, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);
        }

        private static int CountCities(GameState state, int ownerId)
        {
            int count = 0;
            for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].OwnerId == ownerId) count++;
            return count;
        }

        private static int Average(int total, int count) { return count <= 0 ? 0 : total / count; }
        private static string Format(int tenths) { return (tenths / 10.0).ToString("0.0"); }
        private static string Signed(int tenths) { return (tenths > 0 ? "+" : "") + Format(tenths); }

        private static string EraName(Era era)
        {
            switch (era)
            {
                case Era.Communities: return "общин";
                case Era.Cities: return "городов";
                case Era.ConnectedLands: return "связанных земель";
                case Era.Industry: return "индустрии";
                case Era.Synthesis: return "синтеза";
                default: return "неизвестной эпохи";
            }
        }

        private static void DestroyChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }
    }
}
