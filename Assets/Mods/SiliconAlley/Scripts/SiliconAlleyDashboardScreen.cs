using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using BAModAPI;
using Entities;
using Helpers;
using Localizor;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static SiliconAlleyUI; // issue #54: the shared Make* styled-component layer

[assembly: RegisterModClass(typeof(SiliconAlleyDashboardScreenMod))]

// Issue #59 (epic #53 — UI overhaul): the "Silicon Alley Clients" studio dashboard as card-based UI.
// The phone contact (SiliconAlleyClientDialog) is a native text-only Dialog, so it cannot host cards;
// instead it gains a "View studios" button that opens THIS screen — a code-built uGUI window on a mod-owned
// Canvas (the proven "approach A", same pattern as SiliconAlleyProjectScreen — see docs/CAPABILITIES.md).
// One card per player-owned studio: type icon + name + a colour-coded demand-trend pill, a phase progress
// bar, and the key stats at a glance (quality / reputation / installed base / support / ship ETA), with an
// "Open" deep-link into the full F9 project screen. PRESENTATION ONLY — it reads the live SiliconAlleyState
// the simulator maintains and never writes gameplay/save state. Visuals may need in-engine tweaks (the
// custom-panel path is unsupported per CLAUDE.md).
[ModEntryOnCityLoad]
public class SiliconAlleyDashboardScreenMod : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    private GameObject _host;

    public Task OnLoadAsync(ModContext context)
    {
        if (SiliconAlleyDashboardScreen.Instance == null)
        {
            _host = new GameObject("SiliconAlleyDashboardScreen");
            _host.AddComponent<SiliconAlleyDashboardScreen>();
        }
        context.Logger.Info("SiliconAlley: studio dashboard ready (open from the phone client or with F8).");
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

public class SiliconAlleyDashboardScreen : MonoBehaviour
{
    public static SiliconAlleyDashboardScreen Instance { get; private set; }

    // The hotkey that toggles the dashboard (machine-local; rebindable via the options panel). Defaults to
    // F8 so it doesn't clash with the project screen's F9. KeyChoices is the options dropdown's index map.
    public static KeyCode ToggleKey = KeyCode.F8;
    public static readonly KeyCode[] KeyChoices =
        { KeyCode.F8, KeyCode.F7, KeyCode.F6, KeyCode.F5, KeyCode.Tab, KeyCode.BackQuote };

    private const float WindowWidth = 560f;
    private const float MaxHeight = 940f; // window caps here (at the 1080 reference) and scrolls beyond

    private GameObject _root;
    private RectTransform _windowRt, _contentRt;
    private GameObject _cardsHost;  // the studio cards are pooled under here
    private TMP_Text _emptyText;    // shown when the player owns no studio
    private bool _built;
    private bool _visible;
    private float _refresh;         // seconds until the next live refresh

    private readonly List<string> _studioKeys = new List<string>();
    private readonly List<BuildingRegistration> _studioRegs = new List<BuildingRegistration>();
    private readonly List<StudioCard> _cards = new List<StudioCard>();

    // Opened from the phone client ("View studios") — and toggled by the hotkey.
    public static void Open()
    {
        if (Instance != null)
            Instance.OpenInternal();
    }

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

    private void Update()
    {
        if (Input.GetKeyDown(ToggleKey))
            Toggle();
        else if (_visible && Input.GetKeyDown(KeyCode.Escape))
            Close();

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
            OpenInternal();
    }

    private void OpenInternal()
    {
        EnsureBuilt();
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

    // The player's studios = our business types with no rival owner (the same rule the client uses).
    private void PopulateStudios()
    {
        _studioKeys.Clear();
        _studioRegs.Clear();
        var current = SaveGameManager.Current;
        if (current?.BuildingRegistrations == null)
            return;
        foreach (var reg in current.BuildingRegistrations)
            if (SiliconAlleyClient.IsPlayerOwned(reg))
            {
                _studioKeys.Add(SiliconAlleyState.KeyFor(reg));
                _studioRegs.Add(reg);
            }
    }

    // Re-read studios each tick (the count can change while open — a studio could be bought/sold), grow the
    // card pool to fit, fill the active cards and hide the rest, then clamp the window to content.
    private void Refresh()
    {
        PopulateStudios();
        var count = _studioRegs.Count;
        _emptyText.gameObject.SetActive(count == 0);
        for (var i = 0; i < count; i++)
        {
            var card = EnsureCard(i);
            card.Root.SetActive(true);
            card.Fill(_studioRegs[i], _studioKeys[i]);
        }
        for (var i = count; i < _cards.Count; i++)
            _cards[i].Root.SetActive(false);
        ClampHeight();
    }

    private StudioCard EnsureCard(int index)
    {
        while (index >= _cards.Count)
            _cards.Add(StudioCard.Build(_cardsHost.transform, OpenDetail));
        return _cards[index];
    }

    // A card's "Open" deep-links into the full project screen for that studio. Close first so the two
    // overlays (both at sortingOrder 5000) don't stack.
    private void OpenDetail(string key)
    {
        Close();
        SiliconAlleyProjectScreen.Open(key);
    }

    private void ClampHeight()
    {
        if (_contentRt == null || _windowRt == null)
            return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRt);
        _windowRt.sizeDelta = new Vector2(WindowWidth, Mathf.Min(_contentRt.rect.height, MaxHeight));
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
        _root = new GameObject("SiliconAlleyDashboardCanvas",
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

        // Dim backdrop that closes the screen and blocks uGUI click-through.
        var backdrop = MakeImage(_root.transform, "Backdrop", new Color(0f, 0f, 0f, 0.55f));
        Stretch(backdrop.rectTransform);
        var backdropButton = backdrop.gameObject.AddComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(Close);

        // Window: fixed-width panel, centred, height clamped each Refresh; hosts a vertical ScrollRect so a
        // long list of studios scrolls instead of overflowing the screen.
        var window = MakePanel(_root.transform, "Window");
        _windowRt = window.rectTransform;
        _windowRt.anchorMin = _windowRt.anchorMax = new Vector2(0.5f, 0.5f);
        _windowRt.sizeDelta = new Vector2(WindowWidth, 600f);
        _windowRt.anchoredPosition = Vector2.zero;
        var scroll = window.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 24f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(window.transform, false);
        var viewportRt = (RectTransform)viewport.transform;
        Stretch(viewportRt);
        scroll.viewport = viewportRt;

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewport.transform, false);
        _contentRt = (RectTransform)contentGo.transform;
        _contentRt.anchorMin = new Vector2(0f, 1f);
        _contentRt.anchorMax = new Vector2(1f, 1f);
        _contentRt.pivot = new Vector2(0.5f, 1f);
        _contentRt.offsetMin = new Vector2(0f, _contentRt.offsetMin.y);
        _contentRt.offsetMax = new Vector2(0f, _contentRt.offsetMax.y);
        _contentRt.anchoredPosition = Vector2.zero;
        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 18, 18);
        layout.spacing = 10f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperCenter;
        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = _contentRt;
        var root = contentGo.transform;

        // Title row: title (flexible) + [X] close.
        var titleRow = MakeRow(root, 6f, 30);
        var title = MakeText(titleRow.transform, "Title", SiliconAlleyTheme.Sizes.Title, TextAnchor.MiddleLeft, FontStyle.Bold);
        title.text = "siliconalley:dash_title".GetLocalization();
        FixWidth(MakeButton(titleRow.transform, "X", Close), 34f);

        // Empty-state line (toggled in Refresh when the player owns no studio).
        _emptyText = MakeText(root, "Empty", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleLeft);
        _emptyText.text = "siliconalley:dash_empty".GetLocalization();

        // The studio cards are pooled under here.
        _cardsHost = MakeSection(root);
    }

    // ---- per-studio card -----------------------------------------------------------------------------

    // One card per studio. Built once (pooled), then Fill() re-sets its controls from live state each tick.
    private sealed class StudioCard
    {
        public GameObject Root;
        private string _key;
        private Image _typeIcon;
        private TMP_Text _name;
        private Image _trendChip;
        private TMP_Text _trendLabel;
        private TMP_Text _phaseText;
        private SiliconAlleyUI.ProgressBar _progress;
        private SiliconAlleyUI.StatRow _quality, _reputation, _installed, _support, _shipEta;

        public static StudioCard Build(Transform parent, Action<string> onOpen)
        {
            var c = new StudioCard();
            var card = MakeCardPanel(parent, "StudioCard");
            var t = card.transform;

            // Header: [type icon] name (grows) [demand-trend pill, hugs right].
            var header = MakeRow(t, 8f, 30);
            header.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false; // so the pill hugs, not stretches
            c._typeIcon = MakeIcon(header.transform, null, 26f, SiliconAlleyTheme.Text);
            c._name = MakeText(header.transform, "Name", SiliconAlleyTheme.Sizes.Subtitle, TextAnchor.MiddleLeft, FontStyle.Bold);
            c._name.GetComponent<LayoutElement>().flexibleWidth = 1f; // absorb the slack so the pill is pushed right
            c._trendChip = MakeChip(header.transform, SiliconAlleyTheme.Ok, SiliconAlleyTheme.Text, out c._trendLabel);

            // Phase line + a phase-progress bar.
            c._phaseText = MakeText(t, "Phase", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
            c._phaseText.color = SiliconAlleyTheme.TextMuted;
            c._progress = MakeProgressBar(t, 10f);

            // Key stats at a glance.
            c._quality = MakeStatRow(t);
            c._reputation = MakeStatRow(t);
            c._installed = MakeStatRow(t);
            c._support = MakeStatRow(t);
            c._shipEta = MakeStatRow(t);

            // "Open" deep-link, right-aligned (a flexible spacer pushes it to the edge).
            var actionRow = MakeRow(t, 6f, 30);
            actionRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            var spacer = MakeText(actionRow.transform, "Spacer", 1, TextAnchor.MiddleLeft);
            spacer.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var openBtn = MakeButton(actionRow.transform, "siliconalley:dash_open_detail".GetLocalization(), () => onOpen(c._key));
            FixWidth(openBtn, 100f);

            c.Root = card;
            return c;
        }

        public void Fill(BuildingRegistration reg, string key)
        {
            _key = key;
            var businessType = BusinessTypeHelper.GetData(reg);
            // Note the type so EffectiveProjectSize is feature-aware (mirrors the project screen's Refresh).
            SiliconAlleyState.NoteBusinessType(key, businessType?.businessTypeName);
            var size = SiliconAlleyState.EffectiveProjectSize(key);
            var rawProgress = SiliconAlleyState.GetProgress(key);
            var phase = SiliconAlleyState.PhaseOf(rawProgress, size);
            var perHour = SiliconAlleyOfficeSimulator.CurrentHourlyProgress(reg);

            // Header: type icon + studio name.
            SetIconSprite(_typeIcon, SiliconAlleyTheme.IconFor(reg.businessTypeName));
            _name.text = reg.GetDisplayName();

            // Demand trend made visual: ▲ green when rising, ▼ amber when falling (+ the demand value).
            var day = TimeHelper.CurrentDay;
            var rising = SiliconAlleyMarket.IsRising(reg.businessTypeName, day);
            var demand = SiliconAlleyMarket.DemandFactor(reg.businessTypeName, day);
            _trendChip.color = rising ? SiliconAlleyTheme.Ok : SiliconAlleyTheme.Warn;
            _trendLabel.text = (rising ? "▲ " : "▼ ") + demand.ToString("F2", CultureInfo.InvariantCulture);

            // Phase + phase-progress bar.
            var phaseFrac = SiliconAlleyState.PhaseProgressFraction(rawProgress, size);
            _phaseText.text = Compose("siliconalley:dash_phase",
                ("phase", SiliconAlleyState.PhaseNameKey(phase).GetLocalization()),
                ("progress", Pct(phaseFrac)));
            SetProgress(_progress, phaseFrac);

            // Stats (the stems light up if their icon ships; otherwise the row keeps a consistent indent).
            SetStat(_quality, "stat_quality", "siliconalley:dash_lbl_quality",
                SiliconAlleyFormat.Quality(SiliconAlleyState.GetAverageQuality(key)), SiliconAlleyTheme.Accent);
            // Issue #61: reputation + installed base count to their new value (quality can be "—" and support
            // is a "$/day" string, so those stay plain).
            SetStatNum(_reputation, "stat_reputation", "siliconalley:dash_lbl_reputation",
                SiliconAlleyState.GetReputation(key), FmtF2, SiliconAlleyTheme.Text);
            SetStatNum(_installed, "stat_installed", "siliconalley:dash_lbl_installed",
                SiliconAlleyState.GetInstalledBase(key), FmtInt, SiliconAlleyTheme.Text);
            SetStat(_support, "stat_cost", "siliconalley:dash_lbl_support",
                SiliconAlleyFormat.SupportPerDay(reg, key), SiliconAlleyTheme.Ok);
            SetStat(_shipEta, "stat_eta", "siliconalley:dash_lbl_shipeta",
                SiliconAlleyFormat.Eta(size - rawProgress, perHour), SiliconAlleyTheme.Text);
        }

        private static void SetStat(SiliconAlleyUI.StatRow row, string iconStem, string labelKey, string value, Color valueColor)
        {
            SetIconSprite(row.Icon, SiliconAlleyTheme.IconFor(iconStem));
            row.Label.text = labelKey.GetLocalization();
            row.Value.text = value;
            row.Value.color = valueColor;
        }

        // Issue #61: like SetStat but the numeric value counts to its new target each frame.
        private static void SetStatNum(SiliconAlleyUI.StatRow row, string iconStem, string labelKey, float target, Func<float, string> format, Color valueColor)
        {
            SetIconSprite(row.Icon, SiliconAlleyTheme.IconFor(iconStem));
            row.Label.text = labelKey.GetLocalization();
            row.Value.color = valueColor;
            AnimateNumber(row.Value, target, format);
        }

        private static readonly Func<float, string> FmtInt = v => Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);
        private static readonly Func<float, string> FmtF2 = v => v.ToString("F2", CultureInfo.InvariantCulture);

        private static string Pct(float fraction01) =>
            Mathf.RoundToInt(Mathf.Clamp01(fraction01) * 100f).ToString(CultureInfo.InvariantCulture);

        private static string Compose(string key, params (string, string)[] args)
        {
            var dict = new Dictionary<string, string>();
            foreach (var (k, v) in args)
                dict[k] = v;
            return key.Localize(dict).ToString();
        }
    }
}
