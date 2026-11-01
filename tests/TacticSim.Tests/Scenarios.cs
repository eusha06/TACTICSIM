using TacticSim.Core;

namespace TacticSim.Tests;

internal static class Scenarios
{
    internal static Terrain[,] PlainTiles() => new Terrain[BattleMap.Size, BattleMap.Size];

    internal static Unit Unit(string type, int id, Side side, int x, int y) => type switch
    {
        "Infantry" => new Infantry(id, side, new Position(x, y)),
        "Armor" => new Armor(id, side, new Position(x, y)),
        "Artillery" => new Artillery(id, side, new Position(x, y)),
        _ => throw new ArgumentException("Unknown test unit type.", nameof(type))
    };

    internal static Game Duel(Unit human, Unit computer, Terrain[,]? tiles = null,
        Side firstSide = Side.Human) => new(new BattleMap(tiles ?? PlainTiles()), [human, computer], firstSide);

    internal static Unit Find(Game game, int id) => game.Units.Single(u => u.Id == id);

    internal static void NextHumanTurn(Game game)
    {
        Assert.True(game.EndTurn(Side.Human).IsSuccess);
        Assert.True(game.EndTurn(Side.Computer).IsSuccess);
    }

    // Captures observable state, including flags, so rejection checks catch
    // accidental partial changes as well as position/health changes.
    internal static string Snapshot(Game game) =>
        $"{game.CurrentSide}|{game.TurnNumber}|{game.Winner}|" +
        string.Join(";", game.Units.OrderBy(u => u.Id).Select(u =>
            $"{u.Id}:{u.Side}:{u.Position}:{u.Health}:{u.HasMoved}:{u.HasAttacked}"));
}
