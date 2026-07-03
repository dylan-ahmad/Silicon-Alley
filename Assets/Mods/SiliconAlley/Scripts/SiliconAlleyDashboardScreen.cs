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
// "Open" deep-link into the full F9 project screen. MOSTLY PRESENTATION — the per-second Refresh only READS
// the live SiliconAlleyState the simulator maintains. The one deliberate WRITE path is the Servers section
// (issue #104): clicking a server's role button calls SiliconAlleyState.SetServerRole on user input; the
// refresh loop never writes. Visuals may need in-engine tweaks (the custom-panel path is unsupported per
// CLAUDE.md).
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

    // Issue #104: the Servers section — a per-studio group card (pooled), each listing its placed servers
    // with a role selector + a live counts summary.
    private GameObject _serversBlock;  // whole section (divider+header+hint+host); hidden when no studios
    private GameObject _serversHost;   // the per-studio group cards are pooled under here
    private TMP_Text _serversEmpty;    // shown when studios exist but no servers are placed

    private readonly List<string> _studioKeys = new List<string>();
    private readonly List<BuildingRegistration> _studioRegs = new List<BuildingRegistration>();
    private readonly List<StudioCard> _cards = new List<StudioCard>();
    private readonly List<ServerGroupCard> _serverCards = new List<ServerGroupCard>();

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

    // The player's studios = our business types rented by the player (the same rule the client uses).
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
        _emptyText.text = SiliconAlleyRegistry.NoStudioLocalizationKey(
            "siliconalley:dash_empty",
            "siliconalley:dash_registration_failed").GetLocalization();
        _emptyText.gameObject.SetActive(count == 0);
        for (var i = 0; i < count; i++)
        {
            var card = EnsureCard(i);
            card.Root.SetActive(true);
            card.Fill(_studioRegs[i], _studioKeys[i]);
        }
        for (var i = count; i < _cards.Count; i++)
            _cards[i].Root.SetActive(false);

        RefreshServers(count);
        ClampHeight();
    }

    private StudioCard EnsureCard(int index)
    {
        while (index >= _cards.Count)
            _cards.Add(StudioCard.Build(_cardsHost.transform, OpenDetail));
        return _cards[index];
    }

    // Issue #104: the Servers section reuses the studios already populated this tick. The group pool is
    // index-aligned with the studio pool (one slot per studio); a card self-hides when its studio owns no
    // servers, and Fill returns that studio's server count so we can show the empty hint when all are zero.
    private void RefreshServers(int count)
    {
        _serversBlock.SetActive(count > 0);
        if (count == 0)
            return;
        var totalServers = 0;
        for (var i = 0; i < count; i++)
        {
            var group = EnsureServerCard(i);
            var n = group.Fill(_studioRegs[i], _studioKeys[i]);
            group.Root.SetActive(n > 0);
            totalServers += n;
        }
        for (var i = count; i < _serverCards.Count; i++)
            _serverCards[i].Root.SetActive(false);
        _serversEmpty.gameObject.SetActive(totalServers == 0);
    }

    private ServerGroupCard EnsureServerCard(int index)
    {
        while (index >= _serverCards.Count)
            _serverCards.Add(ServerGroupCard.Build(_serversHost.transform, Refresh));
        return _serverCards[index];
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

        // Servers section (issue #104): per-studio server role assignment. The whole block is toggled off
        // when the player owns no studio, so "Servers" never shows above the top-level empty state.
        _serversBlock = MakeSection(root);
        MakeDivider(_serversBlock.transform);
        MakeHeader(_serversBlock.transform, "siliconalley:dash_servers_header");
        _serversEmpty = MakeText(_serversBlock.transform, "ServersEmpty",
            SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        _serversEmpty.color = SiliconAlleyTheme.TextMuted;
        _serversEmpty.text = "siliconalley:dash_servers_hint".GetLocalization();
        _serversHost = MakeSection(_serversBlock.transform);
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

            // Stage + stage-progress bar (issue #88: an idle studio reads "Idle · 0%", not the derived phase).
            var stage = SiliconAlleyState.GetStage(key);
            var phaseFrac = stage == SiliconAlleyState.ProjectStage.Idle
                ? 0f
                : SiliconAlleyState.PhaseProgressFraction(rawProgress, size);
            _phaseText.text = Compose("siliconalley:dash_phase",
                ("phase", SiliconAlleyState.StageNameKey(stage).GetLocalization()),
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

    // Issue #104: token substitution for the Servers section's nested classes. StudioCard keeps its own
    // private copy; this outer-class copy is reachable (unqualified) from ServerGroupCard/ServerRow via C#
    // enclosing-scope name lookup.
    private static string Compose(string key, params (string, string)[] args)
    {
        var dict = new Dictionary<string, string>();
        foreach (var (k, v) in args)
            dict[k] = v;
        return key.Localize(dict).ToString();
    }

    private static string Pct(float fraction01) =>
        Mathf.RoundToInt(Mathf.Clamp01(fraction01) * 100f).ToString(CultureInfo.InvariantCulture);

    private static string Money(float amount) =>
        amount < 0f ? "-" + SiliconAlleyFormat.Money(0f - amount) : SiliconAlleyFormat.Money(amount);

    // ---- Servers section (issue #104) --------------------------------------------------------------

    // One group card per studio that owns >=1 server. Built once (pooled); Fill() re-reads live state each
    // tick and returns the studio's server count (0 => the screen hides this card). Only the per-server role
    // buttons write save state (SiliconAlleyState.SetServerRole); everything here is otherwise a read.
    private sealed class ServerGroupCard
    {
        public GameObject Root;
        private Image _typeIcon;
        private TMP_Text _name;
        private TMP_Text _chipTotal, _chipInfra, _chipBackend, _chipHosting, _chipUnassigned;
        private TMP_Text _economy;
        private GameObject _rowsHost;
        private readonly List<ServerRow> _rows = new List<ServerRow>();
        private readonly List<string> _ids = new List<string>(); // reused scratch: this studio's server ids
        private Action _onChanged;

        public static ServerGroupCard Build(Transform parent, Action onChanged)
        {
            var c = new ServerGroupCard { _onChanged = onChanged };
            var card = MakeCardPanel(parent, "ServerGroupCard");
            var t = card.transform;

            // Header: [type icon] studio name (grows to fill).
            var header = MakeRow(t, 8f, 28);
            header.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            c._typeIcon = MakeIcon(header.transform, null, 24f, SiliconAlleyTheme.Text);
            c._name = MakeText(header.transform, "Name", SiliconAlleyTheme.Sizes.Subtitle, TextAnchor.MiddleLeft, FontStyle.Bold);
            c._name.GetComponent<LayoutElement>().flexibleWidth = 1f;

            // Counts summary: five chips (built once, text re-set each tick). Reuses MakeChip per the issue.
            var chips = MakeRow(t, 6f, 24);
            chips.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            MakeChip(chips.transform, SiliconAlleyTheme.Slate, SiliconAlleyTheme.Text, out c._chipTotal);
            MakeChip(chips.transform, SiliconAlleyTheme.Accent, SiliconAlleyTheme.Text, out c._chipInfra);
            MakeChip(chips.transform, SiliconAlleyTheme.Accent, SiliconAlleyTheme.Text, out c._chipBackend);
            MakeChip(chips.transform, SiliconAlleyTheme.Accent, SiliconAlleyTheme.Text, out c._chipHosting);
            MakeChip(chips.transform, SiliconAlleyTheme.Slate, SiliconAlleyTheme.TextMuted, out c._chipUnassigned);

            c._economy = MakeText(t, "ServerEconomy", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
            c._economy.color = SiliconAlleyTheme.TextMuted;

            c._rowsHost = MakeSection(t); // per-server rows pooled here
            c.Root = card;
            return c;
        }

        // Returns the studio's live server count. Enumerates itemInstances twice (once here for the sorted id
        // list, once inside ServerCountsByRole which also prunes stale role entries) — negligible at 1 Hz over
        // a handful of servers, and the two passes agree because both filter IsServerInstance over the same set.
        public int Fill(BuildingRegistration reg, string key)
        {
            SetIconSprite(_typeIcon, SiliconAlleyTheme.IconFor(reg.businessTypeName));
            _name.text = reg.GetDisplayName();

            // Stable "Server N" numbering: sort the ids (Dictionary iteration order is not guaranteed).
            _ids.Clear();
            if (reg.itemInstances != null)
                foreach (var pair in reg.itemInstances)
                    if (SiliconAlleyOfficeSimulator.IsServerInstance(pair.Value))
                        _ids.Add(pair.Key);
            _ids.Sort(StringComparer.Ordinal);

            var counts = SiliconAlleyState.ServerCountsByRole(key, reg);
            _chipTotal.text = Compose("siliconalley:dash_servers_total", ("n", _ids.Count.ToString(CultureInfo.InvariantCulture)));
            _chipInfra.text = Compose("siliconalley:dash_servers_infrastructure", ("n", Num(counts, SiliconAlleyState.ServerRole.Infrastructure)));
            _chipBackend.text = Compose("siliconalley:dash_servers_backend", ("n", Num(counts, SiliconAlleyState.ServerRole.Backend)));
            _chipHosting.text = Compose("siliconalley:dash_servers_hosting", ("n", Num(counts, SiliconAlleyState.ServerRole.Hosting)));
            _chipUnassigned.text = Compose("siliconalley:dash_servers_unassigned", ("n", Num(counts, SiliconAlleyState.ServerRole.Unassigned)));
            FillEconomy(key, reg, counts);

            for (var i = 0; i < _ids.Count; i++)
            {
                var row = EnsureRow(i);
                row.Root.SetActive(true);
                row.Fill(key, _ids[i], i + 1);
            }
            for (var i = _ids.Count; i < _rows.Count; i++)
                _rows[i].Root.SetActive(false);

            return _ids.Count;
        }

        private ServerRow EnsureRow(int index)
        {
            while (index >= _rows.Count)
                _rows.Add(ServerRow.Build(_rowsHost.transform, _onChanged));
            return _rows[index];
        }

        private static string Num(Dictionary<SiliconAlleyState.ServerRole, int> counts, SiliconAlleyState.ServerRole role) =>
            counts[role].ToString(CultureInfo.InvariantCulture);

        private void FillEconomy(string key, BuildingRegistration reg,
            Dictionary<SiliconAlleyState.ServerRole, int> counts)
        {
            var total = _ids.Count;
            var hosting = counts[SiliconAlleyState.ServerRole.Hosting];
            var backend = counts[SiliconAlleyState.ServerRole.Backend];
            var infra = counts[SiliconAlleyState.ServerRole.Infrastructure];
            var upkeep = SiliconAlleyOfficeSimulator.ServerUpkeepPerDay(total);
            var hostingGross = SiliconAlleyOfficeSimulator.HostingIncomePerDay(hosting);
            var hostingNet = hostingGross - upkeep;
            var backendCapacity = Mathf.RoundToInt(backend * SiliconAlleyState.BackendCapPerServer);
            var coverage = SiliconAlleyOfficeSimulator.BackendCoverage(key, reg);
            var infraBonus = SiliconAlleyOfficeSimulator.InfrastructureProgressMultiplier(infra) - 1f;

            _economy.text = Compose("siliconalley:dash_servers_economy",
                ("upkeep", Money(upkeep)),
                ("hosting", Money(hostingGross)),
                ("net", Money(hostingNet)),
                ("coverage", Pct(coverage)),
                ("capacity", backendCapacity.ToString(CultureInfo.InvariantCulture)),
                ("infra", Pct(infraBonus)));
        }
    }

    // One row per placed server: a "Server N" label + a 3-button role selector (the scope-picker recolour
    // pattern — active role tinted Accent, others Slate, working because MakeButton keeps normalColor white).
    // Pooled: Build() runs once, Fill() re-binds it to whichever server it currently shows.
    private sealed class ServerRow
    {
        public GameObject Root;
        private string _key, _id;            // the CURRENTLY bound server, re-set every Fill
        private TMP_Text _label;
        private readonly Image[] _roleImages = new Image[3];
        private Action _onChanged;

        // The fixed role set the buttons offer (Unassigned is the default/cleared state, not a button).
        private static readonly SiliconAlleyState.ServerRole[] Roles =
        {
            SiliconAlleyState.ServerRole.Infrastructure,
            SiliconAlleyState.ServerRole.Backend,
            SiliconAlleyState.ServerRole.Hosting
        };
        private static readonly string[] RoleKeys =
        {
            "siliconalley:server_role_infrastructure",
            "siliconalley:server_role_backend",
            "siliconalley:server_role_hosting"
        };

        public static ServerRow Build(Transform parent, Action onChanged)
        {
            var r = new ServerRow { _onChanged = onChanged };
            var row = MakeRow(parent, 6f, 32);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;

            r._label = MakeText(row.transform, "Label", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleLeft);
            FixWidth(r._label, 72f); // fixed left column so the three buttons split the rest evenly

            for (var i = 0; i < 3; i++)
            {
                var role = Roles[i]; // per-index local (value type, own copy) captured by the closure
                var btn = MakeButton(row.transform, RoleKeys[i].GetLocalization(), () => r.OnRole(role));
                SetButtonIcon(btn, SiliconAlleyTheme.IconFor(RoleKeys[i])); // optional art; null-graceful
                var lbl = btn.GetComponentInChildren<TMP_Text>();
                lbl.fontSize = SiliconAlleyTheme.Sizes.Caption; // shrink so "Infrastructure" fits a third-width button
                lbl.enableWordWrapping = false;                 // never grow to two lines (layout stability)
                r._roleImages[i] = btn.GetComponent<Image>();
            }
            r.Root = row;
            return r;
        }

        public void Fill(string key, string id, int number)
        {
            _key = key;
            _id = id;
            _label.text = Compose("siliconalley:dash_server_label", ("n", number.ToString(CultureInfo.InvariantCulture)));
            var current = SiliconAlleyState.GetServerRole(key, id);
            for (var i = 0; i < 3; i++)
                _roleImages[i].color = Roles[i] == current ? SiliconAlleyTheme.Accent : SiliconAlleyTheme.Slate;
        }

        // Toggle: clicking the active role clears it back to Unassigned (SetServerRole removes the entry);
        // clicking another role switches to it. This is the only save-write in the whole screen.
        private void OnRole(SiliconAlleyState.ServerRole role)
        {
            var current = SiliconAlleyState.GetServerRole(_key, _id);
            var next = current == role ? SiliconAlleyState.ServerRole.Unassigned : role;
            SiliconAlleyState.SetServerRole(_key, _id, next);
            _onChanged?.Invoke(); // = the screen's Refresh: immediate recolour + summary update
        }
    }
}
