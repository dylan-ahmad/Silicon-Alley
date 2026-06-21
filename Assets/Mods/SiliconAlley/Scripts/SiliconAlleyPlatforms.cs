using System;
using UnityEngine;

// Issue #37 (Operating systems / platforms): the mod-defined platforms each business type can target in the
// Design phase, plus the reach↔scope math the wizard + simulator share. Targeting more platforms WIDENS the
// reachable market at launch (a bigger installed-base jump) but ADDS PORTING WORK (raises EffectiveProjectSize,
// so a wider release takes longer) — Software Inc.'s "supported OS" checklist. The picker is the Operating
// Systems page of the design wizard (#34/#35); selection persists in BusinessState.PlatformMask (the bit slot
// reserved by #40), the sibling of FeatureMask (#26).
//
// SAVE-COMPAT: the per-type tables are APPEND-ONLY. A platform's Bit is its persisted token (set in the
// PlatformMask bitmask), so a Bit, once shipped, must NEVER be renamed/reordered/removed — only append new
// platforms (and record them in the SHIPPED_ENUMS ledger in CLAUDE.md). PlatformMask 0 (old saves / no
// selection) ⇒ the single "home" platform: ReachMultiplier 1.0 and SizeMultiplier 1.0 ⇒ behaviour identical
// to before this issue (never 0 reach). ShareWeight/ScopeCost are tunable data, NOT persisted. Display names
// live in Locales/en.json and may change freely.
public static class SiliconAlleyPlatforms
{
    public readonly struct Platform
    {
        public readonly int Bit;          // persisted bit position in PlatformMask — APPEND-ONLY
        public readonly string Id;        // stable identifier (documentation/debug; not localized)
        public readonly string NameKey;   // locale key for the display name
        public readonly float ShareWeight;// market-share weight => launch reach when selected
        public readonly float ScopeCost;  // +fraction of project size/dev-time (porting work) when selected

        public Platform(int bit, string id, string nameKey, float shareWeight, float scopeCost)
        {
            Bit = bit; Id = id; NameKey = nameKey; ShareWeight = shareWeight; ScopeCost = scopeCost;
        }
    }

    // The shipped platform tables (Bit = array index = the persisted token; APPEND-ONLY — never reorder/remove).
    // The primary platform of each type carries ShareWeight ~1.0 so a single-platform release reproduces today's
    // reach; secondary platforms add reach (and porting cost) on top.
    public static readonly Platform[] Office =
    {
        new Platform(0, "siliconalley:platform_office_desktop", "siliconalley:platform_office_desktop", 1.0f, 0.15f),
        new Platform(1, "siliconalley:platform_office_web",     "siliconalley:platform_office_web",     0.6f, 0.15f),
        new Platform(2, "siliconalley:platform_office_mobile",  "siliconalley:platform_office_mobile",  0.5f, 0.20f),
        new Platform(3, "siliconalley:platform_office_cloud",   "siliconalley:platform_office_cloud",   0.7f, 0.18f),
    };

    public static readonly Platform[] Security =
    {
        new Platform(0, "siliconalley:platform_security_desktop", "siliconalley:platform_security_desktop", 1.0f, 0.15f),
        new Platform(1, "siliconalley:platform_security_server",  "siliconalley:platform_security_server",  0.8f, 0.18f),
        new Platform(2, "siliconalley:platform_security_cloud",   "siliconalley:platform_security_cloud",   0.7f, 0.18f),
        new Platform(3, "siliconalley:platform_security_mobile",  "siliconalley:platform_security_mobile",  0.4f, 0.20f),
    };

    public static readonly Platform[] Game =
    {
        new Platform(0, "siliconalley:platform_game_pc",      "siliconalley:platform_game_pc",      1.0f, 0.15f),
        new Platform(1, "siliconalley:platform_game_console", "siliconalley:platform_game_console", 0.8f, 0.25f),
        new Platform(2, "siliconalley:platform_game_mobile",  "siliconalley:platform_game_mobile",  0.6f, 0.20f),
        new Platform(3, "siliconalley:platform_game_web",     "siliconalley:platform_game_web",     0.3f, 0.12f),
    };

    // The platform list a Silicon Alley business can target. Unknown/other types have none (the wizard skips
    // the page), so the math below is a no-op for them.
    public static Platform[] PlatformsFor(string businessTypeName)
    {
        switch (businessTypeName)
        {
            case "siliconalley:businesstype_softwarestudio": return Office;
            case "siliconalley:businesstype_cybersecurity":  return Security;
            case "siliconalley:businesstype_gamestudio":     return Game;
            default: return Array.Empty<Platform>();
        }
    }

    // The largest table — the UI builds this many reusable toggle slots and shows only the ones a given
    // business type actually has. Appending a platform to any table grows this automatically.
    public static int MaxCount => Mathf.Max(Office.Length, Mathf.Max(Security.Length, Game.Length));

    // ---- pure math shared by the wizard readout and the simulator ----
    // Project-size multiplier from the selected platforms: 1 + Σ ScopeCost over set bits. Mask 0 ⇒ 1.0 (the
    // single home platform, no porting). Composes with the #26 feature size multiplier.
    public static float SizeMultiplier(int mask, string businessTypeName)
    {
        if (mask == 0)
            return 1f;
        var sum = 0f;
        foreach (var p in PlatformsFor(businessTypeName))
            if ((mask & (1 << p.Bit)) != 0)
                sum += p.ScopeCost;
        return 1f + sum;
    }

    // Launch reach multiplier from the selected platforms: Σ ShareWeight over set bits. Mask 0 ⇒ 1.0 (the
    // single home platform — never 0 reach). Layered on the launch installed-base units; does NOT touch payout.
    public static float ReachMultiplier(int mask, string businessTypeName)
    {
        if (mask == 0)
            return 1f;
        var sum = 0f;
        foreach (var p in PlatformsFor(businessTypeName))
            if ((mask & (1 << p.Bit)) != 0)
                sum += p.ShareWeight;
        return sum;
    }
}
