using UnityEngine;

// Issue #38 (Market targeting): the audience segment a product is designed for, set in the design wizard. The
// segment shifts the product's baseline PRICE↔VOLUME shape before any marketing: a niche/pro audience pays more
// per unit but is smaller (high price, low volume); a mass audience is larger but lower-margin (low price, high
// volume). The price factor scales the launch payout; the volume factor scales the launch installed-base jump
// (so it feeds recurring support). Persisted in BusinessState.SegmentId (the ordinal slot reserved by #40).
//
// SAVE-COMPAT: the ordinals are APPEND-ONLY and were frozen by #40 (0=Broad, 1=Enterprise, 2=Prosumer,
// 3=Consumer) — never rename/reorder/remove; add new segments only by appending (and record them in CLAUDE.md).
// SegmentId 0 (Broad / old saves) ⇒ PriceFactor 1.0 and VolumeFactor 1.0 ⇒ payout + launch identical to before
// this issue. The price/volume factors and market-size indicators are tunable data (NOT persisted). Display
// names live in Locales/en.json and may change freely.
public static class SiliconAlleySegments
{
    public readonly struct Segment
    {
        public readonly int Ordinal;        // persisted token (= SegmentId) — APPEND-ONLY, never reorder/remove
        public readonly string Id;          // stable identifier (documentation/debug; not localized)
        public readonly string NameKey;     // locale key for the display name
        public readonly string MarketSizeKey; // locale key for the legible market-size indicator
        public readonly float PriceFactor;  // ×launch payout (per-unit price effect); Broad = 1.0
        public readonly float VolumeFactor; // ×launch installed-base units (market size); Broad = 1.0

        public Segment(int ordinal, string id, string nameKey, string marketSizeKey, float priceFactor, float volumeFactor)
        {
            Ordinal = ordinal; Id = id; NameKey = nameKey; MarketSizeKey = marketSizeKey;
            PriceFactor = priceFactor; VolumeFactor = volumeFactor;
        }
    }

    // The shipped segments. Ordinal = array index = the persisted token (APPEND-ONLY). Broad is the neutral
    // default (1.0 / 1.0); the others trade price against volume so the choice is a real strategy, not a free win.
    public static readonly Segment[] All =
    {
        new Segment(0, "siliconalley:segment_broad",      "siliconalley:segment_broad",      "siliconalley:segmentsize_broad",      1.0f, 1.0f),
        new Segment(1, "siliconalley:segment_enterprise", "siliconalley:segment_enterprise", "siliconalley:segmentsize_enterprise", 2.0f, 0.5f),
        new Segment(2, "siliconalley:segment_prosumer",   "siliconalley:segment_prosumer",   "siliconalley:segmentsize_prosumer",   1.3f, 0.85f),
        new Segment(3, "siliconalley:segment_consumer",   "siliconalley:segment_consumer",   "siliconalley:segmentsize_consumer",   0.7f, 1.7f),
    };

    public static int Count => All.Length;

    // Bounds-checked lookup. An out-of-range/forward ordinal (corrupt or newer save) defaults to Broad so the
    // product always has a concrete, neutral market shape.
    public static Segment Get(int segmentId)
        => segmentId >= 0 && segmentId < All.Length ? All[segmentId] : All[0];

    // The per-unit price multiplier and the market-volume multiplier for a segment. SegmentId 0 ⇒ 1.0 (neutral).
    public static float PriceFactor(int segmentId) => Get(segmentId).PriceFactor;
    public static float VolumeFactor(int segmentId) => Get(segmentId).VolumeFactor;
}
