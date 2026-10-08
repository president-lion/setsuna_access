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
        public struct Cell : IEquatable<Cell>
        {
            public readonly int X, Z;
            public Cell(int x, int z) { X = x; Z = z; }
            public bool Equals(Cell o) { return X == o.X && Z == o.Z; }
            public override bool Equals(object o) { return o is Cell && Equals((Cell)o); }
            public override int GetHashCode() { return X * 73856093 ^ Z * 19349663; }
            public override string ToString() { return "(" + X + "," + Z + ")"; }
        }

        private const float Diagonal = 1.41421356f;

        /// <summary>
        /// Path from start to the first cell for which isGoal is true (start included), or null.
        /// canStep(from, to) is asked only for neighbouring cells. heuristicTarget guides the search.
        /// </summary>
        public static List<Cell> Find(Cell start, Cell heuristicTarget, Func<Cell, bool> isGoal,
                                      Func<Cell, Cell, bool> canStep, int maxExpanded = 6000)
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
                        var next = new Cell(cur.X + dx, cur.Z + dz);
                        if (closed.Contains(next)) continue;
                        var diag = dx != 0 && dz != 0;
                        if (diag && (!canStep(cur, new Cell(cur.X + dx, cur.Z)) || !canStep(cur, new Cell(cur.X, cur.Z + dz)))) continue;
                        if (!canStep(cur, next)) continue;
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
