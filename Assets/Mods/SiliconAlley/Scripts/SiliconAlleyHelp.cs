#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using Localizor;
// The game type `HelpSystem` lives in the GLOBAL namespace, while the help entry structs live in the
// namespace `UnityEngine.UI.Extensions.HelpSystem` — importing that namespace would shadow the type with
// the same simple name. So we DON'T `using` it; we alias just the two structs and reference the global
// `HelpSystem` type directly.
using GroupEntry = UnityEngine.UI.Extensions.HelpSystem.HelpStructureGroupEntry;
using PageEntry = UnityEngine.UI.Extensions.HelpSystem.HelpStructurePageEntry;

[assembly: RegisterModClass(typeof(SiliconAlleyHelpHotkeyHost))]

// Issue #64 (epic #62 — in-game help): the FOUNDATION that puts Silicon Alley into Big Ambitions' native
// Help System. BA exposes no Mod-API help hook — the sidebar structure lives in a private
// `HelpSystem._categories` list (deserialized from a base-game `helpstructure.json`) and is rebuilt by a
// private `LoadCategories()`. We REFLECT into those two members to append the mod's pages, then rebuild.
//
// Page CONTENT, by contrast, is fully reachable with no reflection: the native renderer resolves a page
// body from the `help_{slug}_content` localization key, and `HelpSystem.OpenLink(slug)` renders ANY slug
// even when it isn't in the sidebar. So OpenPage() below is the guaranteed path; the sidebar injection is
// best-effort polish on top.
//
// This issue is the MECHANISM only — the real page copy is owned by sub-issues (#69 business types + goods;
// #65 / #66 / #67 systems & getting-started) and discoverability (phone / hotkey / first-run nudge) by #68.
// Sub-issues extend the `Pages` table here and add the matching bare `en.json` keys — no mechanism change.
//
// Presentation only: no modData, no enums, no save-compat surface. Every game-internals touch is wrapped so
// a future game update that renames the private members degrades to a safe no-op (content still opens).
public static class SiliconAlleyHelp
{
    // Display label of the dedicated category (en.json: "siliconalley-help-category" -> "Silicon Alley").
    private const string ModCategoryKey = "siliconalley-help-category";

    // Base-game category keys (from helpstructure.json) we append into. Match-tolerant: if a key can't be
    // found on a future game update, the page falls back to the dedicated "Silicon Alley" category.
    private const string BusinessTypesCategoryKey = "common_business_types";   // "Business Types"
    private const string GoodsCategoryKey = "common_sellable_products";        // "Goods and Services"

    private const string OverviewSlug = "siliconalley-overview";

    // The mod's help pages. Slug == PageLocalizorKeyPrefix on purpose, so a single content key
    // (help_{slug}_content) and a single title key ({slug}) serve BOTH the sidebar click and a direct
    // OpenPage(slug). Sub-issues append rows here (+ the en.json keys) to add pages.
    private static readonly (string Slug, string CategoryKey)[] Pages =
    {
        // Business types -> native "Business Types" group (bodies fleshed out by #69).
        ("siliconalley-softwarestudio", BusinessTypesCategoryKey),
        ("siliconalley-cybersecurity",  BusinessTypesCategoryKey),
        ("siliconalley-gamestudio",     BusinessTypesCategoryKey),
        // Goods -> native "Goods and Services" group (#69).
        ("siliconalley-softwarelicense", GoodsCategoryKey),
        ("siliconalley-securityaudit",   GoodsCategoryKey),
        ("siliconalley-videogame",       GoodsCategoryKey),
        // Overview + system pages -> dedicated "Silicon Alley" category (#66 / #67 still to come).
        (OverviewSlug, ModCategoryKey),
        ("siliconalley-getting-started", ModCategoryKey),   // #67 — new-player walkthrough
        ("siliconalley-wizard", ModCategoryKey),   // #65 — the design-wizard guide
        // #66 — economy & market system pages.
        ("siliconalley-contracts",     ModCategoryKey),
        ("siliconalley-market-demand", ModCategoryKey),
        ("siliconalley-marketing",     ModCategoryKey),
        ("siliconalley-publishers",    ModCategoryKey),
        ("siliconalley-lifecycle",     ModCategoryKey),
        ("siliconalley-bugs-reviews",  ModCategoryKey),
    };

    private static FieldInfo? _categoriesField;
    private static MethodInfo? _loadCategoriesMethod;
    private static bool _languageHookAdded;
    private static IModLogger? _logger;
    private static bool _errorLogged;

    public static string LastError { get; private set; } = "";

    // Issue #68: the hotkey that opens the help overview (legacy Input, like the project/dashboard screens).
    // Rebindable via the options panel (machine-local); KeyChoices is the dropdown's index→KeyCode mapping.
    // Polled by SiliconAlleyHelpHotkey.Update (created on city load by SiliconAlleyHelpHotkeyHost below).
    public static KeyCode HotKey = KeyCode.F1;
    public static readonly KeyCode[] KeyChoices =
        { KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.Tab, KeyCode.BackQuote };

    // Idempotent: inject the mod's pages into HelpSystem's sidebar and rebuild it. Safe to call any number
    // of times and before the help UI exists (no-op until HelpSystem is alive — a later city-load /
    // language-change / OpenPage call re-attempts). Never throws.
    public static void EnsureRegistered(ModContext context)
    {
        _logger = context.Logger;
        try
        {
            Inject();
        }
        catch (Exception ex)
        {
            Fail("EnsureRegistered", ex.Message);
        }
    }

    // The issue's OpenLink(slug) helper / guaranteed entry point (discoverability wiring is #68). Opens the
    // native Help window to a mod page. A sidebar-injection hiccup never blocks the open — page bodies render
    // from help_{slug}_content regardless. Returns false only if the Help system itself isn't available yet.
    public static bool OpenPage(string slug)
    {
        // Best-effort: make sure the sidebar is populated, but don't let it block the open.
        try { Inject(); }
        catch (Exception ex) { Fail("OpenPage.inject", ex.Message); }

        try
        {
            var help = HelpSystem.Instance;
            if (help == null)
                return false;
            help.Toggle(true, slug);
            return true;
        }
        catch (Exception ex)
        {
            Fail("OpenPage.open", ex.Message);
            return false;
        }
    }

    // Convenience for #68 (phone client / hotkey / first-run nudge).
    public static bool OpenOverview() => OpenPage(OverviewSlug);

    // Mirror of the registry's UnregisterAll: drop our language-change subscription on mod unload so a
    // disable/re-enable doesn't double-subscribe and our handler never fires after we're gone. The game's
    // own injected categories are rebuilt from disk on its next ReloadHelpStructure, so no cleanup needed.
    public static void Unregister()
    {
        if (!_languageHookAdded)
            return;
        try
        {
            LocalizorManager.OnLanguageChanged = (Action)Delegate.Remove(
                LocalizorManager.OnLanguageChanged, new Action(OnLanguageChanged));
        }
        catch
        {
            // Best-effort cleanup — never throw on unload.
        }
        _languageHookAdded = false;
    }

    private static void OnLanguageChanged()
    {
        try { Inject(); }
        catch (Exception ex) { Fail("OnLanguageChanged", ex.Message); }
    }

    // The reflection core. Callers wrap this in try/catch.
    private static void Inject()
    {
        var help = HelpSystem.Instance;
        if (help == null)
            return; // Not in a game yet; re-attempted on city load / language change / OpenPage.

        _categoriesField ??= typeof(HelpSystem).GetField(
            "_categories", BindingFlags.NonPublic | BindingFlags.Instance);
        _loadCategoriesMethod ??= typeof(HelpSystem).GetMethod(
            "LoadCategories", BindingFlags.NonPublic | BindingFlags.Instance);

        if (_categoriesField == null || _loadCategoriesMethod == null)
        {
            Fail("Inject", "HelpSystem internals not found (_categories / LoadCategories).");
            return;
        }

        if (_categoriesField.GetValue(help) is not List<GroupEntry> categories)
            return; // Structure not deserialized yet (HelpSystem.Start hasn't run ReloadHelpStructure).

        // Subscribe ONLY now that _categories is populated. A populated list means HelpSystem.Start already
        // ran — and therefore already added its own ReloadHelpStructure to OnLanguageChanged. Subscribing
        // after it guarantees our re-inject fires LAST on a language change (multicast delegates invoke in
        // subscription order), so ReloadHelpStructure wipes the structure first and we re-apply onto the
        // freshly reloaded list. Subscribing earlier would let our entries get wiped right after we add them.
        if (!_languageHookAdded)
        {
            LocalizorManager.OnLanguageChanged = (Action)Delegate.Combine(
                LocalizorManager.OnLanguageChanged, new Action(OnLanguageChanged));
            _languageHookAdded = true;
        }

        var changed = false;

        foreach (var (slug, categoryKey) in Pages)
        {
            // Destination group: the named base-game group, else the dedicated "Silicon Alley" category.
            var group = FindGroup(categories, categoryKey) ?? EnsureModCategory(categories, ref changed);

            group.Pages ??= new List<PageEntry>();
            if (group.Pages.Any(p => p != null && p.Slug == slug))
                continue; // Already injected — idempotent, no duplicates.

            group.Pages.Add(new PageEntry { Slug = slug, PageLocalizorKeyPrefix = slug });
            changed = true;
        }

        if (changed)
        {
            _loadCategoriesMethod.Invoke(help, null);
            _errorLogged = false;
            LastError = "";
        }
    }

    private static GroupEntry? FindGroup(List<GroupEntry> categories, string categoryKey)
        => categories.FirstOrDefault(c => c != null && c.CategoryLocalizorKey == categoryKey);

    private static GroupEntry EnsureModCategory(List<GroupEntry> categories, ref bool changed)
    {
        var group = FindGroup(categories, ModCategoryKey);
        if (group == null)
        {
            group = new GroupEntry { CategoryLocalizorKey = ModCategoryKey, Pages = new List<PageEntry>() };
            categories.Add(group);
            changed = true;
        }
        return group;
    }

    private static void Fail(string where, string message)
    {
        LastError = where + ": " + message;
        if (_errorLogged)
            return;
        _errorLogged = true;
        _logger?.Error("SiliconAlley: help integration failed (" + LastError +
                       "). Sidebar skipped; page content still opens via OpenPage.");
    }
}

// Issue #68: a persistent input listener that opens the help overview on the configurable hotkey
// (SiliconAlleyHelp.HotKey, default F1). Created on city load — mirrors the project-screen/dashboard host
// pattern (SiliconAlleyProjectScreenMod) — so its Update() polls the key from city load onward, regardless
// of whether any mod window is open. Presentation only; no save-compat surface.
[ModEntryOnCityLoad]
public class SiliconAlleyHelpHotkeyHost : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    private GameObject _host;

    public Task OnLoadAsync(ModContext context)
    {
        if (SiliconAlleyHelpHotkey.Instance == null)
        {
            _host = new GameObject("SiliconAlleyHelpHotkey");
            _host.AddComponent<SiliconAlleyHelpHotkey>();
        }
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        if (_host != null)
            UnityEngine.Object.Destroy(_host);
        _host = null;
        return Task.CompletedTask;
    }
}

public class SiliconAlleyHelpHotkey : MonoBehaviour
{
    public static SiliconAlleyHelpHotkey Instance { get; private set; }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (Input.GetKeyDown(SiliconAlleyHelp.HotKey))
            SiliconAlleyHelp.OpenOverview();
    }
}
