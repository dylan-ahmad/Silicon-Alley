#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;

[assembly: RegisterModClass(typeof(ExampleFurnitureMod))]

[ModEntryOnInitializationLoad]
public class ExampleFurnitureMod : IModBigAmbitions
{
    private const string BundleKey = "AssetBundles/example-furniture.unity3d";
    public string[] RelativeAssetBundlePaths => new[] { BundleKey };
    private Item modItem;

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        modItem = bundle.LoadAsset<Item>("Assets/Mods/Example-Furniture/GigaCounter.asset");
        ItemsGetter.RegisterModItem(modItem);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        ItemsGetter.UnregisterModItem(modItem.itemName);
        return Task.CompletedTask;
    }
}