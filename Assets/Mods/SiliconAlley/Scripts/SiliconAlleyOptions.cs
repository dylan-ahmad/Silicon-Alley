using BAModAPI;
using BigAmbitions.Mods;
using UnityEngine;

// Tier 3: in-game options panel to tune the project simulator. Slider values persist via the
// game's settings store. Values feed the tunables in SiliconAlleyState.
public static class SiliconAlleyOptions
{
    public static void Register(ModContext context)
    {
        var options = new ModOptions()
            .AddHeader("siliconalley:options_header")
            .AddDropdown("siliconalley_projecttype", "siliconalley:options_projecttype",
                new[] { "siliconalley:projecttype_quick", "siliconalley:projecttype_standard", "siliconalley:projecttype_ambitious" },
                1, OnProjectType)
            .AddSlider("siliconalley_projectspeed", "siliconalley:options_projectspeed", 10, 500, 100, OnProjectSpeed)
            .AddSlider("siliconalley_payout", "siliconalley:options_payout", 10, 500, 100, OnPayout)
            .AddSlider("siliconalley_support", "siliconalley:options_support", 0, 100, 20, OnSupport)
            .AddDropdown("siliconalley_screenkey", "siliconalley:options_key",
                new[] { "siliconalley:key_f9", "siliconalley:key_f10", "siliconalley:key_f11",
                        "siliconalley:key_f12", "siliconalley:key_tab", "siliconalley:key_backquote" },
                0, OnScreenKey)
            .AddDropdown("siliconalley_dashboardkey", "siliconalley:options_dashboardkey",
                new[] { "siliconalley:key_f8", "siliconalley:key_f7", "siliconalley:key_f6",
                        "siliconalley:key_f5", "siliconalley:key_tab", "siliconalley:key_backquote" },
                0, OnDashboardKey)
            .AddSplitter();

        OptionsService.Register(context.ModId, options);
        context.Logger.Info("SiliconAlley: options registered.");
    }

    public static void Unregister(ModContext context)
    {
        OptionsService.RemoveModOptions(context.ModId);
    }

    private static void OnProjectType(int value) => SiliconAlleyState.GlobalProjectType = value;
    private static void OnProjectSpeed(int value) => SiliconAlleyState.ProjectSpeed = value / 100f;
    private static void OnPayout(int value) => SiliconAlleyState.PayoutMultiplier = value / 100f;
    private static void OnSupport(int value) => SiliconAlleyState.SupportRatePerDay = value / 1000f;

    // Issue #14: the key that opens/closes the project screen (machine-local; index maps to KeyChoices).
    private static void OnScreenKey(int value) =>
        SiliconAlleyProjectScreen.ToggleKey =
            SiliconAlleyProjectScreen.KeyChoices[Mathf.Clamp(value, 0, SiliconAlleyProjectScreen.KeyChoices.Length - 1)];

    // Issue #59: the key that opens/closes the studio dashboard (machine-local; index maps to KeyChoices).
    private static void OnDashboardKey(int value) =>
        SiliconAlleyDashboardScreen.ToggleKey =
            SiliconAlleyDashboardScreen.KeyChoices[Mathf.Clamp(value, 0, SiliconAlleyDashboardScreen.KeyChoices.Length - 1)];
}
