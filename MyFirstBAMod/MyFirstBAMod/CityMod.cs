using BAModAPI;
using MyFirstBAMod.Logic;

[assembly: RegisterModClass(typeof(CityMod))]

[ModEntryOnCityLoad]
public class CityMod : IModBigAmbitions
{
    private readonly CityWindmillMoneyLogic _logic = new();

    public string[] RelativeAssetBundlePaths => new[] { "AssetBundles/testbundle.unity3d" };

    public Task OnLoadAsync(ModContext context)
    {
        _logic.Initialize(context, RelativeAssetBundlePaths[0]);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        _logic.Shutdown();
        return Task.CompletedTask;
    }
}