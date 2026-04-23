using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace BAModTemplate.Editor
{
    [Flags]
    public enum ModTargetPlatforms
    {
        None = 0,
        Windows = 1 << 0,
        Mac = 1 << 1,
    }

    /// <summary>
    /// Per-mod manifest asset. Lives at Assets/Mods/&lt;ModId&gt;/ModManifest.asset and
    /// drives discovery, validation, and packaging for a single mod.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ModManifest",
        menuName = "Big Ambitions/Mod Manifest",
        order = 0)]
    public sealed class BAModManifest : ScriptableObject
    {
        [Tooltip("Folder-name-safe identifier. Must match the mod's folder name under Assets/Mods/.")]
        public string ModId = string.Empty;

        [Tooltip("Human-readable name shown in the mod menu.")]
        public string DisplayName = string.Empty;

        public string Author = string.Empty;
        public string Version = "0.1.0";

        [Tooltip("Full Unity AssetBundle identifier (including variant suffix). " +
                 "Example: 'falcon.unity3d'. Must match the assetBundleName+variant set on each asset meta. " +
                 "Defaults to ModId lowercased if left empty.")]
        public string AssetBundleName = string.Empty;

        [Tooltip("The mod's own asmdef. Produces the shipped DLL.")]
        public AssemblyDefinitionAsset ModAssembly;

        [Tooltip("Optional folder containing locale JSON files. Copied as-is to Output/<ModId>/Locales/.")]
        public DefaultAsset LocalesFolder;

        [Tooltip("Optional folder containing third-party managed DLLs. Copied to Output/<ModId>/Dependencies/.")]
        public DefaultAsset DependenciesFolder;

        [Tooltip("Optional enums.txt file; copied to Output/<ModId>/enums.txt.")]
        public TextAsset EnumsFile;

        [Tooltip("Platforms the packager builds AssetBundles for. Default is Windows + Mac.")]
        public ModTargetPlatforms TargetPlatforms = ModTargetPlatforms.Windows | ModTargetPlatforms.Mac;

        public string EffectiveAssetBundleName =>
            string.IsNullOrWhiteSpace(AssetBundleName) ? (ModId ?? string.Empty).ToLowerInvariant() : AssetBundleName;
    }
}
