using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using MelonLoader;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Frame-rate cap to cut the game's CPU use. Unity's own targetFrameRate cap kept the CPU at ~92% in play
    /// (this Unity waits for the next frame busily), so the mod paces frames itself: vsync off, Unity uncapped,
    /// and at the end of every frame the main thread sleeps until the next frame is due (with the 1 ms Windows
    /// timer so the timing stays steady). The game's own targetFrameRate = 10 during Momentum
    /// (UiBattleWindow.OnDecideSetsunaSystem) is overridden every frame, as vsync always overrode it.
    /// F2 cycles 30, 45, 60 and the game's own setting; saved in MelonPreferences.
    /// </summary>
    internal static class FrameCap
    {
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);

        private static readonly int[] Choices = { 30, 45, 60, 0 }; // 0 = the game's own vsync
        private static MelonPreferences_Entry<int> _entry;
        private static bool _restored, _timerSet, _started;
        private static readonly Stopwatch _frame = new Stopwatch();

        /// <summary>Main-thread work per frame (frame time minus the sleep), for perf.log.</summary>
        public static double LastWorkMs { get; private set; }
        public static double LastSleepMs { get; private set; }

        public static void Init()
        {
            var cat = MelonPreferences.CreateCategory("SetsunaAccess", "Setsuna Access");
            _entry = cat.CreateEntry("FrameCap", 30, "Frame rate cap (0 = game default vsync)");
        }

        /// <summary>The Control M menu's value text.</summary>
        public static string Describe()
        {
            if (_entry == null) return "";
            return _entry.Value > 0 ? Strings.FrameCapValue(_entry.Value) : Strings.FrameCapGame;
        }

        /// <summary>The Control M menu: step through 30, 45, 60 and the game's own setting.</summary>
        public static void Step(int step)
        {
            var cur = System.Array.IndexOf(Choices, _entry.Value);
            if (cur < 0) cur = 0;
            _entry.Value = Choices[(cur + step + Choices.Length) % Choices.Length];
            MelonPreferences.Save();
            _restored = false;
        }

        public static void Cycle()
        {
            var cur = System.Array.IndexOf(Choices, _entry.Value);
            _entry.Value = Choices[(cur + 1) % Choices.Length];
            MelonPreferences.Save();
            _restored = false;
            Speech.Say(_entry.Value > 0 ? Strings.FrameCap(_entry.Value) : Strings.FrameCapOff);
        }

        /// <summary>Per frame from OnUpdate: keep vsync and Unity's own cap out of the way.</summary>
        public static void Tick()
        {
            if (!_started)
            {
                _started = true;
                MelonCoroutines.Start(EndOfFrame());
            }
            var cap = _entry == null ? 0 : _entry.Value;
            if (cap > 0)
            {
                if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
                if (Application.targetFrameRate != -1) Application.targetFrameRate = -1;
                if (!_timerSet) { timeBeginPeriod(1); _timerSet = true; }
                _restored = false;
            }
            else if (!_restored)
            {
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = -1;
                if (_timerSet) { timeEndPeriod(1); _timerSet = false; }
                _restored = true;
            }
        }

        /// <summary>After each rendered frame: sleep off whatever is left of this frame's time slice.</summary>
        private static IEnumerator EndOfFrame()
        {
            var wait = new WaitForEndOfFrame();
            _frame.Start();
            while (true)
            {
                yield return wait;
                var cap = _entry == null ? 0 : _entry.Value;
                var work = _frame.Elapsed.TotalMilliseconds;
                double slept = 0;
                if (cap > 0)
                {
                    var budget = 1000.0 / cap;
                    var left = budget - work;
                    if (left > 1.0)
                    {
                        var t0 = _frame.Elapsed.TotalMilliseconds;
                        Thread.Sleep((int)left);
                        slept = _frame.Elapsed.TotalMilliseconds - t0;
                    }
                }
                LastWorkMs = work;
                LastSleepMs = slept;
                Perf.FrameWork(work, slept);
                _frame.Reset();
                _frame.Start();
            }
        }
    }
}
