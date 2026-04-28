using BAModAPI;
using SliderInMyDMs.Logic;

[assembly: RegisterModClass(typeof(OptionsMod))]

[ModEntryMainMenu]
public class OptionsMod : IModBigAmbitions
{
    private readonly OptionsLogic _logic = new();

    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext context)
    {
        _logic.Initialize(context);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        _logic.Shutdown();
        return Task.CompletedTask;
    }
}
