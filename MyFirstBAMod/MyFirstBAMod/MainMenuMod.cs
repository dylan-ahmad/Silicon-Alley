using BAModAPI;
using MyFirstBAMod.Logic;

[assembly: RegisterModClass(typeof(MainMenuMod))]

[ModEntryMainMenu]
public class MainMenuMod : ModBigAmbitionsBase
{
    private readonly MainMenuLogic _logic = new();

    public override Task OnLoadAsync(ModContext context)
    {
        _logic.Initialize(context);
        return Task.CompletedTask;
    }

    public override Task OnUnloadAsync()
    {
        _logic.Shutdown();
        return Task.CompletedTask;
    }
}