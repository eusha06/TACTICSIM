namespace TacticSim.Core;

public enum Terrain { Plain, Forest, Hill, Water }

public sealed class BattleMap
{
    public const int Size = 8;
    private readonly Terrain[,] _tiles;

    // Arrays use [y, x], while positions are written (x, y).
    public BattleMap(Terrain[,] tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (tiles.GetLength(0) != Size || tiles.GetLength(1) != Size)
            throw new ArgumentException("The map must be 8x8.", nameof(tiles));

        foreach (var terrain in tiles)
            if (!Enum.IsDefined(terrain))
                throw new ArgumentException("Unknown terrain.", nameof(tiles));

        _tiles = (Terrain[,])tiles.Clone();
    }

    public bool Contains(Position position) =>
        position.X >= 0 && position.X < Size && position.Y >= 0 && position.Y < Size;

    public Terrain GetTerrain(Position position)
    {
        if (!Contains(position))
            throw new ArgumentOutOfRangeException(nameof(position));
        return _tiles[position.Y, position.X];
    }

    public int GetDefense(Position position) => GetTerrain(position) switch
    {
        Terrain.Forest => 1,
        Terrain.Hill => 2,
        _ => 0
    };

    public static BattleMap Default { get; } = CreateDefault();

    private static BattleMap CreateDefault()
    {
        string[] rows =
        [
            "........",
            "..FF.H..",
            ".H..W...",
            "...WW.F.",
            ".F.WW...",
            "...W..H.",
            "..H.FF..",
            "........"
        ];
        var tiles = new Terrain[Size, Size];
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                tiles[y, x] = rows[y][x] switch
                {
                    'F' => Terrain.Forest,
                    'H' => Terrain.Hill,
                    'W' => Terrain.Water,
                    _ => Terrain.Plain
                };
        return new BattleMap(tiles);
    }
}
