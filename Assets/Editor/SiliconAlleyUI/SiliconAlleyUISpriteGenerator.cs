#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Issue #54 (+ #143): procedural generator for the Silicon Alley UI 9-slice sprite kit. Editor-only and
// parked OUTSIDE the mod folder (under Assets/Editor) so the Mod Builder packager never sweeps it into
// the AssetBundle and it is not compiled into the runtime mod assembly. It renders white, anti-aliased
// PNGs (tinted at runtime by SiliconAlleyTheme) and authors their importer settings — Sprite type,
// 9-slice border, AssetBundle assignment — entirely in code, so there is no hand-edited .meta. Re-run
// "Big Ambitions ▸ Silicon Alley ▸ Generate UI Sprites" after tweaking a radius/size to regenerate; the
// packager then bundles the PNGs into siliconalley.unity3d on the next build.
//
// Radii/pads here MIRROR SiliconAlleyTheme.Radius / Elevation.ShadowPad by hand: the runtime asmdef is
// not auto-referenced (autoReferenced:false + BA_GAME_DLLS_IMPORTED), so this Editor assembly cannot
// reference it. Keep both sides in sync when tuning.
public static class SiliconAlleyUISpriteGenerator
{
    private const string OutputDir = "Assets/Mods/SiliconAlley/UI";
    private const string BundleName = "siliconalley";
    private const string BundleVariant = "unity3d";

    // Base sprite size (px). Small keeps the bundle tiny; the 9-slice scales the corner to any panel size.
    private const int Size = 48;
    // shadow.png: falloff pad around the 48×48 body. SiliconAlleyUI.AddShadow expands the shadow Image by
    // exactly this many px so the ring's transparent interior aligns with the host edge (= Theme.Elevation.ShadowPad).
    private const int ShadowPad = 24;

    [MenuItem("Big Ambitions/Silicon Alley/Generate UI Sprites")]
    public static void Generate()
    {
        Directory.CreateDirectory(OutputDir);
        // Different corner radius per shape: panel > card > button (Software-Inc-ish rounding); the
        // values mirror SiliconAlleyTheme.Radius (Panel/Card/Control).
        WriteFill("panel.png", 16);
        WriteFill("card.png", 12);
        WriteFill("button.png", 10);
        // #143: a true capsule for chips/dots/bars (sliced via SiliconAlleyUI.ApplyPill). Radius 22, not
        // 24: a zero-px stretch centre slices badly, so leave 2px flat. Border 23 = Theme.Radius.PillBorder.
        WriteFill("pill.png", 22);
        // #143: 2px inner stroke on the button radius — hairline emphasis/focus borders.
        WriteStroke("outline.png", 10, 2f);
        // #143: soft drop-shadow ring around a card-radius body. The interior is fully transparent: the
        // white sprite is tinted black at runtime and drawn as a CHILD of the card image (children render
        // on top of their parent), so only the outside-the-body ring may carry alpha.
        WriteShadow("shadow.png", 12);
        AssetDatabase.Refresh();
        Debug.Log("[SiliconAlley] UI sprite kit generated in " + OutputDir + " (panel/card/button/pill/outline/shadow).");
    }

    // ---- Shape writers, all through the same supersampled renderer + importer authoring. ----

    private static void WriteFill(string fileName, int radius)
    {
        var half = Size / 2f;
        // Border = radius + 1 so the 9-slice keeps the corner arc fixed while the flat centre stretches.
        WriteSprite(fileName, Size, radius + 1,
            (px, py) => RoundedRectDistance(px, py, half, half, radius) <= 0f ? 1f : 0f);
    }

    private static void WriteStroke(string fileName, int radius, float thickness)
    {
        var half = Size / 2f;
        WriteSprite(fileName, Size, radius + 1, (px, py) =>
        {
            var d = RoundedRectDistance(px, py, half, half, radius);
            return d <= 0f && d >= -thickness ? 1f : 0f;
        });
    }

    private static void WriteShadow(string fileName, int radius)
    {
        var canvas = Size + 2 * ShadowPad;
        var centre = canvas / 2f;
        var half = Size / 2f;
        // Border = pad + radius + 1 keeps the whole padded corner (falloff ring + arc) fixed under the 9-slice.
        WriteSprite(fileName, canvas, ShadowPad + radius + 1, (px, py) =>
        {
            var d = RoundedRectDistance(px, py, centre, half, radius);
            if (d <= 0f)
                return 0f; // interior stays clear — the card face must not be tinted
            var t = 1f - Mathf.Clamp01(d / ShadowPad);
            return t * t; // squared falloff: dense at the edge, feathered tail
        });
    }

    private static void WriteSprite(string fileName, int canvas, int border, Func<float, float, float> alphaAt)
    {
        var tex = Render(canvas, alphaAt);
        var bytes = tex.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(tex);

        var path = OutputDir + "/" + fileName;
        File.WriteAllBytes(path, bytes);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.alphaIsTransparency = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.spritePixelsPerUnit = 100f;
        importer.spriteBorder = new Vector4(border, border, border, border);
        importer.SetAssetBundleNameAndVariant(BundleName, BundleVariant);
        importer.SaveAndReimport();
    }

    // White texture whose alpha is the 4×4-supersampled average of alphaAt (each sample 0..1). RGB stays
    // white so the runtime Image.color tint produces the themed surface/accent/shadow.
    private static Texture2D Render(int size, Func<float, float, float> alphaAt)
    {
        const int ss = 4;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var sum = 0f;
                for (var sy = 0; sy < ss; sy++)
                    for (var sx = 0; sx < ss; sx++)
                        sum += alphaAt(x + (sx + 0.5f) / ss, y + (sy + 0.5f) / ss);
                var a = (byte)Mathf.RoundToInt(255f * sum / (ss * ss));
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    // Signed distance (px) from (px,py) to the edge of a rounded rect of half-extent `half`, centred at
    // `centre` on both axes; negative inside. Generalises the old boolean coverage test so fill, stroke
    // and shadow all share one geometry.
    private static float RoundedRectDistance(float px, float py, float centre, float half, float radius)
    {
        var qx = Mathf.Abs(px - centre) - (half - radius);
        var qy = Mathf.Abs(py - centre) - (half - radius);
        var ox = Mathf.Max(qx, 0f);
        var oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }
}
#endif
