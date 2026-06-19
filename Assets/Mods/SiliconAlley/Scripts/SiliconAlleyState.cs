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
    }

    private static readonly Dictionary<string, BusinessState> States = new Dictionary<string, BusinessState>();

    // ---- tunables (driven by the in-game options panel; defaults match a 100/100/20 slider) ----
    public static float ProjectSpeed = 1f;         // progress per programmer skill-point per hour
    public static float ProjectSize = 2800f;       // progress to complete one project (~2800 ≈ a skill-70 solo programmer over ~7 in-game calendar days full-time; the Project-speed slider tunes the pace)
    public static float PayoutMultiplier = 1f;     // global payout scale
    public static float SupportRatePerDay = 0.02f; // support income per installed unit per day, as a fraction of market price

    // ---- project lifecycle phases (derived from Progress, so they need no extra persisted state) ----
    // A project advances Design -> Development -> Testing -> Release as Progress climbs to ProjectSize.
    public enum ProjectPhase { Design, Development, Testing, Release }
    private const float DesignFraction = 0.15f;      // Design occupies 0%..15% of ProjectSize
    private const float DevelopmentFraction = 0.70f; // Development 15%..70%, Testing 70%..100%

    public static ProjectPhase PhaseOf(float progress)
    {
        var fraction = progress / Mathf.Max(1f, ProjectSize);
        if (fraction >= 1f) return ProjectPhase.Release;
        if (fraction >= DevelopmentFraction) return ProjectPhase.Testing;
        if (fraction >= DesignFraction) return ProjectPhase.Development;
        return ProjectPhase.Design;
    }

    // Progress within the current phase, 0..1 (for display).
    public static float PhaseProgressFraction(float progress)
    {
        var fraction = Mathf.Clamp01(progress / Mathf.Max(1f, ProjectSize));
        float lo, hi;
        if (fraction >= DevelopmentFraction) { lo = DevelopmentFraction; hi = 1f; }
        else if (fraction >= DesignFraction) { lo = DesignFraction; hi = DevelopmentFraction; }
        else { lo = 0f; hi = DesignFraction; }
        return Mathf.Clamp01((fraction - lo) / Mathf.Max(0.0001f, hi - lo));
    }

    // Progress value at which the given phase ends (Testing/Release end at completion). Lets the
    // dashboard compute "progress remaining in this phase" without duplicating the fraction constants.
    public static float PhaseEndProgress(ProjectPhase phase)
    {
        switch (phase)
        {
            case ProjectPhase.Design: return DesignFraction * ProjectSize;
            case ProjectPhase.Development: return DevelopmentFraction * ProjectSize;
            default: return ProjectSize;
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

    public static void OnProjectCompleted(string key, float quality)
    {
        var state = Get(key);
        state.Reputation = Mathf.Min(3f, state.Reputation + quality * 0.1f);
        state.InstalledBase++;
        state.QualitySum = 0f;     // the next project's quality accrues fresh
        state.QualityWeight = 0f;
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
    // key|progress|reputation|installedBase|supportAccrual|qualitySum|qualityWeight|lastPatchDay, joined
    // by ';'. Older saves omit the trailing fields; LoadFrom tolerates their absence.
    // InvariantCulture is required so a locale with comma decimals (e.g. nl-NL) cannot corrupt it.
    public static string Serialize()
    {
        var builder = new StringBuilder();
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
                .Append(state.LastPatchDay.ToString(CultureInfo.InvariantCulture)).Append(';');
        }
        return builder.ToString();
    }

    public static void LoadFrom(string data)
    {
        States.Clear();
        if (string.IsNullOrEmpty(data))
            return;
        foreach (var entry in data.Split(';'))
        {
            if (string.IsNullOrEmpty(entry))
                continue;
            var parts = entry.Split('|');
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
            States[parts[0]] = state;
        }
    }
}
