// Issue #83: competitor vendors that can license product dependencies to the player's studio.
//
// SAVE-COMPAT: roster order is APPEND-ONLY. Dependency choices persist vendor ordinals, so once a vendor
// ships its Index must never be renamed/reordered/removed. Display names live in Locales/en.json.
public static class SiliconAlleyVendors
{
    public readonly struct Vendor
    {
        public readonly int Index;       // persisted ordinal - APPEND-ONLY
        public readonly string Id;       // stable identifier (documentation/debug; not localized)
        public readonly string NameKey;  // locale key for display name

        public Vendor(int index, string id, string nameKey)
        {
            Index = index;
            Id = id;
            NameKey = nameKey;
        }
    }

    public static readonly Vendor[] Roster =
    {
        new Vendor(0, "siliconalley:vendor_omniware",    "siliconalley:vendor_omniware"),
        new Vendor(1, "siliconalley:vendor_nimbushost",  "siliconalley:vendor_nimbushost"),
        new Vendor(2, "siliconalley:vendor_cipherworks", "siliconalley:vendor_cipherworks"),
        new Vendor(3, "siliconalley:vendor_sentinelgrid","siliconalley:vendor_sentinelgrid"),
        new Vendor(4, "siliconalley:vendor_pixelruntime","siliconalley:vendor_pixelruntime"),
        new Vendor(5, "siliconalley:vendor_playfabrix",  "siliconalley:vendor_playfabrix"),
    };

    public static bool TryGetById(int index, out Vendor vendor)
    {
        if (index >= 0 && index < Roster.Length)
        {
            vendor = Roster[index];
            return true;
        }
        vendor = default;
        return false;
    }
}
