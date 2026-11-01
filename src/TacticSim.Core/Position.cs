namespace TacticSim.Core;

public readonly record struct Position(int X, int Y)
{
    public int ManhattanDistanceTo(Position other) =>
        Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    // Fixed order also makes path searches easy to inspect while debugging.
    internal IEnumerable<Position> Neighbors()
    {
        yield return new Position(X, Y - 1);
        yield return new Position(X - 1, Y);
        yield return new Position(X + 1, Y);
        yield return new Position(X, Y + 1);
    }
}
