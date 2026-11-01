using TacticSim.Core;

namespace TacticSim.Tests;

public class ComputerOpponentTests
{
    [Fact]
    public void Opponent_rejects_human_turn_without_changing_state()
    {
        var game = Game.CreateDefault();
        var before = Scenarios.Snapshot(game);
        Assert.Equal(GameError.NotYourTurn, new ComputerOpponent().PlayTurn(game).Error);
        Assert.Equal(before, Scenarios.Snapshot(game));
    }

    [Fact]
    public void Opponent_moves_then_attacks_through_normal_commands()
    {
        var game = Scenarios.Duel(new Infantry(1, Side.Human, new Position(0, 0)),
            new Armor(2, Side.Computer, new Position(2, 0)), firstSide: Side.Computer);

        Assert.True(new ComputerOpponent().PlayTurn(game).IsSuccess);

        Assert.Equal(new Position(1, 0), Scenarios.Find(game, 2).Position);
        Assert.Equal(4, Scenarios.Find(game, 1).Health);
        Assert.True(Scenarios.Find(game, 2).HasMoved);
        Assert.True(Scenarios.Find(game, 2).HasAttacked);
        Assert.Equal(Side.Human, game.CurrentSide);
    }

    [Fact]
    public void Opponent_does_not_move_after_attacking()
    {
        var game = Scenarios.Duel(new Armor(1, Side.Human, new Position(0, 0)),
            new Armor(2, Side.Computer, new Position(1, 0)), firstSide: Side.Computer);

        Assert.True(new ComputerOpponent().PlayTurn(game).IsSuccess);

        Assert.Equal(new Position(1, 0), Scenarios.Find(game, 2).Position);
        Assert.False(Scenarios.Find(game, 2).HasMoved);
        Assert.True(Scenarios.Find(game, 2).HasAttacked);
        Assert.Equal(10, Scenarios.Find(game, 1).Health);
    }

    [Fact]
    public void Artillery_repositions_to_respect_minimum_range()
    {
        var game = Scenarios.Duel(new Infantry(1, Side.Human, new Position(1, 0)),
            new Artillery(2, Side.Computer, new Position(0, 0)), firstSide: Side.Computer);

        Assert.True(new ComputerOpponent().PlayTurn(game).IsSuccess);

        Assert.Equal(new Position(0, 1), Scenarios.Find(game, 2).Position);
        Assert.Equal(5, Scenarios.Find(game, 1).Health);
    }

    [Fact]
    public void Trapped_opponent_waits_without_crossing_water_or_exceeding_range()
    {
        var tiles = Scenarios.PlainTiles();
        tiles[0, 1] = tiles[1, 0] = Terrain.Water;
        var game = Scenarios.Duel(new Infantry(1, Side.Human, new Position(7, 7)),
            new Armor(2, Side.Computer, new Position(0, 0)), tiles, Side.Computer);

        Assert.True(new ComputerOpponent().PlayTurn(game).IsSuccess);

        Assert.Equal(new Position(0, 0), Scenarios.Find(game, 2).Position);
        Assert.Equal(10, Scenarios.Find(game, 1).Health);
        Assert.False(Scenarios.Find(game, 2).HasMoved);
        Assert.False(Scenarios.Find(game, 2).HasAttacked);
        Assert.Equal(Side.Human, game.CurrentSide);
    }

    [Fact]
    public void Opponent_respects_actions_already_used_in_its_current_turn()
    {
        var game = Scenarios.Duel(new Armor(1, Side.Human, new Position(0, 0)),
            new Armor(2, Side.Computer, new Position(2, 0)), firstSide: Side.Computer);
        Assert.True(game.Move(Side.Computer, 2, new Position(1, 0)).IsSuccess);
        Assert.True(game.Attack(Side.Computer, 2, 1).IsSuccess);

        Assert.True(new ComputerOpponent().PlayTurn(game).IsSuccess);
        Assert.Equal(10, Scenarios.Find(game, 1).Health);
        Assert.Equal(new Position(1, 0), Scenarios.Find(game, 2).Position);
    }

    [Fact]
    public void Opponent_stops_immediately_after_victory()
    {
        var game = Scenarios.Duel(new Artillery(1, Side.Human, new Position(0, 0)),
            new Armor(2, Side.Computer, new Position(1, 0)), firstSide: Side.Computer);
        var opponent = new ComputerOpponent();
        Assert.True(opponent.PlayTurn(game).IsSuccess);
        Assert.True(game.EndTurn(Side.Human).IsSuccess);
        Assert.True(opponent.PlayTurn(game).IsSuccess);

        Assert.Equal(Side.Computer, game.Winner);
        Assert.Equal(Side.Computer, game.CurrentSide);
        var before = Scenarios.Snapshot(game);
        Assert.Equal(GameError.GameOver, opponent.PlayTurn(game).Error);
        Assert.Equal(before, Scenarios.Snapshot(game));
    }

    [Fact]
    public void Same_initial_state_produces_identical_computer_turns()
    {
        var first = Game.CreateDefault();
        var second = Game.CreateDefault();
        var opponent = new ComputerOpponent();

        for (var turn = 0; turn < 12 && first.Winner is null; turn++)
        {
            Assert.True(first.EndTurn(Side.Human).IsSuccess);
            Assert.True(second.EndTurn(Side.Human).IsSuccess);
            Assert.True(opponent.PlayTurn(first).IsSuccess);
            Assert.True(opponent.PlayTurn(second).IsSuccess);
            Assert.Equal(Scenarios.Snapshot(first), Scenarios.Snapshot(second));
        }
    }
}
