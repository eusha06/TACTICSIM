using System.Collections.ObjectModel;

namespace TacticSim.Core;

/// <summary>Owns match state. All move, attack, and turn changes go through here.</summary>
public sealed class Game
{
    private readonly Dictionary<int, Unit> _units;
    private readonly List<GameAction> _actions = [];

    // Custom scenarios use the same 8x8 rules as the default match.
    public Game(BattleMap map, IEnumerable<Unit> units, Side firstSide = Side.Human)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(units);
        if (!Enum.IsDefined(firstSide)) throw new ArgumentOutOfRangeException(nameof(firstSide));

        var copies = new List<Unit>();
        foreach (var unit in units)
        {
            if (unit is null) throw new ArgumentException("Units cannot contain null.", nameof(units));
            copies.Add(unit.Copy());
        }

        if (copies.Select(u => u.Id).Distinct().Count() != copies.Count)
            throw new ArgumentException("Unit IDs must be unique.", nameof(units));
        if (!copies.Any(u => u.Side == Side.Human) || !copies.Any(u => u.Side == Side.Computer))
            throw new ArgumentException("Both sides must have units.", nameof(units));
        if (copies.Any(u => !map.Contains(u.Position) || map.GetTerrain(u.Position) == Terrain.Water))
            throw new ArgumentException("Units must be on passable map cells.", nameof(units));
        var living = copies.Where(u => u.IsAlive).ToList();
        if (living.Select(u => u.Position).Distinct().Count() != living.Count)
            throw new ArgumentException("Living units cannot share a cell.", nameof(units));

        Map = map;
        Units = copies.AsReadOnly();
        _units = copies.ToDictionary(u => u.Id);
        CurrentSide = firstSide;
        UpdateWinner();
    }

    public BattleMap Map { get; }
    public IReadOnlyList<Unit> Units { get; }
    public Side CurrentSide { get; private set; }
    public int TurnNumber { get; private set; } = 1;
    public Side? Winner { get; private set; }
    // Actions accepted since creation/restoration, for the server's action history.
    public IReadOnlyList<GameAction> Actions => _actions.AsReadOnly();

    public GameSnapshot ExportSnapshot() => new(CurrentSide, TurnNumber,
        Enumerable.Range(0, BattleMap.Size * BattleMap.Size)
            .Select(i => Map.GetTerrain(new Position(i % BattleMap.Size, i / BattleMap.Size))).ToArray(),
        Units.Select(u => new UnitSnapshot(u.GetType().Name, u.Id, u.Side, u.Position,
            u.Health, u.HasMoved, u.HasAttacked)).ToArray());

    public static Game Restore(GameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.TurnNumber < 1 || snapshot.Tiles.Length != BattleMap.Size * BattleMap.Size)
            throw new ArgumentException("Invalid saved game.", nameof(snapshot));
        var tiles = new Terrain[BattleMap.Size, BattleMap.Size];
        for (var i = 0; i < snapshot.Tiles.Length; i++)
            tiles[i / BattleMap.Size, i % BattleMap.Size] = snapshot.Tiles[i];
        var units = snapshot.Units.Select(saved =>
        {
            Unit unit = saved.Type switch
            {
                nameof(Infantry) => new Infantry(saved.Id, saved.Side, saved.Position),
                nameof(Armor) => new Armor(saved.Id, saved.Side, saved.Position),
                nameof(Artillery) => new Artillery(saved.Id, saved.Side, saved.Position),
                _ => throw new ArgumentException("Unknown saved unit type.", nameof(snapshot))
            };
            if (saved.Health < 0 || saved.Health > unit.MaxHealth)
                throw new ArgumentException("Invalid saved health.", nameof(snapshot));
            unit.Health = saved.Health;
            unit.HasMoved = saved.HasMoved;
            unit.HasAttacked = saved.HasAttacked;
            return unit;
        });
        return new Game(new BattleMap(tiles), units, snapshot.CurrentSide) { TurnNumber = snapshot.TurnNumber };
    }

    public static Game CreateDefault() => new(BattleMap.Default,
    [
        new Infantry(1, Side.Human, new Position(0, 0)),
        new Armor(2, Side.Human, new Position(1, 0)),
        new Artillery(3, Side.Human, new Position(2, 0)),
        new Infantry(4, Side.Computer, new Position(7, 7)),
        new Armor(5, Side.Computer, new Position(6, 7)),
        new Artillery(6, Side.Computer, new Position(5, 7))
    ]);

    public ActionResult Move(Side actor, int unitId, Position destination)
    {
        var error = ValidateUnit(actor, unitId);
        if (error != GameError.None) return new ActionResult(error);
        var unit = _units[unitId];
        error = ValidateMovementAllowance(unit);
        if (error != GameError.None) return new ActionResult(error);
        if (!Map.Contains(destination)) return new ActionResult(GameError.OutOfBounds);
        if (Map.GetTerrain(destination) == Terrain.Water)
            return new ActionResult(GameError.ImpassableTerrain);
        if (IsOccupied(destination)) return new ActionResult(GameError.DestinationOccupied);
        if (!FindReachableCells(unit).ContainsKey(destination))
            return new ActionResult(GameError.Unreachable);

        unit.Position = destination;
        unit.HasMoved = true;
        _actions.Add(new GameAction(TurnNumber, actor, ActionKind.Move, unitId, Destination: destination));
        return new ActionResult(GameError.None);
    }

    public ActionResult Attack(Side actor, int unitId, int targetId)
    {
        var error = ValidateAttack(actor, unitId, targetId);
        if (error != GameError.None) return new ActionResult(error);

        var attacker = _units[unitId];
        var target = _units[targetId];
        var damage = Math.Max(1, attacker.BaseDamage - Map.GetDefense(target.Position));
        var actualDamage = Math.Min(target.Health, damage);
        target.Health -= actualDamage;
        attacker.HasAttacked = true;
        UpdateWinner();
        _actions.Add(new GameAction(TurnNumber, actor, ActionKind.Attack, unitId, targetId,
            Damage: actualDamage, Destroyed: !target.IsAlive));
        return new ActionResult(GameError.None, actualDamage);
    }

    public ActionResult EndTurn(Side actor)
    {
        var error = ValidateTurn(actor);
        if (error != GameError.None) return new ActionResult(error);

        _actions.Add(new GameAction(TurnNumber, actor, ActionKind.EndTurn));
        CurrentSide = CurrentSide == Side.Human ? Side.Computer : Side.Human;
        TurnNumber++;
        foreach (var unit in Units.Where(u => u.Side == CurrentSide && u.IsAlive))
        {
            unit.HasMoved = false;
            unit.HasAttacked = false;
        }
        return new ActionResult(GameError.None);
    }

    // Returns shortest movement costs, excluding the unit's current cell.
    public IReadOnlyDictionary<Position, int> GetLegalMoves(Side actor, int unitId)
    {
        if (ValidateUnit(actor, unitId) != GameError.None ||
            ValidateMovementAllowance(_units[unitId]) != GameError.None)
            return new ReadOnlyDictionary<Position, int>(new Dictionary<Position, int>());

        return new ReadOnlyDictionary<Position, int>(FindReachableCells(_units[unitId]));
    }

    public IReadOnlyList<Unit> GetLegalTargets(Side actor, int unitId) =>
        Units.Where(target => ValidateAttack(actor, unitId, target.Id) == GameError.None)
            .OrderBy(target => target.Id).ToList().AsReadOnly();

    internal GameError ValidateTurn(Side actor)
    {
        if (Winner is not null) return GameError.GameOver;
        return actor == CurrentSide ? GameError.None : GameError.NotYourTurn;
    }

    private GameError ValidateUnit(Side actor, int unitId)
    {
        var error = ValidateTurn(actor);
        if (error != GameError.None) return error;
        if (!_units.TryGetValue(unitId, out var unit)) return GameError.UnitNotFound;
        if (unit.Side != actor) return GameError.NotYourUnit;
        return unit.IsAlive ? GameError.None : GameError.UnitDestroyed;
    }

    private static GameError ValidateMovementAllowance(Unit unit)
    {
        if (unit.HasAttacked) return GameError.MoveAfterAttack;
        return unit.HasMoved ? GameError.AlreadyMoved : GameError.None;
    }

    private GameError ValidateAttack(Side actor, int unitId, int targetId)
    {
        var error = ValidateUnit(actor, unitId);
        if (error != GameError.None) return error;
        var attacker = _units[unitId];
        if (attacker.HasAttacked) return GameError.AlreadyAttacked;
        if (!_units.TryGetValue(targetId, out var target)) return GameError.TargetNotFound;
        if (!target.IsAlive) return GameError.TargetDestroyed;
        if (target.Side == actor) return GameError.FriendlyFire;
        var distance = attacker.Position.ManhattanDistanceTo(target.Position);
        return distance >= attacker.MinimumRange && distance <= attacker.MaximumRange
            ? GameError.None : GameError.OutOfRange;
    }

    private bool IsOccupied(Position position) =>
        Units.Any(u => u.IsAlive && u.Position == position);

    private Dictionary<Position, int> FindReachableCells(Unit unit)
    {
        // Dijkstra: expand the cheapest known cell first. A later, cheaper
        // route replaces an earlier cost; stale queue entries are skipped.
        var costs = new Dictionary<Position, int> { [unit.Position] = 0 };
        var frontier = new PriorityQueue<Position, int>();
        frontier.Enqueue(unit.Position, 0);

        while (frontier.TryDequeue(out var current, out var currentCost))
        {
            if (costs[current] != currentCost) continue;
            foreach (var next in current.Neighbors())
            {
                if (!Map.Contains(next) || IsOccupied(next)) continue;
                var stepCost = unit.GetMovementCost(Map.GetTerrain(next));
                if (stepCost is null) continue;
                var totalCost = currentCost + stepCost.Value;
                if (totalCost > unit.MovementPoints) continue;
                if (costs.TryGetValue(next, out var knownCost) && knownCost <= totalCost) continue;

                costs[next] = totalCost;
                frontier.Enqueue(next, totalCost);
            }
        }

        costs.Remove(unit.Position);
        return costs;
    }

    private void UpdateWinner()
    {
        if (!Units.Any(u => u.IsAlive && u.Side == Side.Human)) Winner = Side.Computer;
        else if (!Units.Any(u => u.IsAlive && u.Side == Side.Computer)) Winner = Side.Human;
    }
}
