using BAModAPI;
using MyFirstBAMod.Logic;

[assembly: RegisterModClass(typeof(MainMenuMod))]

[ModEntryMainMenu]
public class MainMenuMod : IModBigAmbitions
{
    private readonly MainMenuLogic _logic = new();

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