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
        // Issue #64: inject the mod's pages into BA's native Help System. Harmless no-op this early (the
        // help UI isn't alive yet) — the city-load guard re-runs it once HelpSystem exists and actually
        // performs the injection (and subscribes the language-change re-inject hook) then.
        SiliconAlleyHelp.EnsureRegistered(context);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        SiliconAlleyRegistry.UnregisterAll(_context);
        SiliconAlleyHelp.Unregister();

        if (_context != null)
            SiliconAlleyOptions.Unregister(_context);
        return Task.CompletedTask;
    }
}
