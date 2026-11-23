namespace TacticSim.Sim;

/// <summary>What one side can see right now.</summary>
public sealed record SideView(Side Side, HashSet<Hex> VisibleHexes, List<UnitState> VisibleEnemies)
{
    public bool Sees(UnitState enemy) => VisibleHexes.Contains(enemy.Pos);
}

/// <summary>Line of sight and fog of war.</summary>
public static class Vision
{
    /// <summary>
    /// The first hex strictly between two hexes that blocks line of sight, or null when clear.
    /// Forest and town block. A hill blocks only between two lower hexes. A unit on a hill sees
    /// over everything, and (so sight stays two-way) a hill-top is seen over everything too.
    /// </summary>
    public static Hex? Blocker(Board board, Hex from, Hex to)
    {
        if (board.TerrainAt(from).Hill || board.TerrainAt(to).Hill) return null;
        var line = HexMath.Line(from, to);
        for (var i = 1; i < line.Count - 1; i++)
        {
            // A line hugging the jagged map edge can round to a hex just outside: treat it as open.
            if (board.InBounds(line[i]) && board.TerrainAt(line[i]).BlocksLos) return line[i];
        }
        return null;
    }

    public static bool HasLos(Board board, Hex from, Hex to) => Blocker(board, from, to) is null;

    public static int VisionRadius(GameContent content, Board board, UnitState unit) =>
        content.Units[unit.Type].Vision + (board.TerrainAt(unit.Pos).Hill ? content.Rules.HillVisionBonus : 0);

    public static bool CanSee(GameContent content, Board board, UnitState observer, Hex target) =>
        HexMath.Distance(observer.Pos, target) <= VisionRadius(content, board, observer) && HasLos(board, observer.Pos, target);

    public static HashSet<Hex> VisibleHexes(GameContent content, Board board, IEnumerable<UnitState> units)
    {
        var visible = new HashSet<Hex>();
        foreach (var unit in units.Where(u => u.Alive))
            foreach (var hex in board.AllHexes())
                if (!visible.Contains(hex) && CanSee(content, board, unit, hex)) visible.Add(hex);
        return visible;
    }

    public static SideView ViewOf(GameContent content, Board board, IEnumerable<UnitState> units, Side side)
    {
        var all = units.Where(u => u.Alive).ToList();
        var hexes = VisibleHexes(content, board, all.Where(u => u.Side == side));
        return new SideView(side, hexes, all.Where(u => u.Side != side && hexes.Contains(u.Pos)).OrderBy(u => u.Id).ToList());
    }

    public static SideView ViewOf(GameContent content, MatchState state, Side side) =>
        ViewOf(content, content.Board(state.Scenario), state.Units, side);
}
