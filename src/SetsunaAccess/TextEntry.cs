using System;
using System.Collections.Generic;
using System.Reflection;
using Setsuna;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// A small typed-text prompt (for renaming scanner objects). While active the game sees no input:
    /// InputManager's per-frame state is cleared after its Update and its button/stick/key queries report
    /// nothing, so letters don't walk the party or open menus. Typed text comes from Input.inputString;
    /// each character is echoed, Backspace says what it deleted, Enter finishes, the start key again cancels.
    /// </summary>
    internal static class TextEntry
    {
        private static Action<string> _done;
        private static string _text = "";
        private static int _startFrame;

        public static bool Active { get; private set; }

        // The Enter that finishes typing must not reach the game in the same frame either.
        private static int _releaseFrame = -1;
        public static bool Blocking { get { return Active || Time.frameCount <= _releaseFrame; } }

        public static void Begin(string prompt, string initial, Action<string> done)
        {
            Active = true;
            _done = done;
            _text = initial ?? "";
            _startFrame = Time.frameCount;
            Speech.Say(_text.Length > 0 ? prompt + " " + _text : prompt);
        }

        public static void Cancel()
        {
            if (!Active) return;
            Active = false;
            _releaseFrame = Time.frameCount + 1;
            _done = null;
            Speech.Say(Strings.RenameCancelled);
        }

        /// <summary>Per frame, from Mod.OnUpdate.</summary>
        public static void Tick()
        {
            if (!Active || Time.frameCount == _startFrame) return; // ignore the frame that started it
            foreach (var ch in Input.inputString)
            {
                if (ch == '\b')
                {
                    if (_text.Length == 0) continue;
                    var gone = _text.Substring(_text.Length - 1);
                    _text = _text.Substring(0, _text.Length - 1);
                    Speech.SayAlways(Strings.Deleted(gone));
                }
                else if (ch == '\n' || ch == '\r')
                {
                    Active = false;
                    _releaseFrame = Time.frameCount + 1;
                    var done = _done;
                    _done = null;
                    if (done != null) done(_text.Trim());
                    return;
                }
                else if (!char.IsControl(ch) && _text.Length < 40)
                {
                    _text += ch;
                    Speech.SayAlways(ch == ' ' ? Strings.Space : ch.ToString());
                }
            }
        }

        // ---- keeping the game from seeing the keys ---------------------------------------

        private static List<FieldInfo> _state;

        /// <summary>Postfix of InputManager.Update: clear this frame's buttons, sticks and repeat flags.</summary>
        public static void ClearGameInput(InputManager input)
        {
            if (!Blocking) return;
            if (_state == null)
            {
                _state = new List<FieldInfo>();
                foreach (var f in typeof(InputManager).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    if (f.FieldType == typeof(bool) && f.Name.StartsWith("isInput")) _state.Add(f);
                    else if (f.FieldType == typeof(float) && (f.Name.StartsWith("horizontal") || f.Name.StartsWith("vertical")
                             || f.Name.StartsWith("padHorizontal") || f.Name.StartsWith("padVertical"))) _state.Add(f);
                    else if (f.FieldType.IsEnum && f.Name.EndsWith("JoyStick")) _state.Add(f);
                }
            }
            foreach (var f in _state)
            {
                if (f.FieldType == typeof(bool)) f.SetValue(input, false);
                else if (f.FieldType == typeof(float)) f.SetValue(input, 0f);
                else f.SetValue(input, Enum.ToObject(f.FieldType, 0));
            }
        }
    }
}
