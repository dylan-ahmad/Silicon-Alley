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

// Issue #148 (epic #142): ONE attention/totals computation per studio per tick. RefreshHub's pre-pass
// fills an Info per studio; the triage strip, the needs-action-first sort AND each card's badge all
// consume the same result — the three can never disagree. Severity: Danger = a decision is open or a
// deadline is ≤3 days away/overdue; Warn = parked ready work (release/update). Theme note (#143): Warn
// is caution-only and Danger is normally destructive-only — issue #148 explicitly sanctions this
// attention badge as the exception. Presentation only: Compute's single write is the NoteBusinessType
// cache note the detail view's Refresh also makes.
public static class SiliconAlleyAttention
{
    public enum Level { None = 0, Warn = 1, Danger = 2 }

    public struct Info
    {
        public Level Level;
        public string ReasonKey;   // locale key of the WINNING reason; null when quiet
        public string ReasonArg;   // pre-formatted {days} arg (DaysLeft shape), or null
        public bool Idle;          // stage == Idle — the card's quiet-compact rules reuse it
        // Totals fodder, so the strip needs no second pass over the studios:
        public float SupportValue; // SupportPerDayValue(reg, key)
        public int Installed;      // GetInstalledBase(key)
        public int ServerCount;    // itemInstances filtered by IsServerInstance
    }

    // ONE deadline threshold for contract AND publisher deal: ≤3 days, matching the detail view's amber
    // "urgent" tint. The simulator's DealWarnDays=2 TOAST fires later by design — a toast interrupts,
    // the hub badge is ambient — so do not "align" the two.
    public const int DeadlineWarnDays = 3;

    public static Info Compute(BuildingRegistration reg, string key)
    {
        var info = default(Info);
        var businessType = BusinessTypeHelper.GetData(reg);
        // NoteBusinessType FIRST — EffectiveProjectSize and the milestone windows are feature-aware
        // (mirrors the detail view's Refresh ordering).
        SiliconAlleyState.NoteBusinessType(key, businessType?.businessTypeName);
        var size = SiliconAlleyState.EffectiveProjectSize(key);
        var rawProgress = SiliconAlleyState.GetProgress(key);
        var stage = SiliconAlleyState.GetStage(key);
        info.Idle = stage == SiliconAlleyState.ProjectStage.Idle;

        // Danger tier — first found wins within the tier (decision > contract > deal; the detail view
        // surfaces whatever the badge doesn't).
        if (!info.Idle && SiliconAlleyMilestones.TryGetPending(key, stage, rawProgress, size, out _, out _))
            Escalate(ref info, Level.Danger, "siliconalley:dash_attn_decision", null);
        if (SiliconAlleyState.HasContract(key))
        {
            var daysLeft = SiliconAlleyState.GetContractDeadlineDay(key) - TimeHelper.CurrentDay;
            if (daysLeft <= DeadlineWarnDays) // includes overdue (negative ⇒ DaysLeft renders "due now")
                Escalate(ref info, Level.Danger, "siliconalley:dash_attn_contract", DaysLeft(daysLeft));
        }
        if (SiliconAlleyState.HasDeal(key))
        {
            var daysLeft = SiliconAlleyState.GetDealDeadlineDay(key) - TimeHelper.CurrentDay;
            if (daysLeft <= DeadlineWarnDays)
                Escalate(ref info, Level.Danger, "siliconalley:dash_attn_deal", DaysLeft(daysLeft));
        }

        // Warn tier. "Ready" counts only Development/Testing parks — a Design park is the wizard's flow,
        // not a release decision (matches the dev-done / ready-to-release toasts).
        var releaseStage = stage == SiliconAlleyState.ProjectStage.Development
            || stage == SiliconAlleyState.ProjectStage.Testing;
        if (releaseStage && !SiliconAlleyState.IsReleaseRequested(key)
            && rawProgress >= SiliconAlleyState.StageCeiling(stage, size))
            Escalate(ref info, Level.Warn, "siliconalley:dash_attn_ready", null);
        if (SiliconAlleyOfficeSimulator.IsUpdateDue(key) && !SiliconAlleyState.IsUpdateRequested(key))
            Escalate(ref info, Level.Warn, "siliconalley:dash_attn_update", null);

        // Totals fodder (server count WITHOUT ServerCountsByRole — that one allocates a dictionary and
        // prunes state; this is a pure filtered count).
        info.SupportValue = SupportPerDayValue(reg, key);
        info.Installed = SiliconAlleyState.GetInstalledBase(key);
        if (reg.itemInstances != null)
            foreach (var pair in reg.itemInstances)
                if (SiliconAlleyOfficeSimulator.IsServerInstance(pair.Value))
                    info.ServerCount++;
        return info;
    }

    // The badge/chip text for an Info; null when quiet (SetBadge hides on null).
    public static string ReasonText(in Info info) =>
        info.ReasonKey == null ? null
        : info.ReasonArg == null ? info.ReasonKey.GetLocalization()
        : Compose(info.ReasonKey, ("days", info.ReasonArg));

    // Keep the higher severity; within a tier the FIRST reason wins (call order = priority).
    private static void Escalate(ref Info info, Level level, string reasonKey, string arg)
    {
        if (level <= info.Level)
            return;
        info.Level = level;
        info.ReasonKey = reasonKey;
        info.ReasonArg = arg;
    }
}

// Issue #148: the hub's triage strip — "N studios need you" + one clickable chip per needing studio +
// the aggregate totals line. Built once; Fill runs each tick from the SAME Info list the sort and the
// card badges consume (RefreshHub's pre-pass), so the strip can never disagree with the cards. The
// totals sum the pre-pass floats and format ONCE via the shared table (#144/#148 SupportPerDayValue).
sealed class SiliconAlleyHubStrip
{
    public GameObject Root;
    private TMP_Text _headline;
    private GameObject _chipsRow;
    private readonly List<Chip> _chips = new List<Chip>(); // grow-only pool of clickable pills
    private TMP_Text _totals;
    private TMP_Text _serversHint;
    private Action<string> _onOpen;

    private sealed class Chip
    {
        public GameObject Root;
        public Image Image;
        public TMP_Text Label;
        public string Key; // the CURRENT bind — the click delegate reads it at click time
    }

    public static SiliconAlleyHubStrip Build(Transform parent, Action<string> onOpen)
    {
        var s = new SiliconAlleyHubStrip { _onOpen = onOpen };
        var panel = MakeCardPanel(parent, "HubStrip");
        s.Root = panel;
        var t = panel.transform;
        s._headline = MakeText(t, "Headline", SiliconAlleyTheme.Sizes.Header, TextAnchor.MiddleLeft, FontStyle.Bold);
        s._headline.color = SiliconAlleyTheme.Header;
        s._chipsRow = MakeRow(t, SiliconAlleyTheme.Space.Small, 26);
        s._chipsRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false; // chips hug left
        s._totals = MakeText(t, "Totals", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        s._totals.color = SiliconAlleyTheme.TextMuted;
        // The old Servers block's onboarding hint, relocated (#148 deleted that block): shown only while
        // no studio owns a server at all.
        s._serversHint = MakeText(t, "ServersHint", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        s._serversHint.color = SiliconAlleyTheme.TextMuted;
        s._serversHint.text = "siliconalley:dash_servers_hint".GetLocalization();
        return s;
    }

    public void Fill(List<BuildingRegistration> regs, List<string> keys, List<SiliconAlleyAttention.Info> infos)
    {
        var count = keys.Count;
        var needing = 0;
        var support = 0f;
        var installed = 0;
        var servers = 0;
        for (var i = 0; i < count; i++)
        {
            var inf = infos[i];
            if (inf.Level != SiliconAlleyAttention.Level.None)
                needing++;
            support += inf.SupportValue; // floats summed, Money() ONCE below — matches the card figures
            installed += inf.Installed;
            servers += inf.ServerCount;
        }

        _headline.text = needing == 0
            ? "siliconalley:dash_strip_quiet".GetLocalization()
            : needing == 1
                ? "siliconalley:dash_strip_needs_one".GetLocalization()
                : Compose("siliconalley:dash_strip_needs", ("n", needing.ToString(CultureInfo.InvariantCulture)));

        // One clickable chip per needing studio, Danger tier first — the same order the grid sorts by.
        var c = 0;
        for (var lvl = (int)SiliconAlleyAttention.Level.Danger; lvl >= (int)SiliconAlleyAttention.Level.Warn; lvl--)
            for (var i = 0; i < count; i++)
            {
                if ((int)infos[i].Level != lvl)
                    continue;
                var chip = EnsureChip(c++);
                chip.Root.SetActive(true);
                chip.Key = keys[i];
                chip.Image.color = lvl == (int)SiliconAlleyAttention.Level.Danger
                    ? SiliconAlleyTheme.Danger : SiliconAlleyTheme.Warn;
                chip.Label.text = Compose("siliconalley:dash_strip_chip",
                    ("studio", regs[i].GetDisplayName()),
                    ("reason", SiliconAlleyAttention.ReasonText(infos[i])));
            }
        for (; c < _chips.Count; c++)
            _chips[c].Root.SetActive(false);
        _chipsRow.SetActive(needing > 0);

        var upkeep = SiliconAlleyOfficeSimulator.ServerUpkeepPerDay(servers);
        _totals.text = Compose("siliconalley:dash_strip_totals",
            ("studios", count.ToString(CultureInfo.InvariantCulture)),
            ("support", Money(support) + "/day"),
            ("installed", installed.ToString("N0", CultureInfo.InvariantCulture)),
            ("servers", servers.ToString(CultureInfo.InvariantCulture)),
            ("upkeep", Money(upkeep) + "/day"));
        _serversHint.gameObject.SetActive(servers == 0);
    }

    // A chip = a MakeChip pill made clickable (the MakeCardItem recipe at pill scale). Built once,
    // re-bound each Fill; chips squash to the pill's 44px floor + ellipsis under pressure (accepted at
    // realistic studio counts — the cards below carry the full story).
    private Chip EnsureChip(int index)
    {
        while (index >= _chips.Count)
        {
            var chip = new Chip();
            var img = MakeChip(_chipsRow.transform, SiliconAlleyTheme.Danger, SiliconAlleyTheme.Text, out var label);
            img.raycastTarget = true; // MakeChip defaults off — this pill IS a button
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = SiliconAlleyTheme.Interaction;
            btn.onClick.AddListener(() => _onOpen?.Invoke(chip.Key));
            img.gameObject.AddComponent<SiliconAlleyHoverScale>().Gate = btn;
            chip.Root = img.gameObject;
            chip.Image = img;
            chip.Label = label;
            _chips.Add(chip);
        }
        return _chips[index];
    }
}

// Issue #148: one grid CELL = a studio card + that studio's server group card stacked in a half-width
// column of a MakeColumns row. Cells are built at pool-grow time ONLY (#147: the value path never
// re-parents); the cell at slot s is BOUND each tick to whichever studio sorts s-th. The stacked
// placement (group directly under its studio, same column) IS the visible studio↔servers link the
// acceptance asks for — the old disconnected Servers block below the cards is gone.
sealed class SiliconAlleyHubCell
{
    public GameObject Root; // the half-width column inside a MakeColumns row
    public SiliconAlleyStudioCard Studio;
    public SiliconAlleyServerGroupCard Servers;

    public static SiliconAlleyHubCell Build(Transform columnsRow, Action<string> onOpen)
    {
        var c = new SiliconAlleyHubCell();
        c.Root = MakeSection(columnsRow);
        c.Studio = SiliconAlleyStudioCard.Build(c.Root.transform, onOpen);
        c.Servers = SiliconAlleyServerGroupCard.Build(c.Root.transform);
        return c;
    }

    public void Fill(BuildingRegistration reg, string key, in SiliconAlleyAttention.Info attn)
    {
        Studio.Root.SetActive(true);
        Studio.Fill(reg, key, attn);
        Servers.Root.SetActive(attn.ServerCount > 0); // a serverless studio has no group card at all
        if (attn.ServerCount > 0)
            Servers.Fill(reg, key);
    }

    // Empty this cell but leave Root's active state to the caller: the odd-count FILLER cell must stay
    // ACTIVE (empty) — MakeColumns force-expands children, so a row with one active child would stretch
    // the lone card to full width.
    public void Hide()
    {
        Studio.Root.SetActive(false);
        Servers.Root.SetActive(false);
    }
}

// Issue #59 (now hosted by the #127 hub): one pooled card per player-owned studio — type icon + name +
// attention badge + a colour-coded demand-trend pill, the product being built, a stage progress bar and
// the key stats at a glance. #148: the WHOLE card is the deep-link into the studio's detail view.
// Build() runs once; Fill() re-reads live state each refresh tick.
sealed class SiliconAlleyStudioCard
{
    public GameObject Root;
    private string _key;
    private string _typeName; // issue #146: the bound type, so the trend tooltip reads live demand
    private Image _typeIcon;
    private TMP_Text _name;
    private SiliconAlleyUI.Badge _attnBadge; // issue #148: the needs-you badge (Danger/Warn; hidden when quiet)
    private Image _trendChip;
    private TMP_Text _trendLabel;
    private TMP_Text _productText; // issue #148: "{product} · v{n}" — what is being built, not just the studio
    private TMP_Text _phaseText;
    private SiliconAlleyUI.ProgressBar _progress;
    private SiliconAlleyUI.StatRow _quality, _reputation, _installed, _support, _shipEta;

    public static SiliconAlleyStudioCard Build(Transform parent, Action<string> onOpen)
    {
        var c = new SiliconAlleyStudioCard();
        var card = MakeCardPanel(parent, "StudioCard");
        var t = card.transform;

        // Issue #148: the WHOLE card is the open affordance (the MakeCardItem recipe) — the old
        // right-aligned "Open" button is gone. The delegate reads the card's CURRENT bind (_key), so a
        // click always opens whatever the card shows, even right after a re-sort re-binds it.
        var cardImage = card.GetComponent<Image>();
        var button = card.AddComponent<Button>();
        button.targetGraphic = cardImage;
        button.colors = SiliconAlleyTheme.Interaction;
        button.onClick.AddListener(() => onOpen(c._key));
        card.AddComponent<SiliconAlleyHoverScale>().Gate = button; // hover elevation; OnDisable resets (pooled-safe)

        // Header: [type icon] name (grows) [attention badge] [demand-trend pill] — pills hug right.
        var header = MakeRow(t, 8f, 30);
        header.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        c._typeIcon = MakeIcon(header.transform, null, 26f, SiliconAlleyTheme.Text);
        c._name = MakeText(header.transform, "Name", SiliconAlleyTheme.Sizes.Subtitle, TextAnchor.MiddleLeft, FontStyle.Bold);
        c._name.GetComponent<LayoutElement>().flexibleWidth = 1f; // absorb the slack so the pills are pushed right
        c._name.enableWordWrapping = false;                        // #148: a 437px column must never wrap the header
        c._name.overflowMode = TextOverflowModes.Ellipsis;
        c._attnBadge = MakeBadge(header.transform, SiliconAlleyTheme.Danger, SiliconAlleyTheme.Text); // issue #148
        c._trendChip = MakeChip(header.transform, SiliconAlleyTheme.Ok, SiliconAlleyTheme.Text, out c._trendLabel);
        // Issue #146: hovering the ▲/▼ pill explains what the demand trend means (live-evaluated at 1 Hz).
        // #148 known dead zone: the tooltip keeps the pill raycastable, so clicks over the pill don't
        // reach the card button — accepted; the pill reads as its own control.
        SiliconAlleyTooltip.Attach(c._trendChip, () => c.TrendTip());

        // Issue #148: WHAT is being built — product name + version, under the studio name.
        c._productText = MakeText(t, "Product", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        c._productText.color = SiliconAlleyTheme.TextMuted;

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

    public void Fill(BuildingRegistration reg, string key, in SiliconAlleyAttention.Info attn)
    {
        _key = key;
        _typeName = reg.businessTypeName; // issue #146: bind the trend tooltip to this studio's category
        // Issue #148: the needs-you badge — same Info the strip and the sort consumed, so they agree.
        SetBadge(_attnBadge, SiliconAlleyAttention.ReasonText(attn),
            attn.Level == SiliconAlleyAttention.Level.Danger ? SiliconAlleyTheme.Danger : SiliconAlleyTheme.Warn);
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

        // Issue #148: what is being built — product name + version (shared formatter, #144).
        _productText.text = Compose("siliconalley:dash_product_line",
            ("product", ProductDisplayName(key, businessType)),
            ("version", SiliconAlleyState.GetVersion(key).ToString(CultureInfo.InvariantCulture)));

        // Stage + stage-progress bar (issue #88: an idle studio reads "Idle", not the derived phase).
        var stage = SiliconAlleyState.GetStage(key);
        var idle = stage == SiliconAlleyState.ProjectStage.Idle;
        var phaseFrac = idle ? 0f : SiliconAlleyState.PhaseProgressFraction(rawProgress, size);
        _phaseText.text = idle
            ? SiliconAlleyState.StageNameKey(stage).GetLocalization() // bare "Idle" — no "· 0%" noise
            : Compose("siliconalley:dash_phase",
                ("phase", SiliconAlleyState.StageNameKey(stage).GetLocalization()),
                ("progress", Pct(phaseFrac)));
        SetProgress(_progress, phaseFrac);

        // Issue #148 quiet-compact: an Idle studio drops its progress bar + ship ETA, and a "—" quality
        // drops the quality row. Value-driven SetActive — same-value calls are no-ops; real flips are the
        // sim-driven structure changes FollowContentHeight exists for (#147).
        _progress.Root.SetActive(!idle);
        _shipEta.Root.SetActive(!idle);
        var avgQ = SiliconAlleyState.GetAverageQuality(key);
        _quality.Root.SetActive(avgQ >= 0f);

        // Stats (the stems light up if their icon ships; otherwise the row keeps a consistent indent).
        SetStat(_quality, "stat_quality", "siliconalley:dash_lbl_quality",
            SiliconAlleyFormat.Quality(avgQ), SiliconAlleyTheme.Accent);
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
}

// Issue #104 (now hosted by the #127 hub): one pooled group card per studio that owns >=1 server, each
// listing its placed servers with a 3-button role selector + a live counts/economy summary. Only the
// per-server role buttons write save state (SiliconAlleyState.SetServerRole); everything else is a read.
sealed class SiliconAlleyServerGroupCard
{
    public GameObject Root;
    private string _key;                 // issue #147: cached bind so a role click can repaint in place
    private BuildingRegistration _reg;
    private TMP_Text _chipTotal, _chipInfra, _chipBackend, _chipHosting, _chipUnassigned;
    private TMP_Text _economy;
    private GameObject _rowsHost;
    private readonly List<ServerRow> _rows = new List<ServerRow>();
    private readonly List<string> _ids = new List<string>(); // reused scratch: this studio's server ids

    public static SiliconAlleyServerGroupCard Build(Transform parent)
    {
        var c = new SiliconAlleyServerGroupCard();
        var card = MakeCardPanel(parent, "ServerGroupCard");
        var t = card.transform;

        // Issue #148: the group sits directly UNDER its studio's card in the same grid cell — repeating
        // the type icon + studio name here would be exactly the duplicated-info smell the issue bans, so
        // a muted "Servers" caption is the only header.
        var caption = MakeText(t, "ServersCaption", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        caption.color = SiliconAlleyTheme.TextMuted;
        caption.text = "siliconalley:dash_servers_header".GetLocalization();

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
        _key = key;  // issue #147: cache the bind — RefreshRolesInPlace repaints without arguments
        _reg = reg;

        // Stable "Server N" numbering: sort the ids (Dictionary iteration order is not guaranteed).
        _ids.Clear();
        if (reg.itemInstances != null)
            foreach (var pair in reg.itemInstances)
                if (SiliconAlleyOfficeSimulator.IsServerInstance(pair.Value))
                    _ids.Add(pair.Key);
        _ids.Sort(StringComparer.Ordinal);

        var counts = SiliconAlleyState.ServerCountsByRole(key, reg);
        FillCounts(counts);
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
            _rows.Add(ServerRow.Build(_rowsHost.transform, RefreshRolesInPlace));
        return _rows[index];
    }

    // Issue #147: a role click repaints THIS card — row tints, count chips, economy line — instead of
    // triggering the screen-wide Refresh (and its full layout rebuild) for what is a colour change. A
    // role click re-buckets servers but never adds/removes one, so no row shows or hides; the only
    // layout-dirtying writes are the auto-sizing chip texts. Cross-card effects (backend coverage on the
    // studio cards) arrive via the next 1 Hz tick, like any sim-driven value. _reg is re-cached every
    // Fill; a click landing in the second after the building was sold hits ServerCountsByRole's existing
    // stale-id tolerance, same as the row's cached _key/_id always did.
    private void RefreshRolesInPlace()
    {
        if (_key == null || _reg == null)
            return;
        var counts = SiliconAlleyState.ServerCountsByRole(_key, _reg);
        FillCounts(counts);
        FillEconomy(_key, _reg, counts);
        for (var i = 0; i < _ids.Count && i < _rows.Count; i++)
            _rows[i].RefreshRole();
    }

    // The five per-role count chips (split out of Fill so RefreshRolesInPlace can reuse it, #147).
    private void FillCounts(Dictionary<SiliconAlleyState.ServerRole, int> counts)
    {
        _chipTotal.text = Compose("siliconalley:dash_servers_total", ("n", _ids.Count.ToString(CultureInfo.InvariantCulture)));
        _chipInfra.text = Compose("siliconalley:dash_servers_infrastructure", ("n", Num(counts, SiliconAlleyState.ServerRole.Infrastructure)));
        _chipBackend.text = Compose("siliconalley:dash_servers_backend", ("n", Num(counts, SiliconAlleyState.ServerRole.Backend)));
        _chipHosting.text = Compose("siliconalley:dash_servers_hosting", ("n", Num(counts, SiliconAlleyState.ServerRole.Hosting)));
        _chipUnassigned.text = Compose("siliconalley:dash_servers_unassigned", ("n", Num(counts, SiliconAlleyState.ServerRole.Unassigned)));
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
        // Issue #148: BUTTON labels only — at the 437px grid column each role button gets ~107px, which
        // "Infrastructure" cannot fit beside its icon. RoleKeys keeps driving the icons + semantics.
        private static readonly string[] ShortRoleKeys =
        {
            "siliconalley:server_role_infrastructure_short",
            "siliconalley:server_role_backend_short",
            "siliconalley:server_role_hosting_short"
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
                var btn = MakeButton(row.transform, ShortRoleKeys[i].GetLocalization(), () => r.OnRole(role));
                SetButtonIcon(btn, SiliconAlleyTheme.IconFor(RoleKeys[i])); // optional art; null-graceful
                var lbl = btn.GetComponentInChildren<TMP_Text>();
                lbl.fontSize = SiliconAlleyTheme.Sizes.Caption; // shrink so the label fits a third-width button
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
            RefreshRole();
        }

        // Recolour the three role buttons from the currently bound server's role (issue #147: also the
        // in-place repaint a role click triggers via the group card — colour-only, layout-inert).
        public void RefreshRole()
        {
            var current = SiliconAlleyState.GetServerRole(_key, _id);
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
            _onChanged?.Invoke(); // issue #147: the GROUP CARD's in-place repaint (was the screen-wide Refresh)
        }
    }
}
