namespace TacticSim.Sim;

public enum OrderType { Hold, Move, Attack, Overwatch, Barrage }

/// <summary>One unit's order for the turn. Path excludes the unit's own hex.</summary>
public sealed record Order(int UnitId, OrderType Type, Hex[]? Path = null, int? TargetId = null, Hex? TargetHex = null)
{
    public static Order Hold(int unitId) => new(unitId, OrderType.Hold);
    public bool Moves => Type is OrderType.Move or OrderType.Attack && Path is { Length: > 0 };
}

public sealed record OrderCheck(Order[] Orders, string? Error, int? UnitId)
{
    public bool Ok => Error is null;
}

/// <summary>Server-side order validation. Every rejection is a sentence the UI shows verbatim.</summary>
public static class OrderRules
{
    /// <summary>Validates one side's orders and fills in Hold for every unit without one.</summary>
    public static OrderCheck Validate(GameContent content, MatchState state, Side side, IReadOnlyList<Order>? submitted)
    {
        if (state.IsOver) return Fail("This match is over.");
        submitted ??= [];
        var board = content.Board(state.Scenario);
        foreach (var order in submitted)
        {
            var unit = state.Unit(order.UnitId);
            if (unit is null || !unit.Alive) return Fail($"Unit #{order.UnitId} is not on the battlefield.", order.UnitId);
            if (unit.Side != side) return Fail("You can only give orders to your own units.", order.UnitId);
        }
        var duplicate = submitted.GroupBy(o => o.UnitId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            return Fail($"{Name(content, state.Unit(duplicate.Key)!)} has more than one order. Each unit gets exactly one.", duplicate.Key);

        var view = Vision.ViewOf(content, board, state.Units, side);
        var blocked = view.VisibleEnemies.Select(e => e.Pos).ToHashSet();
        var given = submitted.ToDictionary(o => o.UnitId);
        var orders = state.Units.Where(u => u.Alive && u.Side == side).OrderBy(u => u.Id)
            .Select(u => Normalize(given.GetValueOrDefault(u.Id) ?? Order.Hold(u.Id))).ToArray();
        foreach (var order in orders)
        {
            var problem = Check(content, board, state, view, blocked, order);
            if (problem is not null) return Fail(problem, order.UnitId);
        }
        // Two of your units can't plan to finish on the same hex.
        var ends = orders.Select(o => (Order: o, End: o.Moves ? o.Path![^1] : state.Unit(o.UnitId)!.Pos)).ToList();
        foreach (var a in ends)
        {
            var clash = ends.FirstOrDefault(b => b.Order.UnitId != a.Order.UnitId && b.End == a.End);
            if (clash.Order is not null && a.Order.Moves)
                return Fail($"{Name(content, state.Unit(a.Order.UnitId)!)} and {Name(content, state.Unit(clash.Order.UnitId)!)} " +
                    $"can't both end on {a.End}.", a.Order.UnitId);
        }
        return new OrderCheck(orders, null, null);
    }

    public static string Name(GameContent content, UnitState unit) => $"{content.Units[unit.Type].Name} ({unit.Pos})";

    /// <summary>The reason an attack or barrage can't reach, e.g. "Out of range: needs 3–6, target is 2."</summary>
    public static string RangeProblem(UnitDef unit, Hex from, Hex to) =>
        $"Out of range: needs {unit.MinRange}–{unit.MaxRange}, target is {HexMath.Distance(from, to)}.";

    private static Order Normalize(Order o) => o.Type switch
    {
        OrderType.Hold or OrderType.Overwatch => new Order(o.UnitId, o.Type),
        OrderType.Barrage => new Order(o.UnitId, o.Type, TargetHex: o.TargetHex),
        _ => o with { Path = o.Path ?? [], TargetHex = null }
    };

    private static string? Check(GameContent content, Board board, MatchState state, SideView view, ISet<Hex> blocked, Order order)
    {
        var unit = state.Unit(order.UnitId)!;
        var def = content.Units[unit.Type];
        UnitState? target = null;
        if (order.TargetId is int targetId)
        {
            target = state.Unit(targetId);
            if (target is null || !target.Alive || target.Side == unit.Side || !view.Sees(target))
                return "That enemy isn't in sight right now. Pick a visible enemy.";
        }
        switch (order.Type)
        {
            case OrderType.Hold:
            case OrderType.Overwatch:
                return null;
            case OrderType.Move:
                if (order.Path!.Length == 0) return "Move needs a destination hex.";
                if (target is not null && def.CannotMoveAndFire) return $"{def.Name} can't fire after moving.";
                return Pathfinder.Check(content, board, def, unit.Pos, order.Path, blocked, out _, out _);
            case OrderType.Attack:
                if (target is null) return "Attack needs a target: pick a visible enemy.";
                if (order.Path!.Length > 0)
                    return def.CannotMoveAndFire ? $"{def.Name} can't fire after moving."
                        : Pathfinder.Check(content, board, def, unit.Pos, order.Path, blocked, out _, out _);
                if (!Combat.InRange(def, unit.Pos, target.Pos)) return RangeProblem(def, unit.Pos, target.Pos);
                if (!def.Indirect && Vision.Blocker(board, unit.Pos, target.Pos) is Hex wall)
                    return $"No line of sight: {board.TerrainAt(wall).Name} at {wall} blocks it.";
                return null;
            case OrderType.Barrage:
                if (!def.Indirect) return "Only Artillery can Barrage.";
                if (order.TargetHex is not Hex hex || !board.InBounds(hex)) return "Barrage needs a target hex on the map.";
                if (!Combat.InRange(def, unit.Pos, hex)) return RangeProblem(def, unit.Pos, hex);
                if (!view.VisibleHexes.Contains(hex)) return $"Barrage needs a hex your units can see. {hex} is in the fog.";
                return null;
            default:
                return "Unknown order type.";
        }
    }

    private static OrderCheck Fail(string error, int? unitId = null) => new([], error, unitId);
}
