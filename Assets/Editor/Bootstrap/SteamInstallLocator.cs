#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using UnityEngine;

namespace BAModTemplate.Editor
{
    /// <summary>
    /// Locates the user's Big Ambitions install and current Steam build id by walking the
    /// standard Steam client data files on Windows.
    ///
    /// Discovery order:
    /// <list type="number">
    ///   <item>Read the Steam client install path from the registry
    ///         (<c>HKCU\Software\Valve\Steam\SteamPath</c>, falling back to
    ///         <c>HKLM\SOFTWARE\WOW6432Node\Valve\Steam\InstallPath</c>).</item>
    ///   <item>Enumerate Steam libraries from <c>steamapps/libraryfolders.vdf</c>.</item>
    ///   <item>For each library, check for <c>steamapps/appmanifest_&lt;AppId&gt;.acf</c>
    ///         and parse <c>installdir</c> and <c>buildid</c>.</item>
    /// </list>
    ///
    /// Steam's <c>.vdf</c>/<c>.acf</c> format is key-value text; we parse with simple regex
    /// rather than pulling in a full VDF library. Path strings use <c>\\</c> escapes — those
    /// are unescaped after matching.
    /// </summary>
    public static class SteamInstallLocator
    {
        /// <summary>
        /// Big Ambitions' Steam app id (from the store URL /app/1331550/).
        /// If this ever changes, the name-based fallback below still finds the install.
        /// </summary>
        public const string BigAmbitionsAppId = "1331550";

        /// <summary>
        /// Game name as written in Steam's appmanifest, used as a name-based fallback when
        /// the hardcoded app id lookup misses (e.g. app id changed).
        /// </summary>
        public const string BigAmbitionsName = "Big Ambitions";

        private const string ManagedRelativePath = "Big Ambitions_Data/Managed";

        private static readonly Regex NameRegex = new(
            "\"name\"\\s+\"([^\"]+)\"",
            RegexOptions.Compiled);

        private static readonly Regex AppIdRegex = new(
            "\"appid\"\\s+\"([^\"]+)\"",
            RegexOptions.Compiled);

        private static readonly Regex PathEntryRegex = new(
            "\"path\"\\s+\"([^\"]+)\"",
            RegexOptions.Compiled);

        private static readonly Regex InstallDirRegex = new(
            "\"installdir\"\\s+\"([^\"]+)\"",
            RegexOptions.Compiled);

        private static readonly Regex BuildIdRegex = new(
            "\"buildid\"\\s+\"([^\"]+)\"",
            RegexOptions.Compiled);

        /// <summary>
        /// Attempts to auto-detect a Big Ambitions install via the Steam client.
        /// Returns <c>true</c> only when both the install folder and its <c>Managed</c>
        /// subfolder are present on disk.
        /// </summary>
        public static bool TrySteamAutoDetect(out SteamInstallInfo info)
        {
            info = default;

            if (!TryGetSteamRoot(out var steamRoot))
            {
                Debug.Log("[SteamInstallLocator] Could not locate the Steam client via registry or common paths.");
                return false;
            }
            Debug.Log($"[SteamInstallLocator] Steam client root: {steamRoot}");

            var libraries = EnumerateLibraryFolders(steamRoot).ToList();
            foreach (var library in libraries)
            {
                if (!TryReadAppManifest(library, out var installDir, out var buildId))
                    continue;

                var installPath = Path.Combine(library, "steamapps", "common", installDir);
                if (!IsValidBigAmbitionsInstall(installPath))
                {
                    Debug.LogWarning(
                        $"[SteamInstallLocator] Found Big Ambitions appmanifest in '{library}', " +
                        $"but '{installPath}' does not contain '{ManagedRelativePath}'. Is the game fully installed?");
                    continue;
                }

                info = new SteamInstallInfo(installPath, buildId);
                Debug.Log($"[SteamInstallLocator] Detected Big Ambitions at '{installPath}' (Steam build {buildId}).");
                return true;
            }

            Debug.Log(
                "[SteamInstallLocator] Searched " + libraries.Count + " Steam library folder(s) — no Big Ambitions " +
                "appmanifest found. Libraries scanned: " + string.Join(" ; ", libraries));
            return false;
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="installPath"/> points at a plausible
        /// Big Ambitions install (the <c>Big Ambitions_Data/Managed</c> folder exists under it).
        /// </summary>
        public static bool IsValidBigAmbitionsInstall(string? installPath)
        {
            if (string.IsNullOrWhiteSpace(installPath)) return false;
            var managed = Path.Combine(installPath!, ManagedRelativePath);
            return Directory.Exists(managed);
        }

        /// <summary>
        /// Returns the absolute <c>Managed</c> folder path for the given install, without
        /// checking whether it exists.
        /// </summary>
        public static string GetManagedFolder(string installPath)
            => Path.Combine(installPath, ManagedRelativePath);

        /// <summary>
        /// Walks up from <paramref name="installPath"/> to find the enclosing Steam library
        /// (a folder that contains a <c>steamapps</c> subfolder), then reads the Big Ambitions
        /// appmanifest's <c>buildid</c>. Returns <c>false</c> for installs that are not under a
        /// Steam library (e.g. a hand-copied game folder).
        /// </summary>
        public static bool TryReadBuildIdFor(string installPath, out string buildId)
        {
            buildId = string.Empty;
            if (string.IsNullOrWhiteSpace(installPath)) return false;

            var library = FindEnclosingSteamLibrary(installPath);
            if (library == null) return false;

            return TryReadAppManifest(library, out _, out buildId);
        }

        private static string? FindEnclosingSteamLibrary(string installPath)
        {
            try
            {
                var dir = new DirectoryInfo(installPath);
                while (dir != null)
                {
                    var steamappsCandidate = Path.Combine(dir.FullName, "steamapps");
                    if (Directory.Exists(steamappsCandidate))
                        return dir.FullName;
                    dir = dir.Parent;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SteamInstallLocator] FindEnclosingSteamLibrary failed: {e.Message}");
            }
            return null;
        }

        private static bool TryGetSteamRoot(out string steamRoot)
        {
            steamRoot = string.Empty;
            try
            {
                using (var hkcu = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (hkcu?.GetValue("SteamPath") is string p && !string.IsNullOrWhiteSpace(p))
                    {
                        steamRoot = NormalizePath(p);
                        if (Directory.Exists(steamRoot)) return true;
                    }
                }

                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                    .OpenSubKey(@"SOFTWARE\Valve\Steam"))
                {
                    if (hklm?.GetValue("InstallPath") is string p && !string.IsNullOrWhiteSpace(p))
                    {
                        steamRoot = NormalizePath(p);
                        if (Directory.Exists(steamRoot)) return true;
                    }
                }

                using (var hklm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    if (hklm64?.GetValue("InstallPath") is string p && !string.IsNullOrWhiteSpace(p))
                    {
                        steamRoot = NormalizePath(p);
                        if (Directory.Exists(steamRoot)) return true;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SteamInstallLocator] Registry read failed: {e.Message}");
            }

            foreach (var fallback in CommonSteamRoots())
            {
                if (Directory.Exists(fallback))
                {
                    steamRoot = NormalizePath(fallback);
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<string> CommonSteamRoots()
        {
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(pf86)) yield return Path.Combine(pf86, "Steam");

            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(pf)) yield return Path.Combine(pf, "Steam");
        }

        private static IEnumerable<string> EnumerateLibraryFolders(string steamRoot)
        {
            // The Steam client itself is always library #0.
            yield return steamRoot;

            var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath)) yield break;

            string text;
            try { text = File.ReadAllText(vdfPath); }
            catch (Exception e)
            {
                Debug.LogWarning($"[SteamInstallLocator] Could not read libraryfolders.vdf: {e.Message}");
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { NormalizePath(steamRoot) };
            foreach (Match m in PathEntryRegex.Matches(text))
            {
                var raw = m.Groups[1].Value;
                var path = NormalizePath(UnescapeVdfString(raw));
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (!seen.Add(path)) continue;
                if (!Directory.Exists(path)) continue;
                yield return path;
            }
        }

        private static bool TryReadAppManifest(string library, out string installDir, out string buildId)
        {
            installDir = string.Empty;
            buildId = string.Empty;

            var byAppId = Path.Combine(library, "steamapps", $"appmanifest_{BigAmbitionsAppId}.acf");
            if (File.Exists(byAppId) && TryParseAppManifest(byAppId, out installDir, out buildId))
                return true;

            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps)) return false;

            foreach (var acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
            {
                string text;
                try { text = File.ReadAllText(acf); }
                catch { continue; }

                var nameMatch = NameRegex.Match(text);
                if (!nameMatch.Success) continue;
                var gameName = UnescapeVdfString(nameMatch.Groups[1].Value);
                if (!string.Equals(gameName, BigAmbitionsName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (TryParseAppManifest(acf, out installDir, out buildId))
                {
                    var foundAppId = AppIdRegex.Match(text).Groups[1].Value;
                    if (!string.Equals(foundAppId, BigAmbitionsAppId, StringComparison.Ordinal) &&
                        !string.IsNullOrEmpty(foundAppId))
                    {
                        Debug.LogWarning(
                            $"[SteamInstallLocator] Matched Big Ambitions by name in '{Path.GetFileName(acf)}' " +
                            $"(app id {foundAppId}). Update BigAmbitionsAppId constant to match.");
                    }
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseAppManifest(string acfPath, out string installDir, out string buildId)
        {
            installDir = string.Empty;
            buildId = string.Empty;

            string text;
            try { text = File.ReadAllText(acfPath); }
            catch (Exception e)
            {
                Debug.LogWarning($"[SteamInstallLocator] Could not read {acfPath}: {e.Message}");
                return false;
            }

            var installMatch = InstallDirRegex.Match(text);
            if (!installMatch.Success) return false;
            installDir = UnescapeVdfString(installMatch.Groups[1].Value);

            var buildMatch = BuildIdRegex.Match(text);
            buildId = buildMatch.Success ? buildMatch.Groups[1].Value : string.Empty;

            return !string.IsNullOrWhiteSpace(installDir);
        }

        private static string UnescapeVdfString(string raw)
            => raw.Replace("\\\\", "\\").Replace("\\\"", "\"");

        private static string NormalizePath(string path)
        {
            try { return Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar)); }
            catch { return path; }
        }
    }

    /// <summary>
    /// Result of a successful Steam lookup: the install folder and the Steam build id
    /// for that specific download.
    /// </summary>
    public readonly struct SteamInstallInfo
    {
        public SteamInstallInfo(string installPath, string buildId)
        {
            InstallPath = installPath ?? string.Empty;
            BuildId = buildId ?? string.Empty;
        }

        /// <summary>Absolute path to the Big Ambitions install folder (contains <c>Big Ambitions_Data</c>).</summary>
        public string InstallPath { get; }

        /// <summary>Steam build id as reported by the appmanifest, or empty string if absent.</summary>
        public string BuildId { get; }

        public bool IsValid => !string.IsNullOrEmpty(InstallPath);
    }
}
