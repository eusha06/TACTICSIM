using TacticSim.Core;

namespace TacticSim.Tests;

public class TurnTests
{
    [Fact]
    public void Move_then_attack_is_allowed_but_second_move_and_attack_are_rejected()
    {
        var game = Scenarios.Duel(new Infantry(1, Side.Human, new Position(0, 0)),
            new Armor(2, Side.Computer, new Position(2, 0)));

        Assert.True(game.Move(Side.Human, 1, new Position(1, 0)).IsSuccess);
        Assert.Equal(GameError.AlreadyMoved, game.Move(Side.Human, 1, new Position(1, 1)).Error);
        Assert.Empty(game.GetLegalMoves(Side.Human, 1));
        Assert.True(game.Attack(Side.Human, 1, 2).IsSuccess);
        var before = Scenarios.Snapshot(game);
        Assert.Equal(GameError.AlreadyAttacked, game.Attack(Side.Human, 1, 2).Error);
        Assert.Empty(game.GetLegalTargets(Side.Human, 1));
        Assert.Equal(before, Scenarios.Snapshot(game));
    }

    [Fact]
    public void Attacking_first_forbids_movement_without_spending_movement_action()
    {
        var game = Scenarios.Duel(new Infantry(1, Side.Human, new Position(0, 0)),
            new Armor(2, Side.Computer, new Position(1, 0)));
        Assert.True(game.Attack(Side.Human, 1, 2).IsSuccess);
        var before = Scenarios.Snapshot(game);

        Assert.Equal(GameError.MoveAfterAttack, game.Move(Side.Human, 1, new Position(0, 1)).Error);
        Assert.False(Scenarios.Find(game, 1).HasMoved);
        Assert.Empty(game.GetLegalMoves(Side.Human, 1));
        Assert.Equal(before, Scenarios.Snapshot(game));
    }

    [Fact]
    public void Failed_attack_does_not_prevent_movement_or_a_later_valid_attack()
    {
        var game = Scenarios.Duel(new Infantry(1, Side.Human, new Position(0, 0)),
            new Armor(2, Side.Computer, new Position(2, 0)));

        Assert.Equal(GameError.OutOfRange, game.Attack(Side.Human, 1, 2).Error);
        Assert.False(Scenarios.Find(game, 1).HasAttacked);
        Assert.True(game.Move(Side.Human, 1, new Position(1, 0)).IsSuccess);
        Assert.True(game.Attack(Side.Human, 1, 2).IsSuccess);
    }

    [Fact]
    public void Incoming_side_gets_new_actions_and_turns_may_end_early()
    {
        var game = Scenarios.Duel(new Infantry(1, Side.Human, new Position(0, 0)),
            new Armor(2, Side.Computer, new Position(2, 0)));
        Assert.True(game.Move(Side.Human, 1, new Position(1, 0)).IsSuccess);
        Assert.True(game.Attack(Side.Human, 1, 2).IsSuccess);
        Assert.True(game.EndTurn(Side.Human).IsSuccess);
        Assert.Equal(Side.Computer, game.CurrentSide);
        Assert.Equal(2, game.TurnNumber);
        Assert.True(Scenarios.Find(game, 1).HasMoved);
        Assert.True(Scenarios.Find(game, 1).HasAttacked);
        Assert.True(game.EndTurn(Side.Computer).IsSuccess);

        Assert.Equal(Side.Human, game.CurrentSide);
        Assert.Equal(3, game.TurnNumber);
        Assert.False(Scenarios.Find(game, 1).HasMoved);
        Assert.False(Scenarios.Find(game, 1).HasAttacked);
        Assert.True(game.Move(Side.Human, 1, new Position(1, 1)).IsSuccess);
    }

    [Fact]
    public void Actor_must_own_both_the_turn_and_the_unit()
    {
        var game = Scenarios.Duel(new Infantry(1, Side.Human, new Position(0, 0)),
            new Armor(2, Side.Computer, new Position(1, 0)));
        var before = Scenarios.Snapshot(game);

        Assert.Equal(GameError.NotYourTurn, game.Move(Side.Computer, 2, new Position(2, 0)).Error);
        Assert.Equal(GameError.NotYourTurn, game.Attack(Side.Computer, 2, 1).Error);
        Assert.Equal(GameError.NotYourTurn, game.EndTurn(Side.Computer).Error);
        Assert.Equal(GameError.NotYourUnit, game.Move(Side.Human, 2, new Position(2, 0)).Error);
        Assert.Equal(GameError.NotYourUnit, game.Attack(Side.Human, 2, 1).Error);
        Assert.Equal(GameError.UnitNotFound, game.Move(Side.Human, 999, new Position(0, 1)).Error);
        Assert.Equal(GameError.UnitNotFound, game.Attack(Side.Human, 999, 2).Error);
        Assert.Equal(GameError.NotYourTurn, game.EndTurn((Side)999).Error);
        Assert.Empty(game.GetLegalMoves(Side.Computer, 2));
        Assert.Empty(game.GetLegalTargets(Side.Human, 2));
        Assert.Empty(game.GetLegalMoves(Side.Human, 999));
        Assert.Equal(before, Scenarios.Snapshot(game));
    }
}
