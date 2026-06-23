#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModAPI;
using TMPro;
using UnityEngine;

// Issue #54 (epic #53 — UI overhaul, the foundation that blocks the rest): the shared theme for every
// Silicon Alley screen. Holds the refined Software-Inc-flavoured dark palette, a named typography scale,
// the resolved game font, and the bundled 9-slice sprite kit (panel/card/button). Loaded once from the
// mod's AssetBundle in SiliconAlleyMod.OnLoadAsync (init load), so the kit is cached before the project
// screen builds on city load. The styled-component layer (SiliconAlleyUI) reads everything from here.
//
// Every sprite is OPTIONAL: if a save's bundle predates the kit (or a load fails) the sprite stays null
// and the helpers fall back to the old flat-colour boxes — a missing sprite never breaks a save or a
// screen. Presentation only: no modData, no enums, no save-compat surface.
public static class SiliconAlleyTheme
{
    // ---- Palette. White 9-slice sprites are tinted with these; the values refine the original flat
    // navy constants into a cohesive surface/accent/text set the later screens share. ----
    public static readonly Color Surface   = new Color(0.086f, 0.098f, 0.125f, 0.98f); // window / panel background
    public static readonly Color Card      = new Color(0.12f,  0.14f,  0.18f,  1f);     // raised card surface
    public static readonly Color Elevated  = new Color(0.16f,  0.18f,  0.23f,  1f);     // hovered / elevated card
    public static readonly Color Accent    = new Color(0.20f,  0.50f,  0.86f,  1f);     // primary / selected (game blue)
    public static readonly Color Slate     = new Color(0.18f,  0.21f,  0.27f,  1f);     // default button
    public static readonly Color Warn      = new Color(0.80f,  0.55f,  0.20f,  1f);     // amber — licensed/royalty (#36)
    public static readonly Color Text      = new Color(0.90f,  0.92f,  0.96f,  1f);     // body text
    public static readonly Color TextMuted = new Color(0.66f,  0.70f,  0.78f,  1f);     // secondary text
    public static readonly Color Header    = new Color(0.52f,  0.72f,  1f,     1f);     // section-header accent
    public static readonly Color Divider   = new Color(1f,     1f,     1f,     0.08f);  // thin separator line

    // ---- Typography scale. Named sizes replace the scattered magic numbers (22/17/16/15/14). ----
    public static class Sizes
    {
        public const int Title    = 22;
        public const int Subtitle = 17;
        public const int Header   = 16;
        public const int Body     = 15;
        public const int Caption  = 14;
        public const int Button   = 16;
    }

    // ---- 9-slice sprite kit (bundled). Asset paths match the files the generator writes + the packager
    // sweeps into siliconalley.unity3d. Null until Load runs (or if absent ⇒ flat-colour fallback). ----
    public const string PanelSpritePath  = "Assets/Mods/SiliconAlley/UI/panel.png";
    public const string CardSpritePath   = "Assets/Mods/SiliconAlley/UI/card.png";
    public const string ButtonSpritePath = "Assets/Mods/SiliconAlley/UI/button.png";

    public static Sprite? PanelSprite  { get; private set; }
    public static Sprite? CardSprite   { get; private set; }
    public static Sprite? ButtonSprite { get; private set; }

    public static bool SpritesReady => PanelSprite != null && CardSprite != null && ButtonSprite != null;

    // ---- Icon set (issue #55). Every concept (feature/tool/platform/segment/phase/type/scope) carries a
    // stable NameKey; the icon for it is the bundled PNG whose file stem = the NameKey minus "siliconalley:"
    // (e.g. feature_office_cloudsync.png). Loaded from Assets/Mods/SiliconAlley/UI/Icons/ into this map keyed
    // by lowercased file stem. Resolution is two-tier (see IconFor): exact concept icon → per-category
    // placeholder (cat_<category>) → null (graceful, no broken sprite). ----
    public static Dictionary<string, Sprite>? Icons { get; private set; }
    public static bool IconsReady => Icons != null && Icons.Count > 0;

    // The game's TMP font (Exo2), resolved lazily and cached so text matches the game's typography.
    private static TMP_FontAsset? _font;
    private static bool _fontResolved;
    public static TMP_FontAsset? Font
    {
        get
        {
            if (!_fontResolved)
            {
                _font = ResolveFont();
                _fontResolved = true;
            }
            return _font;
        }
    }

    // Load the bundled sprite kit. Called from SiliconAlleyMod with the bundle it already opened; safe to
    // pass a null bundle/logger. Tolerant of missing sprites — partial kits still light up what loaded.
    public static void Load(AssetBundle? bundle, IModLogger? logger)
    {
        if (bundle == null)
        {
            logger?.Warn("SiliconAlley: UI theme — no asset bundle; using flat-colour fallback.");
            return;
        }

        PanelSprite  = bundle.LoadAsset<Sprite>(PanelSpritePath);
        CardSprite   = bundle.LoadAsset<Sprite>(CardSpritePath);
        ButtonSprite = bundle.LoadAsset<Sprite>(ButtonSpritePath);

        if (SpritesReady)
            logger?.Info("SiliconAlley: UI theme sprite kit loaded (panel/card/button).");
        else
            logger?.Warn("SiliconAlley: UI theme sprite kit missing/partial; flat-colour fallback for absent sprites.");

        Icons = LoadIcons(bundle, logger);
    }

    // Load every sprite under …/UI/Icons/ into a name→sprite map (key = lowercased file stem). Adding an icon
    // is drop-in (no code change). Tolerant of a missing folder (older bundle) ⇒ empty map ⇒ graceful fallback.
    private static Dictionary<string, Sprite> LoadIcons(AssetBundle bundle, IModLogger? logger)
    {
        var icons = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in bundle.GetAllAssetNames()) // bundle paths are lowercased
        {
            if (name.IndexOf("/ui/icons/", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                continue;
            var sprite = bundle.LoadAsset<Sprite>(name);
            if (sprite == null)
                continue;
            icons[Path.GetFileNameWithoutExtension(name)] = sprite;
        }
        if (icons.Count > 0)
            logger?.Info($"SiliconAlley: UI icon set loaded ({icons.Count} icon(s)).");
        else
            logger?.Warn("SiliconAlley: no UI icons in bundle; concept icons will be absent (text-only fallback).");
        return icons;
    }

    // Resolve the icon for a concept, given its NameKey (e.g. "siliconalley:feature_office_cloudsync") or a
    // bare key. Two-tier + graceful: exact concept icon → per-category placeholder (cat_<category>) → null.
    public static Sprite? IconFor(string? nameKeyOrKey)
    {
        if (Icons == null || string.IsNullOrEmpty(nameKeyOrKey))
            return null;
        var key = IconKey(nameKeyOrKey!);
        if (Icons.TryGetValue(key, out var exact))
            return exact;
        var category = CategoryOf(key);
        if (category.Length > 0 && Icons.TryGetValue("cat_" + category, out var placeholder))
            return placeholder;
        return null;
    }

    // The icon-file stem for a concept key: drop the "siliconalley:" prefix and lowercase.
    private static string IconKey(string nameKey)
    {
        const string prefix = "siliconalley:";
        var k = nameKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? nameKey.Substring(prefix.Length) : nameKey;
        return k.ToLowerInvariant();
    }

    // The concept category = the text before the first underscore (feature/tool/platform/segment/phase/…).
    private static string CategoryOf(string key)
    {
        var idx = key.IndexOf('_');
        return idx > 0 ? key.Substring(0, idx) : key;
    }

    // Resolve the game's TMP font (Exo2). Falls back to any loaded TMP font asset (preferring the "Exo"
    // family) if no project default is set. (Moved verbatim from SiliconAlleyProjectScreen.)
    private static TMP_FontAsset? ResolveFont()
    {
        if (TMP_Settings.defaultFontAsset != null)
            return TMP_Settings.defaultFontAsset;
        TMP_FontAsset? first = null;
        foreach (var fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (fa == null)
                continue;
            first ??= fa;
            if (fa.name.IndexOf("Exo", StringComparison.OrdinalIgnoreCase) >= 0)
                return fa;
        }
        return first;
    }
}
