#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

// Epic #101 loose end: issue #102 committed a hand-authored Server-rack mesh (Models/ServerRack.obj) but left
// Server.prefab's MeshFilter pointing at Unity's built-in Cube primitive, so a placed Server rendered as a
// dark box instead of the modeled rack. This editor command repoints the prefab's MeshFilter at the real OBJ
// mesh (and resets the scale the cube needed) so the shipped Server looks like a server rack.
//
// Editor-only and parked OUTSIDE the mod folder (like the SiliconAlleyUI generators) so the Mod Builder
// packager never sweeps it into the AssetBundle and it is not compiled into the runtime mod assembly. It
// rewrites the committed Server.prefab in place — run it once, commit the prefab, then Build + Install.
// Re-runnable: if the OBJ ever re-imports with a different mesh sub-asset id, run it again.
// Menu: Big Ambitions ▸ Silicon Alley ▸ Wire Server Model.
public static class SiliconAlleyServerModel
{
    private const string ObjPath = "Assets/Mods/SiliconAlley/Models/ServerRack.obj";
    private const string PrefabPath = "Assets/Mods/SiliconAlley/Server.prefab";

    [MenuItem("Big Ambitions/Silicon Alley/Wire Server Model")]
    private static void WireServerModel()
    {
        // Force-import so the OBJ's mesh sub-asset exists even if it was committed un-imported
        // (its .meta shipped with an empty internalIDToNameTable).
        AssetDatabase.ImportAsset(ObjPath, ImportAssetOptions.ForceUpdate);
        var mesh = AssetDatabase.LoadAllAssetsAtPath(ObjPath).OfType<Mesh>().FirstOrDefault();
        if (mesh == null)
        {
            Debug.LogError($"[SiliconAlley] No mesh found in {ObjPath} — cannot wire the Server model.");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            // There is exactly one MeshFilter in the prefab (the ServerRackVisual child); find it by
            // component rather than by name so a rename doesn't break the wiring.
            var meshFilter = root.GetComponentInChildren<MeshFilter>(true);
            if (meshFilter == null)
            {
                Debug.LogError($"[SiliconAlley] {PrefabPath} has no MeshFilter — nothing to wire.");
                return;
            }

            meshFilter.sharedMesh = mesh;
            // The prefab scaled the unit cube by 0.8x1.6x0.6 to make it rack-sized; ServerRack.obj is already
            // authored at that size, so keep scale at 1 to avoid double-scaling.
            meshFilter.transform.localScale = Vector3.one;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[SiliconAlley] Server.prefab now renders '{mesh.name}' from ServerRack.obj " +
                      "(scale reset to 1). Rebuild the bundle (Build + Install) to ship it.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.Refresh();
    }
}
#endif
