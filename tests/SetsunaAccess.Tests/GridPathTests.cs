using System.Collections.Generic;
using SetsunaAccess;
using Xunit;
using Cell = SetsunaAccess.GridPath.Cell;

public class GridPathTests
{
    /// <summary>Map from ASCII rows: '#' wall, 'S' start, 'G' goal, '.' open. Row 0 is z = 0.</summary>
    private static (Cell start, Cell goal, System.Func<Cell, Cell, bool> canStep) Map(params string[] rows)
    {
        Cell s = default, g = default;
        var walls = new HashSet<Cell>();
        for (var z = 0; z < rows.Length; z++)
            for (var x = 0; x < rows[z].Length; x++)
            {
                var c = new Cell(x, z);
                switch (rows[z][x])
                {
                    case '#': walls.Add(c); break;
                    case 'S': s = c; break;
                    case 'G': g = c; break;
                }
            }
        int w = rows[0].Length, h = rows.Length;
        return (s, g, (a, b) => b.X >= 0 && b.Z >= 0 && b.X < w && b.Z < h && !walls.Contains(b));
    }

    [Fact]
    public void StraightLineInTheOpen()
    {
        var (s, g, step) = Map("S...G");
        var path = GridPath.Find(s, g, c => c.Equals(g), step);
        Assert.Equal(5, path.Count);
        Assert.Equal(s, path[0]);
        Assert.Equal(g, path[^1]);
    }

    [Fact]
    public void GoesAroundAWallThroughTheGap()
    {
        var (s, g, step) = Map(
            "S.#..",
            "..#..",
            "..#.G",
            ".....");
        var path = GridPath.Find(s, g, c => c.Equals(g), step);
        Assert.NotNull(path);
        Assert.Contains(path, c => c.Z == 3); // only way past the wall is the bottom row
        Assert.DoesNotContain(path, c => c.X == 2 && c.Z < 3);
    }

    [Fact]
    public void NoCornerCutting()
    {
        // The diagonal step from (0,0) to (1,1) would squeeze between two walls.
        var (s, g, step) = Map(
            "S#",
            "#G");
        Assert.Null(GridPath.Find(s, g, c => c.Equals(g), step));
    }

    [Fact]
    public void UnreachableReturnsNull()
    {
        var (s, g, step) = Map(
            "S.#G",
            "..#.");
        Assert.Null(GridPath.Find(s, g, c => c.Equals(g), step));
    }

    [Fact]
    public void GoalRegionStopsEarly()
    {
        var (s, g, step) = Map("S......G");
        // Anything within 2 cells of G counts as arrived.
        var path = GridPath.Find(s, g, c => System.Math.Abs(c.X - g.X) <= 2, step);
        Assert.Equal(new Cell(5, 0), path[^1]);
    }

    [Fact]
    public void LengthCountsDiagonals()
    {
        var path = new List<Cell> { new Cell(0, 0), new Cell(1, 1), new Cell(2, 1) };
        Assert.Equal(1f + 1.41421356f, GridPath.Length(path), 3);
    }
}
