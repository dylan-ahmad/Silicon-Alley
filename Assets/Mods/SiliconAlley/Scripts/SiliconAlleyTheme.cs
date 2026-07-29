#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModAPI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    public static readonly Color Warn      = new Color(0.80f,  0.55f,  0.20f,  1f);     // amber — CAUTION ONLY (licensed/royalty/gap/time-pressure); destructive states use Danger
    public static readonly Color Ok        = new Color(0.33f,  0.70f,  0.45f,  1f);     // green — owned / covered (#57)
    public static readonly Color Text      = new Color(0.90f,  0.92f,  0.96f,  1f);     // body text
    public static readonly Color TextMuted = new Color(0.66f,  0.70f,  0.78f,  1f);     // secondary text
    public static readonly Color Header    = new Color(0.52f,  0.72f,  1f,     1f);     // section-header accent
    public static readonly Color Divider   = new Color(1f,     1f,     1f,     0.08f);  // thin separator line
    public static readonly Color Danger    = new Color(0.85f,  0.30f,  0.28f,  1f);     // red — destructive / armed confirms (#143)
    public static readonly Color Info      = new Color(0.30f,  0.68f,  0.82f,  1f);     // cyan — neutral informational (reserved for #146)
    public static readonly Color Focus     = new Color(0.52f,  0.72f,  1f,     0.85f);  // focus ring (reserved for #146)
    public static readonly Color Scrim     = new Color(0f,     0f,     0f,     0.55f);  // modal backdrop dim
    public static readonly Color Shadow    = new Color(0f,     0f,     0f,     1f);     // drop-shadow base; alpha set per Elevation level

    // ---- Named state blends. One home for the tint math the screens used to duplicate inline. ----
    public static readonly Color CardSelected = Color.Lerp(Card, Accent, 0.30f); // selected picker card
    public static readonly Color CardLicensed = Color.Lerp(Card, Warn, 0.30f);   // licensed/royalty card
    public static readonly Color StepDone     = Color.Lerp(Slate, Accent, 0.45f); // completed wizard step dot

    // Selectable tint set shared by buttons and clickable cards. normalColor stays white — LOAD-BEARING:
    // the tints multiply the graphic's own colour, so white lets each control's image.color show through
    // (scope/overtime/hold buttons recolour their image directly).
    public static readonly ColorBlock Interaction = new ColorBlock
    {
        normalColor      = Color.white,
        highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f),
        pressedColor     = new Color(0.70f, 0.70f, 0.70f, 1f),
        selectedColor    = Color.white,
        disabledColor    = new Color(0.55f, 0.55f, 0.55f, 0.70f),
        colorMultiplier  = 1f,
        fadeDuration     = 0.08f,
    };

    // ---- Typography scale. Named sizes replace the scattered magic numbers (22/17/16/15/14/13).
    // Roles may share a value (Header/Button are both 16) — a later restyle can split them without
    // touching call sites. ----
    public static class Sizes
    {
        public const int Title    = 22;
        public const int Subtitle = 17;
        public const int Header   = 16;
        public const int Body     = 15;
        public const int Caption  = 14;
        public const int Button   = 16;
        public const int Status   = 13; // muted status lines (idle/dev/ship/update/contract readouts)
    }

    // ---- Spacing scale (#143). Pads and gaps in px; matches the values the screens actually use. ----
    public static class Space
    {
        public const int Hairline = 2;  // divider thickness, chip vertical pad
        public const int Tight    = 4;  // dense stacks (card-item body)
        public const int Small    = 6;  // chip/dot gaps
        public const int Base     = 8;  // default row/section spacing, chip horizontal pad
        public const int Medium   = 10; // card inner pad/spacing
        public const int Large    = 12; // card-panel pad
        public const int Gutter   = 14; // wizard column gutters
        public const int WindowX  = 26; // window-root horizontal pad
        public const int WindowY  = 22; // window-root vertical pad
    }

    // ---- Control-height scale (#143). ----
    public static class Height
    {
        public const float Bar     = 10f; // progress bar default
        public const float Dot     = 12f; // wizard step dot (the active one widens to 18)
        public const float Chip    = 20f; // chip/badge pill (16 label + 2+2 pad)
        public const float Slider  = 24f;
        public const float Row     = 36f; // MakeRow default
        public const float Control = 38f; // button / input field
        public const float Card    = 52f; // CardItem min height
    }

    // ---- Corner-radius scale (#143). KEEP IN SYNC with SiliconAlleyUISpriteGenerator (the Editor
    // assembly cannot reference this one — asmdef autoReferenced:false — so the generator mirrors
    // these values as its own literals). ----
    public static class Radius
    {
        public const int Control    = 10; // button.png / outline.png
        public const int Card       = 12; // card.png / shadow.png body
        public const int Panel      = 16; // panel.png
        public const int PillBorder = 23; // pill.png 9-slice border; ApplyPill scales it to any height
    }

    // ---- Elevation presets (#143) for the shared shadow ring sprite (shadow.png). ShadowPad MUST
    // equal the falloff pad baked into the sprite (see the generator): the shadow Image is expanded by
    // exactly this many px so the ring's transparent interior aligns with its host's edge — a different
    // expansion would slide the ring onto (or off) the card face. Levels therefore differ by ALPHA only. ----
    public static class Elevation
    {
        public const float ShadowPad  = 24f; // = generator's ShadowPad; keep in sync
        public const float CardAlpha  = 0.35f;
        public const float PanelAlpha = 0.45f;
    }

    // ---- 9-slice sprite kit (bundled). Asset paths match the files the generator writes + the packager
    // sweeps into siliconalley.unity3d. Null until Load runs (or if absent ⇒ flat-colour fallback). ----
    public const string PanelSpritePath   = "Assets/Mods/SiliconAlley/UI/panel.png";
    public const string CardSpritePath    = "Assets/Mods/SiliconAlley/UI/card.png";
    public const string ButtonSpritePath  = "Assets/Mods/SiliconAlley/UI/button.png";
    public const string PillSpritePath    = "Assets/Mods/SiliconAlley/UI/pill.png";
    public const string OutlineSpritePath = "Assets/Mods/SiliconAlley/UI/outline.png";
    public const string ShadowSpritePath  = "Assets/Mods/SiliconAlley/UI/shadow.png";

    public static Sprite? PanelSprite   { get; private set; }
    public static Sprite? CardSprite    { get; private set; }
    public static Sprite? ButtonSprite  { get; private set; }
    public static Sprite? PillSprite    { get; private set; }
    public static Sprite? OutlineSprite { get; private set; }
    public static Sprite? ShadowSprite  { get; private set; }

    // Deliberately only the original #54 trio: a pre-#143 bundle must not flip this false. The #143
    // additions (pill/outline/shadow) are each optional and null-guarded at their consumers.
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

        PanelSprite   = bundle.LoadAsset<Sprite>(PanelSpritePath);
        CardSprite    = bundle.LoadAsset<Sprite>(CardSpritePath);
        ButtonSprite  = bundle.LoadAsset<Sprite>(ButtonSpritePath);
        PillSprite    = bundle.LoadAsset<Sprite>(PillSpritePath);
        OutlineSprite = bundle.LoadAsset<Sprite>(OutlineSpritePath);
        ShadowSprite  = bundle.LoadAsset<Sprite>(ShadowSpritePath);

        if (SpritesReady)
            logger?.Info("SiliconAlley: UI theme sprite kit loaded (panel/card/button" +
                         (PillSprite != null && OutlineSprite != null && ShadowSprite != null
                             ? "/pill/outline/shadow)." : "; pre-0.6.0 bundle — no pill/outline/shadow)."));
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
