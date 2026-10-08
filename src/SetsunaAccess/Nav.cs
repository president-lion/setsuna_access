using System.Collections.Generic;
using Setsuna;
using UnityEngine;
using Cell = SetsunaAccess.GridPath.Cell;

namespace SetsunaAccess
{
    /// <summary>
    /// Walkable-route finding on the field. The maps have no navmesh, so the grid is probed
    /// from the game's own collision, the way characters actually move:
    ///  - ground: a downward ray on "HitGround" like BaseCharacter.UpdateHeight; a step may rise
    ///    or drop at most a little per half-metre cell (bigger drops are where the game makes you fall);
    ///  - room to stand: a capsule the size of the party's collider against "HitWall" and NPCs.
    /// Cells are probed lazily by A* (GridPath). A route is a list of points; guidance aims at the
    /// farthest point still reachable in a straight line.
    /// </summary>
    internal static class Nav
    {
        public const float CellSize = 0.5f;
        private const float MaxRise = 0.5f, MaxDrop = 0.45f;

        private static int _groundMask = -1, _blockMask;
        private static readonly Dictionary<Cell, float> _ground = new Dictionary<Cell, float>(); // NaN = no ground
        private static readonly Dictionary<Cell, bool> _clear = new Dictionary<Cell, bool>();
        private static readonly HashSet<Cell> _blocked = new HashSet<Cell>(); // learned from getting stuck
        private static string _scene;

        public static Cell ToCell(Vector3 p) { return new Cell(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize)); }

        private static Vector3 Center(Cell c, float y) { return new Vector3((c.X + 0.5f) * CellSize, y, (c.Z + 0.5f) * CellSize); }

        /// <summary>Remember a cell the player couldn't get through, until the map changes.</summary>
        public static void MarkBlocked(Vector3 p) { _blocked.Add(ToCell(p)); }

        /// <summary>
        /// Route from <paramref name="from"/> to within <paramref name="goalRadius"/> of <paramref name="to"/>:
        /// world points, first = start. Null if no walkable route was found.
        /// </summary>
        public static List<Vector3> FindRoute(Vector3 from, Vector3 to, float goalRadius)
        {
            Prepare();
            _clear.Clear(); // people move; ground doesn't
            var start = ToCell(from);
            _ground[start] = from.y;
            var target = ToCell(to);
            var r2 = Mathf.Max(goalRadius, CellSize * 1.5f);
            r2 *= r2;
            System.Func<Cell, bool> isGoal = c =>
            {
                var d = Center(c, 0f) - new Vector3(to.x, 0f, to.z);
                return d.sqrMagnitude <= r2;
            };
            var cells = GridPath.Find(start, target, isGoal, CanStep, 12000);
            if (cells == null) return null;
            var pts = new List<Vector3>(cells.Count);
            foreach (var c in cells)
            {
                float y;
                pts.Add(Center(c, _ground.TryGetValue(c, out y) && !float.IsNaN(y) ? y : from.y));
            }
            pts[0] = from;
            return pts;
        }

        /// <summary>Index of the farthest route point (within lookAhead points) reachable straight from pos.</summary>
        public static int LookAhead(List<Vector3> route, int fromIndex, Vector3 pos, int lookAhead = 24)
        {
            var best = Mathf.Min(fromIndex + 1, route.Count - 1);
            var end = Mathf.Min(route.Count - 1, fromIndex + lookAhead);
            for (var i = end; i > fromIndex; i--)
                if (StraightClear(pos, route[i])) return i;
            return best;
        }

        /// <summary>Index of the route point nearest to pos (searching from a hint onwards).</summary>
        public static int Nearest(List<Vector3> route, Vector3 pos, int hint)
        {
            var best = hint;
            var bestD = float.MaxValue;
            for (var i = Mathf.Max(0, hint - 4); i < route.Count; i++)
            {
                var d = route[i] - pos;
                d.y = 0f;
                if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; best = i; }
            }
            return best;
        }

        public static float Length(List<Vector3> route, int fromIndex)
        {
            var len = 0f;
            for (var i = fromIndex + 1; i < route.Count; i++)
            {
                var d = route[i] - route[i - 1];
                d.y = 0f;
                len += d.magnitude;
            }
            return len;
        }

        // ---- probing --------------------------------------------------------------------

        private static void Prepare()
        {
            if (_groundMask == -1)
            {
                _groundMask = LayerMask.GetMask("HitGround");
                _blockMask = LayerMask.GetMask("HitWall", "NPC", "Enemy");
            }
            var scene = Application.loadedLevelName;
            if (scene != _scene)
            {
                _scene = scene;
                _ground.Clear();
                _blocked.Clear();
            }
        }

        private static bool CanStep(Cell from, Cell to)
        {
            if (_blocked.Contains(to)) return false;
            float fromY;
            if (!_ground.TryGetValue(from, out fromY) || float.IsNaN(fromY)) return false;
            var toY = Ground(to, fromY);
            if (float.IsNaN(toY) || toY > fromY + MaxRise || toY < fromY - MaxDrop) return false;
            return Clear(to, toY);
        }

        /// <summary>Ground height in a cell, probing from just above the neighbour's height (bridges, stairs).</summary>
        private static float Ground(Cell c, float refY)
        {
            float y;
            if (_ground.TryGetValue(c, out y) && (float.IsNaN(y) || Mathf.Abs(y - refY) < 2f)) return y;
            RaycastHit hit;
            var origin = Center(c, refY + 1.5f);
            y = Physics.Raycast(origin, Vector3.down, out hit, 1.5f + 2.5f, _groundMask) ? hit.point.y : float.NaN;
            _ground[c] = y;
            return y;
        }

        private static bool Clear(Cell c, float y)
        {
            bool ok;
            if (_clear.TryGetValue(c, out ok)) return ok;
            var r = Radius();
            var h = Mathf.Max(FieldPartyManager.CollisionHeight, r * 2f + 0.2f);
            var p = Center(c, y);
            ok = !Physics.CheckCapsule(p + Vector3.up * (r + 0.3f), p + Vector3.up * Mathf.Max(h - r, r + 0.31f), r,
                                       _blockMask, QueryTriggerInteraction.Ignore);
            _clear[c] = ok;
            return ok;
        }

        private static float Radius()
        {
            var r = FieldPartyManager.CollisionRadius;
            return r > 0.05f && r < 1.5f ? r * 0.9f : 0.3f;
        }

        /// <summary>Walkable in a straight line: nothing in the way at body height, and no steps too big along it.</summary>
        private static bool StraightClear(Vector3 a, Vector3 b)
        {
            var flat = b - a;
            flat.y = 0f;
            var dist = flat.magnitude;
            if (dist < 0.05f) return true;
            var r = Radius();
            var origin = a + Vector3.up * (r + 0.35f);
            var dir = flat / dist;
            if (Physics.SphereCast(origin, r, dir, out _hit, dist, _blockMask, QueryTriggerInteraction.Ignore)) return false;
            var y = a.y;
            for (var t = CellSize; t < dist; t += CellSize)
            {
                var c = ToCell(a + dir * t);
                if (_blocked.Contains(c)) return false;
                var gy = Ground(c, y);
                if (float.IsNaN(gy) || gy > y + MaxRise || gy < y - MaxDrop) return false;
                y = gy;
            }
            return true;
        }

        private static RaycastHit _hit;
    }
}
