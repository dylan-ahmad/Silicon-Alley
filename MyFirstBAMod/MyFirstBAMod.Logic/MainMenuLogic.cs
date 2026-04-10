using BAModAPI;

namespace MyFirstBAMod.Logic;

public class MainMenuLogic
{
    private ModContext _context = null!;

    public void Initialize(ModContext context)
    {
        _context = context;
        _context.Logger.Info("Hello Main Menu!");
    }

    public void Shutdown()
    {
        _context.Logger.Info("Goodbye Main Menu!");
    }
}