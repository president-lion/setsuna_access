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

    [Fact]
    public void FloodStopsAtWalls()
    {
        var (s, g, step) = Map(
            "S.#G",
            "..#.");
        var reached = GridPath.Flood(s, step, 1000, out var complete);
        Assert.True(complete);
        Assert.Equal(4, reached.Count);          // the two left columns only
        Assert.DoesNotContain(g, reached);
    }

    [Fact]
    public void FloodReportsBudgetExhausted()
    {
        var (s, _, step) = Map("S.........");
        var reached = GridPath.Flood(s, step, 4, out var complete);
        Assert.False(complete);
        Assert.Equal(4, reached.Count);
    }

    [Fact]
    public void FloodJobInSlicesMatchesFlood()
    {
        var (s, _, step) = Map(
            "S...#....",
            "....#....",
            ".........");
        var whole = GridPath.Flood(s, step, 1000, out var complete);
        var job = new GridPath.FloodJob(s, step, 1000);
        var slices = 0;
        while (!job.Step(2)) slices++;
        Assert.True(slices > 1);
        Assert.True(job.Complete);
        Assert.Equal(complete, job.Complete);
        Assert.True(whole.SetEquals(job.Seen));
    }

    [Fact]
    public void SearchJobInSlicesMatchesFind()
    {
        var (s, g, step) = Map(
            "S.#......",
            "..#..##..",
            "..#..#...",
            ".....#..G");
        var whole = GridPath.Find(s, g, c => c.Equals(g), step);
        var job = new GridPath.SearchJob(s, g, c => c.Equals(g), step, 6000);
        var slices = 0;
        while (!job.Step(1)) slices++;
        Assert.True(slices > 1);
        Assert.NotNull(job.Path);
        Assert.Equal(GridPath.Length(whole), GridPath.Length(job.Path), 3);
        Assert.Equal(g, job.Path[^1]);
    }

    [Fact]
    public void SearchJobReportsNoPath()
    {
        var (s, g, step) = Map("S#G");
        var job = new GridPath.SearchJob(s, g, c => c.Equals(g), step, 6000);
        while (!job.Step(5)) { }
        Assert.Null(job.Path);
    }

    [Fact]
    public void ExtraCostSteersAwayFromCostlyCells()
    {
        // Two equal-length lanes (rows 0 and 2); row 0 is "next to a wall" and costs extra.
        var (s, g, step) = Map(
            "S....G",
            "......",
            "......");
        var job = new GridPath.SearchJob(new Cell(0, 1), new Cell(5, 1), c => c.Equals(new Cell(5, 1)), step, 6000,
                                         c => c.Z == 1 ? 5f : 0f);
        while (!job.Step(50)) { }
        Assert.NotNull(job.Path);
        Assert.Contains(job.Path, c => c.Z != 1); // leaves the costly middle row
    }
}
