using Setsuna;
using UnityEngine;
using UnityEngine.UI;

namespace SetsunaAccess
{
    /// <summary>
    /// The camp menu's Settings (UiCampConfigChoice rows: battle mode, cursor memory, battle help,
    /// volumes, voices). The chosen option is shown only by a highlight sprite, so the value is
    /// read from the row's state: buttonIndex for option rows (label = the Text drawn on that
    /// option's image), slider value for volumes.
    /// </summary>
    internal static class ConfigMenu
    {
        public static string Describe(UiCampConfigChoice row)
        {
            var name = TextClean.Clean(row.Category.ToName());
            var value = Value(row);
            var label = value.Length > 0 ? name + ": " + value : name;

            var common = row.GetComponentInParent<UiCampConfigCommon>();
            var all = common == null ? null : Reflect.Arr(common, "choices");
            var line = all == null ? label : Strings.Item(label, row.choicesNumber, all.Length);
            var about = TextClean.Clean(row.Category.ToAbout());
            return about.Length > 0 ? line + ". " + about : line;
        }

        public static string Value(UiCampConfigChoice row)
        {
            var type = Reflect.Int(row, "type");
            if (type == (int)UiConfigType.Slider)
                return Strings.Percent(Mathf.RoundToInt(row.value * 100f));
            if (type != (int)UiConfigType.Button) return "";

            var images = Reflect.Arr(row, "button_cursor");
            var index = row.buttonIndex;
            if (images == null || index < 0 || index >= images.Length) return "";
            var img = images.GetValue(index) as Image;
            var text = img == null ? null : NearestText(row.transform, img.rectTransform);
            var s = Ui.Read(text);
            return s.Length > 0 ? s : Strings.Option(index, images.Length);
        }

        /// <summary>The Text under the row whose centre is closest to the option's highlight image.</summary>
        private static Text NearestText(Transform row, RectTransform target)
        {
            Text best = null;
            var bestD = float.MaxValue;
            var c = target.TransformPoint(target.rect.center);
            foreach (var t in row.GetComponentsInChildren<Text>())
            {
                if (!t.enabled || string.IsNullOrEmpty(t.text)) continue;
                var d = (t.rectTransform.TransformPoint(t.rectTransform.rect.center) - c).sqrMagnitude;
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }
    }
}
