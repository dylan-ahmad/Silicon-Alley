#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;
using Buildings;
using Helpers;
using UnityEngine;

[assembly: RegisterModClass(typeof(SiliconAlleyRegistrationGuard))]

public static class SiliconAlleyRegistry
{
    public const string BundleKey = "AssetBundles/siliconalley.unity3d";
    public const string BusinessTypePrefix = "siliconalley:";

    private const string OfficeBuildingType = "ba:buildingtype_office";

    private static readonly string[] ItemAssetPaths =
    {
        "Assets/Mods/SiliconAlley/SoftwareLicense.asset",
        "Assets/Mods/SiliconAlley/SecurityAudit.asset",
        "Assets/Mods/SiliconAlley/VideoGame.asset",
    };

    private static readonly string[] BusinessAssetPaths =
    {
        "Assets/Mods/SiliconAlley/SoftwareStudio.asset",
        "Assets/Mods/SiliconAlley/CyberSecurityFirm.asset",
        "Assets/Mods/SiliconAlley/GameStudio.asset",
    };

    public static readonly string[] ExpectedBusinessTypeNames =
    {
        "siliconalley:businesstype_softwarestudio",
        "siliconalley:businesstype_cybersecurity",
        "siliconalley:businesstype_gamestudio",
    };

    private static readonly List<Item> RegisteredItems = new List<Item>();
    private static readonly List<BusinessType> RegisteredBusinesses = new List<BusinessType>();
    private static string _lastHealthLine = "";
    private static bool _themeLoaded;

    public static int ExpectedItemCount => ItemAssetPaths.Length;
    public static int ExpectedBusinessCount => BusinessAssetPaths.Length;
    public static int ExpectedAssetCount => ItemAssetPaths.Length + BusinessAssetPaths.Length;

    public static bool BundleLoaded { get; private set; }
    public static bool AllExpectedAssetsFound { get; private set; }
    public static string LastError { get; private set; } = "";

    public static int RegisteredItemCount => RegisteredItems.Count;
    public static int RegisteredBusinessCount => ExpectedBusinessTypeNames.Count(name => BusinessTypeHelper.GetData(name) != null);
    public static bool OfficeAvailabilityPatched => ExpectedBusinessTypeNames.All(IsAvailableInOffice);
    public static bool OfficeAvailabilityKnown => BuildingTypeHelper.GetData(OfficeBuildingType) != null;
    public static bool HasRegistrationProblem => !BundleLoaded
                                                 || !AllExpectedAssetsFound
                                                 || RegisteredBusinessCount < ExpectedBusinessCount
                                                 || (OfficeAvailabilityKnown && !OfficeAvailabilityPatched);
    public static bool IsReady => !HasRegistrationProblem;

    public static bool EnsureRegistered(ModContext context)
    {
        LastError = "";

        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            BundleLoaded = false;
            AllExpectedAssetsFound = false;
            LastError = "asset bundle missing; expected " + ExpectedBundlePaths(context);
            LogHealth(context, 0);
            return false;
        }

        BundleLoaded = true;

        if (!_themeLoaded)
        {
            SiliconAlleyTheme.Load(bundle, context.Logger);
            _themeLoaded = true;
        }

        var expectedAssetsFound = CountExpectedAssets(bundle, context);
        AllExpectedAssetsFound = expectedAssetsFound == ExpectedAssetCount;

        RegisterItems(bundle, context);
        RegisterBusinesses(bundle, context);
        LogHealth(context, expectedAssetsFound);

        return IsReady;
    }

    public static void UnregisterAll(ModContext? context)
    {
        foreach (var business in RegisteredBusinesses.ToArray())
            ModdingAPI.UnregisterModBusinessType(business);

        foreach (var item in RegisteredItems.ToArray())
            ItemsGetter.UnregisterModItem(item.itemName);

        RegisteredBusinesses.Clear();
        RegisteredItems.Clear();
        BundleLoaded = false;
        AllExpectedAssetsFound = false;
        LastError = "";
        _themeLoaded = false;
        _lastHealthLine = "";

        context?.Logger.Info("SiliconAlley: registry unregistered.");
    }

    public static string NoStudioLocalizationKey(string normalKey, string registrationFailedKey)
        => HasRegistrationProblem ? registrationFailedKey : normalKey;

    private static void RegisterItems(AssetBundle bundle, ModContext context)
    {
        foreach (var path in ItemAssetPaths)
        {
            var item = bundle.LoadAsset<Item>(path);
            if (item == null)
            {
                SetError("item asset missing: " + path);
                context.Logger.Error("SiliconAlley: item asset not found in bundle: " + path);
                continue;
            }

            if (RegisteredItems.Any(registered => registered.itemName == item.itemName))
                continue;

            ItemsGetter.RegisterModItem(item);
            RegisteredItems.Add(item);
        }
    }

    private static void RegisterBusinesses(AssetBundle bundle, ModContext context)
    {
        foreach (var path in BusinessAssetPaths)
        {
            var business = bundle.LoadAsset<BusinessType>(path);
            if (business == null)
            {
                SetError("business asset missing: " + path);
                context.Logger.Error("SiliconAlley: business asset not found in bundle: " + path);
                continue;
            }

            if (!(business.simulator is SiliconAlleyOfficeSimulator))
                business.simulator = ScriptableObject.CreateInstance<SiliconAlleyOfficeSimulator>();

            if (ModdingAPI.RegisterModBusinessType(business))
            {
                TrackBusiness(business);
                continue;
            }

            if (BusinessTypeHelper.GetData(business.businessTypeName) != null)
            {
                TrackBusiness(business);
                continue;
            }

            SetError("business registration failed: " + business.businessTypeName);
            context.Logger.Error("SiliconAlley: failed to register business '" + business.businessTypeName + "'.");
        }
    }

    private static int CountExpectedAssets(AssetBundle bundle, ModContext context)
    {
        var assets = new HashSet<string>(bundle.GetAllAssetNames(), StringComparer.OrdinalIgnoreCase);
        var found = 0;

        foreach (var path in ItemAssetPaths.Concat(BusinessAssetPaths))
        {
            if (assets.Contains(path))
            {
                found++;
                continue;
            }

            SetError("asset missing from bundle manifest: " + path);
            context.Logger.Error("SiliconAlley: expected asset missing from bundle manifest: " + path);
        }

        return found;
    }

    private static bool IsAvailableInOffice(string businessTypeName)
    {
        var office = BuildingTypeHelper.GetData(OfficeBuildingType);
        return office?.availableBusinessTypes != null
               && Array.IndexOf(office.availableBusinessTypes, businessTypeName) >= 0;
    }

    private static void TrackBusiness(BusinessType business)
    {
        if (RegisteredBusinesses.Any(registered => registered.businessTypeName == business.businessTypeName))
            return;
        RegisteredBusinesses.Add(business);
    }

    private static void SetError(string error)
    {
        if (string.IsNullOrEmpty(LastError))
            LastError = error;
    }

    private static void LogHealth(ModContext context, int expectedAssetsFound)
    {
        var healthLine =
            "SiliconAlley: registration health: " +
            "bundle=" + (BundleLoaded ? "loaded" : "missing") +
            ", expectedAssets=" + expectedAssetsFound + "/" + ExpectedAssetCount +
            ", items=" + RegisteredItemCount + "/" + ExpectedItemCount +
            ", businessTypes=" + RegisteredBusinessCount + "/" + ExpectedBusinessCount +
            ", officeAvailability=" + OfficeAvailabilityLabel() +
            (string.IsNullOrEmpty(LastError) ? "" : ", error=" + LastError);

        if (healthLine == _lastHealthLine)
            return;

        _lastHealthLine = healthLine;
        if (IsReady)
            context.Logger.Info(healthLine);
        else
            context.Logger.Error(healthLine);
    }

    private static string ExpectedBundlePaths(ModContext context)
    {
        var platformPath = Path.Combine(context.ModRootPath, "AssetBundles", "Windows", "siliconalley.unity3d");
        var flatPath = Path.Combine(context.ModRootPath, "AssetBundles", "siliconalley.unity3d");
        return "'" + platformPath + "' or '" + flatPath + "'";
    }

    private static string OfficeAvailabilityLabel()
    {
        if (!OfficeAvailabilityKnown)
            return "pending";
        return OfficeAvailabilityPatched ? "patched" : "missing";
    }
}

// City-load recovery pass: if initialization registration ran before the game's building/business caches
// were ready, this reapplies the same idempotent registration before the player opens the start-company UI.
[ModEntryOnCityLoad]
public class SiliconAlleyRegistrationGuard : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => new[] { SiliconAlleyRegistry.BundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        SiliconAlleyRegistry.EnsureRegistered(context);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        return Task.CompletedTask;
    }
}
