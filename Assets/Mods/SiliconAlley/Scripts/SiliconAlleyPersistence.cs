using System;
using System.Threading.Tasks;
using BAModAPI;

[assembly: RegisterModClass(typeof(SiliconAlleyPersistence))]

// Persists SiliconAlleyState in the game save via GameInstance.modData (which the game serializes
// with the save). Restores on city load; writes on the game's save event. Save() is static so the
// remove-then-add below reliably de-duplicates the subscription across repeated city loads.
[ModEntryOnCityLoad]
public class SiliconAlleyPersistence : IModBigAmbitions
{
    private const string ModDataKey = "SiliconAlley";

    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext context)
    {
        var modData = SaveGameManager.Current != null ? SaveGameManager.Current.modData : null;
        SiliconAlleyState.LoadFrom(modData != null && modData.TryGetValue(ModDataKey, out var data) ? data : string.Empty);

        GlobalEvents.onSaveGame -= Save;
        GlobalEvents.onSaveGame += Save;

        context.Logger.Info("SiliconAlley: state restored from save.");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        GlobalEvents.onSaveGame -= Save;
        return Task.CompletedTask;
    }

    private static void Save()
    {
        if (SaveGameManager.Current != null && SaveGameManager.Current.modData != null)
            SaveGameManager.Current.modData[ModDataKey] = SiliconAlleyState.Serialize();
    }
}
