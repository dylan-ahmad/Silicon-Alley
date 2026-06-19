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
        public int LastPatchDay;     // game-day the live catalog was last patched (post-release updates)
        public int ProjectType = -1; // ProjectKind locked in for the current project; -1 = not yet locked
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
        state.ProjectType = GlobalProjectType; // the next project uses the current global selection
    }

    // Step 3 (quality): sample this hour's staff quality (0..1), weighted by phase so Testing-phase
    // staffing matters more — under-skilled testing lowers the shipped quality ("bugs"). Averaged at
    // release via GetAverageQuality.
    public static void AccumulateQuality(string key, float sample, float weight)
    {
        var state = Get(key);
        state.QualitySum += Mathf.Clamp01(sample) * weight;
        state.QualityWeight += weight;
    }

    // Average accrued quality (0..1), or -1 when nothing has accrued yet (caller falls back).
    public static float GetAverageQuality(string key)
    {
        var state = Get(key);
        return state.QualityWeight > 0f ? state.QualitySum / state.QualityWeight : -1f;
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
    // One entry per building:
    // key|progress|reputation|installedBase|supportAccrual|qualitySum|qualityWeight|lastPatchDay|projectType,
    // joined by ';'. A leading "~global|<index>" entry carries the player's project-type pre-selection so
    // it survives a session before the options menu (which re-applies the PlayerPrefs value) is opened.
    // Older saves omit the trailing fields; LoadFrom tolerates their absence.
    // InvariantCulture is required so a locale with comma decimals (e.g. nl-NL) cannot corrupt it.
    private const string GlobalTypeKey = "~global";

    public static string Serialize()
    {
        var builder = new StringBuilder();
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
                .Append(state.ProjectType.ToString(CultureInfo.InvariantCulture)).Append(';');
        }
        return builder.ToString();
    }

    public static void LoadFrom(string data)
    {
        States.Clear();
        GlobalProjectType = (int)ProjectKind.Standard; // headerless (older) saves default cleanly
        if (string.IsNullOrEmpty(data))
            return;
        foreach (var entry in data.Split(';'))
        {
            if (string.IsNullOrEmpty(entry))
                continue;
            var parts = entry.Split('|');
            if (parts[0] == GlobalTypeKey) // the project-type pre-selection header, not a building
            {
                if (parts.Length > 1)
                    int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out GlobalProjectType);
                continue;
            }
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
            States[parts[0]] = state;
        }
    }
}
