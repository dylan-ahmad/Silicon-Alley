#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace BAModTemplate.Editor
{
    /// <summary>
    /// Copies the canonical Big Ambitions DLLs (see <see cref="CanonicalGameDlls.All"/>) from the
    /// user's Steam install into <c>Assets/_BaDependencies/GameDlls/</c>, and tracks which Steam
    /// <c>buildid</c> they were imported at so the UI can surface "game updated — re-import"
    /// when Steam patches the game.
        ///
        /// The imported DLLs and their generated <c>*.dll.meta</c> files are gitignored because
        /// both are machine-local outputs of the welcome-screen import flow.
        /// After each import, this class reapplies the required <c>PluginImporter</c> settings
        /// (Editor + Standalone, <c>isExplicitlyReferenced: 1</c>, <c>validateReferences: 0</c>)
        /// so fresh clones do not depend on committed plugin metadata.
    /// </summary>
    public static class GameDllImporter
    {
        /// <summary>
        /// <see cref="EditorPrefs"/> key that stores the user's Big Ambitions install path,
        /// mirroring the <c>BAModBuilder.ModsLocalPath</c> convention used by
        /// <see cref="ModInstaller"/>.
        /// </summary>
        public const string EditorPrefsInstallPathKey = "BAModBuilder.BigAmbitionsInstallPath";

        /// <summary>
        /// Scripting define that enables assemblies which depend on the imported game DLLs.
        /// Kept off for a fresh clone so Unity can compile the bootstrap importer first.
        /// </summary>
        public const string ImportedDefine = "BA_GAME_DLLS_IMPORTED";

        /// <summary>
        /// Asset-relative folder that receives the copied DLLs and Unity-generated
        /// <c>.dll.meta</c> files.
        /// </summary>
        public const string GameDllsAssetFolder = "Assets/_BaDependencies/GameDlls";

        /// <summary>
        /// Per-user tracker file. Lives in <c>UserSettings/</c> so it survives Library wipes
        /// but is already covered by the existing user-settings gitignore entry.
        /// </summary>
        private const string TrackerRelativePath = "UserSettings/BAModBuilder.ImportedDlls.json";

        /// <summary>
        /// Overall health of the local <c>GameDlls</c> folder relative to the user's Steam install.
        /// Drives the indicator color and primary button label in the Welcome window.
        /// </summary>
        public enum GameDllState
        {
            /// <summary>Install path is unset, invalid, or does not contain <c>Big Ambitions_Data/Managed</c>.</summary>
            BigAmbitionsNotFound,
            /// <summary>Install path is valid, but no DLLs have been imported yet (or some are missing).</summary>
            ReadyToImport,
            /// <summary>DLLs were imported previously; Steam has since reported a newer build id.</summary>
            UpdateAvailable,
            /// <summary>DLLs on disk match the Steam build id recorded at the last import.</summary>
            UpToDate,
        }

        /// <summary>Snapshot of the current state, used by the Welcome window to render one frame of UI.</summary>
        public readonly struct Status
        {
            public Status(
                GameDllState state,
                string installPath,
                string currentBuildId,
                string importedBuildId,
                DateTime? importedAtUtc,
                int importedDllCount)
            {
                State = state;
                InstallPath = installPath ?? string.Empty;
                CurrentBuildId = currentBuildId ?? string.Empty;
                ImportedBuildId = importedBuildId ?? string.Empty;
                ImportedAtUtc = importedAtUtc;
                ImportedDllCount = importedDllCount;
            }

            public GameDllState State { get; }
            public string InstallPath { get; }
            /// <summary>Current Steam appmanifest build id for this install (may be empty if manual path outside Steam).</summary>
            public string CurrentBuildId { get; }
            /// <summary>Build id recorded in the tracker at the last import (empty if never imported).</summary>
            public string ImportedBuildId { get; }
            public DateTime? ImportedAtUtc { get; }
            public int ImportedDllCount { get; }
        }

        [Serializable]
        private sealed class TrackerFile
        {
            public string installPath = string.Empty;
            public string buildId = string.Empty;
            public string importedAtUtc = string.Empty;
            public int dllCount;
        }

        /// <summary>Reads the persisted install path, or returns empty string if the user has not set one.</summary>
        public static string GetConfiguredInstallPath()
            => EditorPrefs.GetString(EditorPrefsInstallPathKey, string.Empty) ?? string.Empty;

        /// <summary>Persists the user-chosen Big Ambitions install path.</summary>
        public static void SetConfiguredInstallPath(string installPath)
            => EditorPrefs.SetString(EditorPrefsInstallPathKey, installPath ?? string.Empty);

        /// <summary>
        /// Ensures the project-level scripting define matches local machine state. This prevents
        /// an accidentally committed <c>BA_GAME_DLLS_IMPORTED</c> from forcing fresh clones into
        /// the "DLLs already imported" path before the user has actually run the importer.
        /// </summary>
        public static void ReconcileImportedDefine()
        {
            var shouldEnable = ReadTracker() != null && AllDllsPresent();
            SetImportedDefineEnabled(shouldEnable);
        }

        /// <summary>
        /// Computes the full <see cref="Status"/> for the currently configured install path.
        /// Does not mutate anything on disk.
        /// </summary>
        public static Status GetStatus()
        {
            var installPath = GetConfiguredInstallPath();
            var tracker = ReadTracker();
            var importedBuildId = tracker?.buildId ?? string.Empty;
            var importedAt = ParseUtc(tracker?.importedAtUtc);
            var importedCount = tracker?.dllCount ?? 0;

            if (!SteamInstallLocator.IsValidBigAmbitionsInstall(installPath))
            {
                return new Status(
                    GameDllState.BigAmbitionsNotFound,
                    installPath, string.Empty, importedBuildId, importedAt, importedCount);
            }

            SteamInstallLocator.TryReadBuildIdFor(installPath, out var currentBuildId);

            // Require a tracker + on-disk DLLs before we claim anything is imported. DLLs may
            // exist on disk from an earlier state (e.g. a clone made before GameDlls/*.dll
            // was gitignored) — those have never been validated against the user's Steam
            // build, so we still treat this as "ready to import".
            if (tracker == null || !AllDllsPresent())
            {
                return new Status(
                    GameDllState.ReadyToImport,
                    installPath, currentBuildId, importedBuildId, importedAt, importedCount);
            }

            if (!string.IsNullOrEmpty(currentBuildId)
                && !string.IsNullOrEmpty(importedBuildId)
                && !string.Equals(currentBuildId, importedBuildId, StringComparison.Ordinal))
            {
                return new Status(
                    GameDllState.UpdateAvailable,
                    installPath, currentBuildId, importedBuildId, importedAt, importedCount);
            }

            return new Status(
                GameDllState.UpToDate,
                installPath, currentBuildId, importedBuildId, importedAt, importedCount);
        }

        /// <summary>
        /// Copies every DLL in <see cref="CanonicalGameDlls.All"/> from the install's
        /// <c>Big Ambitions_Data/Managed</c> folder into <see cref="GameDllsAssetFolder"/>,
        /// restores deterministic DLL asset GUIDs, then writes the tracker file and refreshes
        /// the asset database.
        /// </summary>
        /// <exception cref="DirectoryNotFoundException">Install path or <c>Managed</c> folder does not exist.</exception>
        /// <exception cref="FileNotFoundException">One or more canonical DLLs are missing from the install.</exception>
        public static void Import(string installPath)
        {
            if (string.IsNullOrWhiteSpace(installPath))
                throw new DirectoryNotFoundException("Big Ambitions install path is empty.");

            var managed = SteamInstallLocator.GetManagedFolder(installPath);
            if (!Directory.Exists(managed))
                throw new DirectoryNotFoundException(
                    $"Managed folder not found under install path. Expected: {managed}");

            var destinationAbsolute = AssetPathToAbsolute(GameDllsAssetFolder);
            Directory.CreateDirectory(destinationAbsolute);

            var missing = CanonicalGameDlls.All
                .Where(n => !File.Exists(Path.Combine(managed, n)))
                .ToList();
            if (missing.Count > 0)
            {
                throw new FileNotFoundException(
                    "Some canonical DLLs are missing from the Steam install. " +
                    "Verify Big Ambitions is fully downloaded and matches this template's expected version.\n" +
                    "Missing: " + string.Join(", ", missing));
            }

            var totalTimer = System.Diagnostics.Stopwatch.StartNew();
            var copyTimer = System.Diagnostics.Stopwatch.StartNew();
            var copied = 0;
            foreach (var name in CanonicalGameDlls.All)
            {
                var src = Path.Combine(managed, name);
                var dst = Path.Combine(destinationAbsolute, name);
                if (CopyIfChanged(src, dst))
                    copied++;
            }
            copyTimer.Stop();

            var refreshTimer = System.Diagnostics.Stopwatch.StartNew();
            if (copied > 0 || AnyDllMetaMissing())
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            refreshTimer.Stop();

            var guidTimer = System.Diagnostics.Stopwatch.StartNew();
            var updatedGuids = EnsureCanonicalDllGuids();
            guidTimer.Stop();

            var pluginTimer = System.Diagnostics.Stopwatch.StartNew();
            var updatedImporters = ApplyPluginImportSettingsToAllGameDlls();
            pluginTimer.Stop();

            SteamInstallLocator.TryReadBuildIdFor(installPath, out var buildId);
            WriteTracker(new TrackerFile
            {
                installPath = installPath,
                buildId = buildId ?? string.Empty,
                importedAtUtc = DateTime.UtcNow.ToString("O"),
                dllCount = CanonicalGameDlls.All.Count,
            });

            SetConfiguredInstallPath(installPath);
            var defineTimer = System.Diagnostics.Stopwatch.StartNew();
            EnsureImportedDefineEnabled();
            defineTimer.Stop();
            totalTimer.Stop();

            Debug.Log(
                $"[GameDllImporter] Imported {CanonicalGameDlls.All.Count} DLLs from '{managed}' " +
                $"({copied} file copies, {updatedGuids} guid updates, {updatedImporters} importer updates, " +
                $"copy {copyTimer.ElapsedMilliseconds} ms, refresh {refreshTimer.ElapsedMilliseconds} ms, " +
                $"guid repair {guidTimer.ElapsedMilliseconds} ms, plugin settings {pluginTimer.ElapsedMilliseconds} ms, " +
                $"define {defineTimer.ElapsedMilliseconds} ms, " +
                $"total {totalTimer.ElapsedMilliseconds} ms)" +
                (string.IsNullOrEmpty(buildId) ? "." : $" for Steam build {buildId}."));
        }

        /// <summary>
        /// Returns <c>true</c> when every name in <see cref="CanonicalGameDlls.All"/> exists
        /// under <see cref="GameDllsAssetFolder"/> on disk.
        /// </summary>
        public static bool AllDllsPresent()
        {
            var folder = AssetPathToAbsolute(GameDllsAssetFolder);
            if (!Directory.Exists(folder)) return false;
            foreach (var name in CanonicalGameDlls.All)
            {
                if (!File.Exists(Path.Combine(folder, name))) return false;
            }
            return true;
        }

        private static TrackerFile? ReadTracker()
        {
            var abs = TrackerAbsolutePath();
            if (!File.Exists(abs)) return null;
            try
            {
                var json = File.ReadAllText(abs);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonConvert.DeserializeObject<TrackerFile>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameDllImporter] Could not read tracker file: {e.Message}");
                return null;
            }
        }

        private static void WriteTracker(TrackerFile file)
        {
            var abs = TrackerAbsolutePath();
            var dir = Path.GetDirectoryName(abs);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonConvert.SerializeObject(file, Formatting.Indented);
            File.WriteAllText(abs, json);
        }

        private static string TrackerAbsolutePath()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            return Path.Combine(projectRoot, TrackerRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string AssetPathToAbsolute(string assetPath)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static int ApplyPluginImportSettingsToAllGameDlls()
        {
            var changedPaths = CanonicalGameDlls.All
                .Select(dllName => GameDllsAssetFolder + "/" + dllName)
                .Where(ApplyPluginImportSettings)
                .ToList();

            if (changedPaths.Count == 0)
                return 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var dllAssetPath in changedPaths)
                    AssetDatabase.WriteImportSettingsIfDirty(dllAssetPath);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            return changedPaths.Count;
        }

        private static bool AnyDllMetaMissing()
        {
            return CanonicalGameDlls.All.Any(dllName =>
                !File.Exists(AssetPathToAbsolute(GameDllsAssetFolder + "/" + dllName) + ".meta"));
        }

        private static int EnsureCanonicalDllGuids()
        {
            var changedPaths = CanonicalGameDlls.All
                .Select(dllName => GameDllsAssetFolder + "/" + dllName)
                .Where(EnsureCanonicalDllGuid)
                .ToList();

            if (changedPaths.Count == 0)
                return 0;

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var dllAssetPath in changedPaths)
                AssetDatabase.ImportAsset(
                    dllAssetPath,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            return changedPaths.Count;
        }

        private static bool EnsureCanonicalDllGuid(string dllAssetPath)
        {
            var metaPath = AssetPathToAbsolute(dllAssetPath) + ".meta";
            if (!File.Exists(metaPath))
            {
                Debug.LogWarning(
                    $"[GameDllImporter] Missing meta for '{dllAssetPath}' after refresh; cannot repair GUID.");
                return false;
            }

            var expectedGuid = ComputeDeterministicDllGuid(Path.GetFileName(dllAssetPath));
            var metaText = File.ReadAllText(metaPath);
            var currentGuid = TryReadGuid(metaText);
            if (string.Equals(currentGuid, expectedGuid, StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrEmpty(currentGuid))
            {
                Debug.LogWarning(
                    $"[GameDllImporter] Could not locate a guid entry in '{metaPath}'; leaving generated meta untouched.");
                return false;
            }

            var updated = metaText.Replace(
                $"guid: {currentGuid}",
                $"guid: {expectedGuid}",
                StringComparison.Ordinal);

            if (string.Equals(updated, metaText, StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    $"[GameDllImporter] Failed to rewrite guid for '{dllAssetPath}'; leaving generated meta untouched.");
                return false;
            }

            File.WriteAllText(metaPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }

        private static bool CopyIfChanged(string sourceAbsolutePath, string destinationAbsolutePath)
        {
            var sourceInfo = new FileInfo(sourceAbsolutePath);
            if (sourceInfo.Exists == false)
                throw new FileNotFoundException("Source DLL not found.", sourceAbsolutePath);

            var destinationInfo = new FileInfo(destinationAbsolutePath);
            if (destinationInfo.Exists
                && destinationInfo.Length == sourceInfo.Length
                && destinationInfo.LastWriteTimeUtc == sourceInfo.LastWriteTimeUtc)
            {
                return false;
            }

            File.Copy(sourceAbsolutePath, destinationAbsolutePath, overwrite: true);

            // Keep timestamps aligned so future no-op imports can skip the copy cheaply.
            File.SetLastWriteTimeUtc(destinationAbsolutePath, sourceInfo.LastWriteTimeUtc);
            return true;
        }

        private static bool ApplyPluginImportSettings(string dllAssetPath)
        {
            var importer = AssetImporter.GetAtPath(dllAssetPath) as PluginImporter;
            if (importer == null) return false;

            var changed = false;

            if (importer.GetCompatibleWithAnyPlatform())
            {
                importer.SetCompatibleWithAnyPlatform(false);
                changed = true;
            }

            changed |= SetCompatibleWithEditor(importer, true);
            changed |= SetPlatform(importer, BuildTarget.StandaloneWindows, true);
            changed |= SetPlatform(importer, BuildTarget.StandaloneWindows64, true);
            changed |= SetPlatform(importer, BuildTarget.StandaloneOSX, true);
            changed |= SetPlatform(importer, BuildTarget.StandaloneLinux64, true);
            changed |= SetIsExplicitlyReferenced(importer, true);
            changed |= SetValidateReferences(importer, false);

            if (changed)
                EditorUtility.SetDirty(importer);

            return changed;
        }

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

        private static bool SetValidateReferences(PluginImporter importer, bool value)
        {
            var so = new SerializedObject(importer);
            var prop = so.FindProperty("m_ValidateReferences");
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

        private static void EnsureImportedDefineEnabled()
            => SetImportedDefineEnabled(true);

        private static void SetImportedDefineEnabled(bool enabled)
        {
            var target = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            var raw = PlayerSettings.GetScriptingDefineSymbols(target);
            var defines = raw
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var hasDefine = defines.Contains(ImportedDefine, StringComparer.Ordinal);
            if (enabled && hasDefine) return;
            if (!enabled && !hasDefine) return;

            if (enabled)
                defines.Add(ImportedDefine);
            else
                defines.RemoveAll(d => string.Equals(d, ImportedDefine, StringComparison.Ordinal));

            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
            Debug.Log(
                $"[GameDllImporter] " +
                (enabled ? "Enabled" : "Removed") +
                $" scripting define '{ImportedDefine}' for {target.TargetName}.");
        }

        private static DateTime? ParseUtc(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (DateTime.TryParse(
                    s,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var dt))
                return dt;
            return null;
        }

        private static string ComputeDeterministicDllGuid(string dllFileName)
        {
            using var md5 = MD5.Create();
            var input = Encoding.UTF8.GetBytes("BAModTemplate.GameDllGuid:" + dllFileName.ToLowerInvariant());
            var hash = md5.ComputeHash(input);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                builder.Append(b.ToString("x2"));
            return builder.ToString();
        }

        private static string TryReadGuid(string metaText)
        {
            if (string.IsNullOrEmpty(metaText))
                return string.Empty;

            using var reader = new StringReader(metaText);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.StartsWith("guid: ", StringComparison.Ordinal))
                    continue;

                var guid = line.Substring("guid: ".Length).Trim();
                return guid.Length == 32 ? guid : string.Empty;
            }

            return string.Empty;
        }

        [InitializeOnLoad]
        private static class ImportedDefineGuard
        {
            static ImportedDefineGuard()
            {
                EditorApplication.delayCall += TryReconcile;
            }

            private static void TryReconcile()
            {
                try
                {
                    if (Application.isBatchMode) return;
                    ReconcileImportedDefine();
                    if (AllDllsPresent())
                        ApplyPluginImportSettingsToAllGameDlls();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[GameDllImporter] Failed to reconcile imported define: {ex.Message}");
                }
            }
        }
    }
}
