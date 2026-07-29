using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.Items;
using Buildings.BuildingTypes.Shared.Dirtiness; // issue #130: GetCleanliness for the review forecast
using Entities;
using Helpers;
using Localizor;
using TMPro;
using UI.Notification;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static SiliconAlleyUI; // issue #54: Make* helpers now live in the shared styled-component layer
using static SiliconAlleyFormat; // issue #144: ONE format table — no private Money/Pct/Eta re-implementations

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

    // The hotkey that toggles the screen (legacy Input; ProjectSettings activeInputHandler=2). Rebindable
    // via the options panel (machine-local); KeyChoices is the dropdown's index→KeyCode mapping.
    public static KeyCode ToggleKey = KeyCode.F9;
    public static readonly KeyCode[] KeyChoices =
        { KeyCode.F9, KeyCode.F10, KeyCode.F11, KeyCode.F12, KeyCode.Tab, KeyCode.BackQuote };

    // Issue #147: ONE window width. The old 620/1060 split teleported the window 440px (and re-centred
    // it) whenever a studio entered/left the Design stage; every screen now lays out for this width.
    private const float WindowWidth = 940f;
    private const float MaxHeight = 940f; // window caps here (at the 1080 reference) and scrolls beyond

    // Issue #147: the dragged window position, machine-local (NOT save state — presentation only). The
    // key shape mirrors the game's "m:<modId>:<optionId>" option prefs; the game's Options reset only
    // deletes REGISTERED option ids, so these can't be collaterally cleared. MUST be written/read via
    // UnityEngine.PlayerPrefs — the game DLL declares a global-namespace PlayerPrefs class that shadows
    // Unity's under the plain name.
    private const string PrefWindowX = "m:SiliconAlley:window_x";
    private const string PrefWindowY = "m:SiliconAlley:window_y";

    private static readonly int[] ScopeKinds =
    {
        (int)SiliconAlleyState.ProjectKind.Quick,
        (int)SiliconAlleyState.ProjectKind.Standard,
        (int)SiliconAlleyState.ProjectKind.Ambitious,
    };

    private GameObject _root;
    private RectTransform _windowRt, _contentRt; // window = clamped panel; content = scrollable stack
    private SiliconAlleyWindowDrag _drag;        // issue #147: the title-strip drag handler (owns Clamp)
    private bool _built;
    private bool _visible;
    private bool _suppress;     // ignore control callbacks while we set values programmatically
    private float _refresh;     // seconds until the next live refresh

    private readonly List<string> _studioKeys = new List<string>();
    private string _currentKey;

    // Control references rebuilt once in Build().
    private TMP_Text _titleText, _studioText, _phaseText, _summaryText;
    private Image _typeIcon, _phaseIcon; // issue #55: current business-type + phase icons (next to studio name / phase)
    private GameObject _wizardSection, _developmentSection, _testingSection, _releaseSection;
    // ---- Design wizard (issue #35): a paged Concept → … → Summary flow shown during the Design phase.
    // Pages are shown one at a time; sub-issues (#26 features / #36 tools / #37 OS / #38 market) insert their
    // own page before Summary via _wizardPages, with an IsPresent that returns false until the feature ships
    // (the wizard skips absent pages, so today it reduces to Concept → Summary).
    // Order = canonical wizard step (Concept 0, Features 10, Tools 20, OS 30, Market 40, Summary 100); pages sort
    // by it in RebuildVisiblePages so siblings display in step order regardless of which PR/merge registered them.
    private struct WizardPage { public int Order; public GameObject Root; public Func<bool> IsPresent; public Action Refresh; public string TitleKey; }
    private readonly List<WizardPage> _wizardPages = new List<WizardPage>();
    private readonly List<WizardPage> _visiblePages = new List<WizardPage>(); // present pages, rebuilt per refresh
    private int _wizardPage; // index into _visiblePages
    // Issue #81: the two multi-column phase containers that wrap the folded sub-pages (Features+Tools+Deps;
    // Platforms+Segment). The other two phases (Concept, Summary) are the sub-page roots directly.
    private GameObject _phaseDependencies, _phaseMarket;

    // Issue #56: wizard shell — step indicator (dots + "Step N of M · Title") + fade/scale-pop page transitions.
    private const int WizardStepCapacity = 8;       // dot pool size (>= the max number of wizard pages)
    private const float TransitionDuration = 0.16f;
    private const float ScalePopFrom = 0.96f;
    private GameObject _stepIndicator;              // header + dots, shown above the active page
    private TMP_Text _stepHeaderText;
    private Image[] _stepDots;
    private GameObject _lastShownPage;             // last page displayed, so only real page changes animate
    private CanvasGroup _animCg;                    // page currently fading/scaling in (null = idle)
    private RectTransform _animRt;
    private float _animT;
    // Per-refresh context, set by RefreshWizard so the parameterless page refreshers can read it.
    private BuildingRegistration _ctxReg;
    private BusinessType _ctxBusinessType;
    private float _ctxSize, _ctxProgress, _ctxPerHour;
    // Concept page
    private GameObject _conceptPage;
    private TMP_Text _designQualityText, _leadText, _etaText, _conceptNameText;
    private SiliconAlleyUI.TabBar _scopeTabs; // issue #146: the scope picker as a real segmented control
    private TMP_InputField _productNameInput;
    private Slider _focusSlider;
    // Summary page (placeholder rows today; sub-issues fill them in)
    private GameObject _summaryPage;
    private Image _sumScopeIcon;                       // issue #58: review-card hero scope icon
    private TMP_Text _sumHeroTitle, _sumHeroSub;
    // Issue #87: the review card also states the build-or-buy split (#84) and the market fit (#85/#86).
    private SiliconAlleyUI.StatRow _sumQuality, _sumCoverage, _sumComponents, _sumCost, _sumRoyalty, _sumMarket, _sumFit;
    // Features page (issue #26): a fixed pool of toggle buttons (sized to the largest feature table), relabelled
    // and shown/hidden per business type each refresh; bit i toggles the matching FeatureMask bit.
    private GameObject _featuresPage;
    private SiliconAlleyUI.CardItem[] _featureCards; // issue #57: one card per feature (icon + chips + state)
    private TMP_Text _featuresReadout;
    // Operating-systems page (issue #37): same card-pool pattern as features; bit i toggles a PlatformMask bit.
    private GameObject _platformsPage;
    private SiliconAlleyUI.CardItem[] _platformCards;
    private TMP_Text _platformsReadout;
    // Editors & tools page (issue #36): one CYCLE card per tool (Off → Licensed → Owned); the studio-level
    // OwnedToolsMask + per-project UsedToolsMask back it. Building (own) charges R&D cash via SiliconAlleyMoney.
    private GameObject _toolsPage;
    private SiliconAlleyUI.CardItem[] _toolCards;
    private TMP_Text _toolsReadout;
    // Market page (issue #38): single-select audience segment (Broad/Enterprise/Prosumer/Consumer); shifts
    // the price↔volume tradeoff. Backed by the per-project SegmentId ordinal. Cards stack vertically (#57).
    private GameObject _marketPage;
    private SiliconAlleyUI.CardItem[] _segmentCards;
    private TMP_Text _marketReadout;
    // Dependencies page (issue #39): read-only feature→tool coverage cards (no input — derived from the
    // Features + Tools choices). One card per selected feature, covered/uncovered, + a coverage readout.
    private GameObject _dependenciesPage;
    private SiliconAlleyUI.CardItem[] _depCards;
    private TMP_Text _depReadout;
    // Components page (issue #84): interactive build-or-buy product dependencies (#83). One cycle card per
    // dependency slot — Off → License Vendor A → License Vendor B → Build in-house (owned, reusable). Its own
    // wizard phase (Order 15); mirrors the Tools page and reads/writes the persisted #83 dependency state.
    private GameObject _componentsPage;
    private SiliconAlleyUI.CardItem[] _componentCards;
    private TMP_Text _componentsReadout;
    // Market-targeting (issue #86): per-feature % allocation sliders + a per-aspect demand panel with live fit.
    // Sits atop the Market phase; reads/writes the #85 FeatureWeights + the SiliconAlleyAspects demand/fit model.
    private GameObject _targetingBlock, _allocationPage, _demandPage;
    private TMP_Text _allocHint, _targetingReadout;
    private WeightRow[] _weightRows;
    private DemandRow[] _demandRows;
    // Issue #146: the demand chart — market-wants vs your-allocation as two segmented distribution bars.
    private SiliconAlleyUI.SegmentedBar _demandBar, _allocBar;

    // A pooled per-feature allocation row: [icon] name … slider … %. The slider sets the feature weight (#85).
    private sealed class WeightRow
    {
        public GameObject Root;
        public Image Icon;
        public TMP_Text Label, Pct;
        public Slider Slider;
    }

    // Issue #146: a pooled per-aspect LEGEND row for the demand chart — [colour swatch] name … "wants/you".
    // (The old shape — two stacked mini progress-bars per aspect — became the two segmented bars above it.)
    private sealed class DemandRow
    {
        public GameObject Root;
        public Image Swatch;
        public TMP_Text Name, Lbl;
    }

    // Issue #146: per-aspect chart colours, index-aligned with the aspect catalog and repeated when a type
    // ever grows past them. Amber is deliberately absent (caution-only, #143); Info gets its first consumer.
    private static readonly Color[] AspectColors =
        { SiliconAlleyTheme.Accent, SiliconAlleyTheme.Ok, SiliconAlleyTheme.Info };
    // Read-only recap shown once the concept is locked (no longer editable)
    private GameObject _wizardRecap;
    private TMP_Text _recapText, _recapStatusText;
    // Nav row: Back · Next/Confirm
    private GameObject _wizardNavRow;
    private Button _wizardBackButton, _wizardNextButton;
    private TMP_Text _wizardNextLabel;
    // Idle section (issue #88: no active project — the player starts the next version here)
    private GameObject _idleSection;
    private TMP_Text _idleStatusText, _startLabel;
    private Button _startButton;
    // Footer "Abandon project" escape hatch (issue #113). Shown in any active stage. #146 moved the old
    // two-click arm into the in-canvas confirm modal — Danger styling lives on the modal's confirm button.
    private Button _abandonButton;
    // Development section (issue #60: card + build-progress bar + stat rows)
    private SiliconAlleyUI.ProgressBar _devBuildBar;
    private SiliconAlleyUI.StatRow _devThroughput, _devBuild, _devEta;
    private SiliconAlleyUI.ToggleRow _overtimeToggle; // issue #146: checkbox row (was a recoloured button)
    // Issue #88: the Development push controls — Send to testing (when the build is done) + Release now (anytime)
    private TMP_Text _devStatusText, _toTestLabel, _devReleaseLabel;
    private Button _toTestButton, _devReleaseButton;
    // Testing / Release-gate section (issue #60 card; issue #88 manual release: status line + Release button)
    private SiliconAlleyUI.ProgressBar _testPolishBar;
    private SiliconAlleyUI.StatRow _testBugs, _testStaff;
    private TMP_Text _shipStatusText, _shipLabel;
    private Button _shipButton;
    // Updates section (issue #88: manual post-launch updates for the live catalog — independent of phase)
    private GameObject _updateSection;
    private TMP_Text _updateStatusText, _updateLabel;
    private Button _updateButton;
    // Marketing section (issue #21): shown pre-release (Design→Testing); cash-funded awareness campaign.
    private GameObject _marketingSection;
    private SiliconAlleyUI.StatRow _mktAwareness, _mktHype, _mktSynergy; // #29 synergy row hidden when no agency
    private SiliconAlleyUI.ToggleRow _adSpendToggle; // issue #146: checkbox row (was a recoloured button)
    // Issue #130: the live Press Build timing line + the projected-review rows with the quality-gate readout.
    private TMP_Text _mktTimingText;
    private SiliconAlleyUI.StatRow _devForecast, _testForecast;
    private TMP_Text _devForecastMults, _testForecastMults;
    private Button _pressReleaseButton, _pressBuildButton, _hypeButton;
    private TMP_Text _pressReleaseLabel, _pressBuildLabel, _hypeLabel;
    // Publisher section (issue #17/#22/#23): shown pre-release; sign a publishing deal or watch its countdown.
    // Issue #60: eligible publishers are offer cards; an active deal is a stat-row card with a ship bar.
    private GameObject _publisherSection;
    private TMP_Text _pubNoDealText;
    private SiliconAlleyUI.CardItem[] _publisherCards;
    private GameObject _pubDealCard;
    private SiliconAlleyUI.StatRow _pubDealPublisher, _pubDealDeadline, _pubDealShipEta, _pubDealBonus;
    private SiliconAlleyUI.ProgressBar _pubShipBar;
    // Release section (transient ship report; issue #60: review + freshness bars + stat rows)
    private SiliconAlleyUI.ProgressBar _relReviewBar, _relFreshBar;
    private SiliconAlleyUI.StatRow _relProduct, _relReview, _relQuality, _relRevenue, _relRep, _relSupport, _relPatch;
    // Issue #146: review vs the previous release as a before › after delta (hidden on a debut).
    private GameObject _relReviewDeltaRow;
    private SiliconAlleyUI.DeltaReadout _relReviewDelta;
    // Contract section (issue #27): read-only — shown whenever the studio holds an accepted contract.
    private GameObject _contractSection;
    private SiliconAlleyUI.ProgressBar _contractBar;
    private SiliconAlleyUI.StatRow _contractProgress, _contractDue, _contractPayout;
    // Issue #129 (epic #121): the persistent release-history section — the reload-proof home of the ship
    // report (#122 record extras). Pooled rows, newest first; the newest row carries the multiplier
    // breakdown ("why did I earn this"), rendering "—" for pre-0.5.0 rows that never recorded it.
    private GameObject _historySection, _historyHost;
    private readonly List<HistoryRow> _historyRows = new List<HistoryRow>();
    private const int HistoryMaxRows = 8;

    // A pooled release-history row: [name vN … review badge], a meta caption, and (newest row only) the
    // recorded multiplier breakdown.
    private sealed class HistoryRow
    {
        public GameObject Root;
        public TMP_Text Title, Meta, Mults;
        public SiliconAlleyUI.Badge Review; // issue #146: the standalone-badge API (was a loose MakeChip)
    }

    // Issue #129: the #126 contract staff-split dial (contract-first ‹ … › product-first) + its readout.
    private Slider _contractFocusSlider;
    private TMP_Text _contractFocusText;

    // Issue #128 (epic #121): the milestone decision card — renders whatever #123 milestone window is open
    // for the current studio (stage + progress derived; nothing persisted but the resolved bit). The click
    // handlers route through SiliconAlleyMilestones.TryResolve, which re-validates the window.
    private GameObject _milestoneSection;
    private Image _milestoneIcon;
    private TMP_Text _milestoneTitle, _milestoneDesc, _milestoneCountdown, _msOptASummary, _msOptBSummary;
    private Button _msOptAButton, _msOptBButton;
    private TMP_Text _msOptALabel, _msOptBLabel;
    private TMP_Text _msOptAReason, _msOptBReason; // issue #146: why a paid option is greyed ("You have $X of $Y")
    private int _msSlot = -1; // the slot the card currently shows (what the buttons resolve)
    // Issue #127 (epic #121): hub mode — the old F8 dashboard's content (studio cards + Servers) is this
    // screen's LANDING page. F9/F8 open the hub; a card's "Open" (or a toast deep-link) switches to that
    // studio's detail view, and the header's "‹ Overview" button returns. One menu entry point, clicked
    // through — no separate dashboard window anymore.
    private bool _hubMode;
    private GameObject _hubSection, _hubCardsHost, _hubServersBlock, _hubServersHost;
    private TMP_Text _hubEmptyText, _hubServersEmpty;
    private GameObject _studioRow, _phaseRow; // header rows hidden while the hub shows
    private Button _overviewButton;
    private readonly List<BuildingRegistration> _studioRegs = new List<BuildingRegistration>();
    private readonly List<SiliconAlleyStudioCard> _hubCards = new List<SiliconAlleyStudioCard>();
    private readonly List<SiliconAlleyServerGroupCard> _hubServerCards = new List<SiliconAlleyServerGroupCard>();
    // Issue #148: the per-tick attention pre-pass (index-aligned with _studioRegs) + the sorted binding
    // order — pooled card slot s is BOUND to studio _hubOrder[s]; nothing ever moves siblings (#147).
    private readonly List<SiliconAlleyAttention.Info> _hubInfos = new List<SiliconAlleyAttention.Info>();
    private readonly List<int> _hubOrder = new List<int>();

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

    // Issue #127: open the screen on its hub landing page (the F8 alias and the phone client route here).
    public static void OpenHub()
    {
        if (Instance != null)
            Instance.OpenForHub();
    }

    // Issue #127: the F8 alias's toggle — close when already showing the hub, otherwise land on it (also
    // from the detail view: F8 is "take me to the overview" wherever you are).
    public static void ToggleHub()
    {
        if (Instance == null)
            return;
        if (Instance._visible && Instance._hubMode)
            Instance.Close();
        else
            Instance.OpenForHub();
    }

    private void Update()
    {
        if (Input.GetKeyDown(ToggleKey))
            Toggle();
        else if (_visible && Input.GetKeyDown(KeyCode.Escape))
        {
            // Issue #146: an open confirm modal consumes Esc (cancel); only the NEXT press closes the
            // screen. (The game's own GameManager also sees this Esc — pre-existing, harmless.)
            if (!SiliconAlleyModal.TryHandleEscape())
                Close();
        }

        if (_visible)
        {
            _refresh -= Time.unscaledDeltaTime;
            if (_refresh <= 0f)
            {
                _refresh = 1f;
                Refresh();
            }
            FollowContentHeight(); // #147: value-path window sizing — never forces a layout pass
        }

        // Issue #56: advance the page-transition tween (fade + scale-pop). Layout-safe — alpha/scale never
        // feed the LayoutGroup/ContentSizeFitter, so the wizard height doesn't jitter while it plays.
        if (_animCg != null)
        {
            _animT += Time.unscaledDeltaTime / TransitionDuration;
            var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_animT));
            _animCg.alpha = t;
            var s = Mathf.Lerp(ScalePopFrom, 1f, t);
            _animRt.localScale = new Vector3(s, s, 1f);
            if (_animT >= 1f)
            {
                _animCg.alpha = 1f;
                _animRt.localScale = Vector3.one;
                _animCg = null;
            }
        }
    }

    private void Toggle()
    {
        if (_visible)
            Close();
        else
            OpenForHub(); // issue #127: the hotkey lands on the hub — click through to a studio from there
    }

    // Open the screen focused on a specific studio's detail view (toast deep-links and hub cards route here).
    private void OpenFor(string key)
    {
        EnsureBuilt();
        _windowRt.anchoredPosition = _drag.Clamp(_windowRt.anchoredPosition); // #147: repair a stale position
        PopulateStudios();
        _hubMode = false; // issue #127: an explicit studio ask goes straight to detail, not the hub
        if (!string.IsNullOrEmpty(key) && _studioKeys.Contains(key))
            _currentKey = key;
        else if (string.IsNullOrEmpty(_currentKey) || !_studioKeys.Contains(_currentKey))
            _currentKey = _studioKeys.Count > 0 ? _studioKeys[0] : null;

        _root.SetActive(true);
        _visible = true;
        _refresh = 1f;
        _wizardPage = 0; // always open the wizard at the first page
        RefreshStructural(); // issue #147: first layout of the detail view
    }

    // Issue #127: open on the hub landing page.
    private void OpenForHub()
    {
        EnsureBuilt();
        _windowRt.anchoredPosition = _drag.Clamp(_windowRt.anchoredPosition); // #147: repair a stale position
        _hubMode = true;
        _root.SetActive(true);
        _visible = true;
        _refresh = 1f;
        RefreshStructural(); // issue #147: first layout of the hub
    }

    // Issue #127: a hub card's "Open" — switch to that studio's detail view in place.
    private void OpenDetailFromHub(string key)
    {
        _hubMode = false;
        if (_studioKeys.Contains(key))
            _currentKey = key;
        _wizardPage = 0;
        RefreshStructural(); // issue #147: hub → detail swaps the whole section set
    }

    // Issue #127: the header's "‹ Overview" — back to the hub in place.
    private void GoHub()
    {
        _hubMode = true;
        RefreshStructural(); // issue #147: detail → hub swaps the whole section set
    }

    private void Close()
    {
        _visible = false;
        SiliconAlleyModal.CloseAll(); // issue #146: closing the screen never strands an open confirm
        if (_root != null)
            _root.SetActive(false);
    }

    // ---- data --------------------------------------------------------------------------------------

    private void PopulateStudios()
    {
        _studioKeys.Clear();
        _studioRegs.Clear(); // issue #127: the hub cards need the registrations, kept index-aligned
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
        // Issue #127: the hub landing page replaces every per-studio section while it shows.
        if (_hubMode)
        {
            RefreshHub();
            return;
        }
        _hubSection.SetActive(false);
        _studioRow.SetActive(true);
        _phaseRow.SetActive(true);
        _summaryText.gameObject.SetActive(true);
        _overviewButton.gameObject.SetActive(true);

        var reg = FindRegistration(_currentKey);
        if (reg == null)
        {
            _titleText.text = "siliconalley:screen_title_plain".GetLocalization();
            _studioText.text = SiliconAlleyRegistry.NoStudioLocalizationKey(
                "siliconalley:screen_nostudio",
                "siliconalley:screen_registration_failed").GetLocalization();
            _phaseText.text = "";
            _summaryText.text = "";
            _wizardSection.SetActive(false);
            _developmentSection.SetActive(false);
            _testingSection.SetActive(false);
            _milestoneSection.SetActive(false); // issue #128
            _marketingSection.SetActive(false);
            _publisherSection.SetActive(false);
            _releaseSection.SetActive(false);
            _historySection.SetActive(false); // issue #129
            _updateSection.SetActive(false);
            _idleSection.SetActive(false);
            _contractSection.SetActive(false);
            RefreshAbandon(true); // issue #113: no resolvable studio ⇒ nothing to abandon
            return; // #147: the section hides are picked up by FollowContentHeight a frame later
        }

        var businessType = BusinessTypeHelper.GetData(reg);
        var key = _currentKey;
        // Issue #26: note the type so EffectiveProjectSize (just below) and the wizard readouts are feature-aware.
        SiliconAlleyState.NoteBusinessType(key, businessType?.businessTypeName);
        var size = SiliconAlleyState.EffectiveProjectSize(key);
        var rawProgress = SiliconAlleyState.GetProgress(key);
        var phase = SiliconAlleyState.PhaseOf(rawProgress, size);
        var perHour = SiliconAlleyOfficeSimulator.CurrentHourlyProgress(reg);

        // Issue #88: the header names the persisted STAGE (Idle / Design / Development / Testing), not the
        // derived phase — an idle studio shows "Idle", not "Design 0%".
        var stage = SiliconAlleyState.GetStage(key);
        var idle = stage == SiliconAlleyState.ProjectStage.Idle;
        var stageName = SiliconAlleyState.StageNameKey(stage).GetLocalization();

        _titleText.text = Compose("siliconalley:screen_title", ("phase", stageName));
        _studioText.text = Compose("siliconalley:screen_studio",
            ("business", reg.GetDisplayName()), ("product", ProductDisplayName(key, businessType)));
        // Issue #55: reflect the current business type + phase as icons next to their labels (none when idle).
        SetIconSprite(_typeIcon, SiliconAlleyTheme.IconFor(businessType?.businessTypeName));
        SetIconSprite(_phaseIcon, idle ? null : SiliconAlleyTheme.IconFor(SiliconAlleyState.PhaseNameKey(phase)));

        if (idle)
        {
            _phaseText.text = stageName; // "Idle"
            _summaryText.text = "siliconalley:screen_idle_summary".GetLocalization();
        }
        else
        {
            _phaseText.text = Compose("siliconalley:screen_phase",
                ("phase", stageName), ("progress", Pct(SiliconAlleyState.PhaseProgressFraction(rawProgress, size))));
            var avgQ = SiliconAlleyState.GetAverageQuality(key);
            // A product parked at its stage ceiling is done for this stage — show that instead of an ETA.
            var shipEta = rawProgress >= size
                ? "siliconalley:screen_ready_short".GetLocalization()
                : Eta(size - rawProgress, perHour);
            _summaryText.text = Compose("siliconalley:screen_summary",
                ("quality", Quality(avgQ)),
                ("shipeta", shipEta));
        }

        // Issue #88: section visibility follows the persisted STAGE — the studio sits Idle until the player
        // starts a project, then parks at each stage until they push it forward.
        var inDesign = stage == SiliconAlleyState.ProjectStage.Design;
        var inDevelopment = stage == SiliconAlleyState.ProjectStage.Development;
        var inTesting = stage == SiliconAlleyState.ProjectStage.Testing;
        _idleSection.SetActive(idle);
        _wizardSection.SetActive(inDesign);
        _developmentSection.SetActive(inDevelopment);
        _testingSection.SetActive(inTesting);
        if (idle)
            RefreshIdle(key);
        else if (inDesign)
            RefreshWizard(reg, businessType, key, size, rawProgress, perHour);
        else if (inDevelopment)
            RefreshDevelopment(reg, businessType, key, size, rawProgress, perHour);
        else if (inTesting)
            RefreshTesting(reg, businessType, key, perHour, rawProgress >= size);

        // Issue #128: the milestone decision card — visible while a #123 window is open for this stage.
        // Pending is derived every tick, so the card expires (and the neutral auto-resolve happens) even
        // while the screen sits open.
        var msSlot = -1;
        var msEvt = default(SiliconAlleyMilestones.MilestoneEvent);
        var msPending = !idle && SiliconAlleyMilestones.TryGetPending(key, stage, rawProgress, size, out msSlot, out msEvt);
        _milestoneSection.SetActive(msPending);
        if (msPending)
            RefreshMilestone(reg, key, msSlot, msEvt, size);

        // Marketing (issue #21) + Publisher deal (issue #17/#22/#23): pre-release campaign blocks — visible on
        // an ACTIVE project (Design/Development/Testing); hidden when idle (nothing to market). Issue #35:
        // while the concept wizard is still editable they stay hidden so the wizard is a focused flow; they
        // appear once the concept is locked. CanEditConcept is true only in the Design phase.
        var preRelease = inDesign || inDevelopment || inTesting;
        var showCampaign = preRelease && !SiliconAlleyState.CanEditConcept(key);
        _marketingSection.SetActive(showCampaign);
        if (showCampaign)
            RefreshMarketing(reg, key, rawProgress, size);

        _publisherSection.SetActive(showCampaign);
        if (showCampaign)
            RefreshPublisher(reg, businessType, key, rawProgress, size, perHour);

        // Release "ship report" shows independently of the current phase whenever a recent ship exists.
        var report = SiliconAlleyState.GetLastShip(key);
        _releaseSection.SetActive(report.Has);
        if (report.Has)
            RefreshRelease(reg, businessType, key, report);

        // Issue #129: the persistent release history — unlike the transient report above, this survives a
        // reload (backed by the #78 release rows + the #122 multiplier extras).
        RefreshHistory(businessType, key);

        // Issue #88 (manual updates): the live catalog's post-launch update gate — independent of the current
        // project's phase. Shown whenever an update is due (installed base + the patch timer); the button is
        // gated on staffing inside RefreshUpdate (an update is dev work).
        var updateDue = SiliconAlleyOfficeSimulator.IsUpdateDue(key);
        _updateSection.SetActive(updateDue);
        if (updateDue)
            RefreshUpdate(reg, businessType, key, CountStaff(reg) > 0);

        // Contract (issue #27): shown whenever the studio holds a contract (it diverts staff from the product).
        var onContract = SiliconAlleyState.HasContract(key);
        _contractSection.SetActive(onContract);
        if (onContract)
            RefreshContract(key);

        // Issue #113: the Abandon escape hatch — offered in every active stage, hidden while Idle.
        RefreshAbandon(idle);
        // #147: no layout clamp here — the value path never forces a rebuild. Structure changes reach
        // RebuildLayout via RefreshStructural (clicks) or FollowContentHeight (sim-driven flips).
    }

    // Issue #127: the hub landing page — studio cards + the Servers section (the old F8 dashboard content),
    // pooled and re-filled each refresh tick exactly as the standalone window did. Hides the detail header
    // rows and every per-studio section; a card's "Open" switches to that studio's detail in place.
    private void RefreshHub()
    {
        PopulateStudios(); // the count can change while open (a studio bought/sold)
        _titleText.text = "siliconalley:dash_title".GetLocalization();
        _studioRow.SetActive(false);
        _phaseRow.SetActive(false);
        _summaryText.gameObject.SetActive(false);
        _overviewButton.gameObject.SetActive(false); // already home
        _idleSection.SetActive(false);
        _wizardSection.SetActive(false);
        _developmentSection.SetActive(false);
        _testingSection.SetActive(false);
        _milestoneSection.SetActive(false); // issue #128
        _marketingSection.SetActive(false);
        _publisherSection.SetActive(false);
        _releaseSection.SetActive(false);
        _historySection.SetActive(false); // issue #129
        _updateSection.SetActive(false);
        _contractSection.SetActive(false);
        RefreshAbandon(true); // nothing to abandon from the overview
        _hubSection.SetActive(true);

        var count = _studioRegs.Count;
        _hubEmptyText.text = SiliconAlleyRegistry.NoStudioLocalizationKey(
            "siliconalley:dash_empty",
            "siliconalley:dash_registration_failed").GetLocalization();
        _hubEmptyText.gameObject.SetActive(count == 0);

        // Issue #148 pre-pass: ONE attention/totals computation per studio, BEFORE any card binds — the
        // triage strip renders above the cards and must show this tick's figures, not last tick's.
        _hubInfos.Clear();
        for (var i = 0; i < count; i++)
            _hubInfos.Add(SiliconAlleyAttention.Compute(_studioRegs[i], _studioKeys[i]));

        // Sorted BINDING order (#147: pooled slots never move — slot s is simply bound to the studio that
        // sorts s-th): Danger, then Warn, then quiet; stable by original index within each tier. When a
        // severity flips mid-hover the card under the cursor re-binds to a different studio — accepted:
        // the click delegate reads the card's CURRENT bind, so a click always opens what the card shows.
        _hubOrder.Clear();
        for (var lvl = (int)SiliconAlleyAttention.Level.Danger; lvl >= (int)SiliconAlleyAttention.Level.None; lvl--)
            for (var i = 0; i < count; i++)
                if ((int)_hubInfos[i].Level == lvl)
                    _hubOrder.Add(i);

        for (var slot = 0; slot < count; slot++)
        {
            var i = _hubOrder[slot];
            var card = EnsureHubCard(slot);
            card.Root.SetActive(true);
            card.Fill(_studioRegs[i], _studioKeys[i], _hubInfos[i]);
        }
        for (var i = count; i < _hubCards.Count; i++)
            _hubCards[i].Root.SetActive(false);

        RefreshHubServers(count);
        // #147: no layout clamp here — see Refresh()'s tail note.
    }

    private SiliconAlleyStudioCard EnsureHubCard(int index)
    {
        while (index >= _hubCards.Count)
            _hubCards.Add(SiliconAlleyStudioCard.Build(_hubCardsHost.transform, OpenDetailFromHub));
        return _hubCards[index];
    }

    // Issue #104 (hub-hosted): per-studio server role assignment. The group pool is index-aligned with the
    // studio pool; a card self-hides when its studio owns no servers, and the empty hint shows when all do.
    private void RefreshHubServers(int count)
    {
        _hubServersBlock.SetActive(count > 0);
        if (count == 0)
            return;
        var totalServers = 0;
        for (var i = 0; i < count; i++)
        {
            var group = EnsureHubServerCard(i);
            var n = group.Fill(_studioRegs[i], _studioKeys[i]);
            group.Root.SetActive(n > 0);
            totalServers += n;
        }
        for (var i = count; i < _hubServerCards.Count; i++)
            _hubServerCards[i].Root.SetActive(false);
        _hubServersEmpty.gameObject.SetActive(totalServers == 0);
    }

    private SiliconAlleyServerGroupCard EnsureHubServerCard(int index)
    {
        while (index >= _hubServerCards.Count)
            _hubServerCards.Add(SiliconAlleyServerGroupCard.Build(_hubServersHost.transform)); // #147: role clicks repaint in place
        return _hubServerCards[index];
    }

    // Issue #128: fill the decision card from the pending milestone's deterministic event. Paid options show
    // their cost in the label (from the catalog's locale strings) and disable when the player can't pay.
    private void RefreshMilestone(BuildingRegistration reg, string key, int slot,
        SiliconAlleyMilestones.MilestoneEvent evt, float size)
    {
        _msSlot = slot;
        SetIconSprite(_milestoneIcon, SiliconAlleyTheme.IconFor("ms_" + evt.Id)); // drop-in art; null-graceful
        _milestoneTitle.text = evt.TitleKey.GetLocalization();
        _milestoneDesc.text = evt.DescKey.GetLocalization();
        FillMilestoneOption(_msOptAButton, _msOptALabel, _msOptASummary, _msOptAReason, evt.OptionA, reg);
        FillMilestoneOption(_msOptBButton, _msOptBLabel, _msOptBSummary, _msOptBReason, evt.OptionB, reg);
        _milestoneCountdown.text = Compose("siliconalley:ms_countdown",
            ("pct", Pct(SiliconAlleyMilestones.CloseProgress(slot, size) / Mathf.Max(1f, size))));
    }

    private static void FillMilestoneOption(Button button, TMP_Text label, TMP_Text summary, TMP_Text reason,
        SiliconAlleyMilestones.MilestoneOption option, BuildingRegistration reg)
    {
        label.text = option.LabelKey.GetLocalization();
        summary.text = option.SummaryKey.GetLocalization();
        // Issue #146: a greyed paid option now SAYS why — the exact shortfall — instead of staying silent.
        var affordable = option.Cost <= 0f || SiliconAlleyMoney.CanAfford(reg, option.Cost);
        SetDisabledReason(button, reason, affordable,
            affordable ? null : Compose("siliconalley:ui_disabled_funds",
                ("have", Money(SaveGameManager.Current.Money)),
                ("need", Money(option.Cost))));
    }

    // Issue #128: resolve the shown slot with the clicked option. TryResolve re-validates the window (the
    // card may have expired between the render and the click) and charges paid options itself.
    private void OnMilestoneOption(int optionIndex)
    {
        if (_msSlot < 0 || string.IsNullOrEmpty(_currentKey))
            return;
        var reg = FindRegistration(_currentKey);
        if (reg == null)
            return;
        SiliconAlleyMilestones.TryResolve(_currentKey, _msSlot, optionIndex, reg);
        RefreshStructural(); // reflect the resolution (or the expiry) immediately — the card disappears (#147)
    }

    // Issue #113: the footer "Abandon project" button. Hidden when the studio is Idle (there is nothing to
    // abandon, and the Idle card's "Start new project" is the right action). #146: a plain Slate trigger —
    // the destructive confirm (and its Danger styling) moved into the SiliconAlleyModal dialog.
    private void RefreshAbandon(bool idle)
    {
        if (_abandonButton == null)
            return;
        _abandonButton.gameObject.SetActive(!idle);
    }

    // Issue #147: the STRUCTURE path — for every click/navigation that swaps view modes, wizard pages,
    // stages, or whole sections (anything that adds/removes/reflows blocks of content), where the window
    // must resize the same frame. The 1 Hz tick and value-only clicks use plain Refresh() (the VALUE
    // path), which never forces a synchronous layout pass — FollowContentHeight picks up whatever the
    // engine's own end-of-frame layout produces.
    private void RefreshStructural()
    {
        Refresh();
        RebuildLayout();
    }

    // Issue #147: the ONE forced-synchronous layout rebuild, run only on real structure changes (never
    // the 1 Hz tick — that was the old per-second "breathing"). Captures and restores the scroll offset
    // across the rebuild: the content pivot is top, so anchoredPosition.y IS the pixel scroll offset,
    // and the Clamped ScrollRect re-clamps any overshoot in its LateUpdate — shrinking content lands at
    // the nearest valid offset instead of jumping to a stale one.
    private void RebuildLayout()
    {
        if (_contentRt == null || _windowRt == null)
            return;
        var scrollY = _contentRt.anchoredPosition.y;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRt);
        _windowRt.sizeDelta = new Vector2(WindowWidth, Mathf.Min(_contentRt.rect.height, MaxHeight));
        _contentRt.anchoredPosition = new Vector2(_contentRt.anchoredPosition.x, scrollY);
    }

    // Issue #147: the VALUE path's window sizing. Reads the content height Unity's own end-of-frame
    // layout produced LAST frame and follows it — no ForceUpdateCanvases, no ForceRebuildLayoutImmediate,
    // ever. Catches every sim-driven section flip (ship report appearing, a milestone window opening or
    // expiring, update-due, a contract landing) one frame late, which is invisible; the epsilon keeps
    // steady-state writes at zero (inside the scrollbar auto-hide's 4px/1px hysteresis), and the constant
    // width means content height can't feed back into itself — no oscillation.
    private const float HeightFollowEpsilon = 1f;

    private void FollowContentHeight()
    {
        if (_windowRt == null || _contentRt == null)
            return;
        var target = Mathf.Min(_contentRt.rect.height, MaxHeight);
        if (Mathf.Abs(_windowRt.sizeDelta.y - target) > HeightFollowEpsilon)
            _windowRt.sizeDelta = new Vector2(WindowWidth, target);
    }

    // Issue #35: drive the Design-phase wizard. While the concept is editable, show one page at a time with
    // Back / Next-Confirm nav; once locked, replace the flow with a read-only recap (the campaign blocks
    // reappear at that point — see Refresh). Page refreshers are parameterless, so stash the context here.
    private void RefreshWizard(BuildingRegistration reg, BusinessType businessType, string key, float size, float rawProgress, float perHour)
    {
        _ctxReg = reg;
        _ctxBusinessType = businessType;
        _ctxSize = size;
        _ctxProgress = rawProgress;
        _ctxPerHour = perHour;

        if (!SiliconAlleyState.CanEditConcept(key))
        {
            foreach (var page in _wizardPages) // the recap replaces the flow: every page hides
                page.Root.SetActive(false);
            _stepIndicator.SetActive(false);
            _wizardNavRow.SetActive(false);
            _wizardRecap.SetActive(true);
            RefreshRecap(key);
            return;
        }

        _wizardRecap.SetActive(false);
        _wizardNavRow.SetActive(true);
        _stepIndicator.SetActive(true);
        RebuildVisiblePages();
        if (_visiblePages.Count == 0)
        {
            foreach (var page in _wizardPages)
                page.Root.SetActive(false);
            return;
        }
        _wizardPage = Mathf.Clamp(_wizardPage, 0, _visiblePages.Count - 1);
        var current = _visiblePages[_wizardPage];
        // Issue #147: compute the target page FIRST and hide only the OTHERS — the active page must never
        // see a same-frame off/on. The old hide-all-then-reshow bounced the whole visible subtree through
        // OnDisable/OnEnable every 1 Hz tick (all its Graphics re-dirtied, hover scales reset, two layout
        // dirties per second). Iterating _wizardPages (not _visiblePages) still hides a page whose
        // IsPresent flipped false.
        foreach (var page in _wizardPages)
            if (page.Root != current.Root)
                page.Root.SetActive(false);
        if (!current.Root.activeSelf)
            current.Root.SetActive(true);
        current.Refresh();
        UpdateStepIndicator(current);
        if (current.Root != _lastShownPage) // only real page changes animate (not the 1s same-page refresh)
        {
            StartPageTransition(current.Root);
            _lastShownPage = current.Root;
        }

        var isLast = _wizardPage >= _visiblePages.Count - 1;
        _wizardBackButton.interactable = _wizardPage > 0;
        _wizardNextLabel.text = (isLast ? "siliconalley:wiz_startdev" : "siliconalley:wiz_next").GetLocalization();
    }

    // Issue #81: the Dependencies phase folds Features + Tools + Coverage into columns. Each column self-hides
    // when its content is absent (a type with no tools, or no features selected yet) so there's no empty
    // column, then defers to the existing sub-page refreshers.
    private void RefreshDependenciesPhase()
    {
        var hasFeatures = SiliconAlleyFeatures.FeaturesFor(_ctxBusinessType?.businessTypeName).Length > 0;
        var hasTools = SiliconAlleyTools.ToolsFor(_ctxBusinessType?.businessTypeName).Length > 0;
        var hasDeps = SiliconAlleyState.GetFeatureMask(_currentKey) != 0;
        _featuresPage.SetActive(hasFeatures);
        _toolsPage.SetActive(hasTools);
        _dependenciesPage.SetActive(hasDeps);
        if (hasFeatures) RefreshFeaturesPage();
        if (hasTools) RefreshToolsPage();
        if (hasDeps) RefreshDependenciesPage();
    }

    // Issue #81: the Market phase folds Platforms + Segment into columns (segments are universal). Issue #86:
    // the market-targeting block (per-feature % sliders + a per-aspect demand panel with live fit) sits on top.
    private void RefreshMarketPhase()
    {
        var type = _ctxBusinessType?.businessTypeName;
        var hasPlatforms = SiliconAlleyPlatforms.PlatformsFor(type).Length > 0;
        _platformsPage.SetActive(hasPlatforms);
        if (hasPlatforms) RefreshPlatformsPage();
        RefreshMarketPage();
        // Issue #86: market targeting is present when the type has features to allocate across.
        var hasFeatures = SiliconAlleyFeatures.FeaturesFor(type).Length > 0;
        _targetingBlock.SetActive(hasFeatures);
        if (hasFeatures)
        {
            RefreshAllocationPage();
            RefreshDemandPage();
        }
    }

    // Issue #86: a per-feature allocation row — [icon] name + a 0..1 emphasis slider + a normalized % readout.
    // The slider maps to the #85 feature weight (0..1 ↔ weight 0..2, so neutral 1.0 sits at the 0.5 midpoint).
    private WeightRow BuildWeightRow(Transform parent, int slot)
    {
        var row = MakeRow(parent, 8f, 26);
        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.childForceExpandWidth = false;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        var icon = MakeIcon(row.transform, null, 18f);
        var label = MakeText(row.transform, "WLabel", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        FixWidth(label, 118f);
        var slider = MakeSlider(row.transform); // flexibleWidth 1 from MakeSlider
        slider.onValueChanged.AddListener(v => OnWeightChanged(slot, v));
        var pct = MakeText(row.transform, "WPct", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        pct.alignment = TextAlignmentOptions.Right;
        pct.color = SiliconAlleyTheme.TextMuted;
        FixWidth(pct, 44f);
        return new WeightRow { Root = row, Icon = icon, Label = label, Slider = slider, Pct = pct };
    }

    // Issue #86 → #146: a per-aspect legend row under the demand chart — the swatch ties the name to its
    // segment colour in both bars; the label keeps the exact wants/you percentages.
    private DemandRow BuildDemandRow(Transform parent, int index)
    {
        var row = MakeRow(parent, 6f, 20);
        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.childForceExpandWidth = false;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        var swatch = MakeImage(row.transform, "Swatch", AspectColors[index % AspectColors.Length]);
        swatch.raycastTarget = false;
        ApplyPill(swatch, 10f);
        var sle = swatch.gameObject.AddComponent<LayoutElement>();
        sle.minWidth = sle.preferredWidth = 10f;
        sle.minHeight = sle.preferredHeight = 10f;
        sle.flexibleWidth = 0f;
        var name = MakeText(row.transform, "AName", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft, FontStyle.Bold);
        name.GetComponent<LayoutElement>().flexibleWidth = 1f;
        var lbl = MakeText(row.transform, "ALbl", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        lbl.alignment = TextAlignmentOptions.Right;
        lbl.color = SiliconAlleyTheme.TextMuted;
        lbl.enableWordWrapping = false;
        FixWidth(lbl, 150f);
        return new DemandRow { Root = row, Swatch = swatch, Name = name, Lbl = lbl };
    }

    // Issue #86: the per-feature % sliders. Show a row per SELECTED feature; set each slider from the stored
    // weight (suppressing the write-back) and a normalized % readout. A hint shows until 2+ features are picked.
    private void RefreshAllocationPage()
    {
        var key = _currentKey;
        var type = _ctxBusinessType?.businessTypeName;
        var feats = SiliconAlleyFeatures.FeaturesFor(type);
        var mask = SiliconAlleyState.GetFeatureMask(key);
        var totalW = 0f;
        var selected = 0;
        foreach (var f in feats)
            if ((mask & (1 << f.Bit)) != 0)
            {
                totalW += SiliconAlleyState.GetFeatureWeight(key, f.Bit);
                selected++;
            }
        _suppress = true; // setting slider values must not write back through OnWeightChanged
        for (var i = 0; i < _weightRows.Length; i++)
        {
            var row = _weightRows[i];
            var has = i < feats.Length && (mask & (1 << feats[i].Bit)) != 0;
            row.Root.SetActive(has);
            if (!has)
                continue;
            var f = feats[i];
            var w = SiliconAlleyState.GetFeatureWeight(key, f.Bit);
            SetIconSprite(row.Icon, SiliconAlleyTheme.IconFor(f.NameKey));
            row.Label.text = f.NameKey.GetLocalization();
            row.Slider.value = Mathf.Clamp01(w / 2f);
            row.Pct.text = Pct(totalW > 0f ? w / totalW : 0f);
        }
        _suppress = false;
        _allocHint.gameObject.SetActive(selected < 2);
        _allocHint.text = "siliconalley:wiz_alloc_hint".GetLocalization();
    }

    // Issue #86: the demand panel + live fit readout. Per aspect: the market's demand share (Ok bar) vs the
    // player's allocation share (Accent bar). The readout shows the fit's effect on revenue + quality (signed,
    // relative to the even allocation, so neutral reads +0%/+0%).
    private void RefreshDemandPage()
    {
        var key = _currentKey;
        var type = _ctxBusinessType?.businessTypeName;
        var day = TimeHelper.CurrentDay;
        var aspects = SiliconAlleyAspects.AspectsFor(type);
        var weights = SiliconAlleyState.GetFeatureWeights(key);
        var mask = SiliconAlleyState.GetFeatureMask(key);
        var demand = SiliconAlleyAspects.DemandProfile(type, day);
        var alloc = SiliconAlleyAspects.AllocationProfile(type, mask, weights);
        // Issue #146: the market's wanted mix vs the player's allocation, one segmented bar each; the
        // legend rows below tie names to segment colours and keep the exact percentages.
        SetSegments(_demandBar, demand, AspectColors);
        SetSegments(_allocBar, alloc, AspectColors);
        for (var i = 0; i < _demandRows.Length; i++)
        {
            var row = _demandRows[i];
            var has = i < aspects.Length;
            row.Root.SetActive(has);
            if (!has)
                continue;
            var wants = demand != null && i < demand.Length ? demand[i] : 0f;
            var you = alloc != null && i < alloc.Length ? alloc[i] : 0f;
            row.Name.text = aspects[i].NameKey.GetLocalization();
            row.Lbl.text = Compose("siliconalley:wiz_demand_row",
                ("wants", Pct(wants)),
                ("you", Pct(you)));
        }
        var market = SiliconAlleyAspects.MarketFitFactor(mask, weights, type, day);
        var quality = SiliconAlleyAspects.QualityFitBonus(mask, weights, type, day);
        _targetingReadout.text = Compose("siliconalley:wiz_targeting_fit",
            ("market", SignedPct(market - 1f)),
            ("quality", SignedPct(quality)));
    }

    // Issue #86: a per-feature allocation slider moved. Resolve the feature bit from the row's slot (only
    // selected features show a row), write the weight (slider 0..1 ↔ weight 0..2), then refresh the targeting
    // display so the % readouts + demand/fit update live. The _suppress guard makes the slider-value set during
    // that refresh a no-op (no write-back loop); we refresh only the targeting block, not the whole wizard.
    private void OnWeightChanged(int slot, float value)
    {
        if (_suppress)
            return;
        var feats = SiliconAlleyFeatures.FeaturesFor(_ctxBusinessType?.businessTypeName);
        if (slot < 0 || slot >= feats.Length)
            return;
        SiliconAlleyState.SetFeatureWeight(_currentKey, feats[slot].Bit, value * 2f);
        RefreshAllocationPage();
        RefreshDemandPage();
    }

    // Filter _wizardPages down to the pages whose feature is present (Concept + Summary today). Sub-issues'
    // pages drop in/out here via their IsPresent, so navigation only ever sees real pages.
    private void RebuildVisiblePages()
    {
        _visiblePages.Clear();
        foreach (var page in _wizardPages)
            if (page.IsPresent == null || page.IsPresent())
                _visiblePages.Add(page);
        _visiblePages.Sort((a, b) => a.Order.CompareTo(b.Order)); // issue #36: show pages in canonical step order
    }

    // ---- Issue #56: wizard step indicator + page transitions ----

    // The step indicator: a centred "Step N of M · Title" header above a row of progress dots. Built once
    // at the top of the wizard (above the active page); updated each refresh by UpdateStepIndicator.
    private void BuildStepIndicator(Transform parent)
    {
        _stepIndicator = MakeSection(parent);
        _stepHeaderText = MakeText(_stepIndicator.transform, "StepHeader", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleCenter, FontStyle.Bold);
        _stepHeaderText.color = SiliconAlleyTheme.Header;

        // A centred row of fixed-size dots. MakeRow force-expands its children, so build the row by hand.
        var dotsGo = new GameObject("StepDots", typeof(RectTransform));
        dotsGo.transform.SetParent(_stepIndicator.transform, false);
        var h = dotsGo.AddComponent<HorizontalLayoutGroup>();
        h.spacing = SiliconAlleyTheme.Space.Small;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleCenter;
        dotsGo.AddComponent<LayoutElement>().minHeight = 16f;

        _stepDots = new Image[WizardStepCapacity];
        for (var i = 0; i < _stepDots.Length; i++)
        {
            var dot = MakeImage(dotsGo.transform, "Dot", SiliconAlleyTheme.Slate);
            ApplyPill(dot, SiliconAlleyTheme.Height.Dot); // true capsule — the active dot widens without corner distortion (#143)
            var le = dot.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = SiliconAlleyTheme.Height.Dot;
            le.minWidth = le.preferredWidth = SiliconAlleyTheme.Height.Dot;
            _stepDots[i] = dot;
        }
    }

    // Header text + dot states for the current visible page (current = bright + wider, done = blended, todo = slate).
    private void UpdateStepIndicator(WizardPage current)
    {
        var count = _visiblePages.Count;
        _stepHeaderText.text = Compose("siliconalley:wiz_step_label",
            ("n", (_wizardPage + 1).ToString(CultureInfo.InvariantCulture)),
            ("m", count.ToString(CultureInfo.InvariantCulture)),
            ("title", current.TitleKey.GetLocalization()));
        for (var i = 0; i < _stepDots.Length; i++)
        {
            var on = i < count;
            _stepDots[i].gameObject.SetActive(on);
            if (!on)
                continue;
            var le = _stepDots[i].GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = i == _wizardPage ? 18f : SiliconAlleyTheme.Height.Dot; // the current step reads as a wider pill
            _stepDots[i].color = i == _wizardPage ? SiliconAlleyTheme.Accent
                : i < _wizardPage ? SiliconAlleyTheme.StepDone
                : SiliconAlleyTheme.Slate;
        }
    }

    // Begin the fade + scale-pop for the page that just became current (advanced each frame in Update).
    private void StartPageTransition(GameObject page)
    {
        _animCg = page.GetComponent<CanvasGroup>();
        _animRt = (RectTransform)page.transform;
        _animT = 0f;
        if (_animCg != null)
            _animCg.alpha = 0f;
        _animRt.localScale = new Vector3(ScalePopFrom, ScalePopFrom, 1f);
    }

    // Concept page: today's scope + focus controls plus read-only product name and baseline readouts.
    private void RefreshConceptPage()
    {
        var key = _currentKey;
        var productName = ProductDisplayName(key, _ctxBusinessType);
        _conceptNameText.text = Compose("siliconalley:wiz_product", ("product", productName));
        if (_productNameInput.text != productName)
        {
            _suppress = true;
            _productNameInput.SetTextWithoutNotify(productName);
            _suppress = false;
        }

        var designQ = SiliconAlleyState.GetPhaseQuality(key, SiliconAlleyState.ProjectPhase.Design);
        _designQualityText.text = Compose("siliconalley:screen_designquality",
            ("value", Quality(designQ)));

        var hasDesigner = _ctxBusinessType != null
            && System.Array.IndexOf(_ctxBusinessType.employeePrimarySkills, "ba:skill_graphicdesigner") >= 0;
        var leadKey = hasDesigner ? "siliconalley:screen_lead_designer" : "siliconalley:screen_lead_programmer";
        _leadText.text = Compose("siliconalley:screen_lead",
            ("lead", leadKey.GetLocalization()), ("staff", CountStaff(_ctxReg).ToString(CultureInfo.InvariantCulture)));

        var remaining = SiliconAlleyState.PhaseEndProgress(SiliconAlleyState.ProjectPhase.Design, _ctxSize) - _ctxProgress;
        _etaText.text = Compose("siliconalley:screen_designeta", ("eta", Eta(remaining, _ctxPerHour)));

        var currentKind = SiliconAlleyState.GetProjectType(key);
        SetTabSelected(_scopeTabs, Array.IndexOf(ScopeKinds, currentKind)); // issue #146: state-driven tab tint

        _suppress = true; // setting the value must not write back through OnFocusChanged
        _focusSlider.value = SiliconAlleyState.GetDesignFocus(key);
        _suppress = false;
    }

    // Features page (issue #26): the current business type's feature list as toggle buttons; each selected
    // feature raises projected size/dev-time and the quality ceiling. The readout updates as bits flip, so the
    // player sees the cost/benefit before committing. Unused slots (types with fewer features) are hidden.
    private void RefreshFeaturesPage()
    {
        var key = _currentKey;
        var feats = SiliconAlleyFeatures.FeaturesFor(_ctxBusinessType?.businessTypeName);
        var mask = SiliconAlleyState.GetFeatureMask(key);
        for (var i = 0; i < _featureCards.Length; i++)
        {
            var c = _featureCards[i];
            var has = i < feats.Length;
            c.Root.SetActive(has);
            if (!has)
                continue;
            var f = feats[i];
            var selected = (mask & (1 << f.Bit)) != 0;
            SetIconSprite(c.Icon, SiliconAlleyTheme.IconFor(f.NameKey)); // #55 icon
            c.Title.text = f.NameKey.GetLocalization();
            SetCardChips(c, // #57: cost/benefit chips
                Compose("siliconalley:wiz_chip_size", ("v", Pct(f.SizeCost))),
                Compose("siliconalley:wiz_chip_ceiling", ("v", Pct(f.QualityContribution))));
            SetCardChecked(c, selected); // issue #146: the checkbox IS the state — no tint, no "Selected" badge
            SetCardBadge(c, null, SiliconAlleyTheme.Accent);
        }
        _featuresReadout.text = Compose("siliconalley:wiz_features_readout",
            ("size", Mathf.RoundToInt(_ctxSize).ToString(CultureInfo.InvariantCulture)),
            ("eta", Eta(_ctxSize - _ctxProgress, _ctxPerHour)),
            ("ceiling", Pct(ProjectedCeiling(key))));
    }

    // A display estimate of the achievable quality ceiling: the design baseline (clamped, so it reads sensibly
    // before any Design work) raised by the selected features (#26) and the tools used (#36). Matches the
    // simulator's DesignQualityCeiling for a real design quality; purely for the wizard preview.
    private float ProjectedCeiling(string key)
    {
        var type = _ctxBusinessType?.businessTypeName;
        var dq = Mathf.Max(0f, SiliconAlleyState.GetPhaseQuality(key, SiliconAlleyState.ProjectPhase.Design));
        var bonus = SiliconAlleyFeatures.QualityBonus(SiliconAlleyState.GetFeatureMask(key), type)
            + SiliconAlleyTools.QualityBonus(SiliconAlleyState.GetUsedToolsMask(key), type)
            + SiliconAlleyState.DependencyQualityBonus(key, type) // issue #84: product-dependency quality (matches DesignQualityCeiling)
            + SiliconAlleyAspects.QualityFitBonus(SiliconAlleyState.GetFeatureMask(key), SiliconAlleyState.GetFeatureWeights(key), type, TimeHelper.CurrentDay); // issue #85: market-fit (0 at neutral)
        var ceiling = Mathf.Min(1f, 0.5f + 0.5f * dq + bonus);
        // Issue #39: uncovered feature→tool dependencies cap the ceiling (full coverage ⇒ no change).
        return Mathf.Min(ceiling, SiliconAlleyDependencies.CoverageCeiling(
            SiliconAlleyState.GetFeatureMask(key), SiliconAlleyState.GetOwnedToolsMask(key),
            SiliconAlleyState.GetUsedToolsMask(key), type));
    }

    // Operating-systems page (issue #37): the current type's platform list as toggles; each selected platform
    // widens launch reach and adds porting work, so the projected size/ETA + reach in the readout move as bits
    // flip. Unused slots (types with fewer platforms) are hidden.
    private void RefreshPlatformsPage()
    {
        var key = _currentKey;
        var type = _ctxBusinessType?.businessTypeName;
        var plats = SiliconAlleyPlatforms.PlatformsFor(type);
        var mask = SiliconAlleyState.GetPlatformMask(key);
        for (var i = 0; i < _platformCards.Length; i++)
        {
            var c = _platformCards[i];
            var has = i < plats.Length;
            c.Root.SetActive(has);
            if (!has)
                continue;
            var p = plats[i];
            var selected = (mask & (1 << p.Bit)) != 0;
            SetIconSprite(c.Icon, SiliconAlleyTheme.IconFor(p.NameKey)); // #55 icon
            c.Title.text = p.NameKey.GetLocalization();
            SetCardChips(c, // #57: cost/benefit chips
                Compose("siliconalley:wiz_chip_reach", ("v", p.ShareWeight.ToString("0.0", CultureInfo.InvariantCulture))),
                Compose("siliconalley:wiz_chip_size", ("v", Pct(p.ScopeCost))));
            SetCardChecked(c, selected); // issue #146: the checkbox IS the state — no tint, no "Selected" badge
            SetCardBadge(c, null, SiliconAlleyTheme.Accent);
        }
        _platformsReadout.text = Compose("siliconalley:wiz_platforms_readout",
            ("market", PlatformMarketText(key, type)),
            ("size", Mathf.RoundToInt(_ctxSize).ToString(CultureInfo.InvariantCulture)),
            ("eta", Eta(_ctxSize - _ctxProgress, _ctxPerHour)));
    }

    // Shared "reach ×N.N · K platform(s)" phrase for the OS page readout and the Summary market row. PlatformMask
    // 0 reads as the single implicit home platform (reach ×1.0 · 1).
    private string PlatformMarketText(string key, string businessTypeName)
    {
        var mask = SiliconAlleyState.GetPlatformMask(key);
        var reach = SiliconAlleyPlatforms.ReachMultiplier(mask, businessTypeName);
        var count = 0;
        foreach (var p in SiliconAlleyPlatforms.PlatformsFor(businessTypeName))
            if ((mask & (1 << p.Bit)) != 0) count++;
        if (count == 0) count = 1; // the implicit single home platform
        return Compose("siliconalley:wiz_market_reach",
            ("reach", reach.ToString("0.0", CultureInfo.InvariantCulture)),
            ("count", count.ToString(CultureInfo.InvariantCulture)));
    }

    // Editors & tools page (issue #36): one cycle button per tool. Off → Licensed (royalty) → Owned (pay R&D
    // cash once, then free). Owned tools toggle in/out of the current product; the bonus + royalty in the readout
    // move as the player cycles. Unused slots (types with fewer tools) are hidden.
    private void RefreshToolsPage()
    {
        var key = _currentKey;
        var type = _ctxBusinessType?.businessTypeName;
        var tools = SiliconAlleyTools.ToolsFor(type);
        for (var i = 0; i < _toolCards.Length; i++)
        {
            var c = _toolCards[i];
            var has = i < tools.Length;
            c.Root.SetActive(has);
            if (!has)
                continue;
            var t = tools[i];
            var owned = SiliconAlleyState.IsToolOwned(key, t.Bit);
            var used = SiliconAlleyState.IsToolUsed(key, t.Bit);
            var quality = Compose("siliconalley:wiz_chip_quality", ("v", Pct(t.QualityBonus)));
            var royalty = Compose("siliconalley:wiz_chip_royalty", ("v", Pct(t.RoyaltyRate)));
            var build = Compose("siliconalley:wiz_chip_build", ("v", Money(t.BuildCost)));
            SetIconSprite(c.Icon, SiliconAlleyTheme.IconFor(t.NameKey)); // #55 icon
            c.Title.text = t.NameKey.GetLocalization();
            if (owned && used) // owned + in this product
            {
                SetCardChips(c, quality);
                c.Card.color = SiliconAlleyTheme.CardSelected;
                SetCardBadge(c, "siliconalley:wiz_state_owned".GetLocalization(), SiliconAlleyTheme.Ok);
            }
            else if (owned) // owned studio asset, not used here
            {
                SetCardChips(c, quality);
                c.Card.color = SiliconAlleyTheme.Card;
                SetCardBadge(c, "siliconalley:wiz_state_owned".GetLocalization(), SiliconAlleyTheme.Slate);
            }
            else if (used) // licensed (royalty)
            {
                SetCardChips(c, quality, royalty, build);
                c.Card.color = SiliconAlleyTheme.CardLicensed;
                SetCardBadge(c, "siliconalley:wiz_state_licensed".GetLocalization(), SiliconAlleyTheme.Warn);
            }
            else // off
            {
                SetCardChips(c, royalty, build);
                c.Card.color = SiliconAlleyTheme.Card;
                SetCardBadge(c, "siliconalley:wiz_state_off".GetLocalization(), SiliconAlleyTheme.Slate);
            }
        }
        _toolsReadout.text = Compose("siliconalley:wiz_tools_readout",
            ("quality", Pct(SiliconAlleyTools.QualityBonus(SiliconAlleyState.GetUsedToolsMask(key), type))),
            ("royalty", Pct(SiliconAlleyState.ToolRoyalty(key, type))),
            ("licensed", LicensedToolCount(key, type).ToString(CultureInfo.InvariantCulture)));
    }

    // Count of licensed tools (used but not owned) on the current project — drives the Summary royalty row.
    private int LicensedToolCount(string key, string businessTypeName)
    {
        var licensed = SiliconAlleyState.GetUsedToolsMask(key) & ~SiliconAlleyState.GetOwnedToolsMask(key);
        var count = 0;
        foreach (var t in SiliconAlleyTools.ToolsFor(businessTypeName))
            if ((licensed & (1 << t.Bit)) != 0) count++;
        return count;
    }

    // Total R&D cash sunk into the owned tools used on this product — drives the Summary up-front-cost row.
    private float OwnedToolsRnd(string key, string businessTypeName)
    {
        var ownedUsed = SiliconAlleyState.GetUsedToolsMask(key) & SiliconAlleyState.GetOwnedToolsMask(key);
        var sum = 0f;
        foreach (var t in SiliconAlleyTools.ToolsFor(businessTypeName))
            if ((ownedUsed & (1 << t.Bit)) != 0) sum += t.BuildCost;
        return sum;
    }

    // Components page (issue #84): one cycle card per product dependency (#83). Off → License Vendor A →
    // License Vendor B → Build in-house (R&D cash, then owned/reusable). Owned deps toggle in/out of the
    // current product. Chips show the licensor/royalty/quality; the readout sums the quality + royalty.
    private void RefreshComponentsPage()
    {
        var key = _currentKey;
        var type = _ctxBusinessType?.businessTypeName;
        var deps = SiliconAlleyProductDependencies.DependenciesFor(type);
        for (var i = 0; i < _componentCards.Length; i++)
        {
            var c = _componentCards[i];
            var has = i < deps.Length;
            c.Root.SetActive(has);
            if (!has)
                continue;
            var d = deps[i];
            var owned = SiliconAlleyState.IsDependencyOwned(key, d.Bit);
            var used = SiliconAlleyState.IsDependencyUsed(key, d.Bit);
            var selfQuality = Compose("siliconalley:wiz_chip_quality", ("v", Pct(d.SelfBuildQuality)));
            var build = Compose("siliconalley:wiz_chip_build", ("v", Money(d.BuildCost)));
            SetIconSprite(c.Icon, SiliconAlleyTheme.IconFor(d.NameKey)); // #55 icon
            c.Title.text = d.NameKey.GetLocalization();
            if (owned && used) // self-built, in this product
            {
                SetCardChips(c, selfQuality);
                c.Card.color = SiliconAlleyTheme.CardSelected;
                SetCardBadge(c, "siliconalley:wiz_state_owned".GetLocalization(), SiliconAlleyTheme.Ok);
            }
            else if (owned) // self-built studio asset, not used here
            {
                SetCardChips(c, selfQuality);
                c.Card.color = SiliconAlleyTheme.Card;
                SetCardBadge(c, "siliconalley:wiz_state_owned".GetLocalization(), SiliconAlleyTheme.Slate);
            }
            else if (used) // licensed from a competitor vendor (royalty)
            {
                var vendorOrdinal = SiliconAlleyState.GetDependencyVendorOrdinal(key, d.Bit);
                var vendorName = SiliconAlleyVendors.TryGetById(vendorOrdinal, out var vendor) ? vendor.NameKey.GetLocalization() : "—";
                var royalty = Compose("siliconalley:wiz_chip_royalty", ("v", Pct(OfferRoyalty(type, d.Bit, vendorOrdinal))));
                var quality = Compose("siliconalley:wiz_chip_quality", ("v", Pct(OfferQuality(type, d.Bit, vendorOrdinal))));
                SetCardChips(c, vendorName, royalty, quality);
                c.Card.color = SiliconAlleyTheme.CardLicensed;
                SetCardBadge(c, "siliconalley:wiz_state_licensed".GetLocalization(), SiliconAlleyTheme.Warn);
            }
            else // off — show the build cost + the cheapest licensing royalty as a hint
            {
                var royaltyHint = Compose("siliconalley:wiz_chip_royalty", ("v", Pct(MinOfferRoyalty(type, d.Bit))));
                SetCardChips(c, build, royaltyHint);
                c.Card.color = SiliconAlleyTheme.Card;
                SetCardBadge(c, "siliconalley:wiz_state_off".GetLocalization(), SiliconAlleyTheme.Slate);
            }
        }
        _componentsReadout.text = Compose("siliconalley:wiz_components_readout",
            ("quality", Pct(SiliconAlleyState.DependencyQualityBonus(key, type))),
            ("royalty", Pct(SiliconAlleyState.DependencyRoyalty(key, type))),
            ("licensed", LicensedDependencyCount(key, type).ToString(CultureInfo.InvariantCulture)));
    }

    // Ordered vendor ordinals offering a dependency slot (catalog order), driving the build-or-buy cycle.
    private static List<int> VendorOrdinalsFor(string businessTypeName, int dependencyBit)
    {
        var list = new List<int>();
        foreach (var o in SiliconAlleyProductDependencies.OffersFor(businessTypeName))
            if (o.DependencyBit == dependencyBit)
                list.Add(o.VendorOrdinal);
        return list;
    }

    private static float OfferRoyalty(string businessTypeName, int dependencyBit, int vendorOrdinal)
        => SiliconAlleyProductDependencies.TryGetOffer(businessTypeName, dependencyBit, vendorOrdinal, out var offer) ? offer.RoyaltyRate : 0f;

    private static float OfferQuality(string businessTypeName, int dependencyBit, int vendorOrdinal)
        => SiliconAlleyProductDependencies.TryGetOffer(businessTypeName, dependencyBit, vendorOrdinal, out var offer) ? offer.QualityBonus : 0f;

    // The cheapest royalty among the slot's vendor offers — the "off"-state licensing hint chip.
    private static float MinOfferRoyalty(string businessTypeName, int dependencyBit)
    {
        var min = float.MaxValue;
        foreach (var o in SiliconAlleyProductDependencies.OffersFor(businessTypeName))
            if (o.DependencyBit == dependencyBit && o.RoyaltyRate < min)
                min = o.RoyaltyRate;
        return min == float.MaxValue ? 0f : min;
    }

    // Count of licensed (used but not self-built) dependencies on the current project — drives the readout
    // and the Summary royalty row alongside the licensed-tool count.
    private int LicensedDependencyCount(string key, string businessTypeName)
    {
        var licensed = SiliconAlleyState.GetUsedDependencyMask(key) & ~SiliconAlleyState.GetOwnedDependencyMask(key);
        var count = 0;
        foreach (var d in SiliconAlleyProductDependencies.DependenciesFor(businessTypeName))
            if ((licensed & (1 << d.Bit)) != 0) count++;
        return count;
    }

    // Issue #87: count of self-built (owned AND used) dependencies on the current project — the other half of
    // the Summary's build-or-buy row. Mirrors LicensedDependencyCount with `used & owned` instead of
    // `used & ~owned`, so the two counts always partition the used mask.
    private int BuiltDependencyCount(string key, string businessTypeName)
    {
        var built = SiliconAlleyState.GetUsedDependencyMask(key) & SiliconAlleyState.GetOwnedDependencyMask(key);
        var count = 0;
        foreach (var d in SiliconAlleyProductDependencies.DependenciesFor(businessTypeName))
            if ((built & (1 << d.Bit)) != 0) count++;
        return count;
    }

    // Total R&D cash sunk into the owned dependencies used on this product — drives the Summary cost row
    // alongside the owned-tool R&D.
    private float OwnedDependenciesRnd(string key, string businessTypeName)
    {
        var ownedUsed = SiliconAlleyState.GetUsedDependencyMask(key) & SiliconAlleyState.GetOwnedDependencyMask(key);
        var sum = 0f;
        foreach (var d in SiliconAlleyProductDependencies.DependenciesFor(businessTypeName))
            if ((ownedUsed & (1 << d.Bit)) != 0) sum += d.BuildCost;
        return sum;
    }

    // Market page (issue #38): single-select audience segment. Recolors the chosen segment (like the scope
    // buttons) and shows its market-size indicator + the price/volume factors it applies.
    private void RefreshMarketPage()
    {
        var key = _currentKey;
        var current = SiliconAlleyState.GetSegmentId(key);
        for (var i = 0; i < _segmentCards.Length; i++)
        {
            var c = _segmentCards[i];
            var s = SiliconAlleySegments.All[i];
            var selected = i == current;
            SetIconSprite(c.Icon, SiliconAlleyTheme.IconFor(s.NameKey)); // #55 icon
            c.Title.text = s.NameKey.GetLocalization();
            SetCardChips(c, // #57: price/volume + market-size chips
                Compose("siliconalley:wiz_chip_price", ("v", s.PriceFactor.ToString("0.0", CultureInfo.InvariantCulture))),
                Compose("siliconalley:wiz_chip_volume", ("v", s.VolumeFactor.ToString("0.0", CultureInfo.InvariantCulture))),
                s.MarketSizeKey.GetLocalization());
            c.Card.color = selected ? SiliconAlleyTheme.CardSelected : SiliconAlleyTheme.Card;
            SetCardBadge(c, selected ? "siliconalley:wiz_state_selected".GetLocalization() : null, SiliconAlleyTheme.Accent);
        }
        _marketReadout.text = SegmentText(current);
    }

    // "Segment · {size} · price ×P / volume ×V" phrase for the Market page readout.
    private string SegmentText(int segmentId)
    {
        var s = SiliconAlleySegments.Get(segmentId);
        return Compose("siliconalley:wiz_market_segment",
            ("segment", s.NameKey.GetLocalization()),
            ("size", s.MarketSizeKey.GetLocalization()),
            ("price", s.PriceFactor.ToString("0.0", CultureInfo.InvariantCulture)),
            ("volume", s.VolumeFactor.ToString("0.0", CultureInfo.InvariantCulture)));
    }

    // Epic #34 capstone: the aggregate reachable-market estimate the Summary commits to — platform reach (#37)
    // × segment volume (#38), the combined multiplier on the launch installed-base jump vs a single-home-platform
    // Broad product (price is the separate revenue axis). Neutral (1 home platform + Broad) ⇒ ~1.0×.
    private string MarketSummaryText(string key, string businessTypeName)
    {
        var reach = SiliconAlleyState.LaunchReach(key, businessTypeName);
        var volume = SiliconAlleyState.SegmentVolumeFactor(key);
        var price = SiliconAlleyState.SegmentPriceFactor(key);
        var segment = SiliconAlleySegments.Get(SiliconAlleyState.GetSegmentId(key)).NameKey.GetLocalization();
        return Compose("siliconalley:wiz_market_estimate",
            ("market", (reach * volume).ToString("0.0", CultureInfo.InvariantCulture)),
            ("reach", reach.ToString("0.0", CultureInfo.InvariantCulture)),
            ("segment", segment),
            ("volume", volume.ToString("0.0", CultureInfo.InvariantCulture)),
            ("price", price.ToString("0.0", CultureInfo.InvariantCulture)));
    }

    // Dependencies page (issue #39, read-only): one row per SELECTED feature, covered/uncovered against the
    // studio's owned + licensed tools. Uncovered features lower the quality ceiling; the readout shows the
    // coverage figure + the resulting cap. No input — derived from the Features (#26) + Tools (#36) choices.
    private void RefreshDependenciesPage()
    {
        var key = _currentKey;
        var type = _ctxBusinessType?.businessTypeName;
        var featureMask = SiliconAlleyState.GetFeatureMask(key);
        var owned = SiliconAlleyState.GetOwnedToolsMask(key);
        var used = SiliconAlleyState.GetUsedToolsMask(key);
        var row = 0;
        foreach (var f in SiliconAlleyFeatures.FeaturesFor(type))
        {
            if ((featureMask & (1 << f.Bit)) == 0 || row >= _depCards.Length)
                continue; // only selected features get a card
            var c = _depCards[row];
            SetIconSprite(c.Icon, SiliconAlleyTheme.IconFor(f.NameKey)); // #55 icon
            c.Title.text = f.NameKey.GetLocalization();
            c.Card.color = SiliconAlleyTheme.Card;
            if (SiliconAlleyDependencies.IsCovered(type, f.Bit, owned, used))
            {
                SetCardChips(c);
                SetCardBadge(c, "siliconalley:wiz_state_covered".GetLocalization(), SiliconAlleyTheme.Ok);
            }
            else
            {
                SetCardChips(c, Compose("siliconalley:wiz_chip_needs", ("tools", ProviderToolNames(type, f.Bit))));
                SetCardBadge(c, "siliconalley:wiz_state_uncovered".GetLocalization(), SiliconAlleyTheme.Warn);
            }
            c.Root.SetActive(true);
            row++;
        }
        for (; row < _depCards.Length; row++)
            _depCards[row].Root.SetActive(false);

        SiliconAlleyDependencies.Coverage(featureMask, owned, used, type, out var covered, out var total);
        _depReadout.text = Compose("siliconalley:wiz_deps_readout",
            ("covered", covered.ToString(CultureInfo.InvariantCulture)),
            ("total", total.ToString(CultureInfo.InvariantCulture)),
            ("ceiling", Pct(ProjectedCeiling(key))));
    }

    // The provider tool name(s) for an uncovered feature — what the player could own/license to cover it.
    private string ProviderToolNames(string businessTypeName, int featureBit)
    {
        var mask = SiliconAlleyDependencies.ProviderMask(businessTypeName, featureBit);
        var names = new List<string>();
        foreach (var t in SiliconAlleyTools.ToolsFor(businessTypeName))
            if ((mask & (1 << t.Bit)) != 0)
                names.Add(t.NameKey.GetLocalization());
        return names.Count > 0 ? string.Join(" / ", names) : "—";
    }

    // Summary page: a read-only review aggregated before commit. Today only scope/ETA and a design-quality
    // baseline are computable; the remaining rows show neutral placeholders that the sub-issues fill in.
    private void RefreshSummaryPage()
    {
        var key = _currentKey;
        var kind = SiliconAlleyState.GetProjectType(key);
        var type = _ctxBusinessType?.businessTypeName;

        // Hero: product name + "scope · size · ship eta", with the per-scope icon.
        _sumHeroTitle.text = ProductDisplayName(key, _ctxBusinessType);
        SetIconSprite(_sumScopeIcon, SiliconAlleyTheme.IconFor(SiliconAlleyState.ProjectTypeNameKey(kind)));
        _sumHeroSub.text = Compose("siliconalley:wiz_sum_scope",
            ("scope", SiliconAlleyState.ProjectTypeNameKey(kind).GetLocalization()),
            ("size", Mathf.RoundToInt(_ctxSize).ToString(CultureInfo.InvariantCulture)),
            ("eta", Eta(_ctxSize - _ctxProgress, _ctxPerHour)));

        // Quality ceiling (features + owned/used tools).
        SetStat(_sumQuality, "stat_quality", "siliconalley:wiz_sum_lbl_quality",
            Pct(ProjectedCeiling(key)), SiliconAlleyTheme.Accent);

        // #39 dependencies: feature→tool coverage (green when full, amber with gaps).
        SiliconAlleyDependencies.Coverage(SiliconAlleyState.GetFeatureMask(key),
            SiliconAlleyState.GetOwnedToolsMask(key), SiliconAlleyState.GetUsedToolsMask(key), type,
            out var covCovered, out var covTotal);
        var covFull = covCovered >= covTotal;
        SetStat(_sumCoverage, "stat_coverage", "siliconalley:wiz_sum_lbl_coverage",
            covFull ? "siliconalley:wiz_coverage_full".GetLocalization()
                    : Compose("siliconalley:wiz_coverage_value",
                        ("covered", covCovered.ToString(CultureInfo.InvariantCulture)),
                        ("total", covTotal.ToString(CultureInfo.InvariantCulture))),
            covFull ? SiliconAlleyTheme.Ok : SiliconAlleyTheme.Warn);

        // Issue #87: the #84 build-or-buy split, stated outright at the commit gate. The two counts partition
        // the used-dependency mask, so "1 built · 2 licensed" reads as the whole stack. Licensed items carry a
        // royalty (amber); an all-self-built stack carries none (green); nothing selected is neutral.
        var builtDeps = BuiltDependencyCount(key, type);
        var licensedDeps = LicensedDependencyCount(key, type);
        SetStat(_sumComponents, "stat_components", "siliconalley:wiz_sum_lbl_components",
            builtDeps + licensedDeps <= 0
                ? "siliconalley:wiz_sum_components_none".GetLocalization()
                : Compose("siliconalley:wiz_sum_components_value",
                    ("built", builtDeps.ToString(CultureInfo.InvariantCulture)),
                    ("licensed", licensedDeps.ToString(CultureInfo.InvariantCulture))),
            builtDeps + licensedDeps <= 0 ? SiliconAlleyTheme.TextMuted
                : licensedDeps > 0 ? SiliconAlleyTheme.Warn : SiliconAlleyTheme.Ok);

        // #36 tools + #84 product dependencies: up-front R&D for owned items + ongoing licensed royalty.
        var ownedRnd = OwnedToolsRnd(key, type) + OwnedDependenciesRnd(key, type);
        SetStat(_sumCost, "stat_cost", "siliconalley:wiz_sum_lbl_cost",
            ownedRnd <= 0f ? "siliconalley:wiz_placeholder_none".GetLocalization()
                           : Compose("siliconalley:wiz_cost_value", ("amount", Money(ownedRnd))),
            ownedRnd <= 0f ? SiliconAlleyTheme.TextMuted : SiliconAlleyTheme.Warn);
        var licensed = LicensedToolCount(key, type) + LicensedDependencyCount(key, type);
        SetStat(_sumRoyalty, "stat_royalty", "siliconalley:wiz_sum_lbl_royalties",
            licensed <= 0 ? "siliconalley:wiz_placeholder_noroyalties".GetLocalization()
                          : Compose("siliconalley:wiz_royalty_value",
                              ("royalty", Pct(SiliconAlleyState.LaunchRoyalty(key, type))),
                              ("count", licensed.ToString(CultureInfo.InvariantCulture))),
            licensed <= 0 ? SiliconAlleyTheme.TextMuted : SiliconAlleyTheme.Warn);

        // Epic #34: reachable market = platform reach (#37) × segment volume (#38).
        SetStat(_sumMarket, "stat_market", "siliconalley:wiz_sum_lbl_market",
            MarketSummaryText(key, type), SiliconAlleyTheme.Text);

        // Issue #87: the #85/#86 market fit — how the player's per-feature allocation scores against demand,
        // in the same vocabulary the Market-targeting step uses, so the two screens agree. Both effects are
        // signed RELATIVE to the neutral even split. Under 2 selected features there is nothing to reallocate
        // (FitDelta returns 0 by design), so say "not targeted" rather than a misleading +0%.
        var fitMask = SiliconAlleyState.GetFeatureMask(key);
        var fitWeights = SiliconAlleyState.GetFeatureWeights(key);
        var day = TimeHelper.CurrentDay;
        var fitMarket = SiliconAlleyAspects.MarketFitFactor(fitMask, fitWeights, type, day);
        var fitQuality = SiliconAlleyAspects.QualityFitBonus(fitMask, fitWeights, type, day);
        var fitDelta = SiliconAlleyAspects.FitDelta(type, fitMask, fitWeights, day);
        // Gate the readout on the SAME condition FitDelta gates on — fewer than 2 selected features means no
        // reallocation is possible. Keying off "delta == 0" instead would mislabel a deliberate allocation
        // that happens to score neutral as "not targeted".
        var selectedFeatures = 0;
        foreach (var f in SiliconAlleyFeatures.FeaturesFor(type))
            if ((fitMask & (1 << f.Bit)) != 0) selectedFeatures++;
        var targeted = selectedFeatures >= 2;
        SetStat(_sumFit, "stat_fit", "siliconalley:wiz_sum_lbl_fit",
            targeted
                ? Compose("siliconalley:wiz_sum_fit_value",
                    ("market", SignedPct(fitMarket - 1f)),
                    ("quality", SignedPct(fitQuality)))
                : "siliconalley:wiz_sum_fit_neutral".GetLocalization(),
            !targeted || Mathf.Approximately(fitDelta, 0f) ? SiliconAlleyTheme.TextMuted
                : fitDelta > 0f ? SiliconAlleyTheme.Ok : SiliconAlleyTheme.Warn);
    }

    // Issue #58: fill one review-card stat row (icon + label + emphasised, colour-coded value).
    private void SetStat(SiliconAlleyUI.StatRow row, string iconStem, string labelKey, string value, Color valueColor)
    {
        SetIconSprite(row.Icon, SiliconAlleyTheme.IconFor(iconStem));
        row.Label.text = labelKey.GetLocalization();
        row.Value.text = value;
        row.Value.color = valueColor;
    }

    // Issue #61: like SetStat but the numeric value counts up/down to its new target (icon/label/colour set once).
    private void SetStatNum(SiliconAlleyUI.StatRow row, string iconStem, string labelKey, float target, Func<float, string> format, Color valueColor)
    {
        SetIconSprite(row.Icon, SiliconAlleyTheme.IconFor(iconStem));
        row.Label.text = labelKey.GetLocalization();
        row.Value.color = valueColor;
        AnimateNumber(row.Value, target, format);
    }

    // Issue #61: allocation-free formatters for the animated stat values (the counting label). #144: they
    // delegate to the SiliconAlleyFormat table — note FmtPct now takes a 0..1 FRACTION, not a 0..100 value.
    private static readonly Func<float, string> FmtInt = v => Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);
    private static readonly Func<float, string> FmtPct = Pct;
    private static readonly Func<float, string> FmtMoney = Money;
    private static readonly Func<float, string> FmtReview = Review;
    // Issue #146: a SIGNED review-point delta ("+0.6" / "-1.2") for the ship report's delta readout.
    private static readonly Func<float, string> FmtSignedReview = v =>
        (v >= 0f ? "+" : "") + v.ToString("F1", CultureInfo.InvariantCulture);

    // Read-only recap shown once the concept is locked: the committed scope, focus and quality baseline.
    private void RefreshRecap(string key)
    {
        var kind = SiliconAlleyState.GetProjectType(key);
        var q = SiliconAlleyState.GetPhaseQuality(key, SiliconAlleyState.ProjectPhase.Design);
        if (q < 0f)
            q = SiliconAlleyState.GetAverageQuality(key);
        _recapText.text = Compose("siliconalley:wiz_recap",
            ("product", ProductDisplayName(key, _ctxBusinessType)),
            ("scope", SiliconAlleyState.ProjectTypeNameKey(kind).GetLocalization()),
            ("focus", Pct(SiliconAlleyState.GetDesignFocus(key))),
            ("quality", Quality(q)));
        _recapStatusText.text = "siliconalley:screen_locked".GetLocalization();
    }

    private void RefreshDevelopment(BuildingRegistration reg, BusinessType businessType, string key, float size, float rawProgress, float perHour)
    {
        SetProgress(_devBuildBar, size > 0f ? Mathf.Clamp01(rawProgress / size) : 0f);
        SetStat(_devBuild, "phase_development", "siliconalley:screen_dev_lbl_build",
            Compose("siliconalley:screen_dev_val_build",
                ("progress", Mathf.RoundToInt(rawProgress).ToString(CultureInfo.InvariantCulture)),
                ("size", Mathf.RoundToInt(size).ToString(CultureInfo.InvariantCulture))),
            SiliconAlleyTheme.Text);
        SetStat(_devThroughput, "stat_market", "siliconalley:screen_dev_lbl_throughput",
            ThroughputValue(reg, perHour), SiliconAlleyTheme.Text);
        var remaining = SiliconAlleyState.PhaseEndProgress(SiliconAlleyState.ProjectPhase.Development, size) - rawProgress;
        SetStat(_devEta, "stat_eta", "siliconalley:screen_dev_lbl_eta", Eta(remaining, perHour), SiliconAlleyTheme.Text);
        RefreshForecast(reg, businessType, key, _devForecast, _devForecastMults); // issue #130

        // Issue #146: a real checkbox — the tick IS the state (no more "Overtime: ON" + colour lerp).
        SetToggle(_overtimeToggle, SiliconAlleyState.IsOvertime(key));

        // Issue #88: the player pushes the build forward. "Send to testing" appears once Development has filled
        // (parked at its ceiling); "Release now" is available any moment (an early ship reviews worse).
        var requested = SiliconAlleyState.IsReleaseRequested(key);
        var devDone = rawProgress >= SiliconAlleyState.StageCeiling(SiliconAlleyState.ProjectStage.Development, size);
        if (requested)
            _devStatusText.text = "siliconalley:screen_release_pending".GetLocalization();
        else if (devDone)
            _devStatusText.text = Compose("siliconalley:screen_dev_done", ("demand", DemandText(businessType)));
        else
            _devStatusText.text = "siliconalley:screen_dev_inprogress".GetLocalization();
        _toTestLabel.text = "siliconalley:screen_to_testing".GetLocalization();
        _toTestButton.interactable = devDone && !requested;
        _devReleaseLabel.text = (requested
            ? "siliconalley:screen_release_pending_btn"
            : "siliconalley:screen_release_btn").GetLocalization();
        _devReleaseButton.interactable = !requested;
    }

    // Issue #88: the Testing/QA stage. Shows the live polish/bug readouts; the Release button ships any moment
    // (parked at 100% = fully tested, but you can release earlier — it reviews worse). Pending ⇒ "Releasing…".
    private void RefreshTesting(BuildingRegistration reg, BusinessType businessType, string key, float perHour, bool parked)
    {
        // Issue #19: the real tracked bug count + the derived 0..100% polish — polish drives the bar.
        SetProgress(_testPolishBar, SiliconAlleyState.GetPolish(key));
        SetStatNum(_testBugs, "stat_coverage", "siliconalley:screen_test_lbl_bugs",
            SiliconAlleyState.GetBugCount(key), FmtInt, SiliconAlleyTheme.Text);
        SetStat(_testStaff, "stat_market", "siliconalley:screen_test_lbl_staff",
            ThroughputValue(reg, perHour), SiliconAlleyTheme.Text);
        RefreshForecast(reg, businessType, key, _testForecast, _testForecastMults); // issue #130

        var requested = SiliconAlleyState.IsReleaseRequested(key);
        if (requested)
            _shipStatusText.text = "siliconalley:screen_release_pending".GetLocalization();
        else if (parked)
            // Fully tested: lead with the market-demand timing hint — releasing in a demand peak earns more.
            _shipStatusText.text = Compose("siliconalley:screen_release_ready", ("demand", DemandText(businessType)));
        else
            _shipStatusText.text = Compose("siliconalley:screen_release_testing", ("demand", DemandText(businessType)));
        _shipLabel.text = (requested
            ? "siliconalley:screen_release_pending_btn"
            : "siliconalley:screen_release_btn").GetLocalization();
        // Release any moment in Testing (an early ship reviews worse — the player's call).
        _shipButton.interactable = !requested;
    }

    // Issue #88: the Idle stage — no active project. Staff do no product work; the player starts the next
    // version here. The button label carries the next version number (the previous ship already bumped it).
    private void RefreshIdle(string key)
    {
        _idleStatusText.text = "siliconalley:screen_idle_status".GetLocalization();
        _startLabel.text = Compose("siliconalley:screen_start_project",
            ("version", "v" + SiliconAlleyState.GetVersion(key).ToString(CultureInfo.InvariantCulture)));
        _startButton.interactable = true;
    }

    // Issue #88: the manual post-launch update gate for the live catalog. Shown whenever an update is due;
    // the button credits the support patch (same revenue the old auto-patch did) on the player's command,
    // gated on staffing (an update is dev work). A pending request shows "Update queued…".
    private void RefreshUpdate(BuildingRegistration reg, BusinessType businessType, string key, bool staffed)
    {
        var requested = SiliconAlleyState.IsUpdateRequested(key);
        if (requested)
            _updateStatusText.text = "siliconalley:screen_update_pending".GetLocalization();
        else if (!staffed)
            _updateStatusText.text = "siliconalley:screen_update_needstaff".GetLocalization();
        else
            _updateStatusText.text = Compose("siliconalley:screen_update_ready", ("demand", DemandText(businessType)));
        var revenue = SiliconAlleyOfficeSimulator.EstimateUpdateRevenue(reg, businessType, key);
        _updateLabel.text = requested
            ? "siliconalley:screen_update_pending_btn".GetLocalization()
            : Compose("siliconalley:screen_update_btn", ("revenue", Money(revenue)));
        _updateButton.interactable = staffed && !requested;
    }

    // Issue #88: the current market-demand multiplier as a timing hint ("×1.12"). Releasing while it is high
    // earns more (it scales launch + update revenue, #28). Neutral "×1.00" without a resolvable type.
    private static string DemandText(BusinessType businessType) =>
        Demand(businessType == null
            ? 1f
            : SiliconAlleyMarket.DemandFactor(businessType.businessTypeName, TimeHelper.CurrentDay));

    // Shared "{staff} staff · {perhour}/h" value for the development throughput + testing QA stat rows.
    private static string ThroughputValue(BuildingRegistration reg, float perHour) =>
        Compose("siliconalley:screen_val_throughput",
            ("staff", CountStaff(reg).ToString(CultureInfo.InvariantCulture)),
            ("perhour", Mathf.RoundToInt(perHour).ToString(CultureInfo.InvariantCulture)));

    private void RefreshRelease(BuildingRegistration reg, BusinessType businessType, string key, SiliconAlleyState.ShipReport report)
    {
        SetStat(_relProduct, "stat_market", "siliconalley:screen_rel_lbl_product",
            string.IsNullOrWhiteSpace(report.ProductName) ? ProductDisplayName(key, businessType) : report.ProductName,
            SiliconAlleyTheme.Header);
        // Issue #20/#60: lead with the critical-reception score + a 0..10 review bar (color-graded).
        SetStatNum(_relReview, "stat_quality", "siliconalley:screen_rel_lbl_review", report.Review, FmtReview, SiliconAlleyTheme.Header);
        var review01 = Mathf.Clamp01(report.Review / 10f);
        SetProgress(_relReviewBar, review01,
            report.Review >= 7f ? SiliconAlleyTheme.Ok : report.Review >= 4f ? SiliconAlleyTheme.Accent : SiliconAlleyTheme.Warn);
        // Issue #146: how this release reviewed against the previous one. The newest history row IS this
        // ship (appended in OnProjectCompleted), so the previous release sits at Count - 2.
        var releases = SiliconAlleyState.GetReleaseHistory(key);
        var hasPrev = releases.Count >= 2;
        _relReviewDeltaRow.SetActive(hasPrev);
        if (hasPrev)
        {
            var prev = releases[releases.Count - 2].Review;
            SetDelta(_relReviewDelta, Review(prev), Review(report.Review), report.Review - prev, FmtSignedReview);
        }
        SetStatNum(_relQuality, "stat_coverage", "siliconalley:screen_rel_lbl_quality", Mathf.Clamp01(report.Quality), FmtPct, SiliconAlleyTheme.Text);
        SetStat(_relRevenue, "stat_cost", "siliconalley:screen_rel_lbl_revenue",
            Compose("siliconalley:screen_rel_val_revenue",
                ("payout", Money(report.Payout)),
                ("repmult", report.RepMult.ToString("F2", CultureInfo.InvariantCulture)),
                ("marketmult", report.MarketMult.ToString("F2", CultureInfo.InvariantCulture))),
            SiliconAlleyTheme.Ok);
        // Issue #24: the franchise's version + IP reputation alongside reputation and installed base.
        SetStat(_relRep, "stat_market", "siliconalley:screen_rel_lbl_rep",
            Compose("siliconalley:screen_rel_val_rep",
                ("reputation", SiliconAlleyState.GetReputation(key).ToString("F2", CultureInfo.InvariantCulture)),
                ("iprep", SiliconAlleyState.GetIpReputation(key).ToString("F2", CultureInfo.InvariantCulture)),
                ("version", "v" + SiliconAlleyState.GetVersion(key).ToString(CultureInfo.InvariantCulture)),
                ("base", SiliconAlleyState.GetInstalledBase(key).ToString(CultureInfo.InvariantCulture))),
            SiliconAlleyTheme.Text);
        // Issue #25/#60: support income + the current freshness as a thin bar (declines as the catalog ages).
        // Issue #144: the CANONICAL demand-aware SupportPerDay — the private demand-less copy this screen used
        // showed a different $/day than the hub card for the same studio at the same tick.
        SetStat(_relSupport, "stat_royalty", "siliconalley:screen_rel_lbl_support", SupportPerDay(reg, key), SiliconAlleyTheme.Text);
        SetProgress(_relFreshBar, SiliconAlleyState.SupportFreshness(key, TimeHelper.CurrentDay));
        SetStat(_relPatch, "stat_eta", "siliconalley:screen_rel_lbl_patch", PatchEta(key), SiliconAlleyTheme.Text);
    }

    // Issue #27: progress of the studio's accepted contract — % done, days until the deadline, payout — plus
    // the #126/#129 staff-split dial (the section's only write besides the phone's Accept).
    private void RefreshContract(string key)
    {
        var scope = SiliconAlleyState.GetContractScope(key);
        var frac = scope > 0f ? Mathf.Clamp01(SiliconAlleyState.GetContractProgress(key) / scope) : 0f;
        var daysLeft = Mathf.Max(0, SiliconAlleyState.GetContractDeadlineDay(key) - TimeHelper.CurrentDay);
        // Amber bar — the contract competes with the studio's own product for staff (the dial below splits them).
        SetProgress(_contractBar, frac, SiliconAlleyTheme.Warn);
        SetStatNum(_contractProgress, "stat_coverage", "siliconalley:screen_contract_lbl_progress", frac, FmtPct, SiliconAlleyTheme.Text);
        SetStat(_contractDue, "stat_eta", "siliconalley:screen_contract_lbl_due",
            DaysLeft(daysLeft), SiliconAlleyTheme.Text);
        SetStatNum(_contractPayout, "stat_cost", "siliconalley:screen_contract_lbl_payout",
            SiliconAlleyState.GetContractPayout(key), FmtMoney, SiliconAlleyTheme.Ok);
        // Issue #129: reflect the persisted focus without writing back through the change handler.
        var focus = SiliconAlleyState.GetContractFocus(key);
        _suppress = true;
        _contractFocusSlider.value = 1f - focus;
        _suppress = false;
        // Round ONCE so the two shares always sum to 100%.
        var contractPct = Mathf.RoundToInt(Mathf.Clamp01(focus) * 100f);
        _contractFocusText.text = Compose("siliconalley:screen_contract_focus_val",
            ("contract", Pct(contractPct / 100f)),
            ("product", Pct((100 - contractPct) / 100f)));
    }

    // Issue #129: the staff-split dial writes the #126 contractFocus (slider 0 = all contract, 1 = all product).
    private void OnContractFocusChanged(float value)
    {
        if (_suppress || string.IsNullOrEmpty(_currentKey))
            return;
        SiliconAlleyState.SetContractFocus(_currentKey, 1f - value);
    }

    // Issue #129: the persistent release-history rows — newest first, capped at HistoryMaxRows. The newest
    // row shows the recorded multiplier breakdown; -1 sentinels (pre-0.5.0 rows) render as "not recorded".
    private void RefreshHistory(BusinessType businessType, string key)
    {
        var releases = SiliconAlleyState.GetReleaseHistory(key);
        var count = Mathf.Min(HistoryMaxRows, releases.Count);
        _historySection.SetActive(count > 0);
        if (count == 0)
            return;
        for (var i = 0; i < count; i++)
        {
            var row = EnsureHistoryRow(i);
            row.Root.SetActive(true);
            FillHistoryRow(row, releases[releases.Count - 1 - i], businessType, i == 0);
        }
        for (var i = count; i < _historyRows.Count; i++)
            _historyRows[i].Root.SetActive(false);
    }

    private HistoryRow EnsureHistoryRow(int index)
    {
        while (index >= _historyRows.Count)
            _historyRows.Add(BuildHistoryRow(_historyHost.transform));
        return _historyRows[index];
    }

    private static HistoryRow BuildHistoryRow(Transform parent)
    {
        var r = new HistoryRow();
        var card = MakeCardPanel(parent, "HistoryRow");
        var t = card.transform;
        var header = MakeRow(t, 8f, 26);
        header.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        r.Title = MakeText(header.transform, "Title", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleLeft, FontStyle.Bold);
        r.Title.GetComponent<LayoutElement>().flexibleWidth = 1f; // push the review badge to the right edge
        r.Review = MakeBadge(header.transform, SiliconAlleyTheme.Slate, SiliconAlleyTheme.Text); // issue #146
        r.Meta = MakeText(t, "Meta", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        r.Meta.color = SiliconAlleyTheme.TextMuted;
        r.Mults = MakeText(t, "Mults", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        r.Mults.color = SiliconAlleyTheme.TextMuted;
        r.Root = card;
        return r;
    }

    private void FillHistoryRow(HistoryRow row, SiliconAlleyState.ReleaseRecord rec, BusinessType businessType, bool expand)
    {
        var name = string.IsNullOrWhiteSpace(rec.ProductName) ? ProductDisplayName(businessType) : rec.ProductName;
        row.Title.text = name + " v" + rec.Version.ToString(CultureInfo.InvariantCulture);
        // The same grading the ship report's review bar uses (>=7 good, >=4 fine, else rough).
        SetBadge(row.Review, Review(rec.Review), rec.Review >= 7f ? SiliconAlleyTheme.Ok
            : rec.Review >= 4f ? SiliconAlleyTheme.Accent : SiliconAlleyTheme.Warn);
        var publisher = SiliconAlleyPublishers.TryGetById(rec.Publisher, out var pub)
            ? pub.NameKey.GetLocalization()
            : "—";
        row.Meta.text = Compose("siliconalley:screen_history_meta",
            ("day", rec.Day.ToString(CultureInfo.InvariantCulture)),
            ("kind", SiliconAlleyState.ProjectTypeNameKey(rec.Kind).GetLocalization()),
            ("quality", Pct(rec.Quality)),
            ("payout", Money(rec.LaunchPayout)),
            ("units", rec.LaunchUnits.ToString(CultureInfo.InvariantCulture)),
            ("publisher", publisher));
        // The multiplier breakdown only expands on the newest row; -1 = never recorded (pre-0.5.0 ship).
        row.Mults.gameObject.SetActive(expand);
        if (!expand)
            return;
        row.Mults.text = rec.RepMult < 0f
            ? "siliconalley:screen_history_mults_na".GetLocalization()
            : Compose("siliconalley:screen_history_mults",
                ("rep", rec.RepMult.ToString("F2", CultureInfo.InvariantCulture)),
                ("market", rec.MarketMult.ToString("F2", CultureInfo.InvariantCulture)),
                ("demand", rec.DemandMult.ToString("F2", CultureInfo.InvariantCulture)),
                ("clean", Pct(rec.Cleanliness)));
    }

    // Issue #21 (Marketing): refresh the campaign block — current awareness/hype, channel costs, and the
    // Ad Spend toggle. Buttons gate on affordability (SiliconAlleyMoney.CanAfford). Press Build calls out
    // that it lands hardest in late Development (the simulator applies the timing bonus on purchase).
    private void RefreshMarketing(BuildingRegistration reg, string key, float rawProgress, float size)
    {
        SetStatNum(_mktAwareness, "stat_market", "siliconalley:screen_mkt_lbl_awareness",
            SiliconAlleyState.GetAwareness(key), FmtInt, SiliconAlleyTheme.Text);
        SetStatNum(_mktHype, "stat_market", "siliconalley:screen_mkt_lbl_hype",
            SiliconAlleyState.GetHype(key), FmtInt, SiliconAlleyTheme.Text);

        // Issue #29: surface the free awareness from a player-operated marketing agency (row hidden when none owned).
        var agencies = SiliconAlleyOfficeSimulator.OwnedMarketingAgencies();
        _mktSynergy.Root.SetActive(agencies > 0);
        if (agencies > 0)
            SetStat(_mktSynergy, "stat_royalty", "siliconalley:screen_mkt_lbl_synergy",
                Compose("siliconalley:screen_mkt_val_synergy",
                    ("rate", (agencies * SiliconAlleyOfficeSimulator.MarketingSynergyAwarenessPerHour).ToString("0.0", CultureInfo.InvariantCulture)),
                    ("count", agencies.ToString(CultureInfo.InvariantCulture))),
                SiliconAlleyTheme.Ok);

        _pressReleaseLabel.text = Compose("siliconalley:screen_mkt_press_release", ("cost", Money(SiliconAlleyState.PressReleaseCost)));
        _pressBuildLabel.text = Compose("siliconalley:screen_mkt_press_build", ("cost", Money(SiliconAlleyState.PressBuildCost)));
        _hypeLabel.text = Compose("siliconalley:screen_mkt_hype", ("cost", Money(SiliconAlleyState.HypeCost)));

        _pressReleaseButton.interactable = SiliconAlleyMoney.CanAfford(reg, SiliconAlleyState.PressReleaseCost);
        _pressBuildButton.interactable = SiliconAlleyMoney.CanAfford(reg, SiliconAlleyState.PressBuildCost);
        _hypeButton.interactable = SiliconAlleyMoney.CanAfford(reg, SiliconAlleyState.HypeCost);

        // Issue #146: checkbox row — the label re-states the hourly cost, the tick is the state.
        _adSpendToggle.Label.text = Compose("siliconalley:toggle_adspend",
            ("cost", Money(SiliconAlleyState.AdSpendCostPerHour)));
        SetToggle(_adSpendToggle, SiliconAlleyState.IsAdSpend(key));

        // Issue #130: the Press Build timing line — the window the purchase handler always applied, live.
        var timing = SiliconAlleyState.PressBuildTiming(rawProgress, size);
        var windowStart = Pct(SiliconAlleyState.PressBuildWindowStart);
        var windowEnd = Pct(SiliconAlleyState.PressBuildWindowEnd);
        if (timing >= 1f)
        {
            _mktTimingText.text = Compose("siliconalley:screen_mkt_timing_hot", ("start", windowStart), ("end", windowEnd));
            _mktTimingText.color = SiliconAlleyTheme.Ok;
        }
        else
        {
            _mktTimingText.text = Compose("siliconalley:screen_mkt_timing_cold",
                ("factor", timing.ToString("0.0#", CultureInfo.InvariantCulture)),
                ("start", windowStart), ("end", windowEnd));
            _mktTimingText.color = SiliconAlleyTheme.TextMuted;
        }
    }

    // Issue #130: the projected review — the exact ship-block quality math (accrued capped by the design
    // ceiling, then the cleanliness floor and the bug factor), fed through ComputeReviewScore — plus the
    // silent quality gates spelled out as multipliers. "—" until any quality has accrued (fresh project).
    private void RefreshForecast(BuildingRegistration reg, BusinessType businessType, string key,
        SiliconAlleyUI.StatRow forecastRow, TMP_Text multsText)
    {
        var accrued = SiliconAlleyState.GetAverageQuality(key);
        if (accrued < 0f)
        {
            SetStat(forecastRow, "stat_quality", "siliconalley:screen_forecast_lbl", "—", SiliconAlleyTheme.TextMuted);
            multsText.gameObject.SetActive(false);
            return;
        }
        var designQuality = SiliconAlleyState.GetPhaseQuality(key, SiliconAlleyState.ProjectPhase.Design);
        var ceiling = SiliconAlleyState.DesignQualityCeiling(key, businessType?.businessTypeName, designQuality, TimeHelper.CurrentDay);
        var capped = Mathf.Min(accrued, ceiling);
        var cleanFactor = Mathf.Max(0.25f, Mathf.Clamp01(reg.GetCleanliness() / 100f));
        var bugFactor = SiliconAlleyState.BugQualityFactor(key);
        var review = SiliconAlleyState.ComputeReviewScore(capped * cleanFactor * bugFactor, designQuality,
            SiliconAlleyState.GetAwareness(key));
        SetStat(forecastRow, "stat_quality", "siliconalley:screen_forecast_lbl",
            Compose("siliconalley:screen_forecast_val", ("review", Review(review))),
            review >= 7f ? SiliconAlleyTheme.Ok : review >= 4f ? SiliconAlleyTheme.Accent : SiliconAlleyTheme.Warn);
        multsText.gameObject.SetActive(true);
        var capFactor = accrued > 0f ? capped / accrued : 1f;
        multsText.text = Compose("siliconalley:screen_forecast_mults",
            ("cap", capFactor.ToString("F2", CultureInfo.InvariantCulture)),
            ("clean", cleanFactor.ToString("F2", CultureInfo.InvariantCulture)),
            ("bugs", bugFactor.ToString("F2", CultureInfo.InvariantCulture)));
    }

    // Issue #17/#22/#23 (Publishers): with no active deal, show one sign button per ELIGIBLE publisher (focus
    // match or generalist), labelled with the live offer (payout / deadline days / your reputation); ineligible
    // publishers' buttons are hidden. With an active deal, hide the buttons and show the publisher + a live
    // day-countdown to the deadline, the ship ETA and the locked payout.
    private void RefreshPublisher(BuildingRegistration reg, BusinessType businessType, string key, float rawProgress, float size, float perHour)
    {
        if (SiliconAlleyState.HasDeal(key))
        {
            // Active deal: hide the offer cards + prompt, show the deal card with a ship-progress bar.
            _pubNoDealText.gameObject.SetActive(false);
            for (int i = 0; i < _publisherCards.Length; i++)
                _publisherCards[i].Root.SetActive(false);
            _pubDealCard.SetActive(true);

            var pub = SiliconAlleyState.GetDealPublisher(key);
            var name = SiliconAlleyPublishers.TryGetById(pub, out var publisher) ? publisher.NameKey.GetLocalization() : "";
            var daysLeft = SiliconAlleyState.GetDealDeadlineDay(key) - TimeHelper.CurrentDay;
            var urgent = daysLeft <= 3;
            var deadline = DaysLeft(daysLeft); // exact countdown, bare "Nd" — "due now" once lapsed (#144)
            SetProgress(_pubShipBar, size > 0f ? Mathf.Clamp01(rawProgress / size) : 0f,
                urgent ? SiliconAlleyTheme.Warn : SiliconAlleyTheme.Accent);
            SetStat(_pubDealPublisher, "stat_royalty", "siliconalley:screen_pub_lbl_publisher", name, SiliconAlleyTheme.Text);
            SetStat(_pubDealDeadline, "stat_eta", "siliconalley:screen_pub_lbl_deadline", deadline,
                urgent ? SiliconAlleyTheme.Warn : SiliconAlleyTheme.Text);
            SetStat(_pubDealShipEta, "stat_eta", "siliconalley:screen_pub_lbl_shipeta", Eta(size - rawProgress, perHour), SiliconAlleyTheme.Text);
            SetStatNum(_pubDealBonus, "stat_cost", "siliconalley:screen_pub_lbl_bonus", SiliconAlleyState.GetDealPayout(key), FmtMoney, SiliconAlleyTheme.Ok);
            return;
        }

        // No deal: the prompt + one offer card per eligible publisher (icon + name + payout/deadline/rep chips).
        _pubDealCard.SetActive(false);
        _pubNoDealText.gameObject.SetActive(true);
        _pubNoDealText.text = "siliconalley:screen_pub_none".GetLocalization();
        var marketPrice = MarketPrice(businessType);
        var businessTypeName = reg.businessTypeName;
        var roster = SiliconAlleyPublishers.Roster;
        for (int i = 0; i < roster.Length; i++)
        {
            var c = _publisherCards[i];
            var pub = roster[i];
            var eligible = SiliconAlleyPublishers.IsEligible(pub, businessTypeName);
            c.Root.SetActive(eligible);
            if (!eligible)
                continue;
            var rep = SiliconAlleyState.GetPublisherRep(pub.Index);
            SiliconAlleyPublishers.OfferFor(pub, businessTypeName, marketPrice, rep,
                out var days, out var payout, out _, out _);
            SetIconSprite(c.Icon, SiliconAlleyTheme.IconFor(pub.NameKey));
            c.Title.text = pub.NameKey.GetLocalization();
            SetCardChips(c,
                "+" + Money(payout),
                DaysLeft(days),
                Compose("siliconalley:screen_pub_chip_rep", ("rep", rep.ToString("F1", CultureInfo.InvariantCulture))));
            SetCardBadge(c, "siliconalley:screen_pub_badge_sign".GetLocalization(), SiliconAlleyTheme.Accent);
        }
    }

    // ---- control callbacks -------------------------------------------------------------------------

    private void OnScopeSelected(int kind)
    {
        SiliconAlleyState.SetScope(_currentKey, kind);
        Refresh();
    }

    // Issue #146: a scope tab picked (single-select — the tab index maps straight onto ScopeKinds).
    private void OnScopeTab(int index)
    {
        if (index >= 0 && index < ScopeKinds.Length)
            OnScopeSelected(ScopeKinds[index]);
    }

    // Issue #38: pick the target audience segment for this product (single-select, like scope).
    private void OnSelectSegment(int ordinal)
    {
        SiliconAlleyState.SetSegmentId(_currentKey, ordinal);
        Refresh();
    }

    private void OnFocusChanged(float value)
    {
        if (_suppress)
            return;
        SiliconAlleyState.SetDesignFocus(_currentKey, value);
    }

    private void OnProductNameChanged(string value)
    {
        if (_suppress)
            return;
        SiliconAlleyState.SetProductName(_currentKey, value);
    }

    // Issue #35: wizard navigation. Back steps to the previous page; Next advances, and on the last (Summary)
    // page it doubles as Confirm — committing the concept via LockConcept (mirrors the old Lock button).
    private void OnWizardBack()
    {
        if (_wizardPage > 0)
            _wizardPage--;
        RefreshStructural(); // issue #147: a page swap re-lays the wizard out
    }

    private void OnWizardNext()
    {
        RebuildVisiblePages();
        if (_visiblePages.Count == 0)
        {
            RefreshStructural();
            return;
        }
        if (_wizardPage >= _visiblePages.Count - 1)
            SiliconAlleyState.BeginDevelopment(_currentKey); // issue #88: Summary confirm = Start development
        else
            _wizardPage++;
        RefreshStructural(); // issue #147: page swap (or the Summary confirm's stage change)
    }

    // Issue #26: toggle the feature shown in this Features-page slot for the current business type. The slot
    // index maps to the type's feature list at refresh time, so the bit toggled is always the right one.
    private void OnToggleFeature(int slot)
    {
        var feats = SiliconAlleyFeatures.FeaturesFor(_ctxBusinessType?.businessTypeName);
        if (slot >= 0 && slot < feats.Length)
            SiliconAlleyState.ToggleFeature(_currentKey, feats[slot].Bit);
        RefreshStructural(); // issue #147: dep-coverage cards + weight rows appear/disappear with the bit
    }

    // Issue #37: toggle the platform shown in this OS-page slot for the current business type (same slot→bit
    // resolution as features).
    private void OnTogglePlatform(int slot)
    {
        var plats = SiliconAlleyPlatforms.PlatformsFor(_ctxBusinessType?.businessTypeName);
        if (slot >= 0 && slot < plats.Length)
            SiliconAlleyState.TogglePlatform(_currentKey, plats[slot].Bit);
        Refresh();
    }

    // Issue #36: cycle this tool slot. Off → Licensed (use it, pay royalty) → Build & Own (charge R&D cash once,
    // then it's a free reusable studio asset). An owned tool just toggles in/out of the current product. The
    // build cost is shown on the button before the spending tap; an unaffordable build leaves it Licensed (the
    // base-game money API shows its own insufficient-funds toast). Gated to the editable Design phase.
    private void OnCycleTool(int slot)
    {
        if (!SiliconAlleyState.CanEditConcept(_currentKey))
            return;
        var tools = SiliconAlleyTools.ToolsFor(_ctxBusinessType?.businessTypeName);
        if (slot < 0 || slot >= tools.Length)
            return;
        var t = tools[slot];
        if (SiliconAlleyState.IsToolOwned(_currentKey, t.Bit))
            SiliconAlleyState.ToggleToolUsed(_currentKey, t.Bit);        // owned: toggle use on/off (can't un-own)
        else if (!SiliconAlleyState.IsToolUsed(_currentKey, t.Bit))
            SiliconAlleyState.ToggleToolUsed(_currentKey, t.Bit);        // Off → Licensed
        else if (SiliconAlleyMoney.TrySpend(_ctxReg, t.BuildCost,        // Licensed → Build & Own (charge R&D)
            t.NameKey.GetLocalization(), "siliconalley:transaction_tools"))
            SiliconAlleyState.SetToolOwned(_currentKey, t.Bit);          // owned now (usedToolsMask stays set)
        RefreshStructural(); // issue #147: the card's chip count changes between states (1 ↔ 3)
    }

    // Issue #84: cycle this product-dependency slot (#83 build-or-buy). Off → License Vendor A → License
    // Vendor B → Build in-house (charge R&D cash once; then it's an owned, reusable studio asset). An owned
    // dependency just toggles in/out of the current product (can't un-own). An unaffordable build (the money
    // API shows its own insufficient-funds toast) loops back to Off. Gated to the editable Design phase.
    private void OnCycleComponent(int slot)
    {
        if (!SiliconAlleyState.CanEditConcept(_currentKey))
            return;
        var type = _ctxBusinessType?.businessTypeName;
        var deps = SiliconAlleyProductDependencies.DependenciesFor(type);
        if (slot < 0 || slot >= deps.Length)
            return;
        var d = deps[slot];
        var vendors = VendorOrdinalsFor(type, d.Bit);
        if (SiliconAlleyState.IsDependencyOwned(_currentKey, d.Bit))
        {
            // Owned (self-built, reusable): toggle whether this product uses it.
            if (SiliconAlleyState.IsDependencyUsed(_currentKey, d.Bit))
                SiliconAlleyState.ClearDependency(_currentKey, type, d.Bit);
            else
                SiliconAlleyState.UseOwnedDependency(_currentKey, type, d.Bit);
        }
        else if (!SiliconAlleyState.IsDependencyUsed(_currentKey, d.Bit))
        {
            // Off → license the first vendor offer.
            if (vendors.Count > 0)
                SiliconAlleyState.LicenseDependency(_currentKey, type, d.Bit, vendors[0]);
        }
        else
        {
            // Licensing a vendor → advance to the next vendor, or (past the last) build in-house.
            var idx = vendors.IndexOf(SiliconAlleyState.GetDependencyVendorOrdinal(_currentKey, d.Bit));
            if (idx >= 0 && idx < vendors.Count - 1)
                SiliconAlleyState.LicenseDependency(_currentKey, type, d.Bit, vendors[idx + 1]);
            else if (SiliconAlleyMoney.TrySpend(_ctxReg, d.BuildCost,
                d.NameKey.GetLocalization(), "siliconalley:transaction_dependencies"))
                SiliconAlleyState.SetDependencyOwned(_currentKey, type, d.Bit); // built & owned now
            else
                SiliconAlleyState.ClearDependency(_currentKey, type, d.Bit);    // can't afford → back to Off
        }
        RefreshStructural(); // issue #147: the card's chip count changes between states (1 ↔ 3)
    }

    private void OnToggleOvertime()
    {
        SiliconAlleyState.SetOvertime(_currentKey, !SiliconAlleyState.IsOvertime(_currentKey));
        Refresh();
    }

    // Issue #88: queue the current product's release (shared by the Development + Testing buttons). The
    // simulator ships it on its next tick at the CURRENT accrued quality — an early release reviews worse.
    private void OnReleaseNow()
    {
        SiliconAlleyState.RequestRelease(_currentKey);
        Refresh();
    }

    // Issue #88: start the next project (the next version). Opens the Design wizard; also clears any lingering
    // ship report. Shared by the Idle section's "Start new project" and the ship report's "Start next project".
    private void OnStartProject()
    {
        SiliconAlleyState.StartProject(_currentKey);
        SiliconAlleyState.ClearLastShip(_currentKey);
        RefreshStructural(); // issue #147: Idle → Design swaps the section set
    }

    // Issue #113: abandon the in-flight project — the permanent escape hatch out of any stage/wizard dead
    // end. #146: the old two-click arm is a real confirm modal now (scrim + Danger confirm; Esc/backdrop
    // cancels); the body names the product being discarded. Confirming returns the studio to Idle.
    private void OnAbandonPressed()
    {
        var reg = FindRegistration(_currentKey);
        if (reg == null)
            return;
        SiliconAlleyModal.Confirm(_root.transform,
            "siliconalley:modal_abandon_title".GetLocalization(),
            Compose("siliconalley:modal_abandon_body",
                ("product", ProductDisplayName(_currentKey, BusinessTypeHelper.GetData(reg)))),
            "siliconalley:modal_abandon_confirm".GetLocalization(),
            () =>
            {
                SiliconAlleyState.AbandonProject(_currentKey);
                _wizardPage = 0; // the next project opens its wizard at the first page
                RefreshStructural(); // issue #147: the stage drops to Idle — whole section set changes
            });
    }

    // Issue #88: push the finished build from Development into Testing/QA (available once Development is done).
    private void OnSendToTesting()
    {
        SiliconAlleyState.SendToTesting(_currentKey);
        RefreshStructural(); // issue #147: Development → Testing swaps the section set
    }

    // Issue #88: queue a post-launch update for the live catalog. The simulator credits the support patch on
    // its next staffed tick (when an update is actually due) and clears the request.
    private void OnReleaseUpdate()
    {
        SiliconAlleyState.RequestUpdate(_currentKey);
        Refresh();
    }

    // Issue #88: the ship report's button now genuinely starts the next project (it used to just dismiss the
    // report, while the next project auto-started). Delegates to the shared start handler.
    private void OnStartNext() => OnStartProject();

    // ---- issue #21 (Marketing) callbacks -----------------------------------------------------------

    private void OnPressRelease()
    {
        BuyCampaign(SiliconAlleyState.PressReleaseCost, SiliconAlleyState.PressReleaseAwareness, 0f, "siliconalley:mkt_name_pressrelease");
    }

    // Press Build is strongest fired late in Development (~the back half of the build); off-window it
    // delivers a fraction of its awareness. Timing factor 0.4..1.0 across the project.
    private void OnPressBuild()
    {
        // Issue #130: the window is a shared State helper now, so this and the card's timing line agree.
        var timing = SiliconAlleyState.PressBuildTiming(SiliconAlleyState.GetProgress(_currentKey),
            SiliconAlleyState.EffectiveProjectSize(_currentKey));
        BuyCampaign(SiliconAlleyState.PressBuildCost, SiliconAlleyState.PressBuildAwareness * timing, 0f, "siliconalley:mkt_name_pressbuild");
    }

    private void OnHype()
    {
        BuyCampaign(SiliconAlleyState.HypeCost, 0f, SiliconAlleyState.HypeAmount, "siliconalley:mkt_name_hype");
    }

    private void OnToggleAdSpend()
    {
        SiliconAlleyState.SetAdSpend(_currentKey, !SiliconAlleyState.IsAdSpend(_currentKey));
        Refresh();
    }

    // Shared one-shot purchase: spend cash, then add awareness/hype and toast. No-op if the studio can't
    // pay (TrySpend returns false), so the player is never charged for a campaign that didn't land.
    private void BuyCampaign(float cost, float awareness, float hype, string channelNameKey)
    {
        var reg = FindRegistration(_currentKey);
        if (reg == null || !SiliconAlleyMoney.TrySpend(reg, cost, channelNameKey.GetLocalization()))
            return;
        if (awareness > 0f)
            SiliconAlleyState.AddAwareness(_currentKey, awareness);
        if (hype > 0f)
            SiliconAlleyState.AddHype(_currentKey, hype);
        var data = new Dictionary<string, string>
        {
            ["business"] = reg.GetDisplayName(),
            ["channel"] = channelNameKey.GetLocalization(),
            ["awareness"] = Mathf.RoundToInt(SiliconAlleyState.GetAwareness(_currentKey)).ToString(CultureInfo.InvariantCulture),
        };
        var key = _currentKey;
        Notifications.Show(NotificationType.Success, "siliconalley:notify_marketing", data, 4f, key + ":mkt",
            () => Open(key));
        Refresh();
    }

    // Issue #23 (Publisher deals): sign the offer from the clicked publisher's button. Computes the terms from
    // the current relationship (so they match the label the player saw), locks the deadline (now + days) and
    // payout, then refreshes. No-op if a deal is already active or the publisher isn't eligible (defensive).
    private void OnSignDeal(int publisherIndex)
    {
        var reg = FindRegistration(_currentKey);
        if (reg == null || SiliconAlleyState.HasDeal(_currentKey))
            return;
        if (!SiliconAlleyPublishers.TryGetById(publisherIndex, out var pub))
            return;
        var businessType = BusinessTypeHelper.GetData(reg);
        if (businessType == null || !SiliconAlleyPublishers.IsEligible(pub, reg.businessTypeName))
            return;
        var rep = SiliconAlleyState.GetPublisherRep(publisherIndex);
        SiliconAlleyPublishers.OfferFor(pub, reg.businessTypeName, MarketPrice(businessType), rep,
            out var days, out var payout, out _, out _);
        SiliconAlleyState.SignDeal(_currentKey, publisherIndex, TimeHelper.CurrentDay + days, payout);
        RefreshStructural(); // issue #147: the offer cards give way to the active-deal card
    }

    private void CycleStudio(int delta)
    {
        if (_studioKeys.Count == 0)
            return;
        var idx = Mathf.Max(0, _studioKeys.IndexOf(_currentKey));
        idx = (idx + delta + _studioKeys.Count) % _studioKeys.Count;
        _currentKey = _studioKeys[idx];
        _wizardPage = 0; // each studio opens its wizard at the first page
        RefreshStructural(); // issue #147: a whole-detail swap
    }

    // ---- helpers (formatting lives in SiliconAlleyFormat — issue #144) -----------------------------

    // #148: the product display name lives in SiliconAlleyFormat now (ProductDisplayName) — the private
    // ProductName/DisplayProductName copies this file carried are gone.

    // Market price of the business's primary product (drives publisher offer math); 0 if none.
    private static float MarketPrice(BusinessType businessType)
    {
        if (businessType?.businessProducts == null || businessType.businessProducts.Length == 0)
            return 0f;
        var item = ItemsGetter.GetByName(businessType.businessProducts[0].itemName);
        return item != null ? item.DefaultMarketPrice : 0f;
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
        var backdrop = MakeImage(_root.transform, "Backdrop", SiliconAlleyTheme.Scrim);
        Stretch(backdrop.rectTransform);
        var backdropButton = backdrop.gameObject.AddComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(Close);

        // Window: fixed-width panel; hosts a vertical ScrollRect so tall content (e.g. the ship report +
        // Design stacked) scrolls instead of overflowing the screen. Issue #147: the pivot sits at the TOP
        // centre, so height changes grow the window DOWNWARD from a fixed top edge instead of moving both
        // edges (the old per-second "breathing"); the default position puts a full-height (MaxHeight)
        // window vertically centred on screen.
        var window = MakePanel(_root.transform, "Window");
        _windowRt = window.rectTransform;
        _windowRt.anchorMin = _windowRt.anchorMax = new Vector2(0.5f, 0.5f);
        _windowRt.pivot = new Vector2(0.5f, 1f);
        _windowRt.sizeDelta = new Vector2(WindowWidth, 600f);
        _windowRt.anchoredPosition = new Vector2(0f, MaxHeight * 0.5f);
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
        // Pin horizontally to the viewport so content width == viewport width (otherwise an uninitialized
        // sizeDelta.x leaves the content wider than the viewport and the left edge gets clipped).
        _contentRt.offsetMin = new Vector2(0f, _contentRt.offsetMin.y);
        _contentRt.offsetMax = new Vector2(0f, _contentRt.offsetMax.y);
        _contentRt.anchoredPosition = Vector2.zero;
        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(SiliconAlleyTheme.Space.WindowX, SiliconAlleyTheme.Space.WindowX,
                                        SiliconAlleyTheme.Space.WindowY, SiliconAlleyTheme.Space.WindowY);
        layout.spacing = 11f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperCenter;
        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = _contentRt;
        // Issue #146: the themed scrollbar — the affordance that tall content continues below the fold.
        // Overlays the window's right padding (non-layout) and alpha-fades out when the content fits.
        MakeScrollbar(scroll);
        var root = contentGo.transform;

        // Title row: title (flexible) + [‹ Overview] (issue #127: back to the hub; hidden on it) + [X] close.
        var titleRow = MakeRow(root, 6f, 30);
        // Issue #147: invisible drag strip behind the title row — the window's drag handle. First sibling
        // + ignoreLayout: the row's buttons render later and keep raycast priority; the strip catches
        // everything else (alpha-0 Images still raycast; rows themselves have no Graphic). It implements
        // the drag interfaces itself, so the ScrollRect never scrolls during a title drag, and it has no
        // IScrollHandler, so the mouse wheel still bubbles to the ScrollRect. The strip scrolls WITH the
        // content — draggable while scrolled to top; the always-visible fixed header is #149's.
        var dragStrip = MakeImage(titleRow.transform, "DragStrip", Color.clear);
        dragStrip.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        Stretch(dragStrip.rectTransform);
        dragStrip.rectTransform.SetAsFirstSibling();
        _drag = dragStrip.gameObject.AddComponent<SiliconAlleyWindowDrag>();
        _drag.Window = _windowRt;
        _drag.ScaleSource = canvas;
        _drag.OnMoved = SaveWindowPosition;
        _titleText = MakeText(titleRow.transform, "Title", SiliconAlleyTheme.Sizes.Title, TextAnchor.MiddleLeft, FontStyle.Bold);
        _overviewButton = MakeButton(titleRow.transform, "siliconalley:screen_overview".GetLocalization(), GoHub);
        FixWidth(_overviewButton, 120f);
        FixWidth(MakeButton(titleRow.transform, "X", Close), 34f);

        // Studio selector: [<]  [type icon] name  [>]  (detail mode only — the hub hides this row, #127)
        var studioRow = MakeRow(root);
        _studioRow = studioRow;
        FixWidth(MakeButton(studioRow.transform, "‹", () => CycleStudio(-1)), 44f);
        _typeIcon = MakeIcon(studioRow.transform, null, 22f, SiliconAlleyTheme.Text); // issue #55: current business-type icon
        _studioText = MakeText(studioRow.transform, "Studio", SiliconAlleyTheme.Sizes.Subtitle, TextAnchor.MiddleCenter);
        FixWidth(MakeButton(studioRow.transform, "›", () => CycleStudio(1)), 44f);

        // Phase indicator: [phase icon] phase + progress (issue #55 adds the icon).
        var phaseRow = MakeRow(root, 6f, 24);
        _phaseRow = phaseRow;
        _phaseIcon = MakeIcon(phaseRow.transform, null, 20f, SiliconAlleyTheme.Header);
        _phaseText = MakeText(phaseRow.transform, "Phase", SiliconAlleyTheme.Sizes.Header, TextAnchor.MiddleLeft);
        _summaryText = MakeText(root, "Summary", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleLeft);

        // ---- Hub landing page (issue #127): the old F8 dashboard content, hosted as this screen's first
        // page — studio cards + the Servers section. Hidden whenever a studio's detail view shows.
        _hubSection = MakeSection(root);
        _hubEmptyText = MakeText(_hubSection.transform, "HubEmpty", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleLeft);
        _hubCardsHost = MakeSection(_hubSection.transform);
        _hubServersBlock = MakeSection(_hubSection.transform);
        MakeDivider(_hubServersBlock.transform);
        MakeHeader(_hubServersBlock.transform, "siliconalley:dash_servers_header");
        _hubServersEmpty = MakeText(_hubServersBlock.transform, "ServersEmpty",
            SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        _hubServersEmpty.color = SiliconAlleyTheme.TextMuted;
        _hubServersEmpty.text = "siliconalley:dash_servers_hint".GetLocalization();
        _hubServersHost = MakeSection(_hubServersBlock.transform);
        _hubSection.SetActive(false);

        // ---- Idle section (issue #88: no active project — start the next version) ----
        _idleSection = MakeSection(root);
        MakeHeader(_idleSection.transform, "siliconalley:screen_idle_header");
        var idleCard = MakeCardPanel(_idleSection.transform, "IdleCard");
        _idleStatusText = MakeText(idleCard.transform, "IdleStatus", SiliconAlleyTheme.Sizes.Status, TextAnchor.MiddleLeft);
        _idleStatusText.color = SiliconAlleyTheme.TextMuted;
        _startButton = MakeButton(idleCard.transform, "", OnStartProject, primary: true);
        _startLabel = _startButton.GetComponentInChildren<TMP_Text>();

        // ---- Design wizard (issue #35): paged Concept → … → Summary, shown in the Design phase ----
        _wizardSection = MakeSection(root);
        MakeDivider(_wizardSection.transform);
        BuildStepIndicator(_wizardSection.transform); // issue #56: step indicator above the active page

        // Concept page: today's scope + focus controls, read-only product name and baseline readouts.
        _conceptPage = MakeSection(_wizardSection.transform);
        MakeHeader(_conceptPage.transform, "siliconalley:screen_scope");
        // Issue #146: the scope picker is a real segmented control (was 3 hand-recoloured MakeButtons).
        var scopeKeys = new[] { "siliconalley:projecttype_quick", "siliconalley:projecttype_standard", "siliconalley:projecttype_ambitious" };
        var scopeLabels = new string[scopeKeys.Length];
        for (var i = 0; i < scopeKeys.Length; i++)
            scopeLabels[i] = scopeKeys[i].GetLocalization();
        _scopeTabs = MakeTabs(_conceptPage.transform, scopeLabels, OnScopeTab);
        for (var i = 0; i < scopeKeys.Length; i++)
            SetButtonIcon(_scopeTabs.Buttons[i], SiliconAlleyTheme.IconFor(scopeKeys[i])); // issue #55: scope icon (fixed set)
        _conceptNameText = MakeText(_conceptPage.transform, "ConceptName", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleLeft);
        MakeHeader(_conceptPage.transform, "siliconalley:screen_product_name");
        _productNameInput = MakeInputField(_conceptPage.transform, "ProductNameInput",
            "siliconalley:screen_product_name_placeholder".GetLocalization(), 64);
        _productNameInput.onValueChanged.AddListener(OnProductNameChanged);
        MakeHeader(_conceptPage.transform, "siliconalley:screen_focus");
        var focusRow = MakeRow(_conceptPage.transform, 10f, 28);
        FixWidth(MakeTextButtonless(focusRow.transform, "siliconalley:screen_focus_polish".GetLocalization()), 70f);
        _focusSlider = MakeSlider(focusRow.transform);
        _focusSlider.onValueChanged.AddListener(OnFocusChanged);
        FixWidth(MakeTextButtonless(focusRow.transform, "siliconalley:screen_focus_speed".GetLocalization()), 70f);
        _designQualityText = MakeText(_conceptPage.transform, "DesignQuality", SiliconAlleyTheme.Sizes.Header, TextAnchor.MiddleLeft);
        _leadText = MakeText(_conceptPage.transform, "Lead", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleLeft);
        _etaText = MakeText(_conceptPage.transform, "Eta", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleLeft);

        // Summary page (issue #58): a scannable review CARD — product/scope/ETA hero + icon stat rows.
        _summaryPage = MakeSection(_wizardSection.transform);
        MakeHeader(_summaryPage.transform, "siliconalley:wiz_summary_header");
        var reviewCard = MakeCardPanel(_summaryPage.transform, "ReviewCard");
        var heroRow = MakeRow(reviewCard.transform, 10f, 40);
        _sumScopeIcon = MakeIcon(heroRow.transform, null, 28f, SiliconAlleyTheme.Header);
        var heroCol = MakeSection(heroRow.transform);
        _sumHeroTitle = MakeText(heroCol.transform, "HeroTitle", SiliconAlleyTheme.Sizes.Subtitle, TextAnchor.MiddleLeft, FontStyle.Bold);
        _sumHeroSub = MakeText(heroCol.transform, "HeroSub", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        _sumHeroSub.color = SiliconAlleyTheme.TextMuted;
        // Issue #87: the card grew past the point where a flat list scans well, so the rows sit in three
        // labelled groups — what you're making, what it costs, who it reaches. The two new rows borrow the
        // cat_tool / cat_segment placeholder icons (#55); dropping stat_components.png / stat_fit.png into
        // UI/Icons and switching those stems in RefreshSummaryPage is the drop-in upgrade.
        MakeDivider(reviewCard.transform);
        MakeHeader(reviewCard.transform, "siliconalley:wiz_sum_grp_product");
        _sumQuality = MakeStatRow(reviewCard.transform);
        _sumCoverage = MakeStatRow(reviewCard.transform);
        MakeDivider(reviewCard.transform);
        MakeHeader(reviewCard.transform, "siliconalley:wiz_sum_grp_cost");
        _sumComponents = MakeStatRow(reviewCard.transform);
        _sumCost = MakeStatRow(reviewCard.transform);
        _sumRoyalty = MakeStatRow(reviewCard.transform);
        MakeDivider(reviewCard.transform);
        MakeHeader(reviewCard.transform, "siliconalley:wiz_sum_grp_market");
        _sumMarket = MakeStatRow(reviewCard.transform);
        _sumFit = MakeStatRow(reviewCard.transform);

        // Features page (issue #26): the design-document feature picker. A reusable pool of toggle buttons,
        // sized to the largest feature table; RefreshFeaturesPage relabels + shows the current type's list.
        _featuresPage = MakeSection(_wizardSection.transform);
        MakeHeader(_featuresPage.transform, "siliconalley:wiz_features_header");
        var featureSlots = SiliconAlleyFeatures.MaxCount;
        _featureCards = new SiliconAlleyUI.CardItem[featureSlots];
        for (var i = 0; i < featureSlots; i++)
        {
            var slot = i; // capture per-slot index for the toggle closure (the bit is resolved at click time)
            _featureCards[i] = MakeCardItem(_featuresPage.transform, () => OnToggleFeature(slot), checkable: true); // #146: real checkbox
        }
        _featuresReadout = MakeText(_featuresPage.transform, "FeaturesReadout", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft, FontStyle.Italic);

        // Operating-systems page (issue #37): the platform checklist. Same reusable toggle-button pool as the
        // features page, sized to the largest platform table; RefreshPlatformsPage relabels + shows the type.
        _platformsPage = MakeSection(_wizardSection.transform);
        MakeHeader(_platformsPage.transform, "siliconalley:wiz_platforms_header");
        var platformSlots = SiliconAlleyPlatforms.MaxCount;
        _platformCards = new SiliconAlleyUI.CardItem[platformSlots];
        for (var i = 0; i < platformSlots; i++)
        {
            var slot = i; // capture per-slot index for the toggle closure (the bit is resolved at click time)
            _platformCards[i] = MakeCardItem(_platformsPage.transform, () => OnTogglePlatform(slot), checkable: true); // #146: real checkbox
        }
        _platformsReadout = MakeText(_platformsPage.transform, "PlatformsReadout", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft, FontStyle.Italic);

        // Editors & tools page (issue #36): the dependency catalog. A reusable pool of CYCLE buttons (one per
        // tool: Off → Licensed → Owned), sized to the largest tool table; RefreshToolsPage relabels per type.
        _toolsPage = MakeSection(_wizardSection.transform);
        MakeHeader(_toolsPage.transform, "siliconalley:wiz_tools_header");
        var toolSlots = SiliconAlleyTools.MaxCount;
        _toolCards = new SiliconAlleyUI.CardItem[toolSlots];
        for (var i = 0; i < toolSlots; i++)
        {
            var slot = i; // capture per-slot index for the cycle closure (the bit is resolved at click time)
            _toolCards[i] = MakeCardItem(_toolsPage.transform, () => OnCycleTool(slot));
        }
        _toolsReadout = MakeText(_toolsPage.transform, "ToolsReadout", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft, FontStyle.Italic);

        // Market page (issue #38): single-select audience segment (price↔volume). One button per segment, like
        // the scope buttons; RefreshMarketPage recolors the chosen one and shows its market-size + factors.
        _marketPage = MakeSection(_wizardSection.transform);
        MakeHeader(_marketPage.transform, "siliconalley:wiz_market_header");
        var segmentCount = SiliconAlleySegments.Count;
        _segmentCards = new SiliconAlleyUI.CardItem[segmentCount];
        for (var i = 0; i < segmentCount; i++)
        {
            var ordinal = i; // capture per-segment ordinal for the select closure
            _segmentCards[i] = MakeCardItem(_marketPage.transform, () => OnSelectSegment(ordinal)); // #57: vertical card stack
        }
        _marketReadout = MakeText(_marketPage.transform, "MarketReadout", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft, FontStyle.Italic);

        // Dependencies page (issue #39): read-only feature→tool coverage. A pool of text rows (one per selected
        // feature, sized to the largest feature table), relabelled covered/uncovered each refresh; no input.
        _dependenciesPage = MakeSection(_wizardSection.transform);
        MakeHeader(_dependenciesPage.transform, "siliconalley:wiz_deps_header");
        _depCards = new SiliconAlleyUI.CardItem[SiliconAlleyFeatures.MaxCount];
        for (var i = 0; i < _depCards.Length; i++)
            _depCards[i] = MakeCardItem(_dependenciesPage.transform, null, 1); // read-only coverage card (1 "needs …" chip)
        _depReadout = MakeText(_dependenciesPage.transform, "DepReadout", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft, FontStyle.Italic);

        // Components page (issue #84): interactive build-or-buy product dependencies (#83). A reusable pool of
        // CYCLE cards (one per dependency slot: Off → License Vendor A → License Vendor B → Build in-house &
        // own), sized to the largest dependency table; RefreshComponentsPage relabels per type. Its own phase.
        _componentsPage = MakeSection(_wizardSection.transform);
        MakeHeader(_componentsPage.transform, "siliconalley:wiz_components_header");
        var componentSlots = SiliconAlleyProductDependencies.MaxCount;
        _componentCards = new SiliconAlleyUI.CardItem[componentSlots];
        for (var i = 0; i < componentSlots; i++)
        {
            var slot = i; // capture per-slot index for the cycle closure (the bit is resolved at click time)
            _componentCards[i] = MakeCardItem(_componentsPage.transform, () => OnCycleComponent(slot));
        }
        _componentsReadout = MakeText(_componentsPage.transform, "ComponentsReadout", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft, FontStyle.Italic);

        // Market-targeting pages (issue #86): Allocation = a per-feature % weight slider pool (one per feature in
        // the largest table; shown only for the selected features); Demand = a per-aspect demand vs allocation
        // bar pool + a live fit readout. Built here, re-parented into the Market phase's targeting columns below.
        _allocationPage = MakeSection(_wizardSection.transform);
        MakeHeader(_allocationPage.transform, "siliconalley:wiz_alloc_header");
        _weightRows = new WeightRow[SiliconAlleyFeatures.MaxCount];
        for (var i = 0; i < _weightRows.Length; i++)
        {
            var slot = i; // capture per-row index; the feature bit is resolved at change time
            _weightRows[i] = BuildWeightRow(_allocationPage.transform, slot);
        }
        _allocHint = MakeText(_allocationPage.transform, "AllocHint", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft, FontStyle.Italic);

        _demandPage = MakeSection(_wizardSection.transform);
        MakeHeader(_demandPage.transform, "siliconalley:wiz_demand_header");
        // Issue #146: two distribution charts (market vs you) + a shared colour legend replace the old
        // two-stacked-mini-bars-per-aspect layout — the segmented-bar primitive's live call site.
        var demandBarLbl = MakeText(_demandPage.transform, "DemandBarLbl", SiliconAlleyTheme.Sizes.Status, TextAnchor.MiddleLeft);
        demandBarLbl.color = SiliconAlleyTheme.TextMuted;
        demandBarLbl.text = "siliconalley:wiz_demand_market_lbl".GetLocalization();
        _demandBar = MakeSegmentedBar(_demandPage.transform, SiliconAlleyAspects.MaxCount);
        var allocBarLbl = MakeText(_demandPage.transform, "AllocBarLbl", SiliconAlleyTheme.Sizes.Status, TextAnchor.MiddleLeft);
        allocBarLbl.color = SiliconAlleyTheme.TextMuted;
        allocBarLbl.text = "siliconalley:wiz_demand_you_lbl".GetLocalization();
        _allocBar = MakeSegmentedBar(_demandPage.transform, SiliconAlleyAspects.MaxCount);
        _demandRows = new DemandRow[SiliconAlleyAspects.MaxCount];
        for (var i = 0; i < _demandRows.Length; i++)
            _demandRows[i] = BuildDemandRow(_demandPage.transform, i);
        MakeDivider(_demandPage.transform);
        _targetingReadout = MakeText(_demandPage.transform, "TargetingReadout", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft, FontStyle.Italic);

        // ---- Issue #81: fold the 7 sub-pages into 4 wide, multi-column phases ----
        // Concept and Summary stay single-column. Dependencies groups Features + Tools + Coverage as columns;
        // Market groups Platforms + Segment as columns. The sub-page roots (built above) are re-parented into
        // MakeColumns rows — their build + Refresh code is untouched; each column self-hides when empty.
        _phaseDependencies = MakeSection(_wizardSection.transform);
        var depsColumns = MakeColumns(_phaseDependencies.transform);
        _featuresPage.transform.SetParent(depsColumns.transform, false);
        _toolsPage.transform.SetParent(depsColumns.transform, false);
        _dependenciesPage.transform.SetParent(depsColumns.transform, false);

        _phaseMarket = MakeSection(_wizardSection.transform);
        // Issue #86: the market-targeting block (Allocation sliders | Demand + fit) sits ABOVE the existing
        // Platforms | Segment columns. Wrapped in its own section so RefreshMarketPhase can hide it wholesale for
        // a type with no features (defensive; all three types have features).
        _targetingBlock = MakeSection(_phaseMarket.transform);
        var targetingColumns = MakeColumns(_targetingBlock.transform);
        _allocationPage.transform.SetParent(targetingColumns.transform, false);
        _demandPage.transform.SetParent(targetingColumns.transform, false);
        MakeDivider(_targetingBlock.transform);
        var marketColumns = MakeColumns(_phaseMarket.transform);
        _platformsPage.transform.SetParent(marketColumns.transform, false);
        _marketPage.transform.SetParent(marketColumns.transform, false);

        // Register the 4 phases. RebuildVisiblePages sorts by Order (canonical step), so call order is free.
        _wizardPages.Add(new WizardPage { Order = 0, Root = _conceptPage, IsPresent = () => true, Refresh = RefreshConceptPage, TitleKey = "siliconalley:wiz_step_concept" });
        // Dependencies phase: present if the type has any features OR tools (each column self-hides below).
        _wizardPages.Add(new WizardPage
        {
            Order = 10,
            Root = _phaseDependencies,
            IsPresent = () => SiliconAlleyFeatures.FeaturesFor(_ctxBusinessType?.businessTypeName).Length > 0
                || SiliconAlleyTools.ToolsFor(_ctxBusinessType?.businessTypeName).Length > 0,
            Refresh = RefreshDependenciesPhase,
            TitleKey = "siliconalley:wiz_step_deps",
        });
        // Components phase (issue #84): interactive build-or-buy product dependencies (#83). Present for any
        // type that has dependency slots (all three do). Sits between the Dependencies and Market phases.
        _wizardPages.Add(new WizardPage
        {
            Order = 15,
            Root = _componentsPage,
            IsPresent = () => SiliconAlleyProductDependencies.DependenciesFor(_ctxBusinessType?.businessTypeName).Length > 0,
            Refresh = RefreshComponentsPage,
            TitleKey = "siliconalley:wiz_step_components",
        });
        // Market phase: Platforms (self-hides if none) + Segment (universal), always present.
        _wizardPages.Add(new WizardPage { Order = 20, Root = _phaseMarket, IsPresent = () => true, Refresh = RefreshMarketPhase, TitleKey = "siliconalley:wiz_step_market" });
        _wizardPages.Add(new WizardPage { Order = 100, Root = _summaryPage, IsPresent = () => true, Refresh = RefreshSummaryPage, TitleKey = "siliconalley:wiz_step_summary" });

        // Issue #56: each page fades/scales in on entry — give every page a CanvasGroup to drive the alpha.
        foreach (var page in _wizardPages)
            if (page.Root.GetComponent<CanvasGroup>() == null)
                page.Root.AddComponent<CanvasGroup>();

        // Nav row: Back · Next/Confirm (Next's label flips to Confirm on the Summary page).
        _wizardNavRow = MakeRow(_wizardSection.transform, 10f, 40);
        _wizardBackButton = MakeButton(_wizardNavRow.transform, "siliconalley:wiz_back".GetLocalization(), OnWizardBack);
        _wizardNextButton = MakeButton(_wizardNavRow.transform, "siliconalley:wiz_next".GetLocalization(), OnWizardNext, primary: true);
        _wizardNextLabel = _wizardNextButton.GetComponentInChildren<TMP_Text>();

        // Read-only recap shown once the concept is locked (replaces the pages + nav).
        _wizardRecap = MakeSection(_wizardSection.transform);
        MakeHeader(_wizardRecap.transform, "siliconalley:wiz_recap_header");
        _recapText = MakeText(_wizardRecap.transform, "Recap", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleLeft);
        _recapStatusText = MakeText(_wizardRecap.transform, "RecapStatus", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft, FontStyle.Italic);

        // ---- Development section (issue #60: card + build-progress bar + stat rows) ----
        _developmentSection = MakeSection(root);
        MakeHeader(_developmentSection.transform, "siliconalley:screen_dev_header");
        var devCard = MakeCardPanel(_developmentSection.transform, "DevCard");
        _devBuildBar = MakeProgressBar(devCard.transform);
        _devBuild = MakeStatRow(devCard.transform);
        _devThroughput = MakeStatRow(devCard.transform);
        _devEta = MakeStatRow(devCard.transform);
        // Issue #130: the projected review + the silent quality gates, live while building.
        _devForecast = MakeStatRow(devCard.transform);
        _devForecastMults = MakeText(devCard.transform, "DevForecastMults", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        _devForecastMults.color = SiliconAlleyTheme.TextMuted;
        // Issue #146: the Overtime toggle as a real checkbox row (the label states the tradeoff once).
        _overtimeToggle = MakeToggle(devCard.transform, "siliconalley:toggle_overtime".GetLocalization(), OnToggleOvertime);
        // Issue #88: the Development push controls — a status line + Send to testing / Release now buttons.
        _devStatusText = MakeText(devCard.transform, "DevStatus", SiliconAlleyTheme.Sizes.Status, TextAnchor.MiddleLeft);
        _devStatusText.color = SiliconAlleyTheme.TextMuted;
        var devButtonRow = MakeRow(devCard.transform, 10f, 40);
        _toTestButton = MakeButton(devButtonRow.transform, "", OnSendToTesting);
        _toTestLabel = _toTestButton.GetComponentInChildren<TMP_Text>();
        _devReleaseButton = MakeButton(devButtonRow.transform, "", OnReleaseNow, primary: true);
        _devReleaseLabel = _devReleaseButton.GetComponentInChildren<TMP_Text>();

        // ---- Testing / Release-gate section (issue #60 card; issue #88 manual release) ----
        _testingSection = MakeSection(root);
        MakeHeader(_testingSection.transform, "siliconalley:screen_test_header");
        var testCard = MakeCardPanel(_testingSection.transform, "TestCard");
        _testPolishBar = MakeProgressBar(testCard.transform);
        _testBugs = MakeStatRow(testCard.transform);
        _testStaff = MakeStatRow(testCard.transform);
        // Issue #130: the projected review + quality gates — most useful right before the Release call.
        _testForecast = MakeStatRow(testCard.transform);
        _testForecastMults = MakeText(testCard.transform, "TestForecastMults", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        _testForecastMults.color = SiliconAlleyTheme.TextMuted;
        // Manual release: a status line (ready / market-demand timing hint) + the Release button. Replaces the
        // old Hold toggle + Ship-now — a product no longer auto-ships (see SiliconAlleyOfficeSimulator).
        _shipStatusText = MakeText(testCard.transform, "ShipStatus", SiliconAlleyTheme.Sizes.Status, TextAnchor.MiddleLeft);
        _shipStatusText.color = SiliconAlleyTheme.TextMuted;
        _shipButton = MakeButton(testCard.transform, "", OnReleaseNow, primary: true);
        _shipLabel = _shipButton.GetComponentInChildren<TMP_Text>();

        // ---- Milestone decision card (issue #128, epic #121): shown while a #123 milestone window is open
        // in Development/Testing. Two option buttons with effect summaries + the auto-resolve countdown.
        _milestoneSection = MakeSection(root);
        MakeHeader(_milestoneSection.transform, "siliconalley:ms_header");
        var msCard = MakeCardPanel(_milestoneSection.transform, "MilestoneCard");
        var msTitleRow = MakeRow(msCard.transform, 8f, 28);
        msTitleRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        _milestoneIcon = MakeIcon(msTitleRow.transform, null, 24f, SiliconAlleyTheme.Accent);
        _milestoneTitle = MakeText(msTitleRow.transform, "MsTitle", SiliconAlleyTheme.Sizes.Subtitle, TextAnchor.MiddleLeft, FontStyle.Bold);
        _milestoneTitle.GetComponent<LayoutElement>().flexibleWidth = 1f;
        _milestoneDesc = MakeText(msCard.transform, "MsDesc", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleLeft);
        _milestoneDesc.color = SiliconAlleyTheme.TextMuted;
        _msOptAButton = MakeButton(msCard.transform, "", () => OnMilestoneOption(0), primary: true);
        _msOptALabel = _msOptAButton.GetComponentInChildren<TMP_Text>();
        _msOptAReason = MakeDisabledReason(msCard.transform); // issue #146: the affordability shortfall
        _msOptASummary = MakeText(msCard.transform, "MsOptASum", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        _msOptASummary.color = SiliconAlleyTheme.TextMuted;
        _msOptBButton = MakeButton(msCard.transform, "", () => OnMilestoneOption(1));
        _msOptBLabel = _msOptBButton.GetComponentInChildren<TMP_Text>();
        _msOptBReason = MakeDisabledReason(msCard.transform); // issue #146
        _msOptBSummary = MakeText(msCard.transform, "MsOptBSum", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        _msOptBSummary.color = SiliconAlleyTheme.TextMuted;
        _milestoneCountdown = MakeText(msCard.transform, "MsCountdown", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        _milestoneCountdown.color = SiliconAlleyTheme.TextMuted;

        // ---- Updates section (issue #88: manual post-launch updates for the live catalog) ----
        _updateSection = MakeSection(root);
        MakeHeader(_updateSection.transform, "siliconalley:screen_update_header");
        var updateCard = MakeCardPanel(_updateSection.transform, "UpdateCard");
        _updateStatusText = MakeText(updateCard.transform, "UpdateStatus", SiliconAlleyTheme.Sizes.Status, TextAnchor.MiddleLeft);
        _updateStatusText.color = SiliconAlleyTheme.TextMuted;
        _updateButton = MakeButton(updateCard.transform, "", OnReleaseUpdate, primary: true);
        _updateLabel = _updateButton.GetComponentInChildren<TMP_Text>();

        // ---- Marketing section (issue #21; issue #60 card restyle) ----
        _marketingSection = MakeSection(root);
        MakeHeader(_marketingSection.transform, "siliconalley:screen_mkt_header");
        var mktCard = MakeCardPanel(_marketingSection.transform, "MktCard");
        _mktAwareness = MakeStatRow(mktCard.transform);
        _mktHype = MakeStatRow(mktCard.transform);
        _mktSynergy = MakeStatRow(mktCard.transform); // #29: hidden when no marketing agency operated
        _pressReleaseButton = MakeButton(mktCard.transform, "", OnPressRelease);
        _pressReleaseLabel = _pressReleaseButton.GetComponentInChildren<TMP_Text>();
        _pressBuildButton = MakeButton(mktCard.transform, "", OnPressBuild);
        // Issue #130: the Press Build timing window, finally visible (a live line under its button).
        _mktTimingText = MakeText(mktCard.transform, "MktTiming", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        _mktTimingText.color = SiliconAlleyTheme.TextMuted;
        _pressBuildLabel = _pressBuildButton.GetComponentInChildren<TMP_Text>();
        _hypeButton = MakeButton(mktCard.transform, "", OnHype);
        _hypeLabel = _hypeButton.GetComponentInChildren<TMP_Text>();
        // Issue #146: Ad Spend as a checkbox row; the label is set per refresh (it carries the hourly cost).
        _adSpendToggle = MakeToggle(mktCard.transform, "", OnToggleAdSpend);

        // ---- Publisher section (issue #17/#22/#23; issue #60: offer cards + active-deal card) ----
        _publisherSection = MakeSection(root);
        MakeHeader(_publisherSection.transform, "siliconalley:screen_pub_header");
        _pubNoDealText = MakeText(_publisherSection.transform, "PubNoDeal", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        _pubNoDealText.color = SiliconAlleyTheme.TextMuted;
        var roster = SiliconAlleyPublishers.Roster;
        _publisherCards = new SiliconAlleyUI.CardItem[roster.Length];
        for (int i = 0; i < roster.Length; i++)
        {
            var index = i; // capture a stable copy for the click closure
            _publisherCards[i] = MakeCardItem(_publisherSection.transform, () => OnSignDeal(index));
        }
        // Active-deal card (shown instead of the offers once a deal is signed): ship-progress bar + terms.
        _pubDealCard = MakeCardPanel(_publisherSection.transform, "PubDealCard");
        _pubShipBar = MakeProgressBar(_pubDealCard.transform);
        _pubDealPublisher = MakeStatRow(_pubDealCard.transform);
        _pubDealDeadline = MakeStatRow(_pubDealCard.transform);
        _pubDealShipEta = MakeStatRow(_pubDealCard.transform);
        _pubDealBonus = MakeStatRow(_pubDealCard.transform);

        // ---- Release section (transient ship report; issue #60: review + freshness bars + stat rows) ----
        _releaseSection = MakeSection(root);
        MakeHeader(_releaseSection.transform, "siliconalley:screen_rel_header");
        var relCard = MakeCardPanel(_releaseSection.transform, "RelCard");
        _relProduct = MakeStatRow(relCard.transform);
        _relReview = MakeStatRow(relCard.transform);
        _relReviewBar = MakeProgressBar(relCard.transform);
        // Issue #146: "vs previous release   6.8/10 › 7.4/10 (+0.6)" — the delta-readout primitive's
        // first live call site. The whole row hides on a debut (nothing to compare against).
        _relReviewDeltaRow = MakeRow(relCard.transform, 8f, 24);
        _relReviewDeltaRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        var relDeltaLbl = MakeText(_relReviewDeltaRow.transform, "DeltaLbl", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        relDeltaLbl.color = SiliconAlleyTheme.TextMuted;
        relDeltaLbl.text = "siliconalley:rel_review_delta_lbl".GetLocalization();
        relDeltaLbl.GetComponent<LayoutElement>().flexibleWidth = 1f; // push the readout to the right edge
        _relReviewDelta = MakeDeltaReadout(_relReviewDeltaRow.transform);
        _relQuality = MakeStatRow(relCard.transform);
        _relRevenue = MakeStatRow(relCard.transform);
        _relRep = MakeStatRow(relCard.transform);
        _relSupport = MakeStatRow(relCard.transform);
        _relFreshBar = MakeProgressBar(relCard.transform, 6f);
        _relPatch = MakeStatRow(relCard.transform);
        MakeButton(_releaseSection.transform, "siliconalley:screen_startnext".GetLocalization(), OnStartNext, primary: true);

        // ---- Release history (issue #129): the studio's persistent catalog, newest first ----
        _historySection = MakeSection(root);
        MakeDivider(_historySection.transform);
        // Issue #146: the archive folds behind its header, default collapsed — it's pure history (up to 8
        // cards ≈ 700px of scroll) and the fold reclaims that for the sections that need action. The
        // RebuildLayout toggle callback (#147) resizes the window on the click instead of a frame later.
        var historyFold = MakeCollapsible(_historySection.transform, "siliconalley:screen_history_header",
            startExpanded: false, onToggled: RebuildLayout);
        _historyHost = historyFold.Content;

        // ---- Contract section (issue #27; issue #60: card + amber progress bar + stat rows) ----
        _contractSection = MakeSection(root);
        MakeHeader(_contractSection.transform, "siliconalley:screen_contract_header");
        var contractCard = MakeCardPanel(_contractSection.transform, "ContractCard");
        _contractBar = MakeProgressBar(contractCard.transform);
        _contractProgress = MakeStatRow(contractCard.transform);
        _contractDue = MakeStatRow(contractCard.transform);
        _contractPayout = MakeStatRow(contractCard.transform);
        // Issue #129: the #126 staff-split dial. Left = all hands on the contract (the legacy divert),
        // right = product first; the slider maps to contractFocus = 1 − value.
        var cfRow = MakeRow(contractCard.transform, 10f, 28);
        FixWidth(MakeTextButtonless(cfRow.transform, "siliconalley:screen_contract_focus_left".GetLocalization()), 92f);
        _contractFocusSlider = MakeSlider(cfRow.transform);
        _contractFocusSlider.onValueChanged.AddListener(OnContractFocusChanged);
        FixWidth(MakeTextButtonless(cfRow.transform, "siliconalley:screen_contract_focus_right".GetLocalization()), 92f);
        _contractFocusText = MakeText(contractCard.transform, "ContractFocusVal", SiliconAlleyTheme.Sizes.Status, TextAnchor.MiddleLeft);
        _contractFocusText.color = SiliconAlleyTheme.TextMuted;

        // ---- Footer (common) ----
        MakeDivider(root);
        var footer = MakeRow(root, 10f, 40);
        // Issue #113: the Abandon escape hatch — reachable from every active stage, so no wizard/stage state
        // can ever trap a studio again. Hidden while Idle (nothing to abandon); see RefreshAbandon. #146:
        // the label is static now — the confirm (and its Danger red) lives in the modal, not on the button.
        _abandonButton = MakeButton(footer.transform, "siliconalley:screen_abandon_btn".GetLocalization(), OnAbandonPressed);
        MakeButton(footer.transform, "siliconalley:screen_close".GetLocalization(), Close);

        // Issue #147: restore the last dragged position (machine-local; default = top-centred). One canvas
        // force-update first so the CanvasScaler has sized the canvas rect the clamp checks against — a
        // stale/off-screen/other-resolution position self-repairs instead of loading unreachable.
        Canvas.ForceUpdateCanvases();
        _windowRt.anchoredPosition = _drag.Clamp(new Vector2(
            UnityEngine.PlayerPrefs.GetFloat(PrefWindowX, 0f),
            UnityEngine.PlayerPrefs.GetFloat(PrefWindowY, MaxHeight * 0.5f)));

        _root.SetActive(false);
    }

    // Issue #147: persist the dragged position on end-drag (rare — flush immediately so it survives a
    // crash). See PrefWindowX for why the PlayerPrefs qualification is load-bearing.
    private void SaveWindowPosition(Vector2 pos)
    {
        UnityEngine.PlayerPrefs.SetFloat(PrefWindowX, pos.x);
        UnityEngine.PlayerPrefs.SetFloat(PrefWindowY, pos.y);
        UnityEngine.PlayerPrefs.Save();
    }
}
