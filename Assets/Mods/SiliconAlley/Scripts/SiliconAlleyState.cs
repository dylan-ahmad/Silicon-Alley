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
        // Issue #23 (Publisher deals): the active deal binding this studio's CURRENT product to a publisher on
        // a deadline. DealPublisher is the publisher's roster ordinal (see SiliconAlleyPublishers), -1 = no
        // active deal; DealDeadlineDay is the absolute game-day delivery is due; DealPayout is the bonus locked
        // at signing, paid on top of the normal market payout on an on-time ship. SAVE-COMPAT: appended
        // trailing; DealPublisher's field initializer is -1 (NOT 0 — 0 is a valid ordinal), so an old save
        // without these fields reads as "no deal" and ships freely as before. Reset on completion/miss.
        public int DealPublisher = -1;
        public int DealDeadlineDay;
        public float DealPayout;
        // Issue #40 (Design wizard, epic #34): the FROZEN trailing positions reserved up front for the four
        // wizard sibling pages so parallel work can't collide on field order. #40 ships them as no-ops — all
        // default 0 (absent in old saves => 0), so legacy behaviour is unchanged: no extra features, a single
        // "home" platform, no tools, Broad segment. Each sibling fills in ITS field's gameplay at ITS reserved
        // index and must hold the frozen positions (write 0 for any earlier reserved slot not yet implemented).
        // SAVE-COMPAT: pure trailing append (see Serialize/LoadFrom), no schema bump. The FeatureId/ToolId/
        // PlatformId/SegmentId enum families are reserved in CLAUDE.md SHIPPED_ENUMS; the owning sibling mints
        // the actual enum + per-bit assignments. Persisted as the int bitmask/ordinal (per-bit is load-bearing).
        public int FeatureMask;     // #26: bits = FeatureId (per business type); 0 = no extra features (scope x1, ceiling unchanged)
        public int PlatformMask;    // #37: bits = PlatformId; 0 = single "home" platform (reach x1, scope x1 — never 0 reach)
        public int OwnedToolsMask;  // #36: bits = ToolId, studio-level (survives OnProjectCompleted); 0 = no owned tools
        public int UsedToolsMask;   // #36: bits = ToolId, per-project (resets on completion); 0 = no licensed tools
        public int SegmentId;       // #38: SegmentId ordinal (0=Broad,1=Enterprise,2=Prosumer,3=Consumer); 0 = Broad x1
        // Issue #27 (Contracts): an accepted fixed-scope contract job — the FIRST persisted append past the #40
        // wizard reservation (trailing fields, NOT reserved). A studio holds at most one; it is active while
        // ContractScope > 0 and diverts the studio's staff (the product Progress pauses) until it delivers
        // (ContractProgress >= ContractScope, by the deadline) or the deadline passes (a miss). SAVE-COMPAT:
        // appended trailing; absent in old saves => all 0 => no contract => the studio works its product exactly
        // as before. Cleared on delivery/miss.
        public float ContractScope;       // progress to complete; 0 = no active contract
        public float ContractProgress;    // accrued work toward the contract
        public int ContractDeadlineDay;   // absolute game-day the contract is due
        public float ContractPayout;      // guaranteed sum paid on an on-time delivery
        // Issue #88 (player-driven lifecycle): the studio's CURRENT stage. A studio is Idle by default and
        // only works when the player starts a project; staff then work each stage but PARK at its ceiling
        // until the player pushes forward (Start development = confirm wizard; Send to testing; Release).
        // Persisted (trailing append, index 38). New studios default Idle (0). SAVE-COMPAT: absent in an old
        // save ⇒ inferred from Progress in LoadFrom (a legacy in-flight project keeps running, doesn't stall).
        // Append-only ordinals: ProjectStage { Idle=0, Design=1, Development=2, Testing=3 } (Release is the
        // transient ship action, not a stored stage).
        public int Stage;                 // ProjectStage ordinal; 0 = Idle (default)
        // Issue #78 (release history): persisted per-studio records, appended once per shipped product.
        // Stored as one trailing variable-length field after Stage. Empty for old saves.
        public readonly List<ReleaseRecord> Releases = new List<ReleaseRecord>();
        // Issue #82 (product naming): player-typed name for the CURRENT project. Empty means callers derive
        // the product display name from the business type, preserving old saves and untouched projects.
        public string ProductName = string.Empty;
        // Issue #83 (build-or-buy dependencies): explicit OS/runtime/framework dependency slots. Owned is a
        // studio-level self-built asset that survives completion; used/vendor are current-project choices.
        // Persisted as pure trailing appends after productName. Vendor ordinals use -1 = none/self-built.
        public int OwnedDependencyMask;
        public int UsedDependencyMask;
        public int[] DependencyVendorOrdinals;
        // Issue #85 (Market targeting): per-feature % allocation weights, indexed by SiliconAlleyFeatures Bit
        // (FeatureId). The boolean FeatureMask (#26) says WHICH features are in; these weights say HOW MUCH the
        // player allocates to each. Per-project (reset on completion). null / all-even ⇒ neutral allocation ⇒
        // the derived aspect-fit is 0 / ×1 (SiliconAlleyAspects), so old saves and untouched projects are unchanged.
        public float[] FeatureWeights;
        // Issue #103 (server infrastructure): player-chosen role per placed Server item, keyed by stable
        // ItemInstance.id. Unassigned servers are omitted from the map, so old saves and untouched servers
        // serialize compactly and read as neutral.
        public readonly Dictionary<string, ServerRole> ServerRoles = new Dictionary<string, ServerRole>();
        // Issue #26: the business type (game/office/security) that owns this building's current project, noted
        // transiently each sim tick / screen refresh so the per-type feature math (size + quality ceiling) can
        // resolve the feature list from FeatureMask without threading the type through EffectiveProjectSize's
        // many call sites. NOT persisted — re-derived from the building registration after load.
        public string BusinessTypeName;
        public bool DesignPrompted;  // transient (NOT persisted): one "set your concept" nudge per project
        // Issue #12 (Release): a transient (NOT persisted) snapshot of the most recent ship so the screen
        // can show a "ship report". Momentary by design — lost on reload, re-set on the next ship.
        // Issue #20 (Reviews): LastShipReview is the 0..10 critical-reception score derived at that ship.
        public bool HasLastShip;
        public float LastShipQuality, LastShipPayout, LastShipRepMult, LastShipMarketMult, LastShipReview;
        public string LastShipProductName = string.Empty;
        // Manual release (Software-Inc-style): the player decides when a finished product (and each post-
        // launch update) goes live. Both are transient (NOT persisted): a product that is "ready to release"
        // is derived from Progress >= size; an update from the patch timer (see the simulator). The flag is a
        // one-shot request the UI sets and the next staffed/unstaffed hourly tick consumes — lost on reload,
        // which is harmless (the product simply stays parked, ready to release again).
        public bool ReleaseRequested;
        public bool UpdateRequested;
    }

    private static readonly Dictionary<string, BusinessState> States = new Dictionary<string, BusinessState>();

    // Issue #22 (Publisher roster): the player's reputation with each publisher, indexed by the publisher's
    // roster ordinal (SiliconAlleyPublishers.Roster). Player-GLOBAL (your standing with a publisher is shared
    // across studios), persisted in the "~publishers" header. APPEND-ONLY like the roster: an index, once
    // shipped, keeps its meaning; new publishers extend the list. A missing index reads as 0 (old saves have a
    // shorter or empty list). Re-defaulted in LoadFrom and cleared in Reset so reputations never bleed between
    // saves loaded in one session.
    private static readonly List<float> PublisherReps = new List<float>();

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

    // Issue #88 (player-driven lifecycle): the studio's PERSISTED stage — distinct from the derived phase.
    // The studio is Idle until the player starts a project; staff then work each stage but the simulator
    // parks Progress at the stage ceiling until the player pushes forward. APPEND-ONLY ordinals (persisted).
    public enum ProjectStage { Idle = 0, Design = 1, Development = 2, Testing = 3 }

    // Issue #103: persisted per-server role. APPEND-ONLY ordinals; unknown values load as Unassigned.
    public enum ServerRole { Unassigned = 0, Infrastructure = 1, Backend = 2, Hosting = 3 }

    // Issue #78: persisted per-release row. ProductName is optional/escaped; empty means callers derive the
    // name from business type + version. #83 appends a dependency snapshot so recurring support can keep
    // charging royalties for licensed dependencies after current-project choices reset.
    public readonly struct ReleaseRecord
    {
        public readonly int Day, Version, Kind, LaunchUnits, Publisher, FeatureMask, PlatformMask, UsedToolsMask, SegmentId;
        public readonly int UsedDependencyMask;
        public readonly float Review, Quality, LaunchPayout;
        public readonly string ProductName, DependencyVendorOrdinals;

        public ReleaseRecord(int day, int version, int kind, float review, float quality, float launchPayout,
            int launchUnits, int publisher, int featureMask, int platformMask, int usedToolsMask, int segmentId,
            string productName, int usedDependencyMask = 0, string dependencyVendorOrdinals = "")
        {
            Day = day;
            Version = version;
            Kind = kind;
            Review = review;
            Quality = quality;
            LaunchPayout = launchPayout;
            LaunchUnits = launchUnits;
            Publisher = publisher;
            FeatureMask = featureMask;
            PlatformMask = platformMask;
            UsedToolsMask = usedToolsMask;
            SegmentId = segmentId;
            ProductName = productName ?? string.Empty;
            UsedDependencyMask = usedDependencyMask;
            DependencyVendorOrdinals = dependencyVendorOrdinals ?? string.Empty;
        }
    }

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

    // Issue #88: the player-facing label for the studio's current STAGE (the persisted lifecycle position),
    // used by the screen's header. Idle has its own key; the active stages reuse the phase names.
    public static string StageNameKey(ProjectStage stage)
    {
        switch (stage)
        {
            case ProjectStage.Design: return "siliconalley:phase_design";
            case ProjectStage.Development: return "siliconalley:phase_development";
            case ProjectStage.Testing: return "siliconalley:phase_testing";
            default: return "siliconalley:stage_idle";
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

    private static int[] EnsureDependencyVendors(BusinessState state)
    {
        if (state.DependencyVendorOrdinals == null)
        {
            state.DependencyVendorOrdinals = new int[SiliconAlleyProductDependencies.MaxCount];
            for (var i = 0; i < state.DependencyVendorOrdinals.Length; i++)
                state.DependencyVendorOrdinals[i] = -1;
        }
        else if (state.DependencyVendorOrdinals.Length < SiliconAlleyProductDependencies.MaxCount)
        {
            var expanded = new int[SiliconAlleyProductDependencies.MaxCount];
            for (var i = 0; i < expanded.Length; i++)
                expanded[i] = i < state.DependencyVendorOrdinals.Length ? state.DependencyVendorOrdinals[i] : -1;
            state.DependencyVendorOrdinals = expanded;
        }
        return state.DependencyVendorOrdinals;
    }

    public static string KeyFor(BuildingRegistration registration)
    {
        var address = registration.Address;
        return address.streetName + ":" + address.streetNumber;
    }

    private static ServerRole NormalizeServerRole(ServerRole role) => NormalizeServerRole((int)role);

    private static ServerRole NormalizeServerRole(int role)
    {
        return role < (int)ServerRole.Unassigned || role > (int)ServerRole.Hosting
            ? ServerRole.Unassigned
            : (ServerRole)role;
    }

    // Issue #26: record which business type owns this building's current project, so the per-type feature
    // math can resolve the feature list from FeatureMask. Called each sim tick and on each screen refresh.
    public static void NoteBusinessType(string key, string businessTypeName) => Get(key).BusinessTypeName = businessTypeName;

    public static void AddProgress(string key, float amount) => Get(key).Progress += amount;
    public static float GetProgress(string key) => Get(key).Progress;
    public static float GetReputation(string key) => Get(key).Reputation;
    public static int GetInstalledBase(string key) => Get(key).InstalledBase;
    public static int GetLastPatchDay(string key) => Get(key).LastPatchDay;
    public static string GetProductName(string key) => Get(key).ProductName ?? string.Empty;
    public static string GetProductNameOrDefault(string key, string defaultName)
    {
        var name = GetProductName(key);
        return string.IsNullOrWhiteSpace(name) ? defaultName : name;
    }

    public static void SetProductName(string key, string value)
    {
        if (!CanEditConcept(key))
            return;
        var name = (value ?? string.Empty).Trim();
        if (name.Length > 64)
            name = name.Substring(0, 64);
        Get(key).ProductName = name;
    }

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

    // ---- issue #22/#23 (Publishers & deals) ------------------------------------------------------------
    // The player's reputation with a publisher (by roster ordinal). Out-of-range ⇒ 0 (old/short saves).
    public static float GetPublisherRep(int index)
        => index >= 0 && index < PublisherReps.Count ? PublisherReps[index] : 0f;

    // Adjust reputation with a publisher, growing the backing list on demand and clamping to [0, RepMax].
    public static void AddPublisherRep(int index, float delta)
    {
        if (index < 0)
            return;
        while (PublisherReps.Count <= index)
            PublisherReps.Add(0f);
        PublisherReps[index] = Mathf.Clamp(PublisherReps[index] + delta, 0f, SiliconAlleyPublishers.RepMax);
    }

    // The active publisher deal for this studio's current product. HasDeal gates everything; the three getters
    // read the locked terms. Sign/Clear set them. A deal binds the in-flight product until it ships or misses.
    public static bool HasDeal(string key) => Get(key).DealPublisher >= 0;
    public static int GetDealPublisher(string key) => Get(key).DealPublisher;
    public static int GetDealDeadlineDay(string key) => Get(key).DealDeadlineDay;
    public static float GetDealPayout(string key) => Get(key).DealPayout;

    public static void SignDeal(string key, int publisherIndex, int deadlineDay, float payout)
    {
        var state = Get(key);
        state.DealPublisher = publisherIndex;
        state.DealDeadlineDay = deadlineDay;
        state.DealPayout = payout;
    }

    public static void ClearDeal(string key)
    {
        var state = Get(key);
        state.DealPublisher = -1;
        state.DealDeadlineDay = 0;
        state.DealPayout = 0f;
    }

    // ---- issue #27 (Contracts): a fixed-scope gig the studio works instead of its product ----------
    // A studio holds at most one contract. Active while ContractScope > 0. Distinct from a publisher deal:
    // a flat fee, no relationship — accepted from the phone, worked by staff, paid on an on-time delivery.
    public static bool HasContract(string key) => Get(key).ContractScope > 0f;
    public static float GetContractScope(string key) => Get(key).ContractScope;
    public static float GetContractProgress(string key) => Get(key).ContractProgress;
    public static int GetContractDeadlineDay(string key) => Get(key).ContractDeadlineDay;
    public static float GetContractPayout(string key) => Get(key).ContractPayout;

    public static void AcceptContract(string key, float scope, int deadlineDay, float payout)
    {
        var state = Get(key);
        state.ContractScope = scope;
        state.ContractProgress = 0f;
        state.ContractDeadlineDay = deadlineDay;
        state.ContractPayout = payout;
    }

    public static void AddContractProgress(string key, float amount) => Get(key).ContractProgress += amount;

    // Issue #27: lower the studio's reputation (floored at 0) — the penalty for missing a contract deadline.
    public static void PenalizeReputation(string key, float amount) => Get(key).Reputation = Mathf.Max(0f, Get(key).Reputation - amount);

    public static void ClearContract(string key)
    {
        var state = Get(key);
        state.ContractScope = 0f;
        state.ContractProgress = 0f;
        state.ContractDeadlineDay = 0;
        state.ContractPayout = 0f;
    }

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

    // Progress required to complete this building's current project, scaled by its locked type, the features
    // selected in the design wizard (issue #26) and the target platforms (issue #37 — extra platforms add
    // porting work). FeatureMask/PlatformMask 0 ⇒ both multipliers 1.0 ⇒ unchanged from before.
    public static float EffectiveProjectSize(string key)
    {
        var state = Get(key);
        return ProjectSize * DurationMultiplier(state.ProjectType < 0 ? (int)ProjectKind.Standard : state.ProjectType)
            * SiliconAlleyFeatures.SizeMultiplier(state.FeatureMask, state.BusinessTypeName)
            * SiliconAlleyPlatforms.SizeMultiplier(state.PlatformMask, state.BusinessTypeName);
    }

    // Issue #20/#21: complete the current project. The shipped quality already carries the bug penalty
    // (applied in the simulator), so a buggy build gives less reputation here automatically. Marketing
    // awareness adds a small reputation bonus on top and (via launchUnits, computed by the caller from
    // awareness + review) grows the installed base by more than the flat +1. SAVE-COMPAT: a legacy launch
    // has BugCount = Awareness = 0, so the bonus is 0 and launchUnits floors at 1 — identical to before.
    public static void OnProjectCompleted(string key, float quality, int launchUnits, float review,
        int day, float launchPayout, int publisher, string productName = "")
    {
        var state = Get(key);
        var shippedVersion = state.Version;
        var shippedKind = state.ProjectType < 0 ? (int)ProjectKind.Standard : state.ProjectType;
        var shippedProductName = string.IsNullOrWhiteSpace(productName) ? state.ProductName : productName.Trim();
        var shippedDependencyVendors = SerializeDependencyVendorOrdinals(EnsureDependencyVendors(state));
        state.Releases.Add(new ReleaseRecord(day, shippedVersion, shippedKind, review, quality, launchPayout,
            Mathf.Max(1, launchUnits), publisher, state.FeatureMask, state.PlatformMask, state.UsedToolsMask,
            state.SegmentId, shippedProductName, state.UsedDependencyMask, shippedDependencyVendors));
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
        state.FeatureMask = 0;   // issue #26: features are chosen per product — the next project starts feature-free
        state.FeatureWeights = null; // issue #85: per-feature allocation weights reset to neutral for the next product
        state.PlatformMask = 0;  // issue #37: target platforms are chosen per product too — reset for the next
        state.UsedToolsMask = 0; // issue #36: licensed/used tools are per-project — reset (OwnedToolsMask persists)
        state.SegmentId = 0;     // issue #38: the audience segment is a per-product choice — reset to Broad
        state.ProductName = string.Empty; // issue #82: the next project starts with the derived/default name
        state.UsedDependencyMask = 0;
        ResetDependencyVendors(state);
        state.DesignPrompted = false; // nudge again for the next project
        // Issue #88 (player-driven lifecycle): a ship returns the studio to Idle. Reset Progress explicitly
        // (an early release ships below 100%, so the simulator no longer subtracts the project size), and the
        // next project does NOT auto-start — the player chooses to start it (Stage stays Idle until then).
        state.Progress = 0f;
        state.Stage = (int)ProjectStage.Idle;
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

    // ---- issue #26 (Design Document): per-project feature selection ---------------------------------
    // The bitmask of features chosen for the current project (bit = SiliconAlleyFeatures Bit, per business
    // type). Edited only while the concept is editable, like scope/focus; reset per product on completion.
    public static int GetFeatureMask(string key) => Get(key).FeatureMask;
    public static bool HasFeature(string key, int bit) => (Get(key).FeatureMask & (1 << bit)) != 0;

    public static void ToggleFeature(string key, int bit)
    {
        if (CanEditConcept(key))
            Get(key).FeatureMask ^= 1 << bit;
    }

    // ---- issue #85 (Market targeting): per-feature % allocation weights ------------------------------
    // The weight the player allocates to a feature (indexed by FeatureId bit). Absent/non-positive ⇒ the
    // neutral 1.0, so an untouched/legacy project reads as an even allocation. Editable in Design only.
    public static float GetFeatureWeight(string key, int bit)
    {
        var weights = Get(key).FeatureWeights;
        return weights != null && bit >= 0 && bit < weights.Length && weights[bit] > 0f ? weights[bit] : 1f;
    }

    // The raw weights array (null when untouched ⇒ neutral). Read-only callers: the aspect fit math.
    public static float[] GetFeatureWeights(string key) => Get(key).FeatureWeights;

    public static void SetFeatureWeight(string key, int bit, float value)
    {
        if (!CanEditConcept(key) || bit < 0 || bit >= SiliconAlleyFeatures.MaxCount)
            return;
        var state = Get(key);
        if (state.FeatureWeights == null)
        {
            // Materialise the array at neutral (all 1.0) on first edit, so setting one slot reallocates relative
            // to the rest while every untouched project stays null ⇒ serialises empty ⇒ legacy-clean.
            state.FeatureWeights = new float[SiliconAlleyFeatures.MaxCount];
            for (var i = 0; i < state.FeatureWeights.Length; i++)
                state.FeatureWeights[i] = 1f;
        }
        state.FeatureWeights[bit] = Mathf.Max(0f, value);
    }

    // ---- issue #103 (Server roles): per-placed-server assignments -------------------------------
    // Role choices are keyed by ItemInstance.id, so moving/saving/reloading a placed server keeps the
    // assignment. Missing ids and Unassigned are neutral.
    public static ServerRole GetServerRole(string key, string itemInstanceId)
    {
        if (string.IsNullOrEmpty(itemInstanceId))
            return ServerRole.Unassigned;
        return Get(key).ServerRoles.TryGetValue(itemInstanceId, out var role)
            ? NormalizeServerRole(role)
            : ServerRole.Unassigned;
    }

    public static void SetServerRole(string key, string itemInstanceId, ServerRole role)
    {
        if (string.IsNullOrEmpty(itemInstanceId))
            return;
        var state = Get(key);
        var normalized = NormalizeServerRole(role);
        if (normalized == ServerRole.Unassigned)
            state.ServerRoles.Remove(itemInstanceId);
        else
            state.ServerRoles[itemInstanceId] = normalized;
    }

    public static Dictionary<ServerRole, int> ServerCountsByRole(string key, BuildingRegistration registration)
    {
        var counts = new Dictionary<ServerRole, int>
        {
            { ServerRole.Unassigned, 0 },
            { ServerRole.Infrastructure, 0 },
            { ServerRole.Backend, 0 },
            { ServerRole.Hosting, 0 }
        };

        var state = Get(key);
        if (registration?.itemInstances == null)
            return counts;

        var liveServerIds = new HashSet<string>();
        foreach (var pair in registration.itemInstances)
        {
            if (!SiliconAlleyOfficeSimulator.IsServerInstance(pair.Value))
                continue;
            liveServerIds.Add(pair.Key);
            counts[GetServerRole(key, pair.Key)]++;
        }

        var stale = new List<string>();
        foreach (var pair in state.ServerRoles)
            if (!liveServerIds.Contains(pair.Key))
                stale.Add(pair.Key);
        for (var i = 0; i < stale.Count; i++)
            state.ServerRoles.Remove(stale[i]);

        return counts;
    }

    // The design-phase quality ceiling, raised by the project's selected features (issue #26) and the tools it
    // uses (issue #36), and lowered by uncovered feature→tool dependencies (issue #39). A weak Design phase still
    // caps the shipped quality (issue #9). SAVE-COMPAT: designQuality < 0 (no Design work yet / legacy save) ⇒ no
    // cap (1.0), exactly as before; FeatureMask / UsedToolsMask 0 ⇒ bonus 0 + full coverage ⇒ the cap is the
    // unchanged 0.5 + 0.5*designQuality.
    public static float DesignQualityCeiling(string key, string businessTypeName, float designQuality, int day)
    {
        if (designQuality < 0f)
            return 1f;
        var state = Get(key);
        var bonus = SiliconAlleyFeatures.QualityBonus(state.FeatureMask, businessTypeName)
            + SiliconAlleyTools.QualityBonus(state.UsedToolsMask, businessTypeName) // issue #36
            + SiliconAlleyProductDependencies.QualityBonus(state.UsedDependencyMask, state.OwnedDependencyMask,
                EnsureDependencyVendors(state), businessTypeName) // issue #83
            + SiliconAlleyAspects.QualityFitBonus(state.FeatureMask, state.FeatureWeights, businessTypeName, day); // issue #85: market-fit (0 at neutral)
        var ceiling = Mathf.Min(1f, 0.5f + 0.5f * designQuality + bonus);
        // Issue #39: uncovered features (no owned/licensed provider tool) cap the achievable quality. Full
        // coverage / no features ⇒ CoverageCeiling 1.0 ⇒ the cap above is unchanged.
        return Mathf.Min(ceiling, SiliconAlleyDependencies.CoverageCeiling(
            state.FeatureMask, state.OwnedToolsMask, state.UsedToolsMask, businessTypeName));
    }

    // ---- issue #37 (Platforms): per-project target operating systems ---------------------------------
    // The bitmask of platforms targeted by the current project (bit = SiliconAlleyPlatforms Bit, per business
    // type). Edited only while the concept is editable, like features; reset per product on completion. More
    // platforms widen reach (LaunchReach) and add porting work (folded into EffectiveProjectSize).
    public static int GetPlatformMask(string key) => Get(key).PlatformMask;
    public static bool HasPlatform(string key, int bit) => (Get(key).PlatformMask & (1 << bit)) != 0;

    public static void TogglePlatform(string key, int bit)
    {
        if (CanEditConcept(key))
            Get(key).PlatformMask ^= 1 << bit;
    }

    // The launch reach multiplier from the project's selected platforms (issue #37): Σ selected share weights,
    // 1.0 for the default single "home" platform (PlatformMask 0). Scales the launch installed-base jump only;
    // the payout / MarketFactor are untouched. PlatformMask 0 ⇒ 1.0 ⇒ launch identical to before.
    public static float LaunchReach(string key, string businessTypeName)
        => SiliconAlleyPlatforms.ReachMultiplier(Get(key).PlatformMask, businessTypeName);

    // ---- issue #36 (Editors & tools): build-in-house vs license -------------------------------------
    // Two masks (bit = SiliconAlleyTools Bit, per business type): OwnedToolsMask is the studio's self-built tools
    // — STUDIO-LEVEL, it survives OnProjectCompleted and is reused across products for free. UsedToolsMask is the
    // tools applied to the CURRENT project — per-project, reset on completion. A used tool that isn't owned is
    // LICENSED (pays its royalty); a used+owned tool is free. Owning a tool (SetToolOwned) is paid for in cash by
    // the caller (SiliconAlleyMoney) before flipping the bit. Editable only while the concept is editable.
    public static int GetOwnedToolsMask(string key) => Get(key).OwnedToolsMask;
    public static int GetUsedToolsMask(string key) => Get(key).UsedToolsMask;
    public static bool IsToolOwned(string key, int bit) => (Get(key).OwnedToolsMask & (1 << bit)) != 0;
    public static bool IsToolUsed(string key, int bit) => (Get(key).UsedToolsMask & (1 << bit)) != 0;

    public static void ToggleToolUsed(string key, int bit)
    {
        if (CanEditConcept(key))
            Get(key).UsedToolsMask ^= 1 << bit;
    }

    // Mark a tool as studio-owned (built in-house). The caller charges the R&D cost first; this only flips the
    // persistent bit. Owning never resets, so a built tool is reusable on every future project at no cost.
    public static void SetToolOwned(string key, int bit)
    {
        if (CanEditConcept(key))
            Get(key).OwnedToolsMask |= 1 << bit;
    }

    // The recurring royalty fraction owed on the current project's LICENSED tools (used but not owned), 0 for an
    // all-owned / tool-free / legacy project. Layered on launch revenue + support income in the simulator; never
    // redefines MarketFactor / SupportRatePerDay. Both masks 0 ⇒ 0 ⇒ revenue/support identical to before.
    public static float ToolRoyalty(string key, string businessTypeName)
    {
        var state = Get(key);
        return SiliconAlleyTools.Royalty(state.UsedToolsMask, state.OwnedToolsMask, businessTypeName);
    }

    // ---- issue #83 (Build-or-buy dependencies): current-project slots + vendor ordinals ---------------
    public static int GetOwnedDependencyMask(string key) => Get(key).OwnedDependencyMask;
    public static int GetUsedDependencyMask(string key) => Get(key).UsedDependencyMask;
    public static bool IsDependencyOwned(string key, int bit) => (Get(key).OwnedDependencyMask & (1 << bit)) != 0;
    public static bool IsDependencyUsed(string key, int bit) => (Get(key).UsedDependencyMask & (1 << bit)) != 0;

    public static int GetDependencyVendorOrdinal(string key, int bit)
    {
        var vendors = EnsureDependencyVendors(Get(key));
        return bit >= 0 && bit < vendors.Length ? vendors[bit] : -1;
    }

    public static int[] GetDependencyVendorOrdinals(string key)
    {
        var vendors = EnsureDependencyVendors(Get(key));
        var copy = new int[vendors.Length];
        for (var i = 0; i < vendors.Length; i++)
            copy[i] = vendors[i];
        return copy;
    }

    public static bool IsDependencyOfferAvailable(string businessTypeName, int dependencyBit, int vendorOrdinal)
        => SiliconAlleyProductDependencies.HasOffer(businessTypeName, dependencyBit, vendorOrdinal);

    public static bool LicenseDependency(string key, string businessTypeName, int dependencyBit, int vendorOrdinal)
    {
        if (!CanEditConcept(key) || !SiliconAlleyProductDependencies.HasOffer(businessTypeName, dependencyBit, vendorOrdinal))
            return false;
        var state = Get(key);
        if ((state.OwnedDependencyMask & (1 << dependencyBit)) != 0)
            return UseOwnedDependency(key, businessTypeName, dependencyBit);
        var vendors = EnsureDependencyVendors(state);
        state.UsedDependencyMask |= 1 << dependencyBit;
        vendors[dependencyBit] = vendorOrdinal;
        return true;
    }

    public static bool UseOwnedDependency(string key, string businessTypeName, int dependencyBit)
    {
        if (!CanEditConcept(key) || !SiliconAlleyProductDependencies.TryGetDependency(businessTypeName, dependencyBit, out _))
            return false;
        var state = Get(key);
        if ((state.OwnedDependencyMask & (1 << dependencyBit)) == 0)
            return false;
        state.UsedDependencyMask |= 1 << dependencyBit;
        EnsureDependencyVendors(state)[dependencyBit] = -1;
        return true;
    }

    public static bool SetDependencyOwned(string key, string businessTypeName, int dependencyBit)
    {
        if (!CanEditConcept(key) || !SiliconAlleyProductDependencies.TryGetDependency(businessTypeName, dependencyBit, out _))
            return false;
        var state = Get(key);
        state.OwnedDependencyMask |= 1 << dependencyBit;
        state.UsedDependencyMask |= 1 << dependencyBit;
        EnsureDependencyVendors(state)[dependencyBit] = -1;
        return true;
    }

    public static bool ClearDependency(string key, string businessTypeName, int dependencyBit)
    {
        if (!CanEditConcept(key) || !SiliconAlleyProductDependencies.TryGetDependency(businessTypeName, dependencyBit, out _))
            return false;
        var state = Get(key);
        state.UsedDependencyMask &= ~(1 << dependencyBit);
        EnsureDependencyVendors(state)[dependencyBit] = -1;
        return true;
    }

    public static float DependencyQualityBonus(string key, string businessTypeName)
    {
        var state = Get(key);
        return SiliconAlleyProductDependencies.QualityBonus(state.UsedDependencyMask, state.OwnedDependencyMask,
            EnsureDependencyVendors(state), businessTypeName);
    }

    public static float DependencyRoyalty(string key, string businessTypeName)
    {
        var state = Get(key);
        return SiliconAlleyProductDependencies.Royalty(state.UsedDependencyMask, state.OwnedDependencyMask,
            EnsureDependencyVendors(state), businessTypeName);
    }

    public static float LaunchRoyalty(string key, string businessTypeName)
        => Mathf.Clamp(ToolRoyalty(key, businessTypeName) + DependencyRoyalty(key, businessTypeName), 0f, SiliconAlleyTools.MaxRoyalty);

    public static float DependencySupportRoyalty(string key, string businessTypeName)
    {
        var state = Get(key);
        if (state.InstalledBase <= 0 || state.Releases.Count == 0)
            return 0f;
        var weighted = 0f;
        foreach (var release in state.Releases)
        {
            if (release.LaunchUnits <= 0 || release.UsedDependencyMask == 0)
                continue;
            var vendors = ParseDependencyVendorOrdinals(release.DependencyVendorOrdinals);
            var royalty = SiliconAlleyProductDependencies.RoyaltyFromSnapshot(release.UsedDependencyMask, vendors, businessTypeName);
            weighted += release.LaunchUnits * royalty;
        }
        return Mathf.Clamp(weighted / Mathf.Max(1, state.InstalledBase), 0f, SiliconAlleyTools.MaxRoyalty);
    }

    // ---- issue #38 (Market): per-project audience segment -------------------------------------------
    // The product's target audience (SegmentId ordinal: 0=Broad default, 1=Enterprise, 2=Prosumer, 3=Consumer).
    // Shifts the price↔volume tradeoff: PriceFactor scales the launch payout, VolumeFactor the launch installed-
    // base jump. Per-project, edited only while the concept is editable; reset on completion. 0 ⇒ both ×1.0.
    public static int GetSegmentId(string key)
    {
        var id = Get(key).SegmentId;
        return id >= 0 && id < SiliconAlleySegments.Count ? id : 0; // out-of-range/forward save ⇒ Broad
    }

    public static void SetSegmentId(string key, int segmentId)
    {
        if (CanEditConcept(key) && segmentId >= 0 && segmentId < SiliconAlleySegments.Count)
            Get(key).SegmentId = segmentId;
    }

    public static float SegmentPriceFactor(string key) => SiliconAlleySegments.PriceFactor(GetSegmentId(key));
    public static float SegmentVolumeFactor(string key) => SiliconAlleySegments.VolumeFactor(GetSegmentId(key));

    // Issue #10 (Development): the studio's Overtime policy — a sticky toggle that only takes effect in
    // the Development phase (speeds the build, lowers quality). Settable any time; it just persists.
    public static bool IsOvertime(string key) => Get(key).Overtime != 0;
    public static void SetOvertime(string key, bool on) => Get(key).Overtime = on ? 1 : 0;

    // Issue #11 (Testing): the studio's legacy "Hold" QA policy. SUPERSEDED by manual release (every product
    // now parks at 100% and waits for the player), but the Hold field stays SERIALIZED at its index for
    // save-compat — these accessors remain so old saves load cleanly; the simulator no longer reads Hold.
    public static bool IsHold(string key) => Get(key).Hold != 0;
    public static void SetHold(string key, bool on) => Get(key).Hold = on ? 1 : 0;

    // ---- issue #88 (player-driven lifecycle) -------------------------------------------------------
    public static ProjectStage GetStage(string key) => (ProjectStage)Get(key).Stage;

    // The progress ceiling for a stage: staff work up to it, then the project PARKS there until the player
    // pushes forward. Design/Development park just UNDER their band end so the derived phase stays put (and
    // the wizard stays editable in Design); Testing parks at completion (100% = the "ready to release" state).
    public static float StageCeiling(ProjectStage stage, float size)
    {
        switch (stage)
        {
            case ProjectStage.Design: return DesignFraction * size - 1f;
            case ProjectStage.Development: return DevelopmentFraction * size - 1f;
            case ProjectStage.Testing: return size;
            default: return 0f; // Idle: no product work
        }
    }

    // Park Progress at a ceiling (no-op until it reaches the ceiling). Generalises the old ParkAtCompletion.
    public static void ParkBelowCeiling(string key, float ceiling)
    {
        var state = Get(key);
        if (state.Progress > ceiling)
            state.Progress = ceiling;
    }

    // Idle → Design: the player starts a fresh project (the next version). Resets progress + reopens the
    // wizard. The simulator then works the Design stage (parked at its ceiling) until the wizard is confirmed.
    public static void StartProject(string key)
    {
        var state = Get(key);
        state.Stage = (int)ProjectStage.Design;
        state.Progress = 0f;
        state.ConceptLocked = 0;
        state.ProjectType = GlobalProjectType; // adopt the current scope pre-selection for the new project
        state.ProductName = string.Empty;
        state.UsedDependencyMask = 0;
        ResetDependencyVendors(state);
        state.DesignPrompted = false;
    }

    // Design → Development: confirm the wizard (the 6 steps) — locks the concept AND advances the stage, so
    // staff start building past the Design ceiling. Called by the wizard's final "Start development" button.
    public static void BeginDevelopment(string key)
    {
        LockConcept(key);
        Get(key).Stage = (int)ProjectStage.Development;
    }

    // Development → Testing: the player sends the finished build to QA (available once Development is parked).
    public static void SendToTesting(string key) => Get(key).Stage = (int)ProjectStage.Testing;

    public static bool IsReleaseRequested(string key) => Get(key).ReleaseRequested;
    public static void RequestRelease(string key) => Get(key).ReleaseRequested = true;
    public static void ClearReleaseRequest(string key) => Get(key).ReleaseRequested = false;

    // Manual updates: an update is requested by the player; the simulator credits the patch on its next tick
    // (when an update is actually due — see PatchIntervalDays) and clears the request.
    public static bool IsUpdateRequested(string key) => Get(key).UpdateRequested;
    public static void RequestUpdate(string key) => Get(key).UpdateRequested = true;
    public static void ClearUpdateRequest(string key) => Get(key).UpdateRequested = false;

    // "Release": request that the simulator ship the current product on its next tick (the tick has the
    // correctly-bound building registration). Available any moment in Development/Testing — it ships at the
    // CURRENT accrued quality (an early ship reviews worse). Kept under the ShipNow name for existing callers.
    public static void ShipNow(string key) => RequestRelease(key);

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
        public readonly string ProductName;
        public ShipReport(bool has, float quality, float payout, float repMult, float marketMult, float review, string productName)
        {
            Has = has; Quality = quality; Payout = payout; RepMult = repMult; MarketMult = marketMult; Review = review;
            ProductName = productName ?? string.Empty;
        }
    }

    // Record the just-shipped project's headline numbers (the same values the success toast encodes),
    // including the 0..10 review score (#20).
    public static void SetLastShip(string key, float quality, float payout, float repMult, float marketMult, float review, string productName = "")
    {
        var state = Get(key);
        state.HasLastShip = true;
        state.LastShipQuality = quality;
        state.LastShipPayout = payout;
        state.LastShipRepMult = repMult;
        state.LastShipMarketMult = marketMult;
        state.LastShipReview = review;
        state.LastShipProductName = productName ?? string.Empty;
    }

    public static ShipReport GetLastShip(string key)
    {
        var s = Get(key);
        return new ShipReport(s.HasLastShip, s.LastShipQuality, s.LastShipPayout, s.LastShipRepMult,
            s.LastShipMarketMult, s.LastShipReview, s.LastShipProductName);
    }

    public static void ClearLastShip(string key)
    {
        var state = Get(key);
        state.HasLastShip = false;
        state.LastShipProductName = string.Empty;
    }

    public static List<ReleaseRecord> GetReleaseHistory(string key) => new List<ReleaseRecord>(Get(key).Releases);

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

    public static void Reset()
    {
        States.Clear();
        PublisherReps.Clear(); // issue #22: don't let publisher reputation bleed between saves loaded in a session
    }

    // --- persistence (stored in GameInstance.modData by SiliconAlleyPersistence) ---
    // SAVE COMPATIBILITY: this format is FORWARD-COMPATIBLE ONLY (see the Save Compatibility Policy in
    // CLAUDE.md). Never change the meaning/format of an existing field or reorder a persisted enum's
    // values; to change semantics, add a NEW field/key, bump CurrentSchemaVersion, and add a Migrate step.
    //
    // One entry per building (fields are APPEND-ONLY; older saves omit trailing fields, which default):
    // key|progress|reputation|installedBase|supportAccrual|qualitySum|qualityWeight|lastPatchDay|projectType
    //    |designQualitySum|designQualityWeight|devQualitySum|devQualityWeight|testQualitySum|testQualityWeight
    //    |designFocus|conceptLocked|overtime|hold|bugCount|awareness|hype|adSpend|supportFreshDay|version|ipReputation
    //    |dealPublisher|dealDeadlineDay|dealPayout|featureMask|platformMask|ownedToolsMask|usedToolsMask|segmentId
    //    |contractScope|contractProgress|contractDeadlineDay|contractPayout|stage|releaseHistory|productName
    //    |ownedDependencyMask|usedDependencyMask|dependencyVendorOrdinals|featureWeights|serverRoles,
    // joined by ';'. The publisher-deal fields (issue #23: dealPublisher default -1 = no deal, dealDeadlineDay/
    // dealPayout 0) append after the lifecycle fields; absent in old saves ⇒ no active deal. A third reserved
    // header "~publishers|r0,r1,…" carries the player's per-publisher reputation (issue #22, append-only by
    // roster ordinal; absent ⇒ all 0). The six per-phase quality fields (issue #8) and the Design/Development/Testing screen
    // fields (issue #9: designFocus default 0.5 = neutral, conceptLocked 0; issue #10: overtime 0 = off;
    // issue #11: hold 0 = off), then the go-to-market fields (issue #19: bugCount 0 = no bugs; issue #21:
    // awareness/hype/adSpend 0 = unmarketed), then the product-lifecycle fields (issue #25: supportFreshDay
    // 0 = anchored to full freshness on first use; issue #24: version default 1 = debut, ipReputation 0 = no
    // sequel bonus), were
    // appended at schema v1; a save from before a given field omits it and it defaults (per-phase quality
    // reads "not accrued" via GetPhaseQuality; designFocus stays 0.5; conceptLocked 0; overtime 0; bugCount,
    // awareness, hype, adSpend 0; supportFreshDay 0 ⇒ full freshness; version 1; ipReputation 0) while the
    // aggregate qualitySum/qualityWeight still yields the real shipped quality. ReleaseHistory is a count-prefixed
    // variable block; absent or count 0 means no recorded releases. ProductName is percent-escaped; absent or
    // empty means derive the name from the business type. Two reserved
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
    // Issue #22: reserved header carrying the player's per-publisher reputation (comma-separated, by ordinal).
    private const string PublishersKey = "~publishers";

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
        // Issue #22: the player's per-publisher reputation as a reserved header, comma-separated by roster
        // ordinal (append-only). Building keys never start with '~', so this can't collide with a record.
        builder.Append(PublishersKey).Append('|');
        for (int i = 0; i < PublisherReps.Count; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(PublisherReps[i].ToString(CultureInfo.InvariantCulture));
        }
        builder.Append(';');
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
                .Append(state.IpReputation.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.DealPublisher.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.DealDeadlineDay.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.DealPayout.ToString(CultureInfo.InvariantCulture)).Append('|')
                // Issue #40: design-wizard reserved block (frozen order — never reorder). All no-op 0 today.
                .Append(state.FeatureMask.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.PlatformMask.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.OwnedToolsMask.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.UsedToolsMask.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.SegmentId.ToString(CultureInfo.InvariantCulture)).Append('|')
                // Issue #27: contract job (first trailing append past the #40 wizard block). All 0 ⇒ no contract.
                .Append(state.ContractScope.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.ContractProgress.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.ContractDeadlineDay.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.ContractPayout.ToString(CultureInfo.InvariantCulture)).Append('|')
                // Issue #88: player-driven lifecycle stage (index 38, trailing append). Absent ⇒ inferred.
                .Append(state.Stage.ToString(CultureInfo.InvariantCulture)).Append('|')
                // Issue #78: release history block (index 39). Empty/absent => no history.
                .Append(SerializeReleaseHistory(state.Releases)).Append('|')
                // Issue #82: player-typed product name (index 40). Empty/absent => derived display name.
                .Append(EscapeReleaseString(state.ProductName)).Append('|')
                // Issue #83: build-or-buy dependencies (indices 41..43). All absent/0/-1 => no dependencies.
                .Append(state.OwnedDependencyMask.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(state.UsedDependencyMask.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(SerializeDependencyVendorOrdinals(EnsureDependencyVendors(state))).Append('|')
                // Issue #85: per-feature allocation weights (index 44). Empty/absent => neutral (even) weights.
                .Append(SerializeFeatureWeights(state.FeatureWeights)).Append('|')
                // Issue #103: server roles (index 45). Empty/absent => all placed servers Unassigned.
                .Append(SerializeServerRoles(state.ServerRoles)).Append(';');
        }
        return builder.ToString();
    }

    public static void LoadFrom(string data)
    {
        States.Clear();
        PublisherReps.Clear(); // issue #22: re-default so reputation can't bleed from a previously loaded save
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
                if (parts[0] == PublishersKey) // issue #22: per-publisher reputation (comma-separated by ordinal)
                {
                    if (parts.Length > 1 && parts[1].Length > 0)
                        foreach (var token in parts[1].Split(','))
                            PublisherReps.Add(float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var rep) ? rep : 0f);
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
                if (parts.Length > 26) // issue #23: active-deal publisher ordinal. Keep the -1 default (no deal)
                {
                    // if a present value parses as garbage, so a corrupt field never reads as publisher 0.
                    if (int.TryParse(parts[26], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dealPub))
                        state.DealPublisher = dealPub;
                }
                if (parts.Length > 27) // issue #23: deal deadline day (absent ⇒ 0)
                    int.TryParse(parts[27], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.DealDeadlineDay);
                if (parts.Length > 28) // issue #23: deal payout (absent ⇒ 0)
                    float.TryParse(parts[28], NumberStyles.Float, CultureInfo.InvariantCulture, out state.DealPayout);
                // Issue #40: design-wizard reserved block (frozen indices 29..33). All absent ⇒ field default 0.
                if (parts.Length > 29) // #26: feature bitmask (absent ⇒ 0, no extra features)
                    int.TryParse(parts[29], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.FeatureMask);
                if (parts.Length > 30) // #37: platform bitmask (absent ⇒ 0, single home platform)
                    int.TryParse(parts[30], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.PlatformMask);
                if (parts.Length > 31) // #36: owned-tools bitmask, studio-level (absent ⇒ 0, none)
                    int.TryParse(parts[31], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.OwnedToolsMask);
                if (parts.Length > 32) // #36: used-tools bitmask, per-project (absent ⇒ 0, none)
                    int.TryParse(parts[32], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.UsedToolsMask);
                if (parts.Length > 33) // #38: audience segment ordinal (absent ⇒ 0, Broad)
                    int.TryParse(parts[33], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.SegmentId);
                // Issue #27: contract job (indices 34..37, absent ⇒ 0 ⇒ no active contract).
                if (parts.Length > 34)
                    float.TryParse(parts[34], NumberStyles.Float, CultureInfo.InvariantCulture, out state.ContractScope);
                if (parts.Length > 35)
                    float.TryParse(parts[35], NumberStyles.Float, CultureInfo.InvariantCulture, out state.ContractProgress);
                if (parts.Length > 36)
                    int.TryParse(parts[36], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.ContractDeadlineDay);
                if (parts.Length > 37)
                    float.TryParse(parts[37], NumberStyles.Float, CultureInfo.InvariantCulture, out state.ContractPayout);
                // Issue #88: lifecycle stage (index 38). Absent in an old save ⇒ infer so a legacy in-flight
                // project keeps running to completion (Testing ceiling = 100%, then manual release) and a
                // just-shipped / fresh studio is Idle. New-format saves carry the real stage.
                if (parts.Length > 38)
                    int.TryParse(parts[38], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.Stage);
                else
                    state.Stage = state.Progress > 0f ? (int)ProjectStage.Testing : (int)ProjectStage.Idle;
                if (parts.Length > 39)
                    LoadReleaseHistory(parts[39], state.Releases);
                if (parts.Length > 40)
                    state.ProductName = UnescapeReleaseString(parts[40]);
                if (parts.Length > 41)
                    int.TryParse(parts[41], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.OwnedDependencyMask);
                if (parts.Length > 42)
                    int.TryParse(parts[42], NumberStyles.Integer, CultureInfo.InvariantCulture, out state.UsedDependencyMask);
                if (parts.Length > 43)
                    state.DependencyVendorOrdinals = ParseDependencyVendorOrdinals(parts[43]);
                if (parts.Length > 44) // issue #85: per-feature weights (absent ⇒ null ⇒ neutral allocation)
                    state.FeatureWeights = ParseFeatureWeights(parts[44]);
                if (parts.Length > 45) // issue #103: server roles keyed by ItemInstance.id (absent => all Unassigned)
                    LoadServerRoles(parts[45], state.ServerRoles);
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

    private static void ResetDependencyVendors(BusinessState state)
    {
        var vendors = EnsureDependencyVendors(state);
        for (var i = 0; i < vendors.Length; i++)
            vendors[i] = -1;
    }

    private static string SerializeDependencyVendorOrdinals(int[] vendors)
    {
        if (vendors == null || vendors.Length == 0)
            return string.Empty;
        var builder = new StringBuilder();
        for (var i = 0; i < vendors.Length; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(vendors[i].ToString(CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }

    private static int[] ParseDependencyVendorOrdinals(string encoded)
    {
        var vendors = new int[SiliconAlleyProductDependencies.MaxCount];
        for (var i = 0; i < vendors.Length; i++)
            vendors[i] = -1;
        if (string.IsNullOrEmpty(encoded))
            return vendors;
        var tokens = encoded.Split(',');
        for (var i = 0; i < tokens.Length && i < vendors.Length; i++)
            if (int.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var vendor))
                vendors[i] = SiliconAlleyVendors.TryGetById(vendor, out _) ? vendor : -1;
        return vendors;
    }

    // Issue #85: per-feature allocation weights as a comma-separated float list (by FeatureId bit). A null/empty
    // array (untouched/legacy project) serialises to "" ⇒ absent ⇒ neutral on load. InvariantCulture (nl-NL dev).
    private static string SerializeFeatureWeights(float[] weights)
    {
        if (weights == null || weights.Length == 0)
            return string.Empty;
        var builder = new StringBuilder();
        for (var i = 0; i < weights.Length; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(weights[i].ToString(CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }

    // Parse the per-feature weights; absent/empty ⇒ null ⇒ neutral (even) allocation (legacy unchanged). A
    // present-but-short list defaults the missing slots to the neutral 1.0; negatives clamp to neutral.
    private static float[] ParseFeatureWeights(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return null;
        var weights = new float[SiliconAlleyFeatures.MaxCount];
        for (var i = 0; i < weights.Length; i++)
            weights[i] = 1f;
        var tokens = encoded.Split(',');
        for (var i = 0; i < tokens.Length && i < weights.Length; i++)
            if (float.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w >= 0f)
                weights[i] = w;
        return weights;
    }

    private static string SerializeServerRoles(Dictionary<string, ServerRole> roles)
    {
        if (roles == null || roles.Count == 0)
            return "0:";

        var count = 0;
        foreach (var pair in roles)
            if (!string.IsNullOrEmpty(pair.Key) && NormalizeServerRole(pair.Value) != ServerRole.Unassigned)
                count++;
        if (count == 0)
            return "0:";

        var builder = new StringBuilder();
        builder.Append(count.ToString(CultureInfo.InvariantCulture)).Append(':');
        var written = 0;
        foreach (var pair in roles)
        {
            var role = NormalizeServerRole(pair.Value);
            if (string.IsNullOrEmpty(pair.Key) || role == ServerRole.Unassigned)
                continue;
            if (written > 0) builder.Append(',');
            builder.Append(EscapeReleaseString(pair.Key)).Append('~')
                .Append(((int)role).ToString(CultureInfo.InvariantCulture));
            written++;
        }
        return builder.ToString();
    }

    private static void LoadServerRoles(string encoded, Dictionary<string, ServerRole> roles)
    {
        roles.Clear();
        if (string.IsNullOrEmpty(encoded))
            return;

        var colon = encoded.IndexOf(':');
        if (colon < 0)
            return;
        if (!int.TryParse(encoded.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            || count <= 0)
            return;

        var payload = encoded.Substring(colon + 1);
        if (payload.Length == 0)
            return;
        var records = payload.Split(',');
        for (var i = 0; i < records.Length && i < count; i++)
        {
            var fields = records[i].Split('~');
            if (fields.Length < 2)
                continue;

            var itemInstanceId = UnescapeReleaseString(fields[0]);
            if (string.IsNullOrEmpty(itemInstanceId))
                continue;
            if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawRole))
                continue;

            var role = NormalizeServerRole(rawRole);
            if (role != ServerRole.Unassigned)
                roles[itemInstanceId] = role;
        }
    }

    private static string SerializeReleaseHistory(List<ReleaseRecord> releases)
    {
        if (releases == null || releases.Count == 0)
            return "0:";
        var builder = new StringBuilder();
        builder.Append(releases.Count.ToString(CultureInfo.InvariantCulture)).Append(':');
        for (int i = 0; i < releases.Count; i++)
        {
            if (i > 0) builder.Append(',');
            var r = releases[i];
            builder
                .Append(r.Day.ToString(CultureInfo.InvariantCulture)).Append('~')
                .Append(r.Version.ToString(CultureInfo.InvariantCulture)).Append('~')
                .Append(r.Kind.ToString(CultureInfo.InvariantCulture)).Append('~')
                .Append(r.Review.ToString(CultureInfo.InvariantCulture)).Append('~')
                .Append(r.Quality.ToString(CultureInfo.InvariantCulture)).Append('~')
                .Append(r.LaunchPayout.ToString(CultureInfo.InvariantCulture)).Append('~')
                .Append(r.LaunchUnits.ToString(CultureInfo.InvariantCulture)).Append('~')
                .Append(r.Publisher.ToString(CultureInfo.InvariantCulture)).Append('~')
                .Append(r.FeatureMask.ToString(CultureInfo.InvariantCulture)).Append('~')
                .Append(r.PlatformMask.ToString(CultureInfo.InvariantCulture)).Append('~')
                .Append(r.UsedToolsMask.ToString(CultureInfo.InvariantCulture)).Append('~')
                .Append(r.SegmentId.ToString(CultureInfo.InvariantCulture)).Append('~')
                .Append(EscapeReleaseString(r.ProductName)).Append('~')
                .Append(r.UsedDependencyMask.ToString(CultureInfo.InvariantCulture)).Append('~')
                .Append(EscapeReleaseString(r.DependencyVendorOrdinals));
        }
        return builder.ToString();
    }

    private static void LoadReleaseHistory(string encoded, List<ReleaseRecord> releases)
    {
        releases.Clear();
        if (string.IsNullOrEmpty(encoded))
            return;

        var colon = encoded.IndexOf(':');
        if (colon < 0)
            return;
        if (!int.TryParse(encoded.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            || count <= 0)
            return;

        var payload = encoded.Substring(colon + 1);
        if (payload.Length == 0)
            return;
        var records = payload.Split(',');
        for (int i = 0; i < records.Length && i < count; i++)
        {
            var fields = records[i].Split('~');
            if (fields.Length < 12)
                continue;

            int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var day);
            int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var version);
            int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kind);
            float.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var review);
            float.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var quality);
            float.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var launchPayout);
            int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var launchUnits);
            int.TryParse(fields[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var publisher);
            int.TryParse(fields[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var featureMask);
            int.TryParse(fields[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var platformMask);
            int.TryParse(fields[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out var usedToolsMask);
            int.TryParse(fields[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out var segmentId);
            var productName = fields.Length > 12 ? UnescapeReleaseString(fields[12]) : string.Empty;
            var usedDependencyMask = 0;
            if (fields.Length > 13)
                int.TryParse(fields[13], NumberStyles.Integer, CultureInfo.InvariantCulture, out usedDependencyMask);
            var dependencyVendorOrdinals = fields.Length > 14 ? UnescapeReleaseString(fields[14]) : string.Empty;
            releases.Add(new ReleaseRecord(day, version, kind, review, quality, launchPayout, launchUnits,
                publisher, featureMask, platformMask, usedToolsMask, segmentId, productName,
                usedDependencyMask, dependencyVendorOrdinals));
        }
    }

    private static string EscapeReleaseString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c == '%' || c == '|' || c == ';' || c == ',' || c == ':' || c == '~')
                builder.Append('%').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
            else
                builder.Append(c);
        }
        return builder.ToString();
    }

    private static string UnescapeReleaseString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && i + 2 < value.Length
                && int.TryParse(value.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
            {
                builder.Append((char)code);
                i += 2;
            }
            else
            {
                builder.Append(value[i]);
            }
        }
        return builder.ToString();
    }
}
