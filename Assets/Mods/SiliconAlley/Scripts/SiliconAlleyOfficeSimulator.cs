using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BigAmbitions.Characters.Skills;
using BigAmbitions.DayNightCycle;
using BigAmbitions.Items;
using BigAmbitions.Tags;
using Buildings.BuildingTypes.Shared.Dirtiness;
using Entities;
using Helpers;
using Helpers.BusinessSimulation;
using Localizor;
using UI.Notification;
using UnityEngine;

// Tier 2/3: software-project business simulator (mod-defined, assigned in code by SiliconAlleyMod).
// Each sim hour, programmers working at a workstation accrue project progress; completed projects
// pay quality/reputation-scaled revenue through the normal order path (so it shows as business
// revenue), and an installed base earns recurring support income. See docs/DESIGN.md.
public class SiliconAlleyOfficeSimulator : BusinessSimulator
{
    // Post-release updates: a staffed studio patches its live catalog this often, earning a fraction
    // of each product's market price per installed unit.
    // public so the phone dashboard can show "next update in N days" off the same schedule.
    public const int PatchIntervalDays = 7;
    private const float PatchRevenueFraction = 0.08f;

    public override void SimulateCurrentHour()
    {
        var businessType = BusinessTypeHelper.GetData(buildingRegistration);
        if (businessType == null || !businessType.HasTag(TagRef.Businesstag.generatesrevenue))
            return;

        var key = SiliconAlleyState.KeyFor(buildingRegistration);

        // 1) Gather the programmers working at a workstation this hour.
        float totalSkill = 0f;
        float totalSatisfaction = 0f;
        int programmers = 0;
        foreach (var instance in buildingRegistration.itemInstances.Values)
        {
            if ((instance.ItemCached.type & ItemType.EmployeeWorkstation) == 0)
                continue;
            var employee = EmployeeHelper.GetEmployeeAtStationAndHour(buildingRegistration, instance.id, currentHour);
            if (employee == null)
                continue;
            var skill = employee.characterData.skills.FirstOrDefault(s => businessType.employeePrimarySkills.Contains(s.name));
            if (skill == null)
                continue;
            totalSkill += skill.value;
            totalSatisfaction += employee.satisfaction;
            programmers++;
        }

        // 2) Accrue project progress through the lifecycle phases (or slowly lose reputation when idle).
        if (programmers > 0)
        {
            var progressBefore = SiliconAlleyState.GetProgress(key);
            SiliconAlleyState.AddProgress(key, totalSkill * SiliconAlleyState.ProjectSpeed);
            var progressAfter = SiliconAlleyState.GetProgress(key);
            AnnouncePhaseTransition(businessType, key, progressBefore, progressAfter);
            // Step 3 (quality): sample this hour's staff quality; Testing-phase work counts double.
            var hourQuality = Mathf.Clamp01(totalSkill / programmers / 100f) * Mathf.Clamp01(totalSatisfaction / programmers / 100f);
            var phaseWeight = SiliconAlleyState.PhaseOf(progressBefore) == SiliconAlleyState.ProjectPhase.Testing ? 2f : 1f;
            SiliconAlleyState.AccumulateQuality(key, hourQuality, phaseWeight);
            Debug.Log($"[SiliconAlley] {key} h{currentHour}: {programmers} programmer(s), {SiliconAlleyState.PhaseOf(progressAfter)} progress {progressAfter:F0}/{SiliconAlleyState.ProjectSize:F0}");
        }
        else
        {
            SiliconAlleyState.DecayReputation(key, 0.001f);
        }

        var product = PrimaryProduct(businessType);
        var marketPrice = product != null ? MarketPrice(product) : 0f;

        // 3) Recurring support income from the installed base.
        if (product != null && marketPrice > 0f)
        {
            var support = SiliconAlleyState.AccrueSupport(key, marketPrice);
            if (support > 0f)
                CreditRevenue(product, support, 1f);
        }

        // 3b) Post-release updates: a staffed studio with shipped products patches its live catalog
        // every PatchIntervalDays for extra revenue — the "Support/Updates" stage of the lifecycle.
        if (programmers > 0 && product != null && marketPrice > 0f)
        {
            var catalog = SiliconAlleyState.GetInstalledBase(key);
            if (catalog > 0 && TimeHelper.CurrentDay - SiliconAlleyState.GetLastPatchDay(key) >= PatchIntervalDays)
            {
                var patchRevenue = marketPrice * catalog * PatchRevenueFraction * MarketFactor(buildingRegistration);
                CreditRevenue(product, patchRevenue, 1f);
                SiliconAlleyState.SetLastPatchDay(key, TimeHelper.CurrentDay);
                AnnouncePatch(businessType, key, catalog, patchRevenue);
            }
        }

        // 4) Complete any finished projects.
        while (programmers > 0 && product != null && marketPrice > 0f
               && SiliconAlleyState.GetProgress(key) >= SiliconAlleyState.ProjectSize)
        {
            SiliconAlleyState.AddProgress(key, -SiliconAlleyState.ProjectSize);
            var cleanliness = Mathf.Clamp01(buildingRegistration.GetCleanliness() / 100f);
            // Step 3 (quality): ship at the quality accrued across all phases (Testing weighted heavier),
            // not just the final hour's staffing. Fall back to this hour if nothing has accrued.
            var accruedQuality = SiliconAlleyState.GetAverageQuality(key);
            if (accruedQuality < 0f)
                accruedQuality = Mathf.Clamp01(totalSkill / programmers / 100f) * Mathf.Clamp01(totalSatisfaction / programmers / 100f);
            var quality = accruedQuality * Mathf.Max(0.25f, cleanliness);
            var reputationFactor = 0.75f + SiliconAlleyState.GetReputation(key);
            var marketFactor = MarketFactor(buildingRegistration);
            var payout = marketPrice * (0.5f + quality) * reputationFactor * marketFactor * SiliconAlleyState.PayoutMultiplier;
            CreditRevenue(product, payout, quality);
            SiliconAlleyState.OnProjectCompleted(key, quality);
            SiliconAlleyState.SetLastPatchDay(key, TimeHelper.CurrentDay); // a fresh release resets the patch clock
            Debug.Log($"[SiliconAlley] {key} completed a project (quality {quality:F2}, payout {payout:F0}, reputation {SiliconAlleyState.GetReputation(key):F2}).");
            ShowProjectCompleteNotification(businessType, key, quality, payout, reputationFactor, marketFactor);
        }
    }

    public override void OnTimeMachineEnd(BuildingRegistration registration)
    {
    }

    // Credit revenue the same way OfficeBusinessSimulator does: a completed, paid order.
    private void CreditRevenue(string itemName, float price, float quality)
    {
        var order = new Order
        {
            completed = true,
            cleanliness = buildingRegistration.GetCleanliness(),
            customerServiceSkill = quality * 100f,
            customerDemandScore = 1f,
            timestamp = new Timestamp(TimeHelper.CurrentDay, currentHour, 0f),
        };
        order.entries.Add(new OrderEntry
        {
            itemName = itemName,
            price = price,
            available = true,
            paid = true,
            priceAccceptable = true,
        });
        buildingRegistration.unprocessedCompletedOrders.Add(order);
    }

    // Step 1 (visibility): announce a completed project to the player via the game's notification
    // system (the same path the base game uses for business events). Values mirror the Debug.Log
    // above; numbers use InvariantCulture (dev machine is nl-NL). duplicateIdentifier = key coalesces
    // a burst of same-business completions (e.g. during time-machine catch-up) into a single toast,
    // while completions in normal play still each show.
    private void ShowProjectCompleteNotification(BusinessType businessType, string key, float quality, float payout, float reputationFactor, float marketFactor)
    {
        var data = new Dictionary<string, string>
        {
            ["business"] = buildingRegistration.GetDisplayName(),
            ["product"] = ProductDisplayName(businessType),
            ["quality"] = Mathf.RoundToInt(Mathf.Clamp01(quality) * 100f).ToString(CultureInfo.InvariantCulture) + "%",
            ["payout"] = "$" + Mathf.RoundToInt(payout).ToString("N0", CultureInfo.InvariantCulture),
            // Show why the payout is what it is: reputation lifts it, neighborhood competition trims it.
            ["repmult"] = reputationFactor.ToString("F2", CultureInfo.InvariantCulture),
            ["marketmult"] = marketFactor.ToString("F2", CultureInfo.InvariantCulture),
        };
        Notifications.Show(NotificationType.Success, "siliconalley:notify_projectcomplete", data, 6f, key);
    }

    // Step 2 (lifecycle): announce entry into Development or Testing. Release is announced by the
    // completion toast above; Design is the implicit start of each project (shown in the dashboard).
    // duplicateIdentifier is per business + phase so each transition shows once.
    private void AnnouncePhaseTransition(BusinessType businessType, string key, float before, float after)
    {
        var oldPhase = SiliconAlleyState.PhaseOf(before);
        var newPhase = SiliconAlleyState.PhaseOf(after);
        if (newPhase <= oldPhase || newPhase == SiliconAlleyState.ProjectPhase.Release)
            return;
        var data = new Dictionary<string, string>
        {
            ["business"] = buildingRegistration.GetDisplayName(),
            ["product"] = ProductDisplayName(businessType),
            ["phase"] = SiliconAlleyState.PhaseNameKey(newPhase).GetLocalization(),
        };
        Notifications.Show(NotificationType.Info, "siliconalley:notify_phase", data, 5f, key + ":" + newPhase);
    }

    // Step 3 (support/updates): announce a periodic patch shipped to the studio's live catalog.
    private void AnnouncePatch(BusinessType businessType, string key, int catalog, float revenue)
    {
        var data = new Dictionary<string, string>
        {
            ["business"] = buildingRegistration.GetDisplayName(),
            ["product"] = ProductDisplayName(businessType),
            ["catalog"] = catalog.ToString(CultureInfo.InvariantCulture),
            ["revenue"] = "$" + Mathf.RoundToInt(revenue).ToString("N0", CultureInfo.InvariantCulture),
        };
        Notifications.Show(NotificationType.Info, "siliconalley:notify_patch", data, 5f, key + ":patch");
    }

    // Localized display name of the business's primary product (themes the toast per business type:
    // a Game Studio ships "Video Game", a Cyber Security Firm a "Security Audit", etc.).
    private static string ProductDisplayName(BusinessType businessType)
    {
        var product = PrimaryProduct(businessType);
        return product != null ? product.GetLocalization() : "project";
    }

    private static string PrimaryProduct(BusinessType businessType)
    {
        if (businessType.businessProducts == null || businessType.businessProducts.Length == 0)
            return null;
        return businessType.businessProducts[0].itemName;
    }

    private static float MarketPrice(string itemName)
    {
        var item = ItemsGetter.GetByName(itemName);
        return item != null ? item.DefaultMarketPrice : 0f;
    }

    // Tier 3 market modifier: competing businesses of the same type in the same neighborhood. Same
    // competitor definition as the founding UI (StartBusinessUI). Static so the phone dashboard can
    // show the player the same figure that scales the payout.
    public static int CompetitorCount(BuildingRegistration registration)
    {
        var current = SaveGameManager.Current;
        if (current == null || current.BuildingRegistrations == null)
            return 0;
        var neighborhood = registration.Neighborhood;
        var typeName = registration.businessTypeName;
        int sameType = 0;
        foreach (var other in current.BuildingRegistrations)
            if (other.businessTypeName == typeName && other.Neighborhood == neighborhood)
                sameType++;
        return Mathf.Max(0, sameType - 1); // exclude this business itself
    }

    // More neighborhood competitors trims the per-project payout.
    public static float MarketFactor(BuildingRegistration registration)
    {
        return 1f / (1f + CompetitorCount(registration) * 0.25f);
    }

    // Project progress this studio accrues per in-game hour at the current hour's staffing
    // (sum of programmer skill x ProjectSpeed) — the exact throughput SimulateCurrentHour applies.
    // The phone dashboard divides remaining progress by this to estimate phase/ship ETAs. Returns 0
    // when no qualifying programmer is at a workstation this hour (the studio is idle right now).
    public static float CurrentHourlyProgress(BuildingRegistration registration)
    {
        var businessType = BusinessTypeHelper.GetData(registration);
        if (businessType == null || registration.itemInstances == null)
            return 0f;

        var hour = TimeHelper.CurrentHour;
        float totalSkill = 0f;
        foreach (var instance in registration.itemInstances.Values)
        {
            if ((instance.ItemCached.type & ItemType.EmployeeWorkstation) == 0)
                continue;
            var employee = EmployeeHelper.GetEmployeeAtStationAndHour(registration, instance.id, hour);
            if (employee == null)
                continue;
            var skill = employee.characterData.skills.FirstOrDefault(s => businessType.employeePrimarySkills.Contains(s.name));
            if (skill == null)
                continue;
            totalSkill += skill.value;
        }
        return totalSkill * SiliconAlleyState.ProjectSpeed;
    }
}
