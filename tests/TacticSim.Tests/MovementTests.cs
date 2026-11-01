using TacticSim.Core;

namespace TacticSim.Tests;

public class MovementTests
{
    [Fact]
    public void Water_wall_blocks_crossing_even_when_destination_is_nearby()
    {
        var tiles = Scenarios.PlainTiles();
        for (var y = 0; y < BattleMap.Size; y++) tiles[y, 1] = Terrain.Water;
        var game = Scenarios.Duel(new Armor(1, Side.Human, new Position(0, 3)),
            new Infantry(2, Side.Computer, new Position(7, 7)), tiles);
        var before = Scenarios.Snapshot(game);

        Assert.Equal(GameError.Unreachable, game.Move(Side.Human, 1, new Position(2, 3)).Error);
        Assert.DoesNotContain(new Position(2, 3), game.GetLegalMoves(Side.Human, 1).Keys);
        Assert.Equal(before, Scenarios.Snapshot(game));
    }

    [Fact]
    public void Water_destination_is_rejected()
    {
        var tiles = Scenarios.PlainTiles();
        tiles[0, 1] = Terrain.Water;
        var game = Scenarios.Duel(new Infantry(1, Side.Human, new Position(0, 0)),
            new Infantry(2, Side.Computer, new Position(7, 7)), tiles);

        Assert.Equal(GameError.ImpassableTerrain, game.Move(Side.Human, 1, new Position(1, 0)).Error);
        Assert.False(Scenarios.Find(game, 1).HasMoved);
    }

    [Fact]
    public void Route_around_water_costs_four_steps_instead_of_two()
    {
        var tiles = Scenarios.PlainTiles();
        tiles[0, 1] = Terrain.Water;
        var armor = Scenarios.Duel(new Armor(1, Side.Human, new Position(0, 0)),
            new Infantry(2, Side.Computer, new Position(7, 7)), tiles);
        var infantry = Scenarios.Duel(new Infantry(1, Side.Human, new Position(0, 0)),
            new Infantry(2, Side.Computer, new Position(7, 7)), tiles);

        Assert.Equal(4, armor.GetLegalMoves(Side.Human, 1)[new Position(2, 0)]);
        Assert.True(armor.Move(Side.Human, 1, new Position(2, 0)).IsSuccess);
        Assert.Equal(GameError.Unreachable, infantry.Move(Side.Human, 1, new Position(2, 0)).Error);
    }

    [Fact]
    public void Dijkstra_finds_cheaper_route_instead_of_first_discovered_route()
    {
        var tiles = Scenarios.PlainTiles();
        tiles[0, 1] = Terrain.Forest;
        var game = Scenarios.Duel(new Armor(1, Side.Human, new Position(0, 0)),
            new Infantry(2, Side.Computer, new Position(7, 7)), tiles);

        // Right then down costs 4; down then right costs 2.
        Assert.Equal(2, game.GetLegalMoves(Side.Human, 1)[new Position(1, 1)]);
        Assert.True(game.Move(Side.Human, 1, new Position(1, 1)).IsSuccess);
    }

    [Theory]
    [InlineData("Infantry", Terrain.Plain, 1)]
    [InlineData("Infantry", Terrain.Forest, 2)]
    [InlineData("Infantry", Terrain.Hill, 2)]
    [InlineData("Armor", Terrain.Plain, 1)]
    [InlineData("Armor", Terrain.Forest, 3)]
    [InlineData("Armor", Terrain.Hill, 2)]
    [InlineData("Artillery", Terrain.Plain, 1)]
    [InlineData("Artillery", Terrain.Forest, 2)]
    public void Terrain_entry_cost_depends_on_unit_type(string type, Terrain terrain, int cost)
    {
        var tiles = Scenarios.PlainTiles();
        tiles[0, 1] = terrain;
        var game = Scenarios.Duel(Scenarios.Unit(type, 1, Side.Human, 0, 0),
            new Infantry(2, Side.Computer, new Position(7, 7)), tiles);

        Assert.Equal(cost, game.GetLegalMoves(Side.Human, 1)[new Position(1, 0)]);
        Assert.True(game.Move(Side.Human, 1, new Position(1, 0)).IsSuccess);
    }

    [Fact]
    public void Artillery_cannot_afford_even_an_adjacent_hill()
    {
        var tiles = Scenarios.PlainTiles();
        tiles[0, 1] = Terrain.Hill;
        var game = Scenarios.Duel(new Artillery(1, Side.Human, new Position(0, 0)),
            new Infantry(2, Side.Computer, new Position(7, 7)), tiles);

        Assert.Equal(GameError.Unreachable, game.Move(Side.Human, 1, new Position(1, 0)).Error);
    }

    [Fact]
    public void Terrain_cost_is_summed_and_budget_is_inclusive()
    {
        var tiles = Scenarios.PlainTiles();
        tiles[0, 1] = Terrain.Forest;
        var game = Scenarios.Duel(new Infantry(1, Side.Human, new Position(0, 0)),
            new Infantry(2, Side.Computer, new Position(7, 7)), tiles);

        Assert.Equal(3, game.GetLegalMoves(Side.Human, 1)[new Position(2, 0)]);
        Assert.Equal(GameError.Unreachable, game.Move(Side.Human, 1, new Position(3, 0)).Error);
        Assert.True(game.Move(Side.Human, 1, new Position(2, 0)).IsSuccess);
    }

    [Fact]
    public void Starting_terrain_does_not_consume_movement_points()
    {
        var tiles = Scenarios.PlainTiles();
        tiles[0, 0] = Terrain.Hill;
        var game = Scenarios.Duel(new Artillery(1, Side.Human, new Position(0, 0)),
            new Infantry(2, Side.Computer, new Position(7, 7)), tiles);

        Assert.Equal(2, game.GetLegalMoves(Side.Human, 1)[new Position(2, 0)]);
    }

    [Theory]
    [InlineData(Side.Human)]
    [InlineData(Side.Computer)]
    public void Occupied_cells_block_both_crossing_and_stopping(Side blockingSide)
    {
        var tiles = new Terrain[BattleMap.Size, BattleMap.Size];
        for (var y = 1; y < BattleMap.Size; y++)
            for (var x = 0; x < BattleMap.Size; x++) tiles[y, x] = Terrain.Water;
        var game = new Game(new BattleMap(tiles),
        [
            new Infantry(1, Side.Human, new Position(0, 0)),
            new Infantry(2, blockingSide, new Position(1, 0)),
            new Infantry(3, Side.Computer, new Position(7, 0))
        ]);

        Assert.Equal(GameError.DestinationOccupied, game.Move(Side.Human, 1, new Position(1, 0)).Error);
        Assert.Equal(GameError.Unreachable, game.Move(Side.Human, 1, new Position(2, 0)).Error);
        Assert.Empty(game.GetLegalMoves(Side.Human, 1));
    }

    [Fact]
    public void Diagonal_cannot_bypass_two_blocked_orthogonal_neighbors()
    {
        var tiles = Scenarios.PlainTiles();
        tiles[0, 1] = tiles[1, 0] = Terrain.Water;
        var game = Scenarios.Duel(new Infantry(1, Side.Human, new Position(0, 0)),
            new Infantry(2, Side.Computer, new Position(7, 7)), tiles);

        Assert.Equal(GameError.Unreachable, game.Move(Side.Human, 1, new Position(1, 1)).Error);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(8, 0)]
    [InlineData(0, 8)]
    [InlineData(int.MaxValue, int.MinValue)]
    public void Outside_board_moves_are_rejected_without_mutation(int x, int y)
    {
        var game = Game.CreateDefault();
        var before = Scenarios.Snapshot(game);
        Assert.Equal(GameError.OutOfBounds, game.Move(Side.Human, 1, new Position(x, y)).Error);
        Assert.Equal(before, Scenarios.Snapshot(game));
    }

    [Fact]
    public void Legal_move_queries_are_read_only_and_every_listed_move_is_accepted()
    {
        var game = Game.CreateDefault();
        var before = Scenarios.Snapshot(game);
        var legalMoves = game.GetLegalMoves(Side.Human, 1);
        Assert.NotEmpty(legalMoves);
        Assert.DoesNotContain(Scenarios.Find(game, 1).Position, legalMoves.Keys);
        Assert.Equal(before, Scenarios.Snapshot(game));

        foreach (var destination in legalMoves.Keys)
            Assert.True(Game.CreateDefault().Move(Side.Human, 1, destination).IsSuccess);
    }
}
