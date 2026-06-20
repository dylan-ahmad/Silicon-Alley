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
    private Text _titleText, _studioText, _phaseText, _summaryText;
    private GameObject _designSection, _developmentSection, _testingSection;
    // Design section
    private Text _designQualityText, _leadText, _etaText, _statusText;
    private readonly Image[] _scopeImages = new Image[3];
    private readonly Button[] _scopeButtons = new Button[3];
    private Slider _focusSlider;
    private Button _lockButton;
    // Development section
    private Text _devThroughputText, _devBuildText, _devEtaText, _overtimeLabel;
    private Image _overtimeImage;
    // Testing section
    private Text _testBugsText, _testStaffText, _holdLabel;
    private Image _holdImage;

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
        var reg = FindRegistration(_currentKey);
        if (reg == null)
        {
            _titleText.text = "siliconalley:screen_title_plain".GetLocalization();
            _studioText.text = "siliconalley:screen_nostudio".GetLocalization();
            _phaseText.text = "";
            _summaryText.text = "";
            _designSection.SetActive(false);
            _developmentSection.SetActive(false);
            _testingSection.SetActive(false);
            return;
        }

        var businessType = BusinessTypeHelper.GetData(reg);
        var key = _currentKey;
        var size = SiliconAlleyState.EffectiveProjectSize(key);
        var rawProgress = SiliconAlleyState.GetProgress(key);
        var phase = SiliconAlleyState.PhaseOf(rawProgress, size);
        var perHour = SiliconAlleyOfficeSimulator.CurrentHourlyProgress(reg);
        var phaseName = SiliconAlleyState.PhaseNameKey(phase).GetLocalization();

        _titleText.text = Compose("siliconalley:screen_title", ("phase", phaseName));
        _studioText.text = Compose("siliconalley:screen_studio",
            ("business", reg.GetDisplayName()), ("product", ProductName(businessType)));
        _phaseText.text = Compose("siliconalley:screen_phase",
            ("phase", phaseName), ("progress", Pct(SiliconAlleyState.PhaseProgressFraction(rawProgress, size))));

        var avgQ = SiliconAlleyState.GetAverageQuality(key);
        _summaryText.text = Compose("siliconalley:screen_summary",
            ("quality", avgQ < 0f ? "—" : Pct(avgQ) + "%"),
            ("shipeta", EtaText(size - rawProgress, perHour)));

        var inDesign = phase == SiliconAlleyState.ProjectPhase.Design;
        var inDevelopment = phase == SiliconAlleyState.ProjectPhase.Development;
        var inTesting = phase == SiliconAlleyState.ProjectPhase.Testing;
        _designSection.SetActive(inDesign);
        _developmentSection.SetActive(inDevelopment);
        _testingSection.SetActive(inTesting);
        if (inDesign)
            RefreshDesign(reg, businessType, key, size, rawProgress, perHour);
        else if (inDevelopment)
            RefreshDevelopment(reg, key, size, rawProgress, perHour);
        else if (inTesting)
            RefreshTesting(reg, key, perHour);
    }

    private void RefreshDesign(BuildingRegistration reg, BusinessType businessType, string key, float size, float rawProgress, float perHour)
    {
        var editable = SiliconAlleyState.CanEditConcept(key);

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

        var currentKind = SiliconAlleyState.GetProjectType(key);
        for (var i = 0; i < 3; i++)
            _scopeImages[i].color = ScopeKinds[i] == currentKind ? ButtonSelected : ButtonColor;

        _suppress = true; // setting the value must not write back through OnFocusChanged
        _focusSlider.value = SiliconAlleyState.GetDesignFocus(key);
        _suppress = false;

        SetControlsInteractable(editable);
    }

    private void RefreshDevelopment(BuildingRegistration reg, string key, float size, float rawProgress, float perHour)
    {
        _devThroughputText.text = Compose("siliconalley:screen_dev_throughput",
            ("staff", CountStaff(reg).ToString(CultureInfo.InvariantCulture)),
            ("perhour", Mathf.RoundToInt(perHour).ToString(CultureInfo.InvariantCulture)));
        _devBuildText.text = Compose("siliconalley:screen_dev_build",
            ("progress", Mathf.RoundToInt(rawProgress).ToString(CultureInfo.InvariantCulture)),
            ("size", Mathf.RoundToInt(size).ToString(CultureInfo.InvariantCulture)));
        var remaining = SiliconAlleyState.PhaseEndProgress(SiliconAlleyState.ProjectPhase.Development, size) - rawProgress;
        _devEtaText.text = Compose("siliconalley:screen_dev_eta", ("eta", EtaText(remaining, perHour)));

        var on = SiliconAlleyState.IsOvertime(key);
        _overtimeLabel.text = Compose("siliconalley:screen_overtime",
            ("state", (on ? "siliconalley:screen_on" : "siliconalley:screen_off").GetLocalization()));
        _overtimeImage.color = on ? ButtonSelected : ButtonColor;
    }

    private void RefreshTesting(BuildingRegistration reg, string key, float perHour)
    {
        var avgQ = SiliconAlleyState.GetAverageQuality(key);
        var bugs = avgQ < 0f ? "—" : Mathf.CeilToInt((1f - Mathf.Clamp01(avgQ)) * 20f).ToString(CultureInfo.InvariantCulture);
        _testBugsText.text = Compose("siliconalley:screen_test_bugs", ("bugs", bugs));
        _testStaffText.text = Compose("siliconalley:screen_test_staff",
            ("staff", CountStaff(reg).ToString(CultureInfo.InvariantCulture)),
            ("perhour", Mathf.RoundToInt(perHour).ToString(CultureInfo.InvariantCulture)));

        var held = SiliconAlleyState.IsHold(key);
        _holdLabel.text = Compose("siliconalley:screen_hold",
            ("state", (held ? "siliconalley:screen_on" : "siliconalley:screen_off").GetLocalization()));
        _holdImage.color = held ? ButtonSelected : ButtonColor;
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

    private void OnToggleOvertime()
    {
        SiliconAlleyState.SetOvertime(_currentKey, !SiliconAlleyState.IsOvertime(_currentKey));
        Refresh();
    }

    private void OnToggleHold()
    {
        SiliconAlleyState.SetHold(_currentKey, !SiliconAlleyState.IsHold(_currentKey));
        Refresh();
    }

    private void OnShipNow()
    {
        SiliconAlleyState.ShipNow(_currentKey);
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
        panelRt.sizeDelta = new Vector2(560f, 0f); // height auto-fits the active section (ContentSizeFitter)
        panelRt.anchoredPosition = Vector2.zero;
        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 18, 18);
        layout.spacing = 9f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperCenter;
        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _titleText = MakeText(panel.transform, "Title", 22, TextAnchor.MiddleCenter, FontStyle.Bold);

        // Studio selector: [<]  name  [>]
        var studioRow = MakeRow(panel.transform);
        FixWidth(MakeButton(studioRow.transform, "‹", () => CycleStudio(-1)), 44f);
        _studioText = MakeText(studioRow.transform, "Studio", 17, TextAnchor.MiddleCenter);
        FixWidth(MakeButton(studioRow.transform, "›", () => CycleStudio(1)), 44f);

        _phaseText = MakeText(panel.transform, "Phase", 16, TextAnchor.MiddleLeft);
        _summaryText = MakeText(panel.transform, "Summary", 15, TextAnchor.MiddleLeft);

        // ---- Design section (shown in the Design phase) ----
        _designSection = MakeSection(panel.transform);
        MakeHeader(_designSection.transform, "siliconalley:screen_scope");
        var scopeRow = MakeRow(_designSection.transform);
        var scopeKeys = new[] { "siliconalley:projecttype_quick", "siliconalley:projecttype_standard", "siliconalley:projecttype_ambitious" };
        for (var i = 0; i < 3; i++)
        {
            var kind = ScopeKinds[i];
            var btn = MakeButton(scopeRow.transform, scopeKeys[i].GetLocalization(), () => OnScopeSelected(kind));
            _scopeButtons[i] = btn;
            _scopeImages[i] = btn.GetComponent<Image>();
        }
        MakeHeader(_designSection.transform, "siliconalley:screen_focus");
        var focusRow = MakeRow(_designSection.transform, 10f, 28);
        FixWidth(MakeTextButtonless(focusRow.transform, "siliconalley:screen_focus_polish".GetLocalization()), 70f);
        _focusSlider = MakeSlider(focusRow.transform);
        _focusSlider.onValueChanged.AddListener(OnFocusChanged);
        FixWidth(MakeTextButtonless(focusRow.transform, "siliconalley:screen_focus_speed".GetLocalization()), 70f);
        _designQualityText = MakeText(_designSection.transform, "DesignQuality", 16, TextAnchor.MiddleLeft);
        _leadText = MakeText(_designSection.transform, "Lead", 15, TextAnchor.MiddleLeft);
        _etaText = MakeText(_designSection.transform, "Eta", 15, TextAnchor.MiddleLeft);
        _statusText = MakeText(_designSection.transform, "Status", 14, TextAnchor.MiddleLeft, FontStyle.Italic);
        _lockButton = MakeButton(_designSection.transform, "siliconalley:screen_lock".GetLocalization(), OnLock);

        // ---- Development section (shown in the Development phase) ----
        _developmentSection = MakeSection(panel.transform);
        _devThroughputText = MakeText(_developmentSection.transform, "DevThroughput", 15, TextAnchor.MiddleLeft);
        _devBuildText = MakeText(_developmentSection.transform, "DevBuild", 16, TextAnchor.MiddleLeft);
        _devEtaText = MakeText(_developmentSection.transform, "DevEta", 15, TextAnchor.MiddleLeft);
        var overtimeButton = MakeButton(_developmentSection.transform, "", OnToggleOvertime);
        _overtimeImage = overtimeButton.GetComponent<Image>();
        _overtimeLabel = overtimeButton.GetComponentInChildren<Text>();

        // ---- Testing section (shown in the Testing phase) ----
        _testingSection = MakeSection(panel.transform);
        _testBugsText = MakeText(_testingSection.transform, "TestBugs", 16, TextAnchor.MiddleLeft);
        _testStaffText = MakeText(_testingSection.transform, "TestStaff", 15, TextAnchor.MiddleLeft);
        var testRow = MakeRow(_testingSection.transform, 10f, 40);
        var holdButton = MakeButton(testRow.transform, "", OnToggleHold);
        _holdImage = holdButton.GetComponent<Image>();
        _holdLabel = holdButton.GetComponentInChildren<Text>();
        MakeButton(testRow.transform, "siliconalley:screen_ship".GetLocalization(), OnShipNow);

        // ---- Footer (common) ----
        var footer = MakeRow(panel.transform, 10f, 40);
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

    // A vertical sub-container the screen toggles per phase (SetActive). It reports its own preferred
    // height, so the panel's ContentSizeFitter shrinks to whichever section is active.
    private GameObject MakeSection(Transform parent)
    {
        var go = new GameObject("Section", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = 8f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.UpperLeft;
        return go;
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
