using Setsuna;
using UnityEngine;
using UnityEngine.UI;

namespace SetsunaAccess
{
    /// <summary>
    /// The Settings screen shown at boot and from the title (UiConfigWindow). Five rows in
    /// ITEM order - Language, Screen Mode, Resolution, Control Type, Exit Settings - with
    /// left/right changing the value. Labels come from its text[] (TEXT_ID order) and the
    /// option buttons' own Text, so they follow the game's language.
    /// </summary>
    internal static class ConfigScreen
    {
        private const int RowCount = 5;
        // TEXT_ID indexes for each row's label: LANGUAGE, SCREEN, RESO, CONTROLLER, END.
        private static readonly int[] RowTitle = { 1, 4, 5, 6, 9 };
        // The option-button array and the field holding the chosen option, per row.
        private static readonly string[] RowButtons = { "languageButton", "screenButton", "resoButton", "controllerButton" };
        private static readonly string[] RowValue = { "language", "screen", "reso", "controller" };

        public static void OnOpen(UiConfigWindow win)
        {
            var caption = Label(win, 0);
            Speech.Say(caption);
            Speech.Say(Row(win, true), false);
        }

        public static void OnRow(UiConfigWindow win) { Speech.Say(Row(win, true)); }

        public static void OnValue(UiConfigWindow win) { Speech.Say(Value(win, Reflect.Int(win, "item"))); }

        private static string Row(UiConfigWindow win, bool withPosition)
        {
            var row = Reflect.Int(win, "item");
            if (row < 0 || row >= RowCount) return null;
            var label = Label(win, RowTitle[row]);
            var value = Value(win, row);
            if (!string.IsNullOrEmpty(value)) label = label + ": " + value;
            return withPosition ? Strings.Item(label, row, RowCount) : label;
        }

        private static string Value(UiConfigWindow win, int row)
        {
            if (row < 0 || row >= RowValue.Length) return null;
            var buttons = Reflect.Arr(win, RowButtons[row]);
            var chosen = Reflect.Int(win, RowValue[row]);
            if (buttons == null || chosen < 0 || chosen >= buttons.Length) return null;
            var go = buttons.GetValue(chosen) as GameObject;
            var t = go == null ? null : (go.GetComponent<Text>() ?? go.GetComponentInChildren<Text>());
            if (t == null && row == 2)
            {
                var reso = Reflect.Arr(win, "resoText");
                if (reso != null && chosen < reso.Length) t = reso.GetValue(chosen) as Text;
            }
            return t == null ? null : TextClean.Clean(t.text);
        }

        private static string Label(UiConfigWindow win, int textId)
        {
            var texts = Reflect.Arr(win, "text");
            if (texts == null || textId >= texts.Length) return "";
            var t = texts.GetValue(textId) as Text;
            return t == null ? "" : TextClean.Clean(t.text);
        }
    }
}
