using System;

namespace SetsunaAccess
{
    /// <summary>
    /// Drops a line identical to the previous one if it arrives within a short window.
    /// Game events often fire twice for one change. Pure logic, so it is unit tested.
    /// </summary>
    internal sealed class RepeatFilter
    {
        private readonly TimeSpan _window;
        private string _last;
        private DateTime _lastAt;

        public RepeatFilter(TimeSpan window) { _window = window; }

        public bool Allow(string text, DateTime now)
        {
            if (text == _last && now - _lastAt < _window) return false;
            _last = text;
            _lastAt = now;
            return true;
        }

        public void Reset() { _last = null; }
    }
}
