using System.Reflection;
using Setsuna;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Walks the party leader to the scanner's selected object by writing the stick values
    /// InputManager.Update has just computed (horizontal/vertical, camera-relative exactly as
    /// BaseObject.CreateMoveVec reads them). Follows the Guide's walkable route; when stuck it
    /// teaches the route finder about the obstacle, re-plans, and sidesteps as a last resort.
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
        private static float _backUntil;
        private static Vector3 _startPos;
        private static float _travelled;
        private static Vector3 _backDir;
        // Progress check: the stuck count resets whenever a sidestep moves the party, so a wall it can't
        // get past was bumped forever. Give up when the distance left hasn't improved for a while.
        private static float _bestLeft, _bestAt;
        private static bool _saidStraight;
        private static int _routeVersion;
        private static Vector3 _stuckSpot;
        private static int _stuckRepeats;
        private static float _pausedAt = -10f;   // last time a menu, event or battle held the walk
        private static bool _saidPaused;
        private static float _lastDistance = float.MaxValue;
        private static bool _hadBattle; // a battle paused this walk: a monster target is gone because it was fought

        /// <summary>Seconds since the walk started (a held key's repeat shouldn't count as "stop").</summary>
        public static float Age { get { return Time.time - _startedAt; } }

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
            _lastDistance = float.MaxValue;
            _hadBattle = false;
            _stuck = 0;
            _detourUntil = 0f;
            _nextCheck = Time.time + 1f;
            var p = Leader();
            _lastPos = p == null ? Vector3.zero : p.position;
            _startPos = _lastPos;
            _travelled = 0f;
            _bestLeft = float.MaxValue;
            _saidStraight = false;
            _routeVersion = Guide.Version;
            _stuckRepeats = 0;
            _stuckSpot = new Vector3(1e6f, 0f, 1e6f);
            _bestAt = Time.time;
            NavLog.Line("walk start -> " + name + " " + NavLog.P(target.position) + " from " + NavLog.P(_lastPos) + ", arrive within " + arriveRadius.ToString("0.0"));
            Speech.Say(Strings.WalkingTo(name));
        }

        public static void Stop(string say)
        {
            if (!_active) return;
            var p = Leader();
            NavLog.Line("walk end: " + (string.IsNullOrEmpty(say) ? "scene change" : say) + " at " + (p == null ? "-" : NavLog.P(p.position))
                        + ", " + (Time.time - _startedAt).ToString("0.0") + " s, walked " + _travelled.ToString("0.0") + " m, stuck " + _stuck);
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

            // Menus, events and battles: hold still but keep the walk. Checked first: the arrow keys there move
            // menu cursors and battle targets, not the party, so they mustn't cancel it (Control Home still does).
            var gs = GameManager.NowGameState;
            if ((gs != GAME_STATE.FIELD && gs != GAME_STATE.WORLD) || EventManager.IsEvent || UiCampManager.IsShowing || Settings.Open)
            {
                _bestAt = Time.time; // paused time isn't lack of progress
                _pausedAt = Time.time;
                if (gs == GAME_STATE.BATTLE) _hadBattle = true;
                if (!_saidPaused) { _saidPaused = true; NavLog.Line("walk paused (" + gs + (EventManager.IsEvent ? ", event" : "") + ")"); }
                return;
            }
            if (_saidPaused)
            {
                _saidPaused = false;
                _nextCheck = Time.time + 1f;  // the party stood still meanwhile; that isn't being stuck
                var lp = Leader();
                if (lp != null) _lastPos = lp.position;
                NavLog.Line("walk resumed");
            }
            // The player took over: only a real movement key counts (axis values linger and drift).
            if (Time.time - _startedAt > 0.3f && MovementKeyHeld() && Time.time - _pausedAt > 0.5f)
            {
                Stop(Strings.WalkCancelled);
                return;
            }
            if (_target == null || !_target.gameObject.activeInHierarchy)
            {
                // An exit switches itself off as its map jump starts: that's arriving, and the new map's name follows.
                if (_arrive <= 0.25f && _lastDistance < 4f) { Stop(null); return; }
                // Walked to a monster and the battle started: it's gone because it was fought.
                if (_hadBattle) { Stop(null); return; }
                Stop(Strings.WalkLost);
                return;
            }

            var leader = Leader();
            if (leader == null) return;
            var d = _target.position - leader.position;
            d.y = 0f;
            _lastDistance = d.magnitude;
            if (d.magnitude <= _arrive) { Stop(Strings.Arrived(_name)); return; }

            // Head for the next point on the walkable route (straight at the target if there is none).
            Vector3 aim;
            float left;
            Guide.SetTarget(_target, _arrive);
            var routed = Guide.Aim(leader.position, out aim, out left);
            var dir = aim - leader.position;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : d.normalized;

            // No plan found a way. The probes have been wrong (Serendale), so try straight at it rather than
            // refuse; the no-progress check below stops it if it really can't get there.
            // The world map's walls are mountains and sea: a straight line into them just bumps.
            if (Guide.NoRoute && GameManager.NowGameState == GAME_STATE.WORLD)
            {
                Stop(Strings.NoWorldRoute(_name));
                return;
            }
            if (Guide.NoRoute && !_saidStraight)
            {
                _saidStraight = true;
                NavLog.Line("walk: no route, trying straight");
                Speech.Say(Strings.NoRouteTryingStraight);
            }
            var remaining = left > 0f ? left : d.magnitude;
            // A new plan measures differently (straight line before the first route, then a longer winding
            // route): restart the check, or a 150 m route never beat the 34 m straight line it started with.
            // Only when the new plan is longer: re-plans every few seconds would otherwise hide real stalls.
            if (Guide.Version != _routeVersion)
            {
                _routeVersion = Guide.Version;
                if (remaining > _bestLeft + 1f) { _bestLeft = remaining; _bestAt = Time.time; }
            }
            if (remaining < _bestLeft - 1f) { _bestLeft = remaining; _bestAt = Time.time; }
            else if (Time.time - _bestAt > (Guide.NoRoute ? 8f : 15f) && !Guide.Planning)
            {
                NavLog.Line("walk: no progress, " + remaining.ToString("0.0") + " m left (best " + _bestLeft.ToString("0.0") + ")");
                Stop(Strings.Blocked(_name));
                return;
            }

            if (Time.time >= _nextCheck)
            {
                var moved = leader.position - _lastPos;
                moved.y = 0f;
                _travelled += moved.magnitude;
                if (moved.magnitude < 0.3f)
                {
                    // Stopped against the thing itself (a chest's box, a person): close enough to use it.
                    if (_arrive > 0.25f && d.magnitude <= _arrive + 1f)
                    {
                        NavLog.Line("walk: stopped " + d.magnitude.ToString("0.0") + " m from the target, counting it as arrived");
                        Stop(Strings.Arrived(_name));
                        return;
                    }
                    _stuck++;
                    NavLog.Line("walk stuck #" + _stuck + " at " + NavLog.P(leader.position) + " heading " + NavLog.D(dir)
                                + (_stuck >= 3 ? ", backing off and sidestepping" : ", backing off"));
                    if (_stuck > 6) { Stop(Strings.Blocked(_name)); return; }
                    // Learn the obstacle, back off a step, and re-plan; after a couple of tries also sidestep.
                    var here = leader.position - _stuckSpot; here.y = 0f;
                    _stuckRepeats = here.magnitude < 1.5f ? _stuckRepeats + 1 : 0;
                    _stuckSpot = leader.position;
                    Nav.LearnFromBump(leader.position, dir, _stuckRepeats);
                    Guide.Stuck(leader.position, dir);
                    _backDir = dir;
                    _backUntil = Time.time + 0.35f;
                    if (_stuck >= 3)
                    {
                        _detourAngle = (_stuck % 2 == 1 ? 1f : -1f) * 70f;
                        _detourUntil = Time.time + 0.7f;
                    }
                }
                else if (Time.time > _detourUntil) _stuck = 0;
                _lastPos = leader.position;
                _nextCheck = Time.time + 0.8f;
            }
            // Last few metres to an exit: straight in. Doors are solid and the exit is just behind one, so
            // sliding along the "wall" would steer off the door.
            // ...but only when the route itself is about that short: a doorway round a corner inside a house is
            // 3 m away in a straight line and 11 m on foot.
            var intoExit = _arrive <= 0.25f && d.magnitude < 3.5f && (!routed || left < d.magnitude + 1f);
            if (Time.time < _backUntil) dir = -_backDir;                       // backing off a wall
            else if (Time.time < _detourUntil) dir = Quaternion.Euler(0f, _detourAngle, 0f) * dir;
            else if (intoExit) dir = d.normalized;
            else dir = Nav.Slide(leader.position, dir);                         // slide along walls

            var cam = MainCameraControl.cameraTransform;
            var right = cam.right; right.y = 0f; right.Normalize();
            var fwd = Vector3.Cross(right, Vector3.up).normalized;
            var stick = UiCommon.StickInputValue;
            // Walk (not run) for the last couple of meters so the stop is precise.
            var speed = d.magnitude < 2.5f ? 0.5f : 1f;
            _h.SetValue(input, Vector3.Dot(dir, right) * stick * speed);
            _v.SetValue(input, Vector3.Dot(dir, fwd) * stick * speed);
        }

        private static readonly InputManager.InputKey[] MoveKeys =
        {
            InputManager.InputKey.InputKey_Up, InputManager.InputKey.InputKey_Down,
            InputManager.InputKey.InputKey_Left, InputManager.InputKey.InputKey_Right
        };

        private static bool MovementKeyHeld()
        {
            if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) ||
                Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow)) return true;
            foreach (var k in MoveKeys)
            {
                var code = InputManager.GetKeyBoardSetting(k);
                if (code != KeyCode.None && Input.GetKey(code)) return true;
            }
            return false;
        }

        private static Transform Leader()
        {
            var m = FieldPartyManager.Member;
            return m != null && m.Count > 0 && m[0] != null ? m[0].transform : null;
        }
    }
}
