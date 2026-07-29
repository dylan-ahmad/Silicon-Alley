using System.Globalization;
using BigAmbitions.Items;
using Entities;
using Helpers;
using Localizor;
using UnityEngine;

// Issue #59 (+ #144): THE format table — every user-visible number shape lives here, so the hub, the
// detail view, the phone dialog and the toasts format the same value identically instead of drifting
// apart in private copies (#144 fixed exactly such a drift: a demand-less SupportPerDay clone).
// Conventions: the formatter owns a standalone value's suffix/glyph ("%", "/10", "d", "/day", "×");
// "~" marks throughput ESTIMATES only (Eta) — exact calendar-day countdowns (DaysLeft/PatchEta) are
// bare "Nd"; the visual list separator is "·" (locale-side). Presentation only: these read live state
// and return display strings, they never write state. All float formatting uses InvariantCulture (the
// dev machine is nl-NL; see CLAUDE.md).
public static class SiliconAlleyFormat
{
    // "$1,234" / "-$1,234" — a rounded money string. Rounds FIRST, then signs, so -0.4f is "$0", not "-$0".
    public static string Money(float amount)
    {
        var rounded = Mathf.RoundToInt(amount);
        return rounded < 0
            ? "-$" + (-rounded).ToString("N0", CultureInfo.InvariantCulture)
            : "$" + rounded.ToString("N0", CultureInfo.InvariantCulture);
    }

    // A 0..1 fraction as "42%".
    public static string Pct(float fraction01) =>
        Mathf.RoundToInt(Mathf.Clamp01(fraction01) * 100f).ToString(CultureInfo.InvariantCulture) + "%";

    // A signed 0..1 fraction delta as "+3%" / "-2%" / "0%". Not clamped — deltas may legitimately exceed ±1.
    public static string SignedPct(float fraction)
    {
        var rounded = Mathf.RoundToInt(fraction * 100f);
        return (rounded > 0 ? "+" : "") + rounded.ToString(CultureInfo.InvariantCulture) + "%";
    }

    // The phase-weighted average quality (0..1) as a percentage, or "—" before any quality has accrued (< 0).
    public static string Quality(float quality) =>
        quality < 0f ? "—" : Pct(quality);

    // A 0..10 review score as "7.4/10".
    public static string Review(float review) =>
        review.ToString("F1", CultureInfo.InvariantCulture) + "/10";

    // A market-demand multiplier as "×1.12".
    public static string Demand(float factor) =>
        "×" + factor.ToString("F2", CultureInfo.InvariantCulture);

    // Remaining progress / current hourly throughput, as a short "~Nd Nh" ESTIMATE. perHour is this hour's
    // live staffing, so an unstaffed studio reports "needs staff" rather than an infinite ETA; "due now" at <= 0.
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

    // An EXACT calendar-day countdown as a bare "Nd" ("0d" on the last day); "due now" once it lapses.
    // No "~" — deadlines are exact, unlike the throughput estimate Eta.
    public static string DaysLeft(int days)
    {
        if (days < 0)
            return "siliconalley:client_eta_due".GetLocalization();
        return days.ToString(CultureInfo.InvariantCulture) + "d";
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
        return daysUntil <= 0
            ? "siliconalley:client_eta_due".GetLocalization()
            : DaysLeft(daysUntil);
    }
}
