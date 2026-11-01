using TacticSim.Core;

namespace TacticSim.Tests;

public class GameSetupTests
{
    [Fact]
    public void Default_match_has_three_distinct_healthy_units_per_side_on_passable_cells()
    {
        var game = Game.CreateDefault();
        Assert.Equal(6, game.Units.Count);
        Assert.Equal(6, game.Units.Select(u => u.Id).Distinct().Count());
        Assert.Equal(6, game.Units.Select(u => u.Position).Distinct().Count());
        foreach (var side in new[] { Side.Human, Side.Computer })
        {
            var units = game.Units.Where(u => u.Side == side).ToList();
            Assert.Equal(3, units.Count);
            Assert.Single(units.OfType<Infantry>());
            Assert.Single(units.OfType<Armor>());
            Assert.Single(units.OfType<Artillery>());
        }
        Assert.All(game.Units, unit =>
        {
            Assert.Equal(unit.MaxHealth, unit.Health);
            Assert.NotEqual(Terrain.Water, game.Map.GetTerrain(unit.Position));
        });
        Assert.Equal(Side.Human, game.CurrentSide);
        Assert.Equal(1, game.TurnNumber);
        Assert.Null(game.Winner);
    }

    [Fact]
    public void Games_do_not_share_mutable_unit_state_even_if_given_same_instances()
    {
        var human = new Infantry(1, Side.Human, new Position(0, 0));
        var computer = new Armor(2, Side.Computer, new Position(2, 0));
        var first = Scenarios.Duel(human, computer);
        var second = Scenarios.Duel(human, computer);
        var before = Scenarios.Snapshot(second);

        Assert.True(first.Move(Side.Human, 1, new Position(1, 0)).IsSuccess);
        Assert.True(first.Attack(Side.Human, 1, 2).IsSuccess);

        Assert.Equal(before, Scenarios.Snapshot(second));
        Assert.Equal(new Position(0, 0), human.Position);
        Assert.Equal(16, computer.Health);
    }

    [Fact]
    public void Map_copies_input_terrain_and_validates_dimensions_and_values()
    {
        var tiles = Scenarios.PlainTiles();
        var map = new BattleMap(tiles);
        tiles[0, 0] = Terrain.Water;
        Assert.Equal(Terrain.Plain, map.GetTerrain(new Position(0, 0)));
        Assert.Throws<ArgumentException>(() => new BattleMap(new Terrain[7, 8]));
        tiles[0, 0] = (Terrain)999;
        Assert.Throws<ArgumentException>(() => new BattleMap(tiles));
    }

    [Fact]
    public void Invalid_initial_placements_and_duplicate_ids_are_rejected()
    {
        var human = new Infantry(1, Side.Human, new Position(0, 0));
        Assert.Throws<ArgumentException>(() => Scenarios.Duel(human,
            new Armor(1, Side.Computer, new Position(1, 0))));
        Assert.Throws<ArgumentException>(() => Scenarios.Duel(human,
            new Armor(2, Side.Computer, new Position(0, 0))));
        Assert.Throws<ArgumentException>(() => Scenarios.Duel(human,
            new Armor(2, Side.Computer, new Position(8, 0))));
        var tiles = Scenarios.PlainTiles();
        tiles[0, 1] = Terrain.Water;
        Assert.Throws<ArgumentException>(() => Scenarios.Duel(human,
            new Armor(2, Side.Computer, new Position(1, 0)), tiles));
        Assert.Throws<ArgumentException>(() => new Game(BattleMap.Default, [human]));
    }
}
