using System.Collections.Generic;

namespace BAModTemplate.Editor
{
    /// <summary>
    /// Single source-of-truth list of precompiled game DLLs shipped under
    /// <c>Assets/_BaDependencies/GameDlls/</c>.
    ///
    /// Every mod asmdef must set <c>overrideReferences: true</c> and list every entry from
    /// <see cref="All"/> under <c>precompiledReferences</c>. This is because the game DLLs are
    /// <b>not</b> auto-referenced project-wide (<c>isExplicitlyReferenced: 1</c> in every
    /// <c>*.dll.meta</c>) — if they were, every Unity package assembly in the project
    /// (<c>com.unity.visualscripting</c>, <c>addressables</c>, <c>services.core</c>, …)
    /// would pull them in and produce type collisions (e.g.
    /// <c>BigAmbitions.Legacy.PlayerPref</c> vs Unity's <c>PlayerPrefs</c>).
    ///
    /// When a mod adds a local dependency under <c>Dependencies/</c>, the dependency's DLL
    /// filename is appended to the same <c>precompiledReferences</c> list; the canonical game
    /// DLL list stays in place.
    ///
    /// <c>NaughtyAttributes.Core.dll</c> is included here because several Big Ambitions assemblies
    /// reference it directly. Unity's API Updater needs the physical DLL present under
    /// <c>Assets/_BaDependencies/GameDlls/</c> while reimporting those assemblies, otherwise the
    /// importer can stall on an unresolved assembly dependency.
    /// </summary>
    public static class CanonicalGameDlls
    {
        /// <summary>
        /// Filename suffix required on precompiled references.
        /// </summary>
        public const string Extension = ".dll";

        /// <summary>
        /// Ordered, alphabetised list of game DLL filenames (including <c>.dll</c>).
        /// Matches the files under <c>Assets/_BaDependencies/GameDlls/</c>.
        /// </summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            "BehaviorDesigner.Runtime.dll",
            "BigAmbitions.AI.dll",
            "BigAmbitions.Characters.dll",
            "BigAmbitions.DebugMode.dll",
            "BigAmbitions.dll",
            "BigAmbitions.Factories.dll",
            "BigAmbitions.GameAnalytics.dll",
            "BigAmbitions.InputSystem.dll",
            "BigAmbitions.InteriorDesigner.dll",
            "BigAmbitions.Items.dll",
            "BigAmbitions.Legacy.dll",
            "BigAmbitions.ModAPI.dll",
            "BigAmbitions.ModsInternal.dll",
            "BigAmbitions.Neighborhoods.dll",
            "BigAmbitions.PlacementSystem.dll",
            "BigAmbitions.Seasons.dll",
            "BigAmbitions.SoundSystem.dll",
            "DayNightCycle.dll",
            "DOTween.dll",
            "DOTween.Modules.dll",
            "ExternalPlugins.dll",
            "Facepunch.Steamworks.Win64.dll",
            "Google.OrTools.dll",
            "Google.Protobuf.dll",
            "HBAO.HighDefinition.Runtime.dll",
            "HGExtensions.dll",
            "HGPlugins.dll",
            "JimmysUnityUtilities.dll",
            "NaughtyAttributes.Core.dll",
            "OdinSerializer.dll",
            "System.Runtime.CompilerServices.Unsafe.dll",
            "UnityUIExtensions.dll",
        };
    }
}
