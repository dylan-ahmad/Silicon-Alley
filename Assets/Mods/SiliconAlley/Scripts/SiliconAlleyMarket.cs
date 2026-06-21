using UnityEngine;

// Issue #28 (Dynamic market): a per-business-type demand cycle that makes the same product earn more when its
// category is "hot" and less when it cools — Software Inc.'s shifting market. A slow sine keyed off the game
// calendar, bounded around 1.0, phase-shifted per type so the three categories peak at different times. Folded
// into the product's market revenue (launch payout + support + post-release patches) as a NEW factor alongside
// MarketFactor/reputationFactor — the competition factor is NOT redefined. Contracts (#27) and publisher-deal
// bonuses (#23) are fixed agreed fees and are unaffected.
//
// SAVE-COMPAT: purely DERIVED from the game day — zero persisted state, no schema change. Old saves simply gain
// the dynamic market (bounded ±Amplitude, so payouts stay sensible).
public static class SiliconAlleyMarket
{
    public const float Amplitude = 0.25f;    // demand swings ±25% around 1.0
    public const float PeriodDays = 72f;     // ~one full hot→cold→hot swing every 72 in-game days

    // The current demand multiplier for a business type, in [1 - Amplitude, 1 + Amplitude]. Pure function of
    // the day, so any caller (simulator payout, phone dashboard) agrees and nothing is persisted.
    public static float DemandFactor(string businessTypeName, int day)
        => 1f + Amplitude * Mathf.Sin(2f * Mathf.PI * (day + PhaseOffset(businessTypeName)) / PeriodDays);

    // Per-type phase offset (period/3 apart) so game / office / security demand peaks don't coincide.
    private static float PhaseOffset(string businessTypeName)
    {
        switch (businessTypeName)
        {
            case "siliconalley:businesstype_gamestudio":     return 0f;
            case "siliconalley:businesstype_softwarestudio": return PeriodDays / 3f;
            case "siliconalley:businesstype_cybersecurity":  return 2f * PeriodDays / 3f;
            default:                                         return 0f;
        }
    }

    // Is the category's demand rising right now? (Tomorrow's factor vs today's — for a ▲/▼ dashboard hint.)
    public static bool IsRising(string businessTypeName, int day)
        => DemandFactor(businessTypeName, day + 1) >= DemandFactor(businessTypeName, day);
}
