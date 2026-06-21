using UnityEngine;

// Issue #22 (Publisher roster) + #23 (deals): the mod-defined publisher table and the pure offer math the
// deal system sits on. A publisher promotes ONE of your finished products on a deadline: better-known
// publishers (higher tier) and a stronger relationship with you (higher per-publisher reputation, persisted
// globally in SiliconAlleyState's "~publishers" header) demand a TIGHTER deadline but pay MORE and grant MORE
// reputation — so the relationship compounds. A publisher also has a product-type FOCUS; a matching product
// earns a payout bonus.
//
// SAVE-COMPAT: the roster order is APPEND-ONLY. A signed deal persists the publisher's INDEX (ordinal), so an
// index, once shipped, must never be renamed/reordered/removed — only append new publishers (see the
// SHIPPED_ENUMS ledger in CLAUDE.md). Display names live in Locales/en.json and may change freely.
public static class SiliconAlleyPublishers
{
    public enum PublisherFocus { Any, Games, Office, Security }

    public readonly struct Publisher
    {
        public readonly int Index;            // persisted ordinal — APPEND-ONLY, never reorder/remove
        public readonly string Id;            // stable identifier (documentation/debug; not localized)
        public readonly string NameKey;       // locale key for the display name
        public readonly PublisherFocus Focus; // product type this publisher specializes in (Any = generalist)
        public readonly int Tier;             // fame 1..3: higher = tighter deadline, more pay + reputation

        public Publisher(int index, string id, string nameKey, PublisherFocus focus, int tier)
        {
            Index = index; Id = id; NameKey = nameKey; Focus = focus; Tier = tier;
        }
    }

    // The shipped roster. Indices are the persisted token (APPEND-ONLY). Every studio sees the generalist
    // (index 0) plus its one type-focused partner, so there are always >= 2 options and focus stays meaningful.
    public static readonly Publisher[] Roster =
    {
        new Publisher(0, "siliconalley:publisher_indielabel",  "siliconalley:publisher_indielabel",  PublisherFocus.Any,      1),
        new Publisher(1, "siliconalley:publisher_pixelforge",  "siliconalley:publisher_pixelforge",  PublisherFocus.Games,    2),
        new Publisher(2, "siliconalley:publisher_officeworks", "siliconalley:publisher_officeworks", PublisherFocus.Office,   2),
        new Publisher(3, "siliconalley:publisher_sentinel",    "siliconalley:publisher_sentinel",    PublisherFocus.Security, 2),
    };

    // ---- tuning (deal terms scale with publisher tier + the player's reputation with that publisher) ----
    public const float RepMax = 5f;            // reputation ceiling with any single publisher
    public const int BaseDeadline = 18;        // days a tier-0-ish, no-reputation deal would allow
    public const int TierDays = 3;             // each fame tier shaves this many days off the deadline
    public const float RepDays = 0.8f;         // each reputation point shaves ~this many days off the deadline
    public const int MinDeadline = 5;          // deadlines never drop below this (must stay shippable)
    public const float PublisherPayBase = 0.4f;// payout = marketPrice * tier * this * (1 + rep*RepPayK) * focus
    public const float RepPayK = 0.12f;        // how much each reputation point lifts the payout
    public const float FocusMatchBonus = 1.25f;// exact product-type match pays this multiple
    public const float RepRewardBase = 0.5f;   // reputation gained on a successful delivery ...
    public const float RepRewardTier = 0.25f;  // ... plus this per publisher tier
    public const float RepPenalty = 1f;        // reputation lost when a deal is missed

    // The product type a Silicon Alley business makes (drives focus matching). Unknown/other types map to Any
    // so they can still sign the generalist publisher.
    public static PublisherFocus FocusFor(string businessTypeName)
    {
        switch (businessTypeName)
        {
            case "siliconalley:businesstype_gamestudio": return PublisherFocus.Games;
            case "siliconalley:businesstype_softwarestudio": return PublisherFocus.Office;
            case "siliconalley:businesstype_cybersecurity": return PublisherFocus.Security;
            default: return PublisherFocus.Any;
        }
    }

    // A publisher will offer a deal for this business if it is a generalist or its focus matches the product.
    public static bool IsEligible(Publisher publisher, string businessTypeName)
        => publisher.Focus == PublisherFocus.Any || publisher.Focus == FocusFor(businessTypeName);

    // Null-safe ordinal lookup. Returns false if the index is out of range (defensive: under the append-only
    // rule a persisted index always resolves, but a corrupt/forward save could carry an unknown one).
    public static bool TryGetById(int index, out Publisher publisher)
    {
        if (index >= 0 && index < Roster.Length)
        {
            publisher = Roster[index];
            return true;
        }
        publisher = default;
        return false;
    }

    // The deal terms a publisher offers right now, derived purely from tier, the player's reputation with that
    // publisher and the focus match — so the screen preview and the sign action share one definition (mirrors
    // SiliconAlleyState.ComputeReviewScore). deadlineDays is counted from "now"; payout is the bonus paid on a
    // successful, on-time delivery (additive on top of the normal market payout).
    public static void OfferFor(Publisher publisher, string businessTypeName, float marketPrice, float playerRep,
        out int deadlineDays, out float payout, out float repReward, out float repPenalty)
    {
        var rep = Mathf.Clamp(playerRep, 0f, RepMax);
        deadlineDays = Mathf.Max(MinDeadline,
            BaseDeadline - publisher.Tier * TierDays - Mathf.RoundToInt(rep * RepDays));
        var focusFactor = publisher.Focus != PublisherFocus.Any && publisher.Focus == FocusFor(businessTypeName)
            ? FocusMatchBonus
            : 1f;
        payout = marketPrice * publisher.Tier * PublisherPayBase * (1f + rep * RepPayK) * focusFactor;
        repReward = RepRewardBase + publisher.Tier * RepRewardTier;
        repPenalty = RepPenalty;
    }
}
