# MyFirstBAMod

## Prerequisites
- Windows
- .NET SDK 8.0.100 or newer in the 8.0 feature band
- Big Ambitions installed through Steam
- An IDE such as JetBrains Rider or Visual Studio

## Open And Build In Your IDE
1. Clone the repository.
2. Open `MyFirstBAMod\MyFirstBAMod.sln` in your IDE.
3. Build the solution from the IDE build action.

## If You See Missing DLL Errors
If build errors mention `BigAmbitions.dll`, `BigAmbitions.ModAPI.dll`, `HGExtensions.dll`, `HGPlugins.dll`, or Unity DLLs, set the game managed folder path for your machine.

Default path used by this repo:
`C:\Program Files (x86)\Steam\steamapps\common\Big Ambitions\Big Ambitions_Data\Managed`

## Configure Game Path In IDE (No Command Line)
1. In the `MyFirstBAMod` folder (next to `MyFirstBAMod.sln`), create a file named `Directory.Build.props`.
2. Paste this content and update the path if your Steam library is in another location:

```xml
<Project>
  <PropertyGroup>
    <GameManagedDir>D:\SteamLibrary\steamapps\common\Big Ambitions\Big Ambitions_Data\Managed</GameManagedDir>
  </PropertyGroup>
</Project>
```

3. Save the file.
4. Reload the solution in your IDE if prompted.
5. Build the solution again from the IDE.

## Use The Mod
1. Build the solution.
2. Open `MyFirstBAMod\FinishedModFolder`.
3. Copy the full contents of `FinishedModFolder` into your local mods folder:
`C:\Users\[user]\AppData\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal`

`FinishedModFolder` includes required supporting data:
- `MyFirstBAMod.dll`
- `MyFirstBAMod.Logic.dll`
- `AssetBundles\...`
- `Locales\...`
- `Dependencies\...`

Important:
- Keep the folder structure intact.
- Dependency assemblies should be inside `Dependencies`, not in the root mod folder.

## Mod features
### Localization
How it works now:
- Localization keys are used in code, for example in `MyFirstBAMod.Logic/Handlers/MoneyShowcaseHandler.cs` and `MyFirstBAMod.Logic/Handlers/PlaytimeNotificationShowcaseHandler.cs`.
- Key values are stored in `MyFirstBAMod/FinishedModFolder/Locales/en.json` and `MyFirstBAMod/FinishedModFolder/Locales/nl.json`.

How to add it yourself:
1. Create a new key in each locale file. New locales can be added using the language short (for example `es.json` for Spanish).
2. Use that key in code when showing UI/notifications.
3. Pass dynamic values with placeholders (for example `{moneyAmount}`) using a data object.
4. Rebuild and copy the updated `Locales` folder with your mod.

### Asset loading
How it works now:
- Asset bundle path is declared in `MyFirstBAMod/MyFirstBAMod/CityMod.cs` using `RelativeAssetBundlePaths`.
- The asset is spawned in `MyFirstBAMod.Logic/Handlers/WindmillShowcaseHandler.cs` using `AssetService.Spawn`.

How to add it yourself:
1. Place your `.unity3d` bundle in `FinishedModFolder/AssetBundles`.
2. Add the relative bundle path to `RelativeAssetBundlePaths` in your mod entry class.
3. Spawn your prefab with the exact asset path from the bundle (for example `Assets/YourPrefab.prefab`).
4. Make sure to destroy spawned objects in `OnUnloadAsync`/`Stop`.

### Lifecycle
How it works now:
- Entry points are `MainMenuMod` (`[ModEntryMainMenu]`) and `CityMod` (`[ModEntryOnCityLoad]`).
- Startup is done in `OnLoadAsync`; cleanup is done in `OnUnloadAsync`.
- Long-running hooks are registered/unregistered in handler `Start`/`Stop` methods.
- UnityLifecycleProvider lets you subscribe to `Update` events.

How to add it yourself:
1. Pick the correct entry attribute for the scope your feature should run in.
2. Initialize services, event hooks, and handlers inside `OnLoadAsync`.
3. Unregister every event/callback in `OnUnloadAsync`.

### Dependencies
How it works now:
- Extra mod assemblies are output/copied into `FinishedModFolder/Dependencies`.
- The root mod folder keeps only the main mod DLLs and data folders.
- In the root, there's still only one DLL expected. Other assemblies the mod depends on, should be in the `Dependencies` folder.

How to add it yourself:
1. Reference the dependency in your project.
2. Ensure the dependency DLL is moved or copied into `FinishedModFolder/Dependencies`.
3. Do not place dependency DLLs in the root of the mod folder.
4. Verify your shipped folder contains all required third-party DLLs under `Dependencies` and your root folder contains only the main mod DLL.

### Enum expansion
How it works now:
- Enum extension entries are declared in `MyFirstBAMod/FinishedModFolder/enums.txt`.
- Current example: `Dancing.DanceType.NewDance`.

How to add it yourself:
1. Open `enums.txt`.
2. Add one enum value per line in format `Namespace.EnumType.NewValue`.
3. Use the new enum value in your code using `ModEnumHash.GetSafeHash` method to get the comparison value. The string you request in `GetSafeHash` must match the value in `enums.txt`.

## Notes
- `GameManagedDir` is read by both project files.
- If your default Steam path exists on your machine, `Directory.Build.props` is optional.