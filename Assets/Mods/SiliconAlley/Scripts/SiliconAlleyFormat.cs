using System.Globalization;
using BigAmbitions.Items;
using Entities;
using Helpers;
using Localizor;
using UnityEngine;

// Issue #59: the shared per-studio status formatters, extracted so the phone dialog
// (SiliconAlleyClientDialog) and the new card dashboard (SiliconAlleyDashboardScreen) format the same
// numbers identically — instead of a third near-duplicate copy. Presentation only: these read live state
// and return display strings, they never write state. All float formatting uses InvariantCulture (the dev
// machine is nl-NL; see CLAUDE.md).
public static class SiliconAlleyFormat
{
    // "$N,NNN" — a rounded money string.
    public static string Money(float amount) =>
        "$" + Mathf.RoundToInt(amount).ToString("N0", CultureInfo.InvariantCulture);

    // Remaining progress / current hourly throughput, as a short "~Nd Nh". perHour is this hour's live
    // staffing, so an unstaffed studio reports "needs staff" rather than an infinite ETA; "due now" at <= 0.
    public static string Eta(float remaining, float perHour)
    {
        if (perHour <= 0f)
            return "siliconalley:client_eta_idle".GetLocalization();
        var hours = Mathf.CeilToInt(Mathf.Max(0f, remaining) / perHour);
        if (hours <= 0)
            return "siliconalley:client_eta_due".GetLocalization();
        var days = hours / 24;
        var rest = hours % 24;
        return days > 0
            ? "~" + days.ToString(CultureInfo.InvariantCulture) + "d " + rest.ToString(CultureInfo.InvariantCulture) + "h"
            : "~" + rest.ToString(CultureInfo.InvariantCulture) + "h";
    }

    // The phase-weighted average quality (0..1) as a percentage, or "—" before any quality has accrued (< 0).
    public static string Quality(float quality)
    {
        if (quality < 0f)
            return "—";
        return Mathf.RoundToInt(Mathf.Clamp01(quality) * 100f).ToString(CultureInfo.InvariantCulture) + "%";
    }

    // Estimated recurring support income per day = installed base x product market price x support rate x
    // current market demand — the demand-aware estimate that matches what the simulator actually credits.
    public static string SupportPerDay(BuildingRegistration registration, string key)
    {
        var installedBase = SiliconAlleyState.GetInstalledBase(key);
        var perDay = 0f;
        if (installedBase > 0)
        {
            var businessType = BusinessTypeHelper.GetData(registration);
            if (businessType?.businessProducts != null && businessType.businessProducts.Length > 0)
            {
                var item = ItemsGetter.GetByName(businessType.businessProducts[0].itemName);
                if (item != null)
                    perDay = installedBase * item.DefaultMarketPrice * SiliconAlleyState.SupportRatePerDay
                        * SiliconAlleyMarket.DemandFactor(registration.businessTypeName, TimeHelper.CurrentDay);
            }
        }
        return Money(perDay) + "/day";
    }

    // Days until the studio next patches its live catalog (only meaningful once installed base > 0);
    // "due now" when the interval has elapsed, "—" with nothing released yet.
    public static string PatchEta(string key)
    {
        if (SiliconAlleyState.GetInstalledBase(key) <= 0)
            return "—";
        var daysUntil = SiliconAlleyOfficeSimulator.PatchIntervalDays - (TimeHelper.CurrentDay - SiliconAlleyState.GetLastPatchDay(key));
        if (daysUntil <= 0)
            return "siliconalley:client_eta_due".GetLocalization();
        return "~" + daysUntil.ToString(CultureInfo.InvariantCulture) + "d";
    }
}
