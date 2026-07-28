using UnityEngine;

// Issue #125 (epic #121): deterministic contract offers. The phone client's offer for a studio is a pure
// function of (studio key, the current 3-day offer window) — hashed with FNV-1a, no UnityEngine.Random —
// so hanging up and redialing returns the SAME terms until the window rolls over. That closes the old
// redial-farm exploit (re-rolling until a fat fee appeared) with zero persisted state: the offer is
// re-derived on every call, never stored. Different studios roll different offers in the same window, and
// the same studio rolls a fresh offer every OfferRefreshDays.
//
// SAVE-COMPAT: nothing here persists. Accepting still writes the same four contract fields (#27); the fee
// band is the #124 retune (x1.5-2.5 of scope, so a comparable ship out-earns a contract).
public static class SiliconAlleyContracts
{
    public const int OfferRefreshDays = 3;      // terms hold this many days, then the offer rolls
    public const float ScopeMin = 800f, ScopeMax = 1600f;          // a few in-game days of staffed work
    public const int DeadlineMinDays = 14, DeadlineMaxDays = 30;   // unchanged band (#114: calendar days are tight)
    public const float FeeMultMin = 1.5f, FeeMultMax = 2.5f;       // #124: bridge money, not the meta

    // The deterministic offer a studio sees in the current window. DeadlineDays is relative — the absolute
    // deadline is fixed at accept time (CurrentDay + DeadlineDays), so a shown-but-unaccepted offer never
    // ages while the window holds.
    public readonly struct ContractOffer
    {
        public readonly float Scope;
        public readonly int DeadlineDays;
        public readonly float Payout;

        public ContractOffer(float scope, int deadlineDays, float payout)
        {
            Scope = scope;
            DeadlineDays = deadlineDays;
            Payout = payout;
        }
    }

    public static ContractOffer OfferFor(string key, int day)
    {
        var window = day / OfferRefreshDays;
        var seed = key + "|" + window;
        var scope = Mathf.Lerp(ScopeMin, ScopeMax, Fraction(Fnv1a(seed + "|scope")));
        var deadline = DeadlineMinDays + (int)(Fnv1a(seed + "|days") % (uint)(DeadlineMaxDays - DeadlineMinDays + 1));
        var feeMult = Mathf.Lerp(FeeMultMin, FeeMultMax, Fraction(Fnv1a(seed + "|fee")));
        return new ContractOffer(Mathf.Round(scope), deadline, Mathf.Round(scope * feeMult));
    }

    // Map a hash to a 0..1 fraction with enough granularity for the lerps above.
    private static float Fraction(uint hash) => (hash % 10000u) / 9999f;

    // FNV-1a 32-bit — stable across processes/runtimes (string.GetHashCode is not), so redialing, saving
    // and reloading all re-derive the identical offer.
    private static uint Fnv1a(string text)
    {
        unchecked
        {
            var hash = 2166136261u;
            for (var i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
