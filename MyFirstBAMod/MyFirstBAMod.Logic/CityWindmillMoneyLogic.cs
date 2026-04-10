using BAModAPI;

namespace MyFirstBAMod.Logic;

public class CityWindmillMoneyLogic
{
    private readonly PlaytimeNotificationShowcaseHandler _playtimeNotificationShowcaseHandler = new();
    private readonly WindmillShowcaseHandler _windmillShowcaseHandler = new();

    private ModContext _context = null!;

    public void Initialize(ModContext context, string assetBundlePath)
    {
        _context = context;
        _context.Logger.Info($"Hello City! Welcome to version {GameVersion.GetCurrent().buildNumber}");
        
        _windmillShowcaseHandler.Start(_context, assetBundlePath);
        _playtimeNotificationShowcaseHandler.Start();
        MoneyShowcaseHandler.Start();
    }

    public void Shutdown()
    {
        MoneyShowcaseHandler.Stop();
        _playtimeNotificationShowcaseHandler.Stop();
        _windmillShowcaseHandler.Stop();
        
        _context.Logger.Info("Goodbye City");
    }
}