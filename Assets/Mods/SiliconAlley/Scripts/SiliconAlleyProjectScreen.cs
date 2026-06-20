using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.Items;
using Entities;
using Helpers;
using Localizor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[assembly: RegisterModClass(typeof(SiliconAlleyProjectScreenMod))]

// Issue #9: a real custom UI window (the spike's "approach A") for the project's Design phase. Built
// ENTIRELY in code (no bundled .prefab) on a mod-owned Canvas, so it doesn't depend on the game's UI
// internals — it only reuses the scene EventSystem. Opened with a hotkey (F9) or by clicking the
// "Design" notification the simulator fires. Reads/writes the live SiliconAlleyState the simulator
// maintains and persists. NOTE: visuals are intentionally plain (code-built); this is the unsupported
// custom-panel path and may need in-engine layout tweaks (see docs/CAPABILITIES.md).
[ModEntryOnCityLoad]
public class SiliconAlleyProjectScreenMod : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    private GameObject _host;

    public Task OnLoadAsync(ModContext context)
    {
        if (SiliconAlleyProjectScreen.Instance == null)
        {
            _host = new GameObject("SiliconAlleyProjectScreen");
            _host.AddComponent<SiliconAlleyProjectScreen>();
        }
        context.Logger.Info("SiliconAlley: project screen ready (open with F9).");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        if (_host != null)
            UnityEngine.Object.Destroy(_host);
        _host = null;
        return Task.CompletedTask;
    }
}

public class SiliconAlleyProjectScreen : MonoBehaviour
{
    public static SiliconAlleyProjectScreen Instance { get; private set; }

    // The hotkey that toggles the screen. Legacy Input works here (ProjectSettings activeInputHandler=2);
    // rebinding is future work.
    private const KeyCode ToggleKey = KeyCode.F9;

    private static readonly int[] ScopeKinds =
    {
        (int)SiliconAlleyState.ProjectKind.Quick,
        (int)SiliconAlleyState.ProjectKind.Standard,
        (int)SiliconAlleyState.ProjectKind.Ambitious,
    };

    private Font _font;
    private GameObject _root;
    private bool _built;
    private bool _visible;
    private bool _suppress;     // ignore control callbacks while we set values programmatically
    private float _refresh;     // seconds until the next live refresh

    private readonly List<string> _studioKeys = new List<string>();
    private string _currentKey;

    // Control references rebuilt once in Build().
    private Text _titleText, _studioText, _phaseText, _designQualityText, _leadText, _etaText, _statusText;
    private readonly Image[] _scopeImages = new Image[3];
    private readonly Button[] _scopeButtons = new Button[3];
    private Slider _focusSlider;
    private Button _lockButton;

    private static readonly Color PanelColor = new Color(0.10f, 0.11f, 0.14f, 0.98f);
    private static readonly Color ButtonColor = new Color(0.20f, 0.22f, 0.28f, 1f);
    private static readonly Color ButtonSelected = new Color(0.30f, 0.55f, 0.85f, 1f);
    private static readonly Color TextColor = new Color(0.92f, 0.92f, 0.95f, 1f);

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (_root != null)
            Destroy(_root);
        if (Instance == this)
            Instance = null;
    }

    // Clicking the "Design" notification routes here. No-op if the screen hasn't initialized.
    public static void Open(string key)
    {
        if (Instance != null)
            Instance.OpenFor(key);
    }

    private void Update()
    {
        if (Input.GetKeyDown(ToggleKey))
            Toggle();

        if (_visible)
        {
            _refresh -= Time.unscaledDeltaTime;
            if (_refresh <= 0f)
            {
                _refresh = 1f;
                Refresh();
            }
        }
    }

    private void Toggle()
    {
        if (_visible)
            Close();
        else
            OpenFor(null);
    }

    // Open the screen, optionally focused on a specific studio (else keep/!default to the first owned).
    private void OpenFor(string key)
    {
        EnsureBuilt();
        PopulateStudios();
        if (!string.IsNullOrEmpty(key) && _studioKeys.Contains(key))
            _currentKey = key;
        else if (string.IsNullOrEmpty(_currentKey) || !_studioKeys.Contains(_currentKey))
            _currentKey = _studioKeys.Count > 0 ? _studioKeys[0] : null;

        _root.SetActive(true);
        _visible = true;
        _refresh = 1f;
        Refresh();
    }

    private void Close()
    {
        _visible = false;
        if (_root != null)
            _root.SetActive(false);
    }

    // ---- data --------------------------------------------------------------------------------------

    private void PopulateStudios()
    {
        _studioKeys.Clear();
        var current = SaveGameManager.Current;
        if (current?.BuildingRegistrations == null)
            return;
        foreach (var reg in current.BuildingRegistrations)
            if (SiliconAlleyClient.IsPlayerOwned(reg))
                _studioKeys.Add(SiliconAlleyState.KeyFor(reg));
    }

    private BuildingRegistration FindRegistration(string key)
    {
        var current = SaveGameManager.Current;
        if (current?.BuildingRegistrations == null || string.IsNullOrEmpty(key))
            return null;
        foreach (var reg in current.BuildingRegistrations)
            if (SiliconAlleyClient.IsPlayerOwned(reg) && SiliconAlleyState.KeyFor(reg) == key)
                return reg;
        return null;
    }

    private void Refresh()
    {
        _titleText.text = "siliconalley:screen_title".GetLocalization();

        var reg = FindRegistration(_currentKey);
        if (reg == null)
        {
            _studioText.text = "siliconalley:screen_nostudio".GetLocalization();
            _phaseText.text = _designQualityText.text = _leadText.text = _etaText.text = "";
            _statusText.text = "";
            SetControlsInteractable(false);
            for (var i = 0; i < 3; i++)
                _scopeImages[i].color = ButtonColor;
            return;
        }

        var businessType = BusinessTypeHelper.GetData(reg);
        var key = _currentKey;
        var size = SiliconAlleyState.EffectiveProjectSize(key);
        var rawProgress = SiliconAlleyState.GetProgress(key);
        var phase = SiliconAlleyState.PhaseOf(rawProgress, size);
        var perHour = SiliconAlleyOfficeSimulator.CurrentHourlyProgress(reg);
        var editable = SiliconAlleyState.CanEditConcept(key);

        var product = ProductName(businessType);
        _studioText.text = Compose("siliconalley:screen_studio", ("business", reg.GetDisplayName()), ("product", product));
        _phaseText.text = Compose("siliconalley:screen_phase",
            ("phase", SiliconAlleyState.PhaseNameKey(phase).GetLocalization()),
            ("progress", Pct(SiliconAlleyState.PhaseProgressFraction(rawProgress, size))));

        var designQ = SiliconAlleyState.GetPhaseQuality(key, SiliconAlleyState.ProjectPhase.Design);
        _designQualityText.text = Compose("siliconalley:screen_designquality",
            ("value", designQ < 0f ? "—" : Pct(designQ) + "%"));

        var hasDesigner = businessType != null && businessType.employeePrimarySkills.Contains("ba:skill_graphicdesigner");
        var leadKey = hasDesigner ? "siliconalley:screen_lead_designer" : "siliconalley:screen_lead_programmer";
        _leadText.text = Compose("siliconalley:screen_lead",
            ("lead", leadKey.GetLocalization()), ("staff", CountStaff(reg).ToString(CultureInfo.InvariantCulture)));

        var remaining = SiliconAlleyState.PhaseEndProgress(SiliconAlleyState.ProjectPhase.Design, size) - rawProgress;
        _etaText.text = Compose("siliconalley:screen_designeta", ("eta", EtaText(remaining, perHour)));

        _statusText.text = (editable ? "siliconalley:screen_editable" : "siliconalley:screen_locked").GetLocalization();

        // Scope highlight reflects the locked/selected kind.
        var currentKind = SiliconAlleyState.GetProjectType(key);
        for (var i = 0; i < 3; i++)
            _scopeImages[i].color = ScopeKinds[i] == currentKind ? ButtonSelected : ButtonColor;

        // Focus slider (suppress the callback so setting the value doesn't write back).
        _suppress = true;
        _focusSlider.value = SiliconAlleyState.GetDesignFocus(key);
        _suppress = false;

        SetControlsInteractable(editable);
    }

    private void SetControlsInteractable(bool editable)
    {
        for (var i = 0; i < 3; i++)
            _scopeButtons[i].interactable = editable;
        _focusSlider.interactable = editable;
        _lockButton.interactable = editable;
    }

    // ---- control callbacks -------------------------------------------------------------------------

    private void OnScopeSelected(int kind)
    {
        SiliconAlleyState.SetScope(_currentKey, kind);
        Refresh();
    }

    private void OnFocusChanged(float value)
    {
        if (_suppress)
            return;
        SiliconAlleyState.SetDesignFocus(_currentKey, value);
    }

    private void OnLock()
    {
        SiliconAlleyState.LockConcept(_currentKey);
        Refresh();
    }

    private void CycleStudio(int delta)
    {
        if (_studioKeys.Count == 0)
            return;
        var idx = Mathf.Max(0, _studioKeys.IndexOf(_currentKey));
        idx = (idx + delta + _studioKeys.Count) % _studioKeys.Count;
        _currentKey = _studioKeys[idx];
        Refresh();
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private static string Pct(float fraction01) =>
        Mathf.RoundToInt(Mathf.Clamp01(fraction01) * 100f).ToString(CultureInfo.InvariantCulture);

    private static string ProductName(BusinessType businessType)
    {
        if (businessType?.businessProducts == null || businessType.businessProducts.Length == 0)
            return "project";
        return businessType.businessProducts[0].itemName.GetLocalization();
    }

    private static string Compose(string key, params (string, string)[] args)
    {
        var dict = new Dictionary<string, string>();
        foreach (var (k, v) in args)
            dict[k] = v;
        return key.Localize(dict).ToString();
    }

    private static int CountStaff(BuildingRegistration reg)
    {
        if (reg?.itemInstances == null)
            return 0;
        var hour = TimeHelper.CurrentHour;
        var n = 0;
        foreach (var instance in reg.itemInstances.Values)
        {
            if ((instance.ItemCached.type & ItemType.EmployeeWorkstation) == 0)
                continue;
            if (EmployeeHelper.GetEmployeeAtStationAndHour(reg, instance.id, hour) != null)
                n++;
        }
        return n;
    }

    private static string EtaText(float remaining, float perHour)
    {
        if (perHour <= 0f)
            return "siliconalley:client_eta_idle".GetLocalization();
        var hours = Mathf.CeilToInt(Mathf.Max(0f, remaining) / perHour);
        if (hours <= 0)
            return "siliconalley:client_eta_due".GetLocalization();
        var days = hours / 24;
        var rest = hours % 24;
        return days > 0
            ? "~" + days.ToString(CultureInfo.InvariantCulture) + "d " + rest.ToString(CultureInfo.InvariantCulture) + "h"
            : "~" + rest.ToString(CultureInfo.InvariantCulture) + "h";
    }

    // ---- code-built uGUI ---------------------------------------------------------------------------

    private void EnsureBuilt()
    {
        if (_built)
            return;
        Build();
        _built = true;
    }

    private void Build()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        _root = new GameObject("SiliconAlleyProjectCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (EventSystem.current == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        // Dim backdrop that also closes the screen and blocks uGUI click-through.
        var backdrop = MakeImage(_root.transform, "Backdrop", new Color(0f, 0f, 0f, 0.55f));
        Stretch(backdrop.rectTransform);
        var backdropButton = backdrop.gameObject.AddComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(Close);

        // Centered panel with a vertical layout.
        var panel = MakeImage(_root.transform, "Panel", PanelColor);
        var panelRt = panel.rectTransform;
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(560f, 540f);
        panelRt.anchoredPosition = Vector2.zero;
        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 18, 18);
        layout.spacing = 9f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperCenter;

        _titleText = MakeText(panel.transform, "Title", 22, TextAnchor.MiddleCenter, FontStyle.Bold);

        // Studio selector: [<]  name  [>]
        var studioRow = MakeRow(panel.transform);
        FixWidth(MakeButton(studioRow.transform, "‹", () => CycleStudio(-1)), 44f);
        _studioText = MakeText(studioRow.transform, "Studio", 17, TextAnchor.MiddleCenter);
        FixWidth(MakeButton(studioRow.transform, "›", () => CycleStudio(1)), 44f);

        _phaseText = MakeText(panel.transform, "Phase", 16, TextAnchor.MiddleLeft);

        MakeHeader(panel.transform, "siliconalley:screen_scope");
        var scopeRow = MakeRow(panel.transform);
        var scopeKeys = new[] { "siliconalley:projecttype_quick", "siliconalley:projecttype_standard", "siliconalley:projecttype_ambitious" };
        for (var i = 0; i < 3; i++)
        {
            var kind = ScopeKinds[i];
            var btn = MakeButton(scopeRow.transform, scopeKeys[i].GetLocalization(), () => OnScopeSelected(kind));
            _scopeButtons[i] = btn;
            _scopeImages[i] = btn.GetComponent<Image>();
        }

        // Focus: Polish [====O====] Speed
        MakeHeader(panel.transform, "siliconalley:screen_focus");
        var focusRow = MakeRow(panel.transform, 10f, 28);
        FixWidth(MakeTextButtonless(focusRow.transform, "siliconalley:screen_focus_polish".GetLocalization()), 70f);
        _focusSlider = MakeSlider(focusRow.transform);
        _focusSlider.onValueChanged.AddListener(OnFocusChanged);
        FixWidth(MakeTextButtonless(focusRow.transform, "siliconalley:screen_focus_speed".GetLocalization()), 70f);

        _designQualityText = MakeText(panel.transform, "DesignQuality", 16, TextAnchor.MiddleLeft);
        _leadText = MakeText(panel.transform, "Lead", 15, TextAnchor.MiddleLeft);
        _etaText = MakeText(panel.transform, "Eta", 15, TextAnchor.MiddleLeft);
        _statusText = MakeText(panel.transform, "Status", 14, TextAnchor.MiddleLeft, FontStyle.Italic);

        // Footer: [Lock concept]  [Close]
        var footer = MakeRow(panel.transform, 10f, 40);
        _lockButton = MakeButton(footer.transform, "siliconalley:screen_lock".GetLocalization(), OnLock);
        MakeButton(footer.transform, "siliconalley:screen_close".GetLocalization(), Close);

        _root.SetActive(false);
    }

    private void MakeHeader(Transform parent, string key) =>
        MakeText(parent, "Header", 15, TextAnchor.MiddleLeft, FontStyle.Bold).text = key.GetLocalization();

    private Text MakeText(Transform parent, string name, int size, TextAnchor anchor, FontStyle style = FontStyle.Normal)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = _font;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = anchor;
        text.color = TextColor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        go.AddComponent<LayoutElement>().minHeight = size + 10;
        return text;
    }

    // A standalone label inside a horizontal row (no button behaviour).
    private Text MakeTextButtonless(Transform parent, string value)
    {
        var t = MakeText(parent, "Label", 14, TextAnchor.MiddleCenter);
        t.text = value;
        return t;
    }

    private Button MakeButton(Transform parent, string label, UnityAction onClick)
    {
        var go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = ButtonColor;
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 34f;
        le.preferredHeight = 34f;
        le.flexibleWidth = 1f;

        var text = MakeText(go.transform, "Label", 16, TextAnchor.MiddleCenter, FontStyle.Bold);
        text.text = label;
        Stretch(text.rectTransform);

        if (onClick != null)
            button.onClick.AddListener(onClick);
        return button;
    }

    private static void FixWidth(Component control, float width)
    {
        var le = control.GetComponent<LayoutElement>() ?? control.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.minWidth = width;
        le.flexibleWidth = 0f;
    }

    private GameObject MakeRow(Transform parent, float spacing = 8f, int minHeight = 36)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleCenter;
        go.AddComponent<LayoutElement>().minHeight = minHeight;
        return go;
    }

    private Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void StretchBand(RectTransform rt, float minY, float maxY)
    {
        rt.anchorMin = new Vector2(0f, minY);
        rt.anchorMax = new Vector2(1f, maxY);
        rt.offsetMin = new Vector2(rt.offsetMin.x, 0f);
        rt.offsetMax = new Vector2(rt.offsetMax.x, 0f);
    }

    // A functional uGUI slider built from the standard background / fill / handle hierarchy.
    private Slider MakeSlider(Transform parent)
    {
        var go = new GameObject("FocusSlider", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var slider = go.AddComponent<Slider>();
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 24f;
        le.flexibleWidth = 1f;

        var background = MakeImage(go.transform, "Background", new Color(0.12f, 0.12f, 0.15f, 1f));
        StretchBand(background.rectTransform, 0.35f, 0.65f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        var fillAreaRt = (RectTransform)fillArea.transform;
        StretchBand(fillAreaRt, 0.35f, 0.65f);
        fillAreaRt.offsetMin = new Vector2(8f, fillAreaRt.offsetMin.y);
        fillAreaRt.offsetMax = new Vector2(-8f, fillAreaRt.offsetMax.y);
        var fill = MakeImage(fillArea.transform, "Fill", new Color(0.30f, 0.55f, 0.85f, 1f));
        var fillRt = fill.rectTransform;
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        fillRt.sizeDelta = new Vector2(10f, 0f);

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        var handleAreaRt = (RectTransform)handleArea.transform;
        handleAreaRt.anchorMin = Vector2.zero;
        handleAreaRt.anchorMax = Vector2.one;
        handleAreaRt.offsetMin = new Vector2(8f, 0f);
        handleAreaRt.offsetMax = new Vector2(-8f, 0f);
        var handle = MakeImage(handleArea.transform, "Handle", new Color(0.88f, 0.90f, 0.96f, 1f));
        var handleRt = handle.rectTransform;
        handleRt.anchorMin = new Vector2(0f, 0f);
        handleRt.anchorMax = new Vector2(0f, 1f);
        handleRt.sizeDelta = new Vector2(16f, 0f);

        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.value = 0.5f;
        return slider;
    }
}
