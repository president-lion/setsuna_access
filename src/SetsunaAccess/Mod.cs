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
            try { Field.Tick(); }
            catch (Exception ex) { Log.Once("Field", ex); }
        }

        public override void OnLateUpdate()
        {
            try { Focus.LateTick(); }
            catch (Exception ex) { Log.Once("Focus", ex); }
            try { Battle.LateTick(); }
            catch (Exception ex) { Log.Once("Battle", ex); }
        }

        // F-keys only: the game binds letters, digits, Space, Return, Delete and Left Ctrl.
        private static void Hotkeys()
        {
            var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(KeyCode.F1)) Speech.Say(Strings.Help);
            if (Input.GetKeyDown(KeyCode.F2)) Field.SayLocation();
            if (Input.GetKeyDown(KeyCode.F3)) Speech.Repeat();
            if (Input.GetKeyDown(KeyCode.F4)) Field.NextCategory(shift ? -1 : 1);
            if (Input.GetKeyDown(KeyCode.F5)) Field.Cycle(-1);
            if (Input.GetKeyDown(KeyCode.F6)) Field.Cycle(1);
            if (Input.GetKeyDown(KeyCode.F7)) Field.RepeatSelected();
            if (Input.GetKeyDown(KeyCode.F8)) Field.ToggleBeacon();
            if (Input.GetKeyDown(KeyCode.F9)) Field.SayParty();
            if (Input.GetKeyDown(KeyCode.F11))
            {
                Log.Info("UiDump", UiDump.Write());
                Speech.Say(Strings.UiDumped);
            }
        }

        public override void OnDeinitializeMelon() { Speech.Shutdown(); }
    }
}
