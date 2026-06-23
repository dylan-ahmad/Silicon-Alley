using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.Items;
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

    private const float WindowWidth = 620f;
    private const float MaxHeight = 940f; // window caps here (at the 1080 reference) and scrolls beyond

    private static readonly int[] ScopeKinds =
    {
        (int)SiliconAlleyState.ProjectKind.Quick,
        (int)SiliconAlleyState.ProjectKind.Standard,
        (int)SiliconAlleyState.ProjectKind.Ambitious,
    };

    private GameObject _root;
    private RectTransform _windowRt, _contentRt; // window = clamped panel; content = scrollable stack
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
    private readonly Image[] _scopeImages = new Image[3];
    private readonly Button[] _scopeButtons = new Button[3];
    private Slider _focusSlider;
    // Summary page (placeholder rows today; sub-issues fill them in)
    private GameObject _summaryPage;
    private TMP_Text _sumScopeText, _sumQualityText, _sumCoverageText, _sumCostsText, _sumRoyaltiesText, _sumMarketText;
    // Features page (issue #26): a fixed pool of toggle buttons (sized to the largest feature table), relabelled
    // and shown/hidden per business type each refresh; bit i toggles the matching FeatureMask bit.
    private GameObject _featuresPage;
    private Button[] _featureButtons;
    private Image[] _featureImages;
    private TMP_Text[] _featureLabels;
    private TMP_Text _featuresReadout;
    // Operating-systems page (issue #37): same toggle-pool pattern as features; bit i toggles a PlatformMask bit.
    private GameObject _platformsPage;
    private Button[] _platformButtons;
    private Image[] _platformImages;
    private TMP_Text[] _platformLabels;
    private TMP_Text _platformsReadout;
    // Editors & tools page (issue #36): one CYCLE button per tool (Off → Licensed → Owned); the studio-level
    // OwnedToolsMask + per-project UsedToolsMask back it. Building (own) charges R&D cash via SiliconAlleyMoney.
    private GameObject _toolsPage;
    private Button[] _toolButtons;
    private Image[] _toolImages;
    private TMP_Text[] _toolLabels;
    private TMP_Text _toolsReadout;
    // Market page (issue #38): single-select audience segment (Broad/Enterprise/Prosumer/Consumer), like the
    // scope buttons; shifts the price↔volume tradeoff. Backed by the per-project SegmentId ordinal.
    private GameObject _marketPage;
    private Button[] _segmentButtons;
    private Image[] _segmentImages;
    private TMP_Text _marketReadout;
    // Dependencies page (issue #39): read-only feature→tool coverage matrix (no input — derived from the
    // Features + Tools choices). One text row per selected feature, covered/uncovered, + a coverage readout.
    private GameObject _dependenciesPage;
    private TMP_Text[] _depRows;
    private TMP_Text _depReadout;
    // Read-only recap shown once the concept is locked (no longer editable)
    private GameObject _wizardRecap;
    private TMP_Text _recapText, _recapStatusText;
    // Nav row: Back · Next/Confirm
    private GameObject _wizardNavRow;
    private Button _wizardBackButton, _wizardNextButton;
    private TMP_Text _wizardNextLabel;
    // Development section
    private TMP_Text _devThroughputText, _devBuildText, _devEtaText, _overtimeLabel;
    private Image _overtimeImage;
    // Testing section
    private TMP_Text _testBugsText, _testStaffText, _holdLabel;
    private Image _holdImage;
    // Marketing section (issue #21): shown pre-release (Design→Testing); cash-funded awareness campaign.
    private GameObject _marketingSection;
    private TMP_Text _mktAwarenessText, _adSpendLabel;
    private TMP_Text _mktSynergyText; // issue #29: free awareness from a player-operated marketing agency
    private Image _adSpendImage;
    private Button _pressReleaseButton, _pressBuildButton, _hypeButton;
    private TMP_Text _pressReleaseLabel, _pressBuildLabel, _hypeLabel;
    // Publisher section (issue #17/#22/#23): shown pre-release; sign a publishing deal or watch its countdown.
    private GameObject _publisherSection;
    private TMP_Text _publisherStatusText;
    private Button[] _publisherButtons;
    private TMP_Text[] _publisherLabels;
    // Release section (transient ship report)
    private TMP_Text _relReviewText, _relQualityText, _relRevenueText, _relRepText, _relSupportText, _relPatchText;
    // Contract section (issue #27): read-only — shown whenever the studio holds an accepted contract.
    private GameObject _contractSection;
    private TMP_Text _contractText;

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
        _wizardPage = 0; // always open the wizard at the first page
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
            _wizardSection.SetActive(false);
            _developmentSection.SetActive(false);
            _testingSection.SetActive(false);
            _marketingSection.SetActive(false);
            _publisherSection.SetActive(false);
            _releaseSection.SetActive(false);
            _contractSection.SetActive(false);
            ClampHeight();
            return;
        }

        var businessType = BusinessTypeHelper.GetData(reg);
        var key = _currentKey;
        // Issue #26: note the type so EffectiveProjectSize (just below) and the wizard readouts are feature-aware.
        SiliconAlleyState.NoteBusinessType(key, businessType?.businessTypeName);
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
        // Issue #55: reflect the current business type + phase as icons next to their labels.
        SetIconSprite(_typeIcon, SiliconAlleyTheme.IconFor(businessType?.businessTypeName));
        SetIconSprite(_phaseIcon, SiliconAlleyTheme.IconFor(SiliconAlleyState.PhaseNameKey(phase)));

        var avgQ = SiliconAlleyState.GetAverageQuality(key);
        _summaryText.text = Compose("siliconalley:screen_summary",
            ("quality", avgQ < 0f ? "—" : Pct(avgQ) + "%"),
            ("shipeta", EtaText(size - rawProgress, perHour)));

        var inDesign = phase == SiliconAlleyState.ProjectPhase.Design;
        var inDevelopment = phase == SiliconAlleyState.ProjectPhase.Development;
        var inTesting = phase == SiliconAlleyState.ProjectPhase.Testing;
        _wizardSection.SetActive(inDesign);
        _developmentSection.SetActive(inDevelopment);
        _testingSection.SetActive(inTesting);
        if (inDesign)
            RefreshWizard(reg, businessType, key, size, rawProgress, perHour);
        else if (inDevelopment)
            RefreshDevelopment(reg, key, size, rawProgress, perHour);
        else if (inTesting)
            RefreshTesting(reg, key, perHour);

        // Marketing (issue #21) + Publisher deal (issue #17/#22/#23): pre-release campaign blocks — visible
        // through Design/Development/Testing, hidden once the project ships (Release). Issue #35: while the
        // concept wizard is still editable they stay hidden so the wizard is a focused flow; they appear once
        // the concept is locked. CanEditConcept is true only in the Design phase, so Development/Testing are
        // unaffected (it is already false there).
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
            RefreshRelease(businessType, key, report);

        // Contract (issue #27): shown whenever the studio holds a contract (it diverts staff from the product).
        var onContract = SiliconAlleyState.HasContract(key);
        _contractSection.SetActive(onContract);
        if (onContract)
            RefreshContract(key);

        ClampHeight();
    }

    // Size the window to its content, capped at MaxHeight (the ScrollRect scrolls beyond the cap).
    private void ClampHeight()
    {
        if (_contentRt == null || _windowRt == null)
            return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRt);
        _windowRt.sizeDelta = new Vector2(WindowWidth, Mathf.Min(_contentRt.rect.height, MaxHeight));
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

        // Hide every page up front; the active one (or the recap) is shown below.
        foreach (var page in _wizardPages)
            page.Root.SetActive(false);

        if (!SiliconAlleyState.CanEditConcept(key))
        {
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
            return;
        _wizardPage = Mathf.Clamp(_wizardPage, 0, _visiblePages.Count - 1);
        var current = _visiblePages[_wizardPage];
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
        _wizardNextLabel.text = (isLast ? "siliconalley:wiz_confirm" : "siliconalley:wiz_next").GetLocalization();
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
        _stepHeaderText = MakeText(_stepIndicator.transform, "StepHeader", 15, TextAnchor.MiddleCenter, FontStyle.Bold);
        _stepHeaderText.color = SiliconAlleyTheme.Header;

        // A centred row of fixed-size dots. MakeRow force-expands its children, so build the row by hand.
        var dotsGo = new GameObject("StepDots", typeof(RectTransform));
        dotsGo.transform.SetParent(_stepIndicator.transform, false);
        var h = dotsGo.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleCenter;
        dotsGo.AddComponent<LayoutElement>().minHeight = 16f;

        _stepDots = new Image[WizardStepCapacity];
        for (var i = 0; i < _stepDots.Length; i++)
        {
            var dot = MakeImage(dotsGo.transform, "Dot", SiliconAlleyTheme.Slate);
            if (SiliconAlleyTheme.ButtonSprite != null) // a small rounded pill (Simple — borders don't fit a 12px dot)
            {
                dot.sprite = SiliconAlleyTheme.ButtonSprite;
                dot.type = Image.Type.Simple;
            }
            var le = dot.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 12f;
            le.minWidth = le.preferredWidth = 12f;
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
        var done = Color.Lerp(SiliconAlleyTheme.Slate, SiliconAlleyTheme.Accent, 0.45f);
        for (var i = 0; i < _stepDots.Length; i++)
        {
            var on = i < count;
            _stepDots[i].gameObject.SetActive(on);
            if (!on)
                continue;
            var le = _stepDots[i].GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = i == _wizardPage ? 18f : 12f; // the current step reads as a wider pill
            _stepDots[i].color = i == _wizardPage ? SiliconAlleyTheme.Accent
                : i < _wizardPage ? done
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
        _conceptNameText.text = Compose("siliconalley:wiz_product", ("product", ProductName(_ctxBusinessType)));

        var designQ = SiliconAlleyState.GetPhaseQuality(key, SiliconAlleyState.ProjectPhase.Design);
        _designQualityText.text = Compose("siliconalley:screen_designquality",
            ("value", designQ < 0f ? "—" : Pct(designQ) + "%"));

        var hasDesigner = _ctxBusinessType != null
            && System.Array.IndexOf(_ctxBusinessType.employeePrimarySkills, "ba:skill_graphicdesigner") >= 0;
        var leadKey = hasDesigner ? "siliconalley:screen_lead_designer" : "siliconalley:screen_lead_programmer";
        _leadText.text = Compose("siliconalley:screen_lead",
            ("lead", leadKey.GetLocalization()), ("staff", CountStaff(_ctxReg).ToString(CultureInfo.InvariantCulture)));

        var remaining = SiliconAlleyState.PhaseEndProgress(SiliconAlleyState.ProjectPhase.Design, _ctxSize) - _ctxProgress;
        _etaText.text = Compose("siliconalley:screen_designeta", ("eta", EtaText(remaining, _ctxPerHour)));

        var currentKind = SiliconAlleyState.GetProjectType(key);
        for (var i = 0; i < 3; i++)
            _scopeImages[i].color = ScopeKinds[i] == currentKind ? SiliconAlleyTheme.Accent : SiliconAlleyTheme.Slate;

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
        for (var i = 0; i < _featureButtons.Length; i++)
        {
            var has = i < feats.Length;
            _featureButtons[i].gameObject.SetActive(has);
            if (!has)
                continue;
            var f = feats[i];
            _featureLabels[i].text = Compose("siliconalley:wiz_feature_row",
                ("name", f.NameKey.GetLocalization()),
                ("size", Mathf.RoundToInt(f.SizeCost * 100f).ToString(CultureInfo.InvariantCulture)),
                ("quality", Mathf.RoundToInt(f.QualityContribution * 100f).ToString(CultureInfo.InvariantCulture)));
            _featureImages[i].color = (mask & (1 << f.Bit)) != 0 ? SiliconAlleyTheme.Accent : SiliconAlleyTheme.Slate;
            SetButtonIcon(_featureButtons[i], SiliconAlleyTheme.IconFor(f.NameKey)); // issue #55: per-feature icon (changes with type)
        }
        _featuresReadout.text = Compose("siliconalley:wiz_features_readout",
            ("size", Mathf.RoundToInt(_ctxSize).ToString(CultureInfo.InvariantCulture)),
            ("eta", EtaText(_ctxSize - _ctxProgress, _ctxPerHour)),
            ("ceiling", Pct(ProjectedCeiling(key)) + "%"));
    }

    // A display estimate of the achievable quality ceiling: the design baseline (clamped, so it reads sensibly
    // before any Design work) raised by the selected features (#26) and the tools used (#36). Matches the
    // simulator's DesignQualityCeiling for a real design quality; purely for the wizard preview.
    private float ProjectedCeiling(string key)
    {
        var type = _ctxBusinessType?.businessTypeName;
        var dq = Mathf.Max(0f, SiliconAlleyState.GetPhaseQuality(key, SiliconAlleyState.ProjectPhase.Design));
        var bonus = SiliconAlleyFeatures.QualityBonus(SiliconAlleyState.GetFeatureMask(key), type)
            + SiliconAlleyTools.QualityBonus(SiliconAlleyState.GetUsedToolsMask(key), type);
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
        for (var i = 0; i < _platformButtons.Length; i++)
        {
            var has = i < plats.Length;
            _platformButtons[i].gameObject.SetActive(has);
            if (!has)
                continue;
            var p = plats[i];
            _platformLabels[i].text = Compose("siliconalley:wiz_platform_row",
                ("name", p.NameKey.GetLocalization()),
                ("share", p.ShareWeight.ToString("0.0", CultureInfo.InvariantCulture)),
                ("scope", Mathf.RoundToInt(p.ScopeCost * 100f).ToString(CultureInfo.InvariantCulture)));
            _platformImages[i].color = (mask & (1 << p.Bit)) != 0 ? SiliconAlleyTheme.Accent : SiliconAlleyTheme.Slate;
            SetButtonIcon(_platformButtons[i], SiliconAlleyTheme.IconFor(p.NameKey)); // issue #55: per-platform icon (changes with type)
        }
        _platformsReadout.text = Compose("siliconalley:wiz_platforms_readout",
            ("market", PlatformMarketText(key, type)),
            ("size", Mathf.RoundToInt(_ctxSize).ToString(CultureInfo.InvariantCulture)),
            ("eta", EtaText(_ctxSize - _ctxProgress, _ctxPerHour)));
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
        for (var i = 0; i < _toolButtons.Length; i++)
        {
            var has = i < tools.Length;
            _toolButtons[i].gameObject.SetActive(has);
            if (!has)
                continue;
            var t = tools[i];
            var owned = SiliconAlleyState.IsToolOwned(key, t.Bit);
            var used = SiliconAlleyState.IsToolUsed(key, t.Bit);
            var quality = Mathf.RoundToInt(t.QualityBonus * 100f).ToString(CultureInfo.InvariantCulture);
            var royalty = Mathf.RoundToInt(t.RoyaltyRate * 100f).ToString(CultureInfo.InvariantCulture);
            var cost = Mathf.RoundToInt(t.BuildCost).ToString(CultureInfo.InvariantCulture);
            string state;
            Color color;
            if (owned)
            {
                state = Compose(used ? "siliconalley:wiz_tool_owned_used" : "siliconalley:wiz_tool_owned_off", ("quality", quality));
                color = used ? SiliconAlleyTheme.Accent : SiliconAlleyTheme.Slate;
            }
            else if (used)
            {
                state = Compose("siliconalley:wiz_tool_licensed",
                    ("vendor", t.LicensorNameKey.GetLocalization()), ("royalty", royalty), ("quality", quality), ("cost", cost));
                color = SiliconAlleyTheme.Warn;
            }
            else
            {
                state = Compose("siliconalley:wiz_tool_off", ("royalty", royalty), ("cost", cost));
                color = SiliconAlleyTheme.Slate;
            }
            _toolLabels[i].text = Compose("siliconalley:wiz_tool_row", ("name", t.NameKey.GetLocalization()), ("state", state));
            _toolImages[i].color = color;
            SetButtonIcon(_toolButtons[i], SiliconAlleyTheme.IconFor(t.NameKey)); // issue #55: per-tool icon (changes with type)
        }
        _toolsReadout.text = Compose("siliconalley:wiz_tools_readout",
            ("quality", Mathf.RoundToInt(SiliconAlleyTools.QualityBonus(SiliconAlleyState.GetUsedToolsMask(key), type) * 100f).ToString(CultureInfo.InvariantCulture)),
            ("royalty", Mathf.RoundToInt(SiliconAlleyState.ToolRoyalty(key, type) * 100f).ToString(CultureInfo.InvariantCulture)),
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

    // Market page (issue #38): single-select audience segment. Recolors the chosen segment (like the scope
    // buttons) and shows its market-size indicator + the price/volume factors it applies.
    private void RefreshMarketPage()
    {
        var key = _currentKey;
        var current = SiliconAlleyState.GetSegmentId(key);
        for (var i = 0; i < _segmentButtons.Length; i++)
            _segmentImages[i].color = i == current ? SiliconAlleyTheme.Accent : SiliconAlleyTheme.Slate;
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
            if ((featureMask & (1 << f.Bit)) == 0 || row >= _depRows.Length)
                continue; // only selected features get a row
            var name = f.NameKey.GetLocalization();
            if (SiliconAlleyDependencies.IsCovered(type, f.Bit, owned, used))
                _depRows[row].text = Compose("siliconalley:wiz_dep_covered", ("name", name));
            else
                _depRows[row].text = Compose("siliconalley:wiz_dep_uncovered",
                    ("name", name), ("tools", ProviderToolNames(type, f.Bit)));
            _depRows[row].gameObject.SetActive(true);
            row++;
        }
        for (; row < _depRows.Length; row++)
            _depRows[row].gameObject.SetActive(false);

        SiliconAlleyDependencies.Coverage(featureMask, owned, used, type, out var covered, out var total);
        _depReadout.text = Compose("siliconalley:wiz_deps_readout",
            ("covered", covered.ToString(CultureInfo.InvariantCulture)),
            ("total", total.ToString(CultureInfo.InvariantCulture)),
            ("ceiling", Pct(ProjectedCeiling(key)) + "%"));
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
        _sumScopeText.text = Compose("siliconalley:wiz_sum_scope",
            ("scope", SiliconAlleyState.ProjectTypeNameKey(kind).GetLocalization()),
            ("size", Mathf.RoundToInt(_ctxSize).ToString(CultureInfo.InvariantCulture)),
            ("eta", EtaText(_ctxSize - _ctxProgress, _ctxPerHour)));

        // Quality ceiling — the design-phase baseline raised by the selected features (issue #26; #36 owned
        // tools will lift it further). Features alone raise it above the 50% baseline, so it's always shown.
        _sumQualityText.text = Compose("siliconalley:wiz_sum_quality",
            ("value", Pct(ProjectedCeiling(key)) + "%"));

        var type = _ctxBusinessType?.businessTypeName;
        // #39 dependencies: the feature→tool coverage figure (uncovered features lowered the ceiling above).
        SiliconAlleyDependencies.Coverage(SiliconAlleyState.GetFeatureMask(key),
            SiliconAlleyState.GetOwnedToolsMask(key), SiliconAlleyState.GetUsedToolsMask(key), type,
            out var covCovered, out var covTotal);
        _sumCoverageText.text = Compose("siliconalley:wiz_sum_coverage",
            ("value", covCovered >= covTotal
                ? "siliconalley:wiz_coverage_full".GetLocalization()
                : Compose("siliconalley:wiz_coverage_value",
                    ("covered", covCovered.ToString(CultureInfo.InvariantCulture)),
                    ("total", covTotal.ToString(CultureInfo.InvariantCulture)))));

        // #36 editors & tools: R&D sunk into the owned tools used here, and the ongoing licensed-tool royalty.
        var ownedRnd = OwnedToolsRnd(key, type);
        _sumCostsText.text = Compose("siliconalley:wiz_sum_costs",
            ("value", ownedRnd <= 0f
                ? "siliconalley:wiz_placeholder_none".GetLocalization()
                : Compose("siliconalley:wiz_cost_value", ("amount", Mathf.RoundToInt(ownedRnd).ToString(CultureInfo.InvariantCulture)))));
        var licensed = LicensedToolCount(key, type);
        _sumRoyaltiesText.text = Compose("siliconalley:wiz_sum_royalties",
            ("value", licensed <= 0
                ? "siliconalley:wiz_placeholder_noroyalties".GetLocalization()
                : Compose("siliconalley:wiz_royalty_value",
                    ("royalty", Mathf.RoundToInt(SiliconAlleyState.ToolRoyalty(key, type) * 100f).ToString(CultureInfo.InvariantCulture)),
                    ("count", licensed.ToString(CultureInfo.InvariantCulture)))));
        // Epic #34: the Summary aggregates the reachable market = platform reach (#37) × segment volume (#38).
        _sumMarketText.text = Compose("siliconalley:wiz_sum_market",
            ("value", MarketSummaryText(key, type)));
    }

    // Read-only recap shown once the concept is locked: the committed scope, focus and quality baseline.
    private void RefreshRecap(string key)
    {
        var kind = SiliconAlleyState.GetProjectType(key);
        var q = SiliconAlleyState.GetPhaseQuality(key, SiliconAlleyState.ProjectPhase.Design);
        if (q < 0f)
            q = SiliconAlleyState.GetAverageQuality(key);
        _recapText.text = Compose("siliconalley:wiz_recap",
            ("scope", SiliconAlleyState.ProjectTypeNameKey(kind).GetLocalization()),
            ("focus", Pct(SiliconAlleyState.GetDesignFocus(key)) + "%"),
            ("quality", q < 0f ? "—" : Pct(q) + "%"));
        _recapStatusText.text = "siliconalley:screen_locked".GetLocalization();
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
        _overtimeImage.color = on ? SiliconAlleyTheme.Accent : SiliconAlleyTheme.Slate;
    }

    private void RefreshTesting(BuildingRegistration reg, string key, float perHour)
    {
        // Issue #19: show the real tracked bug count and the derived 0..100% polish, not a quality proxy.
        var bugs = Mathf.RoundToInt(SiliconAlleyState.GetBugCount(key)).ToString(CultureInfo.InvariantCulture);
        _testBugsText.text = Compose("siliconalley:screen_test_bugs",
            ("bugs", bugs), ("polish", Pct(SiliconAlleyState.GetPolish(key))));
        _testStaffText.text = Compose("siliconalley:screen_test_staff",
            ("staff", CountStaff(reg).ToString(CultureInfo.InvariantCulture)),
            ("perhour", Mathf.RoundToInt(perHour).ToString(CultureInfo.InvariantCulture)));

        var held = SiliconAlleyState.IsHold(key);
        _holdLabel.text = Compose("siliconalley:screen_hold",
            ("state", (held ? "siliconalley:screen_on" : "siliconalley:screen_off").GetLocalization()));
        _holdImage.color = held ? SiliconAlleyTheme.Accent : SiliconAlleyTheme.Slate;
    }

    private void RefreshRelease(BusinessType businessType, string key, SiliconAlleyState.ShipReport report)
    {
        // Issue #20: lead the ship report with the critical-reception score.
        _relReviewText.text = Compose("siliconalley:screen_rel_review",
            ("review", report.Review.ToString("F1", CultureInfo.InvariantCulture)));
        _relQualityText.text = Compose("siliconalley:screen_rel_quality", ("quality", Pct(report.Quality) + "%"));
        _relRevenueText.text = Compose("siliconalley:screen_rel_revenue",
            ("payout", Money(report.Payout)),
            ("repmult", report.RepMult.ToString("F2", CultureInfo.InvariantCulture)),
            ("marketmult", report.MarketMult.ToString("F2", CultureInfo.InvariantCulture)));
        // Issue #24: surface the franchise's version + IP reputation alongside reputation and installed base.
        _relRepText.text = Compose("siliconalley:screen_rel_rep",
            ("reputation", SiliconAlleyState.GetReputation(key).ToString("F2", CultureInfo.InvariantCulture)),
            ("iprep", SiliconAlleyState.GetIpReputation(key).ToString("F2", CultureInfo.InvariantCulture)),
            ("version", "v" + SiliconAlleyState.GetVersion(key).ToString(CultureInfo.InvariantCulture)),
            ("base", SiliconAlleyState.GetInstalledBase(key).ToString(CultureInfo.InvariantCulture)));
        // Issue #25: show support income with the current freshness (declines as the catalog ages).
        _relSupportText.text = Compose("siliconalley:screen_rel_support",
            ("support", SupportPerDay(businessType, key)),
            ("fresh", Pct(SiliconAlleyState.SupportFreshness(key, TimeHelper.CurrentDay)) + "%"));
        _relPatchText.text = Compose("siliconalley:screen_rel_patch", ("patcheta", PatchEta(key)));
    }

    // Issue #27: read-only progress of the studio's accepted contract — % done, days until the deadline, payout.
    private void RefreshContract(string key)
    {
        var scope = SiliconAlleyState.GetContractScope(key);
        var frac = scope > 0f ? Mathf.Clamp01(SiliconAlleyState.GetContractProgress(key) / scope) : 0f;
        var daysLeft = Mathf.Max(0, SiliconAlleyState.GetContractDeadlineDay(key) - TimeHelper.CurrentDay);
        _contractText.text = Compose("siliconalley:screen_contract",
            ("progress", Pct(frac) + "%"),
            ("days", daysLeft.ToString(CultureInfo.InvariantCulture)),
            ("payout", Money(SiliconAlleyState.GetContractPayout(key))));
    }

    // Issue #21 (Marketing): refresh the campaign block — current awareness/hype, channel costs, and the
    // Ad Spend toggle. Buttons gate on affordability (SiliconAlleyMoney.CanAfford). Press Build calls out
    // that it lands hardest in late Development (the simulator applies the timing bonus on purchase).
    private void RefreshMarketing(BuildingRegistration reg, string key, float rawProgress, float size)
    {
        _mktAwarenessText.text = Compose("siliconalley:screen_mkt_awareness",
            ("awareness", Mathf.RoundToInt(SiliconAlleyState.GetAwareness(key)).ToString(CultureInfo.InvariantCulture)),
            ("hype", Mathf.RoundToInt(SiliconAlleyState.GetHype(key)).ToString(CultureInfo.InvariantCulture)));

        // Issue #29: surface the free awareness from a player-operated marketing agency (hidden when none owned).
        var agencies = SiliconAlleyOfficeSimulator.OwnedMarketingAgencies();
        _mktSynergyText.gameObject.SetActive(agencies > 0);
        if (agencies > 0)
            _mktSynergyText.text = Compose("siliconalley:screen_mkt_synergy",
                ("rate", (agencies * SiliconAlleyOfficeSimulator.MarketingSynergyAwarenessPerHour).ToString("0.0", CultureInfo.InvariantCulture)),
                ("count", agencies.ToString(CultureInfo.InvariantCulture)));

        _pressReleaseLabel.text = Compose("siliconalley:screen_mkt_press_release", ("cost", Money(SiliconAlleyState.PressReleaseCost)));
        _pressBuildLabel.text = Compose("siliconalley:screen_mkt_press_build", ("cost", Money(SiliconAlleyState.PressBuildCost)));
        _hypeLabel.text = Compose("siliconalley:screen_mkt_hype", ("cost", Money(SiliconAlleyState.HypeCost)));

        _pressReleaseButton.interactable = SiliconAlleyMoney.CanAfford(reg, SiliconAlleyState.PressReleaseCost);
        _pressBuildButton.interactable = SiliconAlleyMoney.CanAfford(reg, SiliconAlleyState.PressBuildCost);
        _hypeButton.interactable = SiliconAlleyMoney.CanAfford(reg, SiliconAlleyState.HypeCost);

        var on = SiliconAlleyState.IsAdSpend(key);
        _adSpendLabel.text = Compose("siliconalley:screen_mkt_adspend",
            ("state", (on ? "siliconalley:screen_on" : "siliconalley:screen_off").GetLocalization()),
            ("cost", Money(SiliconAlleyState.AdSpendCostPerHour)));
        _adSpendImage.color = on ? SiliconAlleyTheme.Accent : SiliconAlleyTheme.Slate;
    }

    // Issue #17/#22/#23 (Publishers): with no active deal, show one sign button per ELIGIBLE publisher (focus
    // match or generalist), labelled with the live offer (payout / deadline days / your reputation); ineligible
    // publishers' buttons are hidden. With an active deal, hide the buttons and show the publisher + a live
    // day-countdown to the deadline, the ship ETA and the locked payout.
    private void RefreshPublisher(BuildingRegistration reg, BusinessType businessType, string key, float rawProgress, float size, float perHour)
    {
        if (SiliconAlleyState.HasDeal(key))
        {
            for (int i = 0; i < _publisherButtons.Length; i++)
                _publisherButtons[i].gameObject.SetActive(false);
            var pub = SiliconAlleyState.GetDealPublisher(key);
            var name = SiliconAlleyPublishers.TryGetById(pub, out var publisher) ? publisher.NameKey.GetLocalization() : "";
            var daysLeft = SiliconAlleyState.GetDealDeadlineDay(key) - TimeHelper.CurrentDay;
            var deadline = daysLeft < 0
                ? "siliconalley:client_eta_due".GetLocalization()
                : "~" + daysLeft.ToString(CultureInfo.InvariantCulture) + "d";
            _publisherStatusText.text = Compose("siliconalley:screen_pub_active",
                ("publisher", name),
                ("deadline", deadline),
                ("shipeta", EtaText(size - rawProgress, perHour)),
                ("payout", Money(SiliconAlleyState.GetDealPayout(key))));
            return;
        }

        _publisherStatusText.text = "siliconalley:screen_pub_none".GetLocalization();
        var marketPrice = MarketPrice(businessType);
        var businessTypeName = reg.businessTypeName;
        var roster = SiliconAlleyPublishers.Roster;
        for (int i = 0; i < roster.Length; i++)
        {
            var pub = roster[i];
            var eligible = SiliconAlleyPublishers.IsEligible(pub, businessTypeName);
            _publisherButtons[i].gameObject.SetActive(eligible);
            if (!eligible)
                continue;
            var rep = SiliconAlleyState.GetPublisherRep(pub.Index);
            SiliconAlleyPublishers.OfferFor(pub, businessTypeName, marketPrice, rep,
                out var days, out var payout, out _, out _);
            _publisherLabels[i].text = Compose("siliconalley:screen_pub_sign",
                ("publisher", pub.NameKey.GetLocalization()),
                ("payout", Money(payout)),
                ("deadline", days.ToString(CultureInfo.InvariantCulture)),
                ("rep", rep.ToString("F1", CultureInfo.InvariantCulture)));
        }
    }

    // ---- control callbacks -------------------------------------------------------------------------

    private void OnScopeSelected(int kind)
    {
        SiliconAlleyState.SetScope(_currentKey, kind);
        Refresh();
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

    // Issue #35: wizard navigation. Back steps to the previous page; Next advances, and on the last (Summary)
    // page it doubles as Confirm — committing the concept via LockConcept (mirrors the old Lock button).
    private void OnWizardBack()
    {
        if (_wizardPage > 0)
            _wizardPage--;
        Refresh();
    }

    private void OnWizardNext()
    {
        RebuildVisiblePages();
        if (_visiblePages.Count == 0)
        {
            Refresh();
            return;
        }
        if (_wizardPage >= _visiblePages.Count - 1)
            SiliconAlleyState.LockConcept(_currentKey); // Confirm on the Summary page
        else
            _wizardPage++;
        Refresh();
    }

    // Issue #26: toggle the feature shown in this Features-page slot for the current business type. The slot
    // index maps to the type's feature list at refresh time, so the bit toggled is always the right one.
    private void OnToggleFeature(int slot)
    {
        var feats = SiliconAlleyFeatures.FeaturesFor(_ctxBusinessType?.businessTypeName);
        if (slot >= 0 && slot < feats.Length)
            SiliconAlleyState.ToggleFeature(_currentKey, feats[slot].Bit);
        Refresh();
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

    private void OnStartNext()
    {
        SiliconAlleyState.ClearLastShip(_currentKey);
        Refresh();
    }

    // ---- issue #21 (Marketing) callbacks -----------------------------------------------------------

    private void OnPressRelease()
    {
        BuyCampaign(SiliconAlleyState.PressReleaseCost, SiliconAlleyState.PressReleaseAwareness, 0f, "siliconalley:mkt_name_pressrelease");
    }

    // Press Build is strongest fired late in Development (~the back half of the build); off-window it
    // delivers a fraction of its awareness. Timing factor 0.4..1.0 across the project.
    private void OnPressBuild()
    {
        var fraction = SiliconAlleyState.GetProgress(_currentKey) / Mathf.Max(1f, SiliconAlleyState.EffectiveProjectSize(_currentKey));
        var timing = (fraction >= 0.5f && fraction <= 0.72f) ? 1f : 0.4f;
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
        Refresh();
    }

    private void CycleStudio(int delta)
    {
        if (_studioKeys.Count == 0)
            return;
        var idx = Mathf.Max(0, _studioKeys.IndexOf(_currentKey));
        idx = (idx + delta + _studioKeys.Count) % _studioKeys.Count;
        _currentKey = _studioKeys[idx];
        _wizardPage = 0; // each studio opens its wizard at the first page
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

    private static string Money(float amount) =>
        "$" + Mathf.RoundToInt(amount).ToString("N0", CultureInfo.InvariantCulture);

    // Market price of the business's primary product (drives publisher offer math); 0 if none.
    private static float MarketPrice(BusinessType businessType)
    {
        if (businessType?.businessProducts == null || businessType.businessProducts.Length == 0)
            return 0f;
        var item = ItemsGetter.GetByName(businessType.businessProducts[0].itemName);
        return item != null ? item.DefaultMarketPrice : 0f;
    }

    // Estimated recurring support income per day — mirrors the phone dashboard (SiliconAlleyClient).
    private static string SupportPerDay(BusinessType businessType, string key)
    {
        var installed = SiliconAlleyState.GetInstalledBase(key);
        var perDay = 0f;
        if (installed > 0 && businessType?.businessProducts != null && businessType.businessProducts.Length > 0)
        {
            var item = ItemsGetter.GetByName(businessType.businessProducts[0].itemName);
            if (item != null)
                perDay = installed * item.DefaultMarketPrice * SiliconAlleyState.SupportRatePerDay;
        }
        return Money(perDay) + "/day";
    }

    // Days until the next post-release patch — mirrors the phone dashboard. "—" before anything ships.
    private static string PatchEta(string key)
    {
        if (SiliconAlleyState.GetInstalledBase(key) <= 0)
            return "—";
        var days = SiliconAlleyOfficeSimulator.PatchIntervalDays - (TimeHelper.CurrentDay - SiliconAlleyState.GetLastPatchDay(key));
        return days <= 0
            ? "siliconalley:client_eta_due".GetLocalization()
            : "~" + days.ToString(CultureInfo.InvariantCulture) + "d";
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

        // Window: fixed-width panel, centred, height clamped each Refresh; hosts a vertical ScrollRect so
        // tall content (e.g. the ship report + Design stacked) scrolls instead of overflowing the screen.
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
        // Pin horizontally to the viewport so content width == viewport width (otherwise an uninitialized
        // sizeDelta.x leaves the content wider than the viewport and the left edge gets clipped).
        _contentRt.offsetMin = new Vector2(0f, _contentRt.offsetMin.y);
        _contentRt.offsetMax = new Vector2(0f, _contentRt.offsetMax.y);
        _contentRt.anchoredPosition = Vector2.zero;
        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(26, 26, 22, 22);
        layout.spacing = 11f;
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
        _titleText = MakeText(titleRow.transform, "Title", 22, TextAnchor.MiddleLeft, FontStyle.Bold);
        FixWidth(MakeButton(titleRow.transform, "X", Close), 34f);

        // Studio selector: [<]  [type icon] name  [>]
        var studioRow = MakeRow(root);
        FixWidth(MakeButton(studioRow.transform, "‹", () => CycleStudio(-1)), 44f);
        _typeIcon = MakeIcon(studioRow.transform, null, 22f, SiliconAlleyTheme.Text); // issue #55: current business-type icon
        _studioText = MakeText(studioRow.transform, "Studio", 17, TextAnchor.MiddleCenter);
        FixWidth(MakeButton(studioRow.transform, "›", () => CycleStudio(1)), 44f);

        // Phase indicator: [phase icon] phase + progress (issue #55 adds the icon).
        var phaseRow = MakeRow(root, 6f, 24);
        _phaseIcon = MakeIcon(phaseRow.transform, null, 20f, SiliconAlleyTheme.Header);
        _phaseText = MakeText(phaseRow.transform, "Phase", 16, TextAnchor.MiddleLeft);
        _summaryText = MakeText(root, "Summary", 15, TextAnchor.MiddleLeft);

        // ---- Design wizard (issue #35): paged Concept → … → Summary, shown in the Design phase ----
        _wizardSection = MakeSection(root);
        MakeDivider(_wizardSection.transform);
        BuildStepIndicator(_wizardSection.transform); // issue #56: step indicator above the active page

        // Concept page: today's scope + focus controls, read-only product name and baseline readouts.
        _conceptPage = MakeSection(_wizardSection.transform);
        MakeHeader(_conceptPage.transform, "siliconalley:screen_scope");
        var scopeRow = MakeRow(_conceptPage.transform);
        var scopeKeys = new[] { "siliconalley:projecttype_quick", "siliconalley:projecttype_standard", "siliconalley:projecttype_ambitious" };
        for (var i = 0; i < 3; i++)
        {
            var kind = ScopeKinds[i];
            var btn = MakeButton(scopeRow.transform, scopeKeys[i].GetLocalization(), () => OnScopeSelected(kind));
            _scopeButtons[i] = btn;
            _scopeImages[i] = btn.GetComponent<Image>();
            SetButtonIcon(btn, SiliconAlleyTheme.IconFor(scopeKeys[i])); // issue #55: scope icon (fixed set)
        }
        _conceptNameText = MakeText(_conceptPage.transform, "ConceptName", 15, TextAnchor.MiddleLeft);
        MakeHeader(_conceptPage.transform, "siliconalley:screen_focus");
        var focusRow = MakeRow(_conceptPage.transform, 10f, 28);
        FixWidth(MakeTextButtonless(focusRow.transform, "siliconalley:screen_focus_polish".GetLocalization()), 70f);
        _focusSlider = MakeSlider(focusRow.transform);
        _focusSlider.onValueChanged.AddListener(OnFocusChanged);
        FixWidth(MakeTextButtonless(focusRow.transform, "siliconalley:screen_focus_speed".GetLocalization()), 70f);
        _designQualityText = MakeText(_conceptPage.transform, "DesignQuality", 16, TextAnchor.MiddleLeft);
        _leadText = MakeText(_conceptPage.transform, "Lead", 15, TextAnchor.MiddleLeft);
        _etaText = MakeText(_conceptPage.transform, "Eta", 15, TextAnchor.MiddleLeft);

        // Summary page: read-only review aggregated before commit (placeholder rows sub-issues fill in).
        _summaryPage = MakeSection(_wizardSection.transform);
        MakeHeader(_summaryPage.transform, "siliconalley:wiz_summary_header");
        _sumScopeText = MakeText(_summaryPage.transform, "SumScope", 15, TextAnchor.MiddleLeft);
        _sumQualityText = MakeText(_summaryPage.transform, "SumQuality", 15, TextAnchor.MiddleLeft);
        _sumCoverageText = MakeText(_summaryPage.transform, "SumCoverage", 15, TextAnchor.MiddleLeft);
        _sumCostsText = MakeText(_summaryPage.transform, "SumCosts", 15, TextAnchor.MiddleLeft);
        _sumRoyaltiesText = MakeText(_summaryPage.transform, "SumRoyalties", 15, TextAnchor.MiddleLeft);
        _sumMarketText = MakeText(_summaryPage.transform, "SumMarket", 15, TextAnchor.MiddleLeft);

        // Features page (issue #26): the design-document feature picker. A reusable pool of toggle buttons,
        // sized to the largest feature table; RefreshFeaturesPage relabels + shows the current type's list.
        _featuresPage = MakeSection(_wizardSection.transform);
        MakeHeader(_featuresPage.transform, "siliconalley:wiz_features_header");
        var featureSlots = SiliconAlleyFeatures.MaxCount;
        _featureButtons = new Button[featureSlots];
        _featureImages = new Image[featureSlots];
        _featureLabels = new TMP_Text[featureSlots];
        for (var i = 0; i < featureSlots; i++)
        {
            var slot = i; // capture per-slot index for the toggle closure (the bit is resolved at click time)
            var btn = MakeButton(_featuresPage.transform, "", () => OnToggleFeature(slot));
            _featureButtons[i] = btn;
            _featureImages[i] = btn.GetComponent<Image>();
            _featureLabels[i] = btn.GetComponentInChildren<TMP_Text>();
        }
        _featuresReadout = MakeText(_featuresPage.transform, "FeaturesReadout", 14, TextAnchor.MiddleLeft, FontStyle.Italic);

        // Operating-systems page (issue #37): the platform checklist. Same reusable toggle-button pool as the
        // features page, sized to the largest platform table; RefreshPlatformsPage relabels + shows the type.
        _platformsPage = MakeSection(_wizardSection.transform);
        MakeHeader(_platformsPage.transform, "siliconalley:wiz_platforms_header");
        var platformSlots = SiliconAlleyPlatforms.MaxCount;
        _platformButtons = new Button[platformSlots];
        _platformImages = new Image[platformSlots];
        _platformLabels = new TMP_Text[platformSlots];
        for (var i = 0; i < platformSlots; i++)
        {
            var slot = i; // capture per-slot index for the toggle closure (the bit is resolved at click time)
            var btn = MakeButton(_platformsPage.transform, "", () => OnTogglePlatform(slot));
            _platformButtons[i] = btn;
            _platformImages[i] = btn.GetComponent<Image>();
            _platformLabels[i] = btn.GetComponentInChildren<TMP_Text>();
        }
        _platformsReadout = MakeText(_platformsPage.transform, "PlatformsReadout", 14, TextAnchor.MiddleLeft, FontStyle.Italic);

        // Editors & tools page (issue #36): the dependency catalog. A reusable pool of CYCLE buttons (one per
        // tool: Off → Licensed → Owned), sized to the largest tool table; RefreshToolsPage relabels per type.
        _toolsPage = MakeSection(_wizardSection.transform);
        MakeHeader(_toolsPage.transform, "siliconalley:wiz_tools_header");
        var toolSlots = SiliconAlleyTools.MaxCount;
        _toolButtons = new Button[toolSlots];
        _toolImages = new Image[toolSlots];
        _toolLabels = new TMP_Text[toolSlots];
        for (var i = 0; i < toolSlots; i++)
        {
            var slot = i; // capture per-slot index for the cycle closure (the bit is resolved at click time)
            var btn = MakeButton(_toolsPage.transform, "", () => OnCycleTool(slot));
            _toolButtons[i] = btn;
            _toolImages[i] = btn.GetComponent<Image>();
            _toolLabels[i] = btn.GetComponentInChildren<TMP_Text>();
        }
        _toolsReadout = MakeText(_toolsPage.transform, "ToolsReadout", 14, TextAnchor.MiddleLeft, FontStyle.Italic);

        // Market page (issue #38): single-select audience segment (price↔volume). One button per segment, like
        // the scope buttons; RefreshMarketPage recolors the chosen one and shows its market-size + factors.
        _marketPage = MakeSection(_wizardSection.transform);
        MakeHeader(_marketPage.transform, "siliconalley:wiz_market_header");
        var segmentCount = SiliconAlleySegments.Count;
        _segmentButtons = new Button[segmentCount];
        _segmentImages = new Image[segmentCount];
        var segmentRow = MakeRow(_marketPage.transform);
        for (var i = 0; i < segmentCount; i++)
        {
            var ordinal = i; // capture per-segment ordinal for the select closure
            var btn = MakeButton(segmentRow.transform, SiliconAlleySegments.All[i].NameKey.GetLocalization(), () => OnSelectSegment(ordinal));
            _segmentButtons[i] = btn;
            _segmentImages[i] = btn.GetComponent<Image>();
            SetButtonIcon(btn, SiliconAlleyTheme.IconFor(SiliconAlleySegments.All[i].NameKey)); // issue #55: segment icon (fixed set)
        }
        _marketReadout = MakeText(_marketPage.transform, "MarketReadout", 14, TextAnchor.MiddleLeft, FontStyle.Italic);

        // Dependencies page (issue #39): read-only feature→tool coverage. A pool of text rows (one per selected
        // feature, sized to the largest feature table), relabelled covered/uncovered each refresh; no input.
        _dependenciesPage = MakeSection(_wizardSection.transform);
        MakeHeader(_dependenciesPage.transform, "siliconalley:wiz_deps_header");
        _depRows = new TMP_Text[SiliconAlleyFeatures.MaxCount];
        for (var i = 0; i < _depRows.Length; i++)
            _depRows[i] = MakeText(_dependenciesPage.transform, "DepRow", 14, TextAnchor.MiddleLeft);
        _depReadout = MakeText(_dependenciesPage.transform, "DepReadout", 14, TextAnchor.MiddleLeft, FontStyle.Italic);

        // Register the wizard pages. RebuildVisiblePages sorts by Order (canonical step), so the order of these
        // calls doesn't matter — each sibling just registers with its step number and gates with IsPresent.
        _wizardPages.Add(new WizardPage { Order = 0, Root = _conceptPage, IsPresent = () => true, Refresh = RefreshConceptPage, TitleKey = "siliconalley:wiz_step_concept" });
        _wizardPages.Add(new WizardPage { Order = 100, Root = _summaryPage, IsPresent = () => true, Refresh = RefreshSummaryPage, TitleKey = "siliconalley:wiz_step_summary" });
        // Issue #26: Features (step 2), present for any business type that has a feature list.
        _wizardPages.Add(new WizardPage
        {
            Order = 10,
            Root = _featuresPage,
            IsPresent = () => SiliconAlleyFeatures.FeaturesFor(_ctxBusinessType?.businessTypeName).Length > 0,
            Refresh = RefreshFeaturesPage,
            TitleKey = "siliconalley:wiz_step_features",
        });
        // Issue #36: Editors & tools (step 3).
        _wizardPages.Add(new WizardPage
        {
            Order = 20,
            Root = _toolsPage,
            IsPresent = () => SiliconAlleyTools.ToolsFor(_ctxBusinessType?.businessTypeName).Length > 0,
            Refresh = RefreshToolsPage,
            TitleKey = "siliconalley:wiz_step_tools",
        });
        // Issue #39: Dependencies (step 3b) — only once features are selected (else there's nothing to cover).
        _wizardPages.Add(new WizardPage
        {
            Order = 25,
            Root = _dependenciesPage,
            IsPresent = () => SiliconAlleyState.GetFeatureMask(_currentKey) != 0,
            Refresh = RefreshDependenciesPage,
            TitleKey = "siliconalley:wiz_step_deps",
        });
        // Issue #37: Operating systems (step 4).
        _wizardPages.Add(new WizardPage
        {
            Order = 30,
            Root = _platformsPage,
            IsPresent = () => SiliconAlleyPlatforms.PlatformsFor(_ctxBusinessType?.businessTypeName).Length > 0,
            Refresh = RefreshPlatformsPage,
            TitleKey = "siliconalley:wiz_step_platforms",
        });
        // Issue #38: Market / audience segment (step 5). Segments are universal, so the page is always present.
        _wizardPages.Add(new WizardPage { Order = 40, Root = _marketPage, IsPresent = () => true, Refresh = RefreshMarketPage, TitleKey = "siliconalley:wiz_step_market" });

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
        _recapText = MakeText(_wizardRecap.transform, "Recap", 15, TextAnchor.MiddleLeft);
        _recapStatusText = MakeText(_wizardRecap.transform, "RecapStatus", 14, TextAnchor.MiddleLeft, FontStyle.Italic);

        // ---- Development section (shown in the Development phase) ----
        _developmentSection = MakeSection(root);
        MakeDivider(_developmentSection.transform);
        _devThroughputText = MakeText(_developmentSection.transform, "DevThroughput", 15, TextAnchor.MiddleLeft);
        _devBuildText = MakeText(_developmentSection.transform, "DevBuild", 16, TextAnchor.MiddleLeft);
        _devEtaText = MakeText(_developmentSection.transform, "DevEta", 15, TextAnchor.MiddleLeft);
        var overtimeButton = MakeButton(_developmentSection.transform, "", OnToggleOvertime);
        _overtimeImage = overtimeButton.GetComponent<Image>();
        _overtimeLabel = overtimeButton.GetComponentInChildren<TMP_Text>();

        // ---- Testing section (shown in the Testing phase) ----
        _testingSection = MakeSection(root);
        MakeDivider(_testingSection.transform);
        _testBugsText = MakeText(_testingSection.transform, "TestBugs", 16, TextAnchor.MiddleLeft);
        _testStaffText = MakeText(_testingSection.transform, "TestStaff", 15, TextAnchor.MiddleLeft);
        var testRow = MakeRow(_testingSection.transform, 10f, 40);
        var holdButton = MakeButton(testRow.transform, "", OnToggleHold);
        _holdImage = holdButton.GetComponent<Image>();
        _holdLabel = holdButton.GetComponentInChildren<TMP_Text>();
        MakeButton(testRow.transform, "siliconalley:screen_ship".GetLocalization(), OnShipNow, primary: true);

        // ---- Marketing section (issue #21; shown in any pre-release phase) ----
        _marketingSection = MakeSection(root);
        MakeDivider(_marketingSection.transform);
        MakeHeader(_marketingSection.transform, "siliconalley:screen_mkt_header");
        _mktAwarenessText = MakeText(_marketingSection.transform, "MktAwareness", 16, TextAnchor.MiddleLeft);
        _mktSynergyText = MakeText(_marketingSection.transform, "MktSynergy", 14, TextAnchor.MiddleLeft, FontStyle.Italic); // #29
        _pressReleaseButton = MakeButton(_marketingSection.transform, "", OnPressRelease);
        _pressReleaseLabel = _pressReleaseButton.GetComponentInChildren<TMP_Text>();
        _pressBuildButton = MakeButton(_marketingSection.transform, "", OnPressBuild);
        _pressBuildLabel = _pressBuildButton.GetComponentInChildren<TMP_Text>();
        _hypeButton = MakeButton(_marketingSection.transform, "", OnHype);
        _hypeLabel = _hypeButton.GetComponentInChildren<TMP_Text>();
        var adSpendButton = MakeButton(_marketingSection.transform, "", OnToggleAdSpend);
        _adSpendImage = adSpendButton.GetComponent<Image>();
        _adSpendLabel = adSpendButton.GetComponentInChildren<TMP_Text>();

        // ---- Publisher section (issue #17/#22/#23; shown in any pre-release phase) ----
        _publisherSection = MakeSection(root);
        MakeDivider(_publisherSection.transform);
        MakeHeader(_publisherSection.transform, "siliconalley:screen_pub_header");
        _publisherStatusText = MakeText(_publisherSection.transform, "PubStatus", 15, TextAnchor.MiddleLeft);
        var roster = SiliconAlleyPublishers.Roster;
        _publisherButtons = new Button[roster.Length];
        _publisherLabels = new TMP_Text[roster.Length];
        for (int i = 0; i < roster.Length; i++)
        {
            var index = i; // capture a stable copy for the click closure
            _publisherButtons[i] = MakeButton(_publisherSection.transform, "", () => OnSignDeal(index));
            _publisherLabels[i] = _publisherButtons[i].GetComponentInChildren<TMP_Text>();
        }

        // ---- Release section (transient ship report; shown independently of phase) ----
        _releaseSection = MakeSection(root);
        MakeDivider(_releaseSection.transform);
        MakeHeader(_releaseSection.transform, "siliconalley:screen_rel_header");
        _relReviewText = MakeText(_releaseSection.transform, "RelReview", 17, TextAnchor.MiddleLeft, FontStyle.Bold);
        _relQualityText = MakeText(_releaseSection.transform, "RelQuality", 16, TextAnchor.MiddleLeft);
        _relRevenueText = MakeText(_releaseSection.transform, "RelRevenue", 15, TextAnchor.MiddleLeft);
        _relRepText = MakeText(_releaseSection.transform, "RelRep", 15, TextAnchor.MiddleLeft);
        _relSupportText = MakeText(_releaseSection.transform, "RelSupport", 15, TextAnchor.MiddleLeft);
        _relPatchText = MakeText(_releaseSection.transform, "RelPatch", 15, TextAnchor.MiddleLeft);
        MakeButton(_releaseSection.transform, "siliconalley:screen_startnext".GetLocalization(), OnStartNext, primary: true);

        // ---- Contract section (issue #27): read-only progress of an accepted contract ----
        _contractSection = MakeSection(root);
        MakeDivider(_contractSection.transform);
        MakeHeader(_contractSection.transform, "siliconalley:screen_contract_header");
        _contractText = MakeText(_contractSection.transform, "Contract", 15, TextAnchor.MiddleLeft);

        // ---- Footer (common) ----
        MakeDivider(root);
        var footer = MakeRow(root, 10f, 40);
        MakeButton(footer.transform, "siliconalley:screen_close".GetLocalization(), Close);

        _root.SetActive(false);
    }
}
