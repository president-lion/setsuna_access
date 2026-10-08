using System.Collections.Generic;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Route guidance to the scanner's selected object, shared by the beacon and walk-to.
    /// Keeps a Nav route, re-plans every couple of seconds, when the player strays from it or
    /// after getting stuck, and aims at the farthest route point reachable in a straight line.
    /// When no route exists it falls back to the straight line and says so.
    /// </summary>
    internal static class Guide
    {
        private static Transform _target;
        private static float _goal;
        private static List<Vector3> _route;
        private static int _index;
        private static float _replanAt, _aimAt;
        private static Vector3 _aim;
        private static float _left;

        public static bool HasRoute { get { return _route != null; } }
        public static Transform Target { get { return _target; } }

        public static void SetTarget(Transform target, float goalRadius)
        {
            if (target == _target && Mathf.Approximately(goalRadius, _goal)) return;
            _target = target;
            _goal = goalRadius;
            _route = null;
            _replanAt = 0f;
            _aimAt = 0f;
        }

        public static void Clear() { _target = null; _route = null; }

        /// <summary>Force a re-plan, e.g. after the scene changed.</summary>
        public static void Invalidate() { _route = null; _replanAt = 0f; _aimAt = 0f; }

        /// <summary>
        /// Where to head from pos right now. Returns false when there's no route (aim = the target itself).
        /// distanceLeft is the remaining route length (straight distance without a route).
        /// </summary>
        public static bool Aim(Vector3 pos, out Vector3 aim, out float distanceLeft)
        {
            aim = pos;
            distanceLeft = 0f;
            if (_target == null) return false;
            var now = Time.unscaledTime;

            if (_route == null ? now >= _replanAt : (now >= _replanAt || Strayed(pos)))
            {
                _route = Nav.FindRoute(pos, _target.position, _goal)
                      ?? Nav.FindRoute(pos, _target.position, _goal + 1.5f, 3000); // e.g. exits past the walkable edge
                _index = 0;
                _replanAt = now + (_route != null ? 4f : 5f);
                Log.Append("nav.log", _route == null
                    ? "no route " + Fmt(pos) + " -> " + _target.name + " " + Fmt(_target.position)
                    : "route " + _route.Count + " pts, " + Nav.Length(_route, 0).ToString("0.0") + " m, " + Fmt(pos) + " -> " + _target.name);
                _aimAt = 0f;
            }

            if (_route == null)
            {
                aim = _target.position;
                var d = aim - pos; d.y = 0f;
                distanceLeft = d.magnitude;
                return false;
            }

            if (now >= _aimAt)
            {
                _aimAt = now + 0.2f;
                _index = Nav.Nearest(_route, pos, _index);
                var last = _route.Count - 1;
                // Off the end of the route: the goal itself (e.g. walk on into an exit).
                _aim = _index >= last - 1 ? _target.position : _route[Nav.LookAhead(_route, _index, pos)];
                var toRoute = _route[_index] - pos; toRoute.y = 0f;
                _left = toRoute.magnitude + Nav.Length(_route, _index);
            }
            aim = _aim;
            distanceLeft = _left;
            return true;
        }

        /// <summary>The player is pushing toward <paramref name="dir"/> and not moving: learn the obstacle, re-plan.</summary>
        public static void Stuck(Vector3 pos, Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) Nav.MarkBlocked(pos + dir.normalized * (Nav.CellSize * 1.2f));
            Invalidate();
        }

        private static string Fmt(Vector3 v) { return "(" + v.x.ToString("0.0") + "," + v.y.ToString("0.0") + "," + v.z.ToString("0.0") + ")"; }

        private static bool Strayed(Vector3 pos)
        {
            var i = Nav.Nearest(_route, pos, _index);
            var d = _route[i] - pos;
            d.y = 0f;
            return d.magnitude > 2.5f;
        }
    }
}
