#nullable enable
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
    private Item? _modItem;

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
            throw new System.InvalidOperationException($"Bundle not found: {BundleKey}");

        _modItem = bundle.LoadAsset<Item>("Assets/Mods/Example-Furniture/GigaCounter.asset");
        if (_modItem == null)
            throw new System.InvalidOperationException(
                "Item asset not found: Assets/Mods/Example-Furniture/GigaCounter.asset");

        ItemsGetter.RegisterModItem(_modItem);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        if (_modItem == null)
            return Task.CompletedTask;

        ItemsGetter.UnregisterModItem(_modItem.itemName);
        return Task.CompletedTask;
    }
}
