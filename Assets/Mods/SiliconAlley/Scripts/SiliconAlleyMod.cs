#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;
using UnityEngine;

[assembly: RegisterModClass(typeof(SiliconAlleyMod))]

// Tier 1: register the Silicon Alley IT office business types and their products.
// Each business reuses the built-in OfficeBusinessSimulator (clients are served by employees
// whose primary skill is ba:skill_programmer); see docs/DESIGN.md.
[ModEntryOnInitializationLoad]
public class SiliconAlleyMod : IModBigAmbitions
{
    private const string BundleKey = "AssetBundles/siliconalley.unity3d";

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    // Items are registered before the business types that reference them by itemName.
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

    private readonly List<Item> _registeredItems = new();
    private readonly List<BusinessType> _registeredBusinesses = new();
    private ModContext? _context;

    public Task OnLoadAsync(ModContext context)
    {
        _context = context;
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Error("SiliconAlley: asset bundle failed to load; nothing registered.");
            return Task.CompletedTask;
        }

        foreach (var path in ItemAssetPaths)
        {
            var item = bundle.LoadAsset<Item>(path);
            if (item == null)
            {
                context.Logger.Error($"SiliconAlley: item asset not found in bundle: {path}");
                continue;
            }

            ItemsGetter.RegisterModItem(item);
            _registeredItems.Add(item);
        }

        foreach (var path in BusinessAssetPaths)
        {
            var business = bundle.LoadAsset<BusinessType>(path);
            if (business == null)
            {
                context.Logger.Error($"SiliconAlley: business asset not found in bundle: {path}");
                continue;
            }

            // Tier 2/3: drive every Silicon Alley business with our custom project simulator
            // (assigned in code; see SiliconAlleyOfficeSimulator).
            business.simulator = ScriptableObject.CreateInstance<SiliconAlleyOfficeSimulator>();

            if (ModdingAPI.RegisterModBusinessType(business))
                _registeredBusinesses.Add(business);
            else
                context.Logger.Error($"SiliconAlley: failed to register business '{business.businessTypeName}'.");
        }

        context.Logger.Info(
            $"SiliconAlley: registered {_registeredItems.Count} item(s) and {_registeredBusinesses.Count} business type(s).");

        SiliconAlleyOptions.Register(context);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        foreach (var business in _registeredBusinesses)
            ModdingAPI.UnregisterModBusinessType(business);

        foreach (var item in _registeredItems)
            ItemsGetter.UnregisterModItem(item.itemName);

        _registeredBusinesses.Clear();
        _registeredItems.Clear();

        if (_context != null)
            SiliconAlleyOptions.Unregister(_context);
        return Task.CompletedTask;
    }
}
