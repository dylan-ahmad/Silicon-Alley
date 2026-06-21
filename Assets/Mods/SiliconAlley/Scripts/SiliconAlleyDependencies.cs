using UnityEngine;

// Issue #39 (Dependencies): every feature in the design document (#26) needs a TOOL that "provides" it (#36).
// Software Inc. binds each feature to an editor/tool on a Dependencies screen; an unmet dependency ("built
// without the right tool") ships at a lower quality ceiling. Silicon Alley keeps that MEANING but drops the
// free-form per-feature binding matrix (high UI cost, thin payoff — simplification recorded in #15): coverage
// is a DERIVED bonus, computed from the already-persisted featureMask + ownedToolsMask/usedToolsMask.
//
// A selected feature is COVERED if the studio owns or licenses at least one tool of its provider set. Uncovered
// features lower the quality ceiling (folded into SiliconAlleyState.DesignQualityCeiling via Min). SAVE-COMPAT:
// ZERO new persisted state — purely derived. A feature-less project / old save has no selected features ⇒
// total 0 ⇒ CoverageCeiling 1.0 ⇒ no penalty ⇒ today's behaviour exactly. The provider mapping is tunable
// catalog data referencing the append-only feature/tool Bits; it is NOT persisted.
public static class SiliconAlleyDependencies
{
    public const float UncoveredCeilingPenalty = 0.12f; // each uncovered feature drops the quality ceiling by this

    // Provider tool mask per feature, indexed by the feature's Bit (see SiliconAlleyFeatures / SiliconAlleyTools).
    // A feature is covered if (ownedToolsMask | usedToolsMask) intersects its provider mask. A 0 entry means "no
    // dependency" ⇒ always covered. Tunable data — never persisted.
    private static readonly int[] Office =   // features: 0 CloudSync 1 PluginApi 2 Collab 3 Automation 4 Enterprise
    {                                          // tools:    0 AppFramework 1 Database 2 UIToolkit
        (1 << 1) | (1 << 0), // CloudSync     <- Database | AppFramework
        (1 << 0),            // PluginApi     <- AppFramework
        (1 << 1) | (1 << 2), // Collab        <- Database | UIToolkit
        (1 << 0),            // Automation    <- AppFramework
        (1 << 1),            // Enterprise    <- Database
    };

    private static readonly int[] Security = // features: 0 ThreatFeed 1 Compliance 2 PenTest 3 ZeroTrust 4 Incident
    {                                          // tools:    0 ScanEngine 1 CryptoLib 2 SIEM
        (1 << 0) | (1 << 2), // ThreatFeed    <- ScanEngine | SIEM
        (1 << 2),            // Compliance    <- SIEM
        (1 << 0),            // PenTest       <- ScanEngine
        (1 << 1),            // ZeroTrust     <- CryptoLib
        (1 << 2),            // Incident      <- SIEM
    };

    private static readonly int[] Game =     // features: 0 Graphics 1 Physics 2 Multiplayer 3 Procedural 4 Mod
    {                                          // tools:    0 GameEngine 1 ArtSuite 2 Audio
        (1 << 0) | (1 << 1), // Graphics      <- GameEngine | ArtSuite
        (1 << 0),            // Physics       <- GameEngine
        (1 << 0),            // Multiplayer   <- GameEngine
        (1 << 0),            // Procedural    <- GameEngine
        (1 << 0),            // Mod           <- GameEngine
    };

    private static int[] ProvidersFor(string businessTypeName)
    {
        switch (businessTypeName)
        {
            case "siliconalley:businesstype_softwarestudio": return Office;
            case "siliconalley:businesstype_cybersecurity":  return Security;
            case "siliconalley:businesstype_gamestudio":     return Game;
            default: return System.Array.Empty<int>();
        }
    }

    // The provider tool mask for a feature Bit (0 = no dependency ⇒ always covered; also for out-of-range bits).
    public static int ProviderMask(string businessTypeName, int featureBit)
    {
        var arr = ProvidersFor(businessTypeName);
        return featureBit >= 0 && featureBit < arr.Length ? arr[featureBit] : 0;
    }

    // True if the feature is covered by the studio's owned + licensed tools (or has no dependency).
    public static bool IsCovered(string businessTypeName, int featureBit, int ownedMask, int usedMask)
    {
        var providers = ProviderMask(businessTypeName, featureBit);
        return providers == 0 || ((ownedMask | usedMask) & providers) != 0;
    }

    // Covered/total over the SELECTED features (those set in featureMask). total 0 ⇒ no features chosen.
    public static void Coverage(int featureMask, int ownedMask, int usedMask, string businessTypeName,
        out int covered, out int total)
    {
        covered = 0;
        total = 0;
        foreach (var f in SiliconAlleyFeatures.FeaturesFor(businessTypeName))
        {
            if ((featureMask & (1 << f.Bit)) == 0)
                continue; // feature not selected
            total++;
            if (IsCovered(businessTypeName, f.Bit, ownedMask, usedMask))
                covered++;
        }
    }

    // The quality-ceiling factor from feature→tool coverage: 1.0 at full coverage (or no features), dropping by
    // UncoveredCeilingPenalty per uncovered feature. Min'd into the design-quality ceiling. featureMask 0 ⇒ 1.0.
    public static float CoverageCeiling(int featureMask, int ownedMask, int usedMask, string businessTypeName)
    {
        Coverage(featureMask, ownedMask, usedMask, businessTypeName, out var covered, out var total);
        if (total == 0)
            return 1f;
        return Mathf.Clamp01(1f - (total - covered) * UncoveredCeilingPenalty);
    }
}
