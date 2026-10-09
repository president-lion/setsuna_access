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
        // Per half-metre cell. Climbing is limited (the game pushes you up slopes and small steps), but walking
        // off a ledge is always allowed: PlayerControl.WalkUpdate keeps moving when there's no ground below and
        // BaseCharacter.UpdateHeight then lets the party fall. Frost Caves room 2 is laid out around 3 m drops
        // (ramp up, drop to the far floor, climb to the objective), which a 1 m limit called walls. Drops are
        // one-way steps, which the directed flood and A* handle.
        private const float MaxRise = 0.8f, MaxDrop = 6f;
        // Height of the thin wall check between cells: ground-layer geometry crossing it is a rock face.
        private const float WallCheckHeight = 0.6f;

        private static int _groundMask = -1, _blockMask;
        // Each grid square may hold several walking surfaces (a cave floor and a ledge above it); each surface is
        // its own cell, told apart by Level. _y is a cell's ground height, _columns lists the cells in a square,
        // _probe caches ground probes by square and the height they were probed from (NaN = no ground).
        private static readonly Dictionary<Cell, float> _y = new Dictionary<Cell, float>();
        private static readonly Dictionary<long, List<Cell>> _columns = new Dictionary<long, List<Cell>>();
        private static readonly Dictionary<Cell, float> _probe = new Dictionary<Cell, float>();
        private static readonly Dictionary<Cell, bool> _clear = new Dictionary<Cell, bool>();
        private static readonly HashSet<Cell> _blocked = new HashSet<Cell>(); // learned from getting stuck
        private static string _scene;

        public static Cell ToCell(Vector3 p) { return CellAt(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize), p.y); }

        /// <summary>
        /// The cell for the surface at height y in square (x, z): an existing one within 1.2 m, or a new level.
        /// Floors stacked closer than that are one surface to the grid.
        /// </summary>
        private static Cell CellAt(int x, int z, float y)
        {
            var key = ((long)x << 32) ^ (uint)z;
            List<Cell> list;
            if (!_columns.TryGetValue(key, out list)) { list = new List<Cell>(2); _columns[key] = list; }
            foreach (var c in list)
                if (Mathf.Abs(_y[c] - y) < 1.2f) return c;
            var level = Mathf.RoundToInt(y);
            while (list.Exists(c => c.Level == level)) level++;
            var cell = new Cell(x, z, level);
            _y[cell] = y;
            list.Add(cell);
            return cell;
        }

        private static List<Cell> Column(int x, int z)
        {
            List<Cell> list;
            return _columns.TryGetValue(((long)x << 32) ^ (uint)z, out list) ? list : null;
        }

        private static float Y(Cell c, float fallback)
        {
            float y;
            return _y.TryGetValue(c, out y) ? y : fallback;
        }

        private static Vector3 Center(Cell c, float y) { return new Vector3((c.X + 0.5f) * CellSize, y, (c.Z + 0.5f) * CellSize); }

        /// <summary>Remember a cell the player couldn't get through, until the map changes.</summary>
        public static void MarkBlocked(Vector3 p) { _blocked.Add(ToCell(p)); _reachDirty = true; }

        /// <summary>
        /// The player pushed toward dir and didn't move. Mark a short strip of cells across the way as
        /// blocked (walls are long, one cell at a time learns too slowly), and look at what solid collider
        /// is actually there: if its layer isn't treated as blocking yet, start treating it so this session.
        /// Everything found is logged to nav.log.
        /// </summary>
        public static void LearnFromBump(Vector3 pos, Vector3 dir, int repeats = 0)
        {
            Prepare();
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            var side = Vector3.Cross(Vector3.up, dir);
            var ahead = pos + dir * (CellSize * 1.2f);
            // Bumping the same spot again means the obstacle is wider than marked (the plan kept going back through
            // it, Mysleigh Woods): widen and deepen the blocked patch each time.
            var half = 1 + Mathf.Min(repeats, 4);
            var depth = repeats >= 2 ? 2 : 1;
            for (var row = 0; row < depth; row++)
                for (var i = -half; i <= half; i++)
                    _blocked.Add(ToCell(ahead + dir * (row * CellSize) + side * (i * CellSize)));
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

        /// <summary>A route search that runs in slices; see Guide.</summary>
        public sealed class RouteJob
        {
            internal GridPath.SearchJob Search;
            internal Vector3 From;
            public float Goal;
            public bool Done { get { return Search.Done; } }
        }

        /// <summary>How strictly a route is planned: the full body, a slim body for narrow doorways, or the
        /// reachability flood's own lenient rules (no rock-face rays) with a strong pull to the middle of paths.</summary>
        public enum Mode { Full, Slim, Lenient }

        public static RouteJob StartRoute(Vector3 from, Vector3 to, float goalRadius, int budget = 8000, Mode mode = Mode.Full,
                                          bool needSight = false)
        {
            Prepare();
            if (_mode != mode)
            {
                _mode = mode;
                _clear.Clear();
                _wallCost.Clear();
            }
            _whyR = new int[6];
            ExpireClearance();
            var start = ToCell(from);
            _y[start] = from.y;
            var r2 = Mathf.Max(goalRadius, CellSize * 1.5f);
            r2 *= r2;
            var goal = new Vector3(to.x, 0f, to.z);
            // Near the target and on its level (not on the floor under a ledge it stands on).
            System.Func<Cell, bool> isGoal = c => (Center(c, 0f) - goal).sqrMagnitude <= r2 && Mathf.Abs(Y(c, to.y) - to.y) <= 3f
                                                  && (!needSight || SightTo(c, to));
            GridPath.Neighbour step = mode == Mode.Lenient ? (GridPath.Neighbour)NbRoute : NbStrict;
            var target = new Cell(Mathf.FloorToInt(to.x / CellSize), Mathf.FloorToInt(to.z / CellSize));
            return new RouteJob { Search = new GridPath.SearchJob(start, target, isGoal, step, budget, WallCost), From = from, Goal = goalRadius };
        }

        /// <summary>
        /// A clear knee-height line from the cell to an exit point, or one that only meets a door (house doors are
        /// solid colliders you walk into). Without it a loose exit goal could be outside the house wall, and the
        /// straight final approach bumped the wall (Floneia Citadel).
        /// </summary>
        private static bool SightTo(Cell c, Vector3 target)
        {
            var a = Center(c, Y(c, target.y) + WallCheckHeight);
            var b = new Vector3(target.x, target.y + WallCheckHeight, target.z);
            RaycastHit h;
            var mask = _groundMask | LayerMask.GetMask("HitWall");
            if (Physics.Linecast(a, b, out h, mask, QueryTriggerInteraction.Ignore) && !IsDoor(h.collider)) return false;
            if (Physics.Linecast(b, a, out h, mask, QueryTriggerInteraction.Ignore) && !IsDoor(h.collider)) return false;
            return true;
        }

        private static bool IsDoor(Collider c)
        {
            return c != null && c.name.IndexOf("door", System.StringComparison.OrdinalIgnoreCase) >= 0;
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
            foreach (var c in cells) pts.Add(Center(c, Y(c, job.From.y)));
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
            // A complete fill is the whole area connected to where it started: still right anywhere inside it.
            if (_reachComplete ? !InReach(player) : moved.magnitude > 12f) return Reach.Unknown;

            int cx = Mathf.FloorToInt(p.x / CellSize), cz = Mathf.FloorToInt(p.z / CellSize);
            var n = Mathf.CeilToInt(slack / CellSize);
            for (var dx = -n; dx <= n; dx++)
                for (var dz = -n; dz <= n; dz++)
                {
                    var col = Column(cx + dx, cz + dz);
                    if (col == null) continue;
                    foreach (var c in col)
                    {
                        // A surface within reach of p's height (not a floor far below a ledge it's on).
                        if (!_reach.Contains(c) || Mathf.Abs(Y(c, p.y) - p.y) > 3f) continue;
                        var d = Center(c, 0f) - new Vector3(p.x, 0f, p.z);
                        if (d.magnitude <= slack + CellSize * 0.75f) return Reach.Yes;
                    }
                }
            if (_reachComplete) return Reach.No;
            var far = p - _reachFrom; far.y = 0f;
            return far.magnitude < _reachMaxDist - 2f ? Reach.No : Reach.Unknown;
        }

        private static void RequestFlood(Vector3 player)
        {
            if (_job != null) return;
            var moved = player - _reachFrom; moved.y = 0f;
            // An unfinished fill (budget ran out) is redone as the player moves; a complete one only when the
            // player leaves it, something was learned, or now and then for people who moved. The world map
            // needs a full fill or far exits stay "unknown" and are never hidden.
            var age = Time.unscaledTime - _reachAt;
            var stale = _reach == null || _reachScene != _scene || _reachDirty
                        || (_reachComplete ? age > 60f || !InReach(player) : age > 10f || moved.magnitude > 3f);
            if (!stale) return;
            var start = ToCell(player);
            _y[start] = player.y;
            _whyLearned = _whyNoGround = _whyRise = _whyDrop = _whyBlocked = _whyWall = 0;
            _blockers.Clear();
            _thin.Clear();
            _job = new GridPath.FloodJob(start, NbFlood, IsWorld ? 400000 : 60000);
            _jobFrom = player;
            _reachDirty = false;
        }

        private static bool IsWorld { get { return string.Equals(_scene, Common.SCENE_NAME_WORLD_MAP, System.StringComparison.OrdinalIgnoreCase); } }

        private static bool InReach(Vector3 p)
        {
            int cx = Mathf.FloorToInt(p.x / CellSize), cz = Mathf.FloorToInt(p.z / CellSize);
            for (var dx = -1; dx <= 1; dx++)
                for (var dz = -1; dz <= 1; dz++)
                {
                    var col = Column(cx + dx, cz + dz);
                    if (col == null) continue;
                    foreach (var c in col)
                        if (_reach.Contains(c) && Mathf.Abs(Y(c, p.y) - p.y) < 1.5f) return true;
                }
            return false;
        }

        /// <summary>Per frame: advance the background flood for at most ~2 ms.</summary>
        public static void Tick()
        {
            if (_job == null) return;
            if (Application.loadedLevelName != _scene) { _job = null; return; }
            _watch.Reset();
            _watch.Start();
            // The world map fill is big; at 30 fps the game uses ~3 ms of each 33 ms frame, so 4 ms is spare.
            var ms = IsWorld ? 4 : 2;
            while (!_job.Done && _watch.ElapsedMilliseconds < ms) _job.Step(40);
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
            Log.Append("nav.log", "flood from " + NavLog.P(_jobFrom) + ": " + _reach.Count + " cells, complete=" + _reachComplete + ", radius " + _reachMaxDist.ToString("0")
                                  + "; refused: no ground " + _whyNoGround + ", rise " + _whyRise + ", drop " + _whyDrop
                                  + ", blocked " + _whyBlocked + ", wall " + _whyWall + ", learned " + _whyLearned
                                  + "; party radius " + FieldPartyManager.CollisionRadius.ToString("0.00")
                                  + "; blockers: " + Blockers());
        }

        /// <summary>
        /// nav.log: the ground and wall colliders the game adds at run time (bridges, ramps, barriers such as
        /// Serendale's pCube boxes), which aren't in the scene files and so can't be checked offline.
        /// </summary>
        public static void LogExtraColliders()
        {
            Prepare();
            var n = 0;
            foreach (var c in Object.FindObjectsOfType<Collider>())
            {
                if (c == null || c.isTrigger || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                var bit = 1 << c.gameObject.layer;
                if (((_groundMask | _blockMask) & bit) == 0 || c.gameObject.layer == LayerMask.NameToLayer("NPC") || c.gameObject.layer == LayerMask.NameToLayer("Enemy")) continue;
                if (c is MeshCollider && (c.name == "MergedCollider" || c.name == "HitGround" || c.name == "HitWall")) continue;
                var b = c.bounds;
                var parent = c.transform.parent == null ? "" : c.transform.parent.name + "/";
                NavLog.Line("collider: " + parent + c.name + " " + c.GetType().Name + " [" + LayerMask.LayerToName(c.gameObject.layer) + "] centre "
                            + NavLog.P(b.center) + " size " + NavLog.P(b.size) + " top " + b.max.y.ToString("0.0"));
                if (++n >= 60) { NavLog.Line("collider: (more not listed)"); break; }
            }
            if (n == 0) NavLog.Line("collider: no extra ground or wall colliders");
        }

        /// <summary>The ground itself changed (a bridge lowered): drop everything probed on this map.</summary>
        public static void ForgetGeometry()
        {
            ResetSurfaces();
            _clear.Clear();
            _thin.Clear();
            _wallCost.Clear();
            _blocked.Clear();
            _reachDirty = true;
            _job = null;
        }

        /// <summary>Forget the reachability fill (scene change, obstacles learned).</summary>
        public static void InvalidateReach() { _reachDirty = true; }

        /// <summary>
        /// Walk-to steering: if a wall is just ahead in dir, slide along it instead of pushing into it.
        /// </summary>
        public static Vector3 Slide(Vector3 pos, Vector3 dir)
        {
            Prepare();
            var r = Radius() * 0.8f;
            // Only upright faces are walls. A ramp ahead (Mysleigh Woods, ~30 degrees) meets the sphere too, and its
            // normal points straight back, so the old test read it as a head-on wall and steered sideways into the
            // rocks beside the ramp; the game itself walks up it fine.
            var found = false;
            var best = new RaycastHit();
            foreach (var h in Physics.SphereCastAll(pos + Vector3.up * 0.6f, r, dir, 0.6f, _groundMask | _blockMask, QueryTriggerInteraction.Ignore))
            {
                var isBlock = (_blockMask & (1 << h.collider.gameObject.layer)) != 0;
                if (!isBlock && (h.distance <= 0f || Mathf.Abs(h.normal.y) >= 0.35f)) continue;
                if (!found || h.distance < best.distance) { best = h; found = true; }
            }
            if (!found) return dir;
            var n = best.normal; n.y = 0f;
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
                _walked.Clear();
                _walkedY.Clear();
                ResetSurfaces();
                _blocked.Clear();
                _wallCost.Clear();
                _hasWalkPos = false;
                _walkLogs = 0;
            }
        }

        /// <summary>Forget probed ground and surfaces; cells walked this visit are kept (with their heights).</summary>
        private static void ResetSurfaces()
        {
            _y.Clear();
            _columns.Clear();
            _probe.Clear();
            _clear.Clear();
            _thin.Clear();
            foreach (var kv in _walkedY) Register(kv.Key, kv.Value);
        }

        private static void Register(Cell c, float y)
        {
            _y[c] = y;
            var key = ((long)c.X << 32) ^ (uint)c.Z;
            List<Cell> list;
            if (!_columns.TryGetValue(key, out list)) { list = new List<Cell>(2); _columns[key] = list; }
            if (!list.Contains(c)) list.Add(c);
        }

        // ---- where the player has actually walked ------------------------------------------

        // Cells the leader has stood in this visit. A step between two of them is walkable whatever the
        // probes say: in Serendale the player ran down a street both the planner and the flood called walled.
        private static readonly HashSet<Cell> _walked = new HashSet<Cell>();
        private static readonly Dictionary<Cell, float> _walkedY = new Dictionary<Cell, float>();
        private static Vector3 _walkPos;
        private static bool _hasWalkPos;
        private static int _walkLogs;

        /// <summary>Per frame on the field: record the cells the leader passes through.</summary>
        public static void TrackWalked(Vector3 p)
        {
            Prepare();
            if (_hasWalkPos)
            {
                var d = p - _walkPos; d.y = 0f;
                if (d.sqrMagnitude < 0.04f) return;
                if (d.magnitude > 3f) { _hasWalkPos = false; } // warped (scene start, event): no line between
            }
            if (!_hasWalkPos) { _walkPos = p; _hasWalkPos = true; Walk(ToCell(p), p.y); return; }
            var from = _walkPos;
            var flat = p - from; flat.y = 0f;
            var steps = Mathf.CeilToInt(flat.magnitude / (CellSize * 0.5f));
            var prev = ToCell(from);
            for (var i = 1; i <= steps; i++)
            {
                var q = Vector3.Lerp(from, p, (float)i / steps);
                var c = ToCell(q);
                if (c.Equals(prev)) continue;
                float prevY;
                var hasPrev = _y.TryGetValue(prev, out prevY);
                // The game lets the party clip into wall meshes (it moves by transform), so a step through an
                // upright face is the body going into a wall, not a way through: don't learn it.
                if (hasPrev && UprightWall(prev, prevY, c, q.y, false))
                {
                    if (_walkLogs < 40) { _walkLogs++; NavLog.Line("clipped into a wall at " + NavLog.P(q) + ", not learned"); }
                    prev = c;
                    continue;
                }
                if (hasPrev && !_walked.Contains(c) && _walkLogs < 40)
                {
                    var why = Refusal(prev, prevY, c, q.y);
                    if (why != null) { _walkLogs++; NavLog.Line("walked a step the probes refuse, at " + NavLog.P(q) + ": " + why); }
                }
                Walk(c, q.y);
                prev = c;
            }
            _walkPos = p;
        }

        private static void Walk(Cell c, float y)
        {
            _walkedY[c] = y;
            _probe[new Cell(c.X, c.Z, Mathf.FloorToInt(y))] = y;
            _blocked.Remove(c);
            if (_walked.Add(c) && _reach != null && !_reach.Contains(c)) _reachDirty = true;
        }

        private static bool Walked(Cell from, Cell to) { return _walked.Contains(from) && _walked.Contains(to); }

        /// <summary>Why the strict and lenient rules would refuse this step, with the collider in the way (nav.log).</summary>
        private static string Refusal(Cell from, float fromY, Cell to, float toY)
        {
            var parts = new List<string>();
            var p = Center(to, toY);
            RaycastHit h;
            var a = Center(from, fromY + WallCheckHeight);
            var b = Center(to, toY + WallCheckHeight);
            if (Physics.Linecast(a, b, out h, _groundMask, QueryTriggerInteraction.Ignore) || Physics.Linecast(b, a, out h, _groundMask, QueryTriggerInteraction.Ignore))
                parts.Add("knee line hits " + h.collider.name + " (normal y " + h.normal.y.ToString("0.00") + ")");
            foreach (var height in new[] { 0.5f, 1.0f })
                foreach (var dir in Ring)
                    if (Physics.Raycast(p + Vector3.up * height, dir, out h, BodyRadius() * 0.85f, _groundMask, QueryTriggerInteraction.Ignore))
                    {
                        parts.Add("ray at " + height.ToString("0.0") + " m hits " + h.collider.name + " " + h.distance.ToString("0.00") + " m away");
                        goto rays;
                    }
            rays:
            var r = BodyRadius() * 0.85f;
            var hh = Mathf.Max(FieldPartyManager.CollisionHeight, r * 2f + 0.2f);
            if (Physics.CheckCapsule(p + Vector3.up * (r + 0.3f), p + Vector3.up * Mathf.Max(hh - r, r + 0.31f), r, _blockMask, QueryTriggerInteraction.Ignore))
                foreach (var col in Physics.OverlapSphere(p + Vector3.up * 0.9f, r + 0.2f, _blockMask))
                    parts.Add("body overlaps " + col.name + " [" + LayerMask.LayerToName(col.gameObject.layer) + "]");
            return parts.Count == 0 ? null : string.Join("; ", parts.ToArray());
        }

        /// <summary>
        /// Where a step lands: 0 = on cell "to", 2 = no ground, 3 = too steep a rise, 4 = too far a drop
        /// (1 = the start cell has no known height). The ground is probed from just above the step's own
        /// height, so a ledge over a floor and the floor under it are found from their own levels.
        /// </summary>
        private static int Land(Cell from, int dx, int dz, out Cell to, out float fromY, out float toY)
        {
            to = default(Cell);
            toY = 0f;
            if (!_y.TryGetValue(from, out fromY)) return 1;
            int x = from.X + dx, z = from.Z + dz;
            toY = Ground(x, z, fromY);
            if (float.IsNaN(toY)) return 2;
            if (toY > fromY + MaxRise) return 3;
            if (toY < fromY - MaxDrop) return 4;
            to = CellAt(x, z, toY);
            return 0;
        }

        /// <summary>A walked cell next door at a reachable height, when the probes found nothing walkable.</summary>
        private static Cell? WalkedNext(Cell from, int dx, int dz, float fromY)
        {
            if (!_walked.Contains(from)) return null;
            var col = Column(from.X + dx, from.Z + dz);
            if (col == null) return null;
            foreach (var c in col)
            {
                if (!_walked.Contains(c)) continue;
                var y = Y(c, fromY);
                if (y <= fromY + MaxRise + 0.2f && y >= fromY - MaxDrop) return c;
            }
            return null;
        }

        /// <summary>
        /// Lenient stepping for the scanner's reachability filter: ground, walls and upright ground-layer faces,
        /// but not the strict rock-face rules, so the filter only hides things there's truly no way to.
        /// </summary>
        private static Cell? NbFlood(Cell from, int dx, int dz)
        {
            Cell to;
            float fromY, toY;
            var r = Land(from, dx, dz, out to, out fromY, out toY);
            if (r != 0)
            {
                var w = WalkedNext(from, dx, dz, fromY);
                if (w != null) return w;
                if (r == 2) _whyNoGround++; else if (r == 3) _whyRise++; else if (r == 4) _whyDrop++;
                return null;
            }
            if (Walked(from, to)) return to;
            if (_blocked.Contains(to)) { _whyLearned++; return null; }
            if (!ThinClear(to, toY)) { _whyBlocked++; return null; }
            if (UprightWall(from, fromY, to, toY, true)) { _whyWall++; return null; }
            return to;
        }

        /// <summary>
        /// A wall on the ground layer between two cells: a knee-height line between them meets a near-vertical
        /// face (checked both ways, mesh faces are one-sided). Serendale's barriers (wall1, pCube boxes) are
        /// ground-layer boxes the thin capsule ignores; sloped ground and bumps have faces that point up, so
        /// they don't count (the full rock-face rules over-hid paths in Dazzshire Woods).
        /// </summary>
        private static bool UprightWall(Cell from, float fromY, Cell to, float toY, bool note)
        {
            var a = Center(from, fromY + WallCheckHeight);
            var b = Center(to, toY + WallCheckHeight);
            RaycastHit h;
            if ((Physics.Linecast(a, b, out h, _groundMask, QueryTriggerInteraction.Ignore) && Mathf.Abs(h.normal.y) < 0.35f)
                || (Physics.Linecast(b, a, out h, _groundMask, QueryTriggerInteraction.Ignore) && Mathf.Abs(h.normal.y) < 0.35f))
            {
                if (note && _blockers.Count < 64)
                {
                    var key = h.collider.name + " [ground wall]";
                    int n; _blockers.TryGetValue(key, out n); _blockers[key] = n + 1;
                }
                return true;
            }
            return false;
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
        private static int _whyLearned, _whyNoGround, _whyRise, _whyDrop, _whyBlocked, _whyWall;

        // Why route steps were refused: learned, no ground, rise, drop, body/rays, knee line (logged on a failed search).
        private static int[] _whyR = new int[6];

        public static string RouteRefusals()
        {
            return "learned " + _whyR[0] + ", no ground " + _whyR[1] + ", rise " + _whyR[2] + ", drop " + _whyR[3]
                   + ", body " + _whyR[4] + ", rock face " + _whyR[5];
        }

        private static Cell? NbStrict(Cell from, int dx, int dz)
        {
            Cell to;
            float fromY, toY;
            var r = Land(from, dx, dz, out to, out fromY, out toY);
            if (r != 0)
            {
                var w = WalkedNext(from, dx, dz, fromY);
                if (w != null) return w;
                if (r >= 2) _whyR[r - 1]++;
                return null;
            }
            if (Walked(from, to)) return to;
            if (_blocked.Contains(to)) { _whyR[0]++; return null; }
            if (!Clear(to, toY)) { _whyR[4]++; return null; }
            // Rock faces and cliffs are part of the ground mesh (HitGround), and a downward ray that starts
            // inside one misses it. A thin line at knee height between the two cells catches the face, while
            // branches, arches and overhangs above it don't count (a full-height check hid real paths).
            if (Physics.Linecast(Center(from, fromY + WallCheckHeight), Center(to, toY + WallCheckHeight), _groundMask)) { _whyR[5]++; return null; }
            return to;
        }

        /// <summary>
        /// Lenient route stepping: the reachability flood's rules (ground, slope, a thin capsule against walls
        /// and people). The last fallback when the body-width plan finds the area closed off (stairs and town
        /// steps whose risers the rock-face rays mistake for walls). WallCost keeps it to the middle of paths.
        /// </summary>
        private static Cell? NbRoute(Cell from, int dx, int dz)
        {
            Cell to;
            float fromY, toY;
            var r = Land(from, dx, dz, out to, out fromY, out toY);
            if (r != 0)
            {
                var w = WalkedNext(from, dx, dz, fromY);
                if (w != null) return w;
                if (r >= 2) _whyR[r - 1]++;
                return null;
            }
            if (Walked(from, to)) return to;
            if (_blocked.Contains(to)) { _whyR[0]++; return null; }
            if (!ThinClear(to, toY)) { _whyR[4]++; return null; }
            if (UprightWall(from, fromY, to, toY, false)) { _whyR[5]++; return null; }
            return to;
        }

        /// <summary>
        /// Ground height in square (x, z), probing down from just above refY (the neighbour's height), so a
        /// bridge, stairs or a ledge is found from its own level. Cached per square and 1 m of refY.
        /// </summary>
        private static float Ground(int x, int z, float refY)
        {
            var key = new Cell(x, z, Mathf.FloorToInt(refY));
            float y;
            if (_probe.TryGetValue(key, out y)) return y;
            RaycastHit hit;
            var origin = new Vector3((x + 0.5f) * CellSize, refY + 1.5f, (z + 0.5f) * CellSize);
            y = Physics.Raycast(origin, Vector3.down, out hit, 1.5f + MaxDrop + 0.5f, _groundMask) ? hit.point.y : float.NaN;
            _probe[key] = y;
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
            if (!_y.TryGetValue(c, out y)) return 0f;
            var near = Radius() + 0.45f;
            var p = Center(c, y);
            if (_mode == Mode.Lenient)
                // No body test in this mode: keep well clear of anything within the body's width.
                cost = RingHits(p, 0.6f, BodyRadius(), _groundMask | _blockMask) ? 4f
                     : RingHits(p, 0.6f, BodyRadius() + 0.45f, _groundMask | _blockMask) ? 1.5f : 0f;
            else cost = RingHits(p, 0.6f, near, _groundMask | _blockMask) ? 1.5f : 0f;
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
        private static Mode _mode;

        private static float Radius() { return _mode == Mode.Full ? BodyRadius() * 0.85f : 0.22f; }

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
                var q = a + dir * t;
                int x = Mathf.FloorToInt(q.x / CellSize), z = Mathf.FloorToInt(q.z / CellSize);
                var gy = Ground(x, z, y);
                if (float.IsNaN(gy) || gy > y + MaxRise || gy < y - MaxDrop) return false;
                if (_blocked.Contains(CellAt(x, z, gy))) return false;
                y = gy;
            }
            return true;
        }

        private static RaycastHit _hit;
    }
}
