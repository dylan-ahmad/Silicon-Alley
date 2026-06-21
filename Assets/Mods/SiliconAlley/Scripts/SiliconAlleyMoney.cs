using System.Collections.Generic;
using Entities;
using UnityEngine;

// Issue #21 (Marketing): the SINGLE place the mod spends cash. Marketing campaigns deduct money here;
// project revenue/support keep using the simulator's CreditRevenue (the order path). Centralising the
// debit means there is exactly one symbol to verify against the decompiled game DLLs (the API source of
// truth per CLAUDE.md) and one place to swap implementations.
//
// VERIFIED against decompiled/BigAmbitions: cash leaves the player's account via GameManager.ChangeMoneySafe
// — the SAME first-class money API the base game uses for casino tickets, entrance fees and seller-stand
// buys (it negates the amount, books a labelled Transaction, plays the spend sound, and shows the
// insufficient-money toast itself). GameManager / TransactionInfo / SaveGameManager / Address live in the
// global namespace. This replaces the earlier "negative-price completed Order" workaround, which only netted
// against the building's *daily* revenue (deferred, mislabelled as revenue, and silently dropped a whole
// building's order processing if the net day went negative while near-broke).
public static class SiliconAlleyMoney
{
    // Localized label shown in the financial transaction log (Locales/en.json: siliconalley:transaction_marketing).
    private const string MarketingTransactionType = "siliconalley:transaction_marketing";

    // True if the player can pay `amount` right now. Reads the SAME live balance ChangeMoneySafe debits, so
    // the button gate in SiliconAlleyProjectScreen matches exactly what TrySpend will actually allow.
    public static bool CanAfford(BuildingRegistration registration, float amount)
    {
        if (registration == null || amount < 0f)
            return false;
        return SaveGameManager.Current.Money >= amount;
    }

    // Deduct `amount` from the player's account as a marketing expense — immediately and atomically — via the
    // base-game money API. Returns false (and spends nothing) if the registration is missing or the player
    // can't afford it, so callers can refuse the campaign cleanly and never charge for one that didn't land.
    public static bool TrySpend(BuildingRegistration registration, float amount, string reason)
    {
        if (registration == null || amount <= 0f)
            return false;

        var data = new Dictionary<string, string>
        {
            ["businessName"] = registration.BusinessName,
            ["reason"] = reason ?? string.Empty,
        };
        var info = new TransactionInfo(MarketingTransactionType, data);

        // amount<0 with force:false ⇒ ChangeMoneySafe books the expense, plays the spend sound and shows the
        // insufficient-money toast itself, returning false (and changing nothing) if the player can't afford it.
        bool spent = GameManager.ChangeMoneySafe(0f - amount, info, null, registration.Address,
            force: false, showNotification: true);
        if (spent)
            Debug.Log($"[SiliconAlley] {SiliconAlleyState.KeyFor(registration)} spent ${amount:F0} on {reason}.");
        return spent;
    }
}
