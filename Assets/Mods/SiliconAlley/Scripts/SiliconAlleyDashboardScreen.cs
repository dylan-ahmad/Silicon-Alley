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
using UnityEngine.UI;
using static SiliconAlleyUI; // issue #54: the shared Make* styled-component layer
using static SiliconAlleyFormat; // issue #144: ONE format table — no private Money/Pct re-implementations

[assembly: RegisterModClass(typeof(SiliconAlleyDashboardScreenMod))]

// Issue #127 (epic #121): the standalone F8 dashboard window is gone — its content (the per-studio cards
// and the Servers section, issues #59/#104) now lives as the HUB landing page inside the F9 project screen,
// so there is ONE menu entry point the player clicks through. This file keeps:
//   - SiliconAlleyDashboardScreen: a thin alias — the F8 hotkey (still rebindable from the options panel)
//     and the phone client's "View studios" both open the same hub page.
//   - SiliconAlleyStudioCard / SiliconAlleyServerGroupCard: the pooled card builders the hub hosts,
//     unchanged in behaviour (per-second read-only Fill; the server role buttons remain the only write).
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
        context.Logger.Info("SiliconAlley: studio hub ready (open from the phone client, F8, or F9).");
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

// Issue #127: the F8 alias. The key is machine-local + rebindable (the options panel writes ToggleKey), and
// both entry points route into the project screen's hub page — no separate window, nothing else to maintain.
public class SiliconAlleyDashboardScreen : MonoBehaviour
{
    public static SiliconAlleyDashboardScreen Instance { get; private set; }

    // The hotkey that opens the hub (machine-local; rebindable via the options panel). Defaults to F8 so it
    // doesn't clash with the project screen's F9. KeyChoices is the options dropdown's index map.
    public static KeyCode ToggleKey = KeyCode.F8;
    public static readonly KeyCode[] KeyChoices =
        { KeyCode.F8, KeyCode.F7, KeyCode.F6, KeyCode.F5, KeyCode.Tab, KeyCode.BackQuote };

    // Opened from the phone client ("View studios") — same hub the hotkeys reach.
    public static void Open() => SiliconAlleyProjectScreen.OpenHub();

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (Input.GetKeyDown(ToggleKey))
            SiliconAlleyProjectScreen.ToggleHub();
    }
}

// Issue #59 (now hosted by the #127 hub): one pooled card per player-owned studio — type icon + name + a
// colour-coded demand-trend pill, a stage progress bar, the key stats at a glance, and an "Open" deep-link
// into that studio's detail view. Build() runs once; Fill() re-reads live state each refresh tick.
sealed class SiliconAlleyStudioCard
{
    public GameObject Root;
    private string _key;
    private string _typeName; // issue #146: the bound type, so the trend tooltip reads live demand
    private Image _typeIcon;
    private TMP_Text _name;
    private Image _trendChip;
    private TMP_Text _trendLabel;
    private TMP_Text _phaseText;
    private SiliconAlleyUI.ProgressBar _progress;
    private SiliconAlleyUI.StatRow _quality, _reputation, _installed, _support, _shipEta;

    public static SiliconAlleyStudioCard Build(Transform parent, Action<string> onOpen)
    {
        var c = new SiliconAlleyStudioCard();
        var card = MakeCardPanel(parent, "StudioCard");
        var t = card.transform;

        // Header: [type icon] name (grows) [demand-trend pill, hugs right].
        var header = MakeRow(t, 8f, 30);
        header.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false; // so the pill hugs, not stretches
        c._typeIcon = MakeIcon(header.transform, null, 26f, SiliconAlleyTheme.Text);
        c._name = MakeText(header.transform, "Name", SiliconAlleyTheme.Sizes.Subtitle, TextAnchor.MiddleLeft, FontStyle.Bold);
        c._name.GetComponent<LayoutElement>().flexibleWidth = 1f; // absorb the slack so the pill is pushed right
        c._trendChip = MakeChip(header.transform, SiliconAlleyTheme.Ok, SiliconAlleyTheme.Text, out c._trendLabel);
        // Issue #146: hovering the ▲/▼ pill explains what the demand trend means (live-evaluated at 1 Hz).
        SiliconAlleyTooltip.Attach(c._trendChip, () => c.TrendTip());

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
        MakeSpacer(actionRow.transform);
        var openBtn = MakeButton(actionRow.transform, "siliconalley:dash_open_detail".GetLocalization(), () => onOpen(c._key));
        FixWidth(openBtn, 100f);

        c.Root = card;
        return c;
    }

    // Issue #146: the trend pill's tooltip — why the arrow points where it does and what demand scales.
    private string TrendTip()
    {
        if (string.IsNullOrEmpty(_typeName))
            return null; // not bound to a studio yet — no tooltip
        var day = TimeHelper.CurrentDay;
        var rising = SiliconAlleyMarket.IsRising(_typeName, day);
        return Compose("siliconalley:tip_demand_trend",
            ("demand", Demand(SiliconAlleyMarket.DemandFactor(_typeName, day))),
            ("dir", (rising ? "siliconalley:ui_trend_rising" : "siliconalley:ui_trend_falling").GetLocalization()));
    }

    public void Fill(BuildingRegistration reg, string key)
    {
        _key = key;
        _typeName = reg.businessTypeName; // issue #146: bind the trend tooltip to this studio's category
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
        _trendLabel.text = (rising ? "▲ " : "▼ ") + Demand(demand); // ×1.12 — the same shape the detail view uses (#144)

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

    private static string Compose(string key, params (string, string)[] args)
    {
        var dict = new Dictionary<string, string>();
        foreach (var (k, v) in args)
            dict[k] = v;
        return key.Localize(dict).ToString();
    }
}

// Issue #104 (now hosted by the #127 hub): one pooled group card per studio that owns >=1 server, each
// listing its placed servers with a 3-button role selector + a live counts/economy summary. Only the
// per-server role buttons write save state (SiliconAlleyState.SetServerRole); everything else is a read.
sealed class SiliconAlleyServerGroupCard
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

    public static SiliconAlleyServerGroupCard Build(Transform parent, Action onChanged)
    {
        var c = new SiliconAlleyServerGroupCard { _onChanged = onChanged };
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

    private static string Compose(string key, params (string, string)[] args)
    {
        var dict = new Dictionary<string, string>();
        foreach (var (k, v) in args)
            dict[k] = v;
        return key.Localize(dict).ToString();
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
        // clicking another role switches to it. This is the only save-write in the whole hub.
        private void OnRole(SiliconAlleyState.ServerRole role)
        {
            var current = SiliconAlleyState.GetServerRole(_key, _id);
            var next = current == role ? SiliconAlleyState.ServerRole.Unassigned : role;
            SiliconAlleyState.SetServerRole(_key, _id, next);
            _onChanged?.Invoke(); // = the hub's refresh: immediate recolour + summary update
        }
    }
}
