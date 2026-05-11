#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;
using UnityEngine;

[assembly: RegisterModClass(typeof(ExampleBusinessTypeMod))]
[assembly: RegisterModClass(typeof(ExampleBusinessTypeCityMod))]

[ModEntryOnInitializationLoad]
public class ExampleBusinessTypeMod : IModBigAmbitions
{
    private const string BundleKey = "AssetBundles/example-businesstype.unity3d";
    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    private BusinessType? modBusinessType;
    private Item? modItem;

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        modItem = bundle.LoadAsset<Item>("Assets/Mods/Example-BusinessType/FalconToy.asset");
        if (modItem != null)
            ItemsGetter.RegisterModItem(modItem);

        modBusinessType = bundle.LoadAsset<BusinessType>("Assets/Mods/Example-BusinessType/ToyStore.asset");
        if (modBusinessType != null)
            ModdingAPI.RegisterModBusinessType(modBusinessType);

        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        if (modBusinessType != null)
            ModdingAPI.UnregisterModBusinessType(modBusinessType);

        if (modItem != null)
            ItemsGetter.UnregisterModItem(modItem.itemName);

        return Task.CompletedTask;
    }
}

[ModEntryOnCityLoad]
public class ExampleBusinessTypeCityMod : IModBigAmbitions
{
    private const string FalconToyItemName = "example-businesstype:itemname_falcontoy";
    private const string RoundedShelfItemName = "ba:itemname_roundedshelf";
    private const string CheapGiftItemName = "ba:itemname_cheapgift";
    private const string ExpensiveGiftItemName = "ba:itemname_expensivegift";
    private const string ExpensiveFlowersItemName = "ba:itemname_expensiveflower";

    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    private readonly Dictionary<Item, string[]> patchedShowcaseShelves = new();

    public Task OnLoadAsync(ModContext context)
    {
        PatchShowcaseShelves();
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        RestoreShowcaseShelves();
        return Task.CompletedTask;
    }

    private void PatchShowcaseShelves()
    {
        if (ItemsGetter.AllItems == null)
            return;

        foreach (var item in ItemsGetter.AllItems)
        {
            if (!ShouldPatchShowcaseShelf(item) || item.itemsThatCanShowcase.Contains(FalconToyItemName))
                continue;

            ShelfController.RegisterItemToShow(FalconToyItemName, item.itemName,
                item.itemName == RoundedShelfItemName ? ExpensiveFlowersItemName : CheapGiftItemName);
            patchedShowcaseShelves[item] = item.itemsThatCanShowcase.ToArray();
            item.itemsThatCanShowcase = item.itemsThatCanShowcase.Concat(new[] { FalconToyItemName }).ToArray();
        }
    }

    private static bool ShouldPatchShowcaseShelf(Item item)
    {
        if (item == null || item.itemsThatCanShowcase == null)
            return false;

        if (item.itemName == RoundedShelfItemName)
            return true;

        return (item.type & ItemType.ShowcaseShelf) != 0
            && (item.itemsThatCanShowcase.Contains(CheapGiftItemName)
                || item.itemsThatCanShowcase.Contains(ExpensiveGiftItemName));
    }

    private void RestoreShowcaseShelves()
    {
        foreach (var patchedShelf in patchedShowcaseShelves)
            patchedShelf.Key.itemsThatCanShowcase = patchedShelf.Value;
        
        ShelfController.UnregisterItemToShow(FalconToyItemName);

        patchedShowcaseShelves.Clear();
    }
}