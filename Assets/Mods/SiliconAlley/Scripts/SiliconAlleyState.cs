using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// Tier 2/3: per-building state + tunables for the Silicon Alley project simulator. Persisted in the
// game save via GameInstance.modData (see SiliconAlleyPersistence): progress, reputation and the
// installed base survive save/reload.
public static class SiliconAlleyState
{
    private sealed class BusinessState
    {
        public float Progress;       // accumulated work toward the next project
        public float Reputation;     // 0..~3, grows with high-quality deliveries
        public int InstalledBase;    // completed projects still earning support income
        public float SupportAccrual; // fractional support income carried between hours
        public float QualitySum;     // accumulated (staff-quality x phase-weight) over the project
        public float QualityWeight;  // total accumulated weight (Testing hours weigh more)
        // Per-phase quality breakdown (issue #8): the same (sample x weight) accrual as the aggregate
        // above, split by the phase the work happened in, so each phase's contribution to the shipped
        // quality is explicit for the per-phase screens. The aggregate pair stays the source of truth
        // for overall quality (GetAverageQuality); these are purely additive. Release never accrues (it
        // is the completion instant). Appended to the save as trailing fields (absent in old saves => 0).
        public float DesignQualitySum, DesignQualityWeight;
        public float DevQualitySum, DevQualityWeight;
        public float TestQualitySum, TestQualityWeight;
        public int LastPatchDay;     // game-day the live catalog was last patched (post-release updates)
        public int ProjectType = -1; // ProjectKind locked in for the current project; -1 = not yet locked
        // Issue #9 (Design screen): player-set, per-project Design controls. DesignFocus 0=polish..1=speed
        // (0.5 = neutral, so old saves and untouched projects behave exactly as before); ConceptLocked
        // freezes scope+focus once the player commits. Both appended to the save (absent in old saves =>
        // the defaults here). Editable only while in the Design phase and not yet locked.
        public float DesignFocus = 0.5f;
        public int ConceptLocked;    // 0 = open (player may still set scope/focus), 1 = locked
        // Issue #10 (Development): Overtime is a sticky per-studio policy — while ON it speeds the build
        // (x1.5 progress) at the cost of quality (x0.85) during Development only. 0 = off (neutral), so old
        // saves and untouched studios are unchanged. Appended to the save (absent in old saves => 0).
        public int Overtime;         // 0 = off, 1 = on
        // Issue #11 (Testing): Hold keeps QA running — it pins progress just under completion so the
        // project stays in Testing (accruing the 2x quality) instead of auto-shipping. 0 = off (the
        // default; auto-ship at 100% as before). Per-project: reset on completion. Appended (absent => 0).
        public int Hold;             // 0 = ship at 100% (default), 1 = keep testing (don't auto-ship)
        // Issue #19 (Bugs): open defects in the current build — accrue during Development (more under
        // Overtime), burn down during Testing/Hold by tester skill. Residual bugs at ship cut the shipped
        // quality (so the existing payout path reflects them), the review score (#20) and the launch jump.
        // Appended to the save (absent in old saves => 0, so a legacy in-flight project has no bugs and
        // ships exactly as before). Reset to 0 on completion.
        public float BugCount;
        // Issue #21 (Marketing): cash-funded pre-release awareness/hype that scales the launch installed-
        // base jump (BA has no "marketer" skill — modelled as spend, see issue #15). Awareness decays each
        // hour (slower while Hype > 0); Hype itself decays. AdSpend is a sticky policy (like Overtime) that
        // spends a little each hour for steady awareness. All appended (absent in old saves => 0, so a
        // legacy launch has no awareness multiplier and adds exactly +1 as before). Awareness/Hype reset on
        // completion (consumed by the launch); AdSpend stays sticky across projects.
        public float Awareness;
        public float Hype;
        public int AdSpend;          // 0 = off (default), 1 = on
        // Issue #25 (Aging): the game-day the product's market freshness was last anchored (= last
        // ship/patch). Support income per installed unit decays as the days since this grow, toward a floor;
        // a ship/patch resets it. SAVE-COMPAT: appended trailing (absent in old saves => 0 = "unanchored");
        // on the first support accrual a 0 anchors to the current day, so a loaded legacy catalog starts at
        // FULL freshness and only ages from load-time on (no retroactive income drop). Reuses no existing
        // field's meaning.
        public int SupportFreshDay;
        // Issue #24 (Sequels): Version is the studio's CURRENT product's version (1 = debut). On ship it
        // increments, so the next product is a sequel (Version >= 2) that leverages the franchise's installed
        // base + IpReputation for a bigger launch. IpReputation (0..IpRepMax) is the franchise's standing:
        // strong releases raise it, weak sequels dent it. SAVE-COMPAT: appended trailing; absent in old saves
        // => Version field-initializer 1 (the in-flight product reads as a debut) and IpReputation 0 (no
        // sequel bonus) => launches unchanged. Legacy installed base is NOT rescaled.
        public int Version = 1;
        public float IpReputation;
        public bool DesignPrompted;  // transient (NOT persisted): one "set your concept" nudge per project
        // Issue #12 (Release): a transient (NOT persisted) snapshot of the most recent ship so the screen
        // can show a "ship report". Momentary by design — lost on reload, re-set on the next ship.
        // Issue #20 (Reviews): LastShipReview is the 0..10 critical-reception score derived at that ship.
        public bool HasLastShip;
        public float LastShipQuality, LastShipPayout, LastShipRepMult, LastShipMarketMult, LastShipReview;
    }

    private static readonly Dictionary<string, BusinessState> States = new Dictionary<string, BusinessState>();

    // ---- tunables (driven by the in-game options panel; defaults match a 100/100/20 slider) ----
    public static float ProjectSpeed = 1f;         // progress per programmer skill-point per hour
    public static float ProjectSize = 2800f;       // progress to complete one project (~2800 ≈ a skill-70 solo programmer over ~7 in-game calendar days full-time; the Project-speed slider tunes the pace)
    public static float PayoutMultiplier = 1f;     // global payout scale
    public static float SupportRatePerDay = 0.02f; // support income per installed unit per day, as a fraction of market price

    // ---- issue #25 (Aging) tuning. Recurring support income decays with the days since the last ship/patch,
    // from full (1.0) down to SupportAgeFloor over SupportAgeFullDays. A staffed studio patches every
    // PatchIntervalDays (7), so it stays near-fresh; a neglected catalog tapers toward the floor. These scale
    // support income only — SupportRatePerDay keeps its meaning (the age factor is a SEPARATE multiplier). ----
    public const float SupportAgeFullDays = 60f; // days since last patch at which freshness reaches the floor
    public const float SupportAgeFloor = 0.25f;  // an aged product still earns this fraction of its support

    // ---- issue #24 (Sequels / IP reputation) tuning. IpReputation is 0..IpRepMax. A release is judged
    // against an expectation bar (higher for sequels); a review above the bar raises IP rep, below it lowers
    // it (a weak sequel disappoints). A sequel's launch leverages the prior installed base, scaled by IP rep
    // and the review. ----
    public const float IpRepMax = 3f;
    public const float DebutReviewBar = 5f;      // a debut (v1) is judged against a modest bar
    public const float SequelReviewBar = 6.5f;   // sequels are held to a higher standard (raised expectation)
    public const float IpRepSensitivity = 0.15f; // IP-rep change per review point above/below the bar
    public const float SequelLeverage = 0.15f;   // sequel launch units = priorInstalled * this * ipRepFrac * reviewFactor

    // ---- issue #19 (Bugs) tuning. Bugs accrue per unit of Development progress and burn down per unit of
    // tester skill in Testing. Defaults are sized against ProjectSize (~2800) and skill-70 staff so a build
    // that skips QA ships visibly buggy while a held/tested build clears them. ----
    public const float BugsPerProgress = 0.01f;       // bugs added per progress point during Development
    public const float OvertimeBugFactor = 2f;        // Overtime rushes the build => ~2x the bugs
    public const float BugFixPerSkillHour = 0.02f;    // bugs cleared per tester skill-point per Testing hour
    public const float BugScale = 30f;                 // bug count that maps to "0% polish" / max ship penalty
    public const float MaxBugQualityPenalty = 0.5f;    // a maximally buggy build loses up to half its quality

    // ---- issue #21 (Marketing) tuning. Awareness is unit-less "buzz"; AwarenessToUnits converts it to
    // extra launch installed-base units. Awareness decays each hour (slower while Hype is active). ----
    public const float AwarenessDecayPerHour = 0.985f; // ~1.5%/h baseline decay
    public const float AwarenessDecayHyped = 0.997f;   // Hype nearly holds awareness steady
    public const float HypeDecayPerHour = 0.95f;       // Hype itself fades faster
    public const float AwarenessToUnits = 0.5f;        // launch units = awareness * this (scaled by review)
    public const float AdSpendAwarenessPerHour = 0.6f; // awareness added each hour while Ad Spend is on
    // Campaign channel costs (cash) and their awareness/hype effect. Verified spend goes through
    // SiliconAlleyMoney (BA money API). Press Build is strongest fired in late Development (see simulator).
    public const float PressReleaseCost = 1500f, PressReleaseAwareness = 8f;
    public const float PressBuildCost = 6000f, PressBuildAwareness = 30f;
    public const float HypeCost = 2500f, HypeAmount = 12f;
    public const float AdSpendCostPerHour = 120f;

    // ---- project type (issue #3): a player-chosen scale that trades duration vs payout vs competition.
    // The dropdown sets GlobalProjectType (the pre-selection); the simulator locks it per project at the
    // start so a mid-project change only affects the NEXT project. Indices match the options dropdown. ----
    public enum ProjectKind { Quick = 0, Standard = 1, Ambitious = 2 }
    public static int GlobalProjectType = (int)ProjectKind.Standard; // next project's type; set by the dropdown + restored from the save

    private static ProjectKind ToKind(int index) =>
        index < (int)ProjectKind.Quick || index > (int)ProjectKind.Ambitious ? ProjectKind.Standard : (ProjectKind)index;

    // Bigger projects take proportionally longer (scales ProjectSize).
    public static float DurationMultiplier(int kind)
    {
        switch (ToKind(kind))
        {
            case ProjectKind.Quick: return 0.5f;
            case ProjectKind.Ambitious: return 2f;
            default: return 1f;
        }
    }

    // Ambitious projects pay a premium; quick wins pay less per project.
    public static float PayoutMultiplierFor(int kind)
    {
        switch (ToKind(kind))
        {
            case ProjectKind.Quick: return 0.6f;
            case ProjectKind.Ambitious: return 1.8f;
            default: return 1f;
        }
    }

    // Per-competitor sensitivity for MarketFactor: quick wins shrug off rivals, ambitious premium work
    // is hit harder by a crowded neighborhood. Standard (0.25) is unchanged from the pre-#3 behavior.
    public static float CompetitionCoefficient(int kind)
    {
        switch (ToKind(kind))
        {
            case ProjectKind.Quick: return 0.15f;
            case ProjectKind.Ambitious: return 0.4f;
            default: return 0.25f;
        }
    }

    public static string ProjectTypeNameKey(int kind)
    {
        switch (ToKind(kind))
        {
            case ProjectKind.Quick: return "siliconalley:projecttype_quick";
            case ProjectKind.Ambitious: return "siliconalley:projecttype_ambitious";
            default: return "siliconalley:projecttype_standard";
        }
    }

    // ---- project lifecycle phases (derived from Progress, so they need no extra persisted state) ----
    // A project advances Design -> Development -> Testing -> Release as Progress climbs to ProjectSize.
    public enum ProjectPhase { Design, Development, Testing, Release }
    private const float DesignFraction = 0.15f;      // Design occupies 0%..15% of ProjectSize
    private const float DevelopmentFraction = 0.70f; // Development 15%..70%, Testing 70%..100%

    // Phase math takes the project's effective size (issue #3: it varies per project type), rather than
    // the global ProjectSize, so a Quick/Standard/Ambitious project reports its own phases and ETAs.
    public static ProjectPhase PhaseOf(float progress, float size)
    {
        var fraction = progress / Mathf.Max(1f, size);
        if (fraction >= 1f) return ProjectPhase.Release;
        if (fraction >= DevelopmentFraction) return ProjectPhase.Testing;
        if (fraction >= DesignFraction) return ProjectPhase.Development;
        return ProjectPhase.Design;
    }

    // Progress within the current phase, 0..1 (for display).
    public static float PhaseProgressFraction(float progress, float size)
    {
        var fraction = Mathf.Clamp01(progress / Mathf.Max(1f, size));
        float lo, hi;
        if (fraction >= DevelopmentFraction) { lo = DevelopmentFraction; hi = 1f; }
        else if (fraction >= DesignFraction) { lo = DesignFraction; hi = DevelopmentFraction; }
        else { lo = 0f; hi = DesignFraction; }
        return Mathf.Clamp01((fraction - lo) / Mathf.Max(0.0001f, hi - lo));
    }

    // Progress value at which the given phase ends (Testing/Release end at completion). Lets the
    // dashboard compute "progress remaining in this phase" without duplicating the fraction constants.
    public static float PhaseEndProgress(ProjectPhase phase, float size)
    {
        switch (phase)
        {
            case ProjectPhase.Design: return DesignFraction * size;
            case ProjectPhase.Development: return DevelopmentFraction * size;
            default: return size;
        }
    }

    public static string PhaseNameKey(ProjectPhase phase)
    {
        switch (phase)
        {
            case ProjectPhase.Design: return "siliconalley:phase_design";
            case ProjectPhase.Development: return "siliconalley:phase_development";
            case ProjectPhase.Testing: return "siliconalley:phase_testing";
            default: return "siliconalley:phase_release";
        }
    }

    private static BusinessState Get(string key)
    {
        if (!States.TryGetValue(key, out var state))
        {
            state = new BusinessState();
            States[key] = state;
        }
        return state;
    }

    public static string KeyFor(BuildingRegistration registration)
    {
        var address = registration.Address;
        return address.streetName + ":" + address.streetNumber;
    }

    public static void AddProgress(string key, float amount) => Get(key).Progress += amount;
    public static float GetProgress(string key) => Get(key).Progress;
    public static float GetReputation(string key) => Get(key).Reputation;
    public static int GetInstalledBase(string key) => Get(key).InstalledBase;
    public static int GetLastPatchDay(string key) => Get(key).LastPatchDay;
    // A ship or patch resets the live catalog's freshness too (issue #25): both always move together, so the
    // patch clock doubles as the aging anchor. Callers (ship completion + periodic patch) pass the current day.
    public static void SetLastPatchDay(string key, int day)
    {
        var state = Get(key);
        state.LastPatchDay = day;
        state.SupportFreshDay = day; // issue #25: a fresh release/update restores full support freshness
    }

    // Issue #24 (Sequels): the version of the studio's current product (1 = debut) and the franchise's IP
    // reputation (0..IpRepMax). Both surface in the ship report / completion toast.
    public static int GetVersion(string key) => Get(key).Version;
    public static float GetIpReputation(string key) => Get(key).IpReputation;

    // The project type locked in for this building's current project (issue #3). Unlocked (-1) reads as
    // Standard so display/calc always have a concrete type.
    public static int GetProjectType(string key)
    {
        var raw = Get(key).ProjectType;
        return raw < 0 ? (int)ProjectKind.Standard : raw;
    }

    // Lock the current project's type when work begins. A fresh project (no progress yet) takes the
    // player's current global selection; a legacy in-flight project loaded from an older save (progress
    // already accrued, type unset) stays Standard so its size isn't suddenly rescaled. Returns the kind.
    public static int EnsureProjectTypeLocked(string key)
    {
        var state = Get(key);
        if (state.ProjectType < 0)
            state.ProjectType = state.Progress <= 0f ? GlobalProjectType : (int)ProjectKind.Standard;
        return state.ProjectType;
    }

    // Progress required to complete this building's current project, scaled by its locked type.
    public static float EffectiveProjectSize(string key) => ProjectSize * DurationMultiplier(GetProjectType(key));

    // Issue #20/#21: complete the current project. The shipped quality already carries the bug penalty
    // (applied in the simulator), so a buggy build gives less reputation here automatically. Marketing
    // awareness adds a small reputation bonus on top and (via launchUnits, computed by the caller from
    // awareness + review) grows the installed base by more than the flat +1. SAVE-COMPAT: a legacy launch
    // has BugCount = Awareness = 0, so the bonus is 0 and launchUnits floors at 1 — identical to before.
    public static void OnProjectCompleted(string key, float quality, int launchUnits, float review)
    {
        var state = Get(key);
        var awarenessRepBonus = Mathf.Min(0.2f, state.Awareness * 0.004f); // 0 when unmarketed (legacy)
        state.Reputation = Mathf.Min(3f, state.Reputation + quality * 0.1f + awarenessRepBonus);
        state.InstalledBase += Mathf.Max(1, launchUnits); // base +1 (legacy) + marketing/review/sequel extra
        // Issue #24 (Sequels): judge this release against an expectation bar (higher for a sequel). Beating it
        // builds the franchise's IP reputation; missing it dents it (a weak sequel disappoints). Then the next
        // product becomes the next version. SAVE-COMPAT: a legacy debut (Version 1) just nudges IP rep from 0
        // by the small (review - DebutReviewBar) term and bumps to v2 — no effect on this launch's size.
        var expectationBar = state.Version >= 2 ? SequelReviewBar : DebutReviewBar;
        state.IpReputation = Mathf.Clamp(state.IpReputation + (review - expectationBar) * IpRepSensitivity, 0f, IpRepMax);
        state.Version += 1;
        // The launch consumes the build: bugs are resolved and awareness/hype reset for the next project.
        state.BugCount = 0f;
        state.Awareness = 0f;
        state.Hype = 0f;
        state.QualitySum = 0f;     // the next project's quality accrues fresh
        state.QualityWeight = 0f;
        // Issue #8: per-phase accumulators reset with the aggregate so the next project's phases accrue
        // from zero (they mirror the aggregate, never replace it).
        state.DesignQualitySum = 0f; state.DesignQualityWeight = 0f;
        state.DevQualitySum = 0f; state.DevQualityWeight = 0f;
        state.TestQualitySum = 0f; state.TestQualityWeight = 0f;
        state.ProjectType = GlobalProjectType; // the next project uses the current global selection
        state.ConceptLocked = 0; // issue #9: the next project's concept reopens (DesignFocus stays sticky)
        state.Hold = 0;          // issue #11: the next project isn't held
        state.DesignPrompted = false; // nudge again for the next project
    }

    // Step 3 (quality): sample this hour's staff quality (0..1), weighted by phase so Testing-phase
    // staffing matters more — under-skilled testing lowers the shipped quality ("bugs"). Averaged at
    // release via GetAverageQuality. Issue #8: the same sample is also recorded against the phase the
    // work happened in, so each phase's contribution is available to the per-phase screens; the
    // aggregate pair stays the source of truth for overall quality (so the payout and legacy saves are
    // unaffected). Within a phase the weight is constant, so its per-phase average is just the mean sample.
    public static void AccumulateQuality(string key, ProjectPhase phase, float sample, float weight)
    {
        var state = Get(key);
        var clamped = Mathf.Clamp01(sample) * weight;
        state.QualitySum += clamped;
        state.QualityWeight += weight;
        switch (phase)
        {
            case ProjectPhase.Design: state.DesignQualitySum += clamped; state.DesignQualityWeight += weight; break;
            case ProjectPhase.Development: state.DevQualitySum += clamped; state.DevQualityWeight += weight; break;
            case ProjectPhase.Testing: state.TestQualitySum += clamped; state.TestQualityWeight += weight; break;
            // Release accrues no work — it is the completion instant.
        }
    }

    // Average accrued quality (0..1), or -1 when nothing has accrued yet (caller falls back).
    public static float GetAverageQuality(string key)
    {
        var state = Get(key);
        return state.QualityWeight > 0f ? state.QualitySum / state.QualityWeight : -1f;
    }

    // Issue #8: average quality accrued during a single phase (0..1), or -1 when that phase has not
    // accrued any work yet — a legacy save carrying only the aggregate, a phase not yet reached, or
    // Release (which never accrues) — so callers fall back to the aggregate or show "—".
    public static float GetPhaseQuality(string key, ProjectPhase phase)
    {
        var state = Get(key);
        switch (phase)
        {
            case ProjectPhase.Design: return state.DesignQualityWeight > 0f ? state.DesignQualitySum / state.DesignQualityWeight : -1f;
            case ProjectPhase.Development: return state.DevQualityWeight > 0f ? state.DevQualitySum / state.DevQualityWeight : -1f;
            case ProjectPhase.Testing: return state.TestQualityWeight > 0f ? state.TestQualitySum / state.TestQualityWeight : -1f;
            default: return -1f;
        }
    }

    // Convenience for the per-phase screens: the building's current derived phase, from its progress and
    // locked effective size (same as PhaseOf, without the caller re-fetching both).
    public static ProjectPhase CurrentPhase(string key) => PhaseOf(GetProgress(key), EffectiveProjectSize(key));

    // ---- issue #9 (Design screen): per-project Design controls -------------------------------------
    // The player edits scope/focus only while the project is still in its Design phase and the concept
    // is not yet locked; the setters are no-ops otherwise, so a mid-project screen can't rescale a
    // project that has moved on (mirrors the EnsureProjectTypeLocked "don't rescale a legacy project" rule).
    public static bool IsConceptLocked(string key) => Get(key).ConceptLocked != 0;
    public static float GetDesignFocus(string key) => Get(key).DesignFocus;

    // True while the player may still shape the concept: in the Design phase and not yet locked.
    public static bool CanEditConcept(string key) =>
        Get(key).ConceptLocked == 0 && CurrentPhase(key) == ProjectPhase.Design;

    // Set this studio's scope for the current project — a per-studio override of the global pre-selection.
    public static void SetScope(string key, int kind)
    {
        if (CanEditConcept(key))
            Get(key).ProjectType = (int)ToKind(kind);
    }

    // Set the Design focus (0 = polish .. 1 = speed), clamped.
    public static void SetDesignFocus(string key, float value)
    {
        if (CanEditConcept(key))
            Get(key).DesignFocus = Mathf.Clamp01(value);
    }

    // Commit the concept: freeze scope + focus for the rest of this project.
    public static void LockConcept(string key)
    {
        if (CurrentPhase(key) == ProjectPhase.Design)
            Get(key).ConceptLocked = 1;
    }

    // Issue #10 (Development): the studio's Overtime policy — a sticky toggle that only takes effect in
    // the Development phase (speeds the build, lowers quality). Settable any time; it just persists.
    public static bool IsOvertime(string key) => Get(key).Overtime != 0;
    public static void SetOvertime(string key, bool on) => Get(key).Overtime = on ? 1 : 0;

    // Issue #11 (Testing): the studio's "Hold" QA policy.
    public static bool IsHold(string key) => Get(key).Hold != 0;
    public static void SetHold(string key, bool on) => Get(key).Hold = on ? 1 : 0;

    // While held, pin progress just inside the Testing band so the project doesn't auto-ship and keeps
    // accruing the 2x Testing quality. No-op until progress is near completion.
    public static void HoldBelowCompletion(string key, float size)
    {
        var state = Get(key);
        var cap = size - 1f;
        if (state.Progress > cap)
            state.Progress = cap;
    }

    // "Ship now": release the hold and complete the project at its current accrued quality via the normal
    // completion path (the simulator's next staffed tick ships it). Only on explicit player action.
    public static void ShipNow(string key)
    {
        var state = Get(key);
        state.Hold = 0;
        state.Progress = EffectiveProjectSize(key);
    }

    // ---- issue #19 (Bugs) -------------------------------------------------------------------------
    // Open defects in the current build. Accrued during Development, burned down during Testing/Hold.
    public static float GetBugCount(string key) => Get(key).BugCount;
    public static void AddBugs(string key, float amount) => Get(key).BugCount = Mathf.Max(0f, Get(key).BugCount + amount);
    public static void BurnBugs(string key, float amount) => Get(key).BugCount = Mathf.Max(0f, Get(key).BugCount - amount);

    // 0..1 "polish": 1 = no bugs, 0 = BugScale-or-more bugs. Drives the Testing readout and the ship penalty.
    public static float GetPolish(string key) => 1f - Mathf.Clamp01(Get(key).BugCount / BugScale);

    // How much residual bugs cut the shipped quality (and therefore payout/review/reputation). 1 = clean.
    public static float BugQualityFactor(string key) => 1f - (1f - GetPolish(key)) * MaxBugQualityPenalty;

    // ---- issue #21 (Marketing) --------------------------------------------------------------------
    public static float GetAwareness(string key) => Get(key).Awareness;
    public static void AddAwareness(string key, float amount) => Get(key).Awareness = Mathf.Max(0f, Get(key).Awareness + amount);
    public static float GetHype(string key) => Get(key).Hype;
    public static void AddHype(string key, float amount) => Get(key).Hype = Mathf.Max(0f, Get(key).Hype + amount);
    public static bool IsAdSpend(string key) => Get(key).AdSpend != 0;
    public static void SetAdSpend(string key, bool on) => Get(key).AdSpend = on ? 1 : 0;

    // Decay awareness (and hype) one hour. Awareness fades slowly, but Hype nearly holds it steady while
    // it lasts — the SI "crest the wave between phases" behaviour. Both are no-ops at 0 (legacy/unmarketed).
    public static void DecayMarketing(string key)
    {
        var state = Get(key);
        if (state.Awareness > 0f)
            state.Awareness *= state.Hype > 0f ? AwarenessDecayHyped : AwarenessDecayPerHour;
        if (state.Hype > 0f)
            state.Hype *= HypeDecayPerHour;
        if (state.Awareness < 0.01f) state.Awareness = 0f;
        if (state.Hype < 0.01f) state.Hype = 0f;
    }

    // Extra launch installed-base units from marketing, amplified by the review score (#20). The base +1
    // is added by OnProjectCompleted, so this returns the EXTRA only — 0 when awareness is 0 (legacy), so
    // an unmarketed/old-save launch still adds exactly +1. review is 0..10; a strong review roughly
    // doubles the conversion, a weak one halves it.
    public static int LaunchBonusUnits(string key, float review)
    {
        var awareness = Get(key).Awareness;
        if (awareness <= 0f)
            return 0;
        var reviewFactor = 0.5f + review / 10f; // 0.5 (review 0) .. 1.5 (review 10)
        return Mathf.Max(0, Mathf.RoundToInt(awareness * AwarenessToUnits * reviewFactor));
    }

    // Issue #20 (Reviews): the 0..10 critical-reception score derived at ship from the shipped quality
    // (already bug-penalised), the design ceiling and marketing awareness. Pure function so the simulator
    // and any preview share one definition. A clean, unmarketed ship scores on quality alone.
    public static float ComputeReviewScore(float shippedQuality, float designQuality, float awareness)
    {
        var score = Mathf.Clamp01(shippedQuality) * 9f;              // quality is the backbone (0..9)
        if (designQuality >= 0f)
            score += Mathf.Clamp01(designQuality) * 0.5f;            // a strong concept nudges critics up
        score += Mathf.Clamp01(awareness / 40f) * 1f;               // buzz lifts reception up to +1
        return Mathf.Clamp(score, 0f, 10f);
    }

    // Issue #12 (Release): a transient snapshot of the most recent ship for the screen's ship report.
    public readonly struct ShipReport
    {
        public readonly bool Has;
        public readonly float Quality, Payout, RepMult, MarketMult, Review;
        public ShipReport(bool has, float quality, float payout, float repMult, float marketMult, float review)
        {
            Has = has; Quality = quality; Payout = payout; RepMult = repMult; MarketMult = marketMult; Review = review;
        }
    }

    // Record the just-shipped project's headline numbers (the same values the success toast encodes),
    // including the 0..10 review score (#20).
    public static void SetLastShip(string key, float quality, float payout, float repMult, float marketMult, float review)
    {
        var state = Get(key);
        state.HasLastShip = true;
        state.LastShipQuality = quality;
        state.LastShipPayout = payout;
        state.LastShipRepMult = repMult;
        state.LastShipMarketMult = marketMult;
        state.LastShipReview = review;
    }

    public static ShipReport GetLastShip(string key)
    {
        var s = Get(key);
        return new ShipReport(s.HasLastShip, s.LastShipQuality, s.LastShipPayout, s.LastShipRepMult, s.LastShipMarketMult, s.LastShipReview);
    }

    public static void ClearLastShip(string key) => Get(key).HasLastShip = false;

    // Returns true exactly once per project (per session) so the simulator nudges the player to set the
    // concept just once. Transient (not persisted): a reload may re-nudge once, which is harmless.
    public static bool TryMarkDesignPrompted(string key)
    {
        var state = Get(key);
        if (state.DesignPrompted)
            return false;
        state.DesignPrompted = true;
        return true;
    }

    public static void DecayReputation(string key, float amount)
    {
        var state = Get(key);
        state.Reputation = Mathf.Max(0f, state.Reputation - amount);
    }

    // Issue #25 (Aging): 0..1 freshness multiplier on support income — full right after a ship/patch, decaying
    // toward SupportAgeFloor as the days since the last patch grow. A 0 anchor (legacy save / never set)
    // anchors to the current day and reads as fully fresh, so a freshly loaded old catalog never suffers a
    // retroactive income drop; it simply ages from load-time on. Pure-ish: it lazily sets the anchor once.
    public static float SupportFreshness(string key, int currentDay)
    {
        var state = Get(key);
        if (state.SupportFreshDay <= 0)
        {
            state.SupportFreshDay = currentDay; // anchor on first use => full freshness, age from here on
            return 1f;
        }
        var days = currentDay - state.SupportFreshDay;
        if (days <= 0)
            return 1f;
        return Mathf.Lerp(1f, SupportAgeFloor, Mathf.Clamp01(days / SupportAgeFullDays));
    }

    // Issue #24 (Sequels): extra launch installed-base units a SEQUEL (Version >= 2) earns by leveraging the
    // franchise's prior installed base, scaled by IP reputation and the review score (#20). Returns 0 for a
    // debut (Version 1), an unbuilt IP (rep 0) or an empty base — so a v1 / legacy launch is unchanged.
    // Computed BEFORE OnProjectCompleted (which grows the installed base and bumps the version).
    public static int SequelLaunchUnits(string key, float review)
    {
        var state = Get(key);
        if (state.Version < 2 || state.IpReputation <= 0f || state.InstalledBase <= 0)
            return 0;
        var ipRepFrac = Mathf.Clamp01(state.IpReputation / IpRepMax);
        var reviewFactor = 0.5f + review / 10f; // 0.5 (review 0) .. 1.5 (review 10)
        return Mathf.Max(0, Mathf.RoundToInt(state.InstalledBase * SequelLeverage * ipRepFrac * reviewFactor));
    }

    // Accrue recurring support income; returns a whole-currency payout once it crosses 1. Issue #25: the
    // installed base earns less as the catalog ages (SupportFreshness), unless kept fresh by ship/patch.
    public static float AccrueSupport(string key, float marketPrice, int currentDay)
    {
        var state = Get(key);
        var freshness = SupportFreshness(key, currentDay);
        state.SupportAccrual += state.InstalledBase * marketPrice * freshness * (SupportRatePerDay / 24f);
        if (state.SupportAccrual >= 1f)
        {
            float payout = Mathf.Floor(state.SupportAccrual);
            state.SupportAccrual -= payout;
            return payout;
        }
        return 0f;
    }

    public static void Reset() => States.Clear();

    // --- persistence (stored in GameInstance.modData by SiliconAlleyPersistence) ---
    // SAVE COMPATIBILITY: this format is FORWARD-COMPATIBLE ONLY (see the Save Compatibility Policy in
    // CLAUDE.md). Never change the meaning/format of an existing field or reorder a persisted enum's
    // values; to change semantics, add a NEW field/key, bump CurrentSchemaVersion, and add a Migrate step.
    //
    // One entry per building (fields are APPEND-ONLY; older saves omit trailing fields, which default):
    // key|progress|reputation|installedBase|supportAccrual|qualitySum|qualityWeight|lastPatchDay|projectType
    //    |designQualitySum|designQualityWeight|devQualitySum|devQualityWeight|testQualitySum|testQualityWeight
    //    |designFocus|conceptLocked|overtime|hold|bugCount|awareness|hype|adSpend|supportFreshDay|version|ipReputation,
    // joined by ';'. The six per-phase quality fields (issue #8) and the Design/Development/Testing screen
    // fields (issue #9: designFocus default 0.5 = neutral, conceptLocked 0; issue #10: overtime 0 = off;
    // issue #11: hold 0 = off), then the go-to-market fields (issue #19: bugCount 0 = no bugs; issue #21:
    // awareness/hype/adSpend 0 = unmarketed), then the product-lifecycle fields (issue #25: supportFreshDay
    // 0 = anchored to full freshness on first use; issue #24: version default 1 = debut, ipReputation 0 = no
    // sequel bonus), were
    // appended at schema v1; a save from before a given field omits it and it defaults (per-phase quality
    // reads "not accrued" via GetPhaseQuality; designFocus stays 0.5; conceptLocked 0; overtime 0; bugCount,
    // awareness, hype, adSpend 0; supportFreshDay 0 ⇒ full freshness; version 1; ipReputation 0) while the
    // aggregate qualitySum/qualityWeight still yields the real shipped quality. Two reserved
    // "~"-prefixed header entries lead the blob:
    //   "~schema|<version>" — the save schema version (added in v1; absent ⇒ the v1 baseline);
    //   "~global|<index>"   — the player's project-type pre-selection, so it survives a session before
    //                          the options menu (which re-applies the PlayerPrefs value) is opened.
    // Building keys are "streetName:streetNumber" and never start with '~', so '~' is a safe sentinel
    // namespace; LoadFrom ignores any other unknown "~"-header (forward-compat with newer saves).
    // Persisted enum ordinals (ProjectKind {Quick=0,Standard=1,Ambitious=2}) are APPEND-ONLY: never
    // renumber them. ProjectPhase is derived from Progress and is NOT persisted, so it is free to change.
    // Older saves omit the trailing fields; LoadFrom tolerates their absence and defaults them.
    // InvariantCulture is required so a locale with comma decimals (e.g. nl-NL) cannot corrupt it.
    private const string GlobalTypeKey = "~global";

    // Save schema version. The CURRENT shipped format is the v1 baseline; a save written before the
    // "~schema" header existed has no such entry and loads as v1 (BaselineSchemaVersion). Bump
    // CurrentSchemaVersion ONLY when the MEANING/format of an existing field changes, and add a matching
    // step in Migrate() so older saves migrate forward.
    private const string SchemaKey = "~schema";
    private const int BaselineSchemaVersion = 1; // saves with no "~schema" header are this version
    private const int CurrentSchemaVersion = 1;  // the version we write today

    public static string Serialize()
    {
        var builder = new StringBuilder();
        builder.Append(SchemaKey).Append('|')
            .Append(CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)).Append(';');
        builder.Append(GlobalTypeKey).Append('|')
            .Append(GlobalProjectType.ToString(CultureInfo.InvariantCulture)).Append(';');
        foreach (var pair in States)
        {
            var state = pair.Value;
            builder.Append(pair.Key).Append('|')
                .Append(state.Progress.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.Reputation.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.InstalledBase.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.SupportAccrual.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.QualitySum.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.QualityWeight.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.LastPatchDay.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.ProjectType.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.DesignQualitySum.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.DesignQualityWeight.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.DevQualitySum.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.DevQualityWeight.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.TestQualitySum.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.TestQualityWeight.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.DesignFocus.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.ConceptLocked.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.Overtime.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.Hold.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.BugCount.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.Awareness.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.Hype.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.AdSpend.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.SupportFreshDay.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.Version.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.IpReputation.ToString(CultureInfo.InvariantCulture)).Append(';');
        }
        return builder.ToString();
    }

    public static void LoadFrom(string data)
    {
        States.Clear();
        GlobalProjectType = (int)ProjectKind.Standard; // headerless (older) saves default cleanly
        if (string.IsNullOrEmpty(data))
            return;

        // A save written before the "~schema" header existed has no such entry: it IS the v1 baseline.
        int schemaVersion = BaselineSchemaVersion;
        foreach (var entry in data.Split(';'))
        {
            if (string.IsNullOrEmpty(entry))
                continue;
            // Each record is parsed in isolation: a single malformed/old/corrupt entry must degrade
            // gracefully (be skipped, defaulting that building lazily via Get) rather than abort the
            // whole save load. TryParse never throws, but the guard keeps any future field shapes safe.
            try
            {
                var parts = entry.Split('|');
                if (parts[0] == SchemaKey) // the schema-version header, not a building
                {
                    if (parts.Length > 1)
                        int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out schemaVersion);
                    continue;
                }
                if (parts[0] == GlobalTypeKey) // the project-type pre-selection header, not a building
                {
                    if (parts.Length > 1)
                        int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out GlobalProjectType);
                    continue;
                }
                if (parts[0].Length > 0 && parts[0][0] == '~')
                    continue; // unknown reserved header (e.g. from a newer save): ignore for forward-compat
                if (parts.Length < 5)
                    continue;
                var state = new BusinessState();
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out state.Progress);
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out state.Reputation);
                int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.InstalledBase);
                float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out state.SupportAccrual);
                if (parts.Length > 6) // newer saves also carry the quality accumulator
                {
                    float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out state.QualitySum);
                    float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out state.QualityWeight);
                }
                if (parts.Length > 7) // and the post-release patch clock
                    int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.LastPatchDay);
                if (parts.Length > 8) // and the locked project type (issue #3)
                    int.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.ProjectType);
                if (parts.Length > 10) // issue #8: per-phase quality — Design (absent in pre-#8 saves ⇒ 0)
                {
                    float.TryParse(parts[9], NumberStyles.Float, CultureInfo.InvariantCulture, out state.DesignQualitySum);
                    float.TryParse(parts[10], NumberStyles.Float, CultureInfo.InvariantCulture, out state.DesignQualityWeight);
                }
                if (parts.Length > 12) // Development
                {
                    float.TryParse(parts[11], NumberStyles.Float, CultureInfo.InvariantCulture, out state.DevQualitySum);
                    float.TryParse(parts[12], NumberStyles.Float, CultureInfo.InvariantCulture, out state.DevQualityWeight);
                }
                if (parts.Length > 14) // Testing
                {
                    float.TryParse(parts[13], NumberStyles.Float, CultureInfo.InvariantCulture, out state.TestQualitySum);
                    float.TryParse(parts[14], NumberStyles.Float, CultureInfo.InvariantCulture, out state.TestQualityWeight);
                }
                if (parts.Length > 15) // issue #9: Design focus (absent in pre-#9 saves ⇒ field default 0.5)
                    float.TryParse(parts[15], NumberStyles.Float, CultureInfo.InvariantCulture, out state.DesignFocus);
                if (parts.Length > 16) // issue #9: concept-locked flag (absent ⇒ 0, open)
                    int.TryParse(parts[16], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.ConceptLocked);
                if (parts.Length > 17) // issue #10: overtime policy (absent ⇒ 0, off)
                    int.TryParse(parts[17], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.Overtime);
                if (parts.Length > 18) // issue #11: hold-testing flag (absent ⇒ 0, off)
                    int.TryParse(parts[18], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.Hold);
                if (parts.Length > 19) // issue #19: open bug count (absent ⇒ 0, no bugs)
                    float.TryParse(parts[19], NumberStyles.Float, CultureInfo.InvariantCulture, out state.BugCount);
                if (parts.Length > 22) // issue #21: marketing awareness/hype/ad-spend (absent ⇒ 0, unmarketed)
                {
                    float.TryParse(parts[20], NumberStyles.Float, CultureInfo.InvariantCulture, out state.Awareness);
                    float.TryParse(parts[21], NumberStyles.Float, CultureInfo.InvariantCulture, out state.Hype);
                    int.TryParse(parts[22], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.AdSpend);
                }
                if (parts.Length > 23) // issue #25: support-freshness anchor (absent ⇒ 0 ⇒ anchored fresh on first use)
                    int.TryParse(parts[23], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.SupportFreshDay);
                if (parts.Length > 24) // issue #24: product version (absent ⇒ field default 1, a debut). Keep
                {
                    // the 1 default if a present value parses as 0/garbage, so the in-flight product still ships as a debut.
                    int.TryParse(parts[24], NumberStyles.Integer, CultureInfo.InvariantCulture, out var version);
                    if (version > 0) state.Version = version;
                }
                if (parts.Length > 25) // issue #24: IP reputation (absent ⇒ 0, no sequel bonus)
                    float.TryParse(parts[25], NumberStyles.Float, CultureInfo.InvariantCulture, out state.IpReputation);
                States[parts[0]] = state;
            }
            catch
            {
                // Skip this record; the building it described falls back to a fresh default state.
            }
        }

        Migrate(schemaVersion);
    }

    // Forward-migrate the in-memory state just loaded from an older schema up to CurrentSchemaVersion,
    // one version at a time. Today v1 == current, so this is a no-op; a future bump adds a `case` that
    // transforms the already-parsed state (e.g. remap a field whose meaning changed) before play resumes.
    // Keep migrations idempotent and order-independent of how records were parsed above.
    private static void Migrate(int fromVersion)
    {
        var version = fromVersion < BaselineSchemaVersion ? BaselineSchemaVersion : fromVersion;
        while (version < CurrentSchemaVersion)
        {
            switch (version)
            {
                // case 1: /* migrate v1 -> v2 here */ version = 2; break;
                default:
                    version = CurrentSchemaVersion; // newer-than-known or no step defined: stop cleanly
                    break;
            }
        }
    }
}
