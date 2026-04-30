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
    public enum BuildState
    {
        Idle,
        Queued,
        CompilingAssembly,
        BuildingBundles,
        Copying,
        Done,
        Failed,
    }

    public sealed class BuildJob
    {
        public BuildJob(DiscoveredMod mod, bool installAfterBuild, bool revealWhenDone)
        {
            Mod = mod;
            InstallAfterBuild = installAfterBuild;
            RevealWhenDone = revealWhenDone;
        }

        public DiscoveredMod Mod { get; }
        public bool InstallAfterBuild { get; }
        public bool RevealWhenDone { get; }

        public BuildState State { get; internal set; } = BuildState.Queued;
        public string StatusText { get; internal set; } = "Queued";
        public List<CompilerMessage> CompilerMessages { get; internal set; } = new();
        public List<string> Log { get; } = new();
        public DateTime StartedUtc { get; internal set; }
        public DateTime CompletedUtc { get; internal set; }
        public string OutputDirectoryAbsolute { get; internal set; } = string.Empty;

        public bool IsTerminal => State == BuildState.Done || State == BuildState.Failed;
    }

    /// <summary>
    /// Per-mod build pipeline. One AssemblyBuilder can run at a time in Unity, so this is a
    /// single-slot queue driven by <see cref="AssemblyBuilder.buildFinished"/> callbacks. The
    /// editor main thread never blocks.
    /// </summary>
    public static class ModPackager
    {
        private static readonly Queue<BuildJob> PendingJobs = new();
        private static BuildJob? _currentJob;
        private static AssemblyBuilder? _currentBuilder;

        public static event Action<BuildJob>? JobChanged;

        public static bool IsBusy => _currentJob != null;
        public static BuildJob? CurrentJob => _currentJob;
        public static IReadOnlyCollection<BuildJob> PendingQueue => PendingJobs.ToArray();

        /// <summary>Enqueue a build for a single mod.</summary>
        public static BuildJob Enqueue(DiscoveredMod mod, bool installAfterBuild = false, bool revealWhenDone = false)
        {
            var job = new BuildJob(mod, installAfterBuild, revealWhenDone);
            job.StartedUtc = DateTime.UtcNow;
            PendingJobs.Enqueue(job);
            RaiseJobChanged(job);
            TryStartNext();
            return job;
        }

        public static IReadOnlyList<BuildJob> EnqueueAll(IEnumerable<DiscoveredMod> mods, bool installAfterBuild = false)
        {
            var jobs = new List<BuildJob>();
            foreach (var mod in mods)
                jobs.Add(Enqueue(mod, installAfterBuild, revealWhenDone: false));
            return jobs;
        }

        private static void TryStartNext()
        {
            if (_currentJob != null) return;
            if (PendingJobs.Count == 0) return;

            var job = PendingJobs.Dequeue();
            _currentJob = job;
            try
            {
                StartCompileStep(job);
            }
            catch (Exception ex)
            {
                FailJob(job, $"Exception starting build: {ex.Message}");
            }
        }

        // ---------------- STEP 1: Player-mode recompile ----------------

        private static void StartCompileStep(BuildJob job)
        {
            var mod = job.Mod;
            if (mod.PlayerAssembly == null)
            {
                FailJob(job, $"Mod '{mod.Manifest.ModId}' has no Player-mode assembly (asmdef missing or excluded from build).");
                return;
            }

            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            var tempRoot = Path.Combine(projectRoot, "Temp", "ModBuilder", mod.Manifest.ModId);
            Directory.CreateDirectory(tempRoot);

            var tempDllPath = Path.Combine(tempRoot, mod.AsmdefName + ".dll");
            if (File.Exists(tempDllPath))
            {
                try { File.Delete(tempDllPath); } catch { /* ignore */ }
            }

            CompilationAssembly player = mod.PlayerAssembly;
            var references = BuildReferenceList(player);

            var builder = new AssemblyBuilder(tempDllPath, player.sourceFiles)
            {
                buildTarget = BuildTarget.StandaloneWindows64,
                buildTargetGroup = BuildTargetGroup.Standalone,
                flags = AssemblyBuilderFlags.None,
                additionalDefines = player.defines,
                additionalReferences = references,
                referencesOptions = ReferencesOptions.UseEngineModules,
                compilerOptions = new ScriptCompilerOptions
                {
                    CodeOptimization = CodeOptimization.Release,
                    // Unity 2022.3 LTS exposes this value as NET_Unity_4_8; older docs/versions call
                    // it NET_Framework / NET_4_6. All three are aliases for the .NET Framework 4.x
                    // profile that Big Ambitions ships with.
                    ApiCompatibilityLevel = ApiCompatibilityLevel.NET_Unity_4_8,
                },
            };

            builder.buildFinished += (outputPath, messages) =>
                OnCompileFinished(job, tempRoot, outputPath, messages);

            _currentBuilder = builder;
            Transition(job, BuildState.CompilingAssembly, $"Compiling {mod.AsmdefName}.dll (Player mode)...");

            if (!builder.Build())
            {
                _currentBuilder = null;
                FailJob(job, "AssemblyBuilder.Build() refused to start — another build may already be running.");
            }
        }

        private static string[] BuildReferenceList(CompilationAssembly player)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (player.compiledAssemblyReferences != null)
            {
                foreach (var r in player.compiledAssemblyReferences)
                    if (!string.IsNullOrEmpty(r)) set.Add(r);
            }
            if (player.assemblyReferences != null)
            {
                foreach (var r in player.assemblyReferences)
                    if (r != null && !string.IsNullOrEmpty(r.outputPath)) set.Add(r.outputPath);
            }
            return set.ToArray();
        }

        private static void OnCompileFinished(
            BuildJob job,
            string tempRoot,
            string compiledDllPath,
            CompilerMessage[] messages)
        {
            job.CompilerMessages = messages?.ToList() ?? new List<CompilerMessage>();
            _currentBuilder = null;

            // AssemblyBuilder.buildFinished runs while Unity is still unwinding its internal
            // compilation bookkeeping. Touching other editor systems immediately from this callback
            // can trip re-entrancy bugs in IsCompiling/IsAnyAssemblyBuilderCompiling, so continue
            // the packaging pipeline on the next editor tick instead.
            EditorApplication.delayCall += ContinueAfterCompile;

            void ContinueAfterCompile()
            {
                var errors = job.CompilerMessages
                    .Where(m => m.type == CompilerMessageType.Error)
                    .ToList();

                foreach (var e in errors)
                    job.Log.Add($"[error] {e.file}({e.line},{e.column}): {e.message}");

                if (errors.Count > 0)
                {
                    FailJob(job, $"Compile failed: {errors.Count} error(s).");
                    return;
                }

                if (!File.Exists(compiledDllPath))
                {
                    FailJob(job, $"Compiler reported success but output DLL not found: {compiledDllPath}");
                    return;
                }

                try
                {
                    StartBundleStep(job, tempRoot, compiledDllPath);
                }
                catch (Exception ex)
                {
                    FailJob(job, $"Bundle step failed: {ex.Message}");
                }
            }
        }

        // ---------------- STEP 2: per-platform AssetBundles ----------------

        private static void StartBundleStep(BuildJob job, string tempRoot, string compiledDllPath)
        {
            var mod = job.Mod;
            Transition(job, BuildState.BuildingBundles, "Building AssetBundles...");

            var bundleName = mod.Manifest.EffectiveAssetBundleName;
            if (string.IsNullOrWhiteSpace(bundleName))
            {
                FailJob(job, "Manifest.AssetBundleName is empty and ModId is empty; cannot derive bundle name.");
                return;
            }

            var assignedCount = EnsureModAssetsAssignedToBundle(mod, bundleName);
            if (assignedCount > 0)
                job.Log.Add($"[info] Assigned {assignedCount} mod asset(s) to bundle '{bundleName}'.");

            var assetPaths = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
            if (assetPaths == null || assetPaths.Length == 0)
            {
                job.Log.Add($"[info] No bundleable assets found under '{mod.ModFolderAssetPath}'; skipping AssetBundle build.");
                StartCopyStep(job, compiledDllPath, new Dictionary<string, string>());
                return;
            }

            var (baseName, variant) = SplitBundleName(bundleName);

            var build = new AssetBundleBuild
            {
                assetBundleName = baseName,
                assetBundleVariant = variant,
                assetNames = assetPaths,
            };

            var targets = ResolveTargets(mod.Manifest.TargetPlatforms);
            if (targets.Count == 0)
            {
                FailJob(job, "Manifest.TargetPlatforms has no platforms selected.");
                return;
            }

            var tempBundleRoot = Path.Combine(tempRoot, "AssetBundles");
            var producedFiles = new Dictionary<string, string>();

            foreach (var target in targets)
            {
                var platformFolder = PlatformFolderName(target);
                Transition(job, BuildState.BuildingBundles, $"Building AssetBundles for {platformFolder}...");

                var platformDir = Path.Combine(tempBundleRoot, platformFolder);
                Directory.CreateDirectory(platformDir);

                var manifest = BuildPipeline.BuildAssetBundles(
                    platformDir,
                    new[] { build },
                    BuildAssetBundleOptions.ChunkBasedCompression,
                    target);

                if (manifest == null)
                {
                    FailJob(job, $"AssetBundle build failed for target {target}. Check the Unity Console.");
                    return;
                }

                var bundleFileName = bundleName;
                var producedPath = Path.Combine(platformDir, bundleFileName);
                if (!File.Exists(producedPath))
                {
                    FailJob(job, $"AssetBundle output not found: {producedPath}");
                    return;
                }
                producedFiles[platformFolder] = producedPath;
            }

            try
            {
                StartCopyStep(job, compiledDllPath, producedFiles);
            }
            catch (Exception ex)
            {
                FailJob(job, $"Copy step failed: {ex.Message}");
            }
        }

        private static int EnsureModAssetsAssignedToBundle(DiscoveredMod mod, string fullBundleName)
        {
            var (baseName, variant) = SplitBundleName(fullBundleName);
            var changedCount = 0;

            foreach (var assetPath in FindBundleableModAssets(mod))
            {
                var importer = AssetImporter.GetAtPath(assetPath);
                if (importer == null)
                    continue;

                if (string.Equals(importer.assetBundleName, baseName, StringComparison.Ordinal)
                    && string.Equals(importer.assetBundleVariant, variant, StringComparison.Ordinal))
                    continue;

                importer.SetAssetBundleNameAndVariant(baseName, variant);
                importer.SaveAndReimport();
                changedCount++;
            }

            return changedCount;
        }

        private static IEnumerable<string> FindBundleableModAssets(DiscoveredMod mod)
        {
            var excludedPrefixes = new List<string>();
            if (mod.Manifest.LocalesFolder != null)
                excludedPrefixes.Add(NormaliseAssetPath(AssetDatabase.GetAssetPath(mod.Manifest.LocalesFolder)) + "/");

            var depsFolder = ModValidator.GetDependenciesFolderAssetPath(mod);
            if (!string.IsNullOrEmpty(depsFolder))
                excludedPrefixes.Add(NormaliseAssetPath(depsFolder) + "/");

            var enumsPath = mod.Manifest.EnumsFile != null
                ? NormaliseAssetPath(AssetDatabase.GetAssetPath(mod.Manifest.EnumsFile))
                : string.Empty;

            foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { mod.ModFolderAssetPath }))
            {
                var assetPath = NormaliseAssetPath(AssetDatabase.GUIDToAssetPath(guid));
                if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                    continue;

                if (string.Equals(assetPath, mod.ManifestAssetPath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(assetPath, mod.AsmdefAssetPath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(assetPath, enumsPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (excludedPrefixes.Any(prefix => assetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var extension = Path.GetExtension(assetPath);
                if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return assetPath;
            }
        }

        private static string NormaliseAssetPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return path!.Replace('\\', '/').TrimEnd('/');
        }

        private static (string name, string variant) SplitBundleName(string fullBundleName)
        {
            var idx = fullBundleName.LastIndexOf('.');
            if (idx <= 0 || idx >= fullBundleName.Length - 1) return (fullBundleName, string.Empty);
            return (fullBundleName.Substring(0, idx), fullBundleName.Substring(idx + 1));
        }

        private static IReadOnlyList<BuildTarget> ResolveTargets(ModTargetPlatforms flags)
        {
            var result = new List<BuildTarget>();
            if ((flags & ModTargetPlatforms.Windows) != 0) result.Add(BuildTarget.StandaloneWindows64);
            if ((flags & ModTargetPlatforms.Mac) != 0) result.Add(BuildTarget.StandaloneOSX);
            return result;
        }

        private static string PlatformFolderName(BuildTarget target) => target switch
        {
            BuildTarget.StandaloneWindows => "Windows",
            BuildTarget.StandaloneWindows64 => "Windows",
            BuildTarget.StandaloneOSX => "Mac",
            BuildTarget.StandaloneLinux64 => "Linux",
            _ => target.ToString(),
        };

        // ---------------- STEP 3: copy to Output/<ModId> ----------------

        private static void StartCopyStep(BuildJob job, string compiledDllPath, IReadOnlyDictionary<string, string> platformBundles)
        {
            var mod = job.Mod;
            Transition(job, BuildState.Copying, "Copying artefacts...");

            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            var outputDir = Path.Combine(projectRoot, "Output", mod.Manifest.ModId);
            if (Directory.Exists(outputDir))
            {
                try { Directory.Delete(outputDir, recursive: true); } catch { /* ignore */ }
            }
            Directory.CreateDirectory(outputDir);
            job.OutputDirectoryAbsolute = outputDir;

            File.Copy(compiledDllPath, Path.Combine(outputDir, mod.AsmdefName + ".dll"), overwrite: true);

            foreach (var (platformFolder, srcPath) in platformBundles)
            {
                var dstFolder = Path.Combine(outputDir, "AssetBundles", platformFolder);
                Directory.CreateDirectory(dstFolder);

                var fileName = Path.GetFileName(srcPath);
                File.Copy(srcPath, Path.Combine(dstFolder, fileName), overwrite: true);

                var manifestSrc = srcPath + ".manifest";
                if (File.Exists(manifestSrc))
                {
                    File.Copy(manifestSrc, Path.Combine(dstFolder, fileName + ".manifest"), overwrite: true);
                }
            }

            CopyDependencies(mod, outputDir);
            CopyLocales(mod, outputDir);
            CopyEnums(mod, outputDir);

            job.CompletedUtc = DateTime.UtcNow;

            if (job.InstallAfterBuild)
            {
                try
                {
                    ModInstaller.Install(mod, outputDir);
                    job.Log.Add($"[info] Installed to {ModInstaller.GetInstallRoot(mod)}.");
                }
                catch (Exception ex)
                {
                    FailJob(job, $"Install failed: {ex.Message}");
                    return;
                }
            }

            Transition(job, BuildState.Done, $"Done in {(job.CompletedUtc - job.StartedUtc).TotalSeconds:F1}s.");

            if (job.RevealWhenDone)
            {
                EditorUtility.RevealInFinder(outputDir);
            }

            CompleteCurrent();
        }

        private static void CopyDependencies(DiscoveredMod mod, string outputDir)
        {
            var depsAssetPath = ModValidator.GetDependenciesFolderAssetPath(mod);
            if (string.IsNullOrEmpty(depsAssetPath)) return;

            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            var depsAbsolute = Path.GetFullPath(Path.Combine(projectRoot, depsAssetPath));
            if (!Directory.Exists(depsAbsolute)) return;

            var dstDir = Path.Combine(outputDir, "Dependencies");
            Directory.CreateDirectory(dstDir);
            foreach (var file in Directory.EnumerateFiles(depsAbsolute, "*.dll", SearchOption.TopDirectoryOnly))
            {
                File.Copy(file, Path.Combine(dstDir, Path.GetFileName(file)), overwrite: true);
            }
        }

        private static void CopyLocales(DiscoveredMod mod, string outputDir)
        {
            if (mod.Manifest.LocalesFolder == null) return;
            var localesAssetPath = AssetDatabase.GetAssetPath(mod.Manifest.LocalesFolder);
            if (string.IsNullOrEmpty(localesAssetPath)) return;

            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            var localesAbsolute = Path.GetFullPath(Path.Combine(projectRoot, localesAssetPath));
            if (!Directory.Exists(localesAbsolute)) return;

            var dstDir = Path.Combine(outputDir, "Locales");
            Directory.CreateDirectory(dstDir);
            foreach (var file in Directory.EnumerateFiles(localesAbsolute, "*", SearchOption.TopDirectoryOnly))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(dstDir, Path.GetFileName(file)), overwrite: true);
            }
        }

        private static void CopyEnums(DiscoveredMod mod, string outputDir)
        {
            if (mod.Manifest.EnumsFile == null) return;
            var enumsAssetPath = AssetDatabase.GetAssetPath(mod.Manifest.EnumsFile);
            if (string.IsNullOrEmpty(enumsAssetPath)) return;

            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            var enumsAbsolute = Path.GetFullPath(Path.Combine(projectRoot, enumsAssetPath));
            if (!File.Exists(enumsAbsolute)) return;

            File.Copy(enumsAbsolute, Path.Combine(outputDir, "enums.txt"), overwrite: true);
        }

        // ---------------- state transitions ----------------

        private static void Transition(BuildJob job, BuildState state, string status)
        {
            job.State = state;
            job.StatusText = status;
            job.Log.Add($"[{state}] {status}");
            RaiseJobChanged(job);
        }

        private static void FailJob(BuildJob job, string reason)
        {
            job.State = BuildState.Failed;
            job.StatusText = "Failed: " + reason;
            job.CompletedUtc = DateTime.UtcNow;
            job.Log.Add("[error] " + reason);
            RaiseJobChanged(job);
            CompleteCurrent();
        }

        private static void CompleteCurrent()
        {
            _currentJob = null;
            _currentBuilder = null;
            EditorApplication.delayCall += TryStartNext;
        }

        private static void RaiseJobChanged(BuildJob job)
        {
            try { JobChanged?.Invoke(job); } catch { /* ignore listener errors */ }
        }
    }
}
