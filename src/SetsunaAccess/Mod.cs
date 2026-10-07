using System;
using MelonLoader;


[assembly: MelonInfo(typeof(SetsunaAccess.Mod), "Setsuna Access", "0.1.0", "sunduijav")]
[assembly: MelonGame]

namespace SetsunaAccess
{
    /// <summary>Entry point: starts speech, applies the hooks, and runs the per-frame tick.</summary>
    public sealed class Mod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            Log.Init(LoggerInstance);
            Log.Info("Boot", "Unity " + UnityEngine.Application.unityVersion + ", product '" + UnityEngine.Application.productName
                             + "', company '" + UnityEngine.Application.companyName + "', " + (IntPtr.Size * 8) + "-bit");
            Speech.Preload(MelonUtils.UserLibsDirectory);
            Speech.Init("auto");
            Speech.Say(Strings.Loaded);
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            Log.Info("Scene", "loaded " + buildIndex + " " + sceneName);
        }

        public override void OnDeinitializeMelon() { Speech.Shutdown(); }
    }
}
