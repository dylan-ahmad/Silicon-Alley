#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;
using Helpers;
using UnityEngine;

[assembly: RegisterModClass(typeof(FalconMod))]
[assembly: RegisterModClass(typeof(FalconModMainMenuLog))]
[assembly: RegisterModClass(typeof(FalconCityLoadTestMod))]

/// <summary>
/// Bump <see cref="BuildId"/> whenever you want Player.log to prove a new DLL is loaded.
/// </summary>
public static class FalconModDiagnostic
{
    public const string BuildId = "20260423-3";
}

[ModEntryOnInitializationLoad]
public class FalconMod : IModBigAmbitions
{
    private const string BundleRelativePath = "AssetBundles/falcon.unity3d";

    private static readonly string[] ModItemAssetPaths =
    {
        "Assets/Mods/FalconToy/FalconToy.asset",
        "Assets/Mods/FalconToy/GigaCounter.asset",
    };

    public string[] RelativeAssetBundlePaths => new[] { BundleRelativePath };

    private readonly List<Item> _modItems = new();

    public Task OnLoadAsync(ModContext context)
    {
        try
        {
            return OnLoadImplAsync(context);
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"[FalconMod] buildId={FalconModDiagnostic.BuildId} OnLoadAsync exception (mod will not be registered).");
            Debug.LogException(e);
            return Task.CompletedTask;
        }
    }

    private Task OnLoadImplAsync(ModContext context)
    {
        Debug.Log(
            $"[FalconMod] Initialization OnLoad start buildId={FalconModDiagnostic.BuildId} modId={context.ModId}");

        var bundle = AssetService.GetBundle(context.ModId, BundleRelativePath);
        if (bundle == null)
        {
            Debug.LogError($"[FalconMod] Bundle not loaded: modId='{context.ModId}', key='{BundleRelativePath}'.");
            return Task.CompletedTask;
        }

        _modItems.Clear();
        foreach (var itemAssetPath in ModItemAssetPaths)
        {
            Item? modItem;
            try
            {
                modItem = bundle.LoadAsset<Item>(itemAssetPath);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[FalconMod] buildId={FalconModDiagnostic.BuildId} LoadAsset failed for '{itemAssetPath}'.");
                Debug.LogException(e);
                continue;
            }

            if (modItem == null)
            {
                Debug.LogError(
                    $"[FalconMod] Item '{itemAssetPath}' not found in bundle. Bundle contents: " +
                    string.Join(", ", bundle.GetAllAssetNames()) +
                    " — fix bundle assignment, rebuild, and reinstall, or the item will be skipped.");
                continue;
            }

            LogFurnitureDataFromItem("Loaded from bundle (before RegisterModItem)", modItem);
            //ItemsGetter.RegisterModItem(modItem);
            _modItems.Add(modItem);

            var roundTrip = ItemsGetter.GetByName(modItem.itemName, suppressError: true);
            if (roundTrip == null)
            {
                Debug.LogError(
                    $"[FalconMod] buildId={FalconModDiagnostic.BuildId} RegisterModItem failed round-trip for '{modItem.itemName}'.");
            }
            else
            {
                LogFurnitureDataFromItem("After RegisterModItem (GetByName round-trip)", roundTrip);
            }
        }

        if (_modItems.Count == 0)
        {
            Debug.LogError(
                $"[FalconMod] buildId={FalconModDiagnostic.BuildId} No Item assets were loaded from the bundle; " +
                $"check the asset bundle for: {string.Join(", ", ModItemAssetPaths)}.");
        }

        return Task.CompletedTask;
    }

    private static void LogFurnitureDataFromItem(string phase, Item item)
    {
        // "Furniture" in gameplay is item.isFurniture; ItemType flags are separate (e.g. Decoration).
        Debug.Log(
            $"[FalconMod] buildId={FalconModDiagnostic.BuildId} {phase}: " +
            $"itemName='{item.itemName}' isFurniture={item.isFurniture} type={item.type} " +
            $"gridSize={item.gridSize} canBeGrabbed={item.canBeGrabbed}");
    }

    public Task OnUnloadAsync()
    {
        for (var i = _modItems.Count - 1; i >= 0; i--)
        {
            var item = _modItems[i];
            //ItemsGetter.UnregisterModItem(item.itemName);
        }

        _modItems.Clear();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Runs on the main menu so Player.log / Unity Console can confirm the mod DLL is loaded
/// (see <see cref="FalconMod" /> for item registration, which is tied to initialization load
/// and may run later than the first frame of the main menu).
/// </summary>
[ModEntryMainMenu]
public class FalconModMainMenuLog : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => new[] { "AssetBundles/falcon.unity3d" };

    public Task OnLoadAsync(ModContext context)
    {
        Debug.Log(
            $"[FalconMod] ModEntryMainMenu (DLL loaded) buildId={FalconModDiagnostic.BuildId} modId={context.ModId}");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync() => Task.CompletedTask;
}

/// <summary>
/// Test-only entry: spawns the FalconToy and GigaCounter prefabs in the world near the
/// player when the city scene finishes loading, so we can verify prefabs (mesh, materials, colliders)
/// load and instantiate correctly from the mod asset bundle. This is a smoke test and
/// bypasses the placement/interior-designer pipeline; it will be removed once the item
/// is properly wired up as placeable furniture.
/// </summary>
[ModEntryOnCityLoad]
public class FalconCityLoadTestMod : IModBigAmbitions
{
    private const string BundleRelativePath = "AssetBundles/falcon.unity3d";
    private const string FalconPrefabPath = "Assets/Mods/FalconToy/FalconToy.prefab";
    private const string GigaCounterPrefabPath = "Assets/Mods/FalconToy/GigaCounter.prefab";
    private const float SpawnDistanceInFrontOfPlayer = 2f;
    private const float GigaCounterSpawnOffsetRight = 2.5f;
    private const string ExpectedFalconItemName = "ba:itemname_falcontoy";
    private const string ExpectedGigaCounterItemName = "falconmod_gigacounter";

    public string[] RelativeAssetBundlePaths => new[] { BundleRelativePath };

    private GameObject? _spawnedFalcon;
    private GameObject? _spawnedGigaCounter;

    public Task OnLoadAsync(ModContext context)
    {
        Debug.Log(
            $"[FalconMod] CityLoad start buildId={FalconModDiagnostic.BuildId} modId={context.ModId}");

        LogFurnitureStateFromItemsGetter(
            "CityLoad (before test spawn, from ItemsGetter)",
            ExpectedFalconItemName);
        LogFurnitureStateFromItemsGetter(
            "CityLoad (before test spawn, from ItemsGetter)",
            ExpectedGigaCounterItemName);

        var player = PlayerHelper.PlayerController;
        Vector3 spawnPosition;
        Vector3 gigaCounterSpawnPosition;
        Quaternion spawnRotation;

        if (player != null)
        {
            var playerTransform = player.transform;
            spawnPosition = playerTransform.position
                + playerTransform.forward * SpawnDistanceInFrontOfPlayer;
            gigaCounterSpawnPosition = spawnPosition + playerTransform.right * GigaCounterSpawnOffsetRight;
            spawnRotation = Quaternion.LookRotation(-playerTransform.forward);
        }
        else
        {
            Debug.LogWarning(
                "[FalconMod] PlayerController not available on city load; spawning test prefabs at origin.");
            spawnPosition = Vector3.zero;
            gigaCounterSpawnPosition = new Vector3(GigaCounterSpawnOffsetRight, 0f, 0f);
            spawnRotation = Quaternion.identity;
        }

        _spawnedFalcon = AssetService.Spawn(
            context.ModId,
            BundleRelativePath,
            FalconPrefabPath,
            spawnPosition,
            spawnRotation);

        if (_spawnedFalcon == null)
        {
            Debug.LogError(
                $"[FalconMod] buildId={FalconModDiagnostic.BuildId} Failed to spawn FalconToy prefab. modId='{context.ModId}', " +
                $"bundleKey='{BundleRelativePath}', prefabPath='{FalconPrefabPath}'.");
        }
        else
        {
            _spawnedFalcon.name = "FalconToy (Test Spawn)";
            var hasItemController = _spawnedFalcon.GetComponent<ItemController>() != null;
            // Raw AssetService.Spawn is not the same as interior placement; a placed furniture
            // instance would also have ItemController on the root in normal gameplay.
            Debug.Log(
                $"[FalconMod] buildId={FalconModDiagnostic.BuildId} Test-spawn: position={spawnPosition} " +
                $"rootHasItemController={hasItemController} " +
                $"(if false, prefab is mesh/collider only — placement pipeline not used here).");
            LogFurnitureStateFromItemsGetter(
                "CityLoad (after test spawn, from ItemsGetter)",
                ExpectedFalconItemName);
        }

        _spawnedGigaCounter = AssetService.Spawn(
            context.ModId,
            BundleRelativePath,
            GigaCounterPrefabPath,
            gigaCounterSpawnPosition,
            spawnRotation);

        if (_spawnedGigaCounter == null)
        {
            Debug.LogError(
                $"[FalconMod] buildId={FalconModDiagnostic.BuildId} Failed to spawn GigaCounter prefab. modId='{context.ModId}', " +
                $"bundleKey='{BundleRelativePath}', prefabPath='{GigaCounterPrefabPath}'.");
        }
        else
        {
            _spawnedGigaCounter.name = "GigaCounter (Test Spawn)";
            var hasItemController = _spawnedGigaCounter.GetComponent<ItemController>() != null;
            Debug.Log(
                $"[FalconMod] buildId={FalconModDiagnostic.BuildId} Test-spawn GigaCounter: position={gigaCounterSpawnPosition} " +
                $"rootHasItemController={hasItemController}.");
            LogFurnitureStateFromItemsGetter(
                "CityLoad (after GigaCounter test spawn, from ItemsGetter)",
                ExpectedGigaCounterItemName);
        }

        return Task.CompletedTask;
    }

    private static void LogFurnitureStateFromItemsGetter(string phase, string expectedItemName)
    {
        var item = ItemsGetter.GetByName(expectedItemName, suppressError: true);
        if (item == null)
        {
            Debug.LogError(
                $"[FalconMod] buildId={FalconModDiagnostic.BuildId} {phase}: " +
                $"no Item for '{expectedItemName}' (Initialization mod may not have run).");
            return;
        }

        Debug.Log(
            $"[FalconMod] buildId={FalconModDiagnostic.BuildId} {phase}: " +
            $"itemName='{item.itemName}' isFurniture={item.isFurniture} type={item.type} " +
            $"(isFurniture=true is required for " + "\"Place\" in the package UI when holding the box)");
    }

    public Task OnUnloadAsync()
    {
        if (_spawnedFalcon != null)
        {
            UnityEngine.Object.Destroy(_spawnedFalcon);
            _spawnedFalcon = null;
        }

        if (_spawnedGigaCounter != null)
        {
            UnityEngine.Object.Destroy(_spawnedGigaCounter);
            _spawnedGigaCounter = null;
        }

        return Task.CompletedTask;
    }
}
