#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// Issue #54: procedural generator for the Silicon Alley UI 9-slice sprite kit. Editor-only and parked
// OUTSIDE the mod folder (under Assets/Editor) so the Mod Builder packager never sweeps it into the
// AssetBundle and it is not compiled into the runtime mod assembly. It renders white, anti-aliased,
// rounded-rectangle PNGs (tinted at runtime by SiliconAlleyTheme) and authors their importer settings —
// Sprite type, 9-slice border, AssetBundle assignment — entirely in code, so there is no hand-edited
// .meta. Re-run "Big Ambitions ▸ Silicon Alley ▸ Generate UI Sprites" after tweaking a radius/size to
// regenerate; the packager then bundles the PNGs into siliconalley.unity3d on the next build.
public static class SiliconAlleyUISpriteGenerator
{
    private const string OutputDir = "Assets/Mods/SiliconAlley/UI";
    private const string BundleName = "siliconalley";
    private const string BundleVariant = "unity3d";

    // Base sprite size (px). Small keeps the bundle tiny; the 9-slice scales the corner to any panel size.
    private const int Size = 48;

    [MenuItem("Big Ambitions/Silicon Alley/Generate UI Sprites")]
    public static void Generate()
    {
        Directory.CreateDirectory(OutputDir);
        // Different corner radius per shape: panel > card > button (Software-Inc-ish rounding).
        WriteSprite("panel.png", 16);
        WriteSprite("card.png", 12);
        WriteSprite("button.png", 10);
        AssetDatabase.Refresh();
        Debug.Log("[SiliconAlley] UI sprite kit generated in " + OutputDir + " (panel/card/button).");
    }

    private static void WriteSprite(string fileName, int radius)
    {
        var tex = RenderRoundedRect(Size, radius);
        var bytes = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);

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
        // Border = radius + 1 so the 9-slice keeps the corner arc fixed while the flat centre stretches.
        var b = radius + 1;
        importer.spriteBorder = new Vector4(b, b, b, b);
        importer.SetAssetBundleNameAndVariant(BundleName, BundleVariant);
        importer.SaveAndReimport();
    }

    // White rounded rectangle with an anti-aliased alpha edge (4×4 supersample per pixel). RGB stays
    // white so the runtime Image.color tint produces the themed surface/accent.
    private static Texture2D RenderRoundedRect(int size, float radius)
    {
        const int ss = 4;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var covered = 0;
                for (var sy = 0; sy < ss; sy++)
                    for (var sx = 0; sx < ss; sx++)
                    {
                        var px = x + (sx + 0.5f) / ss;
                        var py = y + (sy + 0.5f) / ss;
                        if (InsideRoundedRect(px, py, size, radius))
                            covered++;
                    }
                var a = (byte)(255 * covered / (ss * ss));
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    // Rounded-rectangle coverage test: distance outside the inset box, capped against the corner radius.
    private static bool InsideRoundedRect(float px, float py, int size, float radius)
    {
        var half = size / 2f;
        var inner = half - radius;
        var qx = Mathf.Max(Mathf.Abs(px - half) - inner, 0f);
        var qy = Mathf.Max(Mathf.Abs(py - half) - inner, 0f);
        return (qx * qx + qy * qy) <= radius * radius;
    }
}
#endif
