using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BigAmbitions.Characters.Skills;
using BigAmbitions.DayNightCycle;
using BigAmbitions.Items;
using BigAmbitions.Tags;
using Buildings.BuildingTypes.Shared.Dirtiness;
using Entities;
using Helpers;
using Helpers.BusinessSimulation;
using Localizor;
using UI.Notification;
using UnityEngine;

// Tier 2/3: software-project business simulator (mod-defined, assigned in code by SiliconAlleyMod).
// Each sim hour, programmers working at a workstation accrue project progress; completed projects
// pay quality/reputation-scaled revenue through the normal order path (so it shows as business
// revenue), and an installed base earns recurring support income. See docs/DESIGN.md.
public class SiliconAlleyOfficeSimulator : BusinessSimulator
{
    // Post-release updates: a staffed studio patches its live catalog this often, earning a fraction
    // of each product's market price per installed unit.
    // public so the phone dashboard can show "next update in N days" off the same schedule.
    public const int PatchIntervalDays = 7;
    private const float PatchRevenueFraction = 0.08f;

    // Issue #23 (Publisher deals): fire a clickable "deadline approaching" warning when an active deal has
    // this many in-game days or fewer left before delivery is due.
    private const int DealWarnDays = 2;

    // Issue #27 (Contracts): an on-time delivery pays ContractPayout scaled by staffing quality on this curve
    // (a bare-bones job still pays half, a well-staffed one full); a missed deadline dents reputation by this.
    private const float ContractQualityFloor = 0.5f;   // payout = ContractPayout * (Floor + (1-Floor)*quality)
    private const float ContractMissRepPenalty = 0.5f; // reputation lost (toward 0) when a contract deadline lapses

    // Issue #29 (Marketing synergy): free awareness/hour each player-operated marketing agency adds to every IT
    // studio (vs the cash AdSpendAwarenessPerHour 0.6 — running your own marketing is cheaper than buying it).
    // Public so the project screen can show the same per-hour synergy the simulator applies.
    public const float MarketingSynergyAwarenessPerHour = 0.5f;
    // The base-game marketing-agency business type the game tags such buildings with (decompiled: the game's own
    // "is this a marketing agency?" check uses exactly this businessTypeName).
    private const string MarketingAgencyTypeName = "ba:businesstype_marketingagency";

    // Issue #2: the two existing game skills the businesses draw on. Programmers lead Development/Testing;
    // graphic designers lead the Design phase (for the business types that list the designer skill).
    private const string ProgrammerSkill = "ba:skill_programmer";
    private const string GraphicDesignerSkill = "ba:skill_graphicdesigner";

    public const string ServerItemName = "siliconalley:itemname_server";

    public static bool IsServerInstance(ItemInstance instance)
        => instance != null && instance.ItemCached != null && instance.ItemCached.itemName == ServerItemName;

    public override void SimulateCurrentHour()
    {
        var businessType = BusinessTypeHelper.GetData(buildingRegistration);
        if (businessType == null || !businessType.HasTag(TagRef.Businesstag.generatesrevenue))
            return;

        var key = SiliconAlleyState.KeyFor(buildingRegistration);
        // Issue #26: record the business type so the per-type feature math (project size + quality ceiling)
        // can resolve this project's feature list before EffectiveProjectSize is read just below.
        SiliconAlleyState.NoteBusinessType(key, businessType.businessTypeName);

        // 1) Gather the staff working at a workstation this hour, with each person's programmer and
        // graphic-designer skill (issue #2). Only disciplines this business actually lists count.
        var staff = new List<(float programmer, float designer, float satisfaction)>();
        foreach (var instance in buildingRegistration.itemInstances.Values)
        {
            if ((instance.ItemCached.type & ItemType.EmployeeWorkstation) == 0)
                continue;
            var employee = EmployeeHelper.GetEmployeeAtStationAndHour(buildingRegistration, instance.id, currentHour);
            if (employee == null)
                continue;
            var programmer = SkillValue(employee, ProgrammerSkill, businessType);
            var designer = SkillValue(employee, GraphicDesignerSkill, businessType);
            if (programmer <= 0f && designer <= 0f)
                continue; // not a discipline this business uses
            staff.Add((programmer, designer, employee.satisfaction));
        }
        var staffCount = staff.Count;

        // Issue #27: an accepted contract diverts the studio — its staff work the contract and the product is
        // paused (Progress + type-lock untouched) until the contract delivers or its deadline passes. Captured
        // once so the product work is skipped for the whole hour even if the contract resolves mid-hour.
        var onContract = SiliconAlleyState.HasContract(key);
        if (onContract)
        {
            if (TimeHelper.CurrentDay > SiliconAlleyState.GetContractDeadlineDay(key))
                HandleContractMiss(businessType, key);     // deadline passed before delivery (works even unstaffed)
            else if (staffCount > 0)
                WorkContract(businessType, key, staff);    // accrue work; pay + clear on an on-time delivery
        }

        // Issue #3: lock the project type when work begins; the locked type scales size/payout/competition.
        var kind = (staffCount > 0 && !onContract) ? SiliconAlleyState.EnsureProjectTypeLocked(key) : SiliconAlleyState.GetProjectType(key);
        var size = SiliconAlleyState.EffectiveProjectSize(key);
        // Issue #88 (player-driven lifecycle): the studio's stage gates the work. Idle ⇒ no product work at
        // all; an active stage works only up to its ceiling, then PARKS until the player pushes forward
        // (Start development = confirm wizard; Send to testing; Release). The ceiling moves up per stage.
        var stage = SiliconAlleyState.GetStage(key);
        var ceiling = SiliconAlleyState.StageCeiling(stage, size);

        // 2) Accrue progress, phase-weighted by discipline (issue #2): graphic designers drive the Design
        // phase, programmers drive Development/Testing; the off-discipline cross-skills at a reduced rate.
        float effectiveSkill = 0f;
        float totalSatisfaction = 0f;
        // Work only while the studio has an active stage AND this stage isn't parked yet. Idle ⇒ skipped
        // (no product work); a stage parked at its ceiling ⇒ skipped (frozen, waiting for the player's push).
        if (staffCount > 0 && !onContract && stage != SiliconAlleyState.ProjectStage.Idle
            && SiliconAlleyState.GetProgress(key) < ceiling)
        {
            var progressBefore = SiliconAlleyState.GetProgress(key);
            var phase = SiliconAlleyState.PhaseOf(progressBefore, size);
            var hasDesigner = businessType.employeePrimarySkills.Contains(GraphicDesignerSkill);
            GetPhaseWeights(phase, hasDesigner, businessType.businessTypeName, out var designerWeight, out var programmerWeight);
            foreach (var member in staff)
            {
                effectiveSkill += Mathf.Max(member.designer * designerWeight, member.programmer * programmerWeight);
                totalSatisfaction += member.satisfaction;
            }

            // Issue #9/#10: per-phase player controls scale this hour's progress and quality; both default
            // to neutral (1x), so untouched studios and legacy saves are unchanged.
            //  - Design: the Design focus trades progress speed against the design-quality baseline.
            //  - Development: an Overtime policy rushes the build at a quality cost.
            float progressScale = 1f, qualityScale = 1f;
            if (phase == SiliconAlleyState.ProjectPhase.Design)
            {
                var focus = SiliconAlleyState.GetDesignFocus(key);
                progressScale = 1f + (focus - 0.5f) * 0.5f; // 0.75x (polish) .. 1.25x (speed)
                qualityScale = 1f - (focus - 0.5f) * 0.4f;  // 1.2x (polish) .. 0.8x (speed)
                // Nudge the player once per project to set the concept (unless they already locked it).
                if (!SiliconAlleyState.IsConceptLocked(key) && SiliconAlleyState.TryMarkDesignPrompted(key))
                    AnnounceDesignPrompt(businessType, key);
            }
            else if (phase == SiliconAlleyState.ProjectPhase.Development && SiliconAlleyState.IsOvertime(key))
            {
                progressScale = 1.5f;  // rush the build ...
                qualityScale = 0.85f;  // ... at the cost of quality (more bugs surface in Testing)
            }

            var progressDelta = effectiveSkill * SiliconAlleyState.ProjectSpeed * progressScale;
            SiliconAlleyState.AddProgress(key, progressDelta);
            // Issue #88: a stage never auto-advances. The hour it fills the current stage counts (final work),
            // then it PARKS at the stage ceiling (Progress clamped); the guard on this block above freezes
            // quality/bugs from here until the player pushes the stage forward.
            SiliconAlleyState.ParkBelowCeiling(key, ceiling);
            var progressAfter = SiliconAlleyState.GetProgress(key);
            AnnouncePhaseTransition(businessType, key, progressBefore, progressAfter, size);
            // The hour the stage parks (fills to its ceiling), nudge the player how to push it forward (once):
            // Testing ⇒ "ready to release"; Development ⇒ "build done — send to QA or release".
            if (progressBefore < ceiling && progressAfter >= ceiling)
            {
                if (stage == SiliconAlleyState.ProjectStage.Testing)
                    AnnounceReadyToRelease(businessType, key);
                else if (stage == SiliconAlleyState.ProjectStage.Development)
                    AnnounceDevelopmentDone(businessType, key);
            }
            // Step 3 (quality): sample this hour's effective staff quality; Testing-phase work counts double.
            var hourQuality = Mathf.Clamp01(effectiveSkill / staffCount / 100f) * Mathf.Clamp01(totalSatisfaction / staffCount / 100f) * qualityScale;
            var phaseWeight = phase == SiliconAlleyState.ProjectPhase.Testing ? 2f : 1f;
            SiliconAlleyState.AccumulateQuality(key, phase, hourQuality, phaseWeight);

            // Issue #19 (Bugs): code written in Development introduces bugs (more under Overtime); Testing
            // (and Hold/QA) burns them down at a rate set by the staff working it. So skipping QA ships a
            // buggy build, while a held/well-staffed Testing phase clears them before release.
            if (phase == SiliconAlleyState.ProjectPhase.Development)
            {
                var bugRate = SiliconAlleyState.BugsPerProgress * (SiliconAlleyState.IsOvertime(key) ? SiliconAlleyState.OvertimeBugFactor : 1f);
                SiliconAlleyState.AddBugs(key, progressDelta * bugRate);
            }
            else if (phase == SiliconAlleyState.ProjectPhase.Testing)
            {
                SiliconAlleyState.BurnBugs(key, effectiveSkill * SiliconAlleyState.BugFixPerSkillHour);
            }
            Debug.Log($"[SiliconAlley] {key} h{currentHour}: {staffCount} staff, {SiliconAlleyState.PhaseOf(progressAfter, size)} progress {progressAfter:F0}/{size:F0}");
        }
        else if (stage != SiliconAlleyState.ProjectStage.Idle && (staffCount == 0 || onContract))
        {
            // An ACTIVE project left unstaffed or diverted to a contract stagnates — the product line slips.
            // (Idle by choice, and a stage parked-with-staff awaiting the player's push, are NOT penalised.)
            SiliconAlleyState.DecayReputation(key, 0.001f);
        }
        else if (stage == SiliconAlleyState.ProjectStage.Idle && staffCount > 0 && !onContract)
        {
            // Idle with staff on hand: the studio does nothing until the player starts a project. Nudge them
            // once (reuses the per-project DesignPrompted flag, cleared by StartProject / on a ship).
            if (SiliconAlleyState.TryMarkDesignPrompted(key))
                AnnounceStartProject(businessType, key);
        }

        // Issue #21 (Marketing): run the cash-funded "Ad Spend" channel and decay awareness/hype each hour.
        // Ad Spend buys steady awareness while the player keeps it on; if the studio can't pay, it auto-off.
        if (SiliconAlleyState.IsAdSpend(key))
        {
            if (SiliconAlleyMoney.TrySpend(buildingRegistration, SiliconAlleyState.AdSpendCostPerHour, "ad spend"))
                SiliconAlleyState.AddAwareness(key, SiliconAlleyState.AdSpendAwarenessPerHour);
            else
                SiliconAlleyState.SetAdSpend(key, false);
        }
        // Issue #29 (Marketing synergy): if the player also operates marketing-agency businesses, they promote
        // this studio's products for FREE — a small awareness/hour per agency, no per-hour cash (cheaper than Ad
        // Spend). Derived from building ownership (no state); 0 agencies ⇒ +0 ⇒ unchanged. Layered on top.
        var agencies = OwnedMarketingAgencies();
        if (agencies > 0)
            SiliconAlleyState.AddAwareness(key, MarketingSynergyAwarenessPerHour * agencies);
        SiliconAlleyState.DecayMarketing(key); // no-op for an unmarketed/legacy studio (awareness 0)

        // Issue #23 (Publisher deals): enforce the deadline every hour, independent of staffing/Hold, so a
        // stalled, held or under-staffed project still resolves. Past the deadline with the product not yet
        // shipped ⇒ the deal is missed (publisher-reputation penalty + a clear), and a clickable warning fires
        // as the deadline nears. On-time delivery is rewarded in the completion loop below. No-op without a deal.
        if (SiliconAlleyState.HasDeal(key))
        {
            var dealPub = SiliconAlleyState.GetDealPublisher(key);
            var daysLeft = SiliconAlleyState.GetDealDeadlineDay(key) - TimeHelper.CurrentDay;
            if (daysLeft < 0) // past the deadline day with nothing delivered: a miss
            {
                SiliconAlleyState.AddPublisherRep(dealPub, -SiliconAlleyPublishers.RepPenalty);
                SiliconAlleyState.ClearDeal(key);
                ShowDealFailedNotification(key, dealPub);
            }
            else if (daysLeft <= DealWarnDays)
            {
                ShowDealWarningNotification(key, dealPub, daysLeft);
            }
        }

        var product = PrimaryProduct(businessType);
        var marketPrice = product != null ? MarketPrice(product) : 0f;

        // 3) Recurring support income from the installed base.
        if (product != null && marketPrice > 0f)
        {
            var support = SiliconAlleyState.AccrueSupport(key, marketPrice, TimeHelper.CurrentDay);
            // Issue #36: licensed tools take a recurring royalty cut of support income too (0 when no tool is
            // licensed / legacy save, so support is unchanged). Layered on top — SupportRatePerDay is untouched.
            support *= 1f - SiliconAlleyState.ToolRoyalty(key, businessType.businessTypeName);
            support *= 1f - SiliconAlleyState.DependencySupportRoyalty(key, businessType.businessTypeName);
            // Issue #28: recurring support breathes with the category's market demand too (a new factor; the
            // competition MarketFactor / SupportRatePerDay are untouched). Derived from the day — no state.
            support *= SiliconAlleyMarket.DemandFactor(businessType.businessTypeName, TimeHelper.CurrentDay);
            if (support > 0f)
                CreditRevenue(product, support, 1f);
        }

        // 3b) Post-release updates: MANUAL (Software-Inc-style). A staffed studio with a live catalog can ship
        // an update every PatchIntervalDays for extra revenue — but only when the PLAYER releases it (the
        // screen sets SiliconAlleyState.RequestUpdate). The request is consumed here once an update is due.
        if (staffCount > 0 && product != null && marketPrice > 0f && SiliconAlleyState.IsUpdateRequested(key))
        {
            var catalog = SiliconAlleyState.GetInstalledBase(key);
            if (catalog > 0 && TimeHelper.CurrentDay - SiliconAlleyState.GetLastPatchDay(key) >= PatchIntervalDays)
            {
                var patchRevenue = marketPrice * catalog * PatchRevenueFraction * MarketFactor(buildingRegistration, kind)
                    * SiliconAlleyMarket.DemandFactor(businessType.businessTypeName, TimeHelper.CurrentDay); // issue #28: dynamic demand
                CreditRevenue(product, patchRevenue, 1f);
                SiliconAlleyState.SetLastPatchDay(key, TimeHelper.CurrentDay);
                AnnouncePatch(businessType, key, catalog, patchRevenue);
                SiliconAlleyState.ClearUpdateRequest(key); // one request ⇒ one shipped update
            }
        }

        // 4) Release the current product — MANUAL (Software-Inc-style). The player can release ANY moment in
        // Development or Testing (SiliconAlleyState.RequestRelease, set by the screen's Release button); the
        // product ships at its CURRENT accrued quality, so an early release reviews worse. Ships once per
        // request, then the studio returns to Idle (OnProjectCompleted sets Stage=Idle + Progress=0). Does NOT
        // require staff this hour, so a queued release still completes outside working hours.
        if (product != null && marketPrice > 0f && SiliconAlleyState.IsReleaseRequested(key)
            && (stage == SiliconAlleyState.ProjectStage.Development || stage == SiliconAlleyState.ProjectStage.Testing))
        {
            var projectKind = SiliconAlleyState.GetProjectType(key);
            var cleanliness = Mathf.Clamp01(buildingRegistration.GetCleanliness() / 100f);
            // Step 3 (quality): ship at the quality accrued across all phases (Testing weighted heavier),
            // not just the final hour's staffing. Fall back to this hour if nothing has accrued.
            var accruedQuality = SiliconAlleyState.GetAverageQuality(key);
            if (accruedQuality < 0f)
                accruedQuality = Mathf.Clamp01(effectiveSkill / Mathf.Max(1, staffCount) / 100f) * Mathf.Clamp01(totalSatisfaction / Mathf.Max(1, staffCount) / 100f);
            // Issue #9: a weak Design phase caps the shipped quality even if later phases were strong — the
            // design baseline sets the ceiling. Skipped when no Design quality accrued (legacy saves), so
            // old in-flight projects are unaffected.
            var designQuality = SiliconAlleyState.GetPhaseQuality(key, SiliconAlleyState.ProjectPhase.Design);
            // Issue #26: the selected features raise this ceiling. The helper returns 1.0 (no cap) when no
            // Design quality accrued (legacy saves), so old in-flight projects are unaffected, and adds 0 when
            // no features are selected — identical to the previous 0.5 + 0.5*designQuality cap.
            accruedQuality = Mathf.Min(accruedQuality,
                SiliconAlleyState.DesignQualityCeiling(key, businessType.businessTypeName, designQuality, TimeHelper.CurrentDay));
            // Issue #19: residual bugs cut the shipped quality (so the existing payout/reputation path
            // reflects them). Clean/legacy builds (no tracked bugs) keep BugQualityFactor == 1 → unchanged.
            var quality = accruedQuality * Mathf.Max(0.25f, cleanliness) * SiliconAlleyState.BugQualityFactor(key);
            // Issue #20 (Reviews) / #21 (Marketing): derive the 0..10 review and the marketing-scaled launch
            // size BEFORE completing (OnProjectCompleted resets awareness). Awareness 0 (legacy) ⇒ bonus 0 ⇒
            // launch adds exactly +1, identical to before.
            var awareness = SiliconAlleyState.GetAwareness(key);
            var review = SiliconAlleyState.ComputeReviewScore(quality, designQuality, awareness);
            // Issue #24 (Sequels): a sequel (v2+) leverages the franchise's prior installed base + IP rep for
            // extra launch units, on top of the marketing-driven bonus (#21). Both are 0 for a debut/legacy
            // ship, so the launch is unchanged. Read the version being shipped BEFORE OnProjectCompleted bumps it.
            var version = SiliconAlleyState.GetVersion(key);
            var releaseProductName = SiliconAlleyState.GetProductName(key);
            var releaseDisplayName = SiliconAlleyState.GetProductNameOrDefault(key, ProductDisplayName(businessType));
            var launchBonus = SiliconAlleyState.LaunchBonusUnits(key, review) + SiliconAlleyState.SequelLaunchUnits(key, review);
            var reputationFactor = 0.75f + SiliconAlleyState.GetReputation(key);
            var marketFactor = MarketFactor(buildingRegistration, projectKind);
            var payout = marketPrice * (0.5f + quality) * reputationFactor * marketFactor * SiliconAlleyState.PayoutMultiplier * SiliconAlleyState.PayoutMultiplierFor(projectKind);
            // Issue #36: licensed tools take a royalty cut of the launch revenue (0 when no tool is licensed /
            // legacy, so payout is unchanged). Reduces the NET payout the player sees in the toast + ship report;
            // layered on top — MarketFactor / reputationFactor / the project-kind multiplier are untouched.
            payout *= 1f - SiliconAlleyState.LaunchRoyalty(key, businessType.businessTypeName);
            // Issue #38: the target audience segment scales the per-unit launch price (Broad ⇒ ×1.0, so a
            // legacy/default ship is unchanged). A new multiplier layered on top — MarketFactor / reputationFactor
            // / the project-kind multiplier are untouched; the volume side feeds the installed base below.
            payout *= SiliconAlleyState.SegmentPriceFactor(key);
            // Issue #28: the category's current market demand scales the launch revenue too — a new factor
            // layered on top (the competition MarketFactor is untouched), derived from the day so it matches the
            // dashboard. Bounded ±Amplitude, so the same product earns more in a hot market and less in a cold one.
            var demand = SiliconAlleyMarket.DemandFactor(businessType.businessTypeName, TimeHelper.CurrentDay);
            payout *= demand;
            // Issue #85 (Market targeting): how well the player's per-feature allocation fits the current aspect
            // demand scales the launch revenue + installed-base jump. ×1.0 at neutral/legacy weights (the fit is
            // measured relative to the even allocation), so old saves are unchanged. See SiliconAlleyAspects.
            var marketFit = SiliconAlleyAspects.MarketFitFactor(SiliconAlleyState.GetFeatureMask(key),
                SiliconAlleyState.GetFeatureWeights(key), businessType.businessTypeName, TimeHelper.CurrentDay);
            payout *= marketFit;
            CreditRevenue(product, payout, quality);
            var releasePublisher = SiliconAlleyState.HasDeal(key) ? SiliconAlleyState.GetDealPublisher(key) : -1;
            // Issue #23 (Publisher deals): if this product was under a deal, fulfil it on this ship. On-time
            // (shipped on/before the deadline day) pays the locked bonus ON TOP of the normal payout, scaled by
            // the release's review (a buggy/late delivery is worth less to a publisher), and builds reputation
            // with that publisher. A late ship was already penalised + cleared by the hourly check above, so it
            // simply has no deal here. Cleared per-iteration ⇒ a multi-ship catch-up tick fulfils one release.
            if (SiliconAlleyState.HasDeal(key))
            {
                var dealPub = SiliconAlleyState.GetDealPublisher(key);
                var qualityFactor = Mathf.Clamp01(0.4f + 0.6f * (review / 10f));
                if (TimeHelper.CurrentDay <= SiliconAlleyState.GetDealDeadlineDay(key)
                    && SiliconAlleyPublishers.TryGetById(dealPub, out var publisher))
                {
                    var dealPayout = SiliconAlleyState.GetDealPayout(key) * qualityFactor;
                    CreditRevenue(product, dealPayout, quality);
                    var repReward = SiliconAlleyPublishers.RepRewardBase + publisher.Tier * SiliconAlleyPublishers.RepRewardTier;
                    SiliconAlleyState.AddPublisherRep(dealPub, repReward * qualityFactor);
                    ShowDealCompleteNotification(key, dealPub, dealPayout);
                }
                SiliconAlleyState.ClearDeal(key);
            }
            // Issue #37: the target platforms widen the launch installed-base jump (reach = Σ selected share
            // weights; 1.0 for the single home platform, so a legacy/default launch is unchanged). Layered on the
            // launch units only — payout/MarketFactor untouched. OnProjectCompleted still floors at Max(1, …).
            var reach = SiliconAlleyState.LaunchReach(key, businessType.businessTypeName);
            // Issue #38: the audience segment's volume factor scales the installed-base jump too (Broad ⇒ ×1.0).
            // A mass segment grows the base (more recurring support); a niche segment shrinks it — the volume
            // side of the price↔volume tradeoff. SupportRatePerDay is untouched.
            var launchUnits = Mathf.RoundToInt((1 + launchBonus) * reach * SiliconAlleyState.SegmentVolumeFactor(key) * marketFit); // issue #85: market-fit scales the installed-base jump too (×1.0 at neutral)
            SiliconAlleyState.OnProjectCompleted(key, quality, launchUnits, review, TimeHelper.CurrentDay, payout,
                releasePublisher, releaseProductName);
            SiliconAlleyState.SetLastPatchDay(key, TimeHelper.CurrentDay); // a fresh release resets the patch clock + support freshness (#25)
            Debug.Log($"[SiliconAlley] {key} completed v{version} {(SiliconAlleyState.ProjectKind)projectKind} project (quality {quality:F2}, review {review:F1}/10, payout {payout:F0}, market demand x{demand:F2}, +{launchUnits} installed, reputation {SiliconAlleyState.GetReputation(key):F2}, IP rep {SiliconAlleyState.GetIpReputation(key):F2}).");
            ShowProjectCompleteNotification(businessType, key, quality, payout, reputationFactor, marketFactor, review, version, releaseDisplayName);
            // Issue #12: remember this ship so the screen can show a "ship report" (transient).
            SiliconAlleyState.SetLastShip(key, quality, payout, reputationFactor, marketFactor, review, releaseDisplayName);
            // Manual release: consume the request so exactly one product ships per Release click. The studio
            // is now Idle (OnProjectCompleted), so a stale flag couldn't ship anything anyway, but clear it.
            SiliconAlleyState.ClearReleaseRequest(key);
        }
    }

    // Manual updates (UI): is a post-launch update due for this studio's live catalog? (installed base + the
    // PatchIntervalDays timer elapsed). The player ships it via SiliconAlleyState.RequestUpdate; the hourly
    // tick (3b) credits it. Mirrors the gate there so the "Release update" button shows exactly when valid.
    public static bool IsUpdateDue(string key)
    {
        return SiliconAlleyState.GetInstalledBase(key) > 0
            && TimeHelper.CurrentDay - SiliconAlleyState.GetLastPatchDay(key) >= PatchIntervalDays;
    }

    // The revenue a released update would credit right now (same formula the hourly tick uses), so the screen
    // can label the "Release update ($X)" button. 0 when nothing is live / no product. Self-contained so it
    // keeps PatchRevenueFraction private and the formula in one place.
    public static float EstimateUpdateRevenue(BuildingRegistration registration, BusinessType businessType, string key)
    {
        if (registration == null || businessType == null)
            return 0f;
        var product = PrimaryProduct(businessType);
        var marketPrice = product != null ? MarketPrice(product) : 0f;
        var catalog = SiliconAlleyState.GetInstalledBase(key);
        if (marketPrice <= 0f || catalog <= 0)
            return 0f;
        var kind = SiliconAlleyState.GetProjectType(key);
        return marketPrice * catalog * PatchRevenueFraction * MarketFactor(registration, kind)
            * SiliconAlleyMarket.DemandFactor(businessType.businessTypeName, TimeHelper.CurrentDay);
    }

    public override void OnTimeMachineEnd(BuildingRegistration registration)
    {
    }

    // Credit revenue the same way OfficeBusinessSimulator does: a completed, paid order.
    private void CreditRevenue(string itemName, float price, float quality)
    {
        var order = new Order
        {
            completed = true,
            cleanliness = buildingRegistration.GetCleanliness(),
            customerServiceSkill = quality * 100f,
            // customerDemandScore is derived below from the real building state (see comment there).
            timestamp = new Timestamp(TimeHelper.CurrentDay, currentHour, 0f),
        };
        order.entries.Add(new OrderEntry
        {
            itemName = itemName,
            price = price,
            available = true,
            paid = true,
            priceAccceptable = true,
        });
        // The business panel's "Interior" bar binds to satisfaction.facility, which the game derives as
        // the average customerDemandScore over recent orders — on a 0-100 scale (an order with no demand
        // types scores 100). We earlier hardcoded 1f thinking it was 0-1, which pinned Interior at 1.
        // Instead, copy the business type's demand types onto the order and let the game score them against
        // its own cachedFulfilledCustomerDemands (interior-design demand counts as fulfilled once the
        // interior score >= the neighbourhood minimum), so the Interior bar tracks the real office decor.
        var businessType = BusinessTypeHelper.GetData(buildingRegistration);
        if (businessType?.customerDemandSets != null)
        {
            foreach (var demandSet in businessType.customerDemandSets)
                order.customerDemandTypes.Add(demandSet.type);
        }
        order.customerDemandScore = OrderHelper.CalculateOrderDemandScore(order, buildingRegistration);
        buildingRegistration.unprocessedCompletedOrders.Add(order);
    }

    // Step 1 (visibility): announce a completed project to the player via the game's notification
    // system (the same path the base game uses for business events). Values mirror the Debug.Log
    // above; numbers use InvariantCulture (dev machine is nl-NL). duplicateIdentifier = key coalesces
    // a burst of same-business completions (e.g. during time-machine catch-up) into a single toast,
    // while completions in normal play still each show.
    private void ShowProjectCompleteNotification(BusinessType businessType, string key, float quality, float payout,
        float reputationFactor, float marketFactor, float review, int version, string productName)
    {
        var data = new Dictionary<string, string>
        {
            ["business"] = buildingRegistration.GetDisplayName(),
            ["product"] = string.IsNullOrWhiteSpace(productName) ? ProductDisplayName(businessType) : productName,
            // Issue #24: the version that just shipped (v1 = debut, v2+ = sequel).
            ["version"] = "v" + version.ToString(CultureInfo.InvariantCulture),
            ["quality"] = Mathf.RoundToInt(Mathf.Clamp01(quality) * 100f).ToString(CultureInfo.InvariantCulture) + "%",
            ["payout"] = "$" + Mathf.RoundToInt(payout).ToString("N0", CultureInfo.InvariantCulture),
            // Show why the payout is what it is: reputation lifts it, neighborhood competition trims it.
            ["repmult"] = reputationFactor.ToString("F2", CultureInfo.InvariantCulture),
            ["marketmult"] = marketFactor.ToString("F2", CultureInfo.InvariantCulture),
            // Issue #20: the critical-reception score (0..10) the release earned.
            ["review"] = review.ToString("F1", CultureInfo.InvariantCulture),
        };
        Notifications.Show(NotificationType.Success, "siliconalley:notify_projectcomplete", data, 6f, key,
            () => SiliconAlleyProjectScreen.Open(key));
    }

    // Step 2 (lifecycle): announce entry into Development or Testing. Release is announced by the
    // completion toast above; Design is the implicit start of each project (shown in the dashboard).
    // duplicateIdentifier is per business + phase so each transition shows once.
    private void AnnouncePhaseTransition(BusinessType businessType, string key, float before, float after, float size)
    {
        var oldPhase = SiliconAlleyState.PhaseOf(before, size);
        var newPhase = SiliconAlleyState.PhaseOf(after, size);
        if (newPhase <= oldPhase || newPhase == SiliconAlleyState.ProjectPhase.Release)
            return;
        var data = new Dictionary<string, string>
        {
            ["business"] = buildingRegistration.GetDisplayName(),
            ["product"] = ProductDisplayName(key, businessType),
            ["phase"] = SiliconAlleyState.PhaseNameKey(newPhase).GetLocalization(),
        };
        Notifications.Show(NotificationType.Info, "siliconalley:notify_phase", data, 5f, key + ":" + newPhase,
            () => SiliconAlleyProjectScreen.Open(key));
    }

    // Issue #9: nudge the player to open the Design screen and set the concept for a fresh project. The
    // toast is clickable — clicking it opens the project screen focused on this studio. Fired once per
    // project (gated by SiliconAlleyState.TryMarkDesignPrompted), so it never spams.
    private void AnnounceDesignPrompt(BusinessType businessType, string key)
    {
        var data = new Dictionary<string, string>
        {
            ["business"] = buildingRegistration.GetDisplayName(),
            ["product"] = ProductDisplayName(key, businessType),
        };
        Notifications.Show(NotificationType.Info, "siliconalley:notify_design", data, 6f, key + ":design",
            () => SiliconAlleyProjectScreen.Open(key));
    }

    // Issue #88: tell the player a product has reached 100% (Testing parked) and awaits their Release action.
    // Fired once when Testing parks. Clickable → opens the project screen.
    private void AnnounceReadyToRelease(BusinessType businessType, string key)
    {
        var data = new Dictionary<string, string>
        {
            ["business"] = buildingRegistration.GetDisplayName(),
            ["product"] = ProductDisplayName(key, businessType),
        };
        Notifications.Show(NotificationType.Info, "siliconalley:notify_ready", data, 6f, key + ":ready",
            () => SiliconAlleyProjectScreen.Open(key));
    }

    // Issue #88: the build parked at the end of Development — nudge the player to send it to QA or release.
    private void AnnounceDevelopmentDone(BusinessType businessType, string key)
    {
        var data = new Dictionary<string, string>
        {
            ["business"] = buildingRegistration.GetDisplayName(),
            ["product"] = ProductDisplayName(key, businessType),
        };
        Notifications.Show(NotificationType.Info, "siliconalley:notify_devdone", data, 6f, key + ":devdone",
            () => SiliconAlleyProjectScreen.Open(key));
    }

    // Issue #88: the studio is idle with staff on hand — nudge the player to start the next project/version.
    private void AnnounceStartProject(BusinessType businessType, string key)
    {
        var data = new Dictionary<string, string>
        {
            ["business"] = buildingRegistration.GetDisplayName(),
            ["product"] = ProductDisplayName(key, businessType),
        };
        Notifications.Show(NotificationType.Info, "siliconalley:notify_startproject", data, 6f, key + ":start",
            () => SiliconAlleyProjectScreen.Open(key));
    }

    // Step 3 (support/updates): announce a periodic patch shipped to the studio's live catalog.
    private void AnnouncePatch(BusinessType businessType, string key, int catalog, float revenue)
    {
        var data = new Dictionary<string, string>
        {
            ["business"] = buildingRegistration.GetDisplayName(),
            ["product"] = ProductDisplayName(key, businessType),
            ["catalog"] = catalog.ToString(CultureInfo.InvariantCulture),
            ["revenue"] = "$" + Mathf.RoundToInt(revenue).ToString("N0", CultureInfo.InvariantCulture),
        };
        Notifications.Show(NotificationType.Info, "siliconalley:notify_patch", data, 5f, key + ":patch",
            () => SiliconAlleyProjectScreen.Open(key));
    }

    // Issue #23 (Publisher deals): the deal-event toasts. All clickable → open the project screen, and
    // deduplicated per studio+event so a repeated hourly warning coalesces into one toast.
    private void ShowDealCompleteNotification(string key, int publisherIndex, float payout)
    {
        var data = new Dictionary<string, string>
        {
            ["business"] = buildingRegistration.GetDisplayName(),
            ["publisher"] = PublisherName(publisherIndex),
            ["payout"] = "$" + Mathf.RoundToInt(payout).ToString("N0", CultureInfo.InvariantCulture),
        };
        Notifications.Show(NotificationType.Success, "siliconalley:notify_dealdone", data, 6f, key + ":dealdone",
            () => SiliconAlleyProjectScreen.Open(key));
    }

    private void ShowDealFailedNotification(string key, int publisherIndex)
    {
        var data = new Dictionary<string, string>
        {
            ["business"] = buildingRegistration.GetDisplayName(),
            ["publisher"] = PublisherName(publisherIndex),
        };
        Notifications.Show(NotificationType.Warning, "siliconalley:notify_dealfail", data, 6f, key + ":dealfail",
            () => SiliconAlleyProjectScreen.Open(key));
    }

    private void ShowDealWarningNotification(string key, int publisherIndex, int daysLeft)
    {
        var data = new Dictionary<string, string>
        {
            ["business"] = buildingRegistration.GetDisplayName(),
            ["publisher"] = PublisherName(publisherIndex),
            ["days"] = daysLeft.ToString(CultureInfo.InvariantCulture),
        };
        Notifications.Show(NotificationType.Warning, "siliconalley:notify_dealwarn", data, 5f, key + ":dealwarn",
            () => SiliconAlleyProjectScreen.Open(key));
    }

    // Issue #27 (Contracts): a staffed studio holding a contract works it instead of its product. Accrue staff
    // skill toward the scope; on reaching it (the expiry check upstream already cleared any late contract, so
    // arriving here is on time) pay the agreed sum scaled by staffing quality, then clear the contract.
    private void WorkContract(BusinessType businessType, string key,
        List<(float programmer, float designer, float satisfaction)> staff)
    {
        var staffCount = Mathf.Max(1, staff.Count);
        float skill = 0f, satisfaction = 0f;
        foreach (var member in staff)
        {
            skill += Mathf.Max(member.programmer, member.designer);
            satisfaction += member.satisfaction;
        }
        SiliconAlleyState.AddContractProgress(key, skill * SiliconAlleyState.ProjectSpeed);
        if (SiliconAlleyState.GetContractProgress(key) < SiliconAlleyState.GetContractScope(key))
            return;

        var quality = Mathf.Clamp01(skill / staffCount / 100f) * Mathf.Clamp01(satisfaction / staffCount / 100f);
        var payout = SiliconAlleyState.GetContractPayout(key) * (ContractQualityFloor + (1f - ContractQualityFloor) * quality);
        var product = PrimaryProduct(businessType);
        if (product != null)
            CreditRevenue(product, payout, quality);
        Debug.Log($"[SiliconAlley] {key} delivered a contract (quality {quality:F2}, payout {payout:F0}).");
        ShowContractCompleteNotification(key, payout);
        SiliconAlleyState.ClearContract(key);
    }

    // The contract's deadline passed before delivery: no pay, a small reputation dent, and clear it so the
    // studio's product resumes next hour.
    private void HandleContractMiss(BusinessType businessType, string key)
    {
        SiliconAlleyState.PenalizeReputation(key, ContractMissRepPenalty);
        SiliconAlleyState.ClearContract(key);
        ShowContractMissNotification(key);
        Debug.Log($"[SiliconAlley] {key} missed a contract deadline (reputation -{ContractMissRepPenalty:F1}).");
    }

    private void ShowContractCompleteNotification(string key, float payout)
    {
        var data = new Dictionary<string, string>
        {
            ["business"] = buildingRegistration.GetDisplayName(),
            ["payout"] = "$" + Mathf.RoundToInt(payout).ToString("N0", CultureInfo.InvariantCulture),
        };
        Notifications.Show(NotificationType.Success, "siliconalley:notify_contractdone", data, 6f, key + ":contractdone",
            () => SiliconAlleyProjectScreen.Open(key));
    }

    private void ShowContractMissNotification(string key)
    {
        var data = new Dictionary<string, string> { ["business"] = buildingRegistration.GetDisplayName() };
        Notifications.Show(NotificationType.Warning, "siliconalley:notify_contractfail", data, 6f, key + ":contractfail",
            () => SiliconAlleyProjectScreen.Open(key));
    }

    // Localized publisher display name for a roster ordinal, or "" if it can't be resolved (defensive).
    private static string PublisherName(int publisherIndex)
        => SiliconAlleyPublishers.TryGetById(publisherIndex, out var publisher) ? publisher.NameKey.GetLocalization() : "";

    // Localized display name of the business's primary product (themes the toast per business type:
    // a Game Studio ships "Video Game", a Cyber Security Firm a "Security Audit", etc.).
    private static string ProductDisplayName(BusinessType businessType)
    {
        var product = PrimaryProduct(businessType);
        return product != null ? product.GetLocalization() : "project";
    }

    private static string ProductDisplayName(string key, BusinessType businessType) =>
        SiliconAlleyState.GetProductNameOrDefault(key, ProductDisplayName(businessType));

    private static string PrimaryProduct(BusinessType businessType)
    {
        if (businessType?.businessProducts == null || businessType.businessProducts.Length == 0)
            return null;
        return businessType.businessProducts[0].itemName;
    }

    private static float MarketPrice(string itemName)
    {
        var item = ItemsGetter.GetByName(itemName);
        return item != null ? item.DefaultMarketPrice : 0f;
    }

    // Issue #2: an employee's value for a skill, but only if the business actually uses that discipline
    // (so Cyber Security never counts graphic-design skill). 0 when the skill is absent or unused.
    private static float SkillValue(EmployeeInstance employee, string skillName, BusinessType businessType)
    {
        if (!businessType.employeePrimarySkills.Contains(skillName))
            return 0f;
        var skill = employee.characterData.skills.FirstOrDefault(s => s.name == skillName);
        return skill != null ? skill.value : 0f;
    }

    // Issue #2: who drives each phase. Design is led by graphic designers (full rate) with programmers
    // cross-skilling at a per-type rate; a business with no design discipline (Cyber) has programmers
    // plan the design at full rate. Development/Testing are programmer-led, designers cross-skill at 0.5.
    private static void GetPhaseWeights(SiliconAlleyState.ProjectPhase phase, bool hasDesigner, string businessTypeName, out float designerWeight, out float programmerWeight)
    {
        if (phase == SiliconAlleyState.ProjectPhase.Design)
        {
            designerWeight = hasDesigner ? 1f : 0f;
            programmerWeight = hasDesigner ? DesignProgrammerCrossRate(businessTypeName) : 1f;
        }
        else
        {
            designerWeight = 0.5f;
            programmerWeight = 1f;
        }
    }

    // How well programmers cross-skill into the Design phase, per business character: a Game Studio leans
    // hard on designers (programmers weak at art), a Software Studio treats design as optional polish.
    private static float DesignProgrammerCrossRate(string businessTypeName)
    {
        switch (businessTypeName)
        {
            case "siliconalley:businesstype_gamestudio": return 0.3f;
            case "siliconalley:businesstype_softwarestudio": return 0.6f;
            default: return 1f;
        }
    }

    // Tier 3 market modifier: competing businesses of the same type in the same neighborhood. Same
    // competitor definition as the founding UI (StartBusinessUI). Static so the phone dashboard can
    // show the player the same figure that scales the payout.
    public static int CompetitorCount(BuildingRegistration registration)
    {
        var current = SaveGameManager.Current;
        if (current == null || current.BuildingRegistrations == null)
            return 0;
        var neighborhood = registration.Neighborhood;
        var typeName = registration.businessTypeName;
        int sameType = 0;
        foreach (var other in current.BuildingRegistrations)
            if (other.businessTypeName == typeName && other.Neighborhood == neighborhood)
                sameType++;
        return Mathf.Max(0, sameType - 1); // exclude this business itself
    }

    // More neighborhood competitors trims the per-project payout; the project type sets how sensitive
    // it is (issue #3) — quick wins shrug rivals off, ambitious premium work is hit harder.
    public static float MarketFactor(BuildingRegistration registration, int kind)
    {
        return 1f / (1f + CompetitorCount(registration) * SiliconAlleyState.CompetitionCoefficient(kind));
    }

    // Issue #29 (Marketing synergy): how many marketing-agency businesses the player operates. Each one promotes
    // the player's IT studios for free (MarketingSynergyAwarenessPerHour each). Player ownership = RentedByPlayer
    // (the game's own IsPlayerOwnedBusiness rule); a marketing agency carries MarketingAgencyTypeName. Static so
    // the project screen can surface the same synergy the simulator applies. 0 ⇒ no synergy ⇒ behaviour unchanged.
    public static int OwnedMarketingAgencies()
    {
        var current = SaveGameManager.Current;
        if (current == null || current.BuildingRegistrations == null)
            return 0;
        var count = 0;
        foreach (var reg in current.BuildingRegistrations)
            if (reg.RentedByPlayer && reg.businessTypeName == MarketingAgencyTypeName)
                count++;
        return count;
    }

    // Project progress this studio accrues per in-game hour at the current hour's staffing
    // (sum of programmer skill x ProjectSpeed) — the exact throughput SimulateCurrentHour applies.
    // The phone dashboard divides remaining progress by this to estimate phase/ship ETAs. Returns 0
    // when no qualifying programmer is at a workstation this hour (the studio is idle right now).
    public static float CurrentHourlyProgress(BuildingRegistration registration)
    {
        var businessType = BusinessTypeHelper.GetData(registration);
        if (businessType == null || registration.itemInstances == null)
            return 0f;

        var hour = TimeHelper.CurrentHour;
        float totalSkill = 0f;
        foreach (var instance in registration.itemInstances.Values)
        {
            if ((instance.ItemCached.type & ItemType.EmployeeWorkstation) == 0)
                continue;
            var employee = EmployeeHelper.GetEmployeeAtStationAndHour(registration, instance.id, hour);
            if (employee == null)
                continue;
            var skill = employee.characterData.skills.FirstOrDefault(s => businessType.employeePrimarySkills.Contains(s.name));
            if (skill == null)
                continue;
            totalSkill += skill.value;
        }
        return totalSkill * SiliconAlleyState.ProjectSpeed;
    }
}
