using TacticSim.Core;

namespace TacticSim.Tests;

public class CombatTests
{
    [Theory]
    [InlineData("Infantry", 1, 0, true)]
    [InlineData("Infantry", 1, 1, false)]
    [InlineData("Armor", 1, 0, true)]
    [InlineData("Armor", 2, 0, false)]
    [InlineData("Artillery", 1, 0, false)]
    [InlineData("Artillery", 1, 1, true)]
    [InlineData("Artillery", 4, 0, true)]
    [InlineData("Artillery", 2, 2, true)]
    [InlineData("Artillery", 2, 3, false)]
    public void Attack_range_uses_inclusive_Manhattan_distance(string type, int x, int y, bool legal)
    {
        var game = Scenarios.Duel(Scenarios.Unit(type, 1, Side.Human, 0, 0),
            new Armor(2, Side.Computer, new Position(x, y)));
        var before = Scenarios.Snapshot(game);

        Assert.Equal(legal, game.GetLegalTargets(Side.Human, 1).Any(t => t.Id == 2));
        var result = game.Attack(Side.Human, 1, 2);
        Assert.Equal(legal ? GameError.None : GameError.OutOfRange, result.Error);
        if (!legal) Assert.Equal(before, Scenarios.Snapshot(game));
    }

    [Fact]
    public void Water_forest_hills_and_units_do_not_block_line_of_sight()
    {
        var tiles = Scenarios.PlainTiles();
        tiles[0, 1] = Terrain.Water;
        tiles[0, 2] = Terrain.Forest;
        tiles[0, 3] = Terrain.Hill;
        var game = new Game(new BattleMap(tiles),
        [
            new Artillery(1, Side.Human, new Position(0, 0)),
            new Infantry(2, Side.Human, new Position(2, 0)),
            new Armor(3, Side.Computer, new Position(4, 0))
        ]);

        Assert.True(game.Attack(Side.Human, 1, 3).IsSuccess);
        Assert.Equal(11, Scenarios.Find(game, 3).Health);
    }

    [Theory]
    [InlineData("Infantry", Terrain.Plain, 4)]
    [InlineData("Infantry", Terrain.Forest, 3)]
    [InlineData("Infantry", Terrain.Hill, 2)]
    [InlineData("Armor", Terrain.Plain, 6)]
    [InlineData("Armor", Terrain.Forest, 5)]
    [InlineData("Armor", Terrain.Hill, 4)]
    [InlineData("Artillery", Terrain.Plain, 5)]
    [InlineData("Artillery", Terrain.Forest, 4)]
    [InlineData("Artillery", Terrain.Hill, 3)]
    public void Damage_uses_attacker_power_and_target_terrain(string type, Terrain terrain, int expected)
    {
        var tiles = Scenarios.PlainTiles();
        tiles[1, 1] = Terrain.Hill;
        var targetX = type == "Artillery" ? 3 : 2;
        tiles[1, targetX] = terrain;
        var game = Scenarios.Duel(Scenarios.Unit(type, 1, Side.Human, 1, 1),
            new Armor(2, Side.Computer, new Position(targetX, 1)), tiles);

        var result = game.Attack(Side.Human, 1, 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Damage);
        Assert.Equal(16 - expected, Scenarios.Find(game, 2).Health);
        Assert.Equal(Scenarios.Find(game, 1).MaxHealth, Scenarios.Find(game, 1).Health);
    }

    [Fact]
    public void Low_health_does_not_reduce_damage_and_there_is_no_retaliation()
    {
        var game = Scenarios.Duel(new Infantry(1, Side.Human, new Position(0, 0)),
            new Armor(2, Side.Computer, new Position(1, 0)), firstSide: Side.Computer);
        Assert.True(game.Attack(Side.Computer, 2, 1).IsSuccess);
        Assert.Equal(4, Scenarios.Find(game, 1).Health);
        Assert.True(game.EndTurn(Side.Computer).IsSuccess);

        Assert.Equal(4, game.Attack(Side.Human, 1, 2).Damage);
        Assert.Equal(4, Scenarios.Find(game, 1).Health);
        Assert.Equal(12, Scenarios.Find(game, 2).Health);
    }

    [Fact]
    public void Friendly_fire_self_attack_and_missing_targets_do_not_mutate_state()
    {
        var game = new Game(BattleMap.Default,
        [
            new Infantry(1, Side.Human, new Position(0, 0)),
            new Infantry(2, Side.Human, new Position(1, 0)),
            new Infantry(3, Side.Computer, new Position(7, 7))
        ]);
        var before = Scenarios.Snapshot(game);

        Assert.Equal(GameError.FriendlyFire, game.Attack(Side.Human, 1, 2).Error);
        Assert.Equal(GameError.FriendlyFire, game.Attack(Side.Human, 1, 1).Error);
        Assert.Equal(GameError.TargetNotFound, game.Attack(Side.Human, 1, 999).Error);
        Assert.Equal(before, Scenarios.Snapshot(game));
    }

    [Fact]
    public void Death_clamps_health_and_only_last_enemy_death_ends_the_match()
    {
        var game = TwoEnemies();
        Assert.Equal(6, game.Attack(Side.Human, 1, 2).Damage);
        Scenarios.NextHumanTurn(game);

        var killingBlow = game.Attack(Side.Human, 1, 2);
        Assert.Equal(2, killingBlow.Damage);
        Assert.Equal(0, Scenarios.Find(game, 2).Health);
        Assert.False(Scenarios.Find(game, 2).IsAlive);
        Assert.Null(game.Winner);

        Scenarios.NextHumanTurn(game);
        var before = Scenarios.Snapshot(game);
        Assert.Equal(GameError.TargetDestroyed, game.Attack(Side.Human, 1, 2).Error);
        Assert.Equal(before, Scenarios.Snapshot(game));
        Assert.True(game.EndTurn(Side.Human).IsSuccess);
        Assert.Equal(GameError.UnitDestroyed, game.Move(Side.Computer, 2, new Position(0, 1)).Error);
        Assert.Equal(GameError.UnitDestroyed, game.Attack(Side.Computer, 2, 1).Error);
    }

    [Fact]
    public void Destroyed_unit_cell_can_be_entered_on_a_later_turn()
    {
        var game = TwoEnemies();
        Assert.True(game.Attack(Side.Human, 1, 2).IsSuccess);
        Scenarios.NextHumanTurn(game);
        Assert.True(game.Attack(Side.Human, 1, 2).IsSuccess);
        Scenarios.NextHumanTurn(game);

        Assert.True(game.Move(Side.Human, 1, new Position(0, 0)).IsSuccess);
    }

    [Theory]
    [InlineData(Side.Human)]
    [InlineData(Side.Computer)]
    public void Eliminating_last_enemy_declares_winner_and_freezes_match(Side winningSide)
    {
        var losingSide = winningSide == Side.Human ? Side.Computer : Side.Human;
        var game = new Game(new BattleMap(Scenarios.PlainTiles()),
        [
            new Armor(1, winningSide, new Position(0, 0)),
            new Artillery(2, losingSide, new Position(1, 0))
        ], winningSide);
        Assert.Null(game.Winner);
        Assert.True(game.Attack(winningSide, 1, 2).IsSuccess);
        Assert.Null(game.Winner);
        Assert.True(game.EndTurn(winningSide).IsSuccess);
        Assert.True(game.EndTurn(losingSide).IsSuccess);
        Assert.True(game.Attack(winningSide, 1, 2).IsSuccess);

        Assert.Equal(winningSide, game.Winner);
        var before = Scenarios.Snapshot(game);
        Assert.Equal(GameError.GameOver, game.Move(winningSide, 1, new Position(0, 1)).Error);
        Assert.Equal(GameError.GameOver, game.Attack(winningSide, 1, 2).Error);
        Assert.Equal(GameError.GameOver, game.EndTurn(winningSide).Error);
        Assert.Equal(GameError.GameOver, game.EndTurn(losingSide).Error);
        Assert.Empty(game.GetLegalMoves(winningSide, 1));
        Assert.Empty(game.GetLegalTargets(winningSide, 1));
        Assert.Equal(before, Scenarios.Snapshot(game));
    }

    private static Game TwoEnemies() => new(new BattleMap(Scenarios.PlainTiles()),
    [
        new Armor(1, Side.Human, new Position(1, 0)),
        new Artillery(2, Side.Computer, new Position(0, 0)),
        new Infantry(3, Side.Computer, new Position(7, 7))
    ]);
}
