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
        private enum TutorialObjective { ViewCity, AssignWorkers, Research, CityProject, LandImprovement, RecruitSettler, ViewDiplomacy, ViewEvents, ViewVictory, ViewChronicle }
        private enum MapOverlay { Political, Food, Production, Defense }

        private const float MapHexWidth = 64f;
        private const float MapHexHeight = 58f;
        private const float MapHexQStep = 55f;
        private const float MapHexRStepX = 27.5f;
        private const float MapHexRStepY = 48.5f;

        private sealed class BuildingMilestone
        {
            public BuildingDefinition Definition;
            public string CityName;
        }

        private sealed class ImprovementMilestone
        {
            public ImprovementKind Kind;
            public int Q;
            public int R;
        }

        private sealed class UnitMovePresentation
        {
            public int UnitId;
            public int OwnerId;
            public UnitKind Kind;
            public int StartQ;
            public int StartR;
            public bool StartVisible;
            public int TargetQ;
            public int TargetR;
        }

        private sealed class TutorialStep
        {
            public readonly string Title;
            public readonly string Body;
            public readonly ScreenPage Page;
            public readonly TutorialObjective Objective;
            public TutorialStep(string title, string body, ScreenPage page, TutorialObjective objective)
            { Title = title; Body = body; Page = page; Objective = objective; }
        }

        private sealed class HexTileGesture : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
        {
            private Action longPressAction;
            private float pressedAt;
            private bool isPressed;
            private bool longPressFired;

            public void Initialize(Action action) { longPressAction = action; }
            public void OnPointerDown(PointerEventData eventData)
            {
                pressedAt = Time.unscaledTime;
                isPressed = true;
                longPressFired = false;
            }
            public void OnPointerUp(PointerEventData eventData)
            {
                if (isPressed && !longPressFired && Time.unscaledTime - pressedAt >= 0.58f)
                {
                    longPressFired = true;
                    if (longPressAction != null) longPressAction();
                }
                isPressed = false;
            }
            public void OnPointerExit(PointerEventData eventData) { isPressed = false; }
        }

        private static readonly TutorialStep[] TutorialSteps =
        {
            new TutorialStep("Проверьте город", "Откройте карточку столицы и сравните население, запас еды, здоровье и выпуск за ход. В этой игре прежде всего нужно удержать устойчивую жизнь города.", ScreenPage.City, TutorialObjective.ViewCity),
            new TutorialStep("Настройте работников", "Измените распределение хотя бы одного жителя между фермерами, ремесленниками и специалистами. Сравните прогноз выпуска до и после назначения.", ScreenPage.City, TutorialObjective.AssignWorkers),
            new TutorialStep("Выберите исследование", "Изучите технологию под ближайшую задачу: сельское хозяйство и медицина стабилизируют города, письменность и школы открывают дипломатические и научные решения.", ScreenPage.Research, TutorialObjective.Research),
            new TutorialStep("Поставьте городской проект", "Выберите доступное здание или общественный проект. Производство города накапливается в одной очереди; несколько проектов не строятся одновременно.", ScreenPage.City, TutorialObjective.CityProject),
            new TutorialStep("Улучшите свою землю", "Запустите улучшение принадлежащей городу клетки. Фермы, мастерские, парки и укрепления дают разные долгосрочные эффекты.", ScreenPage.City, TutorialObjective.LandImprovement),
            new TutorialStep("Расширяйтесь безопасно", "Начните набор поселенческого обоза. Он стоит еды и жителей: перед этим проверьте, что город сохранит рабочую силу, а путь к свободной территории открыт.", ScreenPage.City, TutorialObjective.RecruitSettler),
            new TutorialStep("Познакомьтесь с соседями", "Откройте дипломатию и сравните отношения, доверие и угрозу. Маршрут, гарантия, помощь и посредничество могут дать выгоду; война ухудшает положение обеих сторон.", ScreenPage.Diplomacy, TutorialObjective.ViewDiplomacy),
            new TutorialStep("Решайте события", "Откройте журнал событий и проверьте, как игра показывает цену и последствия решений. Если сейчас нет события, изучите карточки ожидающих решений и вернитесь позже.", ScreenPage.Events, TutorialObjective.ViewEvents),
            new TutorialStep("Выберите путь победы", "Сравните благополучие, науку, культуру и дипломатию. Подготовьте один путь заранее, но продолжайте поддерживать еду, казну и доверие.", ScreenPage.Victory, TutorialObjective.ViewVictory),
            new TutorialStep("Читайте итоги хода", "Откройте летопись и найдите отчёт хода: изменения еды, населения, казны, знаний, влияния и культуры. Сверьте его с прогнозом города.", ScreenPage.Chronicle, TutorialObjective.ViewChronicle)
        };

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
        private string utilityReturnScreen = "menu";
        private ScreenPage currentPage = ScreenPage.Overview;
        private int selectedCityId = -1;
        private int selectedUnitId = -1;
        private int selectedQ = -1;
        private int selectedR = -1;
        private AudioSource[] soundtracks = new AudioSource[2];
        private AudioSource soundEffects;
        private readonly Dictionary<string, AudioClip> soundEffectCache = new Dictionary<string, AudioClip>();
        private bool musicEnabled = true;
        private int musicVolumeStep = 1;
        private bool soundEffectsEnabled = true;
        private int soundEffectsVolumeStep = 1;
        private int activeSoundtrackIndex;
        private int currentMusicEra = -1;
        private Coroutine soundtrackTransition;
        private static readonly float[] MusicVolumeSteps = { 0.12f, 0.24f, 0.38f, 0.55f };
        private static readonly float[] SoundEffectsVolumeSteps = { 0.18f, 0.28f, 0.40f, 0.54f };
        private static readonly string[] MusicTrackNames =
        {
            "Nasledie_Theme", "Theme_Cities", "Theme_ConnectedLands", "Theme_Industry", "Theme_Synthesis"
        };
        private static readonly Dictionary<string, Sprite> Artwork = new Dictionary<string, Sprite>();
        private static bool artworkAtlasesLoaded;
        private Difficulty selectedDifficulty = Difficulty.Standard;
        private GamePace selectedPace = GamePace.Standard;
        private string selectedNationId = "jericho";
        private string guideSearchQuery = "";
        private int guideCategoryIndex;
        private int tutorialStepIndex;
        private int tutorialCompletedMask;
        private string tutorialFeedback = "";
        private MapOverlay mapOverlay;
        private int textScaleIndex = 1;
        private bool highContrast;
        private bool hapticsEnabled;
        private string notice = "";
        private Rect lastSafeArea;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private int animationMode = 1; // 0: off, 1: quick, 2: full
        private bool presentationBusy;
        private bool dismissPresentationRequested;
        private int movingUnitId = -1;
        private readonly HashSet<int> movingUnitIds = new HashSet<int>();
        private Vector2 mapScrollPosition = new Vector2(0.5f, 0.5f);
        private RectTransform activeMapContent;
        private RectTransform activeMapViewport;
        private ScrollRect activeMapScroll;
        private Text activeMapZoomLabel;
        private int activeMapMinQ;
        private int activeMapMinR;
        private float mapZoom = 1f;
        private float previousPinchDistance;
        private float lastHexTapTime = -1f;
        private int lastHexTapQ = -1;
        private int lastHexTapR = -1;
        private float longPressUntil;
        private int longPressQ = -1;
        private int longPressR = -1;
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
            soundEffectsEnabled = PlayerPrefs.GetInt("nasledie.sfx.enabled", 1) != 0;
            soundEffectsVolumeStep = Mathf.Clamp(PlayerPrefs.GetInt("nasledie.sfx.volume", 1), 0, SoundEffectsVolumeSteps.Length - 1);
            animationMode = Mathf.Clamp(PlayerPrefs.GetInt("jericho.animation.mode", 1), 0, 2);
            mapZoom = Mathf.Clamp(PlayerPrefs.GetFloat("jericho.map.zoom", 1f), 0.65f, 1.5f);
            mapOverlay = (MapOverlay)Mathf.Clamp(PlayerPrefs.GetInt("jericho.map.overlay", 0), 0, 3);
            textScaleIndex = Mathf.Clamp(PlayerPrefs.GetInt("jericho.accessibility.text", 1), 0, 3);
            highContrast = PlayerPrefs.GetInt("jericho.accessibility.contrast", 0) != 0;
            hapticsEnabled = PlayerPrefs.GetInt("jericho.accessibility.haptics", 1) != 0;
            tutorialCompletedMask = PlayerPrefs.GetInt("jericho.tutorial.completed", 0);
            UiKit.TextScaleIndex = textScaleIndex;
            UiKit.HighContrast = highContrast;
            selectedPace = (GamePace)Mathf.Clamp(PlayerPrefs.GetInt("jericho.campaign.pace", (int)GamePace.Standard), 0, 2);
            selectedNationId = PlayerPrefs.GetString("jericho.campaign.nation", "jericho");
            tutorialStepIndex = Mathf.Clamp(PlayerPrefs.GetInt("jericho.tutorial.step", 0), 0, TutorialSteps.Length);
            NationDefinition savedNation = NationCatalog.Get(selectedNationId);
            selectedNationId = savedNation == null ? "jericho" : savedNation.Id;
            EnsureEventSystem();
            BuildCanvas();
            InitializeSoundtrack();
            UiKit.ButtonClickHandler = PlayButtonClick;
            ShowMenu();
        }

        private void OnDestroy()
        {
            if (UiKit.ButtonClickHandler == PlayButtonClick) UiKit.ButtonClickHandler = null;
        }

        private void Update()
        {
            UpdateMapPinch();
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
            backgroundImage.sprite = Resources.Load<Sprite>("Art/Backgrounds/Menu_Background");
            backgroundImage.preserveAspect = false;
            backgroundImage.color = new Color(1f, 1f, 1f, 0.34f);
            backgroundImage.raycastTarget = false;
            UiKit.SetAnchors(UiKit.Rect(background), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            background.transform.SetAsFirstSibling();
            GameObject backgroundBase = UiKit.CreateObject("BackgroundBase", safeRoot);
            Image backgroundBaseImage = backgroundBase.AddComponent<Image>();
            backgroundBaseImage.color = GamePalette.Ink;
            backgroundBaseImage.raycastTarget = false;
            UiKit.SetAnchors(UiKit.Rect(backgroundBase), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            backgroundBase.transform.SetAsFirstSibling();

            GameObject headerObject = UiKit.Panel(safeRoot, "Header",
                new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.68f));
            header = UiKit.Rect(headerObject);
            UiKit.SetAnchors(header, new Vector2(0, 0.90f), Vector2.one, new Vector2(0, 0), Vector2.zero);
            UiKit.Vertical(headerObject, 8, 0);
            titleLabel = UiKit.Label(headerObject.transform, "JERICHO", 26, GamePalette.Gold, TextAnchor.MiddleLeft, true);
            LayoutElement titleLayout = titleLabel.GetComponent<LayoutElement>();
            titleLayout.minHeight = 30;
            titleLayout.preferredHeight = 32;
            resourceLabel = UiKit.Label(headerObject.transform, "", 14, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            LayoutElement resourceLayout = resourceLabel.GetComponent<LayoutElement>();
            resourceLayout.minHeight = 20;
            resourceLayout.preferredHeight = 22;
            noticeLabel = UiKit.Label(headerObject.transform, "", 13, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            LayoutElement noticeLayout = noticeLabel.GetComponent<LayoutElement>();
            noticeLayout.minHeight = 18;
            noticeLayout.preferredHeight = 18;

            GameObject pageObject = UiKit.CreateObject("PageHost", safeRoot);
            pageHost = UiKit.Rect(pageObject);
            UiKit.SetAnchors(pageHost, new Vector2(0, 0.15f), new Vector2(1, 0.90f), Vector2.zero, Vector2.zero);
            pageObject.SetActive(false);

            GameObject navObject = UiKit.Panel(safeRoot, "Navigation",
                new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.72f));
            navHost = UiKit.Rect(navObject);
            UiKit.SetAnchors(navHost, new Vector2(0, 0.075f), new Vector2(1, 0.15f), Vector2.zero, Vector2.zero);
            UiKit.Horizontal(navObject, 4, 4);
            AddNavButton(navObject.transform, "МИР", ScreenPage.Overview);
            AddNavButton(navObject.transform, "ГОРОД", ScreenPage.City);
            AddNavButton(navObject.transform, "НАУКА", ScreenPage.Research);
            AddNavButton(navObject.transform, "ДИПЛОМАТИЯ", ScreenPage.Diplomacy);
            AddNavButton(navObject.transform, "ЗАКОНЫ", ScreenPage.Laws);
            AddNavButton(navObject.transform, "СОБЫТИЯ", ScreenPage.Events);
            AddNavButton(navObject.transform, "ПОБЕДА", ScreenPage.Victory);
            AddNavButton(navObject.transform, "ЛЕТОПИСЬ", ScreenPage.Chronicle);

            GameObject actionObject = UiKit.Panel(safeRoot, "TurnActions",
                new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.78f));
            actionHost = UiKit.Rect(actionObject);
            UiKit.SetAnchors(actionHost, Vector2.zero, new Vector2(1, 0.075f), Vector2.zero, Vector2.zero);
            UiKit.Horizontal(actionObject, 4, 4);
            Button menuButton = UiKit.Button(actionObject.transform, "МЕНЮ", ShowMenu, GamePalette.Ink, 58, 14);
            Button saveButton = UiKit.Button(actionObject.transform, "СОХРАНИТЬ", SaveManual, GamePalette.Ink, 58, 14);
            Button achievementsButton = UiKit.Button(actionObject.transform, "ПОМОЩЬ", ShowPageHelp, GamePalette.Ink, 58, 14);
            Button endButton = UiKit.Button(actionObject.transform, "ЗАВЕРШИТЬ ХОД", EndTurn, GamePalette.Gold, 58, 15);
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
            bool selected = currentPage == page;
            Button button = UiKit.Button(parent, label, delegate { currentPage = page; MarkTutorialPageVisited(page); notice = ""; RenderGame(); },
                selected ? GamePalette.Gold : GamePalette.InkSoft, 52, 13);
            button.GetComponent<LayoutElement>().flexibleWidth = 1f;
        }

        private void ShowMenu()
        {
            activeUtilityScreen = "menu";
            DestroyChildren(menuHost);
            menuHost.gameObject.SetActive(true);
            SetUtilityBackground("Backgrounds/Menu_Background");
            CivilizationState menuPlayer = simulation == null ? null : simulation.State.Civilization(simulation.State.HumanCivilizationId);
            SetMusicEra(menuPlayer == null ? Era.Communities : menuPlayer.Era);
            if (pageHost != null) pageHost.gameObject.SetActive(false);
            if (navHost != null) navHost.gameObject.SetActive(false);
            if (actionHost != null) actionHost.gameObject.SetActive(false);
            if (header != null) header.gameObject.SetActive(false);

            GameObject shade = UiKit.CreateObject("MenuTextShade", menuHost);
            Image shadeImage = shade.AddComponent<Image>();
            shadeImage.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.76f);
            shadeImage.raycastTarget = false;
            UiKit.SetAnchors(UiKit.Rect(shade), Vector2.zero, new Vector2(0.50f, 1f), Vector2.zero, Vector2.zero);

            GameObject menu = UiKit.CreateObject("MainMenuContent", menuHost);
            UiKit.SetAnchors(UiKit.Rect(menu), new Vector2(0.035f, 0.045f), new Vector2(0.475f, 0.955f), Vector2.zero, Vector2.zero);
            UiKit.Vertical(menu, 6, 8);
            GameObject brand = UiKit.CreateObject("MenuBrand", menu.transform);
            LayoutElement brandLayout = brand.AddComponent<LayoutElement>();
            brandLayout.minHeight = 94f;
            brandLayout.preferredHeight = 94f;
            UiKit.Horizontal(brand, 0, 16);
            AddArtwork(brand.transform, "Jericho_Logo", 88f);
            Text brandTitle = UiKit.Label(brand.transform, "JERICHO", 48, GamePalette.Gold, TextAnchor.MiddleLeft, true);
            brandTitle.GetComponent<LayoutElement>().flexibleWidth = 1f;
            UiKit.Label(menu.transform, "От первого поселения — к цивилизации, которую выбирают строить.",
                17, GamePalette.Paper, TextAnchor.MiddleLeft, false);

            if (simulation != null)
            {
                UiKit.Button(menu.transform, "ПРОДОЛЖИТЬ КАМПАНИЮ", delegate { menuHost.gameObject.SetActive(false); RenderGame(); }, GamePalette.Gold, 60, 19);
                AddMenuButtonRow(menu.transform, "СОХРАНИТЬ", SaveManual, "СОХРАНЕНИЯ", ShowSaves);
                AddMenuButtonRow(menu.transform, "НАСТРОЙКИ", ShowSettings, "КАК ИГРАТЬ", ShowTutorial);
                AddMenuButtonRow(menu.transform, "ЭНЦИКЛОПЕДИЯ", ShowEncyclopedia, "ДОСТИЖЕНИЯ", ShowAchievements);
                UiKit.Button(menu.transform, "ЗАВЕРШИТЬ ПАРТИЮ", delegate { simulation = null; ShowMenu(); }, GamePalette.InkSoft, 44, 14);
                AddMenuProfile(menu.transform);
                return;
            }

            UiKit.Button(menu.transform, "НОВАЯ КАМПАНИЯ", StartNewCampaign, GamePalette.Gold, 60, 20);
            Button continueButton = UiKit.Button(menu.transform, "ПРОДОЛЖИТЬ", LoadCampaign, GamePalette.InkSoft, 50, 17);
            continueButton.interactable = SaveService.HasSave;
            if (SaveService.HasSave) UiKit.Button(menu.transform, "СОХРАНЕНИЯ", ShowSaves, GamePalette.InkSoft, 44, 15);
            AddMenuButtonRow(menu.transform, "НАСТРОЙКИ", ShowSettings, "КАК ИГРАТЬ", ShowTutorial);
            AddMenuButtonRow(menu.transform, "ЭНЦИКЛОПЕДИЯ", ShowEncyclopedia, "ДОСТИЖЕНИЯ", ShowAchievements);
            AddMenuProfile(menu.transform);
        }

        private void AddMenuButtonRow(Transform parent, string leftLabel, Action leftAction, string rightLabel, Action rightAction)
        {
            GameObject row = UiKit.CreateObject("MenuActionRow", parent);
            UiKit.Horizontal(row, 0, 10);
            Button left = UiKit.Button(row.transform, leftLabel, leftAction, GamePalette.InkSoft, 46, 14);
            Button right = UiKit.Button(row.transform, rightLabel, rightAction, GamePalette.InkSoft, 46, 14);
            left.GetComponent<LayoutElement>().flexibleWidth = 1f;
            right.GetComponent<LayoutElement>().flexibleWidth = 1f;
        }

        private void AddMenuProfile(Transform parent)
        {
            GameObject profile = UiKit.CreateObject("MenuProfile", parent);
            LayoutElement layout = profile.AddComponent<LayoutElement>();
            layout.minHeight = 38f;
            layout.preferredHeight = 38f;
            UiKit.Label(profile.transform, AchievementService.SelectedTitle + "  ·  " + AchievementService.Renown + " славы  ·  " +
                AchievementService.UnlockedCount + "/" + AchievementService.Definitions.Length + " наград",
                13, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);
        }

        private void InitializeSoundtrack()
        {
            GameObject audioRoot = new GameObject("NasledieAudio");
            audioRoot.transform.SetParent(transform, false);
            if (FindFirstObjectByType<AudioListener>() == null) audioRoot.AddComponent<AudioListener>();
            soundtracks[0] = CreateAudioSource(audioRoot.transform, "MusicA", true);
            soundtracks[1] = CreateAudioSource(audioRoot.transform, "MusicB", true);
            soundEffects = CreateAudioSource(audioRoot.transform, "SoundEffects", false);
            soundEffects.volume = SoundEffectsVolumeSteps[soundEffectsVolumeStep];
            SetMusicEra(Era.Communities, false);
        }

        private static AudioSource CreateAudioSource(Transform parent, string name, bool loop)
        {
            GameObject sourceObject = new GameObject(name);
            sourceObject.transform.SetParent(parent, false);
            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }

        private void SetMusicEra(Era era, bool allowCrossfade = true)
        {
            int trackIndex = Mathf.Clamp((int)era, 0, MusicTrackNames.Length - 1);
            if (trackIndex == currentMusicEra && soundtracks[activeSoundtrackIndex].clip != null) return;

            string resourceName = "Audio/" + MusicTrackNames[trackIndex];
            AudioClip clip = Resources.Load<AudioClip>(resourceName);
            if (clip == null && trackIndex != 0) clip = Resources.Load<AudioClip>("Audio/Nasledie_Theme");
            if (clip == null) return;

            AudioSource outgoing = soundtracks[activeSoundtrackIndex];
            bool hasOutgoing = outgoing.clip != null && outgoing.isPlaying && musicEnabled;
            int incomingIndex = hasOutgoing ? 1 - activeSoundtrackIndex : activeSoundtrackIndex;
            AudioSource incoming = soundtracks[incomingIndex];
            if (soundtrackTransition != null)
            {
                StopCoroutine(soundtrackTransition);
                soundtrackTransition = null;
            }
            incoming.Stop();
            incoming.clip = clip;
            incoming.volume = 0f;
            currentMusicEra = trackIndex;
            activeSoundtrackIndex = incomingIndex;
            if (!musicEnabled) return;
            incoming.Play();

            if (!allowCrossfade || !hasOutgoing)
            {
                outgoing.Stop();
                outgoing.volume = 0f;
                incoming.volume = MusicVolumeSteps[musicVolumeStep];
                return;
            }
            soundtrackTransition = StartCoroutine(CrossfadeMusic(outgoing, incoming, 1.25f));
        }

        private IEnumerator CrossfadeMusic(AudioSource outgoing, AudioSource incoming, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float blend = Mathf.Clamp01(elapsed / duration);
                float volume = musicEnabled ? MusicVolumeSteps[musicVolumeStep] : 0f;
                outgoing.volume = volume * (1f - blend);
                incoming.volume = volume * blend;
                yield return null;
            }
            outgoing.Stop();
            outgoing.clip = null;
            outgoing.volume = 0f;
            incoming.volume = musicEnabled ? MusicVolumeSteps[musicVolumeStep] : 0f;
            soundtrackTransition = null;
        }

        private void PlayButtonClick()
        {
            PlaySoundEffect("UI_Click");
        }

        private void PlaySoundEffect(string cue)
        {
            if (!soundEffectsEnabled || soundEffects == null || string.IsNullOrEmpty(cue)) return;
            AudioClip clip;
            if (!soundEffectCache.TryGetValue(cue, out clip))
            {
                clip = Resources.Load<AudioClip>("Audio/Sfx/" + cue);
                soundEffectCache[cue] = clip;
            }
            if (clip != null) soundEffects.PlayOneShot(clip);
        }

        private void PlayFinalSound()
        {
            int humanId = simulation.State.HumanCivilizationId;
            int[] winners = simulation.State.WinningCivilizationIds ?? Array.Empty<int>();
            if (winners.Length == 0 && simulation.State.WinnerCivilizationId >= 0)
                winners = new[] { simulation.State.WinnerCivilizationId };
            for (int i = 0; i < winners.Length; i++)
                if (winners[i] == humanId) { PlaySoundEffect("Victory"); return; }
            PlaySoundEffect("Defeat");
        }

        private void ToggleMusic()
        {
            musicEnabled = !musicEnabled;
            PlayerPrefs.SetInt("nasledie.music.enabled", musicEnabled ? 1 : 0);
            PlayerPrefs.Save();
            if (soundtrackTransition != null)
            {
                StopCoroutine(soundtrackTransition);
                soundtrackTransition = null;
            }
            for (int i = 0; i < soundtracks.Length; i++)
            {
                if (i == activeSoundtrackIndex)
                {
                    soundtracks[i].volume = musicEnabled ? MusicVolumeSteps[musicVolumeStep] : 0f;
                    if (soundtracks[i].clip != null)
                    {
                        if (musicEnabled) soundtracks[i].Play();
                        else soundtracks[i].Pause();
                    }
                }
                else
                {
                    soundtracks[i].Stop();
                    soundtracks[i].clip = null;
                    soundtracks[i].volume = 0f;
                }
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
            for (int i = 0; i < soundtracks.Length; i++)
                soundtracks[i].volume = i == activeSoundtrackIndex && musicEnabled ? MusicVolumeSteps[musicVolumeStep] : 0f;
            PlayerPrefs.SetInt("nasledie.music.volume", musicVolumeStep);
            PlayerPrefs.Save();
            ShowSettings();
        }

        private void ToggleSoundEffects()
        {
            soundEffectsEnabled = !soundEffectsEnabled;
            PlayerPrefs.SetInt("nasledie.sfx.enabled", soundEffectsEnabled ? 1 : 0);
            PlayerPrefs.Save();
            ShowSettings();
        }

        private string SoundEffectsVolumeLabel()
        {
            return "ГРОМКОСТЬ ЭФФЕКТОВ · " + Mathf.RoundToInt(SoundEffectsVolumeSteps[soundEffectsVolumeStep] * 100f) + "%";
        }

        private void CycleSoundEffectsVolume()
        {
            soundEffectsVolumeStep = (soundEffectsVolumeStep + 1) % SoundEffectsVolumeSteps.Length;
            if (soundEffects != null) soundEffects.volume = SoundEffectsVolumeSteps[soundEffectsVolumeStep];
            PlayerPrefs.SetInt("nasledie.sfx.volume", soundEffectsVolumeStep);
            PlayerPrefs.Save();
            ShowSettings();
        }

        private void ShowSettings()
        {
            RememberUtilityReturn("settings");
            PrepareUtilityOverlay("Backgrounds/Settings_Background");
            activeUtilityScreen = "settings";
            GameObject panel = UtilityPanel("SettingsPanel", new Vector2(0.055f, 0.045f), new Vector2(0.945f, 0.955f));
            UiKit.Vertical(panel, 10, 8);
            AddUtilityHeader(panel.transform, "Настройки", ReturnFromUtilityScreen);

            GameObject columns = UiKit.CreateObject("SettingsColumns", panel.transform);
            LayoutElement columnsLayout = columns.AddComponent<LayoutElement>();
            columnsLayout.flexibleHeight = 1f;
            UiKit.Horizontal(columns, 0, 48);

            GameObject sound = UtilityCard(columns.transform, "ЗВУК");
            UiKit.Button(sound.transform, musicEnabled ? "МУЗЫКА  ·  ВКЛ." : "МУЗЫКА  ·  ВЫКЛ.", ToggleMusic,
                musicEnabled ? GamePalette.Teal : GamePalette.InkSoft, 44, 15);
            AddVolumeSlider(sound.transform, "Громкость музыки", true);
            UiKit.Button(sound.transform, soundEffectsEnabled ? "ЭФФЕКТЫ  ·  ВКЛ." : "ЭФФЕКТЫ  ·  ВЫКЛ.", ToggleSoundEffects,
                soundEffectsEnabled ? GamePalette.Teal : GamePalette.InkSoft, 44, 15);
            AddVolumeSlider(sound.transform, "Громкость эффектов", false);

            GameObject access = UtilityCard(columns.transform, "ИЗОБРАЖЕНИЕ И ДОСТУПНОСТЬ");
            UiKit.Label(access.transform, "АНИМАЦИИ", 14, GamePalette.PaperDim, TextAnchor.MiddleLeft, true);
            AddChoiceButtons(access.transform, new[] { "ВЫКЛ.", "БЫСТРО", "ПОЛНО" }, animationMode, SetAnimationMode);
            UiKit.Label(access.transform, "РАЗМЕР ТЕКСТА", 14, GamePalette.PaperDim, TextAnchor.MiddleLeft, true);
            AddChoiceButtons(access.transform, new[] { "90%", "100%", "115%", "130%" }, textScaleIndex, SetTextScale);
            UiKit.Button(access.transform, highContrast ? "ВЫСОКИЙ КОНТРАСТ  ·  ВКЛ." : "ВЫСОКИЙ КОНТРАСТ  ·  ВЫКЛ.",
                ToggleHighContrast, highContrast ? GamePalette.Teal : GamePalette.InkSoft, 44, 14);
            UiKit.Button(access.transform, hapticsEnabled ? "ВИБРООТКЛИК  ·  ВКЛ." : "ВИБРООТКЛИК  ·  ВЫКЛ.",
                ToggleHaptics, hapticsEnabled ? GamePalette.Teal : GamePalette.InkSoft, 44, 14);
            UiKit.Button(access.transform, "СБРОСИТЬ ДОСТУПНОСТЬ", ResetAccessibilitySettings, GamePalette.Ink, 40, 12);

            GameObject options = UiKit.CreateObject("CampaignOptions", panel.transform);
            UiKit.Horizontal(options, 0, 12);
            Button pace = UiKit.Button(options.transform, "ТЕМП СЛЕДУЮЩЕЙ ПАРТИИ  ·  " + PaceName(selectedPace), CyclePace, GamePalette.InkSoft, 44, 14);
            pace.GetComponent<LayoutElement>().flexibleWidth = 0.8f;
            UiKit.Label(options.transform, "Альбомная ориентация  ·  безопасная область iPhone", 13,
                GamePalette.Muted, TextAnchor.MiddleLeft, false);
            if (!string.IsNullOrEmpty(notice)) UiKit.Label(panel.transform, notice, 13, GamePalette.Gold, TextAnchor.MiddleLeft, true);
        }

        private GameObject UtilityPanel(string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject panel = UiKit.CreateObject(name, menuHost);
            Image image = panel.AddComponent<Image>();
            image.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.02f);
            image.raycastTarget = false;
            UiKit.SetAnchors(UiKit.Rect(panel), anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            return panel;
        }

        private GameObject UtilityCard(Transform parent, string title)
        {
            GameObject card = UiKit.CreateObject("UtilityCard_" + title, parent);
            LayoutElement layout = card.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            layout.flexibleHeight = 1f;
            UiKit.Vertical(card, 14, 8);
            UiKit.Label(card.transform, title, 19, GamePalette.Gold, TextAnchor.MiddleLeft, true);
            return card;
        }

        private void AddUtilityHeader(Transform parent, string title, Action backAction, string exitLabel = "←  НАЗАД")
        {
            GameObject row = UiKit.CreateObject("UtilityHeader", parent);
            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = 52f;
            rowLayout.preferredHeight = 52f;
            UiKit.Horizontal(row, 0, 12);
            Text titleLabel = UiKit.Label(row.transform, title, 32, GamePalette.Paper, TextAnchor.MiddleLeft, true);
            titleLabel.GetComponent<LayoutElement>().flexibleWidth = 1f;
            Button back = UiKit.Button(row.transform, exitLabel, backAction, GamePalette.Ink, 44, 14);
            SetToolbarButtonWidth(back, 176f);
        }

        private void AddVolumeSlider(Transform parent, string title, bool music)
        {
            GameObject row = UiKit.CreateObject("VolumeControl_" + title, parent);
            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = 76f;
            rowLayout.preferredHeight = 76f;
            UiKit.Vertical(row, 0, 0);

            GameObject labels = UiKit.CreateObject("VolumeLabels", row.transform);
            LayoutElement labelsLayout = labels.AddComponent<LayoutElement>();
            labelsLayout.minHeight = 27f;
            labelsLayout.preferredHeight = 27f;
            UiKit.Horizontal(labels, 0, 0);
            UiKit.Label(labels.transform, title, 15, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);
            int initialStep = music ? musicVolumeStep : soundEffectsVolumeStep;
            Text valueLabel = UiKit.Label(labels.transform, VolumePercent(music, initialStep), 15, GamePalette.Gold, TextAnchor.MiddleRight, true);
            valueLabel.GetComponent<LayoutElement>().flexibleWidth = 0f;
            valueLabel.GetComponent<LayoutElement>().minWidth = 52f;
            valueLabel.GetComponent<LayoutElement>().preferredWidth = 52f;

            GameObject sliderObject = UiKit.CreateObject("VolumeSlider", row.transform);
            LayoutElement sliderLayout = sliderObject.AddComponent<LayoutElement>();
            sliderLayout.minHeight = 42f;
            sliderLayout.preferredHeight = 42f;
            Slider slider = sliderObject.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = music ? MusicVolumeSteps.Length - 1 : SoundEffectsVolumeSteps.Length - 1;
            slider.wholeNumbers = true;
            slider.value = initialStep;
            slider.direction = Slider.Direction.LeftToRight;

            GameObject track = UiKit.CreateObject("SliderTrack", sliderObject.transform);
            Image trackImage = track.AddComponent<Image>();
            trackImage.color = new Color(GamePalette.PaperDim.r, GamePalette.PaperDim.g, GamePalette.PaperDim.b, 0.35f);
            trackImage.raycastTarget = false;
            UiKit.SetAnchors(UiKit.Rect(track), new Vector2(0f, 0.44f), new Vector2(1f, 0.56f), new Vector2(10f, 0f), new Vector2(-10f, 0f));

            GameObject fillArea = UiKit.CreateObject("SliderFillArea", sliderObject.transform);
            UiKit.SetAnchors(UiKit.Rect(fillArea), new Vector2(0f, 0.34f), new Vector2(1f, 0.66f), new Vector2(10f, 0f), new Vector2(-10f, 0f));
            GameObject fill = UiKit.CreateObject("SliderFill", fillArea.transform);
            Image fillImage = fill.AddComponent<Image>();
            fillImage.color = GamePalette.Gold;
            fillImage.raycastTarget = false;
            RectTransform fillRect = UiKit.Rect(fill);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            GameObject handleArea = UiKit.CreateObject("SliderHandleArea", sliderObject.transform);
            RectTransform handleAreaRect = UiKit.Rect(handleArea);
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = Vector2.zero;
            handleAreaRect.offsetMax = Vector2.zero;
            GameObject handle = UiKit.CreateObject("SliderHandle", handleArea.transform);
            Image handleImage = handle.AddComponent<Image>();
            handleImage.color = GamePalette.Paper;
            RectTransform handleRect = UiKit.Rect(handle);
            handleRect.anchorMin = new Vector2(0f, 0.5f);
            handleRect.anchorMax = new Vector2(0f, 0.5f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(22f, 22f);
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.onValueChanged.AddListener(delegate(float value)
            {
                int step = Mathf.RoundToInt(value);
                valueLabel.text = VolumePercent(music, step);
                if (music)
                {
                    if (musicVolumeStep == step) return;
                    musicVolumeStep = step;
                    for (int i = 0; i < soundtracks.Length; i++)
                        soundtracks[i].volume = i == activeSoundtrackIndex && musicEnabled ? MusicVolumeSteps[musicVolumeStep] : 0f;
                    PlayerPrefs.SetInt("nasledie.music.volume", musicVolumeStep);
                }
                else
                {
                    if (soundEffectsVolumeStep == step) return;
                    soundEffectsVolumeStep = step;
                    if (soundEffects != null) soundEffects.volume = SoundEffectsVolumeSteps[soundEffectsVolumeStep];
                    PlayerPrefs.SetInt("nasledie.sfx.volume", soundEffectsVolumeStep);
                }
                PlayerPrefs.Save();
            });
        }

        private static string VolumePercent(bool music, int step)
        {
            float[] values = music ? MusicVolumeSteps : SoundEffectsVolumeSteps;
            return Mathf.RoundToInt(values[Mathf.Clamp(step, 0, values.Length - 1)] * 100f) + "%";
        }

        private void AddChoiceButtons(Transform parent, string[] labels, int selected, Action<int> onSelect)
        {
            GameObject row = UiKit.CreateObject("ChoiceButtons", parent);
            UiKit.Horizontal(row, 0, 5);
            for (int i = 0; i < labels.Length; i++)
            {
                int captured = i;
                Button choice = UiKit.Button(row.transform, labels[i], delegate { onSelect(captured); },
                    i == selected ? GamePalette.Gold : GamePalette.InkSoft, 40, 12);
                choice.GetComponent<LayoutElement>().flexibleWidth = 1f;
            }
        }

        private void CopyDiagnosticsToClipboard()
        {
            GUIUtility.systemCopyBuffer = SaveService.BuildDiagnostics(simulation == null ? null : simulation.State);
            notice = "Отчёт диагностики скопирован в буфер обмена.";
            ShowSettings();
        }

        private void SaveDiagnosticsToFile()
        {
            try
            {
                string path = SaveService.SaveDiagnosticsFile(simulation == null ? null : simulation.State);
                notice = "Отчёт диагностики сохранён: " + path;
            }
            catch (Exception exception)
            {
                notice = "Не удалось сохранить отчёт: " + exception.Message;
            }
            ShowSettings();
        }

        private string TextScaleLabel()
        {
            string[] names = { "90%", "100%", "115%", "130%" };
            return "РАЗМЕР ТЕКСТА · " + names[textScaleIndex];
        }

        private void CycleTextScale()
        {
            SetTextScale((textScaleIndex + 1) % 4);
        }

        private void SetTextScale(int index)
        {
            textScaleIndex = Mathf.Clamp(index, 0, 3);
            UiKit.TextScaleIndex = textScaleIndex;
            PlayerPrefs.SetInt("jericho.accessibility.text", textScaleIndex);
            PlayerPrefs.Save();
            ShowSettings();
        }

        private void ToggleHighContrast()
        {
            highContrast = !highContrast;
            UiKit.HighContrast = highContrast;
            PlayerPrefs.SetInt("jericho.accessibility.contrast", highContrast ? 1 : 0);
            PlayerPrefs.Save();
            ShowSettings();
        }

        private void ToggleHaptics()
        {
            hapticsEnabled = !hapticsEnabled;
            PlayerPrefs.SetInt("jericho.accessibility.haptics", hapticsEnabled ? 1 : 0);
            PlayerPrefs.Save();
            if (hapticsEnabled && SystemInfo.supportsVibration) Handheld.Vibrate();
            ShowSettings();
        }

        private void ResetAccessibilitySettings()
        {
            textScaleIndex = 1;
            highContrast = false;
            hapticsEnabled = true;
            animationMode = 1;
            UiKit.TextScaleIndex = textScaleIndex;
            UiKit.HighContrast = highContrast;
            PlayerPrefs.SetInt("jericho.accessibility.text", textScaleIndex);
            PlayerPrefs.SetInt("jericho.accessibility.contrast", 0);
            PlayerPrefs.SetInt("jericho.accessibility.haptics", 1);
            PlayerPrefs.SetInt("jericho.animation.mode", animationMode);
            PlayerPrefs.Save();
            ShowSettings();
        }

        private string AnimationModeLabel()
        {
            return animationMode == 0 ? "АНИМАЦИИ · ВЫКЛЮЧЕНЫ" : animationMode == 1 ? "АНИМАЦИИ · БЫСТРЫЕ" : "АНИМАЦИИ · ПОЛНЫЕ";
        }

        private void CycleAnimationMode()
        {
            SetAnimationMode((animationMode + 1) % 3);
        }

        private void SetAnimationMode(int mode)
        {
            animationMode = Mathf.Clamp(mode, 0, 2);
            PlayerPrefs.SetInt("jericho.animation.mode", animationMode);
            PlayerPrefs.Save();
            ShowSettings();
        }

        private float AnimationScale { get { return animationMode == 2 ? 1f : animationMode == 1 ? 0.45f : 0f; } }

        private void ShowAchievements()
        {
            RememberUtilityReturn("achievements");
            PrepareUtilityOverlay("Backgrounds/Victory_Background");
            activeUtilityScreen = "achievements";
            GameObject panel = UtilityPanel("AchievementsPanel", new Vector2(0.025f, 0.04f), new Vector2(0.975f, 0.96f));
            UiKit.Vertical(panel, 12, 7);
            AddUtilityHeader(panel.transform, "Достижения", ReturnFromUtilityScreen);
            UiKit.Label(panel.transform, AchievementService.SelectedTitle + "  ·  " + AchievementService.Renown + " славы  ·  " +
                AchievementService.UnlockedCount + "/" + AchievementService.Definitions.Length + " открыто",
                15, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);

            float logicalWidth = Screen.width / Mathf.Max(0.1f, canvas.scaleFactor);
            float logicalHeight = Screen.height / Mathf.Max(0.1f, canvas.scaleFactor);
            float cellHeight = Mathf.Clamp((logicalHeight * 0.90f - 200f) / 3f, 155f, 228f);
            float cellWidth = (logicalWidth * 0.95f - 48f - 36f) / 4f;
            GameObject grid = UiKit.CreateObject("AchievementGrid", panel.transform);
            LayoutElement gridElement = grid.AddComponent<LayoutElement>();
            gridElement.minHeight = cellHeight * 3f + 24f;
            gridElement.preferredHeight = cellHeight * 3f + 24f;
            gridElement.flexibleWidth = 1f;
            GridLayoutGroup gridLayout = grid.AddComponent<GridLayoutGroup>();
            gridLayout.padding = new RectOffset(0, 0, 0, 0);
            gridLayout.spacing = new Vector2(12, 12);
            gridLayout.cellSize = new Vector2(cellWidth, cellHeight);
            gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            gridLayout.childAlignment = TextAnchor.UpperLeft;
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 4;

            GameState state = simulation == null ? null : simulation.State;
            for (int i = 0; i < AchievementService.Definitions.Length; i++)
            {
                AchievementDefinition achievement = AchievementService.Definitions[i];
                bool unlocked = AchievementService.IsUnlocked(achievement.Id);
                bool equipped = AchievementService.SelectedTitleId == achievement.Id;
                GameObject card = UiKit.CreateObject("Achievement_" + achievement.Id, grid.transform);
                Image cardImage = card.AddComponent<Image>();
                cardImage.color = new Color(GamePalette.InkSoft.r, GamePalette.InkSoft.g, GamePalette.InkSoft.b, 0.035f);
                cardImage.raycastTarget = false;
                UiKit.Vertical(card, 9, 3);
                GameObject titleRow = UiKit.CreateObject("AchievementHeading", card.transform);
                UiKit.Horizontal(titleRow, 0, 6);
                LayoutElement titleRowLayout = titleRow.AddComponent<LayoutElement>();
                titleRowLayout.minHeight = 36f;
                titleRowLayout.preferredHeight = 36f;
                AddArtwork(titleRow.transform, achievement.Artwork, 34f);
                Text title = UiKit.Label(titleRow.transform, (unlocked ? "✓  " : "") + achievement.Name, 15,
                    unlocked ? GamePalette.Gold : GamePalette.Paper, TextAnchor.MiddleLeft, true);
                title.GetComponent<LayoutElement>().flexibleWidth = 1f;
                Text description = UiKit.Label(card.transform, achievement.Description, 13, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);
                description.GetComponent<LayoutElement>().minHeight = description.GetComponent<LayoutElement>().preferredHeight = 38f;
                Text progressText = UiKit.Label(card.transform, AchievementService.ProgressText(achievement, state), 12,
                    unlocked ? GamePalette.Success : GamePalette.Muted, TextAnchor.MiddleLeft, true);
                progressText.GetComponent<LayoutElement>().minHeight = progressText.GetComponent<LayoutElement>().preferredHeight = 22f;
                Text reward = UiKit.Label(card.transform, "Титул «" + achievement.TitleReward + "» · " + achievement.RenownReward + " славы",
                    12, GamePalette.Gold, TextAnchor.MiddleLeft, false);
                reward.GetComponent<LayoutElement>().minHeight = reward.GetComponent<LayoutElement>().preferredHeight = 23f;
                GameObject divider = UiKit.CreateObject("AchievementDivider", card.transform);
                Image dividerImage = divider.AddComponent<Image>();
                dividerImage.color = new Color(GamePalette.PaperDim.r, GamePalette.PaperDim.g, GamePalette.PaperDim.b, 0.18f);
                dividerImage.raycastTarget = false;
                LayoutElement dividerLayout = divider.AddComponent<LayoutElement>();
                dividerLayout.minHeight = 1f;
                dividerLayout.preferredHeight = 1f;
                Button equip = UiKit.Button(card.transform, equipped ? "ТИТУЛ ВЫБРАН" : unlocked ? "НАДЕТЬ ТИТУЛ" : "ЕЩЁ НЕ ОТКРЫТО",
                    delegate { AchievementService.SelectTitle(achievement.Id); notice = "Титул изменён: «" + achievement.TitleReward + "»."; ShowAchievements(); },
                    unlocked ? GamePalette.Teal : GamePalette.Ink, 42, 12);
                equip.interactable = unlocked && !equipped;
            }
            GameObject footer = UiKit.CreateObject("AchievementActions", panel.transform);
            UiKit.Horizontal(footer, 0, 12);
            Button clear = UiKit.Button(footer.transform, "СНЯТЬ ТИТУЛ", delegate { AchievementService.SelectTitle(""); ShowAchievements(); }, GamePalette.InkSoft, 48, 15);
            clear.GetComponent<LayoutElement>().flexibleWidth = 0.7f;
        }

        private void ShowSaves()
        {
            RememberUtilityReturn("saves");
            PrepareUtilityOverlay("Backgrounds/Archive_Background");
            activeUtilityScreen = "saves";
            GameObject panel = UtilityPanel("SaveManagerPanel", new Vector2(0.035f, 0.045f), new Vector2(0.965f, 0.955f));
            UiKit.Vertical(panel, 12, 8);
            AddUtilityHeader(panel.transform, "Сохранения", ReturnFromUtilityScreen);
            GameObject rows = UiKit.CreateObject("SaveSlotGroups", panel.transform);
            LayoutElement rowsLayout = rows.AddComponent<LayoutElement>();
            rowsLayout.flexibleHeight = 1f;
            UiKit.Vertical(rows, 0, 8);

            GameObject manualGroup = UiKit.CreateObject("ManualSaveGroup", rows.transform);
            LayoutElement manualGroupLayout = manualGroup.AddComponent<LayoutElement>();
            manualGroupLayout.flexibleHeight = 1f;
            UiKit.Vertical(manualGroup, 0, 5);
            UiKit.Label(manualGroup.transform, "РУЧНЫЕ СЛОТЫ", 17, GamePalette.Teal, TextAnchor.MiddleLeft, true);
            GameObject manualRow = UiKit.CreateObject("ManualSaveSlots", manualGroup.transform);
            LayoutElement manualRowLayout = manualRow.AddComponent<LayoutElement>();
            manualRowLayout.flexibleHeight = 1f;
            UiKit.Horizontal(manualRow, 0, 10);
            for (int slot = 0; slot < 3; slot++)
            {
                int capturedSlot = slot;
                GameState manual = SaveService.LoadManual(slot);
                string manualLabel = "РУЧНОЙ СЛОТ " + (slot + 1);
                Action save = simulation == null ? null : delegate { SaveManualSlot(capturedSlot); };
                AddSaveSlot(manualRow.transform, manualLabel, manual,
                    delegate { OpenSavedCampaign(SaveService.LoadManual(capturedSlot), manualLabel); }, save);
            }

            GameObject autoGroup = UiKit.CreateObject("AutoSaveGroup", rows.transform);
            LayoutElement autoGroupLayout = autoGroup.AddComponent<LayoutElement>();
            autoGroupLayout.flexibleHeight = 1f;
            UiKit.Vertical(autoGroup, 0, 5);
            UiKit.Label(autoGroup.transform, "АВТОСОХРАНЕНИЯ", 17, GamePalette.Teal, TextAnchor.MiddleLeft, true);
            GameObject autoRow = UiKit.CreateObject("AutoSaveSlots", autoGroup.transform);
            LayoutElement autoRowLayout = autoRow.AddComponent<LayoutElement>();
            autoRowLayout.flexibleHeight = 1f;
            UiKit.Horizontal(autoRow, 0, 10);
            for (int slot = 0; slot < 3; slot++)
            {
                int capturedSlot = slot;
                GameState auto = SaveService.LoadAuto(slot);
                string label = slot == 0 ? "АВТОСОХРАНЕНИЕ · ПОСЛЕДНЕЕ" : slot == 1 ? "АВТОСОХРАНЕНИЕ · ПРЕДЫДУЩЕЕ" : "АВТОСОХРАНЕНИЕ · РАННЕЕ";
                string capturedLabel = label;
                AddSaveSlot(autoRow.transform, capturedLabel, auto, delegate { OpenSavedCampaign(SaveService.LoadAuto(capturedSlot), capturedLabel); }, null);
            }
        }

        private void AddSaveSlot(Transform parent, string title, GameState state, Action load, Action save = null)
        {
            GameObject card = UiKit.Card(parent, title);
            LayoutElement cardLayout = card.GetComponent<LayoutElement>();
            cardLayout.flexibleHeight = 1f;
            if (state == null)
            {
                UiKit.Label(card.transform, "Слот пуст или файл повреждён.", 16, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            }
            else
            {
                CivilizationState player = state.Civilization(state.HumanCivilizationId);
                UiKit.Label(card.transform, "Ход " + Math.Min(state.Turn, state.MaximumTurn) + "/" + state.MaximumTurn + " · " +
                    PaceName(state.Pace) + " · " + (player == null ? "цивилизация не найдена" : player.Name) + " · мир " + state.Seed +
                    (state.IsFinished ? " · кампания завершена" : ""), 15, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                UiKit.Button(card.transform, "ЗАГРУЗИТЬ", load, GamePalette.Teal, 48, 15);
            }
            if (save != null) UiKit.Button(card.transform, "СОХРАНИТЬ СЮДА", save, GamePalette.Ink, 48, 15);
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
            CivilizationState savedPlayer = saved.Civilization(saved.HumanCivilizationId);
            if (savedPlayer != null) SetMusicEra(savedPlayer.Era);
            selectedCityId = simulation.FindCapital(saved.HumanCivilizationId);
            selectedUnitId = -1;
            currentPage = saved.IsFinished ? ScreenPage.Victory : ScreenPage.Overview;
            notice = source + " загружено · ход " + saved.Turn + ".";
            menuHost.gameObject.SetActive(false);
            RenderGame();
        }

        private void PrepareUtilityOverlay(string backgroundArtKey = "Backgrounds/Menu_Background")
        {
            DestroyChildren(menuHost);
            menuHost.gameObject.SetActive(true);
            SetUtilityBackground(backgroundArtKey);
            GameObject shade = UiKit.CreateObject("UtilityBackdropShade", menuHost);
            Image shadeImage = shade.AddComponent<Image>();
            shadeImage.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.32f);
            shadeImage.raycastTarget = false;
            UiKit.SetAnchors(UiKit.Rect(shade), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            if (pageHost != null) pageHost.gameObject.SetActive(false);
            if (navHost != null) navHost.gameObject.SetActive(false);
            if (actionHost != null) actionHost.gameObject.SetActive(false);
            if (header != null) header.gameObject.SetActive(false);
        }

        private void SetUtilityBackground(string artworkKey)
        {
            if (menuHost == null) return;
            Image image = menuHost.GetComponent<Image>();
            if (image == null) return;
            image.sprite = string.IsNullOrEmpty(artworkKey) ? null : LoadArtwork(artworkKey);
            image.preserveAspect = false;
            image.color = image.sprite == null ? GamePalette.Ink : Color.white;
            image.raycastTarget = false;
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
                "Art/WorldAtlas", "Art/UnitAtlas", "Art/UnitEraAtlas", "Art/UnitRosterAtlas", "Art/AchievementAtlas",
                "Art/Animations/WorkerActivities_Rural", "Art/Animations/WorkerActivities_Civic"
            };
            for (int a = 0; a < atlases.Length; a++)
            {
                Sprite[] sprites = Resources.LoadAll<Sprite>(atlases[a]);
                for (int i = 0; i < sprites.Length; i++)
                    if (sprites[i] != null && !Artwork.ContainsKey(sprites[i].name)) Artwork[sprites[i].name] = sprites[i];
            }
        }

        private static GameObject AddArtwork(Transform parent, string name, float height)
        {
            Sprite sprite = LoadArtwork(name);
            if (sprite == null) return null;
            GameObject art = UiKit.CreateObject("Artwork_" + name, parent);
            Image image = art.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            LayoutElement layout = art.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.minWidth = height;
            layout.preferredWidth = height;
            layout.flexibleWidth = 0f;
            return art;
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
            return BuildingArtwork(id, era, null);
        }

        private static string BuildingArtwork(string id, Era era, string nationId)
        {
            BuildingDefinition definition = RulesCatalog.Building(id);
            if (definition != null && !string.IsNullOrEmpty(definition.ArtKey)) return definition.ArtKey;
            if (id == "school" || id == "university" || id == "academy")
            {
                NationDefinition nation = NationCatalog.Get(nationId);
                if (nation != null) return nation.CampusArtKey;
            }
            string artwork = BuildingArtwork(id);
            return string.IsNullOrEmpty(artwork) ? null : "Building_" + era + "_" + artwork.Substring("Building_".Length);
        }

        private static string DisplayBuildingName(BuildingDefinition building, string nationId)
        {
            if (building == null) return "Неизвестное здание";
            NationDefinition nation = NationCatalog.Get(nationId);
            if (nation == null) return building.NameRu;
            if (building.Id == "school") return building.NameRu + " · " + nation.CampusNameRu;
            if (building.Id == "university") return building.NameRu + " · " + nation.CampusNameRu;
            if (building.Id == "academy") return building.NameRu + " · " + nation.CampusNameRu;
            return building.NameRu;
        }

        private static string BuildingEffectSummary(BuildingDefinition building)
        {
            if (building == null) return "эффект не найден";
            var effects = new List<string>();
            if (building.FlatFoodTenths > 0) effects.Add("+" + Format(building.FlatFoodTenths) + " еды/ход");
            if (building.FlatKnowledgeTenths > 0) effects.Add("+" + Format(building.FlatKnowledgeTenths) + " знаний/ход");
            if (building.FlatTreasuryTenths > 0) effects.Add("+" + Format(building.FlatTreasuryTenths) + " казны/ход");
            if (building.FlatCulturePrestige > 0) effects.Add("+" + building.FlatCulturePrestige + " престижа/ход");
            if (building.HealthPerTurn > 0) effects.Add("+" + building.HealthPerTurn + " здоровья/ход");
            if (building.EducationPerTurn > 0) effects.Add("+" + building.EducationPerTurn + " образования/ход");
            if (building.SafetyPerTurn > 0) effects.Add("+" + building.SafetyPerTurn + " безопасности/ход");
            if (building.ProductionPerArtisan > 0) effects.Add("+" + building.ProductionPerArtisan + " производства/ремесленника");
            if (building.WorkerPlaces > 0)
            {
                string role = building.WorkerPlaceRole == (int)WorkerRole.Artisan ? "ремесленника" : "специалиста";
                string placeWord = building.WorkerPlaces == 1 ? "место" : building.WorkerPlaces < 5 ? "места" : "мест";
                effects.Add("+" + building.WorkerPlaces + " " + placeWord + " " + role);
            }
            return effects.Count == 0 ? "особый культурный район" : string.Join(" · ", effects.ToArray());
        }

        private string CityMapArtwork(CityState city, Era era)
        {
            if (city == null) return "City";
            CivilizationState owner = simulation == null ? null : simulation.State.Civilization(city.OwnerId);
            string[] visualPriority =
            {
                "assembly", "academy", "university", "sumer_ziggurat_archive", "archive", "exchange_house", "cultural_house",
                "jericho_spring_tower", "egypt_nilometer", "indus_great_bath",
                "hospital", "administration", "market", "factory", "workshop", "barracks", "school", "council",
                "clinic", "granary", "residential_quarter", "house", "road_services"
            };
            for (int i = 0; i < visualPriority.Length; i++)
                if (city.BuildingLevel(visualPriority[i]) > 0)
                    return BuildingArtwork(visualPriority[i], era, owner == null ? null : owner.NationId);
            NationDefinition nation = owner == null ? null : NationCatalog.Get(owner.NationId);
            return nation == null ? "City" : nation.ArtKey;
        }

        private static Color NationAccent(CivilizationState civilization)
        {
            string nationId = civilization == null ? "" : civilization.NationId;
            switch (nationId)
            {
                case "sumer": return new Color(0.42f, 0.59f, 0.78f, 1f);
                case "egypt": return new Color(0.24f, 0.68f, 0.67f, 1f);
                case "indus": return new Color(0.73f, 0.43f, 0.32f, 1f);
                default: return new Color(0.91f, 0.70f, 0.34f, 1f);
            }
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
                case ImprovementKind.Park: return "Terrain_Forest";
                case ImprovementKind.Fortification: return BuildingArtwork("barracks", era);
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
            return UnitArtwork(kind, Era.Communities);
        }

        private static string UnitArtwork(UnitKind kind, Era era)
        {
            UnitDefinition definition = RulesCatalog.Unit(kind);
            if (definition == null) return null;
            if (!definition.LegacyOnly && definition.Class != UnitClass.Civilian)
                return "UnitRoster_" + definition.Era + "_" + definition.Class;
            string oldName = kind == UnitKind.SettlerWagon ? "Settler" : kind == UnitKind.FieldForce ? "FieldForce" : null;
            return string.IsNullOrEmpty(oldName) ? null : "Unit_" + era + "_" + oldName;
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
            ShowGuidedTutorial();
        }

        private void CloseGuidedTutorial()
        {
            ReturnFromUtilityScreen();
        }

        private void ShowGuidedTutorial()
        {
            RememberUtilityReturn("tutorial");
            PrepareUtilityOverlay("Backgrounds/Tutorial_Background");
            activeUtilityScreen = "tutorial";
            GameObject tutorialPanel = UiKit.CreateObject("TutorialPanel", menuHost);
            Image tutorialPanelImage = tutorialPanel.AddComponent<Image>();
            tutorialPanelImage.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.02f);
            tutorialPanelImage.raycastTarget = false;
            UiKit.SetAnchors(UiKit.Rect(tutorialPanel), new Vector2(0.035f, 0.045f), new Vector2(0.965f, 0.955f), Vector2.zero, Vector2.zero);
            GameObject content = UiKit.CreateObject("GuidedTutorialContent", tutorialPanel.transform);
            UiKit.SetAnchors(UiKit.Rect(content), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Text heading = UiKit.Label(content.transform, "КАК ИГРАТЬ", 30, GamePalette.Gold, TextAnchor.MiddleLeft, true);
            UiKit.SetAnchors(heading.rectTransform, new Vector2(0.035f, 0.915f), new Vector2(0.68f, 0.985f), Vector2.zero, Vector2.zero);
            Button close = UiKit.Button(content.transform, "ЗАКРЫТЬ", CloseGuidedTutorial, GamePalette.Ink, 44, 12);
            UiKit.SetAnchors(UiKit.Rect(close.gameObject), new Vector2(0.93f, 0.915f), new Vector2(0.965f, 0.985f), Vector2.zero, Vector2.zero);
            GameObject lesson = UiKit.CreateObject("TutorialLessonCard", content.transform);
            Image lessonImage = lesson.AddComponent<Image>();
            lessonImage.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.40f);
            lessonImage.raycastTarget = false;
            UiKit.SetAnchors(UiKit.Rect(lesson), new Vector2(0.035f, 0.265f), new Vector2(0.68f, 0.89f), Vector2.zero, Vector2.zero);
            GameObject progress = UiKit.CreateObject("TutorialProgressCard", content.transform);
            Image progressImage = progress.AddComponent<Image>();
            progressImage.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.22f);
            progressImage.raycastTarget = false;
            UiKit.SetAnchors(UiKit.Rect(progress), new Vector2(0.72f, 0.20f), new Vector2(0.965f, 0.89f), Vector2.zero, Vector2.zero);
            if (tutorialStepIndex >= TutorialSteps.Length)
            {
                Text complete = UiKit.Label(lesson.transform, "ПУТЕВОДИТЕЛЬ ПРОЙДЕН", 34, GamePalette.Gold, TextAnchor.MiddleCenter, true);
                UiKit.SetAnchors(complete.rectTransform, new Vector2(0.08f, 0.55f), new Vector2(0.92f, 0.86f), Vector2.zero, Vector2.zero);
                Text completeBody = UiKit.Label(lesson.transform, "Справочник всегда доступен в меню. Уроки можно пройти заново.",
                    20, GamePalette.Paper, TextAnchor.MiddleCenter, false);
                UiKit.SetAnchors(completeBody.rectTransform, new Vector2(0.08f, 0.25f), new Vector2(0.92f, 0.55f), Vector2.zero, Vector2.zero);
            }
            else
            {
                TutorialStep step = TutorialSteps[tutorialStepIndex];
                Text stepNumber = UiKit.Label(lesson.transform, "ШАГ " + (tutorialStepIndex + 1) + "/" + TutorialSteps.Length +
                    (simulation == null ? " · ДО НАЧАЛА КАМПАНИИ" : " · КАМПАНИЯ: ХОД " + simulation.State.Turn),
                    16, GamePalette.Teal, TextAnchor.MiddleLeft, true);
                UiKit.SetAnchors(stepNumber.rectTransform, new Vector2(0.045f, 0.865f), new Vector2(0.95f, 0.96f), Vector2.zero, Vector2.zero);
                Text lessonTitle = UiKit.Label(lesson.transform, step.Title, 34, GamePalette.Paper, TextAnchor.MiddleLeft, true);
                UiKit.SetAnchors(lessonTitle.rectTransform, new Vector2(0.045f, 0.735f), new Vector2(0.95f, 0.86f), Vector2.zero, Vector2.zero);
                Text lessonBody = UiKit.Label(lesson.transform, step.Body, 21, GamePalette.PaperDim, TextAnchor.UpperLeft, false);
                UiKit.SetAnchors(lessonBody.rectTransform, new Vector2(0.045f, 0.38f), new Vector2(0.95f, 0.70f), Vector2.zero, Vector2.zero);
                bool completed = IsTutorialObjectiveComplete(step.Objective);
                string objectiveStatus = completed
                    ? "✓ Цель выполнена"
                    : simulation == null
                        ? "Ознакомительный режим · задания выполняются в кампании"
                        : "Цель: " + TutorialObjectiveLabel(step.Objective);
                Text status = UiKit.Label(lesson.transform, objectiveStatus, 16, completed ? GamePalette.Success : GamePalette.Muted, TextAnchor.MiddleLeft, false);
                UiKit.SetAnchors(status.rectTransform, new Vector2(0.045f, 0.17f), new Vector2(0.95f, 0.34f), Vector2.zero, Vector2.zero);
                if (!string.IsNullOrEmpty(tutorialFeedback))
                {
                    Text feedback = UiKit.Label(lesson.transform, tutorialFeedback, 14, GamePalette.Gold, TextAnchor.MiddleLeft, true);
                    UiKit.SetAnchors(feedback.rectTransform, new Vector2(0.045f, 0.04f), new Vector2(0.95f, 0.16f), Vector2.zero, Vector2.zero);
                }
            }

            Text progressTitle = UiKit.Label(progress.transform, "ЭТАПЫ", 18, GamePalette.Gold, TextAnchor.MiddleLeft, true);
            UiKit.SetAnchors(progressTitle.rectTransform, new Vector2(0.08f, 0.89f), new Vector2(0.92f, 0.98f), Vector2.zero, Vector2.zero);
            for (int i = 0; i < TutorialSteps.Length; i++)
            {
                float rowTop = 0.85f - i * 0.078f;
                float rowBottom = rowTop - 0.068f;
                bool current = i == tutorialStepIndex;
                bool done = i < tutorialStepIndex || (tutorialCompletedMask & (1 << (int)TutorialSteps[i].Objective)) != 0;
                GameObject row = UiKit.CreateObject("TutorialStep_" + i, progress.transform);
                UiKit.SetAnchors(UiKit.Rect(row), new Vector2(0.05f, rowBottom), new Vector2(0.95f, rowTop), Vector2.zero, Vector2.zero);
                if (current)
                {
                    GameObject accent = UiKit.CreateObject("CurrentStepAccent", row.transform);
                    Image rowImage = accent.AddComponent<Image>();
                    rowImage.color = new Color(GamePalette.Gold.r, GamePalette.Gold.g, GamePalette.Gold.b, 0.75f);
                    rowImage.raycastTarget = false;
                    UiKit.SetAnchors(UiKit.Rect(accent), new Vector2(0f, 0.25f), new Vector2(0f, 0.75f), Vector2.zero, new Vector2(3f, 0f));
                }
                Text rowLabel = UiKit.Label(row.transform, (done ? "✓  " : current ? "›  " : "·  ") + (i + 1) + ". " + TutorialSteps[i].Title,
                    13, done ? GamePalette.Success : current ? GamePalette.Paper : GamePalette.PaperDim, TextAnchor.MiddleLeft, current || done);
                UiKit.SetAnchors(rowLabel.rectTransform, new Vector2(0.05f, 0f), new Vector2(0.96f, 1f), Vector2.zero, Vector2.zero);
            }

            GameObject footer = UiKit.CreateObject("TutorialActions", content.transform);
            UiKit.Horizontal(footer, 0, 8);
            UiKit.SetAnchors(UiKit.Rect(footer), new Vector2(0.035f, 0.06f), new Vector2(0.965f, 0.17f), Vector2.zero, Vector2.zero);
            Button previous = UiKit.Button(footer.transform, "ПРЕДЫДУЩИЙ УРОК", PreviousTutorialStep, GamePalette.InkSoft, 54, 12);
            previous.interactable = tutorialStepIndex > 0 && tutorialStepIndex < TutorialSteps.Length;
            if (simulation != null)
                UiKit.Button(footer.transform, "ОТКРЫТЬ РАЗДЕЛ", OpenTutorialDestination, GamePalette.InkSoft, 48, 13);
            else
                UiKit.Button(footer.transform, "ЭНЦИКЛОПЕДИЯ", ShowEncyclopedia, GamePalette.InkSoft, 48, 13);
            if (tutorialStepIndex < TutorialSteps.Length && !IsTutorialObjectiveComplete(TutorialSteps[tutorialStepIndex].Objective))
                UiKit.Button(footer.transform, "ПРОПУСТИТЬ", SkipTutorialStep, GamePalette.InkSoft, 48, 13);
            UiKit.Button(footer.transform, tutorialStepIndex >= TutorialSteps.Length ? "ПРОЙТИ ЗАНОВО" : "ДАЛЕЕ  →",
                tutorialStepIndex >= TutorialSteps.Length ? (Action)ResetTutorial : AdvanceTutorial, GamePalette.Gold, 48, 14);
        }

        private void OpenTutorialDestination()
        {
            if (simulation == null || tutorialStepIndex >= TutorialSteps.Length) return;
            currentPage = TutorialSteps[tutorialStepIndex].Page;
            MarkTutorialPageVisited(currentPage);
            menuHost.gameObject.SetActive(false);
            RenderGame();
        }

        private void AdvanceTutorial()
        {
            if (tutorialStepIndex >= TutorialSteps.Length) return;
            TutorialStep current = TutorialSteps[tutorialStepIndex];
            if (simulation != null && !IsTutorialObjectiveComplete(current.Objective))
            {
                tutorialFeedback = "Сначала " + TutorialObjectiveLabel(current.Objective) + ". Откройте нужную вкладку или воспользуйтесь кнопкой «Пропустить этот шаг».";
                ShowGuidedTutorial();
                return;
            }
            tutorialFeedback = "";
            tutorialStepIndex = Mathf.Clamp(tutorialStepIndex + 1, 0, TutorialSteps.Length);
            PlayerPrefs.SetInt("jericho.tutorial.step", tutorialStepIndex);
            PlayerPrefs.Save();
            ShowGuidedTutorial();
        }

        private void SkipTutorialStep()
        {
            if (tutorialStepIndex >= TutorialSteps.Length) return;
            MarkTutorialObjective(TutorialSteps[tutorialStepIndex].Objective);
            AdvanceTutorial();
        }

        private bool IsTutorialObjectiveComplete(TutorialObjective objective)
        {
            return (tutorialCompletedMask & (1 << (int)objective)) != 0;
        }

        private void MarkTutorialObjective(TutorialObjective objective)
        {
            tutorialCompletedMask |= 1 << (int)objective;
            tutorialFeedback = "✓ Цель выполнена. Теперь можно перейти дальше.";
            PlayerPrefs.SetInt("jericho.tutorial.completed", tutorialCompletedMask);
            PlayerPrefs.Save();
        }

        private void MarkTutorialPageVisited(ScreenPage page)
        {
            if (tutorialStepIndex >= TutorialSteps.Length) return;
            TutorialObjective objective = TutorialSteps[tutorialStepIndex].Objective;
            bool matching = (page == ScreenPage.City && objective == TutorialObjective.ViewCity) ||
                (page == ScreenPage.Diplomacy && objective == TutorialObjective.ViewDiplomacy) ||
                (page == ScreenPage.Events && objective == TutorialObjective.ViewEvents) ||
                (page == ScreenPage.Victory && objective == TutorialObjective.ViewVictory) ||
                (page == ScreenPage.Chronicle && objective == TutorialObjective.ViewChronicle);
            if (matching) MarkTutorialObjective(objective);
        }

        private static string TutorialObjectiveLabel(TutorialObjective objective)
        {
            switch (objective)
            {
                case TutorialObjective.ViewCity: return "откройте страницу города";
                case TutorialObjective.AssignWorkers: return "измените назначение работников";
                case TutorialObjective.Research: return "выберите технологию";
                case TutorialObjective.CityProject: return "запустите городской проект";
                case TutorialObjective.LandImprovement: return "запустите улучшение клетки";
                case TutorialObjective.RecruitSettler: return "начните набор поселенческого обоза";
                case TutorialObjective.ViewDiplomacy: return "откройте дипломатию";
                case TutorialObjective.ViewEvents: return "откройте журнал событий";
                case TutorialObjective.ViewVictory: return "откройте условия победы";
                default: return "откройте летопись";
            }
        }

        private void PreviousTutorialStep()
        {
            tutorialStepIndex = Mathf.Clamp(tutorialStepIndex - 1, 0, TutorialSteps.Length - 1);
            PlayerPrefs.SetInt("jericho.tutorial.step", tutorialStepIndex);
            PlayerPrefs.Save();
            ShowGuidedTutorial();
        }

        private void ResetTutorial()
        {
            tutorialStepIndex = 0;
            tutorialCompletedMask = 0;
            tutorialFeedback = "";
            PlayerPrefs.SetInt("jericho.tutorial.step", tutorialStepIndex);
            PlayerPrefs.SetInt("jericho.tutorial.completed", tutorialCompletedMask);
            PlayerPrefs.Save();
            ShowGuidedTutorial();
        }

        private void ShowEncyclopedia() { ShowEncyclopedia(null); }

        private void ShowEncyclopedia(string articleId)
        {
            RememberUtilityReturn("encyclopedia");
            PrepareUtilityOverlay("Backgrounds/Archive_Background");
            activeUtilityScreen = "encyclopedia";
            GameObject content = UiKit.ScrollView(menuHost, "EncyclopediaScroll");
            UiKit.Vertical(content, 22, 12);
            GuideArticle article = GameGuide.Find(articleId);
            if (article != null)
            {
                AddUtilityHeader(content.transform, article.Title, ShowEncyclopedia, "←  К СПРАВОЧНИКУ");
                UiKit.Label(content.transform, article.Category.ToUpperInvariant(), 13, GamePalette.Sage, TextAnchor.MiddleLeft, true);
                UiKit.Label(content.transform, article.Summary, 19, GamePalette.PaperDim, TextAnchor.MiddleLeft, true);
                UiKit.Label(content.transform, article.Body, 18, GamePalette.Paper, TextAnchor.UpperLeft, false);
                return;
            }

            AddUtilityHeader(content.transform, "Энциклопедия", CloseEncyclopedia);
            UiKit.Label(content.transform, "Правила, причины событий и советы по развитию", 15, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);
            AddGuideSearchInput(content.transform);
            GameObject controls = UiKit.CreateObject("GuideSearchControls", content.transform);
            UiKit.Horizontal(controls, 0, 8);
            Button category = UiKit.Button(controls.transform, "РАЗДЕЛ · " + GameGuide.Categories[guideCategoryIndex], CycleGuideCategory, GamePalette.InkSoft, 60, 16);
            Button search = UiKit.Button(controls.transform, "НАЙТИ", ShowEncyclopedia, GamePalette.Teal, 60, 19);
            Button clear = UiKit.Button(controls.transform, "ОЧИСТИТЬ", ClearGuideSearch, GamePalette.Ink, 60, 16);
            category.GetComponent<LayoutElement>().flexibleWidth = 1.35f;
            search.GetComponent<LayoutElement>().flexibleWidth = 0.65f;
            clear.GetComponent<LayoutElement>().flexibleWidth = 0.85f;
            GuideArticle[] results = GameGuide.Search(guideSearchQuery, GameGuide.Categories[guideCategoryIndex]);
            UiKit.Label(content.transform, "Найдено статей: " + results.Length, 18, GamePalette.Muted, TextAnchor.MiddleLeft, true);
            for (int i = 0; i < results.Length; i++)
            {
                GuideArticle entry = results[i];
                GameObject card = UiKit.Card(content.transform, entry.Title + " · " + entry.Category);
                UiKit.Label(card.transform, entry.Summary, 20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                UiKit.Button(card.transform, "ЧИТАТЬ", delegate { ShowEncyclopedia(entry.Id); }, GamePalette.Ink, 52, 17);
            }
            if (results.Length == 0)
                UiKit.Label(content.transform, "По этому запросу ничего не найдено. Попробуйте слово из темы: «еда», «миграция», «наука», «договор» или «победа». ",
                    20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
        }

        private void AddGuideSearchInput(Transform parent)
        {
            GameObject field = UiKit.Panel(parent, "GuideSearchField", GamePalette.Paper, 60);
            Image image = field.GetComponent<Image>();
            image.color = GamePalette.Paper;
            RectTransform rect = UiKit.Rect(field);
            rect.anchorMin = new Vector2(0, 0.5f);
            rect.anchorMax = new Vector2(1, 0.5f);
            InputField input = field.AddComponent<InputField>();
            input.targetGraphic = image;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 64;

            GameObject textObject = UiKit.CreateObject("SearchText", field.transform);
            RectTransform textRect = UiKit.Rect(textObject);
            UiKit.SetAnchors(textRect, Vector2.zero, Vector2.one, new Vector2(16, 6), new Vector2(-16, -6));
            Text text = textObject.AddComponent<Text>();
            text.font = UiKit.Font;
            text.fontSize = 22;
            text.color = GamePalette.Ink;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            input.textComponent = text;

            GameObject placeholderObject = UiKit.CreateObject("SearchPlaceholder", field.transform);
            RectTransform placeholderRect = UiKit.Rect(placeholderObject);
            UiKit.SetAnchors(placeholderRect, Vector2.zero, Vector2.one, new Vector2(16, 6), new Vector2(-16, -6));
            Text placeholder = placeholderObject.AddComponent<Text>();
            placeholder.font = UiKit.Font;
            placeholder.fontSize = 21;
            placeholder.color = GamePalette.Muted;
            placeholder.text = "Например: жители уезжают";
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.supportRichText = false;
            input.placeholder = placeholder;
            input.text = guideSearchQuery;
            input.onValueChanged.AddListener(delegate(string value) { guideSearchQuery = value; });
            input.onEndEdit.AddListener(delegate(string value) { guideSearchQuery = value; });
        }

        private void CycleGuideCategory()
        {
            guideCategoryIndex = (guideCategoryIndex + 1) % GameGuide.Categories.Length;
            ShowEncyclopedia();
        }

        private void ClearGuideSearch()
        {
            guideSearchQuery = "";
            guideCategoryIndex = 0;
            ShowEncyclopedia();
        }

        private void CloseEncyclopedia()
        {
            ReturnFromUtilityScreen();
        }

        private void RememberUtilityReturn(string destination)
        {
            if (activeUtilityScreen == destination || activeUtilityScreen == "encyclopedia") return;
            utilityReturnScreen = simulation != null && menuHost != null && !menuHost.gameObject.activeSelf
                ? "game" : activeUtilityScreen;
        }

        private void ReturnFromUtilityScreen()
        {
            switch (utilityReturnScreen)
            {
                case "game":
                    if (simulation != null)
                    {
                        menuHost.gameObject.SetActive(false);
                        RenderGame();
                    }
                    else ShowMenu();
                    return;
                case "tutorial": ShowGuidedTutorial(); return;
                case "new_game": ShowNewCampaignConfirmation(); return;
                case "settings": ShowSettings(); return;
                case "achievements": ShowAchievements(); return;
                case "saves": ShowSaves(); return;
                default: ShowMenu(); return;
            }
        }

        private void CycleDifficulty()
        {
            selectedDifficulty = (Difficulty)(((int)selectedDifficulty + 1) % 4);
            if (activeUtilityScreen == "new_game") ShowNewCampaignConfirmation();
            else ShowMenu();
        }

        private void CycleNation()
        {
            selectedNationId = NationCatalog.GetAt(NationCatalog.IndexOf(selectedNationId) + 1).Id;
            PlayerPrefs.SetString("jericho.campaign.nation", selectedNationId);
            PlayerPrefs.Save();
            if (activeUtilityScreen == "new_game") ShowNewCampaignConfirmation();
            else ShowMenu();
        }

        private void CyclePace()
        {
            selectedPace = selectedPace == GamePace.Standard ? GamePace.Relaxed :
                selectedPace == GamePace.Relaxed ? GamePace.Quick : GamePace.Standard;
            PlayerPrefs.SetInt("jericho.campaign.pace", (int)selectedPace);
            PlayerPrefs.Save();
            if (activeUtilityScreen == "settings") ShowSettings();
            else if (activeUtilityScreen == "new_game") ShowNewCampaignConfirmation();
            else ShowMenu();
        }

        private static string PaceName(GamePace pace)
        {
            switch (pace)
            {
                case GamePace.Relaxed: return "Спокойный · 120 ходов";
                case GamePace.Quick: return "Быстрый · 60 ходов";
                default: return "Стандартный · 80 ходов";
            }
        }

        private static string PaceDescription(GamePace pace)
        {
            switch (pace)
            {
                case GamePace.Relaxed: return "Длинная кампания для освоения систем и неторопливого развития. Мировые кризисы приходят реже: первый — на 24-м ходу, затем раз в 18 ходов.";
                case GamePace.Quick: return "Короткая партия с ранними решениями. Мировые кризисы приходят чаще: первый — на 12-м ходу, затем раз в 9 ходов.";
                default: return "Обычная длина кампании. Первый мировой кризис — на 16-м ходу, затем раз в 12 ходов.";
            }
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

        private static string DifficultyDescription(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Calm: return "ИИ получает 5% меньше выпуска, догоняющую науку при отставании и меняет долгосрочный курс реже — примерно раз в 7 ходов.";
                case Difficulty.Strong: return "ИИ получает 12,5% больше выпуска, 5% быстрее исследует технологии и начинает с дополнительной административной ёмкостью. Соперники настойчивее ведут выбранный путь победы.";
                case Difficulty.Trial: return "ИИ получает 30% больше выпуска, 10% быстрее исследует технологии и начинает с двумя дополнительными единицами административной ёмкости. Соперники ведут специализированные планы победы и могут мобилизовать до трёх отрядов в войне.";
                default: return "Базовый выпуск. ИИ обычно держит курс около 6 ходов, но раньше реагирует на голод, войну и затяжной дефицит.";
            }
        }

        private static string AiGoalName(string goal)
        {
            switch (goal)
            {
                case "science": return "наука";
                case "trade": return "торговля и связи";
                case "culture": return "культурное влияние";
                case "defense": return "оборона";
                case "recovery": return "восстановление";
                case "expansion": return "новые города";
                case "quality": return "качество жизни";
                default: return "развитие";
            }
        }

        private string AiDecisionExplanation(CivilizationState rival)
        {
            if (rival == null) return "Соперник ещё не сформировал стратегический курс.";
            string goal = AiGoalName(rival.StrategicGoal);
            string reason = rival.StrategicGoalScore > 0
                ? "Курс «" + goal + "» получил " + rival.StrategicGoalScore + " очков; следующий — «" + AiGoalName(rival.StrategicRunnerUpGoal) + "» с " + rival.StrategicRunnerUpScore + "."
                : "Долгосрочный курс соперника — «" + goal + "».";
            reason += " Оценка учитывает специализацию лидера, путь победы, учреждения, состояние городов и приоритеты кризиса.";
            bool atWar = false;
            for (int i = 0; i < simulation.State.Conflicts.Length; i++)
            {
                ConflictState conflict = simulation.State.Conflicts[i];
                if (conflict.Active && conflict.Phase == 2 &&
                    (conflict.AggressorId == rival.Id || conflict.DefenderId == rival.Id)) atWar = true;
            }
            int foodRisk = 0;
            for (int i = 0; i < simulation.State.Cities.Length; i++)
            {
                CityState city = simulation.State.Cities[i];
                if (city.OwnerId != rival.Id) continue;
                CityYield yield = EconomyCalculator.Calculate(simulation.State, city);
                if (city.FoodShortageTurns > 0 || city.FoodTenths + yield.FoodTenths < city.PopulationTenths * 2) foodRisk++;
            }
            if (atWar) reason += " Идёт война: оборона временно получает больший приоритет.";
            if (foodRisk > 0) reason += " Городов с риском нехватки еды: " + foodRisk + ".";
            if (rival.ConsecutiveDeficitTurns > 0 || rival.DebtTenths > 0)
                reason += " Дефицит казны и долг усиливают курс восстановления.";
            return reason;
        }

        private void StartNewCampaign()
        {
            ShowNewCampaignConfirmation();
        }

        private void ShowNewCampaignConfirmation()
        {
            RememberUtilityReturn("new_game");
            PrepareUtilityOverlay("Backgrounds/Campaign_Background");
            activeUtilityScreen = "new_game";
            GameObject panel = UtilityPanel("NewCampaignPanel", new Vector2(0.035f, 0.035f), new Vector2(0.965f, 0.965f));
            UiKit.Vertical(panel, 10, 8);
            AddUtilityHeader(panel.transform, "Новая кампания", ShowMenu, "В МЕНЮ");
            UiKit.Label(panel.transform, "Четыре культуры · разные лидеры, районы и бонусы", 14, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);
            NationDefinition selectedNation = NationCatalog.Get(selectedNationId);
            BuildingDefinition uniqueDistrict = RulesCatalog.Building(selectedNation.UniqueDistrictBuildingId);
            GameObject nationRow = UiKit.CreateObject("NationChoices", panel.transform);
            LayoutElement nationRowLayout = nationRow.AddComponent<LayoutElement>();
            nationRowLayout.minHeight = 220f;
            nationRowLayout.preferredHeight = 220f;
            UiKit.Horizontal(nationRow, 0, 12);
            for (int i = 0; i < NationCatalog.All.Length; i++)
            {
                NationDefinition nation = NationCatalog.All[i];
                bool isSelected = nation.Id == selectedNation.Id;
                GameObject choice = UiKit.CreateObject("NationChoice_" + nation.Id, nationRow.transform);
                Image choiceImage = choice.AddComponent<Image>();
                Color choiceTint = new Color(GamePalette.InkSoft.r, GamePalette.InkSoft.g, GamePalette.InkSoft.b, isSelected ? 0.25f : 0.06f);
                choiceImage.color = Color.white;
                if (isSelected)
                {
                    Outline outline = choice.AddComponent<Outline>();
                    outline.effectColor = new Color(GamePalette.Gold.r, GamePalette.Gold.g, GamePalette.Gold.b, 0.85f);
                    outline.effectDistance = new Vector2(2f, -2f);
                    outline.useGraphicAlpha = false;
                }
                Button choose = choice.AddComponent<Button>();
                choose.targetGraphic = choiceImage;
                choose.transition = Selectable.Transition.ColorTint;
                ColorBlock choiceColors = choose.colors;
                choiceColors.normalColor = choiceTint;
                choiceColors.highlightedColor = Color.Lerp(choiceTint, Color.white, 0.12f);
                choiceColors.pressedColor = Color.Lerp(choiceTint, Color.black, 0.16f);
                choose.colors = choiceColors;
                string nationId = nation.Id;
                choose.onClick.AddListener(delegate
                {
                    selectedNationId = nationId;
                    PlayerPrefs.SetString("jericho.campaign.nation", selectedNationId);
                    PlayerPrefs.Save();
                    ShowNewCampaignConfirmation();
                });
                LayoutElement choiceLayout = choice.AddComponent<LayoutElement>();
                choiceLayout.flexibleWidth = 1f;
                choiceLayout.flexibleHeight = 1f;
                UiKit.Vertical(choice, 8, 3);
                AddArtwork(choice.transform, nation.LeaderArtKey, 78);
                UiKit.Label(choice.transform, (isSelected ? "✓  " : "") + nation.NameRu, 17,
                    isSelected ? GamePalette.Gold : GamePalette.Paper, TextAnchor.MiddleCenter, true);
                UiKit.Label(choice.transform, nation.BonusTitle + " · " + nation.BonusDescription, 13, GamePalette.PaperDim, TextAnchor.MiddleCenter, false);
                BuildingDefinition district = RulesCatalog.Building(nation.UniqueDistrictBuildingId);
                UiKit.Label(choice.transform, district == null ? "Особый район" : "РАЙОН · " + district.NameRu, 12, GamePalette.Sage, TextAnchor.MiddleCenter, true);
            }

            GameObject columns = UiKit.CreateObject("CampaignSetupColumns", panel.transform);
            LayoutElement columnsLayout = columns.AddComponent<LayoutElement>();
            columnsLayout.flexibleHeight = 1f;
            UiKit.Horizontal(columns, 0, 14);

            GameObject culture = UtilityCard(columns.transform, "ЛИДЕР И УНИКАЛЬНЫЕ РАЙОНЫ · " + selectedNation.NameRu.ToUpperInvariant());
            UiKit.Label(culture.transform, selectedNation.LeaderNameRu + " · " + selectedNation.LeaderTitleRu, 19, GamePalette.Gold, TextAnchor.MiddleLeft, true);
            UiKit.Label(culture.transform, selectedNation.LeaderHistoricalNoteRu, 14, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Label(culture.transform, "Уникальный район · " + uniqueDistrict.NameRu + " — " + BuildingEffectSummary(uniqueDistrict) +
                ". Учебный район · " + selectedNation.CampusNameRu + ": " + selectedNation.CampusBonusDescriptionRu,
                14, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);

            GameObject rules = UtilityCard(columns.transform, "УСЛОВИЯ ПАРТИИ");
            GameObject choices = UiKit.CreateObject("DifficultyAndPace", rules.transform);
            UiKit.Horizontal(choices, 0, 10);
            UiKit.Button(choices.transform, "СЛОЖНОСТЬ · " + DifficultyName(selectedDifficulty), CycleDifficulty, GamePalette.Ink, 50, 15);
            UiKit.Button(choices.transform, "ТЕМП · " + PaceName(selectedPace), CyclePace, GamePalette.Ink, 50, 15);
            UiKit.Label(rules.transform, CompactHint(DifficultyDescription(selectedDifficulty), 105), 13, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);
            UiKit.Label(rules.transform, CompactHint(PaceDescription(selectedPace), 105), 13, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);
            GameObject footer = UiKit.CreateObject("CampaignSetupFooter", panel.transform);
            UiKit.Horizontal(footer, 0, 12);
            UiKit.Button(footer.transform, "УСЛОВИЯ ПОБЕДЫ", delegate { ShowEncyclopedia("victory-overview"); }, GamePalette.InkSoft, 48, 14);
            if (SaveService.HasSave) UiKit.Button(footer.transform, "СОХРАНЕНИЯ", ShowSaves, GamePalette.InkSoft, 48, 14);
            Button begin = UiKit.Button(footer.transform, "НАЧАТЬ ПАРТИЮ", CreateNewCampaign, GamePalette.Gold, 50, 17);
            begin.GetComponent<LayoutElement>().flexibleWidth = 1.25f;
        }

        private void CreateNewCampaign()
        {
            int seed = UnityEngine.Random.Range(100000, 999999);
            simulation = GameSimulation.NewCampaign(seed, selectedDifficulty, selectedPace, selectedNationId);
            SetMusicEra(simulation.State.Civilization(simulation.State.HumanCivilizationId).Era);
            selectedCityId = simulation.FindCapital(simulation.State.HumanCivilizationId);
            selectedUnitId = -1;
            currentPage = ScreenPage.Overview;
            notice = "Новая кампания создана · " + NationCatalog.Get(selectedNationId).NameRu + " · мир " + seed + ".";
            if (!SaveService.SaveAuto(simulation.State)) notice += " Автосохранение не удалось; проверьте свободное место и сохраните вручную.";
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
            MarkTutorialPageVisited(currentPage);
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
            navHost.gameObject.SetActive(!simulation.State.IsFinished);
            actionHost.gameObject.SetActive(!simulation.State.IsFinished);
            UpdateHeader();
            DestroyChildren(pageHost);
            GameObject shell = UiKit.CreateObject("CampaignShell", pageHost);
            UiKit.SetAnchors(UiKit.Rect(shell), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            UiKit.Horizontal(shell, 8, 8);

            GameObject mapPane = UiKit.CreateObject("WorldMapPane", shell.transform);
            LayoutElement mapPaneLayout = mapPane.AddComponent<LayoutElement>();
            mapPaneLayout.flexibleWidth = 2.25f;
            mapPaneLayout.flexibleHeight = 1f;
            mapPaneLayout.minWidth = 0f;
            AddMap(mapPane.transform);

            GameObject detailPane = UiKit.CreateObject("StrategyPanel", shell.transform);
            Image detailBackground = detailPane.AddComponent<Image>();
            detailBackground.sprite = LoadArtwork(PageBackgroundArtwork(currentPage));
            detailBackground.color = Color.white;
            detailBackground.raycastTarget = false;
            LayoutElement detailPaneLayout = detailPane.GetComponent<LayoutElement>();
            if (detailPaneLayout == null) detailPaneLayout = detailPane.AddComponent<LayoutElement>();
            detailPaneLayout.flexibleWidth = 1f;
            detailPaneLayout.flexibleHeight = 1f;
            detailPaneLayout.minWidth = 300f;
            GameObject detailShade = UiKit.CreateObject("StrategyPanelShade", detailPane.transform);
            Image detailShadeImage = detailShade.AddComponent<Image>();
            detailShadeImage.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.46f);
            detailShadeImage.raycastTarget = false;
            UiKit.SetAnchors(UiKit.Rect(detailShade), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            GameObject detailContent = UiKit.ScrollView(detailPane.transform, "StrategyPanelScroll");
            UiKit.Vertical(detailContent, 12, 10);
            if (currentPage != ScreenPage.Overview)
                RenderSelectedCellContext(detailContent.transform, simulation.State.Civilization(simulation.State.HumanCivilizationId));
            switch (currentPage)
            {
                case ScreenPage.Overview: RenderOverview(detailContent.transform); break;
                case ScreenPage.City: RenderCity(detailContent.transform); break;
                case ScreenPage.Research: RenderResearch(detailContent.transform); break;
                case ScreenPage.Diplomacy: RenderDiplomacy(detailContent.transform); break;
                case ScreenPage.Laws: RenderLaws(detailContent.transform); break;
                case ScreenPage.Events: RenderEvents(detailContent.transform); break;
                case ScreenPage.Victory: RenderVictory(detailContent.transform); break;
                case ScreenPage.Chronicle: RenderChronicle(detailContent.transform); break;
            }
        }

        private static string PageBackgroundArtwork(ScreenPage page)
        {
            switch (page)
            {
                case ScreenPage.City: return "Backgrounds/City_Background";
                case ScreenPage.Research: return "Backgrounds/Research_Background";
                case ScreenPage.Diplomacy: return "Backgrounds/Diplomacy_Background";
                case ScreenPage.Laws: return "Backgrounds/Archive_Background";
                case ScreenPage.Events: return "Backgrounds/Events_Background";
                case ScreenPage.Victory: return "Backgrounds/Victory_Background";
                case ScreenPage.Chronicle: return "Backgrounds/Archive_Background";
                default: return "Backgrounds/City_Background";
            }
        }

        private void UpdateHeader()
        {
            GameState state = simulation.State;
            CivilizationState player = state.Civilization(state.HumanCivilizationId);
            titleLabel.text = "JERICHO     ·     ХОД " + state.Turn + "/" + state.MaximumTurn + "     ·     " + EraName(player.Era);
            resourceLabel.text = player.Name + "  ·  Казна " + Format(player.TreasuryTenths) +
                "  ·  Знания " + Format(player.KnowledgeTenths) +
                "  ·  Влияние " + Format(player.InfluenceTenths) +
                "  ·  Культура " + player.CulturePrestige + "  ·  " + AchievementService.SelectedTitle;
            noticeLabel.text = notice;
            noticeLabel.color = notice.IndexOf("не", StringComparison.OrdinalIgnoreCase) >= 0 || notice.IndexOf("нехват", StringComparison.OrdinalIgnoreCase) >= 0
                ? GamePalette.Danger : GamePalette.Muted;
        }

        private void ShowPageHelp()
        {
            string articleId;
            CityState city = SelectedCity();
            switch (currentPage)
            {
                case ScreenPage.City:
                    CityYield forecast = city == null ? null : simulation.PreviewCity(city.Id);
                    articleId = forecast != null && forecast.FoodTenths < city.PopulationTenths ? "food-growth" :
                        city != null && (city.Health < 50 || city.Trust < 40 || city.Safety < 50) ? "cities-services" :
                        city != null && (city.Project == null || city.Project.Kind == ProjectKind.None) ? "projects" : "workers";
                    break;
                case ScreenPage.Research: articleId = "research"; break;
                case ScreenPage.Diplomacy: articleId = "diplomacy"; break;
                case ScreenPage.Laws: articleId = "laws"; break;
                case ScreenPage.Events:
                    articleId = simulation.State.Crisis != null && simulation.State.Crisis.Kind != CrisisKind.None && !simulation.State.Crisis.Resolved
                        ? "global-crisis" : "events";
                    break;
                case ScreenPage.Victory: articleId = "victory-overview"; break;
                case ScreenPage.Chronicle: articleId = "chronicle"; break;
                default:
                    articleId = simulation.State.Crisis != null && simulation.State.Crisis.Kind != CrisisKind.None && !simulation.State.Crisis.Resolved
                        ? "global-crisis" : city != null && city.FoodTenths < city.PopulationTenths ? "food-growth" : "turn-cycle";
                    break;
            }
            ShowEncyclopedia(articleId);
        }

        private static string CompactHint(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
            int cut = value.LastIndexOf(' ', maxLength - 1);
            if (cut < maxLength / 2) cut = maxLength - 1;
            return value.Substring(0, cut).TrimEnd('.', ',', ';', ':', ' ') + "…";
        }

        private void RenderOverview(Transform parent)
        {
            GameState state = simulation.State;
            CivilizationState player = state.Civilization(state.HumanCivilizationId);
            NationDefinition nation = player == null ? null : NationCatalog.Get(player.NationId);
            RenderSelectedCellContext(parent, player);
            if (nation != null)
            {
                GameObject leaderCard = UiKit.Card(parent, "ВАШ НАРОД · " + nation.NameRu);
                GameObject leaderRow = UiKit.CreateObject("PlayerLeader", leaderCard.transform);
                UiKit.Horizontal(leaderRow, 0, 8);
                AddCityBuildingArtwork(leaderRow.transform, nation.LeaderArtKey, 72);
                GameObject leaderText = UiKit.CreateObject("LeaderDescription", leaderRow.transform);
                UiKit.Vertical(leaderText, 0, 3);
                LayoutElement leaderTextLayout = leaderText.AddComponent<LayoutElement>();
                leaderTextLayout.flexibleWidth = 1f;
                UiKit.Label(leaderText.transform, nation.LeaderNameRu + " · " + nation.LeaderTitleRu, 17, GamePalette.Paper, TextAnchor.MiddleLeft, true);
                UiKit.Label(leaderText.transform, nation.BonusTitle + " · " + nation.BonusDescription, 13, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            }

            CityState selected = SelectedCity();
            if (selected != null)
            {
                GameObject card = UiKit.Card(parent, "СТОЛИЦА · " + selected.Name);
                UiKit.Label(card.transform, "Население " + Format(selected.PopulationTenths) + " / " + Format(selected.HousingTenths) +
                    "\nЕда " + Format(selected.FoodTenths) + " · здоровье " + selected.Health + " · доверие " + selected.Trust,
                    15, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                CityYield yield = simulation.PreviewCity(selected.Id);
                UiKit.Label(card.transform, "Прогноз за ход\nЕда " + Format(yield.FoodTenths) + " · производство " + yield.Production +
                    " · знания " + Format(yield.KnowledgeTenths) + " · казна " + Format(yield.TreasuryTenths), 13, GamePalette.Muted, TextAnchor.MiddleLeft, false);
                UiKit.Button(card.transform, "ОТКРЫТЬ ГОРОД", delegate { currentPage = ScreenPage.City; RenderGame(); }, GamePalette.Ink, 48, 13);
            }

            if (tutorialStepIndex < TutorialSteps.Length)
            {
                TutorialStep step = TutorialSteps[tutorialStepIndex];
                GameObject tutorial = UiKit.Card(parent, "ПУТЕВОДИТЕЛЬ · " + (tutorialStepIndex + 1) + "/" + TutorialSteps.Length);
                UiKit.Label(tutorial.transform, step.Title + "\n" + TutorialObjectiveLabel(step.Objective), 14, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                UiKit.Button(tutorial.transform, "ПРОДОЛЖИТЬ ОБУЧЕНИЕ", ShowTutorial, GamePalette.Gold, 48, 13);
            }

            GameObject worldCard = UiKit.Card(parent, "МИР И ЦЕЛИ");
            UiKit.Label(worldCard.transform, CountCities(state, player.Id) + " городов · " + (state.Civilizations.Length - 1) + " соседних держав\n" +
                "Текущий курс: " + AiGoalName(player.StrategicGoal) + " · эпоха " + EraName(player.Era),
                14, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Button(worldCard.transform, "ДИПЛОМАТИЯ", delegate { currentPage = ScreenPage.Diplomacy; RenderGame(); }, GamePalette.Ink, 46, 12);
            UiKit.Button(worldCard.transform, "НАУКА И ПОБЕДА", delegate { currentPage = ScreenPage.Research; RenderGame(); }, GamePalette.Ink, 46, 12);
        }

        private void RenderSelectedCellContext(Transform parent, CivilizationState player)
        {
            if (player == null || selectedQ < 0 || selectedR < 0) return;
            HexCell cell = simulation.State.Cell(selectedQ, selectedR);
            if (cell == null || (cell.ExploredByMask & (1 << player.Id)) == 0 || CityAt(cell.Q, cell.R) != null) return;

            GameObject card = UiKit.Card(parent, "ВЫБРАННАЯ КЛЕТКА · " + TerrainName(cell.Terrain));
            GameObject row = UiKit.CreateObject("SelectedCellDetails", card.transform);
            UiKit.Horizontal(row, 0, 8);
            string terrainArtwork = TerrainArtwork(cell.Terrain);
            if (!string.IsNullOrEmpty(terrainArtwork)) AddMapArtwork(row.transform, terrainArtwork, new Vector2(78, 68), Vector2.zero, Color.white);
            GameObject detail = UiKit.CreateObject("SelectedCellText", row.transform);
            UiKit.Vertical(detail, 0, 2);
            LayoutElement detailLayout = detail.AddComponent<LayoutElement>();
            detailLayout.flexibleWidth = 1f;
            bool visible = (cell.VisibleToMask & (1 << player.Id)) != 0;
            string ownership = cell.OwnerId == player.Id ? "В ваших владениях" : cell.OwnerId >= 0 ? "Территория соседа" : "Ничейная земля";
            UiKit.Label(detail.transform, ownership + " · координаты " + cell.Q + ", " + cell.R, 14, GamePalette.Paper, TextAnchor.MiddleLeft, true);
            string resource = visible && cell.Resource != ResourceKind.None ? ResourceName(cell.Resource) : "ресурсов не найдено";
            UiKit.Label(detail.transform, resource + (visible && cell.Improvement != ImprovementKind.None ? " · " + ImprovementName(cell.Improvement) : ""),
                13, GamePalette.Muted, TextAnchor.MiddleLeft, false);

            if (cell.OwnerId == player.Id)
            {
                CityState city = SelectedCity();
                if (city != null && HexGrid.Distance(cell.Q, cell.R, city.Q, city.R) <= 2)
                    UiKit.Button(card.transform, "РАБОТЫ НА КЛЕТКЕ", delegate { currentPage = ScreenPage.City; RenderGame(); }, GamePalette.Ink, 44, 12);
            }
        }

        private static string ResourceName(ResourceKind resource)
        {
            switch (resource)
            {
                case ResourceKind.Iron: return "Железо";
                case ResourceKind.Herbs: return "Лекарственные травы";
                case ResourceKind.Salt: return "Соль";
                case ResourceKind.FertileSoil: return "Плодородная почва";
                default: return "ресурсов не найдено";
            }
        }

        private static string ImprovementName(ImprovementKind improvement)
        {
            for (int i = 0; i < RulesCatalog.Improvements.Length; i++)
                if (RulesCatalog.Improvements[i].Kind == improvement) return RulesCatalog.Improvements[i].NameRu;
            return improvement.ToString();
        }

        private static int playerId(GameState state) { return state.HumanCivilizationId; }

        private void AddMap(Transform parent)
        {
            GameObject stage = UiKit.CreateObject("MapStage", parent);
            UiKit.SetAnchors(UiKit.Rect(stage), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            LayoutElement stageLayout = stage.AddComponent<LayoutElement>();
            stageLayout.flexibleWidth = 1f;
            stageLayout.flexibleHeight = 1f;
            Image stageImage = stage.AddComponent<Image>();
            stageImage.color = GamePalette.Hex("1b211d");
            stageImage.raycastTarget = false;

            GameObject mapHeader = UiKit.CreateObject("MapIdentity", stage.transform);
            Image headerImage = mapHeader.AddComponent<Image>();
            headerImage.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.70f);
            headerImage.raycastTarget = false;
            UiKit.SetAnchors(UiKit.Rect(mapHeader), new Vector2(0.02f, 0.948f), new Vector2(0.98f, 0.998f), Vector2.zero, Vector2.zero);
            Text mapTitle = UiKit.Label(mapHeader.transform, "ИЕРИХОНСКАЯ ДОЛИНА  ·  СЕМЯ " + simulation.State.Seed, 14, GamePalette.Paper, TextAnchor.MiddleLeft, true);
            UiKit.SetAnchors(mapTitle.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 0), new Vector2(-16, 0));

            GameObject overlayRow = UiKit.CreateObject("MapOverlayControls", stage.transform);
            UiKit.Horizontal(overlayRow, 3, 6);
            UiKit.SetAnchors(UiKit.Rect(overlayRow), new Vector2(0.02f, 0.875f), new Vector2(0.98f, 0.935f), Vector2.zero, Vector2.zero);
            AddMapOverlayButton(overlayRow.transform, MapOverlay.Political, "ВЛАДЕНИЕ");
            AddMapOverlayButton(overlayRow.transform, MapOverlay.Food, "ЕДА");
            AddMapOverlayButton(overlayRow.transform, MapOverlay.Production, "ПРОИЗВОДСТВО");
            AddMapOverlayButton(overlayRow.transform, MapOverlay.Defense, "ОБОРОНА");
            GameObject scrollObject = UiKit.CreateObject("MapScroll", stage.transform);
            UiKit.SetAnchors(UiKit.Rect(scrollObject), new Vector2(0.02f, 0.075f), new Vector2(0.98f, 0.865f), Vector2.zero, Vector2.zero);
            ScrollRect scroll = scrollObject.AddComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 64f;
            GameObject viewport = UiKit.CreateObject("MapViewport", scrollObject.transform);
            RectTransform viewportRect = UiKit.Rect(viewport);
            activeMapViewport = viewportRect;
            UiKit.SetAnchors(viewportRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image background = viewport.AddComponent<Image>();
            background.color = GamePalette.Hex("31565A");
            viewport.AddComponent<RectMask2D>();
            GameObject content = UiKit.CreateObject("HexContent", viewport.transform);
            RectTransform contentRect = UiKit.Rect(content);
            activeMapContent = contentRect;
            contentRect.localScale = Vector3.one * mapZoom;
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(0, 1);
            contentRect.pivot = new Vector2(0, 1);
            GameState state = simulation.State;
            activeMapMinQ = state.Cells.Min(cell => cell.Q);
            activeMapMinR = state.Cells.Min(cell => cell.R);
            int maxQ = state.Cells.Max(cell => cell.Q);
            int maxR = state.Cells.Max(cell => cell.R);
            float contentWidth = 36f + (maxQ - activeMapMinQ) * MapHexQStep + (maxR - activeMapMinR) * MapHexRStepX + MapHexWidth;
            float contentHeight = 38f + (maxR - activeMapMinR) * MapHexRStepY + MapHexHeight;
            contentRect.sizeDelta = new Vector2(contentWidth, contentHeight);
            contentRect.anchoredPosition = Vector2.zero;
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            activeMapScroll = scroll;
            Canvas.ForceUpdateCanvases();
            float centeredX = Mathf.Max(0f, (viewportRect.rect.width - contentWidth * mapZoom) * 0.5f);
            float centeredY = Mathf.Max(0f, (viewportRect.rect.height - contentHeight * mapZoom) * 0.5f);
            contentRect.anchoredPosition = new Vector2(centeredX, -centeredY);
            scroll.normalizedPosition = mapScrollPosition;
            int humanMask = 1 << state.HumanCivilizationId;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell cell = state.Cells[i];
                CreateHex(content.transform, cell, humanMask);
            }

            Text mapHelp = UiKit.Label(stage.transform, "⌂ город  ·  районы и улучшения на гексах  ·  выберите отряд, затем клетку для движения  ·  туман скрывает неразведанные земли", 12, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);
            UiKit.SetAnchors(mapHelp.rectTransform, new Vector2(0.025f, 0.012f), new Vector2(0.68f, 0.067f), Vector2.zero, Vector2.zero);
            GameObject zoomRow = UiKit.CreateObject("MapZoomControls", stage.transform);
            UiKit.Horizontal(zoomRow, 4, 6);
            UiKit.SetAnchors(UiKit.Rect(zoomRow), new Vector2(0.70f, 0.008f), new Vector2(0.98f, 0.07f), Vector2.zero, Vector2.zero);
            HorizontalLayoutGroup zoomLayout = zoomRow.GetComponent<HorizontalLayoutGroup>();
            zoomLayout.childForceExpandWidth = false;
            Button zoomOut = UiKit.Button(zoomRow.transform, "−", delegate { SetMapZoom(mapZoom - 0.1f, true); }, GamePalette.Ink, 42, 18);
            Button zoomIn = UiKit.Button(zoomRow.transform, "+", delegate { SetMapZoom(mapZoom + 0.1f, true); }, GamePalette.Ink, 42, 18);
            Button zoomReset = UiKit.Button(zoomRow.transform, "СБРОС", delegate { SetMapZoom(1f, true); }, GamePalette.Ink, 42, 12);
            SetToolbarButtonWidth(zoomOut, 48f);
            SetToolbarButtonWidth(zoomIn, 48f);
            SetToolbarButtonWidth(zoomReset, 74f);
            activeMapZoomLabel = UiKit.Label(zoomRow.transform, Mathf.RoundToInt(mapZoom * 100f) + "%", 13, GamePalette.PaperDim, TextAnchor.MiddleCenter, true);
            activeMapZoomLabel.GetComponent<LayoutElement>().flexibleWidth = 1f;
        }

        private static void SetToolbarButtonWidth(Button button, float width)
        {
            LayoutElement layout = button.GetComponent<LayoutElement>();
            if (layout == null) return;
            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.flexibleWidth = 0f;
        }

        private void AddMapOverlayButton(Transform parent, MapOverlay overlay, string label)
        {
            Color color = mapOverlay == overlay ? GamePalette.Gold : GamePalette.InkSoft;
            UiKit.Button(parent, label, delegate
            {
                mapOverlay = overlay;
                PlayerPrefs.SetInt("jericho.map.overlay", (int)mapOverlay);
                PlayerPrefs.Save();
                RenderGame();
            }, color, 50, 14);
        }

        private string MapOverlayLegend()
        {
            switch (mapOverlay)
            {
                case MapOverlay.Food: return "Слой еды: цвет показывает пищевой потенциал рельефа; улучшение учитывается только на видимой клетке.";
                case MapOverlay.Production: return "Слой производства: цвет показывает ремесленный потенциал; улучшение учитывается только на видимой клетке.";
                case MapOverlay.Defense: return "Слой обороны: показаны ваши отряды, укрепления и только видимые вражеские отряды.";
                default: return "Слой владения: цвет территории показывает владельца клетки.";
            }
        }

        private Color MapOverlayColor(HexCell cell, bool explored, bool visible)
        {
            if (!explored) return new Color(0.15f, 0.22f, 0.22f, 1f);
            if (mapOverlay == MapOverlay.Political) return TerrainColor(cell.Terrain);
            if (mapOverlay == MapOverlay.Defense)
            {
                if (!visible) return Color.Lerp(TerrainColor(cell.Terrain), GamePalette.Ink, 0.48f);
                UnitState unit = UnitAt(cell.Q, cell.R);
                if (unit != null) return unit.OwnerId == simulation.State.HumanCivilizationId ? GamePalette.Gold : GamePalette.Danger;
                if (cell.Improvement == ImprovementKind.Fortification && cell.Damage < 75) return GamePalette.Rust;
                return GamePalette.Hex("34443F");
            }

            int score;
            if (mapOverlay == MapOverlay.Food)
            {
                switch (cell.Terrain)
                {
                    case TerrainKind.Meadow: score = 4; break;
                    case TerrainKind.River:
                    case TerrainKind.Coast: score = 3; break;
                    case TerrainKind.Plains: score = 2; break;
                    case TerrainKind.Arid:
                    case TerrainKind.Swamp: score = 0; break;
                    default: score = 1; break;
                }
                if (visible && cell.Damage < 75)
                {
                    if (cell.Improvement == ImprovementKind.Farm) score += 2;
                    if (cell.Improvement == ImprovementKind.IrrigatedFarm) score += 3;
                }
                return score >= 5 ? GamePalette.Hex("B7D276") : score >= 3 ? GamePalette.Hex("769F68") : score >= 1 ? GamePalette.Hex("5B705D") : GamePalette.Hex("705B48");
            }

            switch (cell.Terrain)
            {
                case TerrainKind.Hills:
                case TerrainKind.Forest: score = 4; break;
                case TerrainKind.Plains:
                case TerrainKind.Meadow: score = 2; break;
                default: score = 1; break;
            }
            if (visible && cell.Damage < 75)
            {
                if (cell.Improvement == ImprovementKind.Workshop) score += 3;
                if (cell.Improvement == ImprovementKind.Forestry) score += 2;
            }
            return score >= 5 ? GamePalette.Hex("D6A96B") : score >= 3 ? GamePalette.Hex("A78457") : GamePalette.Hex("625C4D");
        }

        private void SetMapZoom(float value, bool persist)
        {
            if (activeMapScroll != null) mapScrollPosition = activeMapScroll.normalizedPosition;
            mapZoom = Mathf.Clamp(value, 0.65f, 1.5f);
            if (activeMapContent != null) activeMapContent.localScale = Vector3.one * mapZoom;
            if (activeMapZoomLabel != null) activeMapZoomLabel.text = Mathf.RoundToInt(mapZoom * 100f) + "%";
            if (activeMapScroll != null)
            {
                Canvas.ForceUpdateCanvases();
                activeMapScroll.horizontalNormalizedPosition = mapScrollPosition.x;
                activeMapScroll.verticalNormalizedPosition = mapScrollPosition.y;
            }
            if (persist)
            {
                PlayerPrefs.SetFloat("jericho.map.zoom", mapZoom);
                PlayerPrefs.Save();
            }
            if (currentPage == ScreenPage.Overview && simulation != null && !presentationBusy) RenderGame();
        }

        private void UpdateMapPinch()
        {
            if (Input.touchCount != 2)
            {
                if (previousPinchDistance > 0f)
                {
                    PlayerPrefs.SetFloat("jericho.map.zoom", mapZoom);
                    PlayerPrefs.Save();
                }
                previousPinchDistance = 0f;
                return;
            }
            if (activeMapContent == null || activeMapViewport == null) { previousPinchDistance = 0f; return; }
            Touch first = Input.GetTouch(0);
            Touch second = Input.GetTouch(1);
            Vector2 center = (first.position + second.position) * 0.5f;
            if (!RectTransformUtility.RectangleContainsScreenPoint(activeMapViewport, center, null))
            {
                previousPinchDistance = 0f;
                return;
            }
            float distance = Vector2.Distance(first.position, second.position);
            if (previousPinchDistance > 0f && distance > 1f)
            {
                float ratio = distance / previousPinchDistance;
                if (Mathf.Abs(ratio - 1f) > 0.008f)
                {
                    mapZoom = Mathf.Clamp(mapZoom * ratio, 0.65f, 1.5f);
                    activeMapContent.localScale = Vector3.one * mapZoom;
                    if (activeMapScroll != null) mapScrollPosition = activeMapScroll.normalizedPosition;
                }
            }
            previousPinchDistance = distance;
            if (first.phase == TouchPhase.Ended || second.phase == TouchPhase.Ended ||
                first.phase == TouchPhase.Canceled || second.phase == TouchPhase.Canceled)
            {
                PlayerPrefs.SetFloat("jericho.map.zoom", mapZoom);
                PlayerPrefs.Save();
                previousPinchDistance = 0f;
            }
        }

        private void CreateHex(Transform parent, HexCell cell, int humanMask)
        {
            GameObject tile = UiKit.CreateObject("Hex_" + cell.Q + "_" + cell.R, parent);
            RectTransform rect = UiKit.Rect(tile);
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(MapHexWidth, MapHexHeight);
            rect.anchoredPosition = new Vector2(18 + (cell.Q - activeMapMinQ) * MapHexQStep + (cell.R - activeMapMinR) * MapHexRStepX,
                -20 - (cell.R - activeMapMinR) * MapHexRStepY);
            HexTileGraphic graphic = tile.AddComponent<HexTileGraphic>();
            bool explored = (cell.ExploredByMask & humanMask) != 0;
            bool visible = (cell.VisibleToMask & humanMask) != 0;
            graphic.color = MapOverlayColor(cell, explored, visible);
            if (!visible && explored) graphic.color = Color.Lerp(graphic.color, GamePalette.Ink, 0.42f);
            Color ownerColor = OwnerColor(cell.OwnerId);
            if (cell.OwnerId >= 0 && visible && mapOverlay == MapOverlay.Political) graphic.color = Color.Lerp(graphic.color, ownerColor, 0.24f);
            bool selected = (cell.Q == selectedQ && cell.R == selectedR);
            CityState city = visible ? CityAt(cell.Q, cell.R) : null;
            CityState districtCity = !visible || string.IsNullOrEmpty(cell.DistrictBuildingId) ? null : simulation.State.City(cell.DistrictCityId);
            CityState districtProjectCity = visible ? DistrictProjectCityAt(cell.Q, cell.R) : null;
            CityState improvementProjectCity = visible ? ImprovementProjectCityAt(cell.Q, cell.R) : null;
            if (selected || (city != null && city.Id == selectedCityId)) graphic.EdgeColor = GamePalette.Gold;
            else if (visible && cell.OwnerId == simulation.State.HumanCivilizationId) graphic.EdgeColor = new Color(0.69f, 0.8f, 0.57f, 1f);
            else graphic.EdgeColor = new Color(0.12f, 0.18f, 0.17f, 1f);
            Button button = tile.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.transition = Selectable.Transition.None;
            int q = cell.Q, r = cell.R;
            button.onClick.AddListener(delegate { SelectHex(q, r); });
            HexTileGesture gesture = tile.AddComponent<HexTileGesture>();
            gesture.Initialize(delegate { RegisterLongPress(q, r); });
            if (explored) AddMapArtwork(tile.transform, TerrainArtwork(cell.Terrain), new Vector2(49, 43), new Vector2(0, -1), visible ? Color.white : new Color(0.55f, 0.62f, 0.58f, 0.9f));
            string marker = "";
            if (!explored) marker = "?";
            else if (city != null) marker = "⌂";
            else
            {
                UnitState unit = visible ? UnitAt(cell.Q, cell.R) : null;
                if (unit != null && IsUnitMoving(unit.Id)) unit = null;
                if (unit != null) marker = unit.Kind == UnitKind.SettlerWagon ? "◇" : "●";
                else if (districtCity != null) marker = "▥";
                else if (districtProjectCity != null) marker = "+";
                else if (improvementProjectCity != null) marker = "+";
                else if (visible && cell.Improvement != ImprovementKind.None && cell.Damage < 75) marker = ImprovementMark(cell.Improvement);
                else if (visible && cell.Resource != ResourceKind.None) marker = ResourceMark(cell.Resource);
            }
            if (!string.IsNullOrEmpty(marker))
            {
                string artwork = null;
                if (explored && city != null) artwork = CityMapArtwork(city, CivilizationEra(city.OwnerId));
                else
                {
                    UnitState unit = visible ? UnitAt(cell.Q, cell.R) : null;
                    if (unit != null && IsUnitMoving(unit.Id)) unit = null;
                    if (unit != null) artwork = UnitArtwork(unit.Kind, CivilizationEra(unit.OwnerId));
                    else if (districtCity != null) artwork = BuildingArtwork(cell.DistrictBuildingId, CivilizationEra(districtCity.OwnerId), CivilizationNationId(districtCity.OwnerId));
                    else if (districtProjectCity != null) artwork = BuildingArtwork(districtProjectCity.Project.TargetId, CivilizationEra(districtProjectCity.OwnerId), CivilizationNationId(districtProjectCity.OwnerId));
                    else if (improvementProjectCity != null) artwork = ImprovementArtwork(ProjectImprovementKind(improvementProjectCity.Project), CivilizationEra(improvementProjectCity.OwnerId));
                    else if (visible && cell.Improvement != ImprovementKind.None && cell.Damage < 75) artwork = ImprovementArtwork(cell.Improvement, CivilizationEra(cell.OwnerId));
                    else if (visible) artwork = ResourceArtwork(cell.Resource);
                }
                Sprite sprite = LoadArtwork(artwork);
                if (sprite != null)
                {
                    CivilizationState districtOwner = districtCity != null ? simulation.State.Civilization(districtCity.OwnerId) :
                        districtProjectCity != null ? simulation.State.Civilization(districtProjectCity.OwnerId) :
                        improvementProjectCity == null ? null : simulation.State.Civilization(improvementProjectCity.OwnerId);
                    Color tint = districtOwner == null ? Color.white : Color.Lerp(Color.white, NationAccent(districtOwner), 0.16f);
                    if (!visible && city == null) tint = Color.Lerp(tint, GamePalette.Ink, 0.38f);
                    if ((districtProjectCity != null || improvementProjectCity != null) && districtCity == null) tint.a = 0.62f;
                    Image mapArt = AddMapArtwork(tile.transform, artwork, city != null ? new Vector2(48, 44) : districtCity != null || districtProjectCity != null || improvementProjectCity != null ? new Vector2(34, 31) : new Vector2(30, 28),
                        city != null ? new Vector2(0, 4) : new Vector2(12, 8), tint);
                    UnitState displayedUnit = visible ? UnitAt(cell.Q, cell.R) : null;
                    if (displayedUnit != null && !IsUnitMoving(displayedUnit.Id) && animationMode > 0 && mapArt != null)
                    {
                        UiIdleMotion idle = mapArt.gameObject.AddComponent<UiIdleMotion>();
                        idle.Initialize(1.8f, 2.4f, ((cell.Q * 17 + cell.R * 31) & 15) / 16f);
                    }
                    if (city != null)
                    {
                        CivilizationState cityOwner = simulation.State.Civilization(city.OwnerId);
                        NationDefinition nation = cityOwner == null ? null : NationCatalog.Get(cityOwner.NationId);
                        if (nation != null) AddMapArtwork(tile.transform, nation.LeaderArtKey, new Vector2(15, 15), new Vector2(-19, 14), Color.white);
                    }
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
            if (visible && cell.OwnerId == simulation.State.HumanCivilizationId)
            {
                string activity = MapActivity(cell, city, districtCity, districtProjectCity, improvementProjectCity);
                if (!string.IsNullOrEmpty(activity))
                {
                    Vector2 activityPosition = city == null ? new Vector2(-14, -12) : new Vector2(17, 12);
                    Vector2 activitySize = city == null ? new Vector2(18, 21) : new Vector2(15, 18);
                    AddActivitySprite(tile.transform, activity, activitySize, activityPosition, Color.white, animationMode > 0, 4.8f);
                }
                CityProject activeProject = improvementProjectCity != null ? improvementProjectCity.Project :
                    districtProjectCity == null ? null : districtProjectCity.Project;
                if (activeProject != null) AddConstructionProgress(tile.transform, activeProject);
            }
        }

        private static Image AddMapArtwork(Transform parent, string name, Vector2 size, Vector2 position, Color tint)
        {
            Sprite sprite = LoadArtwork(name);
            if (sprite == null) return null;
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
            return image;
        }

        private Era CivilizationEra(int civilizationId)
        {
            CivilizationState civilization = simulation.State.Civilization(civilizationId);
            return civilization == null ? Era.Communities : civilization.Era;
        }

        private string CivilizationNationId(int civilizationId)
        {
            CivilizationState civilization = simulation == null ? null : simulation.State.Civilization(civilizationId);
            return civilization == null ? null : civilization.NationId;
        }

        private void SelectHex(int q, int r)
        {
            if (presentationBusy) return;
            float now = Time.unscaledTime;
            bool doubleTap = q == lastHexTapQ && r == lastHexTapR && now - lastHexTapTime <= 0.42f;
            bool longPress = q == longPressQ && r == longPressR && now <= longPressUntil;
            longPressUntil = 0f;
            lastHexTapQ = q;
            lastHexTapR = r;
            lastHexTapTime = now;
            CityState gestureCity = CityForMapHex(q, r);
            if (longPress && gestureCity != null && gestureCity.OwnerId == simulation.State.HumanCivilizationId)
            {
                lastHexTapTime = -1f;
                selectedQ = q;
                selectedR = r;
                selectedCityId = gestureCity.Id;
                selectedUnitId = -1;
                notice = "«" + gestureCity.Name + "» · население " + Format(gestureCity.PopulationTenths) +
                    " · еда " + Format(gestureCity.FoodTenths) + " · выберите «Управлять городом» для подробностей.";
                RenderGame();
                return;
            }
            if (doubleTap && gestureCity != null && gestureCity.OwnerId == simulation.State.HumanCivilizationId)
            {
                OpenCityFromMapGesture(q, r);
                return;
            }
            selectedQ = q;
            selectedR = r;
            HexCell selectedCell = simulation.State.Cell(q, r);
            bool cellVisible = selectedCell != null && (selectedCell.VisibleToMask & (1 << simulation.State.HumanCivilizationId)) != 0;
            CityState city = cellVisible ? CityAt(q, r) : null;
            bool selectedDistrict = cellVisible && city == null && !string.IsNullOrEmpty(selectedCell.DistrictBuildingId);
            CityState districtProjectCity = cellVisible && city == null ? DistrictProjectCityAt(q, r) : null;
            bool selectedProjectPlot = !selectedDistrict && districtProjectCity != null;
            if (selectedDistrict) city = simulation.State.City(selectedCell.DistrictCityId);
            else if (selectedProjectPlot) city = districtProjectCity;
            if (city != null && city.OwnerId == simulation.State.HumanCivilizationId)
            {
                selectedCityId = city.Id;
                selectedUnitId = -1;
                if (selectedDistrict)
                {
                    BuildingDefinition building = RulesCatalog.Building(selectedCell.DistrictBuildingId);
                    notice = "Выбран район «" + DisplayBuildingName(building, CivilizationNationId(city.OwnerId)) + "» города «" + city.Name + "».";
                }
                else if (selectedProjectPlot)
                {
                    BuildingDefinition building = RulesCatalog.Building(city.Project.TargetId);
                    notice = "Выбран участок будущего района «" + DisplayBuildingName(building, CivilizationNationId(city.OwnerId)) + "» города «" + city.Name + "».";
                }
                else notice = "Выбран город «" + city.Name + "».";
                RenderGame();
                return;
            }
            UnitState ownUnit = cellVisible ? UnitAt(q, r) : null;
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
                int startQ = moving == null ? q : moving.Q;
                int startR = moving == null ? r : moving.R;
                CommandResult movement = simulation.Issue(new GameCommand { Kind = CommandKind.MoveUnit, ActorId = simulation.State.HumanCivilizationId, EntityId = selectedUnitId, Q = q, R = r });
                notice = movement.Message;
                BattleResolutionEvent[] battles = simulation.LastResolvedBattles;
                bool resolvedBattle = movement.Accepted && battles != null && battles.Length > 0;
                if (resolvedBattle && animationMode > 0)
                {
                    selectedUnitId = -1;
                    RenderGame();
                    StartCoroutine(AnimateCommandBattle(battles[0], startQ, startR));
                    return;
                }
                if (resolvedBattle) PlaySoundEffect("Battle_Resolve");
                if (movement.Accepted && moving != null && animationMode > 0 && currentPage == ScreenPage.Overview && startScreen != Vector2.zero)
                {
                    PlaySoundEffect("World_Move");
                    int movedId = moving.Id;
                    UnitKind movedKind = moving.Kind;
                    Era movedEra = CivilizationEra(moving.OwnerId);
                    movingUnitId = movedId;
                    selectedUnitId = -1;
                    RenderGame();
                    StartCoroutine(AnimateUnitMovement(movedId, movedKind, movedEra, startScreen, q, r));
                    return;
                }
                if (movement.Accepted && !resolvedBattle) PlaySoundEffect("World_Move");
                if (movement.Accepted) selectedUnitId = -1;
                RenderGame();
                return;
            }
            HexCell cell = simulation.State.Cell(q, r);
            notice = cell == null ? "Клетки здесь нет." : cellVisible && cell.OwnerId == simulation.State.HumanCivilizationId
                ? "Выбрана клетка. Откройте управление городом, чтобы назначить улучшение." : "Клетка выбрана.";
            RenderGame();
        }

        private CityState CityForMapHex(int q, int r)
        {
            HexCell cell = simulation == null ? null : simulation.State.Cell(q, r);
            if (cell == null || (cell.VisibleToMask & (1 << simulation.State.HumanCivilizationId)) == 0) return null;
            CityState city = CityAt(q, r);
            if (city != null) return city;
            if (!string.IsNullOrEmpty(cell.DistrictBuildingId)) return simulation.State.City(cell.DistrictCityId);
            CityState districtProject = DistrictProjectCityAt(q, r);
            return districtProject ?? ImprovementProjectCityAt(q, r);
        }

        private void OpenCityFromMapGesture(int q, int r)
        {
            if (presentationBusy || simulation == null) return;
            lastHexTapTime = -1f;
            CityState city = CityForMapHex(q, r);
            if (city == null || city.OwnerId != simulation.State.HumanCivilizationId)
            {
                SelectHex(q, r);
                return;
            }
            selectedQ = q;
            selectedR = r;
            selectedCityId = city.Id;
            selectedUnitId = -1;
            currentPage = ScreenPage.City;
            notice = "Открыто управление городом «" + city.Name + "».";
            RenderGame();
        }

        private void RegisterLongPress(int q, int r)
        {
            longPressQ = q;
            longPressR = r;
            longPressUntil = Time.unscaledTime + 0.75f;
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
            return NationAccent(simulation == null ? null : simulation.State.Civilization(ownerId));
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
                case ImprovementKind.Park: return "♧";
                case ImprovementKind.Fortification: return "▧";
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

        private CityState DistrictProjectCityAt(int q, int r)
        {
            int humanId = simulation.State.HumanCivilizationId;
            HexCell cell = simulation.State.Cell(q, r);
            bool visible = cell != null && (cell.VisibleToMask & (1 << humanId)) != 0;
            for (int i = 0; i < simulation.State.Cities.Length; i++)
            {
                CityState city = simulation.State.Cities[i];
                CityProject project = city.Project;
                if ((city.OwnerId == humanId || visible) && project != null && project.Kind == ProjectKind.Building && project.DistrictPlotReserved && project.TargetQ == q && project.TargetR == r)
                    return city;
            }
            return null;
        }

        private CityState ImprovementProjectCityAt(int q, int r)
        {
            int humanId = simulation.State.HumanCivilizationId;
            for (int i = 0; i < simulation.State.Cities.Length; i++)
            {
                CityState city = simulation.State.Cities[i];
                CityProject project = city.Project;
                if (city.OwnerId == humanId && project != null && project.Kind == ProjectKind.Improvement && project.TargetQ == q && project.TargetR == r)
                    return city;
            }
            return null;
        }

        private static ImprovementKind ProjectImprovementKind(CityProject project)
        {
            ImprovementKind kind;
            return project != null && Enum.TryParse(project.TargetId, out kind) ? kind : ImprovementKind.None;
        }

        private string MapActivity(HexCell cell, CityState city, CityState districtCity, CityState districtProjectCity, CityState improvementProjectCity)
        {
            if (districtProjectCity != null || improvementProjectCity != null) return "Activity_Builder";
            if (districtCity != null) return BuildingActivity(cell.DistrictBuildingId);
            if (city != null) return CityWorkerActivity(city);
            switch (cell.Improvement)
            {
                case ImprovementKind.Farm: case ImprovementKind.IrrigatedFarm: return "Activity_Farmer";
                case ImprovementKind.Forestry: return "Activity_Lumberjack";
                case ImprovementKind.Workshop: return "Activity_Blacksmith";
                case ImprovementKind.Market: return "Activity_Merchant";
                default: return null;
            }
        }

        private static string CityWorkerActivity(CityState city)
        {
            if (city == null || city.Workers == null || city.Workers.Length == 0) return null;
            int greatest = 0;
            int role = -1;
            for (int i = 0; i < city.Workers.Length; i++)
                if (city.Workers[i] > greatest) { greatest = city.Workers[i]; role = i; }
            switch ((WorkerRole)role)
            {
                case WorkerRole.Farmer: return "Activity_Farmer";
                case WorkerRole.Artisan: return "Activity_Blacksmith";
                case WorkerRole.Scholar: return "Activity_Scholar";
                case WorkerRole.Merchant: return "Activity_Merchant";
                case WorkerRole.Artist: return "Activity_Artist";
                case WorkerRole.CivilServant: return "Activity_Steward";
                default: return null;
            }
        }

        private static string BuildingActivity(string buildingId)
        {
            BuildingDefinition building = RulesCatalog.Building(buildingId);
            if (building == null) return null;
            if (building.WorkerPlaceRole == (int)WorkerRole.Artisan) return "Activity_Blacksmith";
            if (building.WorkerPlaceRole == (int)WorkerRole.Scholar || building.KnowledgePerScholar > 0) return "Activity_Scholar";
            if (building.WorkerPlaceRole == (int)WorkerRole.Merchant || building.TreasuryPerMerchant > 0) return "Activity_Merchant";
            if (building.WorkerPlaceRole == (int)WorkerRole.Artist || building.CulturalObject || building.CulturePerArtist > 0) return "Activity_Artist";
            return "Activity_Steward";
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
            int usefulContribution = Math.Min(5, Math.Max(1, (crisis.Threat + 3) / 4));
            GameObject card = UiKit.Card(parent, "Мировой кризис · угроза " + crisis.Threat);
            UiKit.Label(card.transform, crisis.PublicText + " Ход кризиса: " + crisis.TurnsActive + ". Любой вклад улучшает репутацию и снижает угрозу; игра спишет только необходимое количество ресурса.", 22, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            GameObject row = UiKit.CreateObject("CrisisContributions", card.transform);
            UiKit.Horizontal(row, 0, 8);
            AddActionButton(row.transform, "ЕДА · до " + usefulContribution, delegate { Issue(CommandKind.ContributeToCrisis, text: "food", value: 5); }, GamePalette.Teal, 62);
            AddActionButton(row.transform, "ЗНАНИЯ · до " + usefulContribution, delegate { Issue(CommandKind.ContributeToCrisis, text: "knowledge", value: 5); }, GamePalette.Ink, 62);
            AddActionButton(row.transform, "ВЛИЯНИЕ · до " + usefulContribution, delegate { Issue(CommandKind.ContributeToCrisis, text: "influence", value: 5); }, GamePalette.Rust, 62);
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
                if (selected.WoundedPopulationTenths > 0)
                    UiKit.Label(card.transform, "Раненые " + Format(selected.WoundedPopulationTenths) + " населения · восстановление через " + selected.WoundedTurns + " хода",
                        19, GamePalette.Rust, TextAnchor.MiddleLeft, false);
                if (definition != null && definition.Class != UnitClass.Civilian)
                {
                    bool canUpgrade = ownerHasEngineering() && HasImprovedIronFor(selected);
                    string upgradeLabel = selected.IronEquipped ? "ЖЕЛЕЗНОЕ СНАРЯЖЕНИЕ · +25% СИЛЫ" : "УСИЛИТЬ ЖЕЛЕЗОМ · 5 КАЗНЫ";
                    Button upgrade = UiKit.Button(card.transform, upgradeLabel,
                        delegate { Issue(CommandKind.UpgradeUnit, entityId: selected.Id); }, GamePalette.Gold, 58, 19);
                    upgrade.interactable = !selected.IronEquipped && canUpgrade && simulation.State.Civilization(owner).TreasuryTenths >= 50;
                }
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
                UiKit.Label(unitsCard.transform, definition.NameRu + " · " + unit.Strength + " силы" + (unit.IronEquipped ? " · железное снаряжение" : "") +
                    (unit.WoundedPopulationTenths > 0 ? " · ранено " + Format(unit.WoundedPopulationTenths) + " · лечение " + unit.WoundedTurns + " хода" : "") +
                    " · " + unit.MovementLeft + " движения · клетка " + unit.Q + "," + unit.R,
                    20, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);
                int id = unit.Id;
                UiKit.Button(unitsCard.transform, "ВЫБРАТЬ ОТРЯД", delegate { selectedUnitId = id; selectedQ = unit.Q; selectedR = unit.R; RenderGame(); }, GamePalette.Ink, 52, 18);
            }
            if (count == 0) UiKit.Label(unitsCard.transform, "Отрядов пока нет. Ополчение эпохи Общин доступно без казарм; остальные роли открываются технологиями и требуют казармы. Обоз появляется при достаточном запасе еды и населения.", 20, GamePalette.Muted, TextAnchor.MiddleLeft, false);

            bool ownerHasEngineering() { return simulation.State.Civilization(owner).Knows("engineering"); }
        }

        private bool HasImprovedIronFor(UnitState unit)
        {
            CityState source = simulation.State.City(unit.CitySourceId);
            for (int i = 0; i < simulation.State.Cells.Length; i++)
            {
                HexCell cell = simulation.State.Cells[i];
                if (cell.OwnerId == unit.OwnerId && cell.Resource == ResourceKind.Iron && cell.Improvement != ImprovementKind.None && cell.Damage < 75 &&
                    (source == null || HexGrid.Distance(source.Q, source.R, cell.Q, cell.R) <= 3)) return true;
            }
            return false;
        }

        private Button AddActionButton(Transform parent, string label, Action action, Color color, float height = 58)
        {
            Button button = UiKit.Button(parent, label, action, color, height, 18);
            LayoutElement layout = button.GetComponent<LayoutElement>();
            layout.flexibleWidth = 1;
            return button;
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
            int districtCount = CountDistricts(city.Id);
            NationDefinition nation = NationCatalog.Get(owner.NationId);
            GameObject card = UiKit.Card(parent, "Городские районы · " + districtCount + " построено");
            GameObject center = UiKit.CreateObject("CityCenterArtwork", card.transform);
            UiKit.Horizontal(center, 8, 14);
            if (nation != null) AddCityBuildingArtwork(center.transform, nation.LeaderArtKey, 68);
            AddCityBuildingArtwork(center.transform, nation == null ? "City" : nation.ArtKey, 92);
            string currentArchitecture = CityMapArtwork(city, owner.Era);
            if (nation == null || currentArchitecture != nation.ArtKey)
                AddCityBuildingArtwork(center.transform, currentArchitecture, 138);
            Text eraDescription = UiKit.Label(center.transform,
                "Персонаж: " + (nation == null ? owner.Name : nation.LeaderNameRu) + ". Центр «" + (nation == null ? owner.Name : nation.NameRu) +
                "» остаётся на центральном гексе. Каждый район занимает отдельную клетку; обычные здания меняют вид с эпохой. " +
                (nation == null ? "" : "Учебный центр «" + nation.CampusNameRu + "»: " + nation.CampusBonusDescriptionRu),
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            eraDescription.GetComponent<LayoutElement>().flexibleWidth = 1f;

            int shown = 0;
            for (int i = 0; i < city.BuildingIds.Length; i++)
            {
                int level = i < city.BuildingLevels.Length ? city.BuildingLevels[i] : 0;
                if (level <= 0) continue;
                BuildingDefinition definition = RulesCatalog.Building(city.BuildingIds[i]);
                if (definition == null) continue;
                HexCell district = DistrictCellForCity(city.Id, definition.Id);
                GameObject row = UiKit.CreateObject("BuiltBuilding_" + city.BuildingIds[i], card.transform);
                UiKit.Horizontal(row, 4, 14);
                AddCityBuildingArtwork(row.transform, BuildingArtwork(definition.Id, owner.Era, owner.NationId), 82);
                string location = district == null ? "· район не найден" : "· район на карте " + district.Q + "," + district.R;
                Text buildingLabel = UiKit.Label(row.transform, DisplayBuildingName(definition, owner.NationId) + " · уровень " + level + " " + location, 19,
                    GamePalette.PaperDim, TextAnchor.MiddleLeft, true);
                buildingLabel.GetComponent<LayoutElement>().flexibleWidth = 1f;
                shown++;
            }
            if (shown == 0)
                UiKit.Label(card.transform, "Городской центр пока единственное завершённое здание. Первый район появится на отдельном свободном гексе после стройки.",
                    19, GamePalette.Muted, TextAnchor.MiddleLeft, false);
        }

        private int CountDistricts(int cityId)
        {
            int count = 0;
            for (int i = 0; i < simulation.State.Cells.Length; i++)
                if (simulation.State.Cells[i].DistrictCityId == cityId && !string.IsNullOrEmpty(simulation.State.Cells[i].DistrictBuildingId)) count++;
            return count;
        }

        private HexCell DistrictCellForCity(int cityId, string buildingId)
        {
            for (int i = 0; i < simulation.State.Cells.Length; i++)
            {
                HexCell cell = simulation.State.Cells[i];
                if (cell.DistrictCityId == cityId && cell.DistrictBuildingId == buildingId) return cell;
            }
            return null;
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
            int assignmentCapacity = Math.Min(city.WorkerCount, EconomyCalculator.WorkerCapacity(simulation.State, city));
            int assignedWorkers = EconomyCalculator.AssignedWorkers(city);
            GameObject card = UiKit.Card(parent, "Работники · " + assignedWorkers + "/" + assignmentCapacity + " рабочих мест");
            UiKit.Label(card.transform, "Жителей для работы: " + city.WorkerCount + " · рабочих мест в городе: " + EconomyCalculator.WorkerCapacity(simulation.State, city) +
                ". Общие места дают освоенные клетки; поля открывают места земледельцам, а профильные районы — специалистам.",
                18, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            WorkerRole[] roles = { WorkerRole.Farmer, WorkerRole.Artisan, WorkerRole.Scholar, WorkerRole.Merchant, WorkerRole.Artist, WorkerRole.CivilServant };
            string[] names = { "Земледельцы · еда", "Ремесленники · производство", "Учёные · знания", "Торговцы · казна", "Художники · культура", "Служащие · услуги" };
            string[] activities = { "Activity_Farmer", "Activity_Blacksmith", "Activity_Scholar", "Activity_Merchant", "Activity_Artist", "Activity_Steward" };
            for (int i = 0; i < roles.Length; i++)
            {
                int roleIndex = i;
                GameObject row = UiKit.CreateObject("Worker_" + i, card.transform);
                UiKit.Horizontal(row, 0, 8);
                Image worker = AddActivitySprite(row.transform, activities[i], new Vector2(38, 38), Vector2.zero,
                    city.Workers[i] > 0 ? Color.white : new Color(0.62f, 0.68f, 0.65f, 0.48f), city.Workers[i] > 0 && animationMode > 0, 5.2f);
                if (worker != null)
                {
                    LayoutElement workerLayout = worker.GetComponent<LayoutElement>();
                    if (workerLayout == null) workerLayout = worker.gameObject.AddComponent<LayoutElement>();
                    workerLayout.minWidth = workerLayout.preferredWidth = 42;
                    workerLayout.minHeight = workerLayout.preferredHeight = 42;
                    workerLayout.flexibleWidth = 0;
                }
                int roleCapacity = EconomyCalculator.WorkerRoleCapacity(simulation.State, city, roles[i]);
                Text value = UiKit.Label(row.transform, names[i] + " · " + city.Workers[i] + "/" + roleCapacity, 20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                value.GetComponent<LayoutElement>().flexibleWidth = 1;
                int next = Math.Max(0, city.Workers[i] - 1);
                Button minus = UiKit.Button(row.transform, "−", delegate { Issue(CommandKind.SetWorkers, city.Id, value: roleIndex, targetId: next); }, GamePalette.Ink, 52, 22);
                minus.GetComponent<LayoutElement>().flexibleWidth = 0;
                minus.GetComponent<LayoutElement>().preferredWidth = 60;
                minus.interactable = city.Workers[i] > 0;
                int increase = assignedWorkers < assignmentCapacity && city.Workers[i] < roleCapacity ? city.Workers[i] + 1 : city.Workers[i];
                Button plus = UiKit.Button(row.transform, "+", delegate { Issue(CommandKind.SetWorkers, city.Id, value: roleIndex, targetId: increase); }, GamePalette.Ink, 52, 22);
                plus.GetComponent<LayoutElement>().flexibleWidth = 0;
                plus.GetComponent<LayoutElement>().preferredWidth = 60;
                plus.interactable = assignedWorkers < assignmentCapacity && city.Workers[i] < roleCapacity;
            }
            UiKit.Label(card.transform, "Перераспределение вручную сохраняет число работников в остальных ролях и не превышает число жителей, мест города или мест выбранной роли.", 19, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            GameObject focusRow = UiKit.CreateObject("WorkerFocus", card.transform);
            UiKit.Horizontal(focusRow, 0, 8);
            AddActionButton(focusRow.transform, "БАЛАНС", delegate { AutoAssign(city.Id, "balanced"); }, GamePalette.Teal);
            AddActionButton(focusRow.transform, "ЕДА", delegate { AutoAssign(city.Id, "food"); }, GamePalette.Ink);
            AddActionButton(focusRow.transform, "НАУКА", delegate { AutoAssign(city.Id, "science"); }, GamePalette.Ink);
            AddActionButton(focusRow.transform, "ТОРГОВЛЯ", delegate { AutoAssign(city.Id, "trade"); }, GamePalette.Ink);
        }

        private static Image AddActivitySprite(Transform parent, string activity, Vector2 size, Vector2 position, Color tint, bool animated, float fps)
        {
            Sprite[] frames = new Sprite[4];
            bool anyFrame = false;
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] = LoadArtwork(activity + "_" + i);
                anyFrame |= frames[i] != null;
            }
            if (!anyFrame) return null;
            GameObject visual = UiKit.CreateObject(activity, parent);
            Image image = visual.AddComponent<Image>();
            image.sprite = frames[0];
            image.preserveAspect = true;
            image.color = tint;
            image.raycastTarget = false;
            RectTransform rect = UiKit.Rect(visual);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            if (animated)
            {
                UiSpriteLoop loop = visual.AddComponent<UiSpriteLoop>();
                loop.Initialize(image, frames, fps);
                UiIdleMotion idle = visual.AddComponent<UiIdleMotion>();
                idle.Initialize(1.2f, 2.8f, Mathf.Abs(activity.GetHashCode() % 100) / 100f);
            }
            return image;
        }

        private void AddConstructionProgress(Transform parent, CityProject project)
        {
            GameObject track = UiKit.CreateObject("ConstructionProgress", parent);
            Image trackImage = track.AddComponent<Image>();
            trackImage.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.8f);
            trackImage.raycastTarget = false;
            RectTransform trackRect = UiKit.Rect(track);
            trackRect.anchorMin = trackRect.anchorMax = new Vector2(0.5f, 0.5f);
            trackRect.pivot = new Vector2(0.5f, 0.5f);
            trackRect.sizeDelta = new Vector2(34, 4);
            trackRect.anchoredPosition = new Vector2(0, -23);
            GameObject fill = UiKit.CreateObject("ConstructionProgressFill", track.transform);
            Image fillImage = fill.AddComponent<Image>();
            fillImage.color = GamePalette.Gold;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = project == null || project.Cost <= 0 ? 0f : Mathf.Clamp01(project.Progress / (float)project.Cost);
            fillImage.raycastTarget = false;
            RectTransform fillRect = UiKit.Rect(fill);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            if (animationMode > 0)
            {
                UiConstructionPulse pulse = track.AddComponent<UiConstructionPulse>();
                pulse.Initialize(trackImage, Mathf.Abs(project.TargetQ * 13 + project.TargetR * 7) / 20f);
            }
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
                CivilizationState owner = simulation.State.Civilization(city.OwnerId);
                bool scienceReadyToValidate = project.Kind == ProjectKind.ScientificProject && owner != null && owner.ScienceProjectTurns >= 4;
                int percent = project.Cost <= 0 ? 0 : Math.Min(100, project.Progress * 100 / project.Cost);
                int production = Math.Max(1, simulation.PreviewCity(city.Id).Production);
                int turnsLeft = Math.Max(0, project.Cost - project.Progress + production - 1) / production;
                if (scienceReadyToValidate)
                    UiKit.Label(card.transform, "Научная программа завершена. Поддерживайте выпуск не ниже 5 знаний за ход два хода подряд · сейчас " +
                        Format(owner.PreviousTurnKnowledgeOutputTenths) + " / " + Format(owner.LastTurnKnowledgeOutputTenths) + ".", 22, GamePalette.Paper, TextAnchor.MiddleLeft, true);
                else
                    UiKit.Label(card.transform, ProjectName(project) + " · " + project.Progress + "/" + project.Cost + " ПП · " + percent + "% · примерно " + turnsLeft + " ход(а) до завершения",
                        22, GamePalette.Paper, TextAnchor.MiddleLeft, true);
                if (project.Kind == ProjectKind.Building && project.DistrictPlotReserved)
                    UiKit.Label(card.transform, "Место будущего района: гекс " + project.TargetQ + "," + project.TargetR + ". Земля уже включена в территорию города; район появится после завершения.",
                        19, GamePalette.Muted, TextAnchor.MiddleLeft, false);
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
                if (!string.IsNullOrEmpty(building.RequiredNationId) && building.RequiredNationId != owner.NationId) continue;
                bool unlocked = string.IsNullOrEmpty(building.RequiredTechnology) || owner.Knows(building.RequiredTechnology);
                bool already = city.BuildingLevel(building.Id) > 0;
                string label = DisplayBuildingName(building, owner.NationId) + " · " + building.ProductionCost + " ПП · содержание " + Format(building.UpkeepTenths) +
                    (string.IsNullOrEmpty(building.RequiredNationId) && building.Id != "factory" ? "" : " · " + BuildingEffectSummary(building)) +
                    (unlocked ? "" : " · нужна технология «" + building.RequiredTechnology + "»") +
                    (already ? " · уже построено" : "");
                Button button = UiKit.Button(card.transform, label,
                    delegate { Issue(CommandKind.SetProject, city.Id, text: building.Id); }, already ? GamePalette.Ink : unlocked ? GamePalette.Teal : GamePalette.Ink, 60, 18);
                AddButtonArtwork(button, BuildingArtwork(building.Id, owner.Era, owner.NationId));
                button.interactable = unlocked && (!already || building.Id == "house") &&
                    (!building.UniqueEmpireBuilding || CountEmpireBuilding(owner.Id, building.Id) == 0) &&
                    (city.Project == null || city.Project.Kind == ProjectKind.None);
            }
        }

        private void RenderRecruitChoices(Transform parent, CityState city, CivilizationState owner)
        {
            GameObject card = UiKit.Card(parent, "Войска эпохи «" + EraName(owner.Era) + "» · выберите роль отряда");
            for (int i = 0; i < RulesCatalog.Units.Length; i++)
            {
                UnitDefinition unit = RulesCatalog.Units[i];
                if (unit.LegacyOnly || (unit.Class != UnitClass.Civilian && unit.Era != owner.Era)) continue;
                bool tech = string.IsNullOrEmpty(unit.RequiredTechnology) || owner.Knows(unit.RequiredTechnology);
                bool building = string.IsNullOrEmpty(unit.RequiredBuilding) || city.BuildingLevel(unit.RequiredBuilding) > 0;
                string label = UnitClassName(unit.Class) + "  ·  " + unit.NameRu + "  ·  " + unit.Strength + " силы  ·  движение " + unit.Movement +
                    "  ·  " + unit.ProductionCost + " ПП  ·  " + Format(unit.TreasuryCostTenths) + " казны  ·  " + Format(unit.PopulationTenths) + " населения";
                TechnologyDefinition requiredTech = string.IsNullOrEmpty(unit.RequiredTechnology) ? null : RulesCatalog.Technology(unit.RequiredTechnology);
                if (!tech) label += "  ·  изучите «" + (requiredTech == null ? unit.RequiredTechnology : requiredTech.NameRu) + "»";
                if (!building)
                {
                    BuildingDefinition requiredBuilding = RulesCatalog.Building(unit.RequiredBuilding);
                    label += "  ·  нужно здание «" + (requiredBuilding == null ? unit.RequiredBuilding : requiredBuilding.NameRu) + "»";
                }
                UnitKind kind = unit.Kind;
                Button button = UiKit.Button(card.transform, label, delegate { Issue(CommandKind.Recruit, city.Id, value: (int)kind); }, unit.Kind == UnitKind.SettlerWagon ? GamePalette.Teal : GamePalette.Ink, 60, 18);
                AddButtonArtwork(button, UnitArtwork(unit.Kind, owner.Era));
                button.interactable = tech && building && (city.Project == null || city.Project.Kind == ProjectKind.None) &&
                    (unit.Kind != UnitKind.SettlerWagon || (city.PopulationTenths >= 80 && city.FoodTenths >= 60)) &&
                    (unit.Kind == UnitKind.SettlerWagon || (city.PopulationTenths >= unit.PopulationTenths + 40 && owner.TreasuryTenths >= unit.TreasuryCostTenths));
            }
            UiKit.Label(card.transform, "В эпохе открываются пять боевых ролей: пехота, стрелки, конница, осадные части и разведка. Исследования и казармы открывают отдельные варианты. Набор резервирует жителей, занимает производство и добавляет содержание. Обоз стоит 6 еды и 2 населения.",
                16, GamePalette.Muted, TextAnchor.MiddleLeft, false);
        }

        private static string UnitClassName(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Infantry: return "ПЕХОТА";
                case UnitClass.Ranged: return "СТРЕЛКИ";
                case UnitClass.Cavalry: return "КОННИЦА";
                case UnitClass.Siege: return "ОСАДНЫЕ";
                case UnitClass.Scout: return "РАЗВЕДКА";
                default: return "ЭКСПЕДИЦИЯ";
            }
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

        private static bool HasEraTransition(GameState state, int ownerId)
        {
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityProject project = state.Cities[i].Project;
                if (state.Cities[i].OwnerId == ownerId && project != null && project.Kind == ProjectKind.EraTransition) return true;
            }
            return false;
        }

        private int CountEmpireBuilding(int ownerId, string buildingId)
        {
            int count = 0;
            for (int i = 0; i < simulation.State.Cities.Length; i++)
                if (simulation.State.Cities[i].OwnerId == ownerId) count += simulation.State.Cities[i].BuildingLevel(buildingId);
            return count;
        }

        private int CountCitiesWithBuilding(int ownerId, string buildingId)
        {
            int count = 0;
            for (int i = 0; i < simulation.State.Cities.Length; i++)
                if (simulation.State.Cities[i].OwnerId == ownerId && simulation.State.Cities[i].BuildingLevel(buildingId) > 0) count++;
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
            if (result.Accepted)
            {
                PlayCommandSound(kind, value);
                MarkTutorialCommand(kind, value, text);
                if (hapticsEnabled && SystemInfo.supportsVibration && kind != CommandKind.SetWorkers && kind != CommandKind.AutoAssignWorkers)
                    Handheld.Vibrate();
            }
            RenderGame();
        }

        private void MarkTutorialCommand(CommandKind kind, int value, string text)
        {
            if (tutorialStepIndex >= TutorialSteps.Length) return;
            switch (TutorialSteps[tutorialStepIndex].Objective)
            {
                case TutorialObjective.AssignWorkers:
                    if (kind == CommandKind.SetWorkers || kind == CommandKind.AutoAssignWorkers) MarkTutorialObjective(TutorialObjective.AssignWorkers);
                    break;
                case TutorialObjective.Research:
                    if (kind == CommandKind.ChooseTechnology) MarkTutorialObjective(TutorialObjective.Research);
                    break;
                case TutorialObjective.CityProject:
                    if (kind == CommandKind.SetProject && !string.IsNullOrEmpty(text) && text != "cancel") MarkTutorialObjective(TutorialObjective.CityProject);
                    break;
                case TutorialObjective.LandImprovement:
                    if (kind == CommandKind.BuildImprovement) MarkTutorialObjective(TutorialObjective.LandImprovement);
                    break;
                case TutorialObjective.RecruitSettler:
                    if (kind == CommandKind.Recruit && value == (int)UnitKind.SettlerWagon) MarkTutorialObjective(TutorialObjective.RecruitSettler);
                    break;
            }
        }

        private void PlayCommandSound(CommandKind kind, int value)
        {
            switch (kind)
            {
                case CommandKind.MoveUnit: PlaySoundEffect("World_Move"); break;
                case CommandKind.ChooseTechnology: PlaySoundEffect("Research_Discovery"); break;
                case CommandKind.ProposeTreaty:
                case CommandKind.AcceptTreaty:
                case CommandKind.MakePeace: PlaySoundEffect("Diplomacy_Seal"); break;
                case CommandKind.DeclareWar: PlaySoundEffect("UI_Confirm"); break;
                case CommandKind.BeginVictoryProject:
                    PlaySoundEffect(value == (int)VictoryPath.Scientific ? "Research_Discovery" :
                        value == (int)VictoryPath.Diplomatic ? "Diplomacy_Seal" : "UI_Confirm");
                    break;
                case CommandKind.SetProject:
                case CommandKind.BuildImprovement:
                case CommandKind.Recruit:
                case CommandKind.EnactLaw:
                case CommandKind.EventChoice: PlaySoundEffect("UI_Confirm"); break;
            }
        }

        private static bool HasVisibleBattles(BattleResolutionEvent[] battles)
        {
            if (battles == null) return false;
            for (int i = 0; i < battles.Length; i++)
                if (battles[i] != null && battles[i].HumanVisibilityMask != 0) return true;
            return false;
        }

        private static int CountVisibleBattles(BattleResolutionEvent[] battles)
        {
            if (battles == null) return 0;
            int count = 0;
            for (int i = 0; i < battles.Length; i++)
                if (battles[i] != null && battles[i].HumanVisibilityMask != 0) count++;
            return count;
        }

        private void SetTax(TaxRate rate) { Issue(CommandKind.SetTax, value: (int)rate); }

        private void SaveManual()
        {
            if (simulation == null) return;
            notice = SaveService.SaveManual(simulation.State)
                ? "Кампания сохранена на этом устройстве."
                : "Не удалось сохранить кампанию. Освободите место и повторите попытку.";
            if (menuHost.gameObject.activeSelf) ShowMenu(); else RenderGame();
        }

        private void SaveManualSlot(int slot)
        {
            if (simulation == null) return;
            notice = SaveService.SaveManual(simulation.State, slot)
                ? "Кампания сохранена в ручной слот " + (slot + 1) + "."
                : "Не удалось сохранить кампанию. Освободите место и повторите попытку.";
            ShowSaves();
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
            bool wasFinished = simulation.State.IsFinished;
            simulation.EndTurn();
            if (simulation.State.Turn != previousTurn)
            {
                if (HasVisibleBattles(simulation.LastResolvedBattles)) PlaySoundEffect("Battle_Resolve");
                PlaySoundEffect("Turn_Advance");
                SetMusicEra(simulation.State.Civilization(simulation.State.HumanCivilizationId).Era);
                bool saved = SaveService.SaveAuto(simulation.State);
                notice = "Ход " + previousTurn + " завершён. " + simulation.LastMessage +
                    (saved ? "" : " Автосохранение не удалось; сохраните кампанию вручную.");
            }
            else notice = simulation.LastMessage;
            if (!wasFinished && simulation.State.IsFinished) PlayFinalSound();
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
            Dictionary<string, string> improvements = CapturePlayerImprovements();
            Dictionary<int, UnitMovePresentation> unitPositions = CaptureUnitPositions();
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
            BattleResolutionEvent[] turnBattles = simulation.LastResolvedBattles == null
                ? new BattleResolutionEvent[0] : (BattleResolutionEvent[])simulation.LastResolvedBattles.Clone();
            bool advanced = simulation.State.Turn != previousTurn;
            if (advanced)
            {
                PlaySoundEffect("Turn_Advance");
                SetMusicEra(simulation.State.Civilization(simulation.State.HumanCivilizationId).Era);
                if (simulation.State.IsFinished) PlayFinalSound();
            }
            bool saved = !advanced || SaveService.SaveAuto(simulation.State);
            ScreenPage resolvedPage = simulation.State.IsFinished ? ScreenPage.Victory : HasPendingEvent() ? ScreenPage.Events : ScreenPage.Overview;
            List<UnitMovePresentation> turnMoves = advanced && resolvedPage == ScreenPage.Overview && animationMode > 0
                ? FindTurnUnitMoves(unitPositions)
                : new List<UnitMovePresentation>();
            if (turnBattles.Length > 0) RemoveBattleParticipantsFromMovement(turnMoves, turnBattles);
            movingUnitIds.Clear();
            for (int i = 0; i < turnMoves.Count; i++) movingUnitIds.Add(turnMoves[i].UnitId);
            currentPage = turnMoves.Count > 0 ? ScreenPage.Overview : resolvedPage;
            notice = advanced
                ? "Ход " + previousTurn + " завершён. " + simulation.LastMessage + (saved ? "" : " Автосохранение не удалось; сохраните кампанию вручную.")
                : simulation.LastMessage;
            RenderGame();

            if (turnMoves.Count > 0 && animationMode > 0)
                yield return AnimateTurnUnitMovements(layer, turnMoves);
            if (advanced && animationMode > 0 && turnBattles.Length > 0)
            {
                if (HasVisibleBattles(turnBattles) && currentPage != ScreenPage.Overview)
                {
                    currentPage = ScreenPage.Overview;
                    movingUnitIds.Clear();
                    RenderGame();
                }
                int shown = 0;
                for (int i = 0; i < turnBattles.Length && shown < 3; i++)
                {
                    if (turnBattles[i] == null || turnBattles[i].HumanVisibilityMask == 0) continue;
                    UnitMovePresentation attackerStart;
                    bool hasVisibleStart = unitPositions.TryGetValue(turnBattles[i].AttackerId, out attackerStart) && attackerStart.StartVisible;
                    int startQ = hasVisibleStart ? attackerStart.StartQ : turnBattles[i].CellQ - 1;
                    int startR = hasVisibleStart ? attackerStart.StartR : turnBattles[i].CellR;
                    yield return PlayBattleAttack(layer, turnBattles[i], startQ, startR);
                    shown++;
                }
            }
            if (currentPage != resolvedPage)
            {
                currentPage = resolvedPage;
                movingUnitIds.Clear();
                RenderGame();
            }

            if (advanced)
            {
                if (animationMode > 0)
                    yield return ShowTurnReport(layer.transform, previousTurn);
                Era nextEra = simulation.State.Civilization(simulation.State.HumanCivilizationId).Era;
                List<BuildingMilestone> completed = FindNewlyCompletedBuildings(buildingLevels);
                List<ImprovementMilestone> completedImprovements = FindNewlyCompletedImprovements(improvements);
                bool hasEraTransition = nextEra != previousEra;
                if (hasEraTransition || completed.Count > 0 || completedImprovements.Count > 0)
                {
                    dismissPresentationRequested = false;
                    ShowTurnMilestones(layer.transform, previousTurn, previousEra, nextEra, completed, completedImprovements);
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

        private Dictionary<int, UnitMovePresentation> CaptureUnitPositions()
        {
            var result = new Dictionary<int, UnitMovePresentation>();
            UnitState[] units = simulation.State.Units;
            for (int i = 0; i < units.Length; i++)
            {
                UnitState unit = units[i];
                if (unit == null) continue;
                HexCell cell = simulation.State.Cell(unit.Q, unit.R);
                bool visible = cell != null && (cell.VisibleToMask & (1 << simulation.State.HumanCivilizationId)) != 0;
                result[unit.Id] = new UnitMovePresentation
                {
                    UnitId = unit.Id, OwnerId = unit.OwnerId, Kind = unit.Kind,
                    StartQ = unit.Q, StartR = unit.R, StartVisible = visible
                };
            }
            return result;
        }

        private List<UnitMovePresentation> FindTurnUnitMoves(Dictionary<int, UnitMovePresentation> previous)
        {
            var result = new List<UnitMovePresentation>();
            if (previous == null) return result;
            UnitState[] units = simulation.State.Units;
            for (int i = 0; i < units.Length; i++)
            {
                UnitState unit = units[i];
                UnitMovePresentation start;
                if (unit == null || !previous.TryGetValue(unit.Id, out start) ||
                    (unit.Q == start.StartQ && unit.R == start.StartR) ||
                    HexGrid.Distance(unit.Q, unit.R, start.StartQ, start.StartR) > 2) continue;
                HexCell destination = simulation.State.Cell(unit.Q, unit.R);
                int humanMask = 1 << simulation.State.HumanCivilizationId;
                if (destination == null || (destination.VisibleToMask & humanMask) == 0) continue;
                result.Add(new UnitMovePresentation
                {
                    UnitId = unit.Id, OwnerId = unit.OwnerId, Kind = unit.Kind,
                    StartQ = start.StartQ, StartR = start.StartR,
                    TargetQ = unit.Q, TargetR = unit.R
                });
            }
            return result;
        }

        private static void RemoveBattleParticipantsFromMovement(List<UnitMovePresentation> moves, BattleResolutionEvent[] battles)
        {
            if (moves == null || battles == null) return;
            for (int moveIndex = moves.Count - 1; moveIndex >= 0; moveIndex--)
            {
                bool isBattleParticipant = false;
                for (int battleIndex = 0; battleIndex < battles.Length; battleIndex++)
                {
                    BattleResolutionEvent battle = battles[battleIndex];
                    if (battle == null || battle.HumanVisibilityMask == 0) continue;
                    if (moves[moveIndex].UnitId == battle.AttackerId || moves[moveIndex].UnitId == battle.DefenderId)
                    {
                        isBattleParticipant = true;
                        break;
                    }
                }
                if (isBattleParticipant) moves.RemoveAt(moveIndex);
            }
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

        private Dictionary<string, string> CapturePlayerImprovements()
        {
            var result = new Dictionary<string, string>();
            int playerId = simulation.State.HumanCivilizationId;
            for (int i = 0; i < simulation.State.Cells.Length; i++)
            {
                HexCell cell = simulation.State.Cells[i];
                if (cell.OwnerId == playerId && cell.Improvement != ImprovementKind.None)
                    result[cell.Q + ":" + cell.R] = cell.Improvement + ":" + cell.ImprovementLevel;
            }
            return result;
        }

        private List<BuildingMilestone> FindNewlyCompletedBuildings(Dictionary<string, int> previous)
        {
            List<BuildingMilestone> completed = new List<BuildingMilestone>();
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
                        if (definition != null) completed.Add(new BuildingMilestone { Definition = definition, CityName = city.Name });
                    }
                }
            }
            return completed;
        }

        private List<ImprovementMilestone> FindNewlyCompletedImprovements(Dictionary<string, string> previous)
        {
            var completed = new List<ImprovementMilestone>();
            int playerId = simulation.State.HumanCivilizationId;
            for (int i = 0; i < simulation.State.Cells.Length; i++)
            {
                HexCell cell = simulation.State.Cells[i];
                if (cell.OwnerId != playerId || cell.Improvement == ImprovementKind.None) continue;
                string key = cell.Q + ":" + cell.R;
                string current = cell.Improvement + ":" + cell.ImprovementLevel;
                string old;
                if (!previous.TryGetValue(key, out old) || old != current)
                    completed.Add(new ImprovementMilestone { Kind = cell.Improvement, Q = cell.Q, R = cell.R });
            }
            return completed;
        }

        private IEnumerator ShowTurnReport(Transform layer, int completedTurn)
        {
            TurnReport report = null;
            TurnReport[] reports = simulation.State.Reports ?? Array.Empty<TurnReport>();
            for (int i = reports.Length - 1; i >= 0; i--)
                if (reports[i] != null && reports[i].Turn == completedTurn) { report = reports[i]; break; }
            if (report == null) yield break;
            int visibleBattleCount = CountVisibleBattles(simulation.LastResolvedBattles);

            DestroyChildren(layer);
            Image blocker = layer.GetComponent<Image>();
            if (blocker != null) blocker.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.025f);
            Button backdrop = layer.GetComponent<Button>();
            if (backdrop == null) backdrop = layer.gameObject.AddComponent<Button>();
            backdrop.targetGraphic = blocker;
            backdrop.transition = Selectable.Transition.None;
            backdrop.onClick.RemoveAllListeners();
            dismissPresentationRequested = false;
            backdrop.onClick.AddListener(delegate { dismissPresentationRequested = true; });

            Image dock = AddPresentationRail(layer, "TurnReportDock", new Vector2(0.025f, 0.045f), new Vector2(0.975f, 0.355f),
                new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.89f));
            AddPresentationLabel(layer, "TurnReportTitle", "ХОД " + completedTurn + " ЗАВЕРШЁН", 25, GamePalette.Gold,
                TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(0.05f, 0.285f), new Vector2(0.54f, 0.34f));
            AddPresentationLabel(layer, "TurnReportSubhead", "ИЗМЕНЕНИЯ ЗА ХОД", 11, GamePalette.Sage,
                TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(0.05f, 0.245f), new Vector2(0.54f, 0.275f));

            string[] metricNames = { "ЕДА", "КАЗНА", "ЗНАНИЯ", "ВЛИЯНИЕ", "КУЛЬТУРА", "ЖИТЕЛИ" };
            string[] metricValues = {
                Signed(report.FoodDeltaTenths), Signed(report.TreasuryDeltaTenths), Signed(report.KnowledgeDeltaTenths),
                Signed(report.InfluenceDeltaTenths), (report.CultureDelta > 0 ? "+" : "") + report.CultureDelta,
                Signed(report.PopulationDeltaTenths)
            };
            for (int i = 0; i < metricNames.Length; i++)
            {
                float x = 0.05f + i * 0.085f;
                AddPresentationLabel(layer, "TurnMetricName_" + i, metricNames[i], 10, GamePalette.Muted,
                    TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(x, 0.183f), new Vector2(x + 0.079f, 0.222f));
                AddPresentationLabel(layer, "TurnMetricValue_" + i, metricValues[i], 20, GamePalette.Paper,
                    TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(x, 0.115f), new Vector2(x + 0.079f, 0.178f));
                if (i < metricNames.Length - 1)
                    AddPresentationRail(layer, "TurnMetricDivider_" + i, new Vector2(x + 0.080f, 0.12f), new Vector2(x + 0.081f, 0.228f),
                        new Color(GamePalette.Paper.r, GamePalette.Paper.g, GamePalette.Paper.b, 0.18f));
            }

            AddPresentationRail(layer, "TurnReportEventsDivider", new Vector2(0.57f, 0.085f), new Vector2(0.571f, 0.315f),
                new Color(GamePalette.Paper.r, GamePalette.Paper.g, GamePalette.Paper.b, 0.2f));
            AddPresentationLabel(layer, "TurnReportEventsTitle", "СОБЫТИЯ", 11, GamePalette.Sage,
                TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(0.595f, 0.27f), new Vector2(0.79f, 0.315f));
            string[] entries = report.Entries ?? Array.Empty<string>();
            int entryCount = Math.Min(3, entries.Length);
            if (entryCount == 0)
                AddPresentationLabel(layer, "TurnReportNoEvents", "Спокойный ход · угроз не обнаружено", 14, GamePalette.PaperDim,
                    TextAnchor.MiddleLeft, FontStyle.Normal, new Vector2(0.595f, 0.13f), new Vector2(0.79f, 0.26f));
            else
            {
                for (int i = 0; i < entryCount; i++)
                {
                    string entry = entries[i] ?? "";
                    if (entry.Length > 70) entry = entry.Substring(0, 67) + "…";
                    float top = 0.258f - i * 0.058f;
                    AddPresentationLabel(layer, "TurnReportEvent_" + i, "·  " + entry, 13, GamePalette.Paper,
                        TextAnchor.MiddleLeft, FontStyle.Normal, new Vector2(0.595f, top - 0.05f), new Vector2(0.79f, top));
                }
            }
            if (visibleBattleCount > 0 || report.TreatyCount > 0)
                AddPresentationLabel(layer, "TurnReportCounts", visibleBattleCount + " видимых боёв  ·  " + report.TreatyCount + " договоров", 11, GamePalette.Muted,
                    TextAnchor.MiddleLeft, FontStyle.Normal, new Vector2(0.595f, 0.08f), new Vector2(0.79f, 0.115f));

            AddPresentationAction(layer, "TurnReportContinue", "ДАЛЬШЕ  →", GamePalette.Gold,
                new Vector2(0.825f, 0.105f), new Vector2(0.95f, 0.18f), delegate { dismissPresentationRequested = true; });
            if (dock != null) dock.raycastTarget = false;
            CanvasGroup group = layer.gameObject.GetComponent<CanvasGroup>();
            if (group == null) group = layer.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            float fade = 0.16f * AnimationScale;
            float elapsed = 0f;
            while (elapsed < fade)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Clamp01(elapsed / fade);
                yield return null;
            }
            group.alpha = 1f;
            float remaining = animationMode == 2 ? 3.2f : 1.75f;
            while (remaining > 0f && !dismissPresentationRequested)
            {
                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }
            if (group != null) group.alpha = 0f;
        }

        private void ShowTurnMilestones(Transform layer, int completedTurn, Era oldEra, Era newEra,
            List<BuildingMilestone> buildings, List<ImprovementMilestone> improvements)
        {
            bool eraTransition = oldEra != newEra;
            if (eraTransition) PlaySoundEffect("Era_Transition");
            else if (buildings.Count > 0 || improvements.Count > 0) PlaySoundEffect("Build_Complete");
            DestroyChildren(layer);
            Image blocker = layer.GetComponent<Image>();
            if (blocker != null) blocker.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, eraTransition ? 0.12f : 0.035f);
            Button backdrop = layer.GetComponent<Button>();
            if (backdrop == null) backdrop = layer.gameObject.AddComponent<Button>();
            backdrop.targetGraphic = blocker;
            backdrop.transition = Selectable.Transition.None;
            backdrop.onClick.RemoveAllListeners();
            backdrop.onClick.AddListener(delegate { dismissPresentationRequested = true; });

            if (eraTransition)
            {
                AddPresentationArtwork(layer, "EraTransitionPanorama", "Backgrounds/Era_Transition_Background");
                AddPresentationRail(layer, "EraTransitionVeil", Vector2.zero, Vector2.one,
                    new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.12f));
                Text chapter = AddPresentationLabel(layer, "EraTransitionChapter", "НОВАЯ ГЛАВА  ·  ХОД " + (completedTurn + 1), 13, GamePalette.Sage,
                    TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(0.045f, 0.915f), new Vector2(0.64f, 0.965f));
                Text eraTitle = AddPresentationLabel(layer, "EraTransitionTitle", EraName(newEra), 38, GamePalette.Paper,
                    TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(0.045f, 0.84f), new Vector2(0.72f, 0.915f));
                AddTextOutline(chapter);
                AddTextOutline(eraTitle);
                AddPresentationLabel(layer, "EraTransitionSubtitle", EraName(oldEra) + "  →  " + EraName(newEra) + "   ·   ваши города растут и меняют облик",
                    17, GamePalette.Paper, TextAnchor.MiddleLeft, FontStyle.Normal, new Vector2(0.045f, 0.79f), new Vector2(0.78f, 0.84f));
            }

            AddPresentationRail(layer, "MilestoneDock", new Vector2(0.025f, 0.035f), new Vector2(0.975f, 0.305f),
                new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.87f));
            string title = eraTransition ? "ПУТИ НОВОЙ ЭПОХИ" : "РАБОТЫ ЗАВЕРШЕНЫ · ХОД " + completedTurn;
            AddPresentationLabel(layer, "MilestoneTitle", title, 14, GamePalette.Gold,
                TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(0.05f, 0.255f), new Vector2(0.77f, 0.295f));

            int itemIndex = 0;
            if (eraTransition)
            {
                TechnologyDefinition[] technologies = RulesCatalog.Technologies;
                for (int i = 0; i < technologies.Length && itemIndex < 3; i++)
                {
                    if (technologies[i].Era != newEra) continue;
                    AddMilestoneItem(layer, itemIndex++, technologies[i].NameRu, "новое направление исследований", null);
                }
            }
            int totalMilestones = buildings.Count + improvements.Count;
            if (eraTransition)
            {
                TechnologyDefinition[] technologies = RulesCatalog.Technologies;
                for (int i = 0; i < technologies.Length; i++)
                    if (technologies[i].Era == newEra) totalMilestones++;
            }
            for (int i = 0; i < buildings.Count && itemIndex < 3; i++)
            {
                BuildingMilestone milestone = buildings[i];
                AddMilestoneItem(layer, itemIndex++, milestone.Definition.NameRu, milestone.CityName,
                    BuildingArtwork(milestone.Definition.Id, newEra));
            }
            for (int i = 0; i < improvements.Count && itemIndex < 3; i++)
            {
                ImprovementMilestone milestone = improvements[i];
                AddMilestoneItem(layer, itemIndex++, ImprovementName(milestone.Kind), "участок " + milestone.Q + ", " + milestone.R,
                    ImprovementArtwork(milestone.Kind, newEra));
            }
            if (itemIndex == 0)
                AddPresentationLabel(layer, "MilestoneNoCompletedWork", "Изменения уже видны на карте", 17, GamePalette.Paper,
                    TextAnchor.MiddleLeft, FontStyle.Normal, new Vector2(0.055f, 0.12f), new Vector2(0.56f, 0.22f));
            if (totalMilestones > itemIndex)
                AddPresentationLabel(layer, "MoreMilestones", "+ ещё " + (totalMilestones - itemIndex), 13, GamePalette.Muted,
                    TextAnchor.MiddleLeft, FontStyle.Normal, new Vector2(0.76f, 0.12f), new Vector2(0.83f, 0.2f));

            AddPresentationAction(layer, "MilestoneContinue", "ПРОДОЛЖИТЬ", GamePalette.Gold,
                new Vector2(0.84f, 0.09f), new Vector2(0.95f, 0.17f), delegate { dismissPresentationRequested = true; });
            CanvasGroup group = layer.gameObject.GetComponent<CanvasGroup>();
            if (group == null) group = layer.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            StartCoroutine(FadeCanvasGroup(group, 0.2f * AnimationScale));
        }

        private void AddMilestoneItem(Transform layer, int index, string title, string detail, string artwork)
        {
            float left = 0.055f + index * 0.225f;
            if (!string.IsNullOrEmpty(artwork))
            {
                Image icon = AddPresentationArtwork(layer, "MilestoneArt_" + index, artwork);
                if (icon != null) UiKit.SetAnchors(UiKit.Rect(icon.gameObject), new Vector2(left, 0.075f), new Vector2(left + 0.055f, 0.235f), Vector2.zero, Vector2.zero);
            }
            AddPresentationLabel(layer, "MilestoneItemTitle_" + index, title, 18, GamePalette.Paper,
                TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(left + 0.065f, 0.16f), new Vector2(left + 0.21f, 0.22f));
            AddPresentationLabel(layer, "MilestoneItemDetail_" + index, detail, 12, GamePalette.Muted,
                TextAnchor.MiddleLeft, FontStyle.Normal, new Vector2(left + 0.065f, 0.105f), new Vector2(left + 0.21f, 0.16f));
            if (index < 2)
                AddPresentationRail(layer, "MilestoneDivider_" + index, new Vector2(left + 0.215f, 0.09f), new Vector2(left + 0.216f, 0.23f),
                    new Color(GamePalette.Paper.r, GamePalette.Paper.g, GamePalette.Paper.b, 0.18f));
        }

        private Image AddPresentationArtwork(Transform parent, string objectName, string artwork)
        {
            Sprite sprite = LoadArtwork(artwork);
            if (sprite == null) return null;
            GameObject go = UiKit.CreateObject(objectName, parent);
            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            UiKit.SetAnchors(UiKit.Rect(go), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return image;
        }

        private static Image AddPresentationRail(Transform parent, string objectName, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            GameObject go = UiKit.CreateObject(objectName, parent);
            Image image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            UiKit.SetAnchors(UiKit.Rect(go), anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            return image;
        }

        private static Text AddPresentationLabel(Transform parent, string objectName, string value, int size, Color color,
            TextAnchor alignment, FontStyle style, Vector2 anchorMin, Vector2 anchorMax)
        {
            Text text = UiKit.Label(parent, value, size, color, alignment, style == FontStyle.Bold);
            text.gameObject.name = objectName;
            text.fontStyle = style;
            UiKit.SetAnchors(text.rectTransform, anchorMin, anchorMax, new Vector2(5, 1), new Vector2(-5, -1));
            return text;
        }

        private static void AddTextOutline(Text text)
        {
            if (text == null) return;
            Outline outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.88f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
        }

        private static Button AddPresentationAction(Transform parent, string objectName, string label, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Action action)
        {
            Button button = UiKit.Button(parent, label, action, color, 54, 15);
            button.gameObject.name = objectName;
            UiKit.SetAnchors(UiKit.Rect(button.gameObject), anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            return button;
        }

        private IEnumerator FadeCanvasGroup(CanvasGroup group, float duration)
        {
            if (group == null) yield break;
            float elapsed = 0f;
            while (elapsed < duration && group != null)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Clamp01(elapsed / duration);
                yield return null;
            }
            if (group != null) group.alpha = 1f;
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

        private IEnumerator AnimateTurnUnitMovements(GameObject layer, List<UnitMovePresentation> moves)
        {
            if (layer == null || moves == null || moves.Count == 0) yield break;
            Image blocker = layer.GetComponent<Image>();
            if (blocker != null) blocker.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.16f);
            DestroyChildren(layer.transform);
            yield return new WaitForEndOfFrame();
            Canvas.ForceUpdateCanvases();

            var movers = new List<RectTransform>();
            var starts = new List<Vector2>();
            var targets = new List<Vector2>();
            var durations = new List<float>();
            float longest = 0f;
            for (int i = 0; i < moves.Count; i++)
            {
                UnitMovePresentation move = moves[i];
                RectTransform fromHex = FindHex(move.StartQ, move.StartR);
                RectTransform toHex = FindHex(move.TargetQ, move.TargetR);
                if (fromHex == null || toHex == null) { movingUnitIds.Remove(move.UnitId); continue; }
                Vector2 fromScreen = RectScreenCenter(fromHex);
                Vector2 toScreen = RectScreenCenter(toHex);
                Vector2 fromLocal, toLocal;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(safeRoot, fromScreen, null, out fromLocal);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(safeRoot, toScreen, null, out toLocal);
                string artwork = UnitArtwork(move.Kind, CivilizationEra(move.OwnerId));
                Sprite sprite = LoadArtwork(artwork);
                if (sprite == null) { movingUnitIds.Remove(move.UnitId); continue; }
                GameObject mover = UiKit.CreateObject("MovingRivalUnit_" + move.UnitId, layer.transform);
                Image image = mover.AddComponent<Image>();
                image.sprite = sprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
                RectTransform rect = UiKit.Rect(mover);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(move.Kind == UnitKind.SettlerWagon ? 58 : 48, move.Kind == UnitKind.SettlerWagon ? 50 : 46);
                rect.anchoredPosition = fromLocal;
                movers.Add(rect);
                starts.Add(fromLocal);
                targets.Add(toLocal);
                float duration = (move.Kind == UnitKind.SettlerWagon ? 0.78f : 0.58f) * AnimationScale;
                durations.Add(duration);
                longest = Mathf.Max(longest, duration);
            }

            float elapsed = 0f;
            while (elapsed < longest)
            {
                elapsed += Time.unscaledDeltaTime;
                for (int i = 0; i < movers.Count; i++)
                {
                    if (movers[i] == null) continue;
                    float t = Mathf.Clamp01(elapsed / durations[i]);
                    float eased = t * t * (3f - 2f * t);
                    float arc = movers[i].sizeDelta.y * 0.4f;
                    movers[i].anchoredPosition = Vector2.Lerp(starts[i], targets[i], eased) + Vector2.up * Mathf.Sin(t * Mathf.PI) * arc;
                    movers[i].localScale = Vector3.one * (0.9f + 0.1f * Mathf.Sin(t * Mathf.PI));
                }
                yield return null;
            }
            movingUnitIds.Clear();
            if (blocker != null) blocker.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.02f);
            RenderGame();
        }

        private IEnumerator AnimateCommandBattle(BattleResolutionEvent battle, int startQ, int startR)
        {
            presentationBusy = true;
            GameObject layer = CreatePresentationLayer();
            yield return PlayBattleAttack(layer, battle, startQ, startR);
            if (presentationLayer != null) Destroy(presentationLayer);
            presentationLayer = null;
            presentationBusy = false;
            RenderGame();
        }

        private IEnumerator PlayBattleAttack(GameObject layer, BattleResolutionEvent battle, int attackerStartQ, int attackerStartR)
        {
            if (layer == null || battle == null) yield break;
            Image blocker = layer.GetComponent<Image>();
            if (blocker != null) blocker.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.025f);
            movingUnitIds.Clear();
            if (battle.AttackerId >= 0) movingUnitIds.Add(battle.AttackerId);
            if (battle.DefenderId >= 0 && battle.DefenderId != battle.AttackerId) movingUnitIds.Add(battle.DefenderId);
            RenderGame();
            yield return new WaitForEndOfFrame();
            Canvas.ForceUpdateCanvases();

            DestroyChildren(layer.transform);

            FocusMapHex(battle.CellQ, battle.CellR);
            Canvas.ForceUpdateCanvases();
            RectTransform targetHex = FindHex(battle.CellQ, battle.CellR);
            RectTransform startHex = FindHex(attackerStartQ, attackerStartR);
            if (targetHex == null)
            {
                movingUnitIds.Clear();
                RenderGame();
                yield break;
            }
            Vector2 targetScreen = RectScreenCenter(targetHex);
            Vector2 startScreen = startHex == null ? targetScreen + Vector2.left * MapHexWidth * 1.35f : RectScreenCenter(startHex);
            Vector2 targetLocal, startLocal;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(safeRoot, targetScreen, null, out targetLocal);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(safeRoot, startScreen, null, out startLocal);
            Vector2 direction = (targetLocal - startLocal).normalized;
            if (direction.sqrMagnitude < 0.01f) direction = Vector2.right;
            Vector2 strikeLocal = targetLocal - direction * (MapHexWidth * 0.48f);

            GameObject trailObject = UiKit.CreateObject("BattleAttackTrail", layer.transform);
            Image trail = trailObject.AddComponent<Image>();
            trail.color = new Color(GamePalette.Gold.r, GamePalette.Gold.g, GamePalette.Gold.b, 0.66f);
            trail.raycastTarget = false;
            RectTransform trailRect = UiKit.Rect(trailObject);
            trailRect.anchorMin = trailRect.anchorMax = new Vector2(0.5f, 0.5f);
            trailRect.pivot = new Vector2(0.5f, 0.5f);
            trailRect.sizeDelta = new Vector2(Vector2.Distance(startLocal, strikeLocal) * 0.56f, 3f);
            trailRect.anchoredPosition = Vector2.Lerp(startLocal, strikeLocal, 0.5f);
            trailRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            CanvasGroup trailGroup = trailObject.AddComponent<CanvasGroup>();
            trailGroup.alpha = 0.45f;

            RectTransform attacker = CreateBattleUnitSprite(layer.transform, battle.AttackerKind,
                CivilizationEra(battle.AttackerOwnerId), startLocal, "BattleAttackerMotion");
            RectTransform defender = CreateBattleUnitSprite(layer.transform, battle.DefenderKind,
                CivilizationEra(battle.DefenderOwnerId), targetLocal, "BattleDefenderMotion");
            CanvasGroup defenderGroup = defender == null ? null : defender.GetComponent<CanvasGroup>();
            Text impact = UiKit.OverlayText(layer.transform, "✦", 46, GamePalette.Gold, TextAnchor.MiddleCenter);
            RectTransform impactRect = impact.rectTransform;
            impactRect.anchorMin = impactRect.anchorMax = new Vector2(0.5f, 0.5f);
            impactRect.sizeDelta = new Vector2(76, 76);
            impactRect.anchoredPosition = targetLocal;
            impact.transform.localScale = Vector3.one * 0.3f;
            CanvasGroup impactGroup = impact.gameObject.AddComponent<CanvasGroup>();
            impactGroup.alpha = 0f;

            Text outcome = UiKit.OverlayText(layer.transform,
                battle.AttackerWon ? "АТАКА" : "ОБОРОНА", 15, GamePalette.Paper, TextAnchor.MiddleCenter);
            RectTransform outcomeRect = outcome.rectTransform;
            outcomeRect.anchorMin = outcomeRect.anchorMax = new Vector2(0.5f, 0.5f);
            outcomeRect.sizeDelta = new Vector2(160, 34);
            outcomeRect.anchoredPosition = targetLocal + Vector2.up * 58f;
            outcome.fontStyle = FontStyle.Bold;
            CanvasGroup outcomeGroup = outcome.gameObject.AddComponent<CanvasGroup>();
            outcomeGroup.alpha = 0f;

            float approachDuration = 0.24f * AnimationScale;
            float elapsed = 0f;
            while (elapsed < approachDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / approachDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                if (attacker != null)
                {
                    attacker.anchoredPosition = Vector2.Lerp(startLocal, strikeLocal, eased) + Vector2.up * Mathf.Sin(t * Mathf.PI) * 12f;
                    attacker.localScale = Vector3.one * (0.9f + 0.14f * Mathf.Sin(t * Mathf.PI));
                }
                if (trailGroup != null) trailGroup.alpha = 0.45f * (1f - t * 0.45f);
                yield return null;
            }

            PlaySoundEffect("Battle_Resolve");
            elapsed = 0f;
            float hitDuration = 0.19f * AnimationScale;
            while (elapsed < hitDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / hitDuration);
                if (attacker != null) attacker.anchoredPosition = Vector2.Lerp(strikeLocal, targetLocal - direction * 19f, t);
                impactGroup.alpha = Mathf.Sin(t * Mathf.PI);
                impact.transform.localScale = Vector3.one * (0.6f + t * 0.65f);
                if (defender != null) defender.anchoredPosition = targetLocal + direction * Mathf.Sin(t * Mathf.PI) * 9f;
                yield return null;
            }

            UnitState attackerAfterBattle = UnitById(battle.AttackerId);
            CanvasGroup attackerGroup = attacker == null ? null : attacker.GetComponent<CanvasGroup>();
            if (!battle.AttackerWon && attackerAfterBattle == null && attackerGroup != null)
            {
                elapsed = 0f;
                float fadeDuration = 0.24f * AnimationScale;
                while (elapsed < fadeDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / fadeDuration);
                    attackerGroup.alpha = 1f - t;
                    if (attacker != null) attacker.localScale = Vector3.one * (1f - t * 0.22f);
                    yield return null;
                }
            }
            else if (!battle.AttackerWon && attacker != null)
            {
                float retreatDuration = 0.28f * AnimationScale;
                elapsed = 0f;
                while (elapsed < retreatDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / retreatDuration);
                    attacker.anchoredPosition = Vector2.Lerp(strikeLocal, startLocal, t);
                    yield return null;
                }
            }

            if (battle.DefenderRetreated)
            {
                RectTransform retreatHex = FindHex(battle.RetreatQ, battle.RetreatR);
                if (retreatHex != null && defender != null)
                {
                    Vector2 retreatLocal;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(safeRoot, RectScreenCenter(retreatHex), null, out retreatLocal);
                    float retreatDuration = 0.28f * AnimationScale;
                    elapsed = 0f;
                    while (elapsed < retreatDuration)
                    {
                        elapsed += Time.unscaledDeltaTime;
                        float t = Mathf.Clamp01(elapsed / retreatDuration);
                        defender.anchoredPosition = Vector2.Lerp(targetLocal, retreatLocal, t);
                        defender.localScale = Vector3.one * (1f - t * 0.08f);
                        yield return null;
                    }
                }
            }
            else if (battle.DefenderDestroyed && defenderGroup != null)
            {
                elapsed = 0f;
                float fadeDuration = 0.24f * AnimationScale;
                while (elapsed < fadeDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / fadeDuration);
                    defenderGroup.alpha = 1f - t;
                    if (defender != null) defender.localScale = Vector3.one * (1f - t * 0.22f);
                    yield return null;
                }
            }

            string winner = battle.AttackerWon ? "Атака удержала позицию" : "Защитники отбили атаку";
            string losses = "Потери: " + Format(battle.AttackerDeathsTenths + battle.DefenderDeathsTenths) + " погибло · " +
                Format(battle.AttackerWoundedTenths + battle.DefenderWoundedTenths) + " ранено";
            notice = winner + " · " + losses + ". Подробности сохранены в летописи.";
            outcome.text = battle.AttackerWon ? "ПОБЕДА АТАКИ" : "ПОБЕДА ОБОРОНЫ";
            outcomeGroup.alpha = 1f;
            impactGroup.alpha = 0f;
            float resultHold = 0.28f * AnimationScale;
            while (resultHold > 0f)
            {
                resultHold -= Time.unscaledDeltaTime;
                yield return null;
            }
            movingUnitIds.Clear();
            RenderGame();
        }

        private RectTransform CreateBattleUnitSprite(Transform parent, UnitKind kind, Era era, Vector2 position, string name)
        {
            string artwork = UnitArtwork(kind, era);
            Sprite sprite = LoadArtwork(artwork);
            if (sprite == null) return null;
            GameObject unit = UiKit.CreateObject(name, parent);
            Image image = unit.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            RectTransform rect = UiKit.Rect(unit);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = kind == UnitKind.SettlerWagon ? new Vector2(70, 64) : new Vector2(60, 58);
            rect.anchoredPosition = position;
            CanvasGroup group = unit.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            return rect;
        }

        private bool IsUnitMoving(int unitId)
        {
            return unitId == movingUnitId || movingUnitIds.Contains(unitId);
        }

        private RectTransform FindHex(int q, int r)
        {
            if (pageHost == null) return null;
            RectTransform[] rects = pageHost.GetComponentsInChildren<RectTransform>(true);
            string id = "Hex_" + q + "_" + r;
            for (int i = 0; i < rects.Length; i++) if (rects[i].name == id) return rects[i];
            return null;
        }

        private void FocusMapHex(int q, int r)
        {
            if (activeMapScroll == null || activeMapContent == null || activeMapViewport == null) return;
            RectTransform hex = FindHex(q, r);
            if (hex == null) return;
            float contentWidth = activeMapContent.rect.width * mapZoom;
            float contentHeight = activeMapContent.rect.height * mapZoom;
            float viewWidth = activeMapViewport.rect.width;
            float viewHeight = activeMapViewport.rect.height;
            float rangeX = Mathf.Max(0f, contentWidth - viewWidth);
            float rangeY = Mathf.Max(0f, contentHeight - viewHeight);
            float offsetX = hex.anchoredPosition.x * mapZoom - viewWidth * 0.5f;
            float offsetY = -hex.anchoredPosition.y * mapZoom - viewHeight * 0.5f;
            float horizontal = rangeX <= 0f ? 0.5f : Mathf.Clamp01(offsetX / rangeX);
            float vertical = rangeY <= 0f ? 0.5f : 1f - Mathf.Clamp01(offsetY / rangeY);
            activeMapScroll.normalizedPosition = new Vector2(horizontal, vertical);
            mapScrollPosition = activeMapScroll.normalizedPosition;
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
            int eraKnowledge = CountTechnologiesInEra(owner, owner.Era);
            int councils = CountCitiesWithBuilding(owner.Id, "council");
            int transitionCost = GameSimulation.EraTransitionProductionCost(owner.Era);
            bool transitionInProgress = HasEraTransition(simulation.State, owner.Id);
            UiKit.Label(summary.transform, "Знания поступают от учёных и школ. Для общественного перехода нужны три технологии текущей эпохи, Совет и 10 влияния; выбранный город вложит " + transitionCost + " производства.",
                20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            Button eraButton = UiKit.Button(summary.transform, (transitionInProgress ? "ПЕРЕХОД ЭПОХИ УЖЕ ИДЁТ" : "НАЧАТЬ ПЕРЕХОД ЭПОХИ") + " · " + eraKnowledge + "/3 · СОВЕТ " + (councils > 0 ? "ЕСТЬ" : "НЕТ") + " · 10 ВЛИЯНИЯ · " + transitionCost + " ПРОИЗВОДСТВА",
                delegate { Issue(CommandKind.SetProject, SelectedCity().Id, text: "era_transition"); }, GamePalette.Gold, 70, 19);
            eraButton.interactable = (int)owner.Era < 4 && !transitionInProgress && eraKnowledge >= 3 && councils > 0 && owner.InfluenceTenths >= 100 &&
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
                NationDefinition otherNation = NationCatalog.Get(other.NationId);
                if (otherNation != null)
                {
                    GameObject leaderRow = UiKit.CreateObject("DiplomacyLeader_" + other.Id, card.transform);
                    UiKit.Horizontal(leaderRow, 4, 10);
                    AddCityBuildingArtwork(leaderRow.transform, otherNation.LeaderArtKey, 64);
                    Text leader = UiKit.Label(leaderRow.transform, otherNation.LeaderNameRu + " · " + otherNation.LeaderTitleRu + "\n" + otherNation.LeaderHistoricalNoteRu,
                        19, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                    leader.GetComponent<LayoutElement>().flexibleWidth = 1f;
                }
                UiKit.Label(card.transform, "Отношение " + player.Relations[other.Id] + " · доверие " + player.Trusts[other.Id] +
                    " · угроза " + player.Threats[other.Id] + " · репутация соседа " + other.Reputation + " · курс: " + AiGoalName(other.StrategicGoal) + " · статус: " + status,
                    21, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                UiKit.Label(card.transform, "Договоры требуют влияния. Торговля безопасна, но разрыв маршрута теряет накопленные доходы и отношения.",
                    18, GamePalette.Muted, TextAnchor.MiddleLeft, false);
                for (int treatyIndex = 0; treatyIndex < state.Treaties.Length; treatyIndex++)
                {
                    TreatyState treaty = state.Treaties[treatyIndex];
                    bool pair = (treaty.FromCivilizationId == player.Id && treaty.ToCivilizationId == other.Id) ||
                                (treaty.FromCivilizationId == other.Id && treaty.ToCivilizationId == player.Id);
                    if (!pair || !treaty.Active) continue;
                    string remaining = treaty.TurnsRemaining <= 0 ? "бессрочно" : "ещё " + treaty.TurnsRemaining + " ход.";
                    UiKit.Label(card.transform, "Действует: " + TreatyName(treaty.Kind) + " · " + remaining, 18, GamePalette.Teal, TextAnchor.MiddleLeft, false);
                }
                if (HasPrisonersHeld(state, player.Id, other.Id))
                {
                    int heldByPlayer = player.PrisonersHeldByCivilizationTenths[other.Id];
                    int heldByOther = other.PrisonersHeldByCivilizationTenths[player.Id];
                    UiKit.Label(card.transform, "Военнопленные: у вас " + Format(heldByPlayer) + " · у соседа " + Format(heldByOther) +
                        " населения. Их можно обменять при переговорах о мире.", 18, GamePalette.Gold, TextAnchor.MiddleLeft, false);
                }
                GameObject row1 = UiKit.CreateObject("TreatyRow1", card.transform);
                UiKit.Horizontal(row1, 0, 8);
                AddActionButton(row1.transform, "ТОРГОВЛЯ · 2 ВЛИЯНИЯ · БЕССРОЧНО", delegate { OfferTreaty(other.Id, TreatyKind.TradeRoute); }, GamePalette.Teal, 56);
                AddActionButton(row1.transform, "ОТКРЫТЫЕ ГРАНИЦЫ · 4 · 5 ХОДОВ", delegate { OfferTreaty(other.Id, TreatyKind.OpenBorders); }, GamePalette.Ink, 56);
                GameObject row2 = UiKit.CreateObject("TreatyRow2", card.transform);
                UiKit.Horizontal(row2, 0, 8);
                AddActionButton(row2.transform, "НЕНАПАДЕНИЕ · 5 ВЛИЯНИЯ · 5 ХОДОВ", delegate { OfferTreaty(other.Id, TreatyKind.NonAggression); }, GamePalette.Ink, 56);
                AddActionButton(row2.transform, "ОБМЕН ЗНАНИЯМИ · 3 · РАЗОВО", delegate { OfferTreaty(other.Id, TreatyKind.KnowledgeExchange); }, GamePalette.Ink, 56);
                GameObject row3 = UiKit.CreateObject("TreatyRow3", card.transform);
                UiKit.Horizontal(row3, 0, 8);
                AddActionButton(row3.transform, "ГАРАНТИЯ · 8 ВЛИЯНИЯ · 5 ХОДОВ", delegate { OfferTreaty(other.Id, TreatyKind.Guarantee); }, GamePalette.Teal, 56);
                Button embargoButton = AddActionButton(row3.transform, "ЭМБАРГО · 3 ВЛИЯНИЯ · 3 ХОДА", delegate { OfferTreaty(other.Id, TreatyKind.Embargo); }, GamePalette.Rust, 56);
                embargoButton.interactable = CanOfferEmbargo(state, player.Id, other.Id);
                GameObject row4 = UiKit.CreateObject("TreatyRow4", card.transform);
                UiKit.Horizontal(row4, 0, 8);
                AddActionButton(row4.transform, "ЭКСПОРТ СОЛИ · 2 ВЛИЯНИЯ · 5 ХОДОВ", delegate { OfferTreaty(other.Id, TreatyKind.ResourceExport); }, GamePalette.Gold, 56);
                AddActionButton(row4.transform, "ПОМОЩЬ · 5 КАЗНЫ / 2 ВЛИЯНИЯ", delegate { OfferTreaty(other.Id, TreatyKind.HumanitarianAid); }, GamePalette.Teal, 56);
                if (HasMediableConflict(state, player.Id, other.Id))
                    UiKit.Button(card.transform, "ПОСРЕДНИЧЕСТВО · 5 ВЛИЯНИЯ", delegate { OfferTreaty(other.Id, TreatyKind.Mediation); }, GamePalette.Ink, 58, 18);
                if (conflict != null && conflict.Phase >= 0 && conflict.Phase <= 2)
                {
                    GameObject peaceTerms = UiKit.CreateObject("PeaceTerms_" + other.Id, card.transform);
                    UiKit.Horizontal(peaceTerms, 0, 8);
                    AddActionButton(peaceTerms.transform, "СТАТУС-КВО · 5 КАЗНЫ", delegate { Issue(CommandKind.MakePeace, targetId: other.Id, value: (int)PeaceTermKind.StatusQuo); }, GamePalette.Ink, 58);
                    AddActionButton(peaceTerms.transform, "КОМПЕНСАЦИЯ ДО 10 КАЗНЫ", delegate { Issue(CommandKind.MakePeace, targetId: other.Id, value: (int)PeaceTermKind.Reparations); }, GamePalette.Gold, 58);
                    AddActionButton(peaceTerms.transform, "ДЕМОБИЛИЗАЦИЯ", delegate { Issue(CommandKind.MakePeace, targetId: other.Id, value: (int)PeaceTermKind.Demobilization); }, GamePalette.Rust, 58);
                    if (CanClaimBorderCell(state, conflict, other.Id))
                        AddActionButton(peaceTerms.transform, "ПЕРЕДАТЬ СПОРНУЮ КЛЕТКУ", delegate { Issue(CommandKind.MakePeace, targetId: other.Id, value: (int)PeaceTermKind.BorderAdjustment); }, GamePalette.Teal, 58);
                    if (CanAnnexOccupiedCity(state, player.Id, other.Id, conflict))
                        AddActionButton(peaceTerms.transform, "АННЕКСИЯ ГОРОДА · 15 ВЛИЯНИЯ", delegate { Issue(CommandKind.MakePeace, targetId: other.Id, value: (int)PeaceTermKind.AnnexOccupiedCity); }, GamePalette.Rust, 58);
                    if (HasPrisonersHeld(state, player.Id, other.Id))
                        AddActionButton(peaceTerms.transform, "ОБМЕНЯТЬ ВОЕННОПЛЕННЫХ", delegate { Issue(CommandKind.MakePeace, targetId: other.Id, value: (int)PeaceTermKind.PrisonerExchange); }, GamePalette.Teal, 58);
                }
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
                bool breaksPact = false;
                for (int treatyIndex = 0; treatyIndex < state.Treaties.Length; treatyIndex++)
                {
                    TreatyState treaty = state.Treaties[treatyIndex];
                    if (treaty.Active && treaty.Kind == TreatyKind.NonAggression &&
                        ((treaty.FromCivilizationId == player.Id && treaty.ToCivilizationId == target.Id) ||
                         (treaty.FromCivilizationId == target.Id && treaty.ToCivilizationId == player.Id)))
                    {
                        breaksPact = true;
                        break;
                    }
                }
                bool breaksGuarantee = false;
                for (int treatyIndex = 0; treatyIndex < state.Treaties.Length; treatyIndex++)
                {
                    TreatyState treaty = state.Treaties[treatyIndex];
                    if (!treaty.Active || treaty.Kind != TreatyKind.Guarantee) continue;
                    int guarantorId = treaty.FromCivilizationId == target.Id ? treaty.ToCivilizationId :
                        treaty.ToCivilizationId == target.Id ? treaty.FromCivilizationId : -1;
                    if (guarantorId >= 0 && guarantorId != player.Id) { breaksGuarantee = true; break; }
                }
                if (breaksPact)
                    UiKit.Label(war.transform, "Нарушение действующего пакта дополнительно снизит вашу репутацию на 20 и ухудшит отношения с соседом.",
                        18, GamePalette.Rust, TextAnchor.MiddleLeft, true);
                if (breaksGuarantee)
                    UiKit.Label(war.transform, "Соседа защищает гарантия: объявление войны повысит угрозу у гаранта, снизит доверие и отнимет ещё 10 репутации.",
                        18, GamePalette.Rust, TextAnchor.MiddleLeft, true);
                UiKit.Label(war.transform, "Выберите цель заранее: она определит доступные условия мира. Военная победа не превращает город в бесплатную добычу.",
                    18, GamePalette.Muted, TextAnchor.MiddleLeft, false);
                string[] warGoals = { "защита спорной границы", "снятие торговой блокады", "демилитаризация границы", "защита гражданского коридора", "территориальный спор вокруг города" };
                Color[] goalColors = { GamePalette.Danger, GamePalette.Rust, GamePalette.Ink, GamePalette.Teal };
                HexCell claim = FindClaimCell(state, player.Id, target.Id);
                int claimQ = claim == null ? q : claim.Q;
                int claimR = claim == null ? r : claim.R;
                for (int goalIndex = 0; goalIndex < warGoals.Length; goalIndex++)
                {
                    string legalGoal = warGoals[goalIndex];
                    int goalQ = goalIndex == 4 ? q : claimQ;
                    int goalR = goalIndex == 4 ? r : claimR;
                    Button declare = UiKit.Button(war.transform,
                        legalGoal.ToUpperInvariant() + " · ПРОТИВ " + target.Name.ToUpperInvariant() + " · 8 КАЗНЫ",
                        delegate { Issue(CommandKind.DeclareWar, targetId: id, q: goalQ, r: goalR, text: legalGoal); }, goalColors[goalIndex], 58, 16);
                    declare.interactable = player.TreasuryTenths >= 80;
                }
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
                case TreatyKind.ResourceExport: return "экспорт соли";
                case TreatyKind.HumanitarianAid: return "гуманитарная помощь";
                case TreatyKind.Mediation: return "посредничество";
                case TreatyKind.Peace: return "перемирие";
                default: return "соглашение";
            }
        }

        private static bool HasMediableConflict(GameState state, int mediatorId, int partyId)
        {
            for (int i = 0; i < state.Conflicts.Length; i++)
            {
                ConflictState conflict = state.Conflicts[i];
                if (conflict.Active && conflict.Phase <= 2 &&
                    (conflict.AggressorId == partyId || conflict.DefenderId == partyId) &&
                    conflict.AggressorId != mediatorId && conflict.DefenderId != mediatorId) return true;
            }
            return false;
        }

        private bool CanOfferEmbargo(GameState state, int firstId, int secondId)
        {
            CityState firstCity = SelectedCity();
            CityState secondCity = FirstCity(secondId);
            if (firstCity == null || secondCity == null) return false;
            bool activeRoute = false;
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (!treaty.Active || (treaty.Kind != TreatyKind.TradeRoute && treaty.Kind != TreatyKind.ResourceExport)) continue;
                bool sameCities = (treaty.RouteCityA == firstCity.Id && treaty.RouteCityB == secondCity.Id) ||
                                  (treaty.RouteCityA == secondCity.Id && treaty.RouteCityB == firstCity.Id);
                if (sameCities) { activeRoute = true; break; }
            }
            if (!activeRoute) return false;
            for (int i = 0; i < state.Conflicts.Length; i++)
            {
                ConflictState conflict = state.Conflicts[i];
                if (conflict.Active && conflict.Phase <= 1 &&
                    ((conflict.AggressorId == firstId && conflict.DefenderId == secondId) ||
                     (conflict.AggressorId == secondId && conflict.DefenderId == firstId))) return true;
            }
            CivilizationState first = state.Civilization(firstId);
            CivilizationState second = state.Civilization(secondId);
            bool recordedRoute = false;
            for (int i = 0; i < state.Treaties.Length; i++)
                if ((state.Treaties[i].Kind == TreatyKind.TradeRoute || state.Treaties[i].Kind == TreatyKind.ResourceExport) &&
                    ((state.Treaties[i].FromCivilizationId == firstId && state.Treaties[i].ToCivilizationId == secondId) ||
                     (state.Treaties[i].FromCivilizationId == secondId && state.Treaties[i].ToCivilizationId == firstId))) recordedRoute = true;
            return recordedRoute && (first.Threats[secondId] >= 60 || second.Threats[firstId] >= 60);
        }

        private static bool CanAnnexOccupiedCity(GameState state, int actorId, int targetId, ConflictState conflict)
        {
            if (conflict == null || conflict.Phase < 0 || conflict.Phase > 2) return false;
            string goal = (conflict.LegalGoal ?? string.Empty).ToLowerInvariant();
            if (!goal.Contains("город") && !goal.Contains("территор")) return false;
            CivilizationState actor = state.Civilization(actorId);
            int owned = 0;
            bool occupied = false;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId == actorId) owned++;
                if (city.OwnerId == targetId && city.OccupiedById == actorId) occupied = true;
            }
            return occupied && actor != null && owned < actor.AdministrativeCapacity && actor.InfluenceTenths >= 150 && actor.TreasuryTenths >= 50;
        }

        private static bool HasPrisonersHeld(GameState state, int firstId, int secondId)
        {
            CivilizationState first = state.Civilization(firstId);
            CivilizationState second = state.Civilization(secondId);
            return first != null && second != null && first.PrisonersHeldByCivilizationTenths != null && second.PrisonersHeldByCivilizationTenths != null &&
                   secondId < first.PrisonersHeldByCivilizationTenths.Length && firstId < second.PrisonersHeldByCivilizationTenths.Length &&
                   (first.PrisonersHeldByCivilizationTenths[secondId] > 0 || second.PrisonersHeldByCivilizationTenths[firstId] > 0);
        }

        private static bool CanClaimBorderCell(GameState state, ConflictState conflict, int targetId)
        {
            HexCell cell = state.Cell(conflict.TargetQ, conflict.TargetR);
            if (cell == null || cell.OwnerId != targetId || cell.DistrictCityId >= 0 ||
                !string.IsNullOrEmpty(cell.DistrictBuildingId) || cell.Terrain == TerrainKind.Water || cell.Terrain == TerrainKind.Mountain) return false;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].Q == cell.Q && state.Cities[i].R == cell.R) return false;
            return true;
        }

        private static HexCell FindClaimCell(GameState state, int claimantId, int targetId)
        {
            HexCell best = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell cell = state.Cells[i];
                if (cell.OwnerId != targetId || cell.Terrain == TerrainKind.Water || cell.Terrain == TerrainKind.Mountain ||
                    cell.Terrain == TerrainKind.Swamp || cell.DistrictCityId >= 0 || !string.IsNullOrEmpty(cell.DistrictBuildingId)) continue;
                bool cityCell = false;
                for (int c = 0; c < state.Cities.Length; c++)
                    if (state.Cities[c].Q == cell.Q && state.Cities[c].R == cell.R) { cityCell = true; break; }
                if (cityCell) continue;
                bool border = false;
                for (int d = 0; d < 6; d++)
                {
                    HexCell neighbor = state.Cell(cell.Q + HexGrid.Directions[d, 0], cell.R + HexGrid.Directions[d, 1]);
                    if (neighbor != null && neighbor.OwnerId == claimantId) { border = true; break; }
                }
                if (!border) continue;
                int nearestClaimant = int.MaxValue;
                int nearestTarget = int.MaxValue;
                for (int c = 0; c < state.Cities.Length; c++)
                {
                    CityState city = state.Cities[c];
                    int distance = HexGrid.Distance(cell.Q, cell.R, city.Q, city.R);
                    if (city.OwnerId == claimantId) nearestClaimant = Mathf.Min(nearestClaimant, distance);
                    if (city.OwnerId == targetId) nearestTarget = Mathf.Min(nearestTarget, distance);
                }
                int score = nearestClaimant * 3 + nearestTarget;
                if (score < bestScore) { bestScore = score; best = cell; }
            }
            return best;
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
                int usefulContribution = Math.Min(5, Math.Max(1, (state.Crisis.Threat + 3) / 4));
                UiKit.Label(crisis.transform, state.Crisis.PublicText + " · угроза " + state.Crisis.Threat + " · ход " + state.Crisis.TurnsActive,
                    21, GamePalette.Paper, TextAnchor.MiddleLeft, false);
                GameObject row = UiKit.CreateObject("CrisisResources", crisis.transform);
                UiKit.Horizontal(row, 0, 8);
                AddActionButton(row.transform, "ВНЕСТИ ДО " + usefulContribution + " ЕДЫ", delegate { Issue(CommandKind.ContributeToCrisis, text: "food", value: 5); }, GamePalette.Teal, 58);
                AddActionButton(row.transform, "ВНЕСТИ ДО " + usefulContribution + " ЗНАНИЙ", delegate { Issue(CommandKind.ContributeToCrisis, text: "knowledge", value: 5); }, GamePalette.Ink, 58);
                AddActionButton(row.transform, "ВНЕСТИ ДО " + usefulContribution + " ВЛИЯНИЯ", delegate { Issue(CommandKind.ContributeToCrisis, text: "influence", value: 5); }, GamePalette.Rust, 58);
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
                    button.interactable = ChoiceAvailable(state, active, choice, player, city);
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
                case "ExchangeLand": return "Обменять свободные приграничные земли · 2 влияния";
                case "Ultimatum": return "Выдвинуть ультиматум соседу";
                case "EmergencyStock": return "Открыть резервы · 6 казны";
                case "NewRoute": return "Открыть торговый маршрут · 1 влияние";
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
                case "Retreat": return "Отступить к ближайшему безопасному поселению";
                case "Internment": return "Принять интернирование · обоз пропустит этот ход";
                case "ContinueJourney": return "Рискнуть прорывом · потерять часть жителей";
                case "AcceptMediation": return "Принять нейтральное перемирие · снизить напряжение";
                case "RejectMediation": return "Отклонить · продолжить войну с ростом напряжения";
                default: return choice;
            }
        }

        private static bool ChoiceAvailable(GameState state, ActiveEventState active, string choice, CivilizationState player, CityState city)
        {
            if (active.Kind == EventKind.NeutralMediation)
            {
                for (int i = 0; i < state.Conflicts.Length; i++)
                {
                    ConflictState conflict = state.Conflicts[i];
                    if (conflict.Active && conflict.Phase == 2 &&
                        ((conflict.AggressorId == player.Id && conflict.DefenderId == active.OtherCivilizationId) ||
                         (conflict.DefenderId == player.Id && conflict.AggressorId == active.OtherCivilizationId)))
                        return choice == "AcceptMediation" || choice == "RejectMediation";
                }
                return false;
            }
            if (active.Kind == EventKind.SettlerInterception)
            {
                UnitState wagon = null;
                for (int i = 0; i < state.Units.Length; i++)
                    if (state.Units[i].Id == active.TargetUnitId && state.Units[i].OwnerId == player.Id && state.Units[i].Kind == UnitKind.SettlerWagon)
                        wagon = state.Units[i];
                return wagon != null && (choice == "Retreat" || choice == "Internment" || choice == "ContinueJourney");
            }
            if (choice == "BuyFood") return player.TreasuryTenths >= 50;
            if (choice == "OpenClinic") return player.TreasuryTenths >= 100;
            if (choice == "BuildHub" || choice == "Filters") return player.TreasuryTenths >= 60;
            if (choice == "Accept") return player.TreasuryTenths >= 40 && city != null && city.PopulationTenths + 10 <= city.HousingTenths;
            if (choice == "OpenCorridor" || choice == "Audit" || choice == "Experiment" || choice == "JointMuseum") return player.InfluenceTenths >= 20;
            if (choice == "Investigate") return player.TreasuryTenths >= 50;
            if (choice == "FundSchools") return player.TreasuryTenths >= 80;
            if (choice == "ExchangeLand") return player.InfluenceTenths >= 20 && GameSimulation.CanExchangeLand(state, active.TargetCityId, player.Id, active.OtherCivilizationId);
            if (choice == "EmergencyStock" || choice == "Festival") return player.TreasuryTenths >= (choice == "Festival" ? 40 : 60);
            if (choice == "PaySupport") return player.TreasuryTenths >= 80;
            if (choice == "JointProject") return player.KnowledgeTenths >= 100;
            if (choice == "ShareKnowledge") return player.KnowledgeTenths >= 50;
            if (choice == "NewRoute") return GameSimulation.CanStartTradeRouteFromEvent(state, player.Id);
            if (choice == "DomesticSupply") return GameSimulation.CanStartDomesticSupply(state, city);
            if (choice == "LocalTruce") return GameSimulation.HasLocalTruceOption(state, player.Id);
            return true;
        }

        private void RenderVictory(Transform parent)
        {
            GameState state = simulation.State;
            CivilizationState player = state.Civilization(state.HumanCivilizationId);
            if (state.IsFinished)
            {
                int[] winners = state.WinningCivilizationIds ?? Array.Empty<int>();
                if (winners.Length == 0 && state.WinnerCivilizationId >= 0) winners = new[] { state.WinnerCivilizationId };
                var winnerNames = new List<string>();
                bool playerWon = false;
                for (int i = 0; i < winners.Length; i++)
                {
                    CivilizationState candidate = state.Civilization(winners[i]);
                    if (candidate != null) winnerNames.Add(candidate.Name);
                    if (winners[i] == player.Id) playerWon = true;
                }
                string resultTitle = winnerNames.Count == 0 ? "Кампания завершена" : winnerNames.Count == 1 ? winnerNames[0] + " победила" : "Совместная победа · " + string.Join(", ", winnerNames.ToArray());
                GameObject final = UiKit.Card(parent, resultTitle);
                AddArtwork(final.transform, playerWon ? "Backgrounds/Victory_Background" : "Backgrounds/Defeat_Background", 230);
                UiKit.Label(final.transform, "Путь: " + VictoryEvaluator.PathName(state.WinningPath) + "\n" +
                    (playerWon ? "Вы завершили кампанию победой." : "Победила другая цивилизация. Вы можете изучить итоги в летописи."),
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
            int totalPopulation = 0;
            int qualifiedPopulation = 0;
            int lowestQuality = 101;
            int highestQuality = -1;
            bool cityServicesReady = true;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != player.Id) continue;
                qualityCities++;
                int index = VictoryEvaluator.CityQualityIndex(state, city);
                lowestQuality = Math.Min(lowestQuality, index);
                highestQuality = Math.Max(highestQuality, index);
                totalPopulation += city.PopulationTenths;
                if (index >= 75) qualifiedPopulation += city.PopulationTenths;
                if (city.Health < 60 || city.Safety < 60) cityServicesReady = false;
            }
            GameObject quality = UiKit.Card(parent, "Благополучие · пассивный путь");
            UiKit.Label(quality.transform, "Пять ходов подряд: 4+ города; индекс качества жизни ≥75 у 80% населения; здоровье и безопасность каждого города ≥60; разница индексов ≤20; дефицит не дольше двух ходов.",
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Label(quality.transform, "Города " + qualityCities + "/4 · население с индексом ≥75: " + (totalPopulation == 0 ? 0 : qualifiedPopulation * 100 / totalPopulation) + "% · диапазон индекса " +
                (qualityCities == 0 ? "—" : lowestQuality + "–" + highestQuality) + " · состояние служб " + (cityServicesReady ? "в норме" : "нуждается в улучшении") +
                " · спокойные ходы " + player.QualityVictoryTurns + "/5", 21, GamePalette.Sage, TextAnchor.MiddleLeft, true);

            int universities = CountCitiesWithBuilding(player.Id, "university");
            int academies = CountEmpireBuilding(player.Id, "academy");
            bool scienceReady = player.Knows("computing_networks") && player.Knows("open_science") && universities >= 2 && academies >= 1 && player.KnowledgeTenths >= 300 && player.InfluenceTenths >= 80;
            GameObject science = UiKit.Card(parent, "Научный прорыв · активный путь");
            UiKit.Label(science.transform, "Исследуйте вычислительные сети и открытую науку; постройте университеты в двух городах и академию; внесите 30 знаний, 20 ПП города и 8 влияния. Проект длится 4 хода, последние два выпуска знаний должны быть ≥5.",
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Label(science.transform, "Города с университетом " + universities + "/2 · академии " + academies + "/1 · прогресс " + player.ScienceProjectTurns + "/4 · выпуск знаний " +
                Format(player.PreviousTurnKnowledgeOutputTenths) + "/" + Format(player.LastTurnKnowledgeOutputTenths) + " из 5", 20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            Button scienceButton = UiKit.Button(science.transform, "НАЧАТЬ ПРОГРАММУ В ГОРОДЕ", delegate { Issue(CommandKind.BeginVictoryProject, SelectedCity().Id, value: (int)VictoryPath.Scientific); }, GamePalette.Teal, 62, 19);
            scienceButton.interactable = scienceReady && (SelectedCity().Project == null || SelectedCity().Project.Kind == ProjectKind.None);

            int culturalObjects = CountCulturalObjects(player.Id);
            GameObject culture = UiKit.Card(parent, "Культурное наследие · активный путь");
            UiKit.Label(culture.transform, "Постройте культурный дом, архив и Дом обмена в трёх разных городах; накопите 100 престижа; влияние должно быть 70/70/40. Оплатите 20 влияния и 10 казны и завершите двухходовый фестиваль.",
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            string festivalProgress = player.CultureFestivalTurns > 0 ? (2 - player.CultureFestivalTurns) + "/2" : "не начат";
            UiKit.Label(culture.transform, "Престиж " + player.CulturePrestige + "/100 · объекты " + culturalObjects + "/3 · влияние " + CulturalInfluenceLabel(player) + " · фестиваль " + festivalProgress, 20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            Button cultureButton = UiKit.Button(culture.transform, "ПРОВЕСТИ ФЕСТИВАЛЬ · 20 ВЛИЯНИЯ И 10 КАЗНЫ",
                delegate { Issue(CommandKind.BeginVictoryProject, SelectedCity().Id, value: (int)VictoryPath.Cultural); }, GamePalette.Rust, 62, 18);
            cultureButton.interactable = VictoryEvaluator.CanStartCulturalFestival(state, player.Id) && player.InfluenceTenths >= 200 && player.TreasuryTenths >= 100 && player.CultureFestivalTurns == 0;

            int routes = CountRoutes(player.Id);
            GameObject diplomatic = UiKit.Card(parent, "Международное сотрудничество · активный путь");
            UiKit.Label(diplomatic.transform, "Исследуйте Международную ассамблею; свяжите её город маршрутами с двумя цивилизациями; внесите 20 влияния в фонд. Разрешите кризис вкладом трёх сторон, получите три голоса из четырёх и удержите коалицию два хода.",
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Label(diplomatic.transform, "Маршруты " + routes + "/2 · участники кризиса " + CountBits(state.ResolvedGlobalCrisisContributorMask) + "/3 · поддержавшие стороны " + DiplomaticPartners(player) + "/2 · коалиция " + player.DiplomaticCoalitionTurns + "/2",
                20, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            bool diplomacyReady = VictoryEvaluator.CanStartDiplomaticCoalition(state, player.Id, SelectedCity().Id) && player.InfluenceTenths >= 200;
            Button diplomaticButton = UiKit.Button(diplomatic.transform, "СОЗВАТЬ КОАЛИЦИЮ В АССАМБЛЕЕ", delegate { Issue(CommandKind.BeginVictoryProject, SelectedCity().Id, value: (int)VictoryPath.Diplomatic); }, GamePalette.Ink, 62, 18);
            diplomaticButton.interactable = diplomacyReady && player.DiplomaticCoalitionTurns == 0 && (SelectedCity().Project == null || SelectedCity().Project.Kind == ProjectKind.None);

            GameObject heritage = UiKit.Card(parent, "Наследие эпох · итоговый путь");
            UiKit.Label(heritage.transform, "Если никто не завершил другой путь, на " + state.MaximumTurn + "-м ходу побеждает цивилизация с наибольшим счётом развития. Он учитывает население, качество городов, технологии, культуру, связи, репутацию и цену войны.",
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

        private static string CulturalInfluenceLabel(CivilizationState civ)
        {
            var values = new List<int>();
            for (int i = 0; i < civ.CulturalInfluence.Length; i++) if (i != civ.Id) values.Add(civ.CulturalInfluence[i]);
            values.Sort(delegate(int a, int b) { return b.CompareTo(a); });
            return values.Count < 3 ? "—" : values[0] + "/" + values[1] + "/" + values[2];
        }

        private static int DiplomaticPartners(CivilizationState civ)
        {
            int count = 0;
            for (int i = 0; i < civ.Relations.Length; i++)
                if (i != civ.Id && civ.Relations[i] >= 25 && i < civ.Trusts.Length && civ.Trusts[i] >= 50) count++;
            return count;
        }

        private static int CountBits(int value)
        {
            int count = 0;
            while (value != 0) { count += value & 1; value >>= 1; }
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
