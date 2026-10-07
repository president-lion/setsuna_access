using System.Reflection;
using Setsuna;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Walks the party leader to the scanner's selected object by writing the stick values
    /// InputManager.Update has just computed (horizontal/vertical, camera-relative exactly as
    /// BaseObject.CreateMoveVec reads them). Straight line with sidestep retries when stuck.
    /// Any movement key from the player cancels it.
    /// </summary>
    internal static class AutoWalk
    {
        private static Transform _target;
        private static string _name;
        private static float _arrive;
        private static bool _active;
        private static float _startedAt;

        private static Vector3 _lastPos;
        private static float _nextCheck;
        private static int _stuck;
        private static float _detourUntil;
        private static float _detourAngle;

        private static FieldInfo _h, _v;

        public static bool Active { get { return _active; } }

        public static void Start(Transform target, string name, float arriveRadius)
        {
            if (target == null) { Speech.Say(Strings.NothingSelected); return; }
            _target = target;
            _name = name;
            _arrive = arriveRadius;
            _active = true;
            _startedAt = Time.time;
            _stuck = 0;
            _detourUntil = 0f;
            _nextCheck = Time.time + 1f;
            var p = Leader();
            _lastPos = p == null ? Vector3.zero : p.position;
            Speech.Say(Strings.WalkingTo(name));
        }

        public static void Stop(string say)
        {
            if (!_active) return;
            _active = false;
            _target = null;
            if (!string.IsNullOrEmpty(say)) Speech.Say(say);
        }

        /// <summary>Postfix of InputManager.Update.</summary>
        public static void AfterInput(InputManager input)
        {
            if (!_active) return;
            if (_h == null)
            {
                _h = typeof(InputManager).GetField("horizontal", BindingFlags.Instance | BindingFlags.NonPublic);
                _v = typeof(InputManager).GetField("vertical", BindingFlags.Instance | BindingFlags.NonPublic);
                if (_h == null || _v == null) { Stop(null); Log.Error("AutoWalk", "stick fields missing"); return; }
            }

            // The player took over.
            // (Unity's axis eases out for a moment after a key is released, so ignore the first instant.)
            if (Time.time - _startedAt > 0.3f && ((float)_h.GetValue(input) != 0f || (float)_v.GetValue(input) != 0f))
            {
                Stop(Strings.WalkCancelled);
                return;
            }
            if (_target == null || !_target.gameObject.activeInHierarchy) { Stop(Strings.WalkLost); return; }
            // Menus, events and battles: hold still but keep the walk.
            var gs = GameManager.NowGameState;
            if ((gs != GAME_STATE.FIELD && gs != GAME_STATE.WORLD) || EventManager.IsEvent || UiCampManager.IsShowing) return;

            var leader = Leader();
            if (leader == null) return;
            var d = _target.position - leader.position;
            d.y = 0f;
            if (d.magnitude <= _arrive) { Stop(Strings.Arrived(_name)); return; }

            if (Time.time >= _nextCheck)
            {
                var moved = leader.position - _lastPos;
                moved.y = 0f;
                if (moved.magnitude < 0.3f)
                {
                    _stuck++;
                    if (_stuck > 5) { Stop(Strings.Blocked(_name)); return; }
                    _detourAngle = (_stuck % 2 == 1 ? 1f : -1f) * (50f + 15f * _stuck);
                    _detourUntil = Time.time + 0.9f;
                }
                else if (Time.time > _detourUntil) _stuck = 0;
                _lastPos = leader.position;
                _nextCheck = Time.time + 0.8f;
            }

            var dir = d.normalized;
            if (Time.time < _detourUntil) dir = Quaternion.Euler(0f, _detourAngle, 0f) * dir;

            var cam = MainCameraControl.cameraTransform;
            var right = cam.right; right.y = 0f; right.Normalize();
            var fwd = Vector3.Cross(right, Vector3.up).normalized;
            var stick = UiCommon.StickInputValue;
            // Walk (not run) for the last couple of meters so the stop is precise.
            var speed = d.magnitude < 2.5f ? 0.5f : 1f;
            _h.SetValue(input, Vector3.Dot(dir, right) * stick * speed);
            _v.SetValue(input, Vector3.Dot(dir, fwd) * stick * speed);
        }

        private static Transform Leader()
        {
            var m = FieldPartyManager.Member;
            return m != null && m.Count > 0 && m[0] != null ? m[0].transform : null;
        }
    }
}
