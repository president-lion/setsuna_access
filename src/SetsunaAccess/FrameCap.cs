using MelonLoader;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Frame-rate cap to cut the game's CPU use. The game runs vsynced at 60 fps (QualitySettings vSyncCount 1),
    /// and vsync waiting can keep a CPU core busy; a targetFrameRate cap with vsync off sleeps between frames
    /// instead. Applied every frame because the game itself sets targetFrameRate = 10 during Momentum
    /// (UiBattleWindow.OnDecideSetsunaSystem), which only ever did nothing because vsync overrode it.
    /// F2 cycles 30, 45, 60 and the game's own setting; the choice is saved in MelonPreferences.
    /// </summary>
    internal static class FrameCap
    {
        private static readonly int[] Choices = { 30, 45, 60, 0 }; // 0 = the game's own vsync
        private static MelonPreferences_Entry<int> _entry;
        private static bool _restored;

        public static void Init()
        {
            var cat = MelonPreferences.CreateCategory("SetsunaAccess", "Setsuna Access");
            _entry = cat.CreateEntry("FrameCap", 30, "Frame rate cap (0 = game default vsync)");
        }

        public static void Cycle()
        {
            var cur = System.Array.IndexOf(Choices, _entry.Value);
            _entry.Value = Choices[(cur + 1) % Choices.Length];
            MelonPreferences.Save();
            _restored = false;
            Speech.Say(_entry.Value > 0 ? Strings.FrameCap(_entry.Value) : Strings.FrameCapOff);
        }

        public static void Tick()
        {
            var cap = _entry == null ? 0 : _entry.Value;
            if (cap > 0)
            {
                if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
                if (Application.targetFrameRate != cap) Application.targetFrameRate = cap;
                _restored = false;
            }
            else if (!_restored)
            {
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = -1;
                _restored = true;
            }
        }
    }
}
