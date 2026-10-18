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
            public int TargetQ;
            public int TargetR;
        }

        private sealed class BattleSidePresentation
        {
            public UnitKind Kind;
            public Era Era;
            public int Loss;
            public string Label;
        }

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
        private string notice = "";
        private Rect lastSafeArea;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private int animationMode = 1; // 0: off, 1: quick, 2: full
        private bool presentationBusy;
        private bool dismissPresentationRequested;
        private int movingUnitId = -1;
        private readonly HashSet<int> movingUnitIds = new HashSet<int>();
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
            soundEffectsEnabled = PlayerPrefs.GetInt("nasledie.sfx.enabled", 1) != 0;
            soundEffectsVolumeStep = Mathf.Clamp(PlayerPrefs.GetInt("nasledie.sfx.volume", 1), 0, SoundEffectsVolumeSteps.Length - 1);
            animationMode = Mathf.Clamp(PlayerPrefs.GetInt("jericho.animation.mode", 1), 0, 2);
            selectedPace = (GamePace)Mathf.Clamp(PlayerPrefs.GetInt("jericho.campaign.pace", (int)GamePace.Standard), 0, 2);
            selectedNationId = PlayerPrefs.GetString("jericho.campaign.nation", "jericho");
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
            UiKit.SetAnchors(pageHost, new Vector2(0, 0.24f), new Vector2(1, 0.88f), Vector2.zero, Vector2.zero);
            pageObject.SetActive(false);

            GameObject navObject = UiKit.Panel(safeRoot, "Navigation", GamePalette.Ink);
            navHost = UiKit.Rect(navObject);
            UiKit.SetAnchors(navHost, new Vector2(0, 0.12f), new Vector2(1, 0.24f), Vector2.zero, Vector2.zero);
            UiKit.Horizontal(navObject, 4, 4);
            AddNavButton(navObject.transform, "МИР", ScreenPage.Overview);
            AddNavButton(navObject.transform, "ГОРОД", ScreenPage.City);
            AddNavButton(navObject.transform, "НАУКА", ScreenPage.Research);
            AddNavButton(navObject.transform, "ДИПЛОМАТИЯ", ScreenPage.Diplomacy);
            AddNavButton(navObject.transform, "ЗАКОНЫ", ScreenPage.Laws);
            AddNavButton(navObject.transform, "СОБЫТИЯ", ScreenPage.Events);
            AddNavButton(navObject.transform, "ПОБЕДА", ScreenPage.Victory);
            AddNavButton(navObject.transform, "ЛЕТОПИСЬ", ScreenPage.Chronicle);

            GameObject actionObject = UiKit.Panel(safeRoot, "TurnActions", GamePalette.InkSoft);
            actionHost = UiKit.Rect(actionObject);
            UiKit.SetAnchors(actionHost, Vector2.zero, new Vector2(1, 0.12f), Vector2.zero, Vector2.zero);
            UiKit.Horizontal(actionObject, 4, 4);
            Button menuButton = UiKit.Button(actionObject.transform, "МЕНЮ", ShowMenu, GamePalette.Ink, 110, 16);
            Button saveButton = UiKit.Button(actionObject.transform, "СОХРАНИТЬ", SaveManual, GamePalette.Ink, 110, 16);
            Button achievementsButton = UiKit.Button(actionObject.transform, "СПРАВКА", ShowEncyclopedia, GamePalette.Ink, 110, 15);
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
            SetUtilityBackground("Backgrounds/Menu_Background");
            CivilizationState menuPlayer = simulation == null ? null : simulation.State.Civilization(simulation.State.HumanCivilizationId);
            SetMusicEra(menuPlayer == null ? Era.Communities : menuPlayer.Era);
            if (pageHost != null) pageHost.gameObject.SetActive(false);
            if (navHost != null) navHost.gameObject.SetActive(false);
            if (actionHost != null) actionHost.gameObject.SetActive(false);
            if (header != null) header.gameObject.SetActive(false);

            GameObject shade = UiKit.CreateObject("MenuTextShade", menuHost);
            Image shadeImage = shade.AddComponent<Image>();
            shadeImage.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.38f);
            shadeImage.raycastTarget = false;
            UiKit.SetAnchors(UiKit.Rect(shade), Vector2.zero, new Vector2(0.56f, 1f), Vector2.zero, Vector2.zero);

            GameObject menu = UiKit.ScrollView(menuHost, "MenuScroll");
            RectTransform menuScroll = menu.transform.parent.parent as RectTransform;
            if (menuScroll != null) UiKit.SetAnchors(menuScroll, Vector2.zero, new Vector2(0.56f, 1f), Vector2.zero, Vector2.zero);
            UiKit.Vertical(menu, 24, 18);
            AddArtwork(menu.transform, "Jericho_Logo", 178);
            UiKit.Label(menu.transform, "JERICHO", 64, GamePalette.Gold, TextAnchor.MiddleCenter, true);
            UiKit.Label(menu.transform, "От первого поселения — к цивилизации, которую выбирают строить.", 26, GamePalette.Paper, TextAnchor.MiddleCenter, false);
            UiKit.Label(menu.transform, "Экономика · люди · культура · договоры · решения с последствиями", 22, GamePalette.Muted, TextAnchor.MiddleCenter, false);
            UiKit.Label(menu.transform, "Профиль · " + AchievementService.SelectedTitle + " · слава " + AchievementService.Renown + " · достижений " + AchievementService.UnlockedCount + "/" + AchievementService.Definitions.Length,
                18, GamePalette.Gold, TextAnchor.MiddleCenter, true);
            AddArtwork(menu.transform, AchievementService.SelectedTitleArtwork, 82);
            NationDefinition selectedNation = NationCatalog.Get(selectedNationId);
            AddArtwork(menu.transform, selectedNation.LeaderArtKey, 154);
            UiKit.Label(menu.transform, selectedNation.LeaderNameRu + " · " + selectedNation.LeaderTitleRu, 25, GamePalette.Gold, TextAnchor.MiddleCenter, true);
            UiKit.Label(menu.transform, selectedNation.LeaderBiographyRu + "\n\n" + selectedNation.LeaderHistoricalNoteRu,
                18, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            AddArtwork(menu.transform, selectedNation.ArtKey, 92);
            UiKit.Button(menu.transform, "НАЦИЯ: " + selectedNation.NameRu + " · ВЫБРАТЬ", CycleNation, GamePalette.InkSoft, 68, 22);
            BuildingDefinition uniqueDistrict = RulesCatalog.Building(selectedNation.UniqueDistrictBuildingId);
            UiKit.Label(menu.transform, selectedNation.IdentityDescription + "\n\nУникальный район: " + uniqueDistrict.NameRu + " — " + BuildingEffectSummary(uniqueDistrict) +
                ". Учебный центр: " + selectedNation.CampusNameRu + " — " + selectedNation.CampusBonusDescriptionRu + "\n" +
                selectedNation.BonusTitle + " · " + selectedNation.BonusDescription + ".", 18,
                GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Button(menu.transform, musicEnabled ? "МУЗЫКА: ВКЛЮЧЕНА" : "МУЗЫКА: ВЫКЛЮЧЕНА", ToggleMusic, GamePalette.InkSoft, 54, 18);
            UiKit.Button(menu.transform, "ДОСТИЖЕНИЯ И НАГРАДЫ", ShowAchievements, GamePalette.InkSoft, 66, 21);
            UiKit.Button(menu.transform, "НАСТРОЙКИ", ShowSettings, GamePalette.InkSoft, 62, 21);
            Button savesButton = UiKit.Button(menu.transform, "УПРАВЛЕНИЕ СОХРАНЕНИЯМИ", ShowSaves, GamePalette.InkSoft, 62, 19);
            savesButton.interactable = SaveService.HasSave;
            UiKit.Button(menu.transform, "ЭНЦИКЛОПЕДИЯ И ПОДСКАЗКИ", ShowEncyclopedia, GamePalette.InkSoft, 62, 21);

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
            UiKit.Button(menu.transform, "ТЕМП: " + PaceName(selectedPace), CyclePace, GamePalette.InkSoft, 70, 22);
            UiKit.Label(menu.transform, PaceDescription(selectedPace), 18, GamePalette.Muted, TextAnchor.MiddleLeft, false);
            UiKit.Button(menu.transform, "КАК ИГРАТЬ", ShowTutorial, GamePalette.InkSoft, 70, 22);
            UiKit.Label(menu.transform, "Демо · 4 древние культуры с персонажами и уникальными районами · 5 путей к победе · офлайн", 20, GamePalette.Muted, TextAnchor.MiddleCenter, false);
        }

        private void InitializeSoundtrack()
        {
            GameObject audioRoot = new GameObject("NasledieAudio");
            audioRoot.transform.SetParent(transform, false);
            if (FindObjectOfType<AudioListener>() == null) audioRoot.AddComponent<AudioListener>();
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
            PrepareUtilityOverlay();
            activeUtilityScreen = "settings";
            GameObject content = UiKit.ScrollView(menuHost, "SettingsScroll");
            UiKit.Label(content.transform, "НАСТРОЙКИ", 44, GamePalette.Gold, TextAnchor.MiddleCenter, true);
            UiKit.Label(content.transform, "Выбор сохраняется на этом устройстве.", 20, GamePalette.Muted, TextAnchor.MiddleCenter, false);
            UiKit.Button(content.transform, musicEnabled ? "МУЗЫКА · ВКЛЮЧЕНА" : "МУЗЫКА · ВЫКЛЮЧЕНА", ToggleMusic, GamePalette.Teal, 68, 22);
            UiKit.Button(content.transform, MusicVolumeLabel() + " · ИЗМЕНИТЬ", CycleMusicVolume, GamePalette.InkSoft, 68, 21);
            UiKit.Button(content.transform, soundEffectsEnabled ? "ЗВУКОВЫЕ ЭФФЕКТЫ · ВКЛЮЧЕНЫ" : "ЗВУКОВЫЕ ЭФФЕКТЫ · ВЫКЛЮЧЕНЫ", ToggleSoundEffects, GamePalette.Teal, 68, 22);
            UiKit.Button(content.transform, SoundEffectsVolumeLabel() + " · ИЗМЕНИТЬ", CycleSoundEffectsVolume, GamePalette.InkSoft, 68, 21);
            UiKit.Button(content.transform, AnimationModeLabel() + " · ИЗМЕНИТЬ", CycleAnimationMode, GamePalette.InkSoft, 68, 21);
            UiKit.Button(content.transform, "ТЕМП НОВОЙ КАМПАНИИ: " + PaceName(selectedPace) + " · ИЗМЕНИТЬ", CyclePace, GamePalette.InkSoft, 68, 21);
            UiKit.Label(content.transform, PaceDescription(selectedPace) +
                (simulation == null ? "" : "\nТекущая кампания: " + PaceName(simulation.State.Pace) + " · её темп не меняется."),
                19, GamePalette.Paper, TextAnchor.MiddleLeft, false);
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
                PaceName(state.Pace) + " · " + (player == null ? "цивилизация не найдена" : player.Name) + " · мир " + state.Seed +
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
            CivilizationState savedPlayer = saved.Civilization(saved.HumanCivilizationId);
            if (savedPlayer != null) SetMusicEra(savedPlayer.Era);
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
            SetUtilityBackground(null);
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
                "Art/WorldAtlas", "Art/UnitAtlas", "Art/UnitEraAtlas", "Art/AchievementAtlas",
                "Art/Animations/WorkerActivities_Rural", "Art/Animations/WorkerActivities_Civic"
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
                "hospital", "administration", "market", "workshop", "barracks", "school", "council",
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
            ShowEncyclopedia("first-turn");
        }

        private void ShowEncyclopedia() { ShowEncyclopedia(null); }

        private void ShowEncyclopedia(string articleId)
        {
            PrepareUtilityOverlay();
            activeUtilityScreen = "encyclopedia";
            GameObject content = UiKit.ScrollView(menuHost, "EncyclopediaScroll");
            UiKit.Vertical(content, 22, 12);
            GuideArticle article = GameGuide.Find(articleId);
            if (article != null)
            {
                UiKit.Label(content.transform, "ЭНЦИКЛОПЕДИЯ · " + article.Category.ToUpperInvariant(), 19, GamePalette.Teal, TextAnchor.MiddleCenter, true);
                UiKit.Label(content.transform, article.Title, 40, GamePalette.Gold, TextAnchor.MiddleCenter, true);
                UiKit.Label(content.transform, article.Summary, 24, GamePalette.Sage, TextAnchor.MiddleLeft, true);
                UiKit.Label(content.transform, article.Body, 22, GamePalette.Paper, TextAnchor.UpperLeft, false);
                UiKit.Button(content.transform, "НАЗАД К СПРАВОЧНИКУ", ShowEncyclopedia, GamePalette.InkSoft, 68, 21);
                UiKit.Button(content.transform, "ЗАКРЫТЬ СПРАВКУ", CloseEncyclopedia, GamePalette.Teal, 68, 21);
                return;
            }

            UiKit.Label(content.transform, "ЭНЦИКЛОПЕДИЯ И ПОДСКАЗКИ", 40, GamePalette.Gold, TextAnchor.MiddleCenter, true);
            UiKit.Label(content.transform, "Найдите правило, причину события или совет. Подсказка на каждой игровой вкладке объясняет текущую ситуацию и ведёт к подходящей статье.",
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
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
            UiKit.Button(content.transform, "ЗАКРЫТЬ СПРАВКУ", CloseEncyclopedia, GamePalette.Teal, 68, 21);
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
            if (simulation != null)
            {
                menuHost.gameObject.SetActive(false);
                RenderGame();
            }
            else ShowMenu();
        }

        private void CycleDifficulty()
        {
            selectedDifficulty = (Difficulty)(((int)selectedDifficulty + 1) % 4);
            ShowMenu();
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
            NationDefinition selectedNation = NationCatalog.Get(selectedNationId);
            BuildingDefinition uniqueDistrict = RulesCatalog.Building(selectedNation.UniqueDistrictBuildingId);
            AddArtwork(content.transform, selectedNation.LeaderArtKey, 160);
            UiKit.Label(content.transform, selectedNation.LeaderNameRu + " · " + selectedNation.LeaderTitleRu, 26, GamePalette.Gold, TextAnchor.MiddleCenter, true);
            UiKit.Label(content.transform, selectedNation.LeaderHistoricalNoteRu + "\n\nУникальный район: " +
                uniqueDistrict.NameRu + " — " + BuildingEffectSummary(uniqueDistrict) + ". Учебный центр: " + selectedNation.CampusNameRu +
                " — " + selectedNation.CampusBonusDescriptionRu,
                20, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            AddArtwork(content.transform, selectedNation.ArtKey, 104);
            UiKit.Button(content.transform, "НАЦИЯ: " + selectedNation.NameRu + " · ИЗМЕНИТЬ", CycleNation, GamePalette.InkSoft, 66, 21);
            UiKit.Label(content.transform, selectedNation.BonusTitle + " · " + selectedNation.BonusDescription + ".\n\n" +
                "Начать новую партию на сложности «" + DifficultyName(selectedDifficulty) + "»?\n\n" +
                DifficultyDescription(selectedDifficulty) + "\n\nТемп: «" + PaceName(selectedPace) + "». " + PaceDescription(selectedPace) + "\n\n" +
                "Текущие автосохранения останутся доступны в управлении слотами, но последующие ходы новой партии начнут вытеснять их по очереди. Ручное сохранение хранится отдельно.",
                23, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Button(content.transform, "НАЧАТЬ НОВУЮ КАМПАНИЮ", CreateNewCampaign, GamePalette.Teal, 78, 22);
            UiKit.Button(content.transform, "ОТКРЫТЬ СОХРАНЕНИЯ", ShowSaves, GamePalette.InkSoft, 66, 21);
            UiKit.Button(content.transform, "ОТМЕНА", ShowMenu, GamePalette.Rust, 66, 21);
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
            GameObject content = UiKit.ScrollView(pageHost, "PageScroll");
            RenderContextHint(content.transform);
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

        private void RenderContextHint(Transform parent)
        {
            GameState state = simulation.State;
            CivilizationState player = state.Civilization(state.HumanCivilizationId);
            CityState city = SelectedCity();
            string title = "Порядок хода";
            string message = "Сравните прогноз с состоянием города, назначьте приоритет и завершите ход. Летопись покажет последствия решений.";
            string articleId = "turn-cycle";

            if (currentPage == ScreenPage.Overview)
            {
                if (state.Crisis != null && state.Crisis.Kind != CrisisKind.None && !state.Crisis.Resolved)
                {
                    title = "Мировой кризис";
                    message = state.Crisis.PublicText + " Угроза сейчас: " + state.Crisis.Threat + ". Общий вклад сторон снижает её и повышает репутацию участников.";
                    articleId = "global-crisis";
                }
                else if (city != null && city.FoodTenths < city.PopulationTenths)
                {
                    title = "Запас еды ниже потребления";
                    message = city.Name + " расходует еду быстрее, чем пополняет запас. Если нехватка затянется, здоровье и население начнут снижаться.";
                    articleId = "food-growth";
                }
                else if (city != null && city.Trust < 40)
                {
                    title = "Доверие жителей падает";
                    message = "Проверьте налоги, питание и последствия войны: низкое доверие ухудшает устойчивость города и может ускорить отток населения.";
                    articleId = "cities-services";
                }
            }
            else if (currentPage == ScreenPage.City && city != null)
            {
                CityYield forecast = simulation.PreviewCity(city.Id);
                if (forecast.FoodTenths < city.PopulationTenths)
                {
                    title = "Городу не хватает еды";
                    message = "Прогноз еды составляет " + Format(forecast.FoodTenths) + ", а потребление — " + Format(city.PopulationTenths) + ". Переведите часть работников на земледелие или улучшите подходящую клетку.";
                    articleId = "food-growth";
                }
                else if (city.Health < 50 || city.Safety < 50 || city.Trust < 40)
                {
                    title = "Службы требуют внимания";
                    message = "Здоровье, безопасность и доверие поддерживают благополучие. Проверьте финансирование, здания и последние записи о событиях в летописи.";
                    articleId = "cities-services";
                }
                else if (city.Project == null || city.Project.Kind == ProjectKind.None)
                {
                    title = "Город ждёт проект";
                    message = "Выберите здание, улучшение или другой проект. Производство ремесленников определяет, как быстро он будет завершён.";
                    articleId = "projects";
                }
                else
                {
                    title = "Проверьте выпуск и потребности";
                    message = "Проект продвигается производством города. Работников можно переназначать, если еда, казна или услуги важнее скорости строительства.";
                    articleId = "workers";
                }
            }
            else if (currentPage == ScreenPage.Research)
            {
                TechnologyDefinition active = player.ActiveResearchIndex >= 0 && player.ActiveResearchIndex < RulesCatalog.Technologies.Length
                    ? RulesCatalog.Technologies[player.ActiveResearchIndex] : null;
                if (active == null)
                {
                    title = "Исследование ещё не выбрано";
                    message = "Выберите доступную технологию. Её требования и открытия показаны прямо в карточке, а накопленные знания сохраняются при смене цели.";
                    articleId = "research";
                }
                else if (CountTechnologiesInEra(player, player.Era) >= 3 && CountCitiesWithBuilding(player.Id, "council") == 0)
                {
                    title = "Можно готовить переход эпохи";
                    message = "Три технологии текущей эпохи уже изучены. Для общественного перехода ещё нужны Совет, влияние и городской проект.";
                    articleId = "eras";
                }
                else
                {
                    title = "Открытия зависят от выбранного курса";
                    message = "Технология «" + active.NameRu + "» получит знания в конце хода. Выбирайте открытия, которые поддерживают ваши города и предполагаемый путь победы.";
                    articleId = "research";
                }
            }
            else if (currentPage == ScreenPage.Diplomacy)
            {
                if (player.WarStrain >= 35)
                {
                    title = "Цивилизация истощена конфликтом";
                    message = "Военное напряжение ухудшает отношения и экономику. Мир, восстановление маршрутов и снижение военных расходов помогут восстановиться.";
                    articleId = "war-cost";
                }
                else
                {
                    title = "Договоры создают устойчивые связи";
                    message = "Торговые маршруты дают доход и нужны для некоторых совместных целей. Следите за доверием и сроком соглашений с соседями.";
                    articleId = "diplomacy";
                }
            }
            else if (currentPage == ScreenPage.Laws)
            {
                title = "У реформ есть постоянные расходы";
                message = "Перед принятием сравните пользу закона с его содержанием на каждый город. Следующая реформа может быть недоступна до окончания периода ожидания.";
                articleId = "laws";
            }
            else if (currentPage == ScreenPage.Events)
            {
                ActiveEventState pending = null;
                for (int i = 0; i < state.Events.Length; i++)
                    if (!state.Events[i].IsResolved && state.Events[i].TargetCivilizationId == player.Id) { pending = state.Events[i]; break; }
                if (pending != null)
                {
                    EventDefinition definition = EventDefinitionFor(pending.Kind);
                    title = "Причина текущего события";
                    message = definition == null ? "Выберите ответ после проверки доступных ресурсов и описания последствий." : definition.NameRu + ": " + definition.TriggerRu + " Решение может расходовать ресурсы или менять показатели города.";
                }
                else if (state.Crisis != null && state.Crisis.Kind != CrisisKind.None && !state.Crisis.Resolved)
                {
                    title = "Мировой кризис ещё действует";
                    message = state.Crisis.PublicText + " Помощь едой, знаниями или влиянием уменьшает общую угрозу.";
                    articleId = "global-crisis";
                }
                else
                {
                    title = "Событий, требующих ответа, нет";
                    message = "Следите за причинами, указанными в летописи: плохое питание, здоровье, мобилизация и торговля влияют на будущие события.";
                }
                if (articleId != "global-crisis") articleId = "events";
            }
            else if (currentPage == ScreenPage.Victory)
            {
                title = "Сравните несколько путей развития";
                message = "На этой вкладке видны ваши условия и очки соперников. Если сложный активный путь пока далеко, укрепляйте наследие через города, население, науку, культуру и мирные связи.";
                articleId = "victory-overview";
            }
            else if (currentPage == ScreenPage.Chronicle)
            {
                title = "Ищите причину рядом с изменением";
                message = "Сопоставьте сообщения одного хода с прогнозом города и ресурсами. Например, дефицит еды ведёт к снижению здоровья, а осада — к падению выпуска.";
                articleId = "chronicle";
            }

            GameObject card = UiKit.Card(parent, "ПОДСКАЗКА · " + title);
            UiKit.Label(card.transform, message, 19, GamePalette.Paper, TextAnchor.MiddleLeft, false);
            UiKit.Button(card.transform, "ПОЧЕМУ ТАК ПРОИСХОДИТ · ЧИТАТЬ", delegate { ShowEncyclopedia(articleId); }, GamePalette.Ink, 52, 17);
        }

        private void RenderOverview(Transform parent)
        {
            GameState state = simulation.State;
            CivilizationState player = state.Civilization(state.HumanCivilizationId);
            NationDefinition nation = player == null ? null : NationCatalog.Get(player.NationId);
            UiKit.Label(parent, "Карта государства", 32, GamePalette.Gold, TextAnchor.MiddleLeft, true);
            if (nation != null)
            {
                GameObject leaderCard = UiKit.CreateObject("PlayerLeader", parent);
                UiKit.Horizontal(leaderCard, 4, 10);
                AddCityBuildingArtwork(leaderCard.transform, nation.LeaderArtKey, 58);
                Text leaderLabel = UiKit.Label(leaderCard.transform,
                    nation.LeaderNameRu + " · " + nation.LeaderTitleRu + "\n" + nation.NameRu + " · " + nation.BonusTitle + " · " + nation.BonusDescription,
                    20, GamePalette.Paper, TextAnchor.MiddleLeft, true);
                leaderLabel.GetComponent<LayoutElement>().flexibleWidth = 1f;
            }
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
                NationDefinition rivalNation = NationCatalog.Get(civ.NationId);
                GameObject rivalRow = UiKit.CreateObject("RivalLeader_" + civ.Id, civCard.transform);
                UiKit.Horizontal(rivalRow, 4, 10);
                if (rivalNation != null) AddCityBuildingArtwork(rivalRow.transform, rivalNation.LeaderArtKey, 58);
                Text rivalLabel = UiKit.Label(rivalRow.transform,
                    (rivalNation == null ? civ.Name : rivalNation.LeaderNameRu + " · " + rivalNation.LeaderTitleRu + " · " + civ.Name) +
                    (rivalNation == null ? "" : " · " + rivalNation.BonusDescription) + " · курс: " + AiGoalName(civ.StrategicGoal) +
                    " · отношение " + civ.Relations[playerId(state)] + " · городов " + CountCities(state, civ.Id),
                    20, GamePalette.PaperDim, TextAnchor.MiddleLeft, false);
                rivalLabel.GetComponent<LayoutElement>().flexibleWidth = 1f;
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
            UiKit.Label(card.transform, "⌂ центр города · отдельные клетки — районы · ● войско · ◇ обоз · F ферма · T лес · = дорога. Цвет границ показывает владельца, тёмные клетки скрыты туманом.",
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
            CityState districtCity = string.IsNullOrEmpty(cell.DistrictBuildingId) ? null : simulation.State.City(cell.DistrictCityId);
            CityState districtProjectCity = DistrictProjectCityAt(cell.Q, cell.R);
            CityState improvementProjectCity = ImprovementProjectCityAt(cell.Q, cell.R);
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
                if (unit != null && IsUnitMoving(unit.Id)) unit = null;
                if (unit != null) marker = unit.Kind == UnitKind.SettlerWagon ? "◇" : "●";
                else if (districtCity != null) marker = "▥";
                else if (districtProjectCity != null) marker = "+";
                else if (improvementProjectCity != null) marker = "+";
                else if (cell.Improvement != ImprovementKind.None && cell.Damage < 75) marker = ImprovementMark(cell.Improvement);
                else if (cell.Resource != ResourceKind.None) marker = ResourceMark(cell.Resource);
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
                    Image mapArt = AddMapArtwork(tile.transform, artwork, city != null ? new Vector2(46, 42) : districtCity != null || districtProjectCity != null || improvementProjectCity != null ? new Vector2(31, 28) : new Vector2(27, 25),
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
            selectedQ = q;
            selectedR = r;
            CityState city = CityAt(q, r);
            HexCell selectedCell = simulation.State.Cell(q, r);
            bool selectedDistrict = city == null && selectedCell != null && !string.IsNullOrEmpty(selectedCell.DistrictBuildingId);
            CityState districtProjectCity = city == null ? DistrictProjectCityAt(q, r) : null;
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
                BattleResolutionEvent[] battles = simulation.LastResolvedBattles;
                bool resolvedBattle = movement.Accepted && battles != null && battles.Length > 0;
                if (resolvedBattle) PlaySoundEffect("Battle_Resolve");
                if (resolvedBattle && animationMode > 0 && currentPage == ScreenPage.Overview)
                {
                    selectedUnitId = -1;
                    RenderGame();
                    StartCoroutine(AnimateCommandBattle(battles[0]));
                    return;
                }
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
            GameObject card = UiKit.Card(parent, "Работники · " + city.WorkerCount + " доступно");
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
                    (string.IsNullOrEmpty(building.RequiredNationId) ? "" : " · " + BuildingEffectSummary(building)) +
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
            if (result.Accepted) PlayCommandSound(kind, value);
            RenderGame();
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

        private void SetTax(TaxRate rate) { Issue(CommandKind.SetTax, value: (int)rate); }

        private void SaveManual()
        {
            if (simulation == null) return;
            notice = SaveService.SaveManual(simulation.State)
                ? "Кампания сохранена на этом устройстве."
                : "Не удалось сохранить кампанию. Освободите место и повторите попытку.";
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
                int shown = 0;
                for (int i = 0; i < turnBattles.Length && shown < 3; i++)
                {
                    if (turnBattles[i] == null || turnBattles[i].HumanVisibilityMask == 0) continue;
                    PlaySoundEffect("Battle_Resolve");
                    yield return ShowBattlePresentation(layer, turnBattles[i]);
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
                result[unit.Id] = new UnitMovePresentation
                {
                    UnitId = unit.Id, OwnerId = unit.OwnerId, Kind = unit.Kind,
                    StartQ = unit.Q, StartR = unit.R
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

        private void ShowTurnMilestones(Transform layer, int completedTurn, Era oldEra, Era newEra,
            List<BuildingMilestone> buildings, List<ImprovementMilestone> improvements)
        {
            if (oldEra != newEra) PlaySoundEffect("Era_Transition");
            else if (buildings.Count > 0 || improvements.Count > 0) PlaySoundEffect("Build_Complete");
            DestroyChildren(layer);
            Image blocker = layer.GetComponent<Image>();
            if (blocker != null) blocker.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.86f);
            Button backdrop = layer.GetComponent<Button>();
            if (backdrop == null) backdrop = layer.gameObject.AddComponent<Button>();
            backdrop.targetGraphic = blocker;
            backdrop.transition = Selectable.Transition.None;
            backdrop.onClick.RemoveAllListeners();
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
                int count = Math.Min(buildings.Count, 3);
                for (int i = 0; i < count; i++)
                {
                    GameObject row = UiKit.CreateObject("CompletedBuilding_" + i, card.transform);
                    UiKit.Horizontal(row, 8, 12);
                    BuildingMilestone milestone = buildings[i];
                    AddArtwork(row.transform, BuildingArtwork(milestone.Definition.Id, newEra), 98);
                    UiKit.Label(row.transform, milestone.Definition.NameRu + " · " + milestone.CityName, 23, GamePalette.Paper, TextAnchor.MiddleLeft, true);
                    CanvasGroup reveal = row.AddComponent<CanvasGroup>();
                    reveal.alpha = 0f;
                    StartCoroutine(ScaleIn(UiKit.Rect(row), reveal, (0.24f + i * 0.08f) * AnimationScale));
                }
                if (buildings.Count > count) UiKit.Label(card.transform, "И ещё завершено зданий: " + (buildings.Count - count), 18, GamePalette.Muted, TextAnchor.MiddleCenter, false);
            }
            if (improvements.Count > 0)
            {
                UiKit.Label(card.transform, "Завершены работы на клетках", 24, GamePalette.Paper, TextAnchor.MiddleCenter, true);
                int count = Math.Min(improvements.Count, 3);
                for (int i = 0; i < count; i++)
                {
                    ImprovementMilestone milestone = improvements[i];
                    GameObject row = UiKit.CreateObject("CompletedImprovement_" + i, card.transform);
                    UiKit.Horizontal(row, 8, 12);
                    AddArtwork(row.transform, ImprovementArtwork(milestone.Kind, newEra), 66);
                    string name = milestone.Kind.ToString();
                    for (int d = 0; d < RulesCatalog.Improvements.Length; d++)
                        if (RulesCatalog.Improvements[d].Kind == milestone.Kind) name = RulesCatalog.Improvements[d].NameRu;
                    UiKit.Label(row.transform, name + " · клетка " + milestone.Q + ", " + milestone.R, 21, GamePalette.Paper, TextAnchor.MiddleLeft, true);
                    CanvasGroup reveal = row.AddComponent<CanvasGroup>();
                    reveal.alpha = 0f;
                    StartCoroutine(ScaleIn(UiKit.Rect(row), reveal, (0.2f + i * 0.06f) * AnimationScale));
                }
                if (improvements.Count > count) UiKit.Label(card.transform, "И ещё завершено работ: " + (improvements.Count - count), 18, GamePalette.Muted, TextAnchor.MiddleCenter, false);
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
            if (blocker != null) blocker.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.72f);
            RenderGame();
        }

        private IEnumerator AnimateCommandBattle(BattleResolutionEvent battle)
        {
            presentationBusy = true;
            GameObject layer = CreatePresentationLayer();
            yield return ShowBattlePresentation(layer, battle);
            if (presentationLayer != null) Destroy(presentationLayer);
            presentationLayer = null;
            presentationBusy = false;
            RenderGame();
        }

        private IEnumerator ShowBattlePresentation(GameObject layer, BattleResolutionEvent battle)
        {
            if (layer == null || battle == null) yield break;
            DestroyChildren(layer.transform);
            Image blocker = layer.GetComponent<Image>();
            if (blocker != null) blocker.color = new Color(GamePalette.Ink.r, GamePalette.Ink.g, GamePalette.Ink.b, 0.9f);
            Button backdrop = layer.GetComponent<Button>();
            if (backdrop == null) backdrop = layer.AddComponent<Button>();
            backdrop.targetGraphic = blocker;
            backdrop.transition = Selectable.Transition.None;
            backdrop.onClick.RemoveAllListeners();
            dismissPresentationRequested = false;
            backdrop.onClick.AddListener(delegate { dismissPresentationRequested = true; });

            GameObject card = UiKit.Panel(layer.transform, "BattleResolution", GamePalette.InkSoft);
            RectTransform cardRect = UiKit.Rect(card);
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(850, 480);
            UiKit.Vertical(card, 24, 12);
            CanvasGroup group = card.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            UiKit.Label(card.transform, "СРАЖЕНИЕ · ХОД " + simulation.State.Turn, 31, GamePalette.Gold, TextAnchor.MiddleCenter, true);
            UiKit.Label(card.transform, "Отряды встретились на клетке " + battle.CellQ + ", " + battle.CellR + ".", 21, GamePalette.Muted, TextAnchor.MiddleCenter, false);

            GameObject sides = UiKit.CreateObject("BattleSides", card.transform);
            UiKit.Horizontal(sides, 12, 24);
            BuildBattleSide(sides.transform, battle.AttackerKind, battle.AttackerOwnerId, battle.AttackerLoss, true);
            UiKit.Label(sides.transform, "×", 43, GamePalette.Rust, TextAnchor.MiddleCenter, true);
            BuildBattleSide(sides.transform, battle.DefenderKind, battle.DefenderOwnerId, battle.DefenderLoss, false);
            UiKit.Label(card.transform, battle.AttackerWon ? "Атака удержала поле боя" : "Защитники отбили атаку", 26,
                battle.AttackerWon ? GamePalette.Success : GamePalette.Rust, TextAnchor.MiddleCenter, true);
            Text continueText = UiKit.Label(card.transform, "Коснитесь экрана, чтобы продолжить", 18, GamePalette.Muted, TextAnchor.MiddleCenter, false);
            continueText.raycastTarget = false;
            Button cardDismiss = card.AddComponent<Button>();
            cardDismiss.targetGraphic = card.GetComponent<Image>();
            cardDismiss.transition = Selectable.Transition.None;
            cardDismiss.onClick.AddListener(delegate { dismissPresentationRequested = true; });
            StartCoroutine(ScaleIn(cardRect, group, 0.2f * AnimationScale));

            float remaining = animationMode == 2 ? 1.8f : 1.0f;
            while (remaining > 0f && !dismissPresentationRequested)
            {
                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void BuildBattleSide(Transform parent, UnitKind kind, int ownerId, int loss, bool attacker)
        {
            GameObject side = UiKit.CreateObject(attacker ? "BattleAttacker" : "BattleDefender", parent);
            UiKit.Vertical(side, 4, 4);
            LayoutElement sideLayout = side.AddComponent<LayoutElement>();
            sideLayout.minWidth = 260;
            sideLayout.preferredWidth = 300;
            sideLayout.flexibleWidth = 1;
            string ownerName = ownerId == simulation.State.HumanCivilizationId ? "Ваш отряд" : simulation.State.Civilization(ownerId).Name;
            UiKit.Label(side.transform, ownerName + " · " + RulesCatalog.Unit(kind).NameRu, 21, GamePalette.Paper, TextAnchor.MiddleCenter, true);
            GameObject figure = UiKit.CreateObject("BattleUnitArt", side.transform);
            Image image = figure.AddComponent<Image>();
            image.sprite = LoadArtwork(UnitArtwork(kind, CivilizationEra(ownerId)));
            image.preserveAspect = true;
            image.raycastTarget = false;
            LayoutElement figureLayout = figure.AddComponent<LayoutElement>();
            figureLayout.minHeight = 150;
            figureLayout.preferredHeight = 150;
            figureLayout.flexibleWidth = 1;
            UiKit.Label(side.transform, "Потери силы: " + loss, 19, loss > 0 ? GamePalette.Rust : GamePalette.Muted, TextAnchor.MiddleCenter, false);
            CanvasGroup reveal = side.AddComponent<CanvasGroup>();
            reveal.alpha = 1f;
            RectTransform sideRect = UiKit.Rect(side);
            StartCoroutine(ScaleIn(sideRect, reveal, 0.28f * AnimationScale));
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
            int councils = CountCitiesWithBuilding(simulation.State, owner.Id, "council");
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
                if (breaksPact)
                    UiKit.Label(war.transform, "Нарушение действующего пакта дополнительно снизит вашу репутацию на 20 и ухудшит отношения с соседом.",
                        18, GamePalette.Rust, TextAnchor.MiddleLeft, true);
                string declarationLabel = breaksPact
                    ? "НАРУШИТЬ ПАКТ И НАЧАТЬ МОБИЛИЗАЦИЮ ПРОТИВ " + target.Name.ToUpperInvariant() + " · 8 КАЗНЫ"
                    : "КРИЗИС МОБИЛИЗАЦИИ ПРОТИВ " + target.Name.ToUpperInvariant() + " · 8 КАЗНЫ";
                Button declare = UiKit.Button(war.transform, declarationLabel,
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
                default: return choice;
            }
        }

        private static bool ChoiceAvailable(GameState state, ActiveEventState active, string choice, CivilizationState player, CityState city)
        {
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
