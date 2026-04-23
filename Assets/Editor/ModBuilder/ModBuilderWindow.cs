#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BAModTemplate.Editor
{
    /// <summary>
    /// IMGUI window — menu entry <c>Big Ambitions/Mod Builder</c> — that discovers mods, validates them,
    /// and drives <see cref="ModPackager"/> / <see cref="ModInstaller"/>.
    /// </summary>
    public sealed class ModBuilderWindow : EditorWindow
    {
        private const int LogCapacity = 200;

        private IReadOnlyList<DiscoveredMod> _mods = Array.Empty<DiscoveredMod>();
        private Dictionary<string, IReadOnlyList<ValidationIssue>> _issuesByModId = new();
        private Dictionary<string, BuildJob> _jobsByModId = new();
        private HashSet<string> _expandedModIds = new();

        private readonly Queue<string> _log = new();
        private Vector2 _modListScroll;
        private Vector2 _logScroll;

        [MenuItem("Big Ambitions/Mod Builder", priority = 10)]
        public static void ShowWindow()
        {
            if (!CanUseModBuilder(out var status))
            {
                BAModTemplate.Editor.Branding.WelcomeWindow.ShowWindow();
                var message = status.State switch
                {
                    GameDllImporter.GameDllState.BigAmbitionsNotFound =>
                        "Import the Big Ambitions DLLs from the Welcome window before using Mod Builder.",
                    GameDllImporter.GameDllState.ReadyToImport =>
                        "Import the Big Ambitions DLLs from the Welcome window before using Mod Builder.",
                    _ =>
                        "Mod Builder is unavailable until the required game DLLs have been imported.",
                };
                EditorUtility.DisplayDialog("Mod Builder Locked", message, "Open Welcome");
                return;
            }

            var window = GetWindow<ModBuilderWindow>("Mod Builder");
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        [MenuItem("Big Ambitions/Mod Builder", validate = true)]
        private static bool ValidateShowWindow()
            => CanUseModBuilder(out _);

        private void OnEnable()
        {
            if (!CanUseModBuilder(out _))
                return;

            ModPackager.JobChanged += OnJobChanged;
            Refresh();
        }

        private void OnDisable()
        {
            ModPackager.JobChanged -= OnJobChanged;
        }

        private void OnJobChanged(BuildJob job)
        {
            _jobsByModId[job.Mod.Manifest.ModId] = job;
            if (job.IsTerminal)
            {
                AppendLog($"[{job.Mod.Manifest.ModId}] {job.State}: {job.StatusText}");
            }
            Repaint();
        }

        private void Refresh()
        {
            _mods = ModDiscovery.DiscoverAll();
            _issuesByModId = _mods.ToDictionary(
                m => m.Manifest.ModId,
                m => ModValidator.Validate(m, _mods));
            Repaint();
        }

        private void OnGUI()
        {
            if (!CanUseModBuilder(out var status))
            {
                DrawBlockedState(status);
                return;
            }

            DrawToolbar();
            EditorGUILayout.Space(4);
            DrawModList();
            EditorGUILayout.Space(4);
            DrawLog();
        }

        private static bool CanUseModBuilder(out GameDllImporter.Status status)
        {
            status = GameDllImporter.GetStatus();
            return status.State == GameDllImporter.GameDllState.UpToDate
                || status.State == GameDllImporter.GameDllState.UpdateAvailable;
        }

        private void DrawBlockedState(GameDllImporter.Status status)
        {
            EditorGUILayout.Space(8);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Mod Builder is locked", EditorStyles.boldLabel);

                var message = status.State switch
                {
                    GameDllImporter.GameDllState.BigAmbitionsNotFound =>
                        "The Big Ambitions install has not been found yet. Open the Welcome window, point it at your Steam install, and import the required DLLs first.",
                    GameDllImporter.GameDllState.ReadyToImport =>
                        "The required Big Ambitions DLLs have not been imported yet. Open the Welcome window and click 'Import DLLs from Steam' before using Mod Builder.",
                    _ =>
                        "Mod Builder is unavailable until the required game DLLs have been imported.",
                };

                EditorGUILayout.HelpBox(message, MessageType.Warning);
                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open Welcome", GUILayout.Height(28)))
                        BAModTemplate.Editor.Branding.WelcomeWindow.ShowWindow();

                    if (GUILayout.Button("Refresh", GUILayout.Height(28)))
                        Repaint();
                }
            }
        }

        // ---------------- toolbar ----------------

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(64)))
                    Refresh();

                GUI.enabled = _mods.Count > 0;
                if (GUILayout.Button("Validate All", EditorStyles.toolbarButton, GUILayout.Width(96)))
                    Refresh();

                GUI.enabled = _mods.Count > 0 && !ModPackager.IsBusy;
                if (GUILayout.Button("Build All", EditorStyles.toolbarButton, GUILayout.Width(72)))
                    BuildAll(installAfterBuild: false);
                if (GUILayout.Button("Build + Install All", EditorStyles.toolbarButton, GUILayout.Width(130)))
                    BuildAll(installAfterBuild: true);
                GUI.enabled = true;

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Open Output", EditorStyles.toolbarButton, GUILayout.Width(96)))
                    OpenOutputFolder();
                if (GUILayout.Button("Open ModsLocal", EditorStyles.toolbarButton, GUILayout.Width(110)))
                    ModInstaller.RevealModsLocal();

                if (GUILayout.Button(ModPackager.IsBusy ? "Busy..." : "Idle",
                        EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    // no-op; informational
                }
            }

            if (!string.IsNullOrEmpty(EditorPrefs.GetString(ModInstaller.EditorPrefsKey, string.Empty)))
            {
                EditorGUILayout.HelpBox(
                    $"ModsLocal install path override: {EditorPrefs.GetString(ModInstaller.EditorPrefsKey, string.Empty)}",
                    MessageType.Info);
            }
        }

        // ---------------- mod list ----------------

        private void DrawModList()
        {
            if (_mods.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No mods discovered. Create a folder under Assets/Mods/ with a ModManifest asset and an .asmdef.",
                    MessageType.Info);
                return;
            }

            _modListScroll = EditorGUILayout.BeginScrollView(_modListScroll);
            foreach (var mod in _mods)
            {
                DrawModRow(mod);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawModRow(DiscoveredMod mod)
        {
            var modId = mod.Manifest.ModId;
            var issues = _issuesByModId.TryGetValue(modId, out var i) ? i : Array.Empty<ValidationIssue>();
            var maxSeverity = ModValidator.MaxSeverity(issues);
            _jobsByModId.TryGetValue(modId, out var job);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var expanded = _expandedModIds.Contains(modId);
                    var newExpanded = EditorGUILayout.Foldout(expanded, GetRowTitle(mod, maxSeverity, job), true);
                    if (newExpanded != expanded)
                    {
                        if (newExpanded) _expandedModIds.Add(modId);
                        else _expandedModIds.Remove(modId);
                    }

                    GUILayout.FlexibleSpace();

                    var canBuild = !ModPackager.IsBusy && maxSeverity != Severity.Error;
                    GUI.enabled = canBuild;
                    if (GUILayout.Button("Build", GUILayout.Width(60)))
                        EnqueueBuild(mod, install: false, reveal: true);
                    if (GUILayout.Button("Build + Install", GUILayout.Width(105)))
                        EnqueueBuild(mod, install: true, reveal: false);
                    GUI.enabled = !ModPackager.IsBusy;
                    if (GUILayout.Button("Add Dep", GUILayout.Width(68)))
                        DependencyActions.AddDependencyViaFilePicker(mod);
                    GUI.enabled = true;

                    if (GUILayout.Button("Reveal", GUILayout.Width(60)))
                        RevealMod(mod);
                }

                if (_expandedModIds.Contains(modId))
                {
                    DrawModDetails(mod, issues, job);
                }
            }
        }

        private string GetRowTitle(DiscoveredMod mod, Severity severity, BuildJob? job)
        {
            var icon = severity switch
            {
                Severity.Error => "[X]",
                Severity.Warning => "[!]",
                _ => "[OK]",
            };

            var title = $"{icon} {mod.DisplayNameOrModId} ({mod.Manifest.ModId})";
            if (job != null && !job.IsTerminal)
                title += $" — {job.StatusText}";
            else if (job is { State: BuildState.Done })
                title += " — Built";
            else if (job is { State: BuildState.Failed })
                title += " — Build failed";

            return title;
        }

        private void DrawModDetails(DiscoveredMod mod, IReadOnlyList<ValidationIssue> issues, BuildJob? job)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Folder:", mod.ModFolderAssetPath);
            EditorGUILayout.LabelField("Asmdef:", string.IsNullOrEmpty(mod.AsmdefAssetPath) ? "(none)" : mod.AsmdefAssetPath);
            EditorGUILayout.LabelField("Bundle:", mod.Manifest.EffectiveAssetBundleName);
            EditorGUILayout.LabelField("Target platforms:", mod.Manifest.TargetPlatforms.ToString());

            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("All checks pass.", MessageType.Info);
            }
            else
            {
                foreach (var issue in issues)
                {
                    var msgType = issue.Severity switch
                    {
                        Severity.Error => MessageType.Error,
                        Severity.Warning => MessageType.Warning,
                        _ => MessageType.Info,
                    };
                    EditorGUILayout.HelpBox(issue.Message, msgType);
                    if (issue.QuickFix != null)
                    {
                        var label = string.IsNullOrEmpty(issue.QuickFixLabel) ? "Fix" : issue.QuickFixLabel;
                        if (GUILayout.Button(label!, GUILayout.Width(120)))
                        {
                            try
                            {
                                issue.QuickFix.Invoke();
                                Refresh();
                            }
                            catch (Exception ex)
                            {
                                AppendLog($"[{mod.Manifest.ModId}] Quick-fix failed: {ex.Message}");
                            }
                        }
                    }
                }
            }

            if (job != null && job.Log.Count > 0)
            {
                EditorGUILayout.LabelField("Last job:", job.StatusText);
                using (new EditorGUILayout.VerticalScope(EditorStyles.textArea))
                {
                    var startIndex = Math.Max(0, job.Log.Count - 8);
                    for (int i = startIndex; i < job.Log.Count; i++)
                        EditorGUILayout.LabelField(job.Log[i]);
                }
            }

            EditorGUI.indentLevel--;
        }

        // ---------------- log ----------------

        private void DrawLog()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                    _log.Clear();
            }

            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.MinHeight(120));
            foreach (var entry in _log)
            {
                EditorGUILayout.LabelField(entry, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        private void AppendLog(string message)
        {
            _log.Enqueue($"{DateTime.Now:HH:mm:ss}  {message}");
            while (_log.Count > LogCapacity) _log.Dequeue();
        }

        // ---------------- actions ----------------

        private void EnqueueBuild(DiscoveredMod mod, bool install, bool reveal)
        {
            AppendLog($"[{mod.Manifest.ModId}] Enqueued (install={install}).");
            var job = ModPackager.Enqueue(mod, install, reveal);
            _jobsByModId[mod.Manifest.ModId] = job;
        }

        private void BuildAll(bool installAfterBuild)
        {
            var buildable = _mods
                .Where(m =>
                {
                    var issues = _issuesByModId.TryGetValue(m.Manifest.ModId, out var i) ? i : Array.Empty<ValidationIssue>();
                    return ModValidator.MaxSeverity(issues) != Severity.Error;
                })
                .ToList();

            if (buildable.Count == 0)
            {
                AppendLog("Build All aborted: no mods pass validation.");
                return;
            }

            AppendLog($"Build All: queuing {buildable.Count} mod(s) (install={installAfterBuild}).");
            foreach (var job in ModPackager.EnqueueAll(buildable, installAfterBuild))
            {
                _jobsByModId[job.Mod.Manifest.ModId] = job;
            }
        }

        private void RevealMod(DiscoveredMod mod)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            var outputDir = Path.Combine(projectRoot, "Output", mod.Manifest.ModId);
            if (Directory.Exists(outputDir))
            {
                EditorUtility.RevealInFinder(outputDir);
                return;
            }
            var folderAbsolute = mod.ModFolderAbsolutePath;
            if (Directory.Exists(folderAbsolute))
                EditorUtility.RevealInFinder(folderAbsolute);
        }

        private void OpenOutputFolder()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            var outputDir = Path.Combine(projectRoot, "Output");
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);
            EditorUtility.RevealInFinder(outputDir);
        }
    }
}
