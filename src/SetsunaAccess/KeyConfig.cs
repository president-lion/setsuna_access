using System.Reflection;
using HarmonyLib;
using Setsuna;
using UnityEngine;
using UnityEngine.UI;

namespace SetsunaAccess
{
    /// <summary>
    /// Key Config (UiConfigHelpWindow, edit state). Rows follow its CURSOR enum; each row's
    /// label is a text[] entry and its binding comes from InputManager. Left/right switches
    /// between keyboard and pad bindings; pressing a key (A-Z, 0-9, Space) on a row assigns it.
    /// </summary>
    internal static class KeyConfig
    {
        // CURSOR order: UP, RIGHT, LEFT, DOWN, DECIDE, CANCEL, MENU, SETSUNA, TAB_RIGHT, TAB_LEFT,
        // PAUSE, RIGHT_ROTATE, LEFT_ROTATE, OK. Label = TEXT_ID index; key = InputManager.InputKey.
        private static readonly int[] RowText = { 9, 10, 11, 12, 2, 3, 4, 5, 6, 7, 8, 13, 14, 18 };
        private static readonly InputManager.InputKey[] RowKey =
        {
            InputManager.InputKey.InputKey_Up, InputManager.InputKey.InputKey_Right,
            InputManager.InputKey.InputKey_Left, InputManager.InputKey.InputKey_Down,
            InputManager.InputKey.InputKey_Circle, InputManager.InputKey.InputKey_Cross,
            InputManager.InputKey.InputKey_Triangle, InputManager.InputKey.InputKey_Square,
            InputManager.InputKey.InputKey_R1, InputManager.InputKey.InputKey_L1,
            InputManager.InputKey.InputKey_Start, InputManager.InputKey.InputKey_RightStickR,
            InputManager.InputKey.InputKey_RightStickL, InputManager.InputKey.InputKey_Invalid
        };
        private const int Ok = 13;
        private const int TextPad = 0, TextKeyboard = 1, TextInfo = 16;

        private static MethodInfo _padName;

        public static void OnOpen(UiConfigHelpWindow win, bool isEdit)
        {
            if (!isEdit) return;
            Speech.Say(Label(win, Reflect.Int(win, "controller") == 0 ? TextPad : TextKeyboard));
            Speech.Say(Label(win, TextInfo), false);
            Speech.Say(Row(win), false);
        }

        public static void OnCursor(UiConfigHelpWindow win, bool isPlaySE)
        {
            if (isPlaySE) Speech.Say(Row(win));
        }

        public static void OnController(UiConfigHelpWindow win, bool isPlaySE)
        {
            if (!isPlaySE) return;
            var pad = Reflect.Int(win, "controller") == 0;
            Speech.Say(Label(win, pad ? TextPad : TextKeyboard) + ". " + Row(win));
        }

        public static void OnAssigned(UiConfigHelpWindow win, bool assigned)
        {
            if (assigned) Speech.Say(Row(win));
        }

        private static string Row(UiConfigHelpWindow win)
        {
            var row = Reflect.Int(win, "cursor");
            if (row < 0 || row >= RowText.Length) return "";
            var label = Label(win, RowText[row]);
            if (row == Ok)
            {
                var ok = Reflect.Get<GameObject>(win, "buttonOK");
                var okText = ok == null ? "" : Ui.ReadAll(ok.transform);
                return Strings.Item(okText.Length > 0 ? okText : label, row, RowText.Length);
            }
            var pad = Reflect.Int(win, "controller") == 0;
            var binding = pad ? PadName(InputManager.GetPadSetting(RowKey[row]))
                              : Strings.KeyName(InputManager.GetKeyBoardSetting(RowKey[row]).ToString());
            return Strings.Item(label + ": " + binding, row, RowText.Length);
        }

        /// <summary>The game's own pad label (A, B, LB...) via UiCommon.ChangeInputNameToConversionText.</summary>
        private static string PadName(string inputName)
        {
            if (string.IsNullOrEmpty(inputName)) return Strings.Unassigned;
            try
            {
                if (_padName == null) _padName = AccessTools.Method(typeof(UiCommon), "ChangeInputNameToConversionText");
                var s = _padName == null ? null : _padName.Invoke(null, new object[] { inputName }) as string;
                return string.IsNullOrEmpty(s) || s == "-" ? inputName : s;
            }
            catch { return inputName; }
        }

        private static string Label(UiConfigHelpWindow win, int textId)
        {
            var texts = Reflect.Arr(win, "text");
            if (texts == null || textId >= texts.Length) return "";
            return Ui.Read(texts.GetValue(textId) as Text);
        }
    }
}
