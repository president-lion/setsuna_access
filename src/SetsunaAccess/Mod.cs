using System;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(SetsunaAccess.Mod), "Setsuna Access", "0.1.0", "sunduijav")]
[assembly: MelonGame("TokyoRPGFactory", "SETSUNA")]

namespace SetsunaAccess
{
    /// <summary>Entry point: starts speech, applies the hooks, and runs the per-frame tick.</summary>
    public sealed class Mod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            Log.Init(LoggerInstance);
            Speech.Preload(MelonUtils.UserLibsDirectory);
            Speech.Init("auto");
            Patches.Apply(new HarmonyLib.Harmony("SetsunaAccess"));
            Speech.Say(Strings.Loaded);
        }

        public override void OnUpdate()
        {
            try { Hotkeys(); }
            catch (Exception ex) { Log.Once("Hotkeys", ex); }
        }

        // F-keys only: the game binds letters, digits, Space, Return, Delete and Left Ctrl.
        private static void Hotkeys()
        {
            if (Input.GetKeyDown(KeyCode.F3)) Speech.Repeat();
            if (Input.GetKeyDown(KeyCode.F11))
            {
                Log.Info("UiDump", UiDump.Write());
                Speech.Say(Strings.UiDumped);
            }
        }

        public override void OnDeinitializeMelon() { Speech.Shutdown(); }
    }
}
