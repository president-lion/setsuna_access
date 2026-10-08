using System;
using System.Collections.Generic;

namespace SetsunaAccess
{
    /// <summary>
    /// A* over an implicit 8-connected grid. The caller supplies whether one cell can be
    /// stepped to from its neighbour, so the grid is never built up front: only cells the search
    /// actually reaches are tested. Diagonal steps need both side cells open (no corner cutting).
    /// Pure logic, unit tested.
    /// </summary>
    internal static class GridPath
    {
        /// <summary>
        /// A grid square on one walking level. Level tells apart floors stacked over the same square (a ledge
        /// above a cave floor): with one cell per square, a search that reached the floor first never explored
        /// the ledge path above it (Frost Caves room 2).
        /// </summary>
        public struct Cell : IEquatable<Cell>
        {
            public readonly int X, Z, Level;
            public Cell(int x, int z) { X = x; Z = z; Level = 0; }
            public Cell(int x, int z, int level) { X = x; Z = z; Level = level; }
            public bool Equals(Cell o) { return X == o.X && Z == o.Z && Level == o.Level; }
            public override bool Equals(object o) { return o is Cell && Equals((Cell)o); }
            public override int GetHashCode() { return X * 73856093 ^ Z * 19349663 ^ Level * 83492791; }
            public override string ToString() { return "(" + X + "," + Z + (Level != 0 ? "," + Level : "") + ")"; }
        }

        /// <summary>
        /// Where a step from cur by (dx, dz) lands, or null if it can't be taken. The caller decides the level
        /// (the ground found next door). Diagonal steps also need both side steps open (no corner cutting).
        /// </summary>
        public delegate Cell? Neighbour(Cell cur, int dx, int dz);

        /// <summary>A yes/no step rule as a Neighbour that stays on the same level.</summary>
        public static Neighbour FromStep(Func<Cell, Cell, bool> canStep)
        {
            return (cur, dx, dz) =>
            {
                var to = new Cell(cur.X + dx, cur.Z + dz, cur.Level);
                return canStep(cur, to) ? to : (Cell?)null;
            };
        }

        private static Cell? Next(Neighbour nb, Cell cur, int dx, int dz)
        {
            if (dx != 0 && dz != 0 && (nb(cur, dx, 0) == null || nb(cur, 0, dz) == null)) return null;
            return nb(cur, dx, dz);
        }

        private const float Diagonal = 1.41421356f;

        /// <summary>
        /// Path from start to the first cell for which isGoal is true (start included), or null.
        /// canStep(from, to) is asked only for neighbouring cells. heuristicTarget guides the search.
        /// </summary>
        public static List<Cell> Find(Cell start, Cell heuristicTarget, Func<Cell, bool> isGoal,
                                      Func<Cell, Cell, bool> canStep, int maxExpanded = 6000)
        {
            return Find(start, heuristicTarget, isGoal, FromStep(canStep), maxExpanded);
        }

        public static List<Cell> Find(Cell start, Cell heuristicTarget, Func<Cell, bool> isGoal, Neighbour nb, int maxExpanded = 6000)
        {
            var open = new MinHeap();
            var g = new Dictionary<Cell, float> { { start, 0f } };
            var came = new Dictionary<Cell, Cell>();
            var closed = new HashSet<Cell>();
            open.Push(start, H(start, heuristicTarget));
            var expanded = 0;

            while (open.Count > 0)
            {
                var cur = open.Pop();
                if (closed.Contains(cur)) continue;
                if (isGoal(cur)) return Rebuild(came, cur);
                closed.Add(cur);
                if (++expanded > maxExpanded) break;

                for (var dx = -1; dx <= 1; dx++)
                    for (var dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        var to = Next(nb, cur, dx, dz);
                        if (to == null) continue;
                        var next = to.Value;
                        if (closed.Contains(next)) continue;
                        var diag = dx != 0 && dz != 0;
                        var cost = g[cur] + (diag ? Diagonal : 1f);
                        float old;
                        if (g.TryGetValue(next, out old) && old <= cost) continue;
                        g[next] = cost;
                        came[next] = cur;
                        open.Push(next, cost + H(next, heuristicTarget));
                    }
            }
            return null;
        }

        /// <summary>
        /// Every cell reachable from start (same stepping rules as Find), up to maxCells.
        /// complete is false when the budget ran out before the area was exhausted.
        /// </summary>
        public static HashSet<Cell> Flood(Cell start, Func<Cell, Cell, bool> canStep, int maxCells, out bool complete)
        {
            return Flood(start, FromStep(canStep), maxCells, out complete);
        }

        public static HashSet<Cell> Flood(Cell start, Neighbour nb, int maxCells, out bool complete)
        {
            var seen = new HashSet<Cell> { start };
            var queue = new Queue<Cell>();
            queue.Enqueue(start);
            complete = true;
            while (queue.Count > 0)
            {
                if (seen.Count >= maxCells) { complete = false; break; }
                var cur = queue.Dequeue();
                for (var dx = -1; dx <= 1; dx++)
                    for (var dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        var to = Next(nb, cur, dx, dz);
                        if (to == null || seen.Contains(to.Value)) continue;
                        seen.Add(to.Value);
                        queue.Enqueue(to.Value);
                    }
            }
            return seen;
        }

        /// <summary>
        /// A flood fill that can be run a slice at a time (so it never stalls a frame).
        /// Same stepping rules as Flood.
        /// </summary>
        public sealed class FloodJob
        {
            private readonly Neighbour _nb;
            private readonly int _maxCells;
            private readonly Queue<Cell> _queue = new Queue<Cell>();
            public readonly HashSet<Cell> Seen = new HashSet<Cell>();
            public bool Done { get; private set; }
            public bool Complete { get; private set; }

            public FloodJob(Cell start, Func<Cell, Cell, bool> canStep, int maxCells) : this(start, FromStep(canStep), maxCells) { }

            public FloodJob(Cell start, Neighbour nb, int maxCells)
            {
                _nb = nb;
                _maxCells = maxCells;
                Seen.Add(start);
                _queue.Enqueue(start);
            }

            /// <summary>Expand up to maxExpand cells; returns Done.</summary>
            public bool Step(int maxExpand)
            {
                while (!Done && maxExpand-- > 0)
                {
                    if (_queue.Count == 0) { Done = true; Complete = true; break; }
                    if (Seen.Count >= _maxCells) { Done = true; Complete = false; break; }
                    var cur = _queue.Dequeue();
                    for (var dx = -1; dx <= 1; dx++)
                        for (var dz = -1; dz <= 1; dz++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            var to = Next(_nb, cur, dx, dz);
                            if (to == null || Seen.Contains(to.Value)) continue;
                            Seen.Add(to.Value);
                            _queue.Enqueue(to.Value);
                        }
                }
                return Done;
            }
        }

        /// <summary>
        /// A* that runs a slice at a time (so planning never stalls a frame). Same rules as Find.
        /// When Done, Path holds the result or null.
        /// </summary>
        public sealed class SearchJob
        {
            private readonly Cell _target;
            private readonly Func<Cell, bool> _isGoal;
            private readonly Neighbour _nb;
            private readonly int _maxExpanded;
            private readonly Func<Cell, float> _extraCost;
            private readonly MinHeap _open = new MinHeap();
            private readonly Dictionary<Cell, float> _g = new Dictionary<Cell, float>();
            private readonly Dictionary<Cell, Cell> _came = new Dictionary<Cell, Cell>();
            private readonly HashSet<Cell> _closed = new HashSet<Cell>();
            private int _expanded;

            public bool Done { get; private set; }
            public List<Cell> Path { get; private set; }
            /// <summary>Cells expanded so far (for nav.log: did it run out of budget?).</summary>
            public int Expanded { get { return _expanded; } }
            public int Budget { get { return _maxExpanded; } }

            /// <param name="extraCost">Optional added cost for entering a cell (e.g. hugging a wall), or null.</param>
            public SearchJob(Cell start, Cell target, Func<Cell, bool> isGoal, Func<Cell, Cell, bool> canStep, int maxExpanded,
                             Func<Cell, float> extraCost = null)
                : this(start, target, isGoal, FromStep(canStep), maxExpanded, extraCost) { }

            public SearchJob(Cell start, Cell target, Func<Cell, bool> isGoal, Neighbour nb, int maxExpanded,
                             Func<Cell, float> extraCost = null)
            {
                _target = target;
                _extraCost = extraCost;
                _isGoal = isGoal;
                _nb = nb;
                _maxExpanded = maxExpanded;
                _g[start] = 0f;
                _open.Push(start, H(start, target));
            }

            public bool Step(int maxExpand)
            {
                while (!Done && maxExpand-- > 0)
                {
                    if (_open.Count == 0 || _expanded > _maxExpanded) { Done = true; break; }
                    var cur = _open.Pop();
                    if (_closed.Contains(cur)) continue;
                    if (_isGoal(cur)) { Path = Rebuild(_came, cur); Done = true; break; }
                    _closed.Add(cur);
                    _expanded++;
                    for (var dx = -1; dx <= 1; dx++)
                        for (var dz = -1; dz <= 1; dz++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            var to = Next(_nb, cur, dx, dz);
                            if (to == null) continue;
                            var next = to.Value;
                            if (_closed.Contains(next)) continue;
                            var diag = dx != 0 && dz != 0;
                            var cost = _g[cur] + (diag ? Diagonal : 1f) + (_extraCost == null ? 0f : _extraCost(next));
                            float old;
                            if (_g.TryGetValue(next, out old) && old <= cost) continue;
                            _g[next] = cost;
                            _came[next] = cur;
                            _open.Push(next, cost + H(next, _target));
                        }
                }
                return Done;
            }
        }

        /// <summary>Octile distance.</summary>
        public static float H(Cell a, Cell b)
        {
            var dx = Math.Abs(a.X - b.X);
            var dz = Math.Abs(a.Z - b.Z);
            return Math.Max(dx, dz) + (Diagonal - 1f) * Math.Min(dx, dz);
        }

        /// <summary>Length of a cell path in cells (diagonals count as 1.414).</summary>
        public static float Length(List<Cell> path)
        {
            var len = 0f;
            for (var i = 1; i < path.Count; i++)
                len += path[i].X != path[i - 1].X && path[i].Z != path[i - 1].Z ? Diagonal : 1f;
            return len;
        }

        private static List<Cell> Rebuild(Dictionary<Cell, Cell> came, Cell end)
        {
            var path = new List<Cell> { end };
            Cell prev;
            while (came.TryGetValue(path[0], out prev)) path.Insert(0, prev);
            return path;
        }

        /// <summary>Binary min-heap of cells by priority (no decrease-key; stale entries are skipped).</summary>
        private sealed class MinHeap
        {
            private readonly List<KeyValuePair<float, Cell>> _items = new List<KeyValuePair<float, Cell>>();
            public int Count { get { return _items.Count; } }

            public void Push(Cell c, float p)
            {
                _items.Add(new KeyValuePair<float, Cell>(p, c));
                var i = _items.Count - 1;
                while (i > 0)
                {
                    var parent = (i - 1) / 2;
                    if (_items[parent].Key <= _items[i].Key) break;
                    Swap(i, parent);
                    i = parent;
                }
            }

            public Cell Pop()
            {
                var top = _items[0].Value;
                var last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);
                var i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < _items.Count && _items[l].Key < _items[m].Key) m = l;
                    if (r < _items.Count && _items[r].Key < _items[m].Key) m = r;
                    if (m == i) break;
                    Swap(i, m);
                    i = m;
                }
                return top;
            }

            private void Swap(int a, int b)
            {
                var t = _items[a];
                _items[a] = _items[b];
                _items[b] = t;
            }
        }
    }
}
