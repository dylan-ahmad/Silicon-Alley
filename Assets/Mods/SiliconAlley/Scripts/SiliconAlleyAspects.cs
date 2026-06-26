using System;
using UnityEngine;

// Issue #85 (Market targeting): a Software-Inc-style aspect-demand + per-feature allocation model. The market
// "wants" a time-varying MIX of per-type ASPECTS; the studio's FEATURES (#26) contribute to aspects; the
// player's per-feature % WEIGHTS (persisted in BusinessState.FeatureWeights) produce an aspect ALLOCATION that
// is scored against demand (a FIT score) to nudge the shipped quality ceiling + launch-market revenue. Backs
// the Market-targeting screen (#86); this issue ships the model only (no UI).
//
// SAVE-COMPAT: everything here is DERIVED (zero persisted state) EXCEPT the per-feature weights, which live in
// SiliconAlleyState. The fit is scored RELATIVE TO THE NEUTRAL (even) allocation, so even/absent weights ⇒
// FitDelta 0 ⇒ QualityFitBonus 0 and MarketFitFactor 1.0 — old saves and untouched projects are unchanged. The
// aspect catalog + feature→aspect map + demand math are tunable (not persisted), so they may change freely
// between versions; only the FeatureId bits (already shipped, #26) are load-bearing.
public static class SiliconAlleyAspects
{
    public readonly struct Aspect
    {
        public readonly int Bit;        // index/phase slot in the per-type aspect array — NOT persisted
        public readonly string Id;      // stable identifier (doc/debug; not localized)
        public readonly string NameKey; // locale key for the display name (#86 surfaces it)

        public Aspect(int bit, string id, string nameKey)
        {
            Bit = bit; Id = id; NameKey = nameKey;
        }
    }

    // A feature's contribution to an aspect (per business type). Weight is unitless; a feature may contribute to
    // several aspects (here each feature maps to one primary aspect at weight 1 — tunable catalog data).
    public readonly struct FeatureAspect
    {
        public readonly int FeatureBit;
        public readonly int AspectBit;
        public readonly float Weight;

        public FeatureAspect(int featureBit, int aspectBit, float weight)
        {
            FeatureBit = featureBit; AspectBit = aspectBit; Weight = weight;
        }
    }

    // ---- per-type aspect catalogs (3 each; tunable display content) ----
    public static readonly Aspect[] Office =
    {
        new Aspect(0, "siliconalley:aspect_office_productivity", "siliconalley:aspect_office_productivity"),
        new Aspect(1, "siliconalley:aspect_office_integration",  "siliconalley:aspect_office_integration"),
        new Aspect(2, "siliconalley:aspect_office_enterprise",   "siliconalley:aspect_office_enterprise"),
    };

    public static readonly Aspect[] Security =
    {
        new Aspect(0, "siliconalley:aspect_security_detection",  "siliconalley:aspect_security_detection"),
        new Aspect(1, "siliconalley:aspect_security_compliance", "siliconalley:aspect_security_compliance"),
        new Aspect(2, "siliconalley:aspect_security_response",   "siliconalley:aspect_security_response"),
    };

    public static readonly Aspect[] Game =
    {
        new Aspect(0, "siliconalley:aspect_game_visuals",    "siliconalley:aspect_game_visuals"),
        new Aspect(1, "siliconalley:aspect_game_simulation", "siliconalley:aspect_game_simulation"),
        new Aspect(2, "siliconalley:aspect_game_content",    "siliconalley:aspect_game_content"),
    };

    // ---- feature→aspect contribution maps (FeatureBit → AspectBit; tunable). Each feature → one aspect. ----
    // Office features (#26): 0 cloudsync, 1 pluginapi, 2 collab, 3 automation, 4 enterprise.
    private static readonly FeatureAspect[] OfficeMap =
    {
        new FeatureAspect(0, 1, 1f), // cloudsync   → Integration
        new FeatureAspect(1, 1, 1f), // pluginapi   → Integration
        new FeatureAspect(2, 0, 1f), // collab      → Productivity
        new FeatureAspect(3, 0, 1f), // automation  → Productivity
        new FeatureAspect(4, 2, 1f), // enterprise  → Enterprise
    };
    // Security features: 0 threatfeed, 1 compliance, 2 pentest, 3 zerotrust, 4 incident.
    private static readonly FeatureAspect[] SecurityMap =
    {
        new FeatureAspect(0, 0, 1f), // threatfeed  → Detection
        new FeatureAspect(1, 1, 1f), // compliance  → Compliance
        new FeatureAspect(2, 0, 1f), // pentest     → Detection
        new FeatureAspect(3, 1, 1f), // zerotrust   → Compliance
        new FeatureAspect(4, 2, 1f), // incident    → Response
    };
    // Game features: 0 graphics, 1 physics, 2 multiplayer, 3 procedural, 4 modsupport.
    private static readonly FeatureAspect[] GameMap =
    {
        new FeatureAspect(0, 0, 1f), // graphics    → Visuals
        new FeatureAspect(1, 1, 1f), // physics     → Simulation
        new FeatureAspect(2, 2, 1f), // multiplayer → Content
        new FeatureAspect(3, 1, 1f), // procedural  → Simulation
        new FeatureAspect(4, 2, 1f), // modsupport  → Content
    };

    // ---- demand + fit tuning (all tunable; non-persisted) ----
    public const float AspectAmplitude = 0.4f; // per-aspect demand swing (vs SiliconAlleyMarket's 0.25 overall)
    public const float PeriodDays = 72f;        // reuse the market-cycle length so the "wanted mix" rotates
    private const float KQuality = 0.15f, MaxQuality = 0.06f; // fit → quality-ceiling bonus (relative to neutral)
    private const float KMarket = 0.5f, MaxMarket = 0.15f;    // fit → launch-market factor (relative to neutral)

    public static Aspect[] AspectsFor(string businessTypeName)
    {
        switch (businessTypeName)
        {
            case "siliconalley:businesstype_softwarestudio": return Office;
            case "siliconalley:businesstype_cybersecurity":  return Security;
            case "siliconalley:businesstype_gamestudio":     return Game;
            default: return Array.Empty<Aspect>();
        }
    }

    private static FeatureAspect[] MapFor(string businessTypeName)
    {
        switch (businessTypeName)
        {
            case "siliconalley:businesstype_softwarestudio": return OfficeMap;
            case "siliconalley:businesstype_cybersecurity":  return SecurityMap;
            case "siliconalley:businesstype_gamestudio":     return GameMap;
            default: return Array.Empty<FeatureAspect>();
        }
    }

    public static int MaxCount => Mathf.Max(Office.Length, Mathf.Max(Security.Length, Game.Length));

    // ---- demand: "what the market wants" as a normalized share per aspect, derived from the game day ----
    // A slow per-aspect sine (phase-spread so the wanted mix rotates), floored ≥ 0 and normalized to sum 1.
    // Mirrors the #28 market cycle but per aspect. Pure function of the day — nothing is persisted.
    public static float[] DemandProfile(string businessTypeName, int day)
    {
        var aspects = AspectsFor(businessTypeName);
        var profile = new float[aspects.Length];
        var sum = 0f;
        var spread = PeriodDays / Mathf.Max(1, aspects.Length);
        for (var i = 0; i < aspects.Length; i++)
        {
            var d = 1f + AspectAmplitude * Mathf.Sin(2f * Mathf.PI * (day + aspects[i].Bit * spread) / PeriodDays);
            profile[i] = Mathf.Max(0f, d);
            sum += profile[i];
        }
        Normalize(profile, sum);
        return profile;
    }

    // ---- allocation: the player's % allocation across aspects, from the selected features' weights ----
    // Each selected feature contributes its weight to the aspect(s) it maps to; the result is normalized to sum
    // 1. `weights` may be null (legacy/untouched) ⇒ even weights. Unselected features contribute nothing.
    public static float[] AllocationProfile(string businessTypeName, int featureMask, float[] weights)
        => BuildAllocation(businessTypeName, featureMask, weights, neutral: false);

    // The neutral baseline: the SAME selected features with EVEN weights. Fit is measured against this, so
    // even/absent weights produce zero delta (legacy unchanged).
    public static float[] NeutralProfile(string businessTypeName, int featureMask)
        => BuildAllocation(businessTypeName, featureMask, null, neutral: true);

    private static float[] BuildAllocation(string businessTypeName, int featureMask, float[] weights, bool neutral)
    {
        var aspectCount = AspectsFor(businessTypeName).Length;
        var profile = new float[aspectCount];
        if (aspectCount == 0 || featureMask == 0)
            return profile; // all-zero ⇒ no allocation signal (FitDelta returns 0 for < 2 features anyway)
        foreach (var fa in MapFor(businessTypeName))
        {
            if ((featureMask & (1 << fa.FeatureBit)) == 0 || fa.AspectBit < 0 || fa.AspectBit >= aspectCount)
                continue;
            var w = (neutral ? 1f : WeightAt(weights, fa.FeatureBit)) * fa.Weight;
            profile[fa.AspectBit] += w;
        }
        var sum = 0f;
        foreach (var v in profile)
            sum += v;
        Normalize(profile, sum);
        return profile;
    }

    // The stored weight for a feature, or the neutral 1.0 when absent/non-positive (legacy / untouched slots).
    private static float WeightAt(float[] weights, int featureBit)
        => weights != null && featureBit >= 0 && featureBit < weights.Length && weights[featureBit] > 0f
            ? weights[featureBit] : 1f;

    private static void Normalize(float[] profile, float sum)
    {
        if (sum <= 0f)
            return;
        for (var i = 0; i < profile.Length; i++)
            profile[i] /= sum;
    }

    // Similarity of two normalized profiles: 1 - half the L1 distance (1 = identical, 0 = disjoint).
    public static float Fit(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
            return 0f;
        var dist = 0f;
        for (var i = 0; i < a.Length; i++)
            dist += Mathf.Abs(a[i] - b[i]);
        return 1f - 0.5f * dist;
    }

    // How much better (or worse) the player's allocation fits demand than the neutral (even) allocation. 0 when
    // weights are neutral/absent, or fewer than 2 features are selected (no reallocation possible) — legacy-safe.
    public static float FitDelta(string businessTypeName, int featureMask, float[] weights, int day)
    {
        if (CountBits(featureMask) < 2)
            return 0f;
        var demand = DemandProfile(businessTypeName, day);
        var player = AllocationProfile(businessTypeName, featureMask, weights);
        var neutral = NeutralProfile(businessTypeName, featureMask);
        return Fit(player, demand) - Fit(neutral, demand);
    }

    // Quality-ceiling bonus from market fit (folded into DesignQualityCeiling). Default 0 at neutral weights.
    public static float QualityFitBonus(int featureMask, float[] weights, string businessTypeName, int day)
        => Mathf.Clamp(FitDelta(businessTypeName, featureMask, weights, day) * KQuality, -MaxQuality, MaxQuality);

    // Launch-market multiplier from market fit (scales launch payout + the installed-base jump). 1.0 at neutral.
    public static float MarketFitFactor(int featureMask, float[] weights, string businessTypeName, int day)
        => 1f + Mathf.Clamp(FitDelta(businessTypeName, featureMask, weights, day) * KMarket, -MaxMarket, MaxMarket);

    private static int CountBits(int mask)
    {
        var count = 0;
        while (mask != 0) { mask &= mask - 1; count++; }
        return count;
    }
}
