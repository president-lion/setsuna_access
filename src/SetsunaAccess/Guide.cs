using System.Collections.Generic;
using Setsuna;
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
        private static int _attempt; // 0 full body, 1 looser goal, 2 slim body, 3 lenient (the flood's rules)

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

        /// <summary>
        /// Work on the pending search for up to maxMs now (Home wants an answer quickly). Returns false if it's
        /// still running; it then carries on a slice per frame, so a long search never freezes the game.
        /// </summary>
        public static bool Complete(Vector3 pos, int maxMs)
        {
            if (_target == null) return true;
            if (_job == null && _route == null) { _attempt = _goodAttempt; _job = StartAttempt(pos, _attempt); }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (_job != null)
            {
                var left = maxMs - (int)watch.ElapsedMilliseconds;
                if (left <= 0) return false;
                if (Nav.StepRoute(_job, left)) Finish();
            }
            return true;
        }

        /// <summary>Raised when a search finishes (with or without a route).</summary>
        public static event System.Action Finished;

        private static Nav.RouteJob _lastSearch;
        private static int _goodAttempt;

        // 0: the full body. 1: exits past the walkable edge, a looser goal. 2: a doorway too narrow for the
        // grid to fit the full body through, a slim body. 3: the way the scanner's flood found (stairs, steps
        // and ramps the body-width rays read as walls).
        // The fallbacks only need to get near: the walk covers the rest in a straight line. Exits (tiny arrive
        // radius) sit behind house doors, which are solid ground-layer colliders you walk into, so every cell
        // within 1.7 m of the exit point was on the far side of the door; 3 m reaches the doorstep.
        private static float Loose { get { return _goal < 0.5f ? _goal + 3f : _goal + 1.5f; } }

        // Exits and story spots: walked into, so the loose goal must see the point (not be behind a wall).
        private static bool IsExit { get { return _goal < 0.5f; } }

        private static Nav.RouteJob StartAttempt(Vector3 from, int attempt)
        {
            switch (attempt)
            {
                case 0: return Nav.StartRoute(from, _target.position, _goal);
                case 1: return Nav.StartRoute(from, _target.position, Loose, 3000, Nav.Mode.Full, IsExit);
                case 2: return Nav.StartRoute(from, _target.position, Loose, 8000, Nav.Mode.Slim, IsExit);
                default: return Nav.StartRoute(from, _target.position, Loose, 12000, Nav.Mode.Lenient, IsExit);
            }
        }

        private static void Finish()
        {
            _lastSearch = _job;
            var route = Nav.RouteOf(_job);
            var from = _job.From;
            // On the world map the walls (HitWall) bound the land itself: the lenient fallback only found gaps the
            // party can't fit through, and walk-to bumped along the mountains (Frost Caves' far entrance). The slim
            // plan stays, with a body only a little slimmer there (Nav.Radius).
            var lastAttempt = MapData.IsWorldMap(SceneManager.CurrentFloorInfo == null ? null : SceneManager.CurrentFloorInfo.id) ? 2 : 3;
            if (route == null && _attempt < lastAttempt)
            {
                NavLog.Line("route attempt " + _attempt + " failed, searched " + _job.Search.Expanded + "/" + _job.Search.Budget
                            + "; refused: " + Nav.RouteRefusals());
                _attempt++;
                _job = StartAttempt(from, _attempt);
                return;
            }
            // Re-plans start from whatever worked, so a town that needs the lenient plan doesn't redo three
            // failing searches every few seconds; a failure starts over from the strictest next time.
            _goodAttempt = route != null ? _attempt : 0;
            if (route == null) _failures++;
            else { _failures = 0; _hadRoute = true; }
            _job = null;
            _route = route;
            _index = 0;
            _aimAt = 0f;
            // A failed chain is four big searches: retry it rarely (the 200 ms hitches in Frost Caves were these).
            _replanAt = Time.unscaledTime + (_route != null ? 4f : 12f);
            var kind = _attempt == 0 ? "full" : _attempt == 1 ? "loose goal" : _attempt == 2 ? "slim body" : "lenient";
            var searched = _lastSearch == null ? "" : ", searched " + _lastSearch.Search.Expanded + "/" + _lastSearch.Search.Budget;
            NavLog.Line(_route == null
                ? "no route (" + kind + searched + ") " + NavLog.P(from) + " -> " + _target.name + " " + NavLog.P(_target.position)
                : "route (" + kind + searched + ") " + _route.Count + " pts, " + Nav.Length(_route, 0).ToString("0.0")
                  + " m, " + NavLog.P(from) + " -> " + _target.name + " " + NavLog.P(_target.position)
                  + (_route == null ? "; refused: " + Nav.RouteRefusals() : ""));
            Version++;
            var done = Finished;
            if (done != null) done();
        }

        public static bool HasRoute { get { return _route != null; } }

        /// <summary>Goes up each time a search finishes, so walk-to can restart its progress check.</summary>
        public static int Version { get; private set; }

        // Every plan, down to the lenient one, found no way: on the first search for this target, or twice
        // running later (one failure mid-walk can be cells just learned from a bump).
        private static int _failures;
        private static bool _hadRoute;
        public static bool NoRoute { get { return _route == null && (_hadRoute ? _failures >= 2 : _failures >= 1); } }
        public static Transform Target { get { return _target; } }

        public static void SetTarget(Transform target, float goalRadius)
        {
            if (target == _target && Mathf.Approximately(goalRadius, _goal)) return;
            _target = target;
            _goal = goalRadius;
            _goodAttempt = 0;
            _failures = 0;
            _hadRoute = false;
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
                _attempt = _goodAttempt;
                _job = StartAttempt(pos, _attempt);
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
