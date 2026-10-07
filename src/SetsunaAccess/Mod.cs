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
            try { Results.Tick(); }
            catch (Exception ex) { Log.Once("Results", ex); }
            try { NameEntry.Tick(); }
            catch (Exception ex) { Log.Once("NameEntry", ex); }
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

        // The game binds letters, digits, Space, Return, Delete and Left Ctrl (Left Ctrl only matters
        // on the Key Config help screen). Z is free by default.
        private static void Hotkeys()
        {
            var ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (Input.GetKeyDown(KeyCode.F1)) Speech.Say(Strings.Help + " " + GameKeys());
            if (Input.GetKeyDown(KeyCode.F3)) Speech.Repeat();
            // Z and P are letters: not while a name is being typed.
            var typing = NameEntry.Open || Setsuna.GuiManager.IsInputName;
            if (Input.GetKeyDown(KeyCode.Z) && !typing) Field.SayLocation();
            if (Input.GetKeyDown(KeyCode.P) && !typing) Field.SayParty();
            if (Input.GetKeyDown(KeyCode.PageUp)) { if (ctrl) Field.NextCategory(-1); else Field.Cycle(-1); }
            if (Input.GetKeyDown(KeyCode.PageDown)) { if (ctrl) Field.NextCategory(1); else Field.Cycle(1); }
            if (Input.GetKeyDown(KeyCode.Home)) { if (ctrl) Field.WalkToSelected(); else Field.RepeatSelected(); }
            if (Input.GetKeyDown(KeyCode.End)) Field.ToggleBeacon();
            if (Input.GetKeyDown(KeyCode.F11))
            {
                Log.Info("UiDump", UiDump.Write());
                Speech.Say(Strings.UiDumped);
            }
        }

        /// <summary>The game's own keys as currently bound (Key Config can change them).</summary>
        private static string GameKeys()
        {
            try
            {
                Func<Setsuna.InputManager.InputKey, string> k = key =>
                    Strings.KeyName(Setsuna.InputManager.GetKeyBoardSetting(key).ToString());
                return Strings.GameKeys(k(Setsuna.InputManager.InputKey.InputKey_Circle), k(Setsuna.InputManager.InputKey.InputKey_Cross),
                    k(Setsuna.InputManager.InputKey.InputKey_Triangle), k(Setsuna.InputManager.InputKey.InputKey_Square),
                    k(Setsuna.InputManager.InputKey.InputKey_Up), k(Setsuna.InputManager.InputKey.InputKey_Left),
                    k(Setsuna.InputManager.InputKey.InputKey_Down), k(Setsuna.InputManager.InputKey.InputKey_Right));
            }
            catch { return ""; }
        }

        public override void OnDeinitializeMelon() { Speech.Shutdown(); }
    }
}
