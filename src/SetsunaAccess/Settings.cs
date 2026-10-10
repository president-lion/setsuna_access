using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// The mod's own options, saved in UserData\MelonPreferences.cfg (category SetsunaAccess), and the Control M
    /// menu that changes them. While the menu is open the game sees no input (TextEntry.Blocking): up and down
    /// pick an option, left, right, Enter or Space change it, Escape or Control M close.
    /// </summary>
    internal static class Settings
    {
        private sealed class Option
        {
            public string Label;
            public string Help;
            public MelonPreferences_Entry<bool> Entry;
            public Func<string> Value;      // for options that aren't on/off
            public Action<int> Change;      // ditto: step through the choices
        }

        private static readonly List<Option> _options = new List<Option>();
        private static MelonPreferences_Category _cat;

        // Navigation
        public static bool WalkTo { get { return Get(_walkTo); } }
        public static bool Objective { get { return Get(_objective); } }
        public static bool ObjectiveHints { get { return Get(_hints); } }
        public static bool Beacon { get { return Get(_beacon); } set { Set(_beacon, value); } }
        public static bool HideUnreachable { get { return Get(_hide); } set { Set(_hide, value); } }
        public static bool Trails { get { return Get(_trails); } }
        public static bool BumpSound { get { return Get(_bump); } }
        // Field
        public static bool DangerWarnings { get { return Get(_danger); } }
        public static bool SceneDescriptions { get { return Get(_scenes); } set { Set(_scenes, value); } }
        public static bool BridgeAnnouncements { get { return Get(_bridges); } }
        // Battle
        public static bool MomentumTone { get { return Get(_momentum); } }
        public static bool ReadyAnnouncements { get { return Get(_ready); } }
        public static bool BattleEvents { get { return Get(_hud); } }
        // Menus
        public static bool Positions { get { return Get(_positions); } }
        public static bool Descriptions { get { return Get(_descriptions); } }
        public static bool EquippedOn { get { return Get(_equipped); } }
        // System
        public static bool CpuGuard { get { return Get(_cpu); } }

        private static MelonPreferences_Entry<bool> _walkTo, _objective, _hints, _beacon, _hide, _trails, _bump, _danger, _scenes,
            _bridges, _momentum, _ready, _hud, _positions, _descriptions, _equipped, _cpu;

        public static void Init()
        {
            _cat = MelonPreferences.GetCategory("SetsunaAccess") ?? MelonPreferences.CreateCategory("SetsunaAccess", "Setsuna Access");
            _walkTo = Add("WalkTo", true, Strings.OptWalkTo, Strings.OptWalkToHelp);
            _objective = Add("Objective", true, Strings.OptObjective, Strings.OptObjectiveHelp);
            _hints = Add("ObjectiveHints", true, Strings.OptHints, Strings.OptHintsHelp);
            _beacon = Add("Beacon", false, Strings.OptBeacon, Strings.OptBeaconHelp);
            _hide = Add("HideUnreachable", true, Strings.OptHide, Strings.OptHideHelp);
            _trails = Add("Trails", true, Strings.OptTrails, Strings.OptTrailsHelp);
            _bump = Add("BumpSound", true, Strings.OptBump, Strings.OptBumpHelp);
            _danger = Add("DangerWarnings", true, Strings.OptDanger, Strings.OptDangerHelp);
            _scenes = Add("SceneDescriptions", true, Strings.OptScenes, Strings.OptScenesHelp);
            _bridges = Add("BridgeAnnouncements", true, Strings.OptBridges, Strings.OptBridgesHelp);
            _momentum = Add("MomentumTone", true, Strings.OptMomentum, Strings.OptMomentumHelp);
            _ready = Add("ReadyAnnouncements", true, Strings.OptReady, Strings.OptReadyHelp);
            _hud = Add("BattleEvents", true, Strings.OptHud, Strings.OptHudHelp);
            _positions = Add("Positions", true, Strings.OptPositions, Strings.OptPositionsHelp);
            _descriptions = Add("Descriptions", true, Strings.OptDescriptions, Strings.OptDescriptionsHelp);
            _equipped = Add("EquippedOn", true, Strings.OptEquipped, Strings.OptEquippedHelp);
            _options.Add(new Option { Label = Strings.OptFrameCap, Help = Strings.OptFrameCapHelp, Value = FrameCap.Describe, Change = FrameCap.Step });
            _cpu = Add("CpuGuard", true, Strings.OptCpu, Strings.OptCpuHelp);
        }

        private static MelonPreferences_Entry<bool> Add(string id, bool def, string label, string help)
        {
            var e = _cat.CreateEntry(id, def, label);
            _options.Add(new Option { Label = label, Help = help, Entry = e });
            return e;
        }

        private static bool Get(MelonPreferences_Entry<bool> e) { return e == null || e.Value; }

        private static void Set(MelonPreferences_Entry<bool> e, bool v)
        {
            if (e == null || e.Value == v) return;
            e.Value = v;
            Save();
        }

        private static void Save()
        {
            try { MelonPreferences.Save(); } catch (Exception ex) { Log.Once("Settings.Save", ex); }
        }

        // ---- the menu --------------------------------------------------------------------

        private static int _index;
        public static bool Open { get; private set; }
        private static int _openFrame;

        public static void Toggle()
        {
            if (Open) { Close(); return; }
            if (TextEntry.Active) return;
            Open = true;
            _openFrame = Time.frameCount;
            TextEntry.Hold(true);
            Speech.Say(Strings.SettingsTitle + ". " + Line());
        }

        private static void Close()
        {
            Open = false;
            TextEntry.Hold(false);
            Save();
            Speech.Say(Strings.SettingsClosed);
        }

        /// <summary>Per frame while open (from Mod.Hotkeys, before the other keys).</summary>
        public static void Tick()
        {
            if (!Open || Time.frameCount == _openFrame) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Input.GetKeyDown(KeyCode.DownArrow)) Move(1);
            else if (Input.GetKeyDown(KeyCode.UpArrow)) Move(-1);
            else if (Input.GetKeyDown(KeyCode.Home)) { _index = 0; Speech.Say(Line()); }
            else if (Input.GetKeyDown(KeyCode.End)) { _index = _options.Count - 1; Speech.Say(Line()); }
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                     || Input.GetKeyDown(KeyCode.Space)) ChangeCurrent(1);
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) ChangeCurrent(-1);
            else if (Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.F1)) Speech.Say(_options[_index].Help);
        }

        private static void Move(int step)
        {
            _index = (_index + step + _options.Count) % _options.Count;
            Speech.Say(Line());
        }

        private static void ChangeCurrent(int step)
        {
            var o = _options[_index];
            if (o.Entry != null) { o.Entry.Value = !o.Entry.Value; Save(); Applied(o); }
            else if (o.Change != null) o.Change(step);
            Speech.Say(Value(o));
        }

        /// <summary>Settings that act at once when changed.</summary>
        private static void Applied(Option o)
        {
            if (o.Entry == _walkTo && !o.Entry.Value && AutoWalk.Active) AutoWalk.Stop(null);
        }

        private static string Value(Option o)
        {
            if (o.Entry != null) return o.Entry.Value ? Strings.On : Strings.Off;
            return o.Value != null ? o.Value() : "";
        }

        private static string Line()
        {
            var o = _options[_index];
            return Strings.Item(o.Label + ", " + Value(o), _index, _options.Count);
        }
    }
}
