#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace BAModTemplate.Editor
{
    public enum Severity
    {
        Info,
        Warning,
        Error,
    }

    public readonly struct ValidationIssue
    {
        public ValidationIssue(Severity severity, string message, Action? quickFix = null, string? quickFixLabel = null)
        {
            Severity = severity;
            Message = message;
            QuickFix = quickFix;
            QuickFixLabel = quickFixLabel;
        }

        public Severity Severity { get; }
        public string Message { get; }
        public Action? QuickFix { get; }
        public string? QuickFixLabel { get; }
    }

    /// <summary>
    /// Fast, pure validation rules that can run on inspector repaint and builder-window refresh.
    /// Expensive checks (Player-compile dry run, AssetBundle build) belong in <c>ModPackager</c>.
    /// </summary>
    public static class ModValidator
    {
        private const string ModApiInterfaceFullName = "BAModAPI.IModBigAmbitions";
        private const string RegisterModClassAttributeFullName = "BAModAPI.RegisterModClassAttribute";

        public static IReadOnlyList<ValidationIssue> Validate(
            DiscoveredMod mod,
            IReadOnlyList<DiscoveredMod> allMods)
        {
            var issues = new List<ValidationIssue>();

            RuleManifestLocation(mod, issues);
            RuleUniqueModId(mod, allMods, issues);
            RuleUniqueAsmdefName(mod, allMods, issues);
            RuleAsmdefPresent(mod, issues);
            RuleEditorCompiledDllExists(mod, issues);
            RuleCanonicalPrecompiledDrift(mod, issues);
            RuleRegisterModClass(mod, issues);
            RuleBundleScoping(mod, issues);
            RuleMacBuildSupport(mod, issues);
            RuleRelativeAssetBundlePathsConvention(mod, issues);
            RuleEnumsTxtSyntax(mod, issues);
            RuleDependenciesFolderShape(mod, issues);
            RuleLocalesFolderShape(mod, issues);

            return issues;
        }

        public static Severity MaxSeverity(IReadOnlyList<ValidationIssue> issues)
        {
            var max = Severity.Info;
            foreach (var issue in issues)
                if (issue.Severity > max) max = issue.Severity;
            return max;
        }

        // -------------- rules ----------------

        private static void RuleManifestLocation(DiscoveredMod mod, List<ValidationIssue> issues)
        {
            var folderAssetPath = mod.ModFolderAssetPath;
            if (!ModDiscovery.IsDirectlyUnderModsRoot(folderAssetPath))
            {
                issues.Add(new ValidationIssue(Severity.Error,
                    $"Manifest is at '{mod.ManifestAssetPath}'; it must live directly under '{ModDiscovery.ModsRootAssetPath}/<ModId>/'."));
                return;
            }

            var folderName = Path.GetFileName(folderAssetPath);
            if (!string.Equals(folderName, mod.Manifest.ModId, StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue(Severity.Error,
                    $"Folder name '{folderName}' does not match ModId '{mod.Manifest.ModId}'. Rename one to match."));
            }

            if (string.IsNullOrWhiteSpace(mod.Manifest.ModId))
            {
                issues.Add(new ValidationIssue(Severity.Error,
                    "ModId is empty on the manifest."));
            }
        }

        private static void RuleUniqueModId(
            DiscoveredMod mod,
            IReadOnlyList<DiscoveredMod> allMods,
            List<ValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(mod.Manifest.ModId)) return;
            var dupes = allMods
                .Where(m => !IsSameMod(m, mod) &&
                    string.Equals(m.Manifest.ModId, mod.Manifest.ModId, StringComparison.Ordinal))
                .ToList();
            if (dupes.Count > 0)
            {
                issues.Add(new ValidationIssue(Severity.Error,
                    $"ModId '{mod.Manifest.ModId}' is not unique; duplicated by: " +
                    string.Join(", ", dupes.Select(d => d.ManifestAssetPath)) + "."));
            }
        }

        // A given manifest asset can be represented by more than one DiscoveredMod instance
        // (the inspector builds its own, the builder window builds another). Identify by the
        // underlying manifest asset path so the uniqueness rules don't flag the mod against
        // itself.
        private static bool IsSameMod(DiscoveredMod a, DiscoveredMod b)
        {
            if (ReferenceEquals(a, b)) return true;
            return !string.IsNullOrEmpty(a.ManifestAssetPath)
                && string.Equals(a.ManifestAssetPath, b.ManifestAssetPath, StringComparison.OrdinalIgnoreCase);
        }

        private static void RuleUniqueAsmdefName(
            DiscoveredMod mod,
            IReadOnlyList<DiscoveredMod> allMods,
            List<ValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(mod.AsmdefName)) return;
            var dupes = allMods
                .Where(m => !IsSameMod(m, mod) &&
                    string.Equals(m.AsmdefName, mod.AsmdefName, StringComparison.Ordinal))
                .ToList();
            if (dupes.Count > 0)
            {
                issues.Add(new ValidationIssue(Severity.Error,
                    $"Asmdef name '{mod.AsmdefName}' is duplicated by other mods. Each mod DLL filename must be unique."));
            }
        }

        private static void RuleAsmdefPresent(DiscoveredMod mod, List<ValidationIssue> issues)
        {
            if (mod.Manifest.ModAssembly == null)
            {
                if (string.IsNullOrEmpty(mod.AsmdefAssetPath))
                {
                    issues.Add(new ValidationIssue(Severity.Error,
                        "Manifest has no ModAssembly reference and no .asmdef was found in the mod folder. " +
                        "Drag the mod's .asmdef into the ModAssembly field in the inspector."));
                }
                else
                {
                    issues.Add(new ValidationIssue(Severity.Info,
                        $"Manifest ModAssembly is unassigned; auto-detected '{mod.AsmdefName}' from the mod folder. " +
                        "Drag it into the ModAssembly field to make the link explicit."));
                }
                return;
            }

            if (string.IsNullOrEmpty(mod.AsmdefAssetPath))
            {
                issues.Add(new ValidationIssue(Severity.Error,
                    "ModAssembly is assigned but its asset path could not be resolved."));
            }
        }

        private static void RuleEditorCompiledDllExists(DiscoveredMod mod, List<ValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(mod.EditorCompiledDllPath)) return;
            if (!File.Exists(mod.EditorCompiledDllPath))
            {
                issues.Add(new ValidationIssue(Severity.Error,
                    $"Editor-compiled DLL '{Path.GetFileName(mod.EditorCompiledDllPath)}' is missing. " +
                    "The asmdef likely failed to compile — check the Unity Console."));
            }
        }

        private static void RuleCanonicalPrecompiledDrift(DiscoveredMod mod, List<ValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(mod.AsmdefAssetPath)) return;
            AsmdefFile asmdef;
            try
            {
                asmdef = AsmdefFile.Load(Path.GetFullPath(mod.AsmdefAssetPath));
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue(Severity.Error,
                    $"Failed to parse asmdef: {ex.Message}"));
                return;
            }

            // Mod asmdefs must opt out of auto-reference — otherwise a mod would not compile at
            // all (game DLLs have isExplicitlyReferenced: 1 project-wide to keep Unity packages
            // isolated from them).
            var needsOverride = !asmdef.OverrideReferences;
            var missing = CanonicalGameDlls.All
                .Where(dll => !asmdef.PrecompiledReferences.Contains(dll, StringComparer.Ordinal))
                .ToList();

            if (!needsOverride && missing.Count == 0) return;

            var messageParts = new List<string>();
            if (needsOverride)
                messageParts.Add("'overrideReferences' is false (must be true so the game DLLs are explicitly linked)");
            if (missing.Count > 0)
                messageParts.Add("missing canonical game DLL refs: " + string.Join(", ", missing));

            issues.Add(new ValidationIssue(
                Severity.Error,
                $"Asmdef is not aligned with the canonical game DLL list — " + string.Join("; ", messageParts) + ".",
                quickFixLabel: "Sync Asmdef",
                quickFix: () =>
                {
                    var fresh = AsmdefFile.Load(asmdef.AbsolutePath);
                    fresh.OverrideReferences = true;
                    var refs = fresh.PrecompiledReferences;
                    foreach (var dll in CanonicalGameDlls.All)
                        if (!refs.Contains(dll, StringComparer.Ordinal))
                            refs.Add(dll);
                    fresh.PrecompiledReferences = refs;
                    fresh.Save();
                    AssetDatabase.ImportAsset(mod.AsmdefAssetPath);
                }));
        }

        private static void RuleRegisterModClass(DiscoveredMod mod, List<ValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(mod.AsmdefName)) return;

            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, mod.AsmdefName, StringComparison.Ordinal));
            if (assembly == null)
            {
                // Missing-DLL already reported by RuleEditorCompiledDllExists.
                return;
            }

            var regAttrs = assembly.GetCustomAttributes(inherit: false)
                .Where(a => string.Equals(a.GetType().FullName, RegisterModClassAttributeFullName, StringComparison.Ordinal))
                .ToArray();
            if (regAttrs.Length == 0)
            {
                issues.Add(new ValidationIssue(Severity.Error,
                    $"Assembly '{mod.AsmdefName}' is missing [assembly: RegisterModClass(typeof(...))]. " +
                    "Without it the runtime loader cannot discover the mod class."));
                return;
            }

            foreach (var attr in regAttrs)
            {
                var t = attr.GetType().GetProperty("ModClassType")?.GetValue(attr) as Type;
                if (t == null)
                {
                    issues.Add(new ValidationIssue(Severity.Error,
                        $"RegisterModClass attribute in '{mod.AsmdefName}' has no type argument."));
                    continue;
                }

                if (!ImplementsInterface(t, ModApiInterfaceFullName))
                {
                    issues.Add(new ValidationIssue(Severity.Error,
                        $"Registered type '{t.FullName}' does not implement IModBigAmbitions."));
                }

                // Runtime uses ModEntryOn* (city, init, intro, …) and ModEntryMainMenu for main menu.
                var hasLifecycleAttr = t.GetCustomAttributes(inherit: false)
                    .Any(a => a.GetType().Name.StartsWith("ModEntryOn", StringComparison.Ordinal) ||
                              a.GetType().Name.StartsWith("ModEntryMain", StringComparison.Ordinal));
                if (!hasLifecycleAttr)
                {
                    issues.Add(new ValidationIssue(Severity.Warning,
                        $"Mod class '{t.FullName}' has no [ModEntryOn*] attribute; it will never be loaded by the runtime."));
                }
            }
        }

        private static void RuleBundleScoping(DiscoveredMod mod, List<ValidationIssue> issues)
        {
            var bundleName = mod.Manifest.EffectiveAssetBundleName;
            if (string.IsNullOrWhiteSpace(bundleName))
            {
                issues.Add(new ValidationIssue(Severity.Error,
                    "Manifest.AssetBundleName is empty and ModId is empty; cannot derive a bundle name."));
                return;
            }

            var paths = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
            if (paths == null || paths.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(mod.Manifest.AssetBundleName))
                    issues.Add(new ValidationIssue(Severity.Error,
                        $"No assets are assigned to asset bundle '{bundleName}'. Set AssetBundleName on at least one asset under '{mod.ModFolderAssetPath}/'."));
                return;
            }

            var modFolder = mod.ModFolderAssetPath.TrimEnd('/') + "/";
            foreach (var path in paths)
            {
                if (!path.Replace('\\', '/').StartsWith(modFolder, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ValidationIssue(Severity.Error,
                        $"Asset '{path}' is in bundle '{bundleName}' but lives outside '{mod.ModFolderAssetPath}/'. Move it into the mod folder or change its bundle assignment."));
                }
            }
        }

        private static void RuleMacBuildSupport(DiscoveredMod mod, List<ValidationIssue> issues)
        {
            if ((mod.Manifest.TargetPlatforms & ModTargetPlatforms.Mac) == 0) return;

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
            {
                issues.Add(new ValidationIssue(Severity.Error,
                    "Mod targets Mac but 'Mac Build Support (Mono)' is not installed. " +
                    "Open Unity Hub → Installs → modify your editor install → add the Mac module. " +
                    "Alternatively, clear 'Mac' from the manifest's TargetPlatforms."));
            }
        }

        private static void RuleRelativeAssetBundlePathsConvention(DiscoveredMod mod, List<ValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(mod.AsmdefName)) return;

            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, mod.AsmdefName, StringComparison.Ordinal));
            if (assembly == null) return;

            var modType = assembly.GetTypes().FirstOrDefault(
                t => !t.IsAbstract && ImplementsInterface(t, ModApiInterfaceFullName));
            if (modType == null) return;

            object? instance;
            try
            {
                instance = Activator.CreateInstance(modType);
            }
            catch
            {
                return;
            }

            var bundlePathsProperty = modType.GetProperty("RelativeAssetBundlePaths");
            if (bundlePathsProperty == null) return;

            string[] paths;
            try
            {
                paths = (bundlePathsProperty.GetValue(instance) as IEnumerable<string>)?.ToArray()
                    ?? Array.Empty<string>();
            }
            catch
            {
                return;
            }

            foreach (var p in paths)
            {
                var normalised = p.Replace('\\', '/');
                if (normalised.Contains("/Windows/", StringComparison.OrdinalIgnoreCase) ||
                    normalised.Contains("/Mac/", StringComparison.OrdinalIgnoreCase) ||
                    normalised.Contains("/Linux/", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ValidationIssue(Severity.Warning,
                        $"RelativeAssetBundlePath '{p}' contains a platform segment. " +
                        "Use the flat path (e.g. 'AssetBundles/example-furniture.unity3d'); the runtime loader inserts the platform segment."));
                }
            }
        }

        private static void RuleEnumsTxtSyntax(DiscoveredMod mod, List<ValidationIssue> issues)
        {
            if (mod.Manifest.EnumsFile == null) return;
            var text = mod.Manifest.EnumsFile.text ?? string.Empty;
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var raw = lines[i].Trim();
                if (raw.Length == 0 || raw.StartsWith("#", StringComparison.Ordinal)) continue;

                if (!System.Text.RegularExpressions.Regex.IsMatch(raw, "^\\w+(\\.\\w+)+$"))
                {
                    issues.Add(new ValidationIssue(Severity.Warning,
                        $"enums.txt line {i + 1} ('{raw}') is not of the form 'Namespace.EnumName'."));
                }
            }
        }

        private static void RuleDependenciesFolderShape(DiscoveredMod mod, List<ValidationIssue> issues)
        {
            var depsFolderAssetPath = GetDependenciesFolderAssetPath(mod);
            if (string.IsNullOrEmpty(depsFolderAssetPath)) return;

            var absolute = AssetPathToAbsolute(depsFolderAssetPath);
            if (!Directory.Exists(absolute)) return;

            foreach (var sub in Directory.GetDirectories(absolute))
            {
                issues.Add(new ValidationIssue(Severity.Error,
                    $"Dependencies folder must be flat; nested folder found: '{Path.GetFileName(sub)}'."));
            }

            foreach (var file in Directory.GetFiles(absolute))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ValidationIssue(Severity.Error,
                        $"Non-DLL file in Dependencies/: '{name}'. Only managed .dll files are allowed."));
                }
            }
        }

        private static void RuleLocalesFolderShape(DiscoveredMod mod, List<ValidationIssue> issues)
        {
            if (mod.Manifest.LocalesFolder == null) return;
            var folderAssetPath = AssetDatabase.GetAssetPath(mod.Manifest.LocalesFolder);
            if (string.IsNullOrEmpty(folderAssetPath)) return;

            var absolute = AssetPathToAbsolute(folderAssetPath);
            if (!Directory.Exists(absolute))
            {
                issues.Add(new ValidationIssue(Severity.Error,
                    $"LocalesFolder '{folderAssetPath}' does not exist on disk."));
                return;
            }

            var hasLocaleFile = Directory.EnumerateFiles(absolute)
                .Any(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));
            if (!hasLocaleFile)
            {
                issues.Add(new ValidationIssue(Severity.Warning,
                    $"LocalesFolder '{folderAssetPath}' is empty."));
            }
        }

        // -------------- helpers ----------------

        internal static string GetDependenciesFolderAssetPath(DiscoveredMod mod)
        {
            if (mod.Manifest.DependenciesFolder != null)
                return AssetDatabase.GetAssetPath(mod.Manifest.DependenciesFolder);
            // Convention fallback: Assets/Mods/<ModId>/Dependencies
            var conventional = mod.ModFolderAssetPath + "/Dependencies";
            return AssetDatabase.IsValidFolder(conventional) ? conventional : string.Empty;
        }

        private static string AssetPathToAbsolute(string assetPath)
        {
            var projectRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath) ?? string.Empty;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static bool ImplementsInterface(Type type, string interfaceFullName)
            => type.GetInterfaces().Any(i => string.Equals(i.FullName, interfaceFullName, StringComparison.Ordinal));
    }
}
