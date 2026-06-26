using System;
using UnityEngine;

// Issue #83: build-or-buy product dependencies (OS/runtime/framework slots) that back the interactive
// Dependencies screen (#84). This is separate from SiliconAlleyDependencies (#39), which is derived
// feature-to-tool coverage.
//
// SAVE-COMPAT: per-type Dependency.Bit values are persisted in owned/used dependency masks and vendor
// choices persist SiliconAlleyVendors.Roster ordinals. These tables are APPEND-ONLY by bit/ordinal; tune
// costs, quality bonuses and royalty rates freely, but never rename/reorder/remove shipped bits/vendors.
public static class SiliconAlleyProductDependencies
{
    public readonly struct Dependency
    {
        public readonly int Bit;                  // persisted bit position - APPEND-ONLY per business type
        public readonly string Id;                // stable identifier (documentation/debug; not localized)
        public readonly string NameKey;           // locale key for display name
        public readonly float BuildCost;          // one-off R&D cash to self-build, then reusable
        public readonly float SelfBuildQuality;   // quality-ceiling bonus when built in-house and used

        public Dependency(int bit, string id, string nameKey, float buildCost, float selfBuildQuality)
        {
            Bit = bit;
            Id = id;
            NameKey = nameKey;
            BuildCost = buildCost;
            SelfBuildQuality = selfBuildQuality;
        }
    }

    public readonly struct VendorOffer
    {
        public readonly int DependencyBit;
        public readonly int VendorOrdinal;
        public readonly float QualityBonus;
        public readonly float RoyaltyRate;

        public VendorOffer(int dependencyBit, int vendorOrdinal, float qualityBonus, float royaltyRate)
        {
            DependencyBit = dependencyBit;
            VendorOrdinal = vendorOrdinal;
            QualityBonus = qualityBonus;
            RoyaltyRate = royaltyRate;
        }
    }

    public static readonly Dependency[] Office =
    {
        new Dependency(0, "siliconalley:dep_office_osruntime",    "siliconalley:dep_office_osruntime",    12000f, 0.06f),
        new Dependency(1, "siliconalley:dep_office_appframework", "siliconalley:dep_office_appframework", 10000f, 0.05f),
        new Dependency(2, "siliconalley:dep_office_cloudbackend", "siliconalley:dep_office_cloudbackend", 11000f, 0.05f),
    };

    public static readonly Dependency[] Security =
    {
        new Dependency(0, "siliconalley:dep_security_hardenedos", "siliconalley:dep_security_hardenedos", 11000f, 0.06f),
        new Dependency(1, "siliconalley:dep_security_crypto",     "siliconalley:dep_security_crypto",      9500f, 0.05f),
        new Dependency(2, "siliconalley:dep_security_threatintel","siliconalley:dep_security_threatintel",10500f, 0.05f),
    };

    public static readonly Dependency[] Game =
    {
        new Dependency(0, "siliconalley:dep_game_runtimeos",  "siliconalley:dep_game_runtimeos",  12500f, 0.06f),
        new Dependency(1, "siliconalley:dep_game_framework",  "siliconalley:dep_game_framework",  11500f, 0.06f),
        new Dependency(2, "siliconalley:dep_game_onlinesdk",  "siliconalley:dep_game_onlinesdk",  10000f, 0.05f),
    };

    private static readonly VendorOffer[] OfficeOffers =
    {
        new VendorOffer(0, 0, 0.05f, 0.07f), // OmniWare Systems - OS Runtime
        new VendorOffer(0, 1, 0.04f, 0.05f), // NimbusHost - OS Runtime
        new VendorOffer(1, 0, 0.04f, 0.05f), // OmniWare Systems - App Framework
        new VendorOffer(1, 4, 0.05f, 0.06f), // PixelRuntime - App Framework
        new VendorOffer(2, 1, 0.05f, 0.06f), // NimbusHost - Cloud Backend
        new VendorOffer(2, 5, 0.04f, 0.05f), // PlayFabrix - Cloud Backend
    };

    private static readonly VendorOffer[] SecurityOffers =
    {
        new VendorOffer(0, 0, 0.05f, 0.06f), // OmniWare Systems - Hardened OS
        new VendorOffer(0, 3, 0.04f, 0.05f), // SentinelGrid - Hardened OS
        new VendorOffer(1, 2, 0.05f, 0.06f), // CipherWorks - Crypto Framework
        new VendorOffer(1, 3, 0.04f, 0.05f), // SentinelGrid - Crypto Framework
        new VendorOffer(2, 3, 0.05f, 0.06f), // SentinelGrid - Threat Intel Platform
        new VendorOffer(2, 1, 0.04f, 0.05f), // NimbusHost - Threat Intel Platform
    };

    private static readonly VendorOffer[] GameOffers =
    {
        new VendorOffer(0, 4, 0.05f, 0.07f), // PixelRuntime - Runtime OS Layer
        new VendorOffer(0, 0, 0.04f, 0.05f), // OmniWare Systems - Runtime OS Layer
        new VendorOffer(1, 4, 0.06f, 0.08f), // PixelRuntime - Game Framework
        new VendorOffer(1, 5, 0.05f, 0.06f), // PlayFabrix - Game Framework
        new VendorOffer(2, 5, 0.05f, 0.06f), // PlayFabrix - Online Services SDK
        new VendorOffer(2, 1, 0.04f, 0.05f), // NimbusHost - Online Services SDK
    };

    public static Dependency[] DependenciesFor(string businessTypeName)
    {
        switch (businessTypeName)
        {
            case "siliconalley:businesstype_softwarestudio": return Office;
            case "siliconalley:businesstype_cybersecurity": return Security;
            case "siliconalley:businesstype_gamestudio": return Game;
            default: return Array.Empty<Dependency>();
        }
    }

    public static VendorOffer[] OffersFor(string businessTypeName)
    {
        switch (businessTypeName)
        {
            case "siliconalley:businesstype_softwarestudio": return OfficeOffers;
            case "siliconalley:businesstype_cybersecurity": return SecurityOffers;
            case "siliconalley:businesstype_gamestudio": return GameOffers;
            default: return Array.Empty<VendorOffer>();
        }
    }

    public static int MaxCount => Mathf.Max(Office.Length, Mathf.Max(Security.Length, Game.Length));

    public static bool TryGetDependency(string businessTypeName, int bit, out Dependency dependency)
    {
        foreach (var d in DependenciesFor(businessTypeName))
        {
            if (d.Bit == bit)
            {
                dependency = d;
                return true;
            }
        }
        dependency = default;
        return false;
    }

    public static bool TryGetOffer(string businessTypeName, int dependencyBit, int vendorOrdinal, out VendorOffer offer)
    {
        foreach (var candidate in OffersFor(businessTypeName))
        {
            if (candidate.DependencyBit == dependencyBit && candidate.VendorOrdinal == vendorOrdinal)
            {
                offer = candidate;
                return true;
            }
        }
        offer = default;
        return false;
    }

    public static bool HasOffer(string businessTypeName, int dependencyBit, int vendorOrdinal)
        => TryGetOffer(businessTypeName, dependencyBit, vendorOrdinal, out _);

    public static float QualityBonus(int usedMask, int ownedMask, int[] vendorOrdinals, string businessTypeName)
    {
        if (usedMask == 0)
            return 0f;

        var sum = 0f;
        foreach (var d in DependenciesFor(businessTypeName))
        {
            if ((usedMask & (1 << d.Bit)) == 0)
                continue;
            if ((ownedMask & (1 << d.Bit)) != 0)
            {
                sum += d.SelfBuildQuality;
                continue;
            }
            var vendor = VendorOrdinalAt(vendorOrdinals, d.Bit);
            if (TryGetOffer(businessTypeName, d.Bit, vendor, out var offer))
                sum += offer.QualityBonus;
        }
        return sum;
    }

    public static float Royalty(int usedMask, int ownedMask, int[] vendorOrdinals, string businessTypeName)
    {
        if (usedMask == 0)
            return 0f;

        var sum = 0f;
        foreach (var d in DependenciesFor(businessTypeName))
        {
            if ((usedMask & (1 << d.Bit)) == 0 || (ownedMask & (1 << d.Bit)) != 0)
                continue;
            var vendor = VendorOrdinalAt(vendorOrdinals, d.Bit);
            if (TryGetOffer(businessTypeName, d.Bit, vendor, out var offer))
                sum += offer.RoyaltyRate;
        }
        return Mathf.Clamp(sum, 0f, SiliconAlleyTools.MaxRoyalty);
    }

    public static float RoyaltyFromSnapshot(int usedMask, int[] vendorOrdinals, string businessTypeName)
        => Royalty(usedMask, 0, vendorOrdinals, businessTypeName);

    private static int VendorOrdinalAt(int[] vendorOrdinals, int bit)
        => vendorOrdinals != null && bit >= 0 && bit < vendorOrdinals.Length ? vendorOrdinals[bit] : -1;
}
