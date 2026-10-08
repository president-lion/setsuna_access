using System.Diagnostics;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Measures the mod's own per-frame cost (Update + LateUpdate work) and the game's frame rate, and
    /// writes a line to perf.log every 10 seconds, so CPU complaints can be pinned on the mod or the game.
    /// </summary>
    internal static class Perf
    {
        private static readonly Stopwatch _sw = new Stopwatch();
        private static double _totalMs, _maxMs;
        private static int _frames;
        private static float _since = -1f;

        public static void Begin() { _sw.Reset(); _sw.Start(); }

        public static void End()
        {
            _sw.Stop();
            var ms = _sw.Elapsed.TotalMilliseconds;
            _totalMs += ms;
            if (ms > _maxMs) _maxMs = ms;
        }

        /// <summary>Once per frame, after the mod's LateUpdate work.</summary>
        public static void Frame()
        {
            _frames++;
            var now = Time.realtimeSinceStartup;
            if (_since < 0f) { _since = now; return; }
            var span = now - _since;
            if (span < 10f) return;
            Log.Append("perf.log", "fps " + (_frames / span).ToString("0") + ", mod " + (_totalMs / _frames).ToString("0.00")
                                   + " ms/frame avg, " + _maxMs.ToString("0.0") + " ms max, level " + Application.loadedLevelName);
            _frames = 0;
            _totalMs = _maxMs = 0;
            _since = now;
        }
    }
}
