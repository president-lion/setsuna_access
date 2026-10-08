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
        private static Nav.RouteJob _job;
        private static int _attempt; // 0 full body, 1 looser goal, 2 slim body

        /// <summary>A first route is still being worked out (don't call it "no path" yet).</summary>
        public static bool Planning { get { return _job != null && _route == null; } }

        /// <summary>Per frame: work on the pending route search for at most ~2 ms.</summary>
        public static void Tick()
        {
            if (_job == null) return;
            if (_target == null) { _job = null; return; }
            if (!Nav.StepRoute(_job, 2)) return;
            Finish();
        }

        /// <summary>Finish any pending search now (Home needs an answer straight away).</summary>
        public static void Complete(Vector3 pos)
        {
            if (_target == null) return;
            if (_job == null && _route == null) { _job = Nav.StartRoute(pos, _target.position, _goal); _attempt = 0; }
            while (_job != null)
            {
                Nav.StepRoute(_job, 1000);
                Finish();
            }
        }

        private static Nav.RouteJob _lastSearch;

        private static void Finish()
        {
            _lastSearch = _job;
            var route = Nav.RouteOf(_job);
            var from = _job.From;
            if (route == null && _attempt < 2)
            {
                NavLog.Line("route attempt " + _attempt + " failed, searched " + _job.Search.Expanded + "/" + _job.Search.Budget);
                _attempt++;
                // 1: exits past the walkable edge, try a looser goal. 2: a doorway too narrow for the grid to
                // fit the full body through, try a slim body.
                _job = _attempt == 1 ? Nav.StartRoute(from, _target.position, _goal + 1.5f, 3000)
                                     : Nav.StartRoute(from, _target.position, _goal + 1.5f, 8000, true);
                return;
            }
            _job = null;
            _route = route;
            _index = 0;
            _aimAt = 0f;
            _replanAt = Time.unscaledTime + (_route != null ? 4f : 5f);
            var kind = _attempt == 0 ? "full" : _attempt == 1 ? "loose goal" : "slim body";
            var searched = _lastSearch == null ? "" : ", searched " + _lastSearch.Search.Expanded + "/" + _lastSearch.Search.Budget;
            NavLog.Line(_route == null
                ? "no route (" + kind + searched + ") " + NavLog.P(from) + " -> " + _target.name + " " + NavLog.P(_target.position)
                : "route (" + kind + searched + ") " + _route.Count + " pts, " + Nav.Length(_route, 0).ToString("0.0")
                  + " m, " + NavLog.P(from) + " -> " + _target.name + " " + NavLog.P(_target.position));
        }

        public static bool HasRoute { get { return _route != null; } }
        public static Transform Target { get { return _target; } }

        public static void SetTarget(Transform target, float goalRadius)
        {
            if (target == _target && Mathf.Approximately(goalRadius, _goal)) return;
            _target = target;
            _goal = goalRadius;
            _route = null;
            _job = null;
            _replanAt = 0f;
            _aimAt = 0f;
        }

        public static void Clear() { _target = null; _route = null; _job = null; }

        /// <summary>Force a re-plan, e.g. after the scene changed.</summary>
        public static void Invalidate() { _replanAt = 0f; _aimAt = 0f; }

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

            if (_job == null && (_route == null ? now >= _replanAt : (now >= _replanAt || Strayed(pos))))
            {
                _job = Nav.StartRoute(pos, _target.position, _goal);
                _attempt = 0;
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
