using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>One place for nav.log lines, so positions and names read the same everywhere.</summary>
    internal static class NavLog
    {
        public static void Line(string text) { Log.Append("nav.log", text); }

        public static string P(Vector3 v)
        {
            return "(" + v.x.ToString("0.0") + "," + v.y.ToString("0.0") + "," + v.z.ToString("0.0") + ")";
        }

        public static string D(Vector3 dir)
        {
            dir.y = 0f;
            return dir.sqrMagnitude < 0.0001f ? "-" : "(" + dir.normalized.x.ToString("0.00") + "," + dir.normalized.z.ToString("0.00") + ")";
        }
    }
}
