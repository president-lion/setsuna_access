using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SetsunaAccess
{
    /// <summary>
    /// F11: writes every active UI Text with its hierarchy path to UserData\SetsunaAccess\ui_dump.txt,
    /// so menus can be read offline without the user describing the screen.
    /// </summary>
    internal static class UiDump
    {
        public static string Write()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# UI dump " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " level=" + Application.loadedLevelName);
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Text>())
            {
                if (!t.enabled || string.IsNullOrEmpty(t.text)) continue;
                sb.Append(Path(t.transform)).Append(" | ").AppendLine(t.text.Replace("\n", "\\n"));
            }
            var file = System.IO.Path.Combine(Log.Dir, "ui_dump.txt");
            File.WriteAllText(file, sb.ToString());
            return file;
        }

        private static string Path(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }
}
