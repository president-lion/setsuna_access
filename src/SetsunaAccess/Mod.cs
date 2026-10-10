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
            FrameCap.Init();
            Settings.Init();
            Speech.Say(Strings.Loaded);
        }

        public override void OnUpdate()
        {
            Perf.Begin();
            try { Hotkeys(); }
            catch (Exception ex) { Log.Once("Hotkeys", ex); }
            Perf.Mark("keys");
            try { Results.Tick(); }
            catch (Exception ex) { Log.Once("Results", ex); }
            Perf.Mark("Results");
            try { NameEntry.Tick(); }
            catch (Exception ex) { Log.Once("NameEntry", ex); }
            Perf.Mark("NameEntry");
            try { FrameCap.Tick(); }
            catch (Exception ex) { Log.Once("FrameCap", ex); }
            Perf.Mark("FrameCap");
            try { Guide.Tick(); }
            catch (Exception ex) { Log.Once("Guide", ex); }
            Perf.Mark("Guide");
            try { Nav.Tick(); }
            catch (Exception ex) { Log.Once("Nav", ex); }
            Perf.Mark("Nav");
            try { Field.Tick(); }
            catch (Exception ex) { Log.Once("Field", ex); }
            Perf.Mark("Field");
            Perf.End();
        }

        public override void OnLateUpdate()
        {
            Perf.Begin();
            try { Focus.LateTick(); }
            catch (Exception ex) { Log.Once("Focus", ex); }
            Perf.Mark("Focus");
            try { Narration.LateTick(); }
            catch (Exception ex) { Log.Once("Narration", ex); }
            Perf.Mark("Narration");
            try { Battle.LateTick(); }
            catch (Exception ex) { Log.Once("Battle", ex); }
            Perf.Mark("Battle");
            Perf.End();
            Perf.Frame();
        }

        // The game binds letters, digits, Space, Return, Delete and Left Ctrl (Left Ctrl only matters
        // on the Key Config help screen). Z is free by default.
        private static readonly System.Collections.Generic.Dictionary<KeyCode, float> _lastPress =
            new System.Collections.Generic.Dictionary<KeyCode, float>();

        /// <summary>
        /// GetKeyDown with a short debounce: in play, one Page Down arrived as two key-downs ~70 ms
        /// apart, skipping an entry each time.
        /// </summary>
        private static bool Pressed(KeyCode key)
        {
            if (!Input.GetKeyDown(key)) return false;
            float last;
            var now = Time.unscaledTime;
            if (_lastPress.TryGetValue(key, out last) && now - last < 0.15f) return false;
            _lastPress[key] = now;
            Perf.Key = key.ToString();
            return true;
        }

        private static void Hotkeys()
        {
            var ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            var f2 = Pressed(KeyCode.F2);
            if (f2 && ctrl)
            {
                if (TextEntry.Active) TextEntry.Cancel(); else Field.RenameSelected();
                return;
            }
            if (TextEntry.Active) { TextEntry.Tick(); return; } // typing a name: no other mod keys
            if (ctrl && Pressed(KeyCode.M)) { Settings.Toggle(); return; }
            if (Settings.Open) { Settings.Tick(); return; }
            if (Pressed(KeyCode.F1)) Speech.Say(Strings.Help + " " + GameKeys());
            if (f2) FrameCap.Cycle();
            if (Pressed(KeyCode.F3)) Speech.Repeat();
            // Z and P are letters: not while a name is being typed.
            var typing = NameEntry.Open || Setsuna.GuiManager.IsInputName;
            if (Pressed(KeyCode.Z) && !typing) Field.SayLocation();
            if (Pressed(KeyCode.P) && !typing) Field.SayParty();
            if (Pressed(KeyCode.G) && !typing) SayGold();
            if (Pressed(KeyCode.L) && !typing) Field.FindSavePoint();
            if (Pressed(KeyCode.N) && !typing) Objective.Find();
            if (Pressed(KeyCode.T) && !typing) Battle.SayTurn();
            if (Pressed(KeyCode.V) && !typing)
            {
                Settings.SceneDescriptions = !Settings.SceneDescriptions;
                Speech.Say(Settings.SceneDescriptions ? Strings.NarrationOn : Strings.NarrationOff);
            }
            if (Pressed(KeyCode.PageUp)) { if (ctrl) Field.NextCategory(-1); else Field.Cycle(-1); }
            if (Pressed(KeyCode.PageDown)) { if (ctrl) Field.NextCategory(1); else Field.Cycle(1); }
            if (Pressed(KeyCode.Home)) { if (ctrl) Field.WalkToSelected(); else Field.RepeatSelected(); }
            if (Pressed(KeyCode.End))
            {
                var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (shift) Field.ToggleReachFilter(); else Field.ToggleBeacon();
            }
            if (Pressed(KeyCode.F11))
            {
                if (ctrl) SaveDump.Write();
                else
                {
                    Log.Info("UiDump", UiDump.Write());
                    Speech.Say(Strings.UiDumped);
                }
            }
        }

        private static void SayGold()
        {
            // Only once a save is running: Boot, Logo and Title have no money to read.
            var level = Application.loadedLevelName;
            if (!level.Contains("_")) { Speech.Say(Strings.NoGameLoaded); return; }
            Speech.Say(Strings.Gold(Setsuna.Common.GetMoney()));
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
