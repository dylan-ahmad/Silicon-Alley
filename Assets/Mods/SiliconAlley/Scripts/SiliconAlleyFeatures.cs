using System;
using UnityEngine;

// Issue #26 (Design Document — feature selection): the mod-defined feature list each business type can put
// into its product during the Design phase, plus the pure scope/quality math the wizard + simulator share.
// Selecting features makes a project BIGGER (more size/dev-time, EffectiveProjectSize) and raises its
// achievable QUALITY CEILING — Software Inc.'s "design box" in miniature. The picker is the Features page of
// the design wizard (#34/#35); selection persists in BusinessState.FeatureMask (the bit slot reserved by #40).
//
// SAVE-COMPAT: the per-type tables are APPEND-ONLY. A feature's Bit is its persisted token (set in the
// FeatureMask bitmask), so a Bit, once shipped, must NEVER be renamed/reordered/removed — only append new
// features (and record them in the SHIPPED_ENUMS ledger in CLAUDE.md). FeatureMask 0 (old saves / no
// selection) ⇒ SizeMultiplier 1.0 and QualityBonus 0 ⇒ behaviour identical to before this issue. Display
// names live in Locales/en.json and may change freely.
public static class SiliconAlleyFeatures
{
    public readonly struct Feature
    {
        public readonly int Bit;                   // persisted bit position in FeatureMask — APPEND-ONLY
        public readonly string Id;                 // stable identifier (documentation/debug; not localized)
        public readonly string NameKey;            // locale key for the display name
        public readonly float SizeCost;            // +fraction of project size/dev-time when selected
        public readonly float QualityContribution; // +fraction added to the quality ceiling when selected

        public Feature(int bit, string id, string nameKey, float sizeCost, float qualityContribution)
        {
            Bit = bit; Id = id; NameKey = nameKey; SizeCost = sizeCost; QualityContribution = qualityContribution;
        }
    }

    // The shipped feature tables (Bit = array index = the persisted token; APPEND-ONLY — never reorder/remove).
    // Office = Software Engineering Studio, Security = Cyber Security Firm, Game = Game Developer Studio.
    public static readonly Feature[] Office =
    {
        new Feature(0, "siliconalley:feature_office_cloudsync",    "siliconalley:feature_office_cloudsync",    0.12f, 0.05f),
        new Feature(1, "siliconalley:feature_office_pluginapi",    "siliconalley:feature_office_pluginapi",    0.15f, 0.05f),
        new Feature(2, "siliconalley:feature_office_collab",       "siliconalley:feature_office_collab",       0.18f, 0.06f),
        new Feature(3, "siliconalley:feature_office_automation",   "siliconalley:feature_office_automation",   0.10f, 0.04f),
        new Feature(4, "siliconalley:feature_office_enterprise",   "siliconalley:feature_office_enterprise",   0.20f, 0.07f),
    };

    public static readonly Feature[] Security =
    {
        new Feature(0, "siliconalley:feature_security_threatfeed", "siliconalley:feature_security_threatfeed", 0.12f, 0.05f),
        new Feature(1, "siliconalley:feature_security_compliance", "siliconalley:feature_security_compliance", 0.10f, 0.04f),
        new Feature(2, "siliconalley:feature_security_pentest",    "siliconalley:feature_security_pentest",    0.18f, 0.06f),
        new Feature(3, "siliconalley:feature_security_zerotrust",  "siliconalley:feature_security_zerotrust",  0.20f, 0.07f),
        new Feature(4, "siliconalley:feature_security_incident",   "siliconalley:feature_security_incident",   0.15f, 0.05f),
    };

    public static readonly Feature[] Game =
    {
        new Feature(0, "siliconalley:feature_game_graphics",       "siliconalley:feature_game_graphics",       0.20f, 0.07f),
        new Feature(1, "siliconalley:feature_game_physics",        "siliconalley:feature_game_physics",        0.15f, 0.05f),
        new Feature(2, "siliconalley:feature_game_multiplayer",    "siliconalley:feature_game_multiplayer",    0.25f, 0.08f),
        new Feature(3, "siliconalley:feature_game_procedural",     "siliconalley:feature_game_procedural",     0.15f, 0.06f),
        new Feature(4, "siliconalley:feature_game_modsupport",     "siliconalley:feature_game_modsupport",     0.10f, 0.04f),
    };

    // The feature list a Silicon Alley business can choose from. Unknown/other types have no features (the
    // wizard simply skips the Features page for them), so the math below is a no-op for them.
    public static Feature[] FeaturesFor(string businessTypeName)
    {
        switch (businessTypeName)
        {
            case "siliconalley:businesstype_softwarestudio": return Office;
            case "siliconalley:businesstype_cybersecurity":  return Security;
            case "siliconalley:businesstype_gamestudio":     return Game;
            default: return Array.Empty<Feature>();
        }
    }

    // The largest table — the UI builds this many reusable toggle slots and shows only the ones a given
    // business type actually has. Appending a feature to any table grows this automatically.
    public static int MaxCount => Mathf.Max(Office.Length, Mathf.Max(Security.Length, Game.Length));

    // ---- pure math shared by the wizard readout and the simulator ----
    // Project-size multiplier from the selected features: 1 + Σ SizeCost over set bits. Mask 0 ⇒ 1.0 (neutral).
    public static float SizeMultiplier(int mask, string businessTypeName)
    {
        if (mask == 0)
            return 1f;
        var sum = 0f;
        foreach (var f in FeaturesFor(businessTypeName))
            if ((mask & (1 << f.Bit)) != 0)
                sum += f.SizeCost;
        return 1f + sum;
    }

    // Quality-ceiling bonus from the selected features: Σ QualityContribution over set bits. Mask 0 ⇒ 0.
    public static float QualityBonus(int mask, string businessTypeName)
    {
        if (mask == 0)
            return 0f;
        var sum = 0f;
        foreach (var f in FeaturesFor(businessTypeName))
            if ((mask & (1 << f.Bit)) != 0)
                sum += f.QualityContribution;
        return sum;
    }
}
