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
        public bool DesignPrompted;  // transient (NOT persisted): one "set your concept" nudge per project
    }

    private static readonly Dictionary<string, BusinessState> States = new Dictionary<string, BusinessState>();

    // ---- tunables (driven by the in-game options panel; defaults match a 100/100/20 slider) ----
    public static float ProjectSpeed = 1f;         // progress per programmer skill-point per hour
    public static float ProjectSize = 2800f;       // progress to complete one project (~2800 ≈ a skill-70 solo programmer over ~7 in-game calendar days full-time; the Project-speed slider tunes the pace)
    public static float PayoutMultiplier = 1f;     // global payout scale
    public static float SupportRatePerDay = 0.02f; // support income per installed unit per day, as a fraction of market price

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
    public static void SetLastPatchDay(string key, int day) => Get(key).LastPatchDay = day;

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

    public static void OnProjectCompleted(string key, float quality)
    {
        var state = Get(key);
        state.Reputation = Mathf.Min(3f, state.Reputation + quality * 0.1f);
        state.InstalledBase++;
        state.QualitySum = 0f;     // the next project's quality accrues fresh
        state.QualityWeight = 0f;
        // Issue #8: per-phase accumulators reset with the aggregate so the next project's phases accrue
        // from zero (they mirror the aggregate, never replace it).
        state.DesignQualitySum = 0f; state.DesignQualityWeight = 0f;
        state.DevQualitySum = 0f; state.DevQualityWeight = 0f;
        state.TestQualitySum = 0f; state.TestQualityWeight = 0f;
        state.ProjectType = GlobalProjectType; // the next project uses the current global selection
        state.ConceptLocked = 0; // issue #9: the next project's concept reopens (DesignFocus stays sticky)
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

    // Accrue recurring support income; returns a whole-currency payout once it crosses 1.
    public static float AccrueSupport(string key, float marketPrice)
    {
        var state = Get(key);
        state.SupportAccrual += state.InstalledBase * marketPrice * (SupportRatePerDay / 24f);
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
    //    |designFocus|conceptLocked|overtime,
    // joined by ';'. The six per-phase quality fields (issue #8) and the Design/Development screen fields
    // (issue #9: designFocus default 0.5 = neutral, conceptLocked 0; issue #10: overtime 0 = off) were
    // appended at schema v1; a save from before a given field omits it and it defaults (per-phase quality
    // reads "not accrued" via GetPhaseQuality; designFocus stays 0.5; conceptLocked 0; overtime 0) while the
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
                .Append(state.Overtime.ToString(CultureInfo.InvariantCulture)).Append(';');
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
