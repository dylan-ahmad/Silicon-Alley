using BigAmbitions.DayNightCycle;
using Entities;
using Helpers;
using UnityEngine;

// Issue #21 (Marketing): the SINGLE place the mod spends cash. Marketing campaigns deduct money here;
// project revenue/support keep using the simulator's CreditRevenue (the order path). Centralising the
// debit means there is exactly one symbol to verify against the decompiled game DLLs (the API source of
// truth per CLAUDE.md) and one place to swap in a first-class BA money/expense API if a cleaner one is
// confirmed in-engine.
//
// IMPLEMENTATION NOTE (verify in the Unity dev env, where the game DLLs are imported): cash leaves the
// studio's account as a business EXPENSE, mirroring how CreditRevenue books income — a completed order on
// the building, but with a negative amount. This reuses the one money channel already proven in this mod
// (BuildingRegistration.unprocessedCompletedOrders), so it compiles against the same types. If BA exposes
// a dedicated transaction/expense API, replace the body of Spend() with it — callers don't change.
public static class SiliconAlleyMoney
{
    // True if the studio can pay `amount`. Best-effort: without a verified balance-read API we don't block
    // the player (an overdraft falls to the game's own handling). Wire a real balance check here once the
    // BA money API is confirmed in-engine; callers already gate their buttons on this.
    public static bool CanAfford(BuildingRegistration registration, float amount)
    {
        return registration != null && amount >= 0f;
    }

    // Deduct `amount` from the studio as a marketing expense. Returns false (and spends nothing) if it
    // can't be afforded or the registration is missing, so callers can refuse the campaign cleanly.
    public static bool TrySpend(BuildingRegistration registration, float amount, string reason)
    {
        if (registration == null || amount <= 0f || !CanAfford(registration, amount))
            return false;
        try
        {
            // Book a completed, paid expense order (negative amount) on the studio — the inverse of the
            // revenue the simulator credits, so marketing cost shows in the business's books.
            var order = new Order
            {
                completed = true,
                cleanliness = registration.GetCleanliness(),
                customerServiceSkill = 0f,
                customerDemandScore = 1f,
                timestamp = new Timestamp(TimeHelper.CurrentDay, TimeHelper.CurrentHour, 0f),
            };
            order.entries.Add(new OrderEntry
            {
                itemName = registration.businessTypeName, // bookkeeping label for the expense line
                price = -Mathf.Abs(amount),
                available = true,
                paid = true,
                priceAccceptable = true,
            });
            registration.unprocessedCompletedOrders.Add(order);
            Debug.Log($"[SiliconAlley] {SiliconAlleyState.KeyFor(registration)} spent ${amount:F0} on {reason}.");
            return true;
        }
        catch
        {
            return false; // never let a marketing buy corrupt the studio's books
        }
    }
}
