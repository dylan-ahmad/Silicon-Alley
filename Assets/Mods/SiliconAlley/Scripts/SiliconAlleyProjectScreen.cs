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

    private TMP_FontAsset _font;
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
    private GameObject _designSection, _developmentSection, _testingSection, _releaseSection;
    // Design section
    private TMP_Text _designQualityText, _leadText, _etaText, _statusText;
    private readonly Image[] _scopeImages = new Image[3];
    private readonly Button[] _scopeButtons = new Button[3];
    private Slider _focusSlider;
    private Button _lockButton;
    // Development section
    private TMP_Text _devThroughputText, _devBuildText, _devEtaText, _overtimeLabel;
    private Image _overtimeImage;
    // Testing section
    private TMP_Text _testBugsText, _testStaffText, _holdLabel;
    private Image _holdImage;
    // Marketing section (issue #21): shown pre-release (Design→Testing); cash-funded awareness campaign.
    private GameObject _marketingSection;
    private TMP_Text _mktAwarenessText, _adSpendLabel;
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

    private static readonly Color PanelColor = new Color(0.086f, 0.098f, 0.125f, 0.98f); // deep navy HUD
    private static readonly Color ButtonColor = new Color(0.18f, 0.21f, 0.27f, 1f);       // slate
    private static readonly Color ButtonSelected = new Color(0.20f, 0.50f, 0.86f, 1f);    // game blue
    private static readonly Color TextColor = new Color(0.90f, 0.92f, 0.96f, 1f);
    private static readonly Color HeaderColor = new Color(0.52f, 0.72f, 1f, 1f);   // section-header accent
    private static readonly Color DividerColor = new Color(1f, 1f, 1f, 0.08f);     // thin separator line

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
            _marketingSection.SetActive(false);
            _publisherSection.SetActive(false);
            _releaseSection.SetActive(false);
            ClampHeight();
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

        // Marketing (issue #21): a pre-release campaign — visible through Design/Development/Testing, hidden
        // once the project ships (Release). Awareness built here scales the launch when the project ships.
        var preRelease = inDesign || inDevelopment || inTesting;
        _marketingSection.SetActive(preRelease);
        if (preRelease)
            RefreshMarketing(reg, key, rawProgress, size);

        // Publisher deal (issue #17/#22/#23): sign/track a publishing deal — pre-release only (nothing to
        // deliver once shipped). Mirrors the marketing gate.
        _publisherSection.SetActive(preRelease);
        if (preRelease)
            RefreshPublisher(reg, businessType, key, rawProgress, size, perHour);

        // Release "ship report" shows independently of the current phase whenever a recent ship exists.
        var report = SiliconAlleyState.GetLastShip(key);
        _releaseSection.SetActive(report.Has);
        if (report.Has)
            RefreshRelease(businessType, key, report);

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

    private void RefreshDesign(BuildingRegistration reg, BusinessType businessType, string key, float size, float rawProgress, float perHour)
    {
        var editable = SiliconAlleyState.CanEditConcept(key);

        var designQ = SiliconAlleyState.GetPhaseQuality(key, SiliconAlleyState.ProjectPhase.Design);
        _designQualityText.text = Compose("siliconalley:screen_designquality",
            ("value", designQ < 0f ? "—" : Pct(designQ) + "%"));

        var hasDesigner = businessType != null
            && System.Array.IndexOf(businessType.employeePrimarySkills, "ba:skill_graphicdesigner") >= 0;
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
        _holdImage.color = held ? ButtonSelected : ButtonColor;
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

    // Issue #21 (Marketing): refresh the campaign block — current awareness/hype, channel costs, and the
    // Ad Spend toggle. Buttons gate on affordability (SiliconAlleyMoney.CanAfford). Press Build calls out
    // that it lands hardest in late Development (the simulator applies the timing bonus on purchase).
    private void RefreshMarketing(BuildingRegistration reg, string key, float rawProgress, float size)
    {
        _mktAwarenessText.text = Compose("siliconalley:screen_mkt_awareness",
            ("awareness", Mathf.RoundToInt(SiliconAlleyState.GetAwareness(key)).ToString(CultureInfo.InvariantCulture)),
            ("hype", Mathf.RoundToInt(SiliconAlleyState.GetHype(key)).ToString(CultureInfo.InvariantCulture)));

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
        _adSpendImage.color = on ? ButtonSelected : ButtonColor;
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
        _font = ResolveFont();

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
        var window = MakeImage(_root.transform, "Window", PanelColor);
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

        // Studio selector: [<]  name  [>]
        var studioRow = MakeRow(root);
        FixWidth(MakeButton(studioRow.transform, "‹", () => CycleStudio(-1)), 44f);
        _studioText = MakeText(studioRow.transform, "Studio", 17, TextAnchor.MiddleCenter);
        FixWidth(MakeButton(studioRow.transform, "›", () => CycleStudio(1)), 44f);

        _phaseText = MakeText(root, "Phase", 16, TextAnchor.MiddleLeft);
        _summaryText = MakeText(root, "Summary", 15, TextAnchor.MiddleLeft);

        // ---- Design section (shown in the Design phase) ----
        _designSection = MakeSection(root);
        MakeDivider(_designSection.transform);
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
        _lockButton = MakeButton(_designSection.transform, "siliconalley:screen_lock".GetLocalization(), OnLock, primary: true);

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

        // ---- Footer (common) ----
        MakeDivider(root);
        var footer = MakeRow(root, 10f, 40);
        MakeButton(footer.transform, "siliconalley:screen_close".GetLocalization(), Close);

        _root.SetActive(false);
    }

    private void MakeHeader(Transform parent, string key)
    {
        var header = MakeText(parent, "Header", 15, TextAnchor.MiddleLeft, FontStyle.Bold);
        header.color = HeaderColor;
        header.text = key.GetLocalization();
    }

    // A thin separator line for visual grouping between sections.
    private void MakeDivider(Transform parent)
    {
        var divider = MakeImage(parent, "Divider", DividerColor);
        var le = divider.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = 2f;
    }

    // Resolve the game's TMP font (Exo2) so our text matches the game's typography. Falls back to any
    // loaded TMP font asset (preferring the "Exo" family) if no project default is set.
    private static TMP_FontAsset ResolveFont()
    {
        if (TMP_Settings.defaultFontAsset != null)
            return TMP_Settings.defaultFontAsset;
        TMP_FontAsset first = null;
        foreach (var fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (fa == null)
                continue;
            first ??= fa;
            if (fa.name.IndexOf("Exo", StringComparison.OrdinalIgnoreCase) >= 0)
                return fa;
        }
        return first;
    }

    private TMP_Text MakeText(Transform parent, string name, int size, TextAnchor anchor, FontStyle style = FontStyle.Normal)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        if (_font != null)
            text.font = _font;
        text.fontSize = size;
        text.fontStyle = style == FontStyle.Bold ? FontStyles.Bold
            : style == FontStyle.Italic ? FontStyles.Italic
            : FontStyles.Normal;
        text.alignment = anchor == TextAnchor.MiddleCenter ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
        text.color = TextColor;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        go.AddComponent<LayoutElement>().minHeight = size + 10;
        return text;
    }

    // A standalone label inside a horizontal row (no button behaviour).
    private TMP_Text MakeTextButtonless(Transform parent, string value)
    {
        var t = MakeText(parent, "Label", 14, TextAnchor.MiddleCenter);
        t.text = value;
        return t;
    }

    private Button MakeButton(Transform parent, string label, UnityAction onClick, bool primary = false)
    {
        var go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = primary ? ButtonSelected : ButtonColor;
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        // Hover/press/disabled feedback. normalColor stays white so each button's own image.color shows
        // through (the scope/overtime/hold buttons recolour their image to signal state); the others tint.
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.pressedColor = new Color(0.66f, 0.66f, 0.66f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 38f;
        le.preferredHeight = 38f;
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
