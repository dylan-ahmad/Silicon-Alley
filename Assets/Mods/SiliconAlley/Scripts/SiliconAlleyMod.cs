#nullable enable
using System.Threading.Tasks;
using BAModAPI;

[assembly: RegisterModClass(typeof(SiliconAlleyMod))]

// Tier 1: register the Silicon Alley IT office business types and their products.
// The registry is shared with the city-load recovery guard so registration can self-heal if the
// initialization pass ran before the game's business/building caches were ready.
[ModEntryOnInitializationLoad]
public class SiliconAlleyMod : IModBigAmbitions
{
    private ModContext? _context;

    public string[] RelativeAssetBundlePaths => new[] { SiliconAlleyRegistry.BundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        _context = context;
        SiliconAlleyRegistry.EnsureRegistered(context);
        SiliconAlleyOptions.Register(context);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        SiliconAlleyRegistry.UnregisterAll(_context);

        if (_context != null)
            SiliconAlleyOptions.Unregister(_context);
        return Task.CompletedTask;
    }
}
