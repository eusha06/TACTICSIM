namespace TacticSim.Sim;

/// <summary>A route to one destination. Path excludes the start hex. Cost is in ticks.</summary>
public sealed record PathInfo(Hex Dest, int Cost, Hex[] Path, bool RoadMarch);

/// <summary>Dijkstra on the hex grid with tick costs. Ties break by hex index, so routes never vary.</summary>
public static class Pathfinder
{
    /// <summary>Ticks a unit may spend moving: its Move, +bonus on an all-road march, capped by the turn length.</summary>
    public static int Budget(GameContent content, UnitDef unit, bool roadMarch) =>
        Math.Min(content.Rules.TicksPerTurn, unit.Move + (roadMarch ? content.Rules.RoadMarchBonus : 0));

    /// <summary>Every hex the unit can reach this turn (start excluded), keyed by destination.</summary>
    public static Dictionary<Hex, PathInfo> Reachable(GameContent content, Board board, UnitDef unit, Hex start, ISet<Hex> blocked)
    {
        var result = new Dictionary<Hex, PathInfo>();
        foreach (var p in Search(board, unit, start, blocked, Budget(content, unit, false), roadOnly: false))
            result[p.Dest] = p;
        // Road march: a vehicle that starts on road and stays on road gets extra ticks.
        if (unit.IsVehicle && board.TerrainAt(start).Road)
            foreach (var p in Search(board, unit, start, blocked, Budget(content, unit, true), roadOnly: true))
                result.TryAdd(p.Dest, p with { RoadMarch = true });
        return result;
    }

    private static List<PathInfo> Search(Board board, UnitDef unit, Hex start, ISet<Hex> blocked, int budget, bool roadOnly)
    {
        var count = board.Width * board.Height;
        var dist = new int[count];
        var prev = new int[count];
        var done = new bool[count];
        Array.Fill(dist, int.MaxValue);
        Array.Fill(prev, -1);
        dist[board.Index(start)] = 0;
        while (true)
        {
            // ponytail: O(n^2) scan is fine for a 165-hex map; use a priority queue if maps grow past ~2k hexes.
            var best = -1;
            for (var i = 0; i < count; i++)
                if (!done[i] && dist[i] != int.MaxValue && (best < 0 || dist[i] < dist[best])) best = i;
            if (best < 0) break;
            done[best] = true;
            foreach (var next in HexMath.Neighbors(board.FromIndex(best)))
            {
                if (!board.InBounds(next) || blocked.Contains(next)) continue;
                if (roadOnly && !board.TerrainAt(next).Road) continue;
                if (board.MoveCost(next, unit) is not int cost) continue;
                var total = dist[best] + cost;
                var index = board.Index(next);
                if (total <= budget && total < dist[index])
                {
                    dist[index] = total;
                    prev[index] = best;
                }
            }
        }
        var paths = new List<PathInfo>();
        for (var i = 0; i < count; i++)
        {
            if (dist[i] == int.MaxValue || i == board.Index(start)) continue;
            var path = new List<Hex>();
            for (var at = i; at != board.Index(start); at = prev[at]) path.Add(board.FromIndex(at));
            path.Reverse();
            paths.Add(new PathInfo(board.FromIndex(i), dist[i], path.ToArray(), false));
        }
        return paths;
    }

    /// <summary>Multi-turn cost from every hex to a target (int.MaxValue = unreachable). Used by the AI.</summary>
    public static int[] CostTo(Board board, UnitDef unit, Hex target)
    {
        var count = board.Width * board.Height;
        var dist = new int[count];
        var done = new bool[count];
        Array.Fill(dist, int.MaxValue);
        if (board.MoveCost(target, unit) is null) return dist;
        dist[board.Index(target)] = 0;
        while (true)
        {
            var best = -1;
            for (var i = 0; i < count; i++)
                if (!done[i] && dist[i] != int.MaxValue && (best < 0 || dist[i] < dist[best])) best = i;
            if (best < 0) break;
            done[best] = true;
            var hex = board.FromIndex(best);
            // Stepping from a neighbor into `hex` costs hex's entry cost.
            var enter = board.MoveCost(hex, unit)!.Value;
            foreach (var from in HexMath.Neighbors(hex))
            {
                if (board.MoveCost(from, unit) is null) continue;
                var index = board.Index(from);
                if (dist[best] + enter < dist[index]) dist[index] = dist[best] + enter;
            }
        }
        return dist;
    }

    /// <summary>Checks a submitted path. Returns a plain-English problem, or null when legal.</summary>
    public static string? Check(GameContent content, Board board, UnitDef unit, Hex start, IReadOnlyList<Hex> path,
        ISet<Hex> blocked, out int cost, out bool roadMarch)
    {
        cost = 0;
        roadMarch = false;
        if (path.Count == 0) return "Pick a destination hex: the path is empty.";
        var at = start;
        var seen = new HashSet<Hex> { start };
        var allRoad = board.TerrainAt(start).Road;
        foreach (var hex in path)
        {
            if (!board.InBounds(hex)) return $"{hex} is off the map.";
            if (!HexMath.Adjacent(at, hex)) return $"The path jumps from {at} to {hex}. Each step must be to a neighboring hex.";
            if (!seen.Add(hex)) return $"The path visits {hex} twice.";
            var terrain = board.TerrainAt(hex);
            if (board.MoveCost(hex, unit) is not int step) return $"{terrain.Name} at {hex} is impassable.";
            if (blocked.Contains(hex)) return $"{hex} is held by an enemy unit.";
            cost += step;
            allRoad &= terrain.Road;
            at = hex;
        }
        roadMarch = unit.IsVehicle && allRoad;
        var budget = Budget(content, unit, roadMarch);
        return cost <= budget ? null : $"Not enough move: the path costs {cost} ticks, {unit.Name} has {budget}.";
    }
}
