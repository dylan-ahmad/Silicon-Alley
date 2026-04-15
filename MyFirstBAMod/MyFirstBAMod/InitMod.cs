using BAModAPI;

[assembly: RegisterModClass(typeof(InitMod))]

[ModEntryOnInitializationLoad]
public class InitMod : ModBigAmbitionsBase
{
    private ModContext _context;

    public override Task OnLoadAsync(ModContext context)
    {
        _context = context;
        context.Logger.Info("Hello World! This mod has been initiated successfully!");
        return Task.CompletedTask;  
    }

    public override Task OnUnloadAsync()
    { 
        _context.Logger.Info("Mod unloaded successfully!");
        return Task.CompletedTask;
    }
}