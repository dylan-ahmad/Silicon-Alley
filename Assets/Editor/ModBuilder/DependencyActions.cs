#nullable enable
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BAModTemplate.Editor
{
    /// <summary>
    /// Editor-side helpers that manage a mod's third-party managed DLL dependencies —
    /// the files dropped in <c>Assets/Mods/&lt;ModId&gt;/Dependencies/</c>.
    ///
    /// Each dependency is imported as a proper Unity plugin (Editor + Standalone enabled,
    /// <c>isExplicitlyReferenced: 1</c>) so the editor compiles against it AND it ships with the
    /// mod. When a mod has at least one dependency, its asmdef must use
    /// <c>overrideReferences: true</c> plus a <c>precompiledReferences</c> list that includes
    /// every canonical game DLL (see <see cref="CanonicalGameDlls"/>) AND each dependency DLL.
    /// </summary>
    public static class DependencyActions
    {
        /// <summary>
        /// Shows a file picker, copies the selected DLL into the mod's <c>Dependencies/</c>
        /// folder, writes a Unity plugin meta next to it, and mutates the mod's asmdef so the
        /// DLL is referenced.
        /// </summary>
        public static void AddDependencyViaFilePicker(DiscoveredMod mod)
        {
            var sourcePath = EditorUtility.OpenFilePanel(
                "Add dependency DLL for " + mod.Manifest.ModId,
                string.Empty,
                "dll");
            if (string.IsNullOrEmpty(sourcePath)) return;
            AddDependency(mod, sourcePath);
        }

        /// <summary>Copy <paramref name="sourceDllAbsolutePath"/> into the mod and wire it up.</summary>
        public static void AddDependency(DiscoveredMod mod, string sourceDllAbsolutePath)
        {
            if (!File.Exists(sourceDllAbsolutePath))
                throw new FileNotFoundException("Dependency DLL not found.", sourceDllAbsolutePath);
            if (!sourceDllAbsolutePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Dependency must be a .dll file.");

            var depsFolderAssetPath = mod.ModFolderAssetPath + "/Dependencies";
            var depsFolderAbsolutePath = AssetPathToAbsolute(depsFolderAssetPath);
            Directory.CreateDirectory(depsFolderAbsolutePath);

            var dllFileName = Path.GetFileName(sourceDllAbsolutePath);
            var destinationAbsolutePath = Path.Combine(depsFolderAbsolutePath, dllFileName);
            File.Copy(sourceDllAbsolutePath, destinationAbsolutePath, overwrite: true);

            AssetDatabase.Refresh(ImportAssetOptions.Default);

            var destinationAssetPath = depsFolderAssetPath + "/" + dllFileName;
            ApplyPluginImportSettings(destinationAssetPath);

            WireAsmdefForDependency(mod, dllFileName);

            AssetDatabase.Refresh();
            Debug.Log($"[ModBuilder] Added dependency '{dllFileName}' to '{mod.Manifest.ModId}'.");
        }

        /// <summary>
        /// Ensures every DLL under <c>Assets/Mods/&lt;ModId&gt;/Dependencies/</c> has plugin import
        /// settings matching (Editor + Standalone, isExplicitlyReferenced=true). Useful when a
        /// modder drops a raw .dll with default settings.
        /// </summary>
        public static int FixDependencyImportSettings(DiscoveredMod mod)
        {
            var depsAssetPath = ModValidator.GetDependenciesFolderAssetPath(mod);
            if (string.IsNullOrEmpty(depsAssetPath)) return 0;

            var absolute = AssetPathToAbsolute(depsAssetPath);
            if (!Directory.Exists(absolute)) return 0;

            var fixedCount = 0;
            foreach (var file in Directory.EnumerateFiles(absolute, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var assetPath = depsAssetPath + "/" + Path.GetFileName(file);
                if (ApplyPluginImportSettings(assetPath)) fixedCount++;
            }

            AssetDatabase.SaveAssets();
            return fixedCount;
        }

        /// <summary>
        /// Returns true if any import settings were changed.
        /// </summary>
        public static bool ApplyPluginImportSettings(string dllAssetPath)
        {
            var importer = AssetImporter.GetAtPath(dllAssetPath) as PluginImporter;
            if (importer == null) return false;

            var changed = false;

            // Editor + Standalone Windows/OSX/Linux enabled; everything else off. This mirrors the
            // intent of the plan: the editor compiles against the DLL AND it ships with mod builds.
            if (importer.GetCompatibleWithAnyPlatform())
            {
                importer.SetCompatibleWithAnyPlatform(false);
                changed = true;
            }
            changed |= SetPlatform(importer, BuildTarget.StandaloneWindows, true);
            changed |= SetPlatform(importer, BuildTarget.StandaloneWindows64, true);
            changed |= SetPlatform(importer, BuildTarget.StandaloneOSX, true);
            changed |= SetPlatform(importer, BuildTarget.StandaloneLinux64, true);
            changed |= SetCompatibleWithEditor(importer, true);

            if (SetIsExplicitlyReferenced(importer, true))
            {
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }
            return changed;
        }

        /// <summary>
        /// <c>PluginImporter.isExplicitlyReferenced</c> isn't public in 2022 LTS, so we flip the
        /// underlying serialized property on the importer directly.
        /// </summary>
        private static bool SetIsExplicitlyReferenced(PluginImporter importer, bool value)
        {
            var so = new SerializedObject(importer);
            var prop = so.FindProperty("m_IsExplicitlyReferenced");
            if (prop == null) return false;
            if (prop.boolValue == value) return false;
            prop.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static bool SetPlatform(PluginImporter importer, BuildTarget target, bool enabled)
        {
            if (importer.GetCompatibleWithPlatform(target) == enabled) return false;
            importer.SetCompatibleWithPlatform(target, enabled);
            return true;
        }

        private static bool SetCompatibleWithEditor(PluginImporter importer, bool enabled)
        {
            if (importer.GetCompatibleWithEditor() == enabled) return false;
            importer.SetCompatibleWithEditor(enabled);
            return true;
        }

        /// <summary>
        /// Flips the mod asmdef to <c>overrideReferences: true</c> and ensures
        /// <c>precompiledReferences</c> contains (a) every canonical game DLL and
        /// (b) the newly-added dependency DLL.
        /// </summary>
        private static void WireAsmdefForDependency(DiscoveredMod mod, string dllFileName)
        {
            if (string.IsNullOrEmpty(mod.AsmdefAssetPath))
                throw new InvalidOperationException(
                    $"Mod '{mod.Manifest.ModId}' has no asmdef; cannot add a dependency reference.");

            var absolute = Path.GetFullPath(mod.AsmdefAssetPath);
            var asmdef = AsmdefFile.Load(absolute);
            asmdef.OverrideReferences = true;

            var refs = asmdef.PrecompiledReferences;
            foreach (var gameDll in CanonicalGameDlls.All)
                if (!refs.Contains(gameDll, StringComparer.Ordinal))
                    refs.Add(gameDll);
            if (!refs.Contains(dllFileName, StringComparer.Ordinal))
                refs.Add(dllFileName);
            asmdef.PrecompiledReferences = refs;

            asmdef.Save();
            AssetDatabase.ImportAsset(mod.AsmdefAssetPath);
        }

        /// <summary>Sync every mod's asmdef against the current <see cref="CanonicalGameDlls.All"/> list.</summary>
        public static int SyncAllModAsmdefs()
        {
            var count = 0;
            foreach (var mod in ModDiscovery.DiscoverAll())
            {
                if (string.IsNullOrEmpty(mod.AsmdefAssetPath)) continue;
                var abs = Path.GetFullPath(mod.AsmdefAssetPath);
                var asmdef = AsmdefFile.Load(abs);
                if (!asmdef.OverrideReferences) continue;

                var refs = asmdef.PrecompiledReferences;
                var before = refs.Count;
                foreach (var gameDll in CanonicalGameDlls.All)
                    if (!refs.Contains(gameDll, StringComparer.Ordinal))
                        refs.Add(gameDll);
                if (refs.Count == before) continue;

                asmdef.PrecompiledReferences = refs;
                asmdef.Save();
                AssetDatabase.ImportAsset(mod.AsmdefAssetPath);
                count++;
            }
            if (count > 0) AssetDatabase.SaveAssets();
            return count;
        }

        private static string AssetPathToAbsolute(string assetPath)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }
}
