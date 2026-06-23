#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// Issue #55: procedural generator for the Silicon Alley per-CATEGORY placeholder icons (cat_feature,
// cat_tool, …). Editor-only and parked OUTSIDE the mod folder so the Mod Builder packager never sweeps it
// into the AssetBundle and it is not compiled into the runtime mod assembly. It renders distinct,
// anti-aliased, white-on-transparent geometric glyphs so the icon system is verifiable in-engine before
// real per-concept art is dropped in — the resolver (SiliconAlleyTheme.IconFor) falls back to these per
// category. Real icons named "<NameKey-without-prefix>.png" (e.g. feature_office_cloudsync.png) override
// them with no code change. Importer is authored in code as a Simple sprite (NO 9-slice border) and
// assigned to siliconalley.unity3d. Menu: Big Ambitions ▸ Silicon Alley ▸ Generate Placeholder Icons.
public static class SiliconAlleyIconPlaceholderGenerator
{
    private const string OutputDir = "Assets/Mods/SiliconAlley/UI/Icons";
    private const string BundleName = "siliconalley";
    private const string BundleVariant = "unity3d";
    private const int Size = 128;

    private enum Shape { Circle, RoundedSquare, Ring, Diamond, Triangle, Plus, Bars }

    // One distinct shape per concept category so placeholders are visually separable.
    private static readonly (string name, Shape shape)[] Categories =
    {
        ("cat_feature", Shape.Circle),
        ("cat_tool", Shape.RoundedSquare),
        ("cat_platform", Shape.Ring),
        ("cat_segment", Shape.Diamond),
        ("cat_phase", Shape.Triangle),
        ("cat_businesstype", Shape.Plus),
        ("cat_projecttype", Shape.Bars),
    };

    [MenuItem("Big Ambitions/Silicon Alley/Generate Placeholder Icons")]
    public static void Generate()
    {
        Directory.CreateDirectory(OutputDir);
        foreach (var (name, shape) in Categories)
            WriteIcon(name, shape);
        AssetDatabase.Refresh();
        Debug.Log($"[SiliconAlley] {Categories.Length} placeholder category icons generated in {OutputDir}.");
    }

    private static void WriteIcon(string name, Shape shape)
    {
        var tex = Render(Size, shape);
        var bytes = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);

        var path = OutputDir + "/" + name + ".png";
        File.WriteAllBytes(path, bytes);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = Vector4.zero; // Simple, not 9-sliced
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.alphaIsTransparency = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.spritePixelsPerUnit = 100f;
        importer.SetAssetBundleNameAndVariant(BundleName, BundleVariant);
        importer.SaveAndReimport();
    }

    // White glyph with an anti-aliased alpha edge (3×3 supersample per pixel).
    private static Texture2D Render(int size, Shape shape)
    {
        const int ss = 3;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var covered = 0;
                for (var sy = 0; sy < ss; sy++)
                    for (var sx = 0; sx < ss; sx++)
                        if (Inside(x + (sx + 0.5f) / ss, y + (sy + 0.5f) / ss, size, shape))
                            covered++;
                var a = (byte)(255 * covered / (ss * ss));
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    private static bool Inside(float x, float y, int size, Shape shape)
    {
        var c = size / 2f;
        var r = size * 0.40f; // glyph radius (~20% padding to the cell edge)
        var dx = x - c;
        var dy = y - c;
        switch (shape)
        {
            case Shape.Circle:
                return dx * dx + dy * dy <= r * r;
            case Shape.RoundedSquare:
            {
                var a = r * 0.92f;
                var rad = r * 0.30f;
                var qx = Mathf.Max(Mathf.Abs(dx) - (a - rad), 0f);
                var qy = Mathf.Max(Mathf.Abs(dy) - (a - rad), 0f);
                return qx * qx + qy * qy <= rad * rad;
            }
            case Shape.Ring:
            {
                var d2 = dx * dx + dy * dy;
                var inner = r * 0.58f;
                return d2 <= r * r && d2 >= inner * inner;
            }
            case Shape.Diamond:
                return Mathf.Abs(dx) + Mathf.Abs(dy) <= r;
            case Shape.Triangle:
            {
                var h = r * 1.6f;
                var top = c - h / 2f;
                if (y < top || y > top + h) return false;
                var t = (y - top) / h;           // 0 at apex, 1 at base
                return Mathf.Abs(dx) <= 0.75f * r * t;
            }
            case Shape.Plus:
            {
                var arm = r;
                var thick = r * 0.34f;
                return (Mathf.Abs(dx) <= thick && Mathf.Abs(dy) <= arm)
                    || (Mathf.Abs(dy) <= thick && Mathf.Abs(dx) <= arm);
            }
            case Shape.Bars:
            {
                var barW = r * 0.34f;
                var gap = r * 0.12f;
                var totalW = 3 * barW + 2 * gap;
                var startX = c - totalW / 2f;
                for (var i = 0; i < 3; i++)
                {
                    var bx0 = startX + i * (barW + gap);
                    if (x >= bx0 && x <= bx0 + barW)
                    {
                        var barH = r * (0.8f + 0.4f * i);
                        var baseY = c + r;
                        return y <= baseY && y >= baseY - barH;
                    }
                }
                return false;
            }
            default:
                return false;
        }
    }
}
#endif
