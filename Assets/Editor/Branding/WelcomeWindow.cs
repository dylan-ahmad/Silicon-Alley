#nullable enable
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BAModTemplate.Editor.Branding
{
    /// <summary>
    /// Branded welcome screen for the Big Ambitions Mod SDK. Opens automatically
    /// on project load (once per editor session), and can be reopened from the
    /// <c>Big Ambitions/Welcome</c> menu. Users can opt out via the "Show on
    /// startup" toggle at the bottom of the window.
    /// </summary>
    public sealed class WelcomeWindow : EditorWindow
    {
        private const string HeaderBackgroundAssetPath = "Assets/Editor/Branding/background.png";
        private const string HeaderLogoAssetPath = "Assets/Editor/Branding/balogo.png";

        // Bump this when the welcome content changes materially so prior "don't show"
        // opt-outs are reset and users see the new welcome once.
        private const int WelcomeVersion = 2;

        private const string PrefAutoShowKey = "BAModTemplate.Welcome.AutoShow.v1";
        private const string PrefLastSeenVersionKey = "BAModTemplate.Welcome.LastSeenVersion";
        private const string SessionShownKey = "BAModTemplate.Welcome.ShownThisSession";

        private const string DiscordUrl = "https://discord.gg/hovgaardgames";
        private const string GitHubUrl = "https://github.com/hovgaardgames/bigambitions";

        private Texture2D? _headerBackground;
        private Texture2D? _headerLogo;
        private Vector2 _scroll;
        private string _installPathField = string.Empty;
        private string? _lastImportError;

        [MenuItem("Big Ambitions/Welcome", priority = 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<WelcomeWindow>(utility: false, title: "Welcome", focus: true);
            window.titleContent = new GUIContent("Welcome", EditorGUIUtility.IconContent("d_UnityLogo").image);
            window.minSize = new Vector2(520, 520);
            window.Show();
        }

        private void OnEnable()
        {
            _headerBackground = AssetDatabase.LoadAssetAtPath<Texture2D>(HeaderBackgroundAssetPath);
            _headerLogo = AssetDatabase.LoadAssetAtPath<Texture2D>(HeaderLogoAssetPath);
            EditorPrefs.SetInt(PrefLastSeenVersionKey, WelcomeVersion);

            _installPathField = GameDllImporter.GetConfiguredInstallPath();
            if (string.IsNullOrWhiteSpace(_installPathField))
            {
                if (SteamInstallLocator.TrySteamAutoDetect(out var info))
                {
                    _installPathField = info.InstallPath;
                    GameDllImporter.SetConfiguredInstallPath(info.InstallPath);
                }
            }
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawHeader();
            EditorGUILayout.Space(8);
            DrawIntro();
            EditorGUILayout.Space(8);
            DrawGameDlls();
            EditorGUILayout.Space(8);
            DrawQuickStart();
            EditorGUILayout.Space(8);
            DrawActionButtons();
            EditorGUILayout.Space(8);
            DrawLinkButtons();
            EditorGUILayout.Space(12);

            EditorGUILayout.EndScrollView();

            DrawFooter();
        }

        // ---------------- header ----------------

        private void DrawHeader()
        {
            if (_headerBackground != null && _headerBackground.width > 0 && _headerBackground.height > 0 &&
                _headerLogo != null && _headerLogo.width > 0 && _headerLogo.height > 0)
            {
                const float minHeaderHeight = 140f;
                const float maxHeaderHeight = 240f;
                const float logoHeightFraction = 0.82f;

                var backgroundAspect = (float)_headerBackground.height / _headerBackground.width;
                var bannerHeight = Mathf.Clamp(position.width * backgroundAspect, minHeaderHeight, maxHeaderHeight);
                var bannerRect = GUILayoutUtility.GetRect(position.width, bannerHeight, GUILayout.ExpandWidth(true));

                GUI.DrawTexture(bannerRect, _headerBackground, ScaleMode.ScaleAndCrop, alphaBlend: true);

                var logoAspect = (float)_headerLogo.width / _headerLogo.height;
                var logoHeight = bannerRect.height * logoHeightFraction;
                var logoWidth = logoHeight * logoAspect;
                var maxLogoWidth = bannerRect.width * 0.92f;
                if (logoWidth > maxLogoWidth)
                {
                    logoWidth = maxLogoWidth;
                    logoHeight = logoWidth / logoAspect;
                }

                var logoRect = new Rect(
                    bannerRect.x + (bannerRect.width - logoWidth) * 0.5f,
                    bannerRect.y + (bannerRect.height - logoHeight) * 0.5f,
                    logoWidth,
                    logoHeight);
                GUI.DrawTexture(logoRect, _headerLogo, ScaleMode.ScaleToFit, alphaBlend: true);
                return;
            }

            const float placeholderHeight = 140f;
            var placeholderRect = GUILayoutUtility.GetRect(position.width, placeholderHeight, GUILayout.ExpandWidth(true));
            // Dark banner background so the placeholder reads as branded regardless of theme.
            EditorGUI.DrawRect(placeholderRect, new Color(0.09f, 0.10f, 0.12f, 1f));
            DrawTextPlaceholderHeader(placeholderRect);
        }

        private static void DrawTextPlaceholderHeader(Rect rect)
        {
            var accent = new Color(0.98f, 0.72f, 0.18f, 1f);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 28,
                normal = { textColor = accent },
            };
            var subtitleStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f, 1f) },
            };
            var hintStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                normal = { textColor = new Color(0.55f, 0.55f, 0.55f, 1f) },
            };

            var titleRect = new Rect(rect.x, rect.y + 28f, rect.width, 40f);
            var subtitleRect = new Rect(rect.x, rect.y + 70f, rect.width, 22f);
            var hintRect = new Rect(rect.x, rect.y + rect.height - 22f, rect.width, 18f);

            GUI.Label(titleRect, "BIG AMBITIONS", titleStyle);
            GUI.Label(subtitleRect, "Mod SDK", subtitleStyle);
            GUI.Label(hintRect, $"Add {HeaderBackgroundAssetPath} and {HeaderLogoAssetPath} to replace this placeholder.", hintStyle);
        }

        // ---------------- content ----------------

        private void DrawIntro()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var style = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 12 };
                EditorGUILayout.LabelField(
                    "Welcome to the official Big Ambitions Mod SDK project. Everything in this " +
                    "project is set up to help you build, validate, and package mods for Big Ambitions.",
                    style);
            }
        }

        private void DrawGameDlls()
        {
            EditorGUILayout.LabelField("Big Ambitions install", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawInstallPathRow();
                EditorGUILayout.Space(4);

                var status = GameDllImporter.GetStatus();
                DrawStatusRow(status);
                EditorGUILayout.Space(4);

                DrawImportButtonRow(status);

                if (!string.IsNullOrEmpty(_lastImportError))
                {
                    EditorGUILayout.Space(2);
                    EditorGUILayout.HelpBox(_lastImportError, MessageType.Error);
                }
            }
        }

        private void DrawInstallPathRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Path", GUILayout.Width(40));

                EditorGUI.BeginChangeCheck();
                var newPath = EditorGUILayout.TextField(_installPathField ?? string.Empty);
                if (EditorGUI.EndChangeCheck())
                {
                    _installPathField = newPath;
                    GameDllImporter.SetConfiguredInstallPath(newPath);
                    _lastImportError = null;
                }

                if (GUILayout.Button("Browse...", GUILayout.Width(80)))
                {
                    var picked = EditorUtility.OpenFolderPanel(
                        "Locate your Big Ambitions install",
                        !string.IsNullOrEmpty(_installPathField) ? _installPathField : string.Empty,
                        string.Empty);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        _installPathField = picked;
                        GameDllImporter.SetConfiguredInstallPath(picked);
                        _lastImportError = null;
                        GUI.FocusControl(null);
                    }
                }

                if (GUILayout.Button("Auto-detect", GUILayout.Width(90)))
                {
                    if (SteamInstallLocator.TrySteamAutoDetect(out var info))
                    {
                        _installPathField = info.InstallPath;
                        GameDllImporter.SetConfiguredInstallPath(info.InstallPath);
                        _lastImportError = null;
                    }
                    else
                    {
                        _lastImportError =
                            "Could not auto-detect Big Ambitions via Steam. Install it via Steam, or point the path at your install folder manually.";
                    }
                    GUI.FocusControl(null);
                }
            }
        }

        private static void DrawStatusRow(GameDllImporter.Status status)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var (color, message) = GetStatusVisuals(status);

                var dotRect = GUILayoutUtility.GetRect(14f, 14f, GUILayout.Width(14), GUILayout.Height(14));
                dotRect.y += 3f;
                dotRect.width = 10f;
                dotRect.height = 10f;
                EditorGUI.DrawRect(dotRect, color);

                var labelStyle = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 12 };
                EditorGUILayout.LabelField(message, labelStyle);
            }
        }

        private void DrawImportButtonRow(GameDllImporter.Status status)
        {
            var (label, enabled) = GetPrimaryButton(status);

            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (GUILayout.Button(label, GUILayout.Height(28)))
                {
                    TryImport(status.InstallPath);
                }
            }
        }

        private void TryImport(string installPath)
        {
            try
            {
                GameDllImporter.Import(installPath);
                _lastImportError = null;
            }
            catch (Exception ex)
            {
                _lastImportError = ex.Message;
                Debug.LogError($"[WelcomeWindow] DLL import failed: {ex}");
            }
            Repaint();
        }

        private static (Color dotColor, string message) GetStatusVisuals(GameDllImporter.Status s)
        {
            var red = new Color(0.85f, 0.25f, 0.25f, 1f);
            var amber = new Color(0.95f, 0.70f, 0.15f, 1f);
            var green = new Color(0.35f, 0.80f, 0.35f, 1f);

            var appLabel = $"Big Ambitions (Steam app {SteamInstallLocator.BigAmbitionsAppId})";

            switch (s.State)
            {
                case GameDllImporter.GameDllState.BigAmbitionsNotFound:
                    return (red, string.IsNullOrEmpty(s.InstallPath)
                        ? $"{appLabel} was not found. Click Auto-detect, or browse to the install folder manually."
                        : $"{appLabel} was not found at this path. Is it installed via Steam? Try Auto-detect, or browse to the correct folder.");
                case GameDllImporter.GameDllState.ReadyToImport:
                    var buildSuffix = string.IsNullOrEmpty(s.CurrentBuildId)
                        ? string.Empty
                        : $" — build id {s.CurrentBuildId}";
                    return (green,
                        $"Found {appLabel}{buildSuffix}. Ready to import {CanonicalGameDlls.All.Count} DLLs.");
                case GameDllImporter.GameDllState.UpdateAvailable:
                    return (amber,
                        $"Steam updated {appLabel} (build id {s.ImportedBuildId} -> {s.CurrentBuildId}). Re-import to stay in sync.");
                case GameDllImporter.GameDllState.UpToDate:
                    var currentText = string.IsNullOrEmpty(s.CurrentBuildId)
                        ? "your current install"
                        : $"build id {s.CurrentBuildId}";
                    return (green, $"All {s.ImportedDllCount} DLLs imported and match {currentText}.");
                default:
                    return (red, "Unknown state.");
            }
        }

        private static (string label, bool enabled) GetPrimaryButton(GameDllImporter.Status s)
        {
            switch (s.State)
            {
                case GameDllImporter.GameDllState.BigAmbitionsNotFound:
                    return ("Import DLLs from Steam", false);
                case GameDllImporter.GameDllState.ReadyToImport:
                    return ("Import DLLs from Steam", true);
                case GameDllImporter.GameDllState.UpdateAvailable:
                    return ($"Update DLLs from Steam (build {s.ImportedBuildId} -> {s.CurrentBuildId})", true);
                case GameDllImporter.GameDllState.UpToDate:
                    return ("Re-import DLLs", true);
                default:
                    return ("Import DLLs from Steam", false);
            }
        }

        private void DrawQuickStart()
        {
            EditorGUILayout.LabelField("Quick start", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawBullet("1.", "Create a folder under Assets/Mods/<YourModId>/ for each mod.");
                DrawBullet("2.", "Add a ModManifest asset (right-click in the folder) and an .asmdef.");
                DrawBullet("3.", "Author your scripts and scene content as normal.");
                DrawBullet("4.", "Open the Mod Builder (Big Ambitions/Mod Builder) to validate and package.");
                DrawBullet("5.", "Use \"Build + Install\" to drop the built mod into your local Big Ambitions install.");
            }
        }

        private static void DrawBullet(string prefix, string text)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(prefix, GUILayout.Width(22));
                EditorGUILayout.LabelField(text, EditorStyles.wordWrappedLabel);
            }
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Mod Builder", GUILayout.Height(28)))
                    EditorApplication.ExecuteMenuItem("Big Ambitions/Mod Builder");
                if (GUILayout.Button("Open Mods Folder", GUILayout.Height(28)))
                    RevealAssetFolder("Assets/Mods");
                if (GUILayout.Button("Open Output Folder", GUILayout.Height(28)))
                    RevealOutputFolder();
            }
        }

        private void DrawLinkButtons()
        {
            EditorGUILayout.LabelField("Resources", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Discord", GUILayout.Height(26)))
                    Application.OpenURL(DiscordUrl);
                if (GUILayout.Button("GitHub", GUILayout.Height(26)))
                    Application.OpenURL(GitHubUrl);
            }
        }

        // ---------------- footer ----------------

        private void DrawFooter()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var autoShow = EditorPrefs.GetBool(PrefAutoShowKey, true);
                var newAutoShow = GUILayout.Toggle(autoShow, " Show on startup", EditorStyles.toolbarButton);
                if (newAutoShow != autoShow)
                    EditorPrefs.SetBool(PrefAutoShowKey, newAutoShow);

                GUILayout.FlexibleSpace();

                var unityVersion = Application.unityVersion;
                GUILayout.Label($"Unity {unityVersion}   \u2022   BA Mod SDK", EditorStyles.miniLabel);
            }
        }

        // ---------------- helpers ----------------

        private static void RevealAssetFolder(string assetPath)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            var absolute = Path.Combine(projectRoot, assetPath);
            if (!Directory.Exists(absolute))
                Directory.CreateDirectory(absolute);
            EditorUtility.RevealInFinder(absolute);
        }

        private static void RevealOutputFolder()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            var absolute = Path.Combine(projectRoot, "Output");
            if (!Directory.Exists(absolute))
                Directory.CreateDirectory(absolute);
            EditorUtility.RevealInFinder(absolute);
        }

        // ---------------- auto-show bootstrap ----------------

        [InitializeOnLoad]
        private static class AutoShow
        {
            static AutoShow()
            {
                // delayCall so we don't fight Unity during the initial import/compile pass.
                EditorApplication.delayCall += TryShow;
            }

            private static void TryShow()
            {
                try
                {
                    if (Application.isBatchMode) return;
                    if (SessionState.GetBool(SessionShownKey, false)) return;

                    var autoShow = EditorPrefs.GetBool(PrefAutoShowKey, true);
                    var lastSeen = EditorPrefs.GetInt(PrefLastSeenVersionKey, 0);
                    var isNewVersion = lastSeen < WelcomeVersion;

                    if (!autoShow && !isNewVersion) return;

                    SessionState.SetBool(SessionShownKey, true);
                    ShowWindow();
                }
                catch (Exception ex)
                {
                    // Never let a branding popup break the editor.
                    Debug.LogWarning($"[BAModTemplate] Welcome window failed to open: {ex.Message}");
                }
            }
        }
    }
}
