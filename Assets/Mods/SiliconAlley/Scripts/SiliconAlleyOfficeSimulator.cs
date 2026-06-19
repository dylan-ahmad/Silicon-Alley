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
using UI.Notification;
using UnityEngine;

// Tier 2/3: software-project business simulator (mod-defined, assigned in code by SiliconAlleyMod).
// Each sim hour, programmers working at a workstation accrue project progress; completed projects
// pay quality/reputation-scaled revenue through the normal order path (so it shows as business
// revenue), and an installed base earns recurring support income. See docs/DESIGN.md.
public class SiliconAlleyOfficeSimulator : BusinessSimulator
{
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

        // 2) Accrue project progress (or slowly lose reputation when idle).
        if (programmers > 0)
            SiliconAlleyState.AddProgress(key, totalSkill * SiliconAlleyState.ProjectSpeed);
        else
            SiliconAlleyState.DecayReputation(key, 0.001f);

        if (programmers > 0)
            Debug.Log($"[SiliconAlley] {key} h{currentHour}: {programmers} programmer(s), progress {SiliconAlleyState.GetProgress(key):F0}/{SiliconAlleyState.ProjectSize:F0}");

        var product = PrimaryProduct(businessType);
        var marketPrice = product != null ? MarketPrice(product) : 0f;

        // 3) Recurring support income from the installed base.
        if (product != null && marketPrice > 0f)
        {
            var support = SiliconAlleyState.AccrueSupport(key, marketPrice);
            if (support > 0f)
                CreditRevenue(product, support, 1f);
        }

        // 4) Complete any finished projects.
        while (programmers > 0 && product != null && marketPrice > 0f
               && SiliconAlleyState.GetProgress(key) >= SiliconAlleyState.ProjectSize)
        {
            SiliconAlleyState.AddProgress(key, -SiliconAlleyState.ProjectSize);
            var avgSkill = totalSkill / programmers;
            var avgSatisfaction = totalSatisfaction / programmers;
            var cleanliness = Mathf.Clamp01(buildingRegistration.GetCleanliness() / 100f);
            var quality = Mathf.Clamp01(avgSkill / 100f) * Mathf.Clamp01(avgSatisfaction / 100f) * Mathf.Max(0.25f, cleanliness);
            var reputationFactor = 0.75f + SiliconAlleyState.GetReputation(key);
            var payout = marketPrice * (0.5f + quality) * reputationFactor * MarketFactor(businessType) * SiliconAlleyState.PayoutMultiplier;
            CreditRevenue(product, payout, quality);
            SiliconAlleyState.OnProjectCompleted(key, quality);
            Debug.Log($"[SiliconAlley] {key} completed a project (quality {quality:F2}, payout {payout:F0}, reputation {SiliconAlleyState.GetReputation(key):F2}).");
            ShowProjectCompleteNotification(key, quality, payout);
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
    private void ShowProjectCompleteNotification(string key, float quality, float payout)
    {
        var data = new Dictionary<string, string>
        {
            ["business"] = buildingRegistration.GetDisplayName(),
            ["quality"] = Mathf.RoundToInt(Mathf.Clamp01(quality) * 100f).ToString(CultureInfo.InvariantCulture) + "%",
            ["payout"] = "$" + Mathf.RoundToInt(payout).ToString("N0", CultureInfo.InvariantCulture),
            ["reputation"] = SiliconAlleyState.GetReputation(key).ToString("F2", CultureInfo.InvariantCulture),
            ["installedbase"] = SiliconAlleyState.GetInstalledBase(key).ToString(CultureInfo.InvariantCulture),
        };
        Notifications.Show(NotificationType.Success, "siliconalley:notify_projectcomplete", data, 6f, key);
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

    // Tier 3 market modifier: more competing businesses of the same type in the same neighborhood
    // trims the per-project payout. Same competitor definition as the founding UI (StartBusinessUI).
    private float MarketFactor(BusinessType businessType)
    {
        var neighborhood = buildingRegistration.Neighborhood;
        var typeName = businessType.businessTypeName;
        int competitors = 0;
        foreach (var registration in SaveGameManager.Current.BuildingRegistrations)
            if (registration.businessTypeName == typeName && registration.Neighborhood == neighborhood)
                competitors++;
        competitors = Mathf.Max(0, competitors - 1); // exclude this business itself
        return 1f / (1f + competitors * 0.25f);
    }
}
