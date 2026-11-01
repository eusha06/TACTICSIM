namespace TacticSim.Core;

public enum Side { Human, Computer }

public abstract class Unit
{
    // Only this assembly can define unit types or change their state.
    private protected Unit(int id, Side side, Position position, int health)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (!Enum.IsDefined(side)) throw new ArgumentOutOfRangeException(nameof(side));
        Id = id;
        Side = side;
        Position = position;
        Health = health;
    }

    public int Id { get; }
    public Side Side { get; }
    public Position Position { get; internal set; }
    public int Health { get; internal set; }
    public bool HasMoved { get; internal set; }
    public bool HasAttacked { get; internal set; }
    public bool IsAlive => Health > 0;
    public abstract int MaxHealth { get; }
    public abstract int MovementPoints { get; }
    public abstract int MinimumRange { get; }
    public abstract int MaximumRange { get; }
    public abstract int BaseDamage { get; }
    public abstract int? GetMovementCost(Terrain terrain);

    // All fields are values; copying prevents two games sharing mutable units.
    internal Unit Copy() => (Unit)MemberwiseClone();
}

public sealed class Infantry(int id, Side side, Position position)
    : Unit(id, side, position, 10)
{
    public override int MaxHealth => 10;
    public override int MovementPoints => 3;
    public override int MinimumRange => 1;
    public override int MaximumRange => 1;
    public override int BaseDamage => 4;

    public override int? GetMovementCost(Terrain terrain) => terrain switch
    {
        Terrain.Plain => 1,
        Terrain.Forest or Terrain.Hill => 2,
        Terrain.Water => null,
        _ => throw new ArgumentOutOfRangeException(nameof(terrain))
    };
}

public sealed class Armor(int id, Side side, Position position)
    : Unit(id, side, position, 16)
{
    public override int MaxHealth => 16;
    public override int MovementPoints => 4;
    public override int MinimumRange => 1;
    public override int MaximumRange => 1;
    public override int BaseDamage => 6;

    public override int? GetMovementCost(Terrain terrain) => terrain switch
    {
        Terrain.Plain => 1,
        Terrain.Forest => 3,
        Terrain.Hill => 2,
        Terrain.Water => null,
        _ => throw new ArgumentOutOfRangeException(nameof(terrain))
    };
}

public sealed class Artillery(int id, Side side, Position position)
    : Unit(id, side, position, 8)
{
    public override int MaxHealth => 8;
    public override int MovementPoints => 2;
    public override int MinimumRange => 2;
    public override int MaximumRange => 4;
    public override int BaseDamage => 5;

    public override int? GetMovementCost(Terrain terrain) => terrain switch
    {
        Terrain.Plain => 1,
        Terrain.Forest => 2,
        Terrain.Hill => 3,
        Terrain.Water => null,
        _ => throw new ArgumentOutOfRangeException(nameof(terrain))
    };
}
