using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.Scripting;

namespace Nasledie.Client
{
    public static class GamePalette
    {
        public static readonly Color Ink = Hex("172725");
        public static readonly Color InkSoft = Hex("263C38");
        public static readonly Color Paper = Hex("F1E9D8");
        public static readonly Color PaperDim = Hex("D8D0C0");
        public static readonly Color Gold = Hex("DAB66A");
        public static readonly Color Sage = Hex("91AA86");
        public static readonly Color Teal = Hex("4A8D84");
        public static readonly Color Rust = Hex("B96F57");
        public static readonly Color Muted = Hex("A5B3A9");
        public static readonly Color Danger = Hex("C96958");
        public static readonly Color Success = Hex("75A77B");

        public static Color Hex(string value)
        {
            Color color;
            if (ColorUtility.TryParseHtmlString("#" + value, out color)) return color;
            return Color.magenta;
        }
    }

    public sealed class HexTileGraphic : Graphic
    {
        public Color EdgeColor = new Color(0.1f, 0.16f, 0.14f, 1f);
        public float EdgeWidth = 2.5f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            Vector2 center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.52f;
            float innerRadius = Mathf.Max(1f, radius - EdgeWidth);
            var outer = new Vector2[6];
            var inner = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                float angle = Mathf.Deg2Rad * (60f * i - 30f);
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                outer[i] = center + direction * radius;
                inner[i] = center + direction * innerRadius;
            }
            for (int i = 0; i < 6; i++)
            {
                int next = (i + 1) % 6;
                AddVertex(vh, outer[i], EdgeColor);
                AddVertex(vh, outer[next], EdgeColor);
                AddVertex(vh, inner[next], EdgeColor);
                AddVertex(vh, inner[i], EdgeColor);
                int start = vh.currentVertCount - 4;
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
            AddVertex(vh, center, color);
            int centerIndex = vh.currentVertCount - 1;
            int[] innerIndices = new int[6];
            for (int i = 0; i < 6; i++)
            {
                AddVertex(vh, inner[i], color);
                innerIndices[i] = vh.currentVertCount - 1;
            }
            for (int i = 0; i < 6; i++) vh.AddTriangle(centerIndex, innerIndices[i], innerIndices[(i + 1) % 6]);
        }

        private static void AddVertex(VertexHelper helper, Vector2 position, Color tint)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = tint;
            helper.AddVert(vertex);
        }
    }

    public static class UiKit
    {
        private static Font runtimeFont;
        public static Font Font
        {
            get
            {
                if (runtimeFont != null) return runtimeFont;
                runtimeFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (runtimeFont == null) runtimeFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return runtimeFont;
            }
        }

        public static RectTransform Rect(GameObject go) { return go.GetComponent<RectTransform>(); }

        public static GameObject CreateObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        public static GameObject Panel(Transform parent, string name, Color color, float height = -1f)
        {
            GameObject go = CreateObject(name, parent);
            Image image = go.AddComponent<Image>();
            image.color = color;
            LayoutElement layout = go.AddComponent<LayoutElement>();
            if (height > 0f) { layout.minHeight = height; layout.preferredHeight = height; }
            layout.flexibleWidth = 1f;
            return go;
        }

        public static Text Label(Transform parent, string value, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, bool bold = false)
        {
            GameObject go = CreateObject("Label", parent);
            Text text = go.AddComponent<Text>();
            text.font = Font;
            text.text = value ?? "";
            int renderSize = Mathf.Max(12, Mathf.RoundToInt(size * 1.45f));
            text.fontSize = renderSize;
            text.color = color;
            text.alignment = align;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.lineSpacing = 1.05f;
            LayoutElement layout = go.AddComponent<LayoutElement>();
            string[] paragraphs = (value ?? "").Split('\n');
            int lineCount = Math.Max(1, paragraphs.Length);
            RectTransform parentRect = parent as RectTransform;
            float estimatedWidth = parentRect != null && parentRect.rect.width > 0f
                ? parentRect.rect.width : Mathf.Max(360f, Screen.width * 0.48f);
            int charactersPerLine = Mathf.Max(18, Mathf.FloorToInt(estimatedWidth / (renderSize * 0.58f)));
            for (int i = 0; i < paragraphs.Length; i++) lineCount += Math.Max(0, Mathf.CeilToInt(paragraphs[i].Length / (float)charactersPerLine) - 1);
            layout.minHeight = renderSize + 12;
            layout.preferredHeight = Mathf.Max(renderSize + 16, lineCount * renderSize * 1.25f + 16);
            layout.flexibleWidth = 1f;
            return text;
        }

        public static Button Button(Transform parent, string label, Action action, Color color, float height = 66f, int fontSize = 25)
        {
            GameObject go = CreateObject("Button_" + label, parent);
            Image image = go.AddComponent<Image>();
            image.color = color;
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.12f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.12f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(color.r, color.g, color.b, 0.4f);
            button.colors = colors;
            button.onClick.AddListener(delegate { if (action != null) action(); });
            LayoutElement layout = go.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleWidth = 1f;
            Text text = Label(go.transform, label, fontSize, GamePalette.Paper, TextAnchor.MiddleCenter, true);
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14, 5);
            textRect.offsetMax = new Vector2(-14, -5);
            LayoutElement textLayout = text.GetComponent<LayoutElement>();
            if (textLayout != null) UnityEngine.Object.Destroy(textLayout);
            return button;
        }

        public static void Vertical(GameObject go, int pad = 14, int spacing = 8)
        {
            VerticalLayoutGroup layout = go.GetComponent<VerticalLayoutGroup>();
            if (layout == null) layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(pad, pad, pad, pad);
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;
        }

        public static void Horizontal(GameObject go, int pad = 8, int spacing = 8)
        {
            HorizontalLayoutGroup layout = go.GetComponent<HorizontalLayoutGroup>();
            if (layout == null) layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(pad, pad, pad, pad);
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleCenter;
        }

        public static GameObject Card(Transform parent, string title, float minHeight = 0f)
        {
            GameObject card = Panel(parent, "Card_" + title, GamePalette.InkSoft);
            Vertical(card, 16, 8);
            if (minHeight > 0)
            {
                LayoutElement element = card.GetComponent<LayoutElement>();
                element.minHeight = minHeight;
                element.preferredHeight = minHeight;
            }
            Label(card.transform, title, 28, GamePalette.Gold, TextAnchor.MiddleLeft, true);
            return card;
        }

        public static GameObject ScrollView(Transform parent, string name)
        {
            GameObject scrollObject = CreateObject(name, parent);
            RectTransform scrollRect = Rect(scrollObject);
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;
            ScrollRect scroll = scrollObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 55f;
            GameObject viewport = CreateObject("Viewport", scrollObject.transform);
            RectTransform viewportRect = Rect(viewport);
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color(0, 0, 0, 0.01f);
            viewport.AddComponent<RectMask2D>();
            GameObject content = CreateObject("Content", viewport.transform);
            RectTransform contentRect = Rect(content);
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0, 0);
            Vertical(content, 12, 12);
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            return content;
        }

        public static Text OverlayText(Transform parent, string text, int size, Color color, TextAnchor alignment)
        {
            GameObject go = CreateObject("OverlayLabel", parent);
            Text label = go.AddComponent<Text>();
            label.font = Font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.fontStyle = FontStyle.Bold;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = false;
            label.raycastTarget = false;
            return label;
        }

        public static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }

    public static class SaveService
    {
        private static string DirectoryPath { get { return Path.Combine(Application.persistentDataPath, "NasledieSaves"); } }
        private static string AutoPath(int slot) { return Path.Combine(DirectoryPath, "autosave_" + slot + ".json"); }
        private static string ManualPath { get { return Path.Combine(DirectoryPath, "campaign.json"); } }

        public static void SaveAuto(GameState state)
        {
            Directory.CreateDirectory(DirectoryPath);
            for (int slot = 2; slot >= 1; slot--)
                if (File.Exists(AutoPath(slot - 1))) File.Copy(AutoPath(slot - 1), AutoPath(slot), true);
            File.WriteAllText(AutoPath(0), JsonUtility.ToJson(state, true));
        }

        public static void SaveManual(GameState state)
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(ManualPath, JsonUtility.ToJson(state, true));
        }

        public static GameState LoadLatest()
        {
            string latestPath = null;
            DateTime latestTime = DateTime.MinValue;
            string[] paths = { ManualPath, AutoPath(0), AutoPath(1), AutoPath(2) };
            for (int i = 0; i < paths.Length; i++)
            {
                if (!File.Exists(paths[i])) continue;
                DateTime time = File.GetLastWriteTimeUtc(paths[i]);
                if (time > latestTime) { latestTime = time; latestPath = paths[i]; }
            }
            return latestPath == null ? null : Read(latestPath);
        }

        public static GameState LoadManual() { return Read(ManualPath); }
        public static GameState LoadAuto(int slot) { return slot < 0 || slot > 2 ? null : Read(AutoPath(slot)); }
        public static bool HasManualSave { get { return File.Exists(ManualPath); } }
        public static bool HasAutoSave(int slot) { return slot >= 0 && slot <= 2 && File.Exists(AutoPath(slot)); }

        public static bool HasSave
        {
            get { return File.Exists(ManualPath) || File.Exists(AutoPath(0)) || File.Exists(AutoPath(1)) || File.Exists(AutoPath(2)); }
        }

        private static GameState Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json)) return null;
                GameState state = JsonUtility.FromJson<GameState>(json);
                if (state == null || state.SaveVersion != 1 || state.Civilizations == null || state.Cities == null || state.Cells == null) return null;
                state.SelectedMapOffset = state.SelectedMapOffset ?? new int[2];
                state.WinningCivilizationIds = state.WinningCivilizationIds ?? Array.Empty<int>();
                state.Units = state.Units ?? Array.Empty<UnitState>();
                state.Treaties = state.Treaties ?? Array.Empty<TreatyState>();
                state.Conflicts = state.Conflicts ?? Array.Empty<ConflictState>();
                state.Events = state.Events ?? Array.Empty<ActiveEventState>();
                state.Reports = state.Reports ?? Array.Empty<TurnReport>();
                state.EventLog = state.EventLog ?? Array.Empty<string>();
                state.Crisis = state.Crisis ?? new CrisisState();
                for (int i = 0; i < state.Cities.Length; i++)
                {
                    CityState city = state.Cities[i];
                    if (city == null) return null;
                    city.Workers = city.Workers ?? new int[6];
                    city.ServiceFunding = city.ServiceFunding ?? new int[3];
                    city.Project = city.Project ?? new CityProject();
                    city.BuildingIds = city.BuildingIds ?? Array.Empty<string>();
                    city.BuildingLevels = city.BuildingLevels ?? Array.Empty<int>();
                    city.CompletedCulturalIds = city.CompletedCulturalIds ?? Array.Empty<string>();
                }
                for (int i = 0; i < state.Civilizations.Length; i++)
                {
                    CivilizationState civ = state.Civilizations[i];
                    if (civ == null) return null;
                    civ.KnownTechnologies = civ.KnownTechnologies ?? Array.Empty<string>();
                    civ.Laws = civ.Laws ?? Array.Empty<string>();
                    civ.TechnologyProgressTenths = civ.TechnologyProgressTenths ?? new int[RulesCatalog.Technologies.Length];
                    civ.Relations = civ.Relations ?? new int[state.Civilizations.Length];
                    civ.Trusts = civ.Trusts ?? new int[state.Civilizations.Length];
                    civ.Threats = civ.Threats ?? new int[state.Civilizations.Length];
                    civ.CulturalInfluence = civ.CulturalInfluence ?? new int[state.Civilizations.Length];
                    civ.TradeDependency = civ.TradeDependency ?? new int[state.Civilizations.Length];
                    if (civ.TechnologyProgressTenths.Length != RulesCatalog.Technologies.Length) Array.Resize(ref civ.TechnologyProgressTenths, RulesCatalog.Technologies.Length);
                }
                for (int i = 0; i < state.Cells.Length; i++) if (state.Cells[i] == null) return null;
                for (int i = 0; i < state.Events.Length; i++)
                {
                    if (state.Events[i] == null) return null;
                    state.Events[i].Choices = state.Events[i].Choices ?? Array.Empty<string>();
                }
                for (int i = 0; i < state.Reports.Length; i++)
                {
                    if (state.Reports[i] == null) return null;
                    state.Reports[i].Entries = state.Reports[i].Entries ?? Array.Empty<string>();
                }
                return state;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Не удалось прочитать сохранение: " + exception.Message);
                return null;
            }
        }
    }
}
