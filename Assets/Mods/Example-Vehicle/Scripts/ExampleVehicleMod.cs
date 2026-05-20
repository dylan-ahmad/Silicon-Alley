#nullable enable
using System.Threading.Tasks;
using BackAlleyDealer;
using BAModAPI;
using BAModAPI.Services;
using UnityEngine;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(ExampleVehicleMod))]

[ModEntryOnInitializationLoad]
public class ExampleVehicleMod : IModBigAmbitions
{
    private const string BundleKey = "AssetBundles/example-vehicle.unity3d";
    private const string TurboHonzaAssetPath = "Assets/Mods/Example-Vehicle/TurboHonza.asset";

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    private VehicleType? turboHonzaVehicleType;

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            Debug.LogError($"ExampleVehicleMod: failed to load bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        turboHonzaVehicleType = bundle.LoadAsset<VehicleType>(TurboHonzaAssetPath);
        if (turboHonzaVehicleType == null)
        {
            Debug.LogError($"ExampleVehicleMod: failed to load vehicle type '{TurboHonzaAssetPath}'.");
            return Task.CompletedTask;
        }

        ModdingAPI.RegisterModVehicleType(turboHonzaVehicleType);
        BackAlleyDealerInit.Instance.RegisterVehicle(turboHonzaVehicleType.vehicleTypeName);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        if (turboHonzaVehicleType == null)
            return Task.CompletedTask;
        
        ModdingAPI.UnregisterModVehicleType(turboHonzaVehicleType.vehicleTypeName);
        BackAlleyDealerInit.Instance.UnregisterVehicle(turboHonzaVehicleType.vehicleTypeName);

        return Task.CompletedTask;
    }
}
