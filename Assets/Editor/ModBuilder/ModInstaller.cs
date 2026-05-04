#nullable enable
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BAModTemplate.Editor
{
    /// <summary>
    /// Installs a built mod (directory under <c>Output/&lt;ModId&gt;/</c>) into the
    /// Big Ambitions ModsLocal folder for end-to-end testing without restarting the editor.
    /// </summary>
    public static class ModInstaller
    {
        public const string EditorPrefsKey = "BAModBuilder.ModsLocalPath";

        /// <summary>
        /// Returns the ModsLocal root (the directory that contains one folder per installed mod).
        /// Overridable via <see cref="EditorPrefsKey"/>.
        /// </summary>
        public static string GetModsLocalRoot()
        {
            var overridePath = EditorPrefs.GetString(EditorPrefsKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(overridePath))
                return overridePath;

            // Windows: %LocalLow%/Hovgaard Games/Big Ambitions/ModsLocal
            // Environment.SpecialFolder.LocalApplicationData is %LocalAppData% on Windows; LocalLow
            // is not exposed directly in .NET Framework. Derive it from LocalAppData by swapping
            // 'Local' -> 'LocalLow' which is the usual Unity/Windows trick.
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var localLow = local.Replace(@"\Local", @"\LocalLow");
            return Path.Combine(localLow, "Hovgaard Games", "Big Ambitions", "ModsLocal");
        }

        public static string GetInstallRoot(DiscoveredMod mod)
            => Path.Combine(GetModsLocalRoot(), mod.Manifest.ModId);

        /// <summary>
        /// Copies <paramref name="outputDirectoryAbsolute"/> (i.e. <c>Output/&lt;ModId&gt;/</c>)
        /// into the ModsLocal target. Overwrites the existing mod folder.
        /// </summary>
        public static void Install(DiscoveredMod mod, string outputDirectoryAbsolute)
        {
            if (!Directory.Exists(outputDirectoryAbsolute))
                throw new DirectoryNotFoundException($"Output folder not found: {outputDirectoryAbsolute}");

            var modsLocalRoot = GetModsLocalRoot();
            var parentOfMod = Path.GetDirectoryName(modsLocalRoot);
            if (string.IsNullOrEmpty(parentOfMod) || !Directory.Exists(parentOfMod))
            {
                throw new InvalidOperationException(
                    $"ModsLocal parent folder '{parentOfMod}' does not exist. " +
                    $"Install Big Ambitions first, or override the ModsLocal path via EditorPrefs key '{EditorPrefsKey}'.");
            }

            Directory.CreateDirectory(modsLocalRoot);

            var installDir = Path.Combine(modsLocalRoot, mod.Manifest.ModId);
            if (Directory.Exists(installDir))
            {
                Directory.Delete(installDir, recursive: true);
            }
            CopyDirectory(outputDirectoryAbsolute, installDir);
            Debug.Log($"[ModInstaller] Installed '{mod.Manifest.ModId}' to '{installDir}'.");
        }

        public static void RevealModsLocal()
        {
            var root = GetModsLocalRoot();
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }
            EditorUtility.RevealInFinder(root);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
            {
                var dst = Path.Combine(destination, Path.GetFileName(file));
                File.Copy(file, dst, overwrite: true);
            }
            foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
            {
                var dst = Path.Combine(destination, Path.GetFileName(dir));
                CopyDirectory(dir, dst);
            }
        }
    }
}
