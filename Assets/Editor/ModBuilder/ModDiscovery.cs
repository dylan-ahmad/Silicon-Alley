#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using CompilationAssembly = UnityEditor.Compilation.Assembly;

namespace BAModTemplate.Editor
{
    /// <summary>
    /// Immutable snapshot of one mod discovered under <c>Assets/Mods/</c>.
    /// </summary>
    public sealed class DiscoveredMod
    {
        public DiscoveredMod(
            BAModManifest manifest,
            string manifestAssetPath,
            string modFolderAssetPath,
            string modFolderAbsolutePath,
            string asmdefAssetPath,
            string asmdefName,
            CompilationAssembly playerAssembly,
            string editorCompiledDllPath)
        {
            Manifest = manifest;
            ManifestAssetPath = manifestAssetPath;
            ModFolderAssetPath = modFolderAssetPath;
            ModFolderAbsolutePath = modFolderAbsolutePath;
            AsmdefAssetPath = asmdefAssetPath;
            AsmdefName = asmdefName;
            PlayerAssembly = playerAssembly;
            EditorCompiledDllPath = editorCompiledDllPath;
        }

        public BAModManifest Manifest { get; }
        public string ManifestAssetPath { get; }

        /// <summary>e.g. <c>Assets/Mods/FalconToy</c>.</summary>
        public string ModFolderAssetPath { get; }

        /// <summary>Absolute OS path to the mod folder.</summary>
        public string ModFolderAbsolutePath { get; }

        /// <summary>e.g. <c>Assets/Mods/FalconToy/FalconToy.asmdef</c>. Empty if manifest has no asmdef reference.</summary>
        public string AsmdefAssetPath { get; }

        /// <summary>Asmdef logical name (DLL filename sans <c>.dll</c>).</summary>
        public string AsmdefName { get; }

        /// <summary>
        /// Player-mode assembly record resolved from <c>CompilationPipeline.GetAssemblies</c>.
        /// <c>null</c> if the asmdef is missing from the player compile (e.g. deleted, filtered out).
        /// </summary>
        public CompilationAssembly PlayerAssembly { get; }

        /// <summary>
        /// Absolute path to <c>Library/ScriptAssemblies/&lt;AsmdefName&gt;.dll</c>. Used only for the
        /// metadata-based <c>RegisterModClass</c> validator; never shipped.
        /// </summary>
        public string EditorCompiledDllPath { get; }

        public string DisplayNameOrModId =>
            string.IsNullOrWhiteSpace(Manifest.DisplayName) ? Manifest.ModId : Manifest.DisplayName;
    }

    public static class ModDiscovery
    {
        public const string ModsRootAssetPath = "Assets/Mods";

        /// <summary>
        /// Scans <c>Assets/Mods/</c> for <see cref="BAModManifest"/> assets and produces one
        /// <see cref="DiscoveredMod"/> per manifest. Mods with a missing asmdef reference still
        /// surface (with empty <c>AsmdefAssetPath</c> / <c>null</c> <c>PlayerAssembly</c>) so the
        /// validator can complain about them.
        /// </summary>
        public static IReadOnlyList<DiscoveredMod> DiscoverAll()
        {
            var results = new List<DiscoveredMod>();

            var playerAssemblies = CompilationPipeline
                .GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)
                .ToDictionary(a => a.name, StringComparer.Ordinal);

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
                return results;

            var scriptAssembliesDir = Path.GetFullPath(Path.Combine(projectRoot, "Library", "ScriptAssemblies"));

            var guids = AssetDatabase.FindAssets("t:" + nameof(BAModManifest));
            foreach (var guid in guids)
            {
                var manifestPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(manifestPath)) continue;

                var manifest = AssetDatabase.LoadAssetAtPath<BAModManifest>(manifestPath);
                if (manifest == null) continue;

                var modFolderAssetPath = NormaliseAssetPath(Path.GetDirectoryName(manifestPath));
                var modFolderAbsolutePath = string.IsNullOrEmpty(modFolderAssetPath)
                    ? string.Empty
                    : Path.GetFullPath(Path.Combine(projectRoot, modFolderAssetPath));

                var asmdefAssetPath = string.Empty;
                var asmdefName = string.Empty;
                if (manifest.ModAssembly != null)
                {
                    asmdefAssetPath = AssetDatabase.GetAssetPath(manifest.ModAssembly);
                    asmdefName = Path.GetFileNameWithoutExtension(asmdefAssetPath);
                }
                else if (!string.IsNullOrEmpty(modFolderAssetPath) && Directory.Exists(modFolderAbsolutePath))
                {
                    // Fallback: if the manifest has no explicit ModAssembly reference but exactly
                    // one .asmdef lives in the mod folder, use it. Keeps the sample self-setup
                    // and lets modders skip the drag-and-drop step.
                    var asmdefs = Directory.GetFiles(modFolderAbsolutePath, "*.asmdef", SearchOption.TopDirectoryOnly);
                    if (asmdefs.Length == 1)
                    {
                        var asmdefFileName = Path.GetFileName(asmdefs[0]);
                        asmdefAssetPath = modFolderAssetPath + "/" + asmdefFileName;
                        asmdefName = Path.GetFileNameWithoutExtension(asmdefFileName);
                    }
                }

                playerAssemblies.TryGetValue(asmdefName ?? string.Empty, out var playerAssembly);

                var editorDllPath = string.IsNullOrEmpty(asmdefName)
                    ? string.Empty
                    : Path.Combine(scriptAssembliesDir, asmdefName + ".dll");

                results.Add(new DiscoveredMod(
                    manifest: manifest,
                    manifestAssetPath: manifestPath,
                    modFolderAssetPath: modFolderAssetPath,
                    modFolderAbsolutePath: modFolderAbsolutePath,
                    asmdefAssetPath: asmdefAssetPath,
                    asmdefName: asmdefName ?? string.Empty,
                    playerAssembly: playerAssembly,
                    editorCompiledDllPath: editorDllPath));
            }

            results.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(
                a.Manifest.ModId, b.Manifest.ModId));

            return results;
        }

        public static DiscoveredMod? DiscoverFor(BAModManifest manifest)
        {
            if (manifest == null) return null;
            foreach (var mod in DiscoverAll())
            {
                if (ReferenceEquals(mod.Manifest, manifest))
                    return mod;
            }
            return null;
        }

        public static bool IsDirectlyUnderModsRoot(string modFolderAssetPath)
        {
            if (string.IsNullOrEmpty(modFolderAssetPath)) return false;
            var parent = NormaliseAssetPath(Path.GetDirectoryName(modFolderAssetPath));
            return string.Equals(parent, ModsRootAssetPath, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormaliseAssetPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return path!.Replace('\\', '/').TrimEnd('/');
        }
    }
}
