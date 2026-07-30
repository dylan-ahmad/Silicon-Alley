using BAModAPI;
using BigAmbitions.Mods;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Tier 3: in-game options panel to tune the project simulator. Values feed the tunables in
// SiliconAlleyState and persist in the game's per-machine settings store (PlayerPrefs, key shape
// "m:<modId>:<optionId>") — NOT in the savegame.
//
// Issue #151: the panel used to be eleven controls in one flat list mixing three concerns, with units
// baked into labels and one value denominated in cents. It is now three labelled sections, every unit
// lives in the slider's own value suffix, and the hotkey rows warn when two functions share a key.
public static class SiliconAlleyOptions
{
    // Option ids. NOTE (#151): hostingperday and supportpct are DELIBERATELY new ids — their sliders were
    // re-scaled into honest units, and the stored pref holds the raw int, so reusing the old id would have
    // silently read a stored "500" (cents/hour) as "$500/day". A new id ignores the stale value and starts
    // from the new default; the orphan key is harmless (the game's reset only clears registered ids).
    private const string IdProjectType = "siliconalley_projecttype";
    private const string IdProjectSpeed = "siliconalley_projectspeed";
    private const string IdPayout = "siliconalley_payout";
    private const string IdSupport = "siliconalley_supportpct";        // was siliconalley_support (‰)
    private const string IdInfrastructure = "siliconalley_infrastructure";
    private const string IdHosting = "siliconalley_hostingperday";     // was siliconalley_hostingincome (cents/h)
    private const string IdServerUpkeep = "siliconalley_serverupkeep";
    private const string IdBackendCap = "siliconalley_backendcap";
    private const string IdScreenKey = "siliconalley_screenkey";
    private const string IdHubKey = "siliconalley_dashboardkey";       // id kept: same setting, honest label now
    private const string IdHelpKey = "siliconalley_helpkey";

    // Defaults, in the units each control now displays. They reproduce SiliconAlleyState's field defaults.
    private const int DefProjectType = 1;      // Standard
    private const int DefProjectSpeed = 100;   // %
    private const int DefPayout = 100;         // %
    private const int DefSupport = 2;          // % of market price per installed unit per day
    private const int DefInfrastructure = 100; // %
    private const int DefHosting = 120;        // $/server/day (÷24 ⇒ the 5f/hour default)
    private const int DefServerUpkeep = 90;    // $/server/day
    private const int DefBackendCap = 100;     // users/server
    private const int DefKey = 0;              // first entry of each KeyChoices array

    private static readonly string[] ProjectTypeChoices =
    {
        "siliconalley:projecttype_quick", "siliconalley:projecttype_standard", "siliconalley:projecttype_ambitious",
    };
    private static readonly string[] ScreenKeyChoices =
    {
        "siliconalley:key_f9", "siliconalley:key_f10", "siliconalley:key_f11",
        "siliconalley:key_f12", "siliconalley:key_tab", "siliconalley:key_backquote",
    };
    private static readonly string[] HubKeyChoices =
    {
        "siliconalley:key_f8", "siliconalley:key_f7", "siliconalley:key_f6",
        "siliconalley:key_f5", "siliconalley:key_tab", "siliconalley:key_backquote",
    };
    private static readonly string[] HelpKeyChoices =
    {
        "siliconalley:key_f1", "siliconalley:key_f2", "siliconalley:key_f3",
        "siliconalley:key_f4", "siliconalley:key_tab", "siliconalley:key_backquote",
    };

    private static SiliconAlleyHotkeyConflictOption _conflictRow;

    public static void Register(ModContext context)
    {
        // Issue #151: apply what the player already chose. The builder callbacks only fire when the options
        // tab is first OPENED, so before this every stored value was ignored for the whole session — rebind
        // the hub to Tab, restart, and you were back on F9.
        ApplyStoredOptions(context.ModId);

        _conflictRow = new SiliconAlleyHotkeyConflictOption();
        var options = new ModOptions()
            .AddHeader("siliconalley:options_header")
            .AddHeader("siliconalley:options_scope_note") // machine-global, not per-save (no description field exists)

            .AddHeader("siliconalley:options_grp_gameplay")
            .AddDropdown(IdProjectType, "siliconalley:options_projecttype", ProjectTypeChoices, DefProjectType, OnProjectType)
            .AddSlider(IdProjectSpeed, "siliconalley:options_projectspeed", 10, 500, DefProjectSpeed, OnProjectSpeed,
                "siliconalley:options_value_pct")
            .AddSlider(IdPayout, "siliconalley:options_payout", 10, 500, DefPayout, OnPayout,
                "siliconalley:options_value_pct")
            .AddSlider(IdSupport, "siliconalley:options_support", 0, 10, DefSupport, OnSupport,
                "siliconalley:options_value_pct")
            .AddSplitter()

            .AddHeader("siliconalley:options_grp_servers")
            .AddSlider(IdInfrastructure, "siliconalley:options_infrastructure", 0, 200, DefInfrastructure, OnInfrastructureStrength,
                "siliconalley:options_value_pct")
            .AddSlider(IdHosting, "siliconalley:options_hostingincome", 0, 480, DefHosting, OnHostingIncome,
                "siliconalley:options_value_perday")
            .AddSlider(IdServerUpkeep, "siliconalley:options_serverupkeep", 0, 500, DefServerUpkeep, OnServerUpkeep,
                "siliconalley:options_value_perday")
            .AddSlider(IdBackendCap, "siliconalley:options_backendcap", 10, 500, DefBackendCap, OnBackendCap,
                "siliconalley:options_value_users")
            .AddSplitter()

            .AddHeader("siliconalley:options_grp_hotkeys")
            .AddDropdown(IdScreenKey, "siliconalley:options_key", ScreenKeyChoices, DefKey, OnScreenKey)
            .AddDropdown(IdHubKey, "siliconalley:options_hubkey", HubKeyChoices, DefKey, OnHubKey)
            .AddDropdown(IdHelpKey, "siliconalley:options_helpkey", HelpKeyChoices, DefKey, OnHelpKey)
            .AddCustom(_conflictRow) // issue #151: the duplicate-binding warning, hidden until it applies
            .AddSplitter();

        OptionsService.Register(context.ModId, options);
        context.Logger.Info("SiliconAlley: options registered.");
    }

    public static void Unregister(ModContext context)
    {
        OptionsService.RemoveModOptions(context.ModId);
        _conflictRow = null;
    }

    // Issue #151: replay every stored value through the same callbacks the panel uses, so the player's
    // settings are live from the first frame instead of from the first time they open the options tab.
    // MUST be UnityEngine.PlayerPrefs — the game DLL declares a global-namespace PlayerPrefs that shadows
    // Unity's (the same trap #147 documented for the window position).
    private static void ApplyStoredOptions(string modId)
    {
        OnProjectType(Stored(modId, IdProjectType, DefProjectType));
        OnProjectSpeed(Stored(modId, IdProjectSpeed, DefProjectSpeed));
        OnPayout(Stored(modId, IdPayout, DefPayout));
        OnSupport(Stored(modId, IdSupport, DefSupport));
        OnInfrastructureStrength(Stored(modId, IdInfrastructure, DefInfrastructure));
        OnHostingIncome(Stored(modId, IdHosting, DefHosting));
        OnServerUpkeep(Stored(modId, IdServerUpkeep, DefServerUpkeep));
        OnBackendCap(Stored(modId, IdBackendCap, DefBackendCap));
        OnScreenKey(Stored(modId, IdScreenKey, DefKey));
        OnHubKey(Stored(modId, IdHubKey, DefKey));
        OnHelpKey(Stored(modId, IdHelpKey, DefKey));
    }

    private static int Stored(string modId, string optionId, int defaultValue) =>
        UnityEngine.PlayerPrefs.GetInt("m:" + modId + ":" + optionId, defaultValue);

    private static void OnProjectType(int value) => SiliconAlleyState.GlobalProjectType = value;
    private static void OnProjectSpeed(int value) => SiliconAlleyState.ProjectSpeed = value / 100f;
    private static void OnPayout(int value) => SiliconAlleyState.PayoutMultiplier = value / 100f;
    // #151: the slider is whole percent now (was per-mille while the label claimed "%").
    private static void OnSupport(int value) => SiliconAlleyState.SupportRatePerDay = value / 100f;
    private static void OnInfrastructureStrength(int value) => SiliconAlleyState.InfrastructureStrength = value / 100f;
    // #151: the slider is dollars per server per DAY now — the shape the hub's server card already prints
    // (it was cents per server per hour, the only cents-denominated value in the mod).
    private static void OnHostingIncome(int value) => SiliconAlleyState.HostingIncomePerServerPerHour = value / 24f;
    private static void OnServerUpkeep(int value) => SiliconAlleyState.ServerUpkeepPerServerPerDay = value;
    private static void OnBackendCap(int value) => SiliconAlleyState.BackendCapPerServer = value;

    // Issue #14: the key that opens/closes the project screen (machine-local; index maps to KeyChoices).
    private static void OnScreenKey(int value)
    {
        SiliconAlleyProjectScreen.ToggleKey =
            SiliconAlleyProjectScreen.KeyChoices[Mathf.Clamp(value, 0, SiliconAlleyProjectScreen.KeyChoices.Length - 1)];
        RefreshConflictHint();
    }

    // Issue #59 (renamed in #151: the standalone dashboard died in 0.5.0 — this key opens the studio HUB).
    private static void OnHubKey(int value)
    {
        SiliconAlleyDashboardScreen.ToggleKey =
            SiliconAlleyDashboardScreen.KeyChoices[Mathf.Clamp(value, 0, SiliconAlleyDashboardScreen.KeyChoices.Length - 1)];
        RefreshConflictHint();
    }

    // Issue #68: the key that opens the in-game help overview (machine-local; index maps to KeyChoices).
    private static void OnHelpKey(int value)
    {
        SiliconAlleyHelp.HotKey =
            SiliconAlleyHelp.KeyChoices[Mathf.Clamp(value, 0, SiliconAlleyHelp.KeyChoices.Length - 1)];
        RefreshConflictHint();
    }

    // Issue #151: flag a duplicate binding. Non-blocking — the key still applies; the player just gets told
    // that two functions now answer to it. The three choice arrays overlap ONLY at Tab and BackQuote (the
    // F-key sets are disjoint), so in practice a conflict always means two dropdowns sitting on the same
    // one of those two. Called from all three key callbacks; the panel fires them in spawn order on every
    // rebuild, so the first calls of a rebuild see a half-updated snapshot — harmless, the last call wins.
    private static void RefreshConflictHint()
    {
        if (_conflictRow == null)
            return;
        var screen = SiliconAlleyProjectScreen.ToggleKey;
        var hub = SiliconAlleyDashboardScreen.ToggleKey;
        var help = SiliconAlleyHelp.HotKey;
        var conflict = screen == hub || screen == help || hub == help;
        _conflictRow.SetConflict(conflict, conflict ? KeyLabelOf(screen == hub || screen == help ? screen : hub) : null);
    }

    // The localized display name for a bound key, reusing the same key_* table the dropdowns are built from.
    private static string KeyLabelOf(KeyCode key) => SiliconAlleyFormat.KeyLabel(key);
}

// Issue #151: the duplicate-hotkey warning row. The options API has no dynamic/conditional row and no way
// to re-style a built-in one at runtime (ModOption.Label is get-only), so this is an AddCustom row whose
// SpawnUi the mod owns — that lets it change its own text and visibility without re-registering (a
// re-register would destroy every mod's rows and re-invoke every callback, which from inside a callback
// recurses). The panel rebuilds on every open, so SpawnUi runs again and refreshes the cached label.
public sealed class SiliconAlleyHotkeyConflictOption : ModOption
{
    private TMP_Text _label;
    private bool _conflict;
    private string _keyName;

    public SiliconAlleyHotkeyConflictOption() : base("siliconalley_hotkeyconflict", "siliconalley:options_hotkey_conflict")
    {
    }

    public override void SpawnUi(Transform parent, string modId)
    {
        var go = new GameObject("SiliconAlleyHotkeyConflict", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = 26f; // the panel's vertical layout needs a height to give this row
        layout.flexibleWidth = 1f;
        _label = SiliconAlleyUI.MakeText(go.transform, "ConflictText",
            SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        _label.color = SiliconAlleyTheme.Danger;
        SiliconAlleyUI.Stretch(_label.rectTransform);
        Apply();
    }

    public void SetConflict(bool conflict, string keyName)
    {
        _conflict = conflict;
        _keyName = keyName;
        Apply();
    }

    private void Apply()
    {
        if (_label == null) // not spawned yet, or destroyed by the last panel rebuild
            return;
        _label.gameObject.SetActive(_conflict);
        if (_conflict)
            _label.text = SiliconAlleyFormat.Compose("siliconalley:options_hotkey_conflict",
                ("key", _keyName ?? ""));
    }
}
