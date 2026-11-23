namespace TacticSim.Sim;

/// <summary>A hex cell in odd-r offset storage: pointy-top hexes, odd rows shifted right.</summary>
public readonly record struct Hex(int Col, int Row)
{
    /// <summary>Board label such as "H5": column letter, then 1-based row.</summary>
    public string Label => $"{(char)('A' + Col)}{Row + 1}";
    public override string ToString() => Label;
}

/// <summary>Hex math. Storage is odd-r offset; distance, neighbors and lines use cube coordinates.</summary>
public static class HexMath
{
    // Fixed direction order (E, NE, NW, W, SW, SE) so every search visits neighbors the same way.
    private static readonly (int X, int Z)[] Directions = [(1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1)];

    public static (int X, int Y, int Z) ToCube(Hex h)
    {
        var x = h.Col - (h.Row - (h.Row & 1)) / 2;
        return (x, -x - h.Row, h.Row);
    }

    public static Hex FromCube(int x, int z) => new(x + (z - (z & 1)) / 2, z);

    public static int Distance(Hex a, Hex b)
    {
        var (ax, ay, az) = ToCube(a);
        var (bx, by, bz) = ToCube(b);
        return Math.Max(Math.Abs(ax - bx), Math.Max(Math.Abs(ay - by), Math.Abs(az - bz)));
    }

    public static bool Adjacent(Hex a, Hex b) => Distance(a, b) == 1;

    public static Hex[] Neighbors(Hex h)
    {
        var (x, _, z) = ToCube(h);
        var result = new Hex[6];
        for (var i = 0; i < 6; i++) result[i] = FromCube(x + Directions[i].X, z + Directions[i].Z);
        return result;
    }

    /// <summary>
    /// Hexes on the straight line between two hex centers, both ends included. Both ends get
    /// the same tiny nudge so a line running exactly along a hex edge always rounds the same way.
    /// The TypeScript preview copies this arithmetic step for step.
    /// </summary>
    public static List<Hex> Line(Hex a, Hex b)
    {
        var n = Distance(a, b);
        var (ax, ay, az) = ToCube(a);
        var (bx, by, bz) = ToCube(b);
        double x1 = ax + 1e-6, y1 = ay + 2e-6, z1 = az - 3e-6;
        double x2 = bx + 1e-6, y2 = by + 2e-6, z2 = bz - 3e-6;
        var line = new List<Hex>(n + 1);
        for (var i = 0; i <= n; i++)
        {
            var t = n == 0 ? 0.0 : (double)i / n;
            line.Add(CubeRound(x1 + (x2 - x1) * t, y1 + (y2 - y1) * t, z1 + (z2 - z1) * t));
        }
        return line;
    }

    private static Hex CubeRound(double x, double y, double z)
    {
        double rx = Math.Floor(x + 0.5), ry = Math.Floor(y + 0.5), rz = Math.Floor(z + 0.5);
        double dx = Math.Abs(rx - x), dy = Math.Abs(ry - y), dz = Math.Abs(rz - z);
        if (dx > dy && dx > dz) rx = -ry - rz;
        else if (dy > dz) ry = -rx - rz;
        else rz = -rx - ry;
        return FromCube((int)rx, (int)rz);
    }
}
