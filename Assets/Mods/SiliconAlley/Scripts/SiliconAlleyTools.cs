using System;
using UnityEngine;

// Issue #36 (Editors & tools / software dependencies): the mod-defined tool catalog each business type builds
// on, plus the build-vs-license math the wizard + simulator share. In Software Inc. a product depends on other
// software (engines / frameworks / editors). For each tool the studio either OWNS a self-built version (a
// one-off R&D cash investment that becomes a reusable studio asset, free thereafter) or LICENSES a competitor's
// (instant, but an ONGOING ROYALTY cut on launch revenue + support income while the license is held). Tools
// grant a quality bonus (raise the design ceiling, like features). The picker is the Editors & tools page of
// the design wizard (#34/#35); selection persists in the BusinessState ownedToolsMask (studio-level) +
// usedToolsMask (per-project) slots reserved by #40.
//
// SAVE-COMPAT: the per-type tables are APPEND-ONLY. A tool's Bit is its persisted token (set in the two tool
// masks), so a Bit, once shipped, must NEVER be renamed/reordered/removed — only append new tools (and record
// them in the SHIPPED_ENUMS ledger in CLAUDE.md). The royalty rate, build cost, quality bonus and licensor are
// CATALOG data (tunable, NOT persisted) — derived from which tools are owned/used, so no extra save field is
// needed beyond the two reserved masks. Both masks 0 (old saves / no tools) ⇒ no quality bonus and no royalty ⇒
// behaviour identical to before this issue. Display names live in Locales/en.json and may change freely.
public static class SiliconAlleyTools
{
    public const float MaxRoyalty = 0.9f; // licensed royalties never take more than this share of revenue

    public readonly struct Tool
    {
        public readonly int Bit;             // persisted bit position in the tool masks — APPEND-ONLY
        public readonly string Id;           // stable identifier (documentation/debug; not localized)
        public readonly string NameKey;      // locale key for the tool's display name
        public readonly string LicensorNameKey; // locale key for the competitor you license it from (flavor)
        public readonly float QualityBonus;  // +fraction added to the quality ceiling when the tool is used
        public readonly float BuildCost;     // one-off R&D cash to own (build in-house) — then free forever
        public readonly float RoyaltyRate;   // recurring revenue cut while licensed (used but not owned)

        public Tool(int bit, string id, string nameKey, string licensorNameKey, float qualityBonus, float buildCost, float royaltyRate)
        {
            Bit = bit; Id = id; NameKey = nameKey; LicensorNameKey = licensorNameKey;
            QualityBonus = qualityBonus; BuildCost = buildCost; RoyaltyRate = royaltyRate;
        }
    }

    // The shipped tool tables (Bit = array index = the persisted token; APPEND-ONLY — never reorder/remove).
    public static readonly Tool[] Office =
    {
        new Tool(0, "siliconalley:tool_office_appframework", "siliconalley:tool_office_appframework", "siliconalley:toolvendor_frameworks", 0.05f, 6000f, 0.06f),
        new Tool(1, "siliconalley:tool_office_database",     "siliconalley:tool_office_database",     "siliconalley:toolvendor_datacore",  0.05f, 7000f, 0.07f),
        new Tool(2, "siliconalley:tool_office_uitoolkit",    "siliconalley:tool_office_uitoolkit",    "siliconalley:toolvendor_pixelworks",0.04f, 5000f, 0.05f),
    };

    public static readonly Tool[] Security =
    {
        new Tool(0, "siliconalley:tool_security_scanengine", "siliconalley:tool_security_scanengine", "siliconalley:toolvendor_sentinellabs", 0.05f, 6500f, 0.06f),
        new Tool(1, "siliconalley:tool_security_cryptolib",  "siliconalley:tool_security_cryptolib",  "siliconalley:toolvendor_cipherco",     0.06f, 8000f, 0.07f),
        new Tool(2, "siliconalley:tool_security_siem",       "siliconalley:tool_security_siem",       "siliconalley:toolvendor_logguard",     0.05f, 7000f, 0.06f),
    };

    public static readonly Tool[] Game =
    {
        new Tool(0, "siliconalley:tool_game_engine",     "siliconalley:tool_game_engine",     "siliconalley:toolvendor_gameforge",  0.07f, 9000f, 0.08f),
        new Tool(1, "siliconalley:tool_game_artsuite",   "siliconalley:tool_game_artsuite",   "siliconalley:toolvendor_pixelforge", 0.05f, 6000f, 0.06f),
        new Tool(2, "siliconalley:tool_game_audio",      "siliconalley:tool_game_audio",      "siliconalley:toolvendor_soundstack", 0.04f, 5000f, 0.05f),
    };

    // The tool catalog a Silicon Alley business builds on. Unknown/other types have none (the wizard skips the
    // page), so the math below is a no-op for them.
    public static Tool[] ToolsFor(string businessTypeName)
    {
        switch (businessTypeName)
        {
            case "siliconalley:businesstype_softwarestudio": return Office;
            case "siliconalley:businesstype_cybersecurity":  return Security;
            case "siliconalley:businesstype_gamestudio":     return Game;
            default: return Array.Empty<Tool>();
        }
    }

    // The largest table — the UI builds this many reusable cycle-button slots and shows only the ones a given
    // business type actually has. Appending a tool to any table grows this automatically.
    public static int MaxCount => Mathf.Max(Office.Length, Mathf.Max(Security.Length, Game.Length));

    // ---- pure math shared by the wizard readout and the simulator ----
    // Total quality-ceiling bonus from the tools USED on the current project (owned or licensed both help):
    // Σ QualityBonus over set bits of usedMask. usedMask 0 ⇒ 0.
    public static float QualityBonus(int usedMask, string businessTypeName)
    {
        if (usedMask == 0)
            return 0f;
        var sum = 0f;
        foreach (var t in ToolsFor(businessTypeName))
            if ((usedMask & (1 << t.Bit)) != 0)
                sum += t.QualityBonus;
        return sum;
    }

    // Total recurring royalty fraction from the LICENSED tools (used but not owned): Σ RoyaltyRate over bits set
    // in usedMask and clear in ownedMask, clamped to [0, MaxRoyalty]. Owned tools cost nothing. Both masks 0 ⇒ 0.
    public static float Royalty(int usedMask, int ownedMask, string businessTypeName)
    {
        var licensed = usedMask & ~ownedMask;
        if (licensed == 0)
            return 0f;
        var sum = 0f;
        foreach (var t in ToolsFor(businessTypeName))
            if ((licensed & (1 << t.Bit)) != 0)
                sum += t.RoyaltyRate;
        return Mathf.Clamp(sum, 0f, MaxRoyalty);
    }
}
