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
        private static double _workMs, _workMax, _sleepMs;
        private static int _workFrames;

        /// <summary>From FrameCap: the whole frame's main-thread work and how long the cap then slept.</summary>
        public static void FrameWork(double work, double slept)
        {
            _workMs += work;
            _sleepMs += slept;
            if (work > _workMax) _workMax = work;
            _workFrames++;
        }

        public static void Begin() { CpuProbe.MarkMain(); _sw.Reset(); _sw.Start(); _markMs = 0; }

        private static int _gcLast = -1;

        private static double _markMs, _slowMs;
        private static string _slowName;

        /// <summary>The last mod key handled this frame, so a slow key press can be named.</summary>
        public static string Key;

        /// <summary>After each part of the mod's frame: remember the slowest part in this 10 s window.</summary>
        public static void Mark(string name)
        {
            var now = _sw.Elapsed.TotalMilliseconds;
            var ms = now - _markMs;
            _markMs = now;
            if (name == "keys" && Key != null) { name = "key " + Key; Key = null; }
            if (ms > _slowMs) { _slowMs = ms; _slowName = name; }
        }

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
            var wf = System.Math.Max(1, _workFrames);
            Log.Append("perf.log", "fps " + (_frames / span).ToString("0") + ", game work " + (_workMs / wf).ToString("0.0") + " ms/frame avg ("
                                   + _workMax.ToString("0") + " max), cap slept " + (_sleepMs / wf).ToString("0.0") + " ms/frame, mod "
                                   + (_totalMs / _frames).ToString("0.00") + " ms/frame avg, " + _maxMs.ToString("0.0") + " ms max, level "
                                   + Application.loadedLevelName
                                   + (_slowMs >= 10 ? ", slowest " + _slowName + " " + _slowMs.ToString("0") + " ms" : ""));
            _slowMs = 0;
            _slowName = null;
            var gc = System.GC.CollectionCount(0);
            var cpu = CpuProbe.Sample();
            if (cpu != null)
                Log.Append("perf.log", cpu + "; garbage collections " + (_gcLast < 0 ? 0 : gc - _gcLast)
                                       + ", managed heap " + (System.GC.GetTotalMemory(false) >> 20) + " MB");
            _gcLast = gc;
            _workMs = _workMax = _sleepMs = 0;
            _workFrames = 0;
            _frames = 0;
            _totalMs = _maxMs = 0;
            _since = now;
        }
    }
}
