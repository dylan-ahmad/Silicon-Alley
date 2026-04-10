using BAModAPI;
using BAModAPI.Services;
using Helpers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MyFirstBAMod.Logic;

public class WindmillShowcaseHandler
{
    private string _assetBundlePath = null!;
    private ModContext _context = null!;
    private GameObject? _windmillBase;

    public void Start(ModContext context, string assetBundlePath)
    {
        _context = context;
        _assetBundlePath = assetBundlePath;
        GlobalEvents.RegisterOnGameLoadedLateCallback(SpawnWindmill);
    }

    public void Stop()
    {
        if (_windmillBase != null)
            Object.Destroy(_windmillBase);
    }

    private void SpawnWindmill()
    {
        _windmillBase = AssetService.Spawn(
            _context.ModId,
            _assetBundlePath,
            "Assets/WindmillBase.prefab",
            PlayerHelper.GetPosition(),
            Quaternion.identity);
    }
}