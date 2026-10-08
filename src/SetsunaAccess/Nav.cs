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
        // Per half-metre cell. The game only drops you when the ground falls over 0.5 m within one frame
        // of movement (BaseCharacter.UpdateHeight), and pushes you up slopes physically, so hills are fine;
        // only real ledges and cliffs exceed these.
        private const float MaxRise = 0.8f, MaxDrop = 1.0f;
        // Height of the thin wall check between cells: ground-layer geometry crossing it is a rock face.
        private const float WallCheckHeight = 0.6f;

        private static int _groundMask = -1, _blockMask;
        private static readonly Dictionary<Cell, float> _ground = new Dictionary<Cell, float>(); // NaN = no ground
        private static readonly Dictionary<Cell, bool> _clear = new Dictionary<Cell, bool>();
        private static readonly HashSet<Cell> _blocked = new HashSet<Cell>(); // learned from getting stuck
        private static string _scene;

        public static Cell ToCell(Vector3 p) { return new Cell(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize)); }

        private static Vector3 Center(Cell c, float y) { return new Vector3((c.X + 0.5f) * CellSize, y, (c.Z + 0.5f) * CellSize); }

        /// <summary>Remember a cell the player couldn't get through, until the map changes.</summary>
        public static void MarkBlocked(Vector3 p) { _blocked.Add(ToCell(p)); _reachDirty = true; }

        /// <summary>
        /// The player pushed toward dir and didn't move. Mark a short strip of cells across the way as
        /// blocked (walls are long, one cell at a time learns too slowly), and look at what solid collider
        /// is actually there: if its layer isn't treated as blocking yet, start treating it so this session.
        /// Everything found is logged to nav.log.
        /// </summary>
        public static void LearnFromBump(Vector3 pos, Vector3 dir)
        {
            Prepare();
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            var side = Vector3.Cross(Vector3.up, dir);
            var ahead = pos + dir * (CellSize * 1.2f);
            for (var i = -1; i <= 1; i++) _blocked.Add(ToCell(ahead + side * (i * CellSize)));
            _reachDirty = true;

            var probe = pos + dir * 0.5f + Vector3.up * 0.8f;
            var player = LayerMask.NameToLayer("Player");
            var ground = LayerMask.NameToLayer("HitGround");
            // Whatever is underfoot is floor, never a wall to learn.
            RaycastHit under;
            var underLayer = Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out under, 2f, ~(1 << player))
                ? under.collider.gameObject.layer : -1;
            foreach (var col in Physics.OverlapSphere(probe, 0.6f))
            {
                if (col == null || col.isTrigger || col.gameObject.layer == player) continue;
                var layer = col.gameObject.layer;
                var bit = 1 << layer;
                var learned = layer != ground && layer != underLayer && (_blockMask & bit) == 0;
                if (learned) { _blockMask |= bit; _clear.Clear(); }
                Log.Append("nav.log", "bump: " + col.name + " layer " + LayerMask.LayerToName(layer) + (learned ? " (now blocking)" : ""));
            }
        }

        /// <summary>
        /// Route from <paramref name="from"/> to within <paramref name="goalRadius"/> of <paramref name="to"/>:
        /// world points, first = start. Null if no walkable route was found.
        /// </summary>
        public static List<Vector3> FindRoute(Vector3 from, Vector3 to, float goalRadius, int budget = 8000)
        {
            Prepare();
            ExpireClearance();
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
            var cells = GridPath.Find(start, target, isGoal, CanStep, budget);
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

        /// <summary>A route search that runs in slices; see Guide.</summary>
        public sealed class RouteJob
        {
            internal GridPath.SearchJob Search;
            internal Vector3 From;
            public float Goal;
            public bool Done { get { return Search.Done; } }
        }

        public static RouteJob StartRoute(Vector3 from, Vector3 to, float goalRadius, int budget = 8000, bool slim = false)
        {
            Prepare();
            if (_slim != slim)
            {
                _slim = slim;
                _clear.Clear();
                _wallCost.Clear();
            }
            ExpireClearance();
            var start = ToCell(from);
            _ground[start] = from.y;
            var r2 = Mathf.Max(goalRadius, CellSize * 1.5f);
            r2 *= r2;
            var goal = new Vector3(to.x, 0f, to.z);
            System.Func<Cell, bool> isGoal = c => (Center(c, 0f) - goal).sqrMagnitude <= r2;
            return new RouteJob { Search = new GridPath.SearchJob(start, ToCell(to), isGoal, CanStep, budget, WallCost), From = from, Goal = goalRadius };
        }

        /// <summary>Advance a route job for up to ms milliseconds; returns true when it has finished.</summary>
        public static bool StepRoute(RouteJob job, int ms)
        {
            _watch.Reset();
            _watch.Start();
            while (!job.Search.Done && _watch.ElapsedMilliseconds < ms) job.Search.Step(30);
            _watch.Stop();
            return job.Search.Done;
        }

        /// <summary>The finished job's route as world points (first = start), or null.</summary>
        public static List<Vector3> RouteOf(RouteJob job)
        {
            var cells = job.Search.Path;
            if (cells == null) return null;
            var pts = new List<Vector3>(cells.Count);
            foreach (var c in cells)
            {
                float y;
                pts.Add(Center(c, _ground.TryGetValue(c, out y) && !float.IsNaN(y) ? y : job.From.y));
            }
            pts[0] = job.From;
            return pts;
        }

        // ---- reachability (scanner filter) -------------------------------------------------

        // Last finished flood and the one running in the background (a slice per frame, see Tick).
        private static HashSet<Cell> _reach;
        private static bool _reachComplete;
        private static float _reachMaxDist;
        private static Vector3 _reachFrom;
        private static float _reachAt = -100f;
        private static string _reachScene;
        private static GridPath.FloodJob _job;
        private static Vector3 _jobFrom;
        private static bool _reachDirty;
        private static readonly System.Diagnostics.Stopwatch _watch = new System.Diagnostics.Stopwatch();

        public enum Reach { Yes, No, Unknown }

        /// <summary>
        /// Can the player walk to within <paramref name="slack"/> of <paramref name="p"/>? Answers from the
        /// last finished flood fill and never waits for a new one; Unknown when there's no usable fill yet
        /// or p is beyond what it covered.
        /// </summary>
        public static Reach CanReach(Vector3 player, Vector3 p, float slack)
        {
            Prepare();
            RequestFlood(player);
            if (_reach == null || _reachScene != _scene) return Reach.Unknown;
            var moved = player - _reachFrom; moved.y = 0f;
            if (moved.magnitude > 12f) return Reach.Unknown;

            var center = ToCell(p);
            var n = Mathf.CeilToInt(slack / CellSize);
            for (var dx = -n; dx <= n; dx++)
                for (var dz = -n; dz <= n; dz++)
                {
                    var c = new Cell(center.X + dx, center.Z + dz);
                    if (!_reach.Contains(c)) continue;
                    var d = Center(c, 0f) - new Vector3(p.x, 0f, p.z);
                    if (d.magnitude <= slack + CellSize * 0.75f) return Reach.Yes;
                }
            if (_reachComplete) return Reach.No;
            var far = p - _reachFrom; far.y = 0f;
            return far.magnitude < _reachMaxDist - 2f ? Reach.No : Reach.Unknown;
        }

        private static void RequestFlood(Vector3 player)
        {
            if (_job != null) return;
            var moved = player - _reachFrom; moved.y = 0f;
            var stale = _reach == null || _reachScene != _scene || _reachDirty
                        || Time.unscaledTime - _reachAt > 10f || moved.magnitude > 3f;
            if (!stale) return;
            var start = ToCell(player);
            _ground[start] = player.y;
            _whyLearned = _whyNoGround = _whyRise = _whyDrop = _whyBlocked = 0;
            _blockers.Clear();
            _thin.Clear();
            _job = new GridPath.FloodJob(start, CanStepLenient, 30000);
            _jobFrom = player;
            _reachDirty = false;
        }

        /// <summary>Per frame: advance the background flood for at most ~2 ms.</summary>
        public static void Tick()
        {
            if (_job == null) return;
            if (Application.loadedLevelName != _scene) { _job = null; return; }
            _watch.Reset();
            _watch.Start();
            while (!_job.Done && _watch.ElapsedMilliseconds < 2) _job.Step(40);
            _watch.Stop();
            if (!_job.Done) return;

            _reach = _job.Seen;
            _reachComplete = _job.Complete;
            _reachFrom = _jobFrom;
            _reachAt = Time.unscaledTime;
            _reachScene = _scene;
            _reachMaxDist = 0f;
            foreach (var c in _reach)
            {
                var d = Center(c, 0f) - new Vector3(_jobFrom.x, 0f, _jobFrom.z);
                if (d.magnitude > _reachMaxDist) _reachMaxDist = d.magnitude;
            }
            _job = null;
            Log.Append("nav.log", "flood " + _reach.Count + " cells, complete=" + _reachComplete + ", radius " + _reachMaxDist.ToString("0")
                                  + "; refused: no ground " + _whyNoGround + ", rise " + _whyRise + ", drop " + _whyDrop
                                  + ", blocked " + _whyBlocked + ", learned " + _whyLearned
                                  + "; party radius " + FieldPartyManager.CollisionRadius.ToString("0.00")
                                  + "; blockers: " + Blockers());
        }

        /// <summary>Forget the reachability fill (scene change, obstacles learned).</summary>
        public static void InvalidateReach() { _reachDirty = true; }

        /// <summary>
        /// Walk-to steering: if a wall is just ahead in dir, slide along it instead of pushing into it.
        /// </summary>
        public static Vector3 Slide(Vector3 pos, Vector3 dir)
        {
            Prepare();
            RaycastHit hit;
            var r = Radius() * 0.8f;
            if (!Physics.SphereCast(pos + Vector3.up * 0.6f, r, dir, out hit, 0.6f, _groundMask | _blockMask, QueryTriggerInteraction.Ignore))
                return dir;
            var n = hit.normal; n.y = 0f;
            if (n.sqrMagnitude < 0.01f) return dir;
            n.Normalize();
            var slide = dir - n * Vector3.Dot(dir, n);
            if (slide.sqrMagnitude < 0.05f) slide = Vector3.Cross(Vector3.up, n); // head-on: pick a side
            return slide.normalized;
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

        private static float _clearAt;

        /// <summary>Room-to-stand results are kept a few seconds: people move, walls don't.</summary>
        private static void ExpireClearance()
        {
            if (Time.unscaledTime - _clearAt < 8f) return;
            _clear.Clear();
            _clearAt = Time.unscaledTime;
        }

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
                _wallCost.Clear();
            }
        }

        /// <summary>
        /// Lenient stepping for the scanner's reachability filter: ground and walls, but not the knee-height
        /// rock-face line, so the filter only hides things there's truly no way to.
        /// </summary>
        private static bool CanStepLenient(Cell from, Cell to)
        {
            if (_blocked.Contains(to)) { _whyLearned++; return false; }
            float fromY;
            if (!_ground.TryGetValue(from, out fromY) || float.IsNaN(fromY)) return false;
            var toY = Ground(to, fromY);
            if (float.IsNaN(toY)) { _whyNoGround++; return false; }
            if (toY > fromY + MaxRise) { _whyRise++; return false; }
            if (toY < fromY - MaxDrop) { _whyDrop++; return false; }
            if (!ThinClear(to, toY)) { _whyBlocked++; return false; }
            return true;
        }

        private static readonly Dictionary<Cell, bool> _thin = new Dictionary<Cell, bool>();
        private static readonly Dictionary<string, int> _blockers = new Dictionary<string, int>();

        /// <summary>
        /// Room for anything at all to pass (12 cm capsule): the reachability filter's test. A body-width test
        /// on a half-metre grid refused the narrow forest gaps the game's own movement slips through.
        /// </summary>
        private static bool ThinClear(Cell c, float y)
        {
            bool ok;
            if (_thin.TryGetValue(c, out ok)) return ok;
            const float r = 0.12f;
            var h = Mathf.Max(FieldPartyManager.CollisionHeight, 1.2f);
            var p = Center(c, y);
            ok = !Physics.CheckCapsule(p + Vector3.up * (r + 0.3f), p + Vector3.up * Mathf.Max(h - r, r + 0.31f), r,
                                       _blockMask, QueryTriggerInteraction.Ignore);
            if (!ok && _blockers.Count < 64)
                foreach (var col in Physics.OverlapSphere(p + Vector3.up * 0.8f, 0.4f, _blockMask))
                {
                    if (col == null || col.isTrigger) continue;
                    var key = col.name + " [" + LayerMask.LayerToName(col.gameObject.layer) + "]";
                    int n; _blockers.TryGetValue(key, out n); _blockers[key] = n + 1;
                }
            _thin[c] = ok;
            return ok;
        }

        private static string Blockers()
        {
            var list = new List<KeyValuePair<string, int>>(_blockers);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            var parts = new List<string>();
            for (var i = 0; i < list.Count && i < 6; i++) parts.Add(list[i].Key + " x" + list[i].Value);
            return parts.Count == 0 ? "none" : string.Join(", ", parts.ToArray());
        }

        // Why flood steps were refused (logged with each finished flood).
        private static int _whyLearned, _whyNoGround, _whyRise, _whyDrop, _whyBlocked;

        private static bool CanStep(Cell from, Cell to)
        {
            if (_blocked.Contains(to)) return false;
            float fromY;
            if (!_ground.TryGetValue(from, out fromY) || float.IsNaN(fromY)) return false;
            var toY = Ground(to, fromY);
            if (float.IsNaN(toY) || toY > fromY + MaxRise || toY < fromY - MaxDrop) return false;
            if (!Clear(to, toY)) return false;
            // Rock faces and cliffs are part of the ground mesh (HitGround), and a downward ray that starts
            // inside one misses it. A thin line at knee height between the two cells catches the face, while
            // branches, arches and overhangs above it don't count (a full-height check hid real paths).
            return !Physics.Linecast(Center(from, fromY + WallCheckHeight), Center(to, toY + WallCheckHeight), _groundMask);
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
                                       _blockMask, QueryTriggerInteraction.Ignore)
                 // Rock faces belong to the ground mesh: short level rays at knee and waist height must not reach
                 // one within the body's radius. (Overhangs above waist height don't count.)
                 && !RingHits(p, 0.5f, r, _groundMask) && !RingHits(p, 1.0f, r, _groundMask);
            _clear[c] = ok;
            return ok;
        }

        private static readonly Vector3[] Ring =
        {
            new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f),
            new Vector3(0.7071f, 0f, 0.7071f), new Vector3(-0.7071f, 0f, 0.7071f),
            new Vector3(0.7071f, 0f, -0.7071f), new Vector3(-0.7071f, 0f, -0.7071f)
        };

        private static bool RingHits(Vector3 floor, float height, float length, int mask)
        {
            var o = floor + Vector3.up * height;
            foreach (var d in Ring)
                if (Physics.Raycast(o, d, length, mask, QueryTriggerInteraction.Ignore)) return true;
            return false;
        }

        private static readonly Dictionary<Cell, float> _wallCost = new Dictionary<Cell, float>();

        /// <summary>Extra route cost for cells close to a wall, so routes keep to the middle of paths.</summary>
        private static float WallCost(Cell c)
        {
            float cost;
            if (_wallCost.TryGetValue(c, out cost)) return cost;
            float y;
            if (!_ground.TryGetValue(c, out y) || float.IsNaN(y)) return 0f;
            var near = Radius() + 0.45f;
            cost = RingHits(Center(c, y), 0.6f, near, _groundMask | _blockMask) ? 1.5f : 0f;
            _wallCost[c] = cost;
            return cost;
        }

        private static float _bodyRadius = -1f;
        private static string _bodyScene;

        /// <summary>
        /// The party leader's real capsule radius in world units (about 0.5 in play). Routes plan with nearly all
        /// of it: planning slimmer sent walk-to along rock faces it then kept bumping into.
        /// </summary>
        public static float BodyRadius()
        {
            if (_bodyRadius > 0f && _bodyScene == _scene) return _bodyRadius;
            var r = 0.4f;
            var m = FieldPartyManager.Member;
            var leader = m != null && m.Count > 0 ? m[0] : null;
            var cap = leader == null ? null : leader.Capsule;
            if (cap != null) r = cap.radius * Mathf.Max(cap.transform.lossyScale.x, cap.transform.lossyScale.z);
            else if (FieldPartyManager.CollisionRadius > 0.05f) r = FieldPartyManager.CollisionRadius;
            _bodyRadius = Mathf.Clamp(r, 0.15f, 0.8f);
            _bodyScene = _scene;
            Log.Append("nav.log", "body radius " + _bodyRadius.ToString("0.00"));
            return _bodyRadius;
        }

        // Slim mode: a fallback plan for narrow doorways the half-metre grid can't fit the full body through.
        private static bool _slim;

        private static float Radius() { return _slim ? 0.22f : BodyRadius() * 0.85f; }

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
            if (Physics.Linecast(a + Vector3.up * WallCheckHeight, b + Vector3.up * WallCheckHeight, _groundMask)) return false;
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
