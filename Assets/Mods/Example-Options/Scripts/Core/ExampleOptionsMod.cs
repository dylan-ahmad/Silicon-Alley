using System;
using System.Threading.Tasks;
using BAModAPI;
using SliderInMyDMs;

[assembly: RegisterModClass(typeof(ExampleOptionsMod))]

namespace SliderInMyDMs
{
    [ModEntryOnInitializationLoad]
    public class ExampleOptionsMod : IModBigAmbitions
    {
        private readonly ExampleOptionsLogic _logic = new();

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
}