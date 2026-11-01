using System.Text.Json;
using TacticSim.Core;

namespace TacticSim.Tests;

public class SnapshotAndHistoryTests
{
    [Fact]
    public void Json_round_trip_preserves_health_positions_turn_and_action_limits()
    {
        var game = Scenarios.Duel(new Infantry(1, Side.Human, new Position(0, 0)),
            new Armor(2, Side.Computer, new Position(2, 0)));
        Assert.True(game.Move(Side.Human, 1, new Position(1, 0)).IsSuccess);
        Assert.True(game.Attack(Side.Human, 1, 2).IsSuccess);
        Assert.True(game.EndTurn(Side.Human).IsSuccess);
        var json = JsonSerializer.Serialize(game.ExportSnapshot());

        var restored = Game.Restore(JsonSerializer.Deserialize<GameSnapshot>(json)!);

        Assert.Equal(Scenarios.Snapshot(game), Scenarios.Snapshot(restored));
        Assert.Empty(restored.Actions); // Saved history is stored separately by Web.
        Assert.True(restored.EndTurn(Side.Computer).IsSuccess);
        Assert.False(Scenarios.Find(restored, 1).HasMoved);
        Assert.False(Scenarios.Find(restored, 1).HasAttacked);
        Assert.Equal(12, Scenarios.Find(restored, 2).Health);
        Assert.True(Scenarios.Find(game, 1).HasMoved);
    }

    [Fact]
    public void Saving_and_restoring_after_attack_still_forbids_a_second_attack_or_move()
    {
        var game = Scenarios.Duel(new Infantry(1, Side.Human, new Position(0, 0)),
            new Armor(2, Side.Computer, new Position(1, 0)));
        Assert.True(game.Attack(Side.Human, 1, 2).IsSuccess);
        var restored = Game.Restore(game.ExportSnapshot());
        Assert.Equal(GameError.AlreadyAttacked, restored.Attack(Side.Human, 1, 2).Error);
        Assert.Equal(GameError.MoveAfterAttack, restored.Move(Side.Human, 1, new Position(0, 1)).Error);
    }

    [Fact]
    public void History_records_only_accepted_actions_with_actual_damage_and_turn()
    {
        var game = Scenarios.Duel(new Armor(1, Side.Human, new Position(0, 0)),
            new Artillery(2, Side.Computer, new Position(2, 0)));
        Assert.False(game.Attack(Side.Human, 1, 2).IsSuccess);
        Assert.True(game.Move(Side.Human, 1, new Position(1, 0)).IsSuccess);
        Assert.True(game.Attack(Side.Human, 1, 2).IsSuccess);
        Scenarios.NextHumanTurn(game);
        Assert.True(game.Attack(Side.Human, 1, 2).IsSuccess);

        Assert.Equal(new[] { ActionKind.Move, ActionKind.Attack, ActionKind.EndTurn,
            ActionKind.EndTurn, ActionKind.Attack }, game.Actions.Select(a => a.Kind));
        var last = game.Actions.Last();
        Assert.Equal(3, last.TurnNumber);
        Assert.Equal(2, last.Damage);
        Assert.True(last.Destroyed);
        Assert.Equal(Side.Human, Game.Restore(game.ExportSnapshot()).Winner);
    }

    [Fact]
    public void Restoring_validates_saved_health_and_does_not_trust_a_type_name()
    {
        var snapshot = Game.CreateDefault().ExportSnapshot();
        snapshot.Units[0] = snapshot.Units[0] with { Health = 999 };
        Assert.Throws<ArgumentException>(() => Game.Restore(snapshot));
        snapshot.Units[0] = snapshot.Units[0] with { Health = 10, Type = "UnknownType" };
        Assert.Throws<ArgumentException>(() => Game.Restore(snapshot));
    }
}
