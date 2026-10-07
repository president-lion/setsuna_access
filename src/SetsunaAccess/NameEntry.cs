using Setsuna;
using UnityEngine.UI;

namespace SetsunaAccess
{
    /// <summary>
    /// Naming screens (UiNameBox: hero, airship, Spritnite). A real InputField: typing works as
    /// normal, so the mod echoes the text as it changes, then reads the Yes / No confirm buttons.
    /// </summary>
    internal static class NameEntry
    {
        private static UiNameBox _box;
        private static string _last;

        public static bool Open { get { return _box != null && _box.IsShowing; } }

        public static void OnOpen(UiNameBox box)
        {
            _box = box;
            var field = Reflect.Get<InputField>(box, "inputField");
            _last = field == null ? "" : field.text;
            var msg = Ui.Read(Reflect.Get<Text>(box, "messageText"));
            var placeholder = field != null && field.placeholder is Text ? Ui.Read((Text)field.placeholder) : "";
            var line = msg;
            if (_last.Length > 0) line += ". " + _last;
            else if (placeholder.Length > 0) line += ". " + Strings.DefaultName(placeholder);
            Speech.Say(line);
        }

        public static void Tick()
        {
            if (!Open) return;
            if (Reflect.Int(_box, "state") != 0) return; // EDIT
            var field = Reflect.Get<InputField>(_box, "inputField");
            if (field == null) return;
            var now = field.text ?? "";
            if (now == _last) return;
            if (now.Length > _last.Length && now.StartsWith(_last)) Speech.Say(now.Substring(_last.Length));
            else if (now.Length < _last.Length && _last.StartsWith(now)) Speech.Say(Strings.Deleted(_last.Substring(now.Length)));
            else Speech.Say(now);
            _last = now;
        }

        /// <summary>After Enter: the name, then Yes / No.</summary>
        public static void OnButton(UiNameBox box)
        {
            var buttons = Reflect.Arr(box, "buttonText");
            var index = Reflect.Int(box, "button");
            if (buttons == null || index < 0 || index >= buttons.Length) return;
            var label = Strings.Item(Ui.Read(buttons.GetValue(index) as Text), index, buttons.Length);
            var name = Reflect.Get<string>(box, "inputName");
            // The first ButtonUpdate comes from EndEdit: say the chosen name with it.
            if (Reflect.Int(box, "state") == 1 && _last != null)
            {
                label = Strings.NameChosen(name) + " " + label;
                _last = null;
            }
            Speech.Say(label);
        }
    }
}
