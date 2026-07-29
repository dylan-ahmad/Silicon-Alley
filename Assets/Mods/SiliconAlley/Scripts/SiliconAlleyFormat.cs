using System.Collections.Generic;
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

    // A signed money delta as "+$1,200" / "-$340" / "$0" (#149 — the ship report's payout-vs-previous
    // readout). Rounds first like Money, so a sub-dollar delta reads "$0" instead of "+$0".
    public static string SignedMoney(float amount)
    {
        var rounded = Mathf.RoundToInt(amount);
        return rounded > 0 ? "+" + Money(rounded) : Money(rounded);
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
    // #148: the raw float, so the hub's totals strip can SUM per-studio values and round ONCE via Money
    // (summing the per-studio rounded strings would drift from the true total and fail the strip-matches-
    // cards acceptance).
    public static float SupportPerDayValue(BuildingRegistration registration, string key)
    {
        var installedBase = SiliconAlleyState.GetInstalledBase(key);
        if (installedBase <= 0)
            return 0f;
        var businessType = BusinessTypeHelper.GetData(registration);
        if (businessType?.businessProducts == null || businessType.businessProducts.Length == 0)
            return 0f;
        var item = ItemsGetter.GetByName(businessType.businessProducts[0].itemName);
        if (item == null)
            return 0f;
        return installedBase * item.DefaultMarketPrice * SiliconAlleyState.SupportRatePerDay
            * SiliconAlleyMarket.DemandFactor(registration.businessTypeName, TimeHelper.CurrentDay);
    }

    public static string SupportPerDay(BuildingRegistration registration, string key) =>
        Money(SupportPerDayValue(registration, key)) + "/day";

    // #148: THE product display name — was three private copies (project screen ×2, simulator ×2) of the
    // same "primary product's localized item name, 'project' fallback, player-typed name overlay" logic,
    // exactly the duplication this table exists to prevent (#144).
    public static string ProductDisplayName(BusinessType businessType)
    {
        if (businessType?.businessProducts == null || businessType.businessProducts.Length == 0)
            return "project";
        return businessType.businessProducts[0].itemName.GetLocalization();
    }

    public static string ProductDisplayName(string key, BusinessType businessType) =>
        SiliconAlleyState.GetProductNameOrDefault(key, ProductDisplayName(businessType));

    // #148: localized-string composition for "{a} · {b}"-style keys — was three identical private copies
    // (dashboard ×2, project screen ×1); the hub strip would have needed a fourth.
    public static string Compose(string key, params (string, string)[] args)
    {
        var dict = new Dictionary<string, string>();
        foreach (var (k, v) in args)
            dict[k] = v;
        return key.Localize(dict).ToString();
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
