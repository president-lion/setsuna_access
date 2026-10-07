using UnityEngine;
using UnityEngine.UI;

namespace SetsunaAccess
{
    /// <summary>Small helpers for reading the game's uGUI hierarchy.</summary>
    internal static class Ui
    {
        /// <summary>Finds a component on a descendant named <paramref name="name"/> of the closest ancestor that has one.</summary>
        public static T FindNear<T>(Transform from, string name, int maxUp = 4) where T : Component
        {
            var t = from;
            for (var up = 0; t != null && up <= maxUp; up++, t = t.parent)
            {
                var hit = FindDeep(t, name);
                if (hit != null)
                {
                    var c = hit.GetComponent<T>();
                    if (c != null) return c;
                }
            }
            return null;
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>Cleaned text of a Text, or "" if it is missing.</summary>
        public static string Read(Text t)
        {
            return t == null ? "" : TextClean.Clean(t.text);
        }

        /// <summary>All active, non-empty Texts under a transform, in hierarchy order, joined with ", ".</summary>
        public static string ReadAll(Transform root)
        {
            if (root == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var t in root.GetComponentsInChildren<Text>())
            {
                if (!t.enabled) continue;
                var s = TextClean.Clean(t.text);
                if (s.Length == 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(s);
            }
            return sb.ToString();
        }
    }
}
